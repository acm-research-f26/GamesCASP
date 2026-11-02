"""Half-zombie snapshot facts, stored memories in decisions, and action mapping.

The solver is substituted here; test_scasp_integration.py runs the same paths
against the real s(CASP) solver inside the gamescasp container.
"""

import asyncio
from functools import partial
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import websockets

SERVER = Path(__file__).resolve().parents[1]
# Absent when only the server directory is mounted (for example, in the container).
AGENT_CS = SERVER.parent.parent / "ScaspUnityGame" / "Assets" / "Scripts" / "Agent.cs"
sys.path.insert(0, str(SERVER))

from FactFactory import FactFactory, configured_bite_window_seconds
from SocketHandler import handler
from StoredFacts import StoredFact, stored_facts_for_actor
from UnityRequest import UnityActionRequest


def state(**changes):
    return dict(health=3, levers_to_win=3, levers_pulled_count=0,
                levers=[], zombies=[], barricades=[]) | changes


def half_zombie(now=115.5, start=100, window=30.0, **changes):
    return state(half_zombie=True, current_time=now, bite_window_started_at=start,
                 bite_window_seconds=window, **changes)


def human(human_id, distance, health=3, **extra):
    return dict(id=human_id, health=health, visible=True, distance=distance) | extra


def payload(fact_type, subject, target="", lifetime="Permanent"):
    return dict(type=fact_type, lifetime=lifetime, subject_id=subject, target_id=target,
                has_distance=False, distance=0)


class HalfZombieFactTests(unittest.TestCase):
    def test_innocent_snapshot_has_no_half_zombie_facts(self):
        facts = FactFactory.generate_npc_facts("player1", state(half_zombie=False, current_time=50.0))
        self.assertIn("innocent(player1).", facts)
        self.assertNotIn("half_zombie(", facts)
        self.assertNotIn("current_time(", facts)
        self.assertNotIn("bite_window_started_at(", facts)

    def test_status_timer_and_argument_order_match_rules(self):
        facts = FactFactory.generate_npc_facts("player1", half_zombie()).splitlines()
        for clause in ("innocent(player1).", "half_zombie(player1).", "health(3, player1).",
                       "current_time(115.5).", "bite_window_started_at(100, player1)."):
            self.assertIn(clause, facts)

    def test_humans_are_observations_without_hidden_status(self):
        facts = FactFactory.generate_npc_facts("player1", state(humans=[
            human("player2", 1.5, half_zombie=True, bite_window_started_at=3),
            human("player1", 0),
        ]))
        for clause in ("innocent(player2).", "health(3, player2).",
                       "distance(player2, 1.5, player1).", "human_visible(player2, player1)."):
            self.assertIn(clause, facts)
        self.assertNotIn("half_zombie(player2)", facts)
        self.assertNotIn("bite_window_started_at", facts)
        self.assertNotIn("human_visible(player1, player1)", facts)
        self.assertNotIn("distance(player1, 0, player1)", facts)

    def test_invalid_half_zombie_state_is_rejected(self):
        bad_states = [
            state(half_zombie="true"),
            half_zombie(window=60.0),
            half_zombie(window=None),
            {k: v for k, v in half_zombie().items() if k != "bite_window_seconds"},
            half_zombie(now=float("inf")),
            half_zombie(start=-1),
            state(humans=[human("Player2", 1)]),
            state(humans=[human("player2", -1)]),
            state(humans=[human("player2", 1, health=True)]),
            state(humans=[human("player2).\nhalf_zombie(player3", 1)]),
        ]
        for bad in bad_states:
            with self.subTest(state=bad), self.assertRaises((ValueError, KeyError)):
                FactFactory.generate_npc_facts("player1", bad)

    def test_numbers_are_prolog_readable(self):
        facts = FactFactory.generate_npc_facts("player1", state(humans=[human("player2", 1e-05)],
                                                                zombies=[dict(id="zombie_a", distance=2.0)]))
        self.assertIn("distance(player2, 0.00001, player1).", facts)
        self.assertIn("distance(zombie_a, 2, player1).", facts)
        self.assertNotRegex(facts, r"\de[+-]?\d")

    def test_bite_window_is_read_from_the_prolog_rules(self):
        behavior = (SERVER / "half_zombie_timed_behavior.pl").read_text(encoding="utf-8")
        critical = float(re.search(r"^critical_bite_seconds\((\d+)\)\.", behavior, re.MULTILINE).group(1))
        self.assertEqual(configured_bite_window_seconds(), 30.0)
        self.assertLess(critical, configured_bite_window_seconds())

    @unittest.skipUnless(AGENT_CS.exists(), "Unity scripts are not available")
    def test_unity_bite_window_matches_prolog(self):
        agent = AGENT_CS.read_text(encoding="utf-8")
        unity = float(re.search(r"BiteWindowSeconds = (\d+(?:\.\d+)?)f;", agent).group(1))
        self.assertEqual(unity, configured_bite_window_seconds())


class StoredMemoryTests(unittest.TestCase):
    def test_witnessed_events_are_permanent_and_name_the_victim(self):
        for fact_type, stored in (("ZombieAttackWitnessed", "zombie_attack_witnessed"),
                                  ("BiteAttemptWitnessed", "bite_attempt_witnessed"),
                                  ("BiteWitnessed", "bite_witnessed")):
            with self.subTest(fact_type=fact_type):
                fact = StoredFact.from_payload("player1", payload(fact_type, "player2", "player3"))
                self.assertEqual(fact.to_prolog(),
                                 f"stored_fact(player1, permanent, {stored}, player2, player3, none).\n")
                with self.assertRaises(ValueError):
                    StoredFact.from_payload("player1", payload(fact_type, "player2", "player3", "Mutable"))
                with self.assertRaises(ValueError):
                    StoredFact.from_payload("player1", payload(fact_type, "player2"))

    def test_only_the_actors_own_canonical_clauses_are_selected(self):
        text = (":- use_module(library(scasp)).\n"
                "noise(unknown).\n"
                "stored_fact(player1, permanent, bite_attempt_witnessed, player2, player3, none).\n"
                "stored_fact(player2, permanent, bite_attempt_witnessed, player4, player1, none).\n"
                "stored_fact(player10, permanent, lever_activated, lever_1, none, none).\n"
                "stored_fact(player1, mutable, agent_attacked, player1, zombie_a, 3.5).\n"
                "stored_fact(player1, permanent, x, y, z, none). extra.\n"
                "stored_fact(Player1, permanent, x, y, z, none).\n")
        self.assertEqual(stored_facts_for_actor(text, "player1"), [
            "stored_fact(player1, permanent, bite_attempt_witnessed, player2, player3, none).\n",
            "stored_fact(player1, mutable, agent_attacked, player1, zombie_a, 3.5).\n",
        ])
        self.assertEqual(len(stored_facts_for_actor(text, "player2")), 1)
        self.assertEqual(stored_facts_for_actor(text, "player3"), [])
        with self.assertRaises(ValueError):
            stored_facts_for_actor(text, "Player1")


class NpcDecisionMemoryTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.facts = self.directory / "facts_temp.pl"
        self.facts.write_text(":- use_module(library(scasp)).\nnoise(unknown).\n", encoding="utf-8")
        server = await websockets.serve(partial(
            handler, server_dir=self.directory, scasp="test-scasp", facts_lock=asyncio.Lock()
        ), "127.0.0.1", 0)
        self.uri = f"ws://127.0.0.1:{server.sockets[0].getsockname()[1]}"

        async def close():
            server.close()
            await server.wait_closed()
        self.addAsyncCleanup(close)
        self.queries = []
        self.solver_output = "X = hunt_lever"

    async def fake_query(self, query_file):
        # Capture exactly what the solver would have been given.
        query = query_file.read_text(encoding="utf-8")
        snapshot = re.search(r"#include\('(npc_facts_[0-9a-f]+\.pl)'\)", query).group(1)
        self.queries.append((query, (self.directory / snapshot).read_text(encoding="utf-8")))
        return self.solver_output

    async def store(self, socket, actor, *facts, request_id="r"):
        await socket.send(json.dumps(dict(request_type="store_facts", request_id=request_id,
                                          actor=actor, facts=list(facts))))

    async def decide(self, socket, actor, npc_state=None):
        await socket.send(json.dumps(dict(request_type="npc_decision", actor=actor,
                                          state=npc_state or state())))

    async def recv(self, socket):
        return json.loads(await asyncio.wait_for(socket.recv(), 2))

    async def test_stored_memories_reach_the_next_decision_on_the_same_socket(self):
        attempt = payload("BiteAttemptWitnessed", "player2", "player3")
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as socket:
                # No wait for the acknowledgment: message order alone must suffice.
                await self.store(socket, "player1", attempt)
                await self.decide(socket, "player1")
                replies = [await self.recv(socket), await self.recv(socket)]
        self.assertEqual({reply["message_type"] for reply in replies}, {"facts_stored", "npc_action"})
        query, snapshot = self.queries[0]
        self.assertIn("#include('half_zombie_timed_behavior.pl').", query)
        self.assertIn("?- action(X, player1).", query)
        self.assertIn("innocent(player1).", snapshot)
        self.assertIn("stored_fact(player1, permanent, bite_attempt_witnessed, player2, player3, none).",
                      snapshot)

    async def test_memories_stay_isolated_per_actor(self):
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as first, websockets.connect(self.uri) as second:
                await self.store(first, "player1", payload("ZombieAttackWitnessed", "zombie_a", "player3"))
                await self.recv(first)
                await self.store(second, "player2", payload("BiteWitnessed", "player4", "player5"))
                await self.recv(second)
                await self.decide(second, "player2")
                await self.recv(second)
        snapshot = self.queries[0][1]
        self.assertIn("stored_fact(player2, permanent, bite_witnessed, player4, player5, none).", snapshot)
        self.assertNotIn("player1, permanent", snapshot)
        self.assertNotIn("zombie_attack_witnessed", snapshot)
        self.assertNotIn("noise(unknown)", snapshot)

    async def test_mutable_replacement_leaves_only_the_latest_value_for_reasoning(self):
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as socket:
                for zombie in ("zombie_a", "zombie_b"):
                    await self.store(socket, "player1", payload("AgentAttacked", "player1", zombie, "Mutable"))
                    await self.recv(socket)
                await self.decide(socket, "player1")
                await self.recv(socket)
        snapshot = self.queries[0][1]
        self.assertIn("stored_fact(player1, mutable, agent_attacked, player1, zombie_b, none).", snapshot)
        self.assertNotIn("zombie_a", snapshot)

    async def test_half_zombie_actions_map_to_actor_action_and_target(self):
        cases = [("X = attack_human(player2)", "attack_human", "player2"),
                 ("Model\n  X = move_to_human(player3) .\n", "move_to_human", "player3"),
                 ("X = run_from(player4)", "run_from", "player4"),
                 ("X = hunt_lever", "hunt_lever", "")]
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as socket:
                for output, action, target in cases:
                    with self.subTest(output=output):
                        self.solver_output = output
                        await self.decide(socket, "player1", half_zombie(humans=[human("player2", 1)]))
                        self.assertEqual(await self.recv(socket), dict(
                            message_type="npc_action", actor="player1", action=action, target=target))
        self.assertIn("half_zombie(player1).", self.queries[0][1])
        self.assertIn("human_visible(player2, player1).", self.queries[0][1])

    async def test_no_models_is_an_empty_decision_not_an_error(self):
        self.solver_output = "% QUERY:?- action(X, player1).\n\nno models\n"
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as socket:
                await self.decide(socket, "player1", half_zombie())
                self.assertEqual(await self.recv(socket), dict(
                    message_type="npc_action", actor="player1", action="", target=""))

    async def test_decision_errors_identify_the_actor(self):
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            async with websockets.connect(self.uri) as socket:
                await self.decide(socket, "player1", half_zombie(window=60.0))
                reply = await self.recv(socket)
                self.assertEqual((reply["error"], reply["request_type"], reply["actor"]),
                                 ("invalid_request", "npc_decision", "player1"))
                self.assertIn("bite_window_seconds", reply["detail"])
                await socket.send(json.dumps(dict(request_type="npc_decision", actor=["player1"], state={})))
                reply = await self.recv(socket)
                self.assertEqual((reply["error"], reply["actor"]), ("invalid_request", None))
        self.assertEqual(self.queries, [])


if __name__ == "__main__":
    unittest.main()

"""Stored memories and half-zombie snapshots through the production handlers and real s(CASP).

Skipped unless scasp is on PATH. Run inside the existing image, for example:
    docker run --rm -v <repo>/DockerServer/Server:/app:ro -w /app dockerserver-gamescasp \
        python3 -B -m unittest tests.test_scasp_integration -v
Rule files are copied into a temporary server directory, so nothing in the repository changes.
"""

import asyncio
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

SERVER = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SERVER))

from UnityRequest import UnityNPCDecisionRequest, UnityRequestContext, UnityStoredFactsRequest

RULES = ("innocent_behavior.pl", "half_zombie_rules.pl", "half_zombie_timed_behavior.pl")


class RecordingSocket:
    def __init__(self):
        self.messages = []

    async def send(self, message):
        self.messages.append(json.loads(message))


def state(**changes):
    return dict(health=3, levers_to_win=3, levers_pulled_count=0,
                levers=[], zombies=[], barricades=[], humans=[]) | changes


def half_zombie(now, start=100, **changes):
    return state(half_zombie=True, current_time=now, bite_window_started_at=start,
                 bite_window_seconds=30.0, **changes)


def human(human_id, distance):
    return dict(id=human_id, health=3, visible=True, distance=distance)


def witnessed(fact_type, attacker, victim):
    return dict(type=fact_type, lifetime="Permanent", subject_id=attacker, target_id=victim,
                has_distance=False, distance=0)


@unittest.skipUnless(shutil.which("scasp"), "requires the Ciao s(CASP) solver (gamescasp image)")
class RealSolverIntegrationTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="scasp_integration_")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        for rule in RULES:
            shutil.copy(SERVER / rule, self.directory / rule)
        shutil.copy(SERVER / "facts.pl", self.directory / "facts_temp.pl")
        self.lock = asyncio.Lock()

    def ctx(self, message, socket):
        return (UnityRequestContext()
                .add("json_message", message)
                .add("server_dir", self.directory)
                .add("scasp", shutil.which("scasp"))
                .add("facts_temp_file", self.directory / "facts_temp.pl")
                .add("facts_lock", self.lock)
                .add("send_lock", asyncio.Lock())
                .add("socket", socket)
                .build())

    async def store(self, actor, *facts):
        socket = RecordingSocket()
        await UnityStoredFactsRequest(self.ctx(dict(
            request_type="store_facts", request_id="r", actor=actor, facts=list(facts)), socket)).process()
        self.assertEqual(socket.messages[-1]["message_type"], "facts_stored")

    async def decide(self, actor, npc_state):
        socket = RecordingSocket()
        await UnityNPCDecisionRequest(self.ctx(dict(
            request_type="npc_decision", actor=actor, state=npc_state), socket)).process()
        reply = socket.messages[-1]
        self.assertEqual((reply["message_type"], reply["actor"]), ("npc_action", actor))
        return reply["action"], reply["target"]

    async def test_innocent_behavior_is_unchanged_without_memories(self):
        self.assertEqual(await self.decide("player1", state(humans=[human("player2", 1)])), ("hunt_lever", ""))
        self.assertEqual(await self.decide("player1", state(zombies=[dict(id="zombie_a", distance=1.5)])),
                         ("run_from", "zombie_a"))
        self.assertEqual(await self.decide("player1", state(levers=[dict(
            id="lever_1", state="unpulled", visible=True, distance=0.5, nearest_zombie_distance=9999)])),
            ("pull_lever", "lever_1"))

    async def test_witnessed_zombie_attack_is_remembered_in_later_decisions(self):
        nearby_victim = state(humans=[human("player2", 1.5)])
        self.assertEqual(await self.decide("player1", nearby_victim), ("hunt_lever", ""))
        await self.store("player1", witnessed("ZombieAttackWitnessed", "zombie_a", "player2"))
        # The zombie is long gone; only the stored memory explains the suspicion.
        self.assertEqual(await self.decide("player1", nearby_victim), ("run_from", "player2"))
        self.assertEqual(await self.decide("player1", state(humans=[human("player2", 3)])), ("hunt_lever", ""))

    async def test_memories_are_isolated_per_observer(self):
        await self.store("player3", witnessed("ZombieAttackWitnessed", "zombie_a", "player2"))
        self.assertEqual(await self.decide("player1", state(humans=[human("player2", 1)])), ("hunt_lever", ""))
        self.assertEqual(await self.decide("player3", state(humans=[human("player2", 1)])), ("run_from", "player2"))

    async def test_attempts_implicate_the_biter_but_confirmed_bites_implicate_both(self):
        await self.store("player1", witnessed("BiteAttemptWitnessed", "player2", "player3"))
        self.assertEqual(await self.decide("player1", state(humans=[human("player3", 1)])), ("hunt_lever", ""))
        self.assertEqual(await self.decide("player1", state(humans=[human("player2", 1)])), ("run_from", "player2"))
        await self.store("player1", witnessed("BiteWitnessed", "player2", "player3"))
        self.assertEqual(await self.decide("player1", state(humans=[human("player3", 1)])), ("run_from", "player3"))

    async def test_half_zombie_hunts_only_in_the_critical_stage(self):
        target = [human("player2", 1)]
        self.assertEqual(await self.decide("player1", half_zombie(105, humans=target)), ("hunt_lever", ""))
        self.assertEqual(await self.decide("player1", half_zombie(120, humans=target)), ("attack_human", "player2"))
        self.assertEqual(await self.decide("player1", half_zombie(120, humans=[human("player2", 3)])),
                         ("move_to_human", "player2"))
        # Critical stage overrides fleeing a nearby zombie.
        self.assertEqual(await self.decide("player1", half_zombie(
            120, humans=target, zombies=[dict(id="zombie_a", distance=1)])), ("attack_human", "player2"))

    async def test_no_target_and_expiry_produce_no_action(self):
        self.assertEqual(await self.decide("player1", half_zombie(120)), ("", ""))
        self.assertEqual(await self.decide("player1", half_zombie(130, humans=[human("player2", 1)])), ("", ""))

    async def test_a_half_zombie_target_still_appears_human(self):
        # Unity never sends another agent's infection, so the biter cannot tell.
        self.assertEqual(await self.decide("player1", half_zombie(120, humans=[human("player2", 1)])),
                         ("attack_human", "player2"))

    async def test_suspicion_fleeing_is_for_uninfected_innocents_only(self):
        await self.store("player1", witnessed("ZombieAttackWitnessed", "zombie_a", "player2"))
        # Suspicion-driven fleeing is for uninfected innocents only.
        self.assertEqual(await self.decide("player1", half_zombie(105, humans=[human("player2", 1)])),
                         ("hunt_lever", ""))


if __name__ == "__main__":
    unittest.main()

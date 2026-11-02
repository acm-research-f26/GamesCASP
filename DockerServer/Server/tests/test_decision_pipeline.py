"""NPC decision pipeline: request correlation, policy selection, context facts, explanations."""

import asyncio
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

SERVER = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SERVER))

from FactFactory import FactFactory
from UnityRequest import UnityActionRequest, UnityNPCDecisionRequest, UnityRequestContext


class RecordingSocket:
    def __init__(self):
        self.messages = []

    async def send(self, message):
        self.messages.append(json.loads(message))


def state(**changes):
    return dict(health=3, levers_to_win=2, levers_pulled_count=0, levers=[], zombies=[], barricades=[]) | changes


class FactContextTests(unittest.TestCase):
    def test_barricade_nearest_zombie_and_context_facts(self):
        facts = FactFactory.generate_npc_facts("player1", state(
            barricades=[dict(id="barricade_1", visible=True, reinforcement=30, distance=2,
                             nearest_zombie="zombie_a", nearest_zombie_distance=0.5)],
            current_action="hold_barricade", current_target="barricade_1",
            unreachable=["lever_2"]))
        self.assertIn("zombie_distance_to_barricade(barricade_1, 0.5, zombie_a).", facts)
        self.assertIn("current_action(hold_barricade(barricade_1), player1).", facts)
        self.assertIn("-reachable(lever_2, player1).", facts)

    def test_context_is_optional_and_validated(self):
        facts = FactFactory.generate_npc_facts("player1", state(current_action="hunt_lever", current_target=""))
        self.assertIn("current_action(hunt_lever, player1).", facts)
        self.assertNotIn("current_action", FactFactory.generate_npc_facts("player1", state()))
        for bad in (state(current_action="Run"), state(unreachable=["lever).\nx("]),
                    state(barricades=[dict(id="b", visible=True, reinforcement=1, distance=1, nearest_zombie="Z", nearest_zombie_distance=1)])):
            with self.subTest(state=bad), self.assertRaises(ValueError):
                FactFactory.generate_npc_facts("player1", bad)


class DecisionRequestTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.queries = []

    def ctx(self, message, socket):
        return (UnityRequestContext().add("json_message", message).add("server_dir", self.directory)
                .add("scasp", "test-scasp").add("send_lock", asyncio.Lock()).add("socket", socket).build())

    async def fake_query(self, query_file, options=()):
        self.queries.append((query_file.read_text(encoding="utf-8"), tuple(options)))
        return "X = move_to_lever(lever_1)\n"

    async def test_request_id_policy_and_timing_are_reported(self):
        socket = RecordingSocket()
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=self.fake_query)):
            for policy, rules in ((None, "half_zombie_timed_behavior.pl"), ("reasoned", "reasoned_behavior.pl")):
                message = dict(request_type="npc_decision", actor="player1", state=state(), request_id="r7")
                if policy:
                    message["policy"] = policy
                await UnityNPCDecisionRequest(self.ctx(message, socket)).process()
                self.assertIn(f"#include('{rules}').", self.queries[-1][0])
                self.assertEqual(self.queries[-1][1], ())
        reply = socket.messages[-1]
        self.assertEqual((reply["action"], reply["target"], reply["request_id"], reply["policy"]),
                         ("move_to_lever", "lever_1", "r7", "reasoned"))
        self.assertGreaterEqual(reply["server_ms"], reply["solver_ms"])
        self.assertNotIn("justification", reply)

    async def test_unknown_policy_is_rejected(self):
        message = dict(request_type="npc_decision", actor="player1", state=state(), policy="oracle")
        with self.assertRaises(ValueError):
            await UnityNPCDecisionRequest(self.ctx(message, RecordingSocket())).process()


@unittest.skipUnless(shutil.which("scasp"), "requires the Ciao s(CASP) solver (gamescasp image)")
class RealSolverExplanationTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        for rule in SERVER.glob("*.pl"):
            shutil.copy(rule, self.directory / rule.name)

    async def decide(self, npc_state, **message):
        socket = RecordingSocket()
        ctx = (UnityRequestContext().add("json_message", dict(request_type="npc_decision", actor="player1", state=npc_state, **message))
               .add("server_dir", self.directory).add("scasp", shutil.which("scasp"))
               .add("send_lock", asyncio.Lock()).add("socket", socket).build())
        await UnityNPCDecisionRequest(ctx).process()
        return socket.messages[-1]

    async def test_policies_diverge_on_the_final_lever_exception(self):
        final = state(levers_pulled_count=1,
                      levers=[dict(id="lever_1", state="unpulled", visible=True, distance=0.6, nearest_zombie_distance=1.8)],
                      zombies=[dict(id="zombie_a", distance=1.8)])
        self.assertEqual((await self.decide(final))["action"], "run_from")
        self.assertEqual((await self.decide(final, policy="reasoned"))["action"], "pull_lever")

    async def test_explanation_is_the_solvers_own_justification(self):
        danger = state(levers=[dict(id="lever_1", state="unpulled", visible=True, distance=3, nearest_zombie_distance=9)],
                       zombies=[dict(id="zombie_a", distance=1.5)])
        reply = await self.decide(danger, policy="reasoned", explain=True)
        self.assertEqual((reply["action"], reply["target"]), ("run_from", "zombie_a"))
        tree = reply["justification"]
        self.assertTrue(tree.startswith("action(run_from(zombie_a),player1)"), tree)
        self.assertIn("threat(zombie_a,player1)", tree)
        self.assertIn("distance(zombie_a,1.5,player1)", tree)
        # The lever and the idle fallback were candidates, proven defeated.
        self.assertEqual(reply["rejected"], ["move_to_lever(lever_1)", "wait"])
        self.assertGreater(reply["explain_ms"], 0)


if __name__ == "__main__":
    unittest.main()

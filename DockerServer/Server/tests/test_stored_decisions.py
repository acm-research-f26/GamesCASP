"""Verify stored knowledge reaches isolated NPC query snapshots."""

import asyncio
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from StoredFacts import StoredFact, select_actor_stored_facts, update_stored_facts
from UnityRequest import UnityNPCDecisionRequest, UnityRequestContext


STATE = {"health": 3, "levers_to_win": 3, "levers_pulled_count": 0,
         "levers": [{"id": "lever1", "state": "unpulled", "visible": True,
                     "distance": 1, "nearest_zombie_distance": 100}]}


class StoredDecisionTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.file = self.directory / "facts_temp.pl"
        self.file.write_text("noise(unknown).\n", encoding="utf-8")
        self.lock = asyncio.Lock()
        self.ctx = (UnityRequestContext().add("server_dir", self.directory)
                    .add("facts_temp_file", self.file).add("facts_lock", self.lock)
                    .add("send_lock", asyncio.Lock()).add("socket", AsyncMock())
                    .add("json_message", {"actor": "player1", "state": STATE}))

    def write_fact(self, actor="player1", lifetime="permanent", type="lever_activated",
                   subject="lever1", target="none", distance="none"):
        update_stored_facts(self.file, [StoredFact(actor, lifetime, type, subject, target, distance)])

    async def test_query_includes_both_lifetimes_only_for_its_observer(self):
        self.write_fact()
        self.write_fact(lifetime="mutable", type="agent_attacked", subject="victim", target="old")
        self.write_fact(lifetime="mutable", type="agent_attacked", subject="victim", target="latest")
        self.write_fact(type="zombie_seen", subject="remembered_zombie", distance="1")
        self.write_fact(actor="player10", subject="secret_lever")
        self.write_fact(actor="other", subject="another_secret")
        request = UnityNPCDecisionRequest(self.ctx)

        async def inspect(query):
            snapshot = next(self.directory.glob("npc_facts_*.pl")).read_text()
            self.assertIn("stored_fact(player1, permanent, lever_activated, lever1, none, none).", snapshot)
            self.assertIn("stored_fact(player1, mutable, agent_attacked, victim, latest, none).", snapshot)
            self.assertIn("stored_fact(player1, permanent, zombie_seen, remembered_zombie, none, 1).", snapshot)
            for absent in ("old,", "secret", "noise(unknown)", "distance(remembered_zombie"):
                self.assertNotIn(absent, snapshot)
            self.assertIn("health(3, player1).", snapshot)
            self.assertIn("half_zombie_timed_behavior.pl", query.read_text())
            self.assertFalse(self.lock.locked())
            return "X = hunt_lever"

        request.query_scasp = AsyncMock(side_effect=inspect)
        await request.process()
        reply = json.loads(self.ctx["socket"].send.call_args.args[0])
        self.assertEqual(reply["action"], "hunt_lever")
        self.assertEqual(reply["actor"], "player1")
        self.assertEqual(list(self.directory.glob("npc_*.pl")), [])

    async def test_query_waits_for_writer_then_keeps_immutable_memory(self):
        self.write_fact(lifetime="mutable", type="agent_attacked", target="before")
        await self.lock.acquire()
        request = UnityNPCDecisionRequest(self.ctx)

        async def inspect(query):
            snapshot_file = next(self.directory.glob("npc_facts_*.pl"))
            original = snapshot_file.read_text()
            self.assertIn("lever1, after, none", original)
            async with self.lock:
                self.write_fact(lifetime="mutable", type="agent_attacked", target="later")
            self.assertEqual(snapshot_file.read_text(), original)
            return "X = pull_lever(lever1)"

        request.query_scasp = AsyncMock(side_effect=inspect)
        task = asyncio.create_task(request.process())
        try:
            await asyncio.sleep(0)
            request.query_scasp.assert_not_called()
            self.write_fact(lifetime="mutable", type="agent_attacked", target="after")
        finally:
            self.lock.release()
        await asyncio.wait_for(task, 2)

    async def test_no_memory_still_queries_and_solver_failure_cleans_up(self):
        self.assertEqual(select_actor_stored_facts(self.file.read_text(), "player1"), "")
        request = UnityNPCDecisionRequest(self.ctx)
        request.query_scasp = AsyncMock(side_effect=RuntimeError("solver unavailable"))
        with self.assertRaisesRegex(RuntimeError, "solver unavailable"):
            await request.process()
        self.assertEqual(list(self.directory.glob("npc_*.pl")), [])
        self.assertFalse(self.lock.locked())

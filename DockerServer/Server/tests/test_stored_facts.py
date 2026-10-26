import asyncio
from functools import partial
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import websockets

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from SocketHandler import handler


class StoredFactTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.file = self.directory / "facts_temp.pl"
        self.file.write_text("baseline.\n", encoding="utf-8")
        server = await websockets.serve(partial(
            handler, server_dir=self.directory, scasp="unused", facts_lock=asyncio.Lock()
        ), "127.0.0.1", 0)
        self.uri = f"ws://127.0.0.1:{server.sockets[0].getsockname()[1]}"

        async def close():
            server.close()
            await server.wait_closed()
        self.addAsyncCleanup(close)

    def fact(self, **changes):
        return dict(type="LeverActivated", lifetime="Permanent", subject_id="lever1",
                    target_id="", has_distance=False, distance=0) | changes

    async def send(self, socket, facts, actor="player1", request_id="test-request"):
        await socket.send(json.dumps(dict(
            request_type="store_facts", request_id=request_id, actor=actor, facts=facts
        )))
        return json.loads(await asyncio.wait_for(socket.recv(), 2))

    async def test_permanent_replays_and_reconnects_are_idempotent(self):
        for _ in range(2):
            async with websockets.connect(self.uri) as socket:
                reply = await self.send(socket, [self.fact(), self.fact()])
                self.assertEqual(reply, dict(
                    message_type="facts_stored", actor="player1", request_id="test-request"
                ))
                # An acknowledgment proves the file was already updated.
                self.assertEqual(self.file.read_text(), "baseline.\n"
                    "stored_fact(player1, permanent, lever_activated, lever1, none, none).\n")

    async def test_mutable_replacement_preserves_other_knowledge_and_legacy_events(self):
        fact = self.fact(type="AgentAttacked", lifetime="Mutable", subject_id="survivor1",
                         target_id="zombie1", has_distance=True, distance=0)
        async with websockets.connect(self.uri) as first, websockets.connect(self.uri) as second:
            await self.send(first, [fact])
            await self.send(second, [fact], actor="player2")
            await first.send('{"request_type":"fact","message_type":"heard_noise"}')
            updated = fact | dict(target_id="zombie2", distance=3.5)
            await self.send(first, [updated, self.fact(), self.fact(target_id="player2")])
        contents = self.file.read_text()
        self.assertIn("baseline.\n", contents)
        self.assertIn("noise(unknown).\n", contents)
        self.assertNotIn("stored_fact(player1, mutable, agent_attacked, survivor1, zombie1,", contents)
        self.assertIn("stored_fact(player1, mutable, agent_attacked, survivor1, zombie2, 3.5).", contents)
        self.assertIn("stored_fact(player2, mutable, agent_attacked, survivor1, zombie1, 0).", contents)
        self.assertEqual(contents.count("permanent, lever_activated"), 2)

    async def test_invalid_batches_do_not_partially_write_and_connection_recovers(self):
        bad_facts = [None, [], {}, self.fact(type=[]), self.fact(type="Unknown"),
                     self.fact(lifetime="Transient"), self.fact(subject_id="Lever1"),
                     self.fact(target_id="zombie).\nalarm_raised. %"),
                     self.fact(has_distance="true"), self.fact(has_distance=True, distance=-1),
                     self.fact(has_distance=True, distance=True),
                     self.fact(has_distance=True, distance=10**400),
                     self.fact(has_distance=True, distance=float("inf"))]
        async with websockets.connect(self.uri) as socket:
            for bad in bad_facts:
                with self.subTest(fact=bad):
                    reply = await self.send(socket, [self.fact(), bad])
                    self.assertEqual(reply["error"], "invalid_request")
                    self.assertEqual(reply["request_id"], "test-request")
                    self.assertEqual(self.file.read_text(), "baseline.\n")
            for facts in ([], None, {}, [self.fact()] * 257):
                self.assertEqual((await self.send(socket, facts))["error"], "invalid_request")
            self.assertEqual((await self.send(socket, [self.fact()], actor="Player"))["error"], "invalid_request")
            self.assertEqual((await self.send(socket, [self.fact()], request_id=""))["error"], "invalid_request")
            self.assertEqual((await self.send(socket, [self.fact()]))["message_type"], "facts_stored")

    async def test_failed_write_is_not_acknowledged_and_does_not_replace_file(self):
        async with websockets.connect(self.uri) as socket:
            # Fail the final replace, after the temporary file has been written.
            with patch.object(Path, "replace", side_effect=OSError("disk unavailable")):
                with self.assertLogs("SocketHandler", level="ERROR"):
                    reply = await self.send(socket, [self.fact()])
            self.assertEqual(reply["error"], "processing_failed")
            self.assertEqual(reply["request_id"], "test-request")
            self.assertEqual(self.file.read_text(), "baseline.\n")
            self.assertFalse(self.file.with_suffix(".pl.tmp").exists())
            self.assertEqual((await self.send(socket, [self.fact()]))["message_type"], "facts_stored")

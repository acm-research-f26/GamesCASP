import asyncio
from functools import partial
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import websockets

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from FactFactory import FactFactory
from SocketHandler import handler
from UnityRequest import UnityActionRequest, UnityRequestContext, UnityTempFactRequest
from UnityRequestFactory import UnityRequestFactory


class RequestTests(unittest.TestCase):
    def test_dispatch_and_unknown_types(self):
        ctx = UnityRequestContext()
        self.assertIsInstance(UnityRequestFactory.make_request("fact", ctx), UnityTempFactRequest)
        self.assertIsInstance(UnityRequestFactory.make_request("get_action", ctx), UnityActionRequest)
        for value in (None, "unknown", [], {}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                UnityRequestFactory.make_request(value, ctx)

    def test_supported_facts(self):
        expected = {
            "heard_noise": "noise(unknown).\n",
            "vase_broken": "broken_vase(player).\n",
            "suspicious_sighting": "suspicious_sighting(player).\nplayer_in_restricted_area.\n",
            "diamond_broken": "diamond_saw_broken.\n",
            "alarm_raised": "alarm_raised.\n",
            "player_seen": "player_seen.\n",
        }
        for event, fact in expected.items():
            with self.subTest(event=event):
                self.assertEqual(FactFactory.generate_fact_from_json({
                    "message_type": event, "culprit": "player"
                }), fact)

    def test_invalid_facts_and_prolog_injection(self):
        for message in ({}, {"message_type": "unknown"}, {"message_type": []}):
            with self.subTest(message=message), self.assertRaises(ValueError):
                FactFactory.generate_fact_from_json(message)
        for culprit in (None, 5, [], "Player", "", "two words", "player).\nalarm_raised.\n%"):
            with self.subTest(culprit=culprit), self.assertRaises(ValueError):
                FactFactory.generate_fact_from_json({"message_type": "vase_broken", "culprit": culprit})
        self.assertEqual(FactFactory.generate_fact_from_json({
            "message_type": "vase_broken", "culprit": "guard_1"
        }), "broken_vase(guard_1).\n")

    def test_output_binding_without_trailing_commentary(self):
        request = UnityActionRequest(UnityRequestContext())
        for output in (
            "X = investigate_noise",
            "Model\n  X = investigate_noise.\nTiming: 10 ms",
            "\x1b[32mX = investigate_noise\x1b[0m\r\n",
        ):
            with self.subTest(output=output):
                self.assertEqual(request.parse_output(output), "investigate_noise")
        for output in ("", "no models", "X = action(argument)", "X = action trailing text"):
            with self.subTest(output=output), self.assertRaises(RuntimeError):
                request.parse_output(output)


class AsyncRequestTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.facts = self.directory / "facts_temp.pl"
        self.facts.write_text("baseline.\n", encoding="utf-8")
        self.lock = asyncio.Lock()
        self.ctx = (UnityRequestContext()
            .add("server_dir", self.directory)
            .add("scasp", "test-scasp")
            .add("facts_temp_file", self.facts)
            .add("facts_lock", self.lock)
            .add("socket", AsyncMock())
        ).build()

    async def start_server(self):
        server = await websockets.serve(partial(
            handler, server_dir=self.directory, scasp="test-scasp", facts_lock=self.lock
        ), "127.0.0.1", 0)
        async def close_server():
            server.close()
            await server.wait_closed()
        self.addAsyncCleanup(close_server)
        return f"ws://127.0.0.1:{server.sockets[0].getsockname()[1]}"

    async def test_bad_messages_recover_and_reconnect_preserves_facts(self):
        uri = await self.start_server()
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(return_value="X = wander_randomly\n")):
            async with websockets.connect(uri) as socket:
                bad_messages = ("not json", "[]", "{}", '{"request_type": []}',
                    '{"request_type":"fact"}',
                    '{"request_type":"fact","message_type":"vase_broken","culprit":"Player"}')
                for message in bad_messages:
                    await socket.send(message)
                    reply = json.loads(await asyncio.wait_for(socket.recv(), 2))
                    self.assertEqual(reply["error"], "invalid_request")
                await socket.send(json.dumps({"request_type": "fact", "message_type": "heard_noise"}))
                await socket.send(json.dumps({"request_type": "get_action"}))
                reply = json.loads(await asyncio.wait_for(socket.recv(), 2))
                self.assertEqual(reply, {"message_type": "possible_actions", "possible_actions": ["wander_randomly"]})
            async with websockets.connect(uri) as socket:
                await socket.send('{"request_type":"get_action"}')
                await asyncio.wait_for(socket.recv(), 2)
            self.assertEqual(self.facts.read_text(), "baseline.\nnoise(unknown).\n")

    async def test_solver_errors_keep_connection_usable(self):
        uri = await self.start_server()
        query = AsyncMock(side_effect=[RuntimeError("solver timed out"), "no binding", "X = wander_randomly"])
        with patch.object(UnityActionRequest, "query_scasp", new=query):
            async with websockets.connect(uri) as socket:
                for expected in ("error", "error", "possible_actions"):
                    await socket.send('{"request_type":"get_action"}')
                    if expected == "error":
                        with self.assertLogs("SocketHandler", level="ERROR"):
                            reply = json.loads(await asyncio.wait_for(socket.recv(), 2))
                        self.assertEqual(reply["error"], "processing_failed")
                    else:
                        reply = json.loads(await asyncio.wait_for(socket.recv(), 2))
                    self.assertEqual(reply["message_type"], expected)

    async def test_slow_query_keeps_server_responsive_and_facts_stable(self):
        uri = await self.start_server()
        started, release = asyncio.Event(), asyncio.Event()
        async def slow_query():
            started.set()
            await release.wait()
            self.assertEqual(self.facts.read_text(), "baseline.\n")
            return "X = wander_randomly"
        with patch.object(UnityActionRequest, "query_scasp", new=AsyncMock(side_effect=slow_query)):
            async with websockets.connect(uri) as first, websockets.connect(uri) as second:
                try:
                    await first.send('{"request_type":"get_action"}')
                    await asyncio.wait_for(started.wait(), 2)
                    await second.send('invalid json')
                    reply = json.loads(await asyncio.wait_for(second.recv(), 2))
                    self.assertEqual(reply["error"], "invalid_request")
                    self.ctx.add("json_message", {"message_type": "heard_noise"})
                    writer = asyncio.create_task(UnityTempFactRequest(self.ctx).process())
                    await asyncio.sleep(0)
                    self.assertFalse(writer.done())
                finally:
                    release.set()
                await asyncio.wait_for(first.recv(), 2)
                await asyncio.wait_for(writer, 2)
        self.assertIn("noise(unknown).", self.facts.read_text())

    async def test_real_child_process_success_failure_timeout_and_cancellation(self):
        # Substitute Python for s(CASP), but use real OS child processes and pipes.
        create_process = asyncio.create_subprocess_exec
        scripts = [
            "import pathlib; assert pathlib.Path('facts_temp.pl').exists(); print('X = wander_randomly')",
            "import sys; sys.stderr.write('solver failure'); sys.exit(1)",
            "import time; time.sleep(60)",
            "import time; time.sleep(60)",
        ]
        children = []
        child_started = asyncio.Event()
        async def substitute(*args, **kwargs):
            self.assertEqual(args, ("test-scasp", "-s1", "mainQuery.pl"))
            child = await create_process(sys.executable, "-c", scripts.pop(0), **kwargs)
            children.append(child)
            child_started.set()
            return child
        request = UnityActionRequest(self.ctx)
        with patch("UnityRequest.asyncio.create_subprocess_exec", side_effect=substitute):
            self.assertIn("X = wander_randomly", await request.query_scasp())
            with self.assertRaisesRegex(RuntimeError, "solver failure"):
                await request.query_scasp()
            with patch("UnityRequest.SOLVER_TIMEOUT_SECONDS", 0.05):
                with self.assertRaisesRegex(RuntimeError, "timed out"):
                    await request.query_scasp()
            self.assertIsNotNone(children[-1].returncode)
            child_started.clear()
            task = asyncio.create_task(request.process())
            await asyncio.wait_for(child_started.wait(), 2)
            task.cancel()
            with self.assertRaises(asyncio.CancelledError):
                await task
            self.assertIsNotNone(children[-1].returncode)
            self.assertFalse(self.lock.locked())


if __name__ == "__main__":
    unittest.main()

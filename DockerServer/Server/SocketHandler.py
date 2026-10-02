import asyncio
from functools import partial
import json
import logging
from pathlib import Path
import shutil

import websockets

from UnityRequest import UnityRequestContext
from UnityRequestFactory import UnityRequestFactory


CURRENT_DIR = Path(__file__).resolve().parent
logger = logging.getLogger(__name__)


async def handler(socket, *, server_dir, scasp, facts_lock):
    try:
        async for message in socket:
            try:
                json_message = json.loads(message)
                if not isinstance(json_message, dict):
                    raise ValueError("Request must be a JSON object")

                ctx = (UnityRequestContext()
                    .add("json_message", json_message)
                    .add("facts_temp_file", server_dir / "facts_temp.pl")
                    .add("server_dir", server_dir)
                    .add("scasp", scasp)
                    .add("facts_lock", facts_lock)
                    .add("socket", socket)
                ).build()

                request = UnityRequestFactory.make_request(
                    json_message.get("request_type"), ctx
                )
                await request.process()
            except ValueError as exc:
                await socket.send(json.dumps({
                    "message_type": "error",
                    "error": "invalid_request",
                    "detail": str(exc),
                }))
            except (RuntimeError, OSError) as exc:
                logger.exception("Request processing failed")
                await socket.send(json.dumps({
                    "message_type": "error",
                    "error": "processing_failed",
                    "detail": str(exc),
                }))
    except websockets.ConnectionClosed:
        logger.info("Client disconnected")


async def mainTask():
    scasp = shutil.which("scasp")
    if scasp is None:
        raise RuntimeError("scasp was not found on PATH")

    # One shared game session per server process; reconnects preserve its facts.
    shutil.copyfile(CURRENT_DIR / "facts.pl", CURRENT_DIR / "facts_temp.pl")
    connection_handler = partial(
        handler, server_dir=CURRENT_DIR, scasp=scasp, facts_lock=asyncio.Lock()
    )
    async with websockets.serve(connection_handler, "0.0.0.0", 6767):
        logger.info("WebSocket server running on port 6767; s(CASP): %s", scasp)
        await asyncio.Future()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    asyncio.run(mainTask())

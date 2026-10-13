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


async def process_request(request, socket, send_lock):
    try:
        await request.process()

    except ValueError as exc:
        async with send_lock:
            await socket.send(json.dumps({
                "message_type": "error",
                "error": "invalid_request",
                "detail": str(exc),
            }))

    except (RuntimeError, OSError) as exc:
        logger.exception("Request processing failed")

        async with send_lock:
            await socket.send(json.dumps({
                "message_type": "error",
                "error": "processing_failed",
                "detail": str(exc),
            }))


async def handler(socket, *, server_dir, scasp, facts_lock):
    pending_tasks = set()
    send_lock = asyncio.Lock()

    try:
        async for message in socket:
            try:
                json_message = json.loads(message)

                if not isinstance(json_message, dict):
                    raise ValueError("Request must be a JSON object")

                request_type = json_message.get("request_type")

                ctx = (
                    UnityRequestContext()
                    .add("json_message", json_message)
                    .add("facts_temp_file", server_dir / "facts_temp.pl")
                    .add("server_dir", server_dir)
                    .add("scasp", scasp)
                    .add("facts_lock", facts_lock)
                    .add("send_lock", send_lock)
                    .add("socket", socket)
                    .build()
                )

                request = UnityRequestFactory.make_request(
                    request_type,
                    ctx
                )

                # Action queries may run concurrently.
                # Both action queries and NPC decisions may run concurrently.
                if request_type in ("get_action", "npc_decision"):
                    task = asyncio.create_task(
                        process_request(request, socket, send_lock)
                    )

                    pending_tasks.add(task)
                    task.add_done_callback(pending_tasks.discard)

                # Fact/state updates stay ordered.
                else:
                    await process_request(
                        request,
                        socket,
                        send_lock
                    )

            except ValueError as exc:
                async with send_lock:
                    await socket.send(json.dumps({
                        "message_type": "error",
                        "error": "invalid_request",
                        "detail": str(exc),
                    }))

    except websockets.ConnectionClosed:
        logger.info("Client disconnected")

    finally:
        for task in pending_tasks:
            task.cancel()

        if pending_tasks:
            await asyncio.gather(
                *pending_tasks,
                return_exceptions=True
            )


async def mainTask():
    scasp = shutil.which("scasp")

    if scasp is None:
        raise RuntimeError("scasp was not found on PATH")

    # Initialize the shared live game-state facts.
    shutil.copyfile(
        CURRENT_DIR / "facts.pl",
        CURRENT_DIR / "facts_temp.pl"
    )

    facts_lock = asyncio.Lock()

    connection_handler = partial(
        handler,
        server_dir=CURRENT_DIR,
        scasp=scasp,
        facts_lock=facts_lock
    )

    async with websockets.serve(
        connection_handler,
        "0.0.0.0",
        6767
    ):
        logger.info(
            "WebSocket server running on port 6767; s(CASP): %s",
            scasp
        )

        await asyncio.Future()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    asyncio.run(mainTask())
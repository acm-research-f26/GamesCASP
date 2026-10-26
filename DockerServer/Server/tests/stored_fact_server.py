"""Isolated Python endpoint for the Unity storage verifier; no solver required."""

import argparse
import asyncio
from functools import partial
import json
from pathlib import Path
import sys

import websockets

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from SocketHandler import handler


class DropFirstAcknowledgment:
    """Simulate a lost reply after the real handler successfully writes the file."""
    def __init__(self, socket):
        self.socket = socket
        self.dropped = False

    def __aiter__(self):
        return self.socket.__aiter__()

    async def send(self, message):
        if not self.dropped and json.loads(message).get("message_type") == "facts_stored":
            self.dropped = True
            return
        await self.socket.send(message)


async def main(directory, drop_first_ack):
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "facts_temp.pl").write_text("verification_baseline.\n", encoding="utf-8")
    process = partial(handler, server_dir=directory, scasp="unused", facts_lock=asyncio.Lock())

    async def connect(socket):
        await process(DropFirstAcknowledgment(socket) if drop_first_ack else socket)

    async with websockets.serve(connect, "127.0.0.1", 0) as server:
        port = server.sockets[0].getsockname()[1]
        print(json.dumps({"url": f"ws://127.0.0.1:{port}"}), flush=True)
        await asyncio.Future()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--drop-first-ack", action="store_true")
    args = parser.parse_args()
    asyncio.run(main(args.directory, args.drop_first_ack))

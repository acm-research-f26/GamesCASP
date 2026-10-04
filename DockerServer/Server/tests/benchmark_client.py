import asyncio
import json
import time

import websockets


async def benchmark(count):
    uri = "ws://127.0.0.1:6767"

    async with websockets.connect(uri) as socket:
        request = json.dumps({
            "request_type": "get_action"
        })

        start = time.perf_counter()

        for _ in range(count):
            await socket.send(request)

        responses = []

        for _ in range(count):
            response = await socket.recv()
            responses.append(json.loads(response))

        elapsed = time.perf_counter() - start

        print(
            f"{count:2d} queries | "
            f"batch = {elapsed * 1000:.2f} ms | "
            f"avg = {(elapsed / count) * 1000:.2f} ms/query"
        )


async def main():
    for count in [1, 5, 10, 30]:
        await benchmark(count)


if __name__ == "__main__":
    asyncio.run(main())
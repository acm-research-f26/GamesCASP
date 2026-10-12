"""THRILLVANE: 30 concurrent, mixed-scenario NPC reasoning tests.

Run on Windows: python npc_stress_test.py
Requires: python -m pip install websockets
Does not write to or modify the game repository or Docker container.
"""

import asyncio
import json
import statistics
import time

import websockets

SERVER_URL = "ws://localhost:6767"
TIMEOUT_SECONDS = 60
REPEATS_PER_SCENARIO = 3


def lever(lever_id, distance=7, nearest_zombie_distance=20):
    return {
        "id": lever_id,
        "state": "unpulled",
        "visible": True,
        "distance": distance,
        "nearest_zombie_distance": nearest_zombie_distance,
    }


def barricade(barricade_id, distance=1, reinforcement=20, zombie_id=None):
    return {
        "id": barricade_id,
        "visible": True,
        "distance": distance,
        "reinforcement": reinforcement,
        "zombie_distances": {zombie_id: 0.5} if zombie_id else {},
    }


def build_case(index):
    actor = f"npc_{index}"
    target = f"lever_{index}"
    zombie = f"zombie_{index}"
    barrier = f"barricade_{index}"
    state = {
        "health": 3,
        "levers_to_win": 15,
        "levers_pulled_count": 0,
        "levers": [],
        "zombies": [],
        "barricades": [],
    }
    kind = index % 10

    if kind == 0:  # Immediate danger defeats lever action.
        name = "flee nearby zombie"
        state["levers"] = [lever(target)]
        state["zombies"] = [{"id": zombie, "distance": 1}]
        expected = ("run_from", zombie)
    elif kind == 1:
        name = "hold urgent barricade"
        state["zombies"] = [{"id": zombie, "distance": 10}]
        state["barricades"] = [barricade(barrier, 1, 20, zombie)]
        expected = ("hold_barricade", barrier)
    elif kind == 2:
        name = "approach urgent barricade"
        state["zombies"] = [{"id": zombie, "distance": 10}]
        state["barricades"] = [barricade(barrier, 6, 20, zombie)]
        expected = ("move_to_barricade", barrier)
    elif kind == 3:
        name = "pull nearby lever"
        state["levers"] = [lever(target, distance=1)]
        expected = ("pull_lever", target)
    elif kind == 4:
        name = "approach distant lever"
        state["levers"] = [lever(target, distance=7)]
        expected = ("move_to_lever", target)
    elif kind == 5:
        name = "hunt for unseen lever"
        expected = ("hunt_lever", "")
    elif kind == 6:
        name = "wait after winning"
        state["levers_pulled_count"] = 15
        expected = ("wait", "")
    elif kind == 7:
        name = "prefer safe over threatened lever"
        threatened = f"threatened_{index}"
        state["levers"] = [
            lever(threatened, distance=2, nearest_zombie_distance=2),
            lever(target, distance=7, nearest_zombie_distance=20),
        ]
        expected = ("move_to_lever", target)
    elif kind == 8:
        name = "barricade priority over lever"
        state["levers"] = [lever(target, distance=1)]
        state["zombies"] = [{"id": zombie, "distance": 10}]
        state["barricades"] = [barricade(barrier, 6, 20, zombie)]
        expected = ("move_to_barricade", barrier)
    else:
        name = "flee priority over barricade"
        state["zombies"] = [{"id": zombie, "distance": 1}]
        state["barricades"] = [barricade(barrier, 1, 20, zombie)]
        expected = ("run_from", zombie)

    return {
        "index": index,
        "name": name,
        "actor": actor,
        "expected": expected,
        "message": {"request_type": "npc_decision", "actor": actor, "state": state},
    }


async def run_case(case):
    start = time.perf_counter()
    try:
        async with websockets.connect(SERVER_URL) as socket:
            await socket.send(json.dumps(case["message"]))
            raw = await asyncio.wait_for(socket.recv(), timeout=TIMEOUT_SECONDS)
        response = json.loads(raw)
        expected_action, expected_target = case["expected"]
        success = (
            response.get("message_type") == "npc_action"
            and response.get("actor") == case["actor"]
            and response.get("action") == expected_action
            and response.get("target") == expected_target
        )
        return case, success, response, time.perf_counter() - start
    except Exception as exc:
        return case, False, f"{type(exc).__name__}: {exc}", time.perf_counter() - start


async def main():
    cases = [build_case(i) for i in range(10 * REPEATS_PER_SCENARIO)]
    print(f"THRILLVANE NPC Stress Test #3: {len(cases)} concurrent mixed decisions")
    print(f"Server: {SERVER_URL}")
    print("No project files are modified.\n")

    start = time.perf_counter()
    results = await asyncio.gather(*(run_case(case) for case in cases))
    wall = time.perf_counter() - start

    passed = sum(success for _, success, _, _ in results)
    for case, success, response, elapsed in results:
        label = "PASS" if success else "FAIL"
        print(f"{label:4}  {case['actor']:7}  {case['name']:36} {elapsed * 1000:7.1f} ms")
        if not success:
            print(f"      Expected: {case['expected']}")
            print(f"      Received: {response}")

    latencies = [elapsed for _, _, _, elapsed in results]
    print("\n--- RESULTS ---")
    print(f"Successful: {passed} of {len(cases)}")
    print(f"Total wall time: {wall:.3f} seconds")
    print(f"Mean client round-trip: {statistics.mean(latencies) * 1000:.1f} ms")
    print(f"Max client round-trip: {max(latencies) * 1000:.1f} ms")
    print(f"Aggregate throughput: {len(cases) / wall:.1f} requests/second")
    print("PASS: all scenarios correct" if passed == len(cases) else "FAIL: investigate mismatches above")
    return passed == len(cases)


if __name__ == "__main__":
    try:
        ok = asyncio.run(main())
    except KeyboardInterrupt:
        print("Interrupted.")
        raise SystemExit(130)
    raise SystemExit(0 if ok else 1)

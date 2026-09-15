import subprocess
import json
import os
import re
import asyncio
import shutil
import websockets
import time

current_dir = os.path.dirname(os.path.abspath(__file__))

rules_file = os.path.join(current_dir, "rules.pl")
facts_temp_file = os.path.join(current_dir, "facts_temp.pl")

SCASP = shutil.which("scasp")

if SCASP is None:
    raise RuntimeError("scasp was not found in the Docker container")

print(f"s(CASP): {SCASP}")
print(f"Prolog file: {rules_file}")

def run_scasp(query: str):
    query_file = os.path.join(current_dir, "run_temp.pl")
    with open(rules_file) as rules_src, \
         open(facts_temp_file) as facts_src, \
         open(query_file, "w") as dst:
        dst.write(rules_src.read())
        dst.write("\n")
        dst.write(facts_src.read())
        dst.write(f"\n?- {query}.\n")

    result = subprocess.run(
        ["scasp", "-s0", query_file],
        capture_output=True,
        text=True,
        timeout=30
    )
    if result.returncode != 0:
        raise RuntimeError(f"scasp failed: {result.stderr}")
    return result.stdout

def parse_scasp_output(output: str):
    """Parse Ciao scasp -s0 text output into a list of {var: value} binding dicts."""
    solutions = []
    blocks = re.split(r'\n\s*ANSWER:', output)
    for block in blocks[1:]:
        bindings = {}
        bindings_match = re.search(r'BINDINGS:\s*\n(.*?)(?:\n\s*\n|\Z)', block, re.DOTALL)
        if bindings_match:
            for line in bindings_match.group(1).strip().splitlines():
                line = line.strip()
                if not line or "=" not in line:
                    continue
                var, val = line.split("=", 1)
                bindings[var.strip()] = val.strip()
        solutions.append(bindings)
    return solutions

async def handler(socket):
    shutil.copy(f"{current_dir}/facts.pl", f"{current_dir}/facts_temp.pl")

    rtt = 0
    with open(facts_temp_file, "a") as factsFile:
        print("client connected!")
        try:
            async for message in socket:
                jsonMessage = json.loads(message)

                print(f"message type received: {jsonMessage['message_type']}")

                if(jsonMessage["message_type"] == "heard_noise"):
                    factsFile.write("noise(unknown).\n")
                elif(jsonMessage["message_type"] == "vase_broken"):
                    factsFile.write(f"broken_vase({jsonMessage['culprit']}).\n")
                    print(f"culprit was {jsonMessage['culprit']}")
                elif(jsonMessage["message_type"] == "suspicious_sighting"):
                    factsFile.write(f"suspicious_sighting(player).\n")
                    factsFile.write("player_in_restricted_area.\n")
                elif(jsonMessage["message_type"] == "diamond_broken"):
                    factsFile.write("diamond_saw_broken.\n")
                elif(jsonMessage["message_type"] == "alarm_raised"):
                    factsFile.write("alarm_raised.\n")
                elif(jsonMessage["message_type"] == "player_seen"):
                    factsFile.write("player_seen.\n")
                elif(jsonMessage["message_type"] == "get_action"):
                    factsFile.flush()  # make sure facts are on disk before scasp reads them

                    startTime = time.time()
                    rawOutput = run_scasp("chosen_action(X)")
                    endTime = time.time()

                    rtt = 0.95 * rtt + 0.05 * (endTime - startTime)
                    print(f"current rtt here is {rtt}")

                    print("raw scasp output:")
                    print(rawOutput)

                    solutions = parse_scasp_output(rawOutput)
                    solutionArr = [sol["X"] for sol in solutions if "X" in sol]

                    dataToSendBack = {
                        "message_type": "possible_actions",
                        "possible_actions": solutionArr
                    }
                    print(f"data being sent back is: {dataToSendBack}")
                    await socket.send(json.dumps(dataToSendBack))
                else:
                    raise ValueError(f"json message is invalid, got {jsonMessage['message_type']}")
                
                factsFile.flush()


        except websockets.ConnectionClosed:
            print("Client disconnected")

async def mainTask():
    async with websockets.serve(handler, "0.0.0.0", 6767):
        print("WebSocket server running!")
        await asyncio.Future()

asyncio.run(mainTask())
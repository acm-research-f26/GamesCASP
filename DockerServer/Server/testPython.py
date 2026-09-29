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

def run_scasp():
    result = subprocess.run(
        ["scasp", "-s1", "mainQuery.pl"],
        capture_output=True,
        text=True,
        timeout=30
    )
    if result.returncode != 0:
        raise RuntimeError(f"scasp failed: {result.stderr}")
    return result.stdout

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

                    rawOutput = run_scasp()
                    
                    endTime = time.time()

                    rtt = 0.95 * rtt + 0.05 * (endTime - startTime)
                    print(f"current rtt here is {rtt}")

                    print("raw scasp output:")
                    print(rawOutput)

                    parsedOutput = rawOutput.split("X = ")[1].strip()

                    print("parsed scasp output:")
                    print(rawOutput)

                    dataToSendBack = {
                        "message_type": "possible_actions",
                        "possible_actions": [parsedOutput]
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
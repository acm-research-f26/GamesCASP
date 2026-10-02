import subprocess
import json
import os
import re
import asyncio
import shutil
import websockets
import time

from FactFactory import FactFactory

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
        try:
            async for message in socket:
                jsonMessage = json.loads(message)
                if (jsonMessage["message_type"] == "get_action"):
                    factsFile.flush()  # is this necessary, nothing written b4hand?

                    startTime = time.time()

                    rawOutput = run_scasp()
                    
                    endTime = time.time()

                    rtt = 0.95 * rtt + 0.05 * (endTime - startTime)
                    print(f"current rtt here is {rtt}")

                    print("raw scasp output:")
                    print(rawOutput)

                    # what if the incoming query doesn't the variable X?
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
                    fact = FactFactory.generate_fact_from_json(jsonMessage)
                    factsFile.write(fact)
                
                factsFile.flush()
                
        except websockets.ConnectionClosed:
            print("Client disconnected")

async def mainTask():
    async with websockets.serve(handler, "0.0.0.0", 6767):
        print("WebSocket server running!")
        await asyncio.Future()

asyncio.run(mainTask())
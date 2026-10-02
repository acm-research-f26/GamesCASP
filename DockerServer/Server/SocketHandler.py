import subprocess
import json
import os
import re
import asyncio
import shutil
import websockets
import time
from typing import Any

from FactFactory import FactFactory

current_dir = os.path.dirname(os.path.abspath(__file__))

rules_file = os.path.join(current_dir, "rules.pl")
facts_temp_file = os.path.join(current_dir, "facts_temp.pl")

SCASP = shutil.which("scasp")

if SCASP is None:
    raise RuntimeError("scasp was not found in the Docker container")

print(f"s(CASP): {SCASP}")
print(f"Prolog file: {rules_file}")

def query_scasp():
    result = subprocess.run(
        ["scasp", "-s1", "mainQuery.pl"],
        capture_output=True,
        text=True,
        timeout=30
    )
    if result.returncode != 0:
        raise RuntimeError(f"scasp failed: {result.stderr}")
    return result.stdout

def delta_time_and_output_of_function(f):
    startTime = time.time()
    rawOutput = f()
    endTime = time.time()

    return (endTime - startTime, rawOutput)

def parse_output(raw_output):
    return raw_output.split("X = ")[1].strip()

def jsonize_parsed_output(parsed_output):
        dataToSendBack = {
            "message_type": "possible_actions",
            "possible_actions": [parsed_output]
        }

        return json.dumps(dataToSendBack)

def copy_facts_files_from(current_dir):
    shutil.copy(f"{current_dir}/facts.pl", f"{current_dir}/facts_temp.pl")


# should only get called after unity time interval, explicit get_action
# OR on a critical game event (e.g. player hits another)

# Sockets should not be called every frame
async def handler(socket):
    copy_facts_files_from(current_dir)

    with open(facts_temp_file, "a") as temp_facts_file:
        try:
            async for message in socket:
                jsonMessage = json.loads(message)

                # get action should be the last message_type
                # UNITY SHOULD LIST FACTS FIRST, ASK FOR ACTION AS LAST PART OF 
                # REQUEST

                fact = FactFactory.generate_fact_from_json(jsonMessage)
                temp_facts_file.write(fact)
                temp_facts_file.flush()
                
            raw_output = query_scasp()
            clean_output = parse_output(raw_output)

            await socket.send(jsonize_parsed_output(clean_output))
                    
                
        except websockets.ConnectionClosed:
            print("Client disconnected")

async def mainTask():
    async with websockets.serve(handler, "0.0.0.0", 6767):
        print("WebSocket server running!")
        await asyncio.Future()

asyncio.run(mainTask())
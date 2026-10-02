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
import UnityRequest as ur

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

    with open(facts_temp_file, "a") as temp_facts_file, open(rules_file, "a") as facts_file:
        try:
            async for message in socket:
                jsonMessage = json.loads(message)

                unity_request_ctx = (ur.UnityRequestContext()
                    .add("json_message", jsonMessage)
                    .add("temp_facts_file", temp_facts_file)
                    .add("facts_file", facts_file)
                    .add("socket", socket)
                ).build()

                unity_request = None

                if jsonMessage['request_type'] == 'fact':
                    unity_request = ur.UnityTempFactRequest(unity_request_ctx)
                elif jsonMessage['request_type'] == 'get_action':
                    unity_request = ur.UnityActionRequest(unity_request_ctx)

                if unity_request:
                    await unity_request.process()        

        except websockets.ConnectionClosed:
            print("Client disconnected")

async def mainTask():
    async with websockets.serve(handler, "0.0.0.0", 6767):
        print("WebSocket server running!")
        await asyncio.Future()

asyncio.run(mainTask())
import subprocess
import websockets
import asyncio
import json

from FactFactory import FactFactory

# ctx = UnityRequestContext().add(1).add("hi").build()

class UnityRequestContext:
    def __init__(self):
        self.vars = {}

    def add(self, name, value):
        self.vars[name] = value
        return self

    def __getitem__(self, name):
        return self.vars[name]

    def build(self):
        return self


class UnityRequest:
    def __init__(self, ctx):
        self.ctx = ctx

    async def process(self):
        pass

class UnityActionRequest(UnityRequest):
    async def process(self):
        raw_output = self.query_scasp()
        clean_output = self.parse_output(raw_output)
        socket = self.ctx["socket"]
        await socket.send(self.jsonize_parsed_output(clean_output))  

    # HELPERS
    def query_scasp(self):
        result = subprocess.run(
            ["scasp", "-s1", "mainQuery.pl"],
            capture_output=True,
            text=True,
            timeout=30
        )
        if result.returncode != 0:
            raise RuntimeError(f"scasp failed: {result.stderr}")
        return result.stdout
    
    def parse_output(self, raw_output):
        return raw_output.split("X = ")[1].strip()

    def jsonize_parsed_output(self, parsed_output):
        dataToSendBack = {
            "message_type": "possible_actions",
            "possible_actions": [parsed_output]
        }

        return json.dumps(dataToSendBack)
        
class UnityTempFactRequest(UnityRequest):
    async def process(self):
        fact = FactFactory.generate_fact_from_json(self.ctx["json_message"])
        temp_file = self.ctx["temp_facts_file"]
        temp_file.write(fact)
        temp_file.flush()
import asyncio
import json
import re

from FactFactory import FactFactory

SOLVER_TIMEOUT_SECONDS = 30


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
        raise NotImplementedError


class UnityActionRequest(UnityRequest):
    async def process(self):
        # Keep the shared facts stable until the solver has finished reading them.
        async with self.ctx["facts_lock"]:
            raw_output = await self.query_scasp()
        clean_output = self.parse_output(raw_output)
        socket = self.ctx["socket"]
        await socket.send(self.jsonize_parsed_output(clean_output))

    async def query_scasp(self):
        process = await asyncio.create_subprocess_exec(
            self.ctx["scasp"], "-s1", "mainQuery.pl",
            cwd=self.ctx["server_dir"],
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        try:
            stdout, stderr = await asyncio.wait_for(
                process.communicate(), timeout=SOLVER_TIMEOUT_SECONDS
            )
        except (asyncio.TimeoutError, asyncio.CancelledError) as exc:
            # Reap the child before releasing the facts lock, even on shutdown.
            if process.returncode is None:
                try:
                    process.kill()
                except ProcessLookupError:
                    pass
            await process.communicate()
            if isinstance(exc, asyncio.CancelledError):
                raise
            raise RuntimeError(
                f"scasp timed out after {SOLVER_TIMEOUT_SECONDS} seconds"
            ) from exc
        if process.returncode != 0:
            detail = stderr.decode("utf-8", errors="replace").strip()
            raise RuntimeError(f"scasp failed: {detail}")
        return stdout.decode("utf-8", errors="replace")

    def parse_output(self, raw_output):
        # chosen_action/1 currently returns plain atoms. Ignore solver commentary.
        output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", raw_output)
        match = re.search(
            r"^[ \t]*X[ \t]*=[ \t]*([a-z][A-Za-z0-9_]*)[ \t]*[.;,]?[ \t]*\r?$",
            output,
            re.MULTILINE,
        )
        if match is None:
            raise RuntimeError("scasp returned no recognizable action binding for X")
        return match.group(1)

    def jsonize_parsed_output(self, parsed_output):
        data_to_send_back = {
            "message_type": "possible_actions",
            "possible_actions": [parsed_output]
        }

        return json.dumps(data_to_send_back)


class UnityTempFactRequest(UnityRequest):
    async def process(self):
        fact = FactFactory.generate_fact_from_json(self.ctx["json_message"])
        async with self.ctx["facts_lock"]:
            with open(self.ctx["facts_temp_file"], "a", encoding="utf-8") as temp_file:
                temp_file.write(fact)

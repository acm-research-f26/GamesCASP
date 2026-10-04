import asyncio
import json
import re
import uuid

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
        snapshot_id = uuid.uuid4().hex

        snapshot_file = (
            self.ctx["server_dir"] / f"facts_snapshot_{snapshot_id}.pl"
        )

        query_file = (
            self.ctx["server_dir"] / f"query_{snapshot_id}.pl"
        )

        # Lock ONLY while reading the shared facts.
        async with self.ctx["facts_lock"]:
            facts = self.ctx["facts_temp_file"].read_text(
                encoding="utf-8"
            )

        # From this point forward, this request owns its own
        # immutable snapshot. No global lock is needed.
        snapshot_file.write_text(
            facts,
            encoding="utf-8"
        )

        query_file.write_text(
            f"#include('{snapshot_file.name}').\n"
            f"#include('rules.pl').\n\n"
            f"?- chosen_action(X).\n",
            encoding="utf-8"
        )

        try:
            raw_output = await self.query_scasp(query_file)

            clean_output = self.parse_output(raw_output)

            response = self.jsonize_parsed_output(clean_output)

            # Multiple concurrent requests may finish together,
            # so serialize writes to the WebSocket only.
            async with self.ctx["send_lock"]:
                await self.ctx["socket"].send(response)

        finally:
            snapshot_file.unlink(missing_ok=True)
            query_file.unlink(missing_ok=True)

    async def query_scasp(self, query_file):
        process = await asyncio.create_subprocess_exec(
            self.ctx["scasp"],
            "-s1",
            query_file.name,
            cwd=self.ctx["server_dir"],
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )

        try:
            stdout, stderr = await asyncio.wait_for(
                process.communicate(),
                timeout=SOLVER_TIMEOUT_SECONDS
            )

        except (asyncio.TimeoutError, asyncio.CancelledError) as exc:
            if process.returncode is None:
                try:
                    process.kill()
                except ProcessLookupError:
                    pass

            await process.communicate()

            if isinstance(exc, asyncio.CancelledError):
                raise

            raise RuntimeError(
                f"scasp timed out after "
                f"{SOLVER_TIMEOUT_SECONDS} seconds"
            ) from exc

        if process.returncode != 0:
            detail = stderr.decode(
                "utf-8",
                errors="replace"
            ).strip()

            raise RuntimeError(
                f"scasp failed: {detail}"
            )

        return stdout.decode(
            "utf-8",
            errors="replace"
        )

    def parse_output(self, raw_output):
        output = re.sub(
            r"\x1b\[[0-?]*[ -/]*[@-~]",
            "",
            raw_output
        )

        match = re.search(
            r"^[ \t]*X[ \t]*=[ \t]*"
            r"([a-z][A-Za-z0-9_]*)"
            r"[ \t]*[.;,]?[ \t]*\r?$",
            output,
            re.MULTILINE,
        )

        if match is None:
            raise RuntimeError(
                "scasp returned no recognizable "
                "action binding for X"
            )

        return match.group(1)

    def jsonize_parsed_output(self, parsed_output):
        data_to_send_back = {
            "message_type": "possible_actions",
            "possible_actions": [parsed_output]
        }

        return json.dumps(data_to_send_back)


class UnityTempFactRequest(UnityRequest):
    async def process(self):
        fact = FactFactory.generate_fact_from_json(
            self.ctx["json_message"]
        )

        # Shared facts are mutated only while holding the lock.
        async with self.ctx["facts_lock"]:
            with open(
                self.ctx["facts_temp_file"],
                "a",
                encoding="utf-8"
            ) as temp_file:
                temp_file.write(fact)
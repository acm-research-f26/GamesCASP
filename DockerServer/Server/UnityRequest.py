import asyncio
import json
import logging
import re
import uuid

from FactFactory import FactFactory
from StoredFacts import StoredFact, require_atom, update_stored_facts

SOLVER_TIMEOUT_SECONDS = 30
logger = logging.getLogger(__name__)


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




class UnityNPCDecisionRequest(UnityActionRequest):
    async def process(self):
        message = self.ctx["json_message"]
        actor = message.get("actor")
        state = message.get("state")

        if (
            not isinstance(actor, str)
            or re.fullmatch(r"[a-z][A-Za-z0-9_]*", actor) is None
        ):
            raise ValueError("actor must be a valid Prolog atom")

        if not isinstance(state, dict):
            raise ValueError("state must be a JSON object")

        try:
            facts = FactFactory.generate_npc_facts(actor, state)
        except (KeyError, TypeError) as exc:
            raise ValueError(
                f"Invalid NPC state: {exc}"
            ) from exc

        snapshot_id = uuid.uuid4().hex

        snapshot_file = (
            self.ctx["server_dir"] / f"npc_facts_{snapshot_id}.pl"
        )

        query_file = (
            self.ctx["server_dir"] / f"npc_query_{snapshot_id}.pl"
        )

        try:
            snapshot_file.write_text(facts, encoding="utf-8")

            query_file.write_text(
                f"#include('{snapshot_file.name}').\n"
                f"#include('innocent_behavior.pl').\n\n"
                f"?- action(X, {actor}).\n",
                encoding="utf-8"
            )

            raw_output = await self.query_scasp(query_file)

            output = re.sub(
                r"\x1b\[[0-?]*[ -/]*[@-~]",
                "",
                raw_output
            )

            match = re.search(
                r"^[ \t]*X[ \t]*=[ \t]*"
                r"([a-z][A-Za-z0-9_]*)"
                r"(?:\(([a-z][A-Za-z0-9_]*)\))?"
                r"[ \t]*[.;,]?[ \t]*\r?$",
                output,
                re.MULTILINE
            )

            if match is None:
                raise RuntimeError(
                    "s(CASP) returned no recognizable NPC action"
                )

            response = json.dumps({
                "message_type": "npc_action",
                "actor": actor,
                "action": match.group(1),
                "target": match.group(2) or ""
            })

            async with self.ctx["send_lock"]:
                await self.ctx["socket"].send(response)

        finally:
            snapshot_file.unlink(missing_ok=True)
            query_file.unlink(missing_ok=True)


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


class UnityStoredFactsRequest(UnityRequest):
    async def process(self):
        message = self.ctx["json_message"]
        actor = require_atom(message.get("actor"), "actor")
        request_id = message.get("request_id")
        if not isinstance(request_id, str) or not request_id or len(request_id) > 128:
            raise ValueError("request_id must be a nonempty string of at most 128 characters")
        payloads = message.get("facts")
        if not isinstance(payloads, list) or not payloads or len(payloads) > 256:
            raise ValueError("facts must contain between 1 and 256 facts")

        # Validate the whole batch before touching the file.
        facts = [StoredFact.from_payload(actor, payload) for payload in payloads]
        async with self.ctx["facts_lock"]:
            update_stored_facts(self.ctx["facts_temp_file"], facts)

        for fact in facts:
            logger.info("Stored fact: %s", fact.to_prolog().strip())

        async with self.ctx["send_lock"]:
            await self.ctx["socket"].send(json.dumps({
                "message_type": "facts_stored",
                "request_id": request_id,
                "actor": actor,
            }))

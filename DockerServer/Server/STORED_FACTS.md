# Stored fact protocol (issue #30)

Unity sends stored knowledge through the existing WebSocket, separately from NPC
decision snapshots and the older `fact` event messages:

```json
{
  "request_type": "store_facts",
  "request_id": "a-unique-request-id",
  "actor": "player1",
  "facts": [{
    "type": "LeverActivated",
    "lifetime": "Permanent",
    "subject_id": "lever1",
    "target_id": "",
    "has_distance": false,
    "distance": 0
  }]
}
```

- Type and lifetime names match the C# enums. Transient lifetime is rejected;
  transient observations continue through `npc_decision`.
- Actor, subject, and nonempty target IDs must be plain Prolog atoms, matching
  `[a-z][A-Za-z0-9_]*`. Actor is the observer, not necessarily the subject.
- An absent/null/empty target is stored as `none`. `has_distance` distinguishes an
  absent distance from zero because Unity's JsonUtility cannot serialize nullable
  floats. A present distance must be finite and nonnegative.
- A batch contains 1–256 facts. The entire batch is validated before writing.

Python writes canonical clauses into the session's `facts_temp.pl`:

```prolog
stored_fact(player1, permanent, lever_activated, lever1, none, none).
stored_fact(player1, mutable, agent_attacked, survivor1, zombie1, none).
```

Permanent facts deduplicate by the complete clause. Mutable facts replace the
previous mutable clause keyed by `(actor, type, subject_id)`; target and distance
are the values that can change. A mutable fact currently represents the latest
observation of this type for this subject, not an event history. Different actors'
knowledge remains separate. Permanent and mutable clauses do not replace each other.

Writes use the existing session lock and replace the file after writing a complete
temporary copy, preserving legacy facts. Only then does Python reply:

```json
{"message_type":"facts_stored","request_id":"a-unique-request-id","actor":"player1"}
```

Validation/storage errors use the existing `error` response with `request_id` added
for correlation. Unity removes facts from its pending queue only after a matching
acknowledgment. Retrying an unacknowledged write is safe under these storage rules.
The queue is in memory and does not survive stopping Play Mode. The current client
does not reconnect automatically after a connection failure.

Storage lasts for this server session, as before: startup restores `facts_temp.pl`
from `facts.pl`. These clauses preserve agent knowledge; this branch does not add
rules that consume it or merge it into NPC decision snapshots.

## Verification

Run the server checks from the repository root:

```powershell
python -B -m unittest discover -s DockerServer/Server/tests -v
```

In Unity, stop Play Mode and choose **Tools > Facts > Verify Python Storage**.
Python must be on PATH with `websockets` installed (see `requirements.txt`). To use
a particular interpreter, set `GAMESCASP_PYTHON` to its executable path before
starting Unity. Docker and s(CASP) are not required for this storage-only check.

The tool starts `tests/stored_fact_server.py` on a free loopback port using the
production Python request handler. It instantiates the actual gameplay NPC and
lever prefabs, invokes lever activation through the real trigger handler, and
verifies the resulting file in `ScaspUnityGame/Temp`. It checks prefab wiring,
pre-connection queuing, acknowledgments, a dropped acknowledgment and safe retry,
permanent deduplication, ordered mutable replacement, observer isolation, optional
distance (including zero), preservation of existing facts, and rejected input.
The process, objects, and temporary directory are removed when it finishes.

For batch execution, use the project's Unity version with these arguments:

```text
-batchmode -nographics -projectPath <ScaspUnityGame path> -executeMethod StoredFactVerification.Run -logFile <log path>
```

Omit `-quit`: this asynchronous verifier exits Unity itself after cleanup, returning
0 on success or 1 on failure. The console/log reports `PYTHON FACT STORAGE VERIFIED`.
The existing **Verify Draft Pipeline** and **Verify Transient Queries** menu tools
remain available for their respective regression checks.

`AgentFactReceiver.FactReceived` still means the strategy accepted a fact locally.
For `ScaspStrategy`, `PendingFactCount` includes queued/in-flight writes and reaches
zero after Python acknowledges them. Use a unique `actorId` per NPC, as the current
`PlayerManager` already does. The gameplay NPC prefab includes `AgentFactReceiver`
and explicitly selects its `ScaspStrategy` and single `ScaspClient`. `MapManager`
assigns each spawned lever's name (such as `lever_1`) to its emitter as well, so
event facts and NPC snapshots identify the same lever.

Validated on October 26, 2026: all 12 Python tests passed, and Unity 6000.6.0f1
completed the Python storage verifier (12 checks), existing fact-pipeline verifier
(31 checks), and transient-query verifier (11 checks), each with exit code 0.
This validates storage and snapshot regressions, not new reasoning over stored facts.

## Watching a gameplay session

After restarting the Python server to load changes, enter Play Mode, start the game,
and activate a lever with an NPC within its emitter's observation radius. Watch:

```powershell
docker compose -f DockerServer/docker-compose.yml logs -f gamescasp
```

Python logs `Stored fact: stored_fact(player1, permanent, lever_activated, lever_1, none, none).`
only after writing the file. Unity logs `Python stored LeverActivated for player1: lever_1`
after receiving the acknowledgment. Replays may produce repeated success log lines;
the permanent fact still appears only once in the file.

To inspect the actual stored facts from the repository root:

```powershell
Select-String -Path DockerServer/Server/facts_temp.pl -Pattern '^stored_fact\('
```

The file is reset when the server starts, so inspect it before restarting the server.
`Survivor1` still uses the local-only sample strategy; the spawned `playerPrefab`
NPCs use Python. Zombie attacks currently log messages but do not emit `AgentAttacked`
facts; lever activation is the gameplay event source for this storage path.

### Inspecting mutable and transient delivery in Play Mode

Select a spawned `playerPrefab(Clone)` NPC in the Hierarchy. Use **Tools > Facts >
Send Mutable Test Fact (Play Mode)** to send a clearly named `mutable_storage_probe`
through its receiver and normal storage path. Python's `Stored fact:` log and
`facts_temp.pl` show a mutable `agent_attacked` clause. Run the menu action again:
the target alternates between `test_attacker_1` and `test_attacker_2`, while only
one mutable clause remains for this observer/type/subject. These are deliberate
test facts and remain until replaced or the server session resets.

For transient observations, use **Tools > Facts > Request NPC Snapshot (Play Mode)**
with that NPC selected. This requests a normal decision and executes the returned
action. Python logs `NPC snapshot received for player1: zombies=[...]` after
validating the incoming snapshot. A nonempty list proves the observed zombie IDs
and distances arrived at Python; `[]` means that request had no zombie observations.
The snapshot is temporary and does not produce transient entries in `facts_temp.pl`.
Place an active zombie within the NPC's detection range to inspect a nonempty
snapshot, and use a valid lowercase-leading entity name such as `zombie1`.

Restart the Python server to load new logging code before this check. Restarting
also resets the stored facts, so do it before sending the mutable test facts.

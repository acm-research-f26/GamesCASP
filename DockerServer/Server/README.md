# Python server review

Run the following commands from the repository root.

## What changed

- `UnityRequestFactory.py`: dispatches on the actual `request_type` value; unsupported or missing types produce a validation error.
- `SocketHandler.py`: validates each incoming JSON message and reports errors without ending the connection. Initialization runs only when launched as a script. Duplicate helpers and the unused writable handle to `rules.pl` were removed.
- `UnityRequest.py`: awaits an async solver subprocess with a 30-second timeout, kills and reaps it on timeout or cancellation, and runs it in the server directory regardless of the launch directory. The parser extracts a plain-atom `X` binding, discards trailing commentary, and reports missing/unrecognized bindings as processing errors.
- `FactFactory.py`: rejects missing/unknown events and restricts `culprit` to `[a-z][A-Za-z0-9_]*`. This prevents values from introducing additional Prolog clauses or variables.

The server represents **one shared game session per process**. Startup copies `facts.pl` to `facts_temp.pl`; connecting or reconnecting does not reset facts. A shared async lock serializes fact writes and solver queries so the solver sees stable input. Other connections and validation responses remain responsive, but fact writes and further queries wait for the active query. Restart the server to begin a fresh session. Run only one server process against this directory; the lock is not a cross-process lock.

Successful fact requests still have no acknowledgment. Action requests still return `{"message_type":"possible_actions","possible_actions":["action_name"]}` and retain the existing `-s1` solver option. This change does not enumerate every possible action. Unity should also handle `message_type: "error"`, with `error` equal to `invalid_request` or `processing_failed`, and a readable `detail`.

## Automated checks

With the server's `requirements.txt` dependencies installed:

```powershell
python -B -m unittest discover -s DockerServer/Server/tests -v
git diff --check
```

The tests use temporary facts, local WebSocket connections, and real Python child processes as controlled substitutes for s(CASP). They cover dispatch, supported events, invalid input, Prolog injection attempts, parsing, recovery after errors, reconnect preservation, locking, responsiveness, timeout cleanup, cancellation cleanup, and working-directory selection. They do not validate the installed s(CASP) output format or the Prolog rules.

## Manual review

1. Read the Python changes with `git diff -- DockerServer/Server`. Follow a request through `handler`, `make_request`, and the selected request's `process`. Review the new tests in `tests/test_requests.py` separately because untracked files do not appear in `git diff`.

2. Start Docker Desktop, then launch the server. This starts a fresh session and resets `facts_temp.pl` from `facts.pl`:

   ```powershell
   docker compose -f DockerServer/docker-compose.yml run --build --rm --service-ports gamescasp python3 SocketHandler.py
   ```

3. In a second terminal, connect using the installed `websockets` command-line client:

   ```powershell
   python -B -m websockets ws://localhost:6767
   ```

4. Enter these messages one line at a time:

   ```json
   {"request_type":"get_action"}
   {"request_type":"fact","message_type":"heard_noise"}
   {"request_type":"get_action"}
   ```

   Action requests should return a plain action name in `possible_actions`, with no timing/model text. The fact request has no response. Inspect `DockerServer/Server/facts_temp.pl` for `noise(unknown).`. If you get a processing error, read its detail and the server traceback; the real solver and its output format still need to pass this check.

5. Test invalid input, then send a valid action request on the same connection:

   ```text
   not json
   []
   {"request_type":"unknown"}
   {"request_type":"fact"}
   {"request_type":"fact","message_type":"vase_broken","culprit":"player). alarm_raised. %"}
   {"request_type":"get_action"}
   ```

   The first five messages should each return `invalid_request`. The final request should still work. The rejected fact must not appear in `facts_temp.pl`.

6. Open another client with the same command, or disconnect and reconnect the first client. Request an action and verify the noise fact remains. Send a valid fact from either client and verify both connections share the same file. Stop and restart the server to verify the file resets to `facts.pl` only at startup.

7. Use the automated timeout/cancellation and slow-query tests to review failure cleanup and responsiveness without intentionally hanging the real solver. Read `test_real_child_process_success_failure_timeout_and_cancellation` and `test_slow_query_keeps_server_responsive_and_facts_stable` for the exact assertions.

The subprocess lifecycle follows the Python [asyncio subprocess documentation](https://docs.python.org/3/library/asyncio-subprocess.html).

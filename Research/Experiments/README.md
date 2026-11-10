# Automatic game batches — issue #55, part one

This branch implements the orchestration/export half of
[issue #55](https://github.com/acm-research-f26/GamesCASP/issues/55).
It automatically runs a chosen number of games, each with two configuration
runs, then writes per-run metrics and aggregate CSVs. Real measurement and
role-based backend switching are intentionally left for part two.

## Run in Unity

1. Open **Tools > Experiments > Run Games**.
2. Set **Games (two runs each)**, NPC count, layout seed, time limit, and backend.
   The default is 100 games / 200 runs. BehaviorTree requires no Python server.
   For Scasp, start the existing server first (`./Start-Playtest.ps1 -NoUnity`).
3. Click **Run experiments**. The window enters Play Mode if necessary.
4. Use **Cancel batch** to stop. Completed runs are retained; an unfinished run
   is not counted or exported. Normal Stop Play Mode also saves completed runs.

The runner reloads `Main` before every run, automatically invokes the same
startup sequence as the Start button, and advances on:

- **Won**: all required levers are activated.
- **NoSurvivors**: no active, living, non-transformed NPC remains. An expired
  half-zombie bite window counts as transformed, as in the existing decision loop.
- **TimedOut**: the configured real-time limit expires (default 120 seconds).

After the final run it reloads the start screen and restores the previous time
scale. Each run uses time scale 1. Invalid setup or export failures stop the batch
with an error rather than silently fabricating a result.

Each pair uses seed `startingSeed + gameNumber - 1`. NPCs, window positions and
lever positions are recreated with the same seed for both configurations. The
current map spawns zombies from windows during gameplay; there are no separately
configured initial zombie positions. Equal starting layouts do not promise
identical gameplay timing or outcomes.

Both configuration slots currently use the backend selected in the window.
The issue repeats the same role mapping for configurations 1 and 2; this branch
does not resolve that typo or implement the deferred strategy swap. CSVs explicitly
mark `role_mapping=pending`.

**Part two update:** the strategy swap is now implemented, so the paragraph above
describes part one only. Configuration 1 runs innocents on s(CASP) and half-zombies
on the Behavior Tree, configuration 2 swaps them, and the window's Backend choice is
not used for experiment runs. Both configurations therefore need the Python server
running, even though step 2 above says BehaviorTree does not. `role_mapping` now says
`innocent:scasp/half_zombie:behavior_tree` or `innocent:behavior_tree/half_zombie:scasp`
(`both_configurations` on the summary `all` row). See "Part two measurements" below.

Each run gives every actor a batch/game/configuration-specific Prolog identity.
Python selects memories by actor, so old games cannot leak stored knowledge into
new games, even if a previous request finishes late. Existing server files are
not cleared; old actors' facts remain until the server is restarted. Scene reloads
destroy old clients and reset agents, map objects, lever progress, diagnostics,
and navigation. A batch assumes exclusive use of the Unity gameplay scene.

## Files and calculations

The Console prints the output folder. By default it is
`Application.persistentDataPath/Experiments/batch_<unique ID>/`. A custom output
directory is a parent folder; a unique batch subfolder prevents overwriting data.

- `games.csv`: one flushed row per completed configuration run, including game,
  configuration, seed, selected backend, outcome, real elapsed seconds, and metrics.
- `summary.csv`: configuration 1, configuration 2, and `all`; includes completion
  status, counts, aggregate metrics, and measured-run counts for each category.

The five latency fields are s(CASP) fact storage, s(CASP) action round trip,
s(CASP) actual query, behavior-tree fact storage, and behavior-tree action.
Each input is a **per-run mean in milliseconds**. The final value is the mean of
those per-run means, not a pooled average over individual requests.

For each of the four strategy/role combinations, inputs are `Total` = T(i) and
`Alive` = A(i). Summary win percentages are **100 × sum(A) / sum(T)**, not the
average of per-run percentages. Percentages are on a 0–100 scale.

Metric inputs start as `null`. They appear as blank cells, and `metrics_status`
is `placeholder`; no fake latency or population measurements are collected.
Once populated, a real zero participates in calculations, while missing data
does not. Summaries safely return zero for empty categories or a zero denominator,
with `*_measured_runs=0` distinguishing missing data from measured zeros. Partial
measurements are labeled `partial`; all nine populated categories yield `measured`.
Counts must satisfy `0 <= Alive <= Total`, and latencies must be finite/nonnegative.

The checked-in [example game CSV](examples/games.csv) and
[example summary CSV](examples/summary.csv) were generated by the production
exporter with empty metric objects. `ExampleOnly` / `example_only` labels make
clear they are schema demonstrations, **not gameplay results**.

Rows flush after every game. Summaries also save on cancellation, handled errors,
and normal application/Play Mode shutdown. A forced process kill can leave only
the already flushed game CSV; it cannot execute a final summary callback.

## Measurement integration for part two

`ExperimentRunner.CurrentMetrics` is a fresh `ExperimentMetrics` for every run.
Use the lifecycle events on the runner returned by `ExperimentRunner.Begin(settings)`:

- `RunPreparing(runner, players)`: fresh scene, default backend configured, before
  NPCs spawn. `GameNumber`, `Configuration`, `LayoutSeed`, and `CurrentMetrics`
  are available. Apply future configuration-specific behavior here.
- `RunStarted(runner, players)`: all NPCs and map objects are initialized.
- `RunFinished(runner, outcome, elapsedSeconds)`: final NPCs still exist. Fill the
  per-game averages and population counts before this synchronous callback returns.
  The exporter validates and copies the metrics immediately afterward.
- `BatchFinished(runner)`: CSVs closed and the scene reset.

Example measurement handoff (illustrative values):

```csharp
runner.RunFinished += (run, outcome, elapsed) => {
    run.CurrentMetrics.ScaspActionRoundTripLatencyMs = measuredRoundTripMean;
    run.CurrentMetrics.ScaspInnocents = new ExperimentPopulation(totalEverInRole, aliveAtEnd);
};
```

Do not attach async measurement work to `RunFinished` without introducing an
awaited completion contract: a callback returning before its server results
arrive would export placeholders. Role transitions, ever-in-role membership,
and measuring Python fact-write latency remain part two's responsibility.

## Command line and verification

A built game starts a batch when passed `-experimentGames`. `Main` is included in
Build Settings. Optional arguments are `-experimentNpcs`, `-experimentSeed`,
`-experimentTimeout`, `-experimentOutput`, `-aiBackend`, and `-scaspPolicy`.

For an unattended Editor run (requires an activated Unity license), use:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' `
  -batchmode -nographics -projectPath "$PWD/ScaspUnityGame" `
  -executeMethod ExperimentWindow.RunBatch `
  -experimentGames 100 -experimentNpcs 4 -experimentSeed 55 `
  -experimentTimeout 120 -aiBackend BehaviorTree `
  -experimentOutput "$PWD/ScaspUnityGame/Logs/Experiments" `
  -logFile "$PWD/ScaspUnityGame/Logs/experiments.log"
```

Do **not** supply `-quit`; the runner exits after export. Exit codes are 0 for
completion, 1 for failure, and 2 for cancellation.

From the repository root, `./Verify-Experiments.ps1` runs the pure C# CSV tests
without Unity or Python. `-CompileUnity` additionally compiles all project C#
sources against the installed Unity references (requires the generated `.csproj`
files and Library assemblies). This does not simulate Unity's runtime lifecycle.
`-ExampleOutput <new-directory>` generates a fresh placeholder example pair.

In Unity, **Tools > Experiments > Verify CSV Calculations** runs the same tests.
**Tools > Experiments > Verify Game Loop** runs two games/four configurations in
the real Main scene and checks resets, paired layouts, actor isolation, all three
end conditions, exact CSV row counts, and final teardown. For batch verification,
use `-executeMethod ExperimentLoopVerification.RunBatch` without `-quit` or
`-experimentGames`.

Implementation validation: 68 standalone CSV checks passed and all C# sources
compiled against Unity assemblies. The real scene-loop verifier could not be
executed in this environment because Unity reported no valid Editor license.

## Part two measurements

`ExperimentRecording` fills `CurrentMetrics` from a fresh `ExperimentMeasurements`
recorder every run. Samples are only kept for the run that started them, and a
finished run rejects anything that arrives later. Empty categories stay blank.

- s(CASP) fact: Python's `write_ms`, the fact file update itself after the lock is taken.
  Lock waiting and the network are not included.
- s(CASP) round trip: from just before Unity sends the decision request until Unity
  handles the matching reply (this includes up to one frame of queue time). It is
  reply latency, not executed actions: a reply older than `maxResponseAge` (3 s) is
  counted because it really took that long, then discarded and never executed.
  Errors, timeouts and replies to abandoned requests are never counted.
- s(CASP) query: Python's `solver_ms`, the solver process run only (including starting
  the `scasp` process), without the websocket transport.
- Behavior Tree fact: the strategy's `StoreFact` call inside `TryStoreFact`, which is
  the blackboard update.
- Behavior Tree action: the existing decision-loop timing around `RequestDecision`
  (the same value as `AiDiagnostics.DecisionMs`), which includes handing the chosen
  action to `AiAction`.
- CSV metadata: `backend` is `scasp_and_behavior_tree` for every experiment, `role_mapping`
  says which role used which, and `scasp_policy` is the one policy the whole batch used.
  Experiments default to **Baseline**; a Reasoned batch must be run and kept as its own
  batch, so a summary never mixes policies.
- Memories replayed after an infection switch are never counted as fact latency.
- Innocents survive if alive and never infected. Half-zombies survive if alive with an
  open bite window; an expired window is a loss. This is role survival, not the match winner.

Verifiers: **Verify Measurements** (also in `Verify-Experiments.ps1`), **Verify Strategy
Switching**, and **Verify Live Experiment (needs server)**, which runs a real 2-game batch
against the server started with `./Start-Playtest.ps1 -NoUnity`.

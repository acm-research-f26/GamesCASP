# s(CASP) reasoning in GamesCASP: architecture, experiments and results

GamesCASP compares symbolic reasoning (s(CASP)) with a Behavior Tree (BT) as NPC
controllers in the same game. This document covers how to choose and verify the AI, how
the s(CASP) reasoning works, what keeps the comparison fair, how to run the benchmarks,
the measured results, and what the half-zombie integration will need.

## 1. Choosing the AI and proving it is running

Normal Play Mode used the Behavior Tree all along: `Main.unity` serializes
`PlayerManager.decisionBackend = BehaviorTree`, and `PlayerManager` calls
`agent.SetStrategy(tree)`, which (by design) outranks the prefab's Inspector-assigned
`ScaspStrategy`. Only the automated tests had selected s(CASP).

The backend is chosen in one place, `PlayerManager`, before Start:

| How | Setting |
|---|---|
| Inspector | PlayerManager > Decision Backend, and AI Experiment > Scasp Policy / Explain / Unchanged Skip / Trace Level |
| Start screen | **B** switches Behavior Tree / s(CASP), **P** switches the s(CASP) policy (shown on screen; F1 hides) |
| Command line | `-aiBackend Scasp`, `-scaspPolicy Baseline\|Reasoned`, `-scaspExplain`, `-scaspSkip <s>`, `-aiTrace Off\|Summary\|Verbose` |

Each NPC runs exactly one strategy; the other is disabled. Evidence it is running:

- Console (Summary level): `[AI] AI backend for this run: s(CASP), Reasoned policy, 5 NPCs ...`,
  one `strategy ScaspStrategy ... via ws://localhost:6767` line per NPC, one
  `s(CASP) connected` per NPC (or one error per NPC if the server is down; clients
  keep retrying with backoff), timeouts, stale replies and failures, and a status block
  every 15 seconds with requests, replies, latency percentiles and last action per NPC.
- Verbose level adds one line per request sent, reply received and command executed.
- The on-screen overlay shows connection state, answered/sent counts, latency and the
  last executed action per NPC.
- Every request carries an ID (`player1-17`); Python echoes it, logs
  `NPC decision for player1 (request player1-17): ...`, and Unity executes a reply
  only if it answers the request still pending. Replies older than 3 s are discarded.

The gameplay harness proves control rather than inferring it: for every NPC, the number
of commands `AiAction` executed must equal the number of correlated s(CASP) replies, and
the server's decision log must contain the same request IDs.

## 2. Pipeline

```
AiAction perception (shared)  ->  NpcState snapshot  ->  FactFactory (Python)  ->  s(CASP)
     detection range 5               ScaspStrategy          facts                 action(X, Actor)
                                                                                      |
AiAction execution + navigation (shared)  <-  ScaspStrategy (ID + age check)  <------+
```

Facts per decision: health, lever goal and progress (`levers_to_win` now comes from the
scene's LeverManager, as the BT reads it; the prefab field said 3 while the game needs
2), visible levers (state, distance, nearest zombie), visible zombies, visible
barricades (reinforcement, distance, nearest visible zombie: the same inputs as the BT's
`BarricadeObservationBuffer`; s(CASP) previously received no barricades at all), the
agent's own current action, and `-reachable(Target, Actor)` for targets navigation proved
unreachable.

**Unchanged-skip.** A decision request is skipped when nothing the rules can distinguish
has changed since the last answer (at most `maxUnchangedSkip`, 2 s). The signature
encodes every quantity the rules test: distance bands at each rule threshold (1, 2, 3, 5),
reinforcement bands (0, 25, 40, 95, 100), the orders used by "closer" and "weaker"
comparisons, lever states, health, progress, the current action and unreachable targets.
Two snapshots with the same signature satisfy exactly the same rule conditions, so the
answer cannot differ; the 2 s bound is a safety net. This removes redundant queries
(the earlier 132 repeated `hunt_lever` answers were legitimate re-confirmations of an
unchanged search, issued once per second) without hiding new threats: a zombie entering
the 2-unit band changes the signature immediately.

## 3. The reasoned policy (`reasoned_innocent.pl`)

`innocent_behavior.pl` is unchanged and remains the **baseline** policy for a like-for-like
comparison with the BT. The **reasoned** policy keeps the same interface
(`candidate_action/2`, `defeated/2`, `action/2`) and adds:

Priorities, as a ground precedence table between action kinds:

| Kind | Action | When |
|---|---|---|
| flee | `run_from(Z)` | a visible zombie within 2 units (the nearest one) |
| finish | `pull_lever(L)` | touching a reachable lever |
| defend | `move_to/hold_barricade(B)` | a zombie is at a visible barricade |
| repair | `move_to/hold_barricade(B)` | a nearby (3 units) barricade at or below 40, or one the agent is already holding until 95 |
| pursue | `move_to_lever(L)` | the preferred reachable lever |
| search | `hunt_lever` | levers remain and none is known |
| maintain | `move_to/hold_barricade(B)` | damaged barricades once every lever is pulled |
| idle | `wait` | always, so a living agent always has a decision |

Defaults and exceptions (default negation reads "unless"):

- **Flee by default, unless** the agent is touching the *final* lever and no zombie is
  in contact range (`ab_flee`): one pull wins the game. In contact, fleeing stands.
- **Defend a breached barricade, unless it is overrun** (gone, with a zombie in the
  opening): every barricade goal is then withdrawn, instead of walking into the breach.
- **Pursue an available lever, unless navigation proved it unreachable.** This is strong
  negation: `-reachable(L, A)` is explicit evidence; missing information keeps the default.
- **Commitment:** an agent repairing a barricade keeps repairing until it is restored
  (hysteresis 40 -> 95), so it does not oscillate between the barricade and its levers.
- **Nonmonotonic revision:** adding a fact (a zombie near the closer lever) changes the
  preferred lever; removing the zombie restores the previous choice.

Each example is a test in `tests/test_reasoned_policy.py`, which runs the real solver
and enumerates *every* surviving action (`-s0`) under both policies, so an extra
contradictory action fails the test.

### Notes on Ciao s(CASP) negation

Two formulations that look natural produced unsound or failing proofs here, and the
policy avoids both:

1. Comparing numeric ranks under negation (`OtherRank #> Rank` inside `not outranked`)
   quantifies over a CLP-constrained variable; the dual was not provable.
2. `Other \= Action` with compound action terms, and quantifying over action terms of
   different shapes (`run_from(Z)`, `hunt_lever`), likewise failed.

Priorities therefore use ground kinds: `outranked(A) :- candidate(A, Kind), above(H, Kind), active(H)`.
`active/2` is a cheap, preference-free condition (for example "some reachable lever is
in reach"), which also cut solving time about tenfold (from ~750-1000 ms to ~85 ms on a
complex snapshot; the process start dominates the remaining ~200 ms).

## 4. Explanations

With explanations on, Python runs the solver with `--tree --mid --pos` and returns its
justification tree for the chosen action, then asks the solver which candidates are
`defeated` (`?- candidate_action(A, Actor), defeated(A, Actor).`). Both come from the
engine; nothing is reconstructed afterwards. Example (zombie at 1.5, lever at 3):

```
action(run_from(zombie_a),player1) :-
    candidate(run_from(zombie_a),player1,flee) :-
        can_act(player1) :- health(3,player1).
        nearest_threat(zombie_a,player1) :-
            threat(zombie_a,player1) :- zombie(zombie_a), distance(zombie_a,1.5,player1).
Defeated alternatives: move_to_lever(lever_1), wait
```

Explanation time is reported separately (`explain_ms`) so benchmarks can separate normal
execution from explanation overhead.

## 5. Fairness

Shared by both architectures: perception (`AiAction.FetchTransientFacts` plus the same
barricade inputs), movement speed, navigation (one grid, one path follower), action
executor, decision interval (1 s game time), spawns, levers, barricades and zombies.

Known differences, by design and recorded in every result:

- s(CASP) decisions are asynchronous (one pending request per NPC); BT decisions are
  synchronous and in-process.
- The BT's final-lever branch ignores danger entirely; the reasoned policy makes the same
  exception only while no zombie is in contact. The baseline policy has no such exception.
- The reasoned policy repairs failing barricades proactively and maintains them after the
  win; the BT and the baseline policy defend only when a zombie is already at the barricade.
- The reasoned policy uses the agent's current action (commitment) and navigation's
  unreachability evidence; the BT has no equivalent inputs.

Compare BT vs **baseline** s(CASP) for architecture differences under the same policy;
compare baseline vs **reasoned** s(CASP) for what the reasoning adds.

## 6. Navigation and windows

`MansionNavigation` builds a grid from the generated walls. NPCs may only stand in rooms,
corridors and doorways, never in a barricade opening; each opening also gets a solid
`Window Blocker` collider. Zombies use the same grid but may pass through openings, so
they still enter through windows, and no longer walk through walls. Fleeing picks the
reachable point farthest from the zombie; searching explores less-visited regions.
Previously, fleeing aimed 5 units straight away from the zombie, which could lead through
a window opening, and movement ignored walls entirely.

## 7. Running the benchmarks

```powershell
docker compose -f DockerServer/docker-compose.yml up -d   # s(CASP) server on 6767
Unity.exe -batchmode -projectPath ScaspUnityGame -executeMethod MansionGameplayVerification.RunBatch `
  -mansionBackend Scasp -mansionPolicy Reasoned -mansionNpcs 5 -mansionSeed 7 -mansionTimeScale 1 `
  -mansionScenario full -mansionCsv results.csv -mansionSamples samples.csv -logFile run.log
python Research/summarize_benchmarks.py results.csv samples.csv
```

Scenarios: `full` (the real game), `threat` (zombie between agent and lever), `barricade`
(failing barricade next to the agent, levers far away), `final_lever` (one pull from
winning, zombie closing). Every run also re-checks layout, spawns, reachability,
escapes, zombies in walls and (for s(CASP)) proof of control.

Policy-level tests without Unity:

```powershell
docker run --rm -v "${PWD}/DockerServer/Server:/app:ro" -w /app dockerserver-gamescasp python3 -B -m unittest discover -s tests -v
```

## 8. Results

RESULTS_PLACEHOLDER

## 9. Half-zombie integration (separate step)

The half-zombie, witnessed-attack and persistent-memory work on `feat/halfzombie-aron` is
not in this branch. Integration points:

- **Rules:** include `half_zombie_rules.pl` from `reasoned_behavior.pl`. Its
  `candidate_action/defeated` clauses keep working; to take part in priorities, timed
  half-zombie actions need kinds via `candidate/3` (for example a `bite` kind above `flee`
  in the critical stage) and an `active/2` clause, and the critical-stage whitelist can
  become a defeater of every kind below it. Witness memories (`stored_fact/6`) then feed
  `suspected_infected`, which adds `threat`-like danger for remembered suspects.
- **Python:** that branch loads the actor's stored facts into each decision snapshot;
  keep this branch's `policy`, `explain`, `request_id` and timing fields alongside. Error
  replies: this branch correlates by `request_id` (`ScaspClient.NpcDecisionError`); that
  branch added `actor`/`request_type` fields and `NpcDecisionFailed`. Keep one mechanism.
- **Unity:** both branches change `ScaspStrategy`, `ScaspClient`, `AiAction`, `NpcState`,
  `Fact.cs`/`FactFactory.py` and `PlayerManager` in different places; the
  `nearest_zombie_distance = 9999f` change is identical. The unchanged-skip signature
  must add the half-zombie fields (own status and timer band, visible humans and their
  distance bands), or it would skip decisions the timer should change.
- **Memory:** this branch adds no memory store of its own; persistent knowledge comes from
  the half-zombie branch's `stored_fact` pipeline.

## 10. Limitations and future work

- Each decision starts a solver process (~150-200 ms, mostly start-up). A persistent
  solver or batching several agents per process would raise throughput.
- Perception is range-only (no line of sight), as before, for both architectures.
- Barricade repair rates, zombie spawn intervals and the 1 s decision interval are
  unchanged game balance; survival is short in long games for both architectures.
- Unity measurements in batch mode use a fixed seed per run; more seeds would tighten
  the distributions.

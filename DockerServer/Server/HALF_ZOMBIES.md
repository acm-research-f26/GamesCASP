# Half-zombie reasoning draft (issue #43)

`half_zombie_behavior.pl` extends `innocent_behavior.pl` and includes
`half_zombie_rules.pl`. Load the behavior entrypoint once, alongside a fact
snapshot, and query `action(Action, Actor)` as the existing Python NPC path does.
The live Python handler now loads `half_zombie_timed_behavior.pl` for every NPC;
see [Live integration](#live-integration) for the Unity and stored-memory contract.

## Original draft: starting model

An innocent can also have the `half_zombie` status. This retains the existing
lever/barricade behavior whenever there is no visible eligible human to hunt.
A half zombie with no innocent role waits when there is no target or danger.

| Input | Meaning |
| --- | --- |
| `innocent(Actor)` | Existing human role and access to human objectives |
| `half_zombie(Actor)` | Confirmed status, supplied directly or derived from infection |
| `bit_human(Attacker, Victim)` | Successful infectious bite; infects the second argument |
| `witnessed_bite(Observer, Attacker, Victim)` | Evidence belonging to one observer |
| `-half_zombie(Person)` | Explicit proof the person is not a half zombie |
| `human_visible(Person, Observer)` | Current visibility of another person |
| `health(Value, Person)` | Current health; targets must be alive |
| `distance(Person, Value, Observer)` | Current distance, matching the live factory |

`infected(Victim)` follows from `bit_human(_, Victim)` and implies
`half_zombie(Victim)`. Witnessing a bite only produces
`suspected_infected(Observer, Person)` for its participants; it does not establish
actual infection or share evidence with other observers.

The original `not -half_zombie(Person)` idea remains as an exception check **after**
requiring bite evidence. Missing proof of humanity alone does not make strangers
suspicious. An explicit negative suppresses suspicion but does not erase a real
bite. As s(CASP) checks consistency, supplying both confirmed half-zombie status
and its explicit negative is contradictory. The negative fact is currently global;
if different observers can hold different clearance evidence, give that evidence
an observer argument in a follow-up.

## Original draft: policy choices

1. A successful infectious bite immediately creates a half zombie. There is no
   incubation timer or cure yet; an attempted attack must not emit `bit_human`.
2. Nearby full zombies remain immediate danger to all living actors (distance
   at most 2), preserving the existing flee behavior.
3. Uninfected innocents also flee a visible suspected person within distance 2.
   Distant suspicion is remembered but does not interrupt objectives. Confirmed
   world status alone does not reveal infection to every innocent.
4. Half zombies choose the nearest visible, living innocent who is neither a
   half zombie nor a full zombie, excluding themselves. Equidistant targets are
   valid alternatives; the existing `-s1` server option chooses one answer.
5. At distance at most 1, choose `attack_human(Target)`; farther away, choose
   `move_to_human(Target)`. These lose to fleeing and defeat human objectives,
   including waiting. When no eligible human is visible, infected innocents
   resume their existing human objectives.

The new action names fit the existing parser's `action(target)` form. Unity now
executes them, `FactFactory` supplies half-zombie snapshots, and stored witness
memories are translated into this evidence; see [Live integration](#live-integration).

## Try it

With the existing `gamescasp` container running, from the repository root:

```powershell
docker exec gamescasp scasp -s1 /app/tests/half_zombie_example.pl
docker exec gamescasp python3 /app/tests/verify_half_zombie.py
```

The example should bind `X = attack_human(survivor1)`. Try changing its distance
from 1 to 3 to get `move_to_human(survivor1)`, or add `zombie(z).` and
`distance(z, 2, player1).` to make fleeing win. The scenario runner invokes the
real solver on isolated temporary snapshots and leaves the live session untouched.
It checks positive decisions, rejected competing actions, observer isolation,
infection direction, range boundaries, targeting, fallback objectives, and death.

## Second draft: timed survival and the critical defeat exception

The original draft is committed as `b4faf2b`. The separate entrypoint
`half_zombie_timed_behavior.pl` adds the survival timer. Load it **instead of**
`half_zombie_behavior.pl`; including both would retain the original unconditional
danger defeaters and block attacking even during the critical stage.

Supply a snapshot with one current game-time value and one current window start
for each half zombie, all in seconds:

```prolog
current_time(115).
bite_window_started_at(100, player1).
```

| Predicate | Meaning |
| --- | --- |
| `bite_window_seconds(30)` | Fixed length of each bite window |
| `critical_bite_seconds(15)` | Critical threshold in seconds remaining |
| `bite_time_remaining(Remaining, Actor)` | Window start + 30 - current time |
| `bite_window_active(Actor)` | Current time is within the window, before expiry |
| `critical_bite_stage(Actor)` | More than 0 and at most 15 seconds remain |
| `bite_window_expired(Actor)` | Zero or fewer seconds remain |
| `transformed_zombie(Actor)` | Expiry has caused transformation |
| `eliminated(Actor)` | Actor has left the round; every action is defeated |
| `active_half_zombie(Actor)` | Window is active and actor is not eliminated |

Above 15 seconds remaining, half zombies with the innocent role behave just like
innocents: human visibility does not interrupt lever/barricade objectives, waiting,
or fleeing. Neither attacking nor approaching humans is a candidate yet. Keep
`innocent(Actor)` alongside `half_zombie(Actor)` to inherit those objectives;
standalone half zombies without the innocent role retain an idle fallback.

During the final 15 seconds, a whitelist permits only `attack_human(Target)` and
`move_to_human(Target)`; all other candidate actions, including fleeing, waiting,
lever tasks, and barricade tasks, are defeated. Attack/approach candidates require
`critical_bite_stage(Actor)`. No danger defeater blocks them during that stage.
After a valid bite refreshes the timer, they stop being candidates and innocent
behavior resumes until the next critical stage.

At exactly the deadline (start + 30), the actor transforms, is classified as a
`zombie`, and is eliminated. The `half_zombie` input remains a history/status
marker; use `active_half_zombie` to ask whether the actor is still playable.
Eliminated actors cannot perform even inherited innocent actions. A missing
clock/start or future window start also blocks half-zombie actions, without
claiming that transformation is proven.

**Window renewal assumption:** each successful bite before expiry starts another
30-second window. The game/server must replace the actor's window-start snapshot
with that bite's timestamp; attempted attacks and historical `bit_human/2` facts
do not reset the clock. A bite at or after expiry is too late. This pure reasoning
draft does not run a clock, mutate timestamps, or remove Unity objects. The caller
must check expiry before accepting a renewal, retain `eliminated(Actor)` once
eliminated, and remove the actor from round participation. A retained elimination
fact blocks actions even if a later snapshot accidentally supplies a new start.

If no eligible human is visible during the critical stage, there is **no available
action**. This is deliberate: the draft does not invent an unseen target or
silently fall back to human objectives. A later search behavior needs an explicit
policy decision and an additional permitted action.

```powershell
docker exec gamescasp scasp -s1 /app/tests/half_zombie_timed_example.pl
docker exec gamescasp python3 -B /app/tests/verify_half_zombie_timed.py
```

The example returns `attack_human(survivor1)` despite a nearby zombie. The timer
runner checks 26 scenarios against the real solver, enumerating **all** surviving
actions, including exact/fractional thresholds, expiry, renewal snapshots, missing
timers, no visible target, elimination, and innocent behavior around visible humans
before the critical stage and after renewal. The first draft's 23 checks remain
available through `verify_half_zombie.py`.

## Live integration

### One 30-second window everywhere

The design brief mentions about 60 seconds; the tested rules use 30, so 30 was kept.
`bite_window_seconds(30)` and `critical_bite_seconds(15)` in
`half_zombie_timed_behavior.pl` are the source of truth. Unity mirrors the window as
`Agent.BiteWindowSeconds` and sends it in every snapshot; Python rejects a half-zombie
snapshot whose window disagrees (`invalid_request`, shown in Unity's console) rather
than reasoning with a conflicting timer. To change the window, edit both; the unit
tests compare them. Both timestamps use Unity's `Time.time`, which pauses with
`Time.timeScale`.

### Snapshot contract

| Unity `NpcState` | Prolog | Notes |
| --- | --- | --- |
| `half_zombie` | `half_zombie(Actor).` | The deciding actor's own status only |
| `current_time` | `current_time(Now).` | Sent only for half zombies |
| `bite_window_started_at` | `bite_window_started_at(Start, Actor).` | Infection or latest confirmed bite |
| `bite_window_seconds` | (validated, not emitted) | Must equal `bite_window_seconds/1` |
| `humans[]`: `id`, `health`, `visible`, `distance` | `innocent(H).` `health(Hp, H).` `distance(H, D, Actor).` `human_visible(H, Actor).` | Other living agents in detection range |

`NpcHumanState` has no infection field, so another agent's half-zombie status cannot
reach a snapshot. Every human therefore looks like an eligible target to a biter, and
others learn of an infection only through witnessed events. Human IDs are
`Agent.AgentId` (`player1`, `player2`, ...), the same IDs used as each NPC's actor.

### Actions

| s(CASP) answer | Unity | Behavior |
| --- | --- | --- |
| `move_to_human(T)` | `AiAction.MoveToHuman` | Approach `T` to `interactionRange` |
| `attack_human(T)` | `AiAction.AttackHuman` | Pursue `T`; once in range, raise `AiAction.BiteRequested(T)` at most once per `biteAttemptCooldown` (1 s game time) |
| `run_from(T)` | `AiAction.RunFrom` | Also resolves a suspected human, not only zombies |
| no models | (none) | Python sends an empty action; Unity keeps current movement, like the tree's `NoEligibleAction` |

`attack_human` refuses agents that are not active half zombies, self-targets, and dead
or disabled victims. `BiteRequested` means an attempt was made in range, never that
it succeeded.

### Witnessed events and stored memories

Python adds the deciding actor's own `stored_fact/6` clauses (and no one else's) to
each decision snapshot. `half_zombie_rules.pl` turns them into evidence:

| Unity `FactType` (Permanent) | Stored type, Subject → Target | Prolog evidence | Suspects |
| --- | --- | --- | --- |
| `ZombieAttackWitnessed` | `zombie_attack_witnessed`, zombie → victim | `witnessed_zombie_attack/3` | Victim |
| `BiteAttemptWitnessed` | `bite_attempt_witnessed`, biter → victim | `witnessed_bite_attempt/3` | Biter only |
| `BiteWitnessed` | `bite_witnessed`, biter → victim | `witnessed_bite/3` (existing) | Biter and victim |

Python accepts these types only as permanent facts with a victim. The victim of a
zombie hit also receives the existing mutable `AgentAttacked` fact (latest attacker).
`AttackWitnessReporter` delivers memories through each observer's `AgentFactReceiver`:
living agents within their own `detectionRange` of both attacker and victim, plus the
victim; never the biter. Perception is range-only, like transient observations and
`LeverFactEmitter`. Each memory reaches each observer once, however often it is reported.

### Handoff to the gameplay mechanic

The AI never applies infection, decides bite success, or transforms agents. The
mechanic connects through these calls:

1. **Infection from a zombie hit:** call `victim.MarkHalfZombie()` on a landed hit,
   next to `AttackWitnessReporter.ReportZombieAttack` in `Zombie.Update`. It returns
   false and changes nothing for agents already infected or dead, so repeated hits
   do not restart the window.
2. **Bites:** subscribe to each NPC's `AiAction.BiteRequested`. Witnesses have already
   recorded an attempt. Decide the outcome. On success, call `victim.MarkHalfZombie()`,
   `biter.RenewBiteWindow()` (false once expired: too late), and
   `AttackWitnessReporter.ReportConfirmedBite(biter, victim)`. On failure do nothing;
   the AI may try again after the cooldown.
3. **Transformation:** `Agent.BiteWindowExpired` becomes true at start + 30 s. From
   then on `Agent.ExecuteAction` stops AI movement instead of executing, and
   `PlayerManager` stops requesting decisions. The mechanic replaces the agent with a
   zombie-controlled bot.

Until the mechanic subscribes, `attack_human` logs one warning per target and nothing
is witnessed, because no bite was attempted.

### Verification

```powershell
python -B -m unittest discover -s DockerServer/Server/tests -v
docker run --rm -v "${PWD}/DockerServer/Server:/app:ro" -w /app dockerserver-gamescasp python3 -B -m unittest discover -s tests -v
```

The first command substitutes the solver; the second runs the same suite with the real
solver, including `test_scasp_integration.py`, which stores memories through the
production handler and checks the decisions that follow. In Unity, use **Tools > Facts >
Verify Half-Zombie Integration**, or batch mode with
`-executeMethod HalfZombieVerification.RunBatch` (also runs the fact-pipeline verifier).

## References

- [s(CASP) Tutorial and User Manual](https://personal.utdallas.edu/~gupta/nfm-ex/scasp-manual.pdf),
  sections 7.2–7.4: explicit negation, defaults, and preferences; chapter 3: numeric constraints.
- [Ciao s(CASP)](https://gitlab.software.imdea.org/ciao-lang/sCASP), the implementation
  installed by this project's Dockerfile.
- `innocent_behavior.pl` and `FactFactory.py` supply the current predicate order.
  The older `template_scasp_*.pl` files use a different order; do not copy their
  `health` or `distance` facts directly into these snapshots.

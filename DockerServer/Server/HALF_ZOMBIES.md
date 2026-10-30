# Half-zombie reasoning draft (issue #43)

`half_zombie_behavior.pl` extends `innocent_behavior.pl` and includes
`half_zombie_rules.pl`. Load the behavior entrypoint once, alongside a fact
snapshot, and query `action(Action, Actor)` as the existing Python NPC path does.
The live Python handler still loads `innocent_behavior.pl`: this draft is exercised
separately until the new facts and actions are ready for integration.

## Starting model

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

## Draft policy choices

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

The new action names fit the existing parser's `action(target)` form. Unity does
not execute them yet. `FactFactory` also still supplies only innocent snapshots,
and persistent `stored_fact/6` clauses are not automatically translated into
these bite/visibility facts. Integration is separate work.

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

## References

- [s(CASP) Tutorial and User Manual](https://personal.utdallas.edu/~gupta/nfm-ex/scasp-manual.pdf),
  sections 7.2–7.4: explicit negation, defaults, and preferences; chapter 3: numeric constraints.
- [Ciao s(CASP)](https://gitlab.software.imdea.org/ciao-lang/sCASP), the implementation
  installed by this project's Dockerfile.
- `innocent_behavior.pl` and `FactFactory.py` supply the current predicate order.
  The older `template_scasp_*.pl` files use a different order; do not copy their
  `health` or `distance` facts directly into these snapshots.

"""Exercise the draft with the real Ciao solver; no server or Unity needed.

Run inside the existing container:
    docker exec gamescasp python3 /app/tests/verify_half_zombie.py
"""

from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile


SERVER = Path(__file__).resolve().parents[1]
BASE = "health(3, actor). levers_to_win(15). levers_pulled_count(0).\n"
HALF = "innocent(actor). half_zombie(actor).\n"
HUMAN = ("innocent(target). health(3, target). "
         "human_visible(target, actor).\n")
BITE = "witnessed_bite(actor, biter, target).\n"
CHECKS = ("attacking(Actor) :- action(attack_human(_), Actor).\n"
          "approaching(Actor) :- action(move_to_human(_), Actor).\n")

# Each query must succeed. Negative assertions check absence of competing actions,
# rather than just accepting whichever action happens to be printed first.
SCENARIOS = [
    ("bite infects the victim, not the attacker", "bit_human(biter, target).",
     "infected(target), half_zombie(target), not infected(biter)"),
    ("witness suspects both participants", BITE,
     "suspected_infected(actor, biter), suspected_infected(actor, target)"),
    ("unrelated observer does not share knowledge", BITE,
     "not suspected_infected(other, target)"),
    ("lack of proof of humanity is not evidence", "",
     "not suspected_infected(actor, stranger)"),
    ("explicit negative defeats suspicion", BITE + "-half_zombie(target).",
     "not suspected_infected(actor, target)"),
    ("suspicion does not establish actual infection", BITE,
     "not half_zombie(target), not infected(target)"),
    ("normal innocent retains lever hunting", "innocent(actor).",
     "action(hunt_lever, actor), not attacking(actor)"),
    ("half zombie attacks at the range boundary", HALF + HUMAN + "distance(target, 1, actor).",
     "action(attack_human(target), actor), not approaching(actor), "
     "not action(hunt_lever, actor), not action(wait, actor)"),
    ("half zombie approaches outside bite range", HALF + HUMAN + "distance(target, 1.1, actor).",
     "action(move_to_human(target), actor), not attacking(actor)"),
    ("actual bite activates half-zombie actions", "innocent(actor). bit_human(biter, actor)."
     + HUMAN + "distance(target, 1, actor).", "action(attack_human(target), actor)"),
    ("nearest human is preferred", HALF + HUMAN + "distance(target, 3, actor). "
     "innocent(near). health(3, near). human_visible(near, actor). distance(near, 1, actor).",
     "action(attack_human(near), actor), not action(move_to_human(target), actor)"),
    ("equidistant humans remain valid alternatives", HALF + HUMAN + "distance(target, 1, actor). "
     "innocent(other). health(3, other). human_visible(other, actor). distance(other, 1, actor).",
     "action(attack_human(target), actor), action(attack_human(other), actor)"),
    ("fleeing beats attacking and human objectives", HALF + HUMAN + "distance(target, 1, actor). "
     "zombie(z). distance(z, 2, actor).",
     "action(run_from(z), actor), not attacking(actor), "
     "not approaching(actor), not action(hunt_lever, actor)"),
    ("half zombies do not target other half zombies", HALF + HUMAN
     + "half_zombie(target). distance(target, 1, actor).",
     "not human_target(target, actor), action(hunt_lever, actor)"),
    ("dead targets are excluded", HALF + "innocent(target). health(0, target). "
     "human_visible(target, actor). distance(target, 1, actor).",
     "not human_target(target, actor), action(hunt_lever, actor)"),
    ("unseen targets are excluded", HALF + "innocent(target). health(3, target). "
     "distance(target, 1, actor).", "not human_target(target, actor)"),
    ("half zombie without human role can wait", "half_zombie(actor).",
     "action(wait, actor)"),
    ("witness flees a nearby visible bite victim", "innocent(actor)." + BITE
     + HUMAN + "distance(target, 2, actor).",
     "action(run_from(target), actor), not action(hunt_lever, actor)"),
    ("distant suspicion does not interrupt objectives", "innocent(actor)." + BITE
     + HUMAN + "distance(target, 3, actor).",
     "action(hunt_lever, actor), not action(run_from(target), actor)"),
    ("actual status is not global observer knowledge", "innocent(actor). half_zombie(target)."
     + HUMAN + "distance(target, 1, actor).",
     "action(hunt_lever, actor), not action(run_from(target), actor)"),
    ("human hunt overrides barricade duty", HALF + HUMAN + "distance(target, 1, actor). "
     "barricade_visible(window, actor). distance(window, 1, actor). "
     "zombie(z). zombie_distance_to_barricade(window, 1, z).",
     "action(attack_human(target), actor), not action(hold_barricade(window), actor)"),
    ("half zombie cannot target itself", HALF + "human_visible(actor, actor). distance(actor, 0, actor).",
     "not human_target(actor, actor)"),
]


def main():
    solver = shutil.which("scasp")
    if solver is None:
        raise SystemExit("scasp is required; run this script in the gamescasp container.")
    failures = []
    with tempfile.TemporaryDirectory(prefix="half_zombie_checks_") as directory:
        # Ciao's #include resolves paths relative to the including file, even
        # when supplied an absolute path. Use an explicit relative path instead.
        behavior = Path(os.path.relpath(SERVER / "half_zombie_behavior.pl", directory)).as_posix()
        for index, (name, facts, query) in enumerate(SCENARIOS):
            path = Path(directory) / f"scenario_{index}.pl"
            path.write_text(
                f"#include('{behavior}').\n"
                + BASE + facts + "\n" + CHECKS + f"verified(yes) :- {query}.\n?- verified(X).\n",
                encoding="utf-8",
            )
            result = subprocess.run(
                [solver, "-s1", str(path)], cwd=SERVER, text=True,
                capture_output=True, timeout=30,
            )
            passed = result.returncode == 0 and re.search(r"\bX\s*=\s*yes\b", result.stdout)
            print(f"{'PASS' if passed else 'FAIL'}: {name}")
            if not passed:
                failures.append(name)
                print(result.stdout + result.stderr)

        # Use a separate snapshot to avoid contradictory health values.
        path = Path(directory) / "dead_actor.pl"
        path.write_text(
            f"#include('{behavior}').\n"
            + BASE.replace("health(3, actor)", "health(0, actor)") + HALF + HUMAN
            + "distance(target, 1, actor). zombie(z). distance(z, 1, actor).\n"
            + "verified(yes) :- dead(actor), not can_act(actor).\n?- verified(X).\n",
            encoding="utf-8",
        )
        result = subprocess.run([solver, "-s1", str(path)], cwd=SERVER, text=True,
                                capture_output=True, timeout=30)
        dead_verified = result.returncode == 0 and re.search(r"\bX\s*=\s*yes\b", result.stdout)
        # Query actions directly as well: Ciao's constructive negation of an
        # unconstrained action term is not a reliable absence check here.
        path.write_text(path.read_text().replace("?- verified(X).", "?- action(X, actor)."))
        actions = subprocess.run([solver, "-s0", str(path)], cwd=SERVER, text=True,
                                 capture_output=True, timeout=30)
        passed = (dead_verified and actions.returncode == 0
                  and re.search(r"^no models\s*$", actions.stdout, re.MULTILINE))
        print(f"{'PASS' if passed else 'FAIL'}: dead actors cannot act")
        if not passed:
            failures.append("dead actors cannot act")
            print(result.stdout + result.stderr + actions.stdout + actions.stderr)

    print(f"{len(SCENARIOS) + 1 - len(failures)}/{len(SCENARIOS) + 1} scenarios passed")
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())

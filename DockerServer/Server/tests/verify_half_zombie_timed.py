"""Run real-solver timer and defeat-exception scenarios in the existing container."""

from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile

from verify_half_zombie import BASE, HALF, HUMAN, SERVER


TARGET = HUMAN + "distance(target, 1, actor).\n"
DANGER = "zombie(z). distance(z, 2, actor).\n"


def clock(now, start=100):
    return f"current_time({now}). bite_window_started_at({start}, actor).\n"


# Check status predicates AND enumerate every action: an expected first answer
# alone would miss an unwanted competing flee/wait/lever action.
SCENARIOS = [
    ("initial window lasts 30 seconds", HALF + TARGET + clock(100),
     "bite_time_remaining(30, actor), active_half_zombie(actor), not critical_bite_stage(actor)",
     {"hunt_lever"}),
    ("visible human does not trigger hunting just above threshold", HALF + TARGET + clock(114.9),
     "not critical_bite_stage(actor)", {"hunt_lever"}),
    ("distant human does not trigger approaching before critical stage", HALF + HUMAN
     + "distance(target, 3, actor)." + clock(100),
     "not critical_bite_stage(actor)", {"hunt_lever"}),
    ("visible human does not interrupt lever activation", HALF + TARGET + clock(100)
     + "lever_visible(lever, actor). lever_state(lever, unpulled). "
     "distance(lever, 1, actor). nearest_zombie_distance_to_lever(lever, 10).",
     "not critical_bite_stage(actor)", {"pull_lever(lever)"}),
    ("visible human does not interrupt barricade duty", HALF + TARGET + clock(100)
     + "barricade_visible(window, actor). distance(window, 1, actor). "
     "zombie(z). zombie_distance_to_barricade(window, 1, z).",
     "not critical_bite_stage(actor)", {"hold_barricade(window)"}),
    ("above threshold danger still wins", HALF + TARGET + DANGER + clock(114.9),
     "not critical_bite_stage(actor)", {"run_from(z)"}),
    ("15 seconds is critical and overrides fleeing", HALF + TARGET + DANGER + clock(115),
     "critical_bite_stage(actor)", {"attack_human(target)"}),
    ("critical actor approaches despite danger", HALF + HUMAN
     + "distance(target, 3, actor)." + DANGER + clock(120),
     "critical_bite_stage(actor)", {"move_to_human(target)"}),
    ("positive fraction before expiry still allows attack", HALF + TARGET + DANGER + clock(129.9),
     "critical_bite_stage(actor), not eliminated(actor)", {"attack_human(target)"}),
    ("zero remaining transforms and eliminates", HALF + TARGET + DANGER + clock(130),
     "bite_time_remaining(0, actor), transformed_zombie(actor), zombie(actor), "
     "eliminated(actor), not active_half_zombie(actor), not critical_bite_stage(actor)", set()),
    ("past expiry cannot act", HALF + TARGET + clock(131),
     "bite_window_expired(actor), eliminated(actor)", set()),
    ("critical stage defeats human objectives without a target", HALF + clock(115),
     "critical_bite_stage(actor)", set()),
    ("critical stage defeats fleeing even without a target", HALF + DANGER + clock(115),
     "critical_bite_stage(actor)", set()),
    ("outside critical stage human objectives remain", HALF + clock(114),
     "not critical_bite_stage(actor)", {"hunt_lever"}),
    ("critical stage also blocks standalone half-zombie waiting",
     "half_zombie(actor)." + clock(115), "critical_bite_stage(actor)", set()),
    ("standalone half zombie can wait before critical stage",
     "half_zombie(actor)." + clock(100), "active_half_zombie(actor)", {"wait"}),
    ("standalone half zombie ignores visible humans before critical stage",
     "half_zombie(actor)." + TARGET + clock(100), "active_half_zombie(actor)", {"wait"}),
    ("missing timer does not grant unlimited actions", HALF + TARGET,
     "not bite_window_active(actor), not transformed_zombie(actor)", set()),
    ("missing clock does not grant unlimited actions",
     HALF + TARGET + "bite_window_started_at(100, actor).",
     "not bite_window_active(actor)", set()),
    ("future start is not an active window", HALF + TARGET + clock(99),
     "not bite_window_active(actor)", set()),
    ("ordinary innocent does not need an infection timer", "innocent(actor). current_time(200).",
     "not critical_bite_stage(actor), not eliminated(actor)", {"hunt_lever"}),
    ("successful bite refresh restores innocent behavior", HALF + TARGET + clock(125, 125)
     + "bit_human(actor, previous_victim).",
     "bite_time_remaining(30, actor), not critical_bite_stage(actor)", {"hunt_lever"}),
    ("old bite history cannot stop expiry", HALF + TARGET + clock(130)
     + "bit_human(actor, previous_victim).",
     "eliminated(actor)", set()),
    ("elimination remains final despite a later timer input", HALF + TARGET
     + clock(140, 140) + "eliminated(actor).",
     "not active_half_zombie(actor)", set()),
    ("critical biting overrides barricade duty", HALF + TARGET + clock(115)
     + "barricade_visible(window, actor). distance(window, 1, actor). "
     "zombie(z). zombie_distance_to_barricade(window, 1, z).",
     "critical_bite_stage(actor)", {"attack_human(target)"}),
]


def main():
    solver = shutil.which("scasp")
    if solver is None:
        raise SystemExit("Run inside gamescasp with the Ciao scasp solver installed.")
    failures = 0
    with tempfile.TemporaryDirectory(prefix="half_zombie_timed_") as directory:
        behavior = Path(os.path.relpath(SERVER / "half_zombie_timed_behavior.pl", directory)).as_posix()
        cases = list(SCENARIOS)
        # Health is a snapshot value, so replace it rather than adding a second fact.
        cases.append(("dead actor cannot bite even in critical stage", HALF + TARGET + clock(115),
                      "dead(actor), critical_bite_stage(actor)", set()))
        for index, (name, facts, status, expected) in enumerate(cases):
            base = BASE.replace("health(3, actor)", "health(0, actor)") if index == len(cases) - 1 else BASE
            source = f"#include('{behavior}').\n" + base + facts + "\n"
            path = Path(directory) / f"scenario_{index}.pl"
            path.write_text(source + f"verified(yes) :- {status}.\n?- verified(X).\n", encoding="utf-8")
            check = subprocess.run([solver, "-s1", str(path)], cwd=SERVER, text=True,
                                   capture_output=True, timeout=30)
            status_ok = check.returncode == 0 and re.search(r"\bX\s*=\s*yes\b", check.stdout)
            path.write_text(source + "?- action(X, actor).\n", encoding="utf-8")
            actions = subprocess.run([solver, "-s0", str(path)], cwd=SERVER, text=True,
                                     capture_output=True, timeout=30)
            actual = set(re.findall(r"^X\s*=\s*([a-z][A-Za-z0-9_]*(?:\([a-z][A-Za-z0-9_]*\))?)\s*$",
                                    actions.stdout, re.MULTILINE))
            empty_ok = expected or re.search(r"^no models\s*$", actions.stdout, re.MULTILINE)
            passed = status_ok and actions.returncode == 0 and actual == expected and empty_ok
            print(f"{'PASS' if passed else 'FAIL'}: {name}")
            if not passed:
                failures += 1
                print(f"Expected actions: {expected}; actual: {actual}")
                print(check.stdout + check.stderr + actions.stdout + actions.stderr)
    print(f"{len(cases) - failures}/{len(cases)} timed scenarios passed")
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())

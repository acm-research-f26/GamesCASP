"""Logical-policy scenarios for the baseline and reasoned s(CASP) policies.

Runs the real solver on controlled fact snapshots and enumerates every surviving
action (-s0), so a scenario fails if an extra, contradictory action is possible.
Skipped unless scasp is on PATH; run inside the gamescasp image:
    docker run --rm -v <repo>/DockerServer/Server:/app:ro -w /app dockerserver-gamescasp \
        python3 -B -m unittest tests.test_reasoned_policy -v
"""

from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

SERVER = Path(__file__).resolve().parents[1]
POLICIES = {"baseline": "half_zombie_timed_behavior.pl", "reasoned": "reasoned_behavior.pl"}
BASE = "innocent(a). health(3, a). levers_to_win(2). levers_pulled_count(0).\n"


def lever(name="l1", distance=3, zombie_distance=20, state="unpulled"):
    return (f"lever_state({name}, {state}). lever_visible({name}, a). distance({name}, {distance}, a). "
            f"nearest_zombie_distance_to_lever({name}, {zombie_distance}).\n")


def zombie(name="z", distance=1.5):
    return f"zombie({name}). distance({name}, {distance}, a).\n"


def barricade(name="b", distance=2, reinforcement=30, zombie_name=None, zombie_distance=0.5):
    facts = (f"barricade_visible({name}, a). barricade_reinforcement({name}, {reinforcement}). "
             f"distance({name}, {distance}, a).\n")
    if zombie_name:
        facts += f"zombie_distance_to_barricade({name}, {zombie_distance}, {zombie_name}).\n"
    return facts


@unittest.skipUnless(shutil.which("scasp"), "requires the Ciao s(CASP) solver (gamescasp image)")
class PolicyScenarioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="policy_")
        cls.directory = Path(cls.temporary.name)
        for rule in SERVER.glob("*.pl"):
            shutil.copy(rule, cls.directory / rule.name)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def actions(self, policy, facts):
        path = self.directory / f"scenario_{policy}.pl"
        path.write_text(f"#include('{POLICIES[policy]}').\n{facts}\n?- action(X, a).\n", encoding="utf-8")
        result = subprocess.run(["scasp", "-s0", path.name], cwd=self.directory, text=True,
                                capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        return set(re.findall(r"^X = (\S+?)\s*$", result.stdout, re.MULTILINE))

    def expect(self, facts, baseline, reasoned):
        self.assertEqual(self.actions("baseline", facts), baseline, "baseline policy")
        self.assertEqual(self.actions("reasoned", facts), reasoned, "reasoned policy")

    # 1. Straightforward objective.
    def test_1_safe_lever_is_approached_then_pulled(self):
        self.expect(BASE + lever(distance=3), {"move_to_lever(l1)"}, {"move_to_lever(l1)"})
        self.expect(BASE + lever(distance=0.5), {"pull_lever(l1)"}, {"pull_lever(l1)"})
        self.expect(BASE, {"hunt_lever"}, {"hunt_lever"})

    # 2. A threat interrupts the objective.
    def test_2_threat_interrupts_lever(self):
        self.expect(BASE + lever(distance=3) + zombie(distance=1.5), {"run_from(z)"}, {"run_from(z)"})

    # 2b. With two threats, the reasoned policy flees the nearest one only.
    def test_2b_two_threats_flee_nearest(self):
        facts = BASE + zombie("z1", 1.9) + zombie("z2", 1.2)
        self.assertEqual(self.actions("reasoned", facts), {"run_from(z2)"})
        self.assertEqual(self.actions("baseline", facts), {"run_from(z1)", "run_from(z2)"})

    # 3. The threat recedes: work resumes.
    def test_3_threat_disappears_and_work_resumes(self):
        self.expect(BASE + lever(distance=3) + zombie(distance=6), {"move_to_lever(l1)"}, {"move_to_lever(l1)"})

    # 4. Barricade decisions.
    def test_4_failing_nearby_barricade_is_repaired_before_it_breaks(self):
        facts = BASE + lever(distance=4) + barricade(distance=2, reinforcement=30)
        self.expect(facts, {"move_to_lever(l1)"}, {"move_to_barricade(b)"})
        self.expect(BASE + lever(distance=4) + barricade(distance=0.8, reinforcement=30),
                    {"move_to_lever(l1)"}, {"hold_barricade(b)"})
        # A distant failing barricade does not pull the actor off its lever.
        self.expect(BASE + lever(distance=4) + barricade(distance=6, reinforcement=30),
                    {"move_to_lever(l1)"}, {"move_to_lever(l1)"})

    def test_4b_breached_barricade_is_defended_unless_overrun(self):
        attacked = BASE + lever(distance=4) + zombie("z", 4) + barricade(distance=3, reinforcement=20, zombie_name="z")
        self.expect(attacked, {"move_to_barricade(b)"}, {"move_to_barricade(b)"})
        overrun = BASE + lever(distance=4) + zombie("z", 4) + barricade(distance=3, reinforcement=0, zombie_name="z")
        # Baseline still walks into an opening that is already lost.
        self.expect(overrun, {"move_to_barricade(b)"}, {"move_to_lever(l1)"})

    # 5. A default overridden by more specific evidence.
    def test_5_final_lever_overrides_fleeing_unless_in_contact(self):
        final = "innocent(a). health(3, a). levers_to_win(2). levers_pulled_count(1).\n" + lever(distance=0.6)
        self.expect(final + zombie(distance=1.8), {"run_from(z)"}, {"pull_lever(l1)"})
        self.expect(final + zombie(distance=0.9), {"run_from(z)"}, {"run_from(z)"})
        # Not the final lever: the default (flee) stands.
        self.expect(BASE + lever(distance=0.6) + zombie(distance=1.8), {"run_from(z)"}, {"run_from(z)"})

    def test_5b_strong_negation_unreachable_lever_is_abandoned(self):
        facts = BASE + lever(distance=3) + "-reachable(l1, a).\n"
        self.expect(facts, {"move_to_lever(l1)"}, {"hunt_lever"})
        # Missing reachability information keeps the default: pursue.
        self.expect(BASE + lever(distance=3), {"move_to_lever(l1)"}, {"move_to_lever(l1)"})

    # 6. New evidence revises an earlier conclusion (nonmonotonic).
    def test_6_new_evidence_revises_the_choice(self):
        before = BASE + lever("l1", 3) + lever("l2", 5)
        self.assertEqual(self.actions("reasoned", before), {"move_to_lever(l1)"})
        # A zombie appears near the closer lever: the safe lever is now preferred.
        after = BASE + lever("l1", 3, zombie_distance=2) + lever("l2", 5)
        self.assertEqual(self.actions("reasoned", after), {"move_to_lever(l2)"})

    # 7. Commitment carried across decisions (knowledge of the actor's own activity).
    def test_7_repair_in_progress_is_finished(self):
        restoring = BASE + lever(distance=4) + barricade(distance=0.5, reinforcement=80)
        self.assertEqual(self.actions("reasoned", restoring), {"move_to_lever(l1)"})
        holding = restoring + "current_action(hold_barricade(b), a).\n"
        self.assertEqual(self.actions("reasoned", holding), {"hold_barricade(b)"})
        restored = BASE + lever(distance=4) + barricade(distance=0.5, reinforcement=97) + "current_action(hold_barricade(b), a).\n"
        self.assertEqual(self.actions("reasoned", restored), {"move_to_lever(l1)"})

    # 8. Conflicting pressures resolved by documented priorities.
    def test_8_conflicting_pressures(self):
        conflict = BASE + lever(distance=0.5) + zombie("z", 4) + barricade(distance=3, reinforcement=20, zombie_name="z")
        # Reasoned: a lever already in reach (45) beats defending (40). Baseline: barricade wins.
        self.expect(conflict, {"move_to_barricade(b)"}, {"pull_lever(l1)"})
        danger = BASE + lever(distance=0.5) + zombie("z", 1.5) + barricade(distance=3, reinforcement=20, zombie_name="z")
        self.expect(danger, {"run_from(z)"}, {"run_from(z)"})

    def test_after_the_win_damaged_barricades_are_maintained(self):
        won = "innocent(a). health(3, a). levers_to_win(2). levers_pulled_count(2).\n" + barricade(distance=4, reinforcement=70)
        self.expect(won, {"wait"}, {"move_to_barricade(b)"})
        self.expect("innocent(a). health(3, a). levers_to_win(2). levers_pulled_count(2).\n", {"wait"}, {"wait"})

    def test_dead_agents_never_act(self):
        dead = "innocent(a). health(0, a). levers_to_win(2). levers_pulled_count(0).\n" + lever(distance=1)
        self.expect(dead, set(), set())


if __name__ == "__main__":
    unittest.main()

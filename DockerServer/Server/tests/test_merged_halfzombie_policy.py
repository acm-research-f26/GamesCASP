"""Reasoned-policy smoke tests for the mansion + half-zombie integration.

Run with the real s(CASP) binary (e.g. in the GamesCASP Docker image).
These exercise the NEW reasoned half-zombie path; baseline remains unchanged.
"""
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

SERVER = Path(__file__).resolve().parents[1]


@unittest.skipUnless(shutil.which("scasp"), "requires the Ciao s(CASP) solver")
class ReasonedHalfZombiePolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="reasoned_hz_")
        cls.work = Path(cls.temp.name)
        for source in SERVER.glob("*.pl"):
            shutil.copyfile(source, cls.work / source.name)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def actions(self, facts):
        query = self.work / "test_query.pl"
        query.write_text("#include('reasoned_behavior.pl').\n" + facts + "\n?- action(X, player1).\n", encoding="utf-8")
        result = subprocess.run(["scasp", "-s0", query.name], cwd=self.work, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        return set(re.findall(r"^[ \t]*X[ \t]*=[ \t]*(\S+?)[ \t]*$", result.stdout, re.MULTILINE))

    def state(self, now=None, human_distance=None):
        facts = ["innocent(player1).", "health(3, player1).", "levers_to_win(2).", "levers_pulled_count(0).", "half_zombie(player1).", "bite_window_started_at(100, player1)."]
        if now is not None:
            facts.append(f"current_time({now}).")
        if human_distance is not None:
            facts += ["innocent(player2).", "health(3, player2).", "human_visible(player2, player1).", f"distance(player2, {human_distance}, player1)."]
        return "\n".join(facts) + "\n"

    def test_before_critical_window_uses_reasoned_innocent_goals(self):
        facts = self.state(now=101) + "lever_state(lever_1, unpulled). lever_visible(lever_1, player1). distance(lever_1, 4, player1). nearest_zombie_distance_to_lever(lever_1, 20).\n"
        self.assertEqual(self.actions(facts), {"move_to_lever(lever_1)"})

    def test_critical_window_chases_human(self):
        self.assertEqual(self.actions(self.state(now=116, human_distance=2)), {"move_to_human(player2)"})

    def test_critical_window_bites_human_in_range(self):
        self.assertEqual(self.actions(self.state(now=116, human_distance=0.5)), {"attack_human(player2)"})

    def test_critical_window_without_target_has_no_model(self):
        self.assertEqual(self.actions(self.state(now=116)), set())

    def test_expired_bite_window_has_no_action(self):
        self.assertEqual(self.actions(self.state(now=130, human_distance=0.5)), set())


if __name__ == "__main__":
    unittest.main()

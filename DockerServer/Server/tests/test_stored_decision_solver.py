"""Check stored knowledge changes decisions with the real s(CASP) solver."""

import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from FactFactory import FactFactory

SERVER = Path(__file__).resolve().parents[1]
STATE = {"health": 3, "levers_to_win": 3, "levers_pulled_count": 0,
         "levers": [{"id": "lever1", "state": "unpulled", "visible": True,
                     "distance": 1, "nearest_zombie_distance": 100}]}


@unittest.skipUnless(shutil.which("scasp"), "Real s(CASP) is required; run inside the gamescasp container")
class StoredDecisionSolverTests(unittest.TestCase):
    def solve(self, memory, state=None, status=None):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "query.pl"
            behavior = Path(os.path.relpath(SERVER / "half_zombie_timed_behavior.pl", directory)).as_posix()
            source = (f"#include('{behavior}').\n" + FactFactory.generate_npc_facts("player1", state or STATE)
                      + memory)
            if status:
                source += f"verified(yes) :- {status}.\n?- verified(X).\n"
            else:
                source += "?- action(X, player1).\n"
            path.write_text(source, encoding="utf-8")
            result = subprocess.run([shutil.which("scasp"), "-s0", str(path)], cwd=SERVER,
                                    text=True, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            answers = set(re.findall(r"^X\s*=\s*([a-z][A-Za-z0-9_]*(?:\([a-z][A-Za-z0-9_]*\))?)\s*$",
                                     result.stdout, re.MULTILINE))
            self.assertTrue(answers, result.stdout + result.stderr)
            return answers

    def test_both_lifetimes_change_lever_decision(self):
        self.assertEqual(self.solve(""), {"pull_lever(lever1)"})
        for lifetime in ("permanent", "mutable"):
            with self.subTest(lifetime=lifetime):
                memory = f"stored_fact(player1, {lifetime}, lever_activated, lever1, none, none).\n"
                self.assertEqual(self.solve(memory), {"hunt_lever"})

    def test_other_observers_memory_does_not_change_decision(self):
        self.assertEqual(self.solve("stored_fact(other, permanent, lever_activated, lever1, none, none).\n"),
                         {"pull_lever(lever1)"})

    def test_old_threats_are_queryable_without_overriding_current_state(self):
        memory = ("stored_fact(player1, mutable, agent_attacked, player1, old_attacker, 1).\n"
                  "stored_fact(player1, permanent, zombie_seen, old_zombie, none, 1).\n")
        self.assertEqual(self.solve(memory), {"pull_lever(lever1)"})
        self.assertEqual(self.solve(memory, status="remembered_attack(player1, old_attacker, player1), "
                                   "remembered_zombie(old_zombie, player1), health(3, player1)"), {"yes"})
        state = STATE | {"zombies": [{"id": "current_zombie", "distance": 1}]}
        self.assertEqual(self.solve(memory, state), {"run_from(current_zombie)"})

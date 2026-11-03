from functools import lru_cache
import math
from pathlib import Path
import re
from typing import Any


TIMED_BEHAVIOR_FILE = Path(__file__).resolve().parent / "half_zombie_timed_behavior.pl"


@lru_cache(maxsize=None)
def configured_bite_window_seconds(path=TIMED_BEHAVIOR_FILE):
    """The s(CASP) rules are the single source of truth for the bite window."""
    match = re.search(r"^bite_window_seconds\((\d+(?:\.\d+)?)\)\.",
                      Path(path).read_text(encoding="utf-8"), re.MULTILINE)
    if match is None:
        raise RuntimeError(f"bite_window_seconds/1 not found in {path}")
    return float(match.group(1))


class FactFactory:
    # Convert a supported event into facts, allowing only plain atom identifiers.
    @staticmethod
    def generate_fact_from_json(json_message: dict[str, Any]) -> str:
        message_type = json_message.get('message_type')

        if message_type == "heard_noise":
            return "noise(unknown).\n"
        if message_type == "vase_broken":
            culprit = json_message.get('culprit')
            if (
                not isinstance(culprit, str)
                or re.fullmatch(r"[a-z][A-Za-z0-9_]*", culprit) is None
            ):
                raise ValueError(
                    "culprit must be a plain Prolog atom, such as 'player' or 'guard_1'"
                )
            return f"broken_vase({culprit}).\n"
        if message_type == "suspicious_sighting":
            return "suspicious_sighting(player).\nplayer_in_restricted_area.\n"
        if message_type == "diamond_broken":
            return "diamond_saw_broken.\n"
        if message_type == "alarm_raised":
            return "alarm_raised.\n"
        if message_type == "player_seen":
            return "player_seen.\n"

        raise ValueError("Unknown or missing fact message_type")



# Code added to generate facts from NPCs
    @staticmethod
    def generate_npc_facts(actor: str, state: dict[str, Any]) -> str:
        def atom(value):
            if not isinstance(value, str) or re.fullmatch(
                r"[a-z][A-Za-z0-9_]*", value
            ) is None:
                raise ValueError(f"Invalid Prolog atom: {value!r}")
            return value

        def number(value):
            if isinstance(value, bool) or not isinstance(value, (int, float)):
                raise ValueError(f"Invalid numeric value: {value!r}")
            if not math.isfinite(value) or value < 0:
                raise ValueError(f"Expected a finite nonnegative number: {value!r}")
            if isinstance(value, int):
                return str(value)
            # Fixed point: str() can produce exponents such as 1e-05.
            return f"{value:.6f}".rstrip("0").rstrip(".")

        actor = atom(actor)

        facts = [
            f"innocent({actor}).",
            f"health({number(state['health'])}, {actor}).",
            f"levers_to_win({number(state['levers_to_win'])}).",
            f"levers_pulled_count({number(state['levers_pulled_count'])}).",
        ]

        # Half-zombie status and bite timer, for this actor only.
        half_zombie = state.get("half_zombie", False)
        if not isinstance(half_zombie, bool):
            raise ValueError("half_zombie must be a boolean")

        if half_zombie:
            window = state.get("bite_window_seconds")
            if window != configured_bite_window_seconds():
                raise ValueError(
                    f"bite_window_seconds {window!r} disagrees with s(CASP) "
                    f"bite_window_seconds({configured_bite_window_seconds():g})"
                )
            facts.append(f"half_zombie({actor}).")
            facts.append(f"current_time({number(state['current_time'])}).")
            facts.append(
                f"bite_window_started_at({number(state['bite_window_started_at'])}, {actor})."
            )

        for lever in state.get("levers", []):
            lever_id = atom(lever["id"])

            facts.append(
                f"lever_state({lever_id}, {atom(lever['state'])})."
            )

            if lever.get("visible", False):
                facts.append(f"lever_visible({lever_id}, {actor}).")

            facts.append(
                f"distance({lever_id}, {number(lever['distance'])}, {actor})."
            )

            facts.append(
                f"nearest_zombie_distance_to_lever("
                f"{lever_id}, {number(lever['nearest_zombie_distance'])})."
            )

        for zombie in state.get("zombies", []):
            zombie_id = atom(zombie["id"])

            facts.append(f"zombie({zombie_id}).")
            facts.append(
                f"distance({zombie_id}, {number(zombie['distance'])}, {actor})."
            )

        # Nearby living humans: bite targets, or suspects to avoid.
        # Only observable fields are read; another human's infection is never emitted.
        for human in state.get("humans", []):
            human_id = atom(human["id"])

            if human_id == actor:
                continue

            facts.append(f"innocent({human_id}).")
            facts.append(f"health({number(human['health'])}, {human_id}).")
            facts.append(
                f"distance({human_id}, {number(human['distance'])}, {actor})."
            )

            if human.get("visible", False):
                facts.append(f"human_visible({human_id}, {actor}).")

        for barricade in state.get("barricades", []):
            barricade_id = atom(barricade["id"])

            if barricade.get("visible", False):
                facts.append(f"barricade_visible({barricade_id}, {actor}).")

            facts.append(
                f"barricade_reinforcement("
                f"{barricade_id}, {number(barricade['reinforcement'])})."
            )

            facts.append(
                f"distance({barricade_id}, "
                f"{number(barricade['distance'])}, {actor})."
            )

            # Unity JsonUtility serializes arrays, not dictionaries. Keep the
            # original dictionary form available for existing callers.
            distances = barricade.get("zombie_distances", [])
            if isinstance(distances, dict):
                distances = [{"id": key, "distance": value} for key, value in distances.items()]
            if not isinstance(distances, list):
                raise ValueError("zombie_distances must be an array or object")
            for entry in distances:
                if not isinstance(entry, dict):
                    raise ValueError("Each zombie distance must be an object")
                facts.append(
                    f"zombie_distance_to_barricade("
                    f"{barricade_id}, {number(entry['distance'])}, {atom(entry['id'])})."
                )

            # Unity's JsonUtility cannot send dictionaries: the nearest visible zombie.
            if barricade.get("nearest_zombie"):
                facts.append(
                    f"zombie_distance_to_barricade({barricade_id}, "
                    f"{number(barricade['nearest_zombie_distance'])}, "
                    f"{atom(barricade['nearest_zombie'])})."
                )

        # Reasoning context, used by the reasoned policy and ignored by the baseline.
        # The actor's own ongoing activity supports commitment (no oscillation).
        current_action = state.get("current_action") or ""
        if current_action:
            target = state.get("current_target") or ""
            term = f"{atom(current_action)}({atom(target)})" if target else atom(current_action)
            facts.append(f"current_action({term}, {actor}).")

        # Strong negation: navigation proved there is no path to these targets.
        for target in state.get("unreachable", []):
            facts.append(f"-reachable({atom(target)}, {actor}).")

        return "\n".join(facts) + "\n"

import re
from typing import Any


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
            import math
            if not math.isfinite(value) or value < 0:
                raise ValueError(f"Expected a finite nonnegative number: {value!r}")
            return str(value)

        actor = atom(actor)

        facts = [
            f"innocent({actor}).",
            f"health({number(state['health'])}, {actor}).",
            f"levers_to_win({number(state['levers_to_win'])}).",
            f"levers_pulled_count({number(state['levers_pulled_count'])}).",
        ]

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

            for zombie_id, distance in barricade.get(
                "zombie_distances", {}
            ).items():
                facts.append(
                    f"zombie_distance_to_barricade("
                    f"{barricade_id}, {number(distance)}, {atom(zombie_id)})."
                )

        return "\n".join(facts) + "\n"
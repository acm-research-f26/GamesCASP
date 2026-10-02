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

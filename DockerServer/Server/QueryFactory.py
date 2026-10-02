from typing import Any

class FactFactory:
    # takes in message type and returns the corresponding s(CASP) query
    @staticmethod
    def generate_fact_from_json(json_message: dict[str, Any]) -> str:
        message_type = json_message['message_type']

        if message_type == "heard_noise":
            return "noise(unknown).\n"
        if message_type == "vase_broken":
            return f"broken_vase({json_message['culprit']}).\n"
        if message_type == "suspicious_sighting":
            return f"suspicious_sighting(player).\nplayer_in_restricted_area.\n"
        if message_type == "diamond_broken":
            return "diamond_saw_broken.\n"
        if message_type == "alarm_raised":
            return "alarm_raised.\n"
        if message_type == "player_seen":
            return "player_seen.\n"

        return ""
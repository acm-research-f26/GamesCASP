import UnityRequest as ur


class UnityRequestFactory:
    @staticmethod
    def make_request(request_type, unity_request_ctx):
        if request_type == 'fact':
            return ur.UnityTempFactRequest(unity_request_ctx)

        if request_type == 'get_action':
            return ur.UnityActionRequest(unity_request_ctx)

        if request_type == 'npc_decision':
            return ur.UnityNPCDecisionRequest(unity_request_ctx)

        raise ValueError(
            "request_type must be 'fact', 'get_action', or 'npc_decision'"
        )
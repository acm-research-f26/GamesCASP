import UnityRequest as ur

# takes in msg and determines 
class UnityRequestFactory:
    @staticmethod
    def make_request(request_type, unity_request_ctx):
        if 'request_type' == 'fact':
            return ur.UnityTempFactRequest(unity_request_ctx)
        if 'request_type' == 'get_action':
            return ur.UnityActionRequest(unity_request_ctx)

        return None
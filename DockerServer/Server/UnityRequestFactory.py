import UnityRequest

# takes in msg and determines 
class UnityRequestFactory:
    @staticmethod
    def make_request_from_request_type(request_type):
        if request_type == 'get_action':
            return 
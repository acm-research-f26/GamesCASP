from FactFactory import FactFactory

# ctx = UnityRequestContext().add(1).add("hi").build()

class UnityRequestContext:
    def __init__(self):
        self.vars = []

    def add(self, arg):
        self.vars.append(arg)

    def build(self):
        return self


class UnityRequest:
    def __init__(self, ctx):
        self.ctx = ctx

    def process(self):
        pass

class UnityActionRequest(UnityRequest):
    def process(self):
        fact = FactFactory.generate_fact_from_json(super.vars.jsonMessage)
        super.varstemp_facts_file.write(fact)
        temp_facts_file.flush()
        
class UnityFactRequest(UnityRequest):
    def process(self):
        
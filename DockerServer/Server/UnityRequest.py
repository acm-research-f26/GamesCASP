from FactFactory import FactFactory

# ctx = UnityRequestContext().add(1).add("hi").build()

class UnityRequestContext:
    def __init__(self):
        self.vars = {}

    def add(self, name, value):
        self.vars[name] = value
        return self

    def __getitem__(self, name):
        return self.vars[name]

    def build(self):
        return self


class UnityRequest:
    def __init__(self, ctx):
        self.ctx = ctx

    def process(self):
        pass

class UnityActionRequest(UnityRequest):
    def process(self):
        pass
        
class UnityFactRequest(UnityRequest):
    def process(self):
        fact = FactFactory.generate_fact_from_json(self.ctx["json_message"])
        temp_file = self.ctx["temp_facts_file"]
        temp_file.write(fact)
        temp_file.flush()
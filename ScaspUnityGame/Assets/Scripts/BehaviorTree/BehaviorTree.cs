using UnityEngine;

public class BehaviorTree : AiStrategy
{
    private Blackboard state = new Blackboard();
    
    public override void storeFact(Fact fact)
    {
        // replace transient facts every tick
        if (fact.type == FactType.transient)
        {
            fact.value = false; // if it was false, keep it false. if it was true, reset it to false
        }
    }

    public override AiAction getNextAction()
    {
        AiAction 
    }
}

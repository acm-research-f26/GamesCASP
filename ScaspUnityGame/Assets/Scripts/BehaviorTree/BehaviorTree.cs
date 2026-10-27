using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public class BehaviorTree : IAgentStrategy
{
    private Blackboard state = new Blackboard();
    private AiAction aiAction = new AiAction();
    
    // store unity gamestate into behavior tree
    public void StoreFact(Fact fact)
    {
        state.update(fact);
    }

    public IReadOnlyList<Fact> FetchTransientFacts()
    {
        // use the AiAction FetchTransientFacts() method
        IReadOnlyList<Fact> transientFacts = aiAction.FetchTransientFacts();

        return transientFacts;
    }

    public void DecideNextAction(Agent_Parent_Script agent)
    {
        // get all transient facts using the behaviortree's FetchTransientFacts() method,
        // which uses the AiAction FetchTransientFacts() method
        IReadOnlyList<Fact> transientFacts = FetchTransientFacts();

        state.updateTransientFacts(transientFacts);

        // call Execute on the AiAction object

        // after executing an action, clear all the current transient facts
        state.clearTransientState(transientFacts);
    }
}

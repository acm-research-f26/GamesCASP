using UnityEngine;

public class LastLeverCondition : Node
{
    private Blackboard state;
    
    // constructor for this node, given a state
    public LastLeverCondition(Blackboard state)
    {
        this.state = state;
    }

    // every tick evaluates whether or not a lever is the last one
    public override NodeStatus Tick()
    {
        if (state.lastLever)
        {
            return NodeStatus.Success;
        }

        return NodeStatus.Failure;
    }
}

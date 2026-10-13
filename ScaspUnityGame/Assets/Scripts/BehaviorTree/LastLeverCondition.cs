using UnityEngine;

public class LastLeverCondition : ConditionNode
{
    // evaluate whether or not a lever is the last one
    public override bool evaluate()
    {
        if (state.lastLever)
        {
            return true;
        }

        return false;
    }
}

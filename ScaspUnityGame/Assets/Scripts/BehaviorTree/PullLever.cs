using UnityEngine;

public class PullLever : ActionNode
{
    LastLeverCondition lastLever;
    public override bool execute()
    {
        if (lastLever.evaluate())
        {
            Debug.Log("Pulling lever!");
            return true;
        }

        return false;
    }
}

using UnityEngine;

public class PullLever : Node
{
    public override NodeStatus Tick()
    {
        Debug.Log("Pulling lever!");
        return NodeStatus.Success;
    }
}

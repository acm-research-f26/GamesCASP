using UnityEngine;
using System.Collections.Generic;

public class Sequence : Node
{
    private List<Node> children;
    public Sequence(List<Node> children) 
    { 
        this.children = children; 
    }

    // tick each node until one fails
    public override NodeStatus Tick()
    {
        foreach (var child in children)
        {
            var status = child.Tick();
            if (status != NodeStatus.Success)
            {
                return status;
            }
        }

        return NodeStatus.Success;
    }
}
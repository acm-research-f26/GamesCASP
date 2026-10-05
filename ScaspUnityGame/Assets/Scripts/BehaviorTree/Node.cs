using UnityEngine;

// all nodes will inherit from this
public abstract class Node
{
    public abstract NodeStatus Tick();
}

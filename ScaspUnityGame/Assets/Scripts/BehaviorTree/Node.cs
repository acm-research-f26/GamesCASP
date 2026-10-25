using UnityEngine;

// all nodes will inherit from this
public abstract class Node
{
    // constructor for a node, each agent gets their own state
    public Blackboard state;
    
    // condition and action nodes will override this method with their own evaluation conditions
    public abstract bool Tick(Blackboard state);
}

using UnityEngine;

public abstract class ConditionNode : Node
{    
    // evaluate left node if evaluate() method returns false
    Node leftNode;

    // evaluate right node if evaluate() method returns true
    Node rightNode;

    // condition nodes will override this method with their conditions
    public abstract bool evaluate();

    public override bool Tick(Blackboard state)
    {
        if (evaluate())
        {
            return rightNode.Tick(state);
        }

        return leftNode.Tick(state);
    }
}

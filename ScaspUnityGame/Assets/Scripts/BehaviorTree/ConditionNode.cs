using System;

public abstract class ConditionNode : Node
{
    // False follows the left child; true follows the right child.
    private readonly Node leftNode;
    private readonly Node rightNode;

    protected ConditionNode(Node leftNode, Node rightNode)
    {
        this.leftNode = leftNode ?? throw new ArgumentNullException(nameof(leftNode));
        this.rightNode = rightNode ?? throw new ArgumentNullException(nameof(rightNode));
    }

    public abstract bool evaluate(Blackboard state);

    public override bool Tick(Blackboard state,
        out (string action, string target) decision)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (evaluate(state)) return rightNode.Tick(state, out decision);
        return leftNode.Tick(state, out decision);
    }
}

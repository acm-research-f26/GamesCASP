public sealed class LastLeverCondition : ConditionNode
{
    public LastLeverCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Keep your boolean. Refresh it from counts when initializing and on activation.
    public override bool evaluate(Blackboard state) => state.lastLever;
}

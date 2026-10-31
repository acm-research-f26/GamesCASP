public sealed class CanActCondition : ConditionNode
{
    public CanActCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // The diagram's first gate means health greater than zero.
    public override bool evaluate(Blackboard state) => state.health > 0;
}

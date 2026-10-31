public sealed class MoreLeversNeededCondition : ConditionNode
{
    public MoreLeversNeededCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    public override bool evaluate(Blackboard state) => state.activatedLevers < state.totalLevers;
}

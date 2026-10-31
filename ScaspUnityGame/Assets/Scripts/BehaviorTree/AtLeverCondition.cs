public sealed class AtLeverCondition : ConditionNode
{
    public AtLeverCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    public override bool evaluate(Blackboard state)
    {
        // Use the same preferred-target helper as both lever action leaves.
        Fact lever = NodeTargets.PreferredLever(state);
        return lever != null && lever.Distance.Value <= 1f;
    }
}

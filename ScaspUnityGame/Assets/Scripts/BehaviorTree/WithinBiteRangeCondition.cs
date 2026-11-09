public sealed class WithinBiteRangeCondition : ConditionNode
{
    public WithinBiteRangeCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Check if the preferred human is close enough to bite
    public override bool evaluate(Blackboard state)
    {
        Fact human = NodeTargets.PreferredHuman(state);
        return human != null && human.Distance.Value <= state.BiteRange;
    }
}

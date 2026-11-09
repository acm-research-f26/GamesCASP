public sealed class WithinBiteRangeCondition : ConditionNode
{
    // Same one unit radius as within_bite_range in half_zombie_rules.pl
    private const float BiteRange = 1f;

    public WithinBiteRangeCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Check if the preferred human is close enough to bite
    public override bool evaluate(Blackboard state)
    {
        Fact human = NodeTargets.PreferredHuman(state);
        return human != null && human.Distance.Value <= BiteRange;
    }
}

public sealed class ImmediateDangerCondition : ConditionNode
{
    public ImmediateDangerCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // A visible zombie alone is not enough: danger is within TWO units.
    public override bool evaluate(Blackboard state) =>
        state.numZombiesNearby > 0 &&
        state.closestZombieDistance.HasValue &&
        state.closestZombieDistance.Value <= 2f &&
        NodeTargets.DangerZombie(state) != null;
}

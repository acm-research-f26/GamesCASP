public sealed class CriticalBiteStageCondition : ConditionNode
{
    public CriticalBiteStageCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Return the normal tree if we arent in the critical stage
    // Healthy NPCs are never in it, so they always get the normal tree
    public override bool evaluate(Blackboard state) => state.InCriticalBiteStage;
}

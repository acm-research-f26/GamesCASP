public sealed class HumanTargetVisibleCondition : ConditionNode
{
    public HumanTargetVisibleCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Here we check if there is any human we can go after, like visible_human_target in Prolog
    public override bool evaluate(Blackboard state) => NodeTargets.PreferredHuman(state) != null;
}

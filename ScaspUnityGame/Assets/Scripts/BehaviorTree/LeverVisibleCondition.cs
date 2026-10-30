public sealed class LeverVisibleCondition : ConditionNode
{
    public LeverVisibleCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Confirm that the flag has a usable observation behind it.
    public override bool evaluate(Blackboard state) =>
        state.canSeeLever && NodeTargets.PreferredLever(state) != null;
}

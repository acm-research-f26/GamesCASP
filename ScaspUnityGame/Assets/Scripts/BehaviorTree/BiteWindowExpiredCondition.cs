public sealed class BiteWindowExpiredCondition : ConditionNode
{
    public BiteWindowExpiredCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }

    // Here we check if the bite window ran out, the Prolog rules defeat every action then
    public override bool evaluate(Blackboard state) => state.BiteWindowExpired;
}

// A healthy agent avoids a witnessed infected person before ordinary objectives.
public sealed class KnownHalfZombieVisibleCondition : ConditionNode
{
    public KnownHalfZombieVisibleCondition(Node leftNode, Node rightNode) : base(leftNode, rightNode) { }
    public override bool evaluate(Blackboard state) => NodeTargets.KnownHalfZombieThreat(state) != null;
}

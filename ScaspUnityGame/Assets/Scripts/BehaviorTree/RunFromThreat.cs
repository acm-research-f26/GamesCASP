public sealed class RunFromThreat : ActionNode
{
    public override (string action, string target) execute(Blackboard state)
    {
        Fact threat = NodeTargets.KnownHalfZombieThreat(state);
        Fact zombie = NodeTargets.DangerZombie(state);
        if (zombie != null && (threat == null || zombie.Distance.Value <= threat.Distance.Value))
            threat = zombie;

        return threat == null ? ("", "") : ("run_from", threat.SubjectId);
    }
}

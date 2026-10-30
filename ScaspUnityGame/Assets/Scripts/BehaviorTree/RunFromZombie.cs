public sealed class RunFromZombie : ActionNode
{
    public override (string action, string target) execute(Blackboard state)
    {
        Fact zombie = NodeTargets.DangerZombie(state);
        if (zombie == null) return ("", "");
        return ("run_from", zombie.SubjectId);
    }
}

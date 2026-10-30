public sealed class MoveToLever : ActionNode
{
    public override (string action, string target) execute(Blackboard state)
    {
        Fact lever = NodeTargets.PreferredLever(state);
        if (lever == null || lever.Distance.Value <= 1f) return ("", "");
        return ("move_to_lever", lever.SubjectId);
    }
}

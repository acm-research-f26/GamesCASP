public sealed class PullLever : ActionNode
{
    public override (string action, string target) execute(Blackboard state)
    {
        Fact lever = NodeTargets.PreferredLever(state);
        if (lever == null || lever.Distance.Value > 1f) return ("", "");
        // Priority comes from the path to this leaf, including the final-lever override.
        return ("pull_lever", lever.SubjectId);
    }
}

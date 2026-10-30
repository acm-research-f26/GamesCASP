public sealed class NoEligibleAction : ActionNode
{
    // Empty action makes ActionNode.Tick return false; this is not the wait action.
    public override (string action, string target) execute(Blackboard state) => ("", "");
}

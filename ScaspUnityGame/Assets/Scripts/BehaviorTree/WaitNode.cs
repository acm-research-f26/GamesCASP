public sealed class WaitNode : ActionNode
{
    public override (string action, string target) execute(Blackboard state) => ("wait", "");
}

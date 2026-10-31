public sealed class HuntLever : ActionNode
{
    public override (string action, string target) execute(Blackboard state) => ("hunt_lever", "");
}

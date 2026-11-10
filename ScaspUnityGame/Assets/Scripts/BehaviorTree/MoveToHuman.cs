public sealed class MoveToHuman : ActionNode
{
    // Move toward the human if we arent close enough
    // AiAction does the actual movement, the same move_to_human s(CASP) sends
    public override (string action, string target) execute(Blackboard state)
    {
        Fact human = NodeTargets.PreferredHuman(state);
        if (human == null) return ("", "");
        return ("move_to_human", human.SubjectId);
    }
}

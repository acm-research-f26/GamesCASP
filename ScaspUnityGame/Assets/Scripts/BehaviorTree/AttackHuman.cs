public sealed class AttackHuman : ActionNode
{
    // Try to bite the human, same attack_human s(CASP) sends
    // AiAction checks the range and cooldown and the bite mechanic decides if it worked
    public override (string action, string target) execute(Blackboard state)
    {
        Fact human = NodeTargets.PreferredHuman(state);
        if (human == null) return ("", "");
        return ("attack_human", human.SubjectId);
    }
}

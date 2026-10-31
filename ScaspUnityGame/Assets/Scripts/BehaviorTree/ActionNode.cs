using System;

public abstract class ActionNode : Node
{
    public abstract (string action, string target) execute(Blackboard state);

    public override bool Tick(Blackboard state,
        out (string action, string target) decision)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        // Call once. Success means a decision was selected, not completed in-world.
        decision = execute(state);
        decision.target = decision.target ?? "";
        return !string.IsNullOrWhiteSpace(decision.action);
    }
}

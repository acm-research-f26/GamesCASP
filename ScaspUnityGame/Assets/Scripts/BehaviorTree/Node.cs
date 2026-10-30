// Nodes select a decision synchronously. AiAction performs it afterward.
public abstract class Node
{
    public abstract bool Tick(
        Blackboard state,
        out (string action, string target) decision);
}

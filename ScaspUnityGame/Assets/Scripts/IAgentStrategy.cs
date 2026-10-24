// One backend per agent handles stored facts and action selection.
public interface IAgentStrategy : IFactStrategy
{
    // Choose an action, then call agent.ExecuteAction(action, target).
    // Async backends may call it after their response arrives.
    void DecideNextAction(Agent_Parent_Script agent);
}

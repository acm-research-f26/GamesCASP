using UnityEngine;
using UnityEngine.Serialization;

public class Agent_Parent_Script : MonoBehaviour
{
    [Tooltip("This agent's AI component. Must implement IAgentStrategy.")]
    [FormerlySerializedAs("factStrategyComponent")]
    [SerializeField] private MonoBehaviour strategyComponent;
    private IAgentStrategy runtimeStrategy;

    // Supports strategies created by code as well as components assigned in the Inspector.
    public void SetStrategy(IAgentStrategy strategy)
    {
        runtimeStrategy = strategy ?? throw new System.ArgumentNullException(nameof(strategy));
    }

    private IAgentStrategy Strategy =>
        runtimeStrategy ?? (strategyComponent as IAgentStrategy);

    // The caller controls decision timing. Asynchronous backends may execute later.
    public void RequestDecision()
    {
        var strategy = Strategy;
        if (strategy == null)
        {
            Debug.LogWarning($"{name}: Assign an AI component implementing IAgentStrategy.", this);
            return;
        }

        strategy.DecideNextAction(this);
    }

    public bool TryStoreFact(Fact fact)
    {
        if (fact == null)
            throw new System.ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new System.ArgumentException("Transient facts must be queried, not stored.", nameof(fact));

        var strategy = Strategy;
        if (strategy == null)
        {
            Debug.LogWarning($"{name}: Assign an AI component implementing IAgentStrategy.", this);
            return false;
        }

        strategy.StoreFact(fact);
        return true;
    }

    // Delegate action selection over to AIStrategy script
    public virtual void ExecuteAction(string action, string target = "")
    {
        if (aiStrategy == null)
        {
            Debug.LogWarning($"{name}: No AI strategy assigned.", this);
            return;
        }

        // Let AI Strategy execute actions for the NPC
        aiStrategy.ExecuteAction(action, target);
    }
}

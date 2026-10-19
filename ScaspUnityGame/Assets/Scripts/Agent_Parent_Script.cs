using UnityEngine;

public class Agent_Parent_Script : MonoBehaviour
{
    [Tooltip("This agent's AI strategy component.")]
    [SerializeField] private AIStrategy aiStrategy;

    [Tooltip("This agent's fact strategy component. Must implement IFactStrategy.")]
    [SerializeField] private MonoBehaviour factStrategyComponent;

    private IFactStrategy runtimeFactStrategy;

    // Supports strategies created by code as well as components assigned in the Inspector
    public void SetFactStrategy(IFactStrategy strategy)
    {
        runtimeFactStrategy = strategy ?? throw new System.ArgumentNullException(nameof(strategy));
    }

    public bool TryStoreFact(Fact fact)
    {
        if (fact == null)
            throw new System.ArgumentNullException(nameof(fact));

        if (fact.Lifetime == FactLifetime.Transient)
            throw new System.ArgumentException("Transient facts must be queried, not stored.", nameof(fact));

        var strategy = runtimeFactStrategy ?? (factStrategyComponent as IFactStrategy);

        if (strategy == null)
        {
            Debug.LogWarning($"{name}: Assign an AI component implementing IFactStrategy before receiving facts.", this);
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

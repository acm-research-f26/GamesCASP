using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(AiAction))]

public class Agent_Parent_Script : MonoBehaviour
{
    [Header("AI Strategy")]
    [Tooltip("Must implement IAgentStrategy.")]
    [FormerlySerializedAs("factStrategyComponent")]
    [SerializeField] private MonoBehaviour strategyComponent;


    private IAgentStrategy runtimeStrategy;
    private AiAction aiAction;
    private IAgentStrategy Strategy => runtimeStrategy ?? (strategyComponent as IAgentStrategy) ?? GetComponent<IAgentStrategy>();

    [HideInInspector]
    public string stratName;


    private void Awake()
    {
        aiAction = GetComponent<AiAction>();
    }


    // Supports strategies created by code as well as components assigned in the Inspector.
    public void SetStrategy(IAgentStrategy strategy, string strategyName)
    {
        runtimeStrategy = strategy ?? throw new System.ArgumentNullException(nameof(strategy));
        stratName = strategyName;
    }

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

    public System.Collections.Generic.IReadOnlyList<Fact> FetchTransientFacts()
    {
        var strategy = Strategy;
        if (strategy == null)
        {
            Debug.LogWarning($"{name}: Assign an AI component implementing IAgentStrategy.", this);
            return System.Array.Empty<Fact>();
        }

        // Copy the backend result so an earlier query remains a stable snapshot.
        var facts = aiAction.FetchTransientFacts();
        if (facts == null)
            throw new System.InvalidOperationException("Strategies must return an empty collection, not null.");
        var snapshot = new System.Collections.Generic.List<Fact>(facts);
        foreach (var fact in snapshot)
            if (fact == null || fact.Lifetime != FactLifetime.Transient)
                throw new System.InvalidOperationException("Transient queries must return only transient facts.");
        return snapshot.AsReadOnly();
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

    // AiAction handles NPC actions now
    public virtual void ExecuteAction(string action, string target = "")
    {
        aiAction.Execute(action, target);
    }
}

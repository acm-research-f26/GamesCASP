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
    // The strategy that currently receives decisions and facts, or null when none/ambiguous.
    public IAgentStrategy ActiveStrategy => ResolveStrategy(out _);

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

        // Only the selected strategy component stays enabled, so a deselected backend
        // cannot keep acting on this agent (for example, a late s(CASP) response).
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            if (component is IAgentStrategy && !ReferenceEquals(component, strategy))
                component.enabled = false;
        if (strategy is MonoBehaviour selected && selected.gameObject == gameObject)
            selected.enabled = true;
    }

    // Deterministic order: SetStrategy, then the Inspector field, then the only enabled
    // strategy component. Several enabled candidates without an explicit choice is an error.
    private IAgentStrategy ResolveStrategy(out string problem)
    {
        problem = null;
        if (runtimeStrategy != null && !(runtimeStrategy is Object destroyed && destroyed == null))
            return runtimeStrategy;

        if (strategyComponent != null)
            return strategyComponent as IAgentStrategy;

        IAgentStrategy found = null;
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
        {
            if (!(component is IAgentStrategy candidate) || !component.enabled)
                continue;
            if (found != null)
            {
                problem = "Several enabled IAgentStrategy components; assign Strategy Component or call SetStrategy.";
                return null;
            }
            found = candidate;
        }
        return found;
    }

    private IAgentStrategy RequireStrategy()
    {
        var strategy = ResolveStrategy(out string problem);
        if (strategy == null)
            Debug.LogWarning($"{name}: {problem ?? "Assign an AI component implementing IAgentStrategy."}", this);
        return strategy;
    }

    // The caller controls decision timing. Asynchronous backends may execute later.
    public void RequestDecision()
    {
        var strategy = RequireStrategy();
        if (strategy == null)
            return;

        strategy.DecideNextAction(this);
    }

    public System.Collections.Generic.IReadOnlyList<Fact> FetchTransientFacts()
    {
        var strategy = RequireStrategy();
        if (strategy == null)
            return System.Array.Empty<Fact>();

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

        var strategy = RequireStrategy();
        if (strategy == null)
            return false;

        // In experiments we time how long the tree takes to store a new fact
        // s(CASP) gets its fact time from Python instead, and replayed memories never come through here
        var recorder = strategy is BehaviorTree ? ExperimentRunner.ActiveMeasurements : null;
        var clock = recorder != null ? System.Diagnostics.Stopwatch.StartNew() : null;

        strategy.StoreFact(fact);

        // Save how long it took, we only get here if storing worked
        if (recorder != null)
            recorder.AddBehaviorTreeFactLatency(clock.Elapsed.TotalMilliseconds);
        return true;
    }

    // AiAction handles NPC actions now
    public virtual void ExecuteAction(string action, string target = "")
    {
        aiAction.Execute(action, target);
    }
}

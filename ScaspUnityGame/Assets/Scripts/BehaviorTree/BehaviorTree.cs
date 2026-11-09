using System;
using System.Collections.Generic;
using UnityEngine;

// One strategy component per agent.
public sealed class BehaviorTree : MonoBehaviour, IAgentStrategy
{
    private Agent owner;
    private AiAction aiAction;
    private Blackboard state;
    private readonly BarricadeObservationBuffer barricades = new BarricadeObservationBuffer();
    private Node root;

    public Blackboard State => state;
    public (string action, string target) LastDecision { get; private set; }

    // halfZombieBehavior adds the half zombie branch, PlayerManager only turns it on in experiments
    public void SetInitialValues(AiAction aiAction, LeverManager leverManager, bool halfZombieBehavior = false)
    {
        if (aiAction == null) throw new ArgumentNullException(nameof(aiAction));
        if (leverManager == null) throw new ArgumentNullException(nameof(leverManager));
        this.aiAction = aiAction;
        owner = aiAction.GetComponent<Agent>();
        if (owner == null) throw new ArgumentException("AiAction must belong to an Agent.", nameof(aiAction));

        state = new Blackboard(owner, leverManager);
        state.RefreshSearchState(aiAction);
        root = BehaviorTreeFactory.Build(barricades, halfZombieBehavior);
        LastDecision = ("", "");
    }

    private void OnEnable()
    {
        var agent = GetComponent<Agent>();
        if (agent != null)
            agent.BecameHalfZombie += RecordInfection;
    }

    private void OnDisable()
    {
        var agent = GetComponent<Agent>();
        if (agent != null)
            agent.BecameHalfZombie -= RecordInfection;
    }

    private void RecordInfection(Agent agent)
    {
        if (agent == owner)
            state?.RecordInfection(agent.BiteWindowStartedAt);
    }

    private void Update()
    {
        // The decision loop skips expired agents. Stop any outstanding tree command.
        if (owner != null && owner.BiteWindowExpired && aiAction != null &&
            !string.IsNullOrEmpty(aiAction.CurrentAction) && ReferenceEquals(owner.ActiveStrategy, this))
            aiAction.StopAllActions();
    }

    // Trees selected in the Inspector are not initialized by PlayerManager.
    private void EnsureInitialized()
    {
        if (state == null)
            SetInitialValues(GetComponent<AiAction>(), FindAnyObjectByType<LeverManager>());
    }

    // Main's interfaces retain their existing names. Your public API is available too.
    public void StoreFact(Fact fact) => storeFact(fact);
    public void storeFact(Fact fact)
    {
        EnsureInitialized();
        state.update(fact);
    }

    public IReadOnlyList<Fact> FetchTransientFacts()
    {
        EnsureInitialized();
        return aiAction.FetchTransientFacts();
    }

    // Select only. The returned decision stays in this strategy, not the blackboard.
    public (string action, string target) getNextAction()
    {
        EnsureInitialized();
        state.RefreshPersistentState();
        state.RefreshSearchState(aiAction);
        try
        {
            state.updateTransientFacts(FetchTransientFacts());
            barricades.Refresh(aiAction, state);
            root.Tick(state, out var decision);
            LastDecision = decision;
            return decision;
        }
        finally
        {
            state.clearTransientState();
            barricades.Clear(state);
        }
    }

    // Called by agent.RequestDecision() through IAgentStrategy.
    public void DecideNextAction(Agent_Parent_Script agent)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));
        EnsureInitialized();
        if (agent != owner) throw new ArgumentException("This tree belongs to another agent.", nameof(agent));

        var decision = getNextAction();
        if (!string.IsNullOrWhiteSpace(decision.action))
        {
            agent.ExecuteAction(decision.action, decision.target ?? "");
        }
        else
        {
            // No eligible decision is different from wait, but old movement must stop.
            aiAction.StopAllActions();
        }
        state.RefreshSearchState(aiAction);
    }
}

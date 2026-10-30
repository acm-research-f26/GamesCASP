using System;
using System.Collections.Generic;
using UnityEngine;

// Plain C# strategy, one instance per agent. Do not attach it as a component.
public sealed class BehaviorTree : IAgentStrategy
{
    private readonly Agent owner;
    private readonly AiAction aiAction;
    private readonly Blackboard state;
    private readonly BarricadeObservationBuffer barricades = new BarricadeObservationBuffer();
    private readonly Node root;

    public Blackboard State => state;
    public (string action, string target) LastDecision { get; private set; }

    public BehaviorTree(AiAction aiAction, LeverManager leverManager)
    {
        if (aiAction == null) throw new ArgumentNullException(nameof(aiAction));
        if (leverManager == null) throw new ArgumentNullException(nameof(leverManager));
        this.aiAction = aiAction;
        owner = aiAction.GetComponent<Agent>();
        if (owner == null) throw new ArgumentException("AiAction must belong to an Agent.", nameof(aiAction));

        state = new Blackboard(owner, leverManager);
        state.RefreshSearchState(aiAction);
        root = BehaviorTreeFactory.Build(barricades);
        LastDecision = ("", "");
    }

    // Main's interfaces retain their existing names. Your public API is available too.
    public void StoreFact(Fact fact) => storeFact(fact);
    public void storeFact(Fact fact) => state.update(fact);

    public IReadOnlyList<Fact> FetchTransientFacts() => aiAction.FetchTransientFacts();

    // Select only. The returned decision stays in this strategy, not the blackboard.
    public (string action, string target) getNextAction()
    {
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
        if (agent != owner) throw new ArgumentException("This tree belongs to another agent.", nameof(agent));

        var decision = getNextAction();
        if (!string.IsNullOrWhiteSpace(decision.action))
        {
            agent.ExecuteAction(decision.action, decision.target ?? "");
        }
        else if (owner.Health <= 0)
        {
            // No eligible decision is different from wait, but old movement must stop.
            aiAction.StopAllActions();
        }
        state.RefreshSearchState(aiAction);
    }
}

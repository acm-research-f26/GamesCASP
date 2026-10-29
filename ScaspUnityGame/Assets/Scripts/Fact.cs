using System;
using System.Collections.Generic;

public enum FactType
{
    LeverActivated,
    AgentAttacked,
    ZombieSeen,
    LeverSearchStarted,
    LeverSearchStopped,
    BarricadeSearchStarted,
    BarricadeSearchStopped
}

public enum FactLifetime
{
    Transient,
    Mutable,
    Permanent
}

// Backend-independent data. Each agent owns the facts it knows.
public sealed class Fact
{
    public FactType Type { get; }
    public FactLifetime Lifetime { get; }
    public string SubjectId { get; }
    public string TargetId { get; }
    public float? Distance { get; }

    public Fact(FactType type, FactLifetime lifetime, string subjectId, string targetId = null,
        float? distance = null)
    {
        if (!Enum.IsDefined(typeof(FactType), type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (!Enum.IsDefined(typeof(FactLifetime), lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A fact needs a subject ID.", nameof(subjectId));
        if (distance.HasValue && (float.IsNaN(distance.Value) || float.IsInfinity(distance.Value) || distance.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(distance));

        Type = type;
        Lifetime = lifetime;
        SubjectId = subjectId;
        TargetId = targetId;
        Distance = distance;
    }

    // Explicit selection avoids scene-wide scans and preserves who observed each fact.
    public static IReadOnlyDictionary<Agent_Parent_Script, IReadOnlyList<Fact>> FetchTransientFacts(
        IEnumerable<Agent_Parent_Script> requestedAgents)
    {
        if (requestedAgents == null)
            throw new ArgumentNullException(nameof(requestedAgents));

        var results = new Dictionary<Agent_Parent_Script, IReadOnlyList<Fact>>();
        foreach (var agent in requestedAgents)
        {
            // Destroyed Unity objects compare equal to null. Repeated requests query once.
            if (agent != null && !results.ContainsKey(agent))
                results.Add(agent, agent.FetchTransientFacts());
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<Agent_Parent_Script, IReadOnlyList<Fact>>(results);
    }
}

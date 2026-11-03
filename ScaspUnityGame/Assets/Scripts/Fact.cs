using System;
using System.Collections.Generic;

public enum FactType
{
    LeverActivated,
    AgentAttacked,
    ZombieSeen,
    LeverSeen,
    LeverSearchStarted,
    LeverSearchStopped,
    BarricadeSearchStarted,
    BarricadeSearchStopped,
    BarricadeSeen,
    // Transient: another living agent in perception range. Never carries infection status.
    HumanSeen,
    // Permanent witnessed events. Subject is the attacker, target is the victim.
    ZombieAttackWitnessed,
    BiteAttemptWitnessed, // attempt only, not proof that the bite succeeded
    BiteWitnessed // gameplay confirmed a successful infectious bite
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
    public float? NearestZombieDistance { get; }
    public float? Reinforcement { get; }
    public IReadOnlyDictionary<string, float> ZombieDistances { get; }
    
    public bool? IsOn { get; }
    public int? Health { get; }

    public Fact(FactType type, FactLifetime lifetime, string subjectId, string targetId = null,
        float? distance = null, float? nearestZombieDistance = null, bool? isOn = null,
        float? reinforcement = null, IReadOnlyDictionary<string, float> zombieDistances = null, int? health = null)
    {
        if (!Enum.IsDefined(typeof(FactType), type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (!Enum.IsDefined(typeof(FactLifetime), lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A fact needs a subject ID.", nameof(subjectId));
        if (distance.HasValue && (float.IsNaN(distance.Value) || float.IsInfinity(distance.Value) || distance.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(distance));

        // Infinity is meaningful here: no zombie is present anywhere in the snapshot.
        if (nearestZombieDistance.HasValue &&
            (float.IsNaN(nearestZombieDistance.Value) || nearestZombieDistance.Value < 0f))
            throw new ArgumentOutOfRangeException(nameof(nearestZombieDistance));
        if (health.HasValue && health.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(health));

        if (reinforcement.HasValue && (float.IsNaN(reinforcement.Value) ||
            float.IsInfinity(reinforcement.Value) || reinforcement.Value < 0f))
            throw new ArgumentOutOfRangeException(nameof(reinforcement));
        var distances = new Dictionary<string, float>();
        if (zombieDistances != null)
            foreach (var pair in zombieDistances)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || float.IsNaN(pair.Value) ||
                    float.IsInfinity(pair.Value) || pair.Value < 0f)
                    throw new ArgumentException("Zombie distances need an ID and finite nonnegative distance.", nameof(zombieDistances));
                distances.Add(pair.Key, pair.Value);
            }
        ZombieDistances = new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(distances);
        Reinforcement = reinforcement;
        NearestZombieDistance = nearestZombieDistance;
        Health = health;
        Type = type;
        Lifetime = lifetime;
        SubjectId = subjectId;
        TargetId = targetId;
        Distance = distance;
        IsOn = isOn;
    }

    // Explicit selection avoids scene-wide scans and preserves who observed each fact.
    public static IReadOnlyDictionary<Agent_Parent_Script, IReadOnlyList<Fact>> FetchAllTransientFacts(
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

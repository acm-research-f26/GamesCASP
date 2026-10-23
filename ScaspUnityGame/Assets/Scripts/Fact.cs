using System;

public enum FactType
{
    LeverActivated,
    AgentAttacked,
    ZombieSeen
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

    public Fact(FactType type, FactLifetime lifetime, string subjectId, string targetId = null)
    {
        if (!Enum.IsDefined(typeof(FactType), type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (!Enum.IsDefined(typeof(FactLifetime), lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A fact needs a subject ID.", nameof(subjectId));

        Type = type;
        Lifetime = lifetime;
        SubjectId = subjectId;
        TargetId = targetId;
    }
}

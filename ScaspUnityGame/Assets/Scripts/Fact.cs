
// not an actual game object, this is for our backend



using System;

public enum FactType
{
    LeverActivated,
    AgentAttacked,
    ZombieSeen
}

// Backend-independent data. Each agent owns the facts it knows.
public sealed class Fact
{
    public FactType Type { get; }
    public string SubjectId { get; }
    public string TargetId { get; }

    public Fact(FactType type, string subjectId, string targetId = null)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A fact needs a subject ID.", nameof(subjectId));

        Type = type;
        SubjectId = subjectId;
        TargetId = targetId;
    }
}

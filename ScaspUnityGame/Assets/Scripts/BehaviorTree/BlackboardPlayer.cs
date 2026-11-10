using System;

[Flags]
public enum InfectionEvidence
{
    None = 0,
    ZombieAttack = 1,
    BiteAttempt = 2,
    ConfirmedBiter = 4,
    ConfirmedVictim = 8
}

// Knowledge belongs to ONE observer. Trust means "no infection evidence yet",
// never a proof of health or a read of another Agent's private infection flag.
public sealed class BlackboardPlayer
{
    public string PlayerId { get; }
    public InfectionEvidence Evidence { get; private set; }
    public bool Trust => Evidence == InfectionEvidence.None;
    public bool KnownHalfZombie => !Trust;

    public BlackboardPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("An ID is required.", nameof(playerId));
        PlayerId = playerId;
    }

    public void Observe(InfectionEvidence evidence) => Evidence |= evidence;
}

using System;

// Shared queries prevent a condition and its action from choosing different targets.
public static class NodeTargets
{
    public static Fact PreferredLever(Blackboard state)
    {
        // Reuse YOUR existing safe/threatened/distance ranking implementation.
        Fact lever = state.getPreferredVisibleLever();
        return HasUsableDistanceAndId(lever) ? lever : null;
    }

    public static Fact DangerZombie(Blackboard state)
    {
        Fact preferred = null;
        foreach (Fact zombie in state.visibleZombies)
        {
            if (!HasUsableDistanceAndId(zombie) || zombie.Distance.Value > 2f) continue;
            // Any immediate-danger zombie is valid in the supplied Prolog policy.
            // Choose the closest; ID order resolves equal-distance observations.
            if (preferred == null || zombie.Distance.Value < preferred.Distance.Value ||
                (zombie.Distance.Value == preferred.Distance.Value &&
                 StringComparer.Ordinal.Compare(zombie.SubjectId, preferred.SubjectId) < 0))
                preferred = zombie;
        }
        return preferred;
    }

    // Pick the nearest living person the observer has no evidence is infected.
    // Keep the first observed target when two distances are equal.
    public static Fact PreferredHuman(Blackboard state)
    {
        Fact preferred = null;
        foreach (Fact human in state.visibleHumans)
        {
            // Skip anything we cant use and never target ourselves
            if (!HasUsableDistanceAndId(human) || human.SubjectId == state.OwnerId) continue;
            // Dead humans are not targets
            if (!human.Health.HasValue || human.Health.Value <= 0 || state.KnowsHalfZombie(human.SubjectId))
                continue;

            if (preferred == null || human.Distance.Value < preferred.Distance.Value)
                preferred = human;
        }
        return preferred;
    }

    public static Fact KnownHalfZombieThreat(Blackboard state)
    {
        if (state.HalfZombie) return null;

        Fact preferred = null;
        foreach (Fact human in state.visibleHumans)
        {
            if (!HasUsableDistanceAndId(human) || (human.Health ?? 0) <= 0 ||
                human.Distance.Value > state.visionRadius || !state.KnowsHalfZombie(human.SubjectId))
                continue;

            if (preferred == null || human.Distance.Value < preferred.Distance.Value)
                preferred = human;
        }
        return preferred;
    }

    private static bool HasUsableDistanceAndId(Fact fact) =>
        fact != null && !string.IsNullOrWhiteSpace(fact.SubjectId) &&
        fact.Distance.HasValue && fact.Distance.Value >= 0f &&
        !float.IsNaN(fact.Distance.Value) && !float.IsInfinity(fact.Distance.Value);
}

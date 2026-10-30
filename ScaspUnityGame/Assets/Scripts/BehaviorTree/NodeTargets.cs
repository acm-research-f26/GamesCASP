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

    private static bool HasUsableDistanceAndId(Fact fact) =>
        fact != null && !string.IsNullOrWhiteSpace(fact.SubjectId) &&
        fact.Distance.HasValue && fact.Distance.Value >= 0f &&
        !float.IsNaN(fact.Distance.Value) && !float.IsInfinity(fact.Distance.Value);
}

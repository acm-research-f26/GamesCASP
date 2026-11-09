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

    // Get the closest valid human target, same as preferred_human_target in half_zombie_rules.pl
    // human_target needs a visible human that isnt us, has health above 0 and a distance of 0 or more
    // preferred means no other target is strictly closer, so on a tie we keep the first one we saw
    // s(CASP) gets the humans in this same order and Python takes its first model, so ties should match too
    public static Fact PreferredHuman(Blackboard state)
    {
        Fact preferred = null;
        foreach (Fact human in state.visibleHumans)
        {
            // Skip anything we cant use and never target ourselves
            if (!HasUsableDistanceAndId(human) || human.SubjectId == state.OwnerId) continue;
            // Dead humans are not targets
            if (!human.Health.HasValue || human.Health.Value <= 0) continue;

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

using System;
using System.Collections.Generic;
using UnityEngine;

// Create one buffer per agent/tree. No Blackboard.cs changes are required.
public sealed class BarricadeObservationBuffer
{
    private readonly List<BarricadeObservation> observations = new List<BarricadeObservation>();
    public IReadOnlyList<BarricadeObservation> Observations => observations.AsReadOnly();

    // Useful for supplying a prebuilt snapshot as well as for isolated checks.
    public void Replace(IEnumerable<BarricadeObservation> current)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        var replacement = new List<BarricadeObservation>(current);
        if (replacement.Exists(item => item == null))
            throw new ArgumentException("Observations cannot contain null.", nameof(current));
        observations.Clear();
        observations.AddRange(replacement);
    }

    public void Clear(Blackboard state)
    {
        observations.Clear();
        state.canSeeBarricade = false;
    }

    // Call AFTER aiAction.FetchTransientFacts() and state.updateTransientFacts().
    // That query already refreshed detectedObjects; do not refresh during traversal.
    public void Refresh(AiAction aiAction, Blackboard state)
    {
        if (aiAction == null) throw new ArgumentNullException(nameof(aiAction));
        if (state == null) throw new ArgumentNullException(nameof(state));
        Clear(state);

        var windows = new HashSet<Window>();
        var zombies = new HashSet<Zombie>();
        var knownZombieIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Fact fact in state.visibleZombies)
            if (fact != null) knownZombieIds.Add(fact.SubjectId);

        foreach (AiAction.DetectedObject detected in aiAction.detectedObjects)
        {
            if (detected.obj == null) continue;
            Window window = detected.obj.GetComponentInParent<Window>();
            if (window != null && window.isActiveAndEnabled) windows.Add(window);
            Zombie zombie = detected.obj.GetComponentInParent<Zombie>();
            if (zombie != null && zombie.isActiveAndEnabled && knownZombieIds.Contains(zombie.gameObject.name))
                zombies.Add(zombie);
        }

        foreach (Window window in windows)
        {
            float nearestZombie = float.PositiveInfinity;
            foreach (Zombie zombie in zombies)
                nearestZombie = Mathf.Min(nearestZombie,
                    Vector2.Distance(window.transform.position, zombie.transform.position));
            observations.Add(new BarricadeObservation(
                window.gameObject.name,
                Vector2.Distance(aiAction.transform.position, window.transform.position),
                window.health,
                nearestZombie));
        }
        state.canSeeBarricade = observations.Count > 0;
    }

    public BarricadeObservation GetPreferredUrgentBarricade()
    {
        BarricadeObservation preferred = null;
        foreach (BarricadeObservation candidate in observations)
        {
            if (!candidate.IsUrgent) continue;
            if (preferred == null || candidate.Reinforcement < preferred.Reinforcement ||
                (candidate.Reinforcement == preferred.Reinforcement &&
                 (candidate.Distance < preferred.Distance ||
                  (candidate.Distance == preferred.Distance &&
                   StringComparer.Ordinal.Compare(candidate.Id, preferred.Id) < 0))))
                preferred = candidate;
        }
        return preferred;
    }
}

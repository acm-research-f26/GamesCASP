using System;

// Supplemental observation data missing from the attached Blackboard/Fact schema.
// This records facts about the world, never a selected action or selected target.
public sealed class BarricadeObservation
{
    public string Id { get; }
    public float Distance { get; }
    public float Reinforcement { get; }
    public float NearestKnownZombieDistance { get; }
    public bool IsUrgent => NearestKnownZombieDistance <= 1f;

    public BarricadeObservation(string id, float distance, float reinforcement,
        float nearestKnownZombieDistance)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An ID is required.", nameof(id));
        if (!FiniteNonnegative(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        if (!FiniteNonnegative(reinforcement)) throw new ArgumentOutOfRangeException(nameof(reinforcement));
        // Positive infinity means that no known zombie was found.
        if (float.IsNaN(nearestKnownZombieDistance) || nearestKnownZombieDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(nearestKnownZombieDistance));
        Id = id;
        Distance = distance;
        Reinforcement = reinforcement;
        NearestKnownZombieDistance = nearestKnownZombieDistance;
    }

    private static bool FiniteNonnegative(float value) =>
        value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

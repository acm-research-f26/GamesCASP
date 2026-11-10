using System;

// The integration contract for issue #55's measurement work. Null means not
// collected; zero is a real measurement and must participate in averages.
public sealed class ExperimentMetrics
{
    // Each latency is already the average for ONE configuration run, in ms.
    public double? ScaspFactLatencyMs;
    public double? ScaspActionRoundTripLatencyMs;
    public double? ScaspActionQueryLatencyMs;
    public double? BehaviorTreeFactLatencyMs;
    public double? BehaviorTreeActionLatencyMs;

    public ExperimentPopulation ScaspInnocents;
    public ExperimentPopulation ScaspHalfZombies;
    public ExperimentPopulation BehaviorTreeInnocents;
    public ExperimentPopulation BehaviorTreeHalfZombies;

    public ExperimentMetrics Copy() => (ExperimentMetrics)MemberwiseClone();

    public void Validate()
    {
        foreach (double? latency in new[] { ScaspFactLatencyMs, ScaspActionRoundTripLatencyMs,
            ScaspActionQueryLatencyMs, BehaviorTreeFactLatencyMs, BehaviorTreeActionLatencyMs })
            if (latency.HasValue && (double.IsNaN(latency.Value) || double.IsInfinity(latency.Value) || latency < 0))
                throw new ArgumentException("Latency must be missing or a finite, nonnegative number.");
        ScaspInnocents.Validate();
        ScaspHalfZombies.Validate();
        BehaviorTreeInnocents.Validate();
        BehaviorTreeHalfZombies.Validate();
    }
}

public struct ExperimentPopulation
{
    // T(i): unique NPCs ever controlled by this strategy in this role.
    public int? Total;
    // A(i): NPCs in that population alive/winning at the end of the run.
    public int? Alive;

    public ExperimentPopulation(int total, int alive) { Total = total; Alive = alive; Validate(); }

    public void Validate()
    {
        if (Total.HasValue != Alive.HasValue || Total < 0 || Alive < 0 || Alive > Total)
            throw new ArgumentException("Population requires both counts, with 0 <= alive <= total, or neither.");
    }
}

using System;

public sealed class HoldBarricade : ActionNode
{
    private readonly BarricadeObservationBuffer observations;

    public HoldBarricade(BarricadeObservationBuffer observations)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    public override (string action, string target) execute(Blackboard state)
    {
        BarricadeObservation barricade = observations.GetPreferredUrgentBarricade();
        if (barricade == null || barricade.Distance > 1f) return ("", "");
        return ("hold_barricade", barricade.Id);
    }
}

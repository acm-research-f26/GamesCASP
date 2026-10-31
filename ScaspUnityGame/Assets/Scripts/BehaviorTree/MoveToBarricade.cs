using System;

public sealed class MoveToBarricade : ActionNode
{
    private readonly BarricadeObservationBuffer observations;

    public MoveToBarricade(BarricadeObservationBuffer observations)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    public override (string action, string target) execute(Blackboard state)
    {
        BarricadeObservation barricade = observations.GetPreferredUrgentBarricade();
        if (barricade == null || barricade.Distance <= 1f) return ("", "");
        return ("move_to_barricade", barricade.Id);
    }
}

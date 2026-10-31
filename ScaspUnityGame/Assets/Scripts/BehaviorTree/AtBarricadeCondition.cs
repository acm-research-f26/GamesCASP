using System;

public sealed class AtBarricadeCondition : ConditionNode
{
    private readonly BarricadeObservationBuffer observations;

    public AtBarricadeCondition(Node leftNode, Node rightNode,
        BarricadeObservationBuffer observations) : base(leftNode, rightNode)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    public override bool evaluate(Blackboard state)
    {
        BarricadeObservation barricade = observations.GetPreferredUrgentBarricade();
        return barricade != null && barricade.Distance <= 1f;
    }
}

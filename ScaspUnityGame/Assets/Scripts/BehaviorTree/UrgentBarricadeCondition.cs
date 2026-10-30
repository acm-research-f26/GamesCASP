using System;

public sealed class UrgentBarricadeCondition : ConditionNode
{
    private readonly BarricadeObservationBuffer observations;

    public UrgentBarricadeCondition(Node leftNode, Node rightNode,
        BarricadeObservationBuffer observations) : base(leftNode, rightNode)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    // canSeeBarricade is necessary but not sufficient: a known zombie must be at it.
    public override bool evaluate(Blackboard state) =>
        state.canSeeBarricade && observations.GetPreferredUrgentBarricade() != null;
}

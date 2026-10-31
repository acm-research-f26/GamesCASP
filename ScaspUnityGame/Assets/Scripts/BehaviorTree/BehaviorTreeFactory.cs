using System;

public static class BehaviorTreeFactory
{
    public static Node Build(BarricadeObservationBuffer barricades)
    {
        if (barricades == null) throw new ArgumentNullException(nameof(barricades));

        // Condition constructor order is always FALSE child, TRUE child.
        Node leverDistance = new AtLeverCondition(
            new MoveToLever(), new PullLever());

        Node noVisibleLever = new MoreLeversNeededCondition(
            new WaitNode(), new HuntLever());

        Node normalLeverBranch = new LeverVisibleCondition(
            noVisibleLever, leverDistance);

        Node barricadeDistance = new AtBarricadeCondition(
            new MoveToBarricade(barricades), new HoldBarricade(barricades), barricades);

        Node barricadeBranch = new UrgentBarricadeCondition(
            normalLeverBranch, barricadeDistance, barricades);

        Node normalPriorities = new ImmediateDangerCondition(
            barricadeBranch, new RunFromZombie());

        // Final objective overrides BOTH fleeing and barricade defense.
        // An unseen final lever is hunted, even when a zombie is nearby.
        Node finalLeverBranch = new LeverVisibleCondition(
            new HuntLever(), leverDistance);

        Node objectivePriority = new LastLeverCondition(
            normalPriorities, finalLeverBranch);

        return new CanActCondition(new NoEligibleAction(), objectivePriority);
    }
}

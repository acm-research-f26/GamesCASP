using System;

public static class BehaviorTreeFactory
{
    // halfZombieBehavior is only turned on for experiments so normal gameplay keeps the same tree
    public static Node Build(BarricadeObservationBuffer barricades, bool halfZombieBehavior = false)
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

        if (!halfZombieBehavior)
            return new CanActCondition(new NoEligibleAction(), objectivePriority);

        Node socialOrObjectives = new KnownHalfZombieVisibleCondition(objectivePriority, new RunFromThreat());
        Node biteOrApproach = new HumanTargetVisibleCondition(
            new NoEligibleAction(), new WithinBiteRangeCondition(new MoveToHuman(), new AttackHuman()));
        Node halfZombieStage = new CriticalBiteStageCondition(socialOrObjectives, biteOrApproach);

        Node livingTree = new CanActCondition(new NoEligibleAction(), halfZombieStage);
        return new BiteWindowExpiredCondition(livingTree, new NoEligibleAction());
    }
}

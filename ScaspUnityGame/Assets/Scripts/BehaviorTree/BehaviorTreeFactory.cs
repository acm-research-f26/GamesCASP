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

        Node innocentTree = new CanActCondition(new NoEligibleAction(), objectivePriority);
        if (!halfZombieBehavior)
            return innocentTree;

        // Half zombie part, copied from the baseline rules in half_zombie_timed_behavior.pl
        // In the critical stage only attack_human or move_to_human are allowed, with no target theres no action
        Node biteOrApproach = new HumanTargetVisibleCondition(
            new NoEligibleAction(), new WithinBiteRangeCondition(new MoveToHuman(), new AttackHuman()));

        // Still needs health like every other action
        Node criticalBranch = new CanActCondition(new NoEligibleAction(), biteOrApproach);

        // Before the critical stage a half zombie just plays the normal innocent tree
        Node halfZombieStage = new CriticalBiteStageCondition(innocentTree, criticalBranch);

        // An expired bite window blocks every action
        return new BiteWindowExpiredCondition(halfZombieStage, new NoEligibleAction());
    }
}

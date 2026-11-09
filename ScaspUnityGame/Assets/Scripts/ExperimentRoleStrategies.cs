using System;

// Which strategy controls each role in each experiment configuration
// Configuration 1 is s(CASP) innocents and Behavior Tree half zombies
// Configuration 2 swaps them so each strategy gets to play both roles
// There is no UnityEngine in here so Verify-Experiments.ps1 can test it without Unity
public static class ExperimentRoleStrategies
{
    // What goes in the CSV backend column, every experiment uses both backends split by role
    // The role_mapping column says which role used which one
    public const string BackendLabel = "scasp_and_behavior_tree";

    // Get the strategy the innocents use in this configuration
    public static ExperimentStrategy InnocentStrategy(int configuration)
    {
        RequireConfiguration(configuration);
        return configuration == 1 ? ExperimentStrategy.Scasp : ExperimentStrategy.BehaviorTree;
    }

    // Half zombies always get the other strategy
    public static ExperimentStrategy HalfZombieStrategy(int configuration)
    {
        RequireConfiguration(configuration);
        return configuration == 1 ? ExperimentStrategy.BehaviorTree : ExperimentStrategy.Scasp;
    }

    // Get the strategy for a role in this configuration
    public static ExperimentStrategy StrategyFor(ExperimentRole role, int configuration) =>
        role == ExperimentRole.Innocent ? InnocentStrategy(configuration) : HalfZombieStrategy(configuration);

    // The runner only ever has configuration 1 or 2
    private static void RequireConfiguration(int configuration)
    {
        if (configuration != 1 && configuration != 2)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Experiments only have configurations 1 and 2.");
    }
}

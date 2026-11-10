using System;

// Which strategy controls each role in each experiment configuration

// we say configuration 1 is s(CASP) innocents and behavior tree half zombies
// we say configuration 2 is s(CASP) innocents and GPT half zombies
// we say config 3 is behavior tree innocents and scasp half zombies
// we say config 4 is behavior tree innocents and gpt half zombies
// we say config 5 is gpt innocents and scasp half zombies
// we say config 6 is gpt innocents and behavior tree half zombies

// Configuration 1 is s(CASP) innocents and Behavior Tree half zombies
// Configuration 2 swaps them so each strategy gets to play both roles
// There is no UnityEngine in here so Verify-Experiments.ps1 can test it without Unity
public static class ExperimentRoleStrategies
{
    // What goes in the CSV backend column, every experiment uses both backends split by role
    // The role_mapping column says which role used which one
    public const string BackendLabel = "scasp_behavior_tree_chatgpt";

    // Get the strategy the innocents use in this configuration
    public static ExperimentStrategy InnocentStrategy(int configuration)
    {
        RequireConfiguration(configuration);
        switch(configuration)
        {
            case 1 or 2:
                return ExperimentStrategy.Scasp;
            case 3 or 4:
                return ExperimentStrategy.BehaviorTree;
            case 5 or 6:
                return ExperimentStrategy.ChatGPT;
            default:
                return ExperimentStrategy.ChatGPT;
        }
    }

    // Half zombies always get the other strategy
    public static ExperimentStrategy HalfZombieStrategy(int configuration)
    {
        RequireConfiguration(configuration);
        switch(configuration)
        {
            case 3 or 5:
                return ExperimentStrategy.Scasp;
            case 1 or 6:
                return ExperimentStrategy.BehaviorTree;
            case 2 or 4:
                return ExperimentStrategy.ChatGPT;
            default:
                return ExperimentStrategy.ChatGPT;
        }
    }

    // Get the strategy for a role in this configuration
    public static ExperimentStrategy StrategyFor(ExperimentRole role, int configuration) =>
        role == ExperimentRole.Innocent ? InnocentStrategy(configuration) : HalfZombieStrategy(configuration);

    // The runner only ever has configuration 1 or 2
    private static void RequireConfiguration(int configuration)
    {
        if (configuration < 1 || configuration > 6)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Experiments only have configurations 1 and 2.");
    }
}

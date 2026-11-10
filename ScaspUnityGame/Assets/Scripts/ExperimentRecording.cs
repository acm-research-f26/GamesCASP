using System.Collections.Generic;
using UnityEngine;

// Connects the part two recorder to the runner's lifecycle events
// Roles get recorded when NPCs spawn and when they get infected, and the results go into CurrentMetrics at the end
public static class ExperimentRecording
{
    public static void Attach(ExperimentRunner runner)
    {
        runner.RunStarted += RecordStartingRoles;
        runner.RunFinished += FinishRun;
    }

    // Every NPC spawns innocent, so record that and then watch for infections
    private static void RecordStartingRoles(ExperimentRunner run, PlayerManager players)
    {
        // Keep this run's recorder so an infection can only ever count toward this run
        ExperimentMeasurements recorder = run.CurrentMeasurements;
        foreach (Agent agent in players.players)
        {
            if (agent == null) continue;
            RecordCurrentRole(recorder, agent, ExperimentRole.Innocent);

            // PlayerManager subscribed first when it spawned the NPC, so it already switched strategy by the time this runs
            // MarkHalfZombie only fires this once per NPC and the recorder uses sets, so nobody gets counted twice
            agent.BecameHalfZombie += infected => RecordCurrentRole(recorder, infected, ExperimentRole.HalfZombie);
        }
    }

    // Record the role under the strategy that is actually in control right now
    private static void RecordCurrentRole(ExperimentMeasurements recorder, Agent agent, ExperimentRole role)
    {
        ExperimentStrategy? strategy = StrategyOf(agent.ActiveStrategy);
        if (strategy == null)
        {
            Debug.LogWarning($"[Experiment] {agent.AgentId} has no known strategy, its {role} role was not recorded.", agent);
            return;
        }
        recorder.RecordRole(agent.AgentId, strategy.Value, role);
    }

    // Get the strategy that is controlling an NPC
    public static ExperimentStrategy? StrategyOf(IAgentStrategy strategy)
    {
        if (strategy is ScaspStrategy) return ExperimentStrategy.Scasp;
        if (strategy is BehaviorTree) return ExperimentStrategy.BehaviorTree;
        return null;
    }

    // Add the final measurements to the CSV
    // RunFinished runs before the scene is torn down, so every NPC still has its final state here
    private static void FinishRun(ExperimentRunner run, ExperimentOutcome outcome, double seconds)
    {
        var players = Object.FindAnyObjectByType<PlayerManager>();
        var endStates = new List<ExperimentMeasurements.NpcEndState>();
        if (players != null)
            foreach (Agent agent in players.players)
                if (agent != null)
                    endStates.Add(new ExperimentMeasurements.NpcEndState(agent.AgentId, agent.Health, agent.IsHalfZombie, agent.BiteWindowExpired));

        // This also locks the recorder so any reply that shows up later is ignored
        run.CurrentMeasurements.FillMetrics(run.CurrentMetrics, endStates);
    }
}

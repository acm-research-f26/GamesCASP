using System;
using System.Globalization;
using System.IO;
using System.Linq;

// Checks for the part two recorder, runs from Unity or the standalone .NET check script
// Same setup as ExperimentMetricsVerification so both work the same way
public static class ExperimentMeasurementsVerification
{
    private static int checks;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Experiments/Verify Measurements")]
#endif
    public static void Run()
    {
        checks = 0;

        // Averages and missing samples
        var empty = new ExperimentMeasurements();
        var emptyMetrics = new ExperimentMetrics();
        empty.FillMetrics(emptyMetrics, Array.Empty<ExperimentMeasurements.NpcEndState>());
        Check(emptyMetrics.ScaspFactLatencyMs == null && emptyMetrics.ScaspActionRoundTripLatencyMs == null &&
              emptyMetrics.ScaspActionQueryLatencyMs == null && emptyMetrics.BehaviorTreeFactLatencyMs == null &&
              emptyMetrics.BehaviorTreeActionLatencyMs == null, "No samples stays missing, not 0 ms");
        Check(emptyMetrics.ScaspInnocents.Total == 0 && emptyMetrics.ScaspInnocents.Alive == 0 &&
              emptyMetrics.BehaviorTreeHalfZombies.Total == 0, "Empty populations are real zero counts");

        var zero = new ExperimentMeasurements();
        Check(zero.AddBehaviorTreeActionLatency(0), "A real 0 ms sample is kept");
        var zeroMetrics = new ExperimentMetrics();
        zero.FillMetrics(zeroMetrics, Array.Empty<ExperimentMeasurements.NpcEndState>());
        Check(zeroMetrics.BehaviorTreeActionLatencyMs == 0, "A real 0 ms sample becomes a 0 ms average");

        // Bad samples get thrown away
        var invalid = new ExperimentMeasurements();
        Check(!invalid.AddScaspFactLatency(double.NaN), "NaN sample rejected");
        Check(!invalid.AddScaspRoundTripLatency(double.PositiveInfinity), "Infinite sample rejected");
        Check(!invalid.AddScaspQueryLatency(-1), "Negative sample rejected");
        Check(invalid.AddScaspFactLatency(4), "Valid sample kept after rejected ones");
        var invalidMetrics = new ExperimentMetrics();
        invalid.FillMetrics(invalidMetrics, Array.Empty<ExperimentMeasurements.NpcEndState>());
        Check(invalidMetrics.ScaspFactLatencyMs == 4 && invalidMetrics.ScaspActionRoundTripLatencyMs == null &&
              invalidMetrics.ScaspActionQueryLatencyMs == null, "Rejected samples do not change any average");

        // Every strategy and category keeps its own samples
        var split = new ExperimentMeasurements();
        split.AddScaspFactLatency(2); split.AddScaspFactLatency(4);
        split.AddScaspRoundTripLatency(100); split.AddScaspRoundTripLatency(200);
        split.AddScaspQueryLatency(30);
        split.AddBehaviorTreeFactLatency(0.5);
        split.AddBehaviorTreeActionLatency(1); split.AddBehaviorTreeActionLatency(2); split.AddBehaviorTreeActionLatency(3);
        var splitMetrics = new ExperimentMetrics();
        split.FillMetrics(splitMetrics, Array.Empty<ExperimentMeasurements.NpcEndState>());
        Check(splitMetrics.ScaspFactLatencyMs == 3, "s(CASP) fact latency averaged alone");
        Check(splitMetrics.ScaspActionRoundTripLatencyMs == 150, "s(CASP) round trip averaged alone");
        Check(splitMetrics.ScaspActionQueryLatencyMs == 30, "s(CASP) query averaged alone");
        Check(splitMetrics.BehaviorTreeFactLatencyMs == 0.5, "Behavior Tree fact latency averaged alone");
        Check(splitMetrics.BehaviorTreeActionLatencyMs == 2, "Behavior Tree action latency averaged alone");

        // Configuration 1 style run, s(CASP) innocents and Behavior Tree half zombies
        var config1 = new ExperimentMeasurements();
        foreach (string id in new[] { "p1", "p2", "p3", "p4", "p5" })
            config1.RecordRole(id, ExperimentStrategy.Scasp, ExperimentRole.Innocent);
        // p2 gets hit by several infection events but should only count once
        for (int i = 0; i < 3; i++)
            config1.RecordRole("p2", ExperimentStrategy.BehaviorTree, ExperimentRole.HalfZombie);
        config1.RecordRole("p3", ExperimentStrategy.BehaviorTree, ExperimentRole.HalfZombie);
        config1.RecordRole("p4", ExperimentStrategy.BehaviorTree, ExperimentRole.HalfZombie);
        config1.RecordRole("p1", ExperimentStrategy.Scasp, ExperimentRole.Innocent);
        Check(config1.TotalFor(ExperimentStrategy.Scasp, ExperimentRole.Innocent) == 5, "Repeated innocent records counted once");
        Check(config1.TotalFor(ExperimentStrategy.BehaviorTree, ExperimentRole.HalfZombie) == 3, "Repeated infections counted once");
        var config1Metrics = new ExperimentMetrics();
        config1.FillMetrics(config1Metrics, new[] {
            // p1 alive and never infected, innocent survivor
            new ExperimentMeasurements.NpcEndState("p1", 3, false, false),
            // p2 infected and window still open, half zombie survivor
            new ExperimentMeasurements.NpcEndState("p2", 2, true, false),
            // p3 infected and the window ran out, a loss even though it still has health
            new ExperimentMeasurements.NpcEndState("p3", 1, true, true),
            // p4 infected and then died
            new ExperimentMeasurements.NpcEndState("p4", 0, true, false),
            // p5 died without getting infected
            new ExperimentMeasurements.NpcEndState("p5", 0, false, false),
        });
        Check(config1Metrics.ScaspInnocents.Total == 5 && config1Metrics.ScaspInnocents.Alive == 1,
            "Infected innocents count toward the innocent total but not as survivors");
        Check(config1Metrics.BehaviorTreeHalfZombies.Total == 3 && config1Metrics.BehaviorTreeHalfZombies.Alive == 1,
            "Only living half zombies with an open window survive");
        Check(config1Metrics.ScaspHalfZombies.Total == 0 && config1Metrics.BehaviorTreeInnocents.Total == 0,
            "Unused strategy and role pairs stay at zero");

        // Configuration 2 style run, the other way around
        var config2 = new ExperimentMeasurements();
        config2.RecordRole("p1", ExperimentStrategy.BehaviorTree, ExperimentRole.Innocent);
        config2.RecordRole("p2", ExperimentStrategy.BehaviorTree, ExperimentRole.Innocent);
        config2.RecordRole("p2", ExperimentStrategy.Scasp, ExperimentRole.HalfZombie);
        var config2Metrics = new ExperimentMetrics();
        // p1 is missing from the end states so it cant count as a survivor
        config2.FillMetrics(config2Metrics, new[] { new ExperimentMeasurements.NpcEndState("p2", 3, true, false) });
        Check(config2Metrics.BehaviorTreeInnocents.Total == 2 && config2Metrics.BehaviorTreeInnocents.Alive == 0,
            "Missing NPCs count toward the total but not as survivors");
        Check(config2Metrics.ScaspHalfZombies.Total == 1 && config2Metrics.ScaspHalfZombies.Alive == 1,
            "Configuration 2 half zombies counted under s(CASP)");

        // Survival rules on their own
        Check(ExperimentMeasurements.SurvivedAs(ExperimentRole.Innocent, new ExperimentMeasurements.NpcEndState("a", 1, false, false)),
            "Healthy uninfected innocent survives");
        Check(!ExperimentMeasurements.SurvivedAs(ExperimentRole.HalfZombie, new ExperimentMeasurements.NpcEndState("a", 1, false, false)),
            "Never infected NPC is not a half zombie survivor");
        Check(!ExperimentMeasurements.SurvivedAs(ExperimentRole.HalfZombie, new ExperimentMeasurements.NpcEndState("a", 3, true, true)),
            "Expired bite window is a half zombie loss");

        // Bad input
        Throws(() => new ExperimentMeasurements().RecordRole("", ExperimentStrategy.Scasp, ExperimentRole.Innocent), "Empty agent id rejected");
        Throws(() => new ExperimentMeasurements().FillMetrics(new ExperimentMetrics(), new[] {
            new ExperimentMeasurements.NpcEndState("p1", 3, false, false),
            new ExperimentMeasurements.NpcEndState("p1", 3, false, false) }), "Duplicate end states rejected");

        // A finished run is locked, so a late reply from an old game cant change it
        var finished = new ExperimentMeasurements();
        finished.AddScaspRoundTripLatency(10);
        finished.RecordRole("p1", ExperimentStrategy.Scasp, ExperimentRole.Innocent);
        var finishedMetrics = new ExperimentMetrics();
        finished.FillMetrics(finishedMetrics, Array.Empty<ExperimentMeasurements.NpcEndState>());
        Check(finished.IsFinished, "FillMetrics finishes the run");
        Check(!finished.AddScaspRoundTripLatency(999) && !finished.AddScaspFactLatency(1) && !finished.AddScaspQueryLatency(1) &&
              !finished.AddBehaviorTreeFactLatency(1) && !finished.AddBehaviorTreeActionLatency(1),
            "Late samples are rejected after the run finished");
        Check(!finished.RecordRole("p2", ExperimentStrategy.Scasp, ExperimentRole.HalfZombie), "Late roles are rejected after the run finished");
        Check(finished.ScaspRoundTripSampleCount == 1 && finished.TotalFor(ExperimentStrategy.Scasp, ExperimentRole.HalfZombie) == 0,
            "Late results did not change the finished run");
        Check(finishedMetrics.ScaspActionRoundTripLatencyMs == 10, "Finished metrics keep their real value");
        var fresh = new ExperimentMeasurements();
        Check(fresh.AddScaspRoundTripLatency(5) && fresh.ScaspRoundTripSampleCount == 1, "The next run's fresh recorder still records");

        // CSV role mapping labels
        Check(ExperimentCsv.RoleMapping(1) == "innocent:scasp/half_zombie:behavior_tree", "Configuration 1 label");
        Check(ExperimentCsv.RoleMapping(2) == "innocent:behavior_tree/half_zombie:scasp", "Configuration 2 label");
        Check(ExperimentCsv.RoleMapping(0) == "both_configurations", "Summary all row label");

        // Strategy for each role in both configurations
        Check(ExperimentRoleStrategies.InnocentStrategy(1) == ExperimentStrategy.Scasp &&
              ExperimentRoleStrategies.HalfZombieStrategy(1) == ExperimentStrategy.BehaviorTree,
            "Configuration 1 is s(CASP) innocents and Behavior Tree half zombies");
        Check(ExperimentRoleStrategies.InnocentStrategy(2) == ExperimentStrategy.BehaviorTree &&
              ExperimentRoleStrategies.HalfZombieStrategy(2) == ExperimentStrategy.Scasp,
            "Configuration 2 is Behavior Tree innocents and s(CASP) half zombies");
        Check(ExperimentRoleStrategies.StrategyFor(ExperimentRole.HalfZombie, 2) == ExperimentStrategy.Scasp &&
              ExperimentRoleStrategies.StrategyFor(ExperimentRole.Innocent, 1) == ExperimentStrategy.Scasp,
            "StrategyFor matches the role methods");
        foreach (int configuration in new[] { 1, 2 })
            Check(ExperimentRoleStrategies.InnocentStrategy(configuration) != ExperimentRoleStrategies.HalfZombieStrategy(configuration),
                "Roles never share a strategy in the same configuration");
        Throws(() => ExperimentRoleStrategies.InnocentStrategy(0), "Configuration 0 rejected");
        Throws(() => ExperimentRoleStrategies.HalfZombieStrategy(3), "Configuration 3 rejected");

        // Make sure the real recorded values end up in Marcelo's CSV files
        string root = Path.Combine(Path.GetTempPath(), "gamescasp_measurements_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var csv = new ExperimentCsv(root, "measurements", 1, "test", "test"))
            {
                // Copy the filled metrics into a run row like the runner does
                var run1 = new ExperimentMetrics();
                config1.FillMetrics(run1, new[] {
                    new ExperimentMeasurements.NpcEndState("p1", 3, false, false),
                    new ExperimentMeasurements.NpcEndState("p2", 2, true, false) });
                run1.ScaspFactLatencyMs = splitMetrics.ScaspFactLatencyMs;
                run1.ScaspActionRoundTripLatencyMs = splitMetrics.ScaspActionRoundTripLatencyMs;
                run1.ScaspActionQueryLatencyMs = splitMetrics.ScaspActionQueryLatencyMs;
                run1.BehaviorTreeFactLatencyMs = splitMetrics.BehaviorTreeFactLatencyMs;
                run1.BehaviorTreeActionLatencyMs = splitMetrics.BehaviorTreeActionLatencyMs;
                csv.Append(1, 1, 55, "TimedOut", 1, run1);
                csv.Append(1, 2, 55, "TimedOut", 1, config2Metrics);
                csv.WriteSummary("completed");

                var game = ExperimentMetricsVerification.Read(csv.GamesPath)[0];
                Check(game["scasp_action_round_trip_latency_ms"] == "150" && game["behavior_tree_action_latency_ms"] == "2",
                    "Recorded latencies exported to games.csv");
                Check(game["scasp_innocents_total"] == "5" && game["scasp_innocents_alive"] == "1" &&
                      game["behavior_tree_half_zombies_total"] == "3" && game["behavior_tree_half_zombies_alive"] == "1",
                    "Recorded populations exported to games.csv");
                Check(game["metrics_status"] == "measured", "Fully recorded run is not a placeholder");
                var gameRows = ExperimentMetricsVerification.Read(csv.GamesPath);
                Check(gameRows[0]["role_mapping"] == ExperimentCsv.RoleMapping(1) && gameRows[1]["role_mapping"] == ExperimentCsv.RoleMapping(2),
                    "games.csv has the real role mapping for each configuration");
                var summaryRows = ExperimentMetricsVerification.Read(csv.SummaryPath);
                Check(summaryRows.All(row => row["role_mapping"] == ExperimentCsv.RoleMapping(row["configuration"] == "all" ? 0 : int.Parse(row["configuration"]))),
                    "summary.csv has the real role mapping for each row");
                Check(gameRows[1]["scasp_fact_latency_ms"] == "", "Missing latency is still a blank cell");

                var all = ExperimentMetricsVerification.Read(csv.SummaryPath).Single(row => row["configuration"] == "all");
                // Only p1 survived as an s(CASP) innocent out of 5, so 20 percent
                Check(Math.Abs(double.Parse(all["scasp_innocents_win_percentage"], CultureInfo.InvariantCulture) - 20) < 0.000001,
                    "s(CASP) innocent win percentage uses sum(A) / sum(T)");
                // p2 survived as a Behavior Tree half zombie out of 3
                Check(Math.Abs(double.Parse(all["behavior_tree_half_zombies_win_percentage"], CultureInfo.InvariantCulture) - 100.0 / 3) < 0.000001,
                    "Behavior Tree half zombie win percentage uses sum(A) / sum(T)");
                Check(Math.Abs(double.Parse(all["behavior_tree_innocents_win_percentage"], CultureInfo.InvariantCulture)) < 0.000001,
                    "Zero survivors gives a 0 percent win rate");
                Check(Math.Abs(double.Parse(all["scasp_half_zombies_win_percentage"], CultureInfo.InvariantCulture) - 100) < 0.000001,
                    "s(CASP) half zombie win percentage uses sum(A) / sum(T)");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

#if UNITY_EDITOR
        UnityEngine.Debug.Log($"EXPERIMENT MEASUREMENTS VERIFIED: {checks} checks passed.");
#else
        Console.WriteLine($"EXPERIMENT MEASUREMENTS VERIFIED: {checks} checks passed.");
#endif
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }

    private static void Throws(Action action, string description)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        throw new Exception(description);
    }
}

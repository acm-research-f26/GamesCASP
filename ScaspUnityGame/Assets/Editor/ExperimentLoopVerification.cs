using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Exercises the real Main scene, prefab spawning, and four complete reloads.
// Intentionally uses BehaviorTree: this verifies orchestration without a server.
// Part two note, the role strategies now override Backend during experiments, so configuration 1
// innocents are on s(CASP) here, without a server they just wait and the forced end conditions still work
public static class ExperimentLoopVerification
{
    private const string Pending = "GamesCASP.VerifyExperimentLoop";
    private static int checks;
    private static double deadline;
    private static readonly Dictionary<int, string> layouts = new Dictionary<int, string>();
    private static readonly HashSet<string> actorIds = new HashSet<string>();
    // The last run's recorder, so we can make sure a late result cant reach it
    private static ExperimentMeasurements previousRecorder;

    [MenuItem("Tools/Experiments/Verify Game Loop")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            StartVerification();
            return;
        }
        if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    public static void RunBatch() => Run();

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseBool(Pending);
            EditorApplication.update -= Watchdog;
        }
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.EraseBool(Pending);
        StartVerification();
    }

    private static void StartVerification()
    {
        checks = 0;
        layouts.Clear();
        actorIds.Clear();
        previousRecorder = null;
        ExperimentMetricsVerification.Run();
        var runner = ExperimentRunner.Begin(new ExperimentSettings {
            GameCount = 2, NpcCount = 2, Seed = 55, MaxGameSeconds = 0.05f,
            Backend = NpcDecisionBackend.BehaviorTree,
            OutputDirectory = Path.GetFullPath("Logs/ExperimentVerification")
        });
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Watchdog;
        runner.RunPreparing += (run, players) => {
            Check(players.players.Count == 0 && !players.GameStarted, "Fresh PlayerManager on every run");
            Check(AiDiagnostics.All.Count == 0, "Diagnostics reset between runs");
            Check(!MansionNavigation.IsBuilt, "Navigation reset before map generation");
            Check(run.CurrentMetrics.ScaspFactLatencyMs == null, "Fresh placeholder metrics each run");
        };
        runner.RunStarted += (run, players) => {
            Check(players.players.Count == 2, "Configured NPC count spawned");
            foreach (Agent agent in players.players)
            {
                Check(agent.Health == 3 && !agent.IsHalfZombie, "NPC health and infection reset");
                Check(actorIds.Add(agent.AgentId), "Actor memories isolated between runs");
                Check(agent.AgentId == agent.name, "Targets and actor identities match");
                CheckRoleStrategy(run, agent);
            }
            Check(players.UsesRoleStrategies, "Experiment runs use role strategies");
            var levers = UnityEngine.Object.FindAnyObjectByType<LeverManager>();
            Check(levers.ActivatedLevers == 0, "Lever progress reset");
            string layout = Layout(players);
            if (run.Configuration == 1) layouts[run.GameNumber] = layout;
            else Check(layouts[run.GameNumber] == layout, "Paired configurations have identical starting layouts");

            // Cover all three end conditions without waiting for AI gameplay.
            if (run.GameNumber == 1 && run.Configuration == 1)
                for (int i = 0; i < levers.TotalLevers; i++) levers.LeverActivated();
            if (run.GameNumber == 1 && run.Configuration == 2)
                foreach (Agent agent in players.players) agent.SetHealth(0);
        };
        runner.RunFinished += (run, outcome, seconds) => {
            var expected = run.GameNumber == 2 ? ExperimentOutcome.TimedOut :
                run.Configuration == 1 ? ExperimentOutcome.Won : ExperimentOutcome.NoSurvivors;
            Check(outcome == expected, "Correct terminal outcome");
            Check(UnityEngine.Object.FindAnyObjectByType<PlayerManager>().GameStarted, "Final metrics hook runs before teardown");
        };
        // Part two, check the recorded populations and that one run cant leak into the next
        runner.RunStarted += (run, players) => {
            Check(run.CurrentMeasurements != null && !run.CurrentMeasurements.IsFinished, "Every run gets a fresh recorder");
            Check(ExperimentRunner.ActiveMeasurements == run.CurrentMeasurements, "Gameplay records into the current run");
            if (previousRecorder != null)
            {
                Check(!ReferenceEquals(previousRecorder, run.CurrentMeasurements), "Runs never share a recorder");
                // Pretend a slow reply from the last run just showed up
                Check(!previousRecorder.AddScaspRoundTripLatency(1) && !previousRecorder.AddBehaviorTreeActionLatency(1),
                    "A late result from the previous run is ignored");
            }
            previousRecorder = run.CurrentMeasurements;
        };
        runner.RunFinished += (run, outcome, seconds) => CheckRecordedPopulations(run);
        runner.BatchFinished += run => {
            EditorApplication.update -= Watchdog;
            try
            {
                Check(run.Failure == null && run.CompletedRuns == 4, "Exactly four configuration runs completed");
                Check(ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "games.csv")).Count == 4, "Exactly one exported row per run");
                // Part two, the exported rows have the real role mapping and real population counts
                foreach (var row in ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "games.csv")))
                {
                    Check(row["role_mapping"] == ExperimentCsv.RoleMapping(int.Parse(row["configuration"])), "games.csv role mapping matches the configuration");
                    Check(row["backend"] == ExperimentRoleStrategies.BackendLabel && row["scasp_policy"] == "Baseline",
                        "games.csv backend and policy describe the real experiment");
                    string innocents = row["configuration"] == "1" ? "scasp_innocents" : "behavior_tree_innocents";
                    Check(row[innocents + "_total"] == "2", "games.csv has the real innocent total");
                }
                var summary = ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "summary.csv"));
                Check(summary.Single(row => row["configuration"] == "all")["completed_runs"] == "4", "Summary matches finished runs");
                Check(!UnityEngine.Object.FindAnyObjectByType<PlayerManager>().GameStarted, "Final game returned to fresh start screen");
                CheckNormalGameplay();
                Debug.Log($"EXPERIMENT LOOP VERIFIED: {checks} checks passed. CSVs: {run.OutputDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        };
    }

    // Check the populations ExperimentRecording put in CurrentMetrics, it runs before this handler
    private static void CheckRecordedPopulations(ExperimentRunner run)
    {
        ExperimentMetrics metrics = run.CurrentMetrics;
        Check(run.CurrentMeasurements.IsFinished, "Recorder is locked once the run finishes");
        bool scaspInnocents = run.Configuration == 1;
        ExperimentPopulation innocents = scaspInnocents ? metrics.ScaspInnocents : metrics.BehaviorTreeInnocents;
        ExperimentPopulation otherInnocents = scaspInnocents ? metrics.BehaviorTreeInnocents : metrics.ScaspInnocents;
        Check(innocents.Total == 2, "Both NPCs counted as innocents under the configuration strategy");
        Check(otherInnocents.Total == 0, "No innocents counted under the other strategy");

        // Game 1 configuration 2 kills everyone, so nobody survives as an innocent
        if (run.GameNumber == 1 && run.Configuration == 2)
            Check(innocents.Alive == 0, "Dead innocents are not survivors");
        // Game 1 configuration 1 wins right away, nobody had time to get infected
        if (run.GameNumber == 1 && run.Configuration == 1)
            Check(innocents.Alive == 2, "Living uninfected innocents are survivors");
    }

    // Check this NPC started on the right strategy for the configuration
    private static void CheckRoleStrategy(ExperimentRunner run, Agent agent)
    {
        // Everyone spawns innocent, so they should all be on the innocent strategy
        bool scaspInnocents = ExperimentRoleStrategies.InnocentStrategy(run.Configuration) == ExperimentStrategy.Scasp;
        IAgentStrategy active = agent.ActiveStrategy;
        Check(scaspInnocents ? active is ScaspStrategy : active is BehaviorTree,
            $"Configuration {run.Configuration} innocents start on the right strategy");

        // Only one strategy can be turned on, so nothing else can control the NPC
        int enabledStrategies = agent.GetComponents<MonoBehaviour>().Count(component => component is IAgentStrategy && component.enabled);
        Check(enabledStrategies == 1, "Only the assigned strategy controls the NPC");

        // The s(CASP) client is ready either way so the NPC can switch roles later
        ScaspClient client = agent.GetComponent<ScaspClient>();
        Check(client != null && client.enabled, "Every experiment NPC has its s(CASP) client turned on");
    }

    // After the batch the start screen has a fresh PlayerManager with no experiment set up
    // Start a normal Behavior Tree game there and make sure it works the same as before part two
    private static void CheckNormalGameplay()
    {
        var players = UnityEngine.Object.FindAnyObjectByType<PlayerManager>();
        Check(!players.UsesRoleStrategies, "Normal gameplay has no role strategies");
        players.DecisionBackend = NpcDecisionBackend.BehaviorTree;
        players.StartGameClicked();
        Check(players.players.Count > 0, "Normal gameplay spawns NPCs");
        foreach (Agent agent in players.players)
        {
            Check(agent.ActiveStrategy is BehaviorTree, "Normal gameplay keeps the single chosen backend");
            ScaspClient client = agent.GetComponent<ScaspClient>();
            // Normal tree games never turned the client on, so it should still be off
            Check(client == null || !client.enabled, "Normal Behavior Tree gameplay does not connect s(CASP)");
        }
    }

    private static string Layout(PlayerManager players)
    {
        var positions = players.players.Select(agent => "npc:" + agent.transform.position.ToString("F4"))
            .Concat(UnityEngine.Object.FindObjectsByType<LeverScript>().Where(lever => lever.name.StartsWith("lever_"))
                .Select(lever => "lever:" + lever.transform.position.ToString("F4")))
            .Concat(UnityEngine.Object.FindObjectsByType<Window>().Select(window => "window:" + window.transform.position.ToString("F4")))
            .Concat(UnityEngine.Object.FindObjectsByType<Zombie>().Select(zombie => "zombie:" + zombie.transform.position.ToString("F4")));
        return string.Join("|", positions.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static void Watchdog()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        EditorApplication.update -= Watchdog;
        Debug.LogError("Experiment loop verification exceeded 120 seconds.");
        if (Application.isBatchMode) EditorApplication.Exit(1);
        else ExperimentRunner.Instance?.Cancel();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}

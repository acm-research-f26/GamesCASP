using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Exercises the real Main scene, prefab spawning, and four complete reloads.
// Intentionally uses BehaviorTree: this verifies orchestration without a server.
public static class ExperimentLoopVerification
{
    private const string Pending = "GamesCASP.VerifyExperimentLoop";
    private static int checks;
    private static double deadline;
    private static readonly Dictionary<int, string> layouts = new Dictionary<int, string>();
    private static readonly HashSet<string> actorIds = new HashSet<string>();

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
            }
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
        runner.BatchFinished += run => {
            EditorApplication.update -= Watchdog;
            try
            {
                Check(run.Failure == null && run.CompletedRuns == 4, "Exactly four configuration runs completed");
                Check(ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "games.csv")).Count == 4, "Exactly one exported row per run");
                var summary = ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "summary.csv"));
                Check(summary.Single(row => row["configuration"] == "all")["completed_runs"] == "4", "Summary matches finished runs");
                Check(!UnityEngine.Object.FindAnyObjectByType<PlayerManager>().GameStarted, "Final game returned to fresh start screen");
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

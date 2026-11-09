using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Live end to end check with the real Python s(CASP) server, start it first with ./Start-Playtest.ps1 -NoUnity
// Runs a small real batch through ExperimentRunner, so every number here comes from real gameplay and real replies
// The only things the test does itself are store one witnessed attack fact and infect one NPC per run,
// so both strategies, a strategy switch and a memory replay happen in every run
public static class LiveExperimentVerification
{
    private const string Pending = "GamesCASP.VerifyLiveExperiment";
    private static int checks;
    private static double deadline;
    private static string failure;
    private static RunState current;

    // What we keep track of during one run
    private sealed class RunState
    {
        public ExperimentRunner Run;
        public PlayerManager Players;
        public double StartedAt;
        public bool Injected;
        public Agent Infected;
        public int NewScaspFacts;
        public int ScaspRoundTripsAtSwitch;
        public int BehaviorTreeActionsAtSwitch;
        public bool ReplayChecked;
    }

    [MenuItem("Tools/Experiments/Verify Live Experiment (needs server)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Begin();
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
            EditorApplication.update -= Drive;
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.EraseBool(Pending);
        Begin();
    }

    private static void Begin()
    {
        checks = 0;
        failure = null;
        current = null;
        var runner = ExperimentRunner.Begin(new ExperimentSettings {
            GameCount = 2, NpcCount = 3, Seed = 55, MaxGameSeconds = 20f,
            Backend = NpcDecisionBackend.Scasp,
            OutputDirectory = Path.GetFullPath("Logs/LiveExperimentVerification")
        });
        deadline = EditorApplication.timeSinceStartup + 300;
        runner.RunStarted += StartRun;
        runner.RunFinished += (run, outcome, seconds) => CheckRun(run, outcome);
        runner.BatchFinished += CheckBatch;
        EditorApplication.update -= Drive;
        EditorApplication.update += Drive;
    }

    // Start tracking a new run and count every new fact an s(CASP) NPC stores
    private static void StartRun(ExperimentRunner run, PlayerManager players)
    {
        var state = new RunState { Run = run, Players = players, StartedAt = EditorApplication.timeSinceStartup };
        Check(run.Settings.Policy == ScaspPolicy.Baseline, "Experiments default to the Baseline s(CASP) policy");
        foreach (Agent agent in players.players)
        {
            Agent owner = agent;
            // Every s(CASP) innocent has to be on the batch policy
            if (owner.ActiveStrategy is ScaspStrategy spawned)
                Check(spawned.Policy == ScaspPolicy.Baseline, "Spawned s(CASP) NPCs use Baseline");
            // These are new facts going through TryStoreFact, replays never fire this event
            owner.GetComponent<AgentFactReceiver>().FactReceived += fact => {
                if (owner.ActiveStrategy is ScaspStrategy) state.NewScaspFacts++;
            };
        }
        current = state;
    }

    // Runs every editor update while a run is going
    private static void Drive()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Live experiment verification took longer than 300 seconds.");
            RunState state = current;
            if (state == null || state.Run.CurrentMeasurements == null || state.Run.CurrentMeasurements.IsFinished) return;
            double elapsed = EditorApplication.timeSinceStartup - state.StartedAt;
            Agent first = state.Players.players[0];
            ExperimentMeasurements recorder = state.Run.CurrentMeasurements;

            // Once connected store a witnessed attack like AttackWitnessReporter would
            // We do this early since s(CASP) innocents can win in just a few seconds
            ScaspClient firstClient = first.GetComponent<ScaspClient>();
            if (!state.Injected && elapsed > 1 && firstClient != null && firstClient.IsConnected)
            {
                state.Injected = true;
                var fact = new Fact(FactType.ZombieAttackWitnessed, FactLifetime.Permanent, "zombie_live", state.Players.players[1].AgentId);
                Check(first.GetComponent<AgentFactReceiver>().ReceiveFact(fact), "Live witnessed attack stored");
            }

            // Infect the first NPC as soon as that fact is stored, so it switches strategy and replays that memory
            // On s(CASP) we wait for Python to acknowledge it first, the tree stores it right away
            bool factStored = !(first.ActiveStrategy is ScaspStrategy waiting) || waiting.PendingFactCount == 0;
            if (state.Injected && state.Infected == null && factStored)
            {
                if (first.Health <= 0 || first.IsHalfZombie)
                {
                    Debug.LogWarning("[LiveVerify] First NPC was already dead or infected by gameplay, skipping the forced switch this run");
                    state.Infected = first;
                    state.ReplayChecked = true;
                    return;
                }
                state.ScaspRoundTripsAtSwitch = recorder.ScaspRoundTripSampleCount;
                state.BehaviorTreeActionsAtSwitch = recorder.BehaviorTreeActionSampleCount;
                Check(first.MarkHalfZombie(), "Live infection works");
                state.Infected = first;
                // A half zombie that switched to s(CASP) has to use the same policy as the rest of the batch
                if (first.ActiveStrategy is ScaspStrategy switched)
                    Check(switched.Policy == ScaspPolicy.Baseline, "Switched s(CASP) half zombies use Baseline");
                Debug.Log($"[LiveVerify] Game {state.Run.GameNumber} configuration {state.Run.Configuration}: infected {first.AgentId}, now on {first.ActiveStrategy.GetType().Name}");
            }

            // Configuration 2 switches to s(CASP), once Python stored the replayed memory check it
            if (state.Infected != null && !state.ReplayChecked && state.Infected.ActiveStrategy is ScaspStrategy scasp && scasp.PendingFactCount == 0)
            {
                state.ReplayChecked = true;
                // Every s(CASP) fact sample has to be a new fact, the replayed one cant be in there
                Check(recorder.ScaspFactSampleCount == state.NewScaspFacts, "Replayed memories are not counted as s(CASP) fact latency");
                // Python acknowledged the replay, so it has to be in this NPC's facts in the real file
                string facts = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../DockerServer/Server/facts_temp.pl")));
                string expected = $"stored_fact({state.Infected.AgentId}, permanent, zombie_attack_witnessed, zombie_live, {state.Players.players[1].AgentId}, none).";
                Check(facts.Contains(expected), "Replayed memory reached this NPC's Python facts");
                Debug.Log($"[LiveVerify] Found in facts_temp.pl: {expected}");
            }
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
            Debug.LogException(exception);
            EditorApplication.update -= Drive;
            ExperimentRunner.Instance?.Cancel();
        }
    }

    // ExperimentRecording already filled CurrentMetrics before this runs
    private static void CheckRun(ExperimentRunner run, ExperimentOutcome outcome)
    {
        RunState state = current;
        ExperimentMeasurements recorder = run.CurrentMeasurements;
        ExperimentMetrics metrics = run.CurrentMetrics;
        Debug.Log($"[LiveVerify] Game {run.GameNumber} config {run.Configuration} ({outcome}): " +
            $"scasp fact {metrics.ScaspFactLatencyMs?.ToString("F3") ?? "blank"} ms x{recorder.ScaspFactSampleCount}, " +
            $"round trip {metrics.ScaspActionRoundTripLatencyMs?.ToString("F1") ?? "blank"} ms x{recorder.ScaspRoundTripSampleCount}, " +
            $"query {metrics.ScaspActionQueryLatencyMs?.ToString("F1") ?? "blank"} ms x{recorder.ScaspQuerySampleCount}, " +
            $"tree fact {metrics.BehaviorTreeFactLatencyMs?.ToString("F4") ?? "blank"} ms x{recorder.BehaviorTreeFactSampleCount}, " +
            $"tree action {metrics.BehaviorTreeActionLatencyMs?.ToString("F4") ?? "blank"} ms x{recorder.BehaviorTreeActionSampleCount}, " +
            $"innocents scasp {metrics.ScaspInnocents.Alive}/{metrics.ScaspInnocents.Total} tree {metrics.BehaviorTreeInnocents.Alive}/{metrics.BehaviorTreeInnocents.Total}, " +
            $"half zombies scasp {metrics.ScaspHalfZombies.Alive}/{metrics.ScaspHalfZombies.Total} tree {metrics.BehaviorTreeHalfZombies.Alive}/{metrics.BehaviorTreeHalfZombies.Total}");

        if (run.Configuration == 1)
        {
            // s(CASP) innocents, real decisions and the stored fact
            Check(metrics.ScaspInnocents.Total == 3 && metrics.BehaviorTreeInnocents.Total == 0, "Configuration 1 innocents all counted under s(CASP)");
            Check(recorder.ScaspRoundTripSampleCount > 0 && recorder.ScaspQuerySampleCount == recorder.ScaspRoundTripSampleCount,
                "Configuration 1 got real s(CASP) decisions, one query time per round trip");
            Check(recorder.ScaspFactSampleCount > 0 && metrics.ScaspFactLatencyMs.HasValue, "Configuration 1 got real Python write times");
            Check(metrics.ScaspActionQueryLatencyMs <= metrics.ScaspActionRoundTripLatencyMs, "Solver time is never more than the full round trip");
            // The infected NPC is on the tree now
            Check(metrics.BehaviorTreeHalfZombies.Total >= 1 && metrics.ScaspHalfZombies.Total == 0, "Configuration 1 half zombies counted under the tree");
            Check(recorder.BehaviorTreeActionSampleCount > state.BehaviorTreeActionsAtSwitch, "Configuration 1 tree half zombie made decisions");
        }
        else
        {
            // Behavior Tree innocents, the stored fact is timed locally
            Check(metrics.BehaviorTreeInnocents.Total == 3 && metrics.ScaspInnocents.Total == 0, "Configuration 2 innocents all counted under the tree");
            Check(recorder.BehaviorTreeActionSampleCount > 0 && recorder.BehaviorTreeFactSampleCount > 0, "Configuration 2 got tree decision and fact times");
            Check(metrics.ScaspHalfZombies.Total >= 1 && metrics.BehaviorTreeHalfZombies.Total == 0, "Configuration 2 half zombies counted under s(CASP)");
            Check(state.ReplayChecked, "Configuration 2 replay was checked against Python");
            // Any s(CASP) decision in configuration 2 came from a switched half zombie
            Check(recorder.ScaspRoundTripSampleCount > state.ScaspRoundTripsAtSwitch, "Switched half zombie made real s(CASP) decisions");
        }
        // Never more s(CASP) fact samples than new facts, so replays never got counted
        Check(recorder.ScaspFactSampleCount <= state.NewScaspFacts, "s(CASP) fact samples only come from new facts");
        foreach (double? latency in new[] { metrics.ScaspFactLatencyMs, metrics.ScaspActionRoundTripLatencyMs, metrics.ScaspActionQueryLatencyMs,
            metrics.BehaviorTreeFactLatencyMs, metrics.BehaviorTreeActionLatencyMs })
            Check(!latency.HasValue || latency.Value >= 0 && latency.Value < 30000, "Latencies are real millisecond values");
        current = null;
    }

    // Read the exported CSVs and make sure the real values made it in
    private static void CheckBatch(ExperimentRunner run)
    {
        EditorApplication.update -= Drive;
        if (failure != null)
            throw new Exception("Live verification failed during a run: " + failure);
        Check(run.Failure == null && run.CompletedRuns == 4, "All four live runs completed");
        var games = ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "games.csv"));
        var summary = ExperimentMetricsVerification.Read(Path.Combine(run.OutputDirectory, "summary.csv"));
        Check(games.Count == 4 && summary.Count == 3, "Live CSVs have every run and summary row");
        foreach (var row in games)
        {
            Check(row["role_mapping"] == ExperimentCsv.RoleMapping(int.Parse(row["configuration"])), "Live games.csv role mapping");
            Check(row["metrics_status"] != "placeholder", "Live rows are not placeholders");
        }
        // The metadata has to describe what really ran, both backends and one policy for the whole batch
        foreach (var row in games.Concat(summary))
            Check(row["backend"] == ExperimentRoleStrategies.BackendLabel && row["scasp_policy"] == "Baseline",
                "CSV backend and policy columns describe the real experiment");

        // Label this folder so nobody mistakes it for research results
        File.WriteAllText(Path.Combine(run.OutputDirectory, "VERIFICATION_DATA.txt"),
            "Verification data from LiveExperimentVerification, not research results.\n" +
            "Every run had one witnessed attack fact stored and one NPC infected by the test itself,\n" +
            "so the numbers are biased and only show that the measurements and exports work.\n");
        Check(games.Where(row => row["configuration"] == "1").All(row => row["scasp_action_round_trip_latency_ms"] != ""),
            "Live s(CASP) round trip exported");
        Check(games.Where(row => row["configuration"] == "2").All(row => row["behavior_tree_action_latency_ms"] != ""),
            "Live tree action time exported");
        Debug.Log("[LiveVerify] games.csv\n" + File.ReadAllText(Path.Combine(run.OutputDirectory, "games.csv")));
        Debug.Log("[LiveVerify] summary.csv\n" + File.ReadAllText(Path.Combine(run.OutputDirectory, "summary.csv")));
        Debug.Log($"LIVE EXPERIMENT VERIFIED: {checks} checks passed. CSVs: {run.OutputDirectory}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}

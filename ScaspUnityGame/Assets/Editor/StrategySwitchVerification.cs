using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Play Mode checks for part two, switching strategy when an NPC gets infected
// Runs in the real Main scene without the Python server, so s(CASP) strategies just queue facts and wait
// Same setup as ExperimentLoopVerification so it works from the menu or -executeMethod
public static class StrategySwitchVerification
{
    private const string Pending = "GamesCASP.VerifyStrategySwitch";
    private static int checks;
    private static IEnumerator routine;
    private static double deadline;
    private static PlayerManager players;

    [MenuItem("Tools/Experiments/Verify Strategy Switching")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Begin();
            return;
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
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
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.EraseBool(Pending);
        Begin();
    }

    // Step through the checks one editor update at a time so the game can run frames in between
    private static void Begin()
    {
        checks = 0;
        routine = Verify();
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update -= Step;
        EditorApplication.update += Step;
    }

    private static void Step()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Strategy switch verification took longer than 120 seconds.");
            if (!routine.MoveNext())
                Finish(null);
        }
        catch (Exception exception)
        {
            Finish(exception);
        }
    }

    private static void Finish(Exception failure)
    {
        EditorApplication.update -= Step;
        if (failure == null)
            Debug.Log($"STRATEGY SWITCH VERIFIED: {checks} checks passed.");
        else
            Debug.LogException(failure);
        if (Application.isBatchMode)
            EditorApplication.Exit(failure == null ? 0 : 1);
    }

    private static IEnumerator Verify()
    {
        // Make sure our C# timing matches the baseline Prolog rules
        CheckTimingMatchesProlog();
        // Make sure write_ms is read the same way ScaspClient reads real Python replies
        CheckWriteMsParsing();
        // Outside an experiment nothing should ever get recorded
        Check(ExperimentRunner.ActiveMeasurements == null, "No recorder outside of experiments");

        // Configuration 1, s(CASP) innocent gets infected and switches to the tree
        IEnumerator load = StartGame(1);
        while (load.MoveNext()) yield return load.Current;
        Agent infected = players.players[0];
        Agent control = players.players[1];
        var scasp = infected.GetComponent<ScaspStrategy>();
        var client = infected.GetComponent<ScaspClient>();
        Check(infected.ActiveStrategy is ScaspStrategy && control.ActiveStrategy is ScaspStrategy,
            "Configuration 1 innocents start on s(CASP)");

        // Learn some facts while still innocent
        NpcFactMemory memory = players.FactMemoryFor(infected);
        Check(memory != null && memory.Count == 0, "Experiment NPCs start with an empty memory");
        LearnFacts(infected, control);
        Check(memory.Count == 3, "Duplicate permanent facts and old mutable facts are not kept");
        Check(memory.Facts.Single(fact => fact.Type == FactType.AgentAttacked).TargetId == "zombie2",
            "Only the latest mutable fact is kept");

        // Save what the NPC looked like before the switch
        Vector3 position = infected.transform.position;
        int health = infected.Health;
        string id = infected.AgentId;
        int decisionsBefore = AiDiagnostics.For(id).DecisionMs.Count;
        Check(decisionsBefore == 0, "Unconnected s(CASP) NPC makes no decisions");

        // Now infect it and watch which actions it does
        var configuration1Actions = new List<(string action, string target)>();
        infected.GetComponent<AiAction>().ActionExecuted += (action, target) => configuration1Actions.Add((action, target));
        // Watch for bite requests from the half zombie's own AiAction from the moment it gets infected
        // This is what the bite mechanic listens to, and it can happen on its own before the test helps
        var biteRequests = new List<Agent>();
        infected.GetComponent<AiAction>().BiteRequested += victim => biteRequests.Add(victim);
        Check(infected.MarkHalfZombie(), "Infection still works");
        Check(configuration1Actions.Count > 0, "New tree decides right away instead of standing still after the switch");
        float biteStart = infected.BiteWindowStartedAt;
        Check(infected.ActiveStrategy is BehaviorTree, "Configuration 1 half zombie switches to the Behavior Tree");
        Check(!scasp.enabled, "Old s(CASP) strategy is turned off");
        Check(EnabledStrategies(infected) == 1, "Only one strategy controls the NPC after switching");
        Check(infected.transform.position == position && infected.Health == health && infected.AgentId == id && infected.name == id,
            "Switching keeps position, health and identity");
        Check(memory.ReplayInto(infected.ActiveStrategy, null) == 0, "Every remembered fact already reached the tree once");

        // A second infection event should not switch again or reset anything
        IAgentStrategy afterSwitch = infected.ActiveStrategy;
        Check(!infected.MarkHalfZombie(), "Repeated infection is ignored");
        Check(ReferenceEquals(infected.ActiveStrategy, afterSwitch) && infected.BiteWindowStartedAt == biteStart,
            "Repeated infection does not switch again or restart the bite window");
        Check(infected.GetComponents<ScaspClient>().Length == 1 && ReferenceEquals(infected.GetComponent<ScaspClient>(), client),
            "Switching does not add another s(CASP) client");

        // Send a late reply to the old s(CASP) strategy and a normal one to a still active s(CASP) strategy
        var oldStats = AiDiagnostics.For(id);
        var controlStats = AiDiagnostics.For(control.AgentId);
        int oldResponses = oldStats.Responses, oldStale = oldStats.Stale, controlStale = controlStats.Stale;
        InjectReply(client, id);
        InjectReply(control.GetComponent<ScaspClient>(), control.AgentId);
        yield return null;
        yield return null;
        Check(oldStats.Responses == oldResponses && oldStats.Stale == oldStale,
            "A late reply cannot reach the turned off s(CASP) strategy");
        Check(controlStats.Stale == controlStale + 1, "The active s(CASP) strategy still gets its replies");

        // The decision loop should follow the tree now
        IEnumerator wait = WaitSeconds(1.2f);
        while (wait.MoveNext()) yield return wait.Current;
        Check(AiDiagnostics.For(id).DecisionMs.Count > decisionsBefore, "Decision loop follows the new Behavior Tree");

        // Real bite in the game, put the half zombie in its critical stage and a human right next to it
        // This uses the real perception, AiAction and bite mechanic, only the positions are set by the test
        // The NPCs spawn 2 apart, so the tree half zombie can already have chased and bitten the human by itself
        // If it hasnt yet, put it in its critical stage and move the human right next to it
        float startBeforeBite = biteStart;
        if (!control.IsHalfZombie)
        {
            SetBiteWindowStart(infected, Time.time - (Agent.BiteWindowSeconds - Agent.CriticalBiteSeconds) - 0.2f);
            startBeforeBite = infected.BiteWindowStartedAt;
            PlaceNear(control, infected, 0.6f);
            double biteDeadline = EditorApplication.timeSinceStartup + 2.5;
            while (!control.IsHalfZombie && EditorApplication.timeSinceStartup < biteDeadline)
                yield return null;
        }
        else
            Debug.Log("[Verify] The tree half zombie already chased and bit the human on its own");
        // The tree can already be chasing this human with move_to_human, then AiAction bites as soon as its in range
        // So we check the tree went after this human, and that the bite came from this half zombie
        Check(configuration1Actions.Contains(("attack_human", control.AgentId)) || configuration1Actions.Contains(("move_to_human", control.AgentId)),
            "Critical Behavior Tree half zombie goes after the nearby human");
        Check(biteRequests.Contains(control), "The tree half zombie requested the bite on that human");
        Check(control.IsHalfZombie, "The bite mechanic infects the human");
        Check(infected.BiteWindowStartedAt > startBeforeBite, "A successful bite renews the attacker's window");
        Check(control.ActiveStrategy is BehaviorTree, "The newly bitten NPC also switches to the configuration 1 half zombie strategy");

        // Configuration 2, tree innocent gets infected and switches to s(CASP)
        load = StartGame(2);
        while (load.MoveNext()) yield return load.Current;
        infected = players.players[0];
        control = players.players[1];
        var tree = infected.GetComponent<BehaviorTree>();
        scasp = infected.GetComponent<ScaspStrategy>();
        client = infected.GetComponent<ScaspClient>();
        Check(infected.ActiveStrategy is BehaviorTree, "Configuration 2 innocents start on the Behavior Tree");
        memory = players.FactMemoryFor(infected);
        LearnFacts(infected, control);
        Check(memory.Count == 3, "Tree innocents remember stored facts too");
        Check(scasp.PendingFactCount == 0, "The turned off s(CASP) strategy got nothing while the tree was active");

        // Let the tree make some decisions first
        wait = WaitSeconds(1.2f);
        while (wait.MoveNext()) yield return wait.Current;
        id = infected.AgentId;
        Check(AiDiagnostics.For(id).DecisionMs.Count > 0, "Tree innocent was deciding before infection");

        var aiAction = infected.GetComponent<AiAction>();
        bool wasMoving = Private<bool>(aiAction, "isMoving");
        Check(infected.MarkHalfZombie(), "Configuration 2 infection still works");

        // Make sure the old tree's movement command was dropped at the switch
        // s(CASP) isnt connected here so it cant give a new command, anything still moving would be the old tree
        Check(!Private<bool>(aiAction, "isMoving") && Private<string>(aiAction, "currentAction") == null,
            "Old tree movement is dropped at the switch");
        Debug.Log($"[Verify] Configuration 2 NPC was {(wasMoving ? "moving" : "not moving")} right before the switch");
        Vector3 positionAtSwitch = infected.transform.position;
        Check(ReferenceEquals(infected.ActiveStrategy, scasp), "Configuration 2 half zombie switches to s(CASP)");
        Check(!tree.enabled && EnabledStrategies(infected) == 1, "Only s(CASP) controls the NPC after switching");
        Check(scasp.ActorId == id, "s(CASP) uses the same Prolog identity");
        // Python never got these since the tree was in charge, so each one gets queued exactly once
        Check(scasp.PendingFactCount == 3, "Remembered facts are queued for Python once");
        Check(memory.ReplayInto(scasp, null) == 0, "Facts are not queued for Python a second time");
        Check(infected.GetComponents<ScaspClient>().Length == 1 && client.enabled, "Same s(CASP) client is reused");

        // The loop should stop running the tree now
        int decisionsAtSwitch = AiDiagnostics.For(id).DecisionMs.Count;
        wait = WaitSeconds(1.2f);
        while (wait.MoveNext()) yield return wait.Current;
        Check(AiDiagnostics.For(id).DecisionMs.Count == decisionsAtSwitch, "Decision loop stops using the old tree");
        Check(Vector3.Distance(infected.transform.position, positionAtSwitch) < 0.2f, "NPC does not keep following the old command");

        // Normal gameplay, no experiment so nothing should switch
        load = StartGame(0);
        while (load.MoveNext()) yield return load.Current;
        Check(players.players.Count >= 4, "Normal game spawned enough NPCs for the checks below");
        Agent normal = players.players[0];
        IAgentStrategy normalStrategy = normal.ActiveStrategy;
        Check(!players.UsesRoleStrategies && players.FactMemoryFor(normal) == null, "Normal gameplay keeps no experiment memory");
        Check(normal.MarkHalfZombie(), "Normal infection still works");
        Check(ReferenceEquals(normal.ActiveStrategy, normalStrategy), "Normal gameplay does not switch strategy on infection");
        Check(!normal.GetComponent<ScaspClient>().enabled, "Normal Behavior Tree gameplay still does not connect s(CASP)");

        // In normal games a tree half zombie still plays like an innocent, so it should never bite
        var normalActions = new List<(string action, string target)>();
        normal.GetComponent<AiAction>().ActionExecuted += (action, target) => normalActions.Add((action, target));
        SetBiteWindowStart(normal, Time.time - (Agent.BiteWindowSeconds - Agent.CriticalBiteSeconds) - 0.2f);
        PlaceNear(players.players[3], normal, 0.6f);
        wait = WaitSeconds(1.5f);
        while (wait.MoveNext()) yield return wait.Current;
        Check(normalActions.Count > 0 && normalActions.All(entry => entry.action != "attack_human" && entry.action != "move_to_human"),
            "Normal gameplay tree half zombies keep the old innocent behavior");

        // Exact decisions from the half zombie tree, using a real NPC and real tree nodes
        CheckTreeDecisions(players.players[1], players.players[2]);
    }

    // Tick the real trees with exact observations so we can check each Prolog rule
    private static void CheckTreeDecisions(Agent npc, Agent deadNpc)
    {
        var levers = UnityEngine.Object.FindAnyObjectByType<LeverManager>();
        Node halfZombieTree = BehaviorTreeFactory.Build(new BarricadeObservationBuffer(), true);
        Node normalTree = BehaviorTreeFactory.Build(new BarricadeObservationBuffer(), false);
        var zombie = new Fact(FactType.ZombieSeen, FactLifetime.Transient, "zombie_t", distance: 1.5f);
        Fact Human(string humanId, float distance, int health = 3) =>
            new Fact(FactType.HumanSeen, FactLifetime.Transient, humanId, distance: distance, health: health);
        var situations = new[] {
            new Fact[0], new[] { zombie }, new[] { Human("h1", 0.5f) }, new[] { zombie, Human("h1", 3f) } };

        // Healthy NPC behavior remaining unchanged
        foreach (Fact[] seen in situations)
            Check(Decide(halfZombieTree, npc, levers, seen) == Decide(normalTree, npc, levers, seen),
                "Healthy NPCs get the exact same decision from the experiment tree");

        // Early half zombie, the first second still plays the innocent tree
        Check(npc.MarkHalfZombie(), "Tree test NPC infected");
        SetBiteWindowStart(npc, Time.time - (Agent.BiteWindowSeconds - Agent.CriticalBiteSeconds) + 0.3f);
        Check(!npc.InCriticalBiteStage && !npc.BiteWindowExpired, "Just before the critical stage");
        foreach (Fact[] seen in situations)
            Check(Decide(halfZombieTree, npc, levers, seen) == Decide(normalTree, npc, levers, seen),
                "Early half zombies play the normal innocent tree");

        // Critical stage turns on after the first second
        SetBiteWindowStart(npc, Time.time - (Agent.BiteWindowSeconds - Agent.CriticalBiteSeconds) - 0.2f);
        Check(npc.InCriticalBiteStage, "Critical stage starts after the first second");
        Check(Decide(halfZombieTree, npc, levers, Human("h1", 3f)) == ("move_to_human", "h1"), "Moves toward a human out of range");
        Check(Decide(halfZombieTree, npc, levers, Human("h1", 0.8f)) == ("attack_human", "h1"), "Bites a human within range");
        Check(Decide(halfZombieTree, npc, levers, Human("h1", 1f)) == ("attack_human", "h1"), "Exactly one unit counts as in range");
        Check(Decide(halfZombieTree, npc, levers, zombie, Human("h1", 3f)) == ("move_to_human", "h1"),
            "Fleeing is defeated in the critical stage");
        Check(Decide(halfZombieTree, npc, levers, zombie) == ("", ""), "No visible human means no action at all");
        Check(Decide(halfZombieTree, npc, levers) == ("", ""), "Nothing visible means no action at all");

        // Target selection matching preferred_human_target
        Check(Decide(halfZombieTree, npc, levers, Human("far", 4f), Human("near", 2f), Human(npc.AgentId, 0.1f), Human("dead", 0.5f, 0))
            == ("move_to_human", "near"), "Closest living human that isnt us is the target");
        Check(Decide(halfZombieTree, npc, levers, Human("first", 2f), Human("second", 2f)) == ("move_to_human", "first") &&
              Decide(halfZombieTree, npc, levers, Human("second", 2f), Human("first", 2f)) == ("move_to_human", "second"),
            "A tie keeps the first human seen");
        Check(Decide(halfZombieTree, npc, levers, new Fact(FactType.HumanSeen, FactLifetime.Transient, "nohealth", distance: 0.5f)) == ("", ""),
            "A human with unknown health is not a target");

        // Expired bite window blocks everything
        SetBiteWindowStart(npc, Time.time - Agent.BiteWindowSeconds - 0.1f);
        Check(npc.BiteWindowExpired, "Bite window expired");
        Check(Decide(halfZombieTree, npc, levers, Human("h1", 0.5f)) == ("", ""), "Expired half zombies get no action");

        // A dead half zombie cant act either
        Check(deadNpc.MarkHalfZombie(), "Dead test NPC infected");
        deadNpc.SetHealth(0);
        SetBiteWindowStart(deadNpc, Time.time - (Agent.BiteWindowSeconds - Agent.CriticalBiteSeconds) - 0.2f);
        Check(Decide(halfZombieTree, deadNpc, levers, Human("h1", 0.5f)) == ("", ""), "Dead half zombies get no action");
    }

    // Make one decision with a fresh blackboard that only sees these observations
    private static (string action, string target) Decide(Node root, Agent npc, LeverManager levers, params Fact[] seen)
    {
        var state = new Blackboard(npc, levers);
        state.updateTransientFacts(seen);
        root.Tick(state, out var decision);
        return (decision.action ?? "", decision.target ?? "");
    }

    // bite_window_seconds and critical_bite_seconds in the Prolog file must equal the C# constants
    private static void CheckTimingMatchesProlog()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../DockerServer/Server/half_zombie_timed_behavior.pl"));
        string rules = File.ReadAllText(path);
        float Read(string name) => float.Parse(Regex.Match(rules, "^" + name + @"\(([0-9.]+)\)\.", RegexOptions.Multiline).Groups[1].Value,
            CultureInfo.InvariantCulture);
        Check(Read("bite_window_seconds") == Agent.BiteWindowSeconds, "Bite window matches the Prolog rules");
        Check(Read("critical_bite_seconds") == Agent.CriticalBiteSeconds, "Critical stage matches the Prolog rules");
    }

    // Parse facts_stored replies with ScaspClient's own Response type and JsonUtility
    private static void CheckWriteMsParsing()
    {
        Type responseType = typeof(ScaspClient).GetNestedType("Response", BindingFlags.NonPublic);
        FieldInfo writeMs = responseType.GetField("write_ms");
        float Parse(string json) => (float)writeMs.GetValue(JsonUtility.FromJson(json, responseType));
        Check(Math.Abs(Parse("{\"message_type\":\"facts_stored\",\"request_id\":\"r\",\"actor\":\"a\",\"write_ms\":0.734}") - 0.734f) < 0.0001f,
            "write_ms from Python is read in ms");
        Check(Parse("{\"message_type\":\"facts_stored\",\"request_id\":\"r\",\"actor\":\"a\",\"write_ms\":0}") == 0f,
            "A real 0 ms write is kept as 0");
        Check(Parse("{\"message_type\":\"facts_stored\",\"request_id\":\"r\",\"actor\":\"a\"}") < 0f,
            "A reply without write_ms comes back negative so it wont be counted");
        var recorder = new ExperimentMeasurements();
        Check(!recorder.AddScaspFactLatency(Parse("{\"message_type\":\"facts_stored\"}")) && recorder.ScaspFactSampleCount == 0,
            "The recorder ignores a missing write time");
    }

    // Move the bite window start so the test doesnt have to wait for real seconds to pass
    private static void SetBiteWindowStart(Agent agent, float startedAt) =>
        typeof(Agent).GetField("biteWindowStartedAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(agent, startedAt);

    // Put one NPC a little way toward another, on the open floor between their spawn spots
    private static void PlaceNear(Agent mover, Agent target, float distance)
    {
        Vector3 direction = (mover.transform.position - target.transform.position).normalized;
        Vector3 position = target.transform.position + direction * distance;
        mover.transform.position = position;
        var body = mover.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
        }
    }

    private static T Private<T>(object target, string field) =>
        (T)typeof(AiAction).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

    // Load a fresh Main scene and start a game, configuration 0 means normal gameplay
    private static IEnumerator StartGame(int configuration)
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(ExperimentRunner.ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        while (!load.isDone) yield return null;
        // Let Start finish like the runner does
        yield return null;
        yield return null;
        AiDiagnostics.Reset();
        MansionNavigation.Clear();

        players = UnityEngine.Object.FindAnyObjectByType<PlayerManager>();
        var map = UnityEngine.Object.FindAnyObjectByType<MapManager>();
        if (configuration > 0)
        {
            players.ConfigureExperiment(2, NpcDecisionBackend.BehaviorTree, ScaspPolicy.Baseline, $"verify_c{configuration}_");
            players.ConfigureRoleStrategies(ExperimentRoleStrategies.InnocentStrategy(configuration),
                ExperimentRoleStrategies.HalfZombieStrategy(configuration));
        }
        else
            players.DecisionBackend = NpcDecisionBackend.BehaviorTree;

        // Same start order as the Start button and the runner
        foreach (var ui in UnityEngine.Object.FindObjectsByType<StartUIScript>())
            ui.StartGameClicked();
        players.StartGameClicked();
        // These are the isolated tests, so keep every client offline even if a real server is running
        // Clients only connect in Start on the next frame, so this happens before any of them try
        // LiveExperimentVerification is the one that tests the real server
        foreach (Agent agent in players.players)
            typeof(ScaspClient).GetField("serverUrl", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(agent.GetComponent<ScaspClient>(), "ws://127.0.0.1:9");
        map.StartGameClicked();
        Check(players.players.Count >= 2, "Game spawned NPCs");
        yield return null;
    }

    // Give an NPC the same kinds of stored facts the game sends, including duplicates and a mutable update
    private static void LearnFacts(Agent agent, Agent other)
    {
        var receiver = agent.GetComponent<AgentFactReceiver>();
        var witnessed = new Fact(FactType.ZombieAttackWitnessed, FactLifetime.Permanent, "zombie1", other.AgentId);
        Check(receiver.ReceiveFact(witnessed), "Witnessed attack stored");
        Check(receiver.ReceiveFact(witnessed), "Duplicate witnessed attack stored");
        Check(receiver.ReceiveFact(new Fact(FactType.AgentAttacked, FactLifetime.Mutable, agent.AgentId, "zombie1")), "First attack stored");
        Check(receiver.ReceiveFact(new Fact(FactType.AgentAttacked, FactLifetime.Mutable, agent.AgentId, "zombie2")), "Newer attack stored");
        Check(receiver.ReceiveFact(new Fact(FactType.LeverActivated, FactLifetime.Permanent, "lever1")), "Lever fact stored");
    }

    // Put a fake Python reply in the client's queue so its Update handles it like a real one
    private static void InjectReply(ScaspClient client, string actor)
    {
        var queue = (ConcurrentQueue<string>)typeof(ScaspClient)
            .GetField("responseQueue", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(client);
        queue.Enqueue("{\"message_type\":\"npc_action\",\"actor\":\"" + actor +
            "\",\"action\":\"wait\",\"target\":\"\",\"request_id\":\"late-reply\",\"solver_ms\":1}");
    }

    private static int EnabledStrategies(Agent agent) =>
        agent.GetComponents<MonoBehaviour>().Count(component => component is IAgentStrategy && component.enabled);

    private static IEnumerator WaitSeconds(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Plays the real game in Play Mode: the Innocent slider and Start button drive PlayerManager,
// MapManager and the NPC AI. Checks the generated objects, colliders and reachability, lets
// the AI play, and (optionally) appends a CSV row of measurements for research comparisons.
// Batch: -executeMethod MansionGameplayVerification.RunBatch
//   [-mansionSeed N] [-mansionNpcs 5] [-mansionBackend BehaviorTree|Scasp] [-mansionPolicy Reasoned|Baseline]
//   [-mansionSkip 2] [-mansionExplain] [-mansionScenario full|threat|barricade|final_lever]
//   [-mansionSeconds 240] [-mansionMinSeconds 120] [-mansionTimeScale 3]
//   [-mansionShot <png>] [-mansionCsv <csv>] [-mansionSamples <csv>]
[InitializeOnLoad]
public static class MansionGameplayVerification
{
    private const string Key = "MansionGameplayVerification.";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const float Cell = 0.25f;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private enum Stage { WaitForPlay, Spawned, Running }

    private static Stage stage;
    private static int startFrame, checks, failures;
    private static float gameStart, nextReport, winTime = -1f;
    private static long memoryAtStart;
    private static MapManager map;
    private static PlayerManager players;
    private static LeverManager levers;
    private static readonly Dictionary<Window, float> lowestHealth = new Dictionary<Window, float>();
    private static readonly HashSet<Window> repaired = new HashSet<Window>();
    private static readonly List<string> leverLog = new List<string>();
    private static readonly Dictionary<Agent, Vector3> lastPosition = new Dictionary<Agent, Vector3>();
    private static readonly Dictionary<Agent, float> travelled = new Dictionary<Agent, float>();
    private static readonly Dictionary<Agent, int> lastHealth = new Dictionary<Agent, int>();
    private static readonly List<string> firstAgentActions = new List<string>();
    private static readonly List<Rect> wallRects = new List<Rect>();
    private static int maxZombies, lastActivated, hits, escapes, zombieInWall;
    private static float firstThreatResponse = -1f, firstRepairTime = -1f;

    static MansionGameplayVerification()
    {
        if (SessionState.GetBool(Key + "Active", false))
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    [MenuItem("Tools/Mansion/Verify Gameplay (Play Mode)")]
    public static void RunMenu() => Begin(false);

    public static void RunBatch() => Begin(true);

    private static void Begin(bool exit)
    {
        SessionState.SetBool(Key + "Active", true);
        SessionState.SetBool(Key + "Exit", exit);
        foreach (var setting in new (string name, string fallback)[] {
            ("Seed", "12345"), ("Npcs", "5"), ("Backend", "BehaviorTree"), ("Policy", "Reasoned"), ("Skip", "2"),
            ("Scenario", "full"), ("Seconds", "240"), ("MinSeconds", "120"), ("TimeScale", "3"),
            ("Shot", ""), ("Csv", ""), ("Samples", "") })
            SessionState.SetString(Key + setting.name, Argument("-mansion" + setting.name) ?? setting.fallback);
        SessionState.SetBool(Key + "Explain", Array.IndexOf(Environment.GetCommandLineArgs(), "-mansionExplain") >= 0);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        stage = Stage.WaitForPlay;
        winTime = -1f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static string Setting(string name) => SessionState.GetString(Key + name, "");
    private static float Number(string name) => float.Parse(Setting(name), CultureInfo.InvariantCulture);
    private static bool Scasp => Setting("Backend").Equals("Scasp", StringComparison.OrdinalIgnoreCase);

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;
        try
        {
            switch (stage)
            {
                case Stage.WaitForPlay:
                    if (Time.frameCount < 5) return;
                    StartGame();
                    startFrame = Time.frameCount;
                    stage = Stage.Spawned;
                    break;
                case Stage.Spawned:
                    if (Time.frameCount < startFrame + 3) return;
                    CheckGeneratedLevel();
                    ApplyScenario();
                    Time.timeScale = Number("TimeScale");
                    gameStart = Time.time;
                    nextReport = gameStart + 20f;
                    memoryAtStart = GC.GetTotalMemory(false);
                    stage = Stage.Running;
                    break;
                case Stage.Running:
                    Observe();
                    bool won = levers.ActivatedLevers >= levers.TotalLevers && levers.winScreen != null && levers.winScreen.activeSelf;
                    if (won && winTime < 0f) winTime = Time.time - gameStart;
                    // Keep playing after the win to exercise barricades, zombies and fleeing.
                    float elapsed = Time.time - gameStart;
                    bool everyoneDown = players.players.TrueForAll(a => a == null || a.Health <= 0);
                    if ((won && elapsed >= Number("MinSeconds")) || everyoneDown || elapsed > Number("Seconds"))
                        Finish();
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            failures++;
            Finish();
        }
    }

    private static void StartGame()
    {
        int npcs = (int)Number("Npcs");
        UnityEngine.Random.InitState((int)Number("Seed"));

        players = UnityEngine.Object.FindAnyObjectByType<PlayerManager>();
        map = UnityEngine.Object.FindAnyObjectByType<MapManager>();
        levers = UnityEngine.Object.FindAnyObjectByType<LeverManager>();

        // The experiment settings PlayerManager exposes in the Inspector.
        Set(players, "decisionBackend", Enum.Parse(typeof(NpcDecisionBackend), Setting("Backend"), true));
        Set(players, "scaspPolicy", Enum.Parse(typeof(ScaspPolicy), Setting("Policy"), true));
        Set(players, "scaspUnchangedSkip", Number("Skip"));
        Set(players, "explainScaspDecisions", SessionState.GetBool(Key + "Explain", false));

        // Configure through the real start screen, as a player would.
        var slider = Array.Find(UnityEngine.Object.FindObjectsByType<Slider>(FindObjectsInactive.Include), s => s.name == "InnocentSlider");
        slider.value = npcs;
        Check(GameSettings.InnocentCount == npcs, $"Innocent slider configures {npcs} NPCs (slider range {slider.minValue}-{slider.maxValue})");

        Array.Find(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include), b => b.name == "StartButton").onClick.Invoke();

        // Same frame as the click: before any NPC has taken a step. Rejected placements
        // keep their clone name until Destroy completes at the end of the frame.
        foreach (LeverScript lever in UnityEngine.Object.FindObjectsByType<LeverScript>())
        {
            if (!lever.name.StartsWith("lever_")) continue;
            float nearest = float.MaxValue;
            foreach (Agent npc in players.players)
                nearest = Mathf.Min(nearest, Vector2.Distance(npc.transform.position, lever.transform.position));
            Check(nearest >= 3f, $"{lever.name} starts {nearest:F1} units from the nearest NPC, so it must be searched for");
            lever.Activated += source => leverLog.Add($"{source.name} at {Time.time - gameStart:F1}s");
        }
        if (players.players.Count > 0)
            players.players[0].GetComponent<AiAction>().ActionExecuted += (action, target) =>
            {
                if (firstAgentActions.Count < 400)
                    firstAgentActions.Add($"{Time.time - gameStart:F1}:{action}{(target.Length > 0 ? "(" + target + ")" : "")}");
            };
        Debug.Log($"GAMEPLAY: started seed {Setting("Seed")}, backend {Setting("Backend")}, policy {Setting("Policy")}, scenario {Setting("Scenario")}");
    }

    private static void CheckGeneratedLevel()
    {
        Physics2D.SyncTransforms();
        int npcs = (int)Number("Npcs");

        var walls = new List<Collider2D>();
        var blockers = new List<Collider2D>();
        foreach (Collider2D collider in UnityEngine.Object.FindObjectsByType<Collider2D>())
        {
            if (collider.isTrigger) continue;
            if (collider.name.StartsWith(map.wallPrefab.name)) walls.Add(collider);
            if (collider.name == "Window Blocker") blockers.Add(collider);
        }
        Check(walls.Count == map.BuiltWalls.Count && walls.Count > 0, $"{walls.Count} wall colliders match the {map.BuiltWalls.Count} generated wall pieces");
        bool collidersMatch = true;
        wallRects.Clear();
        foreach (MapManager.WallPiece piece in map.BuiltWalls)
        {
            wallRects.Add(piece.Footprint);
            collidersMatch &= walls.Exists(w => Vector2.Distance(w.bounds.center, piece.Footprint.center) < 0.02f && Vector2.Distance(w.bounds.size, piece.Footprint.size) < 0.06f);
        }
        Check(collidersMatch, "Every wall collider covers exactly its drawn footprint");
        Check(blockers.Count == map.numWindows, $"Each of the {map.numWindows} barricade openings is closed to NPCs by a solid blocker ({blockers.Count})");
        Check(MansionNavigation.IsBuilt, "Shared navigation grid is built for NPCs and zombies");

        float area = 0f;
        foreach (Rect room in MapManager.Rooms) area += room.width * room.height;
        foreach (Rect door in MapManager.Doorways) area += door.width * door.height;
        Check(area >= 330f && area <= 350f, $"Walkable interior is {area:F0} square units (grand hall alone {MapManager.Interior.width * MapManager.Interior.height:F0})");

        Check(players.players.Count == npcs, $"PlayerManager spawned {players.players.Count} of {npcs} NPCs");
        Vector2 body = new Vector2(0.8f, 1.0f);
        for (int i = 0; i < players.players.Count; i++)
        {
            Agent npc = players.players[i];
            Vector2 p = npc.transform.position;
            Check(MapManager.IsInsideMansion(p - body / 2) && MapManager.IsInsideMansion(p + body / 2), $"{npc.name} spawned inside the mansion at {p}");
            Check(!TouchesWall(p, body, walls), $"{npc.name} does not overlap any wall");
            for (int j = 0; j < i; j++)
                Check(Vector2.Distance(p, players.players[j].transform.position) >= 1f, $"{npc.name} has room from {players.players[j].name}");
            lastPosition[npc] = npc.transform.position;
            travelled[npc] = 0f;
            lastHealth[npc] = npc.Health;
        }

        // Reachability: flood fill NPC-sized positions over the whole house.
        Rect house = MapManager.BuildingBounds;
        int columns = Mathf.RoundToInt(house.width / Cell), rows = Mathf.RoundToInt(house.height / Cell);
        var free = new bool[columns, rows];
        int freeCount = 0;
        var solid = new List<Collider2D>(walls);
        solid.AddRange(blockers);
        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2 p = CellCenter(x, y);
                free[x, y] = MapManager.IsInsideMansion(p) && !TouchesWall(p, body * 0.95f, solid);
                if (free[x, y]) freeCount++;
            }
        var reached = new bool[columns, rows];
        var queue = new Queue<Vector2Int>();
        Vector2Int start = ToCell(players.players[0].transform.position);
        queue.Enqueue(start);
        reached[start.x, start.y] = true;
        int reachedCount = 0;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            reachedCount++;
            foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int n = c + d;
                if (n.x >= 0 && n.y >= 0 && n.x < columns && n.y < rows && free[n.x, n.y] && !reached[n.x, n.y])
                {
                    reached[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
        }
        Check(reachedCount == freeCount, $"All {freeCount} open NPC positions in hall, corridors and wings form one connected floor (reached {reachedCount})");
        Debug.Log($"GAMEPLAY: walkable NPC-center area {freeCount * Cell * Cell:F1} units^2; interior {area:F0} units^2");
        foreach (Rect room in new[] { MapManager.WestWing, MapManager.EastWing, MapManager.WestCorridor, MapManager.EastCorridor })
        {
            Vector2Int c = ToCell(room.center);
            Check(reached[c.x, c.y], $"The room centered at {room.center} is reachable from the spawn");
        }

        foreach (Agent npc in players.players)
        {
            Vector2Int c = ToCell(npc.transform.position);
            Check(reached[c.x, c.y], $"{npc.name} starts on the connected floor");
        }

        var spawnedLevers = new List<LeverScript>(UnityEngine.Object.FindObjectsByType<LeverScript>());
        spawnedLevers.RemoveAll(l => !l.name.StartsWith("lever_"));
        Check(spawnedLevers.Count == map.numLevers && levers.TotalLevers == map.numLevers && levers.ActivatedLevers == 0,
            $"{spawnedLevers.Count} levers spawned; LeverManager requires {levers.TotalLevers} (MapManager numLevers {map.numLevers})");
        foreach (LeverScript lever in spawnedLevers)
        {
            Bounds touch = lever.GetComponent<Collider2D>().bounds;
            Check(MapManager.IsInsideMansion(lever.transform.position) && !walls.Exists(w => w.bounds.Intersects(touch)), $"{lever.name} at {(Vector2)lever.transform.position} is inside the mansion and clear of walls");
            Check(AnyReached(reached, columns, rows, p => new Rect(p - body / 2, body).Overlaps(Rect.MinMaxRect(touch.min.x, touch.min.y, touch.max.x, touch.max.y))),
                $"{lever.name} can be touched from the connected floor");
        }

        Window[] windows = UnityEngine.Object.FindObjectsByType<Window>();
        Check(windows.Length == map.numWindows, $"{windows.Length} barricades spawned (numWindows {map.numWindows})");
        foreach (Window window in windows)
        {
            Bounds trigger = window.GetComponent<Collider2D>().bounds;
            Rect triggerRect = Rect.MinMaxRect(trigger.min.x, trigger.min.y, trigger.max.x, trigger.max.y);
            Check(!walls.Exists(w => w.OverlapPoint(window.transform.position)), $"{window.name} sits in an opening, not inside a wall");
            Check(AnyReached(reached, columns, rows, p => new Rect(p - body / 2 - Vector2.one * Cell, body + Vector2.one * 2 * Cell).Overlaps(triggerRect)),
                $"{window.name} can be reached and defended from inside");
            lowestHealth[window] = window.health;
        }

        var decorator = map.GetComponent<MansionDecorator>();
        Check(decorator != null && decorator.Root != null && decorator.Root.GetComponentsInChildren<Collider2D>(true).Length == 0,
            "Mansion decoration has no colliders, so it cannot block movement");

        var camera = Camera.main;
        Check(camera.GetComponent<MansionCamera>() != null, "Gameplay camera has the overview/follow controller");
        float overview16x9 = Mathf.Max(house.height / 2f, house.width / 2f / (16f / 9f)) + 0.75f;
        float characterPixels = 1080f / (2f * overview16x9) * 1.1f;
        Check(characterPixels >= 45f, $"In the 16:9 overview (size {overview16x9:F1}) an NPC is {characterPixels:F0} px tall at 1080p");

        if (Scasp)
            foreach (Agent npc in players.players)
                Check(npc.GetComponent<ScaspStrategy>().enabled && !npc.GetComponent<BehaviorTree>().enabled, $"{npc.name} is controlled by ScaspStrategy, with the Behavior Tree disabled");
        else
            foreach (Agent npc in players.players)
                Check(npc.GetComponent<BehaviorTree>().enabled && !npc.GetComponent<ScaspStrategy>().enabled, $"{npc.name} is controlled by the Behavior Tree, with ScaspStrategy disabled");
    }

    // Controlled end-to-end situations, identical for both backends.
    private static void ApplyScenario()
    {
        string scenario = Setting("Scenario");
        if (scenario == "full")
            return;

        // One agent, no stray zombies, healthy barricades: only the scenario matters.
        foreach (Zombie zombie in UnityEngine.Object.FindObjectsByType<Zombie>())
            UnityEngine.Object.Destroy(zombie.gameObject);
        Window[] windows = UnityEngine.Object.FindObjectsByType<Window>();
        foreach (Window window in windows)
            window.health = 100f;
        Agent agent = players.players[0];
        var spawnedLevers = new List<LeverScript>(UnityEngine.Object.FindObjectsByType<LeverScript>());
        spawnedLevers.RemoveAll(l => !l.name.StartsWith("lever_"));
        spawnedLevers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        GameObject zombiePrefab = windows[0].zombiePrefab;

        switch (scenario)
        {
            case "threat":
                // A lever 4.5 units away; a zombie stands between the agent and the lever.
                agent.transform.position = new Vector3(-3f, -2f, agent.transform.position.z);
                spawnedLevers[0].transform.position = new Vector3(1.5f, -2f, 0f);
                spawnedLevers[1].transform.position = new Vector3(14f, 4f, 0f);
                UnityEngine.Object.Instantiate(zombiePrefab, new Vector3(-1.4f, -2.2f, 0f), Quaternion.identity);
                break;
            case "barricade":
                // A failing barricade next to the agent; the levers are far away in the wings.
                Window near = windows[0];
                near.health = 30f;
                Vector2 inward = Vector2.zero;
                foreach (Vector2 d in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
                    if (MapManager.IsInsideMansion((Vector2)near.transform.position + d * 0.6f)) inward = d;
                agent.transform.position = (Vector2)near.transform.position + inward * 2.2f;
                spawnedLevers[0].transform.position = new Vector3(-14f, 4f, 0f);
                spawnedLevers[1].transform.position = new Vector3(14f, 4f, 0f);
                break;
            case "final_lever":
                // One lever already pulled; the agent is beside the last one as a zombie closes in.
                spawnedLevers[1].isOn = true;
                levers.LeverActivated();
                spawnedLevers[1].transform.position = new Vector3(14f, 4f, 0f);
                agent.transform.position = new Vector3(-3f, -2f, agent.transform.position.z);
                spawnedLevers[0].transform.position = new Vector3(-2.4f, -2f, 0f);
                UnityEngine.Object.Instantiate(zombiePrefab, new Vector3(-4.8f, -2f, 0f), Quaternion.identity);
                break;
        }
        for (int i = 1; i < players.players.Count; i++)
            players.players[i].SetHealth(0);
        Physics2D.SyncTransforms();
        lastPosition[agent] = agent.transform.position;
        Debug.Log($"GAMEPLAY: scenario {scenario} applied");
    }

    private static void Observe()
    {
        string shot = Setting("Shot");
        if (shot.Length > 0 && Time.time - gameStart > 6f)
        {
            SessionState.SetString(Key + "Shot", "");
            Capture(shot);
        }

        foreach (Window window in new List<Window>(lowestHealth.Keys))
        {
            if (window == null) continue;
            if (window.health < lowestHealth[window]) lowestHealth[window] = window.health;
            else if (window.health > lowestHealth[window] + 1f && repaired.Add(window) && firstRepairTime < 0f)
                firstRepairTime = Time.time - gameStart;
        }
        Zombie[] zombies = UnityEngine.Object.FindObjectsByType<Zombie>();
        maxZombies = Mathf.Max(maxZombies, zombies.Length);
        foreach (Zombie zombie in zombies)
            if (wallRects.Exists(w => w.Contains(zombie.transform.position)))
                zombieInWall++;

        for (int i = 0; i < players.players.Count; i++)
        {
            Agent npc = players.players[i];
            if (npc == null || npc.Health <= 0) continue;
            Vector2 p = npc.transform.position;
            travelled[npc] += Vector3.Distance(lastPosition[npc], npc.transform.position);
            lastPosition[npc] = npc.transform.position;
            if (npc.Health < lastHealth[npc]) hits += lastHealth[npc] - npc.Health;
            lastHealth[npc] = npc.Health;
            // Outside the house, beyond a barricade's reach, is an escape.
            if (!MapManager.IsInsideMansion(p) && !NearOpening(p))
                escapes++;
            if (i == 0 && firstThreatResponse < 0f && npc.GetComponent<AiAction>().CurrentAction == "run_from")
                firstThreatResponse = Time.time - gameStart;
        }
        if (levers.ActivatedLevers != lastActivated)
        {
            lastActivated = levers.ActivatedLevers;
            Debug.Log($"GAMEPLAY: lever {lastActivated}/{levers.TotalLevers} activated at {Time.time - gameStart:F1}s game time");
        }
        if (Time.time > nextReport)
        {
            nextReport += 20f;
            Debug.Log($"GAMEPLAY: t={Time.time - gameStart:F0}s levers {levers.ActivatedLevers}/{levers.TotalLevers}, zombies {maxZombies}, alive {players.players.FindAll(a => a != null && a.Health > 0).Count}");
        }
    }

    private static bool NearOpening(Vector2 p)
    {
        foreach (Rect opening in map.WindowOpenings)
            if (Rect.MinMaxRect(opening.xMin - 0.6f, opening.yMin - 0.6f, opening.xMax + 0.6f, opening.yMax + 0.6f).Contains(p))
                return true;
        return false;
    }

    private static void Finish()
    {
        float elapsed = Time.time - gameStart;
        Time.timeScale = 1f;
        bool won = winTime >= 0f;
        string scenario = Setting("Scenario");

        if (scenario == "full")
            Check(won, won ? $"NPCs activated all {levers.TotalLevers} levers and the win screen appeared after {winTime:F1}s game time"
                           : $"Win screen not reached in {elapsed:F1}s (levers {levers.ActivatedLevers}/{levers.TotalLevers})");
        Check(escapes == 0, $"No NPC left the house through a window or wall ({escapes} frames outside)");
        Check(zombieInWall == 0, $"No zombie walked through a wall ({zombieInWall} zombie-frames inside walls)");

        // Proof of control: every command AiAction executed came from a correlated s(CASP) reply.
        int requests = 0, responses = 0, executed = 0, commands = 0, stale = 0, errors = 0, timeouts = 0, skipped = 0;
        var roundTrip = new List<float>();
        var solver = new List<float>();
        var decision = new List<float>();
        var actions = new Dictionary<string, int>();
        foreach (Agent npc in players.players)
        {
            var stats = AiDiagnostics.For(npc.name);
            requests += stats.Requests; responses += stats.Responses; executed += stats.Executed;
            stale += stats.Stale; errors += stats.Errors; timeouts += stats.Timeouts; skipped += stats.Skipped;
            roundTrip.AddRange(stats.RoundTripMs); solver.AddRange(stats.SolverMs); decision.AddRange(stats.DecisionMs);
            foreach (var pair in stats.Actions) { actions.TryGetValue(pair.Key, out int n); actions[pair.Key] = n + pair.Value; }
            int npcCommands = npc.GetComponent<AiAction>().ExecuteCount;
            commands += npcCommands;
            if (Scasp)
                Check(npcCommands == stats.Executed && (npc.Health <= 0 || stats.Responses > 0),
                    $"{npc.name}: all {npcCommands} executed commands came from {stats.Responses} correlated s(CASP) replies to {stats.Requests} requests");
        }
        if (Scasp)
            Check(responses > 0 && errors == 0 && timeouts == 0, $"s(CASP) answered {responses}/{requests} requests ({skipped} skipped as unchanged, {stale} stale, {errors} errors, {timeouts} timeouts)");
        ScenarioChecks(scenario);

        Debug.Log("GAMEPLAY: lever activations: " + string.Join(", ", leverLog));
        Debug.Log("GAMEPLAY: actions: " + string.Join(", ", Describe(actions)));
        Debug.Log("GAMEPLAY: first agent: " + string.Join(" ", firstAgentActions.GetRange(0, Mathf.Min(40, firstAgentActions.Count))));
        Debug.Log($"GAMEPLAY: barricades repaired {repaired.Count}/{lowestHealth.Count}; zombies max {maxZombies}; hits {hits}; alive {players.players.FindAll(a => a != null && a.Health > 0).Count}/{players.players.Count}");
        if (Scasp)
            Debug.Log($"GAMEPLAY: s(CASP) round trip p50 {AiDiagnostics.Percentile(roundTrip, 0.5f):F0} / p95 {AiDiagnostics.Percentile(roundTrip, 0.95f):F0} / max {AiDiagnostics.Percentile(roundTrip, 1f):F0} ms; solver p50 {AiDiagnostics.Percentile(solver, 0.5f):F0} ms");
        else
            Debug.Log($"GAMEPLAY: Behavior Tree decision p50 {AiDiagnostics.Percentile(decision, 0.5f):F3} / p95 {AiDiagnostics.Percentile(decision, 0.95f):F3} / max {AiDiagnostics.Percentile(decision, 1f):F3} ms");
        Debug.Log(failures == 0 ? $"MANSION GAMEPLAY VERIFIED: {checks} checks passed." : $"MANSION GAMEPLAY FAILED: {failures} of {checks} checks failed.");

        WriteCsv(elapsed, won, requests, responses, skipped, stale, errors, timeouts, executed, commands, roundTrip, solver, decision, actions);

        EditorApplication.update -= Tick;
        bool exit = SessionState.GetBool(Key + "Exit", false);
        SessionState.SetBool(Key + "Active", false);
        if (exit)
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }

    // Outcomes are recorded for both backends; s(CASP) expectations follow from its rules.
    private static void ScenarioChecks(string scenario)
    {
        bool reasoned = Scasp && Setting("Policy").Equals("Reasoned", StringComparison.OrdinalIgnoreCase);
        switch (scenario)
        {
            case "threat":
                Debug.Log($"GAMEPLAY: scenario threat: first run_from at {firstThreatResponse:F1}s; levers {levers.ActivatedLevers}");
                Check(firstThreatResponse >= 0f && firstThreatResponse < 2f, $"The agent fled the zombie within 2 s ({firstThreatResponse:F1}s)");
                break;
            case "barricade":
                Debug.Log($"GAMEPLAY: scenario barricade: first repair at {firstRepairTime:F1}s");
                if (reasoned)
                    Check(firstRepairTime >= 0f && firstRepairTime < 10f, $"Reasoned s(CASP) repairs the failing barricade nearby before it breaks ({firstRepairTime:F1}s)");
                break;
            case "final_lever":
                Debug.Log($"GAMEPLAY: scenario final_lever: won {winTime >= 0f} at {winTime:F1}s, first run_from at {firstThreatResponse:F1}s");
                if (reasoned)
                    Check(winTime >= 0f, "Reasoned s(CASP) finishes the final lever instead of fleeing, and wins");
                break;
        }
    }

    private static IEnumerable<string> Describe(Dictionary<string, int> actions)
    {
        foreach (var pair in actions)
            yield return $"{pair.Key} {pair.Value}";
    }

    private static void WriteCsv(float elapsed, bool won, int requests, int responses, int skipped, int stale, int errors, int timeouts,
        int executed, int commands, List<float> roundTrip, List<float> solver, List<float> decision, Dictionary<string, int> actions)
    {
        string runId = $"{Setting("Backend")}-{(Scasp ? Setting("Policy") : "tree")}-{Setting("Scenario")}-{Setting("Npcs")}-{Setting("Seed")}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        string path = Setting("Csv");
        if (path.Length > 0)
        {
            string[] actionNames = { "hunt_lever", "move_to_lever", "pull_lever", "run_from", "move_to_barricade", "hold_barricade", "wait" };
            string header = "run_id,backend,policy,scenario,seed,agents,time_scale,unchanged_skip,explain,game_seconds,won,win_time,levers_pulled,levers_total," +
                            "barricades_repaired,barricades,first_repair,first_flee,zombies_max,hits,alive_end,distance,escapes,zombie_in_wall," +
                            "requests,responses,skipped,stale,errors,timeouts,executed,commands,decisions_per_real_s," +
                            "rt_p50,rt_p90,rt_p95,rt_p99,rt_max,solver_p50,solver_p95,bt_p50,bt_p95,bt_max,memory_delta_mb," + string.Join(",", actionNames);
            float distance = 0f;
            foreach (float d in travelled.Values) distance += d;
            float realSeconds = Mathf.Max(0.001f, elapsed / Number("TimeScale"));
            var row = new List<string>
            {
                runId, Setting("Backend"), Scasp ? Setting("Policy") : "", Setting("Scenario"), Setting("Seed"), Setting("Npcs"),
                Setting("TimeScale"), Scasp ? Setting("Skip") : "", Scasp ? SessionState.GetBool(Key + "Explain", false).ToString() : "",
                F(elapsed), won.ToString(), won ? F(winTime) : "", levers.ActivatedLevers.ToString(), levers.TotalLevers.ToString(),
                repaired.Count.ToString(), lowestHealth.Count.ToString(), F(firstRepairTime), F(firstThreatResponse), maxZombies.ToString(), hits.ToString(),
                players.players.FindAll(a => a != null && a.Health > 0).Count.ToString(), F(distance), escapes.ToString(), zombieInWall.ToString(),
                requests.ToString(), responses.ToString(), skipped.ToString(), stale.ToString(), errors.ToString(), timeouts.ToString(),
                executed.ToString(), commands.ToString(), F((Scasp ? responses : decision.Count) / realSeconds),
                P(roundTrip, 0.5f), P(roundTrip, 0.9f), P(roundTrip, 0.95f), P(roundTrip, 0.99f), P(roundTrip, 1f), P(solver, 0.5f), P(solver, 0.95f),
                P(decision, 0.5f), P(decision, 0.95f), P(decision, 1f), F((GC.GetTotalMemory(false) - memoryAtStart) / 1048576f)
            };
            foreach (string action in actionNames)
                row.Add(actions.TryGetValue(action, out int n) ? n.ToString() : "0");
            File.AppendAllText(path, (File.Exists(path) ? "" : header + "\n") + string.Join(",", row) + "\n");
            Debug.Log("GAMEPLAY: results appended to " + path);
        }

        string samples = Setting("Samples");
        if (samples.Length > 0)
        {
            var text = new StringBuilder(File.Exists(samples) ? "" : "run_id,backend,kind,ms\n");
            foreach (float v in roundTrip) text.Append($"{runId},{Setting("Backend")},round_trip,{F(v)}\n");
            foreach (float v in solver) text.Append($"{runId},{Setting("Backend")},solver,{F(v)}\n");
            foreach (float v in decision) text.Append($"{runId},{Setting("Backend")},bt_decision,{v.ToString("F4", CultureInfo.InvariantCulture)}\n");
            File.AppendAllText(samples, text.ToString());
        }
    }

    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string P(List<float> values, float p) => values.Count == 0 ? "" : AiDiagnostics.Percentile(values, p).ToString("F3", CultureInfo.InvariantCulture);

    private static void Capture(string path)
    {
        Camera camera = Camera.main;
        var target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.Destroy(image);
        target.Release();
        Debug.Log("GAMEPLAY: Play Mode frame saved to " + path);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);

    private static bool TouchesWall(Vector2 center, Vector2 size, List<Collider2D> walls)
    {
        foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, size, 0f))
            if (walls.Contains(hit))
                return true;
        return false;
    }

    private static bool AnyReached(bool[,] reached, int columns, int rows, Func<Vector2, bool> touches)
    {
        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
                if (reached[x, y] && touches(CellCenter(x, y)))
                    return true;
        return false;
    }

    private static Vector2 CellCenter(int x, int y) => new Vector2(MapManager.BuildingBounds.xMin + (x + 0.5f) * Cell, MapManager.BuildingBounds.yMin + (y + 0.5f) * Cell);

    private static Vector2Int ToCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x - MapManager.BuildingBounds.xMin) / Cell), Mathf.FloorToInt((p.y - MapManager.BuildingBounds.yMin) / Cell));

    private static void Check(bool condition, string description)
    {
        checks++;
        if (condition)
            Debug.Log("PASS: " + description);
        else
        {
            failures++;
            Debug.LogError("FAIL: " + description);
        }
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

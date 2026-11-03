using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;

public enum NpcDecisionBackend { BehaviorTree, Scasp }

public class PlayerManager : MonoBehaviour
{
    [Header("NPC Settings")]
    [SerializeField, Min(0)] private int npcCountOverride = 0; // 0 = use GameSettings
    [SerializeField] private float spawnSpacing = 2f;
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;
    [SerializeField, Min(0.1f)] private float decisionInterval = 0.5f;
    [SerializeField] private NpcDecisionBackend decisionBackend = NpcDecisionBackend.BehaviorTree;
    [SerializeField] private LeverManager leverManager;

    [Header("AI Experiment")]
    [Tooltip("s(CASP) rule set: Baseline = original rules (like-for-like with the Behavior Tree); Reasoned = defeasible policy.")]
    [SerializeField] private ScaspPolicy scaspPolicy = ScaspPolicy.Reasoned;
    [Tooltip("Ask s(CASP) for its justification tree and defeated alternatives (slower; for research and debugging).")]
    [SerializeField] private bool explainScaspDecisions;
    [Tooltip("s(CASP) skips a request while nothing the rules can distinguish has changed, for at most this many seconds (0 = always ask).")]
    [SerializeField, Min(0f)] private float scaspUnchangedSkip = 2f;
    [SerializeField] private AiTraceLevel traceLevel = AiTraceLevel.Summary;
    [Tooltip("On-screen AI status; on the start screen, B switches backend and P the s(CASP) policy.")]
    [SerializeField] private bool showAiStatus = true;

    [Header("NPC Prefabs")]
    public GameObject playerPrefab;
    public List<Agent> players = new List<Agent>();
    private bool gameStarted;

    public NpcDecisionBackend DecisionBackend { get => decisionBackend; set { if (!gameStarted) decisionBackend = value; } }
    public ScaspPolicy ScaspDecisionPolicy { get => scaspPolicy; set { if (!gameStarted) scaspPolicy = value; } }
    public bool GameStarted => gameStarted;

    private void Awake()
    {
        // Experiment overrides for batch runs: -aiBackend Scasp|BehaviorTree,
        // -scaspPolicy Baseline|Reasoned, -scaspExplain, -scaspSkip <s>, -aiTrace Off|Summary|Verbose.
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string value = i + 1 < args.Length ? args[i + 1] : "";
            if (args[i] == "-aiBackend" && System.Enum.TryParse(value, true, out NpcDecisionBackend backend)) decisionBackend = backend;
            if (args[i] == "-scaspPolicy" && System.Enum.TryParse(value, true, out ScaspPolicy policy)) scaspPolicy = policy;
            if (args[i] == "-scaspExplain") explainScaspDecisions = true;
            if (args[i] == "-scaspSkip" && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float skip)) scaspUnchangedSkip = skip;
            if (args[i] == "-aiTrace" && System.Enum.TryParse(value, true, out AiTraceLevel level)) traceLevel = level;
        }
        AiDiagnostics.Level = traceLevel;

        if (showAiStatus && !Application.isBatchMode)
            gameObject.AddComponent<AiStatusOverlay>().Bind(this);
    }

    public void StartGameClicked()
    {
        if (gameStarted) return;
        if (playerPrefab == null || playerPrefab.GetComponent<Agent>() == null)
        {
            Debug.LogError("Assign an NPC prefab containing Agent.cs.", this);
            return;
        }
        if (leverManager == null) leverManager = FindAnyObjectByType<LeverManager>();
        if (decisionBackend == NpcDecisionBackend.BehaviorTree && leverManager == null)
        {
            Debug.LogError("Assign the map's LeverManager before spawning tree agents.", this);
            return;
        }
        
        int count = npcCountOverride > 0 ? npcCountOverride : GameSettings.InnocentCount;
        if (count <= 0) return;
        List<Vector2> spawns = NpcSpawnPositions(count);
        if (spawns.Count < count)
            Debug.LogWarning($"Only {spawns.Count} of {count} NPCs fit on open mansion floor.", this);
        count = spawns.Count;
        gameStarted = true;
        AiDiagnostics.Level = traceLevel;
        AiDiagnostics.Log(decisionBackend == NpcDecisionBackend.Scasp
            ? $"AI backend for this run: s(CASP), {scaspPolicy} policy, {count} NPCs, explanations {(explainScaspDecisions ? "on" : "off")}, unchanged-skip {scaspUnchangedSkip} s"
            : $"AI backend for this run: Behavior Tree, {count} NPCs (s(CASP) not used)", this);
        StartCoroutine(ReportAiStatus());

        // The inactive parent prevents prefab clients from connecting before we select a backend.
        var staging = new GameObject("NPC initialization");
        staging.SetActive(false);
        try
        {
            for (int i = 0; i < count; i++)
            {
                GameObject npc = Instantiate(playerPrefab, staging.transform);
                npc.SetActive(false);
                Agent agent = npc.GetComponent<Agent>();
                Vector2 position = spawns[i];
                agent.Initialize("innocent", position.x, position.y);
                // Assign each NPC a unique Prolog actor ID.
                string actorId = $"player{i + 1}";
                npc.name = actorId;
                // Other agents see this NPC under the same ID, whichever backend controls it.
                agent.SetAgentId(actorId);

                // Only one backend controls a clone, regardless of prefab Inspector assignment.
                foreach (MonoBehaviour component in npc.GetComponents<MonoBehaviour>())
                    if (component is IAgentStrategy) component.enabled = false;
                foreach (ScaspClient existingClient in npc.GetComponentsInChildren<ScaspClient>(true))
                    existingClient.enabled = false;

                ScaspClient client;

                npc.transform.SetParent(null, true);
                npc.SetActive(true);


                if (decisionBackend == NpcDecisionBackend.BehaviorTree)
                {
                    // THIS is where the snippet from INTEGRATION.md belongs.

                    BehaviorTree tree = npc.GetComponent<BehaviorTree>();
                    if (tree == null)
                        tree = npc.AddComponent<BehaviorTree>();

                    tree.enabled = true;
                    agent.SetStrategy(tree, "behavior_tree");

                    tree.SetInitialValues(agent.GetComponent<AiAction>(), leverManager);

                    var treeStats = AiDiagnostics.For(actorId);
                    treeStats.Backend = "behavior_tree";
                    agent.GetComponent<AiAction>().ActionExecuted += (action, target) => { treeStats.Executed++; treeStats.CountAction(action); };
                    AiDiagnostics.Log($"{actorId}: strategy BehaviorTree", npc);

                    if (tree != null)
                        StartCoroutine(RunNpcDecisionLoop(agent, null));
                    else 
                        Debug.LogWarning($"{actorId}: Missing ScaspStrategy or ScaspClient.", npc);
                }
                else
                {
                    ScaspStrategy strategy = npc.GetComponent<ScaspStrategy>();
                    if (strategy == null) 
                        strategy = npc.AddComponent<ScaspStrategy>();
                    client = npc.GetComponent<ScaspClient>();
                    strategy.SetActorId(actorId);
                    strategy.Configure(scaspPolicy, explainScaspDecisions, scaspUnchangedSkip);
                    strategy.enabled = true;
                    client.enabled = true;
                    agent.SetStrategy(strategy, "scasp");
                    AiDiagnostics.Log($"{actorId}: strategy ScaspStrategy ({scaspPolicy} policy) via {client.ServerUrl}", npc);
                    // Refresh each NPC's observations and decision on a regular game-time interval.
                    if (strategy != null && client != null) 
                        StartCoroutine(RunNpcDecisionLoop(agent, client));
                    else 
                        Debug.LogWarning($"{actorId}: Missing ScaspStrategy or ScaspClient.", npc);
                }

                players.Add(agent);

                Debug.Log($"Spawned innocent NPC: {actorId} at {position}", npc);

            }
        }
        finally { Destroy(staging); }
    }

    // Valid starting points on open mansion floor, filled outward from spawnPosition
    // in rows spawnSpacing apart. The Start button spawns NPCs before MapManager builds
    // the walls, so candidates are checked against the wall layout itself as well as
    // any colliders that already exist.
    public List<Vector2> NpcSpawnPositions(int count)
    {
        var spawns = new List<Vector2>();
        if (count <= 0)
            return spawns;

        Vector2 size = NpcFootprint();
        float spacing = Mathf.Max(spawnSpacing, size.x, size.y);
        MapManager map = FindAnyObjectByType<MapManager>();
        Rect interior = MapManager.Interior;
        List<MapManager.WallPiece> walls = MapManager.Layout(map == null || map.advancedMap);

        var candidates = new List<Vector2>();
        int columns = Mathf.CeilToInt(interior.width / spacing);
        int rows = Mathf.CeilToInt(interior.height / spacing);
        for (int row = -rows; row <= rows; row++)
            for (int column = -columns; column <= columns; column++)
                candidates.Add(spawnPosition + new Vector2(column * spacing, row * spacing));

        // Fill the spawn row first, nearest to its center, then the closest rows.
        candidates.Sort((a, b) =>
        {
            int byRow = Mathf.Abs(a.y - spawnPosition.y).CompareTo(Mathf.Abs(b.y - spawnPosition.y));
            if (byRow != 0) return byRow;
            int byColumn = Mathf.Abs(a.x - spawnPosition.x).CompareTo(Mathf.Abs(b.x - spawnPosition.x));
            if (byColumn != 0) return byColumn;
            // Deterministic order for mirrored candidates.
            return a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        });

        foreach (Vector2 candidate in candidates)
        {
            if (spawns.Count == count)
                break;
            if (IsOpenFloor(candidate, size, interior, walls) && !spawns.Exists(other => Mathf.Abs(other.x - candidate.x) < size.x && Mathf.Abs(other.y - candidate.y) < size.y))
                spawns.Add(candidate);
        }
        return spawns;
    }

    private static bool IsOpenFloor(Vector2 center, Vector2 size, Rect interior, List<MapManager.WallPiece> walls)
    {
        var body = new Rect(center - size / 2f, size);
        if (body.xMin < interior.xMin || body.xMax > interior.xMax || body.yMin < interior.yMin || body.yMax > interior.yMax)
            return false;
        foreach (MapManager.WallPiece wall in walls)
            if (wall.Footprint.Overlaps(body))
                return false;

        // Anything solid already in the scene, such as walls from an earlier build.
        foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, size, 0f))
            if (!hit.isTrigger)
                return false;
        return true;
    }

    // The NPC's solid collider plus a margin, so NPCs never start touching a wall or each other.
    private Vector2 NpcFootprint()
    {
        const float margin = 0.2f;
        if (playerPrefab != null)
        {
            foreach (Collider2D solid in playerPrefab.GetComponentsInChildren<Collider2D>())
            {
                if (solid.isTrigger)
                    continue;
                Vector2 scale = solid.transform.lossyScale;
                if (solid is BoxCollider2D box)
                    return Vector2.Scale(box.size, scale) + Vector2.one * margin;
                if (solid is PolygonCollider2D polygon && polygon.points.Length > 0)
                {
                    Vector2 min = polygon.points[0], max = polygon.points[0];
                    foreach (Vector2 point in polygon.points)
                    {
                        min = Vector2.Min(min, point);
                        max = Vector2.Max(max, point);
                    }
                    return Vector2.Scale(max - min, scale) + Vector2.one * margin;
                }
            }
        }
        return new Vector2(0.8f, 1.1f) + Vector2.one * margin;
    }

    private IEnumerator RunNpcDecisionLoop(Agent agent, ScaspClient client)
    {
        // Let all Start-button handlers finish map/progress initialization first.
        yield return null;
        bool usesScasp = client != null;
        var stats = AiDiagnostics.For(agent.name);
        var clock = new System.Diagnostics.Stopwatch();
        while (agent != null)
        {
            // A transformed half-zombie belongs to the gameplay mechanic, not the AI.
            if (agent.isActiveAndEnabled && agent.Health > 0 && !agent.BiteWindowExpired &&
                (!usesScasp || (client != null && client.IsConnected)))
            {
                // Behavior Tree decisions are synchronous: time them in-process.
                clock.Restart();
                agent.RequestDecision();
                if (!usesScasp)
                    stats.DecisionMs.Add((float)clock.Elapsed.TotalMilliseconds);
            }

            // ScaspStrategy coalesces requests while a previous decision is pending.
            // Game-time waits also pause with Time.timeScale.
            yield return new WaitForSeconds(Mathf.Max(0.1f, decisionInterval));
        }
    }

    // A periodic one-block summary instead of a log line per decision.
    private IEnumerator ReportAiStatus()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(15f);
            if (AiDiagnostics.Level >= AiTraceLevel.Summary)
                Debug.Log("[AI] Status\n" + AiDiagnostics.Summary(), this);
        }
    }

}

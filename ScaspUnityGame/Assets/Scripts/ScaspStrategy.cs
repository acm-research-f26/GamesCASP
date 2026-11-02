using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// Which s(CASP) rule set decides: the original rules (like-for-like with the
// Behavior Tree) or the defeasible reasoned policy (reasoned_innocent.pl).
public enum ScaspPolicy { Baseline, Reasoned }

[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(ScaspClient))]
public class ScaspStrategy : MonoBehaviour, IAgentStrategy
{
    [Header("s(CASP) Connection")]
    [SerializeField] private ScaspClient client;
    [SerializeField] private string actorId = "player1";

    [Header("Game Objectives")]
    [Tooltip("Fallback only: the scene's LeverManager supplies the real requirement.")]
    [SerializeField] private int leversToWin = 3;

    [Header("Decision Settings")]
    [SerializeField] private float requestTimeout = 35f;
    [SerializeField] private ScaspPolicy policy = ScaspPolicy.Reasoned;
    [Tooltip("Ask the solver for its justification tree and defeated alternatives (slower).")]
    [SerializeField] private bool explainDecisions;
    [Tooltip("Skip a request while nothing the rules can distinguish has changed, for at most this many seconds. 0 asks every time.")]
    [SerializeField, Min(0f)] private float maxUnchangedSkip = 2f;
    [Tooltip("Replies older than this (real seconds) describe an outdated world and are discarded.")]
    [SerializeField, Min(0.1f)] private float maxResponseAge = 3f;

    private Agent agent;
    private AiAction aiAction;

    private bool requestPending;
    private bool decisionQueued;
    private float requestStartTime;
    private int requestSequence;
    private string pendingRequestId = "";
    private string pendingSignature = "";
    private string answeredSignature = "";
    private float answeredTime = float.NegativeInfinity;

    private readonly List<Fact> storedFacts = new List<Fact>();
    private bool sendingStoredFacts;
    private float nextFactRetryTime;

    // Includes an in-flight fact until Python acknowledges persistence.
    public int PendingFactCount => storedFacts.Count;
    public string ActorId => actorId;
    public ScaspPolicy Policy => policy;
    public bool IsConnected => client != null && client.IsConnected;
    public string PolicyName => policy == ScaspPolicy.Reasoned ? "reasoned" : "baseline";

    // The last explanation the solver returned (explain mode only).
    public string LastJustification { get; private set; } = "";

    public LeverManager levManager;

    private void Awake()
    {
        agent = GetComponent<Agent>();
        aiAction = GetComponent<AiAction>();

        if (client == null)
            client = GetComponent<ScaspClient>();

        levManager = FindAnyObjectByType<LeverManager>();
        if(levManager == null)
        {
            Debug.LogError("unable to find lever manager.");
        }
    }

    // Assign a unique Prolog identity to each NPC.
    public void SetActorId(string id)
    {
        if (!IsValidAtom(id))
            throw new ArgumentException("Actor ID must be a valid Prolog atom.", nameof(id));

        if (requestPending || storedFacts.Count > 0)
            throw new InvalidOperationException("Cannot change actor ID while decisions or fact writes are pending.");

        actorId = id;
    }

    // Experiment settings, applied by PlayerManager for every spawned NPC.
    public void Configure(ScaspPolicy decisionPolicy, bool explain, float unchangedSkipSeconds)
    {
        policy = decisionPolicy;
        explainDecisions = explain;
        maxUnchangedSkip = Mathf.Max(0f, unchangedSkipSeconds);
    }

    private void OnEnable()
    {
        if (client == null)
            client = GetComponent<ScaspClient>();

        if (client != null)
        {
            client.NpcDecisionReceived += HandleDecision;
            client.NpcDecisionError += HandleDecisionError;
        }
    }

    private void OnDisable()
    {
        if (client != null)
        {
            client.NpcDecisionReceived -= HandleDecision;
            client.NpcDecisionError -= HandleDecisionError;
        }

        requestPending = false;
        decisionQueued = false;
    }

    private AiDiagnostics.AgentStats Stats
    {
        get
        {
            var stats = AiDiagnostics.For(actorId);
            stats.Backend = "scasp";
            stats.Policy = PolicyName;
            return stats;
        }
    }

    private void Update()
    {
        FlushStoredFacts();

        // Detect requests that have not received a response.
        if (requestPending && Time.realtimeSinceStartup - requestStartTime > requestTimeout)
        {
            Stats.Timeouts++;
            AiDiagnostics.Warn($"{name}: s(CASP) request {pendingRequestId} timed out after {requestTimeout} s.", this);
            requestPending = false;
        }

        if (!decisionQueued || requestPending || client == null || !client.IsConnected)
            return;

        if (!IsValidAtom(actorId))
        {
            Debug.LogError($"{name}: Invalid Prolog actor ID '{actorId}'.", this);
            decisionQueued = false;
            return;
        }

        if (agent.Role != "innocent")
        {
            Debug.LogWarning($"{name}: This strategy currently supports innocent NPCs only.", this);
            decisionQueued = false;
            return;
        }

        NpcState state = BuildNpcState();

        if (state == null)
        {
            decisionQueued = false;
            return;
        }

        decisionQueued = false;

        // Nothing the rules can tell apart has changed: the answer would be the same.
        string signature = Signature(state);
        if (maxUnchangedSkip > 0f && signature == answeredSignature && Time.realtimeSinceStartup - answeredTime < maxUnchangedSkip)
        {
            Stats.Skipped++;
            return;
        }

        requestPending = true;
        requestStartTime = Time.realtimeSinceStartup;
        pendingRequestId = $"{actorId}-{++requestSequence}";
        pendingSignature = signature;
        Stats.Requests++;
        AiDiagnostics.Trace($"{name}: s(CASP) request {pendingRequestId} sent ({PolicyName}; {state.levers.Length} levers, {state.zombies.Length} zombies, {state.barricades.Length} barricades seen)", this);

        // Send the NPC's current world state to Python.
        client.RequestNpcDecision(actorId, state, pendingRequestId, PolicyName, explainDecisions);
    }

    // IAgentStrategy: request the NPC's next decision.
    public void DecideNextAction(Agent_Parent_Script requestingAgent)
    {
        if (requestingAgent == null)
            throw new ArgumentNullException(nameof(requestingAgent));

        if (requestingAgent != agent)
        {
            Debug.LogError($"{name}: Decision requested by another agent.", this);
            return;
        }

        decisionQueued = true;
    }

    // IFactStrategy: retain facts locally until Python confirms their storage.
    public void StoreFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));

        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts cannot be stored.", nameof(fact));

        ScaspFactPayload.ValidateAtom(actorId, nameof(actorId));
        ScaspFactPayload.FromFact(fact); // Reject invalid IDs before accepting a fact into the queue.
        storedFacts.Add(fact);
        FlushStoredFacts();
    }

    private async void FlushStoredFacts()
    {
        if (sendingStoredFacts || !isActiveAndEnabled || storedFacts.Count == 0 ||
            client == null || !client.IsConnected || Time.realtimeSinceStartup < nextFactRetryTime)
            return;

        sendingStoredFacts = true;
        try
        {
            while (isActiveAndEnabled && client.IsConnected && storedFacts.Count > 0)
            {
                // One at a time preserves the order of mutable updates and retries.
                await client.StoreFactAsync(actorId, storedFacts[0]);
                Debug.Log($"{name}: Python stored {storedFacts[0].Type} for {actorId}: {storedFacts[0].SubjectId}", this);
                storedFacts.RemoveAt(0);
            }
        }
        catch (Exception exception)
        {
            if (this != null)
            {
                nextFactRetryTime = Time.realtimeSinceStartup + 2f;
                Debug.LogWarning($"{name}: Fact remains queued: {exception.Message}", this);
            }
        }
        finally
        {
            sendingStoredFacts = false;
        }
    }

    // Receive Python's already-parsed NPC action, correlated by request ID.
    private void HandleDecision(NpcDecisionResponse reply)
    {
        // Ignore responses intended for another NPC.
        if (reply.Actor != actorId)
            return;

        // A reply to an earlier, abandoned request describes an outdated world.
        if (!requestPending || reply.RequestId != pendingRequestId)
        {
            Stats.Stale++;
            AiDiagnostics.Trace($"{name}: discarded stale s(CASP) reply {reply.RequestId} (waiting for {(requestPending ? pendingRequestId : "nothing")}).", this);
            return;
        }

        requestPending = false;
        float roundTrip = Time.realtimeSinceStartup - requestStartTime;
        var stats = Stats;
        stats.Responses++;
        stats.RoundTripMs.Add(roundTrip * 1000f);
        stats.SolverMs.Add(reply.SolverMs);

        if (roundTrip > maxResponseAge)
        {
            stats.Stale++;
            decisionQueued = true;
            AiDiagnostics.Warn($"{name}: s(CASP) reply {reply.RequestId} arrived after {roundTrip:F1} s; discarded and asking again.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(reply.Action))
        {
            stats.Failures++;
            AiDiagnostics.Warn($"{name}: s(CASP) reply {reply.RequestId} has no action.", this);
            return;
        }

        answeredSignature = pendingSignature;
        answeredTime = Time.realtimeSinceStartup;

        if (explainDecisions)
        {
            LastJustification = reply.Justification;
            AiDiagnostics.Log($"{name}: s(CASP) chose {reply.Action}({reply.Target}) because\n{reply.Justification}\nDefeated alternatives: {string.Join(", ", reply.Rejected)} (explanation {reply.ExplainMs:F0} ms)", this);
        }

        // Agent forwards the action to AiAction.
        agent.ExecuteAction(reply.Action, reply.Target ?? "");
        stats.Executed++;
        stats.CountAction(reply.Action);
        AiDiagnostics.Trace($"{name}: executed s(CASP) {reply.Action}({reply.Target}) from request {reply.RequestId} ({roundTrip * 1000f:F0} ms round trip, solver {reply.SolverMs:F0} ms)", this);
    }

    private void HandleDecisionError(string requestId, string detail)
    {
        if (!requestPending || requestId != pendingRequestId)
            return;

        requestPending = false;
        var stats = Stats;
        stats.Errors++;
        if (stats.Errors == 1 || stats.Errors % 20 == 0)
            AiDiagnostics.Warn($"{name}: s(CASP) request {requestId} failed ({stats.Errors} so far): {detail}", this);
    }

    // Validate identifiers before sending them to Prolog.
    private static bool IsValidAtom(string value)
    {
        return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"^[a-z][A-Za-z0-9_]*\z");
    }

    // Convert Unity's current world state into the Python request format.
    private NpcState BuildNpcState()
    {
        var transientFacts = aiAction.FetchTransientFacts();

        List<NpcLeverState> levers = new List<NpcLeverState>();
        List<NpcZombieState> zombies = new List<NpcZombieState>();

        // Collect lever state, visibility, and distances.
        foreach (Fact observation in transientFacts)
        {
            if(!IsValidAtom(observation.SubjectId))
            {
                Debug.LogError($"{name}: Invalid Prolog ID '{observation.SubjectId}'.", this);
                return null;

            }

            if(observation.Type == FactType.LeverSeen)
            {
                levers.Add(new NpcLeverState
                {
                    id = observation.SubjectId,
                    state = (observation.IsOn ?? false) ? "pulled" : "unpulled",
                    visible = true,
                    distance = observation.Distance ?? 0,
                    nearest_zombie_distance = observation.NearestZombieDistance ?? 0
                });
            }
            else if(observation.Type == FactType.ZombieSeen)
            {
                zombies.Add(new NpcZombieState
                {
                    id = observation.SubjectId,
                    distance = observation.Distance ?? 0
                });
            }
            else
            {
                Debug.LogError($"Got invalid fact type. It was: {observation.Type}");
            }
        }

        // Assemble the snapshot expected by Python's FactFactory.
        return new NpcState
        {
            health = agent.Health,
            // The game's real requirement, as the Behavior Tree's blackboard reads it.
            levers_to_win = levManager != null ? levManager.TotalLevers : leversToWin,
            levers_pulled_count = levManager.ActivatedLevers,
            levers = levers.ToArray(),
            zombies = zombies.ToArray(),
            barricades = ObserveBarricades(),
            current_action = aiAction.CurrentAction ?? "",
            current_target = aiAction.CurrentTarget ?? "",
            unreachable = aiAction.UnreachableTargets()
        };
    }

    // Same perception as the Behavior Tree's BarricadeObservationBuffer: windows in
    // detection range, their health, and the nearest zombie this agent can see.
    private NpcBarricadeState[] ObserveBarricades()
    {
        var windows = new List<Window>();
        var zombies = new List<Zombie>();
        foreach (AiAction.DetectedObject detected in aiAction.detectedObjects)
        {
            if (detected.obj == null)
                continue;
            Window window = detected.obj.GetComponentInParent<Window>();
            if (window != null && window.isActiveAndEnabled && !windows.Contains(window) && IsValidAtom(window.name))
                windows.Add(window);
            Zombie zombie = detected.obj.GetComponentInParent<Zombie>();
            if (zombie != null && zombie.isActiveAndEnabled && !zombies.Contains(zombie))
                zombies.Add(zombie);
        }

        var barricades = new NpcBarricadeState[windows.Count];
        for (int i = 0; i < windows.Count; i++)
        {
            Window window = windows[i];
            Zombie nearest = null;
            float nearestDistance = 0f;
            foreach (Zombie zombie in zombies)
            {
                float d = Vector2.Distance(window.transform.position, zombie.transform.position);
                if (nearest == null || d < nearestDistance)
                {
                    nearest = zombie;
                    nearestDistance = d;
                }
            }

            barricades[i] = new NpcBarricadeState
            {
                id = window.name,
                visible = true,
                reinforcement = Mathf.Clamp(window.health, 0f, 100f),
                distance = Vector2.Distance(transform.position, window.transform.position),
                nearest_zombie = nearest != null ? nearest.gameObject.name : "",
                nearest_zombie_distance = nearestDistance
            };
        }
        return barricades;
    }

    // Everything the rules can distinguish: distance bands at every rule threshold
    // (1, 2, 3, 5), reinforcement bands (0, 25, 40, 95, 100), the order of distances
    // and reinforcements ("closer", "weaker"), states, progress and own activity.
    // If two snapshots share a signature, both policies choose the same action.
    private static string Signature(NpcState s)
    {
        var text = new StringBuilder();
        text.Append(s.health).Append('|').Append(s.levers_to_win).Append('|').Append(s.levers_pulled_count)
            .Append('|').Append(s.current_action).Append('(').Append(s.current_target).Append(")|");
        var unreachable = new List<string>(s.unreachable);
        unreachable.Sort(StringComparer.Ordinal);
        text.Append(string.Join(",", unreachable)).Append('|');

        var levers = new List<NpcLeverState>(s.levers);
        levers.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var lever in levers)
            text.Append(lever.id).Append(':').Append(lever.state).Append(':').Append(Band(lever.distance)).Append(':').Append(Band(lever.nearest_zombie_distance)).Append(';');
        levers.Sort((a, b) => a.nearest_zombie_distance.CompareTo(b.nearest_zombie_distance));
        foreach (var lever in levers)
            text.Append(lever.id).Append(',');
        text.Append('|');

        var zombies = new List<NpcZombieState>(s.zombies);
        zombies.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var zombie in zombies)
            text.Append(zombie.id).Append(':').Append(Band(zombie.distance)).Append(';');
        text.Append('|');

        var barricades = new List<NpcBarricadeState>(s.barricades);
        barricades.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var barricade in barricades)
            text.Append(barricade.id).Append(':').Append(Band(barricade.distance)).Append(':').Append(ReinforcementBand(barricade.reinforcement))
                .Append(':').Append(barricade.nearest_zombie).Append(':').Append(Band(barricade.nearest_zombie_distance)).Append(';');
        barricades.Sort((a, b) => a.reinforcement.CompareTo(b.reinforcement));
        foreach (var barricade in barricades)
            text.Append(barricade.id).Append(',');
        return text.ToString();
    }

    private static int Band(float distance) => distance <= 1f ? 0 : distance <= 2f ? 1 : distance <= 3f ? 2 : distance <= 5f ? 3 : 4;

    private static int ReinforcementBand(float r) => r <= 0f ? 0 : r <= 25f ? 1 : r <= 40f ? 2 : r < 95f ? 3 : r < 100f ? 4 : 5;
}

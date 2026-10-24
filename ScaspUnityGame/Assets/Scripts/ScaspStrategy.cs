using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(ScaspClient))]
public class ScaspStrategy : MonoBehaviour, IAgentStrategy
{
    [Header("s(CASP) Connection")]
    [SerializeField] private ScaspClient client;
    [SerializeField] private string actorId = "player1";

    [Header("Game Objectives")]
    [SerializeField] private int leversToWin = 3;

    [Header("Decision Settings")]
    [SerializeField] private float requestTimeout = 35f;

    private Agent agent;
    private AiAction aiAction;

    private bool requestPending;
    private bool decisionQueued;
    private float requestStartTime;

    private readonly List<Fact> storedFacts = new List<Fact>();

    private void Awake()
    {
        agent = GetComponent<Agent>();
        aiAction = GetComponent<AiAction>();

        if (client == null)
            client = GetComponent<ScaspClient>();
    }

    // Assign a unique Prolog identity to each NPC.
    public void SetActorId(string id)
    {
        if (!IsValidAtom(id))
            throw new ArgumentException("Actor ID must be a valid Prolog atom.", nameof(id));

        if (requestPending)
            throw new InvalidOperationException("Cannot change actor ID while a decision is pending.");

        actorId = id;
    }

    private void OnEnable()
    {
        if (client == null)
            client = GetComponent<ScaspClient>();

        if (client != null)
            client.NpcActionReceived += HandleNpcAction;
    }

    private void OnDisable()
    {
        if (client != null)
            client.NpcActionReceived -= HandleNpcAction;

        requestPending = false;
        decisionQueued = false;
    }

    private void Update()
    {
        // Detect requests that have not received a response.
        if (requestPending && Time.realtimeSinceStartup - requestStartTime > requestTimeout)
        {
            Debug.LogWarning($"{name}: NPC decision timed out.", this);
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
        requestPending = true;
        requestStartTime = Time.realtimeSinceStartup;

        // Send the NPC's current world state to Python.
        client.RequestNpcDecision(actorId, state);
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

    public IReadOnlyList<Fact> FetchTransientFacts() => aiAction.FetchTransientFacts();

    // IFactStrategy: store received facts locally.
    public void StoreFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));

        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts cannot be stored.", nameof(fact));

        storedFacts.Add(fact);

        // NPC reasoning currently uses live world snapshots.
        // Persistent event facts are not yet sent to Python.
        Debug.LogWarning($"{name}: Fact stored locally but not yet included in NPC reasoning.", this);
    }

    // Receive Python's already-parsed NPC action.
    private void HandleNpcAction(string actor, string action, string target)
    {
        // Ignore responses intended for another NPC.
        if (actor != actorId || !requestPending)
            return;

        requestPending = false;

        if (string.IsNullOrWhiteSpace(action))
        {
            Debug.LogWarning($"{name}: Received an empty NPC action.", this);
            return;
        }

        Debug.Log($"{name}: s(CASP) selected {action}, target: {target}", this);

        // Agent forwards the action to AiAction.
        agent.ExecuteAction(action, target ?? "");
    }

    // Validate identifiers before sending them to Prolog.
    private static bool IsValidAtom(string value)
    {
        return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"^[a-z][A-Za-z0-9_]*$");
    }

    // Convert Unity's current world state into the Python request format.
    private NpcState BuildNpcState()
    {
        var transientFacts = FetchTransientFacts();
        GameObject[] allLevers = System.Array.ConvertAll(FindObjectsByType<LeverScript>(FindObjectsSortMode.None), lever => lever.gameObject);
        GameObject[] allZombies = System.Array.ConvertAll(FindObjectsByType<Zombie>(FindObjectsSortMode.None), zombie => zombie.gameObject);

        HashSet<GameObject> visibleObjects = new HashSet<GameObject>();

        foreach (AiAction.DetectedObject detected in aiAction.detectedObjects)
        {
            if (detected.obj != null)
                visibleObjects.Add(detected.obj);
        }

        List<NpcLeverState> levers = new List<NpcLeverState>();
        List<NpcZombieState> zombies = new List<NpcZombieState>();

        int pulledCount = 0;

        // Collect lever state, visibility, and distances.
        foreach (GameObject leverObject in allLevers)
        {
            LeverScript lever = leverObject.GetComponent<LeverScript>();

            if (lever == null)
                continue;

            if (!IsValidAtom(leverObject.name))
            {
                Debug.LogError($"{name}: Invalid Prolog lever ID '{leverObject.name}'.", this);
                return null;
            }

            if (lever.isOn)
                pulledCount++;

            float nearestZombieDistance = 9999f;

            foreach (GameObject zombie in allZombies)
            {
                float distance = Vector2.Distance(leverObject.transform.position, zombie.transform.position);
                nearestZombieDistance = Mathf.Min(nearestZombieDistance, distance);
            }

            levers.Add(new NpcLeverState
            {
                id = leverObject.name,
                state = lever.isOn ? "pulled" : "unpulled",
                visible = visibleObjects.Contains(leverObject),
                distance = Vector2.Distance(transform.position, leverObject.transform.position),
                nearest_zombie_distance = nearestZombieDistance
            });
        }

        // Collect nearby detected zombies.
        foreach (Fact observation in transientFacts)
        {
            if (observation.Type != FactType.ZombieSeen)
                continue;

            if (!IsValidAtom(observation.SubjectId))
            {
                Debug.LogError($"{name}: Invalid Prolog zombie ID '{observation.SubjectId}'.", this);
                return null;
            }

            zombies.Add(new NpcZombieState
            {
                id = observation.SubjectId,
                distance = observation.Distance.Value
            });
        }

        // Assemble the snapshot expected by Python's FactFactory.
        return new NpcState
        {
            health = agent.Health,
            levers_to_win = leversToWin,
            levers_pulled_count = pulledCount,
            levers = levers.ToArray(),
            zombies = zombies.ToArray(),

            // Barricade state will be connected separately.
            barricades = Array.Empty<NpcBarricadeState>()
        };
    }
}

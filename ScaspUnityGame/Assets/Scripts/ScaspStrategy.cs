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
    private bool sendingStoredFacts;
    private float nextFactRetryTime;

    // Includes an in-flight fact until Python acknowledges persistence.
    public int PendingFactCount => storedFacts.Count;

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
        FlushStoredFacts();

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
            levers_to_win = leversToWin,
            levers_pulled_count = levManager.ActivatedLevers,
            levers = levers.ToArray(),
            zombies = zombies.ToArray(),

            // Barricade state will be connected separately.
            barricades = Array.Empty<NpcBarricadeState>()
        };
    }
}

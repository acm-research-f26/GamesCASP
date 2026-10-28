using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerManager : MonoBehaviour
{
    [Header("NPC Settings")]
    [SerializeField] private float spawnSpacing = 2f;
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;
    [Tooltip("Seconds between NPC decision requests. Each request refreshes transient observations.")]
    [SerializeField, Min(0.1f)] private float decisionInterval = 1f;

    [Header("NPC Prefabs")]
    public GameObject playerPrefab;

    // Track all spawned innocent NPCs.
    public List<Agent> players = new List<Agent>();

    private bool gameStarted;

    // Called when the Start Game button is clicked.
    public void StartGameClicked()
    {
        if (gameStarted)
        {
            Debug.LogWarning("NPCs have already been spawned.", this);
            return;
        }

        if (playerPrefab == null || playerPrefab.GetComponent<Agent>() == null)
        {
            Debug.LogError("Assign an NPC prefab containing Agent.cs.", this);
            return;
        }

        int numInnocents = GameSettings.InnocentCount;

        if (numInnocents <= 0)
        {
            Debug.LogWarning("No innocent NPCs configured.", this);
            return;
        }

        gameStarted = true;

        for (int i = 0; i < numInnocents; i++)
        {
            // Spawn each NPC at a different position.
            Vector2 position = spawnPosition + new Vector2(i * spawnSpacing, 0f);

            GameObject npc = Instantiate(playerPrefab);
            Agent agent = npc.GetComponent<Agent>();
            ScaspStrategy strategy = npc.GetComponent<ScaspStrategy>();
            ScaspClient client = npc.GetComponent<ScaspClient>();

            // Initialize the NPC.
            agent.Initialize("innocent", position.x, position.y);

            // Assign each NPC a unique Prolog actor ID.
            string actorId = $"player{i + 1}";

            if (strategy != null)
                strategy.SetActorId(actorId);

            players.Add(agent);

            Debug.Log($"Spawned innocent NPC: {actorId} at {position}", npc);

            // Refresh each NPC's observations and decision on a regular game-time interval.
            if (strategy != null && client != null)
                StartCoroutine(RunNpcDecisionLoop(agent, client));
            else
                Debug.LogWarning($"{actorId}: Missing ScaspStrategy or ScaspClient.", npc);
        }

        Debug.Log($"Successfully spawned {players.Count} innocent NPC(s).", this);
    }

    private IEnumerator RunNpcDecisionLoop(Agent agent, ScaspClient client)
    {
        yield return new WaitUntil(() => agent == null || client == null || client.IsConnected);

        while (agent != null && client != null)
        {
            if (agent.isActiveAndEnabled && agent.Health > 0 && client.IsConnected)
                agent.RequestDecision();

            // ScaspStrategy coalesces requests while a previous decision is pending.
            // Game-time waits also pause with Time.timeScale.
            yield return new WaitForSeconds(Mathf.Max(0.1f, decisionInterval));
        }
    }
}


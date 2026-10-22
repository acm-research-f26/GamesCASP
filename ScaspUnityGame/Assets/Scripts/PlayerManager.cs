using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerManager : MonoBehaviour
{
    [Header("NPC Settings")]
    [SerializeField] private float spawnSpacing = 2f;
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;

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

            // Request the first AI decision when connected.
            if (strategy != null && client != null)
                StartCoroutine(InitializeNpcDecision(agent, client));
            else
                Debug.LogWarning($"{actorId}: Missing ScaspStrategy or ScaspClient.", npc);
        }

        Debug.Log($"Successfully spawned {players.Count} innocent NPC(s).", this);
    }

    // Wait for the Python connection before requesting AI decisions.
    private IEnumerator InitializeNpcDecision(Agent agent, ScaspClient client)
    {
        yield return new WaitUntil(() => agent == null || client == null || client.IsConnected);

        if (agent != null && client != null && client.IsConnected)
            agent.RequestDecision();
    }
}


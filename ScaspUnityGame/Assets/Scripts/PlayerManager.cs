using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum NpcDecisionBackend { BehaviorTree, Scasp }

public class PlayerManager : MonoBehaviour
{
    [Header("NPC Settings")]
    [SerializeField] private float spawnSpacing = 2f;
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;
    [SerializeField, Min(0.1f)] private float decisionInterval = 0.5f;
    [SerializeField] private NpcDecisionBackend decisionBackend = NpcDecisionBackend.BehaviorTree;
    [SerializeField] private LeverManager leverManager;

    [Header("NPC Prefabs")]
    public GameObject playerPrefab;
    public List<Agent> players = new List<Agent>();
    private bool gameStarted;

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
        int count = GameSettings.InnocentCount;
        if (count <= 0) return;
        gameStarted = true;

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
                Vector2 position = spawnPosition + new Vector2(i * spawnSpacing, 0f);
                agent.Initialize("innocent", position.x, position.y);
                // Assign each NPC a unique Prolog actor ID.
                string actorId = $"player{i + 1}";
                npc.name = actorId;

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
                    var tree = new BehaviorTree(agent.GetComponent<AiAction>(), leverManager);
                    agent.SetStrategy(tree, "behavior_tree");
                    if (tree != null) 
                        StartCoroutine(RunNpcDecisionLoop(agent, null));
                    else 
                        Debug.LogWarning($"{actorId}: Missing ScaspStrategy or ScaspClient.", npc);
                }
                else
                {
                    ScaspStrategy strategy = npc.GetComponent<ScaspStrategy>();
                    if (strategy == null) strategy = npc.AddComponent<ScaspStrategy>();
                    client = npc.GetComponent<ScaspClient>();
                    strategy.SetActorId(actorId);
                    strategy.enabled = true;
                    client.enabled = true;
                    agent.SetStrategy(strategy, "scasp");
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

    private IEnumerator RunNpcDecisionLoop(Agent agent, ScaspClient client)
    {
        // Let all Start-button handlers finish map/progress initialization first.
        yield return null;
        bool usesScasp = client != null;
        while (agent != null)
        {
            if (agent.isActiveAndEnabled && agent.Health > 0 &&
                (!usesScasp || (client != null && client.IsConnected)))
                agent.RequestDecision();

            if(usesScasp && !client.IsConnected)
            {
                break;
            }
            // ScaspStrategy coalesces requests while a previous decision is pending.
            // Game-time waits also pause with Time.timeScale.
            yield return new WaitForSeconds(Mathf.Max(0.1f, decisionInterval));
        }
    }

}

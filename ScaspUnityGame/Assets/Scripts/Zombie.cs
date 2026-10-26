using UnityEngine;

public class Zombie : MonoBehaviour
{
    // Runtime identity stays unique across prefab clones and stable across renames.
    // It is not serialized, so spawning a prefab creates a new identity.
    public string EntityId { get; } = "zombie_" + System.Guid.NewGuid().ToString("N");

    [Header("Movement")]
    public float moveSpeed = 2f;

    [Header("Combat")]
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    [Header("References")]
    public PlayerManager playerManager;

    private Agent targetAgent;
    private SpriteRenderer spriteRenderer;
    private float lastAttackTime = 0f;

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerManager == null)
            playerManager = FindAnyObjectByType<PlayerManager>();

        if (playerManager == null)
            Debug.LogError($"{name}: PlayerManager not found.", this);
    }

    private void Update()
    {
        if (playerManager == null)
            return;

        targetAgent = NearestAgent();

        if (targetAgent == null)
            return;

        Vector3 dir = targetAgent.transform.position - transform.position;

        if (spriteRenderer != null)
        {
            if (dir.x < 0)
                spriteRenderer.flipX = true;
            else if (dir.x > 0)
                spriteRenderer.flipX = false;
        }

        float distance = dir.magnitude;

        if (distance > attackRange)
        {
            transform.position += dir.normalized * moveSpeed * Time.deltaTime;
        }
        else if (Time.time > lastAttackTime + attackCooldown)
        {
            Debug.Log($"{name} attacks {targetAgent.name}!");

            lastAttackTime = Time.time;

            // TODO: Apply damage to Agent using SetHealth().
        }
    }

    // Find the nearest living innocent NPC.
    private Agent NearestAgent()
    {
        float nearestDistance = float.MaxValue;
        Agent nearestAgent = null;

        foreach (Agent agent in playerManager.players)
        {
            if (agent == null || agent.Health <= 0)
                continue;

            float distance = Vector3.Distance(transform.position, agent.transform.position);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestAgent = agent;
            }
        }

        return nearestAgent;
    }
}


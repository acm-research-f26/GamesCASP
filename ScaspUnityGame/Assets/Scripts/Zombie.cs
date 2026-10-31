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
    public int attackDamage = 1;

    [Header("References")]
    public PlayerManager playerManager;

    private Agent targetAgent;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer hitPlayer;
    private float lastAttackTime = 0f;

    private void Awake()
    {
        // A unique valid atom doubles as this clone's executor target name.
        // Keep the existing EntityId API used by current main's s(CASP) code.
        gameObject.name = EntityId;
    }

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
            FlashRed(targetAgent.GetComponent<SpriteRenderer>());

            targetAgent.SetHealth(targetAgent.Health - attackDamage);
        }
    }

    private void FlashRed(SpriteRenderer player)
    {
        if (hitPlayer != null)
            hitPlayer.color = new Color(1f, 1f, 1f, hitPlayer.color.a);
        hitPlayer = player;
        hitPlayer.color = new Color(1f, 0f, 0f, hitPlayer.color.a);
        CancelInvoke("ResetColor");
        Invoke("ResetColor", 0.2f);
    }

    private void ResetColor()
    {
        if (hitPlayer != null)
            hitPlayer.color = new Color(1f, 1f, 1f, hitPlayer.color.a);
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

    void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"Colliding with object: {other.name}");
    }
}


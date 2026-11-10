using UnityEngine;

public class Agent : Agent_Parent_Script
{
    // Must equal bite_window_seconds/1 in half_zombie_timed_behavior.pl.
    // NpcState sends it and Python rejects half-zombie snapshots that disagree.
    public const float BiteWindowSeconds = 5f;

    [Header("Agent Properties")]
    [SerializeField] private int health = 3;
    [SerializeField] private string role = "innocent";

    private string agentId;

    // Half-zombie status is owned by the gameplay mechanic; AI code only reads it.
    // Timestamps use Time.time, the same paused-with-timeScale clock as NpcState.current_time.
    private bool isHalfZombie;
    private float biteWindowStartedAt;

    public int Health => health;
    public string Role => role;

    // Prolog-safe identity other agents use for this agent (human targets, witnessed events).
    public string AgentId => agentId;

    public bool IsHalfZombie => isHalfZombie;
    public float BiteWindowStartedAt => biteWindowStartedAt;
    public bool BiteWindowExpired => isHalfZombie && Time.time >= biteWindowStartedAt + BiteWindowSeconds;

    public string statName;

    // Initialize this NPC with its role and starting position.
    public void Initialize(string agentRole, float startPosX, float startPosY)
    {
        health = 3;
        role = agentRole;
        isHalfZombie = false;
        biteWindowStartedAt = 0f;

        transform.position = new Vector3(
            startPosX,
            startPosY,
            transform.position.z);
    }

    public void SetAgentId(string id)
    {
        ScaspFactPayload.ValidateAtom(id, nameof(id));
        agentId = id;
    }

    // Gameplay calls this when infection is applied. Later hits do not restart the window.
    public bool MarkHalfZombie()
    {
        if (isHalfZombie || health <= 0)
            return false;

        isHalfZombie = true;
        biteWindowStartedAt = Time.time;
        return true;
    }

    // Gameplay calls this only after confirming a successful bite before the window expired.
    public bool RenewBiteWindow()
    {
        if (!isHalfZombie || BiteWindowExpired)
            return false;

        biteWindowStartedAt = Time.time;
        return true;
    }

    // An expired half-zombie has transformed; the gameplay mechanic, not the AI, controls it now.
    public override void ExecuteAction(string action, string target = "")
    {
        if (BiteWindowExpired)
        {
            GetComponent<AiAction>().StopAllActions();
            return;
        }

        base.ExecuteAction(action, target);
    }

    // Update the NPC's health.
    public void SetHealth(int newHealth)
    {
        health = Mathf.Max(0, newHealth);
        if (health == 0)
        {
            foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>())
            {
                Color color = sprite.color;
                color.a = 0.5f;
                sprite.color = color;
            }
            AiAction movement = GetComponent<AiAction>();

            if (movement != null) movement.enabled = false;
        }


        // The parent manages AI decisions and facts.
        // AiAction manages movement and physical behavior.
    }
}

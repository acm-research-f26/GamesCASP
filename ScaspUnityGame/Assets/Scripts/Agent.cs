using UnityEngine;

public class Agent : Agent_Parent_Script
{
    // Must equal bite_window_seconds/1 in half_zombie_timed_behavior.pl.
    // NpcState sends it and Python rejects half-zombie snapshots that disagree.
    public const float BiteWindowSeconds = 5f;
    // Must equal critical_bite_seconds/1 in half_zombie_timed_behavior.pl
    // StrategySwitchVerification reads the Prolog file to make sure both stay the same
    public const float CriticalBiteSeconds = 4f;

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

    private bool isDead;

    // This will let us know when the bite window is in its last few seconds
    // Same check as critical_bite_stage in the Prolog rules, start + window - critical <= now < start + window
    public bool InCriticalBiteStage => isHalfZombie && !BiteWindowExpired &&
        Time.time >= biteWindowStartedAt + BiteWindowSeconds - CriticalBiteSeconds;

    // This lets the experiment know when the NPC just got infected so it can change strategy
    // It only fires once per NPC since MarkHalfZombie ignores NPCs that are already infected
    public event System.Action<Agent> BecameHalfZombie;
    public string statName;

    public Sprite InfectedSprite;
    public Sprite DeadSprite;

    // Initialize this NPC with its role and starting position.
    public void Initialize(string agentRole, float startPosX, float startPosY)
    {
        health = 3;
        role = agentRole;
        isHalfZombie = false;
        isDead = false;
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

        GetComponent<SpriteRenderer>().sprite = InfectedSprite;
        
        isHalfZombie = true;
        biteWindowStartedAt = Time.time;
        // Let anyone listening know the infection happened
        BecameHalfZombie?.Invoke(this);
        return true;
    }

    void Update()
    {
        if(BiteWindowExpired && !isDead)
        {
            SetDead();
        }
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

    public void SetDead()
    {
        isDead = true;


        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>())
        {
            /*Color color = sprite.color;
            color.a = 0.5f;
            sprite.color = color;*/
            sprite.sprite = DeadSprite;
        }
        AiAction movement = GetComponent<AiAction>();

        if (movement != null) movement.enabled = false;
    }

    // Update the NPC's health.
    public void SetHealth(int newHealth)
    {
        health = Mathf.Max(0, newHealth);
        if (health < 0 && !isDead)
        {
            SetDead();
        }


        // The parent manages AI decisions and facts.
        // AiAction manages movement and physical behavior.
    }
}

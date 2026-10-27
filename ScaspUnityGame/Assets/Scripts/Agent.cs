using UnityEngine;

public class Agent : Agent_Parent_Script
{
    [Header("Agent Properties")]
    [SerializeField] private int health = 3;
    [SerializeField] private string role = "innocent";

    public int Health => health;
    public string Role => role;

    // Initialize this NPC with its role and starting position.
    public void Initialize(string agentRole, float startPosX, float startPosY)
    {
        health = 3;
        role = agentRole;

        transform.position = new Vector3(
            startPosX,
            startPosY,
            transform.position.z);
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

            if (movement != null)
                movement.enabled = false;
        }


        // The parent manages AI decisions and facts.
        // AiAction manages movement and physical behavior.
    }
}

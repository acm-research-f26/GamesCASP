using UnityEngine;

public class Zombie : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    public PlayerManager playerManager;

    private Player player;
    private SpriteRenderer spriteRenderer;
    private float lastAttackTime = 0f;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerManager = FindFirstObjectByType<PlayerManager>();
    }

    void Update()
    {
        player = nearestPlayer();
        if (player == null)
            return;

        Vector3 dir = player.transform.position - transform.position;

        if (dir.x < 0)
        {
            spriteRenderer.flipX = true;
        }
        else if (dir.x > 0)
        {
            spriteRenderer.flipX = false;
        }

        float distance = dir.magnitude;

        if (distance > attackRange)
        {
            transform.position += dir.normalized * moveSpeed * Time.deltaTime;
        }
        else
        {
            if (Time.time > lastAttackTime + attackCooldown)
            {
                Debug.Log("Zombie attacks!");
                lastAttackTime = Time.time;
            }
        }
    }

    Player nearestPlayer()
    {
        float nearestDistance = 1000f;
        Player nearestPlayer = null;

        foreach (Player player in playerManager.players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlayer = player;
            }
        }
        return nearestPlayer;
    }
}

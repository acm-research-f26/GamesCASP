using UnityEngine;

public class Zombie : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    private Transform player;
    private SpriteRenderer spriteRenderer;
    private float lastAttackTime = 0f;

    void Start()
    {
        player = GameObject.Find("Survivor1").transform;
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        if (player == null)
            return;

        Vector3 dir = player.position - transform.position;

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
}

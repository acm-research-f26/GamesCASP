using UnityEngine;

public class Player : MonoBehaviour
{
    public int health;
    public string role;
    public float moveSpeed;

    public float targetPosX;
    public float targetPosY;

    public void Initialize(string role, float startPosX, float startPosY)
    {
        this.health = 3;
        this.role = role;
        this.moveSpeed = 1;

        this.targetPosX = startPosX;
        this.targetPosY = startPosY;

        transform.position = new Vector3(startPosX, startPosY, 0);
    }

    void Update()
    {
        // move to target pos every frame
        Vector3 targetPosition = new Vector3(targetPosX, targetPosY, 0);
        transform.position = Vector3.MoveTowards(transform.position,targetPosition,this.moveSpeed * Time.deltaTime);
    }
}

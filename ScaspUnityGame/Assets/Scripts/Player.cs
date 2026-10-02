using UnityEngine;

public class Player : MonoBehaviour
{
    public int health;
    public string role;

    public void Initialize(string role)
    {
        this.health = 3;
        this.role = role;

        transform.position = new Vector3(0,0,0);
    }
}

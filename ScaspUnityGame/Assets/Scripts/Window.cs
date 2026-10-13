using UnityEngine;
using System.Collections.Generic;

public class Window : MonoBehaviour
{
    public float health = 100;

    public float increaseSpeed = 10;
    public float decreaseSpeed = 5;
    private HashSet<GameObject> playersInside = new HashSet<GameObject>();

    public GameObject zombiePrefab;

    public float delay = 15;
    private float timer = 0;

    private bool gameStarted = true;
    // Update is called once per frame
    public void StartGameClicked()
    {
        gameStarted = true;
    }
    void Update()
    {
        if (gameStarted == true) { 
            int playerCount = playersInside.Count;
            if (playerCount > 0)
            {
                health += increaseSpeed * playerCount * Time.deltaTime;
                health = Mathf.Clamp(health, 0, 100);
            }
            else
            {
                health -= decreaseSpeed * Time.deltaTime;
                health = Mathf.Clamp(health, 0, 100);
                if (health == 0)
                {
                    timer -= Time.deltaTime;
                    if (timer <= 0)
                    {
                        Instantiate(zombiePrefab, transform.position, Quaternion.identity);

                        timer = delay;
                    }
                }
            }
        }
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playersInside.Add(other.gameObject);
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playersInside.Remove(other.gameObject);
        }
    }
}

using UnityEngine;

public class LeverScript : MonoBehaviour
{
    public LeverManager leverManager;

    public Color offColor = Color.red;
    public Color onColor = Color.green;

    private SpriteRenderer spriteRenderer;
    public bool isOn = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = offColor;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isOn = true;
            spriteRenderer.color = onColor;
            leverManager.LeverActivated();
        }
    }
}
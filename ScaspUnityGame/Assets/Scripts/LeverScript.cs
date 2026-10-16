using UnityEngine;

public class LeverScript : MonoBehaviour
{
    public event System.Action<LeverScript> Activated;

    public LeverManager leverManager;

    public Color offColor = Color.red;
    public Color onColor = Color.green;

    private SpriteRenderer spriteRenderer;
    public bool isOn = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Every lever starts OFF (red).
        isOn = false;
        spriteRenderer.color = offColor;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"Lever touched by: {other.name}, Tag: {other.tag}");
        // Only activate once when the player touches it.
        if (other.CompareTag("Player") && !isOn)
        {
            isOn = true;
            spriteRenderer.color = onColor;
            leverManager.LeverActivated();
            Activated?.Invoke(this);
        }
    }

}

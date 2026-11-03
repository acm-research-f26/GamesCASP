using UnityEngine;

// Shows a barricade's state on its planks: solid when reinforced, fading and reddening as
// zombies break through. Reads Window.health only; repair and spawning stay in Window.
public class MansionBarricadeView : MonoBehaviour
{
    private Window window;
    private SpriteRenderer planks;
    private Color sound;

    public void Bind(Window target, SpriteRenderer boards)
    {
        window = target;
        planks = boards;
        sound = boards.color;
    }

    private void Update()
    {
        if (window == null || planks == null)
            return;

        float strength = Mathf.Clamp01(window.health / 100f);
        Color broken = new Color(0.55f, 0.15f, 0.12f, 0.15f);
        planks.color = Color.Lerp(broken, sound, strength);
    }
}

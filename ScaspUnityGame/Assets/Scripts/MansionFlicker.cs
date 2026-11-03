using UnityEngine;

// Animates mansion fire and fog: cycles sprite frames, breathes a light's alpha, and drifts.
public class MansionFlicker : MonoBehaviour
{
    public Sprite[] frames;
    public float framesPerSecond = 8f;
    public SpriteRenderer target;
    public SpriteRenderer glow;
    public float glowAlpha;
    public float glowVariation = 0.18f;
    public Vector2 drift;

    private float phase;
    private Vector3 origin;

    private void Start()
    {
        phase = Random.value * 10f;
        origin = transform.localPosition;
    }

    private void Update()
    {
        float t = Time.time + phase;

        if (target != null && frames != null && frames.Length > 0)
            target.sprite = frames[(int)(t * framesPerSecond) % frames.Length];

        if (glow != null)
        {
            Color color = glow.color;
            color.a = glowAlpha * (1f - glowVariation + glowVariation * 2f * Mathf.PerlinNoise(t * 3f, phase));
            glow.color = color;
        }

        if (drift != Vector2.zero)
            transform.localPosition = origin + (Vector3)(drift * Mathf.Sin(t * 0.25f));
    }
}

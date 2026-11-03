using UnityEngine;
using UnityEngine.InputSystem;

// Overview of the whole mansion, fitted to the screen's aspect ratio, or a closer
// view following one NPC. Keys: 0 overview, 1-9 follow NPC n, mouse wheel zoom.
[RequireComponent(typeof(Camera))]
public class MansionCamera : MonoBehaviour
{
    [SerializeField, Min(0f)] private float margin = 0.75f;
    [SerializeField, Min(2f)] private float followSize = 6f;
    [SerializeField, Min(0.1f)] private float smoothing = 6f;

    private Camera view;
    private PlayerManager players;
    private int following = -1;
    private float zoom = 1f;

    private void Awake()
    {
        view = GetComponent<Camera>();
        players = FindAnyObjectByType<PlayerManager>();
    }

    private void LateUpdate()
    {
        ReadInput();

        Rect house = MapManager.BuildingBounds;
        float overview = Mathf.Max(house.height / 2f, house.width / 2f / Mathf.Max(0.1f, view.aspect)) + margin;
        Vector3 goal = new Vector3(house.center.x, house.center.y, transform.position.z);
        float size = overview;

        Agent target = Followed();
        if (target != null)
        {
            goal = new Vector3(target.transform.position.x, target.transform.position.y, transform.position.z);
            size = followSize;
        }

        size = Mathf.Clamp(size * zoom, 3f, overview * 1.2f);
        float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
        view.orthographicSize = Mathf.Lerp(view.orthographicSize, size, t);
        transform.position = Vector3.Lerp(transform.position, goal, t);
    }

    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit0Key.wasPressedThisFrame) { following = -1; zoom = 1f; }
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++)
                if (keyboard[digits[i]].wasPressedThisFrame) { following = i; zoom = 1f; }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
            zoom = Mathf.Clamp(zoom * (mouse.scroll.ReadValue().y > 0 ? 0.9f : 1.1f), 0.35f, 1.2f);
    }

    private Agent Followed()
    {
        if (following < 0 || players == null || following >= players.players.Count)
            return null;
        return players.players[following];
    }
}

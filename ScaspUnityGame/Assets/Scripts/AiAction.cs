using System.Collections.Generic;
using UnityEngine;

public class AiAction : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.2f;
    public float fleeDistance = 5f;
    public float interactionRange = 1f;
    public float leaveBarricadeDistance = 2f;

    [Header("Detection")]
    public float detectionRange = 5f;

    [System.Serializable]
    public struct DetectedObject
    {
        public GameObject obj;
        public float distance;
        public float angle;

        public DetectedObject(GameObject obj, float distance, float angle)
        {
            this.obj = obj;
            this.distance = distance;
            this.angle = angle;
        }
    }

    public List<DetectedObject> detectedObjects = new List<DetectedObject>();

    [Header("Transient Query Debug (Play Mode)")]
    [Tooltip("Number of transient queries since this agent's Awake, including empty results.")]
    [SerializeField] private int transientQueryCount;
    [Tooltip("Game time of the last query. -1 means no query yet.")]
    [SerializeField] private float lastTransientQueryTime = -1f;
    [Tooltip("Fact count from the last query; updated only when facts are fetched.")]
    [SerializeField] private int lastTransientFactCount;

    private Vector2 targetPosition;
    private GameObject movementTarget;
    private bool isMoving;
    private bool isSearchingForLever;
    private bool isSearchingForBarricade;
    private float stoppingDistance;
    private float nextWanderTime;

    private void Awake()
    {
        targetPosition = transform.position;
        transientQueryCount = 0;
        lastTransientQueryTime = -1f;
        lastTransientFactCount = 0;
    }

    private void Update()
    {
        DetectObjects();

        if (isSearchingForLever)
        {
            SearchForLever();
        }

        if (isSearchingForBarricade)
        {
            SearchForBarricade();
        }

        UpdateMovement();
    }

    // Detect nearby objects using Unity 2D physics.
    private void DetectObjects()
    {
        detectedObjects.Clear();

        Collider2D[] nearbyObjects = Physics2D.OverlapCircleAll(transform.position, detectionRange);

        foreach (Collider2D col in nearbyObjects)
        {
            if (col.gameObject == gameObject)
                continue;

            Vector2 direction = col.transform.position - transform.position;

            float distance = direction.magnitude;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            detectedObjects.Add(new DetectedObject(col.gameObject, distance, angle));
        }
    }

    // Query physics now, independently of MonoBehaviour Update ordering.
    public IReadOnlyList<Fact> FetchTransientFacts()
    {
        transientQueryCount++;
        lastTransientQueryTime = Time.time;
        Debug.Log($"{name}: FetchTransientFacts called at {Time.time:F2}s", this);
        DetectObjects();
        var facts = new List<Fact>();
        var seen = new HashSet<Zombie>();
        foreach (var detected in detectedObjects)
        {
            if (detected.obj == null)
                continue;
            var zombie = detected.obj.GetComponentInParent<Zombie>();
            if (zombie == null || !zombie.isActiveAndEnabled || !seen.Add(zombie))
                continue;
            facts.Add(new Fact(FactType.ZombieSeen, FactLifetime.Transient, zombie.name,
                distance: Vector2.Distance(transform.position, zombie.transform.position)));
        }
        lastTransientFactCount = facts.Count;
        Debug.Log($"{name}: Found {facts.Count} transient facts", this);
        return facts.AsReadOnly();
    }

    // Execute the action selected by the AI strategy.
    public void Execute(string action, string target = "")
    {
        DetectObjects();
        isSearchingForLever = false;
        isSearchingForBarricade = false;
        StopMovement();

        switch (action)
        {
            case "run_from":
                RunFrom(target);
                break;

            case "hold_barricade":
                HoldBarricade(target);
                break;

            case "move_to_barricade":
                MoveToBarricade(target);
                break;

            case "leave_barricade":
                LeaveBarricade(target);
                break;

            case "pull_lever":
                PullLever(target);
                break;

            case "move_to_lever":
                MoveToLever(target);
                break;

            case "hunt_lever":
                HuntLever();
                break;

            case "hunt_barricade":
                HuntBarricade();
                break;

            case "wait":
                Wait();
                break;

            default:
                Debug.LogWarning(
                    $"{name}: Unknown action '{action}'", this);
                break;
        }
    }

    // Move the NPC toward its current destination.
    private void UpdateMovement()
    {
        if (!isMoving)
            return;

        if (movementTarget != null)
            targetPosition = movementTarget.transform.position;

        Vector2 currentPosition = transform.position;

        if (Vector2.Distance(currentPosition, targetPosition) <= stoppingDistance)
        {
            isMoving = false;
            return;
        }

        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, targetPosition, moveSpeed * Time.deltaTime);

        transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
    }

    private void MoveToPosition(Vector2 position, float stopDistance = 0f)
    {
        movementTarget = null;
        targetPosition = position;
        stoppingDistance = stopDistance;
        isMoving = true;
    }


    private void MoveToObject(GameObject obj, float stopDistance = 0f)
    {
        if (obj == null)
            return;

        movementTarget = obj;
        targetPosition = obj.transform.position;
        stoppingDistance = stopDistance;
        isMoving = true;
    }


    // Flee away from the specified zombie.
    protected virtual void RunFrom(string target)
    {
        GameObject zombie = FindNearest("Zombie", target);

        if (zombie == null)
        {
            Debug.LogWarning($"{name}: Zombie '{target}' not detected.");
            return;
        }

        Vector2 direction =
            transform.position - zombie.transform.position;

        if (direction.sqrMagnitude < 0.01f) {
            direction = Vector2.right;
        }

        Vector2 fleePosition =
            (Vector2)transform.position + direction.normalized * fleeDistance;

        MoveToPosition(fleePosition);

        Debug.Log($"{name}: Running from {zombie.name}");
    }

    // Defend the specified barricade.
    protected virtual void HoldBarricade(string target)
    {
        isMoving = false;
        movementTarget = null;

        Debug.Log($"{name}: Holding barricade {target}");

        // TODO: Connect barricade defense mechanics.
    }

    // Move toward the specified barricade.
    protected virtual void MoveToBarricade(string target)
    {
        GameObject barricade =
            FindNearest("Barricade", target);

        if (barricade == null)
        {
            Debug.LogWarning(
                $"{name}: Barricade '{target}' not detected.");
            return;
        }

        MoveToObject(barricade, interactionRange);
    }

    protected virtual void LeaveBarricade(string target)
    {
        GameObject barricade = FindNearest("Barricade", target);

        if (barricade == null)
        {
            return;
        }

        Vector2 direction = transform.position - barricade.transform.position;
        if (direction.sqrMagnitude < 0.01f)
            direction = Vector2.right;

        MoveToPosition((Vector2)transform.position + direction.normalized * leaveBarricadeDistance);
    }

    // Activate the specified lever.

    protected virtual void PullLever(string target)
    {
        GameObject lever = FindNearest("Lever", target, true);

        if (lever == null)
        {
            Debug.LogWarning($"{name}: Available lever '{target}' not found.");
            return;
        }

        // Approach the lever until physical contact.
        MoveToObject(lever, 0f);

        Debug.Log($"{name}: Moving to activate lever {target}");
    }


    // Move toward an available lever.
    protected virtual void MoveToLever(string target)
    {
        GameObject lever = FindNearest("Lever", target, true);

        if (lever == null)
        {
            Debug.LogWarning($"{name}: Available lever '{target}' not found.");
            return;
        }

        MoveToObject(lever, 0f);
    }

    // Search for an available lever.
    protected virtual void HuntLever()
    {
        isSearchingForLever = true;
        SearchForLever();
    }

    protected virtual void HuntBarricade()
    {
        isSearchingForBarricade = true;
        SearchForBarricade();
    }

    private void StopMovement()
    {
        isMoving = false;
        movementTarget = null;
    }

    private void SearchForLever()
    {
        GameObject lever =
            FindNearest("Lever", "", true);

        if (lever != null)
        {
            isSearchingForLever = false;
            MoveToObject(lever, 0f);
            return;
        }

        if (!isMoving || Time.time >= nextWanderTime || Vector2.Distance(transform.position, targetPosition) < 0.2f)
        {
            Wander();
        }
    }

    private void SearchForBarricade()
    {
        GameObject barricade = FindNearest("Barricade");

        if (barricade != null)
        {
            isSearchingForBarricade = false;
            MoveToBarricade(barricade.name);
            return;
        }

        if (!isMoving || Time.time >= nextWanderTime || Vector2.Distance(transform.position, targetPosition) < 0.2f)
        {
            Wander();
        }
    }

    // Stop the NPC.
    protected virtual void Wait()
    {
        isMoving = false;
        movementTarget = null;

        Debug.Log($"{name}: Waiting");
    }

    // Find the nearest detected object matching the criteria.
    protected GameObject FindNearest(string tag, string target = "", bool requireOffLever = false)
    {
        GameObject nearest = null;
        float closestDistance = float.MaxValue;

        foreach (DetectedObject detected in detectedObjects)
        {
            if (detected.obj == null)
                continue;

            GameObject candidate = null;

            switch (tag)
            {
                case "Lever":
                    LeverScript lever = detected.obj.GetComponentInParent<LeverScript>();

                    if (lever == null || (requireOffLever && lever.isOn))
                        continue;

                    candidate = lever.gameObject;
                    break;

                case "Zombie":
                    Zombie zombie = detected.obj.GetComponentInParent<Zombie>();

                    if (zombie == null)
                        continue;

                    candidate = zombie.gameObject;
                    break;

                case "Barricade":
                    Window window = detected.obj.GetComponentInParent<Window>();
                    if (window != null)
                    {
                        candidate = window.gameObject;
                        break;
                    }

                    if (!detected.obj.name.StartsWith("barricade", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    candidate = detected.obj;
                    break;

                default:
                    continue;
            }

            if (!string.IsNullOrEmpty(target) && candidate.name != target)
                continue;

            float distance = Vector2.Distance(transform.position, candidate.transform.position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }
    // Wander in a random direction while searching.
    protected void Wander()
    {
        Vector2 randomDirection = Random.insideUnitCircle.normalized;

        if (randomDirection == Vector2.zero) {
            randomDirection = Vector2.right;
        }

        Vector2 wanderPosition = (Vector2)transform.position + randomDirection * detectionRange;

        MoveToPosition(wanderPosition);
        nextWanderTime = Time.time + 3f;
    }

    // Visualize NPC detection range in the Unity editor.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}

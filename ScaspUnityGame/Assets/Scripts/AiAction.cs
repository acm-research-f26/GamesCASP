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

    [Header("Half-Zombie")]
    [Tooltip("Minimum game-time seconds between bite requests, so a failed bite can be retried without spamming.")]
    public float biteAttemptCooldown = 1f;

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
    private string currentAction;
    private string currentTarget;

    // The half-zombie controller subscribes to this and performs the actual bite/infection.
    // The event means "bite attempted in range", NOT "bite succeeded".
    public event System.Action<Agent> BiteRequested;
    private float nextBiteRequestTime;
    private bool warnedMissingBiteHandler;

    public bool IsSearchingForLever => isSearchingForLever;
    public bool IsSearchingForBarricade => isSearchingForBarricade;

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
            // Ignore all of this agent's colliders, including child colliders.
            if (col.transform.IsChildOf(transform))
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

        // Refresh observations now rather than depending on Update() timing.
        DetectObjects();

        var facts = new List<Fact>();

        // An entity can have multiple colliders, but should produce one observation.
        var seenZombies = new HashSet<Zombie>();
        var seenLevers = new HashSet<LeverScript>();
        var seenHumans = new HashSet<Agent>();
        Agent self = GetComponent<Agent>();
        Zombie[] allZombies = FindObjectsByType<Zombie>(FindObjectsSortMode.None);

        foreach (var detected in detectedObjects)
        {
            if (detected.obj == null)
            {
                continue;
            }

            // Zombie observation.
            Zombie zombie = detected.obj.GetComponentInParent<Zombie>();

            if (zombie != null &&
                zombie.isActiveAndEnabled &&
                seenZombies.Add(zombie))
            {
                facts.Add(new Fact(
                    FactType.ZombieSeen,
                    FactLifetime.Transient,
                    !string.IsNullOrEmpty(zombie.EntityId) ? zombie.EntityId : zombie.gameObject.name,
                    distance: Vector2.Distance(transform.position, zombie.transform.position)
                ));
            }

            // Available-lever (+ threatened lever) observation.
            LeverScript lever = detected.obj.GetComponentInParent<LeverScript>();

            if (lever != null &&
                lever.isActiveAndEnabled &&
                seenLevers.Add(lever))
            {
                // Use a finite distance for no zombies; Python rejects Infinity.
                float nearestZombieDistance = 9999f;

                foreach (Zombie zombies in allZombies)
                {
                    float distance = Vector2.Distance(lever.transform.position, zombies.transform.position);

                    if (distance < nearestZombieDistance)
                    {
                        nearestZombieDistance = distance;
                    }
                }

                facts.Add(new Fact(
                    FactType.LeverSeen,
                    FactLifetime.Transient,
                    lever.gameObject.name,
                    distance: Vector2.Distance(transform.position, lever.transform.position),
                    nearestZombieDistance: nearestZombieDistance,
                    isOn: lever.isOn
                ));
            }

            // Other living agents look human. Their infection status is never observed here.
            Agent human = detected.obj.GetComponentInParent<Agent>();

            if (human != null &&
                human != self &&
                human.isActiveAndEnabled &&
                human.Health > 0 &&
                ScaspFactPayload.IsAtom(human.AgentId) &&
                seenHumans.Add(human))
            {
                facts.Add(new Fact(
                    FactType.HumanSeen,
                    FactLifetime.Transient,
                    human.AgentId,
                    distance: Vector2.Distance(transform.position, human.transform.position),
                    health: human.Health
                ));
            }
        }

        lastTransientFactCount = facts.Count;
        return facts.AsReadOnly();
    }

    // Execute the action selected by the AI strategy.
    public void Execute(string action, string target = "")
    {
        DetectObjects();
        target = target ?? "";

        // Keep searches/movement alive across identical periodic decisions.
        // Fleeing is re-evaluated so its direction tracks a moving zombie.
        // An attack keeps tracking its victim; biteAttemptCooldown limits repeated requests.
        if (action == currentAction && target == currentTarget && action != "run_from" &&
            (isMoving || isSearchingForLever || isSearchingForBarricade ||
             action == "wait" || action == "hold_barricade" ||
             (action == "attack_human" && movementTarget != null)))
            return;

        if (action != currentAction || target != currentTarget)
            warnedMissingBiteHandler = false;

        currentAction = action;
        currentTarget = target;
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

            case "move_to_human":
                MoveToHuman(target);
                break;

            case "attack_human":
                AttackHuman(target);
                break;

            case "wait":
                Wait();
                break;

            default:
                Debug.LogWarning($"{name}: Unknown action '{action}'", this);
                break;
        }
    }

    // Move the NPC toward its current destination.
    private void UpdateMovement()
    {
        // A bite is requested only after we actually reach the target's interaction range.
        if (currentAction == "attack_human" && movementTarget != null)
        {
            Agent victim = movementTarget.GetComponent<Agent>();
            if (!CanBite(victim))
            {
                StopMovement();
                return;
            }

            if (Vector2.Distance(transform.position, victim.transform.position) <= interactionRange)
            {
                // Hold position but keep tracking, so a fleeing victim is pursued again.
                isMoving = false;
                TryRequestBite(victim);
                return;
            }

            isMoving = true;
        }

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

        // s(CASP) can also flee a suspected half-zombie, identified by its agent ID.
        if (zombie == null && !string.IsNullOrEmpty(target))
            zombie = FindNearest("Human", target);

        if (zombie == null)
        {
            Debug.LogWarning($"{name}: Zombie '{target}' not detected.");
            return;
        }

        Vector2 direction = transform.position - zombie.transform.position;

        if (direction.sqrMagnitude < 0.01f) {
            direction = Vector2.right;
        }

        Vector2 fleePosition = (Vector2)transform.position + direction.normalized * fleeDistance;

        MoveToPosition(fleePosition);

        Debug.Log($"{name}: Running from {zombie.name}");
    }

    // Defend the specified barricade.
    protected virtual void HoldBarricade(string target)
    {
        isMoving = false;
        movementTarget = null;

        Debug.Log($"{name}: Holding barricade {target}");

        // Window repairs when the NPC overlaps its trigger. Approach its center
        // rather than stopping just outside the trigger at distance one.
        GameObject barricade = FindNearest("Barricade", target);
        if (barricade != null) MoveToObject(barricade, 0f);
    }

    // Move toward the specified barricade.
    protected virtual void MoveToBarricade(string target)
    {
        GameObject barricade = FindNearest("Barricade", target);

        if (barricade == null)
        {
            Debug.LogWarning($"{name}: Barricade '{target}' not detected.");
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

    // Approach a living human chosen by s(CASP), using the same actor ID as NpcState.
    protected virtual void MoveToHuman(string target)
    {
        GameObject human = FindNearest("Human", target);

        if (human == null)
        {
            Debug.LogWarning($"{name}: Human '{target}' not detected.", this);
            return;
        }

        MoveToObject(human, interactionRange);
    }

    // Move into range for an s(CASP) attack_human action, then request the bite.
    // Samuel's gameplay controller owns success, animation, infection, and timer reset.
    protected virtual void AttackHuman(string target)
    {
        Agent self = GetComponent<Agent>();
        if (self == null || !self.IsHalfZombie)
        {
            Debug.LogWarning($"{name}: Ignoring attack_human because this agent is not a half-zombie.", this);
            return;
        }

        GameObject humanObject = FindNearest("Human", target);
        if (humanObject == null)
        {
            Debug.LogWarning($"{name}: Bite target '{target}' not detected.", this);
            return;
        }

        // Track the victim; UpdateMovement requests the bite once it is in range.
        MoveToObject(humanObject, interactionRange);

        Agent human = humanObject.GetComponent<Agent>();
        if (CanBite(human) && Vector2.Distance(transform.position, human.transform.position) <= interactionRange)
        {
            isMoving = false;
            TryRequestBite(human);
        }
    }

    // Only a living half-zombie with an open window may bite a different living agent.
    private bool CanBite(Agent victim)
    {
        Agent self = GetComponent<Agent>();
        return self != null && self.IsHalfZombie && !self.BiteWindowExpired && self.Health > 0 &&
               victim != null && victim != self && victim.isActiveAndEnabled && victim.Health > 0;
    }

    private void TryRequestBite(Agent human)
    {
        if (!CanBite(human) || Time.time < nextBiteRequestTime)
            return;

        if (BiteRequested == null)
        {
            if (!warnedMissingBiteHandler)
            {
                Debug.LogWarning($"{name}: attack_human reached {human.name}, but no bite mechanic subscribed to AiAction.BiteRequested.", this);
                warnedMissingBiteHandler = true;
            }
            return;
        }

        nextBiteRequestTime = Time.time + biteAttemptCooldown;

        // Witnesses learn of the attempt only; the mechanic decides whether it succeeds.
        AttackWitnessReporter.ReportBiteAttempt(GetComponent<Agent>(), human);
        BiteRequested.Invoke(human);
        Debug.Log($"{name}: Requested bite against {human.name}.", this);
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

    // Called on death or when a controller needs to cancel ongoing physical behavior.
    public void StopAllActions()
    {
        isSearchingForLever = false;
        isSearchingForBarricade = false;
        currentAction = null;
        currentTarget = "";
        warnedMissingBiteHandler = false;
        StopMovement();
    }

    private void StopMovement()
    {
        isMoving = false;
        movementTarget = null;
    }

    private void SearchForLever()
    {
        GameObject lever = FindNearest("Lever", "", true);

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
            string candidateId = null;

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
                    candidateId = zombie.EntityId;
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

                case "Human":
                    Agent human = detected.obj.GetComponentInParent<Agent>();
                    if (human == null || human == GetComponent<Agent>() || !human.isActiveAndEnabled || human.Health <= 0)
                        continue;

                    candidate = human.gameObject;
                    candidateId = human.AgentId;
                    break;

                default:
                    continue;
            }

            if (!string.IsNullOrEmpty(target) && candidate.name != target && candidateId != target)
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

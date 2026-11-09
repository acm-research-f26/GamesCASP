using System;
using System.Collections.Generic;

// Per-agent knowledge. Decisions remain local to BehaviorTree.
public class Blackboard
{
    private readonly Agent owner;
    private readonly LeverManager leverManager;

    public bool searchingForLever; // mutable executor state
    public bool searchingForBarricade; // mutable executor state
    public List<Fact> visibleLevers = new List<Fact>(); // transient observations
    public bool canSeeLever;
    public bool lastLever; // derived mutable progress, not a permanent latch
    public bool canSeeBarricade;
    public List<Fact> visibleZombies = new List<Fact>();
    public int numZombiesNearby;
    public float? closestZombieDistance = float.PositiveInfinity;
    public int health;
    public float visionRadius = 5f;
    public int totalLevers;
    public int activatedLevers;
    // Humans this NPC can see right now, cleared after every decision like the zombies
    // These are the same HumanSeen observations s(CASP) gets, they never say who is infected
    public List<Fact> visibleHumans = new List<Fact>();

    private readonly Dictionary<string, BlackboardPlayer> playerKnowledge =
        new Dictionary<string, BlackboardPlayer>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, BlackboardPlayer> PlayerKnowledge { get; }
    public string OwnerId => owner.AgentId;
    public bool HalfZombie { get; private set; }
    public float? FirstInfectedAt { get; private set; }
    public float? BiteWindowStartedAt { get; private set; }
    public float CurrentTime { get; private set; }
    public float SecondsSinceBite { get; private set; }
    public float TimeRemaining { get; private set; }
    public float BiteRange { get; private set; } = 1f;
    public bool BiteWindowExpired => HalfZombie && TimeRemaining <= 0f;
    public bool InCriticalBiteStage { get; private set; }

    public bool KnowsHalfZombie(string id) =>
        id != null && playerKnowledge.TryGetValue(id, out var player) && player.KnownHalfZombie;

    internal void RecordInfection(float startedAt)
    {
        FirstInfectedAt = startedAt;
    }

    private BlackboardPlayer RememberPlayer(string id)
    {
        if (!playerKnowledge.TryGetValue(id, out var player))
        {
            player = new BlackboardPlayer(id);
            playerKnowledge.Add(id, player);
        }
        return player;
    }

    private void Learn(string id, InfectionEvidence evidence)
    {
        if (!string.IsNullOrWhiteSpace(id) && id != OwnerId)
            RememberPlayer(id).Observe(evidence);
    }

    public Blackboard(Agent owner, LeverManager leverManager)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.leverManager = leverManager ?? throw new ArgumentNullException(nameof(leverManager));
        PlayerKnowledge = new System.Collections.ObjectModel.ReadOnlyDictionary<string, BlackboardPlayer>(playerKnowledge);
        RefreshPersistentState();
    }

    // Read authoritative components instead of making static accesses or keeping stale copies.
    public void RefreshPersistentState()
    {
        health = owner.Health;
        totalLevers = leverManager.TotalLevers;
        activatedLevers = leverManager.ActivatedLevers;
        lastLever = totalLevers - activatedLevers == 1;
        CurrentTime = UnityEngine.Time.time;
        HalfZombie = owner.IsHalfZombie;
        InCriticalBiteStage = owner.InCriticalBiteStage;
        BiteWindowStartedAt = HalfZombie ? (float?)owner.BiteWindowStartedAt : null;

        if (BiteWindowStartedAt.HasValue)
        {
            // A tree created during the role switch can read the initial start from the Agent.
            // Later renewals update the current window without overwriting that first observation.
            float startedAt = BiteWindowStartedAt.Value;
            if (!FirstInfectedAt.HasValue)
                RecordInfection(startedAt);

            SecondsSinceBite = Math.Max(0f, CurrentTime - startedAt);
            TimeRemaining = Math.Max(0f, startedAt + Agent.BiteWindowSeconds - CurrentTime);
        }
        else
        {
            FirstInfectedAt = null;
            SecondsSinceBite = 0f;
            TimeRemaining = 0f;
        }
    }

    public void RefreshSearchState(AiAction executor)
    {
        visionRadius = executor.detectionRange;
        BiteRange = executor.interactionRange;
        searchingForLever = executor.IsSearchingForLever;
        searchingForBarricade = executor.IsSearchingForBarricade;
    }

    public void update(Fact fact)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts must be fetched, not stored.", nameof(fact));

        if (fact.Type == FactType.ZombieAttackWitnessed)
            Learn(fact.TargetId, InfectionEvidence.ZombieAttack);
        if (fact.Type == FactType.BiteAttemptWitnessed)
            Learn(fact.SubjectId, InfectionEvidence.BiteAttempt);
        if (fact.Type == FactType.BiteWitnessed)
        {
            Learn(fact.SubjectId, InfectionEvidence.ConfirmedBiter);
            Learn(fact.TargetId, InfectionEvidence.ConfirmedVictim);
        }

        if (fact.Type == FactType.LeverActivated)
        {
            RefreshPersistentState();
        }

        if (fact.Type == FactType.AgentAttacked)
        {
            // Zombie.SetHealth already applies real damage. Do not decrement twice.
            RefreshPersistentState();
        }

        if (fact.Type == FactType.LeverSearchStarted)
        {
            searchingForLever = true;
            searchingForBarricade = false;
        }

        if (fact.Type == FactType.LeverSearchStopped)
        {
            searchingForLever = false;
        }

        if (fact.Type == FactType.BarricadeSearchStarted)
        {
            searchingForBarricade = true;
            searchingForLever = false;
        }

        if (fact.Type == FactType.BarricadeSearchStopped)
        {
            searchingForBarricade = false;
        }
    }

    public void updateTransientFacts(IReadOnlyList<Fact> transientFacts)
    {
        if (transientFacts == null) throw new ArgumentNullException(nameof(transientFacts));
        foreach (Fact fact in transientFacts)
        {
            if (fact == null) continue;
            if (fact.Type == FactType.BarricadeSeen)
                canSeeBarricade = true;
            if (fact.Type == FactType.LeverSeen && !(fact.IsOn ?? true))
            {
                visibleLevers.Add(fact);
                canSeeLever = true;
            }

            if (fact.Type == FactType.ZombieSeen)
            {
                visibleZombies.Add(fact);
                numZombiesNearby++;
                if (fact.Distance.HasValue &&
                    (!closestZombieDistance.HasValue || fact.Distance.Value < closestZombieDistance.Value))
                    closestZombieDistance = fact.Distance;
            }

            // Keep the visible humans so a half zombie tree can pick a target
            if (fact.Type == FactType.HumanSeen)
            {
                visibleHumans.Add(fact);
                if (!string.IsNullOrWhiteSpace(fact.SubjectId) && fact.SubjectId != OwnerId)
                    RememberPlayer(fact.SubjectId);
            }
        }
    }

    public void clearTransientState()
    {
        visibleLevers.Clear();
        visibleZombies.Clear();
        visibleHumans.Clear();
        canSeeLever = false;
        canSeeBarricade = false;
        numZombiesNearby = 0;
        closestZombieDistance = float.PositiveInfinity;
        // Mutable search flags and progress survive transient cleanup.
    }

    public Fact getClosestObservation(IReadOnlyList<Fact> observations)
    {
        Fact closest = null;
        foreach (Fact observation in observations)
        {
            if (observation == null || !observation.Distance.HasValue) continue;
            if (closest == null || observation.Distance.Value < closest.Distance.Value)
                closest = observation;
        }
        return closest;
    }

    public Fact getPreferredVisibleLever()
    {
        Fact preferredLever = null;
        foreach (Fact lever in visibleLevers)
        {
            if (lever == null || !lever.Distance.HasValue || !lever.NearestZombieDistance.HasValue) continue;
            if (preferredLever == null)
            {
                preferredLever = lever;
                continue;
            }

            bool leverIsSafe = lever.NearestZombieDistance.Value > 5f;
            bool preferredIsSafe = preferredLever.NearestZombieDistance.Value > 5f;
            if (leverIsSafe && !preferredIsSafe)
            {
                preferredLever = lever;
            }
            else if (leverIsSafe && preferredIsSafe)
            {
                if (lever.Distance.Value < preferredLever.Distance.Value) preferredLever = lever;
            }
            else if (!leverIsSafe && !preferredIsSafe)
            {
                if (lever.NearestZombieDistance.Value > preferredLever.NearestZombieDistance.Value)
                    preferredLever = lever;
            }
            // Equal-ranked candidates retain the first observed lever.
        }
        return preferredLever;
    }
}

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

    public Blackboard(Agent owner, LeverManager leverManager)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.leverManager = leverManager ?? throw new ArgumentNullException(nameof(leverManager));
        RefreshPersistentState();
    }

    // Read authoritative components instead of making static accesses or keeping stale copies.
    public void RefreshPersistentState()
    {
        health = owner.Health;
        totalLevers = leverManager.TotalLevers;
        activatedLevers = leverManager.ActivatedLevers;
        lastLever = totalLevers - activatedLevers == 1;
    }

    public void RefreshSearchState(AiAction executor)
    {
        visionRadius = executor.detectionRange;
        searchingForLever = executor.IsSearchingForLever;
        searchingForBarricade = executor.IsSearchingForBarricade;
    }

    public void update(Fact fact)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts must be fetched, not stored.", nameof(fact));

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
            if (fact.Type == FactType.LeverSeen)
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
        }
    }

    public void clearTransientState()
    {
        visibleLevers.Clear();
        visibleZombies.Clear();
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

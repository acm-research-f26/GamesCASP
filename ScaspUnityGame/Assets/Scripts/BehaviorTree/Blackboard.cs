using JetBrains.Annotations;
using NUnit.Framework.Internal.Filters;
using System.Collections.Generic;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

// unity will send the game state here!
// all nodes will read from this class
public class Blackboard
{
    private AiAction aiAction = new AiAction();
    public bool searchingForLever; // mutable
    public bool searchingForBarricade; // mutable
    public bool canSeeLever; // transient
    public bool lastLever; // permanent
    public bool canSeeBarricade; // transient
    public int numZombiesNearby; // transient, links to the number of "Zombie" objects in AiAction's DetectedObjects
    public float? closestZombieDistance; // transient, updates with findNearest("Zombie") from AiAction (?)
    public int health; // mutable, maybe updates with another variable?

    // how far each NPC can see, same as detectionRange from AiAction
    public float visionRadius = 5f;

    // use separate vars so as not to accidentally overrupdate totalLevers/activatedLevers
    public int totalLevers = LeverManager.totalLevers;
    public int activatedLevers = LeverManager.activatedLevers;

    // update each type of fact
    public void update(Fact fact)
    {
        // permanent
        if (fact.Type == FactType.LeverActivated)
        {
            // activated levers in game is linked to LeverManager.cs
            // everytime the latter updates, the former will too
            // probably should just make a reference var for this
            activatedLevers = LeverManager.activatedLevers;

            // last lever condition
            if (activatedLevers == (totalLevers - 1))
            {
                lastLever = true;
            }
        }

        // mutable
        if (fact.Type == FactType.AgentAttacked)
        {
            health--;
            if (health == 0)
            {
                // agent is dead
                // to-do: delete the entire gameobject
            }
        }

        // mutable
        if (fact.Type == FactType.LeverSearchStarted)
        {
            searchingForLever = true;
            // agent can only search for either a lever or a barricade at once
            searchingForBarricade = false;
        }

        // mutable
        if (fact.Type == FactType.LeverSearchStopped)
        {
            searchingForLever = false;
        }

        // mutable
        if (fact.Type == FactType.BarricadeSearchStarted)
        {
            searchingForBarricade = true;
            searchingForLever = false;
        }

        // mutable
        if (fact.Type == FactType.BarricadeSearchStopped)
        {
            searchingForBarricade = false;
        }
    }

    // update ALL transient facts with their current state in the game
    public void updateTransientFacts(IReadOnlyList<Fact> transientFacts)
    {
        foreach (Fact fact in transientFacts)
        {
            // transient
            if (fact.Type == FactType.ZombieSeen)
            {
                numZombiesNearby++;

                // keep only the distance of the closest zombie
                if (fact.Distance < closestZombieDistance)
                {
                    closestZombieDistance = fact.Distance;
                }
            }
        }
    }

    // clear ALL transient facts
    // only clear bools that were changed by updateTransientFacts()
    public void clearTransientState(IReadOnlyList<Fact> transientFacts)
    {
        // reset ALL transient facts!
        numZombiesNearby = 0;
        closestZombieDistance = 0;

        canSeeLever = false;
        canSeeBarricade = false;
    }
}

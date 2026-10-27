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
    public bool searchingForLever; // transient
    public bool searchingForBarricade; //transient
    public bool canSeeLever; // transient
    public bool lastLever; // permanent
    public bool canSeeBarricade; // transient
    public int numZombiesNearby; // transient, links to the number of "Zombie" objects in AiAction's DetectedObjects
    public int closestZombieDistance; // transient, updates with findNearest("Zombie") from AiAction (?)
    public int health; // mutable, maybe updates with another variable?

    // how far each NPC can see, linked with detectionRange from AiAction
    public float visionRadius = 5f;

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

        // transient
        if (fact.Type == FactType.AgentAttacked)
        {
            health--;
            if (health == 0)
            {
                // agent is dead
                // delete the entire gameobject
            }
        }

        // transient
        if (fact.Type == FactType.ZombieSeen)
        {
            // how far away is the zombie?
            // if it's within fleeDistance from AiAction, update ZombieNearby to be true
        }
    }

    // update ALL transient facts
    public void updateTransientFacts(IReadOnlyList<Fact> transientFacts)
    {
        foreach (Fact fact in transientFacts)
        {
            // update with current state of transient facts in the game
            // will prolly involve me using DetectObjects in AiAction
        }
    }

    // clear ALL transient facts
    // only clear bools that were changed by updateTransientFacts()
    public void clearTransientState(IReadOnlyList<Fact> transientFacts)
    {
        foreach (Fact fact in transientFacts)
        {
            // reset all fact values!
            numZombiesNearby = 0;
            closestZombieDistance = 0;
        }
    }
}

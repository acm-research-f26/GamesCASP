using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class Blackboard
{
    // unity will send the game state here!
    // all nodes will read from this class

    public bool canSeeLever; // transient
    public bool lastLever; // permanent
    public bool canSeeBarricade; // transient
    public bool zombieNearby; // transient

    public List<Fact> allFacts = new List<Fact>();

    public List<Fact> getAllTransientFacts()
    {
        List<Fact> transientFacts = new List<Fact>();
        foreach (Fact facts in allFacts)
        {
            if (facts.type == FactType.transient)
            {
                transientFacts.Add(facts);
            }
        }

        return transientFacts;
    }

    // placeholder value for how far each NPC can see
    // link this with some unity function to know which facts to store
    // i.e. a zombieNear fact will only be stored if the zombie is within the vision radius
    public double visionRadius = 10.0;
}

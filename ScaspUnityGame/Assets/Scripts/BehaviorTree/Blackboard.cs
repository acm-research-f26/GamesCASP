using Unity.Collections;
using UnityEngine;

public class Blackboard
{
    // unity will send the game state here!
    // all nodes will read from this class

    public bool lastLever = false;
    public bool canSeeLever = true;

    // placeholder value for how far each NPC can see
    public double visionRadius = 10.0;
}

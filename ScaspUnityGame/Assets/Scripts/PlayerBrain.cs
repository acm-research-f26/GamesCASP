using System.Collections.Generic;
using UnityEngine;

public class PlayerBrain : MonoBehaviour
{
    public bool isLastLever;
    private Blackboard state = new Blackboard();
    private Node root;

    void Start()
    {
        root = new Sequence(new List<Node> 
        {
            new LastLeverCondition(state),
            new PullLever()
        }
        );
    }

    void Update()
    {
        state.lastLever = isLastLever;
        root.Tick();
    }
}

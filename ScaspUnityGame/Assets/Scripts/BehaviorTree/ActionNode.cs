using Unity.VisualScripting;
using UnityEngine;

public abstract class ActionNode : Node
{
    // action nodes will override this with their specific actions
    public abstract bool execute();

    public override bool Tick()
    {
        return execute();
    }
}

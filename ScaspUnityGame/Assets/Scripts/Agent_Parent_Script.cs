
using UnityEngine;

public class Agent_Parent_Script : MonoBehaviour
{
    public virtual void ExecuteAction(string action, string target = "")
    {
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

            case "pull_lever":
                PullLever(target);
                break;

            case "move_to_lever":
                MoveToLever(target);
                break;

            case "hunt_lever":
                HuntLever();
                break;

            case "wait":
                Wait();
                break;

            default:
                Debug.LogWarning(
                    $"{name}: Unknown action '{action}'"
                );
                break;
        }
    }

    protected virtual void RunFrom(string target)
    {
        Debug.Log($"{name}: Run from {target}");
    }

    protected virtual void HoldBarricade(string target)
    {
        Debug.Log($"{name}: Hold barricade {target}");
    }

    protected virtual void MoveToBarricade(string target)
    {
        Debug.Log($"{name}: Move to barricade {target}");
    }

    protected virtual void PullLever(string target)
    {
        Debug.Log($"{name}: Pull lever {target}");
    }

    protected virtual void MoveToLever(string target)
    {
        Debug.Log($"{name}: Move to lever {target}");
    }

    protected virtual void HuntLever()
    {
        Debug.Log($"{name}: Hunt for lever");
    }

    protected virtual void Wait()
    {
        Debug.Log($"{name}: Wait");
    }
}

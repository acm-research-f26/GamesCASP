
using UnityEngine;

public class Agent_Parent_Script : MonoBehaviour
{
    [Tooltip("This agent's AI component. Must implement IFactStrategy.")]
    [SerializeField] private MonoBehaviour factStrategyComponent;
    private IFactStrategy runtimeFactStrategy;

    // Supports strategies created by code as well as components assigned in the Inspector.
    public void SetFactStrategy(IFactStrategy strategy)
    {
        runtimeFactStrategy = strategy ?? throw new System.ArgumentNullException(nameof(strategy));
    }

    public bool TryStoreFact(Fact fact)
    {
        if (fact == null)
            throw new System.ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new System.ArgumentException("Transient facts must be queried, not stored.", nameof(fact));

        var strategy = runtimeFactStrategy ?? (factStrategyComponent as IFactStrategy);
        if (strategy == null)
        {
            Debug.LogWarning($"{name}: Assign an AI component implementing IFactStrategy before receiving facts.", this);
            return false;
        }

        strategy.StoreFact(fact);
        return true;
    }

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

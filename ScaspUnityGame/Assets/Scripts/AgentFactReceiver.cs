using System;
using UnityEngine;

// Add to the agent root alongside its strategy owner.
[RequireComponent(typeof(Agent_Parent_Script))]
public class AgentFactReceiver : MonoBehaviour
{
    public event Action<Fact> FactReceived;

    public bool ReceiveFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));

        if (!GetComponent<Agent_Parent_Script>().TryStoreFact(fact))
            return false;

        // This notification means storage succeeded, not merely that delivery was attempted.
        FactReceived?.Invoke(fact);
        return true;
    }
}

using System;
using UnityEngine;

// Add to the agent root. The AI integration subscribes to FactReceived.
public class AgentFactReceiver : MonoBehaviour
{
    public event Action<Fact> FactReceived;

    public void ReceiveFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));

        Debug.Log($"{name} learned {fact.Type}: {fact.SubjectId}", this);
        // Strategy owner: subscribe and forward to aiStrategyObj.StoreFact(fact).
        FactReceived?.Invoke(fact);
    }
}

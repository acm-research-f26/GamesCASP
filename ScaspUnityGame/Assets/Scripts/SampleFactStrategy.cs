using System;
using System.Collections.Generic;
using UnityEngine;

// Attach to an agent and assign it to that agent's Fact Strategy Component field.
// Temporary in-memory implementation, independent of either production AI backend.
public class SampleFactStrategy : MonoBehaviour, IFactStrategy
{
    private readonly List<Fact> storedFacts = new List<Fact>();

    [Tooltip("Debug display of facts received during this Play Mode session.")]
    [SerializeField] private List<string> receivedFacts = new List<string>();

    public IReadOnlyList<Fact> StoredFacts => storedFacts.AsReadOnly();

    private void Awake()
    {
        receivedFacts.Clear();
    }

    public void StoreFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts should not be stored.", nameof(fact));

        storedFacts.Add(fact);
        string description = $"{fact.Type}: {fact.SubjectId}, target={fact.TargetId ?? "none"}, {fact.Lifetime}";
        receivedFacts.Add(description);
        Debug.Log($"{name}: Interface worked! Stored fact #{storedFacts.Count}: {description}", this);
    }

    [ContextMenu("Send Sample Fact Through Receiver (Play Mode)")]
    private void SendSampleFact()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode first.", this);
            return;
        }

        var receiver = GetComponent<AgentFactReceiver>();
        if (receiver == null)
        {
            Debug.LogError("Sample fact failed: add AgentFactReceiver to this GameObject.", this);
            return;
        }

        if (!receiver.ReceiveFact(new Fact(
            FactType.LeverActivated, FactLifetime.Permanent, "sample_lever")))
            Debug.LogError("Sample fact failed: assign a Fact Strategy Component on the agent.", this);
    }
}

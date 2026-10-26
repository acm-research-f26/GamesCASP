using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LeverScript))]
public class LeverFactEmitter : MonoBehaviour
{
    [Tooltip("Unique entity ID, for example lever1. Assign a different ID to each lever.")]
    [SerializeField] private string leverId;
    [SerializeField, Min(0f)] private float observationRadius = 5f;
    [Tooltip("Layers containing agent colliders.")]
    [SerializeField] private LayerMask agentLayers;

    private LeverScript lever;

    // MapManager supplies the same unique identity used by NPC world snapshots.
    public void SetLeverId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new System.ArgumentException("A lever needs a unique entity ID.", nameof(id));
        leverId = id;
    }

    private void Awake()
    {
        lever = GetComponent<LeverScript>();
    }

    private void OnEnable()
    {
        lever.Activated += OnActivated;
    }

    private void OnDisable()
    {
        lever.Activated -= OnActivated;
    }

    private void OnActivated(LeverScript source)
    {
        if (string.IsNullOrWhiteSpace(leverId))
        {
            Debug.LogWarning("Assign a unique lever ID before emitting facts.", this);
            return;
        }

        var fact = new Fact(FactType.LeverActivated, FactLifetime.Permanent, leverId);
        var recipients = new HashSet<AgentFactReceiver>();
        var hits = Physics2D.OverlapCircleAll(
            source.transform.position, observationRadius, agentLayers);

        // Draft perception rule: range only. Add line of sight here later.
        foreach (var hit in hits)
        {
            var agent = hit.GetComponentInParent<AgentFactReceiver>();
            if (agent != null && agent.isActiveAndEnabled && recipients.Add(agent))
                agent.ReceiveFact(fact);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, observationRadius);
    }
}

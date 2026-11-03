using System.Collections.Generic;
using UnityEngine;

// Turns explicit attack/bite gameplay events into memories for the agents that perceive them.
// Callers report real events; proximity alone is never treated as an attack.
// Perception matches transient observations: the observer must be within its own
// AiAction.detectionRange of both attacker and victim (range only, like LeverFactEmitter).
public static class AttackWitnessReporter
{
    // Permanent memories never change, so each observer needs each one only once.
    private static readonly HashSet<(AgentFactReceiver, string)> deliveredPermanent = new HashSet<(AgentFactReceiver, string)>();
    private static readonly Dictionary<(AgentFactReceiver, string), string> lastMutableTarget = new Dictionary<(AgentFactReceiver, string), string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        deliveredPermanent.Clear();
        lastMutableTarget.Clear();
    }

    // A full zombie landed a hit. Witnesses, including the victim, remember it permanently.
    // The victim also updates its mutable AgentAttacked knowledge (latest attacker).
    public static int ReportZombieAttack(Zombie zombie, Agent victim)
    {
        if (zombie == null || victim == null || !ScaspFactPayload.IsAtom(zombie.EntityId) || !ScaspFactPayload.IsAtom(victim.AgentId))
            return 0;

        var victimReceiver = victim.GetComponent<AgentFactReceiver>();
        if (victimReceiver != null && CanWitness(victim, victimReceiver, zombie.transform.position, victim))
            DeliverMutable(victimReceiver, new Fact(FactType.AgentAttacked, FactLifetime.Mutable, victim.AgentId, zombie.EntityId));

        return DeliverPermanent(FactType.ZombieAttackWitnessed, zombie.EntityId, zombie.transform.position, victim, null);
    }

    // A half-zombie lunged at a victim in range. This is only an attempt.
    public static int ReportBiteAttempt(Agent biter, Agent victim) => ReportBite(FactType.BiteAttemptWitnessed, biter, victim);

    // Call only after the gameplay mechanic confirms a successful infectious bite.
    public static int ReportConfirmedBite(Agent biter, Agent victim) => ReportBite(FactType.BiteWitnessed, biter, victim);

    private static int ReportBite(FactType type, Agent biter, Agent victim)
    {
        if (biter == null || victim == null || biter == victim || !ScaspFactPayload.IsAtom(biter.AgentId) || !ScaspFactPayload.IsAtom(victim.AgentId))
            return 0;

        // The biter knows what it did; it does not witness itself.
        return DeliverPermanent(type, biter.AgentId, biter.transform.position, victim, biter);
    }

    private static int DeliverPermanent(FactType type, string attackerId, Vector3 attackerPosition, Agent victim, Agent attacker)
    {
        var fact = new Fact(type, FactLifetime.Permanent, attackerId, victim.AgentId);
        int delivered = 0;

        foreach (var receiver in Object.FindObjectsByType<AgentFactReceiver>())
        {
            Agent observer = receiver.GetComponent<Agent>();
            if (observer == attacker || !CanWitness(observer, receiver, attackerPosition, victim))
                continue;

            var key = (receiver, $"{type}|{attackerId}|{victim.AgentId}");
            if (deliveredPermanent.Contains(key))
                continue;

            if (receiver.ReceiveFact(fact))
            {
                deliveredPermanent.Add(key);
                delivered++;
            }
        }

        return delivered;
    }

    // Repeated hits by the same attacker are not new information; a different attacker is.
    private static void DeliverMutable(AgentFactReceiver receiver, Fact fact)
    {
        var key = (receiver, $"{fact.Type}|{fact.SubjectId}");
        if (lastMutableTarget.TryGetValue(key, out string target) && target == fact.TargetId)
            return;

        if (receiver.ReceiveFact(fact))
            lastMutableTarget[key] = fact.TargetId;
    }

    private static bool CanWitness(Agent observer, AgentFactReceiver receiver, Vector3 attackerPosition, Agent victim)
    {
        if (observer == null || !receiver.isActiveAndEnabled || observer.Health <= 0)
            return false;

        // A living victim always perceives an attack on itself.
        if (observer == victim)
            return true;

        AiAction senses = observer.GetComponent<AiAction>();
        float range = senses != null ? senses.detectionRange : 0f;
        Vector2 position = observer.transform.position;
        return Vector2.Distance(position, attackerPosition) <= range && Vector2.Distance(position, victim.transform.position) <= range;
    }
}

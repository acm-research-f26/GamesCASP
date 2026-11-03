using UnityEngine;

[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(AiAction))]
public class HalfZombieBiteMechanic : MonoBehaviour
{
    private Agent agent;
    private AiAction actions;

    private void Awake()
    {
        agent = GetComponent<Agent>();
        actions = GetComponent<AiAction>();
    }

    private void OnEnable()
    {
        actions.BiteRequested += HandleBite;
    }

    private void OnDisable()
    {
        actions.BiteRequested -= HandleBite;
    }

    private void HandleBite(Agent victim)
    {
        // Only living, active half-zombies can infect someone.
        if (agent.Health <= 0 || !agent.IsHalfZombie || agent.BiteWindowExpired)
            return;

        // Cannot bite yourself, dead agents, or already infected agents.
        if (victim == null || victim == agent ||
            victim.Health <= 0 || victim.IsHalfZombie)
            return;

        // AiAction has already checked bite range and cooldown.
        if (!victim.MarkHalfZombie())
            return;

        // Successful bites renew the attacker's window.
        agent.RenewBiteWindow();

        // Notify other NPCs about the confirmed bite.
        AttackWitnessReporter.ReportConfirmedBite(agent, victim);

        Debug.Log($"[BITE SUCCESS] {agent.AgentId} infected {victim.AgentId}!");
    }
}
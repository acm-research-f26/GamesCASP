# Fact pipeline draft

This draft covers lifetime classification and lever-to-strategy delivery for issue #9.

`LeverScript.Activated -> LeverFactEmitter -> AgentFactReceiver -> Agent_Parent_Script.TryStoreFact -> IFactStrategy.StoreFact`

## Integration

1. Have each AI strategy implement `IAgentStrategy`, which extends `IFactStrategy` with `DecideNextAction(Agent_Parent_Script)`. Backend storage, mutable replacement, and permanent-fact deduplication remain the strategy owner's responsibility.
2. Add `AgentFactReceiver` to each agent root. Unity requires an `Agent_Parent_Script` (or existing derived `Player`) beside it.
3. Assign the agent's strategy component to **Strategy Component** on the parent script, or call `SetStrategy` for a strategy created by code. Each agent needs its own strategy instance. Existing serialized `factStrategyComponent` assignments migrate through `FormerlySerializedAs`; the assigned component must now implement `IAgentStrategy`. The survivor prefab is wired to its existing `SampleFactStrategy`.
4. Add `LeverFactEmitter` to each lever. Give each scene instance a unique ID such as `lever1`, set its observation radius, and select the layers containing agent colliders. The default empty mask delivers to nobody.
5. Keep the lever's existing `LeverManager` and `SpriteRenderer` configured. Activating colliders must have the `Player` tag and the normal Unity trigger/rigidbody configuration.

Radius is the current observation rule; walls do not block delivery. Multiple colliders on one agent produce one delivery per activation. Permanence assumes levers never reset during the game. Facts are immutable and may safely be shared between recipients.

`ReceiveFact` returns false and warns if no strategy is configured. `FactReceived` fires only after `StoreFact` returns successfully. A backend that performs asynchronous persistence must manage its own acknowledgement/retry behavior; this handoff does not prove remote persistence.

The game loop calls `Agent_Parent_Script.RequestDecision()` when a decision is needed. The same strategy that receives facts handles that request and calls `agent.ExecuteAction(action, target)` with its choice. An asynchronous backend can do this when its response arrives. Decision requests are not automatically scheduled. Shared execution stays in the parent's overridable action methods; these currently log actions, as before the delegation refactor. `SampleFactStrategy` stores facts in memory and chooses `wait`; production behavior-tree and s(CASP) adapters remain future work.

## Verification

Verified with Unity 6000.6.0f1 on October 19, 2026: all 31 checks passed; Unity exited with code 0. The current output is in `Logs/strategy-verification.log` (local, ignored by Git); `../fact-verification.log` is the earlier fact-only run.

In Unity, run **Tools > Facts > Verify Draft Pipeline**. Or launch the matching Unity Editor with:

```text
-batchmode -nographics -projectPath <ScaspUnityGame path> -executeMethod FactPipelineVerification.Run -quit -logFile <log path>
```

The verifier creates temporary objects, invokes the real lever trigger handler, uses real `Physics2D.OverlapCircleAll` for recipient selection, and checks a recording strategy's calls. It removes its temporary objects afterward. It checks range, layers, disabled receivers, duplicate colliders, repeated activation, payloads, notification ordering, and invalid/transient input. It also verifies shared fact/decision routing, Inspector and runtime assignment, missing/invalid strategies, all seven action routes, unknown actions, and survivor prefab wiring. It does not simulate physical movement into a trigger or exercise either production AI backend.

## Remaining issue work

- Add per-agent current transient observations (visibility and distances), queried before decisions. Transient facts deliberately cannot pass through `TryStoreFact`.
- Add actual damage/attack hooks and additional observation sources. `AgentAttacked` and `ZombieSeen` are vocabulary, not implemented detectors.
- Agree on backend serialization and observer identity. The owning strategy supplies who knows a fact; subject/target identify the entities in the fact.
- Wire scene/prefab instances with Samuel, implement both strategy adapters with their owners, and verify actual trigger contacts in Play Mode.

The strategy integration changes the survivor prefab's component assignment and exposes explicit decision requests. It does not implement production AI backends, movement, interactions, or server protocol changes.

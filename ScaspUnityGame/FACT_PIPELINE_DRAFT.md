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

The game loop calls `Agent_Parent_Script.RequestDecision()` when a decision is needed. The same strategy that receives facts handles that request and calls `agent.ExecuteAction(action, target)` with its choice. An asynchronous backend can do this when its response arrives. `PlayerManager` now requests decisions for spawned s(CASP) NPCs after connection and every one second of game time, configurable through **Decision Interval**. Each request refreshes transient facts. `ScaspStrategy` coalesces requests while a previous decision is pending. Shared execution stays in the parent's overridable action methods, which delegate to `AiAction`. `SampleFactStrategy` stores facts in memory and chooses `wait`.

## Transient queries (issue #18)

Every `IAgentStrategy` now implements `IReadOnlyList<Fact> FetchTransientFacts()`.
It returns a fresh snapshot of current observations, with an empty collection when
nothing is observed. It must never return null, stored facts, or null entries.
`Agent_Parent_Script.FetchTransientFacts()` uses the same configured backend as
storage and decisions, validates the lifetime, and copies the result into a read-only
snapshot. A missing strategy warns and returns an empty result.

Select observers explicitly; the query does not discover agents or hardcode identities:

```csharp
var observations = Fact.FetchTransientFacts(new[] { firstAgent, secondAgent });
foreach (var fact in observations[firstAgent])
    Debug.Log($"{fact.SubjectId}: {fact.Distance}");
```

The result is a read-only dictionary keyed by the requested agent, including agents
with no observations. Duplicate agents are queried once; null/destroyed references
are skipped. A null selection throws. Pass the nearby agents, team, or other subset
appropriate to the caller. This API does not impose sharing or visibility permissions
between agents; selection belongs to the caller.

Both current strategies delegate to `AiAction` to refresh 2D physics detection at
query time. Active zombies in detection range yield `ZombieSeen` facts with a
transient lifetime, the zombie's runtime `EntityId` as `SubjectId`, and current 2D
distance in `Distance` (an optional, nonnegative finite value on `Fact`). Child
colliders resolve to the zombie root and multiple colliders produce one observation.
Detection uses the existing radius rule, without wall occlusion. Each zombie gets
a unique Prolog-safe ID that survives display-name changes; action targets resolve
through that same ID. Moving away or disabling the
zombie removes it from the next query; previous snapshots remain unchanged.

s(CASP)'s NPC request consumes these queried zombie facts. Its other world snapshot
fields retain their existing collection paths. Queries never call `StoreFact`, emit
`FactReceived`, or append to persistent memory. Future strategy implementations must
implement this method; additional transient fact types can use the same format.

Run **Tools > Facts > Verify Transient Queries**, or use the batch command below
with `-executeMethod TransientFactVerification.Run`. This verifies agent selection,
snapshot isolation, lifetime boundaries, physics freshness, collider deduplication,
both current strategies, and s(CASP) request snapshot integration.

Validation on October 23, 2026: game scripts and both verification scripts compile
against the installed Unity assemblies using Roslyn. The Unity batch run could not
execute because the Editor reported no valid license; the physics checks above
remain pending in a licensed Editor. This compilation check does not exercise the
Python server or an end-to-end decision round trip.

## Stored pipeline verification

Verified with Unity 6000.6.0f1 on October 19, 2026: all 31 checks passed; Unity exited with code 0. The current output is in `Logs/strategy-verification.log` (local, ignored by Git); `../fact-verification.log` is the earlier fact-only run.

In Unity, run **Tools > Facts > Verify Draft Pipeline**. Or launch the matching Unity Editor with:

```text
-batchmode -nographics -projectPath <ScaspUnityGame path> -executeMethod FactPipelineVerification.Run -quit -logFile <log path>
```

The verifier creates temporary objects, invokes the real lever trigger handler, uses real `Physics2D.OverlapCircleAll` for recipient selection, and checks a recording strategy's calls. It removes its temporary objects afterward. It checks range, layers, disabled receivers, duplicate colliders, repeated activation, payloads, notification ordering, and invalid/transient input. It also verifies shared fact/decision routing, Inspector and runtime assignment, missing/invalid strategies, all seven action routes, unknown actions, and survivor prefab wiring. It does not simulate physical movement into a trigger or exercise either production AI backend.

## Remaining issue work

- Extend transient observations beyond current nearby zombies as the game vocabulary grows. Transient facts deliberately cannot pass through `TryStoreFact`.
- Add actual damage/attack hooks and additional observation sources. `AgentAttacked` remains vocabulary without a damage hook.
- Agree on backend serialization and observer identity. The owning strategy supplies who knows a fact; subject/target identify the entities in the fact.
- Wire scene/prefab instances with Samuel, implement both strategy adapters with their owners, and verify actual trigger contacts in Play Mode.

The strategy integration changes the survivor prefab's component assignment and exposes explicit decision requests. It does not implement production AI backends, movement, interactions, or server protocol changes.

## Stored Python delivery (issue #30)

`ScaspStrategy.StoreFact` now validates and queues mutable/permanent facts for the
existing `ScaspClient` WebSocket. It sends immediately when connected, waits for
Python's write acknowledgment before removing a queued fact, and retries an
unacknowledged write in order. Python stores observer-scoped `stored_fact/6` clauses
in `facts_temp.pl`, deduplicates permanent facts, and replaces mutable entries by
observer/type/subject. Transient queries retain their separate snapshot path.

The earlier notes about locally stored s(CASP) facts not being sent are superseded
by this implementation. NPC decision queries still use their existing snapshots;
adding reasoning rules for stored knowledge is a separate task.

See [the payload, lifetime rules, and verification instructions](../DockerServer/Server/STORED_FACTS.md).
Run **Tools > Facts > Verify Python Storage** in Edit Mode for the full
receiver-to-Python-file check without Docker or s(CASP).

Validation on October 26, 2026 supersedes the earlier pending Unity execution note:
the stored Python path passed 12 checks, the existing pipeline passed 31 checks,
and transient queries passed 14 checks in Unity 6000.6.0f1. The transient checks
include prefab clone identity, rename stability, and action target resolution.
The Python server suite also passed all 12 tests. Storage verification uses the
actual Python handler and file writes; it does not exercise s(CASP) reasoning.

The storage verifier now instantiates the gameplay NPC and lever prefabs, checking
the receiver/strategy/client wiring and invoking the lever's real trigger handler.
Spawned levers receive their generated `lever_1`, `lever_2`, etc. identities from
`MapManager`, matching decision snapshots. The NPC prefab has one WebSocket client
and an `AgentFactReceiver`. A live Main-scene test against Docker also confirmed
`stored_fact(player1, permanent, lever_activated, lever_2, none, none).` in the normal
server file, with matching Python success and Unity acknowledgment logs.

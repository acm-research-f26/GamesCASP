using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// No scene changes: builds and destroys temporary agents far from the scene's content.
// Covers strategy selection, half-zombie state, human observations, bite requests,
// and witnessed-attack memories. Python and s(CASP) have their own tests.
public static class HalfZombieVerification
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class RecordingStrategy : IAgentStrategy
    {
        public readonly List<Fact> Facts = new List<Fact>();
        public void StoreFact(Fact fact) => Facts.Add(fact);
        public void DecideNextAction(Agent_Parent_Script agent) { }
        public int Count(FactType type) => Facts.FindAll(fact => fact.Type == type).Count;
    }

    // Probe dispatch without relying on movement or scene targets.
    private sealed class RecordingActions : AiAction
    {
        private void Record(string action) => Debug.Log($"{name}: {action}");
        protected override void MoveToHuman(string target) => Record("Move to human " + target);
        protected override void AttackHuman(string target) => Record("Attack human " + target);
        protected override void RunFrom(string target) => Record("Run from " + target);
        protected override void HoldBarricade(string target) => Record("Hold barricade " + target);
        protected override void MoveToBarricade(string target) => Record("Move to barricade " + target);
        protected override void PullLever(string target) => Record("Pull lever " + target);
        protected override void MoveToLever(string target) => Record("Move to lever " + target);
        protected override void HuntLever() => Record("Hunt for lever");
        protected override void Wait() => Record("Wait");
    }

    [MenuItem("Tools/Facts/Verify Half-Zombie Integration")]
    public static void Run()
    {
        var objects = new List<GameObject>();
        var strategies = new Dictionary<Agent, RecordingStrategy>();
        int checks = 0;
        Action<bool, string> check = (condition, description) =>
        {
            if (!condition) throw new Exception("FAIL: " + description);
            checks++;
            Debug.Log("PASS: " + description);
        };
        Func<string, Vector3, Agent> recorded = (id, position) =>
        {
            Agent agent = MakeAgent(objects, id, position, false);
            strategies[agent] = new RecordingStrategy();
            agent.SetStrategy(strategies[agent], "recording");
            return agent;
        };
        var origin = new Vector3(20000, 20000, 0);

        try
        {
            var managerObject = new GameObject("HalfZombieVerification_levers");
            objects.Add(managerObject);
            managerObject.AddComponent<LeverManager>().totalLevers = 3;

            // --- Strategy selection ---
            var both = MakeAgent(objects, "selection1", origin, true);
            var scasp = both.gameObject.AddComponent<ScaspStrategy>();
            var tree = both.gameObject.AddComponent<BehaviorTree>();
            check(both.ActiveStrategy == null &&
                  CaptureLogs(both.RequestDecision).Exists(message => message.Contains("Several enabled IAgentStrategy")),
                "Two enabled strategies without an explicit choice are reported, never picked arbitrarily");
            tree.enabled = false;
            check(ReferenceEquals(both.ActiveStrategy, scasp), "With the tree disabled, the s(CASP) strategy is selected");
            tree.enabled = true;
            scasp.enabled = false;
            check(ReferenceEquals(both.ActiveStrategy, tree), "With s(CASP) disabled, the behavior tree is selected");
            scasp.enabled = true;

            var reversed = MakeAgent(objects, "selection2", origin + Vector3.right * 50, true);
            var reversedTree = reversed.gameObject.AddComponent<BehaviorTree>();
            reversed.gameObject.AddComponent<ScaspStrategy>().enabled = false;
            check(ReferenceEquals(reversed.ActiveStrategy, reversedTree), "Selection does not depend on component order");

            SetInspectorStrategy(both, tree);
            check(ReferenceEquals(both.ActiveStrategy, tree), "An Inspector assignment wins over other enabled strategies");
            both.SetStrategy(scasp, "scasp");
            check(ReferenceEquals(both.ActiveStrategy, scasp) && scasp.enabled && !tree.enabled,
                "SetStrategy overrides the Inspector and leaves only the selected strategy enabled");
            both.SetStrategy(tree, "behavior_tree");
            check(ReferenceEquals(both.ActiveStrategy, tree) && tree.enabled && !scasp.enabled,
                "Runtime switching back disables the s(CASP) strategy, so its late responses cannot act");

            var sample = both.gameObject.AddComponent<SampleFactStrategy>();
            both.SetStrategy(sample, "sample");
            Object.DestroyImmediate(sample);
            check(ReferenceEquals(both.ActiveStrategy, tree), "A destroyed runtime strategy falls back to the Inspector choice");
            SetInspectorStrategy(both, both.GetComponent<AgentFactReceiver>());
            check(both.ActiveStrategy == null &&
                  !both.TryStoreFact(new Fact(FactType.LeverActivated, FactLifetime.Permanent, "lever1")),
                "A non-strategy Inspector assignment is rejected rather than silently replaced");

            var scaspOnly = MakeAgent(objects, "selection3", origin + Vector3.right * 60, true);
            var onlyScasp = scaspOnly.gameObject.AddComponent<ScaspStrategy>();
            check(ReferenceEquals(scaspOnly.ActiveStrategy, onlyScasp), "Only s(CASP) attached: it is selected");

            var treeOnly = MakeAgent(objects, "selection4", origin + Vector3.right * 70, true);
            var onlyTree = treeOnly.gameObject.AddComponent<BehaviorTree>();
            check(ReferenceEquals(treeOnly.ActiveStrategy, onlyTree) &&
                  CaptureLogs(treeOnly.RequestDecision).Contains($"{treeOnly.name}: Hunt for lever"),
                "Only a behavior tree attached: it initializes itself and chooses innocent actions");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/survivorPrefab.prefab");
            var assigned = new SerializedObject(prefab.GetComponent<Agent>()).FindProperty("strategyComponent").objectReferenceValue;
            check(prefab.GetComponent<BehaviorTree>() != null && assigned == prefab.GetComponent<ScaspStrategy>() &&
                  prefab.GetComponents<ScaspClient>().Length == 1 && prefab.GetComponent<AgentFactReceiver>() != null,
                "Gameplay prefab carries both strategies with an explicit s(CASP) default");

            // --- Half-zombie state ---
            var stateAgent = recorded("player5", origin + Vector3.up * 100);
            check(!stateAgent.IsHalfZombie && !stateAgent.RenewBiteWindow() && !stateAgent.BiteWindowExpired,
                "Agents start uninfected and have no window to renew");
            check(stateAgent.MarkHalfZombie() && stateAgent.IsHalfZombie &&
                  stateAgent.BiteWindowStartedAt == Time.time && !stateAgent.BiteWindowExpired,
                "Infection starts the bite window on the game clock");
            SetField(stateAgent, typeof(Agent), "biteWindowStartedAt", Time.time - 10f);
            float started = stateAgent.BiteWindowStartedAt;
            check(!stateAgent.MarkHalfZombie() && stateAgent.BiteWindowStartedAt == started,
                "Repeated infection (for example, further zombie hits) does not restart the window");
            check(stateAgent.RenewBiteWindow() && stateAgent.BiteWindowStartedAt == Time.time,
                "A gameplay-confirmed bite renews the window");
            stateAgent.ExecuteAction("hunt_lever");
            bool searchedWhileActive = stateAgent.GetComponent<AiAction>().IsSearchingForLever;
            SetField(stateAgent, typeof(Agent), "biteWindowStartedAt", Time.time - Agent.BiteWindowSeconds - 1f);
            stateAgent.ExecuteAction("hunt_lever");
            check(searchedWhileActive && stateAgent.BiteWindowExpired && !stateAgent.RenewBiteWindow() &&
                  !stateAgent.GetComponent<AiAction>().IsSearchingForLever,
                "An expired half-zombie cannot renew, and AI actions stop instead of executing");
            stateAgent.Initialize("innocent", stateAgent.transform.position.x, stateAgent.transform.position.y);
            stateAgent.ExecuteAction("hunt_lever");
            check(!stateAgent.IsHalfZombie && stateAgent.GetComponent<AiAction>().IsSearchingForLever,
                "Initialize resets infection and the agent acts again");
            var corpse = recorded("corpse1", origin + Vector3.up * 110);
            corpse.SetHealth(0);
            check(!corpse.MarkHalfZombie(), "Dead agents cannot become half-zombies");

            // --- Human observations and the s(CASP) snapshot ---
            Vector3 c = origin + Vector3.up * 200;
            var observer = recorded("player1", c);
            var seen = recorded("player2", c + Vector3.right * 2);
            var child = new GameObject("HalfZombieVerification_childCollider");
            child.transform.SetParent(seen.transform, false);
            child.AddComponent<CircleCollider2D>();
            seen.MarkHalfZombie();
            recorded("player3", c + Vector3.right).SetHealth(0);
            recorded("player4", c + Vector3.right * 20);
            recorded("player6", c + Vector3.down).enabled = false;
            MakeAgent(objects, null, c + Vector3.up, false);
            var nearbyZombie = MakeObject(objects, "observedZombie", c + Vector3.left * 1.5f).AddComponent<Zombie>();
            Physics2D.SyncTransforms();
            var observed = observer.FetchTransientFacts();
            var humans = observed.Where(FactType.HumanSeen);
            check(humans.Count == 1 && humans[0].SubjectId == "player2" && humans[0].Distance == 2 &&
                  humans[0].Health == 3 && humans[0].Lifetime == FactLifetime.Transient,
                "Only other living, enabled, identified agents in range are seen, once despite two colliders");
            var zombies = observed.Where(FactType.ZombieSeen);
            check(zombies.Count == 1 && zombies[0].SubjectId == nearbyZombie.EntityId && zombies[0].Distance == 1.5f,
                "Existing zombie observations are unchanged alongside human observations");
            var humanFields = new List<string>();
            foreach (FieldInfo field in typeof(NpcHumanState).GetFields())
                humanFields.Add(field.Name);
            check(string.Join(",", humanFields) == "id,health,visible,distance",
                "Human observations carry no infection status");

            var decider = MakeAgent(objects, "player8", c + Vector3.up * 0.1f, true);
            var deciderStrategy = decider.gameObject.AddComponent<ScaspStrategy>();
            Invoke(typeof(ScaspStrategy), deciderStrategy, "Awake");
            deciderStrategy.SetActorId("player8");
            decider.MarkHalfZombie();
            Physics2D.SyncTransforms();
            var snapshot = BuildState(deciderStrategy);
            var ids = new List<string>();
            foreach (var human in snapshot.humans)
                ids.Add(human.id);
            ids.Sort(StringComparer.Ordinal);
            check(snapshot.half_zombie && snapshot.bite_window_started_at == decider.BiteWindowStartedAt &&
                  snapshot.current_time == Time.time && snapshot.bite_window_seconds == Agent.BiteWindowSeconds &&
                  string.Join(",", ids) == "player1,player2" &&
                  snapshot.zombies.Length == 1 && snapshot.zombies[0].id == nearbyZombie.EntityId,
                "s(CASP) snapshot carries own status, game-clock timestamps, window length, humans, and zombies");
            string json = JsonUtility.ToJson(snapshot);
            check(json.Split(new[] { "\"half_zombie\"" }, StringSplitOptions.None).Length == 2,
                "Serialized snapshot mentions infection once: the actor's own status");
            decider.Initialize("innocent", decider.transform.position.x, decider.transform.position.y);
            snapshot = BuildState(deciderStrategy);
            check(!snapshot.half_zombie && snapshot.bite_window_started_at == 0f,
                "Uninfected snapshots carry no bite window");

            // --- Action mapping ---
            var mapping = new[] { "move_to_human", "attack_human", "run_from", "hold_barricade",
                "move_to_barricade", "pull_lever", "move_to_lever", "hunt_lever", "wait" };
            var expected = new[] { "Move to human player2", "Attack human player2", "Run from player2",
                "Hold barricade player2", "Move to barricade player2", "Pull lever player2",
                "Move to lever player2", "Hunt for lever", "Wait" };
            for (int i = 0; i < mapping.Length; i++)
            {
                int index = i;
                check(CaptureLogs(() => decider.ExecuteAction(mapping[index], "player2")).Contains($"{decider.name}: {expected[index]}"),
                    "Executor dispatches " + mapping[i]);
            }
            observer.ExecuteAction("run_from", "player2");
            check((bool)GetField(observer.GetComponent<AiAction>(), "isMoving"),
                "run_from also resolves a suspected human by agent ID");

            // --- Bite requests ---
            Vector3 f = origin + Vector3.up * 300;
            var biter = recorded("player10", f);
            var victim = recorded("player11", f + Vector3.right * 0.5f);
            var witness = recorded("player12", f + Vector3.up * 2);
            var distant = recorded("player13", f + Vector3.up * 30);
            var deadWitness = recorded("player14", f + new Vector3(1, 1, 0));
            deadWitness.SetHealth(0);
            var biterAction = biter.GetComponent<AiAction>();
            int requests = 0;
            Agent requested = null;
            biterAction.BiteRequested += bitten => { requests++; requested = bitten; };
            Physics2D.SyncTransforms();

            biter.ExecuteAction("attack_human", "player11");
            check(requests == 0, "attack_human is ignored for agents that are not half-zombies");
            biter.MarkHalfZombie();
            float biterWindow = biter.BiteWindowStartedAt;
            biter.ExecuteAction("move_to_human", "player11");
            biter.ExecuteAction("attack_human", "player11");
            check(requests == 1 && requested == victim, "In range, attack_human requests one bite against the chosen victim");
            for (int i = 0; i < 3; i++)
                biter.ExecuteAction("attack_human", "player11");
            for (int i = 0; i < 5; i++)
                Invoke(typeof(AiAction), biterAction, "UpdateMovement");
            check(requests == 1, "Repeated decisions and frames cannot re-fire a bite during the cooldown");
            check(!victim.IsHalfZombie && biter.BiteWindowStartedAt == biterWindow,
                "A bite request alone never infects the victim or renews the biter's window");
            check(strategies[witness].Count(FactType.BiteAttemptWitnessed) == 1 &&
                  strategies[victim].Count(FactType.BiteAttemptWitnessed) == 1 &&
                  strategies[biter].Count(FactType.BiteAttemptWitnessed) == 0 &&
                  strategies[distant].Count(FactType.BiteAttemptWitnessed) == 0 &&
                  strategies[deadWitness].Count(FactType.BiteAttemptWitnessed) == 0,
                "Attempts are remembered by observers in range and the victim, not the biter, distant, or dead agents");
            Fact attempt = strategies[witness].Facts.Find(fact => fact.Type == FactType.BiteAttemptWitnessed);
            check(attempt.Lifetime == FactLifetime.Permanent && attempt.SubjectId == "player10" && attempt.TargetId == "player11",
                "An attempt memory is permanent, with the biter as subject and the victim as target");
            check(strategies[witness].Count(FactType.BiteWitnessed) == 0, "An attempt is never recorded as a successful bite");

            // What Samuel's mechanic does after it confirms success.
            AttackWitnessReporter.ReportConfirmedBite(biter, victim);
            AttackWitnessReporter.ReportConfirmedBite(biter, victim);
            check(strategies[witness].Count(FactType.BiteWitnessed) == 1 && strategies[biter].Count(FactType.BiteWitnessed) == 0,
                "A confirmed bite is a separate memory, delivered once");

            Vector3 g = origin + Vector3.up * 400;
            var pursuer = recorded("player16", g);
            var runner = recorded("player17", g + Vector3.right * 3);
            var pursuerAction = pursuer.GetComponent<AiAction>();
            int pursuerRequests = 0;
            pursuerAction.BiteRequested += _ => pursuerRequests++;
            pursuer.MarkHalfZombie();
            Physics2D.SyncTransforms();
            pursuer.ExecuteAction("attack_human", "player17");
            check(pursuerRequests == 0 && (bool)GetField(pursuerAction, "isMoving") &&
                  (GameObject)GetField(pursuerAction, "movementTarget") == runner.gameObject,
                "Out of range, attack_human pursues the victim instead of biting");
            pursuer.transform.position = runner.transform.position + Vector3.left * 0.5f;
            Invoke(typeof(AiAction), pursuerAction, "UpdateMovement");
            check(pursuerRequests == 1, "The bite is requested once the pursuer is physically in range");
            SetField(pursuerAction, typeof(AiAction), "nextBiteRequestTime", 0f);
            pursuer.ExecuteAction("attack_human", "player16");
            check(pursuerRequests == 1, "An agent cannot target itself");
            SetField(pursuerAction, typeof(AiAction), "nextBiteRequestTime", 0f);
            runner.SetHealth(0);
            pursuer.ExecuteAction("move_to_human", "player17");
            pursuer.ExecuteAction("attack_human", "player17");
            Invoke(typeof(AiAction), pursuerAction, "UpdateMovement");
            check(pursuerRequests == 1, "Dead victims cannot be bitten");

            Vector3 h = origin + Vector3.up * 500;
            var unhandled = recorded("player18", h);
            recorded("player19", h + Vector3.right * 0.5f);
            var unhandledWitness = recorded("player20", h + Vector3.up);
            unhandled.MarkHalfZombie();
            Physics2D.SyncTransforms();
            unhandled.ExecuteAction("attack_human", "player19");
            check(strategies[unhandledWitness].Count(FactType.BiteAttemptWitnessed) == 0,
                "Without a subscribed bite mechanic no attempt happens, so nothing is witnessed");

            // --- Zombie attacks ---
            Vector3 z = origin + Vector3.up * 600;
            var zombie = MakeObject(objects, "zombie", z).AddComponent<Zombie>();
            var zombieVictim = recorded("player21", z + Vector3.right * 0.8f);
            var zombieWitness = recorded("player22", z + Vector3.up * 2);
            var zombieDistant = recorded("player23", z + Vector3.up * 30);
            for (int i = 0; i < 3; i++)
                AttackWitnessReporter.ReportZombieAttack(zombie, zombieVictim);
            Fact seenAttack = strategies[zombieWitness].Facts.Find(fact => fact.Type == FactType.ZombieAttackWitnessed);
            check(strategies[zombieWitness].Facts.Count == 1 && seenAttack.Lifetime == FactLifetime.Permanent &&
                  seenAttack.SubjectId == zombie.EntityId && seenAttack.TargetId == "player21" &&
                  strategies[zombieDistant].Facts.Count == 0,
                "Witnesses in range remember a zombie attack once, however many hits land");
            Fact attacked = strategies[zombieVictim].Facts.Find(fact => fact.Type == FactType.AgentAttacked);
            check(strategies[zombieVictim].Facts.Count == 2 && attacked.Lifetime == FactLifetime.Mutable &&
                  attacked.SubjectId == "player21" && attacked.TargetId == zombie.EntityId,
                "The victim remembers the attack and its latest attacker as mutable knowledge");
            var secondZombie = MakeObject(objects, "zombie2", z + Vector3.left * 0.5f).AddComponent<Zombie>();
            AttackWitnessReporter.ReportZombieAttack(secondZombie, zombieVictim);
            check(strategies[zombieVictim].Count(FactType.AgentAttacked) == 2 &&
                  strategies[zombieVictim].Facts.FindLast(fact => fact.Type == FactType.AgentAttacked).TargetId == secondZombie.EntityId,
                "A different attacker updates the mutable knowledge");

            Debug.Log($"HALF-ZOMBIE INTEGRATION VERIFIED: {checks} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        }
    }

    // Batch entry: -executeMethod HalfZombieVerification.RunBatch (exit code 0 on success).
    public static void RunBatch()
    {
        int exitCode = 0;
        foreach (Action verifier in new Action[] { Run, FactPipelineVerification.Run })
        {
            try { verifier(); }
            catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
        }
        EditorApplication.Exit(exitCode);
    }

    private static List<Fact> Where(this IReadOnlyList<Fact> facts, FactType type)
    {
        var matches = new List<Fact>();
        foreach (Fact fact in facts)
            if (fact.Type == type) matches.Add(fact);
        return matches;
    }

    private static GameObject MakeObject(List<GameObject> objects, string name, Vector3 position)
    {
        var obj = new GameObject("HalfZombieVerification_" + name);
        objects.Add(obj);
        obj.transform.position = position;
        obj.AddComponent<CircleCollider2D>().radius = 0.25f;
        return obj;
    }

    private static Agent MakeAgent(List<GameObject> objects, string id, Vector3 position, bool recordingActions)
    {
        var obj = MakeObject(objects, id ?? "anonymous", position);
        if (recordingActions) obj.AddComponent<RecordingActions>();
        var agent = obj.AddComponent<Agent>();
        obj.AddComponent<AgentFactReceiver>();
        // Edit Mode does not run normal MonoBehaviour lifecycle automatically.
        Invoke(typeof(Agent_Parent_Script), agent, "Awake");
        agent.Initialize("innocent", position.x, position.y);
        if (id != null) agent.SetAgentId(id);
        return agent;
    }

    private static NpcState BuildState(ScaspStrategy strategy) =>
        (NpcState)typeof(ScaspStrategy).GetMethod("BuildNpcState", Hidden).Invoke(strategy, null);

    private static void SetInspectorStrategy(Agent agent, MonoBehaviour component)
    {
        var settings = new SerializedObject(agent);
        settings.FindProperty("strategyComponent").objectReferenceValue = component;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Invoke(Type type, object instance, string method) => type.GetMethod(method, Hidden).Invoke(instance, null);

    private static object GetField(AiAction action, string field) => typeof(AiAction).GetField(field, Hidden).GetValue(action);

    private static void SetField(object instance, Type type, string field, object value) => type.GetField(field, Hidden).SetValue(instance, value);

    private static List<string> CaptureLogs(Action action)
    {
        var messages = new List<string>();
        Application.LogCallback capture = (message, stack, type) => messages.Add(message);
        Application.logMessageReceived += capture;
        try { action(); }
        finally { Application.logMessageReceived -= capture; }
        return messages;
    }
}

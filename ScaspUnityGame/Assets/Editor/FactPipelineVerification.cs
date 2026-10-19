using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// No scene changes: builds and destroys a temporary lever/agent fixture.
public static class FactPipelineVerification
{
    private sealed class RecordingStrategy : IFactStrategy
    {
        public readonly List<Fact> Facts = new List<Fact>();
        public void StoreFact(Fact fact) => Facts.Add(fact);
    }

    [MenuItem("Tools/Facts/Verify Draft Pipeline")]
    public static void Run()
    {
        var objects = new List<GameObject>();
        int checks = 0;
        Action<bool, string> check = (condition, description) =>
        {
            if (!condition) throw new Exception("FAIL: " + description);
            checks++;
            Debug.Log("PASS: " + description);
        };

        try
        {
            var fact = new Fact(FactType.LeverActivated, FactLifetime.Permanent, "lever1");
            check(fact.Lifetime == FactLifetime.Permanent && fact.SubjectId == "lever1",
                "Fact preserves explicit lifetime and identity");
            Expect<ArgumentException>(() => new Fact(FactType.LeverActivated, FactLifetime.Permanent, " "));
            check(true, "Blank subject IDs rejected");
            Expect<ArgumentOutOfRangeException>(() => new Fact(FactType.LeverActivated, (FactLifetime)99, "lever1"));
            check(true, "Invalid lifetime rejected");

            // Use a distant fixture position to avoid existing scene objects.
            var origin = new Vector3(10000, 10000, 0);
            var near = Agent(objects, "near", origin + Vector3.right, 0, out var nearStrategy);
            near.gameObject.AddComponent<CircleCollider2D>(); // Duplicate collider must not duplicate delivery.
            int notifications = 0;
            near.FactReceived += received =>
            {
                check(nearStrategy.Facts.Contains(received), "Notification happens after storage");
                notifications++;
            };
            var second = Agent(objects, "second", origin + Vector3.up, 0, out var secondStrategy);
            Agent(objects, "far", origin + Vector3.right * 20, 0, out var farStrategy);
            var disabled = Agent(objects, "disabled", origin + Vector3.left, 0, out var disabledStrategy);
            disabled.enabled = false;
            Agent(objects, "wrong-layer", origin + Vector3.down, 2, out var wrongLayerStrategy);

            var managerObject = Make(objects, "manager", origin);
            var manager = managerObject.AddComponent<LeverManager>();
            manager.totalLevers = 100;
            var leverObject = Make(objects, "lever", origin);
            leverObject.AddComponent<SpriteRenderer>();
            var lever = leverObject.AddComponent<LeverScript>();
            lever.leverManager = manager;
            var emitter = leverObject.AddComponent<LeverFactEmitter>();
            var settings = new SerializedObject(emitter);
            settings.FindProperty("leverId").stringValue = "lever1";
            settings.FindProperty("observationRadius").floatValue = 5;
            settings.FindProperty("agentLayers").intValue = 1; // Default layer only.
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Edit Mode does not run normal MonoBehaviour lifecycle automatically.
            InvokeCallback(lever, "Awake");
            InvokeCallback(emitter, "Awake");
            InvokeCallback(emitter, "OnDisable");
            InvokeCallback(emitter, "OnEnable");
            near.gameObject.tag = "Player";
            Physics2D.SyncTransforms();
            // Invoke the actual trigger handler; recipient selection uses real Physics2D overlap.
            InvokeCallback(lever, "OnTriggerEnter2D", near.GetComponent<Collider2D>());
            check(nearStrategy.Facts.Count == 1, "Nearby agent with two colliders stores exactly once");
            check(secondStrategy.Facts.Count == 1, "Second nearby agent stores the event independently");
            check(farStrategy.Facts.Count == 0, "Out-of-range agent receives nothing");
            check(disabledStrategy.Facts.Count == 0, "Disabled receiver receives nothing");
            check(wrongLayerStrategy.Facts.Count == 0, "Excluded layer receives nothing");
            check(nearStrategy.Facts[0].Type == FactType.LeverActivated &&
                  nearStrategy.Facts[0].Lifetime == FactLifetime.Permanent &&
                  nearStrategy.Facts[0].SubjectId == "lever1", "Stored lever fact has correct payload");
            InvokeCallback(lever, "OnTriggerEnter2D", near.GetComponent<Collider2D>());
            check(nearStrategy.Facts.Count == 1 && notifications == 1, "Already-pulled lever does not emit again");

            Expect<ArgumentException>(() => near.ReceiveFact(
                new Fact(FactType.ZombieSeen, FactLifetime.Transient, "zombie1")));
            check(nearStrategy.Facts.Count == 1, "Transient fact cannot enter stored memory");
            near.ReceiveFact(new Fact(FactType.AgentAttacked, FactLifetime.Mutable, "agent1", "zombie1"));
            check(nearStrategy.Facts.Count == 2 && nearStrategy.Facts[1].TargetId == "zombie1",
                "Mutable fact forwards its target to the strategy");
            Expect<ArgumentNullException>(() => near.ReceiveFact(null));
            check(true, "Null facts rejected");

            var unconfigured = Make(objects, "unconfigured", origin).AddComponent<AgentFactReceiver>();
            int falseNotifications = 0;
            unconfigured.FactReceived += _ => falseNotifications++;
            check(!unconfigured.ReceiveFact(fact) && falseNotifications == 0,
                "Missing strategy reports failure without claiming fact was stored");
            Debug.Log($"FACT PIPELINE VERIFIED: {checks} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    }

    private static GameObject Make(List<GameObject> objects, string name, Vector3 position)
    {
        var obj = new GameObject("FactVerification_" + name);
        objects.Add(obj);
        obj.transform.position = position;
        return obj;
    }

    private static AgentFactReceiver Agent(List<GameObject> objects, string name, Vector3 position,
        int layer, out RecordingStrategy strategy)
    {
        var obj = Make(objects, name, position);
        obj.layer = layer;
        var receiver = obj.AddComponent<AgentFactReceiver>();
        strategy = new RecordingStrategy();
        obj.GetComponent<Agent_Parent_Script>().SetFactStrategy(strategy);
        obj.AddComponent<CircleCollider2D>();
        return receiver;
    }

    // Invoke directly: SendMessage asserts on non-ExecuteAlways scripts in Edit Mode.
    private static void InvokeCallback(MonoBehaviour component, string method, params object[] args)
    {
        component.GetType().GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(component, args);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}

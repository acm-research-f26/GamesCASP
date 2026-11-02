using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TransientFactVerification
{
    private sealed class ProbeStrategy : IAgentStrategy
    {
        public int Queries;
        public readonly List<Fact> Facts = new List<Fact>();
        public IReadOnlyList<Fact> FetchTransientFacts() { Queries++; return Facts; }
        public void StoreFact(Fact fact) => throw new Exception("Query attempted storage");
        public void DecideNextAction(Agent_Parent_Script agent) { }
    }

    [MenuItem("Tools/Facts/Verify Transient Queries")]
    public static void Run()
    {
        var objects = new List<GameObject>();
        int checks = 0;
        Action<bool, string> check = (ok, message) =>
        {
            if (!ok) throw new Exception("FAIL: " + message);
            checks++;
            Debug.Log("PASS: " + message);
        };
        try
        {
            var first = Make(objects, "first", 0).AddComponent<Agent_Parent_Script>();
            var second = Make(objects, "second", 20).AddComponent<Agent_Parent_Script>();
            var excluded = Make(objects, "excluded", 40).AddComponent<Agent_Parent_Script>();
            var a = new ProbeStrategy();
            var b = new ProbeStrategy();
            var c = new ProbeStrategy();
            first.SetStrategy(a, "probe"); second.SetStrategy(b, "probe"); excluded.SetStrategy(c, "probe");
            a.Facts.Add(new Fact(FactType.ZombieSeen, FactLifetime.Transient, "zombie1", distance: 2));
            var result = Fact.FetchAllTransientFacts(new[] { first, first, null, second });
            check(result.Count == 2 && a.Queries == 1 && b.Queries == 1 && c.Queries == 0,
                "Only requested agents queried; duplicates and nulls skipped");
            check(result[first].Count == 1 && result[second].Count == 0,
                "Results retain observer identity, including empty observations");
            a.Facts.Clear();
            check(result[first].Count == 1 && first.FetchTransientFacts().Count == 0,
                "Snapshots remain stable while new queries reflect changes");
            check(Fact.FetchAllTransientFacts(Array.Empty<Agent_Parent_Script>()).Count == 0,
                "Empty selection queries nobody");
            a.Facts.Add(new Fact(FactType.LeverActivated, FactLifetime.Permanent, "lever1"));
            Expect<InvalidOperationException>(() => first.FetchTransientFacts());
            Expect<ArgumentException>(() => first.TryStoreFact(result[first][0]));
            Expect<ArgumentNullException>(() => Fact.FetchAllTransientFacts(null));
            check(true, "Query lifetime and storage boundaries enforced");

            var sample = first.gameObject.AddComponent<SampleFactStrategy>();
            first.SetStrategy(sample, "sample");
            var zombie = Make(objects, "Zomb1", 2);
            var zombieComponent = zombie.AddComponent<Zombie>();
            string zombieId = zombieComponent.EntityId;
            zombie.AddComponent<CircleCollider2D>();
            var child = Make(objects, "zombieCollider", 2);
            child.transform.SetParent(zombie.transform, true);
            child.AddComponent<CircleCollider2D>();
            Physics2D.SyncTransforms();
            var observed = first.FetchTransientFacts();
            check(observed.Count == 1 && observed[0].SubjectId == zombieId &&
                  observed[0].Distance == 2 && observed[0].Lifetime == FactLifetime.Transient,
                "Sample strategy queries physics and deduplicates child colliders");
            ScaspFactPayload.ValidateAtom(zombieId, "zombieId");
            zombie.name = "Zomb1(Clone)";
            check(first.FetchTransientFacts()[0].SubjectId == zombieId,
                "Uppercase and clone display names keep a stable Prolog-safe zombie identity");
            var actionTarget = typeof(AiAction).GetMethod("FindNearest",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(first.GetComponent<AiAction>(), new object[] { "Zombie", zombieId, false });
            check((GameObject)actionTarget == zombie, "Returned zombie action IDs resolve to the observed GameObject");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Zomb1.prefab");
            var firstClone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            objects.Add(firstClone);
            firstClone.transform.position = first.transform.position + Vector3.right * 100;
            var secondClone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            objects.Add(secondClone);
            secondClone.transform.position = first.transform.position + Vector3.right * 110;
            check(firstClone.GetComponent<Zombie>().EntityId != secondClone.GetComponent<Zombie>().EntityId,
                "Two instances of the gameplay zombie prefab receive different identities");
            check(sample.StoredFacts.Count == 0, "Observations never enter stored memory");
            zombie.transform.position += Vector3.right * 100;
            Physics2D.SyncTransforms();
            check(first.FetchTransientFacts().Count == 0 && observed.Count == 1,
                "Moving out of range expires observation without altering old snapshot");
            zombie.transform.position -= Vector3.right * 100;
            zombie.GetComponent<Zombie>().enabled = false;
            Physics2D.SyncTransforms();
            check(first.FetchTransientFacts().Count == 0, "Disabled zombies excluded");
            zombie.GetComponent<Zombie>().enabled = true;

            var manager = Make(objects, "leverManager", 50).AddComponent<LeverManager>();
            var scasp = second.gameObject.AddComponent<ScaspStrategy>();
            // Edit Mode does not automatically call Awake.
            typeof(ScaspStrategy).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(scasp, null);
            second.SetStrategy(scasp, "scasp");
            second.transform.position = first.transform.position;
            Physics2D.SyncTransforms();
            check(second.FetchTransientFacts().Count == 1, "s(CASP) uses the same observation format");
            var state = (NpcState)typeof(ScaspStrategy).GetMethod("BuildNpcState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(scasp, null);
            check(state != null && state.zombies.Length == 1 && state.zombies[0].id == zombieId &&
                  state.zombies[0].distance == 2, "s(CASP) decision snapshot consumes queried observations");

            var barricade = Make(objects, "window", 3);
            var window = barricade.AddComponent<Window>();
            typeof(Window).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(window, null);
            window.health = 24;
            barricade.AddComponent<BoxCollider2D>();
            var barricadeChild = Make(objects, "windowCollider", 3);
            barricadeChild.transform.SetParent(barricade.transform, true);
            barricadeChild.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            var barricadeFacts = first.FetchTransientFacts().Where(f => f.Type == FactType.BarricadeSeen).ToArray();
            check(barricadeFacts.Length == 1 && barricadeFacts[0].Distance == 3 &&
                  barricadeFacts[0].Reinforcement == 24 && barricadeFacts[0].Lifetime == FactLifetime.Transient,
                "Barricade observation includes range and reinforcement and deduplicates child colliders");
            check(barricadeFacts[0].ZombieDistances.Count == 1 &&
                  barricadeFacts[0].ZombieDistances[zombieId] == 1,
                "Barricade threat distance uses the observed zombie's stable identity");
            state = (NpcState)typeof(ScaspStrategy).GetMethod("BuildNpcState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(scasp, null);
            check(state.barricades.Length == 1 && state.barricades[0].visible &&
                  state.barricades[0].id == barricade.name && state.barricades[0].reinforcement == 24 &&
                  state.barricades[0].zombie_distances.Length == 1 &&
                  state.barricades[0].zombie_distances[0].id == zombieId &&
                  state.barricades[0].zombie_distances[0].distance == 1,
                "s(CASP) snapshot includes barricades and zombie-to-barricade distances");
            var roundTrip = JsonUtility.FromJson<NpcState>(JsonUtility.ToJson(state));
            check(roundTrip.barricades[0].zombie_distances[0].distance == 1,
                "Barricade threat array survives Unity JSON serialization");
            if (Application.isBatchMode)
            {
                System.IO.Directory.CreateDirectory("Temp");
                System.IO.File.WriteAllText("Temp/barricade-snapshot.json", JsonUtility.ToJson(state));
            }
            var blackboard = new Blackboard(second.GetComponent<Agent>(), manager);
            blackboard.updateTransientFacts(second.FetchTransientFacts());
            check(blackboard.canSeeBarricade, "Blackboard recognizes shared barricade observations");
            blackboard.clearTransientState();
            check(!blackboard.canSeeBarricade, "Blackboard clears transient barricade visibility");
            window.health = 75;
            zombie.transform.position += Vector3.right * 100;
            Physics2D.SyncTransforms();
            var updated = first.FetchTransientFacts().Single(f => f.Type == FactType.BarricadeSeen);
            check(updated.Reinforcement == 75 && updated.ZombieDistances.Count == 0 &&
                  float.IsPositiveInfinity(updated.NearestZombieDistance.Value) &&
                  barricadeFacts[0].Reinforcement == 24 && barricadeFacts[0].ZombieDistances.Count == 1,
                "Fresh barricade snapshots update health, exclude unseen zombies, and preserve old observations");
            window.enabled = false;
            check(!first.FetchTransientFacts().Any(f => f.Type == FactType.BarricadeSeen),
                "Disabled barricades excluded");
            window.enabled = true;
            barricade.transform.position += Vector3.right * 100;
            Physics2D.SyncTransforms();
            check(!first.FetchTransientFacts().Any(f => f.Type == FactType.BarricadeSeen),
                "Out-of-range barricades expire on the next query");
            check(sample.StoredFacts.Count == 0 && scasp.PendingFactCount == 0,
                "Barricade observations never enter persistent storage");
            Debug.Log($"TRANSIENT FACTS VERIFIED: {checks} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    }

    private static GameObject Make(List<GameObject> objects, string name, float x)
    {
        var obj = new GameObject(name);
        objects.Add(obj);
        obj.transform.position = new Vector3(10000 + x, 10000, 0);
        return obj;
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Temporary real prefab fixtures, real WebSocket, real server decisions.
// Uses a unique observer and sends no stored facts into the current session.
public static class BarricadeGameplayVerification
{
    private static bool running;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Facts/Verify Barricade Gameplay (Play Mode)")]
    public static async void Run()
    {
        if (running) return;
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("Enter Play Mode before running the barricade gameplay verifier.");
            return;
        }
        running = true;
        var objects = new List<GameObject>();
        var report = new List<string>();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BarricadeVerification"));
        Directory.CreateDirectory(output);
        Action<bool, string> check = (condition, description) =>
        {
            if (!condition) throw new Exception("FAIL: " + description);
            report.Add("PASS: " + description);
            Debug.Log(report[report.Count - 1]);
        };
        try
        {
            var origin = new Vector3(10000, 10000, 0);
            var managerObject = new GameObject("Barricade verification manager");
            objects.Add(managerObject);
            var manager = managerObject.AddComponent<LeverManager>();
            manager.totalLevers = 2;
            var staging = new GameObject("Barricade verification staging");
            objects.Add(staging);
            staging.SetActive(false);
            var npc = Spawn("survivorPrefab", staging.transform, origin, objects);
            var agent = npc.GetComponent<Agent>();
            var strategy = npc.GetComponent<ScaspStrategy>();
            var client = npc.GetComponent<ScaspClient>();
            var actions = npc.GetComponent<AiAction>();
            check(agent != null && strategy != null && client != null && actions != null,
                "Gameplay NPC prefab provides the agent, strategy, client, and executor");
            foreach (var component in npc.GetComponents<MonoBehaviour>())
                if (component is IAgentStrategy) component.enabled = component == strategy;
            client.enabled = true;
            string actor = "barricade_probe_" + Guid.NewGuid().ToString("N");
            strategy.SetActorId(actor);
            agent.SetStrategy(strategy, "scasp");
            actions.detectionRange = 5;
            actions.moveSpeed = 0;
            npc.transform.SetParent(null, true);
            strategy.levManager = manager;

            var barricade = Spawn("windowPrefab", staging.transform, origin + Vector3.right * 4, objects);
            var window = barricade.GetComponent<Window>();
            window.health = 20;
            window.increaseSpeed = 0;
            window.decreaseSpeed = 0;
            barricade.transform.SetParent(null, true);
            var enemy = Spawn("Zomb1", staging.transform, origin + Vector3.right * 4.5f, objects);
            var zombie = enemy.GetComponent<Zombie>();
            zombie.moveSpeed = 0;
            zombie.attackDamage = 0;
            enemy.transform.SetParent(null, true);
            Physics2D.SyncTransforms();
            await WaitUntil(() => client != null && client.IsConnected, "WebSocket connection");

            var state = (NpcState)typeof(ScaspStrategy).GetMethod("BuildNpcState", PrivateInstance).Invoke(strategy, null);
            File.WriteAllText(Path.Combine(output, "snapshot.json"), JsonUtility.ToJson(state, true));
            check(state.barricades.Length == 1 && state.barricades[0].id == barricade.name &&
                  state.barricades[0].visible && state.barricades[0].reinforcement == 20 &&
                  Mathf.Abs(state.barricades[0].distance - 4) < 0.01f &&
                  state.barricades[0].zombie_distances.Length == 1 &&
                  state.barricades[0].zombie_distances[0].id == zombie.EntityId &&
                  Mathf.Abs(state.barricades[0].zombie_distances[0].distance - 0.5f) < 0.01f,
                "Real physics snapshot contains the barricade at distance 4, health 20, and zombie distance 0.5");

            var decision = await Decide(agent, client, actor);
            check(decision.action == "move_to_barricade" && decision.target == barricade.name,
                "Real s(CASP) selects move_to_barricade with the observed target ID");
            check((GameObject)typeof(AiAction).GetField("movementTarget", PrivateInstance).GetValue(actions) == barricade,
                "Unity executor resolves the server's target to the actual window");
            actions.moveSpeed = 2;
            await WaitUntil(() => npc.transform.position.x > origin.x + 0.2f, "Movement toward barricade");
            check(npc.transform.position.x < barricade.transform.position.x,
                "NPC physically moves toward the barricade");
            actions.moveSpeed = 0;
            actions.StopAllActions();

            npc.transform.position = origin;
            enemy.transform.position = origin + Vector3.right;
            Physics2D.SyncTransforms();
            decision = await Decide(agent, client, actor);
            check(decision.action == "run_from" && decision.target == zombie.EntityId,
                "A zombie within immediate-danger range overrides barricade duty");

            enemy.transform.position = origin + Vector3.right * 100;
            Physics2D.SyncTransforms();
            decision = await Decide(agent, client, actor);
            check(decision.action == "hunt_lever",
                "A damaged barricade without an observed nearby zombie does not trigger defense");
            check(strategy.PendingFactCount == 0, "Barricade snapshots never enter stored-fact delivery");
            report.Add($"BARRICADE GAMEPLAY VERIFIED: {report.Count} checks passed.");
            Debug.Log(report[report.Count - 1]);
        }
        catch (Exception exception)
        {
            report.Add(exception.ToString());
            Debug.LogException(exception);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(output, "result.txt"), report);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.Destroy(objects[i]);
            running = false;
        }
    }

    private static GameObject Spawn(string prefab, Transform parent, Vector3 position, List<GameObject> objects)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/prefabs/{prefab}.prefab");
        if (source == null) throw new Exception("Missing prefab: " + prefab);
        var instance = UnityEngine.Object.Instantiate(source, parent);
        objects.Add(instance);
        instance.transform.position = position;
        return instance;
    }

    private static async Task<(string action, string target)> Decide(Agent agent, ScaspClient client, string actor)
    {
        var completion = new TaskCompletionSource<(string, string)>();
        Action<string, string, string> receive = (who, action, target) =>
        {
            if (who == actor) completion.TrySetResult((action, target));
        };
        client.NpcActionReceived += receive;
        try
        {
            agent.RequestDecision();
            await WaitUntil(() => completion.Task.IsCompleted, "NPC action response");
            return await completion.Task;
        }
        finally { if (client != null) client.NpcActionReceived -= receive; }
    }

    private static async Task WaitUntil(Func<bool> condition, string description)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped during verification");
            if (watch.Elapsed > TimeSpan.FromSeconds(40)) throw new TimeoutException(description);
            await Task.Delay(20);
        }
    }
}

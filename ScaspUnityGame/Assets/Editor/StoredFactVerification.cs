using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Exercises the real receiver, strategy, client, Python handler, and file storage.
// Everything is temporary; the normal server and open scene are left alone.
public static class StoredFactVerification
{
    [Serializable]
    private sealed class ServerReady
    {
        public string url;
    }

    private static bool running;

    [MenuItem("Tools/Facts/Verify Python Storage")]
    public static async void Run()
    {
        if (running)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Stop Play Mode before running the isolated Python storage verifier.");
            return;
        }

        running = true;
        int exitCode = 1;
        try
        {
            await VerifyAsync();
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            running = false;
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    private static async Task VerifyAsync()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repository = Path.GetFullPath(Path.Combine(project, ".."));
        string directory = Path.Combine(project, "Temp", "StoredFactVerification", Guid.NewGuid().ToString("N"));
        string factsFile = Path.Combine(directory, "facts_temp.pl");
        string script = Path.Combine(repository, "DockerServer", "Server", "tests", "stored_fact_server.py");
        string python = Environment.GetEnvironmentVariable("GAMESCASP_PYTHON") ?? "python";
        Directory.CreateDirectory(directory);
        Process server = null;
        GameObject fixture = null;
        GameObject leverFixture = null;
        EditorApplication.CallbackFunction pump = null;
        Task<string> serverErrors = null;
        int checks = 0;
        Action<bool, string> check = (condition, description) =>
        {
            if (!condition)
                throw new Exception("FAIL: " + description);
            checks++;
            Debug.Log("PASS: " + description);
        };

        try
        {
            server = Process.Start(new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"-B \"{script}\" \"{directory}\" --drop-first-ack",
                WorkingDirectory = repository,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            serverErrors = server.StandardError.ReadToEndAsync();
            string readyLine = await WithTimeout(server.StandardOutput.ReadLineAsync(), "Python server startup");
            if (readyLine == null)
                throw new Exception("Python server failed to start: " + await serverErrors);
            var ready = JsonUtility.FromJson<ServerReady>(readyLine);
            check(ready != null && !string.IsNullOrEmpty(ready.url), "Isolated Python server started");

            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/playerPrefab.prefab");
            fixture = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab);
            fixture.name = "StoredFactVerification";
            fixture.hideFlags = HideFlags.HideAndDontSave;
            fixture.transform.position = new Vector3(10000, 10000, 0);
            var strategy = fixture.GetComponent<ScaspStrategy>();
            var agent = fixture.GetComponent<Agent>();
            var client = fixture.GetComponent<ScaspClient>();
            var receiver = fixture.GetComponent<AgentFactReceiver>();
            check(strategy != null && receiver != null && fixture.GetComponents<ScaspClient>().Length == 1,
                "Gameplay NPC prefab has a fact receiver, s(CASP) strategy, and exactly one client");
            // Unity does not call these non-ExecuteAlways lifecycle methods in Edit Mode.
            InvokeLifecycle(strategy, "Awake");
            agent.SetStrategy(strategy);
            strategy.SetActorId("observer1");
            var settings = new SerializedObject(client);
            settings.FindProperty("serverUrl").stringValue = ready.url;
            settings.FindProperty("factAcknowledgmentTimeout").floatValue = 1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            pump = () =>
            {
                InvokeLifecycle(client, "Update");
                InvokeLifecycle(strategy, "Update");
            };
            EditorApplication.update += pump;

            int received = 0;
            receiver.FactReceived += _ => received++;
            var leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Lever.prefab");
            leverFixture = (GameObject)PrefabUtility.InstantiatePrefab(leverPrefab);
            leverFixture.hideFlags = HideFlags.HideAndDontSave;
            leverFixture.transform.position = fixture.transform.position + Vector3.right;
            leverFixture.name = "lever_1";
            var leverScript = leverFixture.GetComponent<LeverScript>();
            leverScript.leverManager = leverFixture.AddComponent<LeverManager>();
            leverScript.leverManager.totalLevers = 100;
            var emitter = leverFixture.GetComponent<LeverFactEmitter>();
            InvokeLifecycle(leverScript, "Awake");
            InvokeLifecycle(emitter, "Awake");
            InvokeLifecycle(emitter, "OnDisable");
            InvokeLifecycle(emitter, "OnEnable");
            emitter.SetLeverId(leverFixture.name); // Same identity assignment as MapManager.
            Physics2D.SyncTransforms();
            InvokeLifecycle(leverScript, "OnTriggerEnter2D", fixture.GetComponent<Collider2D>());
            var lever = new Fact(FactType.LeverActivated, FactLifetime.Permanent, leverFixture.name);
            check(leverScript.isOn && received == 1 && strategy.PendingFactCount == 1,
                "Gameplay lever activation delivers a permanent fact to the NPC before connection");
            check(ReadFacts(factsFile) == "verification_baseline.\n",
                "Disconnected fact remains queued without changing Python storage");
            Expect<InvalidOperationException>(() => strategy.SetActorId("observer2"));
            check(true, "Pending facts cannot be reassigned to a different observer");

            InvokeLifecycle(client, "Start");
            const string leverClause = "stored_fact(observer1, permanent, lever_activated, lever_1, none, none).";
            await WaitUntil(() => ReadFacts(factsFile).Contains(leverClause), "Initial file write");
            check(strategy.PendingFactCount == 1,
                "A socket send alone does not clear a fact when its acknowledgment is lost");
            await WaitUntil(() => strategy.PendingFactCount == 0, "Retry acknowledgment");
            check(Count(ReadFacts(factsFile), leverClause) == 1,
                "Retry persists one permanent clause and clears the queue after acknowledgment");

            receiver.ReceiveFact(lever);
            receiver.ReceiveFact(new Fact(FactType.AgentAttacked, FactLifetime.Mutable, "survivor1", "zombie1"));
            receiver.ReceiveFact(new Fact(FactType.AgentAttacked, FactLifetime.Mutable, "survivor1", "zombie2", 0));
            await WaitUntil(() => strategy.PendingFactCount == 0, "Ordered mutable updates");
            string contents = ReadFacts(factsFile);
            check(Count(contents, leverClause) == 1, "Repeated permanent delivery is deduplicated");
            check(!contents.Contains("survivor1, zombie1,") &&
                  contents.Contains("stored_fact(observer1, mutable, agent_attacked, survivor1, zombie2, 0"),
                "Mutable facts replace their previous target and preserve a zero distance");

            strategy.SetActorId("observer2");
            receiver.ReceiveFact(lever);
            await WaitUntil(() => strategy.PendingFactCount == 0, "Second observer write");
            contents = ReadFacts(factsFile);
            check(contents.Contains(leverClause) && contents.Contains(
                "stored_fact(observer2, permanent, lever_activated, lever_1, none, none)."),
                "The same fact is stored separately for each observer");
            check(contents.StartsWith("verification_baseline.\n"), "Existing file contents are preserved");

            int accepted = received;
            Expect<ArgumentException>(() => receiver.ReceiveFact(
                new Fact(FactType.ZombieSeen, FactLifetime.Transient, "zombie1", distance: 2)));
            Expect<ArgumentException>(() => receiver.ReceiveFact(
                new Fact(FactType.AgentAttacked, FactLifetime.Mutable, "invalid_id\n")));
            check(received == accepted && strategy.PendingFactCount == 0 && ReadFacts(factsFile) == contents,
                "Transient facts and invalid IDs are rejected without notifications or writes");

            Debug.Log($"PYTHON FACT STORAGE VERIFIED: {checks} checks passed.");
        }
        finally
        {
            if (pump != null)
                EditorApplication.update -= pump;
            if (leverFixture != null)
            {
                InvokeLifecycle(leverFixture.GetComponent<LeverFactEmitter>(), "OnDisable");
                UnityEngine.Object.DestroyImmediate(leverFixture);
            }
            if (fixture != null)
            {
                // Edit Mode also omits OnDestroy for these components.
                InvokeLifecycle(fixture.GetComponent<ScaspClient>(), "OnDestroy");
                UnityEngine.Object.DestroyImmediate(fixture);
            }
            if (server != null)
            {
                if (!server.HasExited)
                    server.Kill();
                await WithTimeout(Task.Run(() => server.WaitForExit()), "Python server cleanup");
                if (serverErrors != null)
                {
                    string errors = await serverErrors;
                    if (!string.IsNullOrWhiteSpace(errors))
                        Debug.Log("Python verifier diagnostics: " + errors);
                }
                server.Dispose();
            }
            // This is the unique directory created above, never the normal server directory.
            Directory.Delete(directory, true);
        }
    }

    private static async Task WaitUntil(Func<bool> condition, string description)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException(description);
            await Task.Delay(20);
        }
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, string description)
    {
        await WithTimeout((Task)task, description);
        return await task;
    }

    private static async Task WithTimeout(Task task, string description)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task)
            throw new TimeoutException(description);
        await task;
    }

    private static int Count(string text, string clause) => text.Split(
        new[] { clause }, StringSplitOptions.None).Length - 1;

    private static string ReadFacts(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static void InvokeLifecycle(MonoBehaviour component, string method, params object[] arguments) => component.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, arguments);

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}

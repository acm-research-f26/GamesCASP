using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ExperimentOutcome { Won, NoSurvivors, TimedOut }

// Owns only batch orchestration. Real measurements and role/strategy switching
// are the second part of issue #55 and can subscribe to the run lifecycle.
public sealed class ExperimentRunner : MonoBehaviour
{
    public const string ScenePath = "Assets/Scenes/Main.unity";
    public static ExperimentRunner Instance { get; private set; }
    public bool IsRunning { get; private set; }
    public int CompletedRuns { get; private set; }
    public int GameNumber { get; private set; }
    public int Configuration { get; private set; }
    public int LayoutSeed { get; private set; }
    public string BatchId { get; private set; }
    public string Failure { get; private set; }
    public ExperimentSettings Settings { get; private set; }

    // Ready fires after fresh scene initialization, before any NPCs are spawned.
    public event Action<ExperimentRunner, PlayerManager> RunPreparing;
    public event Action<ExperimentRunner, PlayerManager> RunStarted;
    // Fired before scene teardown, while all agents and their final states exist.
    public event Action<ExperimentRunner, ExperimentOutcome, double> RunFinished;
    public event Action<ExperimentRunner> BatchFinished;

    private bool cancelRequested;
    private float previousTimeScale;
    private UnityEngine.Random.State previousRandomState;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-experimentGames") < 0 || Instance != null) return;
        try { Begin(ExperimentSettings.FromArguments(args)); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) Application.Quit(1);
        }
    }

    public static ExperimentRunner Begin(ExperimentSettings settings)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Experiments require Play Mode.");
        if (Instance != null) throw new InvalidOperationException("An experiment runner already exists.");
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        settings.Validate();
        var runner = new GameObject("Experiment Runner").AddComponent<ExperimentRunner>();
        Instance = runner;
        DontDestroyOnLoad(runner.gameObject);
        // Snapshot options so editing the window cannot change a batch in progress.
        runner.Settings = JsonUtility.FromJson<ExperimentSettings>(JsonUtility.ToJson(settings));
        runner.BatchId = "batch_" + Guid.NewGuid().ToString("N");
        runner.previousTimeScale = Time.timeScale;
        runner.previousRandomState = UnityEngine.Random.state;
        runner.IsRunning = true;
        runner.StartCoroutine(runner.Guard(runner.RunGames()));
        return runner;
    }

    public void Cancel() => cancelRequested = true;

    // Catch setup/export failures as well as normal completion; never silently
    // leave a batch marked as running after a coroutine exception.
    private IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            object next;
            try
            {
                if (!routine.MoveNext()) break;
                next = routine.Current;
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
                Debug.LogException(exception);
                break;
            }
            yield return next;
        }
        // Also tears down the final run's NPCs, sockets, and coroutines.
        yield return LoadGameScene();
        RestoreGlobals();
        IsRunning = false;
        try { BatchFinished?.Invoke(this); }
        finally
        {
            Debug.Log($"[Experiment] {CompletedRuns}/{Settings.GameCount * 2} runs finished. " +
                (Failure != null ? "Failed." : cancelRequested ? "Cancelled." : "Complete."));
            if (Application.isBatchMode) Application.Quit(Failure != null ? 1 : cancelRequested ? 2 : 0);
            Destroy(gameObject);
        }
    }

    private IEnumerator RunGames()
    {
        for (int game = 1; game <= Settings.GameCount && !cancelRequested; game++)
        {
            for (int configuration = 1; configuration <= 2 && !cancelRequested; configuration++)
            {
                yield return LoadGameScene();
                // Let Start (including the UI sliders) finish before applying settings.
                yield return null;
                Time.timeScale = 1f;
                AiDiagnostics.Reset();
                MansionNavigation.Clear();
                GameNumber = game;
                Configuration = configuration;
                LayoutSeed = unchecked(Settings.Seed + game - 1);
                UnityEngine.Random.InitState(LayoutSeed);

                var players = FindAnyObjectByType<PlayerManager>();
                var map = FindAnyObjectByType<MapManager>();
                var levers = FindAnyObjectByType<LeverManager>();
                if (players == null || map == null || levers == null)
                    throw new InvalidOperationException("Experiment scene requires PlayerManager, MapManager and LeverManager.");
                players.ConfigureExperiment(Settings.NpcCount, Settings.Backend, Settings.Policy,
                    $"{BatchId}_g{game}_c{configuration}_");
                RunPreparing?.Invoke(this, players);

                // Match the existing Start button's order: NPCs, then map.
                foreach (var ui in FindObjectsByType<StartUIScript>())
                    ui.StartGameClicked();
                players.StartGameClicked();
                if (!players.GameStarted || players.players.Count == 0)
                    throw new InvalidOperationException("Experiment could not spawn any NPCs.");
                map.StartGameClicked();
                if (levers.TotalLevers <= 0)
                    throw new InvalidOperationException("Experiment map has no lever objective.");
                RunStarted?.Invoke(this, players);
                Debug.Log($"[Experiment] Game {game}/{Settings.GameCount}, configuration {configuration}, seed {LayoutSeed}.");
                double started = Time.realtimeSinceStartupAsDouble;
                ExperimentOutcome? outcome;
                while (!(outcome = GetOutcome(players, levers, Time.realtimeSinceStartupAsDouble - started,
                    Settings.MaxGameSeconds)).HasValue && !cancelRequested)
                    yield return null;
                if (cancelRequested) yield break;

                RunFinished?.Invoke(this, outcome.Value, Time.realtimeSinceStartupAsDouble - started);
                CompletedRuns++;
            }
        }
    }

    public static ExperimentOutcome? GetOutcome(PlayerManager players, LeverManager levers, double elapsed, double limit)
    {
        if (levers.TotalLevers > 0 && levers.ActivatedLevers >= levers.TotalLevers) return ExperimentOutcome.Won;
        bool survivor = players.players.Exists(agent => agent != null && agent.isActiveAndEnabled &&
            agent.Health > 0 && !agent.BiteWindowExpired);
        if (!survivor) return ExperimentOutcome.NoSurvivors;
        if (elapsed >= limit) return ExperimentOutcome.TimedOut;
        return null;
    }

    private static AsyncOperation LoadGameScene()
    {
#if UNITY_EDITOR
        return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
#else
        return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
#endif
    }

    private void RestoreGlobals()
    {
        Time.timeScale = previousTimeScale;
        UnityEngine.Random.state = previousRandomState;
        MansionNavigation.Clear();
        AiDiagnostics.Reset();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        RestoreGlobals();
        Instance = null;
    }
}

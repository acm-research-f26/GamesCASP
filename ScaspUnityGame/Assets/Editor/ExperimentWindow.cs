using UnityEditor;
using UnityEngine;

public sealed class ExperimentWindow : EditorWindow
{
    private const string PendingSettings = "GamesCASP.ExperimentSettings";
    [SerializeField] private ExperimentSettings settings = new ExperimentSettings();

    [MenuItem("Tools/Experiments/Run Games")]
    public static void Open() => GetWindow<ExperimentWindow>("Game Experiments");

    // Unity.exe -batchmode -executeMethod ExperimentWindow.RunBatch -experimentGames 100
    // Do not pass -quit: the runner exits after writing the final CSVs.
    public static void RunBatch()
    {
        try
        {
            string[] args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-experimentGames") < 0)
                throw new System.ArgumentException("Specify -experimentGames for an unattended experiment.");
            ExperimentSettings.FromArguments(args);
            EditorApplication.EnterPlaymode();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [InitializeOnLoadMethod]
    private static void RegisterPlayModeHook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            SessionState.EraseString(PendingSettings);
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        string json = SessionState.GetString(PendingSettings, "");
        SessionState.EraseString(PendingSettings);
        if (json.Length > 0) ExperimentRunner.Begin(JsonUtility.FromJson<ExperimentSettings>(json));
    }

    private void OnGUI()
    {
        var runner = ExperimentRunner.Instance;
        if (runner != null)
        {
            EditorGUILayout.LabelField($"Completed runs: {runner.CompletedRuns}/{runner.Settings.GameCount * 2}");
            EditorGUILayout.LabelField($"Game {runner.GameNumber}, configuration {runner.Configuration}");
            if (GUILayout.Button("Cancel batch")) runner.Cancel();
            Repaint();
            return;
        }
        settings.GameCount = EditorGUILayout.IntField("Games (two runs each)", settings.GameCount);
        settings.NpcCount = EditorGUILayout.IntField("NPCs", settings.NpcCount);
        settings.Seed = EditorGUILayout.IntField("Starting layout seed", settings.Seed);
        settings.MaxGameSeconds = EditorGUILayout.FloatField("Time limit (real seconds)", settings.MaxGameSeconds);
        settings.Backend = (NpcDecisionBackend)EditorGUILayout.EnumPopup("Backend", settings.Backend);
        settings.Policy = (ScaspPolicy)EditorGUILayout.EnumPopup("s(CASP) policy", settings.Policy);
        settings.OutputDirectory = EditorGUILayout.TextField("Output directory (optional)", settings.OutputDirectory);
        EditorGUILayout.HelpBox("Each game runs twice with the same layout seed. Metrics are placeholders; role-based strategy switching is pending (#55).", MessageType.Info);
        if (!GUILayout.Button("Run experiments")) return;
        try
        {
            settings.Validate();
            if (EditorApplication.isPlaying) ExperimentRunner.Begin(settings);
            else if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                SessionState.SetString(PendingSettings, JsonUtility.ToJson(settings));
                EditorApplication.isPlaying = true;
            }
        }
        catch (System.Exception exception) { Debug.LogException(exception); }
    }
}

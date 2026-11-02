using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the generated mansion through the gameplay camera without saving the scene.
// Batch: -executeMethod MansionPreview.CaptureBatch -mansionOut <directory> (omit -nographics).
public static class MansionPreview
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Tools/Mansion/Capture Gameplay Preview")]
    public static void CaptureMenu()
    {
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "MansionPreview"));
        Capture(directory, 12345);
        EditorUtility.RevealInFinder(directory);
    }

    public static void CaptureBatch()
    {
        int exitCode = 0;
        try
        {
            string directory = Argument("-mansionOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "MansionPreview"));
            Capture(directory, int.Parse(Argument("-mansionSeed") ?? "12345"));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exitCode = 1;
        }
        EditorApplication.Exit(exitCode);
    }

    private static void Capture(string directory, int seed)
    {
        Directory.CreateDirectory(directory);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        UnityEngine.Random.InitState(seed);
        var players = UnityEngine.Object.FindAnyObjectByType<PlayerManager>();
        Vector2[] spawns = SpawnPositions(players);
        var map = UnityEngine.Object.FindAnyObjectByType<MapManager>();
        map.StartGameClicked();

        // Representative characters: the five NPC slots and a zombie at a window.
        var npcPrefab = players.playerPrefab;
        foreach (Vector2 spawn in spawns)
            InstantiatePreview(npcPrefab, spawn);
        var window = UnityEngine.Object.FindAnyObjectByType<Window>();
        var zombiePrefab = window != null ? window.zombiePrefab : null;
        if (zombiePrefab != null)
            InstantiatePreview(zombiePrefab, (Vector2)window.transform.position * 0.85f);

        // MapManager destroys rejected lever placements with Destroy, which Edit Mode ignores.
        foreach (LeverScript lever in UnityEngine.Object.FindObjectsByType<LeverScript>())
            if (!lever.name.StartsWith("lever_"))
                UnityEngine.Object.DestroyImmediate(lever.gameObject);

        var startUi = GameObject.Find("StartUI");
        if (startUi != null) startUi.SetActive(false);

        Camera camera = Camera.main;
        Render(camera, Path.Combine(directory, "gameplay_1920x1080.png"), 1920, 1080);
        Render(camera, Path.Combine(directory, "gameplay_1280x720.png"), 1280, 720);

        // Close-up through the same camera to judge texture detail.
        float size = camera.orthographicSize;
        Vector3 position = camera.transform.position;
        camera.orthographicSize = 3f;
        camera.transform.position = new Vector3(position.x - 3f, position.y + 2f, position.z);
        Render(camera, Path.Combine(directory, "closeup_1920x1080.png"), 1920, 1080);
        camera.orthographicSize = size;
        camera.transform.position = position;

        Debug.Log($"MANSION PREVIEW: wrote images to {directory} (seed {seed}, {spawns.Length} NPC slots)");
    }

    // Same positions PlayerManager uses for five NPCs (it runs before MapManager in game).
    private static Vector2[] SpawnPositions(PlayerManager players) => players.NpcSpawnPositions(5).ToArray();

    private static void InstantiatePreview(GameObject prefab, Vector2 position)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = new Vector3(position.x, position.y, instance.transform.position.z);
    }

    private static void Render(Camera camera, string path, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var previous = camera.targetTexture;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = previous;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

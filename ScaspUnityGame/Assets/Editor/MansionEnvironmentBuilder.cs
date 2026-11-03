using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

// Rebuilds the mansion's static presentation in Main.unity from the project's existing art.
// Safe to rerun: it repaints the floor tilemap and reapplies settings; gameplay objects
// (walls, windows, levers, NPCs) are still generated at runtime by MapManager/PlayerManager.
public static class MansionEnvironmentBuilder
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string Layout = "Assets/Art/Tiles/Flooring/Mansion layout/";
    private const string ProfilePath = "Assets/Settings/MansionVolumeProfile.asset";

    [MenuItem("Tools/Mansion/Build Environment")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        UsePointFiltering("Assets/Art/Tiles/lever/Dungeon Tileset.png");
        PaintFloor(GameObject.Find("Mansion Floor").GetComponent<Tilemap>());

        var map = UnityEngine.Object.FindAnyObjectByType<MapManager>();
        var decorator = map.GetComponent<MansionDecorator>();
        if (decorator == null)
            decorator = map.gameObject.AddComponent<MansionDecorator>();
        var settings = new SerializedObject(decorator);
        settings.FindProperty("brickSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Tiles/Flooring/wood floor.png");
        settings.FindProperty("stoneSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Tiles/Flooring/marble.png");
        settings.FindProperty("dungeonSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Tiles/lever/Dungeon Tileset.png");
        settings.ApplyModifiedPropertiesWithoutUndo();

        // Overview of the whole mansion; MansionCamera refits it to the screen and can follow NPCs.
        Camera camera = Camera.main;
        camera.orthographicSize = 10.8f;
        if (camera.GetComponent<MansionCamera>() == null)
            camera.gameObject.AddComponent<MansionCamera>();
        camera.backgroundColor = new Color(0.04f, 0.04f, 0.09f, 1f);
        EditorUtility.SetDirty(camera);

        // NPCs start in the open southern half, below the cross.
        var players = new SerializedObject(UnityEngine.Object.FindAnyObjectByType<PlayerManager>());
        players.FindProperty("spawnPosition").vector2Value = new Vector2(0f, -2f);
        players.ApplyModifiedPropertiesWithoutUndo();

        // The scene's WASD player stood on the old, thin cross; start it on open floor north of it.
        var player = UnityEngine.Object.FindAnyObjectByType<Player>();
        if (player != null)
        {
            player.transform.position = new Vector3(MapManager.CrossCenter.x, MapManager.CrossCenter.y + 3f, player.transform.position.z);
            EditorUtility.SetDirty(player.transform);
        }

        // A plain white test sprite ("Square", from the early window work) sat far outside
        // the old view; the west wing now covers that spot. Hidden, not deleted.
        var square = GameObject.Find("Square");
        if (square != null && square.GetComponents<Component>().Length == 2 && square.GetComponent<SpriteRenderer>() != null)
        {
            square.SetActive(false);
            EditorUtility.SetDirty(square);
        }

        var volume = UnityEngine.Object.FindAnyObjectByType<Volume>();
        volume.sharedProfile = BuildProfile();
        EditorUtility.SetDirty(volume);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("MANSION ENVIRONMENT BUILT");
    }

    public static void BuildBatch()
    {
        int exitCode = 0;
        try { Build(); }
        catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
        EditorApplication.Exit(exitCode);
    }

    // The original floor, enlarged and following the building: green marble in the
    // hall (with the cross inlay), a polygon-tile gallery in the west wing, a Center X
    // chapel in the east wing, red-brick runners through corridors and doorways, each
    // room edged with the sideways-tile trim. Outside: the Center X band, red brick
    // and cobblestone, plus the southern walkway, as in the original.
    private static void PaintFloor(Tilemap floor)
    {
        TileBase marble = Tile("Green marble_296");
        TileBase trim = Tile("Sideways tile");
        TileBase band = Tile("Center X");
        TileBase brick = Tile("Red Brick");
        TileBase inlay = Tile("Polygon tiling");
        TileBase cobble = Unlocked(AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Art/Tiles/Flooring/Cobblestone outer.asset"), "Cobblestone outer");

        Rect room = MapManager.Interior;
        int yMin = Mathf.RoundToInt(room.yMin);
        Vector2 c = MapManager.CrossCenter;
        int cx = Mathf.FloorToInt(c.x), cy = Mathf.FloorToInt(c.y);
        const int left = -26, right = 25, bottom = -13, top = 14;

        var inside = new System.Collections.Generic.List<Vector2Int>();
        for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
                if (Inside(x, y))
                    inside.Add(new Vector2Int(x, y));

        floor.ClearAllTiles();
        for (int y = bottom; y <= top; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int outside = 0;
                if (!Inside(x, y))
                {
                    outside = int.MaxValue;
                    foreach (Vector2Int cell in inside)
                        outside = Mathf.Min(outside, Mathf.Max(Mathf.Abs(cell.x - x), Mathf.Abs(cell.y - y)));
                }
                bool walkway = y < yMin && x >= cx - 2 && x <= cx + 1 && outside <= 6;
                bool walkwayTrim = y < yMin - 1 && (x == cx - 3 || x == cx + 2) && outside <= 6;
                TileBase tile;
                Color tint;

                if (outside == 0)
                {
                    Vector2 center = new Vector2(x + 0.5f, y + 0.5f);
                    bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                    if (Within(center, MapManager.Interior))
                        tile = edge ? trim : InCross(x, y, cx, cy) ? inlay : NearCross(x, y, cx, cy) ? trim : marble;
                    else if (Within(center, MapManager.WestWing))
                        tile = edge ? trim : inlay;
                    else if (Within(center, MapManager.EastWing))
                        tile = edge ? trim : band;
                    else
                        tile = brick;
                    tint = Vary(x, y, new Color(0.64f, 0.7f, 0.68f), 0.025f);
                    // Occasional worn, darker flagstones.
                    if (!edge && Hash(x, y, 3) < 0.035f) tint *= new Color(0.94f, 0.93f, 0.9f, 1f);
                }
                else if (walkway)
                {
                    tile = marble;
                    tint = Vary(x, y, new Color(0.46f, 0.52f, 0.54f), 0.025f);
                }
                else if (walkwayTrim)
                {
                    tile = trim;
                    tint = Vary(x, y, new Color(0.4f, 0.45f, 0.5f), 0.025f);
                }
                else
                {
                    tile = outside == 1 ? trim : outside <= 3 ? band : outside == 4 ? brick : cobble;
                    // Moonlit grounds: cooler and darker the farther from the house.
                    float fade = Mathf.Lerp(0.42f, 0.24f, Mathf.Clamp01((outside - 1) / 9f));
                    tint = Vary(x, y, new Color(fade * 0.85f, fade * 0.95f, fade * 1.15f), 0.03f);
                }

                var position = new Vector3Int(x, y, 0);
                floor.SetTile(position, tile);
                floor.SetTileFlags(position, TileFlags.None);
                floor.SetColor(position, tint);
            }
        }
        EditorUtility.SetDirty(floor);
    }

    // A floor cell belongs to the house when its center is on walkable floor.
    private static bool Inside(int x, int y) => MapManager.IsInsideMansion(new Vector2(x + 0.5f, y + 0.5f));

    private static bool Within(Vector2 point, Rect area) => area.Contains(point);

    // Plus-shaped inlay under the central cross, two cells wide.
    private static bool InCross(int x, int y, int cx, int cy)
    {
        bool horizontal = y >= cy - 1 && y <= cy && x >= cx - 4 && x <= cx + 3;
        bool vertical = x >= cx - 1 && x <= cx && y >= cy - 4 && y <= cy + 3;
        return horizontal || vertical;
    }

    private static bool NearCross(int x, int y, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (InCross(x + dx, y + dy, cx, cy))
                    return true;
        return false;
    }

    private static Color Vary(int x, int y, Color tint, float amount)
    {
        float v = 1f - amount + 2f * amount * Hash(x, y, 1);
        return new Color(tint.r * v, tint.g * v, tint.b * v, 1f);
    }

    // Deterministic per-cell noise, so the floor is identical on every build.
    private static float Hash(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    private static TileBase Tile(string name) => Unlocked(AssetDatabase.LoadAssetAtPath<TileBase>(Layout + name + ".asset"), name);

    // LockColor makes a loaded scene reset every cell to the tile's own white, discarding
    // the per-cell lighting tints. The tiles' appearance is otherwise unchanged.
    private static TileBase Unlocked(TileBase tileBase, string name)
    {
        if (tileBase == null)
            throw new InvalidOperationException("Missing floor tile " + name);
        if (tileBase is Tile tile && (tile.flags & TileFlags.LockColor) != 0)
        {
            tile.flags &= ~TileFlags.LockColor;
            EditorUtility.SetDirty(tile);
        }
        return tileBase;
    }

    private static void UsePointFiltering(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.filterMode == FilterMode.Point)
            return;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
    }

    // Scene-only grading: the shared SampleSceneProfile is also the render pipeline default.
    private static VolumeProfile BuildProfile()
    {
        AssetDatabase.DeleteAsset(ProfilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.value = TonemappingMode.None;

        var bloom = profile.Add<Bloom>(true);
        // Only firelight is bright enough to bloom against the moonlit floor.
        bloom.threshold.value = 0.75f;
        bloom.intensity.value = 0.45f;
        bloom.scatter.value = 0.65f;
        bloom.tint.value = new Color(1f, 0.85f, 0.7f);

        var vignette = profile.Add<Vignette>(true);
        vignette.color.value = new Color(0.02f, 0.02f, 0.08f);
        vignette.intensity.value = 0.34f;
        vignette.smoothness.value = 0.5f;

        var grading = profile.Add<ColorAdjustments>(true);
        grading.contrast.value = 12f;
        grading.saturation.value = 8f;

        foreach (VolumeComponent component in profile.components)
        {
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }
}

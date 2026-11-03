using UnityEngine;
using System.Collections.Generic;

public class MapManager : MonoBehaviour
{
    // Original prefabs
    public GameObject wallPrefab;
    public GameObject windowPrefab;
    public GameObject leverPrefab;

    public LeverManager leverManager;

    public int numWindows = 3;
    public int numLevers = 2;

    public bool advancedMap = true;

    // The grand hall: the original room, enlarged, centered on its central cross.
    public static readonly Rect Interior = new Rect(-7f, -5f, 14f, 12f);
    public static readonly Vector2 CrossCenter = new Vector2(0f, 1f);

    // Two wings joined to the hall by short corridors. Together with the doorways,
    // the walkable interior is 336 square units (the hall alone is 168).
    public static readonly Rect WestWing = Rect.MinMaxRect(-17.5f, -4.5f, -10.5f, 6.5f);
    public static readonly Rect EastWing = Mirror(WestWing);
    public static readonly Rect WestCorridor = Rect.MinMaxRect(-9.75f, 2.5f, -7.75f, 4.5f);
    public static readonly Rect EastCorridor = Mirror(WestCorridor);
    public static readonly Rect[] Rooms = { Interior, WestWing, EastWing, WestCorridor, EastCorridor };

    // Openings through the walls between hall, corridors and wings.
    public static readonly Rect[] Doorways =
    {
        Rect.MinMaxRect(-7.75f, 2.5f, -7f, 4.5f), Mirror(Rect.MinMaxRect(-7.75f, 2.5f, -7f, 4.5f)),
        Rect.MinMaxRect(-10.5f, 2.5f, -9.75f, 4.5f), Mirror(Rect.MinMaxRect(-10.5f, 2.5f, -9.75f, 4.5f)),
    };

    // Outer extent of every wall.
    public static readonly Rect BuildingBounds = Rect.MinMaxRect(-18.25f, -6.5f, 18.25f, 8.5f);

    private const float outerThickness = 0.75f;
    private const float innerThickness = 0.5f;

    // Footprint is the wall's collider. Face is the visible front along its south edge,
    // part of the footprint, so the top-down wall reads with depth and no roof.
    public struct WallPiece
    {
        public Rect Footprint;
        public float Face;
        public bool Outer;

        public WallPiece(Rect footprint, float face, bool outer)
        {
            Footprint = footprint;
            Face = face;
            Outer = outer;
        }
    }

    // Window centers, one per opening, on the inner face of an outer wall. As on the
    // original thin walls, each barricade's trigger reaches half a unit into the room,
    // so NPCs moving along the walls can repair it.
    private List<Vector3> WindowPositions = new List<Vector3>()
    {
        new Vector3(7f, -2f, 0),
        new Vector3(-7f, -2f, 0),
        new Vector3(-3.5f, -5f, 0),
        new Vector3(3.5f, -5f, 0),
        new Vector3(-3.5f, 7f, 0),
        new Vector3(3.5f, 7f, 0),
        new Vector3(-17.5f, 1f, 0),
        new Vector3(17.5f, 1f, 0),
        new Vector3(-14f, 6.5f, 0),
        new Vector3(14f, 6.5f, 0),
        new Vector3(-14f, -4.5f, 0),
        new Vector3(14f, -4.5f, 0),
    };

    //lever spawn directions
    private List<Vector3> LeverPositions = new List<Vector3>()
    {

        new Vector3(-1f, -1f, 0f),
        new Vector3(-1f,  0f, 0f),
        new Vector3(-1f,  1f, 0f),
        new Vector3( 0f, -1f, 0f),
        new Vector3( 0f,  0f, 0f),
        new Vector3( 0f,  1f, 0f),
        new Vector3( 1f, -1f, 0f),
        new Vector3( 1f,  0f, 0f),
        new Vector3( 1f,  1f, 0f)

    };

    // Lever rings scaled with the room, so levers keep their old relative spread.
    private static readonly Vector2 leverStep = new Vector2(1.5f, 1.25f);
    private const float minLeverDistanceFromNpc = 3f;

    List<Vector3> spawnedWindowPositions = new List<Vector3>();
    private List<Collider2D> spawnedWalls = new List<Collider2D>();
    private readonly List<WallPiece> builtWalls = new List<WallPiece>();
    private readonly List<Rect> windowOpenings = new List<Rect>();

    public IReadOnlyList<WallPiece> BuiltWalls => builtWalls;
    public IReadOnlyList<Vector3> SpawnedWindowPositions => spawnedWindowPositions;
    public IReadOnlyList<Rect> WindowOpenings => windowOpenings;

    // Window is 32 pixels wide at 32 PPU.
    private const float windowOpening = 1f;

    public static Rect Mirror(Rect r) => Rect.MinMaxRect(-r.xMax, r.yMin, -r.xMin, r.yMax);

    // True inside the house's walkable floor: rooms, corridors and doorways.
    public static bool IsInsideMansion(Vector2 point)
    {
        foreach (Rect room in Rooms)
            if (room.Contains(point))
                return true;
        foreach (Rect door in Doorways)
            if (door.Contains(point))
                return true;
        return false;
    }

    // Outer walls of hall, corridors and wings plus, for the advanced map, the
    // original cross: a central plus and four stubs reaching in from the walls.
    public static List<WallPiece> Layout(bool withCross)
    {
        Rect room = Interior;
        float t = outerThickness;
        var walls = new List<WallPiece>
        {
            new WallPiece(Rect.MinMaxRect(room.xMin - t, room.yMax, room.xMax + t, room.yMax + 2 * t), t, true),
            new WallPiece(Rect.MinMaxRect(room.xMin - t, room.yMin - 2 * t, room.xMax + t, room.yMin), t, true),
        };

        // Each side: the hall wall with its doorway, the corridor, and the wing.
        foreach (bool east in new[] { false, true })
        {
            var side = new List<WallPiece>
            {
                // Hall side wall, split by the doorway to the corridor.
                new WallPiece(Rect.MinMaxRect(room.xMin - t, room.yMin, room.xMin, WestCorridor.yMin), 0f, true),
                new WallPiece(Rect.MinMaxRect(room.xMin - t, WestCorridor.yMax, room.xMin, room.yMax + t), t, true),
                // Corridor walls.
                new WallPiece(Rect.MinMaxRect(WestCorridor.xMin, WestCorridor.yMax, WestCorridor.xMax, WestCorridor.yMax + 2 * t), t, true),
                new WallPiece(Rect.MinMaxRect(WestCorridor.xMin, WestCorridor.yMin - 2 * t, WestCorridor.xMax, WestCorridor.yMin), t, true),
                // Wing: north, south, outer side, and the inner side with its doorway.
                new WallPiece(Rect.MinMaxRect(WestWing.xMin - t, WestWing.yMax, WestWing.xMax + t, WestWing.yMax + 2 * t), t, true),
                new WallPiece(Rect.MinMaxRect(WestWing.xMin - t, WestWing.yMin - 2 * t, WestWing.xMax + t, WestWing.yMin), t, true),
                new WallPiece(Rect.MinMaxRect(WestWing.xMin - t, WestWing.yMin, WestWing.xMin, WestWing.yMax + t), 0f, true),
                new WallPiece(Rect.MinMaxRect(WestWing.xMax, WestWing.yMin, WestWing.xMax + t, WestCorridor.yMin), 0f, true),
                new WallPiece(Rect.MinMaxRect(WestWing.xMax, WestCorridor.yMax, WestWing.xMax + t, WestWing.yMax + t), t, true),
            };
            foreach (WallPiece piece in side)
                walls.Add(east ? new WallPiece(Mirror(piece.Footprint), piece.Face, piece.Outer) : piece);
        }

        if (!withCross)
            return walls;

        Vector2 c = CrossCenter;
        float h = innerThickness;
        const float arm = 2f;
        const float stub = 2f;

        // Central plus.
        walls.Add(new WallPiece(Rect.MinMaxRect(c.x - arm, c.y - h, c.x + arm, c.y + h), h, false));
        walls.Add(new WallPiece(Rect.MinMaxRect(c.x - h / 2, c.y - arm, c.x + h / 2, c.y + arm), h, false));

        // Stubs run into the outer walls so their tops join cleanly.
        walls.Add(new WallPiece(Rect.MinMaxRect(room.xMin - t, c.y - h, room.xMin + stub, c.y + h), h, false));
        walls.Add(new WallPiece(Rect.MinMaxRect(room.xMax - stub, c.y - h, room.xMax + t, c.y + h), h, false));
        walls.Add(new WallPiece(Rect.MinMaxRect(c.x - h / 2, room.yMax - stub, c.x + h / 2, room.yMax + 2 * t), h, false));
        walls.Add(new WallPiece(Rect.MinMaxRect(c.x - h / 2, room.yMin - t, c.x + h / 2, room.yMin + stub), 0f, false));
        return walls;
    }

    public void StartGameClicked()
    {
        Debug.Log("Generating map");

        // Preserve Sam's window spawning.
        for (int i = 0; i < numWindows; i++)
        {

            int index = Random.Range(0, WindowPositions.Count);
            Vector3 position = WindowPositions[index];
            GameObject window = Instantiate(windowPrefab);
            window.transform.position = WindowPositions[index];
            WindowPositions.RemoveAt(index);

            spawnedWindowPositions.Add(position);
        }

        foreach (WallPiece wall in Layout(advancedMap))
            CreateWallWithOpenings(wall);

        // NPCs may stand in a barricade's opening to repair it, but never pass through.
        foreach (Rect opening in windowOpenings)
        {
            var blocker = new GameObject("Window Blocker");
            blocker.transform.SetParent(transform, false);
            blocker.transform.position = opening.center;
            blocker.AddComponent<BoxCollider2D>().size = opening.size;
        }

        // Shared navigation for NPCs and zombies, with room for zombies outside.
        Rect area = BuildingBounds;
        area = Rect.MinMaxRect(area.xMin - 4f, area.yMin - 4f, area.xMax + 4f, area.yMax + 4f);
        var walkable = new List<Rect>(Rooms);
        walkable.AddRange(Doorways);
        var footprints = new List<Rect>();
        foreach (WallPiece wall in builtWalls)
            footprints.Add(wall.Footprint);
        MansionNavigation.Build(area, walkable, footprints, windowOpenings);

        //generate levers
        leverManager.totalLevers = numLevers;

        // NPCs spawn first on the same Start click. A lever under one would be pulled
        // without any search, so each lever starts a short walk away from every NPC.
        Agent[] agents = FindObjectsByType<Agent>();

        int spawnRetries = 0;
        for (int i = 0; i < numLevers; i++)
        {
            int index = Random.Range(0, LeverPositions.Count);
            Vector3 position = LeverPositions[index];

            // The hall (around the cross) or either wing.
            int room = Random.Range(0, 3);
            if (room == 0)
            {
                position.x *= Random.Range(1, 4) * leverStep.x;
                position.y *= Random.Range(1, 4) * leverStep.y;
                position += (Vector3)CrossCenter;
            }
            else
            {
                position.x *= Random.Range(1, 3) * leverStep.x;
                position.y *= Random.Range(1, 4) * leverStep.y;
                position += (Vector3)(room == 1 ? WestWing.center : EastWing.center);
            }

            GameObject lever = Instantiate(leverPrefab, position, Quaternion.identity);
            Physics2D.SyncTransforms();
            Bounds leverBounds = lever.GetComponent<SpriteRenderer>().bounds;
            leverBounds.Encapsulate(lever.GetComponent<Collider2D>().bounds);
            if (spawnedWalls.Exists(wall => wall.bounds.Intersects(leverBounds)) ||
                System.Array.Exists(agents, agent => Vector2.Distance(agent.transform.position, position) < minLeverDistanceFromNpc))
            {
                Destroy(lever);
                if (++spawnRetries >= 100)
                {
                    leverManager.totalLevers = i;
                    break;
                }
                i--;
                continue;
            }
            lever.name = $"lever_{i + 1}";
            LeverScript leverScript = lever.GetComponent<LeverScript>();
            leverScript.leverManager = leverManager;
            if (lever.TryGetComponent<LeverFactEmitter>(out var emitter))
                emitter.SetLeverId(lever.name);
            LeverPositions.RemoveAt(index);
        }

        // Castle wall art, lighting and props; purely visual.
        MansionDecorator decorator = GetComponent<MansionDecorator>();
        if (decorator != null)
            decorator.Build(builtWalls, spawnedWindowPositions);
    }

    // Splits a wall around every window on it, along the wall's long axis.
    private void CreateWallWithOpenings(WallPiece wall)
    {
        Rect r = wall.Footprint;
        bool horizontal = r.width >= r.height;
        var cuts = new List<float>();
        // Windows sit on the inner face, so test against the footprint with a small margin.
        Rect withFace = Rect.MinMaxRect(r.xMin - 0.05f, r.yMin - 0.05f, r.xMax + 0.05f, r.yMax + 0.05f);
        foreach (Vector3 window in spawnedWindowPositions)
            if (withFace.Contains(window))
                cuts.Add(horizontal ? window.x : window.y);
        cuts.Sort();

        float start = horizontal ? r.xMin : r.yMin;
        float end = horizontal ? r.xMax : r.yMax;
        foreach (float cut in cuts)
        {
            CreateWall(wall, horizontal, start, cut - windowOpening / 2f);
            start = cut + windowOpening / 2f;
            windowOpenings.Add(horizontal
                ? Rect.MinMaxRect(cut - windowOpening / 2f, r.yMin, cut + windowOpening / 2f, r.yMax)
                : Rect.MinMaxRect(r.xMin, cut - windowOpening / 2f, r.xMax, cut + windowOpening / 2f));
        }
        CreateWall(wall, horizontal, start, end);
    }

    private void CreateWall(WallPiece wall, bool horizontal, float from, float to)
    {
        if (to - from <= 0.01f)
            return;

        Rect r = wall.Footprint;
        Rect footprint = horizontal ? Rect.MinMaxRect(from, r.yMin, to, r.yMax) : Rect.MinMaxRect(r.xMin, from, r.xMax, to);

        GameObject segment = Instantiate(wallPrefab);
        segment.transform.position = footprint.center;
        segment.transform.rotation = Quaternion.identity;
        segment.transform.localScale = new Vector3(footprint.width, footprint.height, 1f);

        // The decorator draws the castle walls; the prefab keeps providing collision.
        if (GetComponent<MansionDecorator>() != null && segment.TryGetComponent<SpriteRenderer>(out var sprite))
            sprite.enabled = false;

        spawnedWalls.Add(segment.GetComponent<Collider2D>());
        builtWalls.Add(new WallPiece(footprint, wall.Face, wall.Outer));
    }
}

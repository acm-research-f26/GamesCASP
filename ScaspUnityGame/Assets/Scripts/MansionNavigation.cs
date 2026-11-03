using System.Collections.Generic;
using UnityEngine;

// Shared grid navigation for the generated mansion. Both AI strategies move NPCs
// through the same paths; zombies use the same grid with their own passability.
// NPCs: room interiors and doorways only, never barricade openings (so they cannot
// leave the house). Zombies: anywhere that is not wall, including window openings.
// Built from the wall geometry MapManager generates, not from physics queries.
public static class MansionNavigation
{
    public const float Cell = 0.25f;

    // Clearance from walls for the body's center (NPC collider about 0.76 x 0.94).
    private static readonly Vector2 npcHalfSize = new Vector2(0.4f, 0.48f);
    private static readonly Vector2 zombieHalfSize = new Vector2(0.36f, 0.4f);

    private static Rect bounds;
    private static int width, height;
    private static bool[] npcFree, zombieFree;
    private static readonly List<Rect> walkable = new List<Rect>();

    public static bool IsBuilt => npcFree != null;
    public static Rect Bounds => bounds;
    public static IReadOnlyList<Rect> WalkableAreas => walkable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        npcFree = zombieFree = null;
        walkable.Clear();
    }

    public static void Build(Rect area, IEnumerable<Rect> walkableAreas, IEnumerable<Rect> walls, IEnumerable<Rect> npcBlocked)
    {
        bounds = area;
        width = Mathf.CeilToInt(area.width / Cell);
        height = Mathf.CeilToInt(area.height / Cell);
        npcFree = new bool[width * height];
        zombieFree = new bool[width * height];
        walkable.Clear();
        walkable.AddRange(walkableAreas);
        var wallList = new List<Rect>(walls);
        var blockedList = new List<Rect>(npcBlocked);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 p = Center(x, y);
                var npcBody = new Rect(p - npcHalfSize, npcHalfSize * 2f);
                var zombieBody = new Rect(p - zombieHalfSize, zombieHalfSize * 2f);
                bool clearForZombie = !wallList.Exists(w => w.Overlaps(zombieBody));
                zombieFree[y * width + x] = clearForZombie;
                npcFree[y * width + x] = IsInside(p) && !wallList.Exists(w => w.Overlaps(npcBody)) && !blockedList.Exists(b => b.Overlaps(npcBody));
            }
    }

    // Inside the house's walkable floor (rooms, corridors, doorways).
    public static bool IsInside(Vector2 point)
    {
        foreach (Rect area in walkable)
            if (area.Contains(point))
                return true;
        return false;
    }

    public static bool IsFree(Vector2 point, bool zombie)
    {
        Vector2Int c = ToCell(point);
        return InGrid(c) && Free(c, zombie);
    }

    public static bool LineClear(Vector2 from, Vector2 to, bool zombie)
    {
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / (Cell * 0.5f)));
        for (int i = 0; i <= steps; i++)
        {
            Vector2Int c = ToCell(Vector2.Lerp(from, to, i / (float)steps));
            if (!InGrid(c) || !Free(c, zombie))
                return false;
        }
        return true;
    }

    // A* over the grid (8-way, no corner cutting), then string-pulled to waypoints.
    // The final waypoint is the goal itself when its cell is free, otherwise the
    // nearest free cell (for targets on a wall face, such as barricades).
    public static bool TryFindPath(Vector2 from, Vector2 to, bool zombie, List<Vector2> path)
    {
        path.Clear();
        if (!IsBuilt)
            return false;
        if (!NearestFree(from, zombie, out Vector2Int start) || !NearestFree(to, zombie, out Vector2Int goal))
            return false;

        int n = width * height;
        var cameFrom = new int[n];
        var cost = new float[n];
        for (int i = 0; i < n; i++) { cameFrom[i] = -1; cost[i] = float.MaxValue; }
        var open = new SortedSet<(float, int)>();
        int startIndex = Index(start), goalIndex = Index(goal);
        cost[startIndex] = 0f;
        open.Add((Heuristic(start, goal), startIndex));
        var closed = new bool[n];

        while (open.Count > 0)
        {
            var best = open.Min;
            open.Remove(best);
            int current = best.Item2;
            if (current == goalIndex)
                break;
            if (closed[current])
                continue;
            closed[current] = true;
            var c = new Vector2Int(current % width, current / width);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var next = new Vector2Int(c.x + dx, c.y + dy);
                    if (!InGrid(next) || !Free(next, zombie)) continue;
                    if (dx != 0 && dy != 0 && (!Free(new Vector2Int(c.x + dx, c.y), zombie) || !Free(new Vector2Int(c.x, c.y + dy), zombie))) continue;
                    int ni = Index(next);
                    float step = dx != 0 && dy != 0 ? 1.41421f : 1f;
                    if (cost[current] + step < cost[ni])
                    {
                        cost[ni] = cost[current] + step;
                        cameFrom[ni] = current;
                        open.Add((cost[ni] + Heuristic(next, goal), ni));
                    }
                }
        }

        if (cost[goalIndex] == float.MaxValue)
            return false;

        var cells = new List<Vector2>();
        for (int i = goalIndex; i != -1; i = cameFrom[i])
            cells.Add(Center(i % width, i / width));
        cells.Reverse();
        if (IsFree(to, zombie))
            cells[cells.Count - 1] = to;

        // String pulling: keep only the waypoints needed for clear straight lines.
        Vector2 anchor = from;
        for (int i = 1; i < cells.Count; i++)
            if (!LineClear(anchor, cells[i], zombie))
            {
                path.Add(cells[i - 1]);
                anchor = cells[i - 1];
            }
        path.Add(cells[cells.Count - 1]);
        return true;
    }

    // Path length in units, or infinity when unreachable.
    public static float PathLength(Vector2 from, Vector2 to, bool zombie)
    {
        var path = new List<Vector2>();
        if (!TryFindPath(from, to, zombie, path))
            return float.PositiveInfinity;
        float length = 0f;
        Vector2 previous = from;
        foreach (Vector2 point in path)
        {
            length += Vector2.Distance(previous, point);
            previous = point;
        }
        return length;
    }

    // The reachable NPC position within radius that is farthest from the threat.
    public static bool TryFleePoint(Vector2 from, Vector2 threat, float radius, out Vector2 point)
    {
        point = from;
        if (!IsBuilt || !NearestFree(from, false, out Vector2Int start))
            return false;
        float best = float.MinValue;
        foreach (Vector2Int c in Reachable(start, radius))
        {
            Vector2 p = Center(c.x, c.y);
            float score = Vector2.Distance(p, threat) - 0.3f * Vector2.Distance(p, from);
            if (score > best)
            {
                best = score;
                point = p;
            }
        }
        return best > float.MinValue;
    }

    // A random reachable NPC position within radius, preferring cells the agent
    // has visited least (so searching explores new rooms instead of circling).
    public static bool TryExplorePoint(Vector2 from, float radius, Dictionary<Vector2Int, int> visits, out Vector2 point)
    {
        point = from;
        if (!IsBuilt || !NearestFree(from, false, out Vector2Int start))
            return false;
        var candidates = Reachable(start, radius);
        if (candidates.Count == 0)
            return false;
        int fewest = int.MaxValue;
        foreach (Vector2Int c in candidates)
            fewest = Mathf.Min(fewest, Visits(visits, Center(c.x, c.y)));
        var least = candidates.FindAll(c => Visits(visits, Center(c.x, c.y)) == fewest && Vector2.Distance(Center(c.x, c.y), from) > radius * 0.4f);
        if (least.Count == 0)
            least = candidates;
        Vector2Int pick = least[Random.Range(0, least.Count)];
        point = Center(pick.x, pick.y);
        return true;
    }

    // Coarse 2-unit regions an explorer has passed through.
    public static Vector2Int Region(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 2f), Mathf.FloorToInt(p.y / 2f));

    private static int Visits(Dictionary<Vector2Int, int> visits, Vector2 p) => visits != null && visits.TryGetValue(Region(p), out int count) ? count : 0;

    private static List<Vector2Int> Reachable(Vector2Int start, float radius)
    {
        var result = new List<Vector2Int>();
        var seen = new HashSet<int> { Index(start) };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        Vector2 origin = Center(start.x, start.y);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            result.Add(c);
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int n = c + d;
                if (InGrid(n) && Free(n, false) && Vector2.Distance(Center(n.x, n.y), origin) <= radius && seen.Add(Index(n)))
                    queue.Enqueue(n);
            }
        }
        return result;
    }

    private static bool NearestFree(Vector2 point, bool zombie, out Vector2Int cell)
    {
        cell = ToCell(point);
        if (InGrid(cell) && Free(cell, zombie))
            return true;
        for (int r = 1; r <= 12; r++)
        {
            float best = float.MaxValue;
            bool found = false;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var c = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!InGrid(c) || !Free(c, zombie)) continue;
                    float d = Vector2.Distance(Center(c.x, c.y), point);
                    if (d < best) { best = d; found = true; cell = c; }
                }
            if (found)
                return true;
        }
        return false;
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + 0.41421f * Mathf.Min(dx, dy);
    }

    private static bool Free(Vector2Int c, bool zombie) => (zombie ? zombieFree : npcFree)[Index(c)];
    private static bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
    private static int Index(Vector2Int c) => c.y * width + c.x;
    private static Vector2 Center(int x, int y) => new Vector2(bounds.xMin + (x + 0.5f) * Cell, bounds.yMin + (y + 0.5f) * Cell);
    private static Vector2Int ToCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x - bounds.xMin) / Cell), Mathf.FloorToInt((p.y - bounds.yMin) / Cell));
}

// Path following for one mover. Walks straight when the line is clear (the common
// case, exactly as before), otherwise follows a planned path, replanning when the
// goal moves or periodically. Without a built grid it falls back to a straight line.
public sealed class NavMover
{
    private readonly bool zombie;
    private readonly List<Vector2> path = new List<Vector2>();
    private int next;
    private Vector2 plannedGoal;
    private float plannedAt = float.NegativeInfinity;

    public bool Unreachable { get; private set; }

    public NavMover(bool zombie)
    {
        this.zombie = zombie;
    }

    public void Reset()
    {
        path.Clear();
        next = 0;
        plannedAt = float.NegativeInfinity;
        Unreachable = false;
    }

    public Vector2 Step(Vector2 position, Vector2 goal, float maxDelta, float time)
    {
        if (!MansionNavigation.IsBuilt || MansionNavigation.LineClear(position, goal, zombie))
        {
            path.Clear();
            Unreachable = false;
            return Vector2.MoveTowards(position, goal, maxDelta);
        }

        if (path.Count == 0 || next >= path.Count || Vector2.Distance(goal, plannedGoal) > 0.5f || time - plannedAt > 0.75f)
        {
            plannedGoal = goal;
            plannedAt = time;
            next = 0;
            Unreachable = !MansionNavigation.TryFindPath(position, goal, zombie, path);
            if (Unreachable)
                return position;
        }

        // Skip waypoints that are already in direct view.
        while (next < path.Count - 1 && MansionNavigation.LineClear(position, path[next + 1], zombie))
            next++;
        Vector2 waypoint = path[next];
        if (Vector2.Distance(position, waypoint) < 0.05f && next < path.Count - 1)
            waypoint = path[++next];
        return Vector2.MoveTowards(position, waypoint, maxDelta);
    }
}

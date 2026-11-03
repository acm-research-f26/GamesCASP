using System.Collections.Generic;
using UnityEngine;

// Castle walls, firelight and props for the mansion, cut from the project's own sprite sheets.
// Purely visual: nothing here has a collider, everything sorts below characters, and props
// stay against walls and in corners, so movement, perception and objectives are unchanged.
public class MansionDecorator : MonoBehaviour
{
    [Header("Existing art (assigned by Tools > Mansion > Build Environment)")]
    [SerializeField] private Texture2D brickSheet; // Art/Tiles/Flooring/wood floor.png
    [SerializeField] private Texture2D stoneSheet; // Art/Tiles/Flooring/marble.png
    [SerializeField] private Texture2D dungeonSheet; // Art/Tiles/lever/Dungeon Tileset.png

    [Header("Palette")]
    [SerializeField] private Color capTint = new Color(0.66f, 0.65f, 0.78f);
    [SerializeField] private Color faceTint = new Color(0.74f, 0.52f, 0.54f);
    [SerializeField] private Color edgeColor = new Color(0.07f, 0.05f, 0.1f);
    [SerializeField] private Color pillarTint = new Color(0.95f, 0.98f, 1f);
    [SerializeField] private Color fireLight = new Color(1f, 0.6f, 0.22f, 0.4f);
    [SerializeField] private Color crystalLight = new Color(0.3f, 1f, 0.85f, 0.3f);
    [SerializeField] private Color fogColor = new Color(0.45f, 1f, 0.6f, 0.11f);
    [SerializeField] private Color moonLight = new Color(0.6f, 0.75f, 1f, 0.2f);

    // Floor tilemap is -10; characters, levers and zombies are 0; windows are 10.
    private const int DecalOrder = -9;
    private const int ShadowOrder = -8;
    private const int EdgeOrder = -7;
    private const int FaceOrder = -6;
    private const int FaceDetailOrder = -5;
    private const int CapOrder = -4;
    private const int PropOrder = -3;
    private const int GlowOrder = -2;
    private const int FireOrder = -1;

    private const float PixelsPerUnit = 32f;
    private const float Pixel = 1f / PixelsPerUnit;

    private static Texture2D whiteTexture, glowTexture, shadowTexture;
    private Transform root;
    private readonly List<Rect> footprints = new List<Rect>();
    private readonly List<Vector2> keepClear = new List<Vector2>();

    public Transform Root => root;

    public void Build(IReadOnlyList<MapManager.WallPiece> walls, IReadOnlyList<Vector3> windows)
    {
        if (root != null)
            Destroy(root.gameObject);
        root = new GameObject("Mansion Decor").transform;
        root.SetParent(transform, false);

        footprints.Clear();
        foreach (MapManager.WallPiece wall in walls)
            footprints.Add(wall.Footprint);

        // Never cover a window/barricade or a lever with decoration.
        keepClear.Clear();
        foreach (Vector3 window in windows)
            keepClear.Add(window);
        foreach (LeverScript lever in FindObjectsByType<LeverScript>())
            keepClear.Add(lever.transform.position);

        foreach (MapManager.WallPiece wall in walls)
            BuildWall(wall);

        BuildCrossCenterpiece(walls);
        BuildTorches();
        BuildCorners();
        BuildBarricades(windows);
        BuildFloorDetails();
        BuildFog();
        BuildWisps();
    }

    // ---------- Walls ----------

    private void BuildWall(MapManager.WallPiece wall)
    {
        Rect r = wall.Footprint;
        float face = Mathf.Min(wall.Face, r.height);

        // Dark masonry edge around the union of all walls (hidden where other walls overlap).
        Quad("Wall Edge", Expand(r, 2 * Pixel), edgeColor, EdgeOrder);

        if (face > 0f)
        {
            var faceRect = new Rect(r.xMin, r.yMin, r.width, face);
            Tiled("Wall Face", faceRect, Brick(), faceTint, FaceOrder);
            // Mortar lip under the capstones, and darkening toward the base.
            Quad("Wall Lip", new Rect(r.xMin, r.yMin + face - 2 * Pixel, r.width, 2 * Pixel), edgeColor, FaceDetailOrder);
            Gradient("Wall Base Shade", new Rect(r.xMin, r.yMin, r.width, face * 0.6f), new Color(0, 0, 0, 0.45f), FaceDetailOrder, true);

            // Soft contact shadow on the floor in front of exposed faces.
            if (MapManager.IsInsideMansion(new Vector2(r.center.x, r.yMin - 0.1f)))
                Gradient("Wall Shadow", new Rect(r.xMin, r.yMin - 0.55f, r.width, 0.55f), new Color(0.02f, 0, 0.06f, 0.5f), ShadowOrder, false);
        }

        Tiled("Wall Cap", new Rect(r.xMin, r.yMin + face, r.width, r.height - face), Cobble(), capTint, CapOrder);

        // Outer side walls also darken the floor next to them, like ambient occlusion.
        if (wall.Outer && face == 0f)
        {
            bool west = r.center.x < 0f;
            var strip = new Rect(west ? r.xMax : r.xMin - 0.45f, r.yMin, 0.45f, r.height);
            SideShade(strip, west);
        }
    }

    private void BuildCrossCenterpiece(IReadOnlyList<MapManager.WallPiece> walls)
    {
        Vector2 c = MapManager.CrossCenter;
        bool hasCross = false;
        foreach (MapManager.WallPiece wall in walls)
            hasCross |= !wall.Outer && wall.Footprint.Contains(c);
        if (!hasCross)
            return;

        // A great fire bowl burns on the crossing, lighting all four quarters.
        Brazier(new Vector2(c.x, c.y - 0.25f), 1.35f, 3.6f);

        // Stone columns finish every free end of the cross.
        foreach (MapManager.WallPiece wall in walls)
        {
            Rect r = wall.Footprint;
            if (wall.Outer)
                continue;

            if (r.width >= r.height)
            {
                if (IsFreeEnd(new Vector2(r.xMin - 0.05f, r.center.y)))
                    Pillar(new Vector2(r.xMin + 0.25f, r.yMin));
                if (IsFreeEnd(new Vector2(r.xMax + 0.05f, r.center.y)))
                    Pillar(new Vector2(r.xMax - 0.25f, r.yMin));
            }
            // Vertical ends are left as capstone: a column there would hide the wall.
        }
    }

    private bool IsFreeEnd(Vector2 beyond)
    {
        if (!MapManager.IsInsideMansion(beyond))
            return false;
        foreach (Rect footprint in footprints)
            if (footprint.Contains(beyond))
                return false;
        return true;
    }

    // ---------- Fire ----------

    private void BuildTorches()
    {
        Rect room = MapManager.Interior;

        // Sconces on the north wall's inner face, the cross faces, and the entrance outside.
        Vector2 c = MapManager.CrossCenter;
        var sconces = new List<Vector2>();
        foreach (float x in new[] { -5.75f, -1.5f, 1.5f, 5.75f })
        {
            sconces.Add(new Vector2(x, room.yMax));
            sconces.Add(new Vector2(x, room.yMin - 1.5f));
        }
        foreach (float x in new[] { c.x - 1.1f, c.x + 1.1f, room.xMin + 1f, room.xMax - 1f })
            sconces.Add(new Vector2(x, c.y - 0.5f));
        foreach (Rect wing in new[] { MapManager.WestWing, MapManager.EastWing })
            foreach (float dx in new[] { -2.2f, 2.2f })
            {
                sconces.Add(new Vector2(wing.center.x + dx, wing.yMax));
                sconces.Add(new Vector2(wing.center.x + dx, wing.yMin - 1.5f));
            }
        foreach (Rect corridor in new[] { MapManager.WestCorridor, MapManager.EastCorridor })
            sconces.Add(new Vector2(corridor.center.x, corridor.yMax));

        foreach (Vector2 baseOfFace in sconces)
        {
            Vector2 at = baseOfFace + new Vector2(0f, 0.12f);
            if (OnSouthFace(at) && IsClear(at, 1.1f))
                Sconce(at);
        }

        // Standing torches flank each side window from inside the room.
        foreach (Vector2 window in keepClear)
        {
            if (!OnSideWall(window))
                continue;
            float x = window.x + Inward(window).x * 0.3f;
            foreach (float dy in new[] { -1.15f, 1.15f })
            {
                Vector2 at = new Vector2(x, window.y + dy - 0.35f);
                if (MapManager.IsInsideMansion(at) && IsClear(at, 0.9f, window))
                    StandingTorch(at);
            }
        }
    }

    // True when the point lies on a horizontal wall's face that no other wall covers.
    private bool OnSouthFace(Vector2 point)
    {
        bool onFace = false;
        foreach (Rect footprint in footprints)
        {
            if (!footprint.Contains(point))
                continue;
            if (footprint.width < footprint.height || point.y > footprint.yMin + 0.5f)
                return false;
            onFace = true;
        }
        return onFace;
    }

    private void Sconce(Vector2 bottom)
    {
        Sprite[] frames = DungeonFrames(new[] { 52, 68, 84, 100 }, 81, 8, 25);
        SpriteRenderer torch = Place("Wall Torch", frames[0], bottom, FireOrder, new Vector2(0.5f, 0f), Color.white);
        SpriteRenderer glow = Glow(bottom + new Vector2(0f, 0.65f), 2.6f, fireLight);
        Animate(torch, frames, 7f, glow, fireLight.a);
    }

    private void StandingTorch(Vector2 bottom)
    {
        Sprite[] frames = DungeonFrames(new[] { 51, 67, 83, 99 }, 49, 10, 28);
        SpriteRenderer torch = Place("Standing Torch", frames[0], bottom, FireOrder, new Vector2(0.5f, 0f), Color.white);
        SpriteRenderer glow = Glow(bottom + new Vector2(0f, 0.7f), 2.2f, fireLight);
        Animate(torch, frames, 7f, glow, fireLight.a * 0.8f);
    }

    private void Brazier(Vector2 bottom, float scale, float radius)
    {
        var frames = new Sprite[7];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = Cut(dungeonSheet, new Rect(80 + 32 * i, 2, 32, 46), new Vector2(0.5f, 0f));
        SpriteRenderer fire = Place("Fire Bowl", frames[0], bottom, FireOrder, new Vector2(0.5f, 0f), Color.white);
        fire.transform.localScale = Vector3.one * scale;
        SpriteRenderer glow = Glow(bottom + new Vector2(0f, 0.6f * scale), radius, fireLight);
        Animate(fire, frames, 9f, glow, fireLight.a * 1.15f);
    }

    // ---------- Props ----------

    private void BuildCorners()
    {
        Rect room = MapManager.Interior;

        // Fire bowls warm the two northern corners.
        Brazier(new Vector2(room.xMin + 0.6f, room.yMax - 1.05f), 1f, 3f);
        Brazier(new Vector2(room.xMax - 0.6f, room.yMax - 1.05f), 1f, 3f);

        // Cold, ghostly crystals and the clutter of an old house fill the southern corners.
        Crystals(new Vector2(room.xMin + 0.45f, room.yMin + 0.2f));
        Crystals(new Vector2(room.xMax - 0.45f, room.yMin + 0.2f));
        Prop("Pot", new Rect(208, 128, 16, 16), new Vector2(room.xMin + 1.1f, room.yMin + 0.12f));
        Prop("Pot", new Rect(208, 112, 16, 16), new Vector2(room.xMin + 0.45f, room.yMin + 0.85f));
        Prop("Chest", new Rect(224, 48, 16, 16), new Vector2(room.xMax - 1.15f, room.yMin + 0.12f));
        Prop("Pot", new Rect(224, 112, 16, 16), new Vector2(room.xMax - 0.45f, room.yMin + 0.85f));
        Prop("Chest", new Rect(224, 80, 16, 16), new Vector2(room.xMin + 1.4f, room.yMax - 0.75f));
        Prop("Pot", new Rect(224, 128, 16, 16), new Vector2(room.xMax - 1.35f, room.yMax - 0.72f));

        // West wing: a cold crystal gallery. East wing: a fire chapel.
        Rect west = MapManager.WestWing, east = MapManager.EastWing;
        Crystals(new Vector2(west.xMin + 0.45f, west.yMax - 0.9f));
        Crystals(new Vector2(west.xMin + 0.45f, west.yMin + 0.2f));
        Crystals(new Vector2(west.xMax - 0.45f, west.yMin + 0.2f));
        Prop("Statue", new Rect(208, 400, 16, 32), new Vector2(west.center.x, west.yMax - 0.98f), pillarTint);
        Prop("Pot", new Rect(224, 128, 16, 16), new Vector2(west.xMax - 0.5f, west.yMax - 0.75f));
        Brazier(new Vector2(east.xMin + 0.6f, east.yMax - 1.05f), 1f, 3f);
        Brazier(new Vector2(east.xMax - 0.6f, east.yMax - 1.05f), 1f, 3f);
        Brazier(new Vector2(east.xMax - 0.6f, east.yMin + 0.1f), 1f, 3f);
        Prop("Chest", new Rect(224, 64, 16, 16), new Vector2(east.xMin + 0.6f, east.yMin + 0.12f));
        Prop("Statue", new Rect(208, 400, 16, 32), new Vector2(east.center.x, east.yMax - 0.98f), pillarTint);

        // Statues keep watch either side of the north stub of the cross.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 at = new Vector2(MapManager.CrossCenter.x + side * 0.85f, room.yMax - 0.98f);
            if (IsClear(at, 0.8f))
                Prop("Statue", new Rect(208, 400, 16, 32), at, pillarTint);
        }
    }

    private void Crystals(Vector2 bottom)
    {
        if (!IsClear(bottom, 0.9f))
            return;
        Prop("Crystals", new Rect(192, 48, 16, 16), bottom);
        SpriteRenderer glow = Glow(bottom + new Vector2(0f, 0.25f), 2f, crystalLight);
        Animate(null, null, 0f, glow, crystalLight.a).glowVariation = 0.3f;
    }

    private void Pillar(Vector2 bottom)
    {
        Place("Column", Cut(dungeonSheet, new Rect(208, 432, 16, 48), new Vector2(0.5f, 0f)), bottom, PropOrder, new Vector2(0.5f, 0f), pillarTint);
    }

    private void Prop(string label, Rect pixels, Vector2 bottom, Color? tint = null)
    {
        if (IsClear(bottom, 0.6f))
            Place(label, Cut(dungeonSheet, pixels, new Vector2(0.5f, 0f)), bottom, PropOrder, new Vector2(0.5f, 0f), tint ?? Color.white);
    }

    // Planks across each window opening, fading as the barricade loses health.
    private void BuildBarricades(IReadOnlyList<Vector3> windows)
    {
        Sprite planks = Cut(brickSheet, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), SpriteMeshType.FullRect);
        foreach (Vector3 position in windows)
        {
            Window window = null;
            foreach (Window candidate in FindObjectsByType<Window>())
                if (Vector2.Distance(candidate.transform.position, position) < 0.1f)
                    window = candidate;
            if (window == null)
                continue;

            bool vertical = OnSideWall(position);
            // Above the window prefab's arch (order 10), so the barricade's state stays visible.
            SpriteRenderer boards = Tiled("Barricade Planks", new Rect((Vector2)position - new Vector2(0.4f, 0.3f), new Vector2(0.8f, 0.6f)), planks, new Color(0.8f, 0.58f, 0.42f, 0.9f), 11);
            if (vertical)
                boards.transform.localRotation = Quaternion.Euler(0, 0, 90);
            boards.gameObject.AddComponent<MansionBarricadeView>().Bind(window, boards);

            // Pale moonlight spills in through every barricade, marking it from across the hall.
            Vector2 inward = Inward(position);
            Animate(null, null, 0f, Glow((Vector2)position + inward * 0.6f, 1.9f, moonLight), moonLight.a).glowVariation = 0.08f;
        }
    }

    // A few pebbles at the foot of the walls; seeded so every game looks the same.
    private void BuildFloorDetails()
    {
        var random = new System.Random(1337);
        Rect room = MapManager.Interior;
        Sprite[] pebbles = { Cut(dungeonSheet, new Rect(160, 48, 16, 16), new Vector2(0.5f, 0.5f)), Cut(dungeonSheet, new Rect(176, 48, 16, 16), new Vector2(0.5f, 0.5f)) };
        for (int i = 0, placed = 0; i < 60 && placed < 12; i++)
        {
            bool vertical = random.NextDouble() < 0.5;
            float along = (float)random.NextDouble();
            float side = random.NextDouble() < 0.5 ? 0.25f : -0.25f;
            Vector2 at = vertical
                ? new Vector2(side > 0 ? room.xMin + 0.25f : room.xMax - 0.25f, Mathf.Lerp(room.yMin + 0.6f, room.yMax - 1.2f, along))
                : new Vector2(Mathf.Lerp(room.xMin + 1f, room.xMax - 1f, along), side > 0 ? room.yMin + 0.22f : room.yMax - 0.75f);
            if (!IsClear(at, 1.2f) || InsideWall(at))
                continue;
            SpriteRenderer pebble = Place("Pebbles", pebbles[placed % 2], at, DecalOrder, new Vector2(0.5f, 0.5f), new Color(0.75f, 0.75f, 0.85f, 0.85f));
            pebble.flipX = random.NextDouble() < 0.5;
            placed++;
        }
    }

    // Green, ghostly mist drifting in the grounds outside, as on the title screen.
    private void BuildFog()
    {
        var random = new System.Random(7);
        Rect room = MapManager.Interior;
        Rect house = MapManager.BuildingBounds;
        for (int i = 0, tries = 0; i < 16 && tries < 400; tries++)
        {
            Vector2 at = new Vector2(Mathf.Lerp(house.xMin - 5f, house.xMax + 5f, (float)random.NextDouble()), Mathf.Lerp(house.yMin - 3f, house.yMax + 3f, (float)random.NextDouble()));
            if (MapManager.IsInsideMansion(at) || InsideWall(at))
                continue;
            i++;
            SpriteRenderer mist = Glow(at, 2.5f + 2f * (float)random.NextDouble(), fogColor);
            mist.name = "Mist";
            mist.transform.localScale = new Vector3(mist.transform.localScale.x * 1.6f, mist.transform.localScale.y, 1f);
            mist.sortingOrder = GlowOrder;
            MansionFlicker drift = Animate(null, null, 0f, mist, fogColor.a);
            drift.glowVariation = 0.45f;
            drift.drift = new Vector2(0.6f + (float)random.NextDouble(), 0.2f);
        }
    }

    // A few faint will-o'-wisps drift along the walls inside: haunted, never in the way.
    private void BuildWisps()
    {
        Rect room = MapManager.Interior;
        Vector2[] spots =
        {
            new Vector2(room.xMin + 0.7f, room.yMin + 3.2f), new Vector2(room.xMax - 0.7f, room.yMax - 3.6f),
            new Vector2(room.xMin + 3.2f, room.yMax - 0.8f), new Vector2(room.xMax - 2.6f, room.yMin + 0.8f),
            new Vector2(MapManager.WestWing.center.x, MapManager.WestWing.yMin + 0.8f), new Vector2(MapManager.EastWing.xMax - 0.8f, MapManager.EastWing.center.y),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            if (!IsClear(spots[i], 1.5f) || InsideWall(spots[i]))
                continue;
            SpriteRenderer core = Glow(spots[i], 0.22f, new Color(0.75f, 1f, 0.85f, 0.55f));
            core.name = "Wisp";
            SpriteRenderer halo = Glow(spots[i], 0.9f, new Color(0.4f, 1f, 0.6f, 0.16f));
            halo.transform.SetParent(core.transform, true);
            MansionFlicker drift = Animate(null, null, 0f, core, 0.55f);
            drift.glowVariation = 0.35f;
            drift.drift = i % 2 == 0 ? new Vector2(0f, 0.45f) : new Vector2(0.45f, 0f);
        }
    }

    // ---------- Placement helpers ----------

    // A window on a side (vertical) wall of the generated layout.
    private static bool OnSideWall(Vector2 window)
    {
        foreach (MapManager.WallPiece piece in MapManager.Layout(true))
        {
            Rect r = piece.Footprint;
            if (Rect.MinMaxRect(r.xMin - 0.05f, r.yMin - 0.05f, r.xMax + 0.05f, r.yMax + 0.05f).Contains(window))
                return r.height > r.width;
        }
        return false;
    }

    // Unit direction from a window into the room it belongs to.
    private static Vector2 Inward(Vector2 window)
    {
        foreach (Vector2 d in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            if (MapManager.IsInsideMansion(window + d * 0.4f))
                return d;
        return Vector2.zero;
    }

    private bool IsClear(Vector2 at, float radius, Vector2? ignore = null)
    {
        foreach (Vector2 point in keepClear)
            if ((ignore == null || point != ignore.Value) && Vector2.Distance(point, at) < radius)
                return false;
        return true;
    }

    private bool InsideWall(Vector2 at)
    {
        foreach (Rect footprint in footprints)
            if (footprint.Contains(at))
                return true;
        return false;
    }

    // ---------- Sprite helpers ----------

    private Sprite brick, cobble;

    private Sprite Brick() => brick != null ? brick : brick = Cut(brickSheet, new Rect(64, 480, 32, 32), new Vector2(0.5f, 0.5f), SpriteMeshType.FullRect);

    private Sprite Cobble() => cobble != null ? cobble : cobble = Cut(stoneSheet, new Rect(64, 352, 32, 32), new Vector2(0.5f, 0.5f), SpriteMeshType.FullRect);

    private Sprite[] DungeonFrames(int[] xs, int y, int width, int height)
    {
        var frames = new Sprite[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            frames[i] = Cut(dungeonSheet, new Rect(xs[i], y, width, height), new Vector2(0.5f, 0f));
        return frames;
    }

    private static Sprite Cut(Texture2D sheet, Rect pixels, Vector2 pivot, SpriteMeshType mesh = SpriteMeshType.Tight)
    {
        return UnityEngine.Sprite.Create(sheet, pixels, pivot, PixelsPerUnit, 0, mesh);
    }

    private SpriteRenderer Place(string label, Sprite sprite, Vector2 position, int order, Vector2 pivot, Color tint)
    {
        var obj = new GameObject(label);
        obj.transform.SetParent(root, false);
        obj.transform.position = position;
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingOrder = order;
        return renderer;
    }

    private SpriteRenderer Tiled(string label, Rect rect, Sprite sprite, Color tint, int order)
    {
        SpriteRenderer renderer = Place(label, sprite, rect.center, order, new Vector2(0.5f, 0.5f), tint);
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        renderer.size = rect.size;
        return renderer;
    }

    private SpriteRenderer Quad(string label, Rect rect, Color tint, int order)
    {
        SpriteRenderer renderer = Place(label, WhiteSprite(), rect.center, order, new Vector2(0.5f, 0.5f), tint);
        renderer.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        return renderer;
    }

    // Vertical gradient, darkest at the top (wall shadows) or at the bottom (face shading).
    private void Gradient(string label, Rect rect, Color tint, int order, bool darkAtBottom)
    {
        SpriteRenderer renderer = Place(label, ShadowSprite(), rect.center, order, new Vector2(0.5f, 0.5f), tint);
        renderer.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        renderer.flipY = darkAtBottom;
    }

    private void SideShade(Rect strip, bool darkOnLeft)
    {
        SpriteRenderer renderer = Place("Wall Side Shadow", ShadowSprite(), strip.center, ShadowOrder, new Vector2(0.5f, 0.5f), new Color(0.02f, 0, 0.06f, 0.4f));
        renderer.transform.localRotation = Quaternion.Euler(0, 0, darkOnLeft ? 90f : -90f);
        renderer.transform.localScale = new Vector3(strip.height, strip.width, 1f);
    }

    private SpriteRenderer Glow(Vector2 center, float radius, Color color)
    {
        SpriteRenderer renderer = Place("Light", GlowSprite(), center, GlowOrder, new Vector2(0.5f, 0.5f), color);
        renderer.transform.localScale = Vector3.one * radius * 2f;
        return renderer;
    }

    private static MansionFlicker Animate(SpriteRenderer target, Sprite[] frames, float fps, SpriteRenderer glow, float glowAlpha)
    {
        GameObject host = target != null ? target.gameObject : glow.gameObject;
        var flicker = host.AddComponent<MansionFlicker>();
        flicker.target = target;
        flicker.frames = frames;
        flicker.framesPerSecond = fps;
        flicker.glow = glow;
        flicker.glowAlpha = glowAlpha;
        return flicker;
    }

    private static Rect Expand(Rect r, float amount) => Rect.MinMaxRect(r.xMin - amount, r.yMin - amount, r.xMax + amount, r.yMax + amount);

    // Generated lighting masks: gradients only, no new artwork.
    private static Sprite WhiteSprite()
    {
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(4, 4) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            whiteTexture.SetPixels(pixels);
            whiteTexture.Apply();
        }
        return UnityEngine.Sprite.Create(whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }

    private static Sprite GlowSprite()
    {
        const int size = 64;
        if (glowTexture == null)
        {
            glowTexture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * size / 2f) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color(1, 1, 1, a * a * (3f - 2f * a));
                }
            glowTexture.SetPixels(pixels);
            glowTexture.Apply();
        }
        return UnityEngine.Sprite.Create(glowTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite ShadowSprite()
    {
        const int size = 32;
        if (shadowTexture == null)
        {
            shadowTexture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = y / (size - 1f);
                    pixels[y * size + x] = new Color(1, 1, 1, a * a);
                }
            shadowTexture.SetPixels(pixels);
            shadowTexture.Apply();
        }
        return UnityEngine.Sprite.Create(shadowTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

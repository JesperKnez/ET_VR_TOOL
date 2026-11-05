using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Einstein Telescope > Hall > Place props
/// Fills the hall with the props from Models/ (Clutter, Equipment, Vacuum, Pipe, ETP_CableKit, Ceiling, Props2) so it
/// looks like a working ETpathfinder hall. Everything goes under one object 'Hall_Props', organized per zone:
///
///   North wall (z+)   technical row: shelving, binder cabinet, control cabinets, 19" racks, cable tray with cables, pipes
///   West wall (x-)    racks, control box, fire extinguisher, environment display, cable tray
///   East wall (x+)    air handling unit, shelving with parts, emergency lighting, fire extinguisher
///   South wall (z-)   AED and first aid next to the control room
///   Storage (southeast) spare tubes on blue stands, bellows, domed door, pallet trolley with crates, cones
///   Workplace (middle) optical table with vacuum parts, tool trolley, step ladder, scaffold tower at T4
///   Ceiling           HEPA grilles and swirl diffusers between the light panels
///
/// The script does not touch existing objects (crane, mast lift, towers, control room). It does measure them: every prop
/// on the floor or against a wall is checked for collisions with the towers (the real mesh), the beam tubes,
/// the control room and everything that is already there. If a prop collides, it is skipped and reported in the Console.
///
/// Orientation: wall props get their back against the wall. The back is the side the pivot lies against
/// (wall boxes, brackets); if the pivot is in the middle, the Blender convention applies (front = -Y in
/// Blender = +Z in Unity). If something faces the wrong way, set flip: true on that line.
///
/// Safe to run again: 'Hall_Props' is replaced. Undo with Ctrl+Z. Bake the lighting again afterwards.
/// The props get their light from the light probes (no lightmap UVs needed), but they do count in the bake.
/// </summary>
public static class HallPropsLayout
{
    const string ROOT_NAME = "Hall_Props";
    const string MODELS = "Assets/_Project/Models/";
    const string TOWER_OBJECT = "TowerConstellation";
    const string HALL_OBJECT = "Hall";
    const string CONTROL_ROOM_OBJECT = "ControlRoom";
    const string DEMO_ROOT = "Laser Demo - Michelson Interferometer";   // optics are in the towers, beams are long lines

    // Room.fbx, if 'Hall' is not in the scene
    static readonly Bounds FALLBACK_HALL = new Bounds(new Vector3(0f, 4f, 0f), new Vector3(34.3f, 8f, 22f));

    const float CELL = 0.2f;              // grid size of the collision check
    const float WALL_GAP = 0.02f;         // between the wall and the back of a prop
    const float TRAY_HEIGHT = 3.0f;       // bottom of the cable tray
    const float TRAY_FROM_WALL = 0.17f;   // center of the tray (see ETP_CableKit/README.txt)
    const float TRAY_WIDTH = 0.30f;
    const float PIPE_LOW = 4.3f;          // center of the D110 pipe
    const float PIPE_HIGH = 4.7f;         // center of the D60 pipe
    const float PIPE_D110_FROM_WALL = 0.27f;   // bracket arm length minus radius; check once in the Scene view
    const float PIPE_D60_FROM_WALL = 0.28f;

    // Models
    const string SHELVING = "Clutter/ETP_WireShelving.fbx";
    const string TOOL_TROLLEY = "Clutter/ETP_ToolTrolley.fbx";
    const string STEP_LADDER = "Clutter/ETP_StepLadder.fbx";
    const string SCAFFOLD = "Clutter/ETP_ScaffoldTower.fbx";
    const string CONE = "Clutter/ETP_TrafficCone.fbx";
    const string PALLET_TROLLEY = "Clutter/ETP_PalletTrolley.fbx";
    const string BOX_CLEAR = "Clutter/ETP_StorageBox_Clear.fbx";
    const string BOX_PINK = "Clutter/ETP_StorageBox_Pink.fbx";
    const string EXTINGUISHER_FLOOR = "Clutter/ETP_FireExtinguisher.fbx";
    const string CABINETS = "Equipment/ETP_ControlCabinets_Floor.fbx";
    const string RACK = "Equipment/ETP_Rack19_GlassDoor.fbx";
    const string CONTROL_BOX = "Equipment/ETP_ControlBox_Wall.fbx";
    const string OPTICAL_TABLE = "Equipment/ETP_OpticalTable.fbx";
    const string BEAM_TUBE = "Vacuum/ETP_BeamTube_2m.fbx";
    const string TUBE_STAND = "Vacuum/ETP_TubeStand_Blue.fbx";
    const string BELLOWS = "Vacuum/ETP_Bellows_0.5m.fbx";
    const string DOMED_DOOR = "Vacuum/ETP_DomedDoor.fbx";
    const string TURBO_PUMP = "Vacuum/ETP_TurboPump.fbx";
    const string FLANGE_CF100 = "Vacuum/ETP_BlankFlange_CF100.fbx";
    const string FLANGE_CF160 = "Vacuum/ETP_BlankFlange_CF160.fbx";
    const string FLANGE_CF250 = "Vacuum/ETP_BlankFlange_CF250.fbx";
    const string PORT_CF100 = "Vacuum/ETP_Port_CF100.fbx";
    const string PORT_CF160 = "Vacuum/ETP_Port_CF160.fbx";
    const string VIEWPORT = "Vacuum/ETP_Viewport_CF100.fbx";
    const string FEEDTHROUGH = "Vacuum/ETP_FeedthroughPlate.fbx";
    const string PIPE_D110 = "Pipe/ETP_Pipe_D110_1m.fbx";
    const string PIPE_D60 = "Pipe/ETP_Pipe_D60_1m.fbx";
    const string PIPE_BRACKET_D110 = "Pipe/ETP_PipeWallBracket_D110.fbx";
    const string PIPE_BRACKET_D60 = "Pipe/ETP_PipeWallBracket_D60.fbx";
    const string TRAY = "ETP_CableKit/Tray/Tray_Straight_1m.fbx";
    const string TRAY_BRACKET = "ETP_CableKit/Tray/Tray_WallBracket.fbx";
    const string CABLE_6 = "ETP_CableKit/Bundle6/CableBundle6_Straight_1m.fbx";
    const string CABLE_6_DROP = "ETP_CableKit/Bundle6/CableBundle6_Drop_1m_R0.15.fbx";
    const string CABLE_12 = "ETP_CableKit/Bundle12Thin/CableBundle12Thin_Straight_1m.fbx";
    const string TILE_HEPA = "Ceiling/ETP_CeilingTile_HEPAGrille.fbx";
    const string TILE_SWIRL = "Ceiling/ETP_CeilingTile_SwirlDiffuser.fbx";
    const string AED = "Props2/ET_AED.fbx";
    const string FIRST_AID = "Props2/ET_EHBO.fbx";
    const string EXTINGUISHER_WALL = "Props2/ET_Brandblusser.fbx";
    const string BINDER_CABINET = "Props2/ET_Ringbandenkast.fbx";
    const string ENV_DISPLAY = "Props2/ET_Omgevingsscherm.fbx";
    const string EMERGENCY_LIGHT = "Props2/ET_Noodverlichting.fbx";
    const string VENTILATION = "Props2/ET_Ventilatie.fbx";

    enum Wall { North, East, South, West }

    struct Cell
    {
        public float lo, hi;
        public string owner;
    }

    static Bounds hall;
    static Bounds controlRoom;
    static Transform root, group;
    static readonly Dictionary<Vector2Int, Cell> grid = new Dictionary<Vector2Int, Cell>();
    static readonly List<string> skipped = new List<string>();
    static readonly List<string> moved = new List<string>();
    static int placed, triangles;

    [MenuItem("Tools/Einstein Telescope/Hall/Place props")]
    static void Place()
    {
        var old = GameObject.Find(ROOT_NAME);
        if (old && !EditorUtility.DisplayDialog("Hall props", $"'{ROOT_NAME}' already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (old) Undo.DestroyObjectImmediate(old);

        hall = FindBounds(HALL_OBJECT, FALLBACK_HALL);
        controlRoom = FindBounds(CONTROL_ROOM_OBJECT, new Bounds(new Vector3(-13.15f, 3f, -7f), new Vector3(8f, 6f, 8f)));
        skipped.Clear();
        moved.Clear();
        placed = triangles = 0;

        root = new GameObject(ROOT_NAME).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place hall props");
        BuildGrid();

        try
        {
            EditorUtility.DisplayProgressBar("Hall props", "North wall", 0.1f); NorthWall();
            EditorUtility.DisplayProgressBar("Hall props", "West wall", 0.3f); WestWall();
            EditorUtility.DisplayProgressBar("Hall props", "East wall", 0.45f); EastWall();
            EditorUtility.DisplayProgressBar("Hall props", "South wall", 0.55f); SouthWall();
            EditorUtility.DisplayProgressBar("Hall props", "Storage", 0.65f); Storage();
            EditorUtility.DisplayProgressBar("Hall props", "Workplace", 0.8f); Workplace();
            EditorUtility.DisplayProgressBar("Hall props", "Ceiling", 0.9f); CeilingTiles();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Selection.activeGameObject = root.gameObject;

        var report = new StringBuilder($"[HallPropsLayout] Placed {placed} props under '{ROOT_NAME}', {triangles / 1000f:0.0}k tris in total.\n");
        report.AppendLine($"  Hall: x {hall.min.x:0.00}..{hall.max.x:0.00}, z {hall.min.z:0.00}..{hall.max.z:0.00}, floor {hall.min.y:0.00}, ceiling {hall.max.y:0.00}");
        if (moved.Count > 0)
        {
            report.AppendLine($"  {moved.Count} moved slightly to avoid a collision:");
            foreach (var s in moved) report.AppendLine("    " + s);
        }
        if (skipped.Count > 0)
        {
            report.AppendLine($"  {skipped.Count} skipped:");
            foreach (var s in skipped) report.AppendLine("    " + s);
        }
        report.AppendLine("  Bake the lighting again afterwards (Window > Rendering > Lighting > Generate Lighting).");
        Debug.Log(report.ToString(), root.gameObject);
    }

    [MenuItem("Tools/Einstein Telescope/Hall/Remove props")]
    static void Remove()
    {
        var old = GameObject.Find(ROOT_NAME);
        if (!old) { Debug.Log($"[HallPropsLayout] No '{ROOT_NAME}' in the scene."); return; }
        Undo.DestroyObjectImmediate(old);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    // ---------------------------------------------------------------- zones

    static void NorthWall()
    {
        Group("North Wall - Technical");
        var shelfA = AgainstWall(SHELVING, Wall.North, -7.0f);
        var shelfB = AgainstWall(SHELVING, Wall.North, -5.6f);
        AgainstWall(BINDER_CABINET, Wall.North, -4.2f);
        AgainstWall(CABINETS, Wall.North, -2.3f);
        AgainstWall(CABINETS, Wall.North, -0.15f);
        foreach (float x in new[] { 1.6f, 2.25f, 2.9f }) AgainstWall(RACK, Wall.North, x);
        AgainstWall(CONTROL_BOX, Wall.North, 4.0f, bottom: 1.0f);
        AgainstWall(EXTINGUISHER_WALL, Wall.North, 5.0f);
        AgainstWall(FIRST_AID, Wall.North, 5.7f);

        StockShelf(shelfA, Wall.North, BOX_CLEAR, BOX_PINK, FLANGE_CF160);
        StockShelf(shelfB, Wall.North, BOX_PINK, PORT_CF160, BOX_CLEAR);

        // Cable tray above the row, from the corner with the west wall to past the racks, with cables dropping into the cabinets
        CableTray(Wall.North, hall.min.x, 4.6f);
        foreach (float x in new[] { -2.3f, -0.15f, 2.25f }) CableDrop(Wall.North, x);

        // Two pipes from wall to wall
        PipeRun(Wall.North, hall.min.x + 0.1f, hall.max.x - 0.1f, PIPE_D110, PIPE_BRACKET_D110, PIPE_LOW, PIPE_D110_FROM_WALL);
        PipeRun(Wall.North, hall.min.x + 0.1f, hall.max.x - 0.1f, PIPE_D60, PIPE_BRACKET_D60, PIPE_HIGH, PIPE_D60_FROM_WALL);
    }

    static void WestWall()
    {
        Group("West Wall");
        AgainstWall(ENV_DISPLAY, Wall.West, 0.2f);
        AgainstWall(EXTINGUISHER_WALL, Wall.West, 5.0f);
        AgainstWall(CONTROL_BOX, Wall.West, 6.4f, bottom: 1.0f);
        AgainstWall(RACK, Wall.West, 7.8f);
        AgainstWall(RACK, Wall.West, 8.45f);
        CableTray(Wall.West, controlRoom.max.z + 0.3f, hall.max.z);
    }

    static void EastWall()
    {
        Group("East Wall");
        AgainstWall(VENTILATION, Wall.East, 1.5f);
        AgainstWall(EMERGENCY_LIGHT, Wall.East, -3.5f, bottom: 2.3f);
        AgainstWall(EXTINGUISHER_FLOOR, Wall.East, -5.0f);
        var shelfA = AgainstWall(SHELVING, Wall.East, -7.0f);
        var shelfB = AgainstWall(SHELVING, Wall.East, -8.4f);
        StockShelf(shelfA, Wall.East, TURBO_PUMP, FLANGE_CF250, BOX_CLEAR);
        StockShelf(shelfB, Wall.East, BOX_PINK, FLANGE_CF100, TURBO_PUMP);
    }

    static void SouthWall()
    {
        Group("South Wall");
        float x = controlRoom.max.x + 1.2f;   // next to the control room
        AgainstWall(AED, Wall.South, x);
        AgainstWall(FIRST_AID, Wall.South, x + 0.8f);
        AgainstWall(EXTINGUISHER_WALL, Wall.South, x + 1.6f);
    }

    // Spare parts in the southeast corner, outside the Y arm (T2 -> T4 -> T5)
    static void Storage()
    {
        Group("Storage - Spare Parts");
        foreach (float z in new[] { -8.3f, -6.5f })
        {
            var a = Floor(TUBE_STAND, 9.4f, z, 90f);
            var b = Floor(TUBE_STAND, 10.8f, z, 90f);
            if (!a || !b) continue;
            float top = Mathf.Min(WorldBounds(a).max.y, WorldBounds(b).max.y) - 0.08f;   // slightly into the saddles
            var tube = Run(BEAM_TUBE, new Vector3(9.1f, 1.5f, z), new Vector3(11.1f, 1.5f, z));
            if (tube) SetBottom(tube, top);
        }
        Floor(BELLOWS, 12.6f, -8.4f, 0f);
        Floor(DOMED_DOOR, 12.8f, -6.4f, 15f);

        var trolley = Floor(PALLET_TROLLEY, 8.0f, -9.9f, 90f);
        if (trolley)
        {
            var box = OnSurface(BOX_CLEAR, trolley, WorldBounds(trolley).center, yaw: 90f);
            if (box) OnSurface(BOX_PINK, box, WorldBounds(box).center, yaw: 95f);
        }

        foreach (float x in new[] { 8.2f, 11.0f, 13.8f }) Floor(CONE, x, -4.9f);
    }

    // Workplace in the open area between the arms, north of T2
    static void Workplace()
    {
        Group("Workplace");
        var table = Floor(OPTICAL_TABLE, -0.5f, 2.0f, 0f);
        if (table)
        {
            Vector3 c = WorldBounds(table).center;
            OnSurface(PORT_CF160, table, c + new Vector3(-0.2f, 0f, 0.1f), yaw: 20f);
            OnSurface(VIEWPORT, table, c + new Vector3(0.15f, 0f, 0.15f));
            OnSurface(FLANGE_CF100, table, c + new Vector3(0.25f, 0f, -0.15f));
            OnSurface(FEEDTHROUGH, table, c + new Vector3(-0.15f, 0f, -0.18f), yaw: -10f);
        }
        Floor(TOOL_TROLLEY, 0.9f, 1.3f, -30f);
        Floor(STEP_LADDER, -1.9f, 2.7f, 20f);
        Floor(BOX_PINK, -1.3f, 0.9f, 10f);

        // Scaffold tower at middle tower T4, as if maintenance is going on there
        Floor(SCAFFOLD, 3.0f, 1.6f, 45f);
        Floor(CONE, 1.9f, 3.0f);
        Floor(CONE, 4.3f, 2.9f);
    }

    // HEPA grilles and swirl diffusers alternating, centered between the light panels (2.4 m grid, see CreatePanelLights)
    static void CeilingTiles()
    {
        Group("Ceiling Tiles");
        for (int i = 0; i < 13; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                if ((i + j) % 2 != 0) continue;
                float x = -14.4f + i * 2.4f, z = -8.4f + j * 2.4f;
                if (x < controlRoom.max.x + 0.6f && z < controlRoom.max.z + 0.6f) continue;   // above the control room
                Ceiling((i / 2) % 2 == 0 ? TILE_HEPA : TILE_SWIRL, x, z);
            }
        }
    }

    // ---------------------------------------------------------------- composite parts

    // Top shelf and the two below it: left, middle, right (seen from the hall)
    static void StockShelf(GameObject shelf, Wall wall, string top, string middle, string low)
    {
        if (!shelf) return;
        Vector3 c = WorldBounds(shelf).center;
        Vector3 along = Along(wall);
        OnSurface(top, shelf, c - along * 0.25f, level: 0);
        OnSurface(top, shelf, c + along * 0.25f, level: 0);
        OnSurface(middle, shelf, c, level: 1);
        OnSurface(low, shelf, c + along * 0.2f, level: 2);
    }

    // Tray along a wall in pieces of 1 m, with a bracket every 1.5 m and two cable bundles in it
    static void CableTray(Wall wall, float from, float to)
    {
        Vector3 a = OnWall(wall, from, TRAY_HEIGHT, TRAY_FROM_WALL);
        Vector3 b = OnWall(wall, to, TRAY_HEIGHT, TRAY_FROM_WALL);
        Run(TRAY, a, b, tiled: true, up: Vector3.up, floorAligned: true);

        int brackets = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(to - from) / 1.5f) + 1);
        for (int i = 0; i < brackets; i++)
        {
            float t = Mathf.Lerp(from, to, (i + 0.5f) / brackets);
            AgainstWall(TRAY_BRACKET, wall, t, pivotY: TRAY_HEIGHT, check: false);
        }

        Vector3 side = -Normal(wall) * (TRAY_WIDTH * 0.2f);
        Vector3 lift = Vector3.up * 0.015f;
        Run(CABLE_6, a + side + lift, b + side + lift);
        Run(CABLE_12, a - side + lift, b - side + lift);
    }

    // Bundle that goes down over the edge of the tray into the cabinet (end point 1 m lower, 0.15 m further into the hall)
    static void CableDrop(Wall wall, float along)
    {
        var go = Spawn(CABLE_6_DROP);
        if (!go) return;
        Vector3 edge = OnWall(wall, along, TRAY_HEIGHT + 0.015f, TRAY_FROM_WALL + TRAY_WIDTH * 0.5f);
        go.transform.SetPositionAndRotation(edge, Quaternion.LookRotation(-Normal(wall), Vector3.up));
        Commit(go, check: false);
    }

    static void PipeRun(Wall wall, float from, float to, string pipe, string bracket, float height, float fromWall)
    {
        Run(pipe, OnWall(wall, from, height, fromWall), OnWall(wall, to, height, fromWall));
        int count = Mathf.RoundToInt(Mathf.Abs(to - from) / 2f);
        for (int i = 0; i < count; i++)
            AgainstWall(bracket, wall, Mathf.Lerp(from, to, (i + 0.5f) / count), pivotY: height, check: false);
    }

    // ---------------------------------------------------------------- placing

    /// <summary>On the floor, centered on (x, z), rotated around the vertical axis.</summary>
    static GameObject Floor(string model, float x, float z, float yaw = 0f)
    {
        var go = Spawn(model);
        if (!go) return null;
        Yaw(go, yaw);
        var b = WorldBounds(go);
        go.transform.position += new Vector3(x - b.center.x, hall.min.y - b.min.y, z - b.center.z);
        return Commit(go, true, FloorNudges());
    }

    // Alternative spots: rings from 25 cm to 1 m, in 8 directions
    static IEnumerable<Vector3> FloorNudges()
    {
        for (float r = 0.25f; r <= 1.01f; r += 0.25f)
            for (int a = 0; a < 8; a++)
                yield return Quaternion.Euler(0f, a * 45f, 0f) * new Vector3(0f, 0f, r);
    }

    static IEnumerable<Vector3> WallNudges(Wall wall)
    {
        for (float r = 0.25f; r <= 1.01f; r += 0.25f)
        {
            yield return Along(wall) * r;
            yield return Along(wall) * -r;
        }
    }

    /// <summary>
    /// With the back against a wall. 'along' is x for north/south and z for east/west.
    /// Height: bottom = bottom at that height; pivotY = pivot at that height; neither = on the floor,
    /// or at the design height if the model floats above its pivot (AED, fire extinguisher with sign).
    /// </summary>
    static GameObject AgainstWall(string model, Wall wall, float along, float? bottom = null, float? pivotY = null,
                                  bool flip = false, bool check = true)
    {
        var go = Spawn(model);
        if (!go) return null;
        Vector3 back = BackDirection(go);
        if (flip) back = -back;
        Vector3 n = Normal(wall);
        Yaw(go, Vector3.SignedAngle(back, n, Vector3.up));

        var b = WorldBounds(go);   // pivot is still at the origin
        Vector3 face = b.center + Vector3.Scale(b.extents, n);
        Vector3 shift = OnWall(wall, along, 0f, WALL_GAP) - face;
        if (pivotY.HasValue) shift.y = pivotY.Value;
        else if (bottom.HasValue) shift.y = bottom.Value - b.min.y;
        else shift.y = b.min.y > 0.3f ? hall.min.y : hall.min.y - b.min.y;
        go.transform.position += shift;
        return Commit(go, check, WallNudges(wall));
    }

    /// <summary>
    /// A straight piece (pipe, tray, cable) from 'from' to 'to'. The long axis of the model is laid in that direction
    /// and stretched; with tiled: true separate pieces of roughly the model length are placed one after another
    /// (for trays with holes that look odd when stretched). floorAligned: the bottom lies on the line instead of the center.
    /// </summary>
    static GameObject Run(string model, Vector3 from, Vector3 to, bool tiled = false, Vector3? up = null,
                          bool floorAligned = false)
    {
        if (tiled)
        {
            var probe = Spawn(model);
            if (!probe) return null;
            Vector3 s = WorldBounds(probe).size;
            Object.DestroyImmediate(probe);
            float pieceLength = Mathf.Max(s.x, s.y, s.z);
            int pieces = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / pieceLength - 0.05f));
            GameObject first = null;
            for (int i = 0; i < pieces; i++)
            {
                var piece = Run(model, Vector3.Lerp(from, to, i / (float)pieces), Vector3.Lerp(from, to, (i + 1f) / pieces),
                                false, up, floorAligned);
                if (!first) first = piece;
            }
            return first;
        }

        var go = Spawn(model);
        if (!go) return null;
        var b = WorldBounds(go);   // only the import rotation (multiples of 90°), so the box is axis-aligned
        Vector3 size = b.size;
        Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        Vector3 modelUp = axis == Vector3.up ? Vector3.forward : Vector3.up;
        float extent = Vector3.Dot(size, axis);

        Vector3 dir = (to - from).normalized;
        Vector3 worldUp = up ?? (Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
        Quaternion turn = Quaternion.LookRotation(dir, worldUp) * Quaternion.Inverse(Quaternion.LookRotation(axis, modelUp));

        // Stretch along the long axis, in the model's local axes
        float k = (to - from).magnitude / extent;
        Vector3 localAxis = Quaternion.Inverse(go.transform.rotation) * axis;
        localAxis = new Vector3(Mathf.Round(Mathf.Abs(localAxis.x)), Mathf.Round(Mathf.Abs(localAxis.y)), Mathf.Round(Mathf.Abs(localAxis.z)));
        go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one + localAxis * (k - 1f));

        // Start point: middle of the start face (or its bottom edge), after stretching
        Vector3 start = b.center - axis * (extent * 0.5f);
        if (floorAligned) start -= modelUp * (Vector3.Dot(b.size, modelUp) * 0.5f);
        start += axis * (Vector3.Dot(start, axis) * (k - 1f));

        go.transform.rotation = turn * go.transform.rotation;
        go.transform.position = from - turn * start;
        return Commit(go, check: false);
    }

    /// <summary>On a surface of a previously placed prop (0 = top, 1 = the shelf below it, ...).</summary>
    static GameObject OnSurface(string model, GameObject support, Vector3 at, int level = 0, float yaw = 0f)
    {
        if (!support) return null;
        var heights = SurfaceHeights(support, at);
        if (level >= heights.Count)
        {
            skipped.Add($"{System.IO.Path.GetFileNameWithoutExtension(model)}: no surface {level} on {support.name}");
            return null;
        }
        var go = Spawn(model);
        if (!go) return null;
        Yaw(go, yaw);
        var b = WorldBounds(go);
        go.transform.position += new Vector3(at.x - b.center.x, heights[level] + 0.002f - b.min.y, at.z - b.center.z);
        return Commit(go, check: false);
    }

    /// <summary>Against the underside of the ceiling.</summary>
    static GameObject Ceiling(string model, float x, float z)
    {
        var go = Spawn(model);
        if (!go) return null;
        var b = WorldBounds(go);
        go.transform.position += new Vector3(x - b.center.x, hall.max.y - 0.002f - b.max.y, z - b.center.z);
        return Commit(go, check: false);
    }

    // ---------------------------------------------------------------- helpers

    static void Group(string name)
    {
        group = new GameObject(name).transform;
        group.SetParent(root, false);
    }

    static GameObject Spawn(string model)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MODELS + model);
        if (!asset)
        {
            skipped.Add($"{model}: model not found");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, group);
        go.transform.position = Vector3.zero;   // the import rotation is kept
        return go;
    }

    /// <summary>
    /// Checks for collisions (first at the intended spot, then at the alternative spots), registers the prop in the grid,
    /// makes it static and counts its triangles. If it fits nowhere, it is removed and reported.
    /// </summary>
    static GameObject Commit(GameObject go, bool check = true, IEnumerable<Vector3> nudges = null)
    {
        if (check)
        {
            var b = WorldBounds(go);
            bool free = !Blocked(b, out string by);
            if (!free && nudges != null)
            {
                foreach (var offset in nudges)
                {
                    var tryBounds = new Bounds(b.center + offset, b.size);
                    if (Blocked(tryBounds, out _)) continue;
                    go.transform.position += offset;
                    moved.Add($"{go.name}: moved {offset.magnitude:0.00} m (collided with '{by}')");
                    free = true;
                    break;
                }
            }
            if (!free)
            {
                skipped.Add($"{go.name} at ({b.center.x:0.0}, {b.center.z:0.0}): collides with '{by}'");
                Object.DestroyImmediate(go);
                return null;
            }
            Occupy(WorldBounds(go), go.name);
        }

        const StaticEditorFlags flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.receiveGI = ReceiveGI.LightProbes;   // no lightmap UVs needed
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) triangles += (int)mf.sharedMesh.GetIndexCount(s) / 3;
        }
        placed++;
        return go;
    }

    static void Yaw(GameObject go, float yaw) => go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;

    static void SetBottom(GameObject go, float y) => go.transform.position += Vector3.up * (y - WorldBounds(go).min.y);

    static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static Bounds FindBounds(string name, Bounds fallback)
    {
        var go = GameObject.Find(name);
        var r = go ? go.GetComponent<Renderer>() : null;
        if (r) return r.bounds;
        Debug.LogWarning($"[HallPropsLayout] '{name}' not found, using the dimensions from Room.fbx.");
        return fallback;
    }

    /// <summary>
    /// World direction (at yaw 0) of the back: the side the pivot lies against (wall boxes, brackets).
    /// If the pivot is roughly in the middle, the Blender convention applies: front = +Z, so back = -Z.
    /// </summary>
    static Vector3 BackDirection(GameObject go)
    {
        var b = WorldBounds(go);
        Vector3 pivot = go.transform.position;
        Vector3 best = Vector3.back;
        float bestScore = 0.2f;
        foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            float extent = Mathf.Abs(Vector3.Dot(b.size, d));
            if (extent < 0.01f) continue;
            float toFace = Vector3.Dot(b.center + Vector3.Scale(b.extents, d) - pivot, d);
            float score = Mathf.Abs(toFace) / extent;
            if (score < bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    static Vector3 Normal(Wall w)
    {
        switch (w)
        {
            case Wall.North: return Vector3.forward;
            case Wall.South: return Vector3.back;
            case Wall.East: return Vector3.right;
            default: return Vector3.left;
        }
    }

    static Vector3 Along(Wall w) => w == Wall.North || w == Wall.South ? Vector3.right : Vector3.forward;

    /// <summary>Point 'offset' meters from a wall, into the hall.</summary>
    static Vector3 OnWall(Wall w, float along, float y, float offset)
    {
        switch (w)
        {
            case Wall.North: return new Vector3(along, y, hall.max.z - offset);
            case Wall.South: return new Vector3(along, y, hall.min.z + offset);
            case Wall.East: return new Vector3(hall.max.x - offset, y, along);
            default: return new Vector3(hall.min.x + offset, y, along);
        }
    }

    // ---------------------------------------------------------------- collision check

    /// <summary>
    /// 20 cm grid with the lowest and highest occupied height per cell. The towers are built up from their triangles
    /// (the bounding box of TowerConstellation is the whole hall); everything else as a bounding box.
    /// Objects larger than 12 m (hall, crane) do not count.
    /// </summary>
    static void BuildGrid()
    {
        grid.Clear();
        var tower = GameObject.Find(TOWER_OBJECT);
        var demo = GameObject.Find(DEMO_ROOT);
        foreach (var r in Object.FindObjectsByType<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (demo && r.transform.IsChildOf(demo.transform)) continue;
            if (tower && r.transform.IsChildOf(tower.transform))
            {
                SampleTriangles(r);
                continue;
            }
            Bounds b = r.bounds;
            if (b.size.x > 12f || b.size.z > 12f) continue;
            if (b.max.y < hall.min.y - 0.5f || b.min.y > hall.max.y) continue;   // e.g. the UI camera at y = -100
            Occupy(b, r.name);
        }
        if (!tower) Debug.LogWarning($"[HallPropsLayout] '{TOWER_OBJECT}' not found: no collision check against the towers.");
    }

    static void SampleTriangles(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return;
        var mesh = mf.sharedMesh;
        var m = r.transform.localToWorldMatrix;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] = m.MultiplyPoint3x4(verts[i]);

        var tris = mesh.triangles;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
            float edge = Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude);
            int n = Mathf.Max(1, Mathf.CeilToInt(edge / (CELL * 0.75f)));
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                    OccupyPoint(a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n), r.name);
        }
    }

    static Vector2Int Key(float x, float z) => new Vector2Int(Mathf.FloorToInt(x / CELL), Mathf.FloorToInt(z / CELL));

    static void OccupyPoint(Vector3 p, string owner) => Mark(Key(p.x, p.z), p.y, p.y, owner);

    static void Occupy(Bounds b, string owner)
    {
        Vector2Int lo = Key(b.min.x, b.min.z), hi = Key(b.max.x, b.max.z);
        for (int x = lo.x; x <= hi.x; x++)
            for (int z = lo.y; z <= hi.y; z++)
                Mark(new Vector2Int(x, z), b.min.y, b.max.y, owner);
    }

    static void Mark(Vector2Int key, float lo, float hi, string owner)
    {
        if (grid.TryGetValue(key, out var c)) { c.lo = Mathf.Min(c.lo, lo); c.hi = Mathf.Max(c.hi, hi); }
        else c = new Cell { lo = lo, hi = hi, owner = owner };
        grid[key] = c;
    }

    static bool Blocked(Bounds b, out string by)
    {
        const float shrink = 0.05f;   // edges may touch
        Vector2Int lo = Key(b.min.x + shrink, b.min.z + shrink), hi = Key(b.max.x - shrink, b.max.z - shrink);
        for (int x = lo.x; x <= hi.x; x++)
        {
            for (int z = lo.y; z <= hi.y; z++)
            {
                if (grid.TryGetValue(new Vector2Int(x, z), out var c) && c.hi > b.min.y + 0.02f && c.lo < b.max.y)
                {
                    by = c.owner;
                    return true;
                }
            }
        }
        by = null;
        return false;
    }

    // ---------------------------------------------------------------- surfaces

    /// <summary>
    /// Heights of the surfaces of 'support' below point (x, z), from top to bottom. Temporarily adds
    /// MeshColliders to the object and probes further and further down with a small sphere (also catches wire grids).
    /// </summary>
    static List<float> SurfaceHeights(GameObject support, Vector3 at)
    {
        const float radius = 0.025f;
        var added = new List<MeshCollider>();
        foreach (var mf in support.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = mf.sharedMesh;
            added.Add(col);
        }
        Physics.SyncTransforms();

        var heights = new List<float>();
        var b = WorldBounds(support);
        float y = b.max.y + 0.5f;
        while (heights.Count < 8)
        {
            float best = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(new Vector3(at.x, y, at.z), radius, Vector3.down, y - b.min.y + 0.1f))
                if (hit.collider is MeshCollider mc && added.Contains(mc) && hit.distance > 0f) best = Mathf.Min(best, hit.point.y);
            if (best == float.MaxValue) break;
            heights.Add(best);
            y = best - 0.06f;   // continue searching just below this shelf
        }

        foreach (var col in added) Object.DestroyImmediate(col);
        return heights;
    }
}

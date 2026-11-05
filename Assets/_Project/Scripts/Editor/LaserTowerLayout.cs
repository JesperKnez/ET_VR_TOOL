using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Laser > Place levels in the towers
/// Moves the EXISTING optics of the three levels (Carlo's test setup) into the towers of ETpathfinder, with the
/// laser running through the beam tubes. All links (curved monitor, level manager, cameras) stay intact.
///
///   T3 injection tower   laser; in level 2/3 also the periscope of two steering mirrors
///   T2 beam splitter     beam splitter + output detector
///   T1 / T4              middle towers: end mirrors in level 1/2, irises in level 3
///   T0 / T5              end towers: end mirrors in level 3 ("Long arms", replaces "Folded arms")
///
/// The arms become 2.5-6x as long as in the test setup. To keep the difficulty the same, step size, range,
/// start misalignment and seismic noise are scaled per mirror by (design lever / new lever): every step then moves
/// the laser spot as far as before. Running it again is safe (it starts from Carlo's design values, not from what is
/// in the scene now). Undo with Ctrl+Z.
///
/// Tools > Einstein Telescope > Laser > Set star par times
/// Sets the 3-star time limit per level (LaserLevel.parTimeSeconds). Also done by "Place levels in the towers".
/// </summary>
public static class LaserTowerLayout
{
    const string DEMO_ROOT = "Laser Demo - Michelson Interferometer";
    const string TOWER_OBJECT = "TowerConstellation";

    // Tower centres (x, z), measured with Towers > Measure tower layout on 2 Nov 2026
    static readonly Vector2 INJECTION = new Vector2(2.600f, -7.700f);    // T3
    static readonly Vector2 SPLITTER = new Vector2(0.000f, -5.100f);     // T2
    static readonly Vector2 MID_X = new Vector2(-4.525f, -0.575f);       // T1, straight arm
    static readonly Vector2 END_X = new Vector2(-11.172f, 6.072f);       // T0
    static readonly Vector2 MID_Y = new Vector2(4.525f, -0.575f);        // T4, reflected arm
    static readonly Vector2 END_Y = new Vector2(11.172f, 6.072f);        // T5
    const float BEAM_HEIGHT = 1.33f;                                      // centre line of the beam tubes

    // Optics: same values as in LaserSetupMenu
    const float SPLITTER_THICKNESS = 0.06f;
    const float SPLITTER_INDEX = 1.45f;
    const float STEERING_THICKNESS = 0.07f;

    // Positions in the towers
    const float DETECTOR_DISTANCE = 0.9f;       // from the centre of T2 (the tank is 2.54 m across)
    const float PERISCOPE_OFFSET = 0.6f;        // steering mirror 1 beside the beam line, in T3
    const float LASER_BEFORE_MIRROR1 = 0.7f;
    const float IRIS_A_BEFORE_SPLITTER = 2.4f;  // in the tube between T3 and T2
    const float IRIS_B_BEFORE_SPLITTER = 1.45f; // just inside T2

    // Carlo's design levers (test setup), for the scale factor
    const float DESIGN_END_SHORT = 2.5f + 1.2f;          // ArmLength + DetectorDistance
    const float DESIGN_END_X_FOLDED = 1.8f + 2.6f + 1.2f;
    const float DESIGN_END_Y_FOLDED = 2f * 1.8f + 1.2f;
    const float DESIGN_MIRROR1 = 2.0f;
    const float DESIGN_MIRROR2 = 1.5f;

    // 3-star time limits (seconds) per level; 2 stars = twice this. Level 1 and 2 match their briefing texts.
    static readonly float[] PAR_TIMES = { 120f, 120f, 240f };

    struct Geometry
    {
        public Vector3 splitter;     // centre of the beam splitter
        public Vector3 dirIn;        // incoming beam = straight arm (X)
        public Vector3 dirY;         // reflected arm (Y)
        public Vector3 coating;      // normal of the coating, towards the laser
        public Vector3 entry;        // where the beam hits the coating: start of the reflected arm (Y)
        public Vector3 xLineStart;   // transmitted beam after the glass plate
        public Vector3 outLineStart; // output beam after the glass plate
    }

    static readonly System.Text.StringBuilder s_Log = new System.Text.StringBuilder();

    [MenuItem("Tools/Einstein Telescope/Laser/Place levels in the towers", priority = 30)]
    static void Place()
    {
        var root = GameObject.Find(DEMO_ROOT);
        if (!root) { Debug.LogError($"[LaserTowerLayout] '{DEMO_ROOT}' not found."); return; }
        if (!EditorUtility.DisplayDialog("Levels in the towers",
                "The optics of all three levels are moved into the towers and the mirrors get finer steps. " +
                "Level 3 becomes 'Long arms'. Undo with Ctrl+Z.", "Place", "Cancel"))
            return;

        Undo.SetCurrentGroupName("Place levels in the towers");
        int group = Undo.GetCurrentGroup();
        s_Log.Clear().AppendLine("[LaserTowerLayout] Levels placed in the towers");

        Geometry g = ComputeGeometry();
        Transform level1 = root.transform.Find("Level 1 - Michelson");
        Transform level2 = root.transform.Find("Level 2 - Beam Walking");
        Transform level3 = root.transform.Find("Level 3 - Folded Arms");

        if (level1) PlaceLevel1(level1, g);
        if (level2) PlaceLevel2(level2, g);
        if (level3) PlaceLevel3(level3, g);

        // The lab box of the test setup does not belong in the hall
        foreach (string box in new[] { "Lab Floor", "Lab Wall (back)", "Lab Wall (left)", "Lab Wall (right)" })
            SetActive(root.transform.Find(box), false);

        ShowTowersClearInAllLevels();
        ApplyParTimes(root);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(s_Log.ToString());
    }

    // ------------------------------------------------------------------ geometry

    static Vector3 At(Vector2 xz) => new Vector3(xz.x, BEAM_HEIGHT, xz.y);

    static Geometry ComputeGeometry()
    {
        var g = new Geometry();
        g.splitter = At(SPLITTER);
        g.dirIn = (At(SPLITTER) - At(INJECTION)).normalized;
        g.dirY = (At(MID_Y) - At(SPLITTER)).normalized;
        g.coating = (g.dirY - g.dirIn).normalized;   // mirrors dirIn exactly onto dirY

        // Where the incoming beam (through the centre) hits the coated face
        float s = SPLITTER_THICKNESS * 0.5f / Vector3.Dot(g.dirIn, g.coating);
        g.entry = g.splitter + g.dirIn * s;

        // The glass plate shifts transmitted and returning beams sideways (Snell), just like in LaserSetupMenu
        g.xLineStart = LaserOptics.PlateExitPoint(g.entry, g.dirIn, g.coating, SPLITTER_THICKNESS, SPLITTER_INDEX);
        g.outLineStart = LaserOptics.PlateExitPoint(g.entry, -g.dirY, g.coating, SPLITTER_THICKNESS, SPLITTER_INDEX);
        return g;
    }

    /// <summary>Point on the beam line closest to the centre of a tower.</summary>
    static Vector3 OnLine(Vector3 start, Vector3 dir, Vector2 tower) => start + dir * Vector3.Dot(At(tower) - start, dir);

    // ------------------------------------------------------------------ levels

    static void PlaceLevel1(Transform level, Geometry g)
    {
        PlaceSplitterAndDetector(level, g);
        Put(level.Find("Laser (532 nm alignment laser)"), At(INJECTION), g.dirIn);
        float lever = PlaceShortArms(level, g);
        ScaleMount(level.Find("End Test Mass X"), 2.5f, 0.6f, 0.003f, DESIGN_END_SHORT, lever);
        ScaleMount(level.Find("End Test Mass Y"), -1.5f, -1.0f, 0.003f, DESIGN_END_SHORT, lever);
        s_Log.AppendLine($"  Level 1: arms {lever - DETECTOR_DISTANCE:0.00} m (was 2.5 m)");
    }

    static void PlaceLevel2(Transform level, Geometry g)
    {
        PlaceSplitterAndDetector(level, g);
        var (lever1, lever2) = PlaceInputPath(level, g);
        float lever = PlaceShortArms(level, g);
        ScaleMount(level.Find("Steering Mirror 1"), 0.4f, -0.25f, 0.002f, DESIGN_MIRROR1, lever1);
        ScaleMount(level.Find("Steering Mirror 2"), -0.3f, 0.2f, 0.002f, DESIGN_MIRROR2, lever2);
        ScaleMount(level.Find("End Test Mass X"), 1.5f, 0.5f, 0.003f, DESIGN_END_SHORT, lever);
        ScaleMount(level.Find("End Test Mass Y"), -1.0f, -0.6f, 0.003f, DESIGN_END_SHORT, lever);
        s_Log.AppendLine($"  Level 2: arms {lever - DETECTOR_DISTANCE:0.00} m, periscope in the injection tower");
    }

    static void PlaceLevel3(Transform level, Geometry g)
    {
        PlaceSplitterAndDetector(level, g);
        var (lever1, lever2) = PlaceInputPath(level, g);

        // End mirrors in the end towers, irises in the middle towers
        Vector3 endX = OnLine(g.xLineStart, g.dirIn, END_X);
        Vector3 endY = OnLine(g.entry, g.dirY, END_Y);
        Put(level.Find("End Test Mass X"), endX, -g.dirIn);
        Put(level.Find("End Test Mass Y"), endY, -g.dirY);
        Put(level.Find("Iris X"), OnLine(g.xLineStart, g.dirIn, MID_X), -g.dirIn);
        Put(level.Find("Iris Y"), OnLine(g.entry, g.dirY, MID_Y), -g.dirY);

        // Fold mirrors do not fit in straight arms
        SetActive(level.Find("Fold Mirror X"), false);
        SetActive(level.Find("Fold Mirror Y"), false);

        float leverX = (endX - g.splitter).magnitude + DETECTOR_DISTANCE;
        float leverY = (endY - g.splitter).magnitude + DETECTOR_DISTANCE;
        ScaleMount(level.Find("Steering Mirror 1"), -0.35f, 0.2f, 0.002f, DESIGN_MIRROR1, lever1);
        ScaleMount(level.Find("Steering Mirror 2"), 0.3f, -0.25f, 0.002f, DESIGN_MIRROR2, lever2);
        ScaleMount(level.Find("End Test Mass X"), 1.0f, -0.5f, 0.003f, DESIGN_END_X_FOLDED, leverX);
        ScaleMount(level.Find("End Test Mass Y"), -1.2f, 0.5f, 0.003f, DESIGN_END_Y_FOLDED, leverY);

        RewriteLevel3(level);
        s_Log.AppendLine($"  Level 3 -> Long arms: arms {leverX - DETECTOR_DISTANCE:0.00} m, irises in the middle towers, fold mirrors off");
    }

    static void PlaceSplitterAndDetector(Transform level, Geometry g)
    {
        Put(level.Find("Beam Splitter"), g.splitter, g.coating);
        Vector3 outDir = -g.dirY;
        Vector3 detector = g.outLineStart + outDir * Vector3.Dot(g.splitter + outDir * DETECTOR_DISTANCE - g.outLineStart, outDir);
        Put(level.Find("Output Port Detector"), detector, g.dirY);   // faces the beam splitter
    }

    /// <summary>End mirrors in the middle towers (level 1 and 2). Returns the lever (mirror -> detector).</summary>
    static float PlaceShortArms(Transform level, Geometry g)
    {
        Vector3 endX = OnLine(g.xLineStart, g.dirIn, MID_X);
        Vector3 endY = OnLine(g.entry, g.dirY, MID_Y);
        Put(level.Find("End Test Mass X"), endX, -g.dirIn);
        Put(level.Find("End Test Mass Y"), endY, -g.dirY);
        return (endX - g.splitter).magnitude + DETECTOR_DISTANCE;
    }

    /// <summary>
    /// Laser + periscope in the injection tower, irises on the line to the beam splitter.
    /// Returns the levers of steering mirror 1 (to iris A) and 2 (to iris B).
    /// </summary>
    static (float, float) PlaceInputPath(Transform level, Geometry g)
    {
        Vector3 side = -g.dirY;   // side of the periscope, away from the reflected arm
        Vector3 mirror2 = At(INJECTION);                           // on the beam line
        Vector3 mirror1 = mirror2 + side * PERISCOPE_OFFSET;
        Vector3 irisA = g.splitter - g.dirIn * IRIS_A_BEFORE_SPLITTER;
        Vector3 irisB = g.splitter - g.dirIn * IRIS_B_BEFORE_SPLITTER;

        Put(level.Find("Laser (532 nm alignment laser)"), mirror1 - g.dirIn * LASER_BEFORE_MIRROR1, g.dirIn);
        // 45° mirror: normal = outgoing - incoming direction
        PutOptic(level.Find("Steering Mirror 1"), mirror1, (-side - g.dirIn).normalized, STEERING_THICKNESS);
        PutOptic(level.Find("Steering Mirror 2"), mirror2, (g.dirIn + side).normalized, STEERING_THICKNESS);
        Put(level.Find("Iris A"), irisA, -g.dirIn);
        Put(level.Find("Iris B"), irisB, -g.dirIn);

        return (PERISCOPE_OFFSET + (irisA - mirror2).magnitude, (irisB - mirror2).magnitude);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Puts an object on a point, aimed horizontally (front = forward of the prefab).</summary>
    static void Put(Transform t, Vector3 position, Vector3 forward)
    {
        if (!t) { s_Log.AppendLine("  (object not found, skipped)"); return; }
        Undo.RecordObject(t, "Place optic");
        t.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z), Vector3.up));
    }

    /// <summary>Flat optic with the centre of the coated front face on <paramref name="facePoint"/> (like PlaceOptic in LaserSetupMenu).</summary>
    static void PutOptic(Transform t, Vector3 facePoint, Vector3 normal, float thickness) =>
        Put(t, facePoint - normal * (thickness * 0.5f), normal);

    static void SetActive(Transform t, bool active)
    {
        if (!t || t.gameObject.activeSelf == active) return;
        Undo.RecordObject(t.gameObject, "Toggle");
        t.gameObject.SetActive(active);
    }

    /// <summary>
    /// Scales the controls of a mirror by k = design lever / new lever, so one step moves the spot as far as in the
    /// test setup. Base values come from the prefab, start values from Carlo's design.
    /// </summary>
    static void ScaleMount(Transform optic, float designYaw, float designPitch, float designNoise, float designLever, float lever)
    {
        var mount = optic ? optic.GetComponentInChildren<MirrorMount>(true) : null;
        if (!mount) { s_Log.AppendLine($"  no MirrorMount on {(optic ? optic.name : "?")}"); return; }

        float k = designLever / lever;
        var source = PrefabUtility.GetCorrespondingObjectFromSource(mount);
        var defaults = source ? new SerializedObject(source) : null;
        float Default(string property, float fallback) =>
            defaults != null && defaults.FindProperty(property) != null ? defaults.FindProperty(property).floatValue : fallback;

        var so = new SerializedObject(mount);
        so.FindProperty("holdSpeedDegreesPerSecond").floatValue = Default("holdSpeedDegreesPerSecond", 1.5f) * k;
        so.FindProperty("maxYawDegrees").floatValue = Mathf.Clamp(Default("maxYawDegrees", 8f) * k, 0.1f, 45f);
        so.FindProperty("maxPitchDegrees").floatValue = Mathf.Clamp(Default("maxPitchDegrees", 8f) * k, 0.1f, 45f);
        so.FindProperty("initialYawDegrees").floatValue = designYaw * k;
        so.FindProperty("initialPitchDegrees").floatValue = designPitch * k;
        so.FindProperty("seismicNoiseDegrees").floatValue = designNoise * k;
        so.FindProperty("sceneLeverArmMeters").floatValue = lever;
        // Coarse: one step moves the spot about one target window (2 cm). A few coarse steps get you close,
        // after that only a handful of fine steps (0.01°) are needed instead of dozens.
        so.FindProperty("coarseStepDegrees").floatValue = Mathf.Rad2Deg * Mathf.Atan(COARSE_SPOT_MOVE / (2f * lever));
        so.ApplyModifiedProperties();

        RoundSteps(mount);

        var final = new SerializedObject(mount);
        float coarse = final.FindProperty("coarseStepDegrees").floatValue;
        float SpotMm(float degrees) => 2f * lever * Mathf.Tan(degrees * Mathf.Deg2Rad) * 1000f;
        s_Log.AppendLine($"    {optic.name}: lever {lever:0.00} m, coarse {coarse:0.00}° = {SpotMm(coarse):0} mm, fine {FINE_STEP:0.00}° = {SpotMm(FINE_STEP):0.0} mm per step");
    }

    const float FINE_STEP = 0.01f;     // fine is always 0.01°: then 0, 1 and 2 (and the start angle) can always be reached
    const float MIN_COARSE_STEP = 0.03f;
    const float COARSE_SPOT_MOVE = 0.02f;   // m: how far the spot moves with one coarse step

    /// <summary>Fine = 0.01°; coarse, range and start angle are rounded to a multiple of it.</summary>
    static void RoundSteps(MirrorMount mount)
    {
        float Round(float value) => Mathf.Round(value / FINE_STEP) * FINE_STEP;

        var so = new SerializedObject(mount);
        so.FindProperty("fineStepDegrees").floatValue = FINE_STEP;
        so.FindProperty("coarseStepDegrees").floatValue = Mathf.Max(MIN_COARSE_STEP, Round(so.FindProperty("coarseStepDegrees").floatValue));
        so.FindProperty("maxYawDegrees").floatValue = Mathf.Max(FINE_STEP * 10f, Round(so.FindProperty("maxYawDegrees").floatValue));
        so.FindProperty("maxPitchDegrees").floatValue = Mathf.Max(FINE_STEP * 10f, Round(so.FindProperty("maxPitchDegrees").floatValue));
        foreach (string property in new[] { "initialYawDegrees", "initialPitchDegrees" })
        {
            var p = so.FindProperty(property);
            float rounded = Round(p.floatValue);
            if (rounded == 0f && p.floatValue != 0f) rounded = Mathf.Sign(p.floatValue) * FINE_STEP;
            p.floatValue = rounded;
        }
        so.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Einstein Telescope/Laser/Round mirror steps (fine = 0.01°)", priority = 31)]
    static void RoundAllMounts()
    {
        var mounts = Object.FindObjectsByType<MirrorMount>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        s_Log.Clear();
        foreach (var mount in mounts)
        {
            // The lever (mirror -> spot) is already on the mount; the coarse step follows from it
            var so = new SerializedObject(mount);
            float lever = so.FindProperty("sceneLeverArmMeters").floatValue;
            so.FindProperty("coarseStepDegrees").floatValue = Mathf.Rad2Deg * Mathf.Atan(COARSE_SPOT_MOVE / (2f * lever));
            so.ApplyModifiedProperties();
            RoundSteps(mount);

            float coarse = new SerializedObject(mount).FindProperty("coarseStepDegrees").floatValue;
            float SpotMm(float degrees) => 2f * lever * Mathf.Tan(degrees * Mathf.Deg2Rad) * 1000f;
            s_Log.AppendLine($"  {mount.DisplayName}: lever {lever:0.00} m, coarse {coarse:0.00}° = {SpotMm(coarse):0} mm, fine {FINE_STEP:0.00}° = {SpotMm(FINE_STEP):0.0} mm per step");
        }

        if (mounts.Length > 0) EditorSceneManager.MarkSceneDirty(mounts[0].gameObject.scene);
        Debug.Log($"[LaserTowerLayout] Steps rounded on {mounts.Length} mirrors: fine {FINE_STEP}°, coarse = ~{COARSE_SPOT_MOVE * 1000f:0} mm spot movement.\n{s_Log}");
        s_Log.Clear();
    }

    [MenuItem("Tools/Einstein Telescope/Laser/Set star par times", priority = 32)]
    static void SetParTimesMenu()
    {
        var root = GameObject.Find(DEMO_ROOT);
        if (!root) { Debug.LogError($"[LaserTowerLayout] '{DEMO_ROOT}' not found."); return; }

        Undo.SetCurrentGroupName("Set star par times");
        int group = Undo.GetCurrentGroup();
        s_Log.Clear().AppendLine("[LaserTowerLayout] Star par times (3 stars within this time, 2 stars within twice it):");
        ApplyParTimes(root);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(s_Log.ToString());
        s_Log.Clear();
    }

    /// <summary>
    /// Writes <see cref="PAR_TIMES"/> into the levels, in the order of the level manager (falls back to the order
    /// of the LaserLevel components under the demo root).
    /// </summary>
    static void ApplyParTimes(GameObject root)
    {
        LaserLevel[] levels = null;
        var manager = root.GetComponentInChildren<LaserLevelManager>(true);
        if (manager)
        {
            var levelsProperty = new SerializedObject(manager).FindProperty("levels");
            levels = new LaserLevel[levelsProperty.arraySize];
            for (int i = 0; i < levels.Length; i++)
                levels[i] = levelsProperty.GetArrayElementAtIndex(i).objectReferenceValue as LaserLevel;
        }

        if (levels == null || levels.Length == 0)
            levels = root.GetComponentsInChildren<LaserLevel>(true);

        for (int i = 0; i < levels.Length; i++)
        {
            if (!levels[i]) continue;
            float par = PAR_TIMES[Mathf.Min(i, PAR_TIMES.Length - 1)];
            var so = new SerializedObject(levels[i]);
            var property = so.FindProperty("parTimeSeconds");
            if (property == null) continue;
            property.floatValue = par;
            so.ApplyModifiedProperties();
            s_Log.AppendLine($"  Level {i + 1} '{levels[i].Title}': {LaserBestTimes.FormatTime(par)} (2 stars within {LaserBestTimes.FormatTime(par * 2f)})");
        }
    }

    /// <summary>Level 3 had fold mirrors; with straight arms it becomes long arms with irises in the middle towers.</summary>
    static void RewriteLevel3(Transform level)
    {
        var laserLevel = level.GetComponent<LaserLevel>();
        if (!laserLevel) return;

        MirrorMount Mount(string name)
        {
            var t = level.Find(name);
            return t ? t.GetComponentInChildren<MirrorMount>(true) : null;
        }

        var so = new SerializedObject(laserLevel);
        var mirrors = so.FindProperty("mirrors");
        var list = new[] { Mount("Steering Mirror 1"), Mount("Steering Mirror 2"), Mount("End Test Mass X"), Mount("End Test Mass Y") };
        mirrors.arraySize = list.Length;
        for (int i = 0; i < list.Length; i++) mirrors.GetArrayElementAtIndex(i).objectReferenceValue = list[i];

        so.FindProperty("title").stringValue = "Long arms";
        so.FindProperty("briefing").stringValue =
            "Long arms: the end test masses now sit in the far towers, almost 16 m from the beam splitter. Walk the beam through " +
            "Iris A and Iris B, check that it passes Iris X and Iris Y in the middle towers, then align the end mirrors.";
        so.FindProperty("objectiveText").stringValue =
            "1. Steer the beam through Iris A and Iris B with the two steering mirrors.\n" +
            "2. The beam now runs through the middle towers to the end towers. If it misses Iris X or Iris Y, the input beam is " +
            "still crooked: go back to step 1.\n" +
            "3. Align End mirror X and End mirror Y until both beams land in the centre of the output detector.";
        so.FindProperty("controlsTip").stringValue =
            "The arms are long: use fine mode for the end mirrors. One coarse step moves the spot a long way.";
        so.FindProperty("physicsText").stringValue =
            "Einstein Telescope will have arms of 10 km. The longer the arm, the further the spot moves for the same tilt of a " +
            "mirror, so the alignment has to be more precise. That same effect is what makes long arms so sensitive: a tiny " +
            "change in length becomes measurable.";

        var hints = so.FindProperty("checkpointHints");
        if (hints.arraySize >= 4)
        {
            hints.GetArrayElementAtIndex(2).stringValue =
                "Iris X sits in the middle tower of the straight arm. If the beam misses it, straighten the input beam with Steering mirror 1 and 2.";
            hints.GetArrayElementAtIndex(3).stringValue =
                "Iris Y sits in the middle tower of the reflected arm. If the beam misses it, straighten the input beam with Steering mirror 1 and 2.";
        }
        so.ApplyModifiedProperties();
    }

    /// <summary>The optics are now inside the towers: make the towers clear in every level, otherwise nothing is visible.</summary>
    static void ShowTowersClearInAllLevels()
    {
        var tower = GameObject.Find(TOWER_OBJECT);
        var tt = tower ? tower.GetComponent<TowerTransparency>() : null;
        if (!tt) return;
        Undo.RecordObject(tt, "Clear towers in all levels");
        tt.clearInLevels = new[] { 1, 2, 3 };
        EditorUtility.SetDirty(tt);
        s_Log.AppendLine("  TowerTransparency: towers clear in level 1, 2 and 3");
    }
}

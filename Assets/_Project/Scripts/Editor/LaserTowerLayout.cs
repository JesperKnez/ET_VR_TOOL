using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Laser > Levels in de torens plaatsen
/// Verplaatst de BESTAANDE optics van de drie levels (Carlo's testopstelling) naar de torens van ETpathfinder,
/// met de laser door de bundelbuizen. Alle koppelingen (curved monitor, level manager, camera's) blijven intact.
///
///   T3 injectietoren   laser; in level 2/3 ook de periscoop van twee stuurspiegels
///   T2 beam splitter    beam splitter + output-detector
///   T1 / T4             middentorens: eindspiegels in level 1/2, irissen in level 3
///   T0 / T5             eindtorens: eindspiegels in level 3 ("Long arms", vervangt "Folded arms")
///
/// De armen worden 2,5-6x zo lang als in de testopstelling. Om de moeilijkheid gelijk te houden worden per spiegel
/// stapgrootte, bereik, beginafwijking en seismische ruis geschaald met (ontwerp-hefboom / nieuwe hefboom):
/// elke stap verplaatst de laservlek dan net zo ver als voorheen. Opnieuw draaien is veilig (rekent vanaf Carlo's
/// ontwerpwaarden, niet vanaf wat er nu staat). Ongedaan maken met Ctrl+Z.
/// </summary>
public static class LaserTowerLayout
{
    const string DEMO_ROOT = "Laser Demo - Michelson Interferometer";
    const string TOWER_OBJECT = "TowerConstellation";

    // Torenmiddelpunten (x, z), gemeten met Torens > Torenlayout meten op 2 nov 2026
    static readonly Vector2 INJECTION = new Vector2(2.600f, -7.700f);    // T3
    static readonly Vector2 SPLITTER = new Vector2(0.000f, -5.100f);     // T2
    static readonly Vector2 MID_X = new Vector2(-4.525f, -0.575f);       // T1, doorgaande arm
    static readonly Vector2 END_X = new Vector2(-11.172f, 6.072f);       // T0
    static readonly Vector2 MID_Y = new Vector2(4.525f, -0.575f);        // T4, gereflecteerde arm
    static readonly Vector2 END_Y = new Vector2(11.172f, 6.072f);        // T5
    const float BEAM_HEIGHT = 1.33f;                                      // hartlijn van de bundelbuizen

    // Optics: zelfde waarden als in LaserSetupMenu
    const float SPLITTER_THICKNESS = 0.06f;
    const float SPLITTER_INDEX = 1.45f;
    const float STEERING_THICKNESS = 0.07f;

    // Plek in de torens
    const float DETECTOR_DISTANCE = 0.9f;      // vanaf het midden van T2 (tank is Ø 2,54 m)
    const float PERISCOPE_OFFSET = 0.6f;       // stuurspiegel 1 naast de bundellijn, in T3
    const float LASER_BEFORE_MIRROR1 = 0.7f;
    const float IRIS_A_BEFORE_SPLITTER = 2.4f; // in de buis tussen T3 en T2
    const float IRIS_B_BEFORE_SPLITTER = 1.45f; // net binnen T2

    // Carlo's ontwerp-hefbomen (testopstelling), voor de schaalfactor
    const float DESIGN_END_SHORT = 2.5f + 1.2f;          // ArmLength + DetectorDistance
    const float DESIGN_END_X_FOLDED = 1.8f + 2.6f + 1.2f;
    const float DESIGN_END_Y_FOLDED = 2f * 1.8f + 1.2f;
    const float DESIGN_MIRROR1 = 2.0f;
    const float DESIGN_MIRROR2 = 1.5f;

    struct Geometry
    {
        public Vector3 splitter;     // midden van de beam splitter
        public Vector3 dirIn;        // invallende bundel = doorgaande arm (X)
        public Vector3 dirY;         // gereflecteerde arm (Y)
        public Vector3 coating;      // normaal van de coating, naar de laser toe
        public Vector3 entry;        // waar de bundel de coating raakt: begin van de gereflecteerde arm (Y)
        public Vector3 xLineStart;   // doorgaande bundel na de glasplaat
        public Vector3 outLineStart; // output-bundel na de glasplaat
    }

    static readonly System.Text.StringBuilder s_Log = new System.Text.StringBuilder();

    [MenuItem("Tools/Einstein Telescope/Laser/Levels in de torens plaatsen", priority = 30)]
    static void Place()
    {
        var root = GameObject.Find(DEMO_ROOT);
        if (!root) { Debug.LogError($"[LaserTowerLayout] '{DEMO_ROOT}' niet gevonden."); return; }
        if (!EditorUtility.DisplayDialog("Levels in de torens",
                "De optics van alle drie de levels worden naar de torens verplaatst en de spiegels worden fijner afgesteld. " +
                "Level 3 wordt 'Long arms'. Ongedaan maken kan met Ctrl+Z.", "Plaatsen", "Annuleren"))
            return;

        Undo.SetCurrentGroupName("Levels in de torens plaatsen");
        int group = Undo.GetCurrentGroup();
        s_Log.Clear().AppendLine("[LaserTowerLayout] Levels in de torens geplaatst");

        Geometry g = ComputeGeometry();
        Transform level1 = root.transform.Find("Level 1 - Michelson");
        Transform level2 = root.transform.Find("Level 2 - Beam Walking");
        Transform level3 = root.transform.Find("Level 3 - Folded Arms");

        if (level1) PlaceLevel1(level1, g);
        if (level2) PlaceLevel2(level2, g);
        if (level3) PlaceLevel3(level3, g);

        // De lab-box van de testopstelling hoort niet in de hal
        foreach (string box in new[] { "Lab Floor", "Lab Wall (back)", "Lab Wall (left)", "Lab Wall (right)" })
            SetActive(root.transform.Find(box), false);

        ShowTowersClearInAllLevels();

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(s_Log.ToString());
    }

    // ------------------------------------------------------------------ geometrie

    static Vector3 At(Vector2 xz) => new Vector3(xz.x, BEAM_HEIGHT, xz.y);

    static Geometry ComputeGeometry()
    {
        var g = new Geometry();
        g.splitter = At(SPLITTER);
        g.dirIn = (At(SPLITTER) - At(INJECTION)).normalized;
        g.dirY = (At(MID_Y) - At(SPLITTER)).normalized;
        g.coating = (g.dirY - g.dirIn).normalized;   // spiegelt dirIn precies naar dirY

        // Waar de invallende bundel (door het midden) het gecoate vlak raakt
        float s = SPLITTER_THICKNESS * 0.5f / Vector3.Dot(g.dirIn, g.coating);
        g.entry = g.splitter + g.dirIn * s;

        // De glasplaat verschuift doorgaande en terugkerende bundels zijwaarts (Snellius), net als in LaserSetupMenu
        g.xLineStart = LaserOptics.PlateExitPoint(g.entry, g.dirIn, g.coating, SPLITTER_THICKNESS, SPLITTER_INDEX);
        g.outLineStart = LaserOptics.PlateExitPoint(g.entry, -g.dirY, g.coating, SPLITTER_THICKNESS, SPLITTER_INDEX);
        return g;
    }

    /// <summary>Punt op de bundellijn dat het dichtst bij het midden van een toren ligt.</summary>
    static Vector3 OnLine(Vector3 start, Vector3 dir, Vector2 tower) => start + dir * Vector3.Dot(At(tower) - start, dir);

    // ------------------------------------------------------------------ levels

    static void PlaceLevel1(Transform level, Geometry g)
    {
        PlaceSplitterAndDetector(level, g);
        Put(level.Find("Laser (532 nm alignment laser)"), At(INJECTION), g.dirIn);
        float lever = PlaceShortArms(level, g);
        ScaleMount(level.Find("End Test Mass X"), 2.5f, 0.6f, 0.003f, DESIGN_END_SHORT, lever);
        ScaleMount(level.Find("End Test Mass Y"), -1.5f, -1.0f, 0.003f, DESIGN_END_SHORT, lever);
        s_Log.AppendLine($"  Level 1: armen {lever - DETECTOR_DISTANCE:0.00} m (was 2,5 m)");
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
        s_Log.AppendLine($"  Level 2: armen {lever - DETECTOR_DISTANCE:0.00} m, periscoop in de injectietoren");
    }

    static void PlaceLevel3(Transform level, Geometry g)
    {
        PlaceSplitterAndDetector(level, g);
        var (lever1, lever2) = PlaceInputPath(level, g);

        // Eindspiegels in de eindtorens, irissen in de middentorens
        Vector3 endX = OnLine(g.xLineStart, g.dirIn, END_X);
        Vector3 endY = OnLine(g.entry, g.dirY, END_Y);
        Put(level.Find("End Test Mass X"), endX, -g.dirIn);
        Put(level.Find("End Test Mass Y"), endY, -g.dirY);
        Put(level.Find("Iris X"), OnLine(g.xLineStart, g.dirIn, MID_X), -g.dirIn);
        Put(level.Find("Iris Y"), OnLine(g.entry, g.dirY, MID_Y), -g.dirY);

        // Vouwspiegels passen niet in rechte armen
        SetActive(level.Find("Fold Mirror X"), false);
        SetActive(level.Find("Fold Mirror Y"), false);

        float leverX = (endX - g.splitter).magnitude + DETECTOR_DISTANCE;
        float leverY = (endY - g.splitter).magnitude + DETECTOR_DISTANCE;
        ScaleMount(level.Find("Steering Mirror 1"), -0.35f, 0.2f, 0.002f, DESIGN_MIRROR1, lever1);
        ScaleMount(level.Find("Steering Mirror 2"), 0.3f, -0.25f, 0.002f, DESIGN_MIRROR2, lever2);
        ScaleMount(level.Find("End Test Mass X"), 1.0f, -0.5f, 0.003f, DESIGN_END_X_FOLDED, leverX);
        ScaleMount(level.Find("End Test Mass Y"), -1.2f, 0.5f, 0.003f, DESIGN_END_Y_FOLDED, leverY);

        RewriteLevel3(level);
        s_Log.AppendLine($"  Level 3 → Long arms: armen {leverX - DETECTOR_DISTANCE:0.00} m, irissen in de middentorens, vouwspiegels uit");
    }

    static void PlaceSplitterAndDetector(Transform level, Geometry g)
    {
        Put(level.Find("Beam Splitter"), g.splitter, g.coating);
        Vector3 outDir = -g.dirY;
        Vector3 detector = g.outLineStart + outDir * Vector3.Dot(g.splitter + outDir * DETECTOR_DISTANCE - g.outLineStart, outDir);
        Put(level.Find("Output Port Detector"), detector, g.dirY);   // kijkt naar de beam splitter
    }

    /// <summary>Eindspiegels in de middentorens (level 1 en 2). Geeft de hefboom terug (spiegel -> detector).</summary>
    static float PlaceShortArms(Transform level, Geometry g)
    {
        Vector3 endX = OnLine(g.xLineStart, g.dirIn, MID_X);
        Vector3 endY = OnLine(g.entry, g.dirY, MID_Y);
        Put(level.Find("End Test Mass X"), endX, -g.dirIn);
        Put(level.Find("End Test Mass Y"), endY, -g.dirY);
        return (endX - g.splitter).magnitude + DETECTOR_DISTANCE;
    }

    /// <summary>
    /// Laser + periscoop in de injectietoren, irissen op de lijn naar de beam splitter.
    /// Geeft de hefbomen van stuurspiegel 1 (tot iris A) en 2 (tot iris B) terug.
    /// </summary>
    static (float, float) PlaceInputPath(Transform level, Geometry g)
    {
        Vector3 side = -g.dirY;   // kant van de periscoop, weg van de gereflecteerde arm
        Vector3 mirror2 = At(INJECTION);                           // op de bundellijn
        Vector3 mirror1 = mirror2 + side * PERISCOPE_OFFSET;
        Vector3 irisA = g.splitter - g.dirIn * IRIS_A_BEFORE_SPLITTER;
        Vector3 irisB = g.splitter - g.dirIn * IRIS_B_BEFORE_SPLITTER;

        Put(level.Find("Laser (532 nm alignment laser)"), mirror1 - g.dirIn * LASER_BEFORE_MIRROR1, g.dirIn);
        // 45°-spiegel: normaal = uitgaande - invallende richting
        PutOptic(level.Find("Steering Mirror 1"), mirror1, (-side - g.dirIn).normalized, STEERING_THICKNESS);
        PutOptic(level.Find("Steering Mirror 2"), mirror2, (g.dirIn + side).normalized, STEERING_THICKNESS);
        Put(level.Find("Iris A"), irisA, -g.dirIn);
        Put(level.Find("Iris B"), irisB, -g.dirIn);

        return (PERISCOPE_OFFSET + (irisA - mirror2).magnitude, (irisB - mirror2).magnitude);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Zet een object op een punt, horizontaal gericht (voorkant = forward van de prefab).</summary>
    static void Put(Transform t, Vector3 position, Vector3 forward)
    {
        if (!t) { s_Log.AppendLine("  (object niet gevonden, overgeslagen)"); return; }
        Undo.RecordObject(t, "Place optic");
        t.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z), Vector3.up));
    }

    /// <summary>Platte optiek met het midden van de gecoate voorkant op <paramref name="facePoint"/> (zoals PlaceOptic in LaserSetupMenu).</summary>
    static void PutOptic(Transform t, Vector3 facePoint, Vector3 normal, float thickness) =>
        Put(t, facePoint - normal * (thickness * 0.5f), normal);

    static void SetActive(Transform t, bool active)
    {
        if (!t || t.gameObject.activeSelf == active) return;
        Undo.RecordObject(t.gameObject, "Toggle");
        t.gameObject.SetActive(active);
    }

    /// <summary>
    /// Schaal de bediening van een spiegel met k = ontwerp-hefboom / nieuwe hefboom, zodat één stap de vlek net zo ver
    /// verplaatst als in de testopstelling. Basiswaarden komen uit de prefab, beginwaarden uit Carlo's ontwerp.
    /// </summary>
    static void ScaleMount(Transform optic, float designYaw, float designPitch, float designNoise, float designLever, float lever)
    {
        var mount = optic ? optic.GetComponentInChildren<MirrorMount>(true) : null;
        if (!mount) { s_Log.AppendLine($"  geen MirrorMount op {(optic ? optic.name : "?")}"); return; }

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
        so.FindProperty("coarseStepDegrees").floatValue = Default("coarseStepDegrees", 0.5f) * k;
        so.ApplyModifiedProperties();

        RoundSteps(mount);
        s_Log.AppendLine($"    {optic.name}: hefboom {lever:0.00} m, stappen x{k:0.00}");
    }

    const float FINE_STEP = 0.01f;     // fijn is altijd 0,01°: dan zijn 0, 1 en 2 (en de beginstand) altijd te bereiken
    const float MIN_COARSE_STEP = 0.05f;

    /// <summary>Fijn = 0,01°; grof, bereik en beginstand worden afgerond op een veelvoud daarvan.</summary>
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

    [MenuItem("Tools/Einstein Telescope/Laser/Spiegelstappen afronden (fijn = 0,01°)", priority = 31)]
    static void RoundAllMounts()
    {
        var mounts = Object.FindObjectsByType<MirrorMount>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var mount in mounts) RoundSteps(mount);
        if (mounts.Length > 0) EditorSceneManager.MarkSceneDirty(mounts[0].gameObject.scene);
        Debug.Log($"[LaserTowerLayout] Stappen afgerond op {mounts.Length} spiegels: fijn {FINE_STEP}°, grof een veelvoud daarvan, beginstand ook.");
    }

    /// <summary>Level 3 had vouwspiegels; in rechte armen worden het lange armen met irissen in de middentorens.</summary>
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

    /// <summary>De optics staan nu ín de torens: maak ze in elk level doorzichtig, anders zie je niets.</summary>
    static void ShowTowersClearInAllLevels()
    {
        var tower = GameObject.Find(TOWER_OBJECT);
        var tt = tower ? tower.GetComponent<TowerTransparency>() : null;
        if (!tt) return;
        Undo.RecordObject(tt, "Clear towers in all levels");
        tt.clearInLevels = new[] { 1, 2, 3 };
        EditorUtility.SetDirty(tt);
        s_Log.AppendLine("  TowerTransparency: torens doorzichtig in level 1, 2 en 3");
    }
}

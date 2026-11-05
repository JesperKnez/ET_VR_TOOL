using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EinsteinTelescope.Laser.Editor
{
    /// <summary>
    /// Menu: Tools > Einstein Telescope > Laser.
    /// Creates the models, materials and prefabs of the laser kit and can build a ready-to-play demo:
    /// a Michelson interferometer (the layout of LIGO, Virgo and each Einstein Telescope detector)
    /// whose two end test masses start misaligned.
    /// </summary>
    public static class LaserSetupMenu
    {
        private const string RootFolder = "Assets/_Project";
        private const string MaterialsFolder = RootFolder + "/Materials/Laser";
        private const string PrefabsFolder = RootFolder + "/Prefabs/Laser";
        private const string ModelsFolder = RootFolder + "/Models/Laser";
        private const string BeamShaderName = "EinsteinTelescope/LaserBeam";
        private const string DemoRootName = "Laser Demo - Michelson Interferometer";

        /// <summary>Asset label marking prefabs made by this version; older generated prefabs are rebuilt.</summary>
        private const string KitVersionLabel = "EinsteinLaserKit-v8";

        private const float BeamHeight = LaserModels.BeamHeight; // Height of the beam above the floor (m).
        private const float TestMassRadius = 0.31f;     // Einstein Telescope HF test mass: 62 cm diameter ...
        private const float TestMassThickness = 0.30f;  // ... and 30 cm thick, about 200 kg of fused silica.
        private const float SplitterRadius = 0.25f;     // 50 cm beam splitter.
        private const float SplitterThickness = 0.06f;  // Also the substrate thickness used for refraction.
        private const float SplitterIndex = 1.45f;      // Fused silica.
        private const float ColliderMargin = 0.01f;     // Collider slightly larger than the optic so beams near the edge are partly clipped, not missed.
        private const float SensorRadius = 0.04f;
        private const float IrisHoleRadius = 0.02f;    // 4 cm opening: a beam 1 cm off centre (the goal edge) still passes ~99%
        private const float ArmLength = 2.5f;
        private const float DetectorDistance = 1.2f;    // From the beam splitter to the output detector.

        // Ergonomics for a standing operator (eyes at about 1.6-1.7 m): monitor slightly below eye level,
        // console at hand height like a lectern.
        private static readonly Vector2 s_ScreenSize = new Vector2(0.52f, 0.30f);
        private const float ScreenDisplayRadius = 0.085f;
        private const float ScreenTilt = 12f;
        private const float ScreenCenterHeight = 1.25f;
        private const float PadCanvasScale = 0.0007f;   // 600 x 640 canvas units -> 42 x 45 cm
        private const float PadCenterHeight = 0.98f;
        private const float PadTilt = 40f;

        private static class PrefabNames
        {
            public const string Laser = "Laser Source";
            public const string TestMass = "Test Mass";
            public const string SteeringMirror = "Steering Mirror";
            public const string BeamSplitter = "Beam Splitter";
            public const string Detector = "QPD Detector";
            public const string Iris = "Alignment Iris";
            public const string BeamDump = "Beam Dump";
            public const string Monitor = "Control Room Monitor";
            public const string ControlPad = "Laser Control Pad";
            public const string MirrorCameraScreen = "Mirror Camera Screen";
        }

        // ----- Menu items -------------------------------------------------------------------------

        [MenuItem("Tools/Einstein Telescope/Laser/Create Materials and Prefabs", priority = 0)]
        public static void CreateMaterialsAndPrefabs()
        {
            EnsurePrefabs(false);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Laser kit",
                "Models, materials and prefabs are ready in\n" + PrefabsFolder + "\n\nDrag them into your scene, " +
                "or use 'Build Demo Interferometer' to see a working example.", "OK");
        }

        [MenuItem("Tools/Einstein Telescope/Laser/Rebuild All Prefabs (overwrite)", priority = 2)]
        public static void RebuildAllPrefabs()
        {
            bool ok = EditorUtility.DisplayDialog("Laser kit",
                "Rebuild every laser-kit prefab from scratch? Changes you made inside these prefabs are lost; " +
                "instances in your scenes update automatically.", "Rebuild", "Cancel");
            if (!ok)
            {
                return;
            }

            EnsurePrefabs(true);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Einstein Telescope/Laser/Build Demo Interferometer In Scene", priority = 1)]
        public static void BuildDemoInterferometer()
        {
            GameObject existing = GameObject.Find(DemoRootName);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog("Laser demo",
                    "A laser demo already exists in this scene. Replace it?", "Replace", "Cancel");
                if (!replace)
                {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            EnsurePrefabs(false);
            GameObject root = BuildDemo();
            Undo.RegisterCreatedObjectUndo(root, "Build laser demo");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Einstein Telescope/Laser/Levels/Start At Level 1 (Michelson)", priority = 20)]
        public static void StartAtLevel1() => SetStartLevel(0);

        [MenuItem("Tools/Einstein Telescope/Laser/Levels/Start At Level 2 (Beam Walking)", priority = 21)]
        public static void StartAtLevel2() => SetStartLevel(1);

        [MenuItem("Tools/Einstein Telescope/Laser/Levels/Start At Level 3 (Folded Arms)", priority = 22)]
        public static void StartAtLevel3() => SetStartLevel(2);

        /// <summary>Choose the level that loads when Play starts, and show its optics in the Scene view.</summary>
        public static void SetStartLevel(int index)
        {
            GameObject demo = GameObject.Find(DemoRootName);
            LaserLevelManager manager = demo != null ? demo.GetComponentInChildren<LaserLevelManager>() : null;
            if (manager == null)
            {
                EditorUtility.DisplayDialog("Laser levels", "Build the demo first: Tools > Einstein Telescope > Laser > Build Demo Interferometer In Scene.", "OK");
                return;
            }

            var so = new SerializedObject(manager);
            SerializedProperty levels = so.FindProperty("levels");
            index = Mathf.Clamp(index, 0, Mathf.Max(0, levels.arraySize - 1));
            so.FindProperty("startLevel").intValue = index;
            so.ApplyModifiedProperties();
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i).objectReferenceValue as LaserLevel;
                if (level != null)
                {
                    Undo.RecordObject(level.gameObject, "Show level");
                    level.gameObject.SetActive(i == index);
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // ----- Demo scene -------------------------------------------------------------------------

        private static GameObject BuildDemo()
        {
            var root = new GameObject(DemoRootName);
            root.transform.position = new Vector3(0f, 0f, 3f);
            Transform parent = root.transform;

            BuildFloorAndWalls(parent);

            // The three levels share the room, the monitor, the control pad and the game logic.
            LaserLevel level1 = BuildLevelMichelson(parent);
            LaserLevel level2 = BuildLevelBeamWalking(parent);
            LaserLevel level3 = BuildLevelFoldedArms(parent);
            level2.gameObject.SetActive(false);
            level3.gameObject.SetActive(false);

            // Monitor and console stand right in front of the player (XR Origin at the world origin).
            GameObject monitor = Place(PrefabNames.Monitor, parent, new Vector3(0.6f, 0f, -2.3f), 0f, "Output Port Monitor");
            var display = monitor.GetComponentInChildren<LaserTargetDisplay>();
            var readout = monitor.GetComponentInChildren<LaserMonitorReadout>();

            // Placeholder D-pad console the player clicks with the controller rays.
            GameObject pad = Place(PrefabNames.ControlPad, parent, new Vector3(-0.45f, 0f, -2.3f), 0f, "Laser Control Pad (placeholder)");
            var controlPad = pad.GetComponentInChildren<LaserControlPad>();

            // Live view from the selected mirror, left of the pad, turned towards the player.
            Place(PrefabNames.MirrorCameraScreen, parent, new Vector3(-1.05f, 0f, -2.45f), -60f, "Mirror Camera Screen");

            // Floating briefing screen between the player and the optics; closed by the player.
            LaserBriefingPanel briefing = BuildBriefingScreen(parent);
            LaserResultsCard results = BuildResultsCard(parent);
            var cameras = monitor.GetComponentInChildren<LaserBeamCameraScreen>();

            BuildGameLogic(parent, new[] { level1, level2, level3 }, display, readout, controlPad, briefing, results, cameras);
            return root;
        }

        // ----- Levels -----------------------------------------------------------------------------
        //
        // All levels use the same Michelson core (beam splitter at the origin, output detector towards the
        // player) and the same accuracy. Only the number and placement of the mirrors changes:
        //   1  Michelson        laser -> splitter -> 2 end test masses                       2 mirrors
        //   2  Beam walking     2 steering mirrors + 2 irises before the splitter           4 mirrors
        //   3  Folded arms      level 2 + a fold mirror and an iris in each arm (GEO600)    6 mirrors
        // Positions follow the real beam lines, including the sideways shift through the splitter glass,
        // so with every mirror at 0 deg the beam passes the centre of every iris and the detector.

        private const float InputMirrorX = -2.2f;      // Steering mirrors of levels 2 and 3.
        private const float InputLaserZ = -1.3f;
        private const float FoldDistance = 1.8f;       // Splitter to fold mirror (level 3).
        private const float FoldedArmEnd = 2.6f;       // Fold mirror X to end mirror X runs up to this z.
        private const float SteeringMirrorRadius = 0.125f;
        private const float SteeringMirrorThickness = 0.07f;

        private static LaserLevel BuildLevelMichelson(Transform parent)
        {
            Transform level = LevelRoot(parent, "Level 1 - Michelson");

            // Laser -> beam splitter (45 deg) -> X arm (to the right) and Y arm (away from the player).
            GameObject laser = Place(PrefabNames.Laser, level, new Vector3(-ArmLength, BeamHeight, 0f), 90f, "Laser (532 nm alignment laser)");
            Michelson core = BuildMichelsonCore(level, false);
            ConfigureMount(core.EndX, "End mirror X", 2.5f, 0.6f, 0.003f, ArmLength + DetectorDistance);
            ConfigureMount(core.EndY, "End mirror Y", -1.5f, -1.0f, 0.003f, ArmLength + DetectorDistance);

            return ConfigureLevel(level.gameObject, "Michelson",
                "Michelson interferometer: steer the two end test masses so both returning beams land in the centre of the output detector.",
                laser, new[] { FindMount(core.EndX), FindMount(core.EndY) }, new GameObject[0], new string[0], new string[0], core.Detector,
                LaserLevelTexts.ObjectiveMichelson, LaserLevelTexts.TipMichelson, LaserLevelTexts.PhysicsMichelson);
        }

        private static LaserLevel BuildLevelBeamWalking(Transform parent)
        {
            Transform level = LevelRoot(parent, "Level 2 - Beam Walking");
            InputPath input = BuildInputPath(level);
            Michelson core = BuildMichelsonCore(level, false);
            ConfigureMount(input.Mirror1, "Steering mirror 1", 0.4f, -0.25f, 0.002f, 2.0f);
            ConfigureMount(input.Mirror2, "Steering mirror 2", -0.3f, 0.2f, 0.002f, 1.5f);
            ConfigureMount(core.EndX, "End mirror X", 1.5f, 0.5f, 0.003f, ArmLength + DetectorDistance);
            ConfigureMount(core.EndY, "End mirror Y", -1.0f, -0.6f, 0.003f, ArmLength + DetectorDistance);

            return ConfigureLevel(level.gameObject, "Beam walking",
                "Beam walking: the laser now reaches the interferometer via two steering mirrors. First send it through Iris A and Iris B, then align the end mirrors.",
                input.Laser,
                new[] { FindMount(input.Mirror1), FindMount(input.Mirror2), FindMount(core.EndX), FindMount(core.EndY) },
                new[] { input.IrisA, input.IrisB }, new[] { "Iris A", "Iris B" },
                new[] { InputIrisAHint, InputIrisBHint }, core.Detector,
                LaserLevelTexts.ObjectiveBeamWalking, LaserLevelTexts.TipBeamWalking, LaserLevelTexts.PhysicsBeamWalking);
        }

        private static LaserLevel BuildLevelFoldedArms(Transform parent)
        {
            Transform level = LevelRoot(parent, "Level 3 - Folded Arms");
            InputPath input = BuildInputPath(level);
            Michelson core = BuildMichelsonCore(level, true);
            ConfigureMount(input.Mirror1, "Steering mirror 1", -0.35f, 0.2f, 0.002f, 2.0f);
            ConfigureMount(input.Mirror2, "Steering mirror 2", 0.3f, -0.25f, 0.002f, 1.5f);
            ConfigureMount(core.FoldX, "Fold mirror X", 0.6f, 0.3f, 0.002f, 1.4f);
            ConfigureMount(core.EndX, "End mirror X", 1.0f, -0.5f, 0.003f, FoldDistance + FoldedArmEnd + DetectorDistance);
            ConfigureMount(core.FoldY, "Fold mirror Y", -0.5f, -0.4f, 0.002f, 1.4f);
            ConfigureMount(core.EndY, "End mirror Y", -1.2f, 0.5f, 0.003f, 2f * FoldDistance + DetectorDistance);

            return ConfigureLevel(level.gameObject, "Folded arms",
                "Folded arms: each arm has an extra fold mirror, like GEO600. Walk the beam through Iris A and B, centre it on the arm irises with the fold mirrors, then align the end mirrors.",
                input.Laser,
                new[] { FindMount(input.Mirror1), FindMount(input.Mirror2), FindMount(core.FoldX), FindMount(core.EndX), FindMount(core.FoldY), FindMount(core.EndY) },
                new[] { input.IrisA, input.IrisB, core.IrisX, core.IrisY }, new[] { "Iris A", "Iris B", "Iris X", "Iris Y" },
                new[]
                {
                    InputIrisAHint, InputIrisBHint,
                    "Fold mirror X sends the beam down the X arm: centre it on Iris X.",
                    "Fold mirror Y sends the beam down the Y arm: centre it on Iris Y."
                }, core.Detector,
                LaserLevelTexts.ObjectiveFoldedArms, LaserLevelTexts.TipFoldedArms, LaserLevelTexts.PhysicsFoldedArms);
        }

        private const string InputIrisAHint = "Beam walking: Steering mirror 1 moves the beam onto Iris A, Steering mirror 2 centres it on Iris B. Repeat until both are centred.";
        private const string InputIrisBHint = "Use Steering mirror 2 to centre the beam on Iris B. If Iris A drifts off, correct it with Steering mirror 1 and come back.";

        private struct Michelson
        {
            public GameObject Splitter, EndX, EndY, FoldX, FoldY, IrisX, IrisY, Detector;
        }

        private struct InputPath
        {
            public GameObject Laser, Mirror1, Mirror2, IrisA, IrisB;
        }

        private static Transform LevelRoot(Transform parent, string name)
        {
            var level = new GameObject(name);
            level.transform.SetParent(parent, false);
            return level.transform;
        }

        /// <summary>
        /// Beam splitter, the two arms (straight, or folded with an iris in each arm) and the output detector.
        /// The input beam must arrive along z = 0 travelling +x, like the laser of level 1.
        /// </summary>
        private static Michelson BuildMichelsonCore(Transform level, bool folded)
        {
            var core = new Michelson();
            core.Splitter = Place(PrefabNames.BeamSplitter, level, new Vector3(0f, BeamHeight, 0f), -45f, "Beam Splitter");
            ConfigureMount(core.Splitter, "Beam splitter", 0f, 0f, 0f, ArmLength + DetectorDistance);

            float yArmX = SplitterEntryPoint().x;   // reflected beam (Y arm) travels +z along this x
            float xArmZ = XArmLineZ();              // transmitted beam (X arm) travels +x along this z
            if (!folded)
            {
                core.EndX = Place(PrefabNames.TestMass, level, new Vector3(ArmLength, BeamHeight, 0f), -90f, "End Test Mass X");
                core.EndY = Place(PrefabNames.TestMass, level, new Vector3(0f, BeamHeight, ArmLength), 180f, "End Test Mass Y");
            }
            else
            {
                // X arm: splitter -> fold mirror (turns the beam +z) -> iris -> end test mass.
                core.FoldX = PlaceOptic(PrefabNames.SteeringMirror, level, new Vector3(FoldDistance, BeamHeight, xArmZ),
                    new Vector3(-1f, 0f, 1f), SteeringMirrorThickness, "Fold Mirror X");
                core.IrisX = Place(PrefabNames.Iris, level, new Vector3(FoldDistance, BeamHeight, 1.25f), 180f, "Iris X");
                core.EndX = PlaceOptic(PrefabNames.TestMass, level, new Vector3(FoldDistance, BeamHeight, FoldedArmEnd),
                    Vector3.back, TestMassThickness, "End Test Mass X");

                // Y arm: splitter -> fold mirror (turns the beam -x) -> iris -> end test mass.
                core.FoldY = PlaceOptic(PrefabNames.SteeringMirror, level, new Vector3(yArmX, BeamHeight, FoldDistance),
                    new Vector3(-1f, 0f, -1f), SteeringMirrorThickness, "Fold Mirror Y");
                core.IrisY = Place(PrefabNames.Iris, level, new Vector3(-0.95f, BeamHeight, FoldDistance), 90f, "Iris Y");
                core.EndY = PlaceOptic(PrefabNames.TestMass, level, new Vector3(-FoldDistance, BeamHeight, FoldDistance),
                    Vector3.right, TestMassThickness, "End Test Mass Y");
                ConfigureCheckpoint(core.IrisX);
                ConfigureCheckpoint(core.IrisY);
            }

            // The recombined beams leave the splitter towards the player: the "output port" (dark port in LIGO).
            // Placed on the line the perfectly aligned beams really take (the splitter's glass shifts them sideways).
            core.Detector = Place(PrefabNames.Detector, level, new Vector3(OutputBeamOffsetX(), BeamHeight, -DetectorDistance), 0f, "Output Port Detector");
            Configure(core.Detector.GetComponent<LaserTarget>(), so =>
            {
                so.FindProperty("requiredBeamCount").intValue = 2;
                // Perfect alignment delivers ~49% of the laser here (half goes back to the laser); 30% leaves room
                // for the small losses of beams that pass the irises off centre but within the 1 cm goal.
                so.FindProperty("requiredPowerFraction").floatValue = 0.3f;
                so.FindProperty("centeringTolerance").floatValue = 0.01f;
            });
            return core;
        }

        /// <summary>
        /// Laser in the front-left corner, two steering mirrors that turn the beam twice (a periscope in the
        /// horizontal plane) and two irises on the line into the beam splitter: the classic beam-walking setup.
        /// </summary>
        private static InputPath BuildInputPath(Transform level)
        {
            var input = new InputPath();
            input.Laser = Place(PrefabNames.Laser, level, new Vector3(-3.0f, BeamHeight, InputLaserZ), 90f, "Laser (532 nm alignment laser)");
            input.Mirror1 = PlaceOptic(PrefabNames.SteeringMirror, level, new Vector3(InputMirrorX, BeamHeight, InputLaserZ),
                new Vector3(-1f, 0f, 1f), SteeringMirrorThickness, "Steering Mirror 1");
            input.Mirror2 = PlaceOptic(PrefabNames.SteeringMirror, level, new Vector3(InputMirrorX, BeamHeight, 0f),
                new Vector3(1f, 0f, -1f), SteeringMirrorThickness, "Steering Mirror 2");
            input.IrisA = Place(PrefabNames.Iris, level, new Vector3(-1.6f, BeamHeight, 0f), -90f, "Iris A");
            input.IrisB = Place(PrefabNames.Iris, level, new Vector3(-0.75f, BeamHeight, 0f), -90f, "Iris B");
            ConfigureCheckpoint(input.IrisA);
            ConfigureCheckpoint(input.IrisB);
            return input;
        }

        /// <summary>
        /// Irises used as checkpoints only judge the outgoing beam, not the light coming back from the arms,
        /// and use the same 1 cm tolerance as the output detector: harder levels add mirrors, not precision.
        /// </summary>
        private static void ConfigureCheckpoint(GameObject iris)
        {
            Configure(iris.GetComponent<LaserTarget>(), so =>
            {
                so.FindProperty("frontFaceOnly").boolValue = true;
                so.FindProperty("centeringTolerance").floatValue = 0.01f;
                so.FindProperty("requiredPowerFraction").floatValue = 0.2f;
            });
        }

        /// <summary>
        /// Places a flat optic so the centre of its coated (front) face sits at <paramref name="facePoint"/>,
        /// facing <paramref name="faceNormal"/> (horizontal). For a 45 deg fold: normal = outgoing - incoming direction.
        /// </summary>
        private static GameObject PlaceOptic(string prefabName, Transform parent, Vector3 facePoint, Vector3 faceNormal, float thickness, string instanceName)
        {
            Vector3 n = faceNormal.normalized;
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            return Place(prefabName, parent, facePoint - n * (thickness * 0.5f), yaw, instanceName);
        }

        private static LaserLevel ConfigureLevel(GameObject levelObject, string title, string briefing, GameObject laser, MirrorMount[] mirrors,
            GameObject[] checkpoints, string[] labels, string[] hints, GameObject detector, string objectiveText, string controlsTip, string physicsText)
        {
            var level = levelObject.AddComponent<LaserLevel>();
            Configure(level, so =>
            {
                so.FindProperty("title").stringValue = title;
                so.FindProperty("briefing").stringValue = briefing;
                so.FindProperty("objectiveText").stringValue = objectiveText;
                so.FindProperty("controlsTip").stringValue = controlsTip;
                so.FindProperty("physicsText").stringValue = physicsText;
                so.FindProperty("laser").objectReferenceValue = laser.GetComponent<LaserEmitter>();
                so.FindProperty("detector").objectReferenceValue = detector.GetComponent<LaserTarget>();
                SetObjectArray(so.FindProperty("mirrors"), mirrors);
                var targets = new Object[checkpoints.Length];
                for (int i = 0; i < checkpoints.Length; i++)
                {
                    targets[i] = checkpoints[i].GetComponent<LaserTarget>();
                }

                SetObjectArray(so.FindProperty("checkpoints"), targets);
                SetStringArray(so.FindProperty("checkpointLabels"), labels);
                SetStringArray(so.FindProperty("checkpointHints"), hints);
            });
            return level;
        }

        private static void SetObjectArray(SerializedProperty list, Object[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void SetStringArray(SerializedProperty list, string[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                list.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }

        /// <summary>
        /// Lab floor at y = 0 (its top sits 2 mm lower so it does not flicker against a floor already in the scene)
        /// and three walls. Walls and floor catch stray beams, so they also trigger the stray-light warnings.
        /// </summary>
        private static void BuildFloorAndWalls(Transform parent)
        {
            Material floor = LitMaterial("Lab Floor", new Color(0.36f, 0.38f, 0.41f), 0f, 0.35f);
            Material wall = LitMaterial("Lab Wall", new Color(0.58f, 0.6f, 0.63f), 0f, 0.2f);

            Box(parent, "Lab Floor", new Vector3(0f, -0.052f, -0.6f), new Vector3(7.2f, 0.1f, 9.4f), floor, true);

            const float wallHeight = 2.6f;
            float wallY = wallHeight * 0.5f;
            Box(parent, "Lab Wall (back)", new Vector3(0f, wallY, 3.45f), new Vector3(7.2f, wallHeight, 0.1f), wall, true);
            Box(parent, "Lab Wall (left)", new Vector3(-3.55f, wallY, 0.85f), new Vector3(0.1f, wallHeight, 5.3f), wall, true);
            Box(parent, "Lab Wall (right)", new Vector3(3.55f, wallY, 0.85f), new Vector3(0.1f, wallHeight, 5.3f), wall, true);
        }

        /// <summary>Where the input beam (along z = 0, travelling +x) meets the coated face of the splitter (yaw -45).</summary>
        private static Vector3 SplitterEntryPoint()
        {
            return new Vector3(-SplitterThickness * 0.5f * Mathf.Sqrt(2f), 0f, 0f);
        }

        private static Vector3 SplitterCoatingNormal() => new Vector3(-1f, 0f, 1f).normalized;

        /// <summary>The transmitted beam (X arm) leaves the splitter glass shifted sideways (Snell's law): its z.</summary>
        private static float XArmLineZ()
        {
            return LaserOptics.PlateExitPoint(SplitterEntryPoint(), Vector3.right, SplitterCoatingNormal(), SplitterThickness, SplitterIndex).z;
        }

        /// <summary>
        /// Sideways position of the output beam when both arms are perfectly aligned: the returning light
        /// crosses the tilted splitter glass, which shifts it (Snell's law).
        /// </summary>
        private static float OutputBeamOffsetX()
        {
            Vector3 exit = LaserOptics.PlateExitPoint(SplitterEntryPoint(), Vector3.back, SplitterCoatingNormal(), SplitterThickness, SplitterIndex);
            return exit.x;
        }

        private static void BuildGameLogic(Transform parent, LaserLevel[] levels, LaserTargetDisplay display, LaserMonitorReadout readout, LaserControlPad pad,
            LaserBriefingPanel briefing, LaserResultsCard results, LaserBeamCameraScreen cameras)
        {
            var logic = new GameObject("Laser Game Logic");
            logic.transform.SetParent(parent, false);

            var panel = logic.AddComponent<MirrorControlPanel>();
            var monitor = logic.AddComponent<LaserSafetyMonitor>();
            var objective = logic.AddComponent<LaserAlignmentObjective>();
            logic.AddComponent<InfraredViewerToggle>();
            var keyboard = logic.AddComponent<LaserDebugKeyboard>();
            var manager = logic.AddComponent<LaserLevelManager>();
            var audioFeedback = logic.AddComponent<LaserAudioFeedback>();
            var thumbstick = logic.AddComponent<LaserThumbstickControl>();
            var highlighter = logic.AddComponent<LaserSelectedBeamHighlighter>();

            // Wire everything to level 1, so the scene also works (and looks right) without the level manager.
            LaserLevel first = levels[0];
            LaserEmitter laser = first.Laser;
            Configure(panel, so => SetObjectArray(so.FindProperty("mirrors"), first.Mirrors));
            Configure(objective, so =>
            {
                SetObjectArray(so.FindProperty("requiredTargets"), first.AllTargets());
                so.FindProperty("safetyMonitor").objectReferenceValue = monitor;
                so.FindProperty("startAutomatically").boolValue = false;   // the level manager starts it when the briefing closes
            });

            Configure(keyboard, so =>
            {
                so.FindProperty("controlPanel").objectReferenceValue = panel;
                so.FindProperty("laser").objectReferenceValue = laser;
                so.FindProperty("objective").objectReferenceValue = objective;
                so.FindProperty("levelManager").objectReferenceValue = manager;
                so.FindProperty("audioFeedback").objectReferenceValue = audioFeedback;
            });

            Configure(audioFeedback, so =>
            {
                so.FindProperty("objective").objectReferenceValue = objective;
                so.FindProperty("safetyMonitor").objectReferenceValue = monitor;
            });
            Configure(thumbstick, so => so.FindProperty("controlPanel").objectReferenceValue = panel);
            Configure(highlighter, so => so.FindProperty("controlPanel").objectReferenceValue = panel);

            Configure(display, so => so.FindProperty("target").objectReferenceValue = first.Detector);
            Configure(readout, so =>
            {
                so.FindProperty("target").objectReferenceValue = first.Detector;
                so.FindProperty("laser").objectReferenceValue = laser;
                so.FindProperty("safetyMonitor").objectReferenceValue = monitor;
                so.FindProperty("objective").objectReferenceValue = objective;
                so.FindProperty("levelManager").objectReferenceValue = manager;
            });

            Configure(pad, so =>
            {
                so.FindProperty("controlPanel").objectReferenceValue = panel;
                so.FindProperty("laser").objectReferenceValue = laser;
                so.FindProperty("levelManager").objectReferenceValue = manager;
            });

            Configure(manager, so =>
            {
                SetObjectArray(so.FindProperty("levels"), levels);
                so.FindProperty("controlPanel").objectReferenceValue = panel;
                so.FindProperty("objective").objectReferenceValue = objective;
                so.FindProperty("targetDisplay").objectReferenceValue = display;
                so.FindProperty("readout").objectReferenceValue = readout;
                so.FindProperty("controlPad").objectReferenceValue = pad;
                so.FindProperty("debugKeyboard").objectReferenceValue = keyboard;
                so.FindProperty("briefing").objectReferenceValue = briefing;
                so.FindProperty("resultsCard").objectReferenceValue = results;
                so.FindProperty("safetyMonitor").objectReferenceValue = monitor;
                so.FindProperty("beamCameras").objectReferenceValue = cameras;
            });
        }

        private static GameObject Place(string prefabName, Transform parent, Vector3 localPosition, float yawDegrees, string instanceName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabName));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = instanceName;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            return instance;
        }

        private static MirrorMount FindMount(GameObject opticRoot)
        {
            return opticRoot.GetComponentInChildren<MirrorMount>();
        }

        private static void ConfigureMount(GameObject opticRoot, string displayName, float startYaw, float startPitch, float seismicNoise, float leverArm)
        {
            Configure(FindMount(opticRoot), so =>
            {
                so.FindProperty("displayName").stringValue = displayName;
                so.FindProperty("initialYawDegrees").floatValue = startYaw;
                so.FindProperty("initialPitchDegrees").floatValue = startPitch;
                so.FindProperty("seismicNoiseDegrees").floatValue = seismicNoise;
                // Distance from the optic to where its beam is judged (next iris, or back through the splitter to the detector).
                so.FindProperty("sceneLeverArmMeters").floatValue = leverArm;
            });
        }

        // ----- Prefabs ----------------------------------------------------------------------------

        private static void EnsurePrefabs(bool forceRebuild)
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ModelsFolder);

            CreatePrefab(PrefabNames.Laser, BuildLaserSource, forceRebuild);
            CreatePrefab(PrefabNames.TestMass, () => BuildSuspendedOptic(PrefabNames.TestMass, OpticKind.TestMass), forceRebuild);
            CreatePrefab(PrefabNames.SteeringMirror, () => BuildSuspendedOptic(PrefabNames.SteeringMirror, OpticKind.SteeringMirror), forceRebuild);
            CreatePrefab(PrefabNames.BeamSplitter, () => BuildSuspendedOptic(PrefabNames.BeamSplitter, OpticKind.BeamSplitter), forceRebuild);
            CreatePrefab(PrefabNames.Detector, BuildDetector, forceRebuild);
            CreatePrefab(PrefabNames.Iris, BuildIris, forceRebuild);
            CreatePrefab(PrefabNames.BeamDump, BuildBeamDump, forceRebuild);
            CreatePrefab(PrefabNames.Monitor, BuildMonitor, forceRebuild);
            CreatePrefab(PrefabNames.ControlPad, BuildControlPad, forceRebuild);
            CreatePrefab(PrefabNames.MirrorCameraScreen, BuildMirrorCameraScreen, forceRebuild);
        }

        /// <summary>Creates the prefab when it is missing or was made by an older version of the kit.</summary>
        private static void CreatePrefab(string prefabName, System.Func<GameObject> build, bool forceRebuild)
        {
            string path = PrefabPath(prefabName);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !forceRebuild && System.Array.IndexOf(AssetDatabase.GetLabels(existing), KitVersionLabel) >= 0)
            {
                return;
            }

            GameObject temporary = build();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            Object.DestroyImmediate(temporary);
            if (saved != null)
            {
                AssetDatabase.SetLabels(saved, new[] { KitVersionLabel });
            }
        }

        private static GameObject BuildLaserSource()
        {
            var root = new GameObject(PrefabNames.Laser);
            var emitter = root.AddComponent<LaserEmitter>();
            AddModel(root.transform, "Model", "Laser Head", () => LaserModels.LaserHead("Laser Head"));

            // Emission indicator on the back and top, so the operator behind the laser can see it is on.
            GameObject indicator = AddModel(root.transform, "Emission Indicator", "Laser Emission Indicator", () => LaserModels.LaserEmissionIndicator("Laser Emission Indicator"));
            var glow = new GameObject("Emission Glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0.05f, 0.11f, -0.43f);
            var glowLight = glow.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.range = 0.8f;
            glowLight.intensity = 0.6f;
            glowLight.shadows = LightShadows.None;
            var statusLight = root.AddComponent<LaserStatusLight>();
            Configure(statusLight, so =>
            {
                so.FindProperty("laser").objectReferenceValue = emitter;
                so.FindProperty("glowLight").objectReferenceValue = glowLight;
                SerializedProperty list = so.FindProperty("indicators");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = indicator.GetComponent<Renderer>();
            });

            AddLabel(root.transform, new Vector3(0f, 0.22f, -0.2f), null, "Laser", null);

            var housing = new GameObject("Housing Collider");
            housing.transform.SetParent(root.transform, false);
            var housingCollider = housing.AddComponent<BoxCollider>();
            housingCollider.center = new Vector3(0f, 0f, -0.225f);
            housingCollider.size = new Vector3(0.18f, 0.13f, 0.42f);

            Configure(emitter, so =>
            {
                so.FindProperty("housingCollider").objectReferenceValue = housingCollider;
                so.FindProperty("beamMaterial").objectReferenceValue = BeamMaterial(false);
                so.FindProperty("spotMaterial").objectReferenceValue = BeamMaterial(true);
            });

            return root;
        }

        /// <summary>
        /// An Einstein-Telescope-style suspended optic. Hierarchy:
        /// root (static) / Suspension Frame (delete it when the optic goes into a vacuum tower)
        ///               / Gimbal (motorised): MirrorMount / Optic (collider + LaserMirror or LaserBeamSplitter)
        ///                                                / Payload Model (optic, fibres, penultimate and reaction mass)
        /// </summary>
        private enum OpticKind
        {
            TestMass,
            BeamSplitter,
            SteeringMirror
        }

        private static GameObject BuildSuspendedOptic(string name, OpticKind kind)
        {
            bool isBeamSplitter = kind == OpticKind.BeamSplitter;
            float radius = kind == OpticKind.BeamSplitter ? SplitterRadius : kind == OpticKind.SteeringMirror ? SteeringMirrorRadius : TestMassRadius;
            float thickness = kind == OpticKind.BeamSplitter ? SplitterThickness : kind == OpticKind.SteeringMirror ? SteeringMirrorThickness : TestMassThickness;
            string kindName = kind == OpticKind.BeamSplitter ? "Splitter" : kind == OpticKind.SteeringMirror ? "Steering Mirror" : "Test Mass";
            var root = new GameObject(name);

            var frame = new GameObject("Suspension Frame");
            frame.transform.SetParent(root.transform, false);
            string frameName = kindName + " Suspension Frame";
            AddModel(frame.transform, "Frame Model", frameName,
                () => LaserModels.SuspensionFrame(frameName, radius, thickness, kind == OpticKind.TestMass));
            float legX = LaserModels.FrameLegX(radius), top = LaserModels.FrameTop(radius), bottom = -BeamHeight;
            AddColliderBox(frame.transform, "Leg Collider (left)", new Vector3(-legX, (top + bottom) * 0.5f, 0f), new Vector3(0.1f, top - bottom, 0.1f));
            AddColliderBox(frame.transform, "Leg Collider (right)", new Vector3(legX, (top + bottom) * 0.5f, 0f), new Vector3(0.1f, top - bottom, 0.1f));
            AddColliderBox(frame.transform, "Top Beam Collider", new Vector3(0f, top - 0.05f, 0f), new Vector3(2f * legX + 0.12f, 0.1f, 0.1f));

            var gimbal = new GameObject("Gimbal (motorised)");
            gimbal.transform.SetParent(root.transform, false);
            gimbal.AddComponent<MirrorMount>();

            var optic = new GameObject((isBeamSplitter ? "Beam Splitter" : kindName) + " Optic");
            optic.transform.SetParent(gimbal.transform, false);
            var collider = optic.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f * (radius + ColliderMargin), 2f * (radius + ColliderMargin), thickness);

            if (isBeamSplitter)
            {
                Configure(optic.AddComponent<LaserBeamSplitter>(), so =>
                {
                    so.FindProperty("radius").floatValue = radius;
                    so.FindProperty("substrateThickness").floatValue = SplitterThickness;
                    so.FindProperty("refractiveIndex").floatValue = SplitterIndex;
                });
                AddModel(gimbal.transform, "Payload Model", "Beam Splitter Payload",
                    () => LaserModels.SuspendedOptic("Beam Splitter Payload", radius, thickness, false));
            }
            else
            {
                Configure(optic.AddComponent<LaserMirror>(), so =>
                {
                    so.FindProperty("mirrorRadius").floatValue = radius;
                    so.FindProperty("reflectivity").floatValue = 0.999f;
                });
                string payloadName = kindName + " Payload";
                AddModel(gimbal.transform, "Payload Model", payloadName,
                    () => LaserModels.SuspendedOptic(payloadName, radius, thickness, kind == OpticKind.TestMass));
            }

            // Name tag above the optic; steerable mirrors also get a glowing ring that shows while selected.
            Renderer ring = null;
            if (!isBeamSplitter)
            {
                string ringName = kindName + " Selection Ring";
                GameObject ringObject = AddModel(gimbal.transform, "Selection Ring", ringName, () => LaserModels.SelectionRing(ringName, radius + 0.045f));
                ringObject.transform.localPosition = new Vector3(0f, 0f, thickness * 0.5f + 0.01f);
                ring = ringObject.GetComponent<Renderer>();
                ringObject.SetActive(false);   // shown by LaserOpticLabel while this mirror is selected
            }

            AddLabel(root.transform, new Vector3(0f, radius + 0.17f, 0f), gimbal.GetComponent<MirrorMount>(), "", ring);
            return root;
        }

        private static GameObject BuildDetector()
        {
            var root = new GameObject(PrefabNames.Detector);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.11f, 0.11f, 0.03f);
            collider.center = new Vector3(0f, 0f, -0.015f);
            var target = root.AddComponent<LaserTarget>();

            AddModel(root.transform, "Model", "QPD Detector", () => LaserModels.QuadrantPhotodiode("QPD Detector", SensorRadius));
            GameObject ring = AddModel(root.transform, "Status Ring", "QPD Status Ring", () => LaserModels.StatusRing("QPD Status Ring", SensorRadius));
            GameObject backLight = AddModel(root.transform, "Status Light (back)", "QPD Status Light Back", () => LaserModels.StatusLightBack("QPD Status Light Back"));
            AddLabel(root.transform, new Vector3(0f, 0.15f, 0f), null, "", null);

            Configure(target, so =>
            {
                so.FindProperty("statusIndicator").objectReferenceValue = ring.GetComponent<Renderer>();
                SerializedProperty extras = so.FindProperty("extraStatusIndicators");
                extras.arraySize = 1;
                extras.GetArrayElementAtIndex(0).objectReferenceValue = backLight.GetComponent<Renderer>();
                so.FindProperty("detectorRadius").floatValue = SensorRadius;
            });

            return root;
        }

        private static GameObject BuildIris()
        {
            var root = new GameObject(PrefabNames.Iris);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.084f, 0.084f, 0.006f);
            var target = root.AddComponent<LaserTarget>();
            AddModel(root.transform, "Model", "Alignment Iris", () => LaserModels.Iris("Alignment Iris", IrisHoleRadius));
            AddLabel(root.transform, new Vector3(0f, 0.13f, 0f), null, "", null);

            Configure(target, so =>
            {
                so.FindProperty("targetType").enumValueIndex = (int)LaserTarget.TargetType.Iris;
                so.FindProperty("detectorRadius").floatValue = IrisHoleRadius;
                so.FindProperty("centeringTolerance").floatValue = 0.004f;
            });

            return root;
        }

        private static GameObject BuildBeamDump()
        {
            var root = new GameObject(PrefabNames.BeamDump);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.09f, 0.09f, 0.11f);
            collider.center = new Vector3(0f, 0f, -0.055f);
            root.AddComponent<LaserBeamDump>();
            AddModel(root.transform, "Model", "Beam Dump", () => LaserModels.BeamDump("Beam Dump"));
            return root;
        }

        /// <summary>Floor-standing monitor whose screen shows the beam spots and the four quadrant bars.</summary>
        private static GameObject BuildMonitor()
        {
            var root = new GameObject(PrefabNames.Monitor);

            var head = new GameObject("Screen Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, ScreenCenterHeight, -0.06f);
            head.transform.localRotation = Quaternion.Euler(ScreenTilt, 0f, 0f);
            Vector3 hinge = head.transform.localPosition + head.transform.localRotation * LaserModels.MountHingeLocal;
            GameObject stand = AddModel(root.transform, "Stand", "Monitor Stand", () => LaserModels.MonitorStand("Monitor Stand", hinge));
            stand.transform.SetAsFirstSibling();
            AddModel(head.transform, "Monitor Model", "Monitor Head", () => LaserModels.MonitorHead("Monitor Head", s_ScreenSize));
            AddModel(head.transform, "Screen Graphics", "Screen Graphics",
                () => LaserModels.ScreenGraphics("Screen Graphics", s_ScreenSize, ScreenDisplayRadius, ScreenDisplayRadius * 0.25f));

            var spotArea = new GameObject("Spot Display");
            spotArea.transform.SetParent(head.transform, false);
            spotArea.transform.localPosition = LaserModels.ScreenTargetCenter(s_ScreenSize, ScreenDisplayRadius);
            var markers = new Transform[2];
            for (int i = 0; i < markers.Length; i++)
            {
                GameObject marker = AddModel(spotArea.transform, "Beam Marker " + (i + 1), "Screen Marker", () => LaserModels.ScreenMarker("Screen Marker", 0.0055f));
                marker.transform.localPosition = new Vector3(0f, 0f, -0.0004f - 0.0001f * i);
                markers[i] = marker.transform;
            }

            var bars = new Transform[4];
            string[] barNames = { "Quadrant Bar (top left)", "Quadrant Bar (top right)", "Quadrant Bar (bottom left)", "Quadrant Bar (bottom right)" };
            for (int i = 0; i < bars.Length; i++)
            {
                GameObject bar = AddModel(head.transform, barNames[i], "Screen Bar", () => LaserModels.ScreenBar("Screen Bar", ScreenDisplayRadius * 0.11f));
                bar.transform.localPosition = LaserModels.ScreenBarStart(s_ScreenSize, ScreenDisplayRadius, i);
                bar.transform.localScale = new Vector3(LaserModels.QuadrantBarHalfLength(s_ScreenSize), 1f, 1f);
                bars[i] = bar.transform;
            }

            var display = spotArea.AddComponent<LaserTargetDisplay>();
            Configure(display, so =>
            {
                so.FindProperty("displayRadius").floatValue = ScreenDisplayRadius;
                so.FindProperty("barHalfPowerLength").floatValue = 2f * LaserModels.QuadrantBarHalfLength(s_ScreenSize);
                SerializedProperty markerList = so.FindProperty("beamMarkers");
                markerList.arraySize = markers.Length;
                for (int i = 0; i < markers.Length; i++)
                {
                    markerList.GetArrayElementAtIndex(i).objectReferenceValue = markers[i];
                }

                SerializedProperty barList = so.FindProperty("quadrantBars");
                barList.arraySize = bars.Length;
                for (int i = 0; i < bars.Length; i++)
                {
                    barList.GetArrayElementAtIndex(i).objectReferenceValue = bars[i];
                }
            });

            BuildMonitorText(head.transform);
            BuildBeamCameraScreen(head.transform);
            return root;
        }

        // Monitor text canvas: 1 canvas unit = 0.25 mm, so font sizes stay crisp at real size.
        private const float MonitorCanvasUnit = 0.00025f;
        private static readonly Color s_MonitorLabelColor = new Color(0.45f, 0.62f, 0.78f, 1f);

        /// <summary>
        /// Text layer of the monitor (status, hint, power, beam positions, laser state), driven by
        /// <see cref="LaserMonitorReadout"/>. Laid out around the spot circle (left) and the quadrant bars (right).
        /// Not clickable: no raycaster, so it never blocks the controller rays.
        /// </summary>
        private static void BuildMonitorText(Transform head)
        {
            var canvasObject = new GameObject("Screen Text", typeof(RectTransform));
            canvasObject.transform.SetParent(head, false);
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = s_ScreenSize / MonitorCanvasUnit;
            canvasRect.localPosition = new Vector3(0f, 0f, -0.0125f);
            canvasRect.localScale = Vector3.one * MonitorCanvasUnit;
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var readout = canvasObject.AddComponent<LaserMonitorReadout>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float halfX = s_ScreenSize.x * 500f;   // mm
            float halfY = s_ScreenSize.y * 500f;
            Vector3 circle = LaserModels.ScreenTargetCenter(s_ScreenSize, ScreenDisplayRadius) * 1000f;
            float radius = ScreenDisplayRadius * 1000f;
            float columnLeft = circle.x + radius + 16f;
            float columnRight = (s_ScreenSize.x * 0.5f - s_ScreenSize.x * 0.25f - 0.01f) * 1000f - 6f;
            float columnWidth = columnRight - columnLeft;
            float columnCenter = (columnLeft + columnRight) * 0.5f;

            Text header = MonitorText(canvasRect, "Header", new Vector2(0f, halfY - 16f), new Vector2(2f * halfX - 24f, 16f),
                "OUTPUT PORT DETECTOR", 10f, font, TextAnchor.MiddleLeft);
            header.color = new Color(0.8f, 0.9f, 1f, 1f);
            Text laserLine = MonitorText(canvasRect, "Laser", new Vector2(0f, halfY - 16f), new Vector2(2f * halfX - 24f, 16f),
                "SHUTTER CLOSED", 10f, font, TextAnchor.MiddleRight);

            Label(canvasRect, "Spot Label", new Vector2(circle.x, circle.y + radius + 9f), new Vector2(2f * radius, 10f), "BEAM SPOTS ON DETECTOR", font, TextAnchor.MiddleCenter);
            float panelCenterX = (s_ScreenSize.x * 0.5f - s_ScreenSize.x * 0.125f - 0.01f) * 1000f;
            Label(canvasRect, "Quadrant Label", new Vector2(panelCenterX, circle.y + radius + 9f), new Vector2(s_ScreenSize.x * 250f, 10f), "QPD QUADRANTS", font, TextAnchor.MiddleCenter);

            Label(canvasRect, "Power Label", new Vector2(columnCenter, circle.y + 66f), new Vector2(columnWidth, 10f), "RECEIVED POWER (share of laser)", font, TextAnchor.MiddleLeft);
            Text power = MonitorText(canvasRect, "Power", new Vector2(columnCenter, circle.y + 48f), new Vector2(columnWidth, 24f), "0 W", 20f, font, TextAnchor.MiddleLeft);
            Label(canvasRect, "Beams Label", new Vector2(columnCenter, circle.y + 22f), new Vector2(columnWidth, 10f), "BEAM POSITION (x right, y up)", font, TextAnchor.MiddleLeft);
            Text beams = MonitorText(canvasRect, "Beams", new Vector2(columnCenter, circle.y - 2f), new Vector2(columnWidth, 32f), "BEAM 1  ---\nBEAM 2  ---", 11f, font, TextAnchor.UpperLeft);
            beams.lineSpacing = 1.15f;

            float statusY = circle.y - radius - 18f;
            Text status = MonitorText(canvasRect, "Status", new Vector2(0f, statusY), new Vector2(2f * halfX - 24f, 22f), "NO SIGNAL", 18f, font, TextAnchor.MiddleLeft);
            status.fontStyle = FontStyle.Bold;
            Text hint = MonitorText(canvasRect, "Hint", new Vector2(0f, (statusY - 11f - (-halfY + 8f)) * 0.5f + (-halfY + 8f)), new Vector2(2f * halfX - 24f, statusY - 11f + halfY - 8f),
                "", 10f, font, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            Configure(readout, so =>
            {
                so.FindProperty("statusText").objectReferenceValue = status;
                so.FindProperty("hintText").objectReferenceValue = hint;
                so.FindProperty("laserText").objectReferenceValue = laserLine;
                so.FindProperty("powerText").objectReferenceValue = power;
                so.FindProperty("beamsText").objectReferenceValue = beams;
            });
        }

        /// <summary>Monitor text with position and size in millimetres on the screen (origin = screen centre).</summary>
        private static Text MonitorText(RectTransform canvas, string name, Vector2 positionMm, Vector2 sizeMm, string value, float heightMm, Font font, TextAnchor anchor)
        {
            float unitsPerMm = 0.001f / MonitorCanvasUnit;
            Text text = UiText(canvas, name, positionMm * unitsPerMm, sizeMm * unitsPerMm, value, Mathf.RoundToInt(heightMm * unitsPerMm), font);
            text.alignment = anchor;
            return text;
        }

        private static void Label(RectTransform canvas, string name, Vector2 positionMm, Vector2 sizeMm, string value, Font font, TextAnchor anchor)
        {
            Text text = MonitorText(canvas, name, positionMm, sizeMm, value, 7f, font, anchor);
            text.color = s_MonitorLabelColor;
        }

        // ----- Control pad (placeholder buttons) ---------------------------------------------------

        private static readonly Vector2 s_PadSize = new Vector2(600f, 640f); // canvas units = millimetres
        private static readonly Color s_PadButtonColor = new Color(0.13f, 0.2f, 0.28f, 1f);
        private static readonly Color s_PadTextColor = new Color(0.85f, 0.93f, 1f, 1f);

        /// <summary>
        /// A console on a stand with a world-space UI: mirror selector, a big D-pad with the step-size
        /// switch in the middle, angle read-out, and reset / shutter / IR-viewer buttons. Clicked with the
        /// XR controller rays (XR UI Input Module on the EventSystem) or the mouse.
        /// </summary>
        private static GameObject BuildControlPad()
        {
            var root = new GameObject(PrefabNames.ControlPad);

            var head = new GameObject("Console Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, PadCenterHeight, -0.02f);
            head.transform.localRotation = Quaternion.Euler(PadTilt, 0f, 0f);
            Vector3 hinge = head.transform.localPosition + head.transform.localRotation * LaserModels.MountHingeLocal;
            GameObject stand = AddModel(root.transform, "Stand", "Console Stand", () => LaserModels.ConsoleStand("Console Stand", hinge));
            stand.transform.SetAsFirstSibling();
            var consoleSize = new Vector2(s_PadSize.x * PadCanvasScale + 0.012f, s_PadSize.y * PadCanvasScale + 0.012f);
            AddModel(head.transform, "Console Model", "Control Pad Console", () => LaserModels.MonitorHead("Control Pad Console", consoleSize));

            // World-space canvas: 1 canvas unit = 1 mm.
            var canvasObject = new GameObject("Pad Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(head.transform, false);
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = s_PadSize;
            canvasRect.localPosition = new Vector3(0f, 0f, -0.0125f);
            canvasRect.localScale = Vector3.one * PadCanvasScale;
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            AddUiRaycaster(canvasObject);
            var pad = canvasObject.AddComponent<LaserControlPad>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            UiImage(canvasRect, "Background", Vector2.zero, s_PadSize, new Color(0.03f, 0.05f, 0.08f, 0.96f), null);
            // Top row: level name (left) and NEXT LEVEL (right).
            Text title = UiText(canvasRect, "Title", new Vector2(-122f, 285f), new Vector2(316f, 50f), "MIRROR ALIGNMENT", 21, font);
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            title.color = new Color(0.55f, 0.8f, 1f, 1f);
            UiButton(canvasRect, "Undo", new Vector2(-212f, 115f), new Vector2(128f, 80f), "UNDO", 24, LaserPadAction.Undo, font, rounded);
            UiButton(canvasRect, "Help", new Vector2(87f, 285f), new Vector2(86f, 48f), "HELP", 20, LaserPadAction.ShowBriefing, font, rounded);
            UiButton(canvasRect, "Next Level", new Vector2(212f, 285f), new Vector2(150f, 48f), "NEXT LEVEL", 20, LaserPadAction.NextLevel, font, rounded);

            UiButton(canvasRect, "Previous Mirror", new Vector2(-215f, 212f), new Vector2(120f, 80f), "PREV", 26, LaserPadAction.PreviousMirror, font, rounded);
            UiButton(canvasRect, "Next Mirror", new Vector2(215f, 212f), new Vector2(120f, 80f), "NEXT", 26, LaserPadAction.NextMirror, font, rounded);
            Text mirrorName = UiText(canvasRect, "Selected Mirror", new Vector2(0f, 212f), new Vector2(290f, 80f), "End mirror X", 30, font);

            // The D-pad: tilt up/down (pitch) and left/right (yaw); centre switches coarse/fine steps.
            UiButton(canvasRect, "Pitch Up", new Vector2(0f, 115f), new Vector2(150f, 100f), "\u25B2", 56, LaserPadAction.PitchUp, font, rounded);
            UiButton(canvasRect, "Pitch Down", new Vector2(0f, -115f), new Vector2(150f, 100f), "\u25BC", 56, LaserPadAction.PitchDown, font, rounded);
            UiButton(canvasRect, "Yaw Left", new Vector2(-165f, 0f), new Vector2(150f, 100f), "\u25C4", 56, LaserPadAction.YawLeft, font, rounded);
            UiButton(canvasRect, "Yaw Right", new Vector2(165f, 0f), new Vector2(150f, 100f), "\u25BA", 56, LaserPadAction.YawRight, font, rounded);
            Text stepMode = UiButton(canvasRect, "Step Size", Vector2.zero, new Vector2(150f, 100f), "COARSE", 24, LaserPadAction.ToggleFineMode, font, rounded);

            Text angles = UiText(canvasRect, "Angles", new Vector2(0f, -200f), new Vector2(560f, 40f), "yaw +0.00\u00b0    pitch +0.00\u00b0", 24, font);
            angles.color = new Color(0.6f, 0.75f, 0.85f, 1f);

            // The LASER (wavelength) and IR VIEWER buttons are retired: the laser is always the visible 532 nm one.
            UiButton(canvasRect, "Reset Mirror", new Vector2(-71f, -268f), new Vector2(132f, 72f), "RESET\nMIRROR", 19, LaserPadAction.ResetMirror, font, rounded);
            Text shutter = UiButton(canvasRect, "Laser Shutter", new Vector2(71f, -268f), new Vector2(132f, 72f), "SHUTTER", 19, LaserPadAction.ToggleShutter, font, rounded);

            Configure(pad, so =>
            {
                so.FindProperty("mirrorNameText").objectReferenceValue = mirrorName;
                so.FindProperty("stepModeText").objectReferenceValue = stepMode;
                so.FindProperty("anglesText").objectReferenceValue = angles;
                so.FindProperty("shutterText").objectReferenceValue = shutter;
                so.FindProperty("levelText").objectReferenceValue = title;
            });

            return root;
        }

        /// <summary>
        /// XR controller rays need the XR Interaction Toolkit's Tracked Device Graphic Raycaster on the canvas.
        /// It is added by name so the kit also compiles in projects without the toolkit (then the mouse works).
        /// </summary>
        private static void AddUiRaycaster(GameObject canvasObject)
        {
            const string trackedRaycaster = "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster";
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(trackedRaycaster);
                if (type != null)
                {
                    canvasObject.AddComponent(type);
                    return;
                }
            }

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        private static RectTransform UiRect(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image UiImage(RectTransform parent, string name, Vector2 position, Vector2 size, Color color, Sprite sprite)
        {
            RectTransform rect = UiRect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        private static Text UiText(RectTransform parent, string name, Vector2 position, Vector2 size, string value, int fontSize, Font font)
        {
            RectTransform rect = UiRect(parent, name, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = s_PadTextColor;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        /// <summary>A pad button: rounded image + UI Button (hover/press colours) + LaserPadButton (the action). Returns its label.</summary>
        private static Text UiButton(RectTransform parent, string name, Vector2 position, Vector2 size, string label, int fontSize,
            LaserPadAction action, Font font, Sprite sprite)
        {
            Text text = UiButtonBase(parent, name, position, size, label, fontSize, font, sprite, s_PadButtonColor, out Image image);
            var padButton = image.gameObject.AddComponent<LaserPadButton>();
            Configure(padButton, so => so.FindProperty("action").enumValueIndex = (int)action);
            return text;
        }

        /// <summary>Rounded image + UI Button (hover / press colours) + label. The caller adds what the button does.</summary>
        private static Text UiButtonBase(RectTransform parent, string name, Vector2 position, Vector2 size, string label, int fontSize,
            Font font, Sprite sprite, Color normalColor, out Image image)
        {
            image = UiImage(parent, name, position, size, Color.white, sprite);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normalColor;
            colors.highlightedColor = new Color(0.2f, 0.42f, 0.6f, 1f);
            colors.pressedColor = new Color(0.25f, 0.85f, 0.45f, 1f);
            colors.selectedColor = normalColor;
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return UiText((RectTransform)image.transform, "Label", Vector2.zero, size, label, fontSize, font);
        }

        // ----- Beam cameras -------------------------------------------------------------------------

        private const float CameraViewRadius = 0.05f;      // round view on the screen (m)
        private const float CameraFieldHalfWidth = 0.035f; // shows +-35 mm of the real iris face (the plate is +-42 mm)

        /// <summary>
        /// Second screen on a dual-screen bracket above the monitor with four "beam camera" views
        /// (close-ups of the irises), laid out at runtime by <see cref="LaserBeamCameraScreen"/>.
        /// </summary>
        private static void BuildBeamCameraScreen(Transform monitorHead)
        {
            var screenRoot = new GameObject("Beam Camera Screen");
            screenRoot.transform.SetParent(monitorHead, false);
            screenRoot.transform.localPosition = new Vector3(0f, s_ScreenSize.y + 0.016f, 0f);
            AddModel(screenRoot.transform, "Camera Screen Model", "Camera Screen Head", () => LaserModels.MonitorHead("Camera Screen Head", s_ScreenSize, false));
            float headerY = s_ScreenSize.y * 0.5f - 0.016f;
            AddModel(screenRoot.transform, "Header Bar", "Camera Screen Header", () =>
            {
                var b = new MeshBuilder(1);
                b.AddBox(0, new Vector3(0f, headerY, -0.0116f), new Vector3(s_ScreenSize.x - 0.008f, 0.024f, 0.0002f), Quaternion.identity);
                return new ModelPart(b.ToMesh("Camera Screen Header"), new[] { ModelMaterial.ScreenHeader });
            });

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform headerCanvas = WorldCanvas(screenRoot.transform, "Header Text", new Vector3(0f, headerY, -0.0123f), new Vector2(s_ScreenSize.x - 0.03f, 0.024f), 0.00025f);
            Text header = UiText(headerCanvas, "Header", Vector2.zero, headerCanvas.sizeDelta, "BEAM CAMERAS", 40, font);
            header.alignment = TextAnchor.MiddleLeft;
            header.color = new Color(0.8f, 0.9f, 1f, 1f);

            float holeFraction = IrisHoleRadius / CameraFieldHalfWidth;
            float goalFraction = 0.01f / CameraFieldHalfWidth;
            var views = new LaserBeamCameraView[4];
            for (int i = 0; i < views.Length; i++)
            {
                var viewObject = new GameObject("Camera View " + (i + 1));
                viewObject.transform.SetParent(screenRoot.transform, false);
                // 2 x 2 grid in the editor; the level manager re-arranges the views for 1, 2 or 4 irises.
                float areaW = s_ScreenSize.x - 0.02f, areaH = s_ScreenSize.y - 0.04f;
                viewObject.transform.localPosition = new Vector3((i % 2 == 0 ? -0.25f : 0.25f) * areaW, -0.014f + (i < 2 ? 0.25f : -0.25f) * areaH, 0f);
                var graphicsObject = AddModel(viewObject.transform, "Graphics", "Beam Camera Graphics",
                    () => LaserModels.BeamCameraGraphics("Beam Camera Graphics", CameraViewRadius, holeFraction, goalFraction));
                graphicsObject.transform.localPosition = new Vector3(-0.055f, 0f, -0.0116f);
                GameObject halo = AddModel(viewObject.transform, "Beam Halo", "Camera Spot", () => LaserModels.ScreenMarker("Camera Spot", 1f));
                halo.transform.localPosition = new Vector3(-0.055f, 0f, -0.0119f);
                GameObject spot = AddModel(viewObject.transform, "Beam Spot", "Camera Spot", () => LaserModels.ScreenMarker("Camera Spot", 1f));
                spot.transform.localPosition = new Vector3(-0.055f, 0f, -0.0121f);

                // The spot and halo move inside the round view: give them a parent at the view centre.
                var plot = new GameObject("Plot");
                plot.transform.SetParent(viewObject.transform, false);
                plot.transform.localPosition = new Vector3(-0.055f, 0f, 0f);
                halo.transform.SetParent(plot.transform, false);
                halo.transform.localPosition = new Vector3(0f, 0f, -0.0119f);
                spot.transform.SetParent(plot.transform, false);
                spot.transform.localPosition = new Vector3(0f, 0f, -0.0121f);
                spot.transform.localScale = Vector3.one * 0.004f;   // unit-radius disc: sized at runtime
                halo.transform.localScale = Vector3.one * 0.009f;
                spot.SetActive(false);                              // shown when a beam hits the target
                halo.SetActive(false);

                RectTransform textCanvas = WorldCanvas(viewObject.transform, "View Text", new Vector3(0.062f, 0f, -0.0123f), new Vector2(0.115f, 0.1f), 0.0005f);
                Text label = UiText(textCanvas, "Label", new Vector2(0f, 62f), new Vector2(230f, 40f), "IRIS", 30, font);
                label.fontStyle = FontStyle.Bold;
                Text position = UiText(textCanvas, "Position", new Vector2(0f, 2f), new Vector2(230f, 52f), "---", 17, font);
                Text state = UiText(textCanvas, "State", new Vector2(0f, -52f), new Vector2(230f, 40f), "NO BEAM", 22, font);
                state.fontStyle = FontStyle.Bold;
                foreach (Text t in new[] { label, position, state })
                {
                    t.alignment = TextAnchor.MiddleLeft;
                }

                label.color = new Color(0.8f, 0.9f, 1f, 1f);
                var view = viewObject.AddComponent<LaserBeamCameraView>();
                Configure(view, so =>
                {
                    so.FindProperty("fieldHalfWidth").floatValue = CameraFieldHalfWidth;
                    so.FindProperty("viewRadius").floatValue = CameraViewRadius;
                    so.FindProperty("spot").objectReferenceValue = spot.transform;
                    so.FindProperty("halo").objectReferenceValue = halo.transform;
                    so.FindProperty("labelText").objectReferenceValue = label;
                    so.FindProperty("positionText").objectReferenceValue = position;
                    so.FindProperty("stateText").objectReferenceValue = state;
                });
                views[i] = view;
            }

            var cameraScreen = screenRoot.AddComponent<LaserBeamCameraScreen>();
            Configure(cameraScreen, so =>
            {
                SetObjectArray(so.FindProperty("views"), views);
                so.FindProperty("area").vector2Value = new Vector2(s_ScreenSize.x - 0.02f, s_ScreenSize.y - 0.04f);
                so.FindProperty("areaCenter").vector2Value = new Vector2(0f, -0.014f);
                so.FindProperty("headerText").objectReferenceValue = header;
            });
        }

        /// <summary>A small world-space canvas (not clickable). <paramref name="size"/> in metres, <paramref name="unit"/> metres per canvas unit.</summary>
        private static RectTransform WorldCanvas(Transform parent, string name, Vector3 localPosition, Vector2 size, float unit)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform));
            canvasObject.transform.SetParent(parent, false);
            var rect = (RectTransform)canvasObject.transform;
            rect.localPosition = localPosition;
            rect.sizeDelta = size / unit;
            rect.localScale = Vector3.one * unit;
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            return rect;
        }

        // ----- Mirror camera screen -----------------------------------------------------------------

        private const float CameraScreenHeight = 1.15f;
        private const float CameraScreenTilt = 15f;

        /// <summary>
        /// Monitor on a floor stand showing the live camera feed from the selected mirror (<see cref="LaserMirrorCamera"/>):
        /// title, the picture with a crosshair (where the beam goes) and a one-line hint. The camera itself is a child
        /// that moves to the selected mirror at runtime.
        /// </summary>
        private static GameObject BuildMirrorCameraScreen()
        {
            var root = new GameObject(PrefabNames.MirrorCameraScreen);
            var head = new GameObject("Screen Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, CameraScreenHeight, -0.06f);
            head.transform.localRotation = Quaternion.Euler(CameraScreenTilt, 0f, 0f);
            Vector3 hinge = head.transform.localPosition + head.transform.localRotation * LaserModels.MountHingeLocal;
            GameObject stand = AddModel(root.transform, "Stand", "Camera Screen Stand", () => LaserModels.MonitorStand("Camera Screen Stand", hinge));
            stand.transform.SetAsFirstSibling();
            AddModel(head.transform, "Monitor Model", "Monitor Head", () => LaserModels.MonitorHead("Monitor Head", s_ScreenSize));

            const float unit = 0.00025f;   // canvas units of 0.25 mm
            RectTransform canvas = WorldCanvas(head.transform, "Screen UI", new Vector3(0f, 0f, -0.0122f), s_ScreenSize, unit);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float mm = 0.001f / unit;
            Vector2 imageSize = new Vector2(0.46f, 0.259f) / unit;            // 16:9 like the render texture
            Vector2 imageCentre = new Vector2(0f, -0.016f) / unit;

            UiImage(canvas, "Header Bar", new Vector2(0f, (s_ScreenSize.y * 0.5f - 0.014f) / unit), new Vector2((s_ScreenSize.x - 0.008f) / unit, 0.022f / unit),
                new Color(0.04f, 0.09f, 0.15f, 1f), null).raycastTarget = false;
            Text title = UiText(canvas, "Title", new Vector2(0f, (s_ScreenSize.y * 0.5f - 0.014f) / unit), new Vector2((s_ScreenSize.x - 0.03f) / unit, 0.022f / unit),
                "MIRROR CAMERA", Mathf.RoundToInt(11f * mm), font);
            title.alignment = TextAnchor.MiddleLeft;
            title.color = new Color(0.8f, 0.9f, 1f, 1f);

            var feedObject = new GameObject("Camera Feed", typeof(RectTransform));
            feedObject.transform.SetParent(canvas, false);
            var feedRect = (RectTransform)feedObject.transform;
            feedRect.anchoredPosition = imageCentre;
            feedRect.sizeDelta = imageSize;
            var feed = feedObject.AddComponent<RawImage>();
            feed.raycastTarget = false;

            Color cross = new Color(0.3f, 0.85f, 1f, 0.75f);
            float arm = 0.03f / unit, thick = 0.0012f / unit, gap = 0.006f / unit;
            foreach (int side in new[] { -1, 1 })
            {
                UiImage(canvas, "Crosshair H" + side, imageCentre + new Vector2(side * (gap + arm * 0.5f), 0f), new Vector2(arm, thick), cross, null).raycastTarget = false;
                UiImage(canvas, "Crosshair V" + side, imageCentre + new Vector2(0f, side * (gap + arm * 0.5f)), new Vector2(thick, arm), cross, null).raycastTarget = false;
            }

            Vector2 hintPos = imageCentre + new Vector2(0f, -imageSize.y * 0.5f + 0.011f / unit);
            UiImage(canvas, "Hint Bar", hintPos, new Vector2(imageSize.x, 0.022f / unit), new Color(0f, 0f, 0f, 0.6f), null).raycastTarget = false;
            Text hint = UiText(canvas, "Hint", hintPos, new Vector2(imageSize.x - 0.01f / unit, 0.022f / unit), "", Mathf.RoundToInt(8.5f * mm), font);
            hint.color = new Color(0.85f, 0.92f, 1f, 1f);

            var cameraObject = new GameObject("Mirror Camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            var mirrorCamera = root.AddComponent<LaserMirrorCamera>();
            Configure(mirrorCamera, so =>
            {
                so.FindProperty("feedCamera").objectReferenceValue = camera;
                so.FindProperty("feedImage").objectReferenceValue = feed;
                so.FindProperty("titleText").objectReferenceValue = title;
                so.FindProperty("hintText").objectReferenceValue = hint;
            });
            return root;
        }

        // ----- Results card -------------------------------------------------------------------------

        private static readonly Vector2 s_ResultsSize = new Vector2(1000f, 700f);   // canvas units = millimetres

        /// <summary>
        /// Floating card shown when a level is completed (same spot as the briefing screen): title, three
        /// star models, time, safety incidents, personal best, a tip or "new best" banner and RETRY / CLOSE / NEXT.
        /// </summary>
        private static LaserResultsCard BuildResultsCard(Transform parent)
        {
            var root = new GameObject("Results Card (floating)");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = s_ResultsPosition;
            var card = root.AddComponent<LaserResultsCard>();

            var screen = new GameObject("Card");
            screen.transform.SetParent(root.transform, false);

            var canvasObject = new GameObject("Card Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(screen.transform, false);
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = s_ResultsSize;
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            AddUiRaycaster(canvasObject);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Color accent = new Color(0.35f, 0.75f, 1f, 1f);
            Color gold = new Color(1f, 0.8f, 0.25f, 1f);
            UiImage(canvasRect, "Frame", Vector2.zero, s_ResultsSize + new Vector2(14f, 14f), new Color(gold.r, gold.g, gold.b, 0.6f), rounded);
            UiImage(canvasRect, "Background", Vector2.zero, s_ResultsSize, new Color(0.02f, 0.04f, 0.07f, 0.95f), rounded);

            Text title = UiText(canvasRect, "Title", new Vector2(0f, 300f), new Vector2(900f, 60f), "LEVEL COMPLETE", 46, font);
            title.fontStyle = FontStyle.Bold;
            title.color = gold;
            Text subtitle = UiText(canvasRect, "Subtitle", new Vector2(0f, 248f), new Vector2(900f, 40f), "", 26, font);
            subtitle.color = new Color(0.6f, 0.7f, 0.8f, 1f);

            var stars = new Renderer[3];
            for (int i = 0; i < stars.Length; i++)
            {
                GameObject star = AddModel(screen.transform, "Star " + (i + 1), "Results Star", () => LaserModels.Star("Results Star"));
                star.transform.localPosition = new Vector3((i - 1) * 0.13f, i == 1 ? 0.175f : 0.16f, -0.012f);
                star.transform.localScale = Vector3.one * (i == 1 ? 0.06f : 0.05f);
                stars[i] = star.GetComponent<Renderer>();
            }

            Text Row(string name, float y, string caption)
            {
                Text label = UiText(canvasRect, name + " Label", new Vector2(-290f, y), new Vector2(320f, 50f), caption, 28, font);
                label.alignment = TextAnchor.MiddleLeft;
                label.color = new Color(0.55f, 0.65f, 0.75f, 1f);
                Text value = UiText(canvasRect, name, new Vector2(160f, y), new Vector2(600f, 56f), "", 30, font);
                value.alignment = TextAnchor.MiddleLeft;
                value.horizontalOverflow = HorizontalWrapMode.Wrap;
                return value;
            }

            Text time = Row("Time", 60f, "TIME");
            Text incidents = Row("Incidents", 0f, "SAFETY INCIDENTS");
            Text best = Row("Best", -60f, "PERSONAL BEST");
            Text banner = UiText(canvasRect, "Banner", new Vector2(0f, -150f), new Vector2(900f, 70f), "", 28, font);
            banner.color = gold;
            banner.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiResultsButton(canvasRect, "Retry", new Vector2(-330f, -275f), new Vector2(230f, 72f), "RETRY", LaserResultsButton.ResultsAction.Retry, font, rounded, s_PadButtonColor);
            UiResultsButton(canvasRect, "Close", new Vector2(0f, -275f), new Vector2(200f, 72f), "CLOSE", LaserResultsButton.ResultsAction.Close, font, rounded, s_PadButtonColor);
            Text next = UiResultsButton(canvasRect, "Next", new Vector2(320f, -275f), new Vector2(270f, 72f), "NEXT LEVEL  \u25BA", LaserResultsButton.ResultsAction.Next, font, rounded, new Color(0.12f, 0.36f, 0.22f, 1f));

            screen.SetActive(false);
            Configure(card, so =>
            {
                so.FindProperty("screen").objectReferenceValue = screen;
                so.FindProperty("titleText").objectReferenceValue = title;
                so.FindProperty("subtitleText").objectReferenceValue = subtitle;
                so.FindProperty("timeText").objectReferenceValue = time;
                so.FindProperty("incidentsText").objectReferenceValue = incidents;
                so.FindProperty("bestText").objectReferenceValue = best;
                so.FindProperty("bannerText").objectReferenceValue = banner;
                so.FindProperty("nextButtonText").objectReferenceValue = next;
                SetObjectArray(so.FindProperty("stars"), stars);
            });
            return card;
        }

        private static Text UiResultsButton(RectTransform parent, string name, Vector2 position, Vector2 size, string label,
            LaserResultsButton.ResultsAction action, Font font, Sprite sprite, Color color)
        {
            Text text = UiButtonBase(parent, name, position, size, label, 28, font, sprite, color, out Image image);
            var button = image.gameObject.AddComponent<LaserResultsButton>();
            Configure(button, so => so.FindProperty("action").enumValueIndex = (int)action);
            return text;
        }

        [MenuItem("Tools/Einstein Telescope/Laser/Levels/Reset Personal Bests", priority = 40)]
        public static void ResetPersonalBests()
        {
            for (int i = 0; i < 3; i++)
            {
                foreach (string title in new[] { "Michelson", "Beam walking", "Folded arms", "Long arms" })
                {
                    LaserBestTimes.Clear("L" + (i + 1) + "_" + title);
                }
            }

            GameObject demo = GameObject.Find(DemoRootName);
            LaserLevelManager manager = demo != null ? demo.GetComponentInChildren<LaserLevelManager>() : null;
            if (manager != null)
            {
                for (int i = 0; i < manager.LevelCount; i++)
                {
                    LaserBestTimes.Clear(manager.LevelKey(i));
                }
            }

            EditorUtility.DisplayDialog("Laser levels", "Personal bests cleared.", "OK");
        }

        // ----- Name tags ----------------------------------------------------------------------------

        /// <summary>
        /// Floating name tag (world-space UI, 1 unit = 1 mm) that turns towards the player. Text: <paramref name="text"/>,
        /// else the mirror's display name, else the name of the object it sits under. Not clickable.
        /// </summary>
        private static void AddLabel(Transform parent, Vector3 localPosition, MirrorMount mount, string text, Renderer ring)
        {
            var labelObject = new GameObject("Name Tag", typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);
            var rect = (RectTransform)labelObject.transform;
            rect.localPosition = localPosition;
            rect.sizeDelta = new Vector2(800f, 90f);
            rect.localScale = Vector3.one * 0.0014f;   // 50 units -> 7 cm text, readable from the control room
            labelObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            labelObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Image background = UiImage(rect, "Background", Vector2.zero, new Vector2(320f, 76f), new Color(0.02f, 0.04f, 0.07f, 0.6f), rounded);
            background.raycastTarget = false;
            Text label = UiText(rect, "Text", Vector2.zero, new Vector2(800f, 76f), text, 50, font);

            var tag = labelObject.AddComponent<LaserOpticLabel>();
            Configure(tag, so =>
            {
                so.FindProperty("mount").objectReferenceValue = mount;
                so.FindProperty("labelText").stringValue = text ?? "";
                so.FindProperty("text").objectReferenceValue = label;
                so.FindProperty("background").objectReferenceValue = background;
                so.FindProperty("selectionRing").objectReferenceValue = ring;
            });
        }

        // ----- Briefing screen --------------------------------------------------------------------

        private static readonly Vector2 s_BriefingSize = new Vector2(1400f, 900f);   // canvas units = millimetres
        // The player stands at (0, 0, -3) in demo space. The briefing floats to the right, 2 m away and 72 deg from
        // straight ahead, turned towards the player: clear of every beam (all within 45 deg) and of the monitors (up to 54 deg),
        // so HELP can stay open while steering. The results card appears straight ahead (the level is done then).
        private static readonly Vector3 s_BriefingPosition = new Vector3(1.90f, 1.55f, -2.38f);
        private const float BriefingYaw = 72f;
        private const float BriefingScale = 0.0009f;   // 1400 x 900 canvas units -> 1.26 x 0.81 m
        private static readonly Vector3 s_ResultsPosition = new Vector3(0f, 1.65f, -1.4f);

        /// <summary>
        /// Big floating screen with three pages (objective, controls, physics), tabs, BACK, NEXT / START and a close
        /// button. Clickable with the XR controller rays and the mouse. Texts are filled per level at runtime.
        /// </summary>
        private static LaserBriefingPanel BuildBriefingScreen(Transform parent)
        {
            var root = new GameObject("Briefing Screen (floating)");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = s_BriefingPosition;
            root.transform.localRotation = Quaternion.Euler(0f, BriefingYaw, 0f);
            var panel = root.AddComponent<LaserBriefingPanel>();

            var canvasObject = new GameObject("Screen", typeof(RectTransform));
            canvasObject.transform.SetParent(root.transform, false);
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = s_BriefingSize;
            canvasRect.localScale = Vector3.one * BriefingScale;
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            AddUiRaycaster(canvasObject);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Color accent = new Color(0.35f, 0.75f, 1f, 1f);
            float halfX = s_BriefingSize.x * 0.5f, halfY = s_BriefingSize.y * 0.5f;

            UiImage(canvasRect, "Frame", Vector2.zero, s_BriefingSize + new Vector2(14f, 14f), new Color(accent.r, accent.g, accent.b, 0.55f), rounded);
            UiImage(canvasRect, "Background", Vector2.zero, s_BriefingSize, new Color(0.02f, 0.04f, 0.07f, 0.95f), rounded);

            Text title = UiText(canvasRect, "Title", new Vector2(-70f, halfY - 52f), new Vector2(s_BriefingSize.x - 220f, 60f), "LEVEL 1/3", 40, font);
            title.alignment = TextAnchor.MiddleLeft;
            title.fontStyle = FontStyle.Bold;
            title.color = accent;
            UiBriefingButton(canvasRect, "Close", new Vector2(halfX - 60f, halfY - 52f), new Vector2(80f, 70f), "X", 38,
                LaserBriefingButton.BriefingAction.Close, font, rounded, new Color(0.45f, 0.12f, 0.1f, 1f), out _);

            // Tabs.
            var tabs = new Image[LaserBriefingPanel.PageCount];
            string[] tabLabels = { "1  OBJECTIVE", "2  CONTROLS", "3  THE PHYSICS" };
            var tabActions = new[] { LaserBriefingButton.BriefingAction.ShowObjective, LaserBriefingButton.BriefingAction.ShowControls, LaserBriefingButton.BriefingAction.ShowPhysics };
            for (int i = 0; i < tabs.Length; i++)
            {
                UiBriefingButton(canvasRect, "Tab " + tabLabels[i].Substring(3), new Vector2(-440f + i * 440f, halfY - 130f), new Vector2(420f, 64f), tabLabels[i], 28,
                    tabActions[i], font, rounded, Color.white, out tabs[i]);
            }

            Text body = UiText(canvasRect, "Body", new Vector2(0f, -40f), new Vector2(s_BriefingSize.x - 80f, 640f), "", 25, font);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.1f;

            // Footer.
            UiBriefingButton(canvasRect, "Back", new Vector2(-halfX + 150f, -halfY + 55f), new Vector2(240f, 70f), "\u25C4  BACK", 28,
                LaserBriefingButton.BriefingAction.PreviousPage, font, rounded, s_PadButtonColor, out _);
            Text pageLabel = UiText(canvasRect, "Page", new Vector2(0f, -halfY + 55f), new Vector2(500f, 50f), "1 / 3", 24, font);
            pageLabel.color = new Color(0.55f, 0.65f, 0.75f, 1f);
            Text next = UiBriefingButton(canvasRect, "Next", new Vector2(halfX - 150f, -halfY + 55f), new Vector2(240f, 70f), "NEXT  \u25BA", 28,
                LaserBriefingButton.BriefingAction.NextPage, font, rounded, new Color(0.12f, 0.36f, 0.22f, 1f), out _);

            canvasObject.SetActive(false);   // opened by the level manager when a level starts
            Configure(panel, so =>
            {
                so.FindProperty("screen").objectReferenceValue = canvasObject;
                so.FindProperty("titleText").objectReferenceValue = title;
                so.FindProperty("bodyText").objectReferenceValue = body;
                so.FindProperty("pageText").objectReferenceValue = pageLabel;
                so.FindProperty("nextButtonText").objectReferenceValue = next;
                SetObjectArray(so.FindProperty("tabImages"), tabs);
                so.FindProperty("controlsText").stringValue = LaserLevelTexts.Controls;
            });
            return panel;
        }

        private static Text UiBriefingButton(RectTransform parent, string name, Vector2 position, Vector2 size, string label, int fontSize,
            LaserBriefingButton.BriefingAction action, Font font, Sprite sprite, Color color, out Image image)
        {
            Text text = UiButtonBase(parent, name, position, size, label, fontSize, font, sprite, color, out image);
            var button = image.gameObject.AddComponent<LaserBriefingButton>();
            Configure(button, so => so.FindProperty("action").enumValueIndex = (int)action);
            return text;
        }

        // ----- Models -----------------------------------------------------------------------------

        /// <summary>Adds a child with a generated mesh (saved as an asset in Models/) and its materials.</summary>
        private static GameObject AddModel(Transform parent, string childName, string meshName, System.Func<ModelPart> create)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(parent, false);

            string meshPath = ModelsFolder + "/" + meshName + ".asset";
            ModelPart part = create();
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = part.Mesh;
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else if (mesh != part.Mesh)
            {
                // Update the existing asset in place so references (and its GUID) stay valid.
                EditorUtility.CopySerialized(part.Mesh, mesh);
                Object.DestroyImmediate(part.Mesh);
            }

            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = child.AddComponent<MeshRenderer>();
            var materials = new Material[part.Materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = MaterialFor(part.Materials[i]);
            }

            meshRenderer.sharedMaterials = materials;
            bool isScreenGraphic = part.Materials.Length > 0 && (part.Materials[0] == ModelMaterial.Indicator || part.Materials[0] == ModelMaterial.ScreenLines);
            meshRenderer.shadowCastingMode = isScreenGraphic ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return child;
        }

        private static void AddColliderBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        private static Material MaterialFor(ModelMaterial slot)
        {
            switch (slot)
            {
                case ModelMaterial.FusedSilica: return TransparentLitMaterial("Fused Silica", new Color(0.82f, 0.92f, 0.97f, 0.28f), 0.95f);
                case ModelMaterial.HrCoating: return LitMaterial("HR Coating", new Color(0.86f, 0.82f, 0.95f), 1f, 0.95f);
                case ModelMaterial.Aluminium: return LitMaterial("Machined Aluminium", new Color(0.78f, 0.79f, 0.8f), 1f, 0.65f);
                case ModelMaterial.Steel: return LitMaterial("Stainless Steel", new Color(0.62f, 0.63f, 0.66f), 1f, 0.8f);
                case ModelMaterial.AnodizedBlack: return LitMaterial("Anodized Black", new Color(0.035f, 0.035f, 0.04f), 0.2f, 0.5f);
                case ModelMaterial.HousingPaint: return LitMaterial("Laser Housing Paint", new Color(0.07f, 0.18f, 0.42f), 0.1f, 0.6f);
                case ModelMaterial.WarningYellow: return LitMaterial("Warning Yellow", new Color(1f, 0.78f, 0.02f), 0f, 0.5f);
                case ModelMaterial.WarningBlack: return LitMaterial("Warning Black", new Color(0.01f, 0.01f, 0.01f), 0f, 0.5f);
                case ModelMaterial.Fibre: return LitMaterial("Silica Fibre", new Color(0.95f, 0.97f, 1f), 0f, 0.9f);
                case ModelMaterial.Copper: return LitMaterial("Coil Copper", new Color(0.85f, 0.45f, 0.25f), 1f, 0.7f);
                case ModelMaterial.Sensor: return LitMaterial("Silicon Sensor", new Color(0.12f, 0.08f, 0.3f), 0.6f, 0.9f);
                case ModelMaterial.BlackGlass: return LitMaterial("Black Glass", new Color(0.004f, 0.004f, 0.005f), 0f, 0.97f);
                case ModelMaterial.Gold: return LitMaterial("Gold Contacts", new Color(0.9f, 0.7f, 0.3f), 1f, 0.75f);
                case ModelMaterial.FramePaint: return LitMaterial("Frame Paint", new Color(0.22f, 0.31f, 0.4f), 0.3f, 0.5f);
                case ModelMaterial.Rubber: return LitMaterial("Rubber", new Color(0.02f, 0.02f, 0.022f), 0f, 0.25f);
                case ModelMaterial.MonitorPlastic: return LitMaterial("Monitor Plastic", new Color(0.05f, 0.055f, 0.06f), 0f, 0.45f);
                case ModelMaterial.ScreenGlass: return UnlitMaterial("Screen Glass", new Color(0.008f, 0.014f, 0.03f));
                case ModelMaterial.ScreenLines: return UnlitMaterial("Screen Graphics Lines", new Color(0.25f, 0.45f, 0.55f));
                case ModelMaterial.ScreenZone: return UnlitMaterial("Screen Graphics Zone", new Color(0.04f, 0.22f, 0.08f));
                case ModelMaterial.ScreenHeader: return UnlitMaterial("Screen Panel", new Color(0.04f, 0.09f, 0.15f));
                default: return UnlitMaterial("Indicator", Color.white);
            }
        }

        // ----- Geometry helpers (table and enclosure) ---------------------------------------------

        private static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        private static void Configure(Object component, System.Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(component);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ----- Materials --------------------------------------------------------------------------

        private static Material BeamMaterial(bool radial)
        {
            string name = radial ? "Laser Spot" : "Laser Beam";
            return GetOrCreateMaterial(name, () =>
            {
                Shader shader = Shader.Find(BeamShaderName);
                if (shader == null)
                {
                    Debug.LogWarning("Laser kit: shader '" + BeamShaderName + "' not found. Is the URP package installed? Using Sprites/Default.");
                    shader = Shader.Find("Sprites/Default");
                }

                var material = new Material(shader);
                if (material.HasProperty("_Radial"))
                {
                    material.SetFloat("_Radial", radial ? 1f : 0f);
                }

                return material;
            });
        }

        private static Material LitMaterial(string name, Color color, float metallic, float smoothness)
        {
            return GetOrCreateMaterial(name, () => CreateLit(color, metallic, smoothness));
        }

        private static Material CreateLit(Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetFloatIfPresent(material, "_Metallic", metallic);
            SetFloatIfPresent(material, "_Smoothness", smoothness);
            SetFloatIfPresent(material, "_Glossiness", smoothness);
            return material;
        }

        /// <summary>URP Lit set to Transparent (alpha blended), e.g. for fused-silica glass.</summary>
        private static Material TransparentLitMaterial(string name, Color color, float smoothness)
        {
            return GetOrCreateMaterial(name, () =>
            {
                Material material = CreateLit(color, 0f, smoothness);
                if (!material.HasProperty("_Surface"))
                {
                    return material; // Not URP: stays opaque.
                }

                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
                SetFloatIfPresent(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("DepthOnly", false);
                return material;
            });
        }

        private static Material UnlitMaterial(string name, Color color)
        {
            return GetOrCreateMaterial(name, () =>
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                var material = new Material(shader);
                SetColorIfPresent(material, "_BaseColor", color);
                SetColorIfPresent(material, "_Color", color);
                return material;
            });
        }

        private static Material GetOrCreateMaterial(string name, System.Func<Material> create)
        {
            EnsureFolder(MaterialsFolder);
            string path = MaterialsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = create();
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetColorIfPresent(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        // ----- Paths ------------------------------------------------------------------------------

        private static string PrefabPath(string prefabName)
        {
            return PrefabsFolder + "/" + prefabName + ".prefab";
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}

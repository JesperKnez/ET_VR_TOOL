using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace ETVRTool.CraneLevel.EditorTools
{
    /// <summary>
    /// Builds the "Align the beam pipes" crane level, based on the photos of the ETpathfinder cleanroom:
    /// white cleanroom hall with LED ceiling panels, cream overhead bridge crane ("2" hoist, stainless hook
    /// block, red 0.005t load display, barcode positioning strip), stainless vacuum towers with flanged
    /// ports, stainless beam pipes on blue stands, grey operator cabinet with an emergency stop.
    ///
    /// Menu: ET VR Tool > Build Crane Pipe-Alignment Level
    /// Everything is plain GameObjects afterwards, so you can move/replace anything by hand.
    /// </summary>
    public static class CraneLevelBuilder
    {
        const string Root = "Assets/CraneLevel";
        const string ScenePath = Root + "/Scenes/CraneLevel_PipeAlignment.unity";
        const float PPM = 2000f; // pixels per metre for world-space text

        // Layout (metres). Hall is 30 x 20 x 10 m, origin in the middle of the floor.
        const float HallX = 15f, HallZ = 10f, HallH = 10f;
        const float RailZ = 9.3f, RailTopY = 8.3f;
        const float PipeY = 1.4f;               // height of the beam-pipe axis at the tower ports
        const float TowerR = 1.1f, PortReach = 1.5f;

        static Font _font;
        static Dictionary<string, Material> _mats;
        static bool _urp;

        enum Ax { X, Y, Z }

        [MenuItem("ET VR Tool/Build Crane Pipe-Alignment Level")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root);
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mats = new Dictionary<string, Material>();
            _urp = GraphicsSettings.currentRenderPipeline != null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var env = new GameObject("Environment").transform;
            BuildHall(env);
            BuildProps(env);

            var towers = new GameObject("Towers").transform;
            BuildTower("Tower_A", new Vector3(6f, 0f, -4f), towers, Vector3.forward, Vector3.left);
            BuildTower("Tower_B", new Vector3(6f, 0f, 4f), towers, Vector3.back);
            BuildTower("Tower_C", new Vector3(-2f, 0f, -4f), towers, Vector3.right);

            var crane = BuildCrane(out var hook);

            // ---- Pipes in the storage rack (all lying along X) ----
            var pipes = new GameObject("Pipes").transform;
            var p1 = BuildPipe("P1", 2.5f, new Vector3(-9f, 0.9f, 2.0f), 0f, pipes);
            var p2 = BuildPipe("P2", 2.5f, new Vector3(-9f, 0.9f, 3.8f), 0f, pipes);
            var p3 = BuildPipe("P3", 5.0f, new Vector3(-9f, 0.9f, 5.8f), 0f, pipes);
            Stand(pipes, new Vector3(-9.8f, 0f, 2.0f), 0.6f, true);
            Stand(pipes, new Vector3(-8.2f, 0f, 2.0f), 0.6f, true);
            Stand(pipes, new Vector3(-9.8f, 0f, 3.8f), 0.6f, true);
            Stand(pipes, new Vector3(-8.2f, 0f, 3.8f), 0.6f, true);
            Stand(pipes, new Vector3(-10.8f, 0f, 5.8f), 0.6f, true);
            Stand(pipes, new Vector3(-7.2f, 0f, 5.8f), 0.6f, true);

            // ---- Where they have to go ----
            var targets = new GameObject("AlignmentTargets").transform;
            var t1 = BuildTarget("P1", 2.5f, new Vector3(6f, PipeY, -1.25f), 90f, targets, hook);
            var t2 = BuildTarget("P2", 2.5f, new Vector3(6f, PipeY, 1.25f), 90f, targets, hook);
            var t3 = BuildTarget("P3", 5.0f, new Vector3(2f, PipeY, -4f), 0f, targets, hook);
            Stand(targets, new Vector3(6f, 0f, -1.25f), PipeY - 0.3f, false);
            Stand(targets, new Vector3(6f, 0f, 1.25f), PipeY - 0.3f, false);
            Stand(targets, new Vector3(2f, 0f, -4f), PipeY - 0.3f, true);

            // ---- Level logic + operator station ----
            var levelGo = new GameObject("PipeLevelManager");
            var level = levelGo.AddComponent<PipeLevelManager>();
            level.crane = crane;
            level.hook = hook;
            level.steps = new List<PipeStep>
            {
                new PipeStep
                {
                    title = "Pipe P1 to Tower A", pipe = p1, target = t1,
                    instruction = "Drive the crane over pipe P1 in the storage rack (left, on the blue stands).\n" +
                                  "Lower the hook onto the yellow master link and press ATTACH.\n" +
                                  "Lift it clear, turn it 90 degrees and bring it to the blue ghost pipe at Tower A.\n" +
                                  "Use CREEP for the last centimetres. RELEASE when the ghost turns green."
                },
                new PipeStep
                {
                    title = "Pipe P2 to Tower B", pipe = p2, target = t2,
                    instruction = "Connect pipe P2 between pipe P1 and Tower B.\n" +
                                  "The flanges must meet exactly - watch the Side / Height / Angle readout."
                },
                new PipeStep
                {
                    title = "Long pipe P3: Tower A to Tower C", pipe = p3, target = t3,
                    instruction = "Pipe P3 is 5 m long. Lift it high enough to clear the towers and\n" +
                                  "line it up between Tower A and Tower C. Long loads need extra care!"
                },
            };

            BuildControlStation(crane, hook, level);
            BuildRig();

            var keyboard = new GameObject("KeyboardControls (Editor testing)").AddComponent<CraneKeyboardInput>();
            keyboard.crane = crane;
            keyboard.hook = hook;

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("[CraneLevel] Built and saved " + ScenePath);
        }

        // =====================================================================================
        // Hall
        // =====================================================================================

        static void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);

            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.7f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(65f, 30f, 0f);

            var lights = new GameObject("CeilingLights").transform;
            foreach (float x in new[] { -10f, 0f, 10f })
            foreach (float z in new[] { -5f, 5f })
            {
                var l = new GameObject("PointLight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.localPosition = new Vector3(x, HallH - 0.6f, z);
                l.type = LightType.Point;
                l.range = 14f;
                l.intensity = 2.2f;
                l.shadows = LightShadows.None;
            }

            // Stainless steel looks black without something to reflect.
            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0f, 3f, 0f);
            probe.size = new Vector3(HallX * 2f, HallH, HallZ * 2f);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        }

        static void BuildHall(Transform env)
        {
            var hall = new GameObject("CleanroomHall").transform;
            hall.SetParent(env, false);

            var floorMat = Mat("Floor_Epoxy", new Color(0.86f, 0.87f, 0.88f), 0f, 0.55f);
            var wallMat = Mat("Wall_Panel", new Color(0.93f, 0.94f, 0.95f), 0f, 0.35f);
            var seamMat = Mat("Wall_Seam", new Color(0.78f, 0.8f, 0.82f), 0f, 0.3f);
            var ceilMat = Mat("Ceiling", new Color(0.9f, 0.91f, 0.92f), 0f, 0.2f);
            var ledMat = Mat("LED_Panel", Color.white, 0f, 0.2f, new Color(1f, 1f, 1f) * 1.6f);
            var diffuserMat = Mat("LaminarFlowPanel", new Color(0.97f, 0.97f, 0.98f), 0f, 0.6f, new Color(1f, 1f, 1f) * 0.6f);

            // Floor (teleportable)
            var floor = Box("Floor", hall, new Vector3(0f, -0.05f, 0f), new Vector3(HallX * 2f, 0.1f, HallZ * 2f), floorMat, true);
            var tp = floor.AddComponent<TeleportationArea>();
            var teleportLayer = InteractionLayerMask.GetMask("Teleport");
            if (teleportLayer != 0) tp.interactionLayers = teleportLayer;

            // Walls
            var walls = new GameObject("Walls").transform;
            walls.SetParent(hall, false);
            walls.gameObject.AddComponent<CraneObstacle>();
            Box("Wall_North", walls, new Vector3(0f, HallH / 2f, HallZ + 0.1f), new Vector3(HallX * 2f, HallH, 0.2f), wallMat, true);
            Box("Wall_South", walls, new Vector3(0f, HallH / 2f, -HallZ - 0.1f), new Vector3(HallX * 2f, HallH, 0.2f), wallMat, true);
            Box("Wall_East", walls, new Vector3(HallX + 0.1f, HallH / 2f, 0f), new Vector3(0.2f, HallH, HallZ * 2f), wallMat, true);
            Box("Wall_West", walls, new Vector3(-HallX - 0.1f, HallH / 2f, 0f), new Vector3(0.2f, HallH, HallZ * 2f), wallMat, true);

            // Panel seams, like the cleanroom wall panels
            var seams = new GameObject("WallSeams").transform;
            seams.SetParent(hall, false);
            for (float x = -HallX + 1.2f; x < HallX; x += 1.2f)
            {
                Box("Seam", seams, new Vector3(x, HallH / 2f, HallZ - 0.005f), new Vector3(0.015f, HallH, 0.01f), seamMat);
                Box("Seam", seams, new Vector3(x, HallH / 2f, -HallZ + 0.005f), new Vector3(0.015f, HallH, 0.01f), seamMat);
            }
            for (float z = -HallZ + 1.2f; z < HallZ; z += 1.2f)
            {
                Box("Seam", seams, new Vector3(HallX - 0.005f, HallH / 2f, z), new Vector3(0.01f, HallH, 0.015f), seamMat);
                Box("Seam", seams, new Vector3(-HallX + 0.005f, HallH / 2f, z), new Vector3(0.01f, HallH, 0.015f), seamMat);
            }

            // Ceiling with square LED panels and a few big laminar-flow diffusers
            Box("Ceiling", hall, new Vector3(0f, HallH + 0.1f, 0f), new Vector3(HallX * 2f, 0.2f, HallZ * 2f), ceilMat);
            var leds = new GameObject("LEDPanels").transform;
            leds.SetParent(hall, false);
            for (float x = -12.5f; x <= 12.5f; x += 2.5f)
            for (float z = -7.5f; z <= 7.5f; z += 2.5f)
                Box("LED", leds, new Vector3(x, HallH - 0.01f, z), new Vector3(0.6f, 0.02f, 0.6f), ledMat);
            foreach (var p in new[] { new Vector3(-6f, 0f, -3.75f), new Vector3(-6f, 0f, 3.75f), new Vector3(6f, 0f, -6.25f), new Vector3(6f, 0f, 6.25f) })
                Box("LaminarFlow", leds, new Vector3(p.x, HallH - 0.02f, p.z), new Vector3(2.4f, 0.03f, 1.6f), diffuserMat);

            // Warning signs (photo: "OVERHEAD CRANE - STAY OUT FROM UNDER SUSPENDED LOADS")
            WarningSign(hall, new Vector3(0f, 6.6f, HallZ - 0.02f), Quaternion.Euler(0f, 0f, 0f));
            WarningSign(hall, new Vector3(-HallX + 0.02f, 6.6f, 0f), Quaternion.Euler(0f, -90f, 0f));
            WarningSign(hall, new Vector3(HallX - 0.02f, 6.6f, 0f), Quaternion.Euler(0f, 90f, 0f));
        }

        static void WarningSign(Transform parent, Vector3 pos, Quaternion rot)
        {
            // rot: sign's forward points INTO the wall, so the text faces the room.
            var sign = new GameObject("Sign_OverheadCrane").transform;
            sign.SetParent(parent, false);
            sign.localPosition = pos;
            sign.localRotation = rot;
            Box("Plate", sign, new Vector3(0f, 0f, -0.01f), new Vector3(1.4f, 0.45f, 0.01f), Mat("Sign_White", Color.white, 0f, 0.3f));
            Box("Pictogram", sign, new Vector3(-0.5f, 0f, -0.017f), new Vector3(0.3f, 0.3f, 0.005f), Mat("Sign_Yellow", new Color(1f, 0.82f, 0f), 0f, 0.3f));
            var c = MakeCanvas("SignText", sign, new Vector3(0f, 0f, -0.021f), Quaternion.identity, new Vector2(1.4f, 0.45f));
            AddText(c, "!", new Vector2(-0.5f, 0f), new Vector2(0.3f, 0.3f), 0.22f, Color.black);
            AddText(c, "OVERHEAD CRANE\nSTAY OUT FROM UNDER\nSUSPENDED LOADS", new Vector2(0.15f, 0f), new Vector2(1.0f, 0.42f), 0.075f, new Color(0.15f, 0.15f, 0.15f));
        }

        static void BuildProps(Transform env)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(env, false);

            // Stainless service pipes on wall brackets (photo of the corridor wall)
            var rack = new GameObject("WallPipeRack").transform;
            rack.SetParent(props, false);
            var thin = Mat("Stainless_Satin", new Color(0.75f, 0.75f, 0.76f), 0.9f, 0.6f);
            var bracket = Mat("Bracket_Grey", new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.4f);
            foreach (float y in new[] { 2.0f, 2.35f, 2.7f })
                Cyl("ServicePipe", rack, new Vector3(0f, y, HallZ - 0.35f), 0.11f, HallX * 2f - 1f, thin, Ax.X);
            for (float x = -13.5f; x <= 13.5f; x += 2.5f)
                Box("Bracket", rack, new Vector3(x, 2.35f, HallZ - 0.2f), new Vector3(0.06f, 1.0f, 0.4f), bracket);

            // Grey electrical control cabinets on a rack (photos of the vacuum control cabinets)
            var cab = Mat("Cabinet_Grey", new Color(0.86f, 0.87f, 0.87f), 0f, 0.4f);
            var frame = Mat("Aluminium_Profile", new Color(0.8f, 0.81f, 0.83f), 0.8f, 0.5f);
            var cabinets = new GameObject("VacuumControlCabinets").transform;
            cabinets.SetParent(props, false);
            cabinets.localPosition = new Vector3(9.5f, 0f, -7.5f);
            cabinets.gameObject.AddComponent<CraneObstacle>();
            Box("Frame", cabinets, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 0.4f), frame, true);
            for (int i = 0; i < 2; i++)
            {
                float x = -0.4f + i * 0.8f;
                Box("Cabinet", cabinets, new Vector3(x, 1.75f, 0f), new Vector3(0.7f, 0.9f, 0.3f), cab, true);
                for (int b = 0; b < 6; b++)
                    Cyl("Knob", cabinets, new Vector3(x - 0.2f + (b % 3) * 0.2f, 1.9f - (b / 3) * 0.2f, -0.16f), 0.04f, 0.02f,
                        b % 2 == 0 ? Mat("Button_Green", new Color(0.1f, 0.6f, 0.4f), 0f, 0.6f) : Mat("Button_Black", new Color(0.05f, 0.05f, 0.05f), 0f, 0.6f), Ax.Z);
            }

            // Orange mast lift parked in the corner (the JLG lift in the photos)
            var lift = new GameObject("MastLift").transform;
            lift.SetParent(props, false);
            lift.localPosition = new Vector3(11.5f, 0f, 7f);
            lift.localRotation = Quaternion.Euler(0f, -30f, 0f);
            lift.gameObject.AddComponent<CraneObstacle>();
            var orange = Mat("JLG_Orange", new Color(0.95f, 0.38f, 0.1f), 0.1f, 0.65f);
            var mast = Mat("JLG_Mast", new Color(0.9f, 0.86f, 0.62f), 0.1f, 0.5f);
            var tyre = Mat("Tyre_White", new Color(0.9f, 0.9f, 0.9f), 0f, 0.3f);
            Box("Chassis", lift, new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.7f, 1.3f), orange, true);
            foreach (var w in new[] { new Vector3(-0.42f, 0.15f, 0.45f), new Vector3(0.42f, 0.15f, 0.45f), new Vector3(-0.42f, 0.15f, -0.45f), new Vector3(0.42f, 0.15f, -0.45f) })
                Cyl("Wheel", lift, w, 0.3f, 0.12f, tyre, Ax.X);
            Box("Mast", lift, new Vector3(0f, 2.1f, -0.35f), new Vector3(0.3f, 2.6f, 0.25f), mast, true);
            Box("Boom", lift, new Vector3(0f, 3.0f, 0.2f), new Vector3(0.18f, 0.18f, 1.4f), orange, false, new Vector3(-30f, 0f, 0f));
            var basket = new GameObject("Basket").transform;
            basket.SetParent(lift, false);
            basket.localPosition = new Vector3(0f, 3.3f, 0.95f);
            Box("Floor", basket, new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.05f, 0.6f), orange);
            Box("RailFront", basket, new Vector3(0f, 0.55f, 0.3f), new Vector3(0.7f, 0.04f, 0.04f), orange);
            Box("RailBack", basket, new Vector3(0f, 0.55f, -0.3f), new Vector3(0.7f, 0.04f, 0.04f), orange);
            Box("RailL", basket, new Vector3(-0.35f, 0.55f, 0f), new Vector3(0.04f, 0.04f, 0.6f), orange);
            Box("RailR", basket, new Vector3(0.35f, 0.55f, 0f), new Vector3(0.04f, 0.04f, 0.6f), orange);
            foreach (var c in new[] { new Vector3(-0.35f, 0.28f, 0.3f), new Vector3(0.35f, 0.28f, 0.3f), new Vector3(-0.35f, 0.28f, -0.3f), new Vector3(0.35f, 0.28f, -0.3f) })
                Box("Post", basket, c, new Vector3(0.04f, 0.55f, 0.04f), orange);
        }

        // =====================================================================================
        // Crane
        // =====================================================================================

        static BridgeCrane BuildCrane(out CraneHook hook)
        {
            var cream = Mat("Crane_Cream", new Color(0.92f, 0.9f, 0.82f), 0.1f, 0.45f);
            var steelStrip = Mat("Rail_Steel", new Color(0.45f, 0.46f, 0.48f), 0.9f, 0.5f);
            var black = Mat("Black_Plastic", new Color(0.04f, 0.04f, 0.045f), 0f, 0.3f);
            var motorGrey = Mat("Motor_Grey", new Color(0.32f, 0.35f, 0.38f), 0.4f, 0.35f);
            var hookSteel = Mat("Hook_Stainless", new Color(0.7f, 0.7f, 0.68f), 0.9f, 0.55f);
            var hookYellow = Mat("Hook_Latch", new Color(0.95f, 0.75f, 0.1f), 0.2f, 0.5f);

            var craneRoot = new GameObject("OverheadCrane").transform;

            // ---- Runway: two wall rails on brackets + black drag chain ----
            var runway = new GameObject("Runway").transform;
            runway.SetParent(craneRoot, false);
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * RailZ;
                Box("RailBeam", runway, new Vector3(0f, RailTopY - 0.15f, z), new Vector3(HallX * 2f, 0.3f, 0.25f), cream);
                Box("RailRunningStrip", runway, new Vector3(0f, RailTopY + 0.015f, z), new Vector3(HallX * 2f, 0.03f, 0.08f), steelStrip);
                for (float x = -13.5f; x <= 13.5f; x += 3f)
                    Box("WallBracket", runway, new Vector3(x, RailTopY - 0.35f, side * (RailZ + 0.4f)), new Vector3(0.2f, 0.3f, 0.6f), cream);
            }
            Box("DragChain", runway, new Vector3(0f, RailTopY - 0.33f, RailZ + 0.2f), new Vector3(HallX * 2f, 0.06f, 0.12f), black);

            // ---- Bridge (moves along X) ----
            var bridge = new GameObject("Bridge").transform;
            bridge.SetParent(craneRoot, false);
            bridge.localPosition = new Vector3(-6f, 0f, 0f);

            float girderLen = (RailZ - 0.2f) * 2f;
            Box("BoxGirder", bridge, new Vector3(0f, RailTopY + 0.07f, 0f), new Vector3(0.55f, 0.75f, girderLen), cream);
            Box("BottomFlange", bridge, new Vector3(0f, RailTopY - 0.31f, 0f), new Vector3(0.7f, 0.03f, girderLen), cream);

            // Barcode positioning strip along the girder (photo)
            var barcode = BarcodeMaterial(girderLen);
            Box("BarcodeStrip_W", bridge, new Vector3(-0.28f, RailTopY + 0.3f, 0f), new Vector3(0.004f, 0.05f, girderLen - 0.4f), barcode);
            Box("BarcodeStrip_E", bridge, new Vector3(0.28f, RailTopY + 0.3f, 0f), new Vector3(0.004f, 0.05f, girderLen - 0.4f), barcode);

            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * RailZ;
                Box("EndTruck", bridge, new Vector3(0f, RailTopY + 0.2f, z), new Vector3(2.2f, 0.35f, 0.4f), cream);
                foreach (float wx in new[] { -0.8f, 0.8f })
                    Cyl("Wheel", bridge, new Vector3(wx, RailTopY + 0.08f, z), 0.2f, 0.12f, black, Ax.Z);
                // Grey geared motor on the end truck
                Box("GearMotor", bridge, new Vector3(-1.25f, RailTopY + 0.22f, z), new Vector3(0.3f, 0.3f, 0.3f), motorGrey);
                Cyl("MotorBody", bridge, new Vector3(-1.25f, RailTopY + 0.22f, z - side * 0.3f), 0.22f, 0.35f, motorGrey, Ax.Z);
                Box("Buffer", bridge, new Vector3(1.15f, RailTopY + 0.2f, z), new Vector3(0.1f, 0.15f, 0.15f), black);
            }

            // Red LED load display at the operator end of the girder (shows e.g. "0.005t")
            var display = new GameObject("LoadDisplay").transform;
            display.SetParent(bridge, false);
            display.localPosition = new Vector3(0f, RailTopY - 0.55f, -RailZ + 0.9f);
            Box("Housing", display, Vector3.zero, new Vector3(0.5f, 0.2f, 0.08f), black);
            Box("Mount", display, new Vector3(0f, 0.17f, 0f), new Vector3(0.05f, 0.15f, 0.05f), steelStrip);
            var displayCanvas = MakeCanvas("Display", display, new Vector3(0f, 0f, -0.041f), Quaternion.identity, new Vector2(0.46f, 0.17f));
            var displayText = AddText(displayCanvas, "0.005t", Vector2.zero, new Vector2(0.46f, 0.17f), 0.12f, new Color(1f, 0.12f, 0.05f));

            // ---- Trolley / hoist (moves along Z, hangs under the girder) ----
            var trolley = new GameObject("Trolley").transform;
            trolley.SetParent(bridge, false);
            trolley.localPosition = new Vector3(0f, 0f, 0f);
            float ty = RailTopY - 0.6f; // centre height of the hoist body
            Box("HoistBody", trolley, new Vector3(0f, ty, 0f), new Vector3(0.7f, 0.45f, 0.8f), cream);
            Box("SidePlate_W", trolley, new Vector3(-0.38f, ty + 0.25f, 0f), new Vector3(0.05f, 0.35f, 0.6f), cream);
            Box("SidePlate_E", trolley, new Vector3(0.38f, ty + 0.25f, 0f), new Vector3(0.05f, 0.35f, 0.6f), cream);
            foreach (float x in new[] { -0.41f, 0.41f })
            foreach (float z in new[] { -0.2f, 0.2f })
                Cyl("TrolleyWheel", trolley, new Vector3(x, ty + 0.3f, z), 0.12f, 0.03f, black, Ax.X);
            // Big "2" like on the real hoist
            Vector3[] numberPos = { new Vector3(0f, ty, -0.401f), new Vector3(-0.351f, ty, 0f), new Vector3(0.351f, ty, 0f) };
            float[] numberYaw = { 0f, 90f, -90f };
            for (int i = 0; i < 3; i++)
            {
                var c = MakeCanvas("HoistNumber", trolley, numberPos[i], Quaternion.Euler(0f, numberYaw[i], 0f), new Vector2(0.3f, 0.35f));
                AddText(c, "2", Vector2.zero, new Vector2(0.3f, 0.35f), 0.3f, Color.black);
            }
            var strapTopA = Empty("StrapTop_A", trolley, new Vector3(-0.08f, ty - 0.23f, 0f));
            var strapTopB = Empty("StrapTop_B", trolley, new Vector3(0.08f, ty - 0.23f, 0f));

            // ---- Hook block (moves up/down, rotates) ----
            var hookBlock = new GameObject("HookBlock").transform;
            hookBlock.SetParent(trolley, false);
            hookBlock.localPosition = new Vector3(0f, 5.0f, 0f);
            Box("SheaveBox", hookBlock, new Vector3(0f, -0.15f, 0f), new Vector3(0.26f, 0.3f, 0.14f), hookSteel);
            Box("RatingPlate", hookBlock, new Vector3(0f, -0.17f, -0.071f), new Vector3(0.12f, 0.06f, 0.002f), Mat("Sign_White", Color.white, 0f, 0.3f));
            Cyl("Shank", hookBlock, new Vector3(0f, -0.34f, 0f), 0.04f, 0.1f, hookSteel);
            // J-shaped hook made of ring segments, open at the top right with a yellow latch
            var hookShape = new GameObject("Hook").transform;
            hookShape.SetParent(hookBlock, false);
            hookShape.localPosition = new Vector3(0f, -0.45f, 0f);
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(100f, 400f, i / 9f) * Mathf.Deg2Rad; // leaves a gap at the top-right
                var seg = Box("HookSeg", hookShape, new Vector3(Mathf.Cos(a) * 0.065f, Mathf.Sin(a) * 0.065f, 0f), new Vector3(0.045f, 0.03f, 0.03f), hookSteel);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }
            Box("Latch", hookShape, new Vector3(0.04f, 0.06f, 0f), new Vector3(0.008f, 0.07f, 0.02f), hookYellow, false, new Vector3(0f, 0f, -35f));
            var attachPoint = Empty("AttachPoint", hookBlock, new Vector3(0f, -0.505f, 0f));
            var strapBottomA = Empty("StrapBottom_A", hookBlock, new Vector3(-0.08f, 0f, 0f));
            var strapBottomB = Empty("StrapBottom_B", hookBlock, new Vector3(0.08f, 0f, 0f));

            // Hoist straps (flat lifting belts in the photo)
            var strapMat = UnlitMat("Hoist_Strap", new Color(0.88f, 0.88f, 0.86f));
            var strapA = Strap("StrapA", trolley, strapMat, 0.035f);
            var strapB = Strap("StrapB", trolley, strapMat, 0.035f);

            // ---- Components ----
            var crane = craneRoot.gameObject.AddComponent<BridgeCrane>();
            crane.bridge = bridge;
            crane.trolley = trolley;
            crane.hookBlock = hookBlock;
            crane.bridgeRange = new Vector2(-HallX + 1.6f, HallX - 1.6f);
            crane.trolleyRange = new Vector2(-RailZ + 1.7f, RailZ - 1.7f);
            crane.hookHeightRange = new Vector2(0.6f, ty - 0.45f);
            crane.straps = new[] { strapA, strapB };
            crane.strapTops = new[] { strapTopA, strapTopB };
            crane.strapBottoms = new[] { strapBottomA, strapBottomB };

            hook = hookBlock.gameObject.AddComponent<CraneHook>();
            hook.crane = crane;
            hook.attachPoint = attachPoint;
            hook.loadDisplay = displayText;
            return crane;
        }

        static LineRenderer Strap(string name, Transform parent, Material m, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.sharedMaterial = m;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            return lr;
        }

        static Material BarcodeMaterial(float length)
        {
            string texPath = Root + "/Materials/Barcode.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = new Texture2D(128, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                var rnd = new System.Random(2);
                for (int x = 0; x < tex.width; x++)
                {
                    Color c = rnd.NextDouble() < 0.5 ? Color.black : Color.white;
                    for (int y = 0; y < tex.height; y++) tex.SetPixel(x, y, c);
                }
                tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }
            var m = Mat("Barcode", Color.white, 0f, 0.3f);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(length / 0.6f, 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // =====================================================================================
        // Towers (stainless vacuum towers: skirt, shells with flange rings, dome with lifting lugs)
        // =====================================================================================

        static void BuildTower(string name, Vector3 pos, Transform parent, params Vector3[] portDirs)
        {
            var shell = Mat("Tower_Stainless", new Color(0.64f, 0.6f, 0.53f), 0.8f, 0.5f);
            var ring = Mat("Tower_FlangeRing", new Color(0.7f, 0.67f, 0.62f), 0.85f, 0.6f);
            var dark = Mat("Tower_Skirt", new Color(0.5f, 0.48f, 0.45f), 0.7f, 0.4f);
            var glass = Mat("Viewport_Glass", new Color(0.15f, 0.18f, 0.2f), 0.2f, 0.95f);

            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.gameObject.AddComponent<CraneObstacle>();

            Cyl("BaseSkirt", t, new Vector3(0f, 0.15f, 0f), 2.4f, 0.3f, dark, Ax.Y, true);
            Cyl("LowerShell", t, new Vector3(0f, 1.1f, 0f), TowerR * 2f, 1.6f, shell, Ax.Y, true);
            Cyl("FlangeRing_1", t, new Vector3(0f, 1.94f, 0f), 2.36f, 0.08f, ring);
            Cyl("MidShell", t, new Vector3(0f, 2.49f, 0f), 2.4f, 1.02f, shell, Ax.Y, true);
            Cyl("FlangeRing_2", t, new Vector3(0f, 3.04f, 0f), 2.5f, 0.08f, ring);
            Cyl("UpperShell", t, new Vector3(0f, 3.69f, 0f), TowerR * 2f, 1.22f, shell, Ax.Y, true);
            Cyl("FlangeRing_3", t, new Vector3(0f, 4.34f, 0f), 2.36f, 0.08f, ring);
            Sphere("Dome", t, new Vector3(0f, 4.38f, 0f), new Vector3(2.2f, 1.0f, 2.2f), shell, true);

            // Lifting lugs on the dome rim
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var lug = Box("LiftingLug", t, dir * 1.02f + Vector3.up * 4.5f, new Vector3(0.03f, 0.28f, 0.2f), ring);
                lug.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));
            }

            // Viewports on the mid shell where there is no beam-pipe port
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (portDirs.Any(p => Vector3.Dot(p, dir) > 0.5f) || i % 2 == 1) continue;
                var vp = new GameObject("Viewport").transform;
                vp.SetParent(t, false);
                vp.localPosition = dir * 1.18f + Vector3.up * 2.5f;
                vp.localRotation = Quaternion.LookRotation(dir);
                Cyl("Flange", vp, new Vector3(0f, 0f, 0.03f), 0.42f, 0.06f, ring, Ax.Z);
                Cyl("Glass", vp, new Vector3(0f, 0f, 0.062f), 0.26f, 0.01f, glass, Ax.Z);
                BoltRing(vp, new Vector3(0f, 0f, 0.06f), Ax.Z, 0.17f, 8, 0.03f, 0.02f);
            }

            foreach (var d in portDirs) BuildPort(t, d);
        }

        static void BuildPort(Transform tower, Vector3 dir)
        {
            var steel = Mat("Pipe_Stainless", new Color(0.8f, 0.8f, 0.8f), 0.9f, 0.7f);
            var flange = Mat("Flange_Stainless", new Color(0.86f, 0.86f, 0.87f), 0.95f, 0.75f);

            var port = new GameObject("Port").transform;
            port.SetParent(tower, false);
            port.localPosition = dir * (TowerR - 0.05f) + Vector3.up * PipeY;
            port.localRotation = Quaternion.LookRotation(dir);
            float stubLen = PortReach - (TowerR - 0.05f) - 0.06f;
            Cyl("Stub", port, new Vector3(0f, 0f, stubLen / 2f), 0.62f, stubLen, steel, Ax.Z);
            Cyl("Flange", port, new Vector3(0f, 0f, stubLen + 0.03f), 0.95f, 0.06f, flange, Ax.Z);
            BoltRing(port, new Vector3(0f, 0f, stubLen + 0.03f), Ax.Z, 0.41f, 12, 0.1f, 0.03f);
            var col = port.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, (stubLen + 0.06f) / 2f);
            col.size = new Vector3(0.95f, 0.95f, stubLen + 0.06f);
        }

        // =====================================================================================
        // Pipes, ghosts and stands
        // =====================================================================================

        static void PipeShape(Transform t, float length, Material tube, Material flange)
        {
            Cyl("Tube", t, Vector3.zero, 0.6f, length - 0.1f, tube, Ax.X);
            Cyl("FlangeA", t, new Vector3(-length / 2f + 0.03f, 0f, 0f), 0.95f, 0.06f, flange, Ax.X);
            Cyl("FlangeB", t, new Vector3(length / 2f - 0.03f, 0f, 0f), 0.95f, 0.06f, flange, Ax.X);
            Cyl("WeldNeckA", t, new Vector3(-length / 2f + 0.12f, 0f, 0f), 0.68f, 0.12f, tube, Ax.X);
            Cyl("WeldNeckB", t, new Vector3(length / 2f - 0.12f, 0f, 0f), 0.68f, 0.12f, tube, Ax.X);
        }

        static PipeSection BuildPipe(string id, float length, Vector3 pos, float yaw, Transform parent)
        {
            var steel = Mat("Pipe_Stainless", new Color(0.8f, 0.8f, 0.8f), 0.9f, 0.7f);
            var flange = Mat("Flange_Stainless", new Color(0.86f, 0.86f, 0.87f), 0.95f, 0.75f);
            var lugMat = Mat("Flange_Stainless", Color.white, 0.95f, 0.75f);
            var linkMat = Mat("MasterLink_Yellow", new Color(1f, 0.78f, 0f), 0.3f, 0.6f);
            var slingMat = UnlitMat("Sling_Green", new Color(0.25f, 0.65f, 0.25f));

            var go = new GameObject("Pipe_" + id);
            var t = go.transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            PipeShape(t, length, steel, flange);

            var bolts = new GameObject("Bolts").transform;
            bolts.SetParent(t, false);
            BoltRing(bolts, new Vector3(-length / 2f + 0.03f, 0f, 0f), Ax.X, 0.41f, 12, 0.1f, 0.03f);
            BoltRing(bolts, new Vector3(length / 2f - 0.03f, 0f, 0f), Ax.X, 0.41f, 12, 0.1f, 0.03f);

            // Lifting lugs + two slings up to a yellow master link
            float lugX = length / 2f - 0.4f;
            var liftPoint = Empty("LiftPoint", t, new Vector3(0f, 0.9f, 0f));
            var ringCenter = new Vector3(0f, 0.83f, 0f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var seg = Box("MasterLink", t, ringCenter + new Vector3(Mathf.Cos(a) * 0.065f, Mathf.Sin(a) * 0.065f, 0f), new Vector3(0.045f, 0.025f, 0.025f), linkMat);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                Box("LiftingLug", t, new Vector3(s * lugX, 0.36f, 0f), new Vector3(0.12f, 0.14f, 0.03f), lugMat);
                var sling = new GameObject("Sling").AddComponent<LineRenderer>();
                sling.transform.SetParent(t, false);
                sling.useWorldSpace = false;
                sling.positionCount = 2;
                sling.SetPosition(0, new Vector3(0f, 0.765f, 0f));
                sling.SetPosition(1, new Vector3(s * lugX, 0.42f, 0f));
                sling.widthMultiplier = 0.035f;
                sling.sharedMaterial = slingMat;
            }

            // Pipe ID painted on both sides
            foreach (float side in new[] { -1f, 1f })
            {
                var c = MakeCanvas("IdLabel", t, new Vector3(0f, 0f, side * 0.305f), Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f), new Vector2(1.0f, 0.25f));
                AddText(c, $"{id}   {length:0.0} m", Vector2.zero, new Vector2(1.0f, 0.25f), 0.14f, new Color(0.1f, 0.1f, 0.12f));
            }

            // Physics: kinematic rigidbody + trigger slightly shorter than the pipe (so flush flanges don't count as a hit)
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(length - 0.12f, 0.95f, 0.95f);

            var pipe = go.AddComponent<PipeSection>();
            pipe.pipeId = id;
            pipe.length = length;
            pipe.massKg = Mathf.Round(95f * length);
            pipe.liftPoint = liftPoint;
            pipe.bolts = bolts.gameObject;
            return pipe;
        }

        static PipeAlignmentTarget BuildTarget(string pipeId, float length, Vector3 pos, float yaw, Transform parent, CraneHook hook)
        {
            var ghostMat = TransparentMat("Ghost", new Color(0.3f, 0.65f, 1f, 0.25f));
            var go = new GameObject("Target_" + pipeId);
            var t = go.transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var ghost = new GameObject("Ghost").transform;
            ghost.SetParent(t, false);
            PipeShape(ghost, length, ghostMat, ghostMat);
            foreach (var r in ghost.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;

            var readoutRoot = new GameObject("Readout").transform;
            readoutRoot.SetParent(t, false);
            readoutRoot.position = pos + Vector3.up * 1.6f;
            readoutRoot.gameObject.AddComponent<FaceCamera>();
            var c = MakeCanvas("Canvas", readoutRoot, Vector3.zero, Quaternion.identity, new Vector2(2.6f, 0.5f));
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(c, false);
            ((RectTransform)bg.transform).sizeDelta = new Vector2(2.6f, 0.5f) * PPM;
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            bg.GetComponent<Image>().raycastTarget = false;
            var text = AddText(c, "Pick up pipe " + pipeId, Vector2.zero, new Vector2(2.5f, 0.45f), 0.09f, Color.white);

            var target = go.AddComponent<PipeAlignmentTarget>();
            target.acceptsPipeId = pipeId;
            target.hook = hook;
            target.ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
            target.readout = text;
            return target;
        }

        /// <summary>Blue steel pipe stand with a saddle (like the blue supports in the cleanroom).</summary>
        static void Stand(Transform parent, Vector3 floorPos, float saddleTop, bool pipeAlongX)
        {
            var blue = Mat("Stand_Blue", new Color(0.07f, 0.24f, 0.72f), 0.3f, 0.5f);
            var rubber = Mat("Black_Plastic", new Color(0.04f, 0.04f, 0.045f), 0f, 0.3f);
            var s = new GameObject("PipeStand").transform;
            s.SetParent(parent, false);
            s.localPosition = floorPos;
            s.localRotation = Quaternion.Euler(0f, pipeAlongX ? 0f : 90f, 0f);
            Box("BasePlate", s, new Vector3(0f, 0.025f, 0f), new Vector3(0.45f, 0.05f, 0.7f), blue, true);
            Box("Post", s, new Vector3(0f, (saddleTop - 0.08f) / 2f, 0f), new Vector3(0.12f, saddleTop - 0.08f, 0.12f), blue, true);
            Box("Brace_A", s, new Vector3(0f, 0.25f, 0.18f), new Vector3(0.05f, 0.5f, 0.05f), blue, false, new Vector3(35f, 0f, 0f));
            Box("Brace_B", s, new Vector3(0f, 0.25f, -0.18f), new Vector3(0.05f, 0.5f, 0.05f), blue, false, new Vector3(-35f, 0f, 0f));
            // Saddle: a V made of two angled plates, pipe rests at saddleTop
            Box("Saddle_L", s, new Vector3(0f, saddleTop - 0.03f, -0.14f), new Vector3(0.22f, 0.04f, 0.3f), blue, false, new Vector3(-30f, 0f, 0f));
            Box("Saddle_R", s, new Vector3(0f, saddleTop - 0.03f, 0.14f), new Vector3(0.22f, 0.04f, 0.3f), blue, false, new Vector3(30f, 0f, 0f));
            Box("Pad", s, new Vector3(0f, saddleTop - 0.02f, 0f), new Vector3(0.22f, 0.04f, 0.2f), rubber, true);
        }

        // =====================================================================================
        // Operator control cabinet + instruction board
        // =====================================================================================

        static void BuildControlStation(BridgeCrane crane, CraneHook hook, PipeLevelManager level)
        {
            var cabMat = Mat("Cabinet_Grey", new Color(0.86f, 0.87f, 0.87f), 0f, 0.4f);
            var frame = Mat("Aluminium_Profile", new Color(0.8f, 0.81f, 0.83f), 0.8f, 0.5f);
            var teal = Mat("Handle_Teal", new Color(0.3f, 0.68f, 0.74f), 0f, 0.5f);
            var dark = Mat("Button_Bezel", new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.5f);

            // The operator stands at z = -8.6 looking +Z (towards the towers and the storage rack).
            var station = new GameObject("CraneControlStation").transform;
            station.localPosition = new Vector3(0f, 0f, -8.0f);

            Box("Leg_L", station, new Vector3(-0.42f, 0.5f, 0.05f), new Vector3(0.04f, 1.0f, 0.04f), frame, true);
            Box("Leg_R", station, new Vector3(0.42f, 0.5f, 0.05f), new Vector3(0.04f, 1.0f, 0.04f), frame, true);
            Box("Foot", station, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.95f, 0.04f, 0.45f), frame, true);
            Box("Cabinet", station, new Vector3(0f, 1.35f, 0.05f), new Vector3(0.9f, 1.0f, 0.25f), cabMat, true);
            Box("HandlePost_L", station, new Vector3(-0.4f, 1.9f, 0.05f), new Vector3(0.03f, 0.1f, 0.03f), frame);
            Box("HandlePost_R", station, new Vector3(0.4f, 1.9f, 0.05f), new Vector3(0.03f, 0.1f, 0.03f), frame);
            Box("Handle", station, new Vector3(0f, 1.97f, 0.05f), new Vector3(0.9f, 0.05f, 0.1f), teal);
            Box("HighVoltageSticker", station, new Vector3(0.36f, 0.93f, -0.076f), new Vector3(0.08f, 0.08f, 0.002f), Mat("Sign_Yellow", new Color(1f, 0.82f, 0f), 0f, 0.3f));

            float face = -0.075f; // front face of the cabinet (local z)
            var labels = MakeCanvas("Labels", station, new Vector3(0f, 1.35f, face - 0.001f), Quaternion.identity, new Vector2(0.9f, 1.0f));
            AddText(labels, "OVERHEAD CRANE 2  -  OPERATOR", new Vector2(0f, 0.46f), new Vector2(0.85f, 0.05f), 0.03f, Color.black);

            // Indicator lamps
            var lamps = station.gameObject.AddComponent<CranePanelLamps>();
            lamps.crane = crane;
            lamps.hook = hook;
            string[] lampNames = { "POWER", "CREEP", "LOAD", "E-STOP" };
            var lampRenderers = new Renderer[4];
            for (int i = 0; i < 4; i++)
            {
                float x = -0.3f + i * 0.2f;
                lampRenderers[i] = Cyl("Lamp_" + lampNames[i], station, new Vector3(x, 1.72f, face - 0.012f), 0.035f, 0.025f, LampMat(), Ax.Z).GetComponent<Renderer>();
                AddText(labels, lampNames[i], new Vector2(x, 1.765f - 1.35f), new Vector2(0.18f, 0.03f), 0.018f, Color.black);
            }
            lamps.powerLamp = lampRenderers[0];
            lamps.creepLamp = lampRenderers[1];
            lamps.loadLamp = lampRenderers[2];
            lamps.stopLamp = lampRenderers[3];

            var black = Mat("Button_Black", new Color(0.05f, 0.05f, 0.05f), 0f, 0.6f);
            var green = Mat("Button_Green", new Color(0.1f, 0.6f, 0.4f), 0f, 0.6f);
            var white = Mat("Button_White", new Color(0.92f, 0.92f, 0.92f), 0f, 0.6f);
            var yellow = Mat("Button_Yellow", new Color(0.95f, 0.75f, 0.1f), 0f, 0.6f);
            var amber = Mat("Button_Amber", new Color(1f, 0.5f, 0.05f), 0f, 0.6f);
            var blue = Mat("Button_Blue", new Color(0.15f, 0.35f, 0.85f), 0f, 0.6f);
            var grey = Mat("Button_Grey", new Color(0.55f, 0.55f, 0.55f), 0f, 0.6f);
            var red = Mat("Button_Red", new Color(0.85f, 0.05f, 0.05f), 0f, 0.6f);

            _btnStation = station; _btnLabels = labels; _btnFace = face; _btnBezel = dark;
            _btnCrane = crane; _btnHook = hook; _btnLevel = level;

            float[] cols = { -0.3f, -0.1f, 0.1f, 0.3f };
            // Row 1: travel
            HoldButton("BRIDGE\nLEFT", cols[0], 1.58f, CraneAxis.Bridge, -1f, black);
            HoldButton("BRIDGE\nRIGHT", cols[1], 1.58f, CraneAxis.Bridge, 1f, black);
            HoldButton("TROLLEY\nBACK", cols[2], 1.58f, CraneAxis.Trolley, -1f, black);
            HoldButton("TROLLEY\nFORWARD", cols[3], 1.58f, CraneAxis.Trolley, 1f, black);
            // Row 2: hoist + rotate
            HoldButton("HOOK\nUP", cols[0], 1.4f, CraneAxis.Hoist, 1f, green);
            HoldButton("HOOK\nDOWN", cols[1], 1.4f, CraneAxis.Hoist, -1f, green);
            HoldButton("TURN\nLEFT", cols[2], 1.4f, CraneAxis.Rotate, -1f, white);
            HoldButton("TURN\nRIGHT", cols[3], 1.4f, CraneAxis.Rotate, 1f, white);
            // Row 3: functions
            ActionButton("ATTACH /\nRELEASE", cols[0], 1.22f, PanelAction.AttachRelease, yellow, 0.05f);
            ActionButton("CREEP\nSPEED", cols[1], 1.22f, PanelAction.ToggleCreep, amber, 0.05f);
            ActionButton("RESET\nE-STOP", cols[2], 1.22f, PanelAction.ResetStop, blue, 0.05f);
            ActionButton("RESTART\nLEVEL", cols[3], 1.22f, PanelAction.RestartLevel, grey, 0.05f);
            // Emergency stop: big red mushroom on a yellow plate
            Cyl("EStopPlate", station, new Vector3(0f, 0.99f, face - 0.003f), 0.16f, 0.006f, yellow, Ax.Z);
            ActionButton("EMERGENCY STOP", 0f, 0.99f, PanelAction.EmergencyStop, red, 0.09f);

            // ---- Instruction board next to the cabinet ----
            var board = new GameObject("InstructionBoard").transform;
            board.localPosition = new Vector3(1.25f, 0f, -7.9f);
            board.localRotation = Quaternion.Euler(0f, 35f, 0f); // turned towards the operator
            Box("Post_L", board, new Vector3(-0.6f, 0.8f, 0.03f), new Vector3(0.04f, 1.6f, 0.04f), frame, true);
            Box("Post_R", board, new Vector3(0.6f, 0.8f, 0.03f), new Vector3(0.04f, 1.6f, 0.04f), frame, true);
            Box("Panel", board, new Vector3(0f, 1.55f, 0.03f), new Vector3(1.3f, 0.9f, 0.03f), Mat("Board_Dark", new Color(0.1f, 0.12f, 0.15f), 0f, 0.4f), true);
            var bc = MakeCanvas("BoardCanvas", board, new Vector3(0f, 1.55f, 0.012f), Quaternion.identity, new Vector2(1.24f, 0.84f));
            level.titleText = AddText(bc, "Crane level", new Vector2(0f, 0.36f), new Vector2(1.2f, 0.08f), 0.05f, new Color(1f, 0.82f, 0.2f), TextAnchor.MiddleLeft);
            level.bodyText = AddText(bc, "", new Vector2(0f, 0.12f), new Vector2(1.2f, 0.38f), 0.03f, Color.white, TextAnchor.UpperLeft, FontStyle.Normal);
            level.messageText = AddText(bc, "", new Vector2(0f, -0.15f), new Vector2(1.2f, 0.12f), 0.032f, new Color(1f, 0.45f, 0.35f), TextAnchor.MiddleLeft);
            level.statusText = AddText(bc, "", new Vector2(0f, -0.33f), new Vector2(1.2f, 0.16f), 0.028f, new Color(0.6f, 1f, 0.7f), TextAnchor.LowerLeft, FontStyle.Normal);
        }

        // Context for the button helpers below (set by BuildControlStation).
        static Transform _btnStation;
        static RectTransform _btnLabels;
        static float _btnFace;
        static Material _btnBezel;
        static BridgeCrane _btnCrane;
        static CraneHook _btnHook;
        static PipeLevelManager _btnLevel;

        static void HoldButton(string label, float x, float y, CraneAxis axis, float dir, Material capMat)
        {
            var b = MakeButton(label, x, y, capMat, 0.05f);
            b.mode = CranePanelButton.Mode.HoldAxis;
            b.axis = axis;
            b.direction = dir;
        }

        static void ActionButton(string label, float x, float y, PanelAction action, Material capMat, float size)
        {
            var b = MakeButton(label, x, y, capMat, size);
            b.mode = CranePanelButton.Mode.Momentary;
            b.action = action;
        }

        static CranePanelButton MakeButton(string label, float x, float y, Material capMat, float size)
        {
            var root = new GameObject("Button_" + label.Replace("\n", " ").Replace("/", "").Replace("  ", " ")).transform;
            root.SetParent(_btnStation, false);
            root.localPosition = new Vector3(x, y, _btnFace);
            Cyl("Bezel", root, new Vector3(0f, 0f, -0.006f), size + 0.016f, 0.012f, _btnBezel, Ax.Z);
            var cap = Cyl("Cap", root, new Vector3(0f, 0f, -0.022f), size, 0.024f, capMat, Ax.Z).transform;
            var col = root.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, -0.025f);
            col.size = new Vector3(size + 0.02f, size + 0.02f, 0.05f);
            var b = root.gameObject.AddComponent<CranePanelButton>(); // also adds XRSimpleInteractable
            b.crane = _btnCrane;
            b.hook = _btnHook;
            b.level = _btnLevel;
            b.cap = cap;
            float labelY = y - 1.35f + size / 2f + 0.035f;
            AddText(_btnLabels, label, new Vector2(x, labelY), new Vector2(0.19f, 0.05f), 0.016f, Color.black);
            return b;
        }

        static Material LampMat()
        {
            var m = Mat("Lamp", new Color(0.15f, 0.15f, 0.15f), 0f, 0.8f, Color.black);
            return m;
        }

        // =====================================================================================
        // XR rig
        // =====================================================================================

        static void BuildRig()
        {
            var rigPos = new Vector3(0f, 0f, -8.75f);
            GameObject prefab = null;
            foreach (var guid in AssetDatabase.FindAssets("XR Origin t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("XR Origin (XR Rig).prefab")) { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); break; }
            }

            if (prefab != null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                rig.transform.SetPositionAndRotation(rigPos, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning("[CraneLevel] No 'XR Origin (XR Rig)' prefab found (XRI Starter Assets). Added a plain camera instead - drag your team's XR rig in at the control cabinet.");
                var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cam.tag = "MainCamera";
                cam.transform.SetPositionAndRotation(rigPos + new Vector3(0f, 1.7f, 0f), Quaternion.Euler(12f, 0f, 0f));
            }

            if (Object.FindFirstObjectByType<XRInteractionManager>() == null)
                new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Shader LitShader()
        {
            Shader s = _urp ? Shader.Find("Universal Render Pipeline/Lit") : null;
            return s != null ? s : Shader.Find("Standard");
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(LitShader());
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            _mats[name] = m;
            return m;
        }

        static Material UnlitMat(string name, Color color)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader s = _urp ? Shader.Find("Universal Render Pipeline/Unlit") : null;
                m = new Material(s != null ? s : Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            EditorUtility.SetDirty(m);
            _mats[name] = m;
            return m;
        }

        static Material TransparentMat(string name, Color color)
        {
            var m = Mat(name, color, 0f, 0.8f);
            if (_urp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                m.SetFloat("_Mode", 2f);
                m.EnableKeyword("_ALPHABLEND_ON");
            }
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Transform Empty(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, bool collider = false, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Cyl(string name, Transform parent, Vector3 localPos, float diameter, float length, Material m, Ax axis = Ax.Y, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = axis == Ax.X ? Quaternion.Euler(0f, 0f, 90f)
                                       : axis == Ax.Z ? Quaternion.Euler(90f, 0f, 0f)
                                       : Quaternion.identity;
            go.transform.localScale = new Vector3(diameter, length / 2f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = true;
            }
            return go;
        }

        static GameObject Sphere(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = true;
            }
            return go;
        }

        static void BoltRing(Transform parent, Vector3 center, Ax axis, float radius, int count, float boltLength, float boltDiameter)
        {
            var boltMat = Mat("Bolt_Steel", new Color(0.42f, 0.42f, 0.44f), 0.85f, 0.5f);
            var ring = new GameObject("BoltRing").transform;
            ring.SetParent(parent, false);
            ring.localPosition = center;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                Vector3 p = axis == Ax.X ? new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a))
                          : axis == Ax.Z ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f)
                          : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Cyl("Bolt", ring, p * radius, boltDiameter, boltLength, boltMat, axis);
            }
        }

        static RectTransform MakeCanvas(string name, Transform parent, Vector3 localPos, Quaternion localRot, Vector2 sizeMetres)
        {
            // A world-space canvas reads correctly when you look along its +Z (forward).
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeMetres * PPM;
            rt.localPosition = localPos;
            rt.localRotation = localRot;
            // Undo the parent's scale so text keeps its size on scaled objects.
            Vector3 ls = parent.lossyScale;
            rt.localScale = new Vector3(1f / (PPM * ls.x), 1f / (PPM * ls.y), 1f / (PPM * ls.z));
            return rt;
        }

        static Text AddText(RectTransform canvas, string text, Vector2 centerMetres, Vector2 sizeMetres, float fontHeightMetres,
                            Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = centerMetres * PPM;
            rt.sizeDelta = sizeMetres * PPM;
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(fontHeightMetres * PPM));
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1f;
            return t;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Plays the laser levels by itself, the way a player would, and writes down what it sees. For finding flaws in the
/// mirror camera and the monitor without a headset. Creates itself on Play (editor and development builds only).
///
/// Start: press F8 in Play Mode, or let LaserPlaytestBridge (editor) start Play Mode with an "autorun" file.
/// Output: Logs/Playtest/report.md plus screenshots of the curved monitor and the mirror camera (PNG).
///
/// Per level it does two things:
///  1. Direction test: for every mirror, in the Whole path and Close-up views, press each arrow once and measure where
///     the dot goes in the mirror camera picture and on the iris/detector views (dartboards) of the monitor.
///  2. Play: follow the on-picture guide ("Press ► 3x") in the Close-up view until the level is complete, and note
///     every press that made things worse, every time the ring or dot is off the picture, and zoom jumps.
/// </summary>
public class LaserPlaytestBot : MonoBehaviour
{
    const float TIME_SCALE = 2f;
    const int MAX_PRESSES_PER_MIRROR = 60;
    const int ROUNDS = 4;

    public static string Folder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Playtest");
    public static string AutorunFile => Path.Combine(Folder, "autorun");

    LaserLevelManager manager;
    MirrorControlPanel panel;
    LaserMirrorCamera mirrorCamera;
    LaserAlignmentObjective objective;
    LaserBeamCameraView[] boardViews;
    Camera monitorCamera;
    readonly StringBuilder report = new StringBuilder();
    bool running;
    int shotCount;
    int problems;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Laser Playtest Bot");
        DontDestroyOnLoad(go);
        go.AddComponent<LaserPlaytestBot>();
    }
#endif

    void Start()
    {
        if (File.Exists(AutorunFile))
        {
            StartCoroutine(Run(File.ReadAllText(AutorunFile), true));
        }
    }

    void Update()
    {
        if (!running && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            StartCoroutine(Run("play", false));
        }
    }

    // ----- Main flow ----------------------------------------------------------------------------------

    IEnumerator Run(string command, bool autorun)
    {
        running = true;
        Application.runInBackground = true;
        Directory.CreateDirectory(Folder);
        foreach (string old in Directory.GetFiles(Folder, "*.png")) File.Delete(old);
        report.Clear();
        problems = 0;
        shotCount = 0;
        report.AppendLine("# Laser playtest " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        report.AppendLine("Command: `" + command.Trim() + "`");
        report.AppendLine();

        yield return new WaitForSecondsRealtime(2f);   // let the scene start (lasers, levels, monitor)
        if (command.Contains("tour"))
        {
            yield return Tour();
            Finish(autorun);
            yield break;
        }

        if (!FindParts())
        {
            Finish(autorun);
            yield break;
        }

        List<int> levels = ParseLevels(command);
        bool directionTest = !command.Contains("nodir");
        bool play = !command.Contains("noplay");
        Time.timeScale = TIME_SCALE;
        foreach (int level in levels)
        {
            yield return PlayLevel(level, directionTest, play, command.Contains("nominal"));
        }

        Time.timeScale = 1f;
        report.AppendLine();
        report.AppendLine("**Problems flagged: " + problems + "**");
        Finish(autorun);
    }

    void Finish(bool autorun)
    {
        File.WriteAllText(Path.Combine(Folder, "report.md"), report.ToString());
        Debug.Log("[LaserPlaytestBot] Done. Report: " + Path.Combine(Folder, "report.md"));
        running = false;
        if (autorun)
        {
            File.Delete(AutorunFile);
            File.WriteAllText(Path.Combine(Folder, "done"), System.DateTime.Now.ToString("o"));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    bool FindParts()
    {
        manager = FindAnyObjectByType<LaserLevelManager>();
        mirrorCamera = FindAnyObjectByType<LaserMirrorCamera>();
        objective = FindAnyObjectByType<LaserAlignmentObjective>();
        boardViews = FindObjectsByType<LaserBeamCameraView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject monitor = GameObject.Find("Monitor UI Camera");
        monitorCamera = monitor ? monitor.GetComponent<Camera>() : null;
        panel = MirrorControlPanel.FirstActive;
        string missing = (!manager ? " LaserLevelManager" : "") + (!mirrorCamera ? " LaserMirrorCamera" : "") +
                         (!objective ? " LaserAlignmentObjective" : "") + (!panel ? " MirrorControlPanel" : "") +
                         (!monitorCamera ? " MonitorUICamera" : "");
        if (missing.Length > 0)
        {
            report.AppendLine("ERROR: not found in the scene:" + missing);
            return false;
        }

        System.Array.Sort(boardViews, (a, b) => string.CompareOrdinal(a.Label, b.Label));
        report.AppendLine("Monitor views (dartboards): " + string.Join(", ", System.Array.ConvertAll(boardViews, v => v.Label + " (" + v.name + ")")));
        report.AppendLine();
        return true;
    }

    List<int> ParseLevels(string command)
    {
        var levels = new List<int>();
        int at = command.IndexOf("levels=");
        if (at >= 0)
        {
            string list = command.Substring(at + 7).Split(' ', '\n', '\r')[0];
            foreach (string part in list.Split(','))
            {
                if (int.TryParse(part, out int n) && n >= 1 && n <= manager.LevelCount) levels.Add(n - 1);
            }
        }

        if (levels.Count == 0)
        {
            for (int i = 0; i < manager.LevelCount; i++) levels.Add(i);
        }

        return levels;
    }

    /// <summary>All mirrors on their designed angle: the level must then complete. Lists the beam pieces either way.</summary>
    IEnumerator NominalCheck(string level)
    {
        report.AppendLine("### Designed angles (all mirrors 0, 0)");
        foreach (MirrorMount mirror in manager.CurrentLevel.Mirrors)
        {
            if (mirror) mirror.ResetToNominal();
        }

        if (objective.IsWaitingToStart) objective.StartObjective();
        float waited = 0f;
        while (!objective.IsCompleted && waited < 8f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        report.AppendLine(objective.IsCompleted ? "Completes after " + waited.ToString("0.0") + " s." : "**Does NOT complete.**");
        if (!objective.IsCompleted) Flag("level does not complete with all mirrors at their designed angle");
        report.AppendLine("```\n" + objective.BuildStatusReport());
        report.AppendLine("Beam pieces (start -> end, power, hit):");
        foreach (LaserEmitter emitter in LaserEmitter.ActiveEmitters)
        {
            foreach (LaserSegment s in emitter.Segments)
            {
                report.AppendLine("  " + V(s.Start) + " -> " + V(s.End) + "  " + (s.Power * 100f).ToString("0.0") + "%  " +
                                  (s.HitCollider ? (s.HitCollider.transform.parent ? s.HitCollider.transform.parent.name + "/" : "") + s.HitCollider.name : "(nothing)"));
            }
        }

        report.AppendLine("```");
        yield return Shot(level + "_nominal");
        manager.LoadLevel(manager.CurrentLevelIndex);   // back to the start angles
        yield return new WaitForSecondsRealtime(1.5f);
        if (manager.IsBriefingOpen) manager.ToggleBriefing();
    }

    static string V(Vector3 v) => "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";

    IEnumerator PlayLevel(int index, bool directionTest, bool play, bool nominal)
    {
        manager.LoadLevel(index);
        yield return new WaitForSecondsRealtime(1.5f);
        if (manager.IsBriefingOpen) manager.ToggleBriefing();
        yield return new WaitForSecondsRealtime(0.5f);
        panel = MirrorControlPanel.FirstActive;

        string level = "L" + (index + 1);
        report.AppendLine("## Level " + (index + 1) + ": " + manager.CurrentTitle);
        report.AppendLine();
        yield return Shot(level + "_start");

        if (nominal) yield return NominalCheck(level);
        int closeUp = FindView(LaserMirrorCamera.ViewMode.AlongBeam);
        int wholePath = FindView(LaserMirrorCamera.ViewMode.BeamPath);

        if (directionTest)
        {
            report.AppendLine("### Direction test (one press, then undone)");
            report.AppendLine("Picture = mirror camera, x right / y up in pixels of a 1000 px wide picture. Board = dartboard on the monitor, in pixels of a 1000 px wide monitor. `OK`/`FLIP` compares with the arrow pressed.");
            report.AppendLine();
            for (int m = 0; m < panel.MirrorCount; m++)
            {
                panel.SelectMirror(m);
                foreach (int view in new[] { wholePath, closeUp })
                {
                    if (view < 0) continue;
                    mirrorCamera.SelectView(view);
                    yield return Settle();
                    string name = level + "_m" + (m + 1) + "_" + mirrorCamera.CurrentMode;
                    report.AppendLine("**" + panel.SelectedMirror.DisplayName + ", " + mirrorCamera.CurrentMode + "** " + Describe(TakeProbe()));
                    yield return Shot(name);
                    foreach (Vector2Int arrow in Arrows)
                    {
                        yield return DirectionTest(arrow);
                    }

                    report.AppendLine();
                }
            }
        }

        if (play)
        {
            report.AppendLine("### Playing with the on-picture guide (Close-up)");
            report.AppendLine();
            if (closeUp >= 0) mirrorCamera.SelectView(closeUp);
            for (int round = 0; round < ROUNDS && !objective.IsCompleted; round++)
            {
                for (int m = 0; m < panel.MirrorCount && !objective.IsCompleted; m++)
                {
                    panel.SelectMirror(m);
                    yield return SteerWithGuide(level, round, m);
                }
            }

            float wait = 0f;
            while (!objective.IsCompleted && wait < 8f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (objective.IsCompleted)
            {
                report.AppendLine("Level COMPLETE in " + objective.ElapsedSeconds.ToString("0.0") + " s game time.");
            }
            else
            {
                Flag("level not completed by following the guide");
                report.AppendLine("```\n" + objective.BuildStatusReport() + "\n```");
            }

            yield return new WaitForSecondsRealtime(1f);
            yield return Shot(level + "_end");
        }

        report.AppendLine();
    }

    int FindView(LaserMirrorCamera.ViewMode mode)
    {
        int keep = mirrorCamera.ViewIndex;
        int found = -1;
        for (int i = 0; i < mirrorCamera.ViewCount && found < 0; i++)
        {
            mirrorCamera.SelectView(i);
            if (mirrorCamera.CurrentMode == mode) found = i;
        }

        mirrorCamera.SelectView(keep);
        return found;
    }

    // ----- Measuring -------------------------------------------------------------------------------------

    static readonly Vector2Int[] Arrows = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    static string ArrowName(Vector2Int a) => a.x > 0 ? "►" : a.x < 0 ? "◄" : a.y > 0 ? "▲" : "▼";

    struct Probe
    {
        public bool hasGoal, centred, reached;
        public Vector3 dot, ring;     // mirror camera viewport (z < 0 = behind the camera)
        public Vector3 landing;       // world (or unfolded) landing point
        public Vector3 right, up;
        public float error, fov;
        public string banner, target;
        public bool inRing;
        public Vector2 plane;         // dot relative to ring centre, metres, along the picture right/up
        public Vector3[] board;       // per dartboard: monitor viewport of the spot (z = 0: spot hidden)
        public string[] boardState;
    }

    Probe TakeProbe()
    {
        var p = new Probe();
        Camera feed = mirrorCamera.FeedCamera;
        p.hasGoal = mirrorCamera.HasGoal;
        p.centred = mirrorCamera.GoalCentred;
        p.reached = mirrorCamera.GoalReached;
        p.banner = mirrorCamera.BannerLine;
        p.fov = feed.orthographic ? feed.orthographicSize : feed.fieldOfView;
        p.right = feed.transform.right;
        p.up = feed.transform.up;
        if (p.hasGoal)
        {
            p.landing = mirrorCamera.LandingPoint;
            p.dot = feed.WorldToViewportPoint(mirrorCamera.LandingPoint);
            p.ring = feed.WorldToViewportPoint(mirrorCamera.GoalPoint);
            p.error = mirrorCamera.GoalError;
            p.target = mirrorCamera.GoalTargetName;
            p.inRing = mirrorCamera.GoalInRing;
            Vector3 rel = mirrorCamera.LandingPoint - mirrorCamera.GoalPoint;
            p.plane = new Vector2(Vector3.Dot(rel, p.right), Vector3.Dot(rel, p.up));
            // As shown on the monitor: the close-up can be flipped to match the dartboard
            if (mirrorCamera.PictureFlippedX) { p.plane.x = -p.plane.x; p.dot.x = 1f - p.dot.x; p.ring.x = 1f - p.ring.x; }
            if (mirrorCamera.PictureFlippedY) { p.plane.y = -p.plane.y; p.dot.y = 1f - p.dot.y; p.ring.y = 1f - p.ring.y; }
        }

        p.board = new Vector3[boardViews.Length];
        p.boardState = new string[boardViews.Length];
        for (int i = 0; i < boardViews.Length; i++)
        {
            Transform spot = boardViews[i].Spot;
            p.boardState[i] = boardViews[i].State.ToString();
            if (spot && spot.gameObject.activeInHierarchy)
            {
                Vector3 vp = monitorCamera.WorldToViewportPoint(spot.position);
                p.board[i] = new Vector3(vp.x, vp.y, 1f);
            }
        }

        return p;
    }

    string Describe(Probe p)
    {
        if (!p.hasGoal) return "| no ring/dot | banner: " + p.banner;
        string s = "| target " + p.target + (p.inRing ? " (in ring)" : "") + " | error " + (p.error * 1000f).ToString("0.0") + " mm, tol " + (mirrorCamera.GoalTolerance * 1000f).ToString("0") +
                   " mm | fov " + p.fov.ToString("0.00") + " | ring at (" + p.ring.x.ToString("0.00") + ", " + p.ring.y.ToString("0.00") +
                   ") dot at (" + p.dot.x.ToString("0.00") + ", " + p.dot.y.ToString("0.00") + ", z " + p.dot.z.ToString("0.0") + ")" +
                   (p.reached ? "" : " | beam misses, target guessed") + " | banner: " + p.banner;
        if (OffPicture(p.ring) || OffPicture(p.dot)) s += " | **OFF PICTURE**";
        return s;
    }

    static bool OffPicture(Vector3 vp) => vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;

    IEnumerator DirectionTest(Vector2Int arrow)
    {
        MirrorMount mirror = panel.SelectedMirror;
        Probe before = TakeProbe();
        Press(arrow);
        yield return Settle();
        Probe after = TakeProbe();

        var line = new StringBuilder("- " + ArrowName(arrow) + ": ");
        if (before.hasGoal && after.hasGoal)
        {
            // How the dot moved on the picture (incl. zoom and camera changes, so what the player sees)
            Vector2 screen = new Vector2(after.dot.x - before.dot.x, (after.dot.y - before.dot.y) / mirrorCamera.FeedCamera.aspect) * 1000f;
            Vector2 plane = (after.plane - before.plane) * 1000f;
            line.Append("plane mm " + Fmt(plane) + " " + Verdict(arrow, plane) + " | picture " + Fmt(screen) + " " + Verdict(arrow, screen));
            if (Mathf.Abs(after.fov - before.fov) > 0.25f * before.fov)
            {
                line.Append(" (zoom " + before.fov.ToString("0.00") + " → " + after.fov.ToString("0.00") + ")");
            }

            if (mirrorCamera.CurrentMode == LaserMirrorCamera.ViewMode.AlongBeam && (Verdict(arrow, screen) == "FLIP" || Verdict(arrow, plane) == "FLIP"))
            {
                Flag("close-up dot moves against the arrow");
            }
        }
        else
        {
            line.Append("picture: no dot");
        }

        for (int i = 0; i < boardViews.Length; i++)
        {
            if (before.board[i].z > 0f && after.board[i].z > 0f)
            {
                Vector2 move = new Vector2(after.board[i].x - before.board[i].x, (after.board[i].y - before.board[i].y) / monitorCamera.aspect) * 1000f;
                string verdict = Verdict(arrow, move);
                line.Append(" | " + boardViews[i].Label + " " + Fmt(move) + " " + verdict);
                if (verdict == "FLIP" && mirrorCamera.CurrentMode == LaserMirrorCamera.ViewMode.AlongBeam) Flag("dartboard " + boardViews[i].Label + " moves against the arrow (" + mirror.DisplayName + ")");
            }
        }

        report.AppendLine(line.ToString());
        Press(new Vector2Int(-arrow.x, -arrow.y));
        yield return Settle();
    }

    static string Fmt(Vector2 v) => "(" + v.x.ToString("+0.0;-0.0") + ", " + v.y.ToString("+0.0;-0.0") + ")";

    /// <summary>OK when the dot moves mostly the way of the arrow, FLIP when mostly the other way, SIDEWAYS otherwise.</summary>
    static string Verdict(Vector2Int arrow, Vector2 move)
    {
        if (move.magnitude < 0.3f) return "TINY";
        float along = Vector2.Dot(move.normalized, new Vector2(arrow.x, arrow.y));
        return along > 0.7f ? "OK" : along < -0.7f ? "FLIP" : "SIDEWAYS";
    }

    void Press(Vector2Int arrow)
    {
        if (arrow.x != 0) panel.NudgeYaw(arrow.x);
        if (arrow.y != 0) panel.NudgePitch(arrow.y);
    }

    IEnumerator Settle()
    {
        MirrorMount mirror = panel.SelectedMirror;
        float start = Time.time;
        yield return new WaitForSeconds(0.25f);
        while (mirror && mirror.IsSwinging && Time.time - start < 3f) yield return null;
        yield return new WaitForSecondsRealtime(1.0f);   // the close-up zoom follows in real time
    }

    IEnumerator SteerWithGuide(string level, int round, int m)
    {
        MirrorMount mirror = panel.SelectedMirror;
        yield return Settle();
        Probe start = TakeProbe();
        report.AppendLine("**Round " + (round + 1) + ", " + mirror.DisplayName + "** " + Describe(start));
        if (!start.hasGoal || start.centred) yield break;

        yield return Shot(level + "_r" + (round + 1) + "_m" + (m + 1) + "_a");
        int presses = 0, worse = 0, offPicture = 0, zoomJumps = 0;
        float lastFov = start.fov;
        var log = new StringBuilder();
        while (presses < MAX_PRESSES_PER_MIRROR && !objective.IsCompleted)
        {
            Probe before = TakeProbe();
            if (!before.hasGoal || before.centred) break;
            if (!mirrorCamera.TryGetSuggestedSteps(out Vector2 steps))
            {
                log.Append(" [no steps]");
                break;
            }

            Vector2Int arrow;
            if (Mathf.Abs(steps.x) >= 0.5f && Mathf.Abs(steps.x) >= Mathf.Abs(steps.y)) arrow = new Vector2Int(steps.x > 0f ? 1 : -1, 0);
            else if (Mathf.Abs(steps.y) >= 0.5f) arrow = new Vector2Int(0, steps.y > 0f ? 1 : -1);
            else if (!mirror.IsFineMode)
            {
                panel.ToggleFineMode();
                log.Append(" FINE");
                continue;
            }
            else
            {
                log.Append(" [guide says 0 steps but not centred, error " + (before.error * 1000f).ToString("0.0") + " mm]");
                break;
            }

            Press(arrow);
            presses++;
            yield return Settle();
            Probe after = TakeProbe();
            log.Append(" " + ArrowName(arrow));
            if (after.hasGoal && after.error > before.error * 1.05f + 0.0005f)
            {
                worse++;
                Vector2 screen = new Vector2(after.dot.x - before.dot.x, after.dot.y - before.dot.y) * 1000f;
                log.Append("(worse " + (before.error * 1000f).ToString("0.0") + "→" + (after.error * 1000f).ToString("0.0") + " mm, dot " + Fmt(screen) + ")");
            }

            if (after.hasGoal && (OffPicture(after.ring) || OffPicture(after.dot))) offPicture++;
            if (after.fov > 0f && (after.fov > lastFov * 2f || after.fov < lastFov * 0.5f)) zoomJumps++;
            lastFov = after.fov;
            if (presses % 12 == 0) yield return Shot(level + "_r" + (round + 1) + "_m" + (m + 1) + "_p" + presses);
        }

        Probe end = TakeProbe();
        report.AppendLine("  presses:" + log);
        report.AppendLine("  → " + presses + " presses, " + worse + " made it worse, " + offPicture + "x ring/dot off picture, " + zoomJumps + " zoom jumps. End " + Describe(end));
        if (worse > 2) Flag(mirror.DisplayName + ": " + worse + " guided presses made the error larger");
        if (offPicture > 0) Flag(mirror.DisplayName + ": ring or dot off the picture " + offPicture + "x");
        if (zoomJumps > 0) Flag(mirror.DisplayName + ": " + zoomJumps + " zoom jumps");
        yield return Shot(level + "_r" + (round + 1) + "_m" + (m + 1) + "_z");
    }

    void Flag(string what)
    {
        problems++;
        report.AppendLine("  - ⚠ " + what);
    }

    // ----- Look-around tour: screenshots of the scene from many places ---------------------------------------

    static readonly string[] TourObjects =
    {
        "Desk", "ETP_MastLift", "ETP_ControlCabinets_Floor", "ETP_OpticalTable", "ETP_ToolTrolley", "ETP_OverheadCrane",
        "TowerConstellation", "InstructionBoard", "Lumi"
    };

    /// <summary>
    /// Player view first, then every object in <see cref="TourObjects"/> and every zone under Hall_Props from two sides.
    /// The camera copies the main camera (same post-processing), so the pictures look like the game.
    /// </summary>
    IEnumerator Tour()
    {
        report.AppendLine("## Tour");
        Camera main = Camera.main;
        var go = new GameObject("Tour Camera");
        var cam = go.AddComponent<Camera>();
        if (main) cam.CopyFrom(main);
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        if (main)
        {
            cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            yield return TourShot(cam, rt, "player_view");
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.15f, 0.6f));
            yield return TourShot(cam, rt, "player_to_hall");
        }

        var targets = new List<Transform>();
        foreach (string name in TourObjects)
        {
            GameObject found = GameObject.Find(name);
            if (found) targets.Add(found.transform);
            else report.AppendLine("- not found: " + name);
        }

        GameObject hallProps = GameObject.Find("Hall_Props");
        if (hallProps)
        {
            foreach (Transform zone in hallProps.transform) targets.Add(zone);
        }

        foreach (Transform target in targets)
        {
            if (!TryBounds(target, out Bounds bounds)) continue;
            for (int side = 0; side < 2; side++)
            {
                float yaw = side == 0 ? 35f : 215f;
                Vector3 dir = Quaternion.Euler(22f, yaw, 0f) * Vector3.back;
                float distance = Mathf.Clamp(bounds.extents.magnitude * 1.9f, 1.2f, 25f);
                Vector3 position = ClampToRoom(bounds.center + dir * distance, bounds.center);
                cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(bounds.center - position));
                yield return TourShot(cam, rt, target.name + "_" + side);
            }
        }

        Destroy(go);
        rt.Release();
    }

    static bool TryBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is LineRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return any;
    }

    /// <summary>Keeps the camera inside the room the object is in (control room or hall).</summary>
    static Vector3 ClampToRoom(Vector3 position, Vector3 target)
    {
        bool controlRoom = target.x < -9.15f && target.z < -3f && target.y > 2.0f;
        Vector3 min = controlRoom ? new Vector3(-16.9f, 2.4f, -10.8f) : new Vector3(-16.8f, 0.3f, -10.7f);
        Vector3 max = controlRoom ? new Vector3(-9.4f, 4.8f, -3.2f) : new Vector3(16.8f, 7.6f, 10.7f);
        return Vector3.Max(min, Vector3.Min(max, position));
    }

    IEnumerator TourShot(Camera cam, RenderTexture rt, string name)
    {
        yield return null;
        cam.Render();
        yield return new WaitForEndOfFrame();
        shotCount++;
        string file = "tour_" + shotCount.ToString("000") + "_" + name + ".png";
        Save(rt, file, 1600);
        report.AppendLine("- " + file + " at " + V(cam.transform.position));
    }

    // ----- Screenshots -----------------------------------------------------------------------------------

    IEnumerator Shot(string name)
    {
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();
        shotCount++;
        string prefix = shotCount.ToString("000") + "_" + name;
        if (monitorCamera.targetTexture) Save(monitorCamera.targetTexture, prefix + "_monitor.png", 1920);
        if (mirrorCamera.Texture) Save(mirrorCamera.Texture, prefix + "_mirrorcam.png", 1024);
    }

    static void Save(RenderTexture source, string fileName, int maxWidth)
    {
        int width = Mathf.Min(source.width, maxWidth);
        int height = Mathf.Max(1, source.height * width / source.width);
        RenderTexture temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, temp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = temp;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temp);
        File.WriteAllBytes(Path.Combine(Folder, fileName), texture.EncodeToPNG());
        Destroy(texture);
    }
}

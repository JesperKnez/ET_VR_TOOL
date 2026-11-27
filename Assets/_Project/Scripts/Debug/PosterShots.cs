using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Takes a large set of high-resolution pictures of Room_Scene, as reference images for the project poster.
/// Started by LaserPlaytestBot with the request "play poster" (see LaserPlaytestBridge).
/// Output: Poster/References/*.jpg (3840 x 2160, some 2160 x 3840) plus small copies in Poster/References/preview.
///
/// The pictures come in four groups, named after their first letter:
///   A  towers solid, the way the hall looks in real life
///   B  towers see-through with the laser of level 1, the way the game looks
///   C  level 3 (long arms) with every mirror on its designed angle: the beams run through the whole hall
///   D  the "LEVEL COMPLETE" screen
/// Nothing in the scene or in any asset is changed: everything happens in Play Mode.
/// </summary>
public class PosterShots
{
    const int WIDTH = 3840, HEIGHT = 2160, PREVIEW_WIDTH = 1024;
    const float CONTROL_ROOM_FLOOR = 2.2f, EYE_HEIGHT = 1.65f;

    static readonly Vector3 ControlRoomCentre = new Vector3(-13.15f, 3.6f, -7f);
    static readonly Vector3 HallCentre = new Vector3(0f, 1.5f, 0f);

    public static string Folder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Poster", "References");

    readonly StringBuilder report;
    Camera cam;
    RenderTexture wide, tall;
    int count;

    PosterShots(StringBuilder report) => this.report = report;

    public static IEnumerator Run(StringBuilder report) => new PosterShots(report).RunAll();

    IEnumerator RunAll()
    {
        report.AppendLine("## Poster reference pictures");
        Directory.CreateDirectory(Path.Combine(Folder, "preview"));
        foreach (string old in Directory.GetFiles(Folder, "*.jpg")) File.Delete(old);
        foreach (string old in Directory.GetFiles(Folder, "*.png")) File.Delete(old);
        foreach (string old in Directory.GetFiles(Path.Combine(Folder, "preview"), "*.jpg")) File.Delete(old);

        var manager = Object.FindAnyObjectByType<LaserLevelManager>();
        var objective = Object.FindAnyObjectByType<LaserAlignmentObjective>();
        var towers = Object.FindAnyObjectByType<TowerTransparency>();
        Camera main = Camera.main;
        CreateCamera(main);
        HidePlayerRig();

        // Lumi looks at the camera that takes the picture
        var lumiLook = Object.FindAnyObjectByType<EinsteinTelescope.Lumi.LumiLookAt>();
        if (lumiLook) lumiLook.Target = cam.transform;

        // Where the player stands (without a headset the XR camera lies on the floor, so only x and z are used)
        Vector3 eye = new Vector3(-12.5f, CONTROL_ROOM_FLOOR + EYE_HEIGHT, -6.5f);
        if (main && InControlRoom(main.transform.position))
        {
            eye = new Vector3(main.transform.position.x, CONTROL_ROOM_FLOOR + EYE_HEIGHT, main.transform.position.z);
        }

        report.AppendLine("- player eye at " + eye.ToString("F2"));

        // ----- A: towers solid ------------------------------------------------------------------------------
        yield return new WaitForSecondsRealtime(2f);
        yield return ObjectShots("A_board_briefing", "InstructionBoard", 3.6f, 50f);   // the briefing is open at the start
        if (manager && manager.IsBriefingOpen) manager.ToggleBriefing();
        if (towers) towers.SetClearImmediate(false);
        yield return new WaitForSecondsRealtime(1f);
        yield return HallShots("A");
        yield return ControlRoomShots("A", eye);
        yield return PropShots("A");

        // ----- B: the game: see-through towers and the laser of level 1 --------------------------------------
        if (towers) towers.SetClearImmediate(true);
        yield return new WaitForSecondsRealtime(1f);
        yield return HallShots("B");
        yield return ControlRoomShots("B", eye);
        yield return LaserShots("B", manager);
        yield return MonitorShots("B_L1");

        // ----- C: level 3, all mirrors on their designed angle -----------------------------------------------
        if (manager && manager.LevelCount >= 3)
        {
            manager.LoadLevel(2);
            yield return new WaitForSecondsRealtime(2f);
            if (manager.IsBriefingOpen) manager.ToggleBriefing();
            if (objective)
            {
                objective.StopObjective();   // on the designed angles the level would complete and show the results
                objective.enabled = false;
            }

            foreach (MirrorMount mirror in manager.CurrentLevel.Mirrors)
            {
                if (mirror) mirror.ResetToNominal();
            }

            if (towers) towers.SetClearImmediate(true);
            yield return new WaitForSecondsRealtime(2f);
            yield return HallShots("C");
            yield return ControlRoomShots("C", eye);
            yield return LaserShots("C", manager);
            yield return MonitorShots("C_L3");

            // The same beams with solid towers: only the part outside the tanks and tubes shows
            if (towers) towers.SetClearImmediate(false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("C_solid_player_view", eye + new Vector3(-0.3f, 0.1f, -0.3f), MonitorCentre(eye), 70f);

            // ----- D: level complete ---------------------------------------------------------------------------
            if (towers) towers.SetClearImmediate(true);
            if (objective)
            {
                objective.enabled = true;
                if (objective.IsWaitingToStart) objective.StartObjective();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!objective.IsCompleted) objective.ForceComplete();
                yield return new WaitForSecondsRealtime(2.5f);
                yield return MonitorShots("D_complete");
                Vector3 monitor = MonitorCentre(eye);
                yield return Shot("D_complete_player_view", eye, monitor, 60f);
                yield return Shot("D_complete_monitor_close", Vector3.Lerp(eye, monitor, 0.35f), monitor, 60f);
                yield return Shot("D_complete_over_shoulder", eye + (eye - monitor).normalized * 1.2f + Vector3.up * 0.4f, monitor, 55f);
            }
        }

        report.AppendLine("- " + count + " pictures in " + Folder);
        Object.Destroy(cam.gameObject);
        wide.Release();
        tall.Release();
    }

    // ----- Groups of pictures ---------------------------------------------------------------------------------

    /// <summary>The hall from its corners, from eye height, from the floor and from above.</summary>
    IEnumerator HallShots(string group)
    {
        string g = group + "_hall_";
        // High views from the corners and the walls
        yield return Shot(g + "overview_north", new Vector3(0f, 5.6f, 10.3f), new Vector3(0f, 1.2f, -3.5f), 65f);
        yield return Shot(g + "overview_northeast", new Vector3(15.8f, 5.8f, 9.8f), new Vector3(-1f, 1.2f, -2.5f), 60f);
        yield return Shot(g + "overview_northwest", new Vector3(-15.8f, 5.8f, 9.8f), new Vector3(1f, 1.2f, -2.5f), 60f);
        yield return Shot(g + "overview_southeast", new Vector3(15.8f, 5.6f, -9.8f), new Vector3(-2f, 1.4f, 0.5f), 60f);
        yield return Shot(g + "overview_east", new Vector3(16.3f, 4.5f, 0f), new Vector3(0f, 1.6f, -1.5f), 60f);
        yield return Shot(g + "overview_south_mid", new Vector3(6f, 6.2f, -10.3f), new Vector3(-1f, 1f, 2f), 70f);

        // Eye height, walking through the hall
        yield return Shot(g + "eye_between_arms", new Vector3(0f, 1.7f, 7.5f), new Vector3(0f, 2.2f, -5.1f), 60f);
        yield return Shot(g + "eye_between_arms_close", new Vector3(0f, 1.7f, 1.5f), new Vector3(0.8f, 2.3f, -5.5f), 65f);
        yield return Shot(g + "eye_west_arm", new Vector3(-14.5f, 1.7f, 9.3f), new Vector3(-1f, 2f, -4f), 50f);
        yield return Shot(g + "eye_east_arm", new Vector3(14.5f, 1.7f, 9.3f), new Vector3(1f, 2f, -4f), 50f);
        yield return Shot(g + "eye_from_east_wall", new Vector3(13f, 1.7f, -3f), new Vector3(0f, 2.2f, -3f), 60f);
        yield return Shot(g + "eye_to_control_room", new Vector3(-2f, 1.7f, 3.5f), new Vector3(-12f, 3.4f, -6f), 60f);
        yield return Shot(g + "control_room_from_hall", new Vector3(-4.5f, 3.6f, 4f), new Vector3(-13f, 3.3f, -7f), 50f);

        // Low, looking up at the tanks
        yield return Shot(g + "low_vertex", new Vector3(0f, 0.45f, -1.2f), new Vector3(0.6f, 3.2f, -6f), 65f);
        yield return Shot(g + "low_east_mid", new Vector3(7.5f, 0.5f, -1.5f), new Vector3(4.4f, 2.8f, -0.6f), 65f);
        yield return Shot(g + "low_west_end", new Vector3(-8.6f, 0.5f, 9f), new Vector3(-11.2f, 2.6f, 6f), 65f);
        yield return Shot(g + "low_tube_west", new Vector3(-6.5f, 0.6f, -3.5f), new Vector3(-2f, 2f, -2.5f), 60f);

        // Tall pictures for a portrait poster
        yield return Shot(g + "tall_vertex", new Vector3(0f, 1.3f, 0.5f), new Vector3(0.3f, 2.8f, -5.6f), 60f, true);
        yield return Shot(g + "tall_between_arms", new Vector3(0f, 1.6f, 9f), new Vector3(0f, 3f, -5.1f), 55f, true);
        yield return Shot(g + "tall_east_mid", new Vector3(8.2f, 1.2f, 3.4f), new Vector3(4.3f, 2.6f, -0.9f), 55f, true);
        yield return Shot(g + "tall_overview_northeast", new Vector3(15.8f, 6.2f, 9.8f), new Vector3(0f, 1.8f, -3f), 55f, true);

        // Straight down, as a map (the crane would cover half of it)
        GameObject crane = GameObject.Find("ETP_OverheadCrane");
        var hidden = new List<Renderer>();
        if (crane) foreach (Renderer r in crane.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
        yield return Shot(g + "top_down", new Vector3(0f, 7.6f, -0.4f), new Vector3(0f, 0f, 0f), 100f);
        foreach (Renderer r in hidden) if (r) r.enabled = true;
    }

    /// <summary>The control room: where the player stands, the corners, the monitor and the view through the glass.</summary>
    IEnumerator ControlRoomShots(string group, Vector3 eye)
    {
        string g = group + "_room_";
        Vector3 monitor = MonitorCentre(eye);
        Vector3 back = (eye - monitor).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, back).normalized;

        yield return Shot(g + "player_view", eye, monitor, 75f);
        yield return Shot(g + "player_view_wide", eye + back * 0.6f, monitor + Vector3.up * 0.2f, 90f);
        yield return Shot(g + "over_shoulder", eye + back * 1.4f + Vector3.up * 0.45f, monitor, 60f);
        yield return Shot(g + "monitor_left", monitor + back * 1.5f + side * 1.5f + Vector3.up * 0.25f, monitor, 55f);
        yield return Shot(g + "monitor_right", monitor + back * 1.5f - side * 1.5f + Vector3.up * 0.25f, monitor, 55f);
        yield return Shot(g + "monitor_close", monitor + back * 1.25f + Vector3.up * 0.05f, monitor, 60f);
        yield return Shot(g + "monitor_low", monitor + back * 1.3f + side * 0.9f - Vector3.up * 0.35f, monitor + Vector3.up * 0.15f, 60f);
        yield return Shot(g + "player_tall", eye + back * 0.8f + Vector3.up * 0.1f, monitor + Vector3.up * 0.3f, 70f, true);

        // Corners of the room (the glass walls are on the +x and +z side)
        yield return Shot(g + "corner_southwest", new Vector3(-13.2f, 4.4f, -10.4f), new Vector3(-11f, 3f, -4f), 80f);
        yield return Shot(g + "corner_northwest", new Vector3(-16.7f, 4.5f, -3.5f), new Vector3(-10.5f, 3.1f, -9f), 80f);
        yield return Shot(g + "corner_southeast", new Vector3(-9.6f, 4.5f, -10.5f), new Vector3(-15f, 3.1f, -4.5f), 80f);
        yield return Shot(g + "corner_northeast", new Vector3(-9.6f, 4.5f, -3.5f), new Vector3(-15.5f, 3.1f, -9.5f), 80f);

        // Through the glass
        yield return Shot(g + "window_to_hall", new Vector3(-9.9f, 3.85f, -6.5f), new Vector3(2f, 1.4f, -3f), 65f);
        yield return Shot(g + "window_east", new Vector3(-11f, 3.85f, -7.5f), new Vector3(0f, 1.6f, -5.5f), 60f);
        yield return Shot(g + "window_north", new Vector3(-13f, 3.85f, -5f), new Vector3(-9f, 1.8f, 6f), 65f);
        yield return Shot(g + "window_tall", new Vector3(-11.2f, 3.9f, -5.2f), new Vector3(0f, 2f, -3f), 60f, true);
    }

    /// <summary>Lumi, the control room props and the props in the hall, each from two sides.</summary>
    IEnumerator PropShots(string group)
    {
        yield return ObjectShots(group + "_lumi_close", "Lumi", 1.1f, 40f);
        yield return ObjectShots(group + "_lumi", "Lumi", 2.4f, 50f);
        yield return ObjectShots(group + "_lumi_tall", "Lumi", 1.3f, 45f, true);
        foreach (string name in new[] { "Desk", "SignalTower_Wall", "ETP_OpticalTable", "ETP_ControlCabinets_Floor", "ETP_ToolTrolley", "ETP_MastLift" })
        {
            yield return ObjectShots(group + "_prop_" + name, name, 0f, 55f);
        }

        yield return Shot(group + "_prop_crane_a", new Vector3(14f, 4.2f, -9f), new Vector3(0f, 7f, 2f), 70f);
        yield return Shot(group + "_prop_crane_b", new Vector3(-6f, 3f, 8.5f), new Vector3(4f, 7f, -2f), 70f);

        GameObject hallProps = GameObject.Find("Hall_Props");
        if (hallProps)
        {
            foreach (Transform zone in hallProps.transform)
            {
                if (zone.name.Contains("Ceiling")) continue;   // seen from below these are just the ceiling
                yield return ObjectShots(group + "_zone_" + zone.name.Replace(" ", ""), zone, 0f, 60f);
            }
        }
    }

    /// <summary>Close to the optics: the laser, every mirror and the detector of the current level, and along the beams.</summary>
    IEnumerator LaserShots(string group, LaserLevelManager manager)
    {
        if (!manager || manager.CurrentLevel == null) yield break;
        string g = group + "_laser_";
        LaserLevel level = manager.CurrentLevel;
        if (level.Laser) yield return ObjectShots(g + "source", level.Laser.transform, 2.3f, 40f);
        if (level.Detector) yield return ObjectShots(g + "detector", level.Detector.transform, 2.3f, 40f);
        int n = 0;
        foreach (MirrorMount mirror in level.Mirrors)
        {
            if (!mirror) continue;
            n++;
            yield return ObjectShots(g + "mirror" + n, mirror.transform, 2.0f, 40f);
        }

        // The beam splitter tower and along both arms
        yield return Shot(g + "splitter", new Vector3(-1.9f, 2.1f, -3.2f), new Vector3(0.3f, 1.3f, -5.4f), 50f);
        yield return Shot(g + "splitter_from_above", new Vector3(0.2f, 4.6f, -2.6f), new Vector3(0.6f, 1.3f, -5.6f), 60f);
        yield return Shot(g + "along_west_arm", new Vector3(-9.6f, 1.9f, 5.6f), new Vector3(0f, 1.33f, -5.1f), 45f);
        yield return Shot(g + "along_east_arm", new Vector3(9.6f, 1.9f, 5.6f), new Vector3(0f, 1.33f, -5.1f), 45f);
        yield return Shot(g + "from_vertex", new Vector3(1.4f, 2.4f, -9.6f), new Vector3(-0.5f, 1.4f, 0f), 75f);
        yield return Shot(g + "arms_from_above", new Vector3(0f, 6.4f, 5.5f), new Vector3(0f, 1.3f, -3.4f), 70f);
        yield return Shot(g + "splitter_tall", new Vector3(-2.2f, 1.5f, -2.9f), new Vector3(0.4f, 2.2f, -5.5f), 55f, true);
    }

    /// <summary>The curved monitor picture itself (3840 x 1200) and the mirror camera picture.</summary>
    IEnumerator MonitorShots(string name)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        GameObject monitor = GameObject.Find("Monitor UI Camera");
        Camera monitorCamera = monitor ? monitor.GetComponent<Camera>() : null;
        if (monitorCamera && monitorCamera.targetTexture) Save(monitorCamera.targetTexture, name + "_monitor_ui", true);
        var mirrorCamera = Object.FindAnyObjectByType<LaserMirrorCamera>();
        if (mirrorCamera && mirrorCamera.Texture) Save(mirrorCamera.Texture, name + "_mirror_camera", true);
    }

    // ----- One picture ----------------------------------------------------------------------------------------

    IEnumerator ObjectShots(string name, string objectName, float distance, float fov, bool portrait = false)
    {
        GameObject found = GameObject.Find(objectName);
        if (!found)
        {
            report.AppendLine("- not found: " + objectName);
            yield break;
        }

        yield return ObjectShots(name, found.transform, distance, fov, portrait);
    }

    /// <summary>
    /// Two pictures of one object, from the side of the middle of its room, 35 degrees to the left and to the right.
    /// Distance 0 = far enough to see the whole object.
    /// </summary>
    IEnumerator ObjectShots(string name, Transform target, float distance, float fov, bool portrait = false)
    {
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        Vector3 centre = bounds.center;
        bool room = InControlRoom(centre);
        Vector3 toMiddle = (room ? ControlRoomCentre : HallCentre) - centre;
        toMiddle.y = 0f;
        if (toMiddle.sqrMagnitude < 0.01f) toMiddle = Vector3.forward;
        toMiddle.Normalize();
        if (distance <= 0f) distance = Mathf.Clamp(bounds.extents.magnitude * 2.1f, 1.3f, 22f);

        for (int side = 0; side < 2; side++)
        {
            Vector3 dir = Quaternion.Euler(0f, side == 0 ? -35f : 35f, 0f) * toMiddle;
            Vector3 position = centre + dir * distance + Vector3.up * distance * 0.22f;
            Vector3 min = room ? new Vector3(-16.9f, 2.5f, -10.8f) : new Vector3(-16.8f, 0.3f, -10.7f);
            Vector3 max = room ? new Vector3(-9.4f, 4.9f, -3.2f) : new Vector3(16.8f, 7.4f, 10.7f);
            position = Vector3.Max(min, Vector3.Min(max, position));
            yield return Shot(name + (side == 0 ? "_a" : "_b"), position, centre, fov, portrait);
        }
    }

    IEnumerator Shot(string name, Vector3 position, Vector3 lookAt, float fov, bool portrait = false)
    {
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        cam.fieldOfView = fov;
        cam.targetTexture = portrait ? tall : wide;
        yield return null;   // one full frame with the camera in place (the beams are widened for it in LateUpdate)
        yield return null;
        yield return new WaitForEndOfFrame();
        Save(cam.targetTexture, name, false);
        report.AppendLine("- " + name + " from " + position.ToString("F1") + " to " + lookAt.ToString("F1"));
    }

    void Save(RenderTexture source, string name, bool png)
    {
        count++;
        string file = count.ToString("000") + "_" + name;
        byte[] full = Encode(source, source.width, png, 93);
        File.WriteAllBytes(Path.Combine(Folder, file + (png ? ".png" : ".jpg")), full);
        File.WriteAllBytes(Path.Combine(Folder, "preview", file + ".jpg"), Encode(source, Mathf.Min(source.width, PREVIEW_WIDTH), false, 80));
    }

    static byte[] Encode(RenderTexture source, int width, bool png, int quality)
    {
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
        byte[] bytes = png ? texture.EncodeToPNG() : texture.EncodeToJPG(quality);
        Object.Destroy(texture);
        return bytes;
    }

    // ----- Setup ----------------------------------------------------------------------------------------------

    void CreateCamera(Camera main)
    {
        var go = new GameObject("Poster Camera");
        cam = go.AddComponent<Camera>();
        if (main) cam.CopyFrom(main);
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        wide = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        tall = new RenderTexture(HEIGHT, WIDTH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = wide;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        go.AddComponent<PosterBeamWidth>();
    }

    /// <summary>Controllers, hands and their rays do not belong in the pictures.</summary>
    static void HidePlayerRig()
    {
        GameObject rig = GameObject.Find("XR Origin Hands (XR Rig)");
        if (!rig) return;
        foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    static bool InControlRoom(Vector3 p) => p.x < -9.15f && p.z < -3f && p.y > 2f;

    static Vector3 MonitorCentre(Vector3 eye)
    {
        GameObject screen = GameObject.Find("Monitor Screen");
        var renderer = screen ? screen.GetComponent<Renderer>() : null;
        return renderer ? renderer.bounds.center : eye + new Vector3(1f, -0.3f, 1f);
    }
}

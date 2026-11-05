using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PS5 (DualSense) or Xbox controller for testing the laser game without aiming the rays.
/// Creates itself on Play (only in the editor and in development builds), so nothing is needed in the scene.
/// Works alongside Carlo's LaserDebugKeyboard.
///   (Left stick, right stick, R2, gyro and R3: walking, pointing, pressing and looking, see GamepadPlaytestRig)
///   D-pad          the arrows on the screen, one step per press
///   L1 / R1        previous / next mirror
///   Cross          fine / coarse
///   Square         mirror back to its start position
///   L2             undo
///   Triangle       talk to Lumi (hold, see LumiListener)
///   Circle         laser shutter
///   Options        next level
///   Create         open / close the briefing
///   L3             beam path of the active level in the Console (diagnostics)
/// </summary>
public class LaserGamepadControls : MonoBehaviour
{
    LaserLevelManager levelManager;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Laser Gamepad Controls");
        DontDestroyOnLoad(go);
        go.AddComponent<LaserGamepadControls>();
    }
#endif

    void Update()
    {
        var pad = Gamepad.current;
        var panel = MirrorControlPanel.FirstActive;
        if (pad == null || !panel) return;

        // D-pad: single steps
        if (pad.dpad.left.wasPressedThisFrame) panel.NudgeYaw(-1);
        if (pad.dpad.right.wasPressedThisFrame) panel.NudgeYaw(1);
        if (pad.dpad.up.wasPressedThisFrame) panel.NudgePitch(1);
        if (pad.dpad.down.wasPressedThisFrame) panel.NudgePitch(-1);

        if (pad.leftShoulder.wasPressedThisFrame) panel.SelectPrevious();
        if (pad.rightShoulder.wasPressedThisFrame) panel.SelectNext();
        if (pad.buttonSouth.wasPressedThisFrame) panel.ToggleFineMode();
        if (pad.buttonWest.wasPressedThisFrame) panel.ResetSelectedToStart();
        if (pad.leftTrigger.wasPressedThisFrame) panel.Undo();   // Triangle is "talk to Lumi" (LumiListener)

        if (!levelManager) levelManager = FindAnyObjectByType<LaserLevelManager>();
        if (!levelManager) return;

        if (pad.buttonEast.wasPressedThisFrame)
        {
            var level = levelManager.CurrentLevel;
            if (level && level.Laser) level.Laser.ToggleShutter();
        }
        if (pad.startButton.wasPressedThisFrame) levelManager.NextLevel();
        if (pad.selectButton.wasPressedThisFrame) levelManager.ToggleBriefing();
        if (pad.leftStickButton.wasPressedThisFrame) LogBeam(levelManager.CurrentLevel);
    }

    /// <summary>Writes the beam path of the active level to the Console: where each segment ends and what it hits.</summary>
    static void LogBeam(LaserLevel level)
    {
        if (!level || !level.Laser) return;
        var laser = level.Laser;
        var report = new System.Text.StringBuilder(
            $"[Beam] {level.name}: laser at {laser.transform.position.ToString("F2")}, shutter {(laser.IsShutterOpen ? "open" : "closed")}\n");

        var segments = laser.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            string hit = "nothing";
            if (s.HitCollider)
            {
                var parent = s.HitCollider.transform.parent;
                hit = (parent ? parent.name + " / " : "") + s.HitCollider.name;
            }
            report.AppendLine($"  {i,2}: {s.Start.ToString("F2")} -> {s.End.ToString("F2")}  power {s.Power:0.00}  hits {hit}");
        }
        Debug.Log(report.ToString());
    }
}

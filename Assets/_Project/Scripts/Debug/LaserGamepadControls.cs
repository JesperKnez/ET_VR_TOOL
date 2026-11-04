using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PS5 (DualSense) of Xbox-controller om het laserspel te testen zonder met de rays te mikken.
/// Maakt zichzelf aan bij Play (alleen in de editor en in development builds), dus niets in de scene nodig.
/// Werkt naast Carlo's LaserDebugKeyboard.
///   Linker stick   geselecteerde spiegel vloeiend kantelen (verder duwen = sneller)
///   D-pad          één stap per druk (precies)
///   L1 / R1        vorige / volgende spiegel
///   Kruisje        fijn / grof
///   Vierkantje     spiegel terug naar de beginstand
///   Driehoekje     undo
///   Rondje         sluiter van de laser
///   Options        volgend level
///   Create         briefing open / dicht
///   L3             straalpad van het actieve level in de Console (diagnose)
/// </summary>
public class LaserGamepadControls : MonoBehaviour
{
    const float DEADZONE = 0.15f;

    LaserLevelManager levelManager;
    bool holding;

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

        // Stick: vloeiend, snelheid evenredig met hoe ver je duwt (vanaf de deadzone opnieuw vanaf 0)
        Vector2 stick = pad.leftStick.ReadValue();
        float amount = Mathf.InverseLerp(DEADZONE, 1f, stick.magnitude);
        if (amount > 0f)
        {
            panel.SetHoldInput(stick.normalized * amount);
            holding = true;
        }
        else if (holding)
        {
            panel.StopHoldMove();
            holding = false;
        }

        // D-pad: losse stappen
        if (pad.dpad.left.wasPressedThisFrame) panel.NudgeYaw(-1);
        if (pad.dpad.right.wasPressedThisFrame) panel.NudgeYaw(1);
        if (pad.dpad.up.wasPressedThisFrame) panel.NudgePitch(1);
        if (pad.dpad.down.wasPressedThisFrame) panel.NudgePitch(-1);

        if (pad.leftShoulder.wasPressedThisFrame) panel.SelectPrevious();
        if (pad.rightShoulder.wasPressedThisFrame) panel.SelectNext();
        if (pad.buttonSouth.wasPressedThisFrame) panel.ToggleFineMode();
        if (pad.buttonWest.wasPressedThisFrame) panel.ResetSelectedToStart();
        if (pad.buttonNorth.wasPressedThisFrame) panel.Undo();

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

    /// <summary>Schrijft het straalpad van het actieve level naar de Console: waar elk stuk eindigt en wat het raakt.</summary>
    static void LogBeam(LaserLevel level)
    {
        if (!level || !level.Laser) return;
        var laser = level.Laser;
        var report = new System.Text.StringBuilder(
            $"[Beam] {level.name}: laser op {laser.transform.position.ToString("F2")}, sluiter {(laser.IsShutterOpen ? "open" : "dicht")}\n");

        var segments = laser.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            string hit = "niets";
            if (s.HitCollider)
            {
                var parent = s.HitCollider.transform.parent;
                hit = (parent ? parent.name + " / " : "") + s.HitCollider.name;
            }
            report.AppendLine($"  {i,2}: {s.Start.ToString("F2")} -> {s.End.ToString("F2")}  vermogen {s.Power:0.00}  raakt {hit}");
        }
        Debug.Log(report.ToString());
    }
}

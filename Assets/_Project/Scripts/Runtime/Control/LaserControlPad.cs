using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>What a button on the control pad does.</summary>
    public enum LaserPadAction
    {
        YawLeft,
        YawRight,
        PitchUp,
        PitchDown,
        PreviousMirror,
        NextMirror,
        ToggleFineMode,
        ResetMirror,
        ToggleShutter,
        ToggleInfraredViewer,
        ToggleWavelength,
        NextLevel,
        ShowBriefing,
        Undo
    }

    /// <summary>
    /// Placeholder control console: a world-space UI panel with a big D-pad that you click with the
    /// XR controller rays (or the mouse). Each button carries a <see cref="LaserPadButton"/>; this
    /// component turns their presses into calls on the <see cref="MirrorControlPanel"/> and keeps the
    /// labels (selected mirror, step size, angles) up to date.
    /// Replace it with your team's real control-room buttons later: they call the same panel methods.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserControlPad : MonoBehaviour
    {
        private const float ReadoutChangeThreshold = 0.005f;

        [Tooltip("The panel to steer. Empty = the first MirrorControlPanel in the scene.")]
        [SerializeField] private MirrorControlPanel controlPanel = null;

        [Tooltip("The laser for the shutter button. Empty = the first laser in the scene.")]
        [SerializeField] private LaserEmitter laser = null;

        [Tooltip("Optional: for the NEXT LEVEL button and the level label.")]
        [SerializeField] private LaserLevelManager levelManager = null;

        [Header("Labels (optional)")]
        [SerializeField] private Text mirrorNameText = null;
        [SerializeField] private Text stepModeText = null;
        [SerializeField] private Text anglesText = null;
        [SerializeField] private Text shutterText = null;
        [SerializeField] private Text infraredText = null;
        [SerializeField] private Text wavelengthText = null;
        [SerializeField] private Text levelText = null;

        private Vector2 m_LastShownAngles = new Vector2(float.NaN, float.NaN);
        private MirrorMount m_LastShownMirror;
        private bool m_LastShownFine;
        private int m_LastShownShutter = -1;
        private int m_LastShownInfrared = -1;
        private float m_LastShownWavelength = -1f;
        private int m_LastShownLevel = -1;

        /// <summary>The panel this pad steers (assigned, or found in the scene).</summary>
        public MirrorControlPanel Panel => controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;

        /// <summary>The laser whose shutter this pad controls (assigned, or found in the scene).</summary>
        public LaserEmitter Laser
        {
            get
            {
                if (laser != null)
                {
                    return laser;
                }

                var emitters = LaserEmitter.ActiveEmitters;
                return emitters.Count > 0 ? emitters[0] : null;
            }
        }

        /// <summary>The laser for the shutter / wavelength buttons (set by the level manager).</summary>
        public void SetLaser(LaserEmitter newLaser)
        {
            laser = newLaser;
            RefreshLabels(true);
        }

        /// <summary>Do a button's action once (a tap).</summary>
        public void Execute(LaserPadAction action)
        {
            MirrorControlPanel panel = Panel;
            switch (action)
            {
                case LaserPadAction.YawLeft: panel?.YawLeft(); break;
                case LaserPadAction.YawRight: panel?.YawRight(); break;
                case LaserPadAction.PitchUp: panel?.PitchUp(); break;
                case LaserPadAction.PitchDown: panel?.PitchDown(); break;
                case LaserPadAction.PreviousMirror: panel?.SelectPrevious(); break;
                case LaserPadAction.NextMirror: panel?.SelectNext(); break;
                case LaserPadAction.ToggleFineMode: panel?.ToggleFineMode(); break;
                case LaserPadAction.ResetMirror: panel?.ResetSelectedToStart(); break;
                case LaserPadAction.ToggleShutter: Laser?.ToggleShutter(); break;
                case LaserPadAction.ToggleInfraredViewer: LaserVisibility.SetInfraredViewer(!LaserVisibility.InfraredViewerActive); break;
                case LaserPadAction.ToggleWavelength: ToggleWavelength(Laser); break;
                case LaserPadAction.NextLevel: if (levelManager != null) levelManager.NextLevel(); break;
                case LaserPadAction.ShowBriefing: if (levelManager != null) levelManager.ToggleBriefing(); break;
                case LaserPadAction.Undo: panel?.Undo(); break;
            }

            RefreshLabels(true);
        }

        /// <summary>
        /// Switches between the green 532 nm alignment laser and the 1064 nm infrared main laser
        /// (the one LIGO, Virgo and ET-HF really use). The infrared beam is invisible without the IR viewer.
        /// </summary>
        public static void ToggleWavelength(LaserEmitter emitter)
        {
            if (emitter == null)
            {
                return;
            }

            emitter.SetWavelength(LaserOptics.IsHumanVisible(emitter.WavelengthNm) ? 1064f : 532f);
        }

        /// <summary>Inspector-friendly version for button events: toggles 532 / 1064 nm on this pad's laser.</summary>
        public void ToggleLaserWavelength()
        {
            ToggleWavelength(Laser);
        }

        /// <summary>Start moving continuously (button held down). Only for the four direction actions.</summary>
        public void BeginHold(LaserPadAction action)
        {
            Vector2 direction = DirectionOf(action);
            if (direction != Vector2.zero)
            {
                Panel?.SetHoldInput(direction);
            }
        }

        /// <summary>Stop continuous movement (button released).</summary>
        public void EndHold()
        {
            Panel?.StopHoldMove();
        }

        /// <summary>Direction of a D-pad action: x = yaw, y = pitch. Zero for the other actions.</summary>
        public static Vector2 DirectionOf(LaserPadAction action)
        {
            switch (action)
            {
                case LaserPadAction.YawLeft: return Vector2.left;
                case LaserPadAction.YawRight: return Vector2.right;
                case LaserPadAction.PitchUp: return Vector2.up;
                case LaserPadAction.PitchDown: return Vector2.down;
                default: return Vector2.zero;
            }
        }

        private void LateUpdate()
        {
            RefreshLabels(false);
        }

        private void RefreshLabels(bool force)
        {
            MirrorControlPanel panel = Panel;
            MirrorMount mirror = panel != null ? panel.SelectedMirror : null;

            if (mirror != null)
            {
                bool mirrorChanged = force || mirror != m_LastShownMirror || mirror.IsFineMode != m_LastShownFine;
                var angles = new Vector2(mirror.ActualYaw, mirror.ActualPitch);
                bool anglesChanged = float.IsNaN(m_LastShownAngles.x) ||
                                     Mathf.Abs(angles.x - m_LastShownAngles.x) > ReadoutChangeThreshold ||
                                     Mathf.Abs(angles.y - m_LastShownAngles.y) > ReadoutChangeThreshold;

                if (mirrorChanged)
                {
                    m_LastShownMirror = mirror;
                    m_LastShownFine = mirror.IsFineMode;
                    SetText(mirrorNameText, mirror.DisplayName);
                    SetText(stepModeText, (mirror.IsFineMode ? "FINE\n" : "COARSE\n") + LaserText.Number(mirror.CurrentStepDegrees, 2) + "°");
                }

                if (mirrorChanged || anglesChanged)
                {
                    m_LastShownAngles = angles;
                    SetText(anglesText, "yaw " + LaserText.SignedNumber(angles.x, 2) + "°    pitch " + LaserText.SignedNumber(angles.y, 2) + "°");
                }
            }
            else if (force || m_LastShownMirror != null)
            {
                m_LastShownMirror = null;
                SetText(mirrorNameText, "no mirror");
            }

            LaserEmitter emitter = Laser;
            int shutter = emitter == null ? 2 : emitter.IsShutterOpen ? 1 : 0;
            if (force || shutter != m_LastShownShutter)
            {
                m_LastShownShutter = shutter;
                SetText(shutterText, shutter == 1 ? "SHUTTER\nOPEN" : shutter == 0 ? "SHUTTER\nCLOSED" : "NO LASER");
            }

            float wavelength = emitter != null ? emitter.WavelengthNm : 0f;
            if (force || !Mathf.Approximately(wavelength, m_LastShownWavelength))
            {
                m_LastShownWavelength = wavelength;
                string kind = emitter == null ? "" : LaserOptics.IsHumanVisible(wavelength) ? "" : " IR";
                SetText(wavelengthText, emitter == null ? "NO LASER" : "LASER\n" + LaserText.Number(wavelength, 0) + " nm" + kind);
            }

            int infrared = LaserVisibility.InfraredViewerActive ? 1 : 0;
            if (force || infrared != m_LastShownInfrared)
            {
                m_LastShownInfrared = infrared;
                SetText(infraredText, infrared == 1 ? "IR VIEWER\nON" : "IR VIEWER\nOFF");
            }

            int level = levelManager != null ? levelManager.CurrentLevelIndex : -1;
            if (levelManager != null && (force || level != m_LastShownLevel))
            {
                m_LastShownLevel = level;
                SetText(levelText, "LEVEL " + (level + 1) + "/" + levelManager.LevelCount + "  " + levelManager.CurrentTitle.ToUpperInvariant());
            }
        }

        private static void SetText(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}

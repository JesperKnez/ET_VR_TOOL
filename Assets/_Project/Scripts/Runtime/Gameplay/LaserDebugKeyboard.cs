using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Keyboard controls for testing the laser puzzle on a PC without a headset. Chosen so they do not
    /// clash with the XR Device Simulator (which uses WASD, Q/E, Tab, Space, Shift and more):
    /// Arrow keys: tilt the selected mirror (tap = one step, hold = keep moving after a moment).
    /// Comma / Period ( &lt; &gt; ): previous / next mirror. Slash: fine/coarse steps. Backspace: reset mirror.
    /// L: laser shutter. K: switch green 532 nm / infrared 1064 nm laser. I: IR viewer. P: print the AI-tutor report.
    /// N: next level. H: open / close the briefing screen. U: undo the last move. T: alignment tone on / off.
    /// Remove or disable this component in the final build.
    /// </summary>
    public class LaserDebugKeyboard : MonoBehaviour
    {
        private const float HoldDelaySeconds = 0.35f;

        [SerializeField] private MirrorControlPanel controlPanel = null;
        [SerializeField] private LaserEmitter laser = null;
        [SerializeField] private LaserAlignmentObjective objective = null;
        [Tooltip("Optional: N loads the next level.")]
        [SerializeField] private LaserLevelManager levelManager = null;
        [Tooltip("Optional: T switches the alignment tone on / off.")]
        [SerializeField] private LaserAudioFeedback audioFeedback = null;

        private float m_HoldTime;
        private bool m_Holding;

        /// <summary>The laser for the shutter / wavelength keys (set by the level manager).</summary>
        public void SetLaser(LaserEmitter newLaser)
        {
            laser = newLaser;
        }

        private void Update()
        {
            if (controlPanel == null)
            {
                return;
            }

            Vector2 axis = ReadArrowAxis();
            HandleTapAndHold(axis);

            if (WasPressed(DebugKey.PreviousMirror)) controlPanel.SelectPrevious();
            if (WasPressed(DebugKey.NextMirror)) controlPanel.SelectNext();
            if (WasPressed(DebugKey.FineMode)) controlPanel.ToggleFineMode();
            if (WasPressed(DebugKey.Reset)) controlPanel.ResetSelectedToStart();
            if (WasPressed(DebugKey.InfraredViewer)) LaserVisibility.SetInfraredViewer(!LaserVisibility.InfraredViewerActive);
            if (WasPressed(DebugKey.Shutter) && laser != null) laser.ToggleShutter();
            if (WasPressed(DebugKey.Wavelength) && laser != null) LaserControlPad.ToggleWavelength(laser);
            if (WasPressed(DebugKey.Report) && objective != null) Debug.Log(objective.BuildStatusReport());
            if (WasPressed(DebugKey.NextLevel) && levelManager != null) levelManager.NextLevel();
            if (WasPressed(DebugKey.Briefing) && levelManager != null) levelManager.ToggleBriefing();
            if (WasPressed(DebugKey.Undo)) controlPanel.Undo();
            if (WasPressed(DebugKey.Tone) && audioFeedback != null) audioFeedback.ToggleAlignmentTone();
        }

        private void HandleTapAndHold(Vector2 axis)
        {
            if (axis == Vector2.zero)
            {
                if (m_Holding)
                {
                    controlPanel.StopHoldMove();
                }

                m_Holding = false;
                m_HoldTime = 0f;
                return;
            }

            if (m_HoldTime == 0f)
            {
                // First frame of a key press: one discrete step.
                if (axis.x != 0f) controlPanel.NudgeYaw(axis.x > 0f ? 1 : -1);
                if (axis.y != 0f) controlPanel.NudgePitch(axis.y > 0f ? 1 : -1);
            }

            m_HoldTime += Time.deltaTime;
            if (m_HoldTime >= HoldDelaySeconds)
            {
                m_Holding = true;
                controlPanel.SetHoldInput(axis);
            }
        }

        private enum DebugKey
        {
            PreviousMirror,
            NextMirror,
            FineMode,
            Reset,
            InfraredViewer,
            Shutter,
            Wavelength,
            Report,
            NextLevel,
            Briefing,
            Undo,
            Tone
        }

#if ENABLE_INPUT_SYSTEM
        private static Vector2 ReadArrowAxis()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            float x = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }

        private static bool WasPressed(DebugKey key)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (key)
            {
                case DebugKey.PreviousMirror: return keyboard.commaKey.wasPressedThisFrame;
                case DebugKey.NextMirror: return keyboard.periodKey.wasPressedThisFrame;
                case DebugKey.FineMode: return keyboard.slashKey.wasPressedThisFrame;
                case DebugKey.Reset: return keyboard.backspaceKey.wasPressedThisFrame;
                case DebugKey.InfraredViewer: return keyboard.iKey.wasPressedThisFrame;
                case DebugKey.Shutter: return keyboard.lKey.wasPressedThisFrame;
                case DebugKey.Wavelength: return keyboard.kKey.wasPressedThisFrame;
                case DebugKey.Report: return keyboard.pKey.wasPressedThisFrame;
                case DebugKey.NextLevel: return keyboard.nKey.wasPressedThisFrame;
                case DebugKey.Briefing: return keyboard.hKey.wasPressedThisFrame;
                case DebugKey.Undo: return keyboard.uKey.wasPressedThisFrame;
                case DebugKey.Tone: return keyboard.tKey.wasPressedThisFrame;
                default: return false;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        private static Vector2 ReadArrowAxis()
        {
            float x = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            return new Vector2(x, y);
        }

        private static bool WasPressed(DebugKey key)
        {
            switch (key)
            {
                case DebugKey.PreviousMirror: return Input.GetKeyDown(KeyCode.Comma);
                case DebugKey.NextMirror: return Input.GetKeyDown(KeyCode.Period);
                case DebugKey.FineMode: return Input.GetKeyDown(KeyCode.Slash);
                case DebugKey.Reset: return Input.GetKeyDown(KeyCode.Backspace);
                case DebugKey.InfraredViewer: return Input.GetKeyDown(KeyCode.I);
                case DebugKey.Shutter: return Input.GetKeyDown(KeyCode.L);
                case DebugKey.Wavelength: return Input.GetKeyDown(KeyCode.K);
                case DebugKey.Report: return Input.GetKeyDown(KeyCode.P);
                case DebugKey.NextLevel: return Input.GetKeyDown(KeyCode.N);
                case DebugKey.Briefing: return Input.GetKeyDown(KeyCode.H);
                case DebugKey.Undo: return Input.GetKeyDown(KeyCode.U);
                case DebugKey.Tone: return Input.GetKeyDown(KeyCode.T);
                default: return false;
            }
        }
#else
        private static Vector2 ReadArrowAxis() => Vector2.zero;
        private static bool WasPressed(DebugKey key) => false;
#endif
    }
}

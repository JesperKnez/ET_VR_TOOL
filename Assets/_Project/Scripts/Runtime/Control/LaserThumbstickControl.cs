using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#else
using UnityEngine.XR;
#endif

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Steer the selected mirror with an XR controller thumbstick. Hold the GRIP button and push the stick:
    /// a short push = one step, keep pushing = keep moving (speed follows how far you push).
    /// While the grip is held: primary button (A / X) = fine / coarse steps, secondary button (B / Y) = next mirror.
    /// The template's thumbstick locomotion (turn, move, teleport) is paused while you steer, so the
    /// stick never turns or moves the player at the same time.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserThumbstickControl : MonoBehaviour
    {
        public enum Hand
        {
            Right,
            Left,
            Either
        }

        private const float HoldDelaySeconds = 0.35f;

        [Tooltip("Empty = the first MirrorControlPanel in the scene.")]
        [SerializeField] private MirrorControlPanel controlPanel = null;

        [SerializeField] private Hand hand = Hand.Either;

        [Tooltip("Only steer while the grip button is held (recommended: the stick also drives locomotion).")]
        [SerializeField] private bool requireGrip = true;

        [SerializeField, Range(0.05f, 0.9f)] private float deadZone = 0.3f;

        [Tooltip("Pause these locomotion components while steering (matched by type name).")]
        [SerializeField] private bool pauseLocomotionWhileSteering = true;
        [SerializeField] private string[] locomotionTypeNameParts = { "TurnProvider", "MoveProvider", "TeleportationProvider" };

        private readonly List<Behaviour> m_Paused = new List<Behaviour>();
        private bool m_Steering;
        private bool m_StickActive;
        private float m_StickTime;
        private bool m_Holding;
        private bool m_PrimaryWasPressed;
        private bool m_SecondaryWasPressed;

        /// <summary>True while the grip is held and the stick steers the mirror.</summary>
        public bool IsSteering => m_Steering;

        private MirrorControlPanel Panel => controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;

        private void Update()
        {
            ReadInput(out Vector2 stick, out bool grip, out bool primary, out bool secondary);
            Process(stick, grip, primary, secondary, Time.deltaTime);
        }

        private void OnDisable()
        {
            SetSteering(false);
        }

        /// <summary>
        /// Feed controller input (also used by tests and by teams with their own input code).
        /// <paramref name="stick"/> -1..1, buttons pressed or not, <paramref name="deltaTime"/> in seconds.
        /// </summary>
        public void Process(Vector2 stick, bool grip, bool primary, bool secondary, float deltaTime)
        {
            MirrorControlPanel panel = Panel;
            bool steering = panel != null && (!requireGrip || grip);
            SetSteering(steering);

            if (!steering)
            {
                ReleaseStick(panel);
                m_PrimaryWasPressed = primary;
                m_SecondaryWasPressed = secondary;
                return;
            }

            if (primary && !m_PrimaryWasPressed) panel.ToggleFineMode();
            if (secondary && !m_SecondaryWasPressed) panel.SelectNext();
            m_PrimaryWasPressed = primary;
            m_SecondaryWasPressed = secondary;

            Vector2 input = new Vector2(Mathf.Abs(stick.x) >= deadZone ? stick.x : 0f, Mathf.Abs(stick.y) >= deadZone ? stick.y : 0f);
            if (input == Vector2.zero)
            {
                ReleaseStick(panel);
                return;
            }

            if (!m_StickActive)
            {
                // First frame past the dead zone: one discrete step on each pushed axis.
                m_StickActive = true;
                m_StickTime = 0f;
                if (input.x != 0f) panel.NudgeYaw(input.x > 0f ? 1 : -1);
                if (input.y != 0f) panel.NudgePitch(input.y > 0f ? 1 : -1);
            }

            m_StickTime += deltaTime;
            if (m_StickTime >= HoldDelaySeconds)
            {
                m_Holding = true;
                // Speed follows how far the stick is pushed past the dead zone.
                Vector2 scaled = new Vector2(Rescale(input.x), Rescale(input.y));
                panel.SetHoldInput(scaled);
            }
        }

        private float Rescale(float value)
        {
            if (value == 0f)
            {
                return 0f;
            }

            float magnitude = Mathf.Clamp01((Mathf.Abs(value) - deadZone) / (1f - deadZone));
            return Mathf.Sign(value) * Mathf.Lerp(0.25f, 1f, magnitude);
        }

        private void ReleaseStick(MirrorControlPanel panel)
        {
            if (m_Holding && panel != null)
            {
                panel.StopHoldMove();
            }

            m_StickActive = false;
            m_Holding = false;
            m_StickTime = 0f;
        }

        private void SetSteering(bool steering)
        {
            if (steering == m_Steering)
            {
                return;
            }

            m_Steering = steering;
            if (!pauseLocomotionWhileSteering)
            {
                return;
            }

            if (steering)
            {
                m_Paused.Clear();
                foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour != null && behaviour.enabled && IsLocomotion(behaviour))
                    {
                        behaviour.enabled = false;
                        m_Paused.Add(behaviour);
                    }
                }
            }
            else
            {
                foreach (Behaviour behaviour in m_Paused)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = true;
                    }
                }

                m_Paused.Clear();
            }
        }

        private bool IsLocomotion(MonoBehaviour behaviour)
        {
            string typeName = behaviour.GetType().Name;
            foreach (string part in locomotionTypeNameParts)
            {
                if (!string.IsNullOrEmpty(part) && typeName.Contains(part))
                {
                    return true;
                }
            }

            return false;
        }

        // ----- Input ------------------------------------------------------------------------------

        private void ReadInput(out Vector2 stick, out bool grip, out bool primary, out bool secondary)
        {
            stick = Vector2.zero;
            grip = primary = secondary = false;
            if (hand != Hand.Left) Accumulate(true, ref stick, ref grip, ref primary, ref secondary);
            if (hand != Hand.Right) Accumulate(false, ref stick, ref grip, ref primary, ref secondary);
        }

        private static void Accumulate(bool right, ref Vector2 stick, ref bool grip, ref bool primary, ref bool secondary)
        {
            if (!ReadController(right, out Vector2 s, out bool g, out bool p, out bool b) || (!g && grip))
            {
                return;
            }

            // With "Either", the hand whose grip is held wins.
            if (g || stick == Vector2.zero)
            {
                stick = s;
            }

            grip |= g;
            primary |= p;
            secondary |= b;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool ReadController(bool right, out Vector2 stick, out bool grip, out bool primary, out bool secondary)
        {
            stick = Vector2.zero;
            grip = primary = secondary = false;
            XRController device = right ? XRController.rightHand : XRController.leftHand;
            if (device == null)
            {
                return false;
            }

            var stickControl = device.TryGetChildControl<Vector2Control>("{Primary2DAxis}");
            var gripControl = device.TryGetChildControl<ButtonControl>("{GripButton}");
            var primaryControl = device.TryGetChildControl<ButtonControl>("{PrimaryButton}");
            var secondaryControl = device.TryGetChildControl<ButtonControl>("{SecondaryButton}");
            stick = stickControl != null ? stickControl.ReadValue() : Vector2.zero;
            grip = gripControl != null && gripControl.isPressed;
            primary = primaryControl != null && primaryControl.isPressed;
            secondary = secondaryControl != null && secondaryControl.isPressed;
            return true;
        }
#else
        private static bool ReadController(bool right, out Vector2 stick, out bool grip, out bool primary, out bool secondary)
        {
            stick = Vector2.zero;
            grip = primary = secondary = false;
            InputDevice device = InputDevices.GetDeviceAtXRNode(right ? XRNode.RightHand : XRNode.LeftHand);
            if (!device.isValid)
            {
                return false;
            }

            device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
            device.TryGetFeatureValue(CommonUsages.gripButton, out grip);
            device.TryGetFeatureValue(CommonUsages.primaryButton, out primary);
            device.TryGetFeatureValue(CommonUsages.secondaryButton, out secondary);
            return true;
        }
#endif
    }
}

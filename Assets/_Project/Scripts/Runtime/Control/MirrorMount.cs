using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Motorised, suspended mount that tilts an optic in yaw (left/right) and pitch (up/down).
    /// Put it on the parent of a <see cref="LaserMirror"/> or <see cref="LaserBeamSplitter"/>; the
    /// mount's pivot should be at the centre of the optic.
    ///
    /// Realism:
    /// - Moves in discrete steps (coarse / fine), like the alignment sliders operators use in the
    ///   LIGO/Virgo control rooms.
    /// - Has a limited actuator range; pushing past it raises a warning.
    /// - Hangs like a pendulum, so it swings briefly after every move and then settles.
    /// - Optional ground motion (microseism, 0.1-0.3 Hz) that slowly shakes the mirror.
    ///
    /// Your teammates' buttons only need to call the public methods (NudgeYaw, NudgePitch, ...).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(LaserExecutionOrder.MirrorMount)]
    public class MirrorMount : MonoBehaviour
    {
        private const float FineSpeedFactor = 0.1f;
        private const float ReadoutChangeThreshold = 0.001f;

        private static readonly List<MirrorMount> s_ActiveMounts = new List<MirrorMount>();

        /// <summary>Raised when any mount is pushed against its actuator limit.</summary>
        public static event Action<MirrorMount> AnyActuatorLimitReached;

        [Tooltip("Name shown in readouts and the AI tutor report. Empty = GameObject name.")]
        [SerializeField] private string displayName = "";

        [Header("Actuator steps")]
        [Tooltip("Tilt per button press in coarse mode (degrees).")]
        [SerializeField, Min(0.0001f)] private float coarseStepDegrees = 0.5f;

        [Tooltip("Tilt per button press in fine mode (degrees).")]
        [SerializeField, Min(0.0001f)] private float fineStepDegrees = 0.05f;

        [Tooltip("Start in fine mode.")]
        [SerializeField] private bool startInFineMode = false;

        [Tooltip("Speed while a button is held, in coarse mode (degrees per second). Fine mode is 10x slower.")]
        [SerializeField, Min(0f)] private float holdSpeedDegreesPerSecond = 1.5f;

        [Header("Actuator range")]
        [Tooltip("Maximum yaw in either direction (degrees). Real test-mass suspensions only have about +/-0.09 degrees (1.6 mrad).")]
        [SerializeField, Range(0.1f, 45f)] private float maxYawDegrees = 8f;

        [Tooltip("Maximum pitch in either direction (degrees).")]
        [SerializeField, Range(0.1f, 45f)] private float maxPitchDegrees = 8f;

        [Header("Suspension (pendulum)")]
        [Tooltip("Let the mirror swing and settle after each move, like a suspended test mass.")]
        [SerializeField] private bool simulateSuspension = true;

        [Tooltip("Pendulum resonance in Hz. Real suspensions resonate around 0.5-1 Hz.")]
        [SerializeField, Range(0.1f, 5f)] private float resonanceFrequencyHz = 0.8f;

        [Tooltip("Damping ratio: 1 = no overshoot, lower = more swinging. Real suspensions are actively damped.")]
        [SerializeField, Range(0.05f, 2f)] private float dampingRatio = 0.5f;

        [Header("Ground motion")]
        [Tooltip("Random shaking amplitude (degrees). 0 = off. The Einstein Telescope is built underground to reduce exactly this.")]
        [SerializeField, Min(0f)] private float seismicNoiseDegrees = 0f;

        [Tooltip("Frequency of the shaking. The ocean-driven microseism is 0.1-0.3 Hz.")]
        [SerializeField, Range(0.01f, 5f)] private float seismicNoiseFrequencyHz = 0.2f;

        [Header("Start misalignment")]
        [Tooltip("Yaw at the start of the level (degrees). Use this to set up the puzzle.")]
        [SerializeField] private float initialYawDegrees = 0f;

        [Tooltip("Pitch at the start of the level (degrees).")]
        [SerializeField] private float initialPitchDegrees = 0f;

        [Tooltip("Add a random misalignment at start, so every attempt is different.")]
        [SerializeField] private bool randomizeStartMisalignment = false;

        [Tooltip("Maximum random misalignment (degrees) per axis.")]
        [SerializeField, Min(0f)] private float randomMisalignmentDegrees = 2f;

        [Header("Real-world readout")]
        [Tooltip("Distance (m) in the scene from this optic to where its beam lands.")]
        [SerializeField, Min(0.01f)] private float sceneLeverArmMeters = 2.5f;

        [Tooltip("Matching distance in the real detector (m). Einstein Telescope arm: 10 000 m.")]
        [SerializeField, Min(0.01f)] private float realLeverArmMeters = 10000f;

        [Header("Events")]
        [Tooltip("Actual angles (x = yaw, y = pitch, degrees) whenever they change.")]
        [SerializeField] private UnityEvent<Vector2> onAnglesChanged = new UnityEvent<Vector2>();

        [Tooltip("A readable line such as 'End mirror X: yaw +0.50 deg, pitch -0.10 deg (fine)'. Connect to a TextMeshPro text.")]
        [SerializeField] private UnityEvent<string> onReadoutChanged = new UnityEvent<string>();

        [Tooltip("The player tried to move past the actuator range. Good moment for a haptic buzz or warning sound.")]
        [SerializeField] private UnityEvent onActuatorLimitReached = new UnityEvent();

        [Tooltip("The mirror stopped swinging after a move.")]
        [SerializeField] private UnityEvent onSettled = new UnityEvent();

        private Quaternion m_NominalLocalRotation;
        private SuspensionAxis m_YawAxis;
        private SuspensionAxis m_PitchAxis;
        private float m_CommandedYaw;
        private float m_CommandedPitch;
        private Vector2 m_HoldInput;
        private bool m_FineMode;
        private bool m_IsSwinging;
        private bool m_HoldWasAtLimit;
        private int m_LimitHitCount;
        private float m_NoiseSeedYaw;
        private float m_NoiseSeedPitch;
        private float m_NoiseYaw;
        private float m_NoisePitch;
        private Vector2 m_LastReportedAngles = new Vector2(float.NaN, float.NaN);
        private bool m_LastReportedFineMode;
        private bool m_Initialized;

        /// <summary>All enabled mounts in the scene.</summary>
        public static IReadOnlyList<MirrorMount> ActiveMounts => s_ActiveMounts;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public bool IsFineMode => m_FineMode;
        public float CurrentStepDegrees => m_FineMode ? fineStepDegrees : coarseStepDegrees;
        public float MaxYawDegrees => maxYawDegrees;
        public float MaxPitchDegrees => maxPitchDegrees;

        /// <summary>Angle the actuator is asked to reach (degrees).</summary>
        public float CommandedYaw => m_CommandedYaw;
        public float CommandedPitch => m_CommandedPitch;

        /// <summary>Angle the mirror actually has right now, including swinging and ground motion (degrees).</summary>
        public float ActualYaw => m_YawAxis.Angle + m_NoiseYaw;
        public float ActualPitch => m_PitchAxis.Angle + m_NoisePitch;

        /// <summary>True while the mirror is still swinging towards its commanded angle.</summary>
        public bool IsSwinging => m_IsSwinging;

        /// <summary>True when either axis is at the end of its range.</summary>
        public bool IsAtLimit => Mathf.Abs(m_CommandedYaw) >= maxYawDegrees - 1e-4f || Mathf.Abs(m_CommandedPitch) >= maxPitchDegrees - 1e-4f;

        /// <summary>How often the player pushed against the actuator limit.</summary>
        public int LimitHitCount => m_LimitHitCount;

        public UnityEvent<Vector2> OnAnglesChanged => onAnglesChanged;
        public UnityEvent<string> OnReadoutChanged => onReadoutChanged;
        public UnityEvent OnActuatorLimitReached => onActuatorLimitReached;
        public UnityEvent OnSettled => onSettled;

        /// <summary>Raised (C#) whenever the actual angles change. Used by the control panel.</summary>
        public event Action<MirrorMount> AnglesChanged;

        /// <summary>Raised (C#) when the commanded angles change; the argument is the previous (yaw, pitch). Used for undo.</summary>
        public event Action<MirrorMount, Vector2> CommandChanged;

        /// <summary>Raised (C#) for every discrete step (tap) on any mirror. Used for the click sound.</summary>
        public static event Action<MirrorMount> AnyStepped;

        /// <summary>True while a hold input keeps the mirror moving.</summary>
        public bool IsHoldMoving => m_HoldInput != Vector2.zero;

        private bool m_SuppressCommandEvent;

        // ----- Button-friendly API ------------------------------------------------------------

        /// <summary>Tilt left/right by a number of steps (positive = right).</summary>
        public void NudgeYaw(int steps)
        {
            SetCommandedAngles(m_CommandedYaw + steps * CurrentStepDegrees, m_CommandedPitch);
            AnyStepped?.Invoke(this);
        }

        /// <summary>Tilt up/down by a number of steps (positive = up).</summary>
        public void NudgePitch(int steps)
        {
            SetCommandedAngles(m_CommandedYaw, m_CommandedPitch + steps * CurrentStepDegrees);
            AnyStepped?.Invoke(this);
        }

        /// <summary>Set the commanded angles without creating an undo entry (used by undo itself).</summary>
        internal void RestoreCommandedAngles(Vector2 yawPitch)
        {
            m_SuppressCommandEvent = true;
            try
            {
                SetCommandedAngles(yawPitch.x, yawPitch.y);
            }
            finally
            {
                m_SuppressCommandEvent = false;
            }
        }

        public void YawRight() => NudgeYaw(1);
        public void YawLeft() => NudgeYaw(-1);
        public void PitchUp() => NudgePitch(1);
        public void PitchDown() => NudgePitch(-1);

        /// <summary>
        /// Keep moving while a button is held. x = yaw (-1..1), y = pitch (-1..1).
        /// Call <see cref="StopHoldMove"/> when the button is released.
        /// </summary>
        public void SetHoldInput(Vector2 input)
        {
            m_HoldInput = Vector2.ClampMagnitude(input, 1f);
            if (m_HoldInput == Vector2.zero)
            {
                m_HoldWasAtLimit = false;
            }
        }

        public void BeginHoldYawRight() => SetHoldInput(new Vector2(1f, m_HoldInput.y));
        public void BeginHoldYawLeft() => SetHoldInput(new Vector2(-1f, m_HoldInput.y));
        public void BeginHoldPitchUp() => SetHoldInput(new Vector2(m_HoldInput.x, 1f));
        public void BeginHoldPitchDown() => SetHoldInput(new Vector2(m_HoldInput.x, -1f));
        public void StopHoldMove() => SetHoldInput(Vector2.zero);

        /// <summary>For a slider or knob: 0 = full left, 0.5 = centre, 1 = full right.</summary>
        public void SetYawNormalized(float value01) => SetCommandedAngles(Mathf.Lerp(-maxYawDegrees, maxYawDegrees, Mathf.Clamp01(value01)), m_CommandedPitch);

        /// <summary>For a slider or knob: 0 = full down, 0.5 = centre, 1 = full up.</summary>
        public void SetPitchNormalized(float value01) => SetCommandedAngles(m_CommandedYaw, Mathf.Lerp(-maxPitchDegrees, maxPitchDegrees, Mathf.Clamp01(value01)));

        public void SetFineMode(bool fine) => m_FineMode = fine;
        public void ToggleFineMode() => m_FineMode = !m_FineMode;

        /// <summary>Back to the designed (perfectly aligned) orientation.</summary>
        public void ResetToNominal() => SetCommandedAngles(0f, 0f);

        /// <summary>Back to the misalignment the level started with.</summary>
        public void ResetToStart() => SetCommandedAngles(initialYawDegrees, initialPitchDegrees);

        /// <summary>Ask the actuator for exact angles (degrees). Values beyond the range are clamped and reported.</summary>
        public void SetCommandedAngles(float yawDegrees, float pitchDegrees)
        {
            EnsureInitialized();
            ApplyCommand(yawDegrees, pitchDegrees, false);
        }

        /// <summary>
        /// Tilt that would give the same beam displacement in the real, kilometres-long detector.
        /// Uses the lever arms set in the inspector.
        /// </summary>
        public float RealWorldEquivalentMicroradians(float sceneDegrees)
        {
            return LaserOptics.EquivalentRealAngleMicroradians(sceneDegrees, sceneLeverArmMeters, realLeverArmMeters);
        }

        /// <summary>Readable status line for UI or the AI tutor.</summary>
        public string BuildReadout()
        {
            float yaw = ActualYaw;
            float pitch = ActualPitch;
            return DisplayName + ": yaw " + LaserText.SignedNumber(yaw, 2) + " deg, pitch " + LaserText.SignedNumber(pitch, 2) +
                   " deg (" + (m_FineMode ? "fine" : "coarse") + " steps of " + LaserText.Number(CurrentStepDegrees, 2) + " deg). " +
                   "Real ET scale: yaw " + LaserText.SignedNumber(RealWorldEquivalentMicroradians(yaw), 1) + " urad, pitch " +
                   LaserText.SignedNumber(RealWorldEquivalentMicroradians(pitch), 1) + " urad.";
        }

        // ----- Unity messages -----------------------------------------------------------------

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            s_ActiveMounts.Add(this);
        }

        private void OnDisable()
        {
            s_ActiveMounts.Remove(this);
            m_HoldInput = Vector2.zero;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            ApplyHoldInput(deltaTime);
            StepDynamics(deltaTime);
            ApplyRotation();
            RaiseChangeEvents();
        }

        // ----- Internals ----------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (m_Initialized)
            {
                return;
            }

            m_Initialized = true;
            m_NominalLocalRotation = transform.localRotation;
            m_FineMode = startInFineMode;
            m_NoiseSeedYaw = UnityEngine.Random.Range(0f, 1000f);
            m_NoiseSeedPitch = UnityEngine.Random.Range(0f, 1000f);

            float startYaw = initialYawDegrees;
            float startPitch = initialPitchDegrees;
            if (randomizeStartMisalignment)
            {
                startYaw += UnityEngine.Random.Range(-randomMisalignmentDegrees, randomMisalignmentDegrees);
                startPitch += UnityEngine.Random.Range(-randomMisalignmentDegrees, randomMisalignmentDegrees);
            }

            m_CommandedYaw = Mathf.Clamp(startYaw, -maxYawDegrees, maxYawDegrees);
            m_CommandedPitch = Mathf.Clamp(startPitch, -maxPitchDegrees, maxPitchDegrees);
            m_YawAxis = new SuspensionAxis(m_CommandedYaw);
            m_PitchAxis = new SuspensionAxis(m_CommandedPitch);
            ApplyRotation();
        }

        private void ApplyHoldInput(float deltaTime)
        {
            if (m_HoldInput == Vector2.zero)
            {
                return;
            }

            float speed = holdSpeedDegreesPerSecond * (m_FineMode ? FineSpeedFactor : 1f) * deltaTime;
            ApplyCommand(m_CommandedYaw + m_HoldInput.x * speed, m_CommandedPitch + m_HoldInput.y * speed, true);
        }

        private void ApplyCommand(float yawDegrees, float pitchDegrees, bool fromHold)
        {
            float clampedYaw = Mathf.Clamp(yawDegrees, -maxYawDegrees, maxYawDegrees);
            float clampedPitch = Mathf.Clamp(pitchDegrees, -maxPitchDegrees, maxPitchDegrees);
            bool pushedPastLimit = !Mathf.Approximately(clampedYaw, yawDegrees) || !Mathf.Approximately(clampedPitch, pitchDegrees);

            var previous = new Vector2(m_CommandedYaw, m_CommandedPitch);
            m_CommandedYaw = clampedYaw;
            m_CommandedPitch = clampedPitch;
            if (!m_SuppressCommandEvent && (previous.x != clampedYaw || previous.y != clampedPitch))
            {
                CommandChanged?.Invoke(this, previous);
            }

            if (!simulateSuspension)
            {
                m_YawAxis.SnapTo(clampedYaw);
                m_PitchAxis.SnapTo(clampedPitch);
            }

            if (!pushedPastLimit)
            {
                if (fromHold)
                {
                    m_HoldWasAtLimit = false;
                }

                return;
            }

            // While holding a button, warn once when the limit is reached instead of every frame.
            if (fromHold && m_HoldWasAtLimit)
            {
                return;
            }

            m_HoldWasAtLimit = fromHold;
            m_LimitHitCount++;
            onActuatorLimitReached.Invoke();
            AnyActuatorLimitReached?.Invoke(this);
        }

        private void StepDynamics(float deltaTime)
        {
            if (simulateSuspension)
            {
                m_YawAxis.Step(m_CommandedYaw, resonanceFrequencyHz, dampingRatio, deltaTime);
                m_PitchAxis.Step(m_CommandedPitch, resonanceFrequencyHz, dampingRatio, deltaTime);
            }
            else
            {
                m_YawAxis.SnapTo(m_CommandedYaw);
                m_PitchAxis.SnapTo(m_CommandedPitch);
            }

            if (seismicNoiseDegrees > 0f)
            {
                float t = Time.time * seismicNoiseFrequencyHz;
                m_NoiseYaw = (Mathf.PerlinNoise(m_NoiseSeedYaw, t) * 2f - 1f) * seismicNoiseDegrees;
                m_NoisePitch = (Mathf.PerlinNoise(m_NoiseSeedPitch, t) * 2f - 1f) * seismicNoiseDegrees;
            }
            else
            {
                m_NoiseYaw = 0f;
                m_NoisePitch = 0f;
            }

            bool swinging = m_YawAxis.IsMoving(m_CommandedYaw) || m_PitchAxis.IsMoving(m_CommandedPitch);
            if (m_IsSwinging && !swinging)
            {
                onSettled.Invoke();
            }

            m_IsSwinging = swinging;
        }

        private void ApplyRotation()
        {
            // Yaw turns around the mount's up axis, pitch around its (yawed) right axis.
            // Positive pitch tilts the reflective face upwards.
            Quaternion yaw = Quaternion.AngleAxis(ActualYaw, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(-ActualPitch, Vector3.right);
            transform.localRotation = m_NominalLocalRotation * yaw * pitch;
        }

        private void RaiseChangeEvents()
        {
            var angles = new Vector2(ActualYaw, ActualPitch);
            bool anglesChanged = float.IsNaN(m_LastReportedAngles.x) ||
                                 Mathf.Abs(angles.x - m_LastReportedAngles.x) > ReadoutChangeThreshold ||
                                 Mathf.Abs(angles.y - m_LastReportedAngles.y) > ReadoutChangeThreshold;
            bool modeChanged = m_FineMode != m_LastReportedFineMode;

            if (!anglesChanged && !modeChanged)
            {
                return;
            }

            m_LastReportedAngles = angles;
            m_LastReportedFineMode = m_FineMode;
            onAnglesChanged.Invoke(angles);
            AnglesChanged?.Invoke(this);
            onReadoutChanged.Invoke(BuildReadout());
        }

        private void OnValidate()
        {
            fineStepDegrees = Mathf.Min(fineStepDegrees, coarseStepDegrees);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_ActiveMounts.Clear();
            AnyActuatorLimitReached = null;
            AnyStepped = null;
        }
    }
}

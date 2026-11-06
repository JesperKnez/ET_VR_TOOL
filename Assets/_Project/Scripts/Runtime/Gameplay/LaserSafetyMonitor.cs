using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Watches all lasers and mirrors and turns mistakes into feedback events (sound, haptics,
    /// a warning light, a message for the AI tutor). It covers what goes wrong when aligning a
    /// real interferometer:
    /// 1. Stray light: the beam hits something that is not an optic. In a real detector scattered
    ///    light adds noise, and the high-power infrared beam is a serious eye and burn hazard.
    /// 2. Clipping: the beam hits the edge of an optic and loses power.
    /// 3. Back-reflection: light returns into a laser that has no Faraday isolator.
    /// 4. Actuator limit: the operator pushes a mirror beyond what its actuators can do.
    ///
    /// Feedback events fire immediately. For the score, stray light and clipping only count as a
    /// mistake when they last longer than <see cref="mistakeGraceSeconds"/>: sweeping a beam across
    /// an edge is normal while aligning, leaving it on a wall is not. Problems that already exist
    /// when the counters are reset (the misaligned start of a level) are not counted.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserSafetyMonitor : MonoBehaviour
    {
        [Tooltip("Total stray light (fraction of laser output) that triggers a warning.")]
        [SerializeField, Range(0.001f, 1f)] private float strayLightWarningFraction = 0.02f;

        [Tooltip("Total clipped light (fraction of laser output) that triggers a warning.")]
        [SerializeField, Range(0.001f, 1f)] private float clippingWarningFraction = 0.05f;

        [Tooltip("Stray light or clipping only counts as a mistake after lasting this long (seconds).")]
        [SerializeField, Min(0f)] private float mistakeGraceSeconds = 2f;

        [Tooltip("Minimum time between two warning messages of the same kind (seconds), so feedback does not spam.")]
        [SerializeField, Min(0f)] private float warningCooldownSeconds = 2f;

        [Header("Events")]
        [SerializeField] private UnityEvent onStrayLightStarted = new UnityEvent();
        [SerializeField] private UnityEvent onStrayLightStopped = new UnityEvent();
        [SerializeField] private UnityEvent onClippingStarted = new UnityEvent();
        [SerializeField] private UnityEvent onClippingStopped = new UnityEvent();
        [SerializeField] private UnityEvent onBackReflection = new UnityEvent();
        [SerializeField] private UnityEvent onActuatorLimitReached = new UnityEvent();

        [Tooltip("A short explanation of what went wrong, for a UI text or the AI tutor.")]
        [SerializeField] private UnityEvent<string> onWarningMessage = new UnityEvent<string>();

        private readonly IncidentTracker m_Stray = new IncidentTracker();
        private readonly IncidentTracker m_Clipping = new IncidentTracker();
        private readonly IncidentTracker m_BackReflection = new IncidentTracker();
        private float m_LastLimitWarningTime = float.NegativeInfinity;
        private string m_StraySurfaceName = "";
        private float m_LastStrayTime = float.NegativeInfinity;

        [Tooltip("Clipping that starts within this many seconds after stray light (the beam coming in over an edge) is not counted and gives no warning.")]
        [SerializeField, Min(0f)] private float enteringSeconds = 3f;

        public int StrayLightIncidents => m_Stray.Count;
        public int ClippingIncidents => m_Clipping.Count;
        public int BackReflectionIncidents => m_BackReflection.Count;
        public int ActuatorLimitIncidents { get; private set; }

        /// <summary>All mistakes counted since the last reset.</summary>
        public int TotalIncidents => StrayLightIncidents + ClippingIncidents + BackReflectionIncidents + ActuatorLimitIncidents;

        public bool IsStrayLightActive => m_Stray.Active;
        public bool IsClippingActive => m_Clipping.Active;

        /// <summary>Name of the object the strongest stray beam currently hits (empty if none).</summary>
        public string StraySurfaceName => m_StraySurfaceName;

        public UnityEvent OnStrayLightStarted => onStrayLightStarted;
        public UnityEvent OnStrayLightStopped => onStrayLightStopped;
        public UnityEvent OnClippingStarted => onClippingStarted;
        public UnityEvent OnClippingStopped => onClippingStopped;
        public UnityEvent OnActuatorLimitReached => onActuatorLimitReached;
        public UnityEvent<string> OnWarningMessage => onWarningMessage;

        /// <summary>Start counting from zero. Problems that are present right now are not counted.</summary>
        public void ResetCounters()
        {
            m_Stray.Reset();
            m_Clipping.Reset();
            m_BackReflection.Reset();
            ActuatorLimitIncidents = 0;
        }

        private void OnEnable()
        {
            MirrorMount.AnyActuatorLimitReached += HandleActuatorLimit;
        }

        private void OnDisable()
        {
            MirrorMount.AnyActuatorLimitReached -= HandleActuatorLimit;
        }

        private void LateUpdate()
        {
            float stray = 0f;
            float clipped = 0f;
            float backReflected = 0f;
            float strongestStray = 0f;
            Collider straySurface = null;

            var emitters = LaserEmitter.ActiveEmitters;
            for (int e = 0; e < emitters.Count; e++)
            {
                LaserEmitter emitter = emitters[e];
                stray += emitter.StrayLightFraction;
                clipped += emitter.ClippedFraction;
                backReflected += emitter.BackReflectionFraction;

                var events = emitter.ScatterEvents;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind == LaserScatterKind.StrayLight && events[i].Power > strongestStray)
                    {
                        strongestStray = events[i].Power;
                        straySurface = events[i].Collider;
                    }
                }
            }

            m_StraySurfaceName = straySurface != null ? straySurface.name : "";
            float now = Time.time;

            switch (m_Stray.Update(stray >= strayLightWarningFraction, now, mistakeGraceSeconds))
            {
                case IncidentTracker.Change.Started:
                    onStrayLightStarted.Invoke();
                    string surfaceText = string.IsNullOrEmpty(m_StraySurfaceName) ? "a surface that is not an optic" : "'" + m_StraySurfaceName + "'";
                    SendMessageWithCooldown(m_Stray, now, "Stray light: the beam is hitting " + surfaceText +
                        ". In a real detector scattered light adds noise, and the infrared beam can burn skin and blind you.");
                    break;
                case IncidentTracker.Change.Stopped:
                    onStrayLightStopped.Invoke();
                    break;
            }

            // A beam that starts beside a mirror must cross the mirror's edge to get onto it: clipping that starts while
            // the beam is (or just was) off the optics is the beam coming in, not a mistake (playtest, 6 Nov 2026).
            if (m_Stray.Active) m_LastStrayTime = now;
            bool comingIn = now - m_LastStrayTime < enteringSeconds;
            switch (m_Clipping.Update(clipped >= clippingWarningFraction, now, mistakeGraceSeconds, comingIn))
            {
                case IncidentTracker.Change.Started:
                    onClippingStarted.Invoke();
                    SendMessageWithCooldown(m_Clipping, now,
                        "Clipping: the beam hits the edge of an optic and loses power. Steer it back to the centre of the mirror.");
                    break;
                case IncidentTracker.Change.Stopped:
                    onClippingStopped.Invoke();
                    break;
            }

            if (m_BackReflection.Update(backReflected >= strayLightWarningFraction, now, 0f) == IncidentTracker.Change.Started)
            {
                onBackReflection.Invoke();
                SendMessageWithCooldown(m_BackReflection, now,
                    "Back-reflection: light is going back into the laser. Without a Faraday isolator this makes the laser unstable.");
            }
        }

        private void SendMessageWithCooldown(IncidentTracker tracker, float now, string message)
        {
            if (now - tracker.LastMessageTime < warningCooldownSeconds)
            {
                return;
            }

            tracker.LastMessageTime = now;
            onWarningMessage.Invoke(message);
        }

        private void HandleActuatorLimit(MirrorMount mount)
        {
            float now = Time.time;
            if (now - m_LastLimitWarningTime < warningCooldownSeconds * 0.5f)
            {
                return;
            }

            m_LastLimitWarningTime = now;
            ActuatorLimitIncidents++;
            onActuatorLimitReached.Invoke();
            onWarningMessage.Invoke("Actuator limit: " + mount.DisplayName +
                                    " cannot tilt any further. Real mirror actuators only have a small range, so try another mirror.");
        }

        /// <summary>Tracks one kind of problem: when it started, and whether it was counted as a mistake.</summary>
        private sealed class IncidentTracker
        {
            public enum Change { None, Started, Stopped }

            public bool Active { get; private set; }
            public int Count { get; private set; }
            public float LastMessageTime { get; set; } = float.NegativeInfinity;

            private float m_ActiveSince;
            private bool m_Counted;
            private bool m_ExistedAtReset;
            private bool m_JustReset;

            public void Reset()
            {
                Count = 0;
                m_ExistedAtReset = Active;
                m_JustReset = true;
            }

            public Change Update(bool isActiveNow, float now, float graceSeconds, bool excused = false)
            {
                Change change = Change.None;
                if (isActiveNow && !Active)
                {
                    Active = true;
                    m_ActiveSince = now;
                    m_Counted = false;
                    // Seen in the very first check after a reset: it was already there, not the player's doing.
                    // Seen in the very first check after a reset, or excused by the caller (the beam is just coming in
                    // over the edge of an optic): not the player's doing, so not counted and no warning.
                    m_ExistedAtReset = m_JustReset || excused;
                    change = excused ? Change.None : Change.Started;
                }
                else if (!isActiveNow && Active)
                {
                    Active = false;
                    m_ExistedAtReset = false;
                    change = Change.Stopped;
                }

                m_JustReset = false;

                if (Active && !m_Counted && !m_ExistedAtReset && now - m_ActiveSince >= graceSeconds)
                {
                    m_Counted = true;
                    Count++;
                }

                return change;
            }
        }
    }
}

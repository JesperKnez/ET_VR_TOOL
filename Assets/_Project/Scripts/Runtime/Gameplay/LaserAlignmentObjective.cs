using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// The goal of an alignment level: all required targets must stay aligned for a while
    /// (a real interferometer must hold alignment before it can "lock").
    /// Also tracks time and mistakes for a star score, and can describe the current situation
    /// in plain text so an AI tutor (LLM) can give grounded hints.
    ///
    /// Timer: <see cref="StartObjective"/> starts it. The level manager calls it when the briefing is closed;
    /// with <c>startOnFirstMirrorMove</c> it also starts as soon as the player moves a mirror, so a player who
    /// leaves the briefing open can still finish the level.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 10)]
    public class LaserAlignmentObjective : MonoBehaviour
    {
        [Tooltip("All of these detectors must be aligned at the same time.")]
        [SerializeField] private LaserTarget[] requiredTargets = new LaserTarget[0];

        [Tooltip("How long (seconds) everything must stay aligned to complete the objective.")]
        [SerializeField, Min(0f)] private float holdSeconds = 2f;

        [Tooltip("Start timing automatically when the scene starts. Otherwise call StartObjective().")]
        [SerializeField] private bool startAutomatically = true;

        [Tooltip("Start the timer when the player first moves a mirror, if it is not running yet " +
                 "(e.g. the briefing is still open). Without it the level can only be won after the briefing is closed.")]
        [SerializeField] private bool startOnFirstMirrorMove = true;

        [Tooltip("Optional. Used to count mistakes for the score and the tutor report.")]
        [SerializeField] private LaserSafetyMonitor safetyMonitor = null;

        [Header("Scoring")]
        [Tooltip("Finishing faster than this with at most the 3-star number of mistakes gives 3 stars. " +
                 "A level can override it (LaserLevel.parTimeSeconds).")]
        [SerializeField, Min(1f)] private float parTimeSeconds = 120f;

        [Tooltip("Up to this many mistakes still gives 3 stars (within the par time).")]
        [SerializeField, Min(0)] private int threeStarMistakeLimit = 1;

        [Tooltip("Up to this many mistakes still gives 2 stars (within twice the par time).")]
        [SerializeField, Min(0)] private int twoStarMistakeLimit = 4;

        [Header("Events")]
        [SerializeField] private UnityEvent onObjectiveStarted = new UnityEvent();
        [Tooltip("Progress of the hold timer, 0..1, while everything is aligned.")]
        [SerializeField] private UnityEvent<float> onHoldProgress = new UnityEvent<float>();
        [SerializeField] private UnityEvent onAlignmentLostDuringHold = new UnityEvent();
        [SerializeField] private UnityEvent onCompleted = new UnityEvent();
        [Tooltip("Stars awarded (1-3) when completed.")]
        [SerializeField] private UnityEvent<int> onStarsAwarded = new UnityEvent<int>();

        private bool m_Running;
        private bool m_Completed;
        private float m_StartTime;
        private float m_CompletionTime;
        private float m_HoldTimer;
        private int m_Stars;
        private float m_ParTimeOverride = -1f;

        public bool IsRunning => m_Running;
        public bool IsCompleted => m_Completed;

        /// <summary>True when the level is loaded but the timer has not started yet (briefing open, no mirror moved).</summary>
        public bool IsWaitingToStart => !m_Running && !m_Completed;

        public int Stars => m_Stars;
        public float HoldProgress => holdSeconds <= 0f ? (AllTargetsAligned() ? 1f : 0f) : Mathf.Clamp01(m_HoldTimer / holdSeconds);

        /// <summary>Seconds everything must stay aligned.</summary>
        public float HoldSeconds => holdSeconds;

        /// <summary>Seconds everything has been aligned so far (0 when not aligned).</summary>
        public float HoldElapsedSeconds => Mathf.Min(m_HoldTimer, holdSeconds);

        public float ElapsedSeconds => m_Completed ? m_CompletionTime - m_StartTime : m_Running ? Time.time - m_StartTime : 0f;
        public int Mistakes => safetyMonitor != null ? safetyMonitor.TotalIncidents : 0;

        /// <summary>3-star time limit of the current level (seconds). 2 stars: twice this.</summary>
        public float ParTimeSeconds => m_ParTimeOverride > 0f ? m_ParTimeOverride : parTimeSeconds;
        public int ThreeStarMistakeLimit => threeStarMistakeLimit;
        public int TwoStarMistakeLimit => twoStarMistakeLimit;

        public UnityEvent OnCompleted => onCompleted;
        public UnityEvent<int> OnStarsAwarded => onStarsAwarded;

        public void StartObjective()
        {
            m_Running = true;
            m_Completed = false;
            m_HoldTimer = 0f;
            m_Stars = 0;
            m_StartTime = Time.time;
            if (safetyMonitor != null)
            {
                safetyMonitor.ResetCounters();
            }

            onObjectiveStarted.Invoke();
        }

        /// <summary>Replace the detectors that must be aligned (e.g. when a new level loads). Call StartObjective() after.</summary>
        public void SetRequiredTargets(LaserTarget[] targets)
        {
            requiredTargets = targets ?? new LaserTarget[0];
            m_HoldTimer = 0f;
        }

        /// <summary>The detectors that must be aligned at the same time.</summary>
        public System.Collections.Generic.IReadOnlyList<LaserTarget> RequiredTargets => requiredTargets;

        /// <summary>
        /// 3-star time limit for the current level, in seconds. 0 or less = the default set on this component.
        /// Called by the level manager when a level loads (before the briefing is shown).
        /// </summary>
        public void SetParTime(float seconds)
        {
            m_ParTimeOverride = seconds > 0f ? seconds : -1f;
        }

        /// <summary>
        /// The star rules of the current level in one short sentence, e.g.
        /// "3 stars: within 2:00 with at most 1 safety incident. 2 stars: within 4:00 with at most 4. Otherwise 1 star."
        /// </summary>
        public string DescribeStarRules()
        {
            float par = ParTimeSeconds;
            return "3 stars: within " + LaserBestTimes.FormatTime(par) + " with at most " + threeStarMistakeLimit +
                   (threeStarMistakeLimit == 1 ? " safety incident" : " safety incidents") +
                   ". 2 stars: within " + LaserBestTimes.FormatTime(par * 2f) + " with at most " + twoStarMistakeLimit +
                   ". Otherwise 1 star.";
        }

        /// <summary>Back to "not started" (no timer, not completed), e.g. while the level briefing is shown.</summary>
        public void ResetObjective()
        {
            m_Running = false;
            m_Completed = false;
            m_HoldTimer = 0f;
            m_Stars = 0;
        }

        public void StopObjective()
        {
            m_Running = false;
        }

        /// <summary>Complete the objective right now, as if everything was aligned (testing and demos).</summary>
        public void ForceComplete()
        {
            if (m_Completed)
            {
                return;
            }

            if (!m_Running)
            {
                StartObjective();
            }

            Complete();
        }

        /// <summary>Number of required targets that count: assigned and active in the scene.</summary>
        public int CountedTargetCount()
        {
            int count = 0;
            foreach (LaserTarget target in requiredTargets)
            {
                if (Counts(target))
                {
                    count++;
                }
            }

            return count;
        }

        public int AlignedTargetCount()
        {
            int count = 0;
            foreach (LaserTarget target in requiredTargets)
            {
                if (Counts(target) && target.IsAligned)
                {
                    count++;
                }
            }

            return count;
        }

        public bool AllTargetsAligned()
        {
            int counted = CountedTargetCount();
            return counted > 0 && AlignedTargetCount() == counted;
        }

        /// <summary>
        /// A target that is not assigned or switched off can never be aligned; it would make the level impossible,
        /// so it is ignored.
        /// </summary>
        private static bool Counts(LaserTarget target) => target != null && target.isActiveAndEnabled;

        /// <summary>
        /// Plain-text snapshot of the level for the AI tutor. Send it along with the player's
        /// question, e.g. as part of the system prompt, so the tutor knows what is going on.
        /// </summary>
        public string BuildStatusReport()
        {
            var report = new StringBuilder(512);
            report.AppendLine("LASER ALIGNMENT STATUS");
            report.Append("Objective: ").Append(m_Completed ? "completed" : m_Running ? "in progress" : "not started")
                  .Append(", time ").Append(LaserText.Number(ElapsedSeconds, 0)).Append(" s, aligned targets ")
                  .Append(AlignedTargetCount()).Append('/').Append(CountedTargetCount()).AppendLine(".");
            report.Append("Star rules: ").AppendLine(DescribeStarRules());
            if (m_Completed)
            {
                report.Append("Stars earned: ").Append(m_Stars).AppendLine(" of 3.");
            }

            foreach (LaserTarget target in requiredTargets)
            {
                if (target == null)
                {
                    continue;
                }

                report.Append("- Detector '").Append(target.name).Append("': ").Append(target.State.ToString().ToLowerInvariant())
                      .Append(", power ").Append(LaserText.Percent(target.ReceivedPowerFraction))
                      .Append(" (needs ").Append(LaserText.Percent(target.RequiredPowerFraction)).Append(")")
                      .Append(", beams ").Append(target.BeamCount).Append('/').Append(target.RequiredBeamCount)
                      .Append(target.BeamCount > 1 ? ", average spot " : ", spot ").Append(target.DescribeSpotPosition())
                      .Append(" (tolerance ").Append(LaserText.Number(target.CenteringTolerance * 100f, 1)).AppendLine(" cm).");

                if (target.TrackedBeamCount > 1)
                {
                    for (int b = 0; b < target.TrackedBeamCount; b++)
                    {
                        target.GetBeam(b, out Vector2 offset, out float power);
                        report.Append("    beam ").Append(b + 1).Append(": ").Append(LaserText.DescribeOffset(offset))
                              .Append(", ").Append(LaserText.Percent(power)).AppendLine(" of laser power.");
                    }
                }
            }

            var mounts = MirrorMount.ActiveMounts;
            for (int i = 0; i < mounts.Count; i++)
            {
                report.Append("- Optic ").AppendLine(mounts[i].BuildReadout());
            }

            var emitters = LaserEmitter.ActiveEmitters;
            for (int i = 0; i < emitters.Count; i++)
            {
                LaserEmitter emitter = emitters[i];
                report.Append("- Laser '").Append(emitter.name).Append("': ")
                      .Append(emitter.IsShutterOpen ? "shutter open" : "shutter closed")
                      .Append(", ").Append(LaserText.Number(emitter.WavelengthNm, 0)).Append(" nm")
                      .Append(", stray light ").Append(LaserText.Percent(emitter.StrayLightFraction))
                      .Append(", clipped ").Append(LaserText.Percent(emitter.ClippedFraction)).AppendLine(".");
            }

            if (safetyMonitor != null)
            {
                report.Append("Mistakes so far: stray light ").Append(safetyMonitor.StrayLightIncidents)
                      .Append(", clipping ").Append(safetyMonitor.ClippingIncidents)
                      .Append(", actuator limit ").Append(safetyMonitor.ActuatorLimitIncidents).AppendLine(".");
                if (safetyMonitor.IsStrayLightActive && !string.IsNullOrEmpty(safetyMonitor.StraySurfaceName))
                {
                    report.Append("The beam is currently hitting '").Append(safetyMonitor.StraySurfaceName).AppendLine("'.");
                }
            }

            return report.ToString();
        }

        private void OnEnable()
        {
            MirrorMount.AnyPlayerInput += HandlePlayerInput;
        }

        private void OnDisable()
        {
            MirrorMount.AnyPlayerInput -= HandlePlayerInput;
        }

        private void HandlePlayerInput(MirrorMount mount)
        {
            if (startOnFirstMirrorMove && IsWaitingToStart && CountedTargetCount() > 0)
            {
                StartObjective();
            }
        }

        private void Start()
        {
            if (startAutomatically)
            {
                StartObjective();
            }
        }

        private void LateUpdate()
        {
            if (!m_Running || m_Completed)
            {
                return;
            }

            if (AllTargetsAligned())
            {
                m_HoldTimer += Time.deltaTime;
                onHoldProgress.Invoke(HoldProgress);
                if (m_HoldTimer >= holdSeconds)
                {
                    Complete();
                }
            }
            else if (m_HoldTimer > 0f)
            {
                m_HoldTimer = 0f;
                onHoldProgress.Invoke(0f);
                onAlignmentLostDuringHold.Invoke();
            }
        }

        private void Complete()
        {
            if (m_Completed)
            {
                return;
            }

            m_Completed = true;
            m_Running = false;
            m_CompletionTime = Time.time;
            m_HoldTimer = holdSeconds;
            m_Stars = CalculateStars(ElapsedSeconds, Mistakes);
            onCompleted.Invoke();
            onStarsAwarded.Invoke(m_Stars);
        }

        private int CalculateStars(float seconds, int mistakes)
        {
            float par = ParTimeSeconds;
            if (seconds <= par && mistakes <= threeStarMistakeLimit)
            {
                return 3;
            }

            if (seconds <= par * 2f && mistakes <= twoStarMistakeLimit)
            {
                return 2;
            }

            return 1;
        }
    }
}

using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A detector the beam must hit. It works like the quadrant photodiodes (QPDs) real
    /// interferometers use for alignment: it measures how much power arrives and how far the spot
    /// is from the centre. The detector face points along the blue (forward) axis; offsets are
    /// measured along its red (right) and green (up) axes.
    /// As an <see cref="TargetType.Iris"/> it lets the centred part of the beam through, like the
    /// irises technicians use to "walk" a beam onto the right line.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.Receiver)]
    public class LaserTarget : MonoBehaviour, ILaserInteractable
    {
        public enum TargetType
        {
            /// <summary>Absorbs all light (photodiode).</summary>
            Photodiode,
            /// <summary>Passes the light that goes through the hole, blocks the rest.</summary>
            Iris
        }

        public enum AlignmentState
        {
            Dark,
            Misaligned,
            Aligned
        }

        [Header("Detector")]
        [SerializeField] private TargetType targetType = TargetType.Photodiode;

        [Tooltip("Radius of the sensitive area (photodiode) or of the hole (iris), in metres.")]
        [SerializeField, Min(0.001f)] private float detectorRadius = 0.04f;

        [Tooltip("Optional. Transform whose position is the detector centre and whose axes define left/right/up/down. Empty = this object.")]
        [SerializeField] private Transform surface = null;

        [Tooltip("Only measure light that arrives at the front face (the side the blue axis points to). Light from behind " +
                 "still passes through an iris but is ignored, e.g. the beam coming back from the interferometer.")]
        [SerializeField] private bool frontFaceOnly = false;

        [Header("Alignment goal")]
        [Tooltip("Every beam must hit within this distance (metres) of the centre to count as aligned.")]
        [SerializeField, Min(0.0001f)] private float centeringTolerance = 0.01f;

        [Tooltip("Minimum received power, as a fraction of the laser output, to count as aligned.")]
        [SerializeField, Range(0f, 1f)] private float requiredPowerFraction = 0.1f;

        [Tooltip("How many separate beams must arrive. For the output port of a Michelson interferometer: 2 (one from each arm).")]
        [SerializeField, Range(1, 8)] private int requiredBeamCount = 1;

        [Tooltip("Once aligned, the spot may drift this much further (as a fraction of the tolerance) before it counts as misaligned. Prevents flickering.")]
        [SerializeField, Range(0f, 1f)] private float hysteresis = 0.15f;

        [Header("Status light (optional)")]
        [SerializeField] private Renderer statusIndicator = null;

        [Tooltip("More lights that show the same state, e.g. one on the back of the detector that the operator can see.")]
        [SerializeField] private Renderer[] extraStatusIndicators = new Renderer[0];
        [SerializeField] private Color darkColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color misalignedColor = new Color(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color alignedColor = new Color(0.2f, 1f, 0.3f, 1f);

        [Header("Events")]
        [SerializeField] private UnityEvent onAligned = new UnityEvent();
        [SerializeField] private UnityEvent onAlignmentLost = new UnityEvent();
        [SerializeField] private UnityEvent onIlluminated = new UnityEvent();
        [SerializeField] private UnityEvent onDark = new UnityEvent();
        [Tooltip("Received power as a fraction of the laser output (0..1), sent every frame it changes.")]
        [SerializeField] private UnityEvent<float> onPowerChanged = new UnityEvent<float>();
        [Tooltip("Spot position: x = left/right, y = down/up, -1..1 across the detector, sent every frame it changes.")]
        [SerializeField] private UnityEvent<Vector2> onSpotOffsetChanged = new UnityEvent<Vector2>();

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Maximum number of individual beams whose position is remembered per frame.</summary>
        public const int MaxTrackedBeams = 8;

        private MaterialPropertyBlock m_PropertyBlock;

        // Individual beams: written during the frame, copied when the frame is evaluated.
        private readonly Vector2[] m_PendingBeamOffsets = new Vector2[MaxTrackedBeams];
        private readonly float[] m_PendingBeamPowers = new float[MaxTrackedBeams];
        private readonly Vector2[] m_BeamOffsets = new Vector2[MaxTrackedBeams];
        private readonly float[] m_BeamPowers = new float[MaxTrackedBeams];
        private int m_TrackedBeamCount;

        // Accumulated during the current frame by the lasers.
        private int m_AccumulatedFrame = -1;
        private float m_SumPowerFraction;
        private Vector2 m_SumWeightedOffset;
        private float m_SumWeightedRadius;
        private float m_MaxBeamOffset;
        private int m_SumBeamCount;
        private float m_SumPowerWatts;

        // Strongest beam that hit the front face this frame, even if the iris plate blocked it (for beam cameras).
        private int m_FaceFrame = -1;
        private float m_PendingFacePower;
        private Vector2 m_PendingFaceOffset;
        private float m_PendingFaceRadius;
        private float m_PendingFaceFraction;
        private bool m_HasFaceHit;
        private Vector2 m_FaceOffset;
        private float m_FaceRadius;
        private float m_FaceFraction;

        // Results of the last finished frame.
        private float m_PowerFraction;
        private float m_PowerWatts;
        private Vector2 m_SpotOffset;
        private float m_SpotRadius;
        private float m_WorstBeamOffset;
        private int m_BeamCount;
        private AlignmentState m_State = AlignmentState.Dark;
        private float m_LastSentPower = -1f;
        private Vector2 m_LastSentOffset = new Vector2(float.NaN, float.NaN);

        public Transform Surface => surface != null ? surface : transform;
        public TargetType Type => targetType;
        public float DetectorRadius => detectorRadius;
        public float CenteringTolerance => centeringTolerance;
        public float RequiredPowerFraction => requiredPowerFraction;
        public int RequiredBeamCount => requiredBeamCount;

        public AlignmentState State => m_State;
        public bool IsAligned => m_State == AlignmentState.Aligned;
        public bool IsIlluminated => m_State != AlignmentState.Dark;

        /// <summary>Received power as a fraction of the laser output (summed over all beams).</summary>
        public float ReceivedPowerFraction => m_PowerFraction;

        /// <summary>Received power in watts.</summary>
        public float ReceivedPowerWatts => m_PowerWatts;

        /// <summary>Number of separate beams that hit the detector.</summary>
        public int BeamCount => m_BeamCount;

        /// <summary>Power-weighted spot centre in metres (x = right, y = up).</summary>
        public Vector2 SpotOffset => m_SpotOffset;

        /// <summary>Spot offset scaled to the detector size (-1..1).</summary>
        public Vector2 NormalizedSpotOffset => m_SpotOffset / detectorRadius;

        /// <summary>Distance of the beam that is furthest from the centre, in metres.</summary>
        public float WorstBeamOffset => m_WorstBeamOffset;

        /// <summary>
        /// Real quadrant-photodiode error signals (-1..1): x = (right - left) / sum, y = (top - bottom) / sum.
        /// Zero means the spot is centred. Control systems drive mirrors to make these zero.
        /// </summary>
        public Vector2 QuadrantSignals => m_BeamCount == 0
            ? Vector2.zero
            : new Vector2(LaserOptics.QuadrantSignal(m_SpotOffset.x, m_SpotRadius), LaserOptics.QuadrantSignal(m_SpotOffset.y, m_SpotRadius));

        /// <summary>True when a beam hits the front face, also when the iris plate blocks it (what a beam camera sees).</summary>
        public bool HasBeamOnFace => m_HasFaceHit;

        /// <summary>Where the strongest beam hits the face (metres, x = right, y = up), blocked or not.</summary>
        public Vector2 FaceHitOffset => m_FaceOffset;

        /// <summary>Radius of that beam (metres).</summary>
        public float FaceHitBeamRadius => m_FaceRadius;

        /// <summary>Power of that beam as a fraction of the laser output.</summary>
        public float FaceHitPowerFraction => m_FaceFraction;

        public UnityEvent OnAligned => onAligned;
        public UnityEvent OnAlignmentLost => onAlignmentLost;
        public UnityEvent OnIlluminated => onIlluminated;
        public UnityEvent OnDark => onDark;
        public UnityEvent<float> OnPowerChanged => onPowerChanged;
        public UnityEvent<Vector2> OnSpotOffsetChanged => onSpotOffsetChanged;

        public void OnLaserHit(in LaserHit hit, LaserBeamQueue beams)
        {
            Transform detector = Surface;
            Vector2 offset = LaserMirror.OffsetOnSurface(hit.Point, detector);
            float captured = LaserOptics.GaussianPowerInsideCircle(hit.BeamRadius, detectorRadius, offset.magnitude);

            if (targetType == TargetType.Iris)
            {
                // The centred part of the beam passes through the hole; the rest is blocked by the iris plate.
                beams.Emit(hit.Point, hit.Direction, hit.Power * captured);
            }

            bool fromFront = Vector3.Dot(hit.Direction, detector.forward) < 0f;
            if (!hit.IsPreview && (fromFront || !frontFaceOnly))
            {
                RecordFaceHit(hit, offset);
            }

            if (!hit.IsPreview && captured > 0f && (fromFront || !frontFaceOnly))
            {
                Accumulate(hit, captured, offset);
            }
        }

        private void RecordFaceHit(in LaserHit hit, Vector2 offset)
        {
            if (m_FaceFrame != Time.frameCount)
            {
                m_FaceFrame = Time.frameCount;
                m_PendingFacePower = 0f;
            }

            if (hit.Power > m_PendingFacePower)
            {
                m_PendingFacePower = hit.Power;
                m_PendingFaceOffset = offset;
                m_PendingFaceRadius = hit.BeamRadius;
                m_PendingFaceFraction = hit.PowerFraction;
            }
        }

        private void Accumulate(in LaserHit hit, float capturedFraction, Vector2 offset)
        {
            if (m_AccumulatedFrame != Time.frameCount)
            {
                m_AccumulatedFrame = Time.frameCount;
                m_SumPowerFraction = 0f;
                m_SumPowerWatts = 0f;
                m_SumWeightedOffset = Vector2.zero;
                m_SumWeightedRadius = 0f;
                m_MaxBeamOffset = 0f;
                m_SumBeamCount = 0;
            }

            float powerFraction = hit.PowerFraction * capturedFraction;
            if (m_SumBeamCount < MaxTrackedBeams)
            {
                m_PendingBeamOffsets[m_SumBeamCount] = offset;
                m_PendingBeamPowers[m_SumBeamCount] = powerFraction;
            }

            m_SumPowerFraction += powerFraction;
            m_SumPowerWatts += hit.Power * capturedFraction;
            m_SumWeightedOffset += offset * powerFraction;
            m_SumWeightedRadius += hit.BeamRadius * powerFraction;
            m_MaxBeamOffset = Mathf.Max(m_MaxBeamOffset, offset.magnitude);
            m_SumBeamCount++;
        }

        private void LateUpdate()
        {
            bool hasData = m_AccumulatedFrame == Time.frameCount && m_SumPowerFraction > 0f;
            m_PowerFraction = hasData ? m_SumPowerFraction : 0f;
            m_PowerWatts = hasData ? m_SumPowerWatts : 0f;
            m_SpotOffset = hasData ? m_SumWeightedOffset / m_SumPowerFraction : Vector2.zero;
            m_SpotRadius = hasData ? m_SumWeightedRadius / m_SumPowerFraction : 0f;
            m_WorstBeamOffset = hasData ? m_MaxBeamOffset : 0f;
            m_BeamCount = hasData ? m_SumBeamCount : 0;
            m_TrackedBeamCount = Mathf.Min(m_BeamCount, MaxTrackedBeams);
            for (int i = 0; i < m_TrackedBeamCount; i++)
            {
                m_BeamOffsets[i] = m_PendingBeamOffsets[i];
                m_BeamPowers[i] = m_PendingBeamPowers[i];
            }

            m_HasFaceHit = m_FaceFrame == Time.frameCount && m_PendingFacePower > 0f;
            m_FaceOffset = m_HasFaceHit ? m_PendingFaceOffset : Vector2.zero;
            m_FaceRadius = m_HasFaceHit ? m_PendingFaceRadius : 0f;
            m_FaceFraction = m_HasFaceHit ? m_PendingFaceFraction : 0f;

            UpdateState(EvaluateState());
            SendContinuousEvents();
        }

        private AlignmentState EvaluateState()
        {
            if (m_BeamCount == 0)
            {
                return AlignmentState.Dark;
            }

            float tolerance = m_State == AlignmentState.Aligned ? centeringTolerance * (1f + hysteresis) : centeringTolerance;
            bool centred = m_WorstBeamOffset <= tolerance;
            bool enoughPower = m_PowerFraction >= requiredPowerFraction;
            bool enoughBeams = m_BeamCount >= requiredBeamCount;
            return centred && enoughPower && enoughBeams ? AlignmentState.Aligned : AlignmentState.Misaligned;
        }

        private void UpdateState(AlignmentState newState)
        {
            if (newState == m_State)
            {
                return;
            }

            AlignmentState previous = m_State;
            m_State = newState;
            UpdateStatusIndicator();

            if (previous == AlignmentState.Dark)
            {
                onIlluminated.Invoke();
            }

            if (previous == AlignmentState.Aligned)
            {
                onAlignmentLost.Invoke();
            }

            if (newState == AlignmentState.Aligned)
            {
                onAligned.Invoke();
            }
            else if (newState == AlignmentState.Dark)
            {
                onDark.Invoke();
            }
        }

        private void SendContinuousEvents()
        {
            if (!Mathf.Approximately(m_PowerFraction, m_LastSentPower))
            {
                m_LastSentPower = m_PowerFraction;
                onPowerChanged.Invoke(m_PowerFraction);
            }

            Vector2 normalized = NormalizedSpotOffset;
            if ((normalized - m_LastSentOffset).sqrMagnitude > 1e-6f || float.IsNaN(m_LastSentOffset.x))
            {
                m_LastSentOffset = normalized;
                onSpotOffsetChanged.Invoke(normalized);
            }
        }

        private void UpdateStatusIndicator()
        {
            Color color = m_State == AlignmentState.Aligned ? alignedColor
                : m_State == AlignmentState.Misaligned ? misalignedColor
                : darkColor;

            ApplyIndicatorColor(statusIndicator, color);
            foreach (Renderer extra in extraStatusIndicators)
            {
                ApplyIndicatorColor(extra, color);
            }
        }

        private void ApplyIndicatorColor(Renderer indicator, Color color)
        {
            if (indicator == null)
            {
                return;
            }

            m_PropertyBlock ??= new MaterialPropertyBlock();
            indicator.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(s_BaseColorId, color);
            m_PropertyBlock.SetColor(s_ColorId, color);
            m_PropertyBlock.SetColor(s_EmissionColorId, color * 2f);
            indicator.SetPropertyBlock(m_PropertyBlock);
        }

        private void Start()
        {
            UpdateStatusIndicator();
        }

        private void OnDisable()
        {
            // Reset quietly: OnDisable also runs while a scene unloads, when listeners may already be gone.
            m_State = AlignmentState.Dark;
            m_BeamCount = 0;
            m_TrackedBeamCount = 0;
            m_PowerFraction = 0f;
            m_HasFaceHit = false;
            UpdateStatusIndicator();
        }

        /// <summary>Number of individual beams available through <see cref="GetBeam"/> (at most <see cref="MaxTrackedBeams"/>).</summary>
        public int TrackedBeamCount => m_TrackedBeamCount;

        /// <summary>Position (metres, x = right, y = up) and power fraction of one individual beam from the last frame.</summary>
        public void GetBeam(int index, out Vector2 offset, out float powerFraction)
        {
            if (index < 0 || index >= m_TrackedBeamCount)
            {
                offset = Vector2.zero;
                powerFraction = 0f;
                return;
            }

            offset = m_BeamOffsets[index];
            powerFraction = m_BeamPowers[index];
        }

        /// <summary>Short human-readable description of where the spot is, e.g. "2.1 cm left, 0.4 cm up".</summary>
        public string DescribeSpotPosition()
        {
            if (m_BeamCount == 0)
            {
                return "no light";
            }

            return LaserText.DescribeOffset(m_SpotOffset);
        }

        private void OnDrawGizmosSelected()
        {
            Transform detector = Surface;
            Gizmos.matrix = Matrix4x4.TRS(detector.position, detector.rotation, new Vector3(1f, 1f, 0.001f));
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(Vector3.zero, detectorRadius);
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(Vector3.zero, centeringTolerance);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}

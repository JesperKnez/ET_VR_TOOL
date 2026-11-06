using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A partially reflecting plate: part of the power is reflected (law of reflection), part goes
    /// straight through. This is the heart of every Michelson interferometer such as LIGO, Virgo and
    /// the Einstein Telescope: one beam is split 50/50 into the two arms and the returning beams
    /// are recombined on the same plate.
    /// The coated face points along the blue (forward) axis. Like a real beam splitter it has ONE
    /// reflective coating: light arriving from the back first crosses the glass, reflects off the coating
    /// from inside and crosses the glass again. That is why, with perfectly aligned arms, both
    /// returning beams leave on exactly the same line.
    /// Note: the powers of recombined beams are simply added; interference is not simulated.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.Receiver)]
    public class LaserBeamSplitter : MonoBehaviour, ILaserInteractable
    {
        [Header("Split ratio")]
        [Tooltip("Fraction of the power that is reflected. Interferometer beam splitters are 50/50 (0.5).")]
        [SerializeField, Range(0f, 1f)] private float reflectance = 0.5f;

        [Tooltip("Fraction of the power that is transmitted. Reflectance + transmittance <= 1; the rest is absorbed.")]
        [SerializeField, Range(0f, 1f)] private float transmittance = 0.5f;

        [Header("Size")]
        [Tooltip("Radius of the optic in metres. Advanced LIGO beam splitter: 0.185 m (37 cm diameter). " +
                 "Make the collider a little bigger, so beams near the edge are partly clipped instead of missed.")]
        [SerializeField, Min(0.001f)] private float radius = 0.08f;

        [Tooltip("Simulate power lost when the beam hits near the edge.")]
        [SerializeField] private bool simulateEdgeClipping = true;

        [Header("Glass substrate")]
        [Tooltip("Shift the transmitted beam sideways, as refraction in a thick glass plate does (Snell's law).")]
        [SerializeField] private bool simulateSubstrateOffset = true;

        [Tooltip("Substrate thickness in metres. Advanced LIGO beam splitter: 0.06 m.")]
        [SerializeField, Min(0f)] private float substrateThickness = 0.02f;

        [Tooltip("Refractive index of the substrate. Fused silica at 1064 nm: about 1.45.")]
        [SerializeField, Range(1f, 4f)] private float refractiveIndex = 1.45f;

        [Tooltip("Optional. Transform whose blue axis is the coated-face normal. Empty = this object.")]
        [SerializeField] private Transform surface = null;

        private int m_AccumulatedFrame = -1;
        private float m_ArrivedPower;
        private int m_BeamCount;
        private float m_LastArrivedPower;
        private int m_LastBeamCount;

        public Transform Surface => surface != null ? surface : transform;
        public float Reflectance => reflectance;
        public float Transmittance => transmittance;
        public float Radius => radius;

        /// <summary>Total power that arrived on the splitter last frame.</summary>
        public float ArrivedPower => m_LastArrivedPower;

        /// <summary>How many beams hit the splitter last frame (2 or more = beams are being recombined).</summary>
        public int BeamCount => m_LastBeamCount;

        /// <summary>
        /// Where the reflected and the transmitted beam start, and which way they go, for a beam that hits this splitter
        /// at <paramref name="point"/> going <paramref name="direction"/>. Same maths as <see cref="OnLaserHit"/>, for
        /// tools that follow one beam through the optics (the mirror camera): the thick substrate shifts both beams by
        /// millimetres, so "starts where the beam hit" is not enough to tell them from the other arm's beams.
        /// </summary>
        public void PredictOutgoing(Vector3 point, Vector3 direction, out Vector3 reflectedOrigin, out Vector3 reflectedDirection, out Vector3 transmittedOrigin)
        {
            Vector3 normal = Surface.forward;
            Vector3 normalTowardsBeam = Vector3.Dot(direction, normal) < 0f ? normal : -normal;
            bool fromBack = Vector3.Dot(direction, normal) > 0f;
            reflectedOrigin = fromBack && simulateSubstrateOffset
                ? LaserOptics.InternalReflectionExitPoint(point, direction, -normal, substrateThickness, refractiveIndex)
                : point;
            reflectedDirection = LaserOptics.Reflect(direction, normal);
            transmittedOrigin = simulateSubstrateOffset
                ? LaserOptics.PlateExitPoint(point, direction, normalTowardsBeam, substrateThickness, refractiveIndex)
                : point;
        }

        public void OnLaserHit(in LaserHit hit, LaserBeamQueue beams)
        {
            Transform plate = Surface;
            Vector3 normal = plate.forward;
            Vector3 normalTowardsBeam = Vector3.Dot(hit.Direction, normal) < 0f ? normal : -normal;

            float offset = LaserMirror.OffsetOnSurface(hit.Point, plate).magnitude;
            float insideFraction = simulateEdgeClipping
                ? LaserOptics.GaussianPowerInsideCircle(hit.BeamRadius, radius, offset)
                : (offset <= radius ? 1f : 0f);

            float usablePower = hit.Power * insideFraction;
            float clippedPower = hit.Power - usablePower;
            if (clippedPower > 0f)
            {
                beams.ReportScatter(LaserScatterKind.Clipping, hit.Point, clippedPower);
            }

            if (reflectance > 0f)
            {
                bool fromBack = Vector3.Dot(hit.Direction, normal) > 0f;
                Vector3 reflectedOrigin = fromBack && simulateSubstrateOffset
                    ? LaserOptics.InternalReflectionExitPoint(hit.Point, hit.Direction, -normal, substrateThickness, refractiveIndex)
                    : hit.Point;
                beams.Emit(reflectedOrigin, LaserOptics.Reflect(hit.Direction, normal), usablePower * reflectance);
            }

            if (transmittance > 0f)
            {
                Vector3 exitPoint = simulateSubstrateOffset
                    ? LaserOptics.PlateExitPoint(hit.Point, hit.Direction, normalTowardsBeam, substrateThickness, refractiveIndex)
                    : hit.Point;
                beams.Emit(exitPoint, hit.Direction, usablePower * transmittance);
            }

            if (!hit.IsPreview)
            {
                Accumulate(hit.Power);
            }
        }

        private void Accumulate(float power)
        {
            if (m_AccumulatedFrame != Time.frameCount)
            {
                m_AccumulatedFrame = Time.frameCount;
                m_ArrivedPower = 0f;
                m_BeamCount = 0;
            }

            m_ArrivedPower += power;
            m_BeamCount++;
        }

        private void LateUpdate()
        {
            bool hasData = m_AccumulatedFrame == Time.frameCount;
            m_LastArrivedPower = hasData ? m_ArrivedPower : 0f;
            m_LastBeamCount = hasData ? m_BeamCount : 0;
        }

        private void OnValidate()
        {
            transmittance = Mathf.Min(transmittance, 1f - reflectance);
        }

        private void OnDrawGizmosSelected()
        {
            Transform plate = Surface;
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(plate.position, plate.rotation, new Vector3(1f, 1f, 0.001f));
            Gizmos.DrawWireSphere(Vector3.zero, radius);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawRay(plate.position, plate.forward * radius * 1.5f);
        }
    }
}

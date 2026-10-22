using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A flat mirror. The coated (reflective) face points along the blue (forward) axis of
    /// <see cref="Surface"/>. Reflection uses the exact law of reflection with the mirror's own
    /// normal, so tilting the mirror by an angle turns the beam by twice that angle.
    /// Beams that hit near the edge lose the part of the beam that falls outside the coating.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.Receiver)]
    public class LaserMirror : MonoBehaviour, ILaserInteractable
    {
        public enum BackSide
        {
            /// <summary>Normal steering mirror: the back is covered by its mount, light hitting it is stray light.</summary>
            BlockedByMount,
            /// <summary>Test-mass style: light can enter from the back (like an input test mass of an arm cavity).</summary>
            Optical
        }

        [Header("Coating")]
        [Tooltip("Fraction of power reflected. Real values: Advanced LIGO end test mass 0.999995 (5 ppm transmission), " +
                 "input test mass 0.986 (1.4% transmission). A simple steering mirror ~0.99.")]
        [SerializeField, Range(0f, 1f)] private float reflectivity = 0.99f;

        [Tooltip("Fraction of power that leaks through the mirror and continues straight on.")]
        [SerializeField, Range(0f, 1f)] private float transmission = 0f;

        [Tooltip("What happens when the beam hits the back of the mirror.")]
        [SerializeField] private BackSide backSide = BackSide.BlockedByMount;

        [Header("Size")]
        [Tooltip("Radius of the reflective area in metres. Real test masses: Advanced LIGO 0.17 m, ET-HF about 0.31 m. " +
                 "Make the collider a little bigger (about 2-3 beam radii), so beams near the edge are partly clipped instead of missed.")]
        [SerializeField, Min(0.001f)] private float mirrorRadius = 0.08f;

        [Tooltip("Simulate power lost when the beam is not centred and part of it falls off the edge.")]
        [SerializeField] private bool simulateEdgeClipping = true;

        [Tooltip("Clipping is reported once more than this fraction of the arriving power is lost.")]
        [SerializeField, Range(0.001f, 0.5f)] private float clippingWarningFraction = 0.02f;

        [Tooltip("Optional. Transform whose blue axis is the mirror normal and whose position is the mirror centre. Empty = this object.")]
        [SerializeField] private Transform surface = null;

        [Header("Events")]
        [SerializeField] private UnityEvent onBeamClippingStarted = new UnityEvent();
        [SerializeField] private UnityEvent onBeamClippingStopped = new UnityEvent();

        private int m_AccumulatedFrame = -1;
        private float m_ArrivedPower;
        private float m_ClippedPower;
        private float m_StrongestHitPower;
        private Vector2 m_StrongestHitOffset;

        private bool m_IsClipping;
        private bool m_IsIlluminated;
        private float m_ClippedFraction;
        private Vector2 m_BeamSpotOffset;

        /// <summary>Position and normal of the reflective surface.</summary>
        public Transform Surface => surface != null ? surface : transform;
        public float MirrorRadius => mirrorRadius;
        public float Reflectivity => reflectivity;

        /// <summary>True when a beam hit this mirror in the last frame.</summary>
        public bool IsIlluminated => m_IsIlluminated;

        /// <summary>True while a noticeable part of the beam falls off the edge.</summary>
        public bool IsBeamClipped => m_IsClipping;

        /// <summary>Fraction of the arriving power that was lost at the edge in the last frame.</summary>
        public float ClippedPowerFraction => m_ClippedFraction;

        /// <summary>Where the strongest beam hits, relative to the mirror centre (metres, x = right, y = up).</summary>
        public Vector2 BeamSpotOffset => m_BeamSpotOffset;

        public UnityEvent OnBeamClippingStarted => onBeamClippingStarted;
        public UnityEvent OnBeamClippingStopped => onBeamClippingStopped;

        public void OnLaserHit(in LaserHit hit, LaserBeamQueue beams)
        {
            Transform mirrorSurface = Surface;
            Vector3 normal = mirrorSurface.forward;
            bool hitFront = Vector3.Dot(hit.Direction, normal) < 0f;

            Vector2 offset = OffsetOnSurface(hit.Point, mirrorSurface);
            float insideFraction = simulateEdgeClipping
                ? LaserOptics.GaussianPowerInsideCircle(hit.BeamRadius, mirrorRadius, offset.magnitude)
                : (offset.magnitude <= mirrorRadius ? 1f : 0f);

            float usablePower = hit.Power * insideFraction;
            float clippedPower = hit.Power - usablePower;

            if (!hitFront && backSide == BackSide.BlockedByMount)
            {
                // The beam hits the mount behind the mirror: that is stray light.
                beams.ReportScatter(LaserScatterKind.StrayLight, hit.Point, hit.Power);
                Accumulate(hit, hit.Power, 0f, offset);
                return;
            }

            if (clippedPower > 0f)
            {
                beams.ReportScatter(LaserScatterKind.Clipping, hit.Point, clippedPower);
            }

            if (reflectivity > 0f)
            {
                beams.Emit(hit.Point, LaserOptics.Reflect(hit.Direction, normal), usablePower * reflectivity);
            }

            float transmitted = Mathf.Min(transmission, 1f - reflectivity);
            if (transmitted > 0f)
            {
                beams.Emit(hit.Point, hit.Direction, usablePower * transmitted);
            }

            Accumulate(hit, hit.Power, clippedPower, offset);
        }

        private void Accumulate(in LaserHit hit, float arrivedPower, float clippedPower, Vector2 offset)
        {
            if (hit.IsPreview)
            {
                return;
            }

            if (m_AccumulatedFrame != Time.frameCount)
            {
                m_AccumulatedFrame = Time.frameCount;
                m_ArrivedPower = 0f;
                m_ClippedPower = 0f;
                m_StrongestHitPower = 0f;
            }

            m_ArrivedPower += arrivedPower;
            m_ClippedPower += clippedPower;
            if (arrivedPower > m_StrongestHitPower)
            {
                m_StrongestHitPower = arrivedPower;
                m_StrongestHitOffset = offset;
            }
        }

        private void LateUpdate()
        {
            bool hasData = m_AccumulatedFrame == Time.frameCount && m_ArrivedPower > 0f;
            m_IsIlluminated = hasData;
            m_ClippedFraction = hasData ? m_ClippedPower / m_ArrivedPower : 0f;
            m_BeamSpotOffset = hasData ? m_StrongestHitOffset : Vector2.zero;

            bool clipping = m_ClippedFraction >= clippingWarningFraction;
            if (clipping == m_IsClipping)
            {
                return;
            }

            m_IsClipping = clipping;
            if (clipping)
            {
                onBeamClippingStarted.Invoke();
            }
            else
            {
                onBeamClippingStopped.Invoke();
            }
        }

        private void OnDisable()
        {
            m_IsIlluminated = false;
            m_IsClipping = false;
            m_ClippedFraction = 0f;
        }

        /// <summary>Hit point in metres relative to the surface centre, measured along the surface's right/up axes.</summary>
        internal static Vector2 OffsetOnSurface(Vector3 worldPoint, Transform surfaceTransform)
        {
            Vector3 delta = worldPoint - surfaceTransform.position;
            return new Vector2(Vector3.Dot(delta, surfaceTransform.right), Vector3.Dot(delta, surfaceTransform.up));
        }

        private void OnValidate()
        {
            transmission = Mathf.Min(transmission, 1f - reflectivity);
        }

        private void OnDrawGizmosSelected()
        {
            Transform mirrorSurface = Surface;
            Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(mirrorSurface.position, mirrorSurface.rotation, new Vector3(1f, 1f, 0.001f));
            Gizmos.DrawWireSphere(Vector3.zero, mirrorRadius);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawRay(mirrorSurface.position, mirrorSurface.forward * mirrorRadius * 1.5f);
        }
    }
}

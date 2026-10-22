using System.Collections.Generic;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Anything the laser can interact with (mirror, beam splitter, detector, beam dump...).
    /// Put a component that implements this on the GameObject with the collider, or on a parent.
    /// </summary>
    public interface ILaserInteractable
    {
        /// <summary>
        /// Called when a beam hits this object. Add any outgoing beams (reflection, transmission)
        /// to <paramref name="beams"/>. Whatever power you do not send on is absorbed.
        /// </summary>
        void OnLaserHit(in LaserHit hit, LaserBeamQueue beams);
    }

    /// <summary>Why power left the beam without going where it should.</summary>
    public enum LaserScatterKind
    {
        /// <summary>The beam hit something that is not an optic (a wall, a mount, the table...).</summary>
        StrayLight,
        /// <summary>The beam hit the edge of an optic; the part outside the coating is scattered.</summary>
        Clipping,
        /// <summary>Light came back into a laser without a Faraday isolator. Real lasers get unstable.</summary>
        BackReflection
    }

    /// <summary>Everything an optic needs to know about one beam hitting it.</summary>
    public readonly struct LaserHit
    {
        public LaserHit(LaserEmitter source, Collider collider, Vector3 point, Vector3 colliderNormal, Vector3 direction,
            float power, float beamRadius, float pathLength, int depth, bool isPreview)
        {
            Source = source;
            Collider = collider;
            Point = point;
            ColliderNormal = colliderNormal;
            Direction = direction;
            Power = power;
            BeamRadius = beamRadius;
            PathLength = pathLength;
            Depth = depth;
            IsPreview = isPreview;
        }

        /// <summary>The laser this light came from.</summary>
        public LaserEmitter Source { get; }
        public Collider Collider { get; }
        /// <summary>World-space hit point.</summary>
        public Vector3 Point { get; }
        /// <summary>Normal of the collider face that was hit (optics usually use their own exact normal instead).</summary>
        public Vector3 ColliderNormal { get; }
        /// <summary>Normalised travel direction of the incoming beam.</summary>
        public Vector3 Direction { get; }
        /// <summary>Beam power in watts arriving here.</summary>
        public float Power { get; }
        /// <summary>Beam radius (1/e^2) at the hit point, in metres.</summary>
        public float BeamRadius { get; }
        /// <summary>Total distance travelled from the laser to this point.</summary>
        public float PathLength { get; }
        /// <summary>Number of optics the light already passed (0 = straight from the laser).</summary>
        public int Depth { get; }
        /// <summary>True when traced only for the editor preview gizmo. Do not store measurements then.</summary>
        public bool IsPreview { get; }

        /// <summary>Power as a fraction of what the laser emits (0..1).</summary>
        public float PowerFraction => Source != null && Source.OutputPower > 0f ? Power / Source.OutputPower : 0f;
    }

    /// <summary>One straight piece of beam between two points.</summary>
    public readonly struct LaserSegment
    {
        public LaserSegment(Vector3 start, Vector3 end, float power, float startRadius, float endRadius, Collider hitCollider, bool highlighted = false)
        {
            Highlighted = highlighted;
            Start = start;
            End = end;
            Power = power;
            StartRadius = startRadius;
            EndRadius = endRadius;
            HitCollider = hitCollider;
        }

        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Power { get; }
        public float StartRadius { get; }
        public float EndRadius { get; }
        /// <summary>What the segment ended on (null when the beam went off into empty space).</summary>
        public Collider HitCollider { get; }

        /// <summary>True when this light came off the highlighted (selected) mirror; drawn in the highlight colour.</summary>
        public bool Highlighted { get; }
    }

    /// <summary>A spot where the beam lights up a surface.</summary>
    public readonly struct LaserSpot
    {
        public LaserSpot(Vector3 point, Vector3 normal, float radius, float power, bool highlighted = false)
        {
            Highlighted = highlighted;
            Point = point;
            Normal = normal;
            Radius = radius;
            Power = power;
        }

        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float Radius { get; }
        public float Power { get; }
        public bool Highlighted { get; }
    }

    /// <summary>Scattered or lost light, reported for safety feedback.</summary>
    public readonly struct LaserScatterEvent
    {
        public LaserScatterEvent(LaserScatterKind kind, Vector3 point, float power, Collider collider)
        {
            Kind = kind;
            Point = point;
            Power = power;
            Collider = collider;
        }

        public LaserScatterKind Kind { get; }
        public Vector3 Point { get; }
        public float Power { get; }
        public Collider Collider { get; }
    }

    /// <summary>
    /// Work list used while tracing a laser. Optics add the beams that leave them;
    /// the emitter keeps tracing until the list is empty.
    /// </summary>
    public sealed class LaserBeamQueue
    {
        internal struct PendingBeam
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public float Power;
            public float PathLength;
            public int Depth;
            public Collider IgnoredCollider;
            public bool Highlighted;
        }

        private readonly List<PendingBeam> m_Beams = new List<PendingBeam>(32);
        private readonly List<LaserScatterEvent> m_Scatter = new List<LaserScatterEvent>(16);
        private int m_Head;
        private LaserHit m_CurrentHit;
        private bool m_HasCurrentHit;
        private bool m_CurrentHighlighted;

        /// <summary>Light leaving this collider (and everything after it) is marked as highlighted.</summary>
        internal Collider HighlightCollider { get; set; }

        /// <summary>Light lost to scattering during the current trace.</summary>
        public IReadOnlyList<LaserScatterEvent> ScatterEvents => m_Scatter;

        /// <summary>
        /// Send a new beam out of the optic that is currently being hit.
        /// The optic's own collider is skipped for this new beam so it does not hit itself.
        /// </summary>
        public void Emit(Vector3 origin, Vector3 direction, float power)
        {
            if (!m_HasCurrentHit || power <= 0f || direction.sqrMagnitude < 1e-8f)
            {
                return;
            }

            float extraPath = Vector3.Distance(m_CurrentHit.Point, origin);
            m_Beams.Add(new PendingBeam
            {
                Origin = origin,
                Direction = direction.normalized,
                Power = power,
                PathLength = m_CurrentHit.PathLength + extraPath,
                Depth = m_CurrentHit.Depth + 1,
                IgnoredCollider = m_CurrentHit.Collider,
                Highlighted = m_CurrentHighlighted || (HighlightCollider != null && m_CurrentHit.Collider == HighlightCollider)
            });
        }

        /// <summary>Report light that was scattered instead of going where it should.</summary>
        public void ReportScatter(LaserScatterKind kind, Vector3 point, float power)
        {
            if (power <= 0f)
            {
                return;
            }

            Collider collider = m_HasCurrentHit ? m_CurrentHit.Collider : null;
            m_Scatter.Add(new LaserScatterEvent(kind, point, power, collider));
        }

        internal void Reset()
        {
            m_Beams.Clear();
            m_Scatter.Clear();
            m_Head = 0;
            m_HasCurrentHit = false;
        }

        internal void AddRoot(Vector3 origin, Vector3 direction, float power, Collider ignoredCollider)
        {
            m_Beams.Add(new PendingBeam
            {
                Origin = origin,
                Direction = direction.normalized,
                Power = power,
                PathLength = 0f,
                Depth = 0,
                IgnoredCollider = ignoredCollider
            });
        }

        internal bool TryTakeNext(out PendingBeam beam)
        {
            if (m_Head >= m_Beams.Count)
            {
                beam = default;
                return false;
            }

            beam = m_Beams[m_Head++];
            return true;
        }

        internal void BeginHit(in LaserHit hit, bool highlighted = false)
        {
            m_CurrentHit = hit;
            m_HasCurrentHit = true;
            m_CurrentHighlighted = highlighted;
        }

        internal void EndHit()
        {
            m_HasCurrentHit = false;
        }
    }
}

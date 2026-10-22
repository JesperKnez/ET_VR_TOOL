using System.Collections.Generic;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A laser source. Every frame it traces the beam through the scene: straight lines between
    /// optics, reflections, splits and losses, and then draws the result.
    /// The beam leaves along the blue (forward) axis of <see cref="BeamOrigin"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(LaserExecutionOrder.Emitter)]
    public class LaserEmitter : MonoBehaviour
    {
        private const int RaycastBufferSize = 16;
        private const float SelfHitEpsilon = 1e-4f;

        private static readonly List<LaserEmitter> s_ActiveEmitters = new List<LaserEmitter>();
        private static int s_LastPhysicsSyncFrame = -1;

        [Header("Laser")]
        [Tooltip("Shutter state at start. Real lasers stay on; a shutter blocks the beam.")]
        [SerializeField] private bool shutterOpenOnStart = true;

        [Tooltip("Output power in watts. Real values: Advanced LIGO up to 125 W, Einstein Telescope HF design 500 W, " +
                 "ET LF design 3 W. Only ratios matter for the simulation.")]
        [SerializeField, Min(0f)] private float outputPower = 1f;

        [Tooltip("Wavelength in nanometres. 532 = green alignment laser (visible). 1064 = main laser of LIGO/Virgo/ET-HF " +
                 "(infrared, invisible). 1550 = planned ET-LF laser (infrared).")]
        [SerializeField, Range(300f, 2200f)] private float wavelengthNm = 532f;

        [Tooltip("Optional. Where and in which direction (blue axis) the beam leaves. Empty = this object.")]
        [SerializeField] private Transform beamOrigin = null;

        [Tooltip("Optional. The laser's own body. The outgoing beam ignores it; light that comes back into it is absorbed.")]
        [SerializeField] private Collider housingCollider = null;

        [Tooltip("Real lasers have a Faraday isolator so reflected light cannot get back in. " +
                 "Without it, light returning into the laser is reported as a back-reflection warning.")]
        [SerializeField] private bool hasFaradayIsolator = true;

        [Header("Beam shape")]
        [Tooltip("Beam radius (1/e^2) at the laser in metres. Real test-mass beams are 5-12 cm, but scale it to your scene.")]
        [SerializeField, Min(0.0005f)] private float waistRadius = 0.006f;

        [Tooltip("How fast the beam widens, in milliradians (half angle). 0 = parallel beam.")]
        [SerializeField, Min(0f)] private float divergenceMilliradians = 0f;

        [Header("Tracing")]
        [Tooltip("Which layers the beam can hit. Put the control-room glass on a layer that is NOT in this mask.")]
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Total path length (metres) after which the beam is no longer traced.")]
        [SerializeField, Min(0.1f)] private float maxPathLength = 60f;

        [Tooltip("Maximum number of optic interactions per frame. Stops endless bouncing between two mirrors.")]
        [SerializeField, Range(1, 256)] private int maxInteractions = 48;

        [Tooltip("Beams weaker than this fraction of the output power are no longer traced or drawn.")]
        [SerializeField, Range(0f, 0.1f)] private float minimumPowerFraction = 0.002f;

        [Tooltip("Light hitting a non-optic counts as stray light when stronger than this fraction of the output power.")]
        [SerializeField, Range(0f, 1f)] private float strayLightThreshold = 0.01f;

        [Header("Visuals")]
        [Tooltip("Material for beam segments (EinsteinTelescope/LaserBeam). Empty = created at runtime.")]
        [SerializeField] private Material beamMaterial = null;

        [Tooltip("Material for the glowing spots (EinsteinTelescope/LaserBeam with Radial = 1). Empty = created at runtime.")]
        [SerializeField] private Material spotMaterial = null;

        [Tooltip("Drawn beam width relative to the physical beam diameter.")]
        [SerializeField, Min(0.1f)] private float beamWidthScale = 1.5f;

        [Tooltip("Size of the glowing spots relative to the beam diameter.")]
        [SerializeField, Min(0.1f)] private float spotSizeScale = 2.5f;

        [Tooltip("HDR brightness of the beam.")]
        [SerializeField, Min(0f)] private float glowIntensity = 2f;

        [Tooltip("Colour used for invisible (infrared) beams while the IR viewer is on.")]
        [SerializeField] private Color infraredFalseColor = new Color(1f, 0.25f, 0.85f, 1f);

        [Tooltip("Draw glowing spots where the beam hits surfaces.")]
        [SerializeField] private bool drawSpots = true;

        [Tooltip("Draw the beam path as a gizmo in the Scene view, also outside Play mode.")]
        [SerializeField] private bool previewInEditor = true;

        private readonly RaycastHit[] m_RaycastBuffer = new RaycastHit[RaycastBufferSize];
        private readonly LaserBeamQueue m_Queue = new LaserBeamQueue();
        private readonly List<LaserSegment> m_Segments = new List<LaserSegment>(32);
        private readonly List<LaserSpot> m_Spots = new List<LaserSpot>(32);
        private readonly List<LaserScatterEvent> m_Scatter = new List<LaserScatterEvent>(16);
        private LaserBeamVisuals m_Visuals;
        private bool m_ShutterOpen;
        private float m_StrayPower;
        private float m_ClippedPower;
        private float m_BackReflectedPower;

        /// <summary>All enabled emitters in the scene.</summary>
        public static IReadOnlyList<LaserEmitter> ActiveEmitters => s_ActiveEmitters;

        public bool IsShutterOpen => m_ShutterOpen;
        public float OutputPower => outputPower;
        public float WavelengthNm => wavelengthNm;
        public Transform BeamOrigin => beamOrigin != null ? beamOrigin : transform;

        /// <summary>The straight beam pieces traced this frame.</summary>
        public IReadOnlyList<LaserSegment> Segments => m_Segments;

        /// <summary>Every place this frame where light was scattered, clipped or reflected back into the laser.</summary>
        public IReadOnlyList<LaserScatterEvent> ScatterEvents => m_Scatter;

        /// <summary>Stray light this frame as a fraction of the output power.</summary>
        public float StrayLightFraction => Fraction(m_StrayPower);

        /// <summary>Power lost at optic edges this frame as a fraction of the output power.</summary>
        public float ClippedFraction => Fraction(m_ClippedPower);

        /// <summary>Power that came back into the laser this frame (only counted without a Faraday isolator).</summary>
        public float BackReflectionFraction => Fraction(m_BackReflectedPower);

        /// <summary>Same threshold the safety monitor uses to decide whether stray light is serious.</summary>
        public float StrayLightThreshold => strayLightThreshold;

        public void OpenShutter() => SetShutter(true);
        public void CloseShutter() => SetShutter(false);
        public void ToggleShutter() => SetShutter(!m_ShutterOpen);

        public void SetShutter(bool open)
        {
            m_ShutterOpen = open;
        }

        public void SetOutputPower(float watts)
        {
            outputPower = Mathf.Max(0f, watts);
        }

        public void SetWavelength(float nanometres)
        {
            wavelengthNm = Mathf.Clamp(nanometres, 300f, 2200f);
        }

        /// <summary>Beam radius after travelling <paramref name="pathLength"/> metres.</summary>
        public float BeamRadiusAt(float pathLength)
        {
            return LaserOptics.BeamRadiusAt(waistRadius, divergenceMilliradians * 0.001f, pathLength);
        }

        private void Awake()
        {
            m_ShutterOpen = shutterOpenOnStart;
        }

        private void OnEnable()
        {
            s_ActiveEmitters.Add(this);
        }

        private void OnDisable()
        {
            s_ActiveEmitters.Remove(this);
            ClearTrace();
            m_Visuals?.Hide();
        }

        private void OnDestroy()
        {
            m_Visuals?.Dispose();
            m_Visuals = null;
        }

        private void LateUpdate()
        {
            Trace(false);
            Draw();
        }

        private void Draw()
        {
            bool visible = m_ShutterOpen && LaserVisibility.ShouldRender(wavelengthNm);
            if (!visible)
            {
                m_Visuals?.Hide();
                return;
            }

            if (m_Visuals == null)
            {
                m_Visuals = new LaserBeamVisuals(transform, beamMaterial, spotMaterial);
            }

            Color highlight = LaserBeamHighlight.Color * glowIntensity;
            highlight.a = 1f;
            m_Visuals.Render(m_Segments, m_Spots, CurrentBeamColor(), outputPower, beamWidthScale, spotSizeScale, drawSpots, highlight);
        }

        private Color CurrentBeamColor()
        {
            Color baseColor = LaserOptics.IsHumanVisible(wavelengthNm) ? LaserOptics.WavelengthToColor(wavelengthNm) : infraredFalseColor;
            Color hdr = baseColor * glowIntensity;
            hdr.a = 1f;
            return hdr;
        }

        private void ClearTrace()
        {
            m_Segments.Clear();
            m_Spots.Clear();
            m_Scatter.Clear();
            m_StrayPower = 0f;
            m_ClippedPower = 0f;
            m_BackReflectedPower = 0f;
        }

        /// <summary>Traces the full beam tree for this frame.</summary>
        private void Trace(bool isPreview)
        {
            ClearTrace();
            if ((!m_ShutterOpen && !isPreview) || outputPower <= 0f)
            {
                return;
            }

            SyncPhysicsTransformsOncePerFrame();

            Transform origin = BeamOrigin;
            m_Queue.Reset();
            m_Queue.HighlightCollider = isPreview ? null : LaserBeamHighlight.SelectedOptic;
            m_Queue.AddRoot(origin.position, origin.forward, outputPower, housingCollider);

            float minimumPower = outputPower * minimumPowerFraction;
            int interactions = 0;

            while (m_Queue.TryTakeNext(out LaserBeamQueue.PendingBeam beam))
            {
                if (beam.Power < minimumPower)
                {
                    continue;
                }

                float remainingLength = maxPathLength - beam.PathLength;
                if (remainingLength <= 0f)
                {
                    continue;
                }

                float startRadius = BeamRadiusAt(beam.PathLength);

                if (!TryFindNearestHit(beam.Origin, beam.Direction, remainingLength, beam.IgnoredCollider, out RaycastHit hit))
                {
                    // The beam leaves the scene without hitting anything.
                    Vector3 end = beam.Origin + beam.Direction * remainingLength;
                    m_Segments.Add(new LaserSegment(beam.Origin, end, beam.Power, startRadius, BeamRadiusAt(maxPathLength), null, beam.Highlighted));
                    continue;
                }

                float pathAtHit = beam.PathLength + hit.distance;
                float radiusAtHit = BeamRadiusAt(pathAtHit);
                m_Segments.Add(new LaserSegment(beam.Origin, hit.point, beam.Power, startRadius, radiusAtHit, hit.collider, beam.Highlighted));
                m_Spots.Add(new LaserSpot(hit.point, hit.normal, radiusAtHit, beam.Power, beam.Highlighted));

                var laserHit = new LaserHit(this, hit.collider, hit.point, hit.normal, beam.Direction,
                    beam.Power, radiusAtHit, pathAtHit, beam.Depth, isPreview);

                m_Queue.BeginHit(laserHit, beam.Highlighted);

                if (housingCollider != null && hit.collider == housingCollider)
                {
                    // Light returned into the laser. With an isolator it is simply absorbed.
                    if (!hasFaradayIsolator)
                    {
                        m_Queue.ReportScatter(LaserScatterKind.BackReflection, hit.point, beam.Power);
                    }
                }
                else
                {
                    ILaserInteractable optic = FindInteractable(hit.collider);
                    if (optic != null && interactions < maxInteractions)
                    {
                        interactions++;
                        optic.OnLaserHit(in laserHit, m_Queue);
                    }
                    else if (optic == null && beam.Power >= outputPower * strayLightThreshold)
                    {
                        m_Queue.ReportScatter(LaserScatterKind.StrayLight, hit.point, beam.Power);
                    }
                }

                m_Queue.EndHit();
            }

            CollectScatter();
        }

        private void CollectScatter()
        {
            IReadOnlyList<LaserScatterEvent> events = m_Queue.ScatterEvents;
            for (int i = 0; i < events.Count; i++)
            {
                LaserScatterEvent scatter = events[i];
                m_Scatter.Add(scatter);
                switch (scatter.Kind)
                {
                    case LaserScatterKind.StrayLight:
                        m_StrayPower += scatter.Power;
                        break;
                    case LaserScatterKind.Clipping:
                        m_ClippedPower += scatter.Power;
                        break;
                    case LaserScatterKind.BackReflection:
                        m_BackReflectedPower += scatter.Power;
                        break;
                }
            }
        }

        private bool TryFindNearestHit(Vector3 origin, Vector3 direction, float maxDistance, Collider ignored, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(new Ray(origin, direction), m_RaycastBuffer, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);

            nearest = default;
            float nearestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = m_RaycastBuffer[i];
                if (candidate.collider == ignored || candidate.distance <= SelfHitEpsilon)
                {
                    continue;
                }

                if (candidate.distance < nearestDistance)
                {
                    nearestDistance = candidate.distance;
                    nearest = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static ILaserInteractable FindInteractable(Collider collider)
        {
            var optic = collider.GetComponentInParent<ILaserInteractable>();
            if (optic is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            {
                return null;
            }

            return optic;
        }

        private float Fraction(float power)
        {
            return outputPower > 0f ? power / outputPower : 0f;
        }

        // Physics queries use collider poses from the last physics step. Mirrors are rotated in Update,
        // so push the new poses to the physics engine before tracing (once per frame for all lasers;
        // always in the editor, where the frame counter does not advance between gizmo redraws).
        private static void SyncPhysicsTransformsOncePerFrame()
        {
            if (s_LastPhysicsSyncFrame == Time.frameCount && Application.isPlaying)
            {
                return;
            }

            s_LastPhysicsSyncFrame = Time.frameCount;
            Physics.SyncTransforms();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_ActiveEmitters.Clear();
            s_LastPhysicsSyncFrame = -1;
        }

        private void OnValidate()
        {
            maxPathLength = Mathf.Max(0.1f, maxPathLength);
            waistRadius = Mathf.Max(0.0005f, waistRadius);
        }

        private void OnDrawGizmos()
        {
            if (!previewInEditor)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                // Outside Play mode we trace here so level designers can see the beam path.
                Trace(true);
            }

            Color color = LaserOptics.IsHumanVisible(wavelengthNm) ? LaserOptics.WavelengthToColor(wavelengthNm) : infraredFalseColor;
            for (int i = 0; i < m_Segments.Count; i++)
            {
                float brightness = Mathf.Clamp(Mathf.Pow(Mathf.Clamp01(Fraction(m_Segments[i].Power)), 0.4f), 0.15f, 1f);
                Gizmos.color = new Color(color.r, color.g, color.b, brightness);
                Gizmos.DrawLine(m_Segments[i].Start, m_Segments[i].End);
            }

            Transform origin = BeamOrigin;
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(origin.position, waistRadius * 2f);
        }
    }

    /// <summary>
    /// Script execution order: mirrors move first, then lasers trace, then optics and detectors
    /// evaluate what they received, then game logic reads the results.
    /// </summary>
    public static class LaserExecutionOrder
    {
        public const int MirrorMount = -50;
        public const int Emitter = 50;
        public const int Receiver = 100;
        public const int GameLogic = 150;
    }
}

using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A control-room screen that shows where the beam(s) land on a <see cref="LaserTarget"/>,
    /// like the beam-spot cameras and QPD read-outs operators watch in a real control room.
    /// Optionally shows four quadrant bars: equal bars mean the spot is centred.
    /// Each marker is moved inside a circle of <see cref="displayRadius"/> (local units of this
    /// transform). Works with 3D objects and with UI RectTransforms.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserTargetDisplay : MonoBehaviour
    {
        [SerializeField] private LaserTarget target = null;

        [Tooltip("One marker per beam (e.g. 2 for the output port of a Michelson). Children of this object.")]
        [SerializeField] private Transform[] beamMarkers = new Transform[0];

        [Tooltip("Radius of the screen circle that matches the edge of the detector, in local units.")]
        [SerializeField, Min(0.0001f)] private float displayRadius = 0.1f;

        [Tooltip("Show the spot this many times bigger than reality, so small errors are easy to see.")]
        [SerializeField, Min(1f)] private float zoom = 1f;

        [Tooltip("Optional: four bars (top-left, top-right, bottom-left, bottom-right) whose length shows the share of light on each " +
                 "quadrant of the photodiode. Pivot at the left end; they are scaled along local X. Four equal bars = centred beam.")]
        [SerializeField] private Transform[] quadrantBars = new Transform[0];

        [Tooltip("Local X scale of a quadrant bar that holds half of all the light.")]
        [SerializeField, Min(0.0001f)] private float barHalfPowerLength = 0.09f;

        [Tooltip("Marker colours follow the target state (needs a Renderer on each marker).")]
        [SerializeField] private bool colorByState = true;
        [SerializeField] private Color misalignedColor = new Color(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color alignedColor = new Color(0.2f, 1f, 0.3f, 1f);

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock m_PropertyBlock;
        private Renderer[] m_MarkerRenderers;
        private Vector3[] m_MarkerRestPositions;
        private Renderer[] m_BarRenderers;
        private Vector3[] m_BarRestScales;

        /// <summary>The detector shown on this screen.</summary>
        public LaserTarget Target => target;

        /// <summary>Show another detector (e.g. when a new level loads).</summary>
        public void SetTarget(LaserTarget newTarget)
        {
            target = newTarget;
        }

        private void Awake()
        {
            m_MarkerRenderers = new Renderer[beamMarkers.Length];
            m_MarkerRestPositions = new Vector3[beamMarkers.Length];
            for (int i = 0; i < beamMarkers.Length; i++)
            {
                if (beamMarkers[i] == null)
                {
                    continue;
                }

                m_MarkerRenderers[i] = beamMarkers[i].GetComponent<Renderer>();
                m_MarkerRestPositions[i] = beamMarkers[i].localPosition;
            }

            m_BarRenderers = new Renderer[quadrantBars.Length];
            m_BarRestScales = new Vector3[quadrantBars.Length];
            for (int i = 0; i < quadrantBars.Length; i++)
            {
                if (quadrantBars[i] == null)
                {
                    continue;
                }

                m_BarRenderers[i] = quadrantBars[i].GetComponentInChildren<Renderer>();
                m_BarRestScales[i] = quadrantBars[i].localScale;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Color color = target.IsAligned ? alignedColor : misalignedColor;
            for (int i = 0; i < beamMarkers.Length; i++)
            {
                Transform marker = beamMarkers[i];
                if (marker == null)
                {
                    continue;
                }

                bool hasBeam = i < target.TrackedBeamCount;
                if (marker.gameObject.activeSelf != hasBeam)
                {
                    marker.gameObject.SetActive(hasBeam);
                }

                if (!hasBeam)
                {
                    continue;
                }

                target.GetBeam(i, out Vector2 offset, out _);
                Vector2 screen = Vector2.ClampMagnitude(offset / target.DetectorRadius * zoom, 1f) * displayRadius;
                Vector3 rest = m_MarkerRestPositions[i];
                marker.localPosition = new Vector3(screen.x, screen.y, rest.z);

                if (colorByState)
                {
                    SetColor(m_MarkerRenderers[i], color);
                }
            }

            UpdateQuadrantBars(color);
        }

        private void UpdateQuadrantBars(Color color)
        {
            if (quadrantBars.Length == 0)
            {
                return;
            }

            // Share of light on each quadrant for a Gaussian spot, from the normalised difference signals.
            Vector2 signal = target.IsIlluminated ? target.QuadrantSignals : Vector2.zero;
            float lit = target.IsIlluminated ? 1f : 0f;
            float topLeft = (1f - signal.x) * (1f + signal.y) * 0.25f;
            float topRight = (1f + signal.x) * (1f + signal.y) * 0.25f;
            float bottomLeft = (1f - signal.x) * (1f - signal.y) * 0.25f;
            float bottomRight = (1f + signal.x) * (1f - signal.y) * 0.25f;

            for (int i = 0; i < quadrantBars.Length && i < 4; i++)
            {
                Transform bar = quadrantBars[i];
                if (bar == null)
                {
                    continue;
                }

                float share = i == 0 ? topLeft : i == 1 ? topRight : i == 2 ? bottomLeft : bottomRight;
                Vector3 scale = m_BarRestScales[i];
                scale.x = Mathf.Max(0.0001f, Mathf.Clamp01(share * 2f) * barHalfPowerLength * lit);
                bar.localScale = scale;

                if (colorByState)
                {
                    SetColor(m_BarRenderers[i], color);
                }
            }
        }

        private void SetColor(Renderer markerRenderer, Color color)
        {
            if (markerRenderer == null)
            {
                return;
            }

            m_PropertyBlock ??= new MaterialPropertyBlock();
            markerRenderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(s_BaseColorId, color);
            m_PropertyBlock.SetColor(s_ColorId, color);
            markerRenderer.SetPropertyBlock(m_PropertyBlock);
        }
    }
}

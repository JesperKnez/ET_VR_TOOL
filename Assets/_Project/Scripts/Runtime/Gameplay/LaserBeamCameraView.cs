using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// One "beam camera" on the control-room screen: a close-up of an iris or detector face showing where the
    /// beam lands, like the cameras real control rooms point at their optics. The spot is drawn in false colour,
    /// with the iris hole, the 1 cm goal zone and a crosshair. It also shows the beam when the iris plate blocks it.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserBeamCameraView : MonoBehaviour
    {
        public enum ViewState
        {
            NoBeam,
            Blocked,
            OffCentre,
            Centred
        }

        [SerializeField] private LaserTarget target = null;
        [SerializeField] private string label = "";

        [Tooltip("Half the width of the camera view on the real optic (metres). 25 mm shows the whole iris hole.")]
        [SerializeField, Min(0.001f)] private float fieldHalfWidth = 0.025f;

        [Tooltip("Radius of the round view on the screen (local units).")]
        [SerializeField, Min(0.001f)] private float viewRadius = 0.05f;

        [Header("Parts")]
        [SerializeField] private Transform spot = null;
        [SerializeField] private Transform halo = null;
        [SerializeField] private Text labelText = null;
        [SerializeField] private Text positionText = null;
        [SerializeField] private Text stateText = null;

        [Header("Colours")]
        [SerializeField] private Color centredColor = new Color(0.25f, 1f, 0.4f, 1f);
        [SerializeField] private Color offCentreColor = new Color(1f, 0.7f, 0.15f, 1f);
        [SerializeField] private Color blockedColor = new Color(1f, 0.3f, 0.15f, 1f);
        [SerializeField] private Color idleColor = new Color(0.55f, 0.62f, 0.7f, 1f);

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock m_Block;
        private Vector3 m_SpotZ;
        private Vector3 m_HaloZ;

        public LaserTarget Target => target;
        public ViewState State { get; private set; }
        public string Label => label;

        /// <summary>Where the spot is drawn in the view (local units), for tests.</summary>
        public Vector2 SpotPosition => spot != null ? new Vector2(spot.localPosition.x, spot.localPosition.y) : Vector2.zero;
        public string PositionLine => positionText != null ? positionText.text : string.Empty;

        public void SetTarget(LaserTarget newTarget, string newLabel)
        {
            target = newTarget;
            label = newLabel ?? string.Empty;
            if (labelText != null)
            {
                labelText.text = label.ToUpperInvariant();
            }
        }

        private void Awake()
        {
            m_SpotZ = spot != null ? new Vector3(0f, 0f, spot.localPosition.z) : Vector3.zero;
            m_HaloZ = halo != null ? new Vector3(0f, 0f, halo.localPosition.z) : Vector3.zero;
            if (labelText != null)
            {
                labelText.text = label.ToUpperInvariant();
            }
        }

        private void LateUpdate()
        {
            if (target == null || !target.isActiveAndEnabled || !target.HasBeamOnFace)
            {
                Show(ViewState.NoBeam, Vector2.zero, 0f, 0f);
                return;
            }

            ViewState state = target.IsAligned ? ViewState.Centred : target.IsIlluminated ? ViewState.OffCentre : ViewState.Blocked;
            Show(state, target.FaceHitOffset, target.FaceHitBeamRadius, target.FaceHitPowerFraction);
        }

        private void Show(ViewState state, Vector2 offsetMeters, float beamRadius, float powerFraction)
        {
            State = state;
            bool visible = state != ViewState.NoBeam;
            SetActive(spot, visible);
            SetActive(halo, visible);

            Color color = state == ViewState.Centred ? centredColor : state == ViewState.OffCentre ? offCentreColor : state == ViewState.Blocked ? blockedColor : idleColor;
            if (visible)
            {
                float scale = viewRadius / fieldHalfWidth;
                Vector2 position = offsetMeters * scale;
                bool offView = position.magnitude > viewRadius;
                position = Vector2.ClampMagnitude(position, viewRadius * 0.88f);   // keep the glow inside the round view
                float size = Mathf.Clamp(beamRadius * scale, viewRadius * 0.06f, viewRadius * 0.6f);
                float brightness = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(powerFraction * 2f));

                if (spot != null)
                {
                    spot.localPosition = new Vector3(position.x, position.y, m_SpotZ.z);
                    spot.localScale = Vector3.one * size;
                    Tint(spot, color * brightness);
                }

                if (halo != null)
                {
                    halo.localPosition = new Vector3(position.x, position.y, m_HaloZ.z);
                    halo.localScale = Vector3.one * size * 2.2f;
                    Tint(halo, color * (0.35f * brightness));
                }

                // The goal is a circle: what counts is the distance from the centre, not x and y separately.
                string distance = LaserText.Number(offsetMeters.magnitude * 1000f, 1);
                string goal = target != null ? LaserText.Number(target.CenteringTolerance * 1000f, 0) : "10";
                SetText(positionText, "x " + LaserText.SignedNumber(offsetMeters.x * 1000f, 1) + "  y " + LaserText.SignedNumber(offsetMeters.y * 1000f, 1) +
                                      "\n" + distance + " mm off (goal < " + goal + ")" + (offView ? "  >>" : ""), idleColor);
            }
            else
            {
                SetText(positionText, "---", idleColor);
            }

            string word = state == ViewState.Centred ? "CENTRED" : state == ViewState.OffCentre ? "OFF CENTRE" : state == ViewState.Blocked ? "BLOCKED BY IRIS" : "NO BEAM";
            if (target != null && target.Type == LaserTarget.TargetType.Photodiode && state == ViewState.Blocked)
            {
                word = "OFF THE SENSOR";
            }

            SetText(stateText, word, color);
        }

        private void Tint(Transform part, Color color)
        {
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            color.a = 1f;
            m_Block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(s_BaseColorId, color);
            m_Block.SetColor(s_ColorId, color);
            m_Block.SetColor(s_EmissionColorId, color * 2f);
            renderer.SetPropertyBlock(m_Block);
        }

        private static void SetActive(Transform part, bool active)
        {
            if (part != null && part.gameObject.activeSelf != active)
            {
                part.gameObject.SetActive(active);
            }
        }

        private static void SetText(Text text, string value, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (text.text != value)
            {
                text.text = value;
            }

            text.color = color;
        }
    }
}

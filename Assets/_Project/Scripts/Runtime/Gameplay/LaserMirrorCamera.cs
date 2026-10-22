using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Live camera feed "from the selected mirror": a real camera sits on the mirror's surface where the
    /// beam hits it and looks along the reflected beam. The beam therefore leaves from the centre of the
    /// picture, and you see where it is heading: the next mirror or iris should end up on the crosshair.
    /// When no beam reaches the mirror, the camera looks straight out of the mirror instead.
    /// Rendered into a small RenderTexture shown on the screen next to the control pad.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 40)]
    public class LaserMirrorCamera : MonoBehaviour
    {
        [Tooltip("Empty = the first MirrorControlPanel in the scene.")]
        [SerializeField] private MirrorControlPanel controlPanel = null;

        [SerializeField] private Camera feedCamera = null;
        [SerializeField] private RawImage feedImage = null;
        [SerializeField] private Text titleText = null;
        [SerializeField] private Text hintText = null;

        [Header("Picture")]
        [SerializeField] private Vector2Int resolution = new Vector2Int(512, 288);
        [Tooltip("Narrow field of view = zoomed in, so distant irises are big enough to see.")]
        [SerializeField, Range(5f, 90f)] private float fieldOfView = 16f;
        [Tooltip("Distance in front of the mirror where the camera starts drawing (skips the mirror's own frame).")]
        [SerializeField, Min(0.01f)] private float nearClip = 0.3f;
        [Tooltip("Render every Nth frame (2 = half the frame rate). Saves performance on standalone headsets.")]
        [SerializeField, Range(1, 6)] private int renderEveryNthFrame = 2;

        private RenderTexture m_Texture;
        private MirrorMount m_ShownMirror;
        private bool m_ShownHasBeam;

        /// <summary>True when the camera follows a beam that hits the selected mirror (false = looking straight out).</summary>
        public bool FollowsBeam { get; private set; }

        /// <summary>The direction the camera looks (world space).</summary>
        public Vector3 ViewDirection { get; private set; }

        public MirrorMount ShownMirror => m_ShownMirror;
        public RenderTexture Texture => m_Texture;

        private void Awake()
        {
            if (feedCamera != null)
            {
                m_Texture = new RenderTexture(Mathf.Max(16, resolution.x), Mathf.Max(16, resolution.y), 16) { name = "Mirror Camera Feed" };
                feedCamera.targetTexture = m_Texture;
                feedCamera.fieldOfView = fieldOfView;
                feedCamera.nearClipPlane = nearClip;
                feedCamera.farClipPlane = 30f;
                feedCamera.enabled = false;
            }

            if (feedImage != null)
            {
                feedImage.texture = m_Texture;
            }
        }

        private void OnDestroy()
        {
            if (m_Texture != null)
            {
                m_Texture.Release();
                Destroy(m_Texture);
            }
        }

        private void LateUpdate()
        {
            MirrorControlPanel panel = controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;
            MirrorMount mirror = panel != null ? panel.SelectedMirror : null;
            if (mirror == null || feedCamera == null)
            {
                SetCamera(false);
                UpdateTexts(null, false);
                return;
            }

            Collider optic = mirror.GetComponentInChildren<Collider>();
            LaserMirror coating = optic != null ? optic.GetComponentInParent<LaserMirror>() : null;
            Transform surface = coating != null ? coating.Surface : optic != null ? optic.transform : mirror.transform;
            Vector3 normal = surface.forward;

            if (TryFindIncomingBeam(optic, out Vector3 hitPoint, out Vector3 incoming) && Vector3.Dot(incoming, normal) < 0f)
            {
                Vector3 outgoing = LaserOptics.Reflect(incoming, normal);
                Place(hitPoint + outgoing * 0.01f, outgoing);
                FollowsBeam = true;
            }
            else
            {
                Vector3 centre = optic != null ? optic.transform.position : surface.position;
                Place(centre + normal * 0.2f, normal);   // just in front of the coated face
                FollowsBeam = false;
            }

            int frame = Time.frameCount;
            SetCamera(renderEveryNthFrame <= 1 || frame % renderEveryNthFrame == 0);
            UpdateTexts(mirror, FollowsBeam);
        }

        private void Place(Vector3 position, Vector3 direction)
        {
            ViewDirection = direction.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(ViewDirection, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            feedCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(ViewDirection, up));
        }

        /// <summary>The strongest beam of any active laser that ends on this optic.</summary>
        private static bool TryFindIncomingBeam(Collider optic, out Vector3 hitPoint, out Vector3 direction)
        {
            hitPoint = Vector3.zero;
            direction = Vector3.zero;
            if (optic == null)
            {
                return false;
            }

            float best = 0f;
            var emitters = LaserEmitter.ActiveEmitters;
            for (int e = 0; e < emitters.Count; e++)
            {
                var segments = emitters[e].Segments;
                for (int i = 0; i < segments.Count; i++)
                {
                    LaserSegment segment = segments[i];
                    if (segment.HitCollider != optic || segment.Power <= best)
                    {
                        continue;
                    }

                    Vector3 delta = segment.End - segment.Start;
                    if (delta.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    best = segment.Power;
                    hitPoint = segment.End;
                    direction = delta.normalized;
                }
            }

            return best > 0f;
        }

        private void SetCamera(bool render)
        {
            if (feedCamera != null && feedCamera.enabled != render)
            {
                feedCamera.enabled = render;
            }
        }

        private void UpdateTexts(MirrorMount mirror, bool hasBeam)
        {
            if (mirror == m_ShownMirror && hasBeam == m_ShownHasBeam && mirror != null)
            {
                return;
            }

            m_ShownMirror = mirror;
            m_ShownHasBeam = hasBeam;
            if (titleText != null)
            {
                titleText.text = mirror != null ? "MIRROR CAMERA  |  " + mirror.DisplayName.ToUpperInvariant() : "MIRROR CAMERA";
            }

            if (hintText != null)
            {
                hintText.text = mirror == null ? "No mirror selected."
                    : hasBeam ? "Looking along the reflected beam: put the next iris or mirror on the crosshair."
                    : "No beam on this mirror: looking straight out of it. Steer the mirrors before it first.";
            }
        }
    }
}

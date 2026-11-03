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

        public enum ViewMode
        {
            /// <summary>Camera circles the mirror at a fixed distance, so you see the tower, the mirror and both beams.</summary>
            Orbit,
            /// <summary>From the mirror surface along the reflected beam.</summary>
            AlongBeam
        }

        [System.Serializable]
        public struct ViewPreset
        {
            public string name;
            public ViewMode mode;
            [Tooltip("Orbit: distance from the mirror (m). Larger than the tank radius = filmed from outside.")]
            public float distance;
            [Tooltip("Orbit: angle above the horizon (degrees). 90 = straight from above, 0 = from the side.")]
            [Range(0f, 90f)] public float elevation;
            [Tooltip("Orbit: angle around the mirror (degrees). 0 = straight in front of the coated face, 90 = side, 180 = behind.")]
            [Range(-180f, 180f)] public float sideAngle;
            [Range(5f, 90f)] public float fieldOfView;

            public ViewPreset(string name, ViewMode mode, float distance, float elevation, float sideAngle, float fieldOfView)
            {
                this.name = name;
                this.mode = mode;
                this.distance = distance;
                this.elevation = elevation;
                this.sideAngle = sideAngle;
                this.fieldOfView = fieldOfView;
            }
        }

        [Header("Views (the VIEW button on the screen cycles through these)")]
        [SerializeField] private ViewPreset[] views = DefaultViews();

        private static ViewPreset[] DefaultViews() => new[]
        {
            new ViewPreset("Overview", ViewMode.Orbit, 5f, 40f, 30f, 35f),
            new ViewPreset("Top", ViewMode.Orbit, 5f, 80f, 0f, 35f),
            new ViewPreset("Along beam", ViewMode.AlongBeam, 0f, 0f, 0f, 16f)
        };

        [ContextMenu("Reset views to defaults")]
        private void ResetViews()
        {
            views = DefaultViews();
            m_ViewIndex = 0;
        }

        private GameObject[] m_Crosshair = new GameObject[0];

        [Tooltip("Optional: shows the name of the current view (e.g. on the VIEW button).")]
        [SerializeField] private Text viewLabel = null;

        private int m_ViewIndex;

        [Header("Hidden in the feed (optional)")]
        [Tooltip("Renderers hidden for this camera only, e.g. a tank wall that blocks the view. Empty = nothing hidden.")]
        [SerializeField] private Renderer[] hideInFeed = new Renderer[0];

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

                // The crosshair only means something when looking along the beam (the beam leaves from the centre).
                var found = new System.Collections.Generic.List<GameObject>();
                foreach (Transform child in feedImage.transform.parent)
                {
                    if (child.name.StartsWith("Crosshair"))
                    {
                        found.Add(child.gameObject);
                    }
                }

                m_Crosshair = found.ToArray();
            }

            UpdateViewLabel();
        }

        private void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += HandleEndCamera;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= HandleEndCamera;
        }

        private void HandleBeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) => SetShellHidden(camera, true);

        private void HandleEndCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) => SetShellHidden(camera, false);

        private void SetShellHidden(Camera camera, bool hidden)
        {
            if (camera != feedCamera)
            {
                return;
            }

            for (int i = 0; i < hideInFeed.Length; i++)
            {
                if (hideInFeed[i] != null)
                {
                    hideInFeed[i].forceRenderingOff = hidden;
                }
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

            Vector3 centre = optic != null ? optic.transform.position : surface.position;
            bool hasBeam = TryFindIncomingBeam(optic, out Vector3 hitPoint, out Vector3 incoming) && Vector3.Dot(incoming, normal) < 0f;
            FollowsBeam = hasBeam;

            ViewPreset view = CurrentView;
            bool showCrosshair = view.mode == ViewMode.AlongBeam;
            for (int i = 0; i < m_Crosshair.Length; i++)
            {
                if (m_Crosshair[i] != null && m_Crosshair[i].activeSelf != showCrosshair)
                {
                    m_Crosshair[i].SetActive(showCrosshair);
                }
            }

            feedCamera.fieldOfView = view.fieldOfView > 0f ? view.fieldOfView : fieldOfView;
            if (view.mode == ViewMode.Orbit)
            {
                PlaceOrbit(centre, normal, view);
            }
            else if (hasBeam)
            {
                Vector3 outgoing = LaserOptics.Reflect(incoming, normal);
                Place(hitPoint + outgoing * 0.01f, outgoing);
            }
            else
            {
                Place(centre + normal * 0.2f, normal);   // just in front of the coated face
            }

            int frame = Time.frameCount;
            SetCamera(renderEveryNthFrame <= 1 || frame % renderEveryNthFrame == 0);
            UpdateTexts(mirror, FollowsBeam);
        }

        /// <summary>Name of the current view, e.g. "Side".</summary>
        public string CurrentViewName => CurrentView.name;

        private ViewPreset CurrentView => views != null && views.Length > 0
            ? views[Mathf.Clamp(m_ViewIndex, 0, views.Length - 1)]
            : new ViewPreset("Overview", ViewMode.Orbit, 5f, 40f, 30f, 35f);

        /// <summary>Next camera view. Hook this to the VIEW button's OnClick.</summary>
        public void NextView() => StepView(1);

        public void PreviousView() => StepView(-1);

        private void StepView(int step)
        {
            if (views == null || views.Length == 0)
            {
                return;
            }

            m_ViewIndex = (m_ViewIndex + step + views.Length) % views.Length;
            m_ShownMirror = null;   // forces the texts to refresh
            UpdateViewLabel();
        }

        private void UpdateViewLabel()
        {
            if (viewLabel != null)
            {
                viewLabel.text = "VIEW: " + CurrentView.name.ToUpperInvariant();
            }
        }

        /// <summary>Camera at the given distance, height and angle around the mirror, looking at its centre.</summary>
        private void PlaceOrbit(Vector3 centre, Vector3 normal, ViewPreset view)
        {
            Vector3 flat = Vector3.ProjectOnPlane(normal, Vector3.up);
            flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;
            flat = Quaternion.AngleAxis(view.sideAngle, Vector3.up) * flat;

            float elevation = view.elevation * Mathf.Deg2Rad;
            Vector3 offset = (flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) * view.distance;
            Place(centre + offset, -offset);
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
                titleText.text = mirror != null ? "MIRROR CAMERA  |  " + mirror.DisplayName.ToUpperInvariant() + "  |  " + CurrentView.name.ToUpperInvariant() : "MIRROR CAMERA";
            }

            if (hintText != null)
            {
                hintText.text = mirror == null ? "No mirror selected."
                    : CurrentView.mode == ViewMode.Orbit ? (hasBeam ? "Press VIEW to look at the mirror from another side." : "No beam on this mirror. Steer the mirrors before it first.")
                    : hasBeam ? "Looking along the reflected beam: put the next iris or mirror on the crosshair."
                    : "No beam on this mirror: looking straight out of it. Steer the mirrors before it first.";
            }
        }
    }
}

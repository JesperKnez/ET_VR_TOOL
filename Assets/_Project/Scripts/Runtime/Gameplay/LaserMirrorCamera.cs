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
        [Tooltip("Width of the laser lines in this picture, in pixels. Constant, so the lines do not jump when the player moves.")]
        [SerializeField, Range(1f, 8f)] private float beamWidthPixels = 3f;
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
            new ViewPreset("Along beam", ViewMode.AlongBeam, 0f, 0f, 0f, 16f),
            new ViewPreset("Overview", ViewMode.Orbit, 5f, 40f, 30f, 35f),
            new ViewPreset("Top", ViewMode.Orbit, 5f, 80f, 0f, 35f)
        };

        [Header("Goal ring (Along beam view)")]
        [Tooltip("Zoom in automatically on the goal so a small error stays visible: the picture is always about 1.6x the current error high.")]
        [SerializeField] private bool autoZoom = true;
        [Tooltip("Smallest picture height (in multiples of the goal tolerance) the auto zoom goes to.")]
        [SerializeField, Min(2f)] private float minFieldInTolerances = 8f;
        [SerializeField] private Color goalColor = new Color(1f, 0.7f, 0.15f, 1f);
        [SerializeField] private Color goalReachedColor = new Color(0.25f, 1f, 0.4f, 1f);

        private const int RingPoints = 48;
        private LineRenderer m_Ring;
        private Material m_RingMaterial;
        private float m_ZoomFov;
        private int m_HintKey = int.MinValue;

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

            CreateRing();
            UpdateViewLabel();
        }

        private void CreateRing()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var ringObject = new GameObject("Goal Ring");
            ringObject.transform.SetParent(transform, false);
            m_Ring = ringObject.AddComponent<LineRenderer>();
            m_Ring.useWorldSpace = true;
            m_Ring.loop = true;
            m_Ring.positionCount = RingPoints;
            m_Ring.numCapVertices = 0;
            m_Ring.alignment = LineAlignment.View;
            m_Ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Ring.receiveShadows = false;
            m_Ring.allowOcclusionWhenDynamic = false;
            m_RingMaterial = new Material(shader) { name = "Goal Ring" };
            m_Ring.sharedMaterial = m_RingMaterial;
            m_Ring.forceRenderingOff = true;   // only the mirror camera sees it, see HandleBeginCamera
            m_Ring.enabled = false;
        }

        private void OnEnable()
        {
            // Along beam: the camera sits inside the tank and the line of sight runs through its wall and pipes. The tower
            // (one mesh) is made very faint in that view (see GhostTower), so you still see it, but the beam stays visible.
            var tower = new System.Collections.Generic.List<Renderer>();
            foreach (TowerTransparency t in FindObjectsByType<TowerTransparency>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                tower.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }

            m_TowerRenderers = tower.ToArray();

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

            if (m_Ring != null)
            {
                m_Ring.forceRenderingOff = !hidden;   // "hidden" is true at the start of this camera's render
            }

            if (CurrentView.mode == ViewMode.AlongBeam)
            {
                GhostTower(hidden);
            }

            if (hidden)
            {
                SetBeamWidthsForFeed();
            }
            else
            {
                RestoreBeamWidths();
            }

            for (int i = 0; i < hideInFeed.Length; i++)
            {
                if (hideInFeed[i] != null)
                {
                    hideInFeed[i].forceRenderingOff = hidden;
                }
            }
        }

        // The beam lines are widened every frame so they stay visible from the player's eye (LaserBeamMinWidth). That
        // width changes whenever the player moves their head, so in this picture the lines would jump. While the mirror
        // camera renders, the lines get a constant width in pixels of this picture instead, and are restored afterwards.
        [Header("Tower in the Along beam view")]
        [Tooltip("The tower stays visible, but very faint, so the wall in front of the camera does not hide the beam. Only for this camera, and only for the see-through (alpha blended) parts.")]
        [SerializeField, Range(0.01f, 1f)] private float towerAlphaInFeed = 0.05f;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_SurfaceId = Shader.PropertyToID("_Surface");
        private MaterialPropertyBlock m_GhostBlock;
        private readonly MaterialPropertyBlock m_ColorBlock = new MaterialPropertyBlock();

        [Tooltip("Along beam view: only draw the beam you are steering (the highlight colour), not the beam coming in or the other arm.")]
        [SerializeField] private bool hideOtherBeams = true;

        /// <summary>The tower's see-through materials get a lower alpha while this camera renders (and back afterwards).</summary>
        private void GhostTower(bool on)
        {
            if (m_GhostBlock == null)
            {
                m_GhostBlock = new MaterialPropertyBlock();
            }

            for (int r = 0; r < m_TowerRenderers.Length; r++)
            {
                Renderer renderer = m_TowerRenderers[r];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetSharedMaterials(m_GhostMaterials);
                for (int i = 0; i < m_GhostMaterials.Count; i++)
                {
                    Material material = m_GhostMaterials[i];
                    if (material == null || !material.HasProperty(s_SurfaceId) || !material.HasProperty(s_BaseColorId) || material.GetFloat(s_SurfaceId) < 0.5f)
                    {
                        continue;   // opaque material: nothing to fade
                    }

                    if (on)
                    {
                        Color color = material.GetColor(s_BaseColorId);
                        color.a = Mathf.Min(color.a, towerAlphaInFeed);
                        m_GhostBlock.Clear();
                        m_GhostBlock.SetColor(s_BaseColorId, color);
                        renderer.SetPropertyBlock(m_GhostBlock, i);
                    }
                    else
                    {
                        renderer.SetPropertyBlock(null, i);
                    }
                }
            }
        }

        private readonly System.Collections.Generic.List<Material> m_GhostMaterials = new System.Collections.Generic.List<Material>();
        private readonly System.Collections.Generic.List<LineRenderer> m_BeamLines = new System.Collections.Generic.List<LineRenderer>();
        private readonly System.Collections.Generic.List<LineRenderer> m_LinesInEmitter = new System.Collections.Generic.List<LineRenderer>();
        private readonly System.Collections.Generic.List<Vector2> m_SavedWidths = new System.Collections.Generic.List<Vector2>();

        private void SetBeamWidthsForFeed()
        {
            m_BeamLines.Clear();
            m_SavedWidths.Clear();
            float worldPerPixelPerMetre = 2f * Mathf.Tan(feedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, m_Texture != null ? m_Texture.height : resolution.y);
            Vector3 from = feedCamera.transform.position;
            var emitters = LaserEmitter.ActiveEmitters;
            for (int e = 0; e < emitters.Count; e++)
            {
                emitters[e].GetComponentsInChildren(false, m_LinesInEmitter);
                for (int i = 0; i < m_LinesInEmitter.Count; i++)
                {
                    LineRenderer line = m_LinesInEmitter[i];
                    if (!line.enabled || line.positionCount < 2)
                    {
                        continue;
                    }

                    m_BeamLines.Add(line);
                    m_SavedWidths.Add(new Vector2(line.startWidth, line.endWidth));

                    // Looking along the beam, only the beam you are steering matters: the light from the selected mirror
                    // (drawn in the highlight colour). The green lines are the beam coming in and the other arm. For an end
                    // mirror they run along the same path, so showing both looks like two lasers.
                    if (hideOtherBeams && CurrentView.mode == ViewMode.AlongBeam && LaserBeamHighlight.SelectedOptic != null)
                    {
                        line.GetPropertyBlock(m_ColorBlock);
                        Color drawn = m_ColorBlock.GetColor(s_BaseColorId);
                        Color highlight = LaserBeamHighlight.Color;
                        bool isHighlight = Mathf.Abs(drawn.r - highlight.r) + Mathf.Abs(drawn.g - highlight.g) + Mathf.Abs(drawn.b - highlight.b) < 0.05f;
                        if (!isHighlight)
                        {
                            line.forceRenderingOff = true;
                        }
                    }
                    line.startWidth = Vector3.Distance(from, line.GetPosition(0)) * worldPerPixelPerMetre * beamWidthPixels;
                    line.endWidth = Vector3.Distance(from, line.GetPosition(1)) * worldPerPixelPerMetre * beamWidthPixels;
                }
            }
        }

        private void RestoreBeamWidths()
        {
            for (int i = 0; i < m_BeamLines.Count; i++)
            {
                if (m_BeamLines[i] != null)
                {
                    m_BeamLines[i].forceRenderingOff = false;
                    m_BeamLines[i].startWidth = m_SavedWidths[i].x;
                    m_BeamLines[i].endWidth = m_SavedWidths[i].y;
                }
            }

            m_BeamLines.Clear();
            m_SavedWidths.Clear();
        }

        private void OnDestroy()
        {
            if (m_Texture != null)
            {
                m_Texture.Release();
                Destroy(m_Texture);
            }

            if (m_RingMaterial != null)
            {
                Destroy(m_RingMaterial);
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
            else
            {
                if (hasBeam)
                {
                    Vector3 outgoing = LaserOptics.Reflect(incoming, normal);
                    PlaceChase(hitPoint + outgoing * 0.01f, outgoing);
                }
                else
                {
                    PlaceChase(centre + normal * 0.2f, normal);   // just in front of the coated face
                }
            }

            int frame = Time.frameCount;
            SetCamera(renderEveryNthFrame <= 1 || frame % renderEveryNthFrame == 0);
            UpdateTexts(mirror, FollowsBeam);

            if (view.mode == ViewMode.AlongBeam)
            {
                UpdateGoal();
            }
            else if (m_Ring != null)
            {
                m_Ring.enabled = false;
                m_ZoomFov = 0f;
            }
        }

        // ----- Goal ring: where the beam must go --------------------------------------------------------

        private readonly System.Collections.Generic.List<LaserSegment> m_Segments = new System.Collections.Generic.List<LaserSegment>();
        private readonly System.Collections.Generic.List<int> m_Path = new System.Collections.Generic.List<int>();
        private float m_SmoothedError = -1f;
        private Renderer[] m_TowerRenderers = new Renderer[0];

        private Vector3 m_BeamOrigin;
        private Vector3 m_BeamDirection;
        private bool m_GoalValid;
        private Vector3 m_GoalError;
        private float m_GoalTolerance;
        private float m_GoalDistance;

        [Header("Chase camera (Along beam view)")]
        [Tooltip("How far the camera sits beside the beam, as a fraction of the distance to the goal. 0 = exactly on the beam (the beam is then seen end-on, as a dot).")]
        [SerializeField, Range(0f, 0.3f)] private float chaseOffset = 0.08f;

        /// <summary>
        /// The camera sits a little beside the beam, close to the mirror, and looks at the spot where the beam lands.
        /// So the beam is a real line in the picture, running to the crosshair, and the ring shows where it must end.
        /// </summary>
        private void PlaceChase(Vector3 beamOrigin, Vector3 beamDirection)
        {
            m_BeamOrigin = beamOrigin;
            m_BeamDirection = beamDirection;
            m_GoalValid = TryTrace(beamOrigin, beamDirection, out m_GoalError, out m_GoalTolerance, out m_GoalDistance);
            float length = m_GoalValid ? Mathf.Max(m_GoalDistance, 0.5f) : 3f;

            Vector3 up = Mathf.Abs(Vector3.Dot(beamDirection, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            Vector3 sideways = Vector3.Cross(beamDirection, up).normalized;
            Vector3 above = Vector3.Cross(sideways, beamDirection).normalized;
            Vector3 position = beamOrigin + (above + sideways).normalized * Mathf.Max(0.08f, chaseOffset * length);
            Vector3 landing = beamOrigin + beamDirection * length;
            Place(position, landing - position);
        }

        /// <summary>
        /// Draws a ring where the beam has to be and zooms in on it. The crosshair is where the beam is; "ring in the
        /// crosshair" means the beam ends up in the centre of the iris or detector it is heading for, which is the same
        /// number as on the detector screen. The zoom follows the error, so a few millimetres stay visible.
        /// </summary>
        private void UpdateGoal()
        {
            Vector3 origin = m_BeamOrigin;
            Vector3 direction = m_BeamDirection;
            Vector3 error = m_GoalError;
            float tolerance = m_GoalTolerance;
            float distance = m_GoalDistance;
            if (!m_GoalValid)
            {
                m_Ring.enabled = false;
                m_ZoomFov = 0f;
                m_SmoothedError = -1f;
                SetHint(-2, "none");
                return;
            }

            float errorLength = error.magnitude;
            distance = Mathf.Max(distance, 0.5f);

            // Smoothed, so the zoom does not pump when the suspension swings
            m_SmoothedError = m_SmoothedError < 0f ? errorLength : Mathf.Lerp(m_SmoothedError, errorLength, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            float half = Mathf.Max(m_SmoothedError * 1.6f, tolerance * minFieldInTolerances * 0.5f);
            if (autoZoom)
            {
                float target = Mathf.Clamp(2f * Mathf.Atan(half / distance) * Mathf.Rad2Deg, 0.2f, 40f);
                m_ZoomFov = m_ZoomFov <= 0f ? target : Mathf.Lerp(m_ZoomFov, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
                feedCamera.fieldOfView = m_ZoomFov;
            }

            // Ring of the goal's tolerance, shifted by the error, drawn just in front of the next optic
            Vector3 centre = origin + direction * (distance - 0.02f) + error;
            Quaternion facing = feedCamera.transform.rotation;
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPoints;
                m_Ring.SetPosition(i, centre + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * tolerance);
            }

            float fieldHeight = 2f * distance * Mathf.Tan(feedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            m_Ring.widthMultiplier = Mathf.Max(fieldHeight * 0.02f, tolerance * 0.15f);
            Color color = errorLength <= tolerance ? goalReachedColor : goalColor;
            m_RingMaterial.color = color;
            m_RingMaterial.SetColor("_BaseColor", color);
            m_Ring.enabled = true;

            float cm = fieldHeight * 100f;
            SetHint(Mathf.RoundToInt(cm * 10f), cm < 10f ? cm.ToString("0.0") : Mathf.RoundToInt(cm).ToString());
        }

        private void SetHint(int key, string heightCm)
        {
            if (hintText == null || key == m_HintKey)
            {
                return;
            }

            m_HintKey = key;
            hintText.text = heightCm == "none"
                ? "No beam path to an iris or detector yet. Crosshair = where the beam goes."
                : "Crosshair = beam. Ring = where it must go. Picture " + heightCm + " cm high.";
        }

        /// <summary>
        /// Follows the beam that leaves the mirror, through the optics after it, to the iris or detector it ends on.
        /// There the beam is some distance from the centre (the same distance the detector screen shows). Every mirror
        /// on the way flips that distance, so it is flipped back mirror by mirror: the result is how far the beam
        /// would have to move, right here, to hit the centre. <paramref name="distance"/> is the length of the first piece of beam.
        /// </summary>
        private bool TryTrace(Vector3 origin, Vector3 direction, out Vector3 error, out float tolerance, out float distance)
        {
            error = Vector3.zero;
            tolerance = 0.01f;
            distance = 0f;

            m_Segments.Clear();
            var emitters = LaserEmitter.ActiveEmitters;
            for (int e = 0; e < emitters.Count; e++)
            {
                var segments = emitters[e].Segments;
                for (int i = 0; i < segments.Count; i++)
                {
                    if ((segments[i].End - segments[i].Start).sqrMagnitude > 1e-8f)
                    {
                        m_Segments.Add(segments[i]);
                    }
                }
            }

            // The first piece: starts at the mirror, goes along the view direction
            int first = -1;
            float best = 0f;
            for (int i = 0; i < m_Segments.Count; i++)
            {
                LaserSegment s = m_Segments[i];
                if (s.Power > best && (s.Start - origin).sqrMagnitude < 0.0025f && Vector3.Dot(SegmentDirection(s), direction) > 0.99f)
                {
                    best = s.Power;
                    first = i;
                }
            }

            if (first < 0)
            {
                return false;
            }

            m_Path.Clear();
            m_Tried.Clear();
            m_Path.Add(first);
            if (!Follow(first, 0, out LaserTarget target))
            {
                return false;
            }

            LaserSegment last = m_Segments[m_Path[m_Path.Count - 1]];
            Vector3 lastDirection = SegmentDirection(last);
            Vector3 e2 = target.Surface.position - last.End;
            e2 -= lastDirection * Vector3.Dot(e2, lastDirection);

            // Back through the mirrors, last one first
            for (int k = m_Path.Count - 1; k >= 1; k--)
            {
                Vector3 before = SegmentDirection(m_Segments[m_Path[k - 1]]);
                Vector3 after = SegmentDirection(m_Segments[m_Path[k]]);
                if (Vector3.Angle(before, after) > 0.5f)
                {
                    Vector3 normal = (after - before).normalized;
                    e2 -= 2f * Vector3.Dot(e2, normal) * normal;
                }
            }

            error = e2 - direction * Vector3.Dot(e2, direction);
            tolerance = target.CenteringTolerance;
            distance = (m_Segments[first].End - m_Segments[first].Start).magnitude;
            return true;
        }

        private static Vector3 SegmentDirection(LaserSegment s) => (s.End - s.Start).normalized;

        /// <summary>Depth-first: the beam pieces that start where this one ends, until one ends on a <see cref="LaserTarget"/>.</summary>
        private bool Follow(int index, int depth, out LaserTarget target)
        {
            LaserSegment segment = m_Segments[index];
            target = segment.HitCollider != null ? segment.HitCollider.GetComponentInParent<LaserTarget>() : null;
            if (target != null)
            {
                return true;
            }

            if (depth >= 8)
            {
                return false;
            }

            // The pieces that continue from here (a beam splitter gives two), nearest start first
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int next = -1;
                float nearest = 0.12f;
                for (int i = 0; i < m_Segments.Count; i++)
                {
                    if (m_Path.Contains(i) || m_Segments[i].Power > segment.Power * 1.001f)
                    {
                        continue;
                    }

                    float distance = (m_Segments[i].Start - segment.End).magnitude;
                    if (distance < nearest && !m_Tried.Contains(depth * 1000 + i))
                    {
                        nearest = distance;
                        next = i;
                    }
                }

                if (next < 0)
                {
                    return false;
                }

                m_Tried.Add(depth * 1000 + next);
                m_Path.Add(next);
                if (Follow(next, depth + 1, out target))
                {
                    return true;
                }

                m_Path.RemoveAt(m_Path.Count - 1);
            }

            target = null;
            return false;
        }

        private readonly System.Collections.Generic.HashSet<int> m_Tried = new System.Collections.Generic.HashSet<int>();

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
            m_HintKey = int.MinValue;   // the goal hint is written again after this
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

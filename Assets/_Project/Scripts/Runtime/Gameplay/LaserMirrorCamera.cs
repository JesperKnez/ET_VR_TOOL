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
            AlongBeam,
            /// <summary>Real 3D picture of the whole path of the beam you steer, from the mirror to its target, seen from the side through the see-through tower.</summary>
            BeamPath
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
            new ViewPreset("Whole path", ViewMode.BeamPath, 0f, 35f, 90f, 40f),
            new ViewPreset("Close-up", ViewMode.AlongBeam, 0f, 0f, 0f, 16f),
            new ViewPreset("Overview", ViewMode.Orbit, 5f, 40f, 30f, 35f),
            new ViewPreset("Top", ViewMode.Orbit, 5f, 80f, 0f, 35f)
        };

        [Header("Goal ring (Along beam view)")]
        [Tooltip("Zoom in automatically on the goal so a small error stays visible: the picture is always about 1.6x the current error high.")]
        [SerializeField] private bool autoZoom = true;
        [SerializeField] private Color goalColor = new Color(1f, 0.7f, 0.15f, 1f);
        [SerializeField] private Color goalReachedColor = new Color(0.25f, 1f, 0.4f, 1f);

        [SerializeField] private Color beamColor = new Color(0.25f, 0.8f, 1f, 1f);

        private const int RingPoints = 48;

        // Close-up: the picture shows only the ring, cross, beam and dot on a plain background. The real hall behind
        // them was confusing: optics in front of the target looked like the target, and the unfolded path does not
        // match the real room anyway. The overlay lines go on this (unnamed, unused) layer.
        private const int OverlayLayer = 30;
        [Tooltip("Close-up: background colour behind the ring and dot.")]
        [SerializeField] private Color closeUpBackground = new Color(0.03f, 0.06f, 0.1f, 1f);
        [SerializeField] private Color crossColor = new Color(0.16f, 0.26f, 0.36f, 1f);
        [Tooltip("The dot counts as on target (green ring, ON TARGET) within this fraction of the tolerance, so seismic noise does not push it out again.")]
        [SerializeField, Range(0.3f, 1f)] private float centredFraction = 0.6f;
        // Close-up picture half height, in tolerances (ring radii). The zoom jumps between these steps.
        private static readonly float[] ZoomSteps = { 4f, 12f, 40f, 120f, 400f };
        private int m_ZoomStep = -1;
        private MirrorMount m_ZoomMirror;
        private LineRenderer m_CrossH, m_CrossV;
        private Material m_CrossMaterial;
        private int m_SceneMask;
        private CameraClearFlags m_SceneClear;
        private Color m_SceneBackground;
        private bool m_PlainBackground;
        private LineRenderer m_Ring;
        private LineRenderer m_BeamLine;
        private LineRenderer m_Dot;
        private Material m_RingMaterial;
        private Material m_BeamMaterial;
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
                feedCamera.farClipPlane = 80f;   // the unfolded goal can lie far behind the first optic
                feedCamera.enabled = false;
                m_SceneMask = feedCamera.cullingMask;
                m_SceneClear = feedCamera.clearFlags;
                m_SceneBackground = feedCamera.backgroundColor;
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
            if (hintText != null && !showHintText)
            {
                hintText.text = "";   // the on-picture guide explains what to do
            }
        }

        private void CreateRing()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            m_RingMaterial = new Material(shader) { name = "Goal Ring" };
            m_BeamMaterial = new Material(shader) { name = "Goal Beam" };
            m_Ring = CreateOverlayLine("Goal Ring", RingPoints, true, 0, m_RingMaterial);
            m_BeamLine = CreateOverlayLine("Goal Beam", 2, false, 0, m_BeamMaterial);
            m_Dot = CreateOverlayLine("Goal Dot", 2, false, 8, m_BeamMaterial);   // a short line with round caps = a dot
            m_CrossMaterial = new Material(shader) { name = "Goal Cross" };
            SetColor(m_CrossMaterial, crossColor);
            m_CrossH = CreateOverlayLine("Goal Cross H", 2, false, 0, m_CrossMaterial);
            m_CrossV = CreateOverlayLine("Goal Cross V", 2, false, 0, m_CrossMaterial);
        }

        private LineRenderer CreateOverlayLine(string objectName, int points, bool loop, int caps, Material material)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.numCapVertices = caps;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.allowOcclusionWhenDynamic = false;
            line.sharedMaterial = material;
            line.forceRenderingOff = true;   // only the mirror camera sees it, see HandleBeginCamera
            lineObject.layer = OverlayLayer;
            line.enabled = false;
            return line;
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

            var crane = new System.Collections.Generic.List<Renderer>();
            GameObject craneRoot = GameObject.Find("ETP_OverheadCrane");
            if (craneRoot != null)
            {
                crane.AddRange(craneRoot.GetComponentsInChildren<Renderer>(false));
            }

            m_CraneRenderers = crane.ToArray();

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

            // "hidden" is true at the start of this camera's render: the overlay is shown only then
            if (m_Ring != null) m_Ring.forceRenderingOff = !hidden;
            if (m_BeamLine != null) m_BeamLine.forceRenderingOff = !hidden;
            if (m_Dot != null) m_Dot.forceRenderingOff = !hidden;
            if (m_CrossH != null) m_CrossH.forceRenderingOff = !hidden;
            if (m_CrossV != null) m_CrossV.forceRenderingOff = !hidden;

            // The towers are one big mesh: in the Overview and Top views the camera can end up inside a tank or right
            // against its wall, and in the top-down view the tank roofs cover the optics, so the picture showed nothing
            // but metal (also the Close-up when no beam reaches the mirror: it looks straight out of the mirror, from
            // inside the tank). This camera simply does not draw them; the player still sees them.
            bool hideTower = hidden;
            for (int i = 0; i < m_TowerRenderers.Length; i++)
            {
                if (m_TowerRenderers[i] != null) m_TowerRenderers[i].forceRenderingOff = hideTower;
            }

            GhostTower(hidden && !hideTower);

            // The overhead crane hangs between the high cameras (Top, Whole path) and the floor
            bool hideCrane = hidden && CurrentView.mode != ViewMode.AlongBeam;
            for (int i = 0; i < m_CraneRenderers.Length; i++)
            {
                if (m_CraneRenderers[i] != null)
                {
                    m_CraneRenderers[i].forceRenderingOff = hideCrane;
                }
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
        private MaterialPropertyBlock m_ColorBlock;   // not created here: a MaterialPropertyBlock cannot be made in a field initializer

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
                    if (CurrentView.mode == ViewMode.AlongBeam && m_GoalValid)
                    {
                        // The unfolded beam line, dot and ring (see UpdateGoal) replace the real lines in this view
                        line.forceRenderingOff = true;
                    }
                    else if (hideOtherBeams && CurrentView.mode == ViewMode.AlongBeam && LaserBeamHighlight.SelectedOptic != null)
                    {
                        if (m_ColorBlock == null)
                        {
                            m_ColorBlock = new MaterialPropertyBlock();
                        }

                        line.GetPropertyBlock(m_ColorBlock);
                        Color drawn = m_ColorBlock.GetColor(s_BaseColorId);
                        Color highlight = LaserBeamHighlight.Color;
                        bool isHighlight = Mathf.Abs(drawn.r - highlight.r) + Mathf.Abs(drawn.g - highlight.g) + Mathf.Abs(drawn.b - highlight.b) < 0.05f;
                        if (!isHighlight)
                        {
                            line.forceRenderingOff = true;
                        }
                    }
                    if (feedCamera.orthographic)
                    {
                        float width = 2f * feedCamera.orthographicSize / Mathf.Max(1, m_Texture != null ? m_Texture.height : resolution.y) * beamWidthPixels;
                        line.startWidth = line.endWidth = width;
                        continue;
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

            if (m_CrossMaterial != null)
            {
                Destroy(m_CrossMaterial);
            }

            if (m_BeamMaterial != null)
            {
                Destroy(m_BeamMaterial);
            }
        }

        private void LateUpdate()
        {
            MirrorControlPanel panel = controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;
            MirrorMount mirror = panel != null ? panel.SelectedMirror : null;
            m_GuideValid = false;
            if (mirror == null || feedCamera == null)
            {
                SetCamera(false);
                UpdateTexts(null, false);
                SetGuideVisible(false);
                return;
            }

            Collider optic = mirror.GetComponentInChildren<Collider>();
            LaserMirror coating = optic != null ? optic.GetComponentInParent<LaserMirror>() : null;
            Transform surface = coating != null ? coating.Surface : optic != null ? optic.transform : mirror.transform;
            Vector3 normal = surface.forward;

            Vector3 centre = optic != null ? optic.transform.position : surface.position;
            bool hasBeam = TryFindIncomingBeam(optic, out Vector3 hitPoint, out Vector3 incoming) && Vector3.Dot(incoming, normal) < 0f;
            FollowsBeam = hasBeam;

            m_GuideMirror = mirror;
            m_GuideSurface = surface;
            m_GuideIncoming = incoming;
            m_GuideNormal = normal;
            m_GuideHasBeam = hasBeam;

            ViewPreset view = CurrentView;
            bool showCrosshair = false;   // replaced by the labelled ring and dot (the crosshair was confusing)
            for (int i = 0; i < m_Crosshair.Length; i++)
            {
                if (m_Crosshair[i] != null && m_Crosshair[i].activeSelf != showCrosshair)
                {
                    m_Crosshair[i].SetActive(showCrosshair);
                }
            }

            feedCamera.fieldOfView = view.fieldOfView > 0f ? view.fieldOfView : fieldOfView;
            feedCamera.orthographic = false;   // only the Beam path view is orthographic (set in PlaceBeamPath)
            if (view.mode == ViewMode.Orbit)
            {
                PlaceOrbit(centre, normal, view);
            }
            else if (view.mode == ViewMode.BeamPath)
            {
                PlaceBeamPath(hasBeam, hitPoint, incoming, normal, centre, view);
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
            else if (view.mode == ViewMode.BeamPath)
            {
                UpdateRealGoal();
            }
            else if (m_Ring != null)
            {
                SetOverlay(false);
                m_ZoomFov = 0f;
            }

            SetPlainBackground(view.mode == ViewMode.AlongBeam && m_GoalValid);
            if (view.mode != ViewMode.Orbit && m_GoalValid && hasBeam)
            {
                UpdateArrowMap();
            }

            UpdatePictureFlip(view.mode == ViewMode.AlongBeam && m_GoalValid);
            UpdateGuide(view.mode);
        }

        // ----- Goal ring: where the beam must go --------------------------------------------------------
        //
        // The beam leaves the mirror, bounces off other optics and ends on an iris or the detector. To show all of that in
        // one straight picture, the path is "unfolded": every optic after the mirror is mirrored away, so the beam becomes
        // one straight line and the target ends up at a fixed point G in front of the camera. The beam's landing point L
        // lies on that same straight line. G does not move when you tilt the mirror (only the beam does), and L - G is
        // exactly the offset the detector screen shows. So: the ring (G) stays put, the beam and its dot (L) move.

        private readonly System.Collections.Generic.List<LaserSegment> m_Segments = new System.Collections.Generic.List<LaserSegment>();
        private readonly System.Collections.Generic.List<int> m_Path = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> m_BestPath = new System.Collections.Generic.List<int>();
        private Renderer[] m_TowerRenderers = new Renderer[0];
        private Renderer[] m_CraneRenderers = new Renderer[0];
        private LaserTarget[] m_Targets = new LaserTarget[0];
        private float m_TargetsTime = float.NegativeInfinity;

        private Vector3 m_BeamOrigin;
        private bool m_GoalValid;
        private Vector3 m_GoalCentre;     // G: centre of the target, unfolded
        private Vector3 m_Landing;        // L: where the beam lands, unfolded
        private float m_GoalTolerance;
        private bool m_GoalReached;       // the path really ends on the target (false = guessed from the direction the beam heads)

        // ----- Beam path view: the real beam in the real tower -----------------------------------------------

        [Header("Beam path view")]
        [Tooltip("The camera stays inside this box (world space), so the hall walls do not block the picture. Default: the hall.")]
        [SerializeField] private Vector3 cameraLimitMin = new Vector3(-16.5f, 0.5f, -10.5f);
        [SerializeField] private Vector3 cameraLimitMax = new Vector3(16.5f, 7.5f, 10.5f);

        private Vector3 m_RealGoal;        // centre of the target, real world position
        private Vector3 m_RealLanding;     // where the beam really lands (or would land, extended to the target)
        private Vector3 m_RealAxis;        // direction of the last piece of beam
        private Bounds m_PathBounds;       // mirror, bounce points and target
        private Vector3 m_PathCentre;
        private float m_PathRadius = -1f;

        /// <summary>
        /// Camera beside the whole path of the steered beam, high enough to look down on it. The real (cyan) beam, the
        /// other beams and the optics are all in the picture at their real positions, so what you see is what the laser does.
        /// </summary>
        private void PlaceBeamPath(bool hasBeam, Vector3 hitPoint, Vector3 incoming, Vector3 normal, Vector3 centre, ViewPreset view)
        {
            Vector3 direction = hasBeam ? LaserOptics.Reflect(incoming, normal) : normal;
            Vector3 origin = hasBeam ? hitPoint + direction * 0.01f : centre + normal * 0.2f;
            m_BeamOrigin = origin;
            m_GoalValid = TryTrace(origin, direction, out m_GoalCentre, out m_Landing, out m_GoalTolerance, out m_GoalReached);
            if (!m_GoalValid)
            {
                m_PathRadius = -1f;
                PlaceOrbit(centre, normal, new ViewPreset("", ViewMode.Orbit, 4f, view.elevation, 30f, view.fieldOfView));
                return;
            }

            // Straight from above (orthographic): no perspective, so nothing in the hall can stand in front of the beam,
            // and the whole path always fits. Screen up = world +Z.
            Bounds bounds = m_PathBounds;
            bounds.Encapsulate(origin);
            float aspect = Mathf.Max(0.5f, feedCamera.aspect);
            float half = Mathf.Max(bounds.extents.z, bounds.extents.x / aspect, 0.5f) * 1.15f + 0.3f;
            if (m_PathRadius < 0f)
            {
                m_PathCentre = bounds.center;
                m_PathRadius = half;
            }
            else
            {
                float t = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);   // smooth: the picture must not jump
                m_PathCentre = Vector3.Lerp(m_PathCentre, bounds.center, t);
                m_PathRadius = Mathf.Lerp(m_PathRadius, half, t);
            }

            feedCamera.orthographic = true;
            feedCamera.orthographicSize = m_PathRadius;
            var position = new Vector3(m_PathCentre.x, cameraLimitMax.y - 0.3f, m_PathCentre.z);
            ViewDirection = Vector3.down;
            feedCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        }

        /// <summary>
        /// Ring round the real target and a dot where the beam really lands, at their real positions. The ring is never
        /// drawn smaller than a few pixels; use the Along beam view for the last millimetres.
        /// </summary>
        private void UpdateRealGoal()
        {
            if (!m_GoalValid)
            {
                SetOverlay(false);
                SetHint(-2, "none");
                return;
            }

            Vector3 toGoal = m_RealGoal - feedCamera.transform.position;
            float distance = Mathf.Max(toGoal.magnitude, 0.5f);
            float fieldHeight = feedCamera.orthographic ? 2f * feedCamera.orthographicSize : 2f * distance * Mathf.Tan(feedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pixel = fieldHeight / Mathf.Max(1, m_Texture != null ? m_Texture.height : resolution.y);
            Quaternion facing = feedCamera.transform.rotation;

            Vector3 offset = m_RealLanding - m_RealGoal;
            offset -= m_RealAxis * Vector3.Dot(offset, m_RealAxis);
            bool centred = m_GoalReached && offset.magnitude <= m_GoalTolerance * centredFraction;
            float ringRadius = Mathf.Max(m_GoalTolerance, pixel * 8f);

            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPoints;
                m_Ring.SetPosition(i, m_RealGoal + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ringRadius);
            }

            m_Ring.widthMultiplier = Mathf.Max(pixel * 3f, 0.0005f);
            SetColor(m_RingMaterial, centred ? goalReachedColor : goalColor);

            float dotSize = Mathf.Max(pixel * 8f, 0.004f);
            Vector3 right = facing * Vector3.right;
            m_Dot.SetPosition(0, m_RealLanding - right * dotSize * 0.05f);
            m_Dot.SetPosition(1, m_RealLanding + right * dotSize * 0.05f);
            m_Dot.widthMultiplier = dotSize;
            SetColor(m_BeamMaterial, beamColor);

            m_Ring.enabled = true;
            m_Dot.enabled = true;
            m_BeamLine.enabled = false;   // the real beam is drawn by the laser itself

            m_GuideValid = true;
            m_GuideSteps = false;
            m_GuideGoal = m_RealGoal;
            m_GuideLanding = m_RealLanding;
            m_GuideBeamMid = Vector3.Lerp(m_BeamOrigin, m_RealLanding, 0.5f);
            m_GuideRingRadius = ringRadius;
            m_GuideCentred = centred;
            m_GuideInRing = offset.magnitude <= m_GoalTolerance;
            SetHint(-3 - (m_GoalReached ? 0 : 1), "path");
        }

        // ----- On-picture explanation ---------------------------------------------------------------------
        //
        // What the player sees must explain itself: a banner says what to do, labels name the beam, the target and the
        // landing dot, an arrow points from the dot to the ring, and (Along beam view) the banner says which buttons to
        // press and how often. That number comes from the mirror's real axes: it is how far the dot moves per button press.

        [Header("On-picture explanation")]
        [SerializeField] private bool showGuide = true;
        [Tooltip("Also show the small hint line under the picture (the guide replaces it).")]
        [SerializeField] private bool showHintText = false;

        private RectTransform m_GuideRoot;
        private Font m_GuideFont;
        private Image m_BannerBackground;
        private Text m_Banner, m_BannerSub, m_LabelBeam, m_LabelGoal, m_LabelDot;
        private Image m_ArrowShaft, m_ArrowHeadA, m_ArrowHeadB;
        private bool m_GuideValid;
        private bool m_GuideSteps;                 // Along beam view: the offset is in the camera's own plane, so button steps can be worked out
        private Vector3 m_GuideGoal, m_GuideLanding, m_GuideBeamMid;
        private float m_GuideRingRadius;
        private bool m_GuideCentred;
        private bool m_GuideInRing;
        private MirrorMount m_GuideMirror;
        private Transform m_GuideSurface;
        private Vector3 m_GuideIncoming, m_GuideNormal;
        private bool m_GuideHasBeam;
        private Vector2 m_SmoothedSteps;
        private bool m_StepsKnown;

        private static readonly Color BannerBack = new Color(0.01f, 0.03f, 0.06f, 0.78f);

        private void EnsureGuide()
        {
            if (m_GuideRoot != null || feedImage == null)
            {
                return;
            }

            Font font = titleText != null && titleText.font != null ? titleText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_GuideFont = font;
            var root = new GameObject("Guide", typeof(RectTransform));
            root.layer = feedImage.gameObject.layer;
            m_GuideRoot = (RectTransform)root.transform;
            m_GuideRoot.SetParent(feedImage.transform, false);
            m_GuideRoot.anchorMin = Vector2.zero;
            m_GuideRoot.anchorMax = Vector2.one;
            m_GuideRoot.offsetMin = m_GuideRoot.offsetMax = Vector2.zero;

            // Banner strip at the top
            m_BannerBackground = GuideImage("Banner Background", BannerBack);
            var back = m_BannerBackground.rectTransform;
            back.anchorMin = new Vector2(0f, 1f);
            back.anchorMax = new Vector2(1f, 1f);
            back.pivot = new Vector2(0.5f, 1f);
            back.sizeDelta = new Vector2(0f, 128f);
            back.anchoredPosition = Vector2.zero;

            m_Banner = GuideText("Banner", font, 46, Color.white, true);
            StretchIn(m_Banner.rectTransform, back, 0.42f, 1f);
            m_BannerSub = GuideText("Banner Sub", font, 34, new Color(0.8f, 0.9f, 1f, 1f), false);
            StretchIn(m_BannerSub.rectTransform, back, 0f, 0.46f);

            m_LabelBeam = GuideText("Label Laser Beam", font, 32, beamColor, true);
            m_LabelGoal = GuideText("Label Target", font, 32, goalColor, true);
            m_LabelDot = GuideText("Label Landing", font, 32, beamColor, true);
            m_LabelBeam.text = "LASER BEAM";
            m_LabelGoal.text = "TARGET";
            m_LabelDot.text = "BEAM LANDS HERE";

            m_ArrowShaft = GuideImage("Arrow Shaft", Color.white);
            m_ArrowHeadA = GuideImage("Arrow Head A", Color.white);
            m_ArrowHeadB = GuideImage("Arrow Head B", Color.white);
            foreach (Image arrow in new[] { m_ArrowShaft, m_ArrowHeadA, m_ArrowHeadB })
            {
                arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = Vector2.zero;
            }

            m_ArrowHeadA.rectTransform.pivot = m_ArrowHeadB.rectTransform.pivot = new Vector2(0f, 0.5f);
            m_ArrowHeadA.rectTransform.sizeDelta = m_ArrowHeadB.rectTransform.sizeDelta = new Vector2(44f, 9f);
        }

        private Image GuideImage(string objectName, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.layer = m_GuideRoot.gameObject.layer;
            go.transform.SetParent(m_GuideRoot, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text GuideText(string objectName, Font font, int size, Color color, bool bold)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            go.layer = m_GuideRoot.gameObject.layer;
            go.transform.SetParent(m_GuideRoot, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520f, 44f);
            return text;
        }

        /// <summary>Places a banner text in a vertical band (fractions) of the banner strip, parented to the root.</summary>
        private void StretchIn(RectTransform text, RectTransform strip, float from, float to)
        {
            text.SetParent(strip, false);
            text.anchorMin = new Vector2(0f, from);
            text.anchorMax = new Vector2(1f, to);
            text.offsetMin = new Vector2(12f, 0f);
            text.offsetMax = new Vector2(-12f, 0f);
        }

        private void SetGuideVisible(bool on)
        {
            if (m_GuideRoot != null && m_GuideRoot.gameObject.activeSelf != on)
            {
                m_GuideRoot.gameObject.SetActive(on);
            }
        }

        private void UpdateGuide(ViewMode mode)
        {
            if (!showGuide || feedImage == null || feedCamera == null || (mode != ViewMode.AlongBeam && mode != ViewMode.BeamPath))
            {
                SetGuideVisible(false);
                return;
            }

            EnsureGuide();
            SetGuideVisible(true);

            bool labels = false;
            if (!m_GuideHasBeam)
            {
                SetBanner("NO LASER REACHES THIS MIRROR", Color.white, "Press PREV to pick the mirror before it, and steer that one first.");
            }
            else if (!m_GuideValid)
            {
                SetBanner("NO PATH TO A TARGET YET", Color.white, "Steer the mirrors before this one until the blue beam reaches an iris or the detector.");
            }
            else
            {
                labels = true;
                string sub = mode == ViewMode.AlongBeam ? StepsText() : "For the last millimetres: press VIEW and choose CLOSE-UP.";
                string targetName = m_GoalTarget != null ? m_GoalTarget.name.ToUpperInvariant() : "THE TARGET";
                string goalLabel = "TARGET: " + targetName;
                if (m_LabelGoal.text != goalLabel) m_LabelGoal.text = goalLabel;
                if (m_GuideCentred)
                {
                    SetBanner("ON TARGET - " + targetName, goalReachedColor, "This mirror is done. Press NEXT for the next mirror.");
                }
                else if (!m_GoalReached)
                {
                    SetBanner("THE BEAM DOES NOT REACH " + targetName, goalColor, sub);
                }
                else if (m_GuideInRing)
                {
                    SetBanner("ALMOST - PUT THE DOT IN THE MIDDLE", Color.white, sub);
                }
                else
                {
                    SetBanner("MOVE THE DOT INTO THE RING", Color.white, sub);
                }
            }

            PlaceLabels(labels);
        }

        private string m_BannerKey = "";

        private void SetBanner(string main, Color color, string sub)
        {
            m_Banner.color = color;
            if (m_BannerKey != main + sub)
            {
                m_BannerKey = main + sub;
                m_Banner.text = main;
                m_BannerSub.text = sub;
            }
        }

        private void PlaceLabels(bool on)
        {
            if (!on) PlaceOpticGauges(false);
            m_LabelBeam.gameObject.SetActive(on);
            m_LabelGoal.gameObject.SetActive(on);
            m_LabelDot.gameObject.SetActive(on);
            bool arrow = false;
            if (on)
            {
                float w = m_GuideRoot.rect.width, h = m_GuideRoot.rect.height;
                Vector3 goalView = Shown(feedCamera.WorldToViewportPoint(m_GuideGoal));
                Vector3 dotView = Shown(feedCamera.WorldToViewportPoint(m_GuideLanding));
                Vector3 beamView = Shown(feedCamera.WorldToViewportPoint(m_GuideBeamMid));
                Vector3 edgeView = Shown(feedCamera.WorldToViewportPoint(m_GuideGoal + feedCamera.transform.right * m_GuideRingRadius));
                float ringPx = Mathf.Max(14f, Mathf.Abs(edgeView.x - goalView.x) * w);

                Color goalLabel = m_GuideCentred ? goalReachedColor : goalColor;
                m_LabelGoal.color = goalLabel;
                SetLabel(m_LabelGoal, goalView, w, h, new Vector2(0f, ringPx + 34f));
                SetLabel(m_LabelDot, dotView, w, h, new Vector2(0f, -(Mathf.Min(ringPx, 40f) + 34f)));
                SetLabel(m_LabelBeam, beamView, w, h, new Vector2(0f, 30f));
                PlaceOpticGauges(CurrentView.mode == ViewMode.AlongBeam);

                // Arrow from the dot to the ring (only when the dot is clearly outside)
                var goalPx = new Vector2(goalView.x * w, goalView.y * h);
                var dotPx = new Vector2(dotView.x * w, dotView.y * h);
                Vector2 delta = goalPx - dotPx;
                float length = delta.magnitude;
                if (!m_GuideCentred && goalView.z > 0f && dotView.z > 0f && length > ringPx + 70f)
                {
                    Vector2 dir = delta / length;
                    Vector2 start = dotPx + dir * 22f;
                    Vector2 end = goalPx - dir * (ringPx + 8f);
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    var shaft = m_ArrowShaft.rectTransform;
                    shaft.pivot = new Vector2(0f, 0.5f);
                    shaft.anchoredPosition = start;
                    shaft.sizeDelta = new Vector2((end - start).magnitude, 9f);
                    shaft.localRotation = Quaternion.Euler(0f, 0f, angle);
                    m_ArrowHeadA.rectTransform.anchoredPosition = end;
                    m_ArrowHeadA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle + 180f - 28f);
                    m_ArrowHeadB.rectTransform.anchoredPosition = end;
                    m_ArrowHeadB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle + 180f + 28f);
                    arrow = true;
                }
            }

            m_ArrowShaft.gameObject.SetActive(arrow);
            m_ArrowHeadA.gameObject.SetActive(arrow);
            m_ArrowHeadB.gameObject.SetActive(arrow);
        }

        private static void SetLabel(Text label, Vector3 view, float width, float height, Vector2 offsetPixels)
        {
            bool visible = view.z > 0f && view.x > -0.1f && view.x < 1.1f && view.y > -0.1f && view.y < 1.1f;
            label.gameObject.SetActive(visible);
            if (visible)
            {
                float x = Mathf.Clamp(view.x, 0.14f, 0.86f) * width;
                float y = Mathf.Clamp(view.y, 0.1f, 0.74f) * height;   // keeps clear of the banner
                label.rectTransform.anchoredPosition = new Vector2(x, y) + offsetPixels;
            }
        }

        /// <summary>"Press ► 3×  then ▲ 2×": the buttons and how many presses bring the dot onto the ring.</summary>
        private string StepsText()
        {
            if (!m_GuideSteps || !TryComputeSteps(out Vector2 raw))
            {
                m_StepsKnown = false;
                return "Move the blue beam so the dot lands in the ring.";
            }

            m_SmoothedSteps = m_StepsKnown ? Vector2.Lerp(m_SmoothedSteps, raw, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime)) : raw;
            m_StepsKnown = true;

            int yaw = Mathf.Min(99, Mathf.RoundToInt(Mathf.Abs(m_SmoothedSteps.x)));
            int pitch = Mathf.Min(99, Mathf.RoundToInt(Mathf.Abs(m_SmoothedSteps.y)));
            if (yaw == 0 && pitch == 0)
            {
                return m_GuideMirror.IsFineMode
                    ? "Almost there: single presses of the arrows."
                    : "Almost there: press the centre button to switch to FINE steps.";
            }

            string text = "Press  ";
            if (yaw > 0)
            {
                text += (m_SmoothedSteps.x > 0f ? "► " : "◄ ") + yaw + "x";
            }

            if (pitch > 0)
            {
                text += (yaw > 0 ? "   and   " : "") + (m_SmoothedSteps.y > 0f ? "▲ " : "▼ ") + pitch + "x";
            }

            return text + (m_GuideMirror.IsFineMode ? "" : "   (centre button = FINE)");
        }

        /// <summary>
        /// How the outgoing beam turns for one step of yaw and one step of pitch (unfolded space). Measured instead of
        /// calculated: tilt the mount by one step the way MirrorMount does (nominal * yaw * pitch), read where the coated
        /// face then points, and put the mount back, all within this frame. Right for any hierarchy (mirrored parents, odd axes).
        /// </summary>
        private bool MeasureAxes(out Vector3 yawTurn, out Vector3 pitchTurn)
        {
            yawTurn = pitchTurn = Vector3.zero;
            if (m_GuideMirror == null || !m_GuideHasBeam || m_GuideSurface == null)
            {
                return false;
            }

            float step = m_GuideMirror.CurrentStepDegrees;
            Transform mount = m_GuideMirror.transform;
            Quaternion saved = mount.localRotation;
            Quaternion yawRotation = Quaternion.AngleAxis(m_GuideMirror.ActualYaw, Vector3.up);
            Quaternion pitchRotation = Quaternion.AngleAxis(-m_GuideMirror.ActualPitch, Vector3.right);
            Quaternion nominal = saved * Quaternion.Inverse(yawRotation * pitchRotation);

            mount.localRotation = nominal * Quaternion.AngleAxis(m_GuideMirror.ActualYaw + step, Vector3.up) * pitchRotation;
            Vector3 yawNormal = m_GuideSurface.forward;
            mount.localRotation = nominal * yawRotation * Quaternion.AngleAxis(-(m_GuideMirror.ActualPitch + step), Vector3.right);   // pitch up = -angle
            Vector3 pitchNormal = m_GuideSurface.forward;
            mount.localRotation = saved;

            Vector3 outgoing = LaserOptics.Reflect(m_GuideIncoming, m_GuideNormal);
            yawTurn = LaserOptics.Reflect(m_GuideIncoming, yawNormal) - outgoing;
            pitchTurn = LaserOptics.Reflect(m_GuideIncoming, pitchNormal) - outgoing;
            return true;
        }

        /// <summary>
        /// Makes the arrow buttons mean "move the spot" on the camera view of the target (the dartboard on the monitor):
        /// right arrow = right, up arrow = up, whichever mirror axis and direction does that. A mirror after this one
        /// mirrors the picture, and a periscope mirror can even turn yaw into up/down, so a fixed "right = yaw +" felt
        /// reversed for some mirrors. Only updated from a path that really reached the target (or the remembered one).
        /// </summary>
        private void UpdateArrowMap()
        {
            if (!m_FoldTrusted || m_GoalTarget == null || !MeasureAxes(out Vector3 yawTurn, out Vector3 pitchTurn))
            {
                return;
            }

            Vector3 boardRight = m_GoalTarget.Surface.right, boardUp = m_GoalTarget.Surface.up;
            Vector3 yawReal = UnfoldDirectionBack(yawTurn), pitchReal = UnfoldDirectionBack(pitchTurn);
            var a = new Vector2(Vector3.Dot(yawReal, boardRight), Vector3.Dot(yawReal, boardUp));
            var b = new Vector2(Vector3.Dot(pitchReal, boardRight), Vector3.Dot(pitchReal, boardUp));

            var map = new MirrorControlPanel.ArrowMap();
            if (Mathf.Abs(a.x) * Mathf.Abs(b.y) >= Mathf.Abs(a.y) * Mathf.Abs(b.x))
            {
                map.Right = new Vector2(Mathf.Sign(a.x), 0f);   // yaw moves the spot sideways
                map.Up = new Vector2(0f, Mathf.Sign(b.y));
            }
            else
            {
                map.Right = new Vector2(0f, Mathf.Sign(b.x));   // axes swapped (periscope)
                map.Up = new Vector2(Mathf.Sign(a.y), 0f);
            }

            MirrorControlPanel.ArrowMaps[m_GuideMirror] = map;
        }

        /// <summary>
        /// Close-up: the unfolded picture can be the mirror image of the dartboard (an odd number of mirrors in between).
        /// Then the picture is flipped, so dot and arrows move the same way on both screens.
        /// </summary>
        private void UpdatePictureFlip(bool alongBeam)
        {
            bool flipX = false, flipY = false;
            if (alongBeam && m_FoldTrusted && m_GoalTarget != null)
            {
                flipX = Vector3.Dot(UnfoldDirectionBack(feedCamera.transform.right), m_GoalTarget.Surface.right) < 0f;
                flipY = Vector3.Dot(UnfoldDirectionBack(feedCamera.transform.up), m_GoalTarget.Surface.up) < 0f;
            }

            m_FlipX = flipX;
            m_FlipY = flipY;
            if (feedImage != null)
            {
                var rect = new Rect(flipX ? 1f : 0f, flipY ? 1f : 0f, flipX ? -1f : 1f, flipY ? -1f : 1f);
                if (feedImage.uvRect != rect) feedImage.uvRect = rect;
            }
        }

        private bool m_FlipX, m_FlipY, m_FoldTrusted;

        /// <summary>A viewport point of the camera as it is shown (after the flip).</summary>
        private Vector3 Shown(Vector3 viewport) => new Vector3(m_FlipX ? 1f - viewport.x : viewport.x, m_FlipY ? 1f - viewport.y : viewport.y, viewport.z);

        /// <summary>Direction in the unfolded space back to the real direction at the target (the fold, undone).</summary>
        private Vector3 UnfoldDirectionBack(Vector3 direction)
        {
            for (int i = m_Fold.Count - 1; i >= 0; i--)
            {
                Vector3 normal = m_Fold[i].PlaneNormal;
                if (normal != Vector3.zero) direction -= 2f * Vector3.Dot(direction, normal) * normal;
            }

            return direction;
        }

        /// <summary>
        /// How many presses of the right and up arrows (signed: negative = left / down) move the dot from where it lands
        /// to the ring. One press tilts the mirror one step (see MeasureAxes and the arrow map); the dot moves that turn
        /// times the distance to the goal.
        /// </summary>
        private bool TryComputeSteps(out Vector2 steps)
        {
            steps = Vector2.zero;
            if (!MeasureAxes(out Vector3 yawTurn, out Vector3 pitchTurn))
            {
                return false;
            }

            float lever = Vector3.Distance(m_BeamOrigin, m_GuideGoal);
            Vector3 right = feedCamera.transform.right;
            Vector3 up = feedCamera.transform.up;
            var yawMove = new Vector2(Vector3.Dot(yawTurn, right), Vector3.Dot(yawTurn, up)) * lever;
            var pitchMove = new Vector2(Vector3.Dot(pitchTurn, right), Vector3.Dot(pitchTurn, up)) * lever;
            MirrorControlPanel.ArrowMap map = MirrorControlPanel.MapFor(m_GuideMirror);
            Vector2 rightMove = map.Right.x * yawMove + map.Right.y * pitchMove;   // what one right arrow does
            Vector2 upMove = map.Up.x * yawMove + map.Up.y * pitchMove;            // what one up arrow does
            Vector3 missing = m_GuideGoal - m_GuideLanding;
            var need = new Vector2(Vector3.Dot(missing, right), Vector3.Dot(missing, up));

            float det = rightMove.x * upMove.y - rightMove.y * upMove.x;
            if (Mathf.Abs(det) < 1e-12f)
            {
                return false;
            }

            steps.x = (need.x * upMove.y - need.y * upMove.x) / det;
            steps.y = (rightMove.x * need.y - rightMove.y * need.x) / det;
            return true;
        }

        [Header("Chase camera (Along beam view)")]
        [Tooltip("How far the camera sits beside the beam, as a fraction of the distance to the goal. 0 = exactly on the beam (the beam is then seen end-on, as a dot).")]
        [SerializeField, Range(0f, 0.3f)] private float chaseOffset = 0.08f;

        /// <summary>
        /// The camera sits a little beside the beam, close to the mirror, and looks at the (fixed) goal. Without a path
        /// to a target it looks along the beam instead.
        /// </summary>
        private void PlaceChase(Vector3 beamOrigin, Vector3 beamDirection)
        {
            m_BeamOrigin = beamOrigin;
            m_BeamDirection = beamDirection;
            m_GoalValid = TryTrace(beamOrigin, beamDirection, out m_GoalCentre, out m_Landing, out m_GoalTolerance, out m_GoalReached);

            Vector3 aim = m_GoalValid ? m_GoalCentre : beamOrigin + beamDirection * 3f;
            Vector3 toAim = aim - beamOrigin;
            float length = Mathf.Max(toAim.magnitude, 0.5f);
            Vector3 axis = toAim.sqrMagnitude > 1e-6f ? toAim / toAim.magnitude : beamDirection;

            Vector3 up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            Vector3 sideways = Vector3.Cross(axis, up).normalized;
            Vector3 above = Vector3.Cross(sideways, axis).normalized;
            Vector3 position = beamOrigin + (above + sideways).normalized * Mathf.Max(0.08f, chaseOffset * length);
            Place(position, aim - position);
        }

        /// <summary>
        /// Draws the ring (goal, fixed in the middle of the picture), the beam line and its dot (where the beam lands).
        /// Dot in the ring = the detector screen says centred. The zoom follows the error, so a few millimetres stay visible.
        /// </summary>
        private void UpdateGoal()
        {
            if (!m_GoalValid)
            {
                SetOverlay(false);
                m_ZoomFov = 0f;
                m_ZoomStep = -1;
                SetHint(-2, "none");
                return;
            }

            Vector3 cameraPosition = feedCamera.transform.position;
            Vector3 toGoal = m_GoalCentre - cameraPosition;
            float distance = Mathf.Max(toGoal.magnitude, 0.5f);
            Vector3 axis = toGoal / Mathf.Max(toGoal.magnitude, 1e-4f);

            // Where the beam lands, relative to the goal, in the plane of the goal facing the camera
            Vector3 offset = m_Landing - m_GoalCentre;
            offset -= axis * Vector3.Dot(offset, axis);
            float errorLength = offset.magnitude;
            float tolerance = m_GoalTolerance;

            // Zoom in fixed steps. Between steps the picture stays the same, so every press moves the dot the same
            // distance and the same way. (A zoom that follows the error keeps the dot at the same place in the picture:
            // pressing the right button then looks like it does nothing, or even pushes the dot away.)
            if (m_ZoomMirror != m_GuideMirror)
            {
                m_ZoomMirror = m_GuideMirror;
                m_ZoomStep = -1;
            }

            float inTolerances = errorLength / Mathf.Max(tolerance, 1e-5f);
            int last = ZoomSteps.Length - 1;
            if (m_ZoomStep < 0)
            {
                m_ZoomStep = last;
                while (m_ZoomStep > 0 && inTolerances < 0.6f * ZoomSteps[m_ZoomStep - 1]) m_ZoomStep--;
            }
            else
            {
                while (m_ZoomStep < last && inTolerances > 0.9f * ZoomSteps[m_ZoomStep]) m_ZoomStep++;    // dot near the edge: zoom out
                while (m_ZoomStep > 0 && inTolerances < 0.4f * ZoomSteps[m_ZoomStep - 1]) m_ZoomStep--;   // well inside the next step: zoom in
            }

            if (autoZoom)
            {
                float half = ZoomSteps[m_ZoomStep] * Mathf.Max(tolerance, 1e-5f);
                float target = Mathf.Clamp(2f * Mathf.Atan(half / distance) * Mathf.Rad2Deg, 0.2f, 40f);
                m_ZoomFov = m_ZoomFov <= 0f ? target : Mathf.Lerp(m_ZoomFov, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
                feedCamera.fieldOfView = m_ZoomFov;
            }

            float fieldHeight = 2f * distance * Mathf.Tan(feedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pixel = fieldHeight / Mathf.Max(1, m_Texture != null ? m_Texture.height : resolution.y);
            Quaternion facing = feedCamera.transform.rotation;
            Vector3 right = facing * Vector3.right;
            Vector3 up = facing * Vector3.up;

            bool inRing = errorLength <= tolerance;
            bool centred = m_GoalReached && errorLength <= tolerance * centredFraction;

            // Ring: the target's tolerance, fixed at the goal
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPoints;
                m_Ring.SetPosition(i, m_GoalCentre + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * tolerance);
            }

            m_Ring.widthMultiplier = Mathf.Max(pixel * 3f, tolerance * 0.12f);
            SetColor(m_RingMaterial, centred ? goalReachedColor : goalColor);

            // Faint cross through the ring centre, across the whole picture: shows where the middle is
            float halfWidth = fieldHeight * 0.5f * feedCamera.aspect * 1.2f;
            m_CrossH.SetPosition(0, m_GoalCentre - right * halfWidth);
            m_CrossH.SetPosition(1, m_GoalCentre + right * halfWidth);
            m_CrossV.SetPosition(0, m_GoalCentre - up * fieldHeight * 0.6f);
            m_CrossV.SetPosition(1, m_GoalCentre + up * fieldHeight * 0.6f);
            m_CrossH.widthMultiplier = m_CrossV.widthMultiplier = pixel * 1.5f;

            // Beam: from the mirror to where it lands. The dot marks the landing point.
            Vector3 landing = m_GoalCentre + offset;
            m_BeamLine.SetPosition(0, m_BeamOrigin);
            m_BeamLine.SetPosition(1, landing);
            m_BeamLine.widthMultiplier = Mathf.Max(pixel * beamWidthPixels, 0.0002f);
            float dotSize = Mathf.Max(pixel * 7f, tolerance * 0.25f);
            m_Dot.SetPosition(0, landing - right * dotSize * 0.05f);
            m_Dot.SetPosition(1, landing + right * dotSize * 0.05f);
            m_Dot.widthMultiplier = dotSize;
            SetColor(m_BeamMaterial, beamColor);

            SetOverlay(true);
            UpdatePathOptics();

            m_GuideValid = true;
            m_GuideSteps = true;
            m_GuideGoal = m_GoalCentre;
            m_GuideLanding = landing;
            m_GuideBeamMid = Vector3.Lerp(m_BeamOrigin, landing, 0.6f);
            m_GuideRingRadius = tolerance;
            m_GuideCentred = centred;
            m_GuideInRing = inRing;

            float cm = fieldHeight * 100f;
            SetHint(Mathf.RoundToInt(cm * 10f) + (m_GoalReached ? 0 : 1), cm < 10f ? cm.ToString("0.0") : Mathf.RoundToInt(cm).ToString());
        }

        private static void SetColor(Material material, Color color)
        {
            material.color = color;
            material.SetColor("_BaseColor", color);
        }

        private void SetPlainBackground(bool on)
        {
            if (feedCamera == null || on == m_PlainBackground)
            {
                return;
            }

            m_PlainBackground = on;
            feedCamera.cullingMask = on ? 1 << OverlayLayer : m_SceneMask;
            feedCamera.clearFlags = on ? CameraClearFlags.SolidColor : m_SceneClear;
            feedCamera.backgroundColor = on ? closeUpBackground : m_SceneBackground;
        }

        // ----- Close-up: the optics on the way -------------------------------------------------------------------
        //
        // The close-up used to show only the ring (target) and the dot (landing point), so you could not see the beam
        // running off the edge of the next mirror on the way: unavoidable clipping incidents (playtest, 6 Nov 2026).
        // Now every optic between this mirror and the target gets a small gauge at the bottom of the picture: a circle
        // (the edge of that optic) with a dot where the beam hits it. White = fine, amber = near the edge, red = off.
        // (3D circles in the picture itself were tried first: the nearby mirror was a huge arc, half out of view.)

        private struct PathOptic
        {
            public Vector3 Centre;     // real world
            public Vector3 Right, Up;  // the optic's own surface axes (real world)
            public float Radius;
            public string Name;
        }

        private readonly System.Collections.Generic.List<PathOptic> m_PathOptics = new System.Collections.Generic.List<PathOptic>();
        private readonly System.Collections.Generic.List<PathOptic> m_CachedOptics = new System.Collections.Generic.List<PathOptic>();
        private readonly System.Collections.Generic.List<Vector3> m_OpticHits = new System.Collections.Generic.List<Vector3>();   // x, y: beam on the optic (-1..1 = inside), z: 1 = known
        private readonly System.Collections.Generic.List<RectTransform> m_Gauges = new System.Collections.Generic.List<RectTransform>();
        private Sprite m_RingSprite, m_DiscSprite;
        private Vector3 m_BeamDirection;

        [Header("Close-up: optics on the way")]
        [SerializeField] private Color opticColor = new Color(0.75f, 0.85f, 0.95f, 1f);
        [SerializeField] private Color opticWarnColor = new Color(1f, 0.6f, 0.1f, 1f);
        [SerializeField] private Color opticOffColor = new Color(1f, 0.2f, 0.15f, 1f);
        [Tooltip("The gauge turns amber when the beam is further out than this fraction of the optic's radius.")]
        [SerializeField, Range(0.3f, 1f)] private float opticWarnFraction = 0.7f;
        [Tooltip("Size of each optic gauge, as a fraction of the picture height.")]
        [SerializeField, Range(0.08f, 0.4f)] private float gaugeFraction = 0.2f;

        /// <summary>The optics at the junctions of the path (every bounce or pass between the mirror and the target).</summary>
        private void BuildPathOptics(System.Collections.Generic.List<int> path, System.Collections.Generic.List<PathOptic> optics)
        {
            optics.Clear();
            for (int k = 1; k < path.Count; k++)
            {
                Collider collider = m_Segments[path[k - 1]].HitCollider;
                if (collider == null || collider.GetComponentInParent<LaserTarget>() != null) continue;   // irises: the target ring covers it

                Transform surface = collider.transform;
                float radius = 0.05f;
                LaserMirror mirror = collider.GetComponentInParent<LaserMirror>();
                LaserBeamSplitter splitter = collider.GetComponentInParent<LaserBeamSplitter>();
                if (mirror != null) { surface = mirror.Surface; radius = mirror.MirrorRadius; }
                else if (splitter != null) { surface = splitter.Surface; radius = splitter.Radius; }

                MirrorMount mount = collider.GetComponentInParent<MirrorMount>();
                string name = mount != null ? mount.DisplayName : splitter != null ? "Beam splitter" : collider.name;
                optics.Add(new PathOptic { Centre = surface.position, Right = surface.right, Up = surface.up, Radius = radius, Name = name.ToUpperInvariant() });
            }
        }

        /// <summary>Where the real beam hits each optic of the path now, relative to its centre (radius = 1).</summary>
        private void UpdatePathOptics()
        {
            m_OpticHits.Clear();
            if (!m_FoldTrusted) return;
            for (int o = 0; o < m_PathOptics.Count; o++)
            {
                PathOptic optic = m_PathOptics[o];
                // The real beam piece that hits this optic: the strongest one ending within the optic's radius (+50 %)
                Vector3 best = Vector3.zero;
                bool found = false;
                float bestPower = 0f;
                for (int i = 0; i < m_Segments.Count; i++)
                {
                    LaserSegment s = m_Segments[i];
                    if (s.HitCollider == null || s.Power <= bestPower) continue;
                    Vector3 rel = s.End - optic.Centre;
                    if (rel.magnitude > optic.Radius * 1.5f + 0.02f) continue;
                    bestPower = s.Power;
                    best = new Vector3(Vector3.Dot(rel, optic.Right), Vector3.Dot(rel, optic.Up), 1f) / optic.Radius;
                    best.z = 1f;
                    found = true;
                }

                m_OpticHits.Add(found ? best : Vector3.zero);
            }
        }

        /// <summary>Gauges at the bottom of the shown picture (guide overlay): edge circle, beam dot, name.</summary>
        private void PlaceOpticGauges(bool on)
        {
            int count = on && m_FoldTrusted ? Mathf.Min(m_PathOptics.Count, m_OpticHits.Count) : 0;
            if (count > 0 && m_RingSprite == null)
            {
                m_RingSprite = MakeCircleSprite(true);
                m_DiscSprite = MakeCircleSprite(false);
            }

            while (m_Gauges.Count < count)
            {
                m_Gauges.Add(CreateGauge());
            }

            for (int i = 0; i < m_Gauges.Count; i++)
            {
                RectTransform gauge = m_Gauges[i];
                bool show = i < count;
                if (gauge.gameObject.activeSelf != show) gauge.gameObject.SetActive(show);
                if (!show) continue;

                Vector3 hit = m_OpticHits[i];
                float out01 = new Vector2(hit.x, hit.y).magnitude;
                Color colour = hit.z < 0.5f || out01 > 1f ? opticOffColor : out01 > opticWarnFraction ? opticWarnColor : opticColor;
                // Sized from the picture (the guide overlay has its own scale): about a fifth of the picture high
                float size = m_GuideRoot.rect.height * gaugeFraction;
                gauge.sizeDelta = new Vector2(size, size);
                ((RectTransform)gauge.GetChild(0)).sizeDelta = new Vector2(size, size);
                ((RectTransform)gauge.GetChild(1)).sizeDelta = new Vector2(size * 0.18f, size * 0.18f);
                var labelRect = (RectTransform)gauge.GetChild(2);
                labelRect.sizeDelta = new Vector2(size * 2.2f, size * 0.4f);
                gauge.anchoredPosition = new Vector2(size * 0.7f + i * size * 2.4f, size * 0.45f);

                var ring = gauge.GetChild(0).GetComponent<Image>();
                var dot = (RectTransform)gauge.GetChild(1);
                var label = gauge.GetChild(2).GetComponent<Text>();
                ring.color = colour;
                bool known = hit.z > 0.5f;
                dot.gameObject.SetActive(known);
                if (known)
                {
                    Vector2 p = Vector2.ClampMagnitude(new Vector2(hit.x, hit.y), 1.15f) * (size * 0.5f);
                    dot.anchoredPosition = p;
                    dot.GetComponent<Image>().color = colour;
                }

                string text = m_PathOptics[i].Name + (known ? "" : "\nMISSED");
                if (label.text != text) label.text = text;
                label.color = colour;
            }
        }

        private RectTransform CreateGauge()
        {
            var root = new GameObject("Optic Gauge", typeof(RectTransform));
            root.layer = m_GuideRoot.gameObject.layer;
            var rect = (RectTransform)root.transform;
            rect.SetParent(m_GuideRoot, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(100f, 100f);   // sized every frame in PlaceOpticGauges

            var back = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            back.layer = root.layer;
            back.transform.SetParent(rect, false);
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.5f);
            backRect.sizeDelta = new Vector2(100f, 100f);
            var ringImage = back.GetComponent<Image>();
            ringImage.sprite = m_RingSprite;
            ringImage.raycastTarget = false;

            var dot = new GameObject("Beam", typeof(RectTransform), typeof(Image));
            dot.layer = root.layer;
            dot.transform.SetParent(rect, false);
            var dotRect = (RectTransform)dot.transform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(18f, 18f);
            var dotImage = dot.GetComponent<Image>();
            dotImage.sprite = m_DiscSprite;
            dotImage.raycastTarget = false;

            Text label = GuideText("Name", m_GuideFont, 32, opticColor, true);
            label.transform.SetParent(rect, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            label.rectTransform.sizeDelta = new Vector2(220f, 40f);
            return rect;
        }

        private static Sprite MakeCircleSprite(bool ring)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = ring ? "Gauge Ring" : "Gauge Disc", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / (size * 0.5f);
                    float a = ring ? Mathf.Clamp01((1f - Mathf.Abs(d - 0.93f) / 0.05f)) : Mathf.Clamp01((1f - d) * 20f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private void SetOverlay(bool on)
        {
            m_Ring.enabled = on;
            m_BeamLine.enabled = on;
            m_Dot.enabled = on;
            m_CrossH.enabled = on && CurrentView.mode == ViewMode.AlongBeam;
            m_CrossV.enabled = m_CrossH.enabled;
        }

        private void SetHint(int key, string heightCm)
        {
            if (hintText == null || key == m_HintKey || !showHintText)
            {
                return;
            }

            m_HintKey = key;
            if (heightCm == "path")
            {
                hintText.text = m_GoalReached
                    ? "Cyan = the beam you steer. Ring = goal, dot = where it lands. For the last millimetres use VIEW: along beam."
                    : "The beam misses the goal. Steer it until it reaches the ring.";
                return;
            }

            hintText.text = heightCm == "none"
                ? "No beam path to an iris or detector yet. Steer the mirrors before it first."
                : (m_GoalReached ? "Dot = where the beam lands. Ring = goal." : "Beam misses the goal: steer the dot towards the ring.") + " Picture " + heightCm + " cm high.";
        }

        /// <summary>
        /// Follows the beam that leaves the mirror, through the optics after it, to this mirror's target, and unfolds that
        /// path (see above). <paramref name="goal"/> and <paramref name="landing"/> are in the unfolded space.
        ///
        /// Which target: beam walking pairs mirrors and irises in order. A mirror with k steerable mirrors right before
        /// it (no iris or beam splitter in between) aims at the (k+1)-th target after it: steering mirror 1 at iris A,
        /// steering mirror 2 at iris B. Targets the beam hits from the back (frontFaceOnly) do not count.
        ///
        /// When the beam does not reach the target (it falls off an optic on the way), the last path that did reach it is
        /// reused, so the ring stays where it was and the dot keeps moving the same way as the buttons. Only when the
        /// beam never reached the target, the target the beam heads for (smallest angle) is guessed.
        /// </summary>
        private bool TryTrace(Vector3 origin, Vector3 direction, out Vector3 goal, out Vector3 landing, out float tolerance, out bool reached)
        {
            goal = landing = Vector3.zero;
            tolerance = 0.01f;
            reached = false;
            m_GoalTarget = null;
            m_FoldTrusted = false;

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

            // The first piece: starts at the mirror, goes along the given direction
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

            int skip = CountSteeredMirrorsBefore(m_Segments[first].Start);
            m_Path.Clear();
            m_BestPath.Clear();
            m_Tried.Clear();
            m_Path.Add(first);
            reached = Follow(first, 0, skip, out LaserTarget target);
            System.Collections.Generic.List<int> path = reached ? m_Path : m_BestPath;
            LaserSegment last = m_Segments[path[path.Count - 1]];
            Vector3 lastDirection = SegmentDirection(last);

            Vector3 targetPoint;
            Vector3 landingPoint;
            bool useCache = false;
            if (reached)
            {
                targetPoint = target.Surface.position;
                landingPoint = last.End;
                BuildFold(path, m_Fold);
                BuildPathOptics(path, m_PathOptics);
                m_CachedFoldMirror = m_GuideMirror;
                m_CachedFoldTarget = target;
                m_CachedFold.Clear();
                m_CachedFold.AddRange(m_Fold);
                m_CachedOptics.Clear();
                m_CachedOptics.AddRange(m_PathOptics);
            }
            else
            {
                useCache = m_CachedFoldMirror == m_GuideMirror && m_GuideMirror != null && m_CachedFoldTarget != null && m_CachedFoldTarget.isActiveAndEnabled;
                target = useCache ? m_CachedFoldTarget : GuessTarget(last.Start, lastDirection);
                if (target == null)
                {
                    return false;
                }

                targetPoint = target.Surface.position;
                landingPoint = last.Start + lastDirection * Vector3.Dot(targetPoint - last.Start, lastDirection);   // the beam, extended to the target
                if (useCache)
                {
                    m_Fold.Clear();
                    m_Fold.AddRange(m_CachedFold);
                    m_PathOptics.Clear();
                    m_PathOptics.AddRange(m_CachedOptics);
                }
                else
                {
                    BuildFold(path, m_Fold);
                    m_PathOptics.Clear();   // guessed path: no optics shown
                }
            }

            m_GoalTarget = target;
            m_FoldTrusted = reached || useCache;
            tolerance = target.CenteringTolerance;

            // The real positions (for the Beam path view) and the box round the path up to the target
            m_RealGoal = targetPoint;
            m_RealLanding = landingPoint;
            m_RealAxis = lastDirection;
            m_PathBounds = new Bounds(targetPoint, Vector3.zero);
            m_PathBounds.Encapsulate(landingPoint);
            for (int k = 0; k < path.Count; k++)
            {
                m_PathBounds.Encapsulate(m_Segments[path[k]].Start);
                if (reached || k < path.Count - 1)
                {
                    m_PathBounds.Encapsulate(m_Segments[path[k]].End);   // not the end of a stray beam: that can be a far wall
                }
            }

            goal = ApplyFold(targetPoint, m_Fold);
            if (useCache)
            {
                // In the unfolded space the beam is one straight line from the mirror
                landing = origin + direction * Vector3.Dot(goal - origin, direction);
            }
            else
            {
                landing = ApplyFold(landingPoint, m_Fold);
            }

            return true;
        }

        private LaserTarget GuessTarget(Vector3 from, Vector3 direction)
        {
            if (Time.unscaledTime - m_TargetsTime > 2f)
            {
                m_TargetsTime = Time.unscaledTime;
                m_Targets = FindObjectsByType<LaserTarget>(FindObjectsSortMode.None);
            }

            float bestAngle = 60f;
            LaserTarget found = null;
            for (int i = 0; i < m_Targets.Length; i++)
            {
                if (m_Targets[i] == null || !m_Targets[i].isActiveAndEnabled)
                {
                    continue;
                }

                Vector3 to = m_Targets[i].Surface.position - from;
                float angle = Vector3.Angle(to, direction);
                if (to.magnitude > 0.3f && angle < bestAngle)
                {
                    bestAngle = angle;
                    found = m_Targets[i];
                }
            }

            return found;
        }

        /// <summary>
        /// How many mirrors of the control panel the beam bounced off right before reaching <paramref name="point"/>
        /// (walking back along the beam, stopping at anything else: an iris, a beam splitter, the laser).
        /// </summary>
        private int CountSteeredMirrorsBefore(Vector3 point)
        {
            MirrorControlPanel panel = controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;
            if (panel == null)
            {
                return 0;
            }

            int count = 0;
            Vector3 current = point;
            for (int guard = 0; guard < 6; guard++)
            {
                // The strongest piece of beam that ends where the current one starts
                int incoming = -1;
                float best = 0f;
                for (int i = 0; i < m_Segments.Count; i++)
                {
                    if (m_Segments[i].Power > best && (m_Segments[i].End - current).sqrMagnitude < 1e-4f)
                    {
                        best = m_Segments[i].Power;
                        incoming = i;
                    }
                }

                if (incoming < 0)
                {
                    break;
                }

                // Where that piece started: the end of an earlier piece, on some optic
                Vector3 start = m_Segments[incoming].Start;
                Collider optic = null;
                best = 0f;
                for (int i = 0; i < m_Segments.Count; i++)
                {
                    if (m_Segments[i].HitCollider != null && m_Segments[i].Power > best && (m_Segments[i].End - start).sqrMagnitude < 1e-4f)
                    {
                        best = m_Segments[i].Power;
                        optic = m_Segments[i].HitCollider;
                    }
                }

                if (optic == null || optic.GetComponentInParent<LaserTarget>() != null || optic.GetComponentInParent<LaserBeamSplitter>() != null)
                {
                    break;
                }

                MirrorMount mount = optic.GetComponentInParent<MirrorMount>();
                bool steered = false;
                for (int m = 0; m < panel.MirrorCount && mount != null; m++)
                {
                    steered |= panel.Mirrors[m] == mount;
                }

                if (!steered)
                {
                    break;
                }

                count++;
                current = start;
            }

            return count;
        }

        // The unfolding as a list of steps (applied last junction first): shift, then mirror in a plane
        private struct FoldStep
        {
            public Vector3 Shift;
            public Vector3 PlanePoint;
            public Vector3 PlaneNormal;   // zero = no reflection here (glass plate)
        }

        private readonly System.Collections.Generic.List<FoldStep> m_Fold = new System.Collections.Generic.List<FoldStep>();
        private readonly System.Collections.Generic.List<FoldStep> m_CachedFold = new System.Collections.Generic.List<FoldStep>();
        private MirrorMount m_CachedFoldMirror;
        private LaserTarget m_CachedFoldTarget;
        private LaserTarget m_GoalTarget;

        /// <summary>
        /// The steps that map a point after the last bounce back into the straight line of the first piece of beam: for
        /// every junction (last one first) remove the sideways shift of the next piece (glass plates shift the beam) and
        /// mirror the point in the optic's plane (only where the beam really changes direction).
        /// </summary>
        private void BuildFold(System.Collections.Generic.List<int> path, System.Collections.Generic.List<FoldStep> fold)
        {
            fold.Clear();
            for (int k = path.Count - 1; k >= 1; k--)
            {
                LaserSegment previous = m_Segments[path[k - 1]];
                LaserSegment current = m_Segments[path[k]];
                Vector3 before = SegmentDirection(previous);
                Vector3 after = SegmentDirection(current);
                fold.Add(new FoldStep
                {
                    Shift = current.Start - previous.End,
                    PlanePoint = previous.End,
                    PlaneNormal = Vector3.Angle(before, after) > 0.5f ? (after - before).normalized : Vector3.zero
                });
            }
        }

        private static Vector3 ApplyFold(Vector3 point, System.Collections.Generic.List<FoldStep> fold)
        {
            for (int i = 0; i < fold.Count; i++)
            {
                point -= fold[i].Shift;
                Vector3 normal = fold[i].PlaneNormal;
                if (normal != Vector3.zero)
                {
                    point -= 2f * Vector3.Dot(point - fold[i].PlanePoint, normal) * normal;
                }
            }

            return point;
        }

        private static Vector3 SegmentDirection(LaserSegment s) => (s.End - s.Start).normalized;

        /// <summary>
        /// True when <paramref name="next"/> is light that <paramref name="piece"/> produces at the optic it ends on.
        /// Mirrors and irises send their light on from the hit point itself. The beam splitter is a thick glass plate:
        /// its reflected and transmitted beams start millimetres away, so its own prediction is used (exact, so the
        /// other arm's beams, which leave the same splitter close by, are never mistaken for this one).
        /// </summary>
        private static bool Continues(LaserSegment piece, LaserSegment next)
        {
            const float sameSpot = 1e-6f;   // 1 mm, squared
            Vector3 inDirection = SegmentDirection(piece);
            Vector3 outDirection = SegmentDirection(next);
            LaserBeamSplitter splitter = piece.HitCollider != null ? piece.HitCollider.GetComponentInParent<LaserBeamSplitter>() : null;
            if (splitter != null)
            {
                splitter.PredictOutgoing(piece.End, inDirection, out Vector3 reflectedOrigin, out Vector3 reflectedDirection, out Vector3 transmittedOrigin);
                bool reflected = (next.Start - reflectedOrigin).sqrMagnitude < sameSpot && Vector3.Angle(outDirection, reflectedDirection) < 0.5f;
                bool transmitted = (next.Start - transmittedOrigin).sqrMagnitude < sameSpot && Vector3.Angle(outDirection, inDirection) < 0.5f;
                return reflected || transmitted;
            }

            return (next.Start - piece.End).sqrMagnitude < sameSpot;
        }

        /// <summary>
        /// A target counts only when the beam hits its front face. Beams coming back from the end mirrors pass the irises
        /// from behind on their way to the laser; those irises are not the goal of an end mirror (the detector is).
        /// </summary>
        private static bool Counts(LaserTarget target, LaserSegment segment) =>
            Vector3.Dot(SegmentDirection(segment), target.Surface.forward) < 0f;

        /// <summary>
        /// Depth-first: the beam pieces that start where this one ends, until one ends on a <see cref="LaserTarget"/>.
        /// The first <paramref name="skip"/> targets are passed (the beam goes on through an iris).
        /// </summary>
        private bool Follow(int index, int depth, int skip, out LaserTarget target)
        {
            LaserSegment segment = m_Segments[index];
            if (m_Path.Count > m_BestPath.Count)
            {
                m_BestPath.Clear();
                m_BestPath.AddRange(m_Path);   // the farthest the beam got, for when no target is reached
            }

            target = segment.HitCollider != null ? segment.HitCollider.GetComponentInParent<LaserTarget>() : null;
            if (target != null && Counts(target, segment))
            {
                if (skip == 0)
                {
                    return true;
                }

                skip--;
            }

            target = null;
            if (depth >= 12)
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
                    // Only light that this piece itself produces: both arms come back to the beam splitter, and near
                    // alignment their beams leave it millimetres apart, so "nearest start" followed the wrong arm.
                    // centimetres apart; the nearest start alone made the guide follow the other arm's beam)
                    if (segment.HitCollider == null || !Continues(segment, m_Segments[i]))
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
                if (Follow(next, depth + 1, skip, out target))
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
            // Stay inside the hall (a camera behind a wall or above the ceiling sees nothing useful)
            Vector3 position = Vector3.Max(cameraLimitMin, Vector3.Min(cameraLimitMax, centre + offset));
            Place(position, centre - position);
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

            if (hintText != null && !showHintText)
            {
                hintText.text = mirror == null ? "No mirror selected." : CurrentView.mode == ViewMode.Orbit ? "Press VIEW for another side." : "";
            }
            else if (hintText != null)
            {
                hintText.text = mirror == null ? "No mirror selected."
                    : CurrentView.mode == ViewMode.Orbit ? (hasBeam ? "Press VIEW to look at the mirror from another side." : "No beam on this mirror. Steer the mirrors before it first.")
                    : hasBeam ? "Looking along the reflected beam: put the next iris or mirror on the crosshair."
                    : "No beam on this mirror: looking straight out of it. Steer the mirrors before it first.";
            }
        }

        // ----- Read-only state for automated play tests (LaserPlaytestBot) -----------------------------------

        public Camera FeedCamera => feedCamera;
        public int ViewIndex => m_ViewIndex;
        public int ViewCount => views != null ? views.Length : 0;
        public ViewMode CurrentMode => CurrentView.mode;

        /// <summary>Switches straight to a view (same as pressing VIEW until it shows).</summary>
        public void SelectView(int index) => StepView(index - m_ViewIndex);

        /// <summary>True when the picture shows a ring (target) and a dot (landing point).</summary>
        public bool HasGoal => m_GuideValid;
        public bool GoalReached => m_GoalReached;
        public bool GoalCentred => m_GuideCentred;
        public Vector3 GoalPoint => m_GuideGoal;
        public Vector3 LandingPoint => m_GuideLanding;
        public float GoalTolerance => m_GoalTolerance;
        public string GoalTargetName => m_GoalTarget != null ? m_GoalTarget.name : "";
        public bool GoalInRing => m_GuideInRing;
        public bool PictureFlippedX => m_FlipX;
        public bool PictureFlippedY => m_FlipY;

        /// <summary>Distance between dot and ring centre, in metres, in the plane of the picture.</summary>
        public float GoalError
        {
            get
            {
                if (!m_GuideValid || feedCamera == null) return -1f;
                Vector3 delta = m_GuideGoal - m_GuideLanding;
                return Vector3.ProjectOnPlane(delta, feedCamera.transform.forward).magnitude;
            }
        }

        public string BannerLine => m_GuideRoot != null && m_GuideRoot.gameObject.activeInHierarchy && m_Banner != null
            ? m_Banner.text + " | " + m_BannerSub.text
            : "";

        /// <summary>The presses the on-picture guide asks for (+x = right arrow, +y = up arrow), unsmoothed.</summary>
        public bool TryGetSuggestedSteps(out Vector2 steps)
        {
            steps = Vector2.zero;
            return m_GuideValid && m_GuideSteps && TryComputeSteps(out steps);
        }
    }
}

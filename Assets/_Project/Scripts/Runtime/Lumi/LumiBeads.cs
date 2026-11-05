using UnityEngine;
using UnityEngine.Rendering;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Lumi's floating "bead" limbs: 13 small glowing dots, drawn as an effect (no extra objects).
    ///
    ///   Arms: 4 beads each, on an arc from the shoulder marker to the hand, following the hands in
    ///         every animation.
    ///   Neck: 2 beads in the gap between body and head, following the head (also when it looks around).
    ///   Tail: 3 shrinking beads under the body that trail behind a little and bob one after another.
    ///
    /// The dots use the team's EinsteinTelescope/LaserBeam shader in round-spot mode (Lumi_BeadGlow
    /// material), so they glow like the game's laser spots. All 13 are drawn in one instanced call,
    /// per camera, each turned to face that camera. Put this on the Lumi object; it finds the bones
    /// and the shoulder markers by name.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)] // after the Animator and LumiLookAt, so the beads use the final pose
    [DisallowMultipleComponent]
    public class LumiBeads : MonoBehaviour
    {
        [Tooltip("Lumi_BeadGlow (EinsteinTelescope/LaserBeam, Radial on). Found automatically in the Editor.")]
        [SerializeField] private Material beadMaterial;

        [Header("Look")]
        [Tooltip("Size of each glow relative to the bead. The glow fades out softly, so > 1 looks right.")]
        [SerializeField, Range(0.5f, 4f)] private float glowScale = 1.8f;
        [Tooltip("Gentle brightness pulse (0 = steady).")]
        [SerializeField, Range(0f, 0.5f)] private float pulse = 0.12f;
        [SerializeField, Range(0.1f, 3f)] private float pulseSpeed = 0.7f;

        [Header("Arms")]
        [Tooltip("How far the arm arc bulges upward (m).")]
        [SerializeField, Range(0f, 0.15f)] private float armArcUp = 0.05f;
        [Tooltip("How far the arm arc bulges outward (m).")]
        [SerializeField, Range(0f, 0.15f)] private float armArcOut = 0.03f;

        [Header("Tail")]
        [Tooltip("Seconds the first tail bead lags behind; each next bead lags a bit more.")]
        [SerializeField, Range(0f, 0.5f)] private float tailLag = 0.08f;
        [Tooltip("Up-and-down bob of the tail beads (m).")]
        [SerializeField, Range(0f, 0.02f)] private float tailBob = 0.004f;

        // Rest-pose layout (metres, in Lumi's own frame: +Y up, +Z forward), from the character sheet.
        private const float NeckBottomY = 0.235f, NeckTopY = 0.27f, TailTopY = 0.085f;
        private static readonly float[] ArmT = { 0.15f, 0.40f, 0.65f, 0.88f };
        private static readonly float[] ArmSize = { 0.018f, 0.016f, 0.014f, 0.012f };
        private static readonly float[] NeckT = { 0.30f, 0.72f };
        private static readonly float[] NeckSize = { 0.016f, 0.013f };
        private static readonly float[] TailDrop = { 0.021f, 0.051f, 0.076f };
        private static readonly float[] TailSize = { 0.026f, 0.018f, 0.011f };
        private const float PalmRadius = 0.022f; // the arm ends at the edge of the palm, not its centre
        private const int Count = 13;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Transform body, head, handL, handR, shoulderL, shoulderR;
        private Vector3 neckBottomInBody, neckTopInHead, tailTopInBody;
        private readonly Vector3[] tailPos = new Vector3[3];
        private readonly Vector3[] tailVelocity = new Vector3[3];
        private readonly Vector3[] positions = new Vector3[Count];
        private readonly float[] sizes = new float[Count];
        private readonly Matrix4x4[] matrices = new Matrix4x4[Count];
        private Mesh quad;
        private Material runtimeMaterial;
        private Color baseColor;
        private bool ready;

        private bool warned;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += DrawForCamera;
            TryInit();
        }

        /// <summary>Finds the bones and builds the glow. Retried each frame until it works (e.g. the material
        /// is filled in just after the component is added).</summary>
        private void TryInit()
        {
            if (ready) return;
            body = FindDeep("body");
            head = FindDeep("head");
            handL = FindDeep("hand_L");
            handR = FindDeep("hand_R");
            shoulderL = FindDeep("shoulder_L");
            shoulderR = FindDeep("shoulder_R");
            if (!(body && head && handL && handR && shoulderL && shoulderR && beadMaterial))
            {
                if (!warned)
                {
                    Debug.LogWarning("[Lumi] LumiBeads: missing a bone/marker (body, head, hand_L/R, shoulder_L/R) " +
                                     "or the bead material.", this);
                    warned = true;
                }
                return;
            }

            // Measured once, in the rest pose (before the Animator's first update), relative to the bones,
            // so the neck and tail beads move with the body and head afterwards.
            neckBottomInBody = body.InverseTransformPoint(transform.TransformPoint(new Vector3(0, NeckBottomY, 0)));
            neckTopInHead = head.InverseTransformPoint(transform.TransformPoint(new Vector3(0, NeckTopY, 0)));
            tailTopInBody = body.InverseTransformPoint(transform.TransformPoint(new Vector3(0, TailTopY, 0)));

            quad = BuildQuad();
            runtimeMaterial = new Material(beadMaterial) { enableInstancing = true, name = beadMaterial.name + " (Lumi)" };
            baseColor = beadMaterial.GetColor(BaseColorId);
            for (int i = 0; i < tailPos.Length; i++)
            {
                tailPos[i] = TailTarget(i, 0f);
                tailVelocity[i] = Vector3.zero;
            }

            ready = true;
            UpdateBeads(); // so the first frame already draws them in the right place
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= DrawForCamera;
            if (quad) DestroyAsset(quad);
            if (runtimeMaterial) DestroyAsset(runtimeMaterial);
            ready = false;
        }

        private void LateUpdate()
        {
            TryInit();
            if (ready) UpdateBeads();
        }

        private void UpdateBeads()
        {
            bool playing = Application.isPlaying;
            float time = playing ? Time.time : 0f;
            int n = 0;

            // Arms: a curve from the shoulder marker to the edge of the palm, bulging up and out.
            n = AddArm(n, shoulderL, handL);
            n = AddArm(n, shoulderR, handR);

            // Neck: between the top of the body and the bottom of the head.
            Vector3 neckBottom = body.TransformPoint(neckBottomInBody);
            Vector3 neckTop = head.TransformPoint(neckTopInHead);
            for (int i = 0; i < NeckT.Length; i++)
            {
                positions[n] = Vector3.Lerp(neckBottom, neckTop, NeckT[i]);
                sizes[n++] = NeckSize[i];
            }

            // Tail: each bead chases its spot under the body a little later than the one above it.
            for (int i = 0; i < tailPos.Length; i++)
            {
                Vector3 goal = TailTarget(i, time);
                tailPos[i] = playing && tailLag > 0f
                    ? Vector3.SmoothDamp(tailPos[i], goal, ref tailVelocity[i], tailLag * (i + 1))
                    : goal;
                positions[n] = tailPos[i];
                sizes[n++] = TailSize[i];
            }

            float glow = 1f + pulse * Mathf.Sin(time * pulseSpeed * 2f * Mathf.PI);
            runtimeMaterial.SetColor(BaseColorId, baseColor * glow);
        }

        private int AddArm(int n, Transform shoulder, Transform hand)
        {
            Vector3 start = shoulder.position;
            Vector3 end = hand.position + (start - hand.position).normalized * PalmRadius;
            Vector3 outward = Vector3.ProjectOnPlane(hand.position - transform.position, transform.up).normalized;
            Vector3 control = (start + end) * 0.5f + transform.up * armArcUp + outward * armArcOut;
            for (int i = 0; i < ArmT.Length; i++)
            {
                float t = ArmT[i];
                positions[n] = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control + t * t * end;
                sizes[n++] = ArmSize[i];
            }
            return n;
        }

        private Vector3 TailTarget(int i, float time)
        {
            Vector3 top = body.TransformPoint(tailTopInBody);
            float bob = tailBob * Mathf.Sin(time * 0.6f * 2f * Mathf.PI - i * 0.9f);
            return top - transform.up * (TailDrop[i] - bob);
        }

        private void DrawForCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!ready || runtimeMaterial == null) return;
            Vector3 camPos = cam.transform.position;
            for (int i = 0; i < Count; i++)
            {
                Quaternion faceCamera = Quaternion.LookRotation(positions[i] - camPos, cam.transform.up);
                matrices[i] = Matrix4x4.TRS(positions[i], faceCamera, Vector3.one * (sizes[i] * glowScale));
            }

            var rp = new RenderParams(runtimeMaterial)
            {
                camera = cam,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(transform.position + transform.up * 0.25f, Vector3.one * 1.5f),
            };
            Graphics.RenderMeshInstanced(rp, quad, 0, matrices);
        }

        private Transform FindDeep(string boneName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == boneName) return t;
            }
            return null;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "Lumi Bead Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyAsset(Object asset)
        {
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

#if UNITY_EDITOR
        // Fill in the bead material automatically when the component is added.
        private void Reset() => FindMaterial();
        private void OnValidate() => FindMaterial();

        private void FindMaterial()
        {
            if (beadMaterial != null) return;
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("Lumi_BeadGlow t:Material"))
            {
                beadMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (beadMaterial != null) return;
            }
        }
#endif
    }
}

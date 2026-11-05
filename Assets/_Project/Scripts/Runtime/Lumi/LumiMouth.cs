using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>The 16 cells of Lumi's mouth atlas (T_Lumi_MouthAtlas), frame 0 top-left, row by row.</summary>
    public enum LumiMouthFrame
    {
        Neutral = 0, Smile = 1, BigSmile = 2, Wavy = 3,
        O = 4, Flat = 5, Sad = 6, TalkClosed = 7,
        TalkSmall = 8, TalkOpen = 9, TalkWide = 10, Question = 11,
        Exclaim = 12, Heart = 13, Star = 14, Dots = 15,
    }

    /// <summary>
    /// Lumi's mouth screen: picks which mouth drawing the Lumi/Screen material shows.
    ///
    /// While Lumi speaks, the mouth follows the loudness of the voice (LumiVoice.Loudness) through four
    /// talk frames: lip sync. Without a voice it fakes a talking rhythm during the Talk mood.
    /// Otherwise the mouth matches the mood and the playing gesture (Think = thinking dots,
    /// Celebrate = big smile, Puzzled = wavy, ...). <see cref="Show"/> overrides it for a moment.
    ///
    /// Runs in LateUpdate, after the Animator. Sets the frame per renderer with a MaterialPropertyBlock,
    /// so the shared Lumi_Screen material asset is never changed. Put it on the Lumi object.
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiMouth : MonoBehaviour
    {
        [Tooltip("The head mesh with the screen material. Empty = found by name (\"Head\").")]
        [SerializeField] private Renderer head;

        [Header("Lip sync")]
        [Tooltip("Loudness (0..1) needed for each bigger talk frame: small, open, wide.")]
        [SerializeField] private Vector3 talkThresholds = new Vector3(0.08f, 0.25f, 0.5f);
        [Tooltip("Shortest time a talk frame stays (s), so the mouth doesn't flicker.")]
        [SerializeField, Range(0.02f, 0.2f)] private float minFrameTime = 0.06f;
        [Tooltip("Mouth movements per second when Lumi 'talks' without a voice.")]
        [SerializeField, Range(2f, 15f)] private float silentTalkRate = 7f;

        [Header("Idle")]
        [Tooltip("Lumi smiles a little every now and then while idle: random pause (min, max) in seconds.")]
        [SerializeField] private Vector2 idleSmileInterval = new Vector2(8f, 16f);
        [SerializeField, Range(0.5f, 4f)] private float idleSmileSeconds = 1.5f;

        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int WaveState = Animator.StringToHash("Wave");
        private static readonly int CelebrateState = Animator.StringToHash("Celebrate");
        private static readonly int PuzzledState = Animator.StringToHash("Puzzled");
        private static readonly int FocusZoomState = Animator.StringToHash("Focus_Zoom");
        private static readonly int PointState = Animator.StringToHash("Point");
        private static readonly int SadState = Animator.StringToHash("Sad");

        private Animator animator;
        private LumiVoice voice;
        private MaterialPropertyBlock block;
        private int screenIndex = -1, gestureLayer = -1;
        private LumiMouthFrame current = LumiMouthFrame.Neutral, overrideFrame;
        private float frameSince, overrideUntil = -1f, nextIdleSmile, idleSmileUntil = -1f;

        /// <summary>The mouth shown right now.</summary>
        public LumiMouthFrame Current => current;

        /// <summary>Show a particular mouth for a while (e.g. Heart or Star on a perfect level).</summary>
        public void Show(LumiMouthFrame frame, float seconds)
        {
            overrideFrame = frame;
            overrideUntil = Time.time + seconds;
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            voice = GetComponent<LumiVoice>();
            block = new MaterialPropertyBlock();
            if (head == null)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Head") head = r;
                }
            }
            if (head != null)
            {
                Material[] materials = head.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].HasProperty(FrameId)) screenIndex = i;
                }
            }
            if (screenIndex < 0)
            {
                Debug.LogWarning("[Lumi] LumiMouth: no Head renderer with the Lumi/Screen material (_Frame) found.", this);
            }
            if (animator != null) gestureLayer = animator.GetLayerIndex("Gestures");
            nextIdleSmile = Time.time + Random.Range(idleSmileInterval.x, idleSmileInterval.y);
        }

        private void LateUpdate()
        {
            if (screenIndex < 0) return;
            LumiMouthFrame wanted = Choose();
            bool talking = IsTalkFrame(wanted) && IsTalkFrame(current);
            // Talk frames change at most every minFrameTime; any other change shows at once.
            if (wanted != current && (!talking || Time.time - frameSince >= minFrameTime))
            {
                current = wanted;
                frameSince = Time.time;
            }

            head.GetPropertyBlock(block, screenIndex);
            block.SetFloat(FrameId, (int)current);
            head.SetPropertyBlock(block, screenIndex);
        }

        private LumiMouthFrame Choose()
        {
            if (Time.time < overrideUntil) return overrideFrame;

            // Lip sync while the voice plays.
            if (voice != null && voice.IsSpeaking) return TalkFrame(voice.Loudness);

            // A gesture on top of the mood decides the face.
            if (animator != null && gestureLayer >= 0 && animator.GetLayerWeight(gestureLayer) > 0.5f)
            {
                int gesture = animator.GetCurrentAnimatorStateInfo(gestureLayer).shortNameHash;
                if (gesture == CelebrateState) return LumiMouthFrame.BigSmile;
                if (gesture == WaveState || gesture == PointState) return LumiMouthFrame.Smile;
                if (gesture == PuzzledState) return LumiMouthFrame.Wavy;
                if (gesture == FocusZoomState) return LumiMouthFrame.O;
                if (gesture == SadState) return LumiMouthFrame.Sad;
            }

            var mode = animator != null && animator.runtimeAnimatorController != null
                ? (LumiMode)animator.GetInteger(LumiAnimatorParams.ModeId)
                : LumiMode.Idle;
            switch (mode)
            {
                case LumiMode.Listen: return LumiMouthFrame.Flat;
                case LumiMode.Think: return LumiMouthFrame.Dots;
                case LumiMode.Talk:
                    // No voice: a talking rhythm from smooth noise, so it doesn't look mechanical.
                    float fake = Mathf.PerlinNoise(Time.time * silentTalkRate, 0.37f);
                    return TalkFrame(fake * 0.7f);
                default:
                    return IdleFrame();
            }
        }

        private LumiMouthFrame IdleFrame()
        {
            if (Time.time >= nextIdleSmile)
            {
                idleSmileUntil = Time.time + idleSmileSeconds;
                nextIdleSmile = Time.time + Random.Range(idleSmileInterval.x, Mathf.Max(idleSmileInterval.x, idleSmileInterval.y));
            }
            return Time.time < idleSmileUntil ? LumiMouthFrame.Smile : LumiMouthFrame.Neutral;
        }

        private LumiMouthFrame TalkFrame(float loudness)
        {
            if (loudness >= talkThresholds.z) return LumiMouthFrame.TalkWide;
            if (loudness >= talkThresholds.y) return LumiMouthFrame.TalkOpen;
            if (loudness >= talkThresholds.x) return LumiMouthFrame.TalkSmall;
            return LumiMouthFrame.TalkClosed;
        }

        private static bool IsTalkFrame(LumiMouthFrame f) =>
            f == LumiMouthFrame.TalkClosed || f == LumiMouthFrame.TalkSmall || f == LumiMouthFrame.TalkOpen || f == LumiMouthFrame.TalkWide;
    }
}

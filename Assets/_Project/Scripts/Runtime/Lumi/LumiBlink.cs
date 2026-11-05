using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Lumi's eye: random camera-shutter blinks, and an aperture that follows Lumi's mood.
    ///
    /// Drives the Lens_Iris blendshapes Aperture_Close / Aperture_Open (0..100). It runs in LateUpdate,
    /// AFTER the Animator, because every exported clip also writes those blendshapes (always 0) and
    /// would otherwise undo this every frame.
    ///
    /// Aperture (-1..1): negative = narrowed (Aperture_Close), positive = wide (Aperture_Open), 0 = normal.
    /// With "Follow Mood" on, it is picked from the Animator: Think narrows, Listen opens a little,
    /// Focus_Zoom and Celebrate open wide. Scripts can also set <see cref="Aperture"/> or call <see cref="Blink"/>.
    /// Put it on the Lumi object.
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiBlink : MonoBehaviour
    {
        [Tooltip("The iris mesh with the Aperture blendshapes. Empty = found by name (\"Lens_Iris\").")]
        [SerializeField] private SkinnedMeshRenderer iris;

        [Header("Blinking")]
        [SerializeField] private bool autoBlink = true;
        [Tooltip("Random pause between blinks, in seconds (min, max).")]
        [SerializeField] private Vector2 blinkInterval = new Vector2(2.5f, 6f);
        [Tooltip("Chance that a blink is a quick double blink.")]
        [SerializeField, Range(0f, 1f)] private float doubleBlinkChance = 0.2f;
        [Tooltip("Seconds to snap shut / stay shut / open again (a camera shutter closes fast, opens slower).")]
        [SerializeField, Min(0.01f)] private float closeTime = 0.06f;
        [SerializeField, Min(0f)] private float holdTime = 0.04f;
        [SerializeField, Min(0.01f)] private float openTime = 0.10f;

        [Header("Aperture")]
        [Tooltip("Pick the aperture from the Animator (Mode and the playing gesture).")]
        [SerializeField] private bool followMood = true;
        [SerializeField, Range(-1f, 1f)] private float idleAperture = 0f;
        [SerializeField, Range(-1f, 1f)] private float listenAperture = 0.3f;
        [SerializeField, Range(-1f, 1f)] private float thinkAperture = -0.4f;
        [SerializeField, Range(-1f, 1f)] private float talkAperture = 0f;
        [SerializeField, Range(-1f, 1f)] private float excitedAperture = 0.8f; // Focus_Zoom, Celebrate
        [Tooltip("Seconds the aperture takes to settle on a new value.")]
        [SerializeField, Range(0.02f, 1f)] private float apertureSmoothTime = 0.2f;

        private const string CloseShape = "Aperture_Close";
        private const string OpenShape = "Aperture_Open";
        private static readonly int FocusZoomState = Animator.StringToHash("Focus_Zoom");
        private static readonly int CelebrateState = Animator.StringToHash("Celebrate");

        private Animator animator;
        private int closeIndex = -1, openIndex = -1, gestureLayer = -1;
        private float aperture, apertureVelocity, manualAperture;
        private float nextBlinkAt, blinkStartedAt = -1f;
        private bool doubleBlinkPending;

        /// <summary>The aperture Lumi settles on when Follow Mood is off (-1 narrowed .. 0 normal .. 1 wide).</summary>
        public float Aperture
        {
            get => manualAperture;
            set => manualAperture = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>Blink once, now.</summary>
        public void Blink()
        {
            blinkStartedAt = Time.time;
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (iris == null)
            {
                foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.name == "Lens_Iris") iris = r;
                }
            }
            if (iris != null && iris.sharedMesh != null)
            {
                closeIndex = iris.sharedMesh.GetBlendShapeIndex(CloseShape);
                openIndex = iris.sharedMesh.GetBlendShapeIndex(OpenShape);
            }
            if (closeIndex < 0 || openIndex < 0)
            {
                Debug.LogWarning("[Lumi] LumiBlink: no Lens_Iris with Aperture_Close / Aperture_Open blendshapes found.", this);
            }
            if (animator != null) gestureLayer = animator.GetLayerIndex("Gestures");
            ScheduleNextBlink();
        }

        private void LateUpdate()
        {
            if (closeIndex < 0 || openIndex < 0) return;

            // 1. The resting aperture for the current mood, eased.
            float goal = followMood ? MoodAperture() : manualAperture;
            aperture = Mathf.SmoothDamp(aperture, goal, ref apertureVelocity, apertureSmoothTime);

            // 2. Blinks (0 = open, 1 = shut), layered over the aperture.
            if (autoBlink && blinkStartedAt < 0f && Time.time >= nextBlinkAt)
            {
                Blink();
                doubleBlinkPending = Random.value < doubleBlinkChance;
            }
            float shut = BlinkAmount();

            // 3. Write the blendshapes. A blink pushes the shutter fully closed whatever the aperture.
            float close = Mathf.Max(0f, -aperture);
            float open = Mathf.Max(0f, aperture);
            iris.SetBlendShapeWeight(closeIndex, Mathf.Lerp(close, 1f, shut) * 100f);
            iris.SetBlendShapeWeight(openIndex, Mathf.Lerp(open, 0f, shut) * 100f);
        }

        private float BlinkAmount()
        {
            if (blinkStartedAt < 0f) return 0f;
            float t = Time.time - blinkStartedAt;
            if (t < 0f) return 0f; // waiting for the second half of a double blink
            if (t < closeTime) return t / closeTime;
            t -= closeTime;
            if (t < holdTime) return 1f;
            t -= holdTime;
            if (t < openTime) return 1f - Mathf.SmoothStep(0f, 1f, t / openTime);

            // Blink finished: maybe a quick second one, otherwise wait for the next.
            if (doubleBlinkPending)
            {
                doubleBlinkPending = false;
                blinkStartedAt = Time.time + 0.08f; // a short pause, then blink again
                return 0f;
            }
            blinkStartedAt = -1f;
            ScheduleNextBlink();
            return 0f;
        }

        private float MoodAperture()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return idleAperture;

            if (gestureLayer >= 0 && animator.GetLayerWeight(gestureLayer) > 0.5f)
            {
                int gesture = animator.GetCurrentAnimatorStateInfo(gestureLayer).shortNameHash;
                if (gesture == FocusZoomState || gesture == CelebrateState) return excitedAperture;
            }

            switch ((LumiMode)animator.GetInteger(LumiAnimatorParams.ModeId))
            {
                case LumiMode.Listen: return listenAperture;
                case LumiMode.Think: return thinkAperture;
                case LumiMode.Talk: return talkAperture;
                default: return idleAperture;
            }
        }

        private void ScheduleNextBlink()
        {
            nextBlinkAt = Time.time + Random.Range(blinkInterval.x, Mathf.Max(blinkInterval.x, blinkInterval.y));
        }
    }
}

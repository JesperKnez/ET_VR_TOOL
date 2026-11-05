using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Makes a stationary Lumi pay attention to the player.
    ///
    /// Head: turns smoothly towards the target within natural limits, ON TOP of whatever clip the
    /// Animator plays (it runs in LateUpdate, after the Animator), so Lumi keeps bobbing, thinking or
    /// waving while it looks at you.
    /// Body: the whole of Lumi turns on the spot only when the target gets too far to the side for
    /// the head (like a creature: turn, settle, stop), instead of constantly tracking.
    ///
    /// Put it on the Lumi object (the one with the Animator). The head and lens bones are found by name.
    /// The target defaults to the main camera, which is the player's head in VR.
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiLookAt : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What Lumi looks at. Empty = the main camera (the player's head in VR).")]
        [SerializeField] private Transform target;

        [Header("Head")]
        [Tooltip("The head bone. Empty = found by name (\"head\").")]
        [SerializeField] private Transform head;
        [Tooltip("Where Lumi 'sees' from. Empty = the \"lens\" bone, else the head.")]
        [SerializeField] private Transform eye;
        [SerializeField, Range(0f, 80f)] private float maxHeadYaw = 40f;
        [SerializeField, Range(0f, 60f)] private float maxHeadUp = 25f;
        [SerializeField, Range(0f, 60f)] private float maxHeadDown = 20f;
        [Tooltip("Seconds the head takes to catch up. Higher = calmer, lower = snappier.")]
        [SerializeField, Range(0.02f, 1f)] private float headSmoothTime = 0.25f;

        [Header("Whole Body")]
        [Tooltip("Turn all of Lumi on the spot when the target goes too far to the side.")]
        [SerializeField] private bool turnBody = true;
        [Tooltip("Start turning when the target is this many degrees to the side.")]
        [SerializeField, Range(0f, 120f)] private float startTurningAt = 35f;
        [Tooltip("Stop turning once facing the target within this many degrees.")]
        [SerializeField, Range(0f, 30f)] private float stopTurningAt = 5f;
        [SerializeField, Range(10f, 360f)] private float maxTurnSpeed = 90f;
        [Tooltip("Seconds a turn roughly takes to settle.")]
        [SerializeField, Range(0.05f, 2f)] private float turnSmoothTime = 0.5f;

        [Header("Blend")]
        [Tooltip("0 = the animation alone, 1 = fully looking at the target.")]
        [SerializeField, Range(0f, 1f)] private float weight = 1f;

        private float headYaw, headPitch, headYawVelocity, headPitchVelocity;
        private float turnVelocity;
        private bool turning;

        /// <summary>What Lumi looks at. Set null to go back to the main camera.</summary>
        public Transform Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>How much the look is applied on top of the animation (0..1), e.g. lower it while pointing.</summary>
        public float Weight
        {
            get => weight;
            set => weight = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            if (head == null) head = FindDeep(transform, "head");
            if (eye == null) eye = FindDeep(transform, "lens");
            if (eye == null) eye = head;
            if (head == null) Debug.LogWarning("[Lumi] LumiLookAt: no bone called \"head\" found.", this);
        }

        private void Update()
        {
            Transform t = CurrentTarget();
            if (!turnBody || t == null) return;

            // Horizontal angle between where Lumi faces and where the target is.
            Vector3 toTarget = Vector3.ProjectOnPlane(t.position - transform.position, Vector3.up);
            if (toTarget.sqrMagnitude < 0.0001f) return;
            float currentYaw = transform.eulerAngles.y;
            float targetYaw = Quaternion.LookRotation(toTarget, Vector3.up).eulerAngles.y;
            float offset = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));

            if (!turning && offset > startTurningAt) turning = true;
            if (turning && offset < stopTurningAt) turning = false;

            // Keep easing even after "stop", so the turn settles instead of halting abruptly.
            if (turning || Mathf.Abs(turnVelocity) > 1f)
            {
                float goal = turning ? targetYaw : currentYaw;
                float yaw = Mathf.SmoothDampAngle(currentYaw, goal, ref turnVelocity, turnSmoothTime, maxTurnSpeed);
                transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up);
            }
        }

        private void LateUpdate()
        {
            Transform t = CurrentTarget();
            if (head == null) return;

            float goalYaw = 0f, goalPitch = 0f;
            if (t != null && weight > 0f)
            {
                // Direction to the target in Lumi's own frame: +Z forward, +Y up, +X to Lumi's right.
                Vector3 local = transform.InverseTransformDirection(t.position - eye.position);
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                bool behind = Mathf.Abs(yaw) > 120f; // don't strain over the shoulder: relax instead
                goalYaw = behind ? 0f : Mathf.Clamp(yaw, -maxHeadYaw, maxHeadYaw) * weight;
                goalPitch = behind ? 0f : Mathf.Clamp(pitch, -maxHeadDown, maxHeadUp) * weight;
            }

            headYaw = Mathf.SmoothDampAngle(headYaw, goalYaw, ref headYawVelocity, headSmoothTime);
            headPitch = Mathf.SmoothDampAngle(headPitch, goalPitch, ref headPitchVelocity, headSmoothTime);

            // Add the look on top of the animated head rotation (around the head's own pivot).
            // Unity: a positive rotation around "right" tips the face down, so looking up is negative.
            Quaternion look = Quaternion.AngleAxis(headYaw, transform.up) * Quaternion.AngleAxis(-headPitch, transform.right);
            head.rotation = look * head.rotation;
        }

        private Transform CurrentTarget()
        {
            if (target != null) return target;
            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            Transform t = target != null ? target : (Camera.main != null ? Camera.main.transform : null);
            Transform from = eye != null ? eye : transform;
            if (t == null) return;
            Gizmos.color = new Color(0.36f, 0.88f, 0.77f); // Lumi's glow colour
            Gizmos.DrawLine(from.position, t.position);
        }
    }
}

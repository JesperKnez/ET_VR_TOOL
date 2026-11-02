using System;
using System.Collections.Generic;
using UnityEngine;

namespace ETVRTool.CraneLevel
{
    public enum PipeState { Stored, Carried, Placed, Installed }

    /// <summary>
    /// A stainless beam-pipe section with a flange on each end, pre-rigged with two slings
    /// and a master link (the lifting point the crane hook picks up).
    /// The pivot is the centre of the pipe axis; the pipe's length runs along local X.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PipeSection : MonoBehaviour
    {
        public static readonly List<PipeSection> All = new List<PipeSection>();

        public string pipeId = "P1";
        public float massKg = 240f;
        public float length = 2.5f;
        public float tubeRadius = 0.3f;
        public float flangeRadius = 0.475f;

        [Tooltip("Master link at the top of the slings. The hook attaches here.")]
        public Transform liftPoint;
        [Tooltip("Bolts that appear once the pipe is installed.")]
        public GameObject bolts;

        public PipeState State { get; private set; } = PipeState.Stored;

        /// <summary>Raised when the carried pipe touches an obstacle (tower, other pipe, wall...).</summary>
        public event Action<PipeSection, Collider> Bumped;

        readonly Dictionary<Collider, float> _lastBump = new Dictionary<Collider, float>();

        void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;   // the crane moves it, not physics
            rb.useGravity = false;
            if (bolts != null) bolts.SetActive(false);
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void AttachTo(Transform hookPoint)
        {
            transform.SetParent(null, true);
            transform.position += hookPoint.position - liftPoint.position;
            transform.SetParent(hookPoint, true);
            State = PipeState.Carried;
        }

        /// <summary>
        /// Puts the pipe down if it is (almost) resting on something: the floor or a support.
        /// Returns false if the load is still hanging too high to release safely.
        /// </summary>
        public bool TrySetDown(float maxGap = 0.15f)
        {
            float bestGap = float.MaxValue;
            Vector3 axis = transform.right;
            float half = length * 0.5f;

            // Sample the bottom of the tube every ~8 cm (supports are narrow), plus the bottom of both flanges.
            int samples = Mathf.Max(4, Mathf.CeilToInt(length / 0.08f));
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(-half + 0.03f, half - 0.03f, i / (float)samples);
                bool atFlange = Mathf.Abs(t) > half - 0.07f;
                float drop = atFlange ? flangeRadius : tubeRadius;
                bestGap = Mathf.Min(bestGap, GapBelow(transform.position + axis * t, drop, maxGap));
            }

            if (bestGap > maxGap) return false;

            // Rest on the highest surface (a negative gap means the pipe was lowered slightly into it).
            transform.SetParent(null, true);
            transform.position += Vector3.down * bestGap;
            State = PipeState.Placed;
            return true;
        }

        /// <summary>Distance from the pipe's underside (axisPoint - drop) down to the first surface below.</summary>
        float GapBelow(Vector3 axisPoint, float drop, float maxGap)
        {
            // Start the ray inside the pipe so it still finds a surface the pipe has sunk into.
            var hits = Physics.RaycastAll(axisPoint, Vector3.down, drop + maxGap + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                best = Mathf.Min(best, h.distance - drop);
            }
            return best;
        }

        public void Install(Vector3 position, Quaternion rotation)
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            State = PipeState.Installed;
            if (bolts != null) bolts.SetActive(true);
        }

        void OnTriggerEnter(Collider other)
        {
            if (State != PipeState.Carried) return;
            if (other.transform.IsChildOf(transform)) return;

            bool isObstacle = other.GetComponentInParent<CraneObstacle>() != null;
            var otherPipe = other.GetComponentInParent<PipeSection>();
            if (otherPipe != null && otherPipe != this) isObstacle = true;
            if (!isObstacle) return;

            // Don't count the same contact more than once per second.
            if (_lastBump.TryGetValue(other, out float t) && Time.time - t < 1f) return;
            _lastBump[other] = Time.time;
            Bumped?.Invoke(this, other);
        }
    }
}

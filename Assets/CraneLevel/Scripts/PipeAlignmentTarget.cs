using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ETVRTool.CraneLevel
{
    /// <summary>
    /// The place where a pipe has to go: between two flanges (tower port / other pipe).
    /// Shows a see-through "ghost" pipe that turns blue -> yellow -> green as the carried pipe
    /// gets closer, plus a readout with the remaining offset. Release the hook when green.
    /// </summary>
    public class PipeAlignmentTarget : MonoBehaviour
    {
        public static readonly List<PipeAlignmentTarget> All = new List<PipeAlignmentTarget>();

        [Tooltip("Only this pipe fits here. Empty = any pipe.")]
        public string acceptsPipeId = "P1";
        public CraneHook hook;

        [Header("Tolerances")]
        [Tooltip("Max distance (m) between pipe centre and target centre.")]
        public float positionTolerance = 0.06f;
        [Tooltip("Max yaw difference (degrees).")]
        public float angleTolerance = 3f;
        [Tooltip("A pipe with a flange on both ends fits both ways round.")]
        public bool symmetric = true;
        [Tooltip("Distance where the ghost starts turning yellow.")]
        public float closeDistance = 0.5f;

        [Header("Visuals")]
        public Renderer[] ghostRenderers;
        public Text readout;
        public Color farColor = new Color(0.3f, 0.65f, 1f, 0.25f);
        public Color closeColor = new Color(1f, 0.85f, 0.2f, 0.35f);
        public Color alignedColor = new Color(0.2f, 1f, 0.35f, 0.5f);

        public bool IsActive { get; private set; }
        public bool Installed { get; private set; }
        /// <summary>Latest human-readable offset, e.g. for the instruction board.</summary>
        public string ReadoutText { get; private set; } = "";

        public event Action<PipeAlignmentTarget, PipeSection> Completed;

        MaterialPropertyBlock _block;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void SetActive(bool active)
        {
            IsActive = active && !Installed;
            foreach (var r in ghostRenderers) if (r != null) r.enabled = IsActive;
            if (readout != null) readout.gameObject.SetActive(IsActive);
        }

        public bool Accepts(PipeSection pipe) =>
            pipe != null && IsActive && !Installed &&
            (string.IsNullOrEmpty(acceptsPipeId) || pipe.pipeId == acceptsPipeId);

        public float PositionError(PipeSection pipe) => Vector3.Distance(pipe.transform.position, transform.position);

        public float AngleError(PipeSection pipe)
        {
            float a = Mathf.Abs(Mathf.DeltaAngle(pipe.transform.eulerAngles.y, transform.eulerAngles.y));
            return symmetric ? Mathf.Min(a, 180f - a) : a;
        }

        public bool IsAligned(PipeSection pipe) =>
            Accepts(pipe) && PositionError(pipe) <= positionTolerance && AngleError(pipe) <= angleTolerance;

        public static PipeAlignmentTarget FindTargetFor(PipeSection pipe)
        {
            PipeAlignmentTarget best = null;
            float bestD = float.MaxValue;
            foreach (var t in All)
            {
                if (!t.Accepts(pipe)) continue;
                float d = t.PositionError(pipe);
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        public void Install(PipeSection pipe)
        {
            Quaternion rot = transform.rotation;
            float raw = Mathf.Abs(Mathf.DeltaAngle(pipe.transform.eulerAngles.y, transform.eulerAngles.y));
            if (symmetric && raw > 90f) rot *= Quaternion.Euler(0f, 180f, 0f); // keep the way it was turned

            pipe.Install(transform.position, rot);
            Installed = true;
            SetActive(false);
            Completed?.Invoke(this, pipe);
        }

        void Update()
        {
            if (!IsActive) return;

            var load = hook != null ? hook.Load : null;
            Color c = farColor;

            if (Accepts(load))
            {
                Vector3 d = load.transform.position - transform.position;
                float side = new Vector2(d.x, d.z).magnitude;
                float angle = AngleError(load);
                ReadoutText = $"Side {side:0.00} m   Height {d.y:+0.00;-0.00;0.00} m   Angle {angle:0.0}°";

                if (IsAligned(load)) { c = alignedColor; ReadoutText += "\nALIGNED - release the hook"; }
                else if (d.magnitude < closeDistance) c = closeColor;
            }
            else
            {
                ReadoutText = load == null ? "Pick up the matching pipe" : "This is not the right pipe for this spot";
            }

            if (readout != null) readout.text = ReadoutText;
            ApplyColor(c);
        }

        void ApplyColor(Color c)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (var r in ghostRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c); // URP
                _block.SetColor("_Color", c);     // Built-in
                r.SetPropertyBlock(_block);
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace ETVRTool.CraneLevel
{
    /// <summary>
    /// The hook on the hook block. Picks up a pipe by its master link and releases it
    /// either into an alignment target (installed) or onto the floor/support (set down).
    /// Also drives the red load display on the crane (like the "0.005t" one in the cleanroom).
    /// </summary>
    public class CraneHook : MonoBehaviour
    {
        public BridgeCrane crane;
        [Tooltip("Bottom of the hook: the master link of the load hangs here.")]
        public Transform attachPoint;
        public float grabRadius = 0.45f;

        [Header("Load display")]
        public Text loadDisplay;
        [Tooltip("Weight of the empty hook block, shown on the display (photo shows 0.005t).")]
        public float hookMassKg = 5f;

        public PipeSection Load { get; private set; }

        public event Action<PipeSection> LoadAttached;
        public event Action<PipeSection> LoadReleased;
        /// <summary>Feedback for the player, e.g. "Hook is not close enough".</summary>
        public event Action<string> Message;

        public void ToggleAttach()
        {
            if (Load == null) Attach();
            else Release();
        }

        public void Attach()
        {
            if (Load != null) { Message?.Invoke("The hook is already carrying a load."); return; }
            if (crane != null && crane.EmergencyStopped) { Message?.Invoke("Emergency stop is active. Press RESET first."); return; }

            PipeSection best = null;
            float bestDistance = grabRadius;
            foreach (var pipe in PipeSection.All)
            {
                if (pipe.State == PipeState.Installed || pipe.liftPoint == null) continue;
                float d = Vector3.Distance(pipe.liftPoint.position, attachPoint.position);
                if (d < bestDistance) { bestDistance = d; best = pipe; }
            }

            if (best == null)
            {
                Message?.Invoke("Hook not close enough to a lifting ring. Lower the hook onto the yellow master link.");
                return;
            }

            best.AttachTo(attachPoint);
            Load = best;
            LoadAttached?.Invoke(best);
        }

        public void Release()
        {
            if (Load == null) { Message?.Invoke("There is no load on the hook."); return; }

            var target = PipeAlignmentTarget.FindTargetFor(Load);
            if (target != null && target.IsAligned(Load))
            {
                var pipe = Load;
                Load = null;
                target.Install(pipe);
                LoadReleased?.Invoke(pipe);
                return;
            }

            if (Load.TrySetDown())
            {
                var pipe = Load;
                Load = null;
                LoadReleased?.Invoke(pipe);
                Message?.Invoke(target != null
                    ? "Pipe set down, but not in its connection position."
                    : "Pipe set down.");
                return;
            }

            Message?.Invoke("Never release a suspended load! Lower it onto a support or the floor first.");
        }

        void Update()
        {
            if (loadDisplay != null)
            {
                float kg = hookMassKg + (Load != null ? Load.massKg : 0f);
                loadDisplay.text = (kg / 1000f).ToString("0.000") + "t";
            }
        }
    }
}

using UnityEngine;

namespace ETVRTool.CraneLevel
{
    /// <summary>
    /// Indicator lamps on the control cabinet: power (green), creep speed (amber),
    /// emergency stop (red) and "load on hook" (white).
    /// </summary>
    public class CranePanelLamps : MonoBehaviour
    {
        public BridgeCrane crane;
        public CraneHook hook;
        public Renderer powerLamp, creepLamp, stopLamp, loadLamp;

        static readonly Color Off = new Color(0.15f, 0.15f, 0.15f);
        MaterialPropertyBlock _block;

        void Update()
        {
            bool stopped = crane != null && crane.EmergencyStopped;
            bool blink = Mathf.Repeat(Time.time, 0.8f) < 0.4f;

            Set(powerLamp, !stopped, new Color(0.1f, 1f, 0.3f));
            Set(creepLamp, crane != null && crane.CreepMode, new Color(1f, 0.6f, 0.05f));
            Set(stopLamp, stopped && blink, new Color(1f, 0.05f, 0.05f));
            Set(loadLamp, hook != null && hook.Load != null, Color.white);
        }

        void Set(Renderer r, bool on, Color color)
        {
            if (r == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            Color c = on ? color : Off;
            _block.SetColor("_BaseColor", c);
            _block.SetColor("_Color", c);
            _block.SetColor("_EmissionColor", on ? color * 2.5f : Color.black);
            r.SetPropertyBlock(_block);
        }
    }
}

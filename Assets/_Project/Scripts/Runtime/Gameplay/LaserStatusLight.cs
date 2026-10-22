using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Laser emission indicator. Real lasers carry a warning light that is on whenever the beam can come
    /// out; it sits on the back and top of the head so the operator sees it from behind.
    /// Shutter open = glowing red (gently pulsing), shutter closed = green.
    /// Infrared lasers also show it, which matters because their beam itself is invisible.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic)]
    public class LaserStatusLight : MonoBehaviour
    {
        [Tooltip("Empty = the LaserEmitter on this object or a parent.")]
        [SerializeField] private LaserEmitter laser = null;

        [Tooltip("The glowing parts (unlit material): beacon, rear LED...")]
        [SerializeField] private Renderer[] indicators = new Renderer[0];

        [Tooltip("Optional real light that tints the surroundings in the same colour.")]
        [SerializeField] private Light glowLight = null;

        [SerializeField] private Color emittingColor = new Color(1f, 0.12f, 0.05f, 1f);
        [SerializeField] private Color safeColor = new Color(0.15f, 0.85f, 0.3f, 1f);

        [Tooltip("Pulses per second while emitting. 0 = steady.")]
        [SerializeField, Min(0f)] private float pulseFrequency = 1f;

        [Tooltip("Intensity of the optional light while emitting.")]
        [SerializeField, Min(0f)] private float lightIntensity = 0.6f;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock m_PropertyBlock;

        private LaserEmitter Laser
        {
            get
            {
                if (laser == null)
                {
                    laser = GetComponentInParent<LaserEmitter>();
                }

                return laser;
            }
        }

        /// <summary>True when the light shows "laser on".</summary>
        public bool ShowsEmitting { get; private set; }

        private void LateUpdate()
        {
            LaserEmitter emitter = Laser;
            bool emitting = emitter != null && emitter.isActiveAndEnabled && emitter.IsShutterOpen && emitter.OutputPower > 0f;
            ShowsEmitting = emitting;

            float pulse = emitting && pulseFrequency > 0f ? 0.75f + 0.25f * Mathf.Cos(2f * Mathf.PI * pulseFrequency * Time.time) : 1f;
            Color color = (emitting ? emittingColor : safeColor) * pulse;
            color.a = 1f;

            m_PropertyBlock ??= new MaterialPropertyBlock();
            foreach (Renderer indicator in indicators)
            {
                if (indicator == null)
                {
                    continue;
                }

                indicator.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor(s_BaseColorId, color);
                m_PropertyBlock.SetColor(s_ColorId, color);
                indicator.SetPropertyBlock(m_PropertyBlock);
            }

            if (glowLight != null)
            {
                glowLight.color = emitting ? emittingColor : safeColor;
                glowLight.intensity = (emitting ? lightIntensity : lightIntensity * 0.3f) * pulse;
            }
        }
    }
}

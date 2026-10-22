using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Safely absorbs a beam. Real detectors catch every unwanted beam in a beam dump
    /// (Advanced LIGO uses black glass, and silicon carbide for high power) so the light cannot
    /// scatter back into the interferometer. Light ending here is NOT counted as stray light.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.Receiver)]
    public class LaserBeamDump : MonoBehaviour, ILaserInteractable
    {
        private int m_AccumulatedFrame = -1;
        private float m_AbsorbedPower;
        private float m_LastAbsorbedPower;

        /// <summary>Power (watts) absorbed during the last frame.</summary>
        public float AbsorbedPower => m_LastAbsorbedPower;

        public void OnLaserHit(in LaserHit hit, LaserBeamQueue beams)
        {
            if (hit.IsPreview)
            {
                return;
            }

            if (m_AccumulatedFrame != Time.frameCount)
            {
                m_AccumulatedFrame = Time.frameCount;
                m_AbsorbedPower = 0f;
            }

            m_AbsorbedPower += hit.Power;
        }

        private void LateUpdate()
        {
            m_LastAbsorbedPower = m_AccumulatedFrame == Time.frameCount ? m_AbsorbedPower : 0f;
        }
    }
}

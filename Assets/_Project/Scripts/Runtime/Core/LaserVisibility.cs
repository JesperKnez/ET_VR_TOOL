using System;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Decides whether beams are drawn. Real interferometer lasers (1064 nm, 1550 nm) are infrared and
    /// invisible; technicians use an IR viewer or IR cards to see them. Visible beams (e.g. the
    /// 532 nm green alignment laser) are always drawn.
    /// </summary>
    public static class LaserVisibility
    {
        /// <summary>Raised when the infrared viewer is switched on or off.</summary>
        public static event Action<bool> InfraredViewerChanged;

        /// <summary>True while the player "looks through" an IR viewer.</summary>
        public static bool InfraredViewerActive { get; private set; }

        public static void SetInfraredViewer(bool active)
        {
            if (InfraredViewerActive == active)
            {
                return;
            }

            InfraredViewerActive = active;
            InfraredViewerChanged?.Invoke(active);
        }

        /// <summary>Should a beam of this wavelength be drawn right now?</summary>
        public static bool ShouldRender(float wavelengthNm)
        {
            return LaserOptics.IsHumanVisible(wavelengthNm) || InfraredViewerActive;
        }

        // Static state survives "Enter Play Mode without domain reload", so reset it explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            InfraredViewerActive = false;
            InfraredViewerChanged = null;
        }
    }
}

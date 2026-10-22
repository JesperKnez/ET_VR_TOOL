using System;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Tiny hub for "a button was pressed" so sound and haptics can react to every kit button
    /// (control pad, briefing screen) without the buttons knowing about audio.
    /// Your own buttons can call <see cref="NotifyPressed"/> too.
    /// </summary>
    public static class LaserUiFeedback
    {
        public static event Action Pressed;

        public static void NotifyPressed() => Pressed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Pressed = null;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Controller vibration through Unity's XR input devices (works with OpenXR on Quest and PC VR).
    /// Without a headset (XR Device Simulator, desktop) it simply does nothing.
    /// </summary>
    public static class LaserHaptics
    {
        private static readonly List<InputDevice> s_Devices = new List<InputDevice>();

        /// <summary>Vibrate the controllers. Amplitude 0..1, duration in seconds.</summary>
        public static void Pulse(float amplitude, float duration, bool left = true, bool right = true)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            if (left) PulseNode(XRNode.LeftHand, amplitude, duration);
            if (right) PulseNode(XRNode.RightHand, amplitude, duration);
        }

        private static void PulseNode(XRNode node, float amplitude, float duration)
        {
            s_Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, s_Devices);
            foreach (InputDevice device in s_Devices)
            {
                if (device.isValid && device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
                {
                    device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
                }
            }
        }
    }
}

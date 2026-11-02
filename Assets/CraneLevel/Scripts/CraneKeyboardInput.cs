using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ETVRTool.CraneLevel
{
    /// <summary>
    /// Keyboard controls so you can test the level in the Editor without a headset.
    ///  W/S trolley forward/back   A/D bridge left/right   R/F hook up/down   Q/E turn load
    ///  Space attach/release   C creep speed   X emergency stop   Backspace reset e-stop
    /// </summary>
    public class CraneKeyboardInput : MonoBehaviour
    {
        public BridgeCrane crane;
        public CraneHook hook;

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null || crane == null) return;

            // Seen from the control cabinet: trolley runs away from you (+Z), bridge runs sideways (X).
            crane.SetKeyboardInput(CraneAxis.Trolley, Axis(k.wKey, k.sKey));
            crane.SetKeyboardInput(CraneAxis.Bridge, Axis(k.dKey, k.aKey));
            crane.SetKeyboardInput(CraneAxis.Hoist, Axis(k.rKey, k.fKey));
            crane.SetKeyboardInput(CraneAxis.Rotate, Axis(k.eKey, k.qKey));

            if (k.spaceKey.wasPressedThisFrame && hook != null) hook.ToggleAttach();
            if (k.cKey.wasPressedThisFrame) crane.ToggleCreep();
            if (k.xKey.wasPressedThisFrame) crane.EmergencyStop();
            if (k.backspaceKey.wasPressedThisFrame) crane.ResetEmergencyStop();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static float Axis(KeyControl positive, KeyControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
#endif
    }
}

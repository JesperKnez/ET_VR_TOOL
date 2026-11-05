using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EinsteinTelescope.Gamepads
{
    /// <summary>
    /// State of <see cref="DualSenseGyroGamepad"/>: Unity's DualSense state (sticks, triggers, buttons) plus the gyro.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 15)]
    public struct DualSenseGyroState : IInputStateTypeInfo
    {
        public static FourCC Format => new FourCC('D', 'S', 'G', 'Y');
        public FourCC format => Format;

        [InputControl(name = "leftStick", layout = "Stick", format = "VC2B")]
        [InputControl(name = "leftStick/x", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "leftStick/left", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "leftStick/right", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
        [InputControl(name = "leftStick/y", offset = 1, format = "BYTE", parameters = "invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "leftStick/up", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "leftStick/down", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1,invert=false")]
        [FieldOffset(0)] public byte leftStickX;
        [FieldOffset(1)] public byte leftStickY;

        [InputControl(name = "rightStick", layout = "Stick", format = "VC2B")]
        [InputControl(name = "rightStick/x", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "rightStick/left", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "rightStick/right", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
        [InputControl(name = "rightStick/y", offset = 1, format = "BYTE", parameters = "invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "rightStick/up", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "rightStick/down", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1,invert=false")]
        [FieldOffset(2)] public byte rightStickX;
        [FieldOffset(3)] public byte rightStickY;

        [InputControl(name = "leftTrigger", format = "BYTE")]
        [FieldOffset(4)] public byte leftTrigger;

        [InputControl(name = "rightTrigger", format = "BYTE")]
        [FieldOffset(5)] public byte rightTrigger;

        [InputControl(name = "dpad", format = "BIT", layout = "Dpad", sizeInBits = 4, defaultState = 8)]
        [InputControl(name = "dpad/up", format = "BIT", layout = "DiscreteButton", parameters = "minValue=7,maxValue=1,nullValue=8,wrapAtValue=7", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/right", format = "BIT", layout = "DiscreteButton", parameters = "minValue=1,maxValue=3", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/down", format = "BIT", layout = "DiscreteButton", parameters = "minValue=3,maxValue=5", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/left", format = "BIT", layout = "DiscreteButton", parameters = "minValue=5, maxValue=7", bit = 0, sizeInBits = 4)]
        [InputControl(name = "buttonWest", displayName = "Square", bit = 4)]
        [InputControl(name = "buttonSouth", displayName = "Cross", bit = 5)]
        [InputControl(name = "buttonEast", displayName = "Circle", bit = 6)]
        [InputControl(name = "buttonNorth", displayName = "Triangle", bit = 7)]
        [FieldOffset(6)] public byte buttons0;

        [InputControl(name = "leftShoulder", bit = 0)]
        [InputControl(name = "rightShoulder", bit = 1)]
        [InputControl(name = "leftTriggerButton", layout = "Button", bit = 2)]
        [InputControl(name = "rightTriggerButton", layout = "Button", bit = 3)]
        [InputControl(name = "select", displayName = "Create", bit = 4)]
        [InputControl(name = "start", displayName = "Options", bit = 5)]
        [InputControl(name = "leftStickPress", bit = 6)]
        [InputControl(name = "rightStickPress", bit = 7)]
        [FieldOffset(7)] public byte buttons1;

        [InputControl(name = "systemButton", layout = "Button", displayName = "PS", bit = 0)]
        [InputControl(name = "touchpadButton", layout = "Button", displayName = "Touchpad Press", bit = 1)]
        [FieldOffset(8)] public byte buttons2;

        // Angular velocity, raw 16-bit (about 16.4 units per degree per second). x = pitch, y = yaw, z = roll.
        [InputControl(name = "gyro", layout = "Vector3", format = "VC3S", displayName = "Gyro")]
        [InputControl(name = "gyro/x", offset = 0, format = "SHRT")]
        [InputControl(name = "gyro/y", offset = 2, format = "SHRT")]
        [InputControl(name = "gyro/z", offset = 4, format = "SHRT")]
        [FieldOffset(9)] public short gyroX;
        [FieldOffset(11)] public short gyroY;
        [FieldOffset(13)] public short gyroZ;
    }

    /// <summary>
    /// PS5 DualSense with its gyro. Unity's own DualSense driver throws the motion data away, so this one replaces it
    /// (editor and development builds only): same buttons and sticks, plus <see cref="gyro"/> (-1..1, about
    /// ±2000 degrees per second). Works with a USB cable and with Bluetooth once the controller sends full reports.
    /// </summary>
    [InputControlLayout(stateType = typeof(DualSenseGyroState), displayName = "PS5 Controller (gyro)")]
    public class DualSenseGyroGamepad : DualShockGamepad, IInputStateCallbackReceiver
    {
        /// <summary>Full scale of <see cref="gyro"/>: 1 = this many degrees per second.</summary>
        public const float DegreesPerSecondAtOne = 32767f / 16.4f;

        public Vector3Control gyro { get; private set; }

        /// <summary>True once a report with motion data arrived (false: minimal Bluetooth reports, no gyro).</summary>
        public bool HasMotion { get; private set; }

        public static new DualSenseGyroGamepad current { get; private set; }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            gyro = GetChildControl<Vector3Control>("gyro");
        }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this) current = null;
        }

        public void OnNextUpdate()
        {
        }

        public bool GetStateOffsetForEvent(InputControl control, InputEventPtr eventPtr, ref uint offset) => false;

        /// <summary>
        /// Turns each raw HID report (USB, Bluetooth or the minimal Bluetooth report) into <see cref="DualSenseGyroState"/>
        /// and stores it as the controller's state.
        /// </summary>
        public unsafe void OnStateEvent(InputEventPtr eventPtr)
        {
            if (eventPtr.type != StateEvent.Type) return;
            StateEvent* stateEvent = StateEvent.From(eventPtr);
            if (stateEvent->stateFormat == DualSenseGyroState.Format)
            {
                InputState.Change(this, eventPtr);   // already in our format (queued by a test)
                return;
            }

            if (stateEvent->stateFormat != new FourCC('H', 'I', 'D')) return;
            uint size = stateEvent->stateSizeInBytes;
            byte* r = (byte*)stateEvent->state;
            if (size < 10) return;

            // Offsets per report type (see Unity's DualShockGamepadHID.cs)
            int sticks, triggers, buttons, gyroAt;
            if (r[0] == 0x01 && (size == 10 || size == 78))
            {
                sticks = 1; buttons = 5; triggers = 8; gyroAt = -1;     // minimal report (Bluetooth before full mode)
            }
            else if (r[0] == 0x01 && size >= 22)
            {
                sticks = 1; triggers = 5; buttons = 8; gyroAt = 16;     // USB
            }
            else if (r[0] == 0x31 && size >= 23)
            {
                sticks = 2; triggers = 6; buttons = 9; gyroAt = 17;     // Bluetooth full report
            }
            else
            {
                return;
            }

            var state = new DualSenseGyroState
            {
                leftStickX = r[sticks], leftStickY = r[sticks + 1], rightStickX = r[sticks + 2], rightStickY = r[sticks + 3],
                leftTrigger = r[triggers], rightTrigger = r[triggers + 1],
                buttons0 = r[buttons], buttons1 = r[buttons + 1], buttons2 = (byte)(r[buttons + 2] & 0x03)
            };

            if (gyroAt >= 0)
            {
                state.gyroX = (short)(r[gyroAt] | (r[gyroAt + 1] << 8));
                state.gyroY = (short)(r[gyroAt + 2] | (r[gyroAt + 3] << 8));
                state.gyroZ = (short)(r[gyroAt + 4] | (r[gyroAt + 5] << 8));
                HasMotion = true;
            }

            InputState.Change(this, state, InputUpdateType.None, eventPtr);
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStart() => Register();
#endif

        /// <summary>Replaces Unity's DualSense layout with this one, for the DualSense and the DualSense Edge.</summary>
        static void Register()
        {
            foreach (string name in InputSystem.ListLayouts())
            {
                if (name == nameof(DualSenseGyroGamepad)) return;   // already registered
            }

            InputSystem.RemoveLayout("DualSenseGamepadHID");
            InputSystem.RegisterLayout<DualSenseGyroGamepad>();
            foreach (int product in new[] { 0xCE6, 0xDF2 })   // DualSense, DualSense Edge
            {
                InputSystem.RegisterLayoutMatcher<DualSenseGyroGamepad>(new InputDeviceMatcher()
                    .WithInterface("HID")
                    .WithCapability("vendorId", 0x54C)
                    .WithCapability("productId", product));
            }
        }
    }
}

using System;
using UnityEngine;

namespace ETVRTool.CraneLevel
{
    public enum CraneAxis { Bridge = 0, Trolley = 1, Hoist = 2, Rotate = 3 }

    /// <summary>
    /// Overhead bridge crane, modelled on the crane in the ETpathfinder cleanroom.
    ///  - The bridge (the cream box girder) rides on two wall rails and moves along X.
    ///  - The trolley/hoist ("2" on the side) rides along the girder and moves along Z.
    ///  - The hook block goes up/down and can turn the load around the vertical axis.
    /// Input comes from the VR control panel buttons and/or the keyboard (for testing).
    /// </summary>
    public class BridgeCrane : MonoBehaviour
    {
        [Header("Moving parts")]
        [Tooltip("Moves along this object's local X (runway direction).")]
        public Transform bridge;
        [Tooltip("Child of the bridge. Moves along local Z (along the girder).")]
        public Transform trolley;
        [Tooltip("Child of the trolley. Moves up/down and turns around Y.")]
        public Transform hookBlock;

        [Header("Travel limits")]
        public Vector2 bridgeRange = new Vector2(-12f, 12f);
        public Vector2 trolleyRange = new Vector2(-7f, 7f);
        [Tooltip("World height range of the hook block.")]
        public Vector2 hookHeightRange = new Vector2(0.6f, 7.4f);

        [Header("Speeds (m/s, deg/s)")]
        public float bridgeSpeed = 1.2f;
        public float trolleySpeed = 0.9f;
        public float hoistSpeed = 0.5f;
        public float rotateSpeed = 25f;
        [Tooltip("Seconds to reach full speed. Real cranes ramp up/down smoothly.")]
        public float rampTime = 0.6f;
        [Tooltip("Speed multiplier in creep (fine positioning) mode.")]
        [Range(0.05f, 1f)] public float creepFactor = 0.2f;

        [Header("Hoist straps (visual)")]
        public LineRenderer[] straps;
        public Transform[] strapTops;     // on the trolley
        public Transform[] strapBottoms;  // on the hook block

        public bool CreepMode { get; private set; }
        public bool EmergencyStopped { get; private set; }
        public float HookYaw => _yaw;

        public event Action<bool> EmergencyStopChanged;
        public event Action<bool> CreepModeChanged;

        readonly float[] _buttonInput = new float[4];
        readonly float[] _keyboardInput = new float[4];
        readonly float[] _velocity = new float[4];
        float _yaw;

        void Start()
        {
            if (hookBlock != null) _yaw = hookBlock.localEulerAngles.y;
        }

        public void SetButtonInput(CraneAxis axis, float value) => _buttonInput[(int)axis] = Mathf.Clamp(value, -1f, 1f);
        public void SetKeyboardInput(CraneAxis axis, float value) => _keyboardInput[(int)axis] = Mathf.Clamp(value, -1f, 1f);

        public void ToggleCreep()
        {
            CreepMode = !CreepMode;
            CreepModeChanged?.Invoke(CreepMode);
        }

        public void EmergencyStop()
        {
            EmergencyStopped = true;
            Array.Clear(_velocity, 0, _velocity.Length);
            EmergencyStopChanged?.Invoke(true);
        }

        public void ResetEmergencyStop()
        {
            if (!EmergencyStopped) return;
            EmergencyStopped = false;
            EmergencyStopChanged?.Invoke(false);
        }

        public bool IsMoving
        {
            get
            {
                foreach (var v in _velocity) if (Mathf.Abs(v) > 0.01f) return true;
                return false;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float[] maxSpeed = { bridgeSpeed, trolleySpeed, hoistSpeed, rotateSpeed };
            float factor = CreepMode ? creepFactor : 1f;

            for (int i = 0; i < 4; i++)
            {
                float input = EmergencyStopped ? 0f : Mathf.Clamp(_buttonInput[i] + _keyboardInput[i], -1f, 1f);
                float target = input * maxSpeed[i] * factor;
                float rate = maxSpeed[i] / Mathf.Max(0.01f, rampTime);
                _velocity[i] = Mathf.MoveTowards(_velocity[i], target, rate * dt);
            }

            // Bridge along X
            if (bridge != null)
            {
                Vector3 p = bridge.localPosition;
                float x = p.x + _velocity[0] * dt;
                if (x < bridgeRange.x || x > bridgeRange.y) _velocity[0] = 0f;
                p.x = Mathf.Clamp(x, bridgeRange.x, bridgeRange.y);
                bridge.localPosition = p;
            }

            // Trolley along Z
            if (trolley != null)
            {
                Vector3 p = trolley.localPosition;
                float z = p.z + _velocity[1] * dt;
                if (z < trolleyRange.x || z > trolleyRange.y) _velocity[1] = 0f;
                p.z = Mathf.Clamp(z, trolleyRange.x, trolleyRange.y);
                trolley.localPosition = p;
            }

            if (hookBlock != null)
            {
                // Hoist (world Y)
                Vector3 w = hookBlock.position;
                float y = w.y + _velocity[2] * dt;
                if (y < hookHeightRange.x || y > hookHeightRange.y) _velocity[2] = 0f;
                w.y = Mathf.Clamp(y, hookHeightRange.x, hookHeightRange.y);
                hookBlock.position = w;

                // Rotating hook
                _yaw += _velocity[3] * dt;
                hookBlock.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            UpdateStraps();
        }

        void UpdateStraps()
        {
            if (straps == null) return;
            for (int i = 0; i < straps.Length; i++)
            {
                if (straps[i] == null || i >= strapTops.Length || i >= strapBottoms.Length) continue;
                straps[i].SetPosition(0, strapTops[i].position);
                straps[i].SetPosition(1, strapBottoms[i].position);
            }
        }
    }
}

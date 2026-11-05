using System.Collections.Generic;
using EinsteinTelescope.Gamepads;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Play testing without a headset, with a PS5 controller (an Xbox controller works too, without gyro).
/// Creates itself on Play (editor and development builds) and takes over as soon as the controller is used while no
/// real headset is active. It then switches off the XR Device Simulator and drives the head and the right controller:
///   Left stick     walk (like WASD)
///   Right stick    point with the right controller
///   R2             trigger: press buttons on the screens (like the trigger of a Quest controller)
///   Gyro           look around (tilt / turn the controller). Without gyro the right stick looks and you point straight ahead.
///   R3             look and point straight ahead again (also resets gyro drift)
///   Triangle       talk to Lumi (LumiListener), D-pad etc.: see LaserGamepadControls
/// Gyro directions and speed can be changed in the Inspector of "Gamepad Playtest Rig" while playing.
/// </summary>
public class GamepadPlaytestRig : MonoBehaviour
{
    [Header("Walking and pointing")]
    public float walkSpeed = 1.6f;
    public float eyeHeight = 1.65f;
    public float pointSpeed = 90f;          // degrees per second at full stick
    public Vector2 pointLimits = new Vector2(70f, 55f);

    [Header("Gyro (look)")]
    public float gyroSensitivity = 1f;
    public bool invertGyroYaw = false;
    public bool invertGyroPitch = false;
    [Tooltip("Turn rates below this (degrees per second) are ignored, so the view does not creep.")]
    public float gyroDeadzone = 0.8f;

    [Header("Without gyro")]
    public float stickLookSpeed = 120f;

    const float STICK_DEADZONE = 0.15f;

    bool active;
    XROrigin origin;
    Transform head, rightController;
    float yaw, pitch, pointYaw, pointPitch;
    Vector3 gyroBias;
    readonly List<(InputAction action, int binding)> addedBindings = new List<(InputAction, int)>();
    readonly List<Behaviour> disabled = new List<Behaviour>();
    readonly List<GameObject> hidden = new List<GameObject>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Gamepad Playtest Rig");
        DontDestroyOnLoad(go);
        go.AddComponent<GamepadPlaytestRig>();
    }
#endif

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return;
        if (!active)
        {
            if (XRSettings.isDeviceActive || !Used(pad)) return;   // a real headset, or the controller is not in use
            if (!TakeOver()) return;
        }

        float dt = Time.unscaledDeltaTime;
        var gyroPad = pad as DualSenseGyroGamepad;
        bool gyro = gyroPad != null && gyroPad.HasMotion;

        if (pad.rightStickButton.wasPressedThisFrame)
        {
            pitch = 0f;
            pointYaw = pointPitch = 0f;
            if (gyro) gyroBias = gyroPad.gyro.ReadValue();
        }

        // Look
        Vector2 right = Stick(pad.rightStick.ReadValue());
        if (gyro)
        {
            Vector3 rate = (gyroPad.gyro.ReadValue() - gyroBias) * DualSenseGyroGamepad.DegreesPerSecondAtOne;
            Vector3 raw = gyroPad.gyro.ReadValue() * DualSenseGyroGamepad.DegreesPerSecondAtOne;
            Vector3 bias = gyroBias * DualSenseGyroGamepad.DegreesPerSecondAtOne;
            if ((raw - bias).magnitude < 3f) gyroBias = Vector3.Lerp(gyroBias, gyroPad.gyro.ReadValue(), dt * 0.5f);   // follow slow drift while still
            float yawRate = Mathf.Abs(rate.y) > gyroDeadzone ? rate.y : 0f;
            float pitchRate = Mathf.Abs(rate.x) > gyroDeadzone ? rate.x : 0f;
            yaw += (invertGyroYaw ? 1f : -1f) * yawRate * gyroSensitivity * dt;
            pitch += (invertGyroPitch ? 1f : -1f) * pitchRate * gyroSensitivity * dt;

            // Point
            pointYaw = Mathf.Clamp(pointYaw + right.x * pointSpeed * dt, -pointLimits.x, pointLimits.x);
            pointPitch = Mathf.Clamp(pointPitch - right.y * pointSpeed * dt, -pointLimits.y, pointLimits.y);
        }
        else
        {
            yaw += right.x * stickLookSpeed * dt;
            pitch -= right.y * stickLookSpeed * dt;
            pointYaw = pointPitch = 0f;
        }

        pitch = Mathf.Clamp(pitch, -80f, 80f);

        // Walk, along the floor, in the direction you look
        Vector2 left = Stick(pad.leftStick.ReadValue());
        Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
        origin.transform.position += heading * new Vector3(left.x, 0f, left.y) * walkSpeed * dt;

        Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
        head.SetPositionAndRotation(origin.transform.position + Vector3.up * eyeHeight, look);
        if (rightController)
        {
            Vector3 hand = head.position + heading * new Vector3(0.2f, -0.35f, 0.3f);
            rightController.SetPositionAndRotation(hand, look * Quaternion.Euler(pointPitch, pointYaw, 0f));
        }
    }

    static Vector2 Stick(Vector2 v)
    {
        float amount = Mathf.InverseLerp(STICK_DEADZONE, 1f, v.magnitude);
        return amount > 0f ? v.normalized * amount : Vector2.zero;
    }

    static bool Used(Gamepad pad) =>
        pad.leftStick.ReadValue().magnitude > 0.5f || pad.rightStick.ReadValue().magnitude > 0.5f ||
        pad.rightTrigger.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame;

    bool TakeOver()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (!origin || !origin.Camera) return false;
        head = origin.Camera.transform;
        rightController = FindChild(origin.transform, "Right Controller");

        // The headset simulator and the tracked pose drivers would move the head and hand back every frame
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            string type = behaviour.GetType().Name;
            if ((type == "XRDeviceSimulator" || type == "XRInteractionSimulator") && behaviour.gameObject.activeSelf)
            {
                behaviour.gameObject.SetActive(false);
                hidden.Add(behaviour.gameObject);
            }
        }

        DisablePoseDrivers(head);
        if (rightController) DisablePoseDrivers(rightController);

        // R2 = trigger of the right controller: press buttons on the screens
        AddTriggerBindings();

        yaw = head.eulerAngles.y;
        pitch = 0f;
        active = true;
        Debug.Log("[GamepadPlaytestRig] Controller takes over: left stick walk, right stick point, R2 press, gyro look, R3 recentre." +
                  (rightController ? "" : " (No 'Right Controller' found under the XR Origin: pointing does not work.)"));
        return true;
    }

    void DisablePoseDrivers(Transform t)
    {
        foreach (Behaviour b in t.GetComponents<Behaviour>())
        {
            if (b && b.enabled && b.GetType().Name == "TrackedPoseDriver")
            {
                b.enabled = false;
                disabled.Add(b);
            }
        }
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }

        return null;
    }

    void AddTriggerBindings()
    {
        var names = new[] { "UI Press", "UI Press Value", "Activate", "Activate Value" };
        var seen = new HashSet<InputAction>();
        foreach (var manager in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>(FindObjectsSortMode.None))
        {
            foreach (InputActionAsset asset in manager.actionAssets)
            {
                if (!asset) continue;
                InputActionMap map = asset.FindActionMap("XRI Right Interaction") ?? asset.FindActionMap("XRI RightHand Interaction");
                if (map == null) continue;
                foreach (string name in names)
                {
                    InputAction action = map.FindAction(name);
                    if (action == null || !seen.Add(action)) continue;
                    bool wasEnabled = action.enabled;
                    action.Disable();
                    action.AddBinding("<Gamepad>/rightTrigger");
                    addedBindings.Add((action, action.bindings.Count - 1));
                    if (wasEnabled) action.Enable();
                }
            }
        }

        if (addedBindings.Count == 0) Debug.LogWarning("[GamepadPlaytestRig] No 'XRI Right Interaction' actions found: R2 does not press.");
    }

    void OnDestroy()
    {
        // Leave the input asset as it was (it is a project asset)
        for (int i = addedBindings.Count - 1; i >= 0; i--)
        {
            var (action, binding) = addedBindings[i];
            if (action == null || binding >= action.bindings.Count) continue;
            bool wasEnabled = action.enabled;
            action.Disable();
            action.ChangeBinding(binding).Erase();
            if (wasEnabled) action.Enable();
        }

        foreach (Behaviour b in disabled) if (b) b.enabled = true;
        foreach (GameObject go in hidden) if (go) go.SetActive(true);
    }
}

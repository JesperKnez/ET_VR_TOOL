using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Writes every input device Unity sees (and the ones it could not use) to Logs/Playtest/devices.txt, for when a
/// controller "does nothing". Menu: Tools/Einstein Telescope/Debug/List input devices.
/// </summary>
static class InputDeviceReport
{
    [MenuItem("Tools/Einstein Telescope/Debug/List input devices")]
    static void Write()
    {
        var text = new StringBuilder();
        text.AppendLine("Gamepad.current: " + (Gamepad.current != null ? Gamepad.current.layout + " " + Gamepad.current.name : "none"));
        text.AppendLine("Devices:");
        foreach (InputDevice device in InputSystem.devices)
        {
            text.AppendLine("  " + device.layout + " | " + device.name + " | enabled " + device.enabled + " | " + Describe(device.description));
        }

        text.AppendLine("Unsupported (no layout matched):");
        foreach (var description in InputSystem.GetUnsupportedDevices())
        {
            text.AppendLine("  " + Describe(description));
        }

        text.AppendLine("Layouts with DualSense/DualShock in the name:");
        foreach (string layout in InputSystem.ListLayouts())
        {
            if (layout.Contains("DualSense") || layout.Contains("DualShock")) text.AppendLine("  " + layout);
        }

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Playtest");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "devices.txt"), text.ToString());
        Debug.Log("[InputDeviceReport] Logs/Playtest/devices.txt");
    }

    static string Describe(UnityEngine.InputSystem.Layouts.InputDeviceDescription d)
    {
        string caps = d.capabilities ?? "";
        int vid = caps.IndexOf("\"vendorId\":"), pid = caps.IndexOf("\"productId\":");
        string ids = (vid >= 0 ? caps.Substring(vid, Mathf.Min(18, caps.Length - vid)) : "") + " " + (pid >= 0 ? caps.Substring(pid, Mathf.Min(19, caps.Length - pid)) : "");
        return "interface '" + d.interfaceName + "' product '" + d.product + "' manufacturer '" + d.manufacturer + "' " + ids;
    }
}

static class InputDeviceRetry
{
    /// <summary>Creates devices for controllers Unity gave up on earlier (e.g. after a layout change), and logs why one fails.</summary>
    [MenuItem("Tools/Einstein Telescope/Debug/Retry unsupported controllers")]
    static void Retry()
    {
        var text = new StringBuilder();
        foreach (var description in InputSystem.GetUnsupportedDevices())
        {
            if (string.IsNullOrEmpty(description.product) || !(description.product.Contains("DualSense") || description.product.Contains("Controller"))) continue;
            try
            {
                InputDevice device = InputSystem.AddDevice(description);
                text.AppendLine("created " + device.layout + " for " + description.product);
            }
            catch (System.Exception e)
            {
                text.AppendLine("FAILED for " + description.product + ": " + e);
            }
        }

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Playtest");
        File.WriteAllText(Path.Combine(folder, "retry.txt"), text.Length > 0 ? text.ToString() : "nothing to retry");
    }
}

static class InputDeviceCleanup
{
    /// <summary>Removes DualSense devices that were created by hand (they get no input from the real controller).</summary>
    [MenuItem("Tools/Einstein Telescope/Debug/Remove DualSense devices")]
    static void Remove()
    {
        var list = new System.Collections.Generic.List<InputDevice>();
        foreach (InputDevice device in InputSystem.devices)
        {
            if (device.layout == "DualSenseGamepadHID" || device.layout == "DualSenseGyroGamepad") list.Add(device);
        }

        foreach (InputDevice device in list) InputSystem.RemoveDevice(device);
        Debug.Log("[InputDeviceCleanup] removed " + list.Count + " DualSense device(s)");
    }
}

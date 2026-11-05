using System.Collections.Generic;
using EinsteinTelescope.Laser;
using UnityEngine;

/// <summary>
/// Keeps the laser beam visible from the control room. Carlo's beam is physically ±1.8 cm wide: fine at 2.5 m,
/// but in the towers (13-25 m from the player) thinner than a pixel. After the emitters have drawn their lines,
/// every line is widened to at least MIN_ANGLE_DEGREES as seen from the player camera.
/// So it is wider far away, while up close it keeps its real thickness. Creates itself on Play.
/// </summary>
[DefaultExecutionOrder(10000)]   // after LaserEmitter (which sets the width again every LateUpdate)
public class LaserBeamMinWidth : MonoBehaviour
{
    const float MIN_ANGLE_DEGREES = 0.1f;   // ±2.5 pixels on a Quest 3

    static readonly List<LineRenderer> s_Lines = new List<LineRenderer>();
    Camera eye;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Laser Beam Min Width");
        DontDestroyOnLoad(go);
        go.AddComponent<LaserBeamMinWidth>();
    }

    void LateUpdate()
    {
        if (!eye) eye = Camera.main;
        if (!eye) return;

        Vector3 from = eye.transform.position;
        float perMeter = Mathf.Tan(MIN_ANGLE_DEGREES * Mathf.Deg2Rad);
        var emitters = LaserEmitter.ActiveEmitters;
        for (int e = 0; e < emitters.Count; e++)
        {
            emitters[e].GetComponentsInChildren(false, s_Lines);
            foreach (var line in s_Lines)
            {
                if (!line.enabled || line.positionCount < 2) continue;
                line.startWidth = Mathf.Max(line.startWidth, Vector3.Distance(from, line.GetPosition(0)) * perMeter);
                line.endWidth = Mathf.Max(line.endWidth, Vector3.Distance(from, line.GetPosition(1)) * perMeter);
            }
        }
    }
}

using System.Collections.Generic;
using EinsteinTelescope.Laser;
using UnityEngine;

/// <summary>
/// The laser beam is ±1.8 cm wide: thinner than a pixel from across the hall. LaserBeamMinWidth widens it for the
/// player; this does the same for the poster camera (it runs after it, and the emitters reset the width every frame).
/// </summary>
[DefaultExecutionOrder(10001)]
public class PosterBeamWidth : MonoBehaviour
{
    const float MIN_ANGLE_DEGREES = 0.14f;

    static readonly List<LineRenderer> s_Lines = new List<LineRenderer>();

    void LateUpdate()
    {
        Vector3 from = transform.position;
        float perMeter = Mathf.Tan(MIN_ANGLE_DEGREES * Mathf.Deg2Rad);
        var emitters = LaserEmitter.ActiveEmitters;
        for (int e = 0; e < emitters.Count; e++)
        {
            emitters[e].GetComponentsInChildren(false, s_Lines);
            foreach (LineRenderer line in s_Lines)
            {
                if (!line.enabled || line.positionCount < 2) continue;
                line.startWidth = Mathf.Max(line.startWidth, Vector3.Distance(from, line.GetPosition(0)) * perMeter);
                line.endWidth = Mathf.Max(line.endWidth, Vector3.Distance(from, line.GetPosition(1)) * perMeter);
            }
        }
    }
}

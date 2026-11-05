using System.Collections.Generic;
using EinsteinTelescope.Laser;
using UnityEngine;

/// <summary>
/// Houdt de laserstraal zichtbaar vanuit de control room. Carlo's straal is fysiek ±1,8 cm breed: prima op 2,5 m,
/// maar in de torens (13-25 m van de speler) dunner dan een pixel. Nadat de emitters hun lijnen hebben getekend,
/// wordt elke lijn verbreed tot minstens MIN_ANGLE_DEGREES gezien vanaf de spelerscamera.
/// Ver weg dus breder, van dichtbij blijft de echte dikte. Maakt zichzelf aan bij Play.
/// </summary>
[DefaultExecutionOrder(10000)]   // na LaserEmitter (die zet elke LateUpdate de breedte opnieuw)
public class LaserBeamMinWidth : MonoBehaviour
{
    const float MIN_ANGLE_DEGREES = 0.1f;   // ±2,5 pixels op een Quest 3

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

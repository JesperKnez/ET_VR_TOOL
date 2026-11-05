using System.Collections.Generic;
using UnityEngine;

/// Put on a SignalTower_Wall / SignalTower_Base object. Switches the three lenses
/// (materials ET2_Signal_Red / _Amber / _Green) on and off, with optional blinking.
/// Call SetState(...) from your own scripts, e.g. when the laser is misaligned.
public class SignalTower : MonoBehaviour
{
    public enum State { Off, Normal, Warning, Alarm }

    public State state = State.Normal;
    [Tooltip("Blink speed (on/off per second) for Warning and Alarm")]
    public float blinkRate = 2f;
    public float intensity = 4f;

    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    readonly Dictionary<string, Material> lenses = new Dictionary<string, Material>();
    Material[] instances;
    readonly Dictionary<string, Color> colours = new Dictionary<string, Color>
    {
        { "Red", new Color(1f, 0.06f, 0.04f) },
        { "Amber", new Color(1f, 0.55f, 0.05f) },
        { "Green", new Color(0.08f, 1f, 0.2f) },
    };

    void Awake()
    {
        var r = GetComponentInChildren<Renderer>();
        if (r == null) return;
        instances = r.materials;                    // instances, so other towers are unaffected
        foreach (var m in instances)
            foreach (var key in colours.Keys)
                if (m.name.Contains("Signal_" + key))
                {
                    m.EnableKeyword("_EMISSION");
                    lenses[key] = m;
                }
    }

    // r.materials creates copies; Unity does not clean those up by itself
    void OnDestroy()
    {
        if (instances == null) return;
        foreach (var m in instances)
            if (m) Destroy(m);
    }

    public void SetState(State s) => state = s;

    void Update()
    {
        bool blinkOn = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
        Set("Green", state == State.Normal);
        Set("Amber", state == State.Warning && blinkOn);
        Set("Red", state == State.Alarm && blinkOn);
    }

    void Set(string key, bool on)
    {
        if (lenses.TryGetValue(key, out var m))
            m.SetColor(EmissionColor, on ? colours[key] * intensity : Color.black);
    }
}

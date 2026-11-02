using System;
using System.Collections.Generic;
using EinsteinTelescope.Laser;
using UnityEngine;

/// <summary>
/// Maakt de torens doorzichtig (bijv. in level 1, zodat je de optics erin ziet).
/// Zelfde PBR-materiaal, alleen een lagere alpha: elk opaque materiaal heeft een "_Clear"-tweeling
/// (Surface Type Transparent) die Tools > Einstein Telescope > Torens aanmaakt.
/// Tijdens het faden gebruiken de renderers kopieën van die tweelingen; is de toren weer helemaal
/// dicht, dan gaan de originele opaque materialen terug (goedkoper en geen sorteerproblemen).
/// Zet dit op de root van TowerConstellation.
/// </summary>
public class TowerTransparency : MonoBehaviour
{
    [Serializable]
    public class MaterialPair
    {
        public Material opaque;
        public Material clear;
    }

    public MaterialPair[] materials = new MaterialPair[0];

    [Range(0f, 1f)] public float clearAlpha = 0.25f;
    [Tooltip("Seconden voor een fade van helemaal dicht naar helemaal open")]
    public float fadeTime = 1.5f;

    [Header("Optioneel: automatisch per level")]
    public LaserLevelManager levelManager;
    [Tooltip("Levelnummers (1 = eerste level) waarin de torens doorzichtig zijn")]
    public int[] clearInLevels = { 1 };

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int Cutoff = Shader.PropertyToID("_Cutoff");

    Renderer[] renderers;
    readonly Dictionary<Material, Material> toClear = new();   // opaque asset → clear-kopie
    readonly Dictionary<Material, Material> toOpaque = new();  // clear-kopie → opaque asset
    readonly List<(Material mat, Color color, float cutoff)> clearInstances = new();
    float alpha = 1f, target = 1f;
    bool usingClear;

    public bool IsClear => target < 1f;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var pair in materials)
        {
            if (pair == null || !pair.opaque || !pair.clear) continue;
            // Kopie, zodat het .mat-bestand in de editor niet mee verandert
            var instance = new Material(pair.clear) { name = pair.clear.name + " (runtime)" };
            toClear[pair.opaque] = instance;
            toOpaque[instance] = pair.opaque;
            float cutoff = instance.HasProperty(Cutoff) ? instance.GetFloat(Cutoff) : 0f;
            clearInstances.Add((instance, instance.GetColor(BaseColor), cutoff));
        }
    }

    void OnEnable()
    {
        if (levelManager) levelManager.OnLevelStarted.AddListener(OnLevelStarted);
    }

    void OnDisable()
    {
        if (levelManager) levelManager.OnLevelStarted.RemoveListener(OnLevelStarted);
    }

    void OnDestroy()
    {
        foreach (var c in clearInstances)
            if (c.mat) Destroy(c.mat);
    }

    // De level manager telt vanaf 0, clearInLevels vanaf 1
    void OnLevelStarted(int index) => SetClear(Array.IndexOf(clearInLevels, index + 1) >= 0);

    /// <summary>Fade naar doorzichtig (true) of dicht (false). Ook bruikbaar vanuit een UnityEvent.</summary>
    public void SetClear(bool clear) => target = clear ? clearAlpha : 1f;

    public void ToggleClear() => SetClear(!IsClear);

    /// <summary>Zelfde als SetClear, maar zonder fade.</summary>
    public void SetClearImmediate(bool clear)
    {
        SetClear(clear);
        alpha = target;
        Apply();
    }

    void Update()
    {
        if (Mathf.Approximately(alpha, target) && usingClear == alpha < 1f) return;
        float step = fadeTime > 0f ? Time.deltaTime / fadeTime : 1f;
        alpha = Mathf.MoveTowards(alpha, target, step);
        Apply();
    }

    void Apply()
    {
        bool wantClear = alpha < 1f;
        if (wantClear != usingClear)
        {
            Swap(wantClear ? toClear : toOpaque);
            usingClear = wantClear;
        }

        foreach (var (mat, color, cutoff) in clearInstances)
        {
            mat.SetColor(BaseColor, new Color(color.r, color.g, color.b, color.a * alpha));
            // Alpha clip (bouten): drempel mee omlaag, anders knipt een lage alpha alles weg
            if (mat.HasProperty(Cutoff)) mat.SetFloat(Cutoff, cutoff * alpha);
        }
    }

    void Swap(Dictionary<Material, Material> map)
    {
        foreach (var r in renderers)
        {
            if (!r) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] && map.TryGetValue(mats[i], out var swapped))
                {
                    mats[i] = swapped;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}

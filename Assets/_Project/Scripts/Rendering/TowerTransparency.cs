using System;
using System.Collections.Generic;
using EinsteinTelescope.Laser;
using UnityEngine;

/// <summary>
/// Makes the towers see-through (e.g. in level 1, so you can see the optics inside).
/// Same PBR material, just a lower alpha: every opaque material has a "_Clear" twin
/// (Surface Type Transparent) that Tools > Einstein Telescope > Towers creates.
/// While fading, the renderers use copies of those twins; once the tower is fully
/// opaque again, the original opaque materials go back (cheaper and no sorting problems).
/// Put this on the root of TowerConstellation.
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
    [Tooltip("Seconds for a fade from fully opaque to fully see-through")]
    public float fadeTime = 1.5f;

    [Header("Optional: automatic per level")]
    public LaserLevelManager levelManager;
    [Tooltip("Level numbers (1 = first level) in which the towers are see-through")]
    public int[] clearInLevels = { 1 };

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int Cutoff = Shader.PropertyToID("_Cutoff");

    Renderer[] renderers;
    readonly Dictionary<Material, Material> toClear = new();   // opaque asset → clear copy
    readonly Dictionary<Material, Material> toOpaque = new();  // clear copy → opaque asset
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
            // Copy, so the .mat file in the editor does not change along with it
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

    // The level manager counts from 0, clearInLevels from 1
    void OnLevelStarted(int index) => SetClear(Array.IndexOf(clearInLevels, index + 1) >= 0);

    /// <summary>Fade to see-through (true) or opaque (false). Also usable from a UnityEvent.</summary>
    public void SetClear(bool clear) => target = clear ? clearAlpha : 1f;

    public void ToggleClear() => SetClear(!IsClear);

    /// <summary>Same as SetClear, but without a fade.</summary>
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
            // Alpha clip (bolts): lower the threshold too, otherwise a low alpha clips everything away
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

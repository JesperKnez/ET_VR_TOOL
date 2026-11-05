using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools > Einstein Telescope > Lighting > Match post-processing to photos
/// Sets the Global Volume of the open scene to a starting point that resembles the ETpathfinder photos:
/// a bright, white cleanroom (high-key) with neutral colours and softly glowing LED panels.
///  - Neutral tonemapping instead of ACES: ACES pushes white towards grey and makes everything flatter and darker.
///  - Slightly more contrast and slightly less saturation, as in the photos.
///  - Bloom only on what is really brighter than white (LED panels, screens).
/// Fine-tune afterwards in the headset; all values are simply in the Inspector of the volume profile.
/// </summary>
public static class HallLookSetup
{
    [MenuItem("Tools/Einstein Telescope/Lighting/Match post-processing to photos")]
    static void Apply()
    {
        Volume volume = null;
        foreach (var v in Object.FindObjectsByType<Volume>())
            if (v.isGlobal && v.sharedProfile) { volume = v; break; }
        if (!volume)
        {
            Debug.LogError("[HallLookSetup] No Global Volume with a profile found in the open scene.");
            return;
        }
        var profile = volume.sharedProfile;

        var tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        var color = GetOrAdd<ColorAdjustments>(profile);
        color.contrast.Override(10f);
        color.saturation.Override(-5f);

        var bloom = GetOrAdd<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.65f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HallLookSetup] Updated {profile.name} (Neutral tonemapping, contrast +10, saturation -5, bloom 0.35).", profile);
    }

    // A volume component has to be a sub-asset of the profile, otherwise it is gone after a restart
    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
        {
            Undo.RecordObject(component, "Hall look");
        }
        else
        {
            component = profile.Add<T>();
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(component);
        return component;
    }
}

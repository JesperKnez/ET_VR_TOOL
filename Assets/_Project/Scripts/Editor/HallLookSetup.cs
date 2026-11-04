using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools > Einstein Telescope > Belichting > Post-processing afstemmen op foto's
/// Zet de Global Volume van de open scene op een startpunt dat lijkt op de foto's van ETpathfinder:
/// een heldere, witte cleanroom (high-key) met neutrale kleuren en zacht gloeiende LED-panelen.
///  - Tonemapping Neutral i.p.v. ACES: ACES drukt wit naar grijs en maakt alles vlakker en donkerder.
///  - Iets meer contrast en iets minder verzadiging, zoals op de foto's.
///  - Bloom alleen op wat echt helderder is dan wit (LED-panelen, schermen).
/// Daarna in de headset bijstellen; alle waarden staan gewoon in de Inspector van het volume profile.
/// </summary>
public static class HallLookSetup
{
    [MenuItem("Tools/Einstein Telescope/Belichting/Post-processing afstemmen op foto's")]
    static void Apply()
    {
        Volume volume = null;
        foreach (var v in Object.FindObjectsByType<Volume>())
            if (v.isGlobal && v.sharedProfile) { volume = v; break; }
        if (!volume)
        {
            Debug.LogError("[HallLookSetup] Geen Global Volume met profile gevonden in de open scene.");
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
        Debug.Log($"[HallLookSetup] {profile.name} bijgewerkt (Neutral tonemapping, contrast +10, saturation -5, bloom 0.35).", profile);
    }

    // Een volume-component moet als sub-asset in het profile staan, anders is hij na herstarten weg
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

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools > Einstein Telescope > Lighting > Match post-processing to photos
/// Sets the look of the open scene to a starting point that resembles the ETpathfinder photos: a bright, white
/// cleanroom (high-key) with neutral colours and softly glowing LED panels, but with enough contrast and contact
/// shadow that things stand on the floor instead of floating in white.
///  - Neutral tonemapping instead of ACES: ACES pushes white towards grey and makes everything flatter and darker.
///  - Exposure -0.5 and contrast +22: the hall was so bright that every material difference disappeared.
///  - Slightly less saturation, as in the photos. Bloom only on what is really brighter than white.
///  - Hall walls, ceiling and floor at 90 % of their colour: pure white burns out (CLAUDE.md: aim for 70-80 % grey;
///    90 % keeps the high-key look of the real hall).
///  - Ambient occlusion: SSAO (contact shadows, radius 20 cm) on the PC renderer only (Link; too heavy for a standalone Quest), and baked AO in the
///    lightmaps (free at runtime, so the Quest build gets contact shadows too).
/// Chosen with LaserPlaytestBot's "looks" tour (variant between C and D, 5 Nov 2026). Bake the lighting afterwards.
/// All values are in the Inspector of the volume profile, the renderer and the Lighting window.
/// </summary>
public static class HallLookSetup
{
    const float POST_EXPOSURE = -0.5f;
    const float CONTRAST = 22f;
    const float HALL_BRIGHTNESS = 0.9f;
    const string PC_RENDERER = "Assets/Settings/Project Configuration/Standalone Preset.asset";

    // Hall surfaces and their colour before this pass (so running it again does not keep darkening them)
    static readonly (string path, Color original)[] HallSurfaces =
    {
        ("Assets/_Project/Materials/Room/Hall_Wall.mat", Color.white),
        ("Assets/_Project/Materials/Room/Ceiling.mat", Color.white),
        ("Assets/_Project/Materials/Room/Hall_Floor.mat", new Color(0.9063396f, 0.9063396f, 0.9063396f, 1f)),
    };

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
        color.postExposure.Override(POST_EXPOSURE);
        color.contrast.Override(CONTRAST);
        color.saturation.Override(-5f);

        var bloom = GetOrAdd<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.65f);

        EditorUtility.SetDirty(profile);

        int surfaces = DimHallSurfaces();
        string ssao = AddSsaoToPcRenderer();
        string baked = EnableBakedAo();

        AssetDatabase.SaveAssets();
        Debug.Log($"[HallLookSetup] {profile.name}: Neutral tonemapping, exposure {POST_EXPOSURE}, contrast +{CONTRAST}, saturation -5, bloom 0.35. " +
                  $"Hall surfaces at {HALL_BRIGHTNESS * 100f:0} %: {surfaces}. SSAO: {ssao}. Baked AO: {baked}. Bake the lighting again.", profile);
    }

    static int DimHallSurfaces()
    {
        int count = 0;
        foreach (var (path, original) in HallSurfaces)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material || !material.HasProperty("_BaseColor")) continue;
            Undo.RecordObject(material, "Hall look");
            Color c = original * HALL_BRIGHTNESS;
            c.a = 1f;
            material.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(material);
            count++;
        }

        return count;
    }

    /// <summary>Adds URP's Screen Space Ambient Occlusion to the PC renderer (Standalone Preset) if it is not there yet.</summary>
    static string AddSsaoToPcRenderer()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PC_RENDERER);
        if (!renderer) return "PC renderer not found at " + PC_RENDERER;

        System.Type type = typeof(ScriptableRendererFeature).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
        if (type == null) return "URP has no ScreenSpaceAmbientOcclusion type";

        ScriptableRendererFeature feature = null;
        foreach (var f in renderer.rendererFeatures)
            if (f && f.GetType() == type) feature = f;

        if (feature == null)
        {
            feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            // The renderer keeps a list of features and a matching list of their file IDs
            var so = new SerializedObject(renderer);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Small contact shadows only: a large radius (0.35 m) blackened everything under the monitor, and the
        // blue-noise method with few samples looked grainy (no TAA in VR to smooth it). Baked AO does the broad shading.
        var settings = new SerializedObject(feature);
        SetFloat(settings, "m_Settings.Intensity", 1.0f);
        SetFloat(settings, "m_Settings.Radius", 0.2f);
        SetEnum(settings, "m_Settings.AOMethod", 1);   // interleaved gradient: smoother than blue noise
        SetEnum(settings, "m_Settings.Samples", 0);    // high
        SetEnum(settings, "m_Settings.BlurQuality", 0); // high (bilateral)
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(renderer);
        return "on (PC renderer)";
    }

    static void SetEnum(SerializedObject so, string path, int value)
    {
        var property = so.FindProperty(path);
        if (property != null) property.intValue = value;
    }

    static void SetFloat(SerializedObject so, string path, float value)
    {
        var property = so.FindProperty(path);
        if (property != null) property.floatValue = value;
    }

    /// <summary>Turns on ambient occlusion in the lightmaps of the open scene (indirect light only, so it adds to SSAO softly).</summary>
    static string EnableBakedAo()
    {
        LightingSettings lighting;
        try
        {
            lighting = Lightmapping.lightingSettings;
        }
        catch (System.Exception)
        {
            return "the scene has no Lighting Settings asset";
        }

        if (!lighting) return "the scene has no Lighting Settings asset";
        Undo.RecordObject(lighting, "Hall look");
        lighting.ao = true;
        lighting.aoMaxDistance = 1f;
        lighting.aoExponentIndirect = 1f;
        lighting.aoExponentDirect = 0f;
        EditorUtility.SetDirty(lighting);
        return "on (max distance 1 m, indirect)";
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

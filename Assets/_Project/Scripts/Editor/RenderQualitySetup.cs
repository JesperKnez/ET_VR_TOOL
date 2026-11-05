using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Einstein Telescope > Lighting > PC quality: HDR, reflection probes, render scale
/// For the demo setup: the 5070 Ti laptop streaming to the Quest over Air Link (Quality URP Config). The Quest build
/// (Performance URP Config) is left alone.
///  1. HDR on (with HDR colour grading): without it nothing is brighter than white, so bloom and tonemapping had
///     almost nothing to work with and highlights on metal and glass clipped flat.
///  2. Reflection probe box projection and blending on in the pipeline. RP_ControlRoom had box projection ticked,
///     but the pipeline switched the feature off, so it was ignored: reflections floated.
///  3. Reflection probes at 512 px, plus two probes for the west and east half of the hall (the single RP_Hall in
///     the middle made every tank and pipe reflect the centre of the hall). Capture points are kept out of the tanks.
///  4. Render scale 1.2: sharper text and thin lines. Not higher: over Air Link the stream's bitrate limits the
///     sharpness anyway, and the laptop GPU must keep 72/90 fps.
/// Also guards against the "radioactive glow" from the bake: small emissive materials (screens, Lumi) are set to
/// not contribute to baked GI. The ceiling panel material Light_Emission stays: it is the main light source.
/// Bake the lighting afterwards (probes and lightmaps).
/// </summary>
public static class RenderQualitySetup
{
    const string PC_PIPELINE = "Assets/Settings/Project Configuration/Quality URP Config.asset";
    const float RENDER_SCALE = 1.2f;
    const int PROBE_RESOLUTION = 512;

    // Emissive materials that may light the room in the bake. Everything else emissive is set to "no GI".
    static readonly HashSet<string> MainLightMaterials = new HashSet<string> { "Light_Emission", "Light_Panel" };

    // Hall: x -17.15..17.15, z -11..11, floor 0, ceiling 8. Control room in the corner x -17.15..-9.15, z -11..-3.
    static readonly (string name, Vector3 capture, Vector3 boxCentre, Vector3 boxSize)[] HallProbes =
    {
        ("RP_Hall_West", new Vector3(-8f, 2.2f, 3.5f), new Vector3(-8.6f, 4f, 0f), new Vector3(17.4f, 8.2f, 22.2f)),
        ("RP_Hall_East", new Vector3(8f, 2.2f, 3.5f), new Vector3(8.6f, 4f, 0f), new Vector3(17.4f, 8.2f, 22.2f)),
    };

    [MenuItem("Tools/Einstein Telescope/Lighting/PC quality: HDR, reflection probes, render scale")]
    static void Apply()
    {
        var log = new StringBuilder("[RenderQualitySetup] ");
        log.Append(SetPipeline()).Append(" ");
        log.Append(SetProbes()).Append(" ");
        log.Append(GuardEmissiveGi());
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log(log + " Bake the lighting again.");
    }

    static string SetPipeline()
    {
        var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PC_PIPELINE);
        if (!asset) return "PC pipeline not found.";
        var so = new SerializedObject(asset);
        Set(so, "m_SupportsHDR", 1);
        Set(so, "m_ColorGradingMode", 1);              // HDR grading
        Set(so, "m_ReflectionProbeBlending", 1);
        Set(so, "m_ReflectionProbeBoxProjection", 1);
        var scale = so.FindProperty("m_RenderScale");
        if (scale != null) scale.floatValue = RENDER_SCALE;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
        return $"PC pipeline: HDR on, box projection + blending on, render scale {RENDER_SCALE}.";
    }

    static void Set(SerializedObject so, string path, int value)
    {
        var p = so.FindProperty(path);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
        else p.intValue = value;
    }

    static string SetProbes()
    {
        GameObject hall = GameObject.Find("RP_Hall");
        Transform parent = hall ? hall.transform.parent : null;
        int added = 0;
        foreach (var (name, capture, boxCentre, boxSize) in HallProbes)
        {
            GameObject go = GameObject.Find(name);
            if (!go)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Hall probes");
                if (parent) go.transform.SetParent(parent, true);
                added++;
            }

            go.transform.position = capture;
            var probe = go.GetComponent<ReflectionProbe>();
            if (!probe) probe = Undo.AddComponent<ReflectionProbe>(go);
            probe.mode = ReflectionProbeMode.Baked;
            probe.importance = 1;
            probe.boxProjection = true;
            probe.size = boxSize;
            probe.center = boxCentre - capture;   // box in world space, capture point elsewhere
            probe.blendDistance = 1f;
            probe.hdr = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
        }

        // RP_Hall becomes the fallback only (importance 0): its capture point sits between the towers, so part of its
        // cubemap sees dark tank walls. With box projection those dark parts landed on props near the walls (the chrome
        // wire racks turned black). The west/east probes now win everywhere in the hall.
        // RP_ControlRoom keeps importance 2 and blend distance 0.1 (CLAUDE.md).
        if (hall)
        {
            var hallProbe = hall.GetComponent<ReflectionProbe>();
            if (hallProbe && hallProbe.importance != 0)
            {
                Undo.RecordObject(hallProbe, "Hall probe importance");
                hallProbe.importance = 0;
            }
        }

        int sharpened = 0;
        foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
        {
            if (probe.resolution != PROBE_RESOLUTION)
            {
                Undo.RecordObject(probe, "Probe resolution");
                probe.resolution = PROBE_RESOLUTION;
                sharpened++;
            }
        }

        return $"Reflection probes: {added} added (west/east hall), {sharpened} set to {PROBE_RESOLUTION} px.";
    }

    /// <summary>
    /// Small emissive materials get "Realtime emissive": realtime GI is off in this project, so they never light the
    /// room in the bake (the radioactive glow came from Baked emissive), but they keep glowing. "None" looked safer,
    /// but URP then strips the _EMISSION keyword and the material stops glowing altogether (Lumi went dark).
    /// </summary>
    static string GuardEmissiveGi()
    {
        var changed = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Materials" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!material || MainLightMaterials.Contains(material.name) || !material.HasProperty("_EmissionColor")) continue;

            Color emission = material.GetColor("_EmissionColor");
            bool emits = material.IsKeywordEnabled("_EMISSION") || emission.maxColorComponent > 0.5f;
            bool baked = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0;
            if (!emits && !baked) continue;
            if (!baked && material.IsKeywordEnabled("_EMISSION") && material.globalIlluminationFlags == MaterialGlobalIlluminationFlags.RealtimeEmissive) continue;

            Undo.RecordObject(material, "Emission GI");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (emits) material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            changed.Add(material.name);
        }

        return changed.Count == 0
            ? "Emissive GI: nothing to change."
            : "Small emissives set to Realtime emissive (glow, but no light in the bake): " + string.Join(", ", changed) + ".";
    }
}

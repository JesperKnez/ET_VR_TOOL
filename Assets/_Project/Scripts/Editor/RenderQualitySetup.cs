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
///  3. Reflection probes at 512 px. RP_Hall covers the whole hall from a capture point in the open (not between the
///     tanks); half-hall probes made the one-mesh towers reflect the wrong half. RP_ControlRoom keeps priority.
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
    // One probe for the whole hall. TowerConstellation is a single 25 m mesh, so Unity picks one probe set for all
    // tanks (by the mesh centre): with two half-hall probes it took the east one, and box projection then gave the
    // west tanks a reflection of the wrong half (black tanks). The capture point must be in the open: RP_Hall used to
    // sit between the tanks, so part of its cubemap saw dark tank walls (black chrome racks with box projection).
    static readonly Vector3 HallBoxCentre = new Vector3(0f, 4f, 0f);
    static readonly Vector3 HallBoxSize = new Vector3(34.6f, 8.2f, 22.2f);
    static readonly Vector3[] HallCaptureCandidates =
    {
        new Vector3(0f, 3f, 5f), new Vector3(0f, 3f, 8f), new Vector3(-2f, 3f, 6f), new Vector3(2f, 3f, 6f), new Vector3(0f, 4.5f, 5f)
    };

    // Tank centres (x, z) from CLAUDE.md, "Laser in de torens"
    static readonly Vector2[] Tanks =
    {
        new Vector2(2.6f, -7.7f), new Vector2(0f, -5.1f), new Vector2(-4.525f, -0.575f), new Vector2(4.525f, -0.575f),
        new Vector2(-11.172f, 6.072f), new Vector2(11.172f, 6.072f)
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
        if (!hall) return "RP_Hall not found.";

        // The half-hall probes from the previous version are no longer needed
        int removed = 0;
        foreach (string name in new[] { "RP_Hall_West", "RP_Hall_East" })
        {
            GameObject old = GameObject.Find(name);
            if (old) { Undo.DestroyObjectImmediate(old); removed++; }
        }

        Vector3 capture = PickCapturePoint(out string why);
        var probe = hall.GetComponent<ReflectionProbe>();
        Undo.RecordObject(hall.transform, "Hall probe");
        Undo.RecordObject(probe, "Hall probe");
        hall.transform.position = capture;
        probe.mode = ReflectionProbeMode.Baked;
        probe.importance = 2;                       // wins in the hall
        probe.boxProjection = true;
        probe.size = HallBoxSize;
        probe.center = HallBoxCentre - capture;     // box on the hall, capture point elsewhere
        probe.blendDistance = 1f;
        probe.hdr = true;

        // The control room stays on its own probe (higher than the hall), blend distance 0.1 (CLAUDE.md)
        GameObject room = GameObject.Find("RP_ControlRoom");
        var roomProbe = room ? room.GetComponent<ReflectionProbe>() : null;
        if (roomProbe && roomProbe.importance <= probe.importance)
        {
            Undo.RecordObject(roomProbe, "Control room probe");
            roomProbe.importance = probe.importance + 1;
        }

        int sharpened = 0;
        foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
        {
            if (p.resolution != PROBE_RESOLUTION)
            {
                Undo.RecordObject(p, "Probe resolution");
                p.resolution = PROBE_RESOLUTION;
                sharpened++;
            }
        }

        return $"Reflection probes: RP_Hall covers the whole hall, capture point {capture} ({why}), importance 2; " +
               $"RP_ControlRoom importance {(roomProbe ? roomProbe.importance : -1)}; {removed} half-hall probes removed; {sharpened} set to {PROBE_RESOLUTION} px.";
    }

    /// <summary>The first candidate at least 3 m from every tank and outside every prop.</summary>
    static Vector3 PickCapturePoint(out string why)
    {
        foreach (Vector3 c in HallCaptureCandidates)
        {
            bool clear = true;
            foreach (Vector2 t in Tanks)
            {
                if (Vector2.Distance(new Vector2(c.x, c.z), t) < 3f) clear = false;
            }

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                Vector3 size = r.bounds.size;
                if (size.x > 12f || size.z > 12f || r is LineRenderer || r is ParticleSystemRenderer) continue;   // hall, towers, crane
                if (r.bounds.Contains(c)) { clear = false; break; }
            }

            if (clear)
            {
                why = "clear of tanks and props";
                return c;
            }
        }

        why = "no candidate was clear, using the first";
        return HallCaptureCandidates[0];
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

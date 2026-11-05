using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Control room > Wall: painted plaster
/// Creates seamless textures (base color, metallic/smoothness, normal map) for a painted plaster wall:
/// off-white, matte (latex), with large, soft trowel marks and a fine roller texture, without seams.
/// Puts them on the existing material ControlRoom_Wall (which Room.prefab already assigns to the control room
/// walls), measures the UVs of those walls and sets the tiling so that one tile is 2 x 2 m.
/// Safe to run again: textures and material are updated, not replaced (GUIDs stay the same).
/// To change the look: see the function Plaster() below.
/// </summary>
public static class ControlRoomWallSetup
{
    const string TEX_DIR = "Assets/_Project/Textures/Room";
    const string MAT_PATH = "Assets/_Project/Materials/Room/ControlRoom_Wall.mat";
    const string TEX_NAME = "T_Plaster";
    const string OLD_TEX_NAME = "T_AcousticPanel";   // first version (panels with seams), gets cleaned up
    const int SIZE = 1024;
    const float TILE_METERS = 2f;                    // one texture tile = 2 x 2 m

    [MenuItem("Tools/Einstein Telescope/Control room/Wall: painted plaster")]
    static void Generate()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        if (!mat) { Debug.LogError($"[ControlRoomWallSetup] {MAT_PATH} not found."); return; }

        TowerMaterialSetup.EnsureFolder(TEX_DIR);
        try
        {
            BakeTextures();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", LoadTexture(TEX_NAME, "_BaseColor"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_MetallicGlossMap", LoadTexture(TEX_NAME, "_MetallicSmoothness"));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 1f);                 // multiplier on the A value of the map
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the alpha of the metallic map
        mat.SetTexture("_BumpMap", LoadTexture(TEX_NAME, "_Normal"));
        mat.SetFloat("_BumpScale", 1f);

        var report = new System.Text.StringBuilder("[ControlRoomWallSetup] Painted plaster applied to ControlRoom_Wall.\n");
        float tiling = MeasureTiling(mat, report);
        mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);   // also syncs _MainTex (for baking)
        EditorUtility.SetDirty(mat);

        foreach (string suffix in new[] { "_BaseColor", "_MetallicSmoothness", "_Normal" })
        {
            string old = $"{TEX_DIR}/{OLD_TEX_NAME}{suffix}.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(old)) AssetDatabase.DeleteAsset(old);
        }
        AssetDatabase.SaveAssets();

        report.AppendLine($"  Tiling {tiling:0.###} (one tile = {TILE_METERS} m). Bake the lighting again (Window > Rendering > Lighting > Generate Lighting).");
        Debug.Log(report.ToString(), mat);
    }

    struct Surface
    {
        public Color baseColor;   // sRGB
        public float smoothness;
        public float height;      // only for the normal map
    }

    // Painted plaster: off-white (±77% grey, not pure white: that overexposes when baking), latex matte.
    // Large, soft trowel marks (slightly lighter/glossier where it was pressed harder) and a fine
    // orange-peel texture from the paint roller. No seams, so the tile does not stand out.
    static Surface Plaster(float u, float v)
    {
        float cloud = TowerMaterialSetup.Fbm(u, v, 3, 3, 4, 17);        // slow tint variation over ±70 cm
        float trowel = TowerMaterialSetup.Fbm(u, v, 8, 5, 3, 31);       // trowel marks, slightly wider than tall
        float peel = TowerMaterialSetup.Fbm(u, v, 160, 160, 3, 43);     // roller texture (±1 cm)
        float grit = TowerMaterialSetup.Noise(u, v, 512, 512, 59);      // sand grain in the plaster

        float shade = (0.97f + 0.03f * cloud) * (0.98f + 0.02f * trowel) * (0.985f + 0.015f * peel);
        Color c = new Color(0.79f, 0.78f, 0.75f) * shade;
        c.a = 1f;

        return new Surface
        {
            baseColor = c,
            smoothness = Mathf.Clamp01(0.22f + (trowel - 0.5f) * 0.10f - (peel - 0.5f) * 0.06f),
            height = trowel * 0.5f + peel * 0.22f + grit * 0.05f,
        };
    }

    static void BakeTextures()
    {
        var albedo = new Color32[SIZE * SIZE];
        var mask = new Color32[SIZE * SIZE];
        var height = new float[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        {
            if (y % 64 == 0)
                EditorUtility.DisplayProgressBar("Control room", "Computing plaster", 0.9f * y / SIZE);
            for (int x = 0; x < SIZE; x++)
            {
                var s = Plaster((x + 0.5f) / SIZE, (y + 0.5f) / SIZE);
                int i = y * SIZE + x;
                albedo[i] = s.baseColor;
                mask[i] = new Color(0f, 0f, 0f, s.smoothness);   // URP Lit: metallic in R, smoothness in A
                height[i] = s.height;
            }
        }

        // Normal map from the height field (central differences, wrapping at the edges for seamless tiling)
        const float normalStrength = 1.5f;
        var normal = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            float dx = height[y * SIZE + (x + 1) % SIZE] - height[y * SIZE + (x + SIZE - 1) % SIZE];
            float dy = height[(y + 1) % SIZE * SIZE + x] - height[(y + SIZE - 1) % SIZE * SIZE + x];
            var n = new Vector3(-dx * normalStrength, -dy * normalStrength, 1f).normalized;
            normal[y * SIZE + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        WriteTexture("_BaseColor", albedo, srgb: true, isNormal: false);
        WriteTexture("_MetallicSmoothness", mask, srgb: false, isNormal: false);
        WriteTexture("_Normal", normal, srgb: false, isNormal: true);
    }

    static void WriteTexture(string suffix, Color32[] pixels, bool srgb, bool isNormal)
    {
        string path = $"{TEX_DIR}/{TEX_NAME}{suffix}.png";
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;   // walls are often seen at an angle; without aniso the texture blurs
        importer.maxTextureSize = SIZE;
        importer.SaveAndReimport();
    }

    static Texture2D LoadTexture(string name, string suffix) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/{name}{suffix}.png");

    /// <summary>
    /// Finds the renderers in the open scene that use ControlRoom_Wall and measures on that submesh how many
    /// meters one UV unit is. No usable UVs: tiling 1/2 (UVs in meters, as with Hall_Wall).
    /// </summary>
    static float MeasureTiling(Material mat, System.Text.StringBuilder report)
    {
        var samples = new List<(float metersPerUv, float area)>();
        float noUv = 0f;
        var users = new List<string>();

        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var mats = mr.sharedMaterials;
            var mf = mr.GetComponent<MeshFilter>();
            var mesh = mf ? mf.sharedMesh : null;
            if (!mesh) continue;

            for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
            {
                if (mats[s] != mat) continue;
                users.Add(mr.name);

                var verts = mesh.vertices;
                var uvs = mesh.uv;
                var toWorld = mr.transform.localToWorldMatrix;
                var tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[t]]);
                    Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
                    Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[t + 2]]);
                    float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    if (area < 1e-8f) continue;

                    float uvArea = 0f;
                    if (uvs.Length == verts.Length)
                    {
                        Vector2 ua = uvs[tris[t]], ub = uvs[tris[t + 1]], uc = uvs[tris[t + 2]];
                        uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * 0.5f;
                    }

                    if (uvArea < 1e-10f) noUv += area;
                    else samples.Add((Mathf.Sqrt(area / uvArea), area));
                }
            }
        }

        if (users.Count == 0)
        {
            report.AppendLine("  NOTE: no renderer in the open scene uses ControlRoom_Wall. Open Room_Scene and run again.");
            return 1f / TILE_METERS;
        }

        report.AppendLine($"  Used by: {string.Join(", ", users.Distinct())}");
        float total = samples.Sum(x => x.area) + noUv;
        if (samples.Count == 0)
        {
            report.AppendLine($"  PROBLEM: the walls have no usable UVs ({total:0.0} m²). Unwrap in Blender (Cube Projection).");
            return 1f / TILE_METERS;
        }

        float p10 = TowerMaterialSetup.Percentile(samples, 0.1f);
        float p50 = TowerMaterialSetup.Percentile(samples, 0.5f);
        float p90 = TowerMaterialSetup.Percentile(samples, 0.9f);
        float badPct = 100f * noUv / total;
        string verdict = badPct > 5f || p90 / Mathf.Max(p10, 1e-6f) > 1.5f
            ? "PROBLEM: uneven UVs, the texture is not the same size everywhere. Unwrap again in Blender (Cube Projection, scale applied)."
            : "ok";
        report.AppendLine($"  UVs: {total:0.0} m², 1 UV unit = {p50:0.00} m (10–90%: {p10:0.00}–{p90:0.00} m), without UV {badPct:0}% → {verdict}");
        return p50 / TILE_METERS;
    }
}

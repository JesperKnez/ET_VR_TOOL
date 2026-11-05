using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Towers > ...
/// PBR metal for TowerConstellation, in three steps:
///  1. Generates seamless textures (base color, metallic/smoothness, normal map) for brushed and
///     blasted stainless steel and creates URP Lit materials, each with a "_Clear" twin (transparent) for TowerTransparency.
///  2. Assigns the materials to the material slots of TowerConstellation.fbx and puts TowerTransparency
///     on the TowerConstellation object in the open scene.
///  3. Measures the UVs of the FBX (meters per UV unit) and sets the tiling so that one texture tile is 1 x 1 m.
/// Safe to run again: existing textures and materials are updated, not replaced (GUIDs stay the same).
/// </summary>
public static class TowerMaterialSetup
{
    const string TEX_DIR = "Assets/_Project/Textures/Tower";
    const string MAT_DIR = "Assets/_Project/Materials/Tower/PBR";
    const string FBX_PATH = "Assets/_Project/Models/Room/TowerConstellation.fbx";
    const string OLD_WELD = "Assets/_Project/Materials/Tower/Weld 3K/WeldTube.mat";
    const string OLD_BOLTS = "Assets/_Project/Materials/Tower/Bolts/BoltDecal.mat";
    const string TOWER_OBJECT = "TowerConstellation";
    const int SIZE = 1024;
    const float TILE_METERS = 1f;   // one texture tile covers 1 x 1 m

    const string BRUSHED = "M_Tower_BrushedSteel";
    const string BLASTED = "M_Tower_BlastedSteel";
    const string WELD = "M_Tower_Weld";
    const string BOLTS = "M_Tower_Bolts";

    // Material slot in the FBX → new material
    static readonly (string slot, string material)[] Slots =
    {
        // Note: in the FBX the tank shells and beam tubes are on the slot "Beam_Glass" (the name is misleading).
        // Champagne stainless steel as in the photos. Without a material on this slot everything turns bright pink.
        ("Beam_Glass", BLASTED),
        // Assumption based on David's names: "Yellowy" = warm stainless steel, "Polished" = the lighter, neutral steel
        // (flanges, port covers). If that is wrong: swap them here.
        ("PolishedSteel", BRUSHED),
        ("Yellowy_Steel", BLASTED),
        ("WeldTube", WELD),
        ("BoltDecal", BOLTS),
    };

    // ---------------------------------------------------------------- 1. textures + materials

    [MenuItem("Tools/Einstein Telescope/Towers/1. Generate PBR textures and materials")]
    static void Generate()
    {
        EnsureFolder(TEX_DIR);
        EnsureFolder(MAT_DIR);
        try
        {
            BakeTextures("T_BrushedSteel", BrushedSteel, 1.5f, 0.0f);
            BakeTextures("T_BlastedSteel", BlastedSteel, 3f, 0.5f);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        var brushed = GetOrCreateMaterial(BRUSHED, null);
        SetupTexturedMetal(brushed, "T_BrushedSteel");

        var blasted = GetOrCreateMaterial(BLASTED, null);
        SetupTexturedMetal(blasted, "T_BlastedSteel");

        // Weld and bolts keep their own detail textures (weld bead, bolt heads); only the PBR values become real stainless steel
        var weld = GetOrCreateMaterial(WELD, AssetDatabase.LoadAssetAtPath<Material>(OLD_WELD));
        weld.SetColor("_BaseColor", new Color(0.64f, 0.58f, 0.49f));   // welds are slightly darker/warmer in the photos
        weld.SetFloat("_Metallic", 1f);
        weld.SetFloat("_Smoothness", 0.40f);

        var bolts = GetOrCreateMaterial(BOLTS, AssetDatabase.LoadAssetAtPath<Material>(OLD_BOLTS));
        bolts.SetFloat("_Metallic", 1f);
        bolts.SetFloat("_Smoothness", 0.55f);

        foreach (var m in new[] { brushed, blasted, weld, bolts })
        {
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(m);
            UpdateClearTwin(m);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TowerMaterialSetup] Textures in {TEX_DIR}, materials in {MAT_DIR}. Next step: 2. Assign.");
    }

    struct Surface
    {
        public Color baseColor;   // sRGB
        public float smoothness;
        public float height;      // only for the normal map
    }

    // Brushed, neutral stainless steel (pipes, flanges): long fine lines along U, satin sheen
    static Surface BrushedSteel(float u, float v)
    {
        float streak = Fbm(u, v, 2, 256, 2, 11);   // long lines along U (visible up close)
        float grain = Fbm(u, v, 96, 96, 2, 23);
        float patch = Fbm(u, v, 6, 6, 3, 31);      // patches of 10-30 cm: break up the reflection at a distance
        float cloud = Fbm(u, v, 2, 2, 3, 37);      // slow variation, so it does not look CG-flat
        float shade = (0.92f + 0.08f * streak) * (0.94f + 0.06f * patch) * (0.97f + 0.03f * cloud);
        return new Surface
        {
            baseColor = Shade(new Color(0.80f, 0.79f, 0.77f), shade),
            smoothness = Mathf.Clamp01(0.62f + (streak - 0.5f) * 0.14f + (patch - 0.5f) * 0.25f - (cloud - 0.5f) * 0.10f),
            height = streak * 0.8f + grain * 0.2f,
        };
    }

    // Blasted stainless steel (tank shells): champagne-coloured and satin-matte as in the ETpathfinder photos,
    // with soft, broad reflections and a light stain here and there
    static Surface BlastedSteel(float u, float v)
    {
        float grain = Fbm(u, v, 128, 128, 2, 41);
        float pits = Noise(u, v, 512, 512, 53);
        float patch = Fbm(u, v, 6, 6, 3, 59);
        float cloud = Fbm(u, v, 4, 4, 4, 67);
        float stain = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.78f, Fbm(u, v, 5, 5, 3, 71)));
        float shade = (0.95f + 0.05f * grain) * (0.95f + 0.05f * patch) * (0.97f + 0.03f * cloud) * (1f - 0.12f * stain);
        return new Surface
        {
            baseColor = Shade(new Color(0.80f, 0.73f, 0.60f), shade),
            smoothness = Mathf.Clamp01(0.50f + (grain - 0.5f) * 0.10f + (patch - 0.5f) * 0.20f - (cloud - 0.5f) * 0.06f - 0.10f * stain),
            height = grain * 0.6f + pits * 0.4f,
        };
    }

    static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

    /// <param name="normalStrength">How strongly the height difference shows up in the normal map.</param>
    /// <param name="progressStart">Only for the progress bar.</param>
    static void BakeTextures(string name, System.Func<float, float, Surface> surface, float normalStrength, float progressStart)
    {
        var albedo = new Color32[SIZE * SIZE];
        var mask = new Color32[SIZE * SIZE];
        var height = new float[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        {
            if (y % 64 == 0)
                EditorUtility.DisplayProgressBar("Towers", $"Computing {name}",progressStart + 0.5f * y / SIZE);
            for (int x = 0; x < SIZE; x++)
            {
                var s = surface((x + 0.5f) / SIZE, (y + 0.5f) / SIZE);
                int i = y * SIZE + x;
                albedo[i] = s.baseColor;
                mask[i] = new Color(1f, 0f, 0f, s.smoothness);   // URP Lit: metallic in R, smoothness in A
                height[i] = s.height;
            }
        }

        // Normal map from the height field (central differences, wrapping at the edges for seamless tiling)
        var normal = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            float dx = height[y * SIZE + (x + 1) % SIZE] - height[y * SIZE + (x + SIZE - 1) % SIZE];
            float dy = height[(y + 1) % SIZE * SIZE + x] - height[(y + SIZE - 1) % SIZE * SIZE + x];
            var n = new Vector3(-dx * normalStrength, -dy * normalStrength, 1f).normalized;
            normal[y * SIZE + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        WriteTexture(name + "_BaseColor", albedo, srgb: true, isNormal: false);
        WriteTexture(name + "_MetallicSmoothness", mask, srgb: false, isNormal: false);
        WriteTexture(name + "_Normal", normal, srgb: false, isNormal: true);
    }

    static void WriteTexture(string name, Color32[] pixels, bool srgb, bool isNormal)
    {
        string path = $"{TEX_DIR}/{name}.png";
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
        importer.anisoLevel = 8;   // tall tanks are seen at an angle; without aniso the lines blur
        importer.maxTextureSize = SIZE;
        importer.SaveAndReimport();
    }

    static Texture2D LoadTexture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/{name}.png");

    static void SetupTexturedMetal(Material m, string textureSet)
    {
        m.SetTexture("_BaseMap", LoadTexture(textureSet + "_BaseColor"));
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_MetallicGlossMap", LoadTexture(textureSet + "_MetallicSmoothness"));
        m.SetFloat("_Metallic", 1f);
        m.SetFloat("_Smoothness", 1f);                 // multiplier on the A value of the map
        m.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the alpha of the metallic map
        m.SetTexture("_BumpMap", LoadTexture(textureSet + "_Normal"));
        m.SetFloat("_BumpScale", 1f);
    }

    static Material GetOrCreateMaterial(string name, Material template)
    {
        string path = $"{MAT_DIR}/{name}.mat";
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = template ? new Material(template) : new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (template)
        {
            mat.CopyPropertiesFromMaterial(template);
        }
        mat.shader = lit;
        return mat;
    }

    /// <summary>Copy of the opaque material with Surface Type Transparent (alpha blend), both sides visible.</summary>
    static void UpdateClearTwin(Material opaque)
    {
        string path = $"{MAT_DIR}/{opaque.name}_Clear.mat";
        var clear = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!clear)
        {
            clear = new Material(opaque);
            AssetDatabase.CreateAsset(clear, path);
        }
        clear.shader = opaque.shader;
        clear.CopyPropertiesFromMaterial(opaque);
        clear.SetFloat("_Surface", (float)BaseShaderGUI.SurfaceType.Transparent);
        clear.SetFloat("_Blend", (float)BaseShaderGUI.BlendMode.Alpha);
        clear.SetFloat("_BlendModePreserveSpecular", 0f);   // otherwise metal (all specular) stays brightly visible
        clear.SetFloat("_Cull", (float)BaseShaderGUI.RenderFace.Both);   // also show the back wall of the tank
        BaseShaderGUI.SetMaterialKeywords(clear, LitGUI.SetMaterialKeywords);
        BaseShaderGUI.SetupMaterialBlendMode(clear);
        EditorUtility.SetDirty(clear);
    }

    // ---------------------------------------------------------------- 2. assign

    [MenuItem("Tools/Einstein Telescope/Towers/2. Assign to TowerConstellation")]
    static void Assign()
    {
        var importer = AssetImporter.GetAtPath(FBX_PATH) as ModelImporter;
        if (!importer) { Debug.LogError($"[TowerMaterialSetup] {FBX_PATH} not found."); return; }

        foreach (var (slot, name) in Slots)
        {
            var mat = LoadMaterial(name);
            if (!mat) { Debug.LogError($"[TowerMaterialSetup] {name} does not exist. Run step 1 first."); return; }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
        }
        importer.SaveAndReimport();

        var tower = GameObject.Find(TOWER_OBJECT);
        if (!tower)
        {
            Debug.LogWarning($"[TowerMaterialSetup] FBX assigned, but '{TOWER_OBJECT}' is not in the open scene. TowerTransparency not added.");
            return;
        }

        // A material override in the scene wins over the FBX remap (that is how PolishedSteel was still on it): revert it
        int reverted = 0;
        foreach (var r in tower.GetComponentsInChildren<Renderer>(true))
        {
            var prop = new SerializedObject(r).FindProperty("m_Materials");
            if (prop == null || !prop.prefabOverride) continue;
            PrefabUtility.RevertPropertyOverride(prop, InteractionMode.UserAction);
            reverted++;
        }
        if (reverted > 0) Debug.Log($"[TowerMaterialSetup] Reverted {reverted} material override(s) in the scene.");

        var tt = tower.GetComponent<TowerTransparency>();
        if (!tt) tt = Undo.AddComponent<TowerTransparency>(tower);
        Undo.RecordObject(tt, "Setup TowerTransparency");
        tt.materials = Slots.Select(s => s.material).Distinct().Select(name => new TowerTransparency.MaterialPair
        {
            opaque = LoadMaterial(name),
            clear = LoadMaterial(name + "_Clear"),
        }).ToArray();
        if (!tt.levelManager) tt.levelManager = Object.FindAnyObjectByType<EinsteinTelescope.Laser.LaserLevelManager>();
        EditorUtility.SetDirty(tt);
        EditorSceneManager.MarkSceneDirty(tower.scene);

        Selection.activeGameObject = tower;
        Debug.Log("[TowerMaterialSetup] Materials assigned and TowerTransparency set up. Save the scene and bake the lighting again.");
    }

    static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MAT_DIR}/{name}.mat");

    // ---------------------------------------------------------------- 3. UV check + tiling

    [MenuItem("Tools/Einstein Telescope/Towers/3. Check UVs and set tiling")]
    static void CheckUVs()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FBX_PATH);
        if (!model) { Debug.LogError($"[TowerMaterialSetup] {FBX_PATH} not found."); return; }

        // Per material: (meters per UV unit, area in m²) per triangle
        var samples = new Dictionary<string, List<(float metersPerUv, float area)>>();
        var noUv = new Dictionary<string, float>();

        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            var mr = mf.GetComponent<MeshRenderer>();
            if (!mesh || !mr) continue;

            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var toWorld = mf.transform.localToWorldMatrix;
            var mats = mr.sharedMaterials;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string key = s < mats.Length && mats[s] ? mats[s].name : $"{mesh.name}[{s}]";
                if (!samples.TryGetValue(key, out var list)) samples[key] = list = new List<(float, float)>();

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

                    if (uvArea < 1e-10f)
                        noUv[key] = (noUv.TryGetValue(key, out var n) ? n : 0f) + area;
                    else
                        list.Add((Mathf.Sqrt(area / uvArea), area));
                }
            }
        }

        var report = new System.Text.StringBuilder("[TowerMaterialSetup] UV check of TowerConstellation.fbx\n");
        foreach (var kv in samples.OrderBy(k => k.Key))
        {
            var list = kv.Value;
            float total = list.Sum(x => x.area) + (noUv.TryGetValue(kv.Key, out var bad) ? bad : 0f);
            if (total <= 0f) continue;

            float badPct = 100f * bad / total;
            if (list.Count == 0)
            {
                report.AppendLine($"  {kv.Key}: NO usable UVs ({total:0.0} m²). Unwrap in Blender or use a triplanar shader.");
                continue;
            }

            float p10 = Percentile(list, 0.1f), p50 = Percentile(list, 0.5f), p90 = Percentile(list, 0.9f);
            float spread = p90 / Mathf.Max(p10, 1e-6f);
            string verdict = badPct > 5f || spread > 4f
                ? "PROBLEM: uneven or missing UVs, textures get stretched. Unwrap again (Smart UV Project) or use triplanar."
                : "ok";
            report.AppendLine($"  {kv.Key}: {total:0.0} m², 1 UV unit = {p50:0.00} m (10–90%: {p10:0.00}–{p90:0.00} m), " +
                              $"without UV {badPct:0}% → {verdict}");

            // Only the generated metals get a custom tiling; weld and bolts have their own UV layouts
            string target = kv.Key == "PolishedSteel" || kv.Key == BRUSHED ? BRUSHED
                          : kv.Key == "Yellowy_Steel" || kv.Key == "Beam_Glass" || kv.Key == BLASTED ? BLASTED
                          : null;
            if (target == null) continue;

            float tiling = p50 / TILE_METERS;
            foreach (var mat in new[] { LoadMaterial(target), LoadMaterial(target + "_Clear") })
            {
                if (!mat) continue;
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);   // also syncs _MainTex (for baking)
                EditorUtility.SetDirty(mat);
            }
            report.AppendLine($"    → tiling of {target} (and _Clear) set to {tiling:0.##}.");
        }

        AssetDatabase.SaveAssets();
        CheckProbes(report);
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Metal only shows what it reflects. If the capture point of a reflection probe is inside a tower,
    /// the probe only sees the dark inside and all the metal turns black.
    /// Test: cast 6 rays from the probe at the tower model (back faces count too).
    /// </summary>
    static void CheckProbes(System.Text.StringBuilder report)
    {
        var tower = GameObject.Find(TOWER_OBJECT);
        if (!tower) return;

        var temp = new List<GameObject>();
        bool oldBackfaces = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = true;
            foreach (var mf in tower.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var go = new GameObject("TowerProbeCheck") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                temp.Add(go);
            }
            Physics.SyncTransforms();

            var dirs = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>())
            {
                Vector3 p = probe.transform.position;
                int hits = 0;
                foreach (var d in dirs)
                    foreach (var hit in Physics.RaycastAll(p, d, 4f))
                        if (temp.Contains(hit.collider.gameObject)) { hits++; break; }

                string verdict = hits >= 5
                    ? "IS INSIDE a tower: all the metal reflects the dark inside. Move the probe to a free spot " +
                      "(adjust Box Offset so the box stays the same) and bake again."
                    : "clear";
                report.AppendLine($"  Reflection probe {probe.name} at {p}: {hits}/6 directions hit a tower → {verdict}");
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
            foreach (var go in temp) Object.DestroyImmediate(go);
        }
    }

    // Area-weighted percentile
    internal static float Percentile(List<(float value, float weight)> list, float p)
    {
        var sorted = list.OrderBy(x => x.value).ToList();
        float total = sorted.Sum(x => x.weight), acc = 0f;
        foreach (var x in sorted)
        {
            acc += x.weight;
            if (acc >= p * total) return x.value;
        }
        return sorted[sorted.Count - 1].value;
    }

    // ---------------------------------------------------------------- seamless noise

    internal static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Value noise with px × py cells over the unit square; wraps around the edges (seamless).</summary>
    internal static float Noise(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x1 = (x0 + 1) % px, y1 = (y0 + 1) % py;
        x0 %= px;
        y0 %= py;
        float top = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
        float bottom = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    internal static float Fbm(float u, float v, int px, int py, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(u, v, px, py, seed + o * 101);
            norm += amp;
            amp *= 0.5f;
            px *= 2;
            py *= 2;
        }
        return sum / norm;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

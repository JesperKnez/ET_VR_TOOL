using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Torens > ...
/// PBR-metaal voor TowerConstellation, in drie stappen:
///  1. Genereert naadloze textures (base color, metallic/smoothness, normal map) voor geborsteld en
///     gestraald RVS en maakt URP Lit-materialen, elk met een "_Clear"-tweeling (transparant) voor TowerTransparency.
///  2. Koppelt de materialen aan de material slots van TowerConstellation.fbx en zet TowerTransparency
///     op het object TowerConstellation in de open scene.
///  3. Meet de UV's van de FBX (meter per UV-eenheid) en zet de tiling zo dat één texture-tegel 1 x 1 m is.
/// Veilig om opnieuw te draaien: bestaande textures en materialen worden bijgewerkt, niet vervangen (GUID's blijven).
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
    const float TILE_METERS = 1f;   // één texture-tegel beslaat 1 x 1 m

    const string BRUSHED = "M_Tower_BrushedSteel";
    const string BLASTED = "M_Tower_BlastedSteel";
    const string WELD = "M_Tower_Weld";
    const string BOLTS = "M_Tower_Bolts";

    // Material slot in de FBX → nieuw materiaal
    static readonly (string slot, string material)[] Slots =
    {
        // Let op: in de FBX staan de tankmantels en bundelbuizen op het slot "Beam_Glass" (naam is misleidend).
        // Champagne RVS zoals op de foto's. Zonder materiaal op dit slot wordt alles felroze.
        ("Beam_Glass", BLASTED),
        // Aanname op basis van Davids namen: "Yellowy" = warm RVS, "Polished" = het lichtere, neutrale RVS
        // (flenzen, poortdeksels). Klopt dat niet: hier omwisselen.
        ("PolishedSteel", BRUSHED),
        ("Yellowy_Steel", BLASTED),
        ("WeldTube", WELD),
        ("BoltDecal", BOLTS),
    };

    // ---------------------------------------------------------------- 1. textures + materialen

    [MenuItem("Tools/Einstein Telescope/Torens/1. PBR-textures en materialen genereren")]
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

        // Las en bouten houden hun eigen detailtextures (lasrups, boutkoppen); alleen de PBR-waarden worden echt RVS
        var weld = GetOrCreateMaterial(WELD, AssetDatabase.LoadAssetAtPath<Material>(OLD_WELD));
        weld.SetColor("_BaseColor", new Color(0.64f, 0.58f, 0.49f));   // lasnaden zijn op de foto's iets donkerder/warmer
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
        Debug.Log($"[TowerMaterialSetup] Textures in {TEX_DIR}, materialen in {MAT_DIR}. Volgende stap: 2. Koppelen.");
    }

    struct Surface
    {
        public Color baseColor;   // sRGB
        public float smoothness;
        public float height;      // alleen voor de normal map
    }

    // Geborsteld, neutraal RVS (leidingen, flenzen): lange fijne lijnen langs U, satijnglans
    static Surface BrushedSteel(float u, float v)
    {
        float streak = Fbm(u, v, 2, 256, 2, 11);   // lange lijnen langs U (zie je van dichtbij)
        float grain = Fbm(u, v, 96, 96, 2, 23);
        float patch = Fbm(u, v, 6, 6, 3, 31);      // vlekken van 10-30 cm: breken de reflectie op afstand
        float cloud = Fbm(u, v, 2, 2, 3, 37);      // trage variatie, zodat het niet CG-vlak oogt
        float shade = (0.92f + 0.08f * streak) * (0.94f + 0.06f * patch) * (0.97f + 0.03f * cloud);
        return new Surface
        {
            baseColor = Shade(new Color(0.80f, 0.79f, 0.77f), shade),
            smoothness = Mathf.Clamp01(0.62f + (streak - 0.5f) * 0.14f + (patch - 0.5f) * 0.25f - (cloud - 0.5f) * 0.10f),
            height = streak * 0.8f + grain * 0.2f,
        };
    }

    // Gestraald RVS (tankmantels): champagnekleurig en satijnmat zoals op de foto's van ETpathfinder,
    // met zachte, brede reflecties en hier en daar een lichte vlek
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

    /// <param name="normalStrength">Hoe sterk het hoogteverschil in de normal map doorwerkt.</param>
    /// <param name="progressStart">Alleen voor de voortgangsbalk.</param>
    static void BakeTextures(string name, System.Func<float, float, Surface> surface, float normalStrength, float progressStart)
    {
        var albedo = new Color32[SIZE * SIZE];
        var mask = new Color32[SIZE * SIZE];
        var height = new float[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        {
            if (y % 64 == 0)
                EditorUtility.DisplayProgressBar("Torens", $"{name} berekenen", progressStart + 0.5f * y / SIZE);
            for (int x = 0; x < SIZE; x++)
            {
                var s = surface((x + 0.5f) / SIZE, (y + 0.5f) / SIZE);
                int i = y * SIZE + x;
                albedo[i] = s.baseColor;
                mask[i] = new Color(1f, 0f, 0f, s.smoothness);   // URP Lit: metallic in R, smoothness in A
                height[i] = s.height;
            }
        }

        // Normal map uit het hoogteveld (centrale differenties, rond de randen voor naadloos tegelen)
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
        importer.anisoLevel = 8;   // hoge tanks zie je onder een schuine hoek; zonder aniso worden de lijnen vaag
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
        m.SetFloat("_Smoothness", 1f);                 // vermenigvuldiger op de A-waarde van de map
        m.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness uit de alpha van de metallic map
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

    /// <summary>Kopie van het opaque materiaal met Surface Type Transparent (alpha blend), beide kanten zichtbaar.</summary>
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
        clear.SetFloat("_BlendModePreserveSpecular", 0f);   // anders blijft metaal (alles specular) fel zichtbaar
        clear.SetFloat("_Cull", (float)BaseShaderGUI.RenderFace.Both);   // achterwand van de tank ook zien
        BaseShaderGUI.SetMaterialKeywords(clear, LitGUI.SetMaterialKeywords);
        BaseShaderGUI.SetupMaterialBlendMode(clear);
        EditorUtility.SetDirty(clear);
    }

    // ---------------------------------------------------------------- 2. koppelen

    [MenuItem("Tools/Einstein Telescope/Torens/2. Koppelen aan TowerConstellation")]
    static void Assign()
    {
        var importer = AssetImporter.GetAtPath(FBX_PATH) as ModelImporter;
        if (!importer) { Debug.LogError($"[TowerMaterialSetup] {FBX_PATH} niet gevonden."); return; }

        foreach (var (slot, name) in Slots)
        {
            var mat = LoadMaterial(name);
            if (!mat) { Debug.LogError($"[TowerMaterialSetup] {name} bestaat niet. Draai eerst stap 1."); return; }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
        }
        importer.SaveAndReimport();

        var tower = GameObject.Find(TOWER_OBJECT);
        if (!tower)
        {
            Debug.LogWarning($"[TowerMaterialSetup] FBX gekoppeld, maar '{TOWER_OBJECT}' staat niet in de open scene. TowerTransparency niet toegevoegd.");
            return;
        }

        // Een material-override in de scene wint van de FBX-remap (zo hing PolishedSteel er nog op): terugdraaien
        int reverted = 0;
        foreach (var r in tower.GetComponentsInChildren<Renderer>(true))
        {
            var prop = new SerializedObject(r).FindProperty("m_Materials");
            if (prop == null || !prop.prefabOverride) continue;
            PrefabUtility.RevertPropertyOverride(prop, InteractionMode.UserAction);
            reverted++;
        }
        if (reverted > 0) Debug.Log($"[TowerMaterialSetup] {reverted} material-override(s) in de scene teruggedraaid.");

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
        Debug.Log("[TowerMaterialSetup] Materialen gekoppeld en TowerTransparency ingesteld. Sla de scene op en bak de belichting opnieuw.");
    }

    static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MAT_DIR}/{name}.mat");

    // ---------------------------------------------------------------- 3. UV-check + tiling

    [MenuItem("Tools/Einstein Telescope/Torens/3. UV's controleren en tiling instellen")]
    static void CheckUVs()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FBX_PATH);
        if (!model) { Debug.LogError($"[TowerMaterialSetup] {FBX_PATH} niet gevonden."); return; }

        // Per materiaal: (meter per UV-eenheid, oppervlak in m²) per driehoek
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

        var report = new System.Text.StringBuilder("[TowerMaterialSetup] UV-check van TowerConstellation.fbx\n");
        foreach (var kv in samples.OrderBy(k => k.Key))
        {
            var list = kv.Value;
            float total = list.Sum(x => x.area) + (noUv.TryGetValue(kv.Key, out var bad) ? bad : 0f);
            if (total <= 0f) continue;

            float badPct = 100f * bad / total;
            if (list.Count == 0)
            {
                report.AppendLine($"  {kv.Key}: GEEN bruikbare UV's ({total:0.0} m²). Unwrappen in Blender of een triplanar shader gebruiken.");
                continue;
            }

            float p10 = Percentile(list, 0.1f), p50 = Percentile(list, 0.5f), p90 = Percentile(list, 0.9f);
            float spread = p90 / Mathf.Max(p10, 1e-6f);
            string verdict = badPct > 5f || spread > 4f
                ? "PROBLEEM: ongelijke of ontbrekende UV's, textures rekken uit. Opnieuw unwrappen (Smart UV Project) of triplanar."
                : "ok";
            report.AppendLine($"  {kv.Key}: {total:0.0} m², 1 UV-eenheid = {p50:0.00} m (10–90%: {p10:0.00}–{p90:0.00} m), " +
                              $"zonder UV {badPct:0}% → {verdict}");

            // Alleen de gegenereerde metalen krijgen een tiling op maat; las en bouten hebben eigen UV-layouts
            string target = kv.Key == "PolishedSteel" || kv.Key == BRUSHED ? BRUSHED
                          : kv.Key == "Yellowy_Steel" || kv.Key == "Beam_Glass" || kv.Key == BLASTED ? BLASTED
                          : null;
            if (target == null) continue;

            float tiling = p50 / TILE_METERS;
            foreach (var mat in new[] { LoadMaterial(target), LoadMaterial(target + "_Clear") })
            {
                if (!mat) continue;
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);   // zet ook _MainTex gelijk (voor het baken)
                EditorUtility.SetDirty(mat);
            }
            report.AppendLine($"    → tiling van {target} (en _Clear) op {tiling:0.##} gezet.");
        }

        AssetDatabase.SaveAssets();
        CheckProbes(report);
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Metaal toont alleen wat het weerspiegelt. Staat het opnamepunt van een reflection probe binnenin een toren,
    /// dan ziet de probe alleen de donkere binnenkant en wordt al het metaal zwart.
    /// Test: vanaf de probe 6 stralen schieten naar het torenmodel (achterkanten tellen mee).
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
                    ? "ZIT BINNENIN een toren: al het metaal weerspiegelt de donkere binnenkant. Verplaats de probe naar een vrije plek " +
                      "(vul Box Offset aan zodat de box gelijk blijft) en bak opnieuw."
                    : "vrij";
                report.AppendLine($"  Reflection probe {probe.name} op {p}: {hits}/6 richtingen raken een toren → {verdict}");
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
            foreach (var go in temp) Object.DestroyImmediate(go);
        }
    }

    // Oppervlakgewogen percentiel
    static float Percentile(List<(float value, float weight)> list, float p)
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

    // ---------------------------------------------------------------- naadloze ruis

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Value noise met px × py cellen over het eenheidsvierkant; loopt over de randen door (naadloos).</summary>
    static float Noise(float u, float v, int px, int py, int seed)
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

    static float Fbm(float u, float v, int px, int py, int octaves, int seed)
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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

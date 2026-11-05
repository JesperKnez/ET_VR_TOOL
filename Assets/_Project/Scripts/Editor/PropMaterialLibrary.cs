using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Real PBR materials for the props (desk, mast lift, control cabinets, ETP_* kits).
///
/// The FBX files carry embedded plain-colour materials, and every metal in them has metallic 0: stainless steel,
/// aluminium and chrome all render as grey plastic. The kits share material names (Steel_Stainless, Paint_Yellow, ...),
/// so this builds ONE library in Materials/Props/PBR (one material per name) and remaps every model to it by name.
///
/// Surfaces get procedural detail (Textures/Props): powder-coat orange peel, brushed and galvanised metal, plastic and
/// rubber grain, plus a little colour and gloss variation, so large panels no longer look like one flat colour.
/// Run again after changing a rule: existing assets are updated in place (GUIDs stay).
/// </summary>
static class PropMaterialLibrary
{
    const string MAT_DIR = "Assets/_Project/Materials/Props/PBR";
    const string TEX_DIR = "Assets/_Project/Textures/Props";
    const int SIZE = 512;

    enum Finish { PowderCoat, Plastic, Rubber, Brushed, Polished, Galvanised, DarkMetal, Wood, Foam, Label, Emissive, Keep }

    struct Rule
    {
        public Finish finish;
        public Color? colour;      // null = keep the FBX colour
        public float smoothness;
        public float metallic;
    }

    static Rule R(Finish f, float smooth, float metal = 0f, Color? colour = null) =>
        new Rule { finish = f, smoothness = smooth, metallic = metal, colour = colour };

    /// <summary>The rule for a material name. Order matters: the first match wins.</summary>
    static Rule RuleFor(string name)
    {
        string n = name.ToLowerInvariant();
        if (n == "screen" || n.Contains("glass") || n.StartsWith("hole")) return R(Finish.Keep, 0f);
        if (n.StartsWith("lamp") || n.StartsWith("led") || n.StartsWith("beacon")) return R(Finish.Emissive, 0.8f);
        if (n.Contains("chrome")) return R(Finish.Polished, 0.9f, 1f, new Color(0.86f, 0.87f, 0.88f));
        if (n.Contains("steel_bright")) return R(Finish.Polished, 0.8f, 1f, new Color(0.80f, 0.81f, 0.82f));
        if (n.Contains("stainless") || n.Contains("brushed") || n == "monitor stand") return R(Finish.Brushed, 0.45f, 1f, new Color(0.74f, 0.74f, 0.75f));
        if (n.Contains("aluminium")) return R(Finish.Brushed, 0.5f, 1f, new Color(0.80f, 0.81f, 0.82f));
        if (n.Contains("galvanised")) return R(Finish.Galvanised, 0.5f, 0.9f, new Color(0.66f, 0.68f, 0.70f));
        if (n.Contains("chain") || n.Contains("wheel_hub")) return R(Finish.DarkMetal, 0.45f, 0.85f);
        if (n.Contains("rubber")) return R(Finish.Rubber, 0.2f, 0f, new Color(0.075f, 0.075f, 0.08f));
        if (n.Contains("foam")) return R(Finish.Foam, 0.08f);
        if (n.Contains("plywood")) return R(Finish.Wood, 0.2f);
        if (n.Contains("label") || n.Contains("sticker")) return R(Finish.Label, 0.35f);
        if (n.Contains("tabletop")) return R(Finish.Plastic, 0.42f, 0f, new Color(0.84f, 0.84f, 0.83f));   // HPL laminate
        if (n.Contains("desk_stands")) return R(Finish.PowderCoat, 0.42f, 0f, new Color(0.70f, 0.71f, 0.72f));
        if (n.Contains("plastic") || n.Contains("handle") || n.Contains("monitor frame") || n.Contains("wheelcover") || n.Contains("sensor") || n.Contains("controller"))
            return R(Finish.Plastic, 0.4f);
        return R(Finish.PowderCoat, 0.4f);   // Paint_*, Lift_*, Grating, Device, Rack, Battery, Button
    }

    [MenuItem("Tools/Einstein Telescope/Look/Props: build PBR materials and remap models")]
    static void Build()
    {
        Directory.CreateDirectory(MAT_DIR);
        Directory.CreateDirectory(TEX_DIR);
        var textures = BuildTextures();

        // One library material per embedded material name
        var library = new Dictionary<string, Material>();
        var models = new List<string>(PropMaterialPass.ModelPaths());
        foreach (string path in models)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Material source && !library.ContainsKey(source.name))
                {
                    Rule rule = RuleFor(source.name);
                    if (rule.finish == Finish.Keep) continue;
                    library[source.name] = MakeMaterial(source, rule, textures);
                }
            }
        }

        AssetDatabase.SaveAssets();

        int remapped = 0;
        foreach (string path in models)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;
            bool changed = false;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Material source && library.TryGetValue(source.name, out Material target))
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name), target);
                    changed = true;
                    remapped++;
                }
            }

            if (changed) importer.SaveAndReimport();
        }

        Debug.Log("[PropMaterialLibrary] " + library.Count + " materials in " + MAT_DIR + ", " + remapped + " slots remapped in " + models.Count + " models. Bake the lighting again.");
    }

    [MenuItem("Tools/Einstein Telescope/Look/Props: back to the FBX materials")]
    static void Revert()
    {
        foreach (string path in PropMaterialPass.ModelPaths())
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;
            foreach (var pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type == typeof(Material)) importer.RemoveRemap(pair.Key);
            }

            importer.SaveAndReimport();
        }
    }

    static Material MakeMaterial(Material source, Rule rule, Dictionary<string, Texture2D> tex)
    {
        string path = MAT_DIR + "/M_Prop_" + Safe(source.name) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(lit);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = lit;
        Color colour = rule.colour ?? (source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color);
        colour.a = 1f;
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_WorkflowMode", 1f);   // metallic
        material.SetFloat("_Metallic", rule.metallic);
        material.SetFloat("_Smoothness", rule.smoothness);
        material.SetTexture("_BaseMap", null);
        material.SetTexture("_BumpMap", null);
        material.SetTexture("_MetallicGlossMap", null);
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        string family = rule.finish switch
        {
            Finish.PowderCoat => "PowderCoat",
            Finish.Plastic => "Plastic",
            Finish.Label => "Plastic",
            Finish.Rubber => "Rubber",
            Finish.Foam => "Rubber",
            Finish.Brushed => "Brushed",
            Finish.Polished => "Polished",
            Finish.Galvanised => "Galvanised",
            Finish.DarkMetal => "Brushed",
            Finish.Wood => "Wood",
            _ => null
        };

        if (rule.finish == Finish.Emissive)
        {
            // Small lights: glow (Bloom picks it up), but never light the room when baking (CLAUDE.md)
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", colour * 1.6f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else if (family != null)
        {
            bool metal = rule.metallic > 0.5f;
            material.SetTexture("_BaseMap", tex["T_Prop_" + family + "_Albedo"]);
            bool flat = family == "PowderCoat" || family == "Plastic" || family == "Polished";   // big panels: stretched kit UVs turn bumps into blotches
            material.SetTexture("_BumpMap", flat ? null : tex["T_Prop_" + family + "_Normal"]);
            material.SetFloat("_BumpScale", rule.finish == Finish.Label ? 0.3f : 1f);
            if (!flat) material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", tex["T_Prop_" + family + (metal ? "_MetalMS" : "_MS")]);
            material.SetFloat("_Smoothness", rule.smoothness);   // URP: map alpha x this
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_SmoothnessTextureChannel", 0f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static string Safe(string name) => name.Replace(' ', '_');

    // ----- Procedural textures ----------------------------------------------------------------------------

    static Dictionary<string, Texture2D> BuildTextures()
    {
        var all = new Dictionary<string, Texture2D>();
        foreach (string family in new[] { "PowderCoat", "Plastic", "Rubber", "Brushed", "Polished", "Galvanised", "Wood" })
        {
            float[] height = new float[SIZE * SIZE];
            float[] tint = new float[SIZE * SIZE];
            float[] gloss = new float[SIZE * SIZE];
            float bump;
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float u = x / (float)SIZE, v = y / (float)SIZE;
                    int i = y * SIZE + x;
                    float large = Tile(u, v, 3f, 11) * 0.6f + Tile(u, v, 7f, 12) * 0.4f;   // wipes, slow mottling
                    switch (family)
                    {
                        case "PowderCoat":   // orange peel: soft round bumps, faint mottling
                            height[i] = Tile(u, v, 48f, 1) * 0.7f + Tile(u, v, 96f, 2) * 0.3f;
                            tint[i] = 0.993f + 0.007f * large;
                            gloss[i] = 0.96f + 0.04f * large;
                            break;
                        case "Plastic":      // fine moulded grain
                            height[i] = Tile(u, v, 160f, 3);
                            tint[i] = 0.993f + 0.007f * large;
                            gloss[i] = 0.95f + 0.05f * large;
                            break;
                        case "Rubber":
                            height[i] = Tile(u, v, 120f, 4) * 0.6f + Tile(u, v, 30f, 5) * 0.4f;
                            tint[i] = 0.97f + 0.03f * Tile(u, v, 20f, 6);
                            gloss[i] = 0.7f + 0.3f * large;
                            break;
                        case "Brushed":      // streaks along U
                            height[i] = Tile(u, v * 0.004f + 0.5f, 1f, 7) * 0.2f + Streak(u, v, 260f, 8) * 0.8f;
                            tint[i] = 0.985f + 0.015f * Streak(u, v, 60f, 9);
                            gloss[i] = 0.93f + 0.07f * Streak(u, v, 40f, 10);
                            break;
                        case "Polished":
                            height[i] = Tile(u, v, 200f, 13) * 0.3f;
                            tint[i] = 0.98f + 0.02f * large;
                            gloss[i] = 0.9f + 0.1f * large;
                            break;
                        case "Galvanised":   // zinc spangles: patches with slightly different brightness and gloss
                            float cell = Cells(u, v, 18f, 14);
                            height[i] = cell * 0.5f + Tile(u, v, 150f, 15) * 0.5f;
                            tint[i] = 0.95f + 0.05f * cell;
                            gloss[i] = 0.88f + 0.12f * cell;
                            break;
                        default:             // wood (plywood): grain along U
                            float grain = Streak(u, v, 40f, 16) * 0.6f + Tile(u, v, 10f, 17) * 0.4f;
                            height[i] = grain;
                            tint[i] = 0.82f + 0.18f * grain;
                            gloss[i] = 0.7f + 0.3f * grain;
                            break;
                    }
                }
            }

            bump = family switch { "PowderCoat" => 0.25f, "Plastic" => 0.2f, "Rubber" => 0.6f, "Brushed" => 0.35f, "Polished" => 0.1f, "Galvanised" => 0.3f, _ => 1f };
            all["T_Prop_" + family + "_Albedo"] = Save("T_Prop_" + family + "_Albedo", p => { float t = tint[p]; return new Color(t, t, t, 1f); }, true, false);
            all["T_Prop_" + family + "_Normal"] = SaveNormal("T_Prop_" + family + "_Normal", height, bump);
            all["T_Prop_" + family + "_MS"] = Save("T_Prop_" + family + "_MS", p => new Color(0f, 0f, 0f, gloss[p]), false, false);
            all["T_Prop_" + family + "_MetalMS"] = Save("T_Prop_" + family + "_MetalMS", p => new Color(1f, 1f, 1f, gloss[p]), false, false);
        }

        return all;
    }

    // Tileable value noise: a lattice of period "freq" over the 0..1 square, smoothly interpolated
    static float Tile(float u, float v, float freq, int seed)
    {
        int period = Mathf.Max(1, Mathf.RoundToInt(freq));
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Smooth(x - x0), fy = Smooth(y - y0);
        float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
        float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // Long thin streaks along U (constant along U, varying fast along V)
    static float Streak(float u, float v, float freq, int seed) => Tile(u * 0.02f, v, freq, seed) * 0.7f + Tile(u, v, freq * 0.25f, seed + 50) * 0.3f;

    // Tileable cell pattern (each cell one value): zinc spangles
    static float Cells(float u, float v, float freq, int seed)
    {
        int period = Mathf.RoundToInt(freq);
        float x = u * period, y = v * period;
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        float best = 9f, value = 0f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int gx = cx + dx, gy = cy + dy;
            float px = gx + Hash(gx, gy, period, seed), py = gy + Hash(gx, gy, period, seed + 1);
            float dist = (px - x) * (px - x) + (py - y) * (py - y);
            if (dist < best) { best = dist; value = Hash(gx, gy, period, seed + 2); }
        }

        return value;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float Hash(int x, int y, int period, int seed)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    static Texture2D Save(string name, System.Func<int, Color> pixel, bool srgb, bool normal)
    {
        var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, !srgb);
        var pixels = new Color[SIZE * SIZE];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = pixel(i);
        texture.SetPixels(pixels);
        texture.Apply();
        string path = TEX_DIR + "/" + name + ".png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = srgb;
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.maxTextureSize = SIZE;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D SaveNormal(string name, float[] height, float strength)
    {
        var colours = new Color[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float l = height[y * SIZE + (x + SIZE - 1) % SIZE], r = height[y * SIZE + (x + 1) % SIZE];
                float d = height[((y + SIZE - 1) % SIZE) * SIZE + x], u = height[((y + 1) % SIZE) * SIZE + x];
                var n = new Vector3((l - r) * strength * 4f, (d - u) * strength * 4f, 1f).normalized;
                colours[y * SIZE + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }

        return Save(name, p => colours[p], false, true);
    }
}

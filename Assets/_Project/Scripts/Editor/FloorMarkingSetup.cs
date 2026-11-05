using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Einstein Telescope > Towers > Floor marking around towers and tubes
/// Lays black-and-yellow safety marking (painted tape) on the hall floor:
///  - a ring around every tower
///  - two strips along every beam tube, from ring to ring
/// Towers and tubes are measured from the TowerConstellation mesh (same detection as "Measure tower layout").
///
/// No URP Decal Projectors: they need a Decal renderer feature (DBuffer), are expensive on the Quest and
/// only support smoothness in DBuffer mode. Instead: one flat mesh 2 mm above the floor
/// (one draw call) with a URP Lit material: painted, glossy (smoothness ±0.75) with worn spots that are more matte,
/// and a normal map so the edges of the paint layers catch the light. The reflection comes from RP_Hall.
///
/// Safe to run again: textures, material and mesh are updated, not replaced (GUIDs stay the same),
/// and the FloorMarkings object in the scene is reused. Bake the lighting again afterwards.
/// To change the look: the function Hazard() and the constants below.
/// </summary>
public static class FloorMarkingSetup
{
    const string TOWER_OBJECT = "TowerConstellation";
    const string ROOT_NAME = "FloorMarkings";
    const string TEX_DIR = "Assets/_Project/Textures/Room";
    const string TEX_NAME = "T_FloorHazard";
    const string MAT_PATH = "Assets/_Project/Materials/Room/M_FloorHazard.mat";
    const string MESH_PATH = "Assets/_Project/Models/Room/FloorMarkings.asset";
    const int SIZE = 512;

    const float WIDTH = 0.12f;          // width of the marking; one texture tile = WIDTH x WIDTH
    const float TOWER_MARGIN = 0.35f;   // clearance between the tank shell and the inside of the ring
    const float TUBE_MARGIN = 0.30f;    // clearance between the tube (seen from above) and the strip
    const float RING_GAP = 0.05f;       // strips stop this far before the ring
    const float SPAN_GAP = 1.5f;        // tube parts with a smaller gap between them (bellows, flange) belong to one tube
    const float MIN_LENGTH = 0.4f;      // shorter pieces of strip are left out
    const float LIFT = 0.002f;          // above the floor, against z-fighting
    const int RING_SEGMENTS = 96;

    struct Line
    {
        public Vector2 dir;      // direction (x, z), normalized
        public float offset;     // distance from the center line to the origin, perpendicular to dir
        public float radius;
        public List<Vector2> spans;   // (start, end) along dir
    }

    [MenuItem("Tools/Einstein Telescope/Towers/Floor marking around towers and tubes")]
    static void Generate()
    {
        var root = GameObject.Find(TOWER_OBJECT);
        if (!root) { Debug.LogError($"[FloorMarkingSetup] '{TOWER_OBJECT}' not found in the open scene."); return; }

        var parts = new List<TowerLayoutProbe.Part>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh) parts.AddRange(TowerLayoutProbe.SplitParts(mf.sharedMesh, mf.transform.localToWorldMatrix));
        var towers = TowerLayoutProbe.FindTowers(parts);
        if (towers.Count == 0) { Debug.LogError("[FloorMarkingSetup] No towers recognized in the mesh."); return; }
        var lines = FindTubeLines(parts);

        float floorY = FindFloor(root, towers, out string floorSource);

        TowerMaterialSetup.EnsureFolder(TEX_DIR);
        try
        {
            BakeTextures();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        var mat = SetupMaterial();
        var mesh = BuildMesh(towers, lines, floorY + LIFT, out int strips);
        mesh = SaveMesh(mesh);
        PlaceInScene(mesh, mat);

        Debug.Log($"[FloorMarkingSetup] {towers.Count} rings and {strips} strips along {lines.Count} tubes, " +
                  $"floor at y = {floorY:0.000} ({floorSource}), {mesh.triangles.Length / 3} tris.\n" +
                  "Save the scene and bake the lighting again (Window > Rendering > Lighting > Generate Lighting).");
    }

    // ---------------------------------------------------------------- measuring

    /// <summary>Beam tubes, merged per center line: a tube often consists of several parts (flanges, bellows).</summary>
    static List<Line> FindTubeLines(List<TowerLayoutProbe.Part> parts)
    {
        var lines = new List<Line>();
        foreach (var part in parts)
        {
            if (!TowerLayoutProbe.TryTube(part, out Vector3 a3, out Vector3 b3, out float radius)) continue;
            var a = new Vector2(a3.x, a3.z);
            var b = new Vector2(b3.x, b3.z);
            var dir = (b - a).normalized;
            if (dir.x < -1e-4f || (Mathf.Abs(dir.x) <= 1e-4f && dir.y < 0f)) dir = -dir;   // one direction per line
            var normal = new Vector2(-dir.y, dir.x);
            float offset = Vector2.Dot(normal, a);
            float t0 = Vector2.Dot(dir, a), t1 = Vector2.Dot(dir, b);
            var span = new Vector2(Mathf.Min(t0, t1), Mathf.Max(t0, t1));

            int i = lines.FindIndex(l => Vector2.Angle(l.dir, dir) < 2f && Mathf.Abs(l.offset - offset) < 0.15f);
            if (i < 0)
            {
                lines.Add(new Line { dir = dir, offset = offset, radius = radius, spans = new List<Vector2> { span } });
            }
            else
            {
                var l = lines[i];
                l.radius = Mathf.Max(l.radius, radius);
                l.spans.Add(span);
                lines[i] = l;
            }
        }

        foreach (var l in lines)
        {
            var merged = new List<Vector2>();
            foreach (var s in l.spans.OrderBy(s => s.x))
            {
                if (merged.Count > 0 && s.x <= merged[merged.Count - 1].y + SPAN_GAP)
                    merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, s.y));
                else
                    merged.Add(s);
            }
            l.spans.Clear();
            l.spans.AddRange(merged);
        }
        return lines;
    }

    /// <summary>
    /// Floor height: raycast down next to the first tower (colliders of TowerConstellation do not count).
    /// Without a floor collider: the bottom of TowerConstellation (the legs stand on the floor).
    /// </summary>
    static float FindFloor(GameObject towerRoot, List<TowerLayoutProbe.Tower> towers, out string source)
    {
        Physics.SyncTransforms();
        var t = towers[0];
        float r = t.diameter * 0.5f + TOWER_MARGIN + WIDTH * 0.5f;
        var origin = new Vector3(t.center.x + r, t.bottom + 0.5f, t.center.y);
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.transform.IsChildOf(towerRoot.transform)) continue;
            source = $"raycast on '{hit.collider.name}'";
            return hit.point.y;
        }

        var bounds = new Bounds();
        bool first = true;
        foreach (var rend in towerRoot.GetComponentsInChildren<Renderer>())
        {
            if (first) { bounds = rend.bounds; first = false; }
            else bounds.Encapsulate(rend.bounds);
        }
        source = "bottom of TowerConstellation, no floor collider found";
        return bounds.min.y;
    }

    // ---------------------------------------------------------------- mesh

    class Builder
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Vector4> tangents = new List<Vector4>();
        public readonly List<int> tris = new List<int>();

        /// <summary>Two vertices across the running direction: v = 0 left, v = 1 right (seen in the running direction).</summary>
        public void AddPair(Vector3 center, Vector3 along, float u)
        {
            // Unity: bitangent = cross(normal, tangent); with normal = up, v = 1 lies on that side
            var side = new Vector3(along.z, 0f, -along.x);
            verts.Add(center - side * (WIDTH * 0.5f));
            verts.Add(center + side * (WIDTH * 0.5f));
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
            var tangent = new Vector4(along.x, 0f, along.z, 1f);
            tangents.Add(tangent);
            tangents.Add(tangent);
        }

        /// <summary>Quad between the previous and the last pair.</summary>
        public void ConnectLastPairs()
        {
            int i = verts.Count - 4;
            AddTriangle(i, i + 1, i + 2);
            AddTriangle(i + 2, i + 1, i + 3);
        }

        void AddTriangle(int a, int b, int c)
        {
            // Front face up (clockwise seen from above)
            if (Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).y < 0f) (b, c) = (c, b);
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }
    }

    static Mesh BuildMesh(List<TowerLayoutProbe.Tower> towers, List<Line> lines, float y, out int strips)
    {
        var builder = new Builder();

        float ringRadius(TowerLayoutProbe.Tower t) => t.diameter * 0.5f + TOWER_MARGIN + WIDTH * 0.5f;

        // Rings around the towers; u runs around and closes on a whole number of tiles, so there is no seam
        foreach (var t in towers)
        {
            float r = ringRadius(t);
            float tiles = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / WIDTH));
            for (int i = 0; i <= RING_SEGMENTS; i++)
            {
                float a = 2f * Mathf.PI * i / RING_SEGMENTS;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var along = new Vector3(-radial.z, 0f, radial.x);
                builder.AddPair(new Vector3(t.center.x, y + 0.001f, t.center.y) + radial * r, along, tiles * i / RING_SEGMENTS);
                if (i > 0) builder.ConnectLastPairs();
            }
        }

        // Strips on both sides of every tube, interrupted where they would touch a ring
        strips = 0;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            var normal = new Vector2(-line.dir.y, line.dir.x);
            foreach (float sideSign in new[] { -1f, 1f })
            {
                float lateral = line.offset + sideSign * (line.radius + TUBE_MARGIN + WIDTH * 0.5f);
                var spans = new List<Vector2>(line.spans);
                foreach (var t in towers)
                {
                    float r = ringRadius(t) + WIDTH * 0.5f + RING_GAP;
                    float h = Vector2.Dot(normal, t.center) - lateral;
                    if (Mathf.Abs(h) >= r) continue;
                    float tc = Vector2.Dot(line.dir, t.center), half = Mathf.Sqrt(r * r - h * h);
                    spans = Subtract(spans, tc - half, tc + half);
                }

                foreach (var s in spans)
                {
                    if (s.y - s.x < MIN_LENGTH) continue;
                    var along = new Vector3(line.dir.x, 0f, line.dir.y);
                    // Each tube a fraction higher, so crossing strips do not z-fight
                    float stripY = y + 0.0002f * li;
                    Vector2 p0 = line.dir * s.x + normal * lateral, p1 = line.dir * s.y + normal * lateral;
                    builder.AddPair(new Vector3(p0.x, stripY, p0.y), along, 0f);
                    builder.AddPair(new Vector3(p1.x, stripY, p1.y), along, (s.y - s.x) / WIDTH);
                    builder.ConnectLastPairs();
                    strips++;
                }
            }
        }

        var mesh = new Mesh { name = "FloorMarkings" };
        mesh.SetVertices(builder.verts);
        mesh.SetUVs(0, builder.uvs);
        mesh.SetTangents(builder.tangents);
        mesh.SetNormals(Enumerable.Repeat(Vector3.up, builder.verts.Count).ToList());
        mesh.SetTriangles(builder.tris, 0);
        mesh.RecalculateBounds();
        Unwrapping.GenerateSecondaryUVSet(mesh);   // lightmap UVs, so the marking receives the baked shadows
        return mesh;
    }

    static List<Vector2> Subtract(List<Vector2> spans, float from, float to)
    {
        var result = new List<Vector2>();
        foreach (var s in spans)
        {
            if (to <= s.x || from >= s.y) { result.Add(s); continue; }
            if (from > s.x) result.Add(new Vector2(s.x, from));
            if (to < s.y) result.Add(new Vector2(to, s.y));
        }
        return result;
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
        if (!existing)
        {
            AssetDatabase.CreateAsset(mesh, MESH_PATH);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);   // same asset, same GUID
        existing.name = "FloorMarkings";
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssets();
        return existing;
    }

    static void PlaceInScene(Mesh mesh, Material mat)
    {
        var go = GameObject.Find(ROOT_NAME);
        if (!go)
        {
            go = new GameObject(ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(go, "Floor marking");
        }
        Undo.RecordObject(go.transform, "Floor marking");
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // the mesh is in world coordinates
        go.transform.localScale = Vector3.one;

        var filter = go.GetComponent<MeshFilter>();
        if (!filter) filter = Undo.AddComponent<MeshFilter>(go);
        var renderer = go.GetComponent<MeshRenderer>();
        if (!renderer) renderer = Undo.AddComponent<MeshRenderer>(go);
        Undo.RecordObject(filter, "Floor marking");
        Undo.RecordObject(renderer, "Floor marking");
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;   // lies flat on the floor
        renderer.receiveShadows = true;
        renderer.receiveGI = ReceiveGI.Lightmaps;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
    }

    // ---------------------------------------------------------------- textures + material

    struct Surface
    {
        public Color baseColor;
        public float smoothness;
        public float height;
    }

    /// <summary>
    /// One tile of WIDTH x WIDTH: u along the strip, v across it. Diagonal stripes at 45°,
    /// one yellow and one black per tile. Painted and glossy, with dirt and matte worn spots (mainly along the edges).
    /// </summary>
    static Surface Hazard(float u, float v)
    {
        var yellow = new Color(0.93f, 0.70f, 0.06f);   // RAL 1003 signal yellow
        var black = new Color(0.07f, 0.07f, 0.07f);
        var floor = new Color(0.62f, 0.62f, 0.60f);    // the floor showing through the worn spots

        // Stripes with a soft edge of ±1.5 pixels (anti-aliasing)
        float s = Mathf.Sin(2f * Mathf.PI * (u + v));
        float isYellow = Mathf.Clamp01(0.5f + 0.5f * s / (2f * Mathf.PI * 1.5f / SIZE));
        var paint = Color.Lerp(black, yellow, isYellow);

        float grime = TowerMaterialSetup.Fbm(u, v, 4, 4, 5, 11);
        float edge = 1f - Mathf.Clamp01(Mathf.Min(v, 1f - v) / 0.1f);   // 1 at the edge of the strip
        float wearNoise = TowerMaterialSetup.Fbm(u, v, 6, 6, 5, 23);
        float wear = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.76f, wearNoise + edge * 0.12f))) * 0.75f;
        float peel = TowerMaterialSetup.Fbm(u, v, 32, 32, 3, 37);   // orange peel of the paint

        var color = Color.Lerp(paint, floor, wear) * Mathf.Lerp(0.8f, 1f, grime);
        color.a = 1f;
        return new Surface
        {
            baseColor = color,
            smoothness = Mathf.Lerp(0.78f, 0.3f, wear) * Mathf.Lerp(0.85f, 1f, grime),
            height = 0.35f * isYellow * (1f - wear) + 0.25f * peel - 0.6f * wear,
        };
    }

    static void BakeTextures()
    {
        var albedo = new Color32[SIZE * SIZE];
        var mask = new Color32[SIZE * SIZE];
        var height = new float[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        {
            if (y % 64 == 0) EditorUtility.DisplayProgressBar("Floor marking", "Computing textures", (float)y / SIZE);
            for (int x = 0; x < SIZE; x++)
            {
                var s = Hazard((x + 0.5f) / SIZE, (y + 0.5f) / SIZE);
                int i = y * SIZE + x;
                albedo[i] = s.baseColor;
                mask[i] = new Color(0f, 0f, 0f, s.smoothness);   // URP Lit: metallic in R (no metal), smoothness in A
                height[i] = s.height;
            }
        }

        var normal = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            float dx = height[y * SIZE + (x + 1) % SIZE] - height[y * SIZE + (x + SIZE - 1) % SIZE];
            float dy = height[(y + 1) % SIZE * SIZE + x] - height[(y + SIZE - 1) % SIZE * SIZE + x];
            var n = new Vector3(-dx * 2f, -dy * 2f, 1f).normalized;
            normal[y * SIZE + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        WriteTexture(TEX_NAME + "_BaseColor", albedo, srgb: true, isNormal: false);
        WriteTexture(TEX_NAME + "_MetallicSmoothness", mask, srgb: false, isNormal: false);
        WriteTexture(TEX_NAME + "_Normal", normal, srgb: false, isNormal: true);
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
        importer.anisoLevel = 8;   // the floor is seen from the control room at a shallow angle
        importer.maxTextureSize = SIZE;
        importer.SaveAndReimport();
    }

    static Material SetupMaterial()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        if (!mat)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, MAT_PATH);
        }
        mat.shader = lit;
        Texture2D Load(string suffix) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/{TEX_NAME}{suffix}.png");

        mat.SetTexture("_BaseMap", Load("_BaseColor"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTextureScale("_BaseMap", Vector2.one);   // the mesh UVs already count in tiles
        mat.SetTexture("_MetallicGlossMap", Load("_MetallicSmoothness"));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 1f);                 // multiplier on the A value of the map
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the alpha of the metallic map
        mat.SetTexture("_BumpMap", Load("_Normal"));
        mat.SetFloat("_BumpScale", 1f);
        mat.SetFloat("_SpecularHighlights", 1f);
        mat.SetFloat("_EnvironmentReflections", 1f);
        mat.enableInstancing = true;
        BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Look pass for the props (desk, mast lift, control cabinets and the ETP_* kits). Their materials are embedded in the
/// FBX files as plain colours, which reads as flat plastic. Step 1 only reports what is there (Logs/Playtest/materials.txt).
/// </summary>
static class PropMaterialPass
{
    internal static readonly string[] Folders =
    {
        "Assets/_Project/Models/Equipment", "Assets/_Project/Models/Clutter", "Assets/_Project/Models/Crane",
        "Assets/_Project/Models/Ceiling", "Assets/_Project/Models/Strut", "Assets/_Project/Models/Vacuum",
        "Assets/_Project/Models/Pipe", "Assets/_Project/Models/ETP_CableKit"
    };

    internal static readonly string[] ExtraFiles = { "Assets/_Project/Models/Room/Desk.fbx" };

    internal static IEnumerable<string> ModelPaths()
    {
        foreach (string folder in Folders)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                yield return AssetDatabase.GUIDToAssetPath(guid);
            }
        }

        foreach (string file in ExtraFiles) yield return file;
    }

    [MenuItem("Tools/Einstein Telescope/Look/Report prop materials")]
    static void Report()
    {
        var text = new StringBuilder();
        var seen = new Dictionary<string, int>();
        foreach (string path in ModelPaths())
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            text.AppendLine("## " + path + (importer ? "  (location " + importer.materialLocation + ", remaps " + importer.GetExternalObjectMap().Count + ")" : ""));
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh)
                {
                    text.AppendLine("  mesh " + mesh.name + ": " + mesh.vertexCount + " verts, uv0 " + (mesh.uv != null && mesh.uv.Length > 0) + ", uv1 " + (mesh.uv2 != null && mesh.uv2.Length > 0) + ", submeshes " + mesh.subMeshCount);
                    for (int s = 0; s < mesh.subMeshCount && mesh.subMeshCount > 1; s++)
                    {
                        var sub = mesh.GetSubMesh(s);
                        text.AppendLine("    sub " + s + ": " + sub.indexCount / 3 + " tris, bounds centre " + sub.bounds.center.ToString("F2") + " size " + sub.bounds.size.ToString("F2"));
                    }
                }
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Material m)
                {
                    seen[m.name] = seen.TryGetValue(m.name, out int n) ? n + 1 : 1;
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                    text.AppendLine("  mat " + m.name + " | " + m.shader.name + " | colour #" + ColorUtility.ToHtmlStringRGB(c) +
                                    " | metallic " + Get(m, "_Metallic") + " | smooth " + Get(m, "_Smoothness") +
                                    " | basemap " + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-"));
                }
            }
        }

        text.AppendLine();
        text.AppendLine("Material names over all files: " + string.Join(", ", seen.OrderByDescending(p => p.Value).Select(p => p.Key + " x" + p.Value)));
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Playtest");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "materials.txt"), text.ToString());
        Debug.Log("[PropMaterialPass] Report written to Logs/Playtest/materials.txt");
    }

    static string Get(Material m, string property) => m.HasProperty(property) ? m.GetFloat(property).ToString("0.00") : "-";
}

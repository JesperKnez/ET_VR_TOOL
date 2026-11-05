using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Towers > Measure tower layout
/// Reads the TowerConstellation mesh in the open scene and writes to the Console:
///  - per tower: center (x, z), diameter, bottom and top
///  - per beam tube: center line (start/end, height, direction), diameter and which towers it connects
///  - the current position of the laser demo
/// Needed to build the levels inside the towers. Does not change anything in the scene.
/// </summary>
public static class TowerLayoutProbe
{
    const string TOWER_OBJECT = "TowerConstellation";
    const string DEMO_ROOT = "Laser Demo - Michelson Interferometer";
    const float WELD = 0.001f;   // vertices within 1 mm belong to the same part

    internal struct Part
    {
        public List<Vector3> points;
        public Bounds bounds;
    }

    internal struct Tower
    {
        public Vector2 center;   // x, z
        public float diameter, bottom, top;
    }

    [MenuItem("Tools/Einstein Telescope/Towers/Measure tower layout")]
    static void Measure()
    {
        var root = GameObject.Find(TOWER_OBJECT);
        if (!root) { Debug.LogError($"[TowerLayoutProbe] '{TOWER_OBJECT}' not found in the open scene."); return; }

        var parts = new List<Part>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh) parts.AddRange(SplitParts(mf.sharedMesh, mf.transform.localToWorldMatrix));

        var report = new StringBuilder($"[TowerLayoutProbe] {parts.Count} separate parts in {TOWER_OBJECT}\n");
        var towers = FindTowers(parts);
        report.AppendLine($"\nTOWERS ({towers.Count}), center in world coordinates:");
        for (int i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            report.AppendLine($"  T{i}: x {t.center.x:0.000}  z {t.center.y:0.000}   Ø {t.diameter:0.00} m   y {t.bottom:0.00} .. {t.top:0.00}");
        }

        report.AppendLine("\nBEAM TUBES (horizontal, Ø ≥ 0.25 m, ≥ 1 m long):");
        foreach (var part in parts)
        {
            if (!TryTube(part, out Vector3 a, out Vector3 b, out float radius)) continue;
            Vector3 dir = (b - a).normalized;
            float heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            report.AppendLine($"  ({a.x:0.000}, {a.y:0.000}, {a.z:0.000}) -> ({b.x:0.000}, {b.y:0.000}, {b.z:0.000})   " +
                              $"length {(b - a).magnitude:0.00} m   Ø {radius * 2f:0.00} m   heading {heading:0.0}°   " +
                              $"{NearestTower(towers, a)} – {NearestTower(towers, b)}");
        }

        var demo = GameObject.Find(DEMO_ROOT);
        if (demo) report.AppendLine($"\nLaser demo: position {demo.transform.position}, rotation {demo.transform.eulerAngles}");

        Debug.Log(report.ToString());
    }

    /// <summary>Splits a mesh into separate parts (triangles that share vertices, welded at 1 mm).</summary>
    internal static List<Part> SplitParts(Mesh mesh, Matrix4x4 toWorld)
    {
        var verts = mesh.vertices;
        var world = new Vector3[verts.Length];
        var weldId = new int[verts.Length];
        var lookup = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < verts.Length; i++)
        {
            world[i] = toWorld.MultiplyPoint3x4(verts[i]);
            var key = Vector3Int.RoundToInt(world[i] / WELD);
            if (!lookup.TryGetValue(key, out int id)) lookup[key] = id = lookup.Count;
            weldId[i] = id;
        }

        var parent = Enumerable.Range(0, lookup.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Union(weldId[tris[t]], weldId[tris[t + 1]]);
                Union(weldId[tris[t]], weldId[tris[t + 2]]);
            }
        }

        var groups = new Dictionary<int, Part>();
        var seen = new HashSet<int>();
        for (int i = 0; i < verts.Length; i++)
        {
            if (!seen.Add(weldId[i])) continue;   // every welded vertex once
            int r = Find(weldId[i]);
            if (!groups.TryGetValue(r, out var part))
                part = new Part { points = new List<Vector3>(), bounds = new Bounds(world[i], Vector3.zero) };
            part.points.Add(world[i]);
            part.bounds.Encapsulate(world[i]);
            groups[r] = part;
        }
        return groups.Values.ToList();
    }

    /// <summary>Tank shells: round parts of 1.4-3.2 m diameter, grouped per center.</summary>
    internal static List<Tower> FindTowers(List<Part> parts)
    {
        var towers = new List<Tower>();
        foreach (var p in parts)
        {
            Vector3 s = p.bounds.size;
            float d = Mathf.Max(s.x, s.z);
            if (d < 1.4f || d > 3.2f || Mathf.Abs(s.x - s.z) > 0.15f * d || s.y < 0.3f) continue;

            var c = new Vector2(p.bounds.center.x, p.bounds.center.z);
            int i = towers.FindIndex(t => (t.center - c).magnitude < 0.6f);
            if (i < 0)
            {
                towers.Add(new Tower { center = c, diameter = d, bottom = p.bounds.min.y, top = p.bounds.max.y });
            }
            else
            {
                var t = towers[i];
                t.diameter = Mathf.Max(t.diameter, d);
                t.bottom = Mathf.Min(t.bottom, p.bounds.min.y);
                t.top = Mathf.Max(t.top, p.bounds.max.y);
                towers[i] = t;
            }
        }
        return towers.OrderBy(t => t.center.x).ThenBy(t => t.center.y).ToList();
    }

    /// <summary>A straight tube: long axis (principal axis of the points), horizontal, round cross-section.</summary>
    internal static bool TryTube(Part part, out Vector3 a, out Vector3 b, out float radius)
    {
        a = b = Vector3.zero;
        radius = 0f;
        var pts = part.points;
        if (pts.Count < 16) return false;

        Vector3 mean = Vector3.zero;
        foreach (var p in pts) mean += p;
        mean /= pts.Count;

        // Principal axis via power iteration on the covariance matrix
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in pts)
        {
            Vector3 d = p - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 axis = new Vector3(1f, 0.1f, 1f).normalized;
        for (int i = 0; i < 30; i++)
        {
            axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                               xy * axis.x + yy * axis.y + yz * axis.z,
                               xz * axis.x + yz * axis.y + zz * axis.z).normalized;
        }
        if (Mathf.Abs(axis.y) > 0.15f) return false;   // not horizontal

        float tMin = float.MaxValue, tMax = float.MinValue;
        var radii = new List<float>(pts.Count);
        foreach (var p in pts)
        {
            float t = Vector3.Dot(p - mean, axis);
            tMin = Mathf.Min(tMin, t);
            tMax = Mathf.Max(tMax, t);
            radii.Add((p - mean - axis * t).magnitude);
        }
        radii.Sort();
        radius = radii[radii.Count / 2];
        float length = tMax - tMin;
        if (length < 1f || radius < 0.125f || length < 3f * radius) return false;

        a = mean + axis * tMin;
        b = mean + axis * tMax;
        return true;
    }

    static string NearestTower(List<Tower> towers, Vector3 p)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < towers.Count; i++)
        {
            float d = (towers[i].center - new Vector2(p.x, p.z)).magnitude - towers[i].diameter * 0.5f;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best < 0 ? "?" : bestDist < 1.5f ? $"T{best}" : $"(T{best} at {bestDist:0.0} m)";
    }
}

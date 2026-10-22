using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EinsteinTelescope.Laser.Editor
{
    /// <summary>
    /// Small procedural modelling toolkit used to generate the laser kit's models in the editor.
    /// Shapes: lathe (revolved profile), rounded box, cylinder/tube between two points, swept tube
    /// along a path, extruded convex polygon. Each shape goes into a submesh (= one material slot).
    /// All coordinates are Unity's: metres, Y up, Z forward, clockwise front faces.
    /// </summary>
    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> m_Vertices = new List<Vector3>(4096);
        private readonly List<Vector3> m_Normals = new List<Vector3>(4096);
        private readonly List<Vector2> m_Uvs = new List<Vector2>(4096);
        private readonly List<List<int>> m_Triangles = new List<List<int>>();

        public MeshBuilder(int submeshCount)
        {
            for (int i = 0; i < submeshCount; i++)
            {
                m_Triangles.Add(new List<int>(4096));
            }
        }

        public int SubmeshCount => m_Triangles.Count;
        public IReadOnlyList<Vector3> Vertices => m_Vertices;
        public IReadOnlyList<Vector3> Normals => m_Normals;
        public IReadOnlyList<int> Triangles(int submesh) => m_Triangles[submesh];

        // ----- Lathe ------------------------------------------------------------------------------

        /// <summary>
        /// Revolves a profile around an axis. Profile points are (radius, height along the axis), listed
        /// so that the outside of the shape is on the right-hand side when walking along the profile
        /// (e.g. bottom centre, out along the bottom, up the side, back in along the top).
        /// Corners sharper than <paramref name="smoothAngle"/> degrees stay crisp; gentler ones are smoothed.
        /// </summary>
        public void AddLathe(int submesh, IList<Vector2> profile, Vector3 center, Quaternion rotation, int segments,
            float smoothAngle = 30f, float startDegrees = 0f, float sweepDegrees = 360f, bool closedProfile = false)
        {
            int pointCount = profile.Count;
            int segmentCount = closedProfile ? pointCount : pointCount - 1;
            var segmentNormals = new Vector2[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = profile[i];
                Vector2 b = profile[(i + 1) % pointCount];
                Vector2 d = b - a;
                segmentNormals[i] = new Vector2(d.y, -d.x).normalized;
            }

            float cosSmooth = Mathf.Cos(smoothAngle * Mathf.Deg2Rad);
            bool fullTurn = sweepDegrees >= 359.99f;
            int ringCount = fullTurn ? segments : segments + 1;

            for (int s = 0; s < segmentCount; s++)
            {
                Vector2 p0 = profile[s];
                Vector2 p1 = profile[(s + 1) % pointCount];
                if ((p1 - p0).sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                Vector2 n0 = BlendNormal(segmentNormals, s, s - 1, closedProfile, cosSmooth);
                Vector2 n1 = BlendNormal(segmentNormals, s, s + 1, closedProfile, cosSmooth);
                int baseIndex = m_Vertices.Count;

                for (int k = 0; k < ringCount; k++)
                {
                    float angle = (startDegrees + sweepDegrees * k / segments) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(angle), si = Mathf.Sin(angle);
                    AddVertex(center, rotation, new Vector3(p0.x * c, p0.y, p0.x * si), new Vector3(n0.x * c, n0.y, n0.x * si), new Vector2(k / (float)segments, s));
                    AddVertex(center, rotation, new Vector3(p1.x * c, p1.y, p1.x * si), new Vector3(n1.x * c, n1.y, n1.x * si), new Vector2(k / (float)segments, s + 1));
                }

                for (int k = 0; k < segments; k++)
                {
                    int k1 = fullTurn ? (k + 1) % segments : k + 1;
                    int a = baseIndex + 2 * k, b = baseIndex + 2 * k + 1;
                    int c2 = baseIndex + 2 * k1, d2 = baseIndex + 2 * k1 + 1;
                    Vector3 faceNormal = m_Normals[a] + m_Normals[b] + m_Normals[c2] + m_Normals[d2];
                    AddQuadIndices(submesh, a, b, d2, c2, faceNormal);
                }
            }

            if (!fullTurn)
            {
                // Close the open sides of a partial sweep with flat caps.
                AddLatheSideCap(submesh, profile, center, rotation, startDegrees, false);
                AddLatheSideCap(submesh, profile, center, rotation, startDegrees + sweepDegrees, true);
            }
        }

        private void AddLatheSideCap(int submesh, IList<Vector2> profile, Vector3 center, Quaternion rotation, float degrees, bool end)
        {
            float angle = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            Vector3 tangent = new Vector3(-s, 0f, c);
            Vector3 normal = end ? tangent : -tangent;
            var points = new List<Vector3>(profile.Count);
            foreach (Vector2 p in profile)
            {
                points.Add(new Vector3(p.x * c, p.y, p.x * s));
            }

            AddPolygon(submesh, points, normal, center, rotation);
        }

        private static Vector2 BlendNormal(Vector2[] normals, int own, int neighbour, bool closed, float cosSmooth)
        {
            int count = normals.Length;
            if (!closed && (neighbour < 0 || neighbour >= count))
            {
                return normals[own];
            }

            Vector2 other = normals[(neighbour + count) % count];
            return Vector2.Dot(normals[own], other) >= cosSmooth ? (normals[own] + other).normalized : normals[own];
        }

        // ----- Simple solids built on the lathe ---------------------------------------------------

        /// <summary>Closed cylinder along <paramref name="axis"/> with optional bevelled edges.</summary>
        public void AddCylinder(int submesh, Vector3 center, Vector3 axis, float radius, float length, int segments, float bevel = 0f)
        {
            float h = length * 0.5f;
            bevel = Mathf.Min(bevel, Mathf.Min(radius * 0.5f, h * 0.5f));
            var profile = bevel > 0f
                ? new List<Vector2> { new Vector2(0f, -h), new Vector2(radius - bevel, -h), new Vector2(radius, -h + bevel), new Vector2(radius, h - bevel), new Vector2(radius - bevel, h), new Vector2(0f, h) }
                : new List<Vector2> { new Vector2(0f, -h), new Vector2(radius, -h), new Vector2(radius, h), new Vector2(0f, h) };
            AddLathe(submesh, profile, center, AxisRotation(axis), segments);
        }

        /// <summary>Cylinder from point a to point b (wires, fibres, posts, rods).</summary>
        public void AddCylinderBetween(int submesh, Vector3 a, Vector3 b, float radius, int segments, bool caps = true)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;
            if (length < 1e-6f)
            {
                return;
            }

            var profile = caps
                ? new List<Vector2> { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, length), new Vector2(0f, length) }
                : new List<Vector2> { new Vector2(radius, 0f), new Vector2(radius, length) };
            AddLathe(submesh, profile, a, AxisRotation(axis), segments);
        }

        /// <summary>Hollow tube (lens tube, ring) along an axis, centred on <paramref name="center"/>.</summary>
        public void AddTube(int submesh, Vector3 center, Vector3 axis, float innerRadius, float outerRadius, float length, int segments)
        {
            float h = length * 0.5f;
            var profile = new List<Vector2>
            {
                new Vector2(innerRadius, -h), new Vector2(outerRadius, -h), new Vector2(outerRadius, h), new Vector2(innerRadius, h)
            };
            AddLathe(submesh, profile, center, AxisRotation(axis), segments, 30f, 0f, 360f, true);
        }

        /// <summary>Torus (O-ring, LED ring) around an axis.</summary>
        public void AddTorus(int submesh, Vector3 center, Vector3 axis, float majorRadius, float minorRadius, int segments, int sides)
        {
            var profile = new List<Vector2>(sides);
            for (int i = 0; i < sides; i++)
            {
                float t = 2f * Mathf.PI * i / sides; // counter-clockwise in (r, h): outside stays on the right
                profile.Add(new Vector2(majorRadius + minorRadius * Mathf.Cos(t), minorRadius * Mathf.Sin(t)));
            }

            AddLathe(submesh, profile, center, AxisRotation(axis), segments, 60f, 0f, 360f, true);
        }

        /// <summary>Flat ring in the plane perpendicular to <paramref name="axis"/>, facing along +axis.</summary>
        public void AddAnnulus(int submesh, Vector3 center, Vector3 axis, float innerRadius, float outerRadius, int segments,
            float startDegrees = 0f, float sweepDegrees = 360f)
        {
            var profile = new List<Vector2> { new Vector2(outerRadius, 0f), new Vector2(innerRadius, 0f) };
            AddLathe(submesh, profile, center, AxisRotation(axis), segments, 30f, startDegrees, sweepDegrees);
        }

        // ----- Rounded box --------------------------------------------------------------------------

        /// <summary>Box with rounded edges and corners (smooth normals). <paramref name="bevel"/> = corner radius.</summary>
        public void AddRoundedBox(int submesh, Vector3 center, Vector3 size, Quaternion rotation, float bevel, int bevelSteps = 3)
        {
            Vector3 half = size * 0.5f;
            bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.999f);
            if (bevel <= 1e-5f)
            {
                bevelSteps = 0;
            }

            float[] xs = AxisSamples(half.x, bevel, bevelSteps);
            float[] ys = AxisSamples(half.y, bevel, bevelSteps);
            float[] zs = AxisSamples(half.z, bevel, bevelSteps);
            Vector3 inner = new Vector3(half.x - bevel, half.y - bevel, half.z - bevel);

            // Each face: a grid over the two other axes, pushed onto the rounded surface.
            AddBoxFace(submesh, center, rotation, inner, bevel, 0, half.x, ys, zs);
            AddBoxFace(submesh, center, rotation, inner, bevel, 0, -half.x, ys, zs);
            // For the Y faces the first grid axis is Z and the second is X (axis + 1, axis + 2).
            AddBoxFace(submesh, center, rotation, inner, bevel, 1, half.y, zs, xs);
            AddBoxFace(submesh, center, rotation, inner, bevel, 1, -half.y, zs, xs);
            AddBoxFace(submesh, center, rotation, inner, bevel, 2, half.z, xs, ys);
            AddBoxFace(submesh, center, rotation, inner, bevel, 2, -half.z, xs, ys);
        }

        /// <summary>Simple box (sharp edges).</summary>
        public void AddBox(int submesh, Vector3 center, Vector3 size, Quaternion rotation)
        {
            AddRoundedBox(submesh, center, size, rotation, 0f, 0);
        }

        private static float[] AxisSamples(float half, float bevel, int steps)
        {
            if (steps <= 0)
            {
                return new[] { -half, half };
            }

            var values = new List<float>(2 * steps + 2);
            float flat = half - bevel;
            for (int k = 0; k <= steps; k++)
            {
                values.Add(-flat - bevel * Mathf.Cos(0.5f * Mathf.PI * k / steps));
            }

            for (int k = steps; k >= 0; k--)
            {
                values.Add(flat + bevel * Mathf.Cos(0.5f * Mathf.PI * k / steps));
            }

            return values.ToArray();
        }

        private void AddBoxFace(int submesh, Vector3 center, Quaternion rotation, Vector3 inner, float bevel, int axis, float value, float[] us, float[] vs)
        {
            int baseIndex = m_Vertices.Count;
            Vector3 faceNormal = Vector3.zero;
            SetComponent(ref faceNormal, axis, Mathf.Sign(value));

            for (int j = 0; j < vs.Length; j++)
            {
                for (int i = 0; i < us.Length; i++)
                {
                    Vector3 p = Vector3.zero;
                    SetComponent(ref p, axis, value);
                    SetComponent(ref p, (axis + 1) % 3, us[i]);
                    SetComponent(ref p, (axis + 2) % 3, vs[j]);

                    Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - q;
                    Vector3 n = d.sqrMagnitude > 1e-12f ? d.normalized : faceNormal;
                    Vector3 position = bevel > 0f ? q + n * bevel : p;
                    if (bevel <= 0f)
                    {
                        n = faceNormal;
                    }

                    AddVertex(center, rotation, position, n, new Vector2(i / (float)(us.Length - 1), j / (float)(vs.Length - 1)));
                }
            }

            int width = us.Length;
            for (int j = 0; j < vs.Length - 1; j++)
            {
                for (int i = 0; i < width - 1; i++)
                {
                    int a = baseIndex + j * width + i;
                    AddQuadIndices(submesh, a, a + 1, a + width + 1, a + width, rotation * faceNormal);
                }
            }
        }

        private static void SetComponent(ref Vector3 v, int axis, float value)
        {
            if (axis == 0) v.x = value;
            else if (axis == 1) v.y = value;
            else v.z = value;
        }

        // ----- Extrusions and sweeps ----------------------------------------------------------------

        /// <summary>Extrudes a convex polygon (in the XY plane) along local Z by <paramref name="depth"/>, centred.</summary>
        public void AddExtrudedPolygon(int submesh, IList<Vector2> polygon, float depth, Vector3 center, Quaternion rotation)
        {
            float h = depth * 0.5f;
            var front = new List<Vector3>(polygon.Count);
            var back = new List<Vector3>(polygon.Count);
            foreach (Vector2 p in polygon)
            {
                front.Add(new Vector3(p.x, p.y, h));
                back.Add(new Vector3(p.x, p.y, -h));
            }

            AddPolygon(submesh, front, Vector3.forward, center, rotation);
            AddPolygon(submesh, back, Vector3.back, center, rotation);

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                Vector2 edge = b - a;
                Vector3 n = new Vector3(edge.y, -edge.x, 0f).normalized;
                Vector3 centroidDirection = new Vector3(a.x + b.x, a.y + b.y, 0f);
                if (Vector3.Dot(n, centroidDirection - Centroid3(polygon)) < 0f)
                {
                    n = -n;
                }

                int baseIndex = m_Vertices.Count;
                AddVertex(center, rotation, new Vector3(a.x, a.y, -h), n, Vector2.zero);
                AddVertex(center, rotation, new Vector3(b.x, b.y, -h), n, Vector2.right);
                AddVertex(center, rotation, new Vector3(b.x, b.y, h), n, Vector2.one);
                AddVertex(center, rotation, new Vector3(a.x, a.y, h), n, Vector2.up);
                AddQuadIndices(submesh, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3, rotation * n);
            }
        }

        private static Vector3 Centroid3(IList<Vector2> polygon)
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 p in polygon) sum += p;
            sum /= polygon.Count;
            return new Vector3(sum.x * 2f, sum.y * 2f, 0f);
        }

        /// <summary>Round tube swept along a path (cables). Uses parallel-transport frames, no twisting.</summary>
        public void AddSweptTube(int submesh, IList<Vector3> path, float radius, int sides)
        {
            if (path.Count < 2)
            {
                return;
            }

            Vector3 tangent = (path[1] - path[0]).normalized;
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            int baseIndex = m_Vertices.Count;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 nextTangent = i < path.Count - 1 ? (path[i + 1] - path[i]).normalized : tangent;
                Vector3 t = i == 0 ? tangent : (tangent + nextTangent).normalized;
                normal = (normal - Vector3.Dot(normal, t) * t).normalized;
                Vector3 binormal = Vector3.Cross(t, normal);
                for (int k = 0; k < sides; k++)
                {
                    float a = 2f * Mathf.PI * k / sides;
                    Vector3 n = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    AddVertex(Vector3.zero, Quaternion.identity, path[i] + n * radius, n, new Vector2(k / (float)sides, i));
                }

                tangent = nextTangent;
            }

            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a = baseIndex + i * sides + k;
                    int b = baseIndex + i * sides + (k + 1) % sides;
                    int c = a + sides, d = b + sides;
                    AddQuadIndices(submesh, a, b, d, c, m_Normals[a] + m_Normals[b]);
                }
            }
        }

        // ----- Low-level ----------------------------------------------------------------------------

        /// <summary>Flat convex polygon with one normal (triangle fan).</summary>
        public void AddPolygon(int submesh, IList<Vector3> localPoints, Vector3 localNormal, Vector3 center, Quaternion rotation)
        {
            int baseIndex = m_Vertices.Count;
            foreach (Vector3 p in localPoints)
            {
                AddVertex(center, rotation, p, localNormal, new Vector2(p.x, p.y));
            }

            Vector3 worldNormal = rotation * localNormal;
            for (int i = 1; i < localPoints.Count - 1; i++)
            {
                AddTriangle(submesh, baseIndex, baseIndex + i, baseIndex + i + 1, worldNormal);
            }
        }

        private void AddVertex(Vector3 center, Quaternion rotation, Vector3 localPosition, Vector3 localNormal, Vector2 uv)
        {
            m_Vertices.Add(center + rotation * localPosition);
            m_Normals.Add((rotation * localNormal).normalized);
            m_Uvs.Add(uv);
        }

        private void AddQuadIndices(int submesh, int a, int b, int c, int d, Vector3 normal)
        {
            AddTriangle(submesh, a, b, c, normal);
            AddTriangle(submesh, a, c, d, normal);
        }

        /// <summary>Adds a triangle wound so that it faces along <paramref name="normal"/> (Unity: clockwise = front).</summary>
        private void AddTriangle(int submesh, int a, int b, int c, Vector3 normal)
        {
            Vector3 pa = m_Vertices[a], pb = m_Vertices[b], pc = m_Vertices[c];
            const float minEdgeSquared = 1e-12f; // corners closer than 1 micrometre: a zero-width sliver
            if ((pb - pa).sqrMagnitude < minEdgeSquared || (pc - pa).sqrMagnitude < minEdgeSquared || (pc - pb).sqrMagnitude < minEdgeSquared)
            {
                return;
            }

            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            if (cross.sqrMagnitude < 1e-16f)
            {
                return; // degenerate (e.g. at a lathe pole)
            }

            List<int> list = m_Triangles[submesh];
            if (Vector3.Dot(cross, normal) >= 0f)
            {
                list.Add(a); list.Add(b); list.Add(c);
            }
            else
            {
                list.Add(a); list.Add(c); list.Add(b);
            }
        }

        /// <summary>Rotation that turns local +Y (the lathe axis) into <paramref name="axis"/>.</summary>
        public static Quaternion AxisRotation(Vector3 axis)
        {
            return Quaternion.FromToRotation(Vector3.up, axis.normalized);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (m_Vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(m_Vertices);
            mesh.SetNormals(m_Normals);
            mesh.SetUVs(0, m_Uvs);
            mesh.subMeshCount = m_Triangles.Count;
            for (int i = 0; i < m_Triangles.Count; i++)
            {
                mesh.SetTriangles(m_Triangles[i], i);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}

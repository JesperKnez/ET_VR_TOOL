// ETpathfinder vacuum tower generator for Unity 6 + ProBuilder 6.
// Put this file in Assets/Editor/. Open it via  Tools > ETpathfinder Tower Builder.
//
// Every part is created as a normal, editable ProBuilder mesh (you can still
// select faces/edges, extrude, use the ProBuilder tools on it afterwards).
//
// All sizes are expressed as fractions of D = the diameter of the upper (narrow)
// shell segments. Proportions were measured from the cleanroom photos
// (PXL / WhatsApp photos of the towers). Change D in the window to rescale.

using System.Collections.Generic;
using UnityEngine;
#if !TOWER_TEST
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
#endif

namespace ETpfTower
{
    public enum Mat { Shell = 0, Polished = 1, Bolt = 2, Weld = 3, BoltDecal = 4 }

    /// Mesh = real 3D hex bolts (heavy), Decal = flat textured quads (2 triangles per bolt), None = no bolts.
    public enum BoltStyle { Decal = 0, Mesh = 1, None = 2 }

    // ------------------------------------------------------------------ math helpers
    public struct Frame
    {
        public Vector3 o, x, y, z;

        public Frame(Vector3 o, Vector3 x, Vector3 y, Vector3 z)
        {
            this.o = o; this.x = x; this.y = y; this.z = z;
        }

        public static Frame World => new Frame(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);

        public Vector3 P(float a, float b, float c) => o + x * a + y * b + z * c;
        public Vector3 V(float a, float b, float c) => x * a + y * b + z * c;

        /// Frame on a vertical shell at angle deg (0 = +X, 90 = +Z), radius r, height h.
        /// z = outward normal, y = up, x = tangent.
        public Frame Radial(float deg, float r, float h)
        {
            float a = deg * Mathf.Deg2Rad;
            Vector3 outward = V(Mathf.Cos(a), 0, Mathf.Sin(a)).normalized;
            Vector3 up = y.normalized;
            return new Frame(P(r * Mathf.Cos(a), h, r * Mathf.Sin(a)), Vector3.Cross(up, outward), up, outward);
        }

        /// Frame whose local Y axis points along 'axis' (used for revolved parts like ports).
        public static Frame Axis(Vector3 origin, Vector3 axis, Vector3 hint)
        {
            Vector3 yy = axis.normalized;
            Vector3 zz = Vector3.Cross(hint, yy);
            if (zz.magnitude < 1e-4f) zz = Vector3.Cross(Vector3.right, yy);
            if (zz.magnitude < 1e-4f) zz = Vector3.Cross(Vector3.forward, yy);
            zz = zz.normalized;
            Vector3 xx = Vector3.Cross(yy, zz).normalized;
            return new Frame(origin, xx, yy, zz);
        }

        /// This frame turned so that its Y axis is its current Z axis (radial frame -> port frame).
        public Frame ZAsAxis() => Axis(o, z, y);

        public Frame Moved(float a, float b, float c) => new Frame(P(a, b, c), x, y, z);
    }

    // ------------------------------------------------------------------ mesh accumulator
    public class Part
    {
        public string name;
        public string group;
        public List<Vector3> pos = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int[]> faces = new List<int[]>();
        public List<int> smooth = new List<int>();
        public List<Mat> mats = new List<Mat>();
        public Mat cur = Mat.Shell;
        int nextGroup = 1;

        public Part(string group, string name) { this.group = group; this.name = name; }

        public int NewGroup()
        {
            int g = nextGroup;
            nextGroup = nextGroup % 30 + 1;
            return g;
        }

        /// Adds one planar convex polygon as a single ProBuilder face.
        /// The winding is chosen so the face looks along 'expectedNormal'.
        public void Poly(Vector3[] v, Vector2[] t, Vector3 expectedNormal, int group)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < v.Length; i++)
                n += Vector3.Cross(v[i], v[(i + 1) % v.Length]);
            if (n.magnitude < 1e-12f) return; // degenerate
            bool flip = Vector3.Dot(n, expectedNormal) < 0;

            int b = pos.Count;
            for (int i = 0; i < v.Length; i++) { pos.Add(v[i]); uv.Add(t[i]); }
            var idx = new List<int>();
            for (int i = 1; i < v.Length - 1; i++)
            {
                if (!flip) { idx.Add(b); idx.Add(b + i); idx.Add(b + i + 1); }
                else { idx.Add(b); idx.Add(b + i + 1); idx.Add(b + i); }
            }
            faces.Add(idx.ToArray());
            smooth.Add(group);
            mats.Add(cur);
        }

        static float Angle(Vector2 a, Vector2 b)
        {
            float d = (a.x * b.x + a.y * b.y) / (a.magnitude * b.magnitude + 1e-12f);
            return Mathf.Acos(Mathf.Clamp(d, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// Revolves a 2D profile (x = radius, y = height along the frame's Y axis) 360 degrees.
        /// Walk the profile so the outside of the surface is on the RIGHT of the walking direction
        /// (i.e. counter-clockwise around a solid cross-section).
        public void Revolve(Frame f, Vector2[] prof, int seg, bool closed, bool smoothAround = true)
        {
            int n = prof.Length;
            int ne = closed ? n : n - 1;
            int[] g = new int[ne];
            float[] vAcc = new float[n + 1];
            for (int e = 0; e < ne; e++)
            {
                Vector2 a = prof[e], b = prof[(e + 1) % n];
                Vector2 d = b - a;
                vAcc[e + 1] = vAcc[e] + d.magnitude;
                if (d.magnitude < 1e-7f || (a.x < 1e-6f && b.x < 1e-6f)) { g[e] = -1; continue; }
                if (!smoothAround || Mathf.Abs(d.y) < 1e-6f) { g[e] = 0; continue; }
                if (e > 0 && g[e - 1] > 0 && Angle(prof[e] - prof[e - 1], d) < 35f) g[e] = g[e - 1];
                else g[e] = NewGroup();
            }

            for (int s = 0; s < seg; s++)
            {
                float a0 = 2f * Mathf.PI * s / seg;
                float a1 = 2f * Mathf.PI * ((s + 1) % seg) / seg;
                float u1 = 2f * Mathf.PI * (s + 1) / seg; // unwrapped angle for UVs
                float am = (a0 + u1) * 0.5f;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                for (int e = 0; e < ne; e++)
                {
                    if (g[e] < 0) continue;
                    Vector2 p0 = prof[e], p1 = prof[(e + 1) % n];
                    Vector2 d = p1 - p0;
                    Vector3 expected = f.V(d.y * Mathf.Cos(am), -d.x, d.y * Mathf.Sin(am));
                    Vector3 A = f.P(p0.x * c0, p0.y, p0.x * s0);
                    Vector3 B = f.P(p0.x * c1, p0.y, p0.x * s1);
                    Vector3 C = f.P(p1.x * c1, p1.y, p1.x * s1);
                    Vector3 Dd = f.P(p1.x * c0, p1.y, p1.x * s0);
                    Vector2 tA = new Vector2(a0 * p0.x, vAcc[e]);
                    Vector2 tB = new Vector2(u1 * p0.x, vAcc[e]);
                    Vector2 tC = new Vector2(u1 * p1.x, vAcc[e + 1]);
                    Vector2 tD = new Vector2(a0 * p1.x, vAcc[e + 1]);
                    if (p0.x < 1e-6f) Poly(new[] { A, C, Dd }, new[] { tA, tC, tD }, expected, g[e]);
                    else if (p1.x < 1e-6f) Poly(new[] { A, B, C }, new[] { tA, tB, tC }, expected, g[e]);
                    else Poly(new[] { A, B, C, Dd }, new[] { tA, tB, tC, tD }, expected, g[e]);
                }
            }
        }

        /// Extrudes a convex polygon drawn in the frame's local XY plane along local Z (from z0 to z1).
        public void Extrude(Frame f, Vector2[] poly, float z0, float z1)
        {
            int n = poly.Length;
            Vector2 c = Vector2.zero;
            foreach (var p in poly) c += p;
            c /= n;
            var top = new Vector3[n]; var bot = new Vector3[n]; var tt = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                top[i] = f.P(poly[i].x, poly[i].y, z1);
                bot[i] = f.P(poly[i].x, poly[i].y, z0);
                tt[i] = poly[i];
            }
            Poly(top, tt, f.z, 0);
            Poly(bot, tt, -f.z, 0);

            int prevG = -1; Vector2 prevD = Vector2.zero;
            int firstG = -1;
            float acc = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 d = b - a;
                int gr;
                if (prevG > 0 && Angle(prevD, d) < 35f) gr = prevG;
                else if (n > 6) gr = NewGroup();
                else gr = 0;
                if (i == n - 1 && firstG > 0 && gr > 0 && Angle(d, poly[1] - poly[0]) < 35f) gr = firstG;
                if (i == 0) firstG = gr;
                prevG = gr; prevD = d;
                Vector2 mid = (a + b) * 0.5f - c;
                Vector3 expected = f.V(mid.x, mid.y, 0);
                float l = d.magnitude;
                Poly(new[] { f.P(a.x, a.y, z0), f.P(b.x, b.y, z0), f.P(b.x, b.y, z1), f.P(a.x, a.y, z1) },
                     new[] { new Vector2(acc, z0), new Vector2(acc + l, z0), new Vector2(acc + l, z1), new Vector2(acc, z1) },
                     expected, gr);
                acc += l;
            }
        }

        /// Axis-aligned (in frame space) box centred on (cx,cy,cz).
        public void Box(Frame f, float cx, float cy, float cz, float sx, float sy, float sz)
        {
            float hx = sx * 0.5f, hy = sy * 0.5f;
            Extrude(f.Moved(cx, cy, 0), new[] {
                new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy) },
                cz - sz * 0.5f, cz + sz * 0.5f);
        }

        public void Scale(float k)
        {
            // decal faces keep their 0..1 UVs; everything else gets UVs in metres
            var keep = new bool[pos.Count];
            for (int fi = 0; fi < faces.Count; fi++)
                if (mats[fi] == Mat.BoltDecal) foreach (int ix in faces[fi]) keep[ix] = true;
            for (int i = 0; i < pos.Count; i++) { pos[i] = pos[i] * k; if (!keep[i]) uv[i] = uv[i] * k; }
        }
    }

    // ------------------------------------------------------------------ profiles
    public static class Prof
    {
        /// Solid ring / disc cross-section (ri = 0 for a solid disc).
        public static Vector2[] Ring(float ri, float ro, float y0, float y1)
        {
            return new[] { new Vector2(ri, y0), new Vector2(ro, y0), new Vector2(ro, y1), new Vector2(ri, y1) };
        }

        /// Elliptical cap (dome / dished head) as a shell with wall thickness t.
        /// up = true: dome pointing up from yBase; false: dish pointing down.
        public static Vector2[] Head(float r, float depth, float t, float yBase, float skirt, bool up, int steps)
        {
            var pts = new List<Vector2>();
            float sgn = up ? 1f : -1f;
            if (up)
            {
                // outer: from bottom of skirt, up and over to the apex
                pts.Add(new Vector2(r, yBase - skirt));
                for (int i = 0; i <= steps; i++)
                {
                    float a = 0.5f * Mathf.PI * i / steps;
                    pts.Add(new Vector2(r * Mathf.Cos(a), yBase + depth * Mathf.Sin(a)));
                }
                // inner: from apex back down
                for (int i = steps; i >= 0; i--)
                {
                    float a = 0.5f * Mathf.PI * i / steps;
                    pts.Add(new Vector2((r - t) * Mathf.Cos(a), yBase + (depth - t) * Mathf.Sin(a)));
                }
                pts.Add(new Vector2(r - t, yBase - skirt));
            }
            else
            {
                // outer: from the bottom apex outward and up, then the straight part up to yBase + skirt
                for (int i = steps; i >= 0; i--)
                {
                    float a = 0.5f * Mathf.PI * i / steps;
                    pts.Add(new Vector2(r * Mathf.Cos(a), yBase - depth * Mathf.Sin(a)));
                }
                pts.Add(new Vector2(r, yBase + skirt));
                pts.Add(new Vector2(r - t, yBase + skirt));
                for (int i = 0; i <= steps; i++)
                {
                    float a = 0.5f * Mathf.PI * i / steps;
                    pts.Add(new Vector2((r - t) * Mathf.Cos(a), yBase - (depth - t) * Mathf.Sin(a)));
                }
            }
            return pts.ToArray();
        }

        /// Small circle cross-section (for weld beads, tube rings). Centre (rc, yc).
        public static Vector2[] Circle(float rc, float yc, float rad, int n)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                p[i] = new Vector2(rc + rad * Mathf.Cos(a), yc + rad * Mathf.Sin(a));
            }
            return p;
        }

        /// Bellows: convoluted outer wall between y0 and y1, straight inner wall.
        public static Vector2[] Bellows(float rIn, float rOut, float y0, float y1, int conv, float t)
        {
            var p = new List<Vector2>();
            p.Add(new Vector2(rIn - t, y0));
            int steps = conv * 8;
            for (int i = 0; i <= steps; i++)
            {
                float k = (float)i / steps;
                float w = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * conv * k);
                p.Add(new Vector2(rIn + (rOut - rIn) * w, y0 + (y1 - y0) * k));
            }
            p.Add(new Vector2(rIn - t, y1));
            return p.ToArray();
        }

        public static Vector2[] RoundedRect(float w, float h, float rad, int cornerSteps)
        {
            var p = new List<Vector2>();
            float hx = w * 0.5f - rad, hy = h * 0.5f - rad;
            Vector2[] cs = { new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy) };
            float[] start = { -90f, 0f, 90f, 180f };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= cornerSteps; i++)
                {
                    float a = (start[c] + 90f * i / cornerSteps) * Mathf.Deg2Rad;
                    p.Add(cs[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
                }
            return p.ToArray();
        }
    }

    // ------------------------------------------------------------------ settings
    [System.Serializable]
    public class TowerSettings
    {
        public float diameter = 2.0f;      // D: diameter of the narrow upper shell (m)
        public int upperSegments = 2;      // shell rings between the ribbed ring and the dome
        public int radialSegments = 64;    // smoothness of the big shells
        public bool ports = true;          // CF flanges, viewports, big doors, bellows
        public BoltStyle bolts = BoltStyle.Decal;
        public bool supports = true;       // legs, feet, floor plate, rail
        public bool lugs = true;           // lifting lugs, dome brackets, ribs, clamp tabs
        public bool welds = true;
    }

    // ------------------------------------------------------------------ the tower itself
    public static class TowerGeometry
    {
        const float TW = 0.006f;   // shell wall thickness (in D)
        const float RU = 0.5f;     // upper shell radius
        const float RW = 0.62f;    // wide section radius
        const float RL = 0.60f;    // lower chamber radius

        static TowerSettings S;
        static List<Part> parts;
        static int seg;

        static Part NewPart(string group, string name, Mat m)
        {
            var p = new Part(group, name) { cur = m };
            parts.Add(p);
            return p;
        }

        public static List<Part> Build(TowerSettings settings)
        {
            S = settings;
            parts = new List<Part>();
            seg = Mathf.Max(16, settings.radialSegments);
            Frame W = Frame.World;

            // ---------------- vertical layout (fractions of D, y = 0 is the floor)
            const float yDishApex = 0.20f, yDishTop = 0.34f, yLowerTop = 0.95f;
            const float yBigFlangeTop = 0.99f, yWideTop = 1.57f, yShoulderTop = 1.59f;
            const float yRibbedTop = 1.76f;
            const float flT = 0.045f;          // upper flange thickness

            // ---------------- lower chamber (dished bottom + cylinder)
            var lower = NewPart("Shell", "LowerChamber", Mat.Shell);
            var lp = new List<Vector2>(Prof.Head(RL, yDishTop - yDishApex, TW, yDishTop, 0f, false, 12));
            // replace the short skirt with the full cylinder height
            lp[13] = new Vector2(RL, yLowerTop);
            lp[14] = new Vector2(RL - TW, yLowerTop);
            lower.Revolve(W, lp.ToArray(), seg, true);

            var bigFlange = NewPart("Flanges", "BigFlange", Mat.Polished);
            bigFlange.Revolve(W, Prof.Ring(RL - 0.02f, 0.665f, yLowerTop, yBigFlangeTop), seg, true);

            // ---------------- wide section + shoulder plate
            var wide = NewPart("Shell", "WideSection", Mat.Shell);
            wide.Revolve(W, Prof.Ring(RW - TW, RW, yBigFlangeTop, yWideTop), seg, true);
            var shoulder = NewPart("Shell", "ShoulderPlate", Mat.Shell);
            shoulder.Revolve(W, Prof.Ring(RU - TW, RW + 0.015f, yWideTop, yShoulderTop), seg, true);

            // ---------------- short ribbed ring
            var ribbed = NewPart("Shell", "RibbedRing", Mat.Shell);
            ribbed.Revolve(W, Prof.Ring(RU - TW, RU, yShoulderTop, yRibbedTop), seg, true);

            // ---------------- upper segments + flanges
            float y = yRibbedTop;
            var segTops = new List<float>();
            var segBottoms = new List<float>();
            int nSeg = Mathf.Clamp(S.upperSegments, 1, 4);
            var flangeYs = new List<float>();
            for (int i = 0; i < nSeg; i++)
            {
                var fl = NewPart("Flanges", "Flange" + (i + 1), Mat.Polished);
                fl.Revolve(W, Prof.Ring(RU - 0.02f, 0.535f, y, y + flT), seg, true);
                flangeYs.Add(y);
                y += flT;
                float h = (i == nSeg - 1) ? 0.57f : 0.44f;
                var sp = NewPart("Shell", "Segment" + (i + 1), Mat.Shell);
                sp.Revolve(W, Prof.Ring(RU - TW, RU, y, y + h), seg, true);
                segBottoms.Add(y);
                y += h;
                segTops.Add(y);
            }
            var domeFl = NewPart("Flanges", "DomeFlange", Mat.Polished);
            domeFl.Revolve(W, Prof.Ring(RU - 0.02f, 0.545f, y, y + 0.05f), seg, true);
            float yDomeFlange = y;
            y += 0.05f;
            const float skirt = 0.06f, domeH = 0.30f;
            var dome = NewPart("Shell", "Dome", Mat.Shell);
            dome.Revolve(W, Prof.Head(RU, domeH, TW, y + skirt, skirt, true, 16), seg, true);
            float yDomeBase = y + skirt;

            // ---------------- welds
            if (S.welds)
            {
                var w = NewPart("Welds", "WeldBeads", Mat.Weld);
                float wr = 0.006f;
                w.Revolve(W, Prof.Circle(RL, yDishTop, wr, 8), seg, true);
                w.Revolve(W, Prof.Circle(RW, yWideTop - 0.002f, wr, 8), seg, true);
                w.Revolve(W, Prof.Circle(RU, yShoulderTop + 0.002f, wr, 8), seg, true);
                w.Revolve(W, Prof.Circle(RU, yDomeBase, wr, 8), seg, true);
                for (int i = 0; i < segBottoms.Count; i++)
                    w.Revolve(W, Prof.Circle(RU, (segBottoms[i] + segTops[i]) * 0.5f, wr * 0.8f, 8), seg, true);
            }

            // ---------------- lugs, ribs, clamp tabs, dome brackets
            if (S.lugs)
            {
                var ribs = NewPart("Details", "RingRibs", Mat.Shell);
                for (int i = 0; i < 12; i++)
                {
                    var f = W.Radial(i * 30f + 15f, RU, (yShoulderTop + yRibbedTop) * 0.5f);
                    ribs.Box(f, 0, 0, 0.025f, 0.012f, yRibbedTop - yShoulderTop, 0.05f);
                }

                var tabs = NewPart("Details", "FlangeClampTabs", Mat.Polished);
                var tabYs = new List<float>(flangeYs) { yDomeFlange };
                foreach (float fy in tabYs)
                    for (int i = 0; i < 24; i++)
                    {
                        float ang = i * 15f;
                        var fTop = W.Radial(ang, RU, fy + flT + 0.009f);
                        tabs.Box(fTop, 0, 0, 0.009f, 0.02f, 0.018f, 0.018f);
                        var fBot = W.Radial(ang + 7.5f, RU, fy - 0.009f);
                        tabs.Box(fBot, 0, 0, 0.009f, 0.02f, 0.018f, 0.018f);
                    }

                var lugs = NewPart("Details", "LiftingLugs", Mat.Shell);
                for (int s = 0; s < segTops.Count; s++)
                    for (int i = 0; i < 4; i++)
                        Lug(lugs, W.Radial(45f + 90f * i, RU, segTops[s] - 0.13f), 0.0f);

                // lugs on the lower part of the dome (surface tilts slightly back)
                for (int i = 0; i < 4; i++)
                {
                    float t = 0.10f; // position along the quarter ellipse (0 = dome base)
                    float a = 0.5f * Mathf.PI * t;
                    float r = RU * Mathf.Cos(a), hy = yDomeBase + domeH * Mathf.Sin(a);
                    float tilt = Mathf.Atan2(Mathf.Sin(a) * RU, Mathf.Cos(a) * domeH) * Mathf.Rad2Deg; // surface normal tilt
                    Lug(lugs, W.Radial(30f + 90f * i, r, hy), tilt);
                }

                // triangular lifting brackets on the dome flange
                var br = NewPart("Details", "DomeBrackets", Mat.Shell);
                for (int i = 0; i < 4; i++)
                {
                    var f = W.Radial(62f + 90f * i, 0f, yDomeFlange + 0.05f);
                    // plate lies in the radial plane: local x = radial, y = up, extruded along tangent
                    Frame pf = new Frame(f.o, f.z, f.y, f.x);
                    br.Extrude(pf, new[] {
                        new Vector2(RU - 0.02f, 0f), new Vector2(0.625f, 0.14f), new Vector2(RU - 0.04f, 0.14f) },
                        -0.008f, 0.008f);
                }
            }

            // ---------------- supports
            if (S.supports)
            {
                var floor = NewPart("Supports", "FloorPlate", Mat.Polished);
                floor.Revolve(W, Prof.Ring(0f, 0.78f, 0f, 0.012f), seg, true);

                var legs = NewPart("Supports", "Legs", Mat.Polished);
                var feet = NewPart("Supports", "Feet", Mat.Polished);
                for (int i = 0; i < 4; i++)
                {
                    float ang = 45f + 90f * i;
                    var f = W.Radial(ang, RL + 0.03f, 0f);
                    float h = yLowerTop - 0.012f;
                    legs.Box(f, 0, 0.012f + h * 0.5f, 0, 0.16f, h, 0.025f);           // flat plate
                    legs.Box(f, 0, 0.012f + h * 0.5f - 0.05f, -0.03f, 0.025f, h - 0.1f, 0.06f); // stiffener back to shell
                    var ff = W.Radial(ang, RL + 0.07f, 0f);
                    feet.Box(ff, 0, 0.012f + 0.01f, 0, 0.22f, 0.02f, 0.18f);
                }

                var rail = NewPart("Supports", "Rail", Mat.Polished);
                rail.Revolve(W, Prof.Circle(RL + 0.05f, yDishTop - 0.02f, 0.012f, 10), seg, true);
            }

            // ---------------- ports
            if (S.ports) BuildPorts(W, segTops, segBottoms, yDishTop, yLowerTop, yBigFlangeTop, yWideTop, yDishApex);

            foreach (var p in parts) p.Scale(S.diameter);
            return parts;
        }

        /// Rectangular lifting lug: welded rounded pad + bar on two standoffs.
        static void Lug(Part p, Frame f, float tiltDeg)
        {
            if (Mathf.Abs(tiltDeg) > 0.01f)
            {
                float a = tiltDeg * Mathf.Deg2Rad;
                Vector3 nz = (f.z * Mathf.Cos(a) + f.y * Mathf.Sin(a)).normalized;
                Vector3 ny = (f.y * Mathf.Cos(a) - f.z * Mathf.Sin(a)).normalized;
                f = new Frame(f.o, f.x, ny, nz);
            }
            p.Extrude(f, Prof.RoundedRect(0.075f, 0.115f, 0.015f, 3), -0.004f, 0.006f);
            p.Box(f, 0, 0.03f, 0.018f, 0.018f, 0.014f, 0.024f);
            p.Box(f, 0, -0.03f, 0.018f, 0.018f, 0.014f, 0.024f);
            p.Box(f, 0, 0f, 0.034f, 0.02f, 0.085f, 0.012f);
        }

        // ---------------------------------------------------------------- ports
        static int portCount;

        /// CF-style port. Frame Y = port axis (pointing out of the vessel), origin on the shell surface.
        static Part Port(string name, Frame f, float rn, float len, float rf, float tf, bool cover, float coverT = 0.012f, int segs = 24)
        {
            var p = NewPart("Ports", name + "_" + (++portCount), Mat.Polished);
            float depth = 0.04f;
            p.Revolve(f, Prof.Ring(0f, rn, -depth, len), segs, true);                 // nozzle
            p.Revolve(f, Prof.Ring(cover ? 0f : rn * 0.85f, rf, len, len + tf), segs, true); // flange
            float top = len + tf;
            if (cover)
            {
                p.Revolve(f, Prof.Ring(0f, rf * 0.995f, top, top + coverT), segs, true);
                top += coverT;
            }
            if (S.bolts != BoltStyle.None)
            {
                int nb = Mathf.Clamp(Mathf.RoundToInt(rf * 2f * Mathf.PI / 0.022f), 6, 40);
                float rb = rf - Mathf.Max(0.008f, (rf - rn) * 0.45f);
                float br = Mathf.Clamp(rf * 0.07f, 0.0045f, 0.011f);
                if (S.bolts == BoltStyle.Decal)
                {
                    // one flat textured square per bolt, lying just above the flange face
                    p.cur = Mat.BoltDecal;
                    float hs = br * 1.25f;        // half size of the square (texture washer ~ 0.92 of it)
                    float lift = 0.0004f;         // tiny gap so it doesn't z-fight with the flange
                    var uvq = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                    for (int i = 0; i < nb; i++)
                    {
                        float a = 2f * Mathf.PI * (i + 0.5f) / nb;
                        Vector3 c = f.P(rb * Mathf.Cos(a), top + lift, rb * Mathf.Sin(a));
                        // square edges along radial (rd) and tangential (td) directions, so every bolt is rotated the same way
                        Vector3 rd = f.V(Mathf.Cos(a), 0, Mathf.Sin(a));
                        Vector3 td = f.V(-Mathf.Sin(a), 0, Mathf.Cos(a));
                        var q = new[] { c - td * hs - rd * hs, c + td * hs - rd * hs, c + td * hs + rd * hs, c - td * hs + rd * hs };
                        p.Poly(q, uvq, f.y, 0);
                    }
                    p.cur = Mat.Polished;
                    return p;
                }
                p.cur = Mat.Bolt;
                for (int i = 0; i < nb; i++)
                {
                    float a = 2f * Mathf.PI * (i + 0.5f) / nb;
                    var bf = new Frame(f.P(rb * Mathf.Cos(a), top, rb * Mathf.Sin(a)), f.x, f.y, f.z);
                    p.Revolve(bf, Prof.Ring(0f, br, 0f, br * 0.9f), 6, true, false);
                    var bf2 = new Frame(f.P(rb * Mathf.Cos(a), len, rb * Mathf.Sin(a)), f.x, f.y, f.z);
                    p.Revolve(bf2, Prof.Ring(0f, br, -br * 0.9f, 0f), 6, true, false);   // nut under the flange
                }
                p.cur = Mat.Polished;
            }
            return p;
        }

        static Frame OnShell(float deg, float r, float h, float tiltDownDeg = 0f)
        {
            Frame rf = Frame.World.Radial(deg, r, h);
            Vector3 axis = rf.z;
            if (Mathf.Abs(tiltDownDeg) > 0.01f)
            {
                float t = tiltDownDeg * Mathf.Deg2Rad;
                axis = rf.z * Mathf.Cos(t) - rf.y * Mathf.Sin(t);
            }
            return Frame.Axis(rf.o, axis, rf.y);
        }

        static void BuildPorts(Frame W, List<float> segTops, List<float> segBottoms, float yDishTop,
                               float yLowerTop, float yBigFlangeTop, float yWideTop, float yDishApex)
        {
            portCount = 0;
            float yc = (yDishTop + yLowerTop) * 0.5f + 0.02f; // centre height of big lower ports

            // NOTE on port frames: local Y = port axis (outward), local X = up, local Z = sideways.

            // Big beam port with bellows (towards the beam tube / next tower), 0 deg
            {
                Frame f = OnShell(0f, RL, yc);
                var p = Port("BeamPort_Bellows", f, 0.22f, 0.06f, 0.27f, 0.05f, false, 0.012f, seg);
                p.Revolve(f, Prof.Bellows(0.20f, 0.235f, 0.11f, 0.45f, 13, 0.006f), seg, true);
                p.Revolve(f, Prof.Ring(0.19f, 0.27f, 0.45f, 0.50f), seg, true);   // end flange
                p.Revolve(f, Prof.Ring(0.19f, 0.215f, 0.50f, 0.75f), seg, true);  // pipe stub
            }

            // Big access door with dished cover, three small CF nozzles and a lifting bracket, 90 deg
            {
                Frame f = OnShell(90f, RL, yc);
                Port("AccessDoor", f, 0.22f, 0.06f, 0.27f, 0.06f, false, 0.012f, seg);
                var cov = NewPart("Ports", "AccessDoorCover", Mat.Polished);
                const float cr = 0.235f, ch = 0.08f, cb = 0.12f;
                cov.Revolve(f, Prof.Head(cr, ch, 0.008f, cb, 0f, true, 10), seg, true);
                for (int i = -1; i <= 1; i++)
                {
                    float side = i * 0.10f;
                    float h = cb + ch * Mathf.Sqrt(Mathf.Max(0f, 1f - (side * side) / (cr * cr))) - 0.01f;
                    Port("DoorNozzle", Frame.Axis(f.P(0f, h, side), f.y, f.x), 0.032f, 0.06f, 0.055f, 0.02f, true);
                }
                var tri = NewPart("Ports", "DoorBracket", Mat.Shell);
                Frame pf = new Frame(f.o, f.y, f.x, f.z); // plate in the plane of axis + up
                tri.Extrude(pf, new[] { new Vector2(0.10f, 0.25f), new Vector2(0.17f, 0.37f), new Vector2(0.03f, 0.37f) },
                            -0.007f, 0.007f);
            }

            // Big blank port opposite the bellows, 180 deg
            Port("BeamPort_Blank", OnShell(180f, RL, yc), 0.22f, 0.06f, 0.27f, 0.05f, true, 0.035f, seg);

            // 270 deg: cluster of CF160 + CF63 blank flanges
            Port("CF160", OnShell(260f, RL, yDishTop + 0.20f), 0.07f, 0.06f, 0.10f, 0.022f, true, 0.014f, 32);
            Port("CF160", OnShell(280f, RL, yDishTop + 0.20f), 0.07f, 0.06f, 0.10f, 0.022f, true, 0.014f, 32);
            Port("CF100", OnShell(270f, RL, yDishTop + 0.42f), 0.05f, 0.06f, 0.075f, 0.02f, true);
            for (int k = 0; k < 2; k++)
                Port("CF63", OnShell(262f + 16f * k, RL, yLowerTop - 0.08f), 0.025f, 0.05f, 0.045f, 0.018f, true);

            // Columns of small CF blank flanges either side of every leg
            for (int leg = 0; leg < 4; leg++)
            {
                float la = 45f + 90f * leg;
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 3; k++)
                        Port("CF63", OnShell(la + side * 14f, RL, yLowerTop - 0.09f - 0.10f * k), 0.025f, 0.05f, 0.045f, 0.018f, true);
            }

            // Bottom (dished head) ports pointing down/out
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + 30f;
                float rr = 0.36f;
                // point on the ellipse under the chamber
                float depth = yDishTop - yDishApex;
                float s = Mathf.Sqrt(Mathf.Max(0f, 1f - (rr * rr) / (RL * RL)));
                float hy = yDishTop - depth * s;
                Frame rf = W.Radial(a, rr, hy);
                Vector3 axis = (-rf.y * 0.85f + rf.z * 0.5f).normalized;
                Port("BottomCF", Frame.Axis(rf.o, axis, rf.z), 0.035f, 0.06f, 0.06f, 0.02f, true);
            }

            // Wide section: row of CF flanges + two angled nozzles
            float yRow = yBigFlangeTop + 0.30f * (yWideTop - yBigFlangeTop);
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f + 11.25f;
                if (i == 3 || i == 11)   // two angled nozzles in the row (as in the photos)
                    Port("WideAngled", OnShell(a, RW, yRow + 0.02f, 25f), 0.04f, 0.10f, 0.07f, 0.022f, true);
                else
                    Port("WideCF", OnShell(a, RW, yRow), 0.035f, 0.05f, 0.065f, 0.02f, true);
            }

            // Viewports / blank 'sun' flanges on the upper segments
            for (int s = 0; s < segTops.Count; s++)
            {
                float mid = (segTops[s] + segBottoms[s]) * 0.5f;
                float a = (s % 2 == 0) ? 300f : 250f;
                Port("Viewport", OnShell(a, RU, mid), 0.065f, 0.035f, 0.085f, 0.022f, true, 0.014f, 32);
            }
        }
    }

#if !TOWER_TEST
    // ------------------------------------------------------------------ Unity editor window
    public class TowerBuilderWindow : EditorWindow
    {
        TowerSettings settings = new TowerSettings();
        Material shellMat, polishedMat, boltMat, weldMat;
        Vector3 placeAt = new Vector3(5f, 0f, 0f);

        const string kShell = "Assets/Models/Materials/Yellowy Steel.mat";
        const string kPolished = "Assets/Models/Materials/PolishedSteel.mat";
        const string kBolt = "Assets/Models/Materials/BoltSteel.mat";
        const string kWeld = "Assets/Models/Materials/Weld 3K/WeldTube.mat";

        [MenuItem("Tools/ETpathfinder Tower Builder")]
        static void Open() => GetWindow<TowerBuilderWindow>("ETpf Tower");

        void OnEnable()
        {
            if (!shellMat) shellMat = AssetDatabase.LoadAssetAtPath<Material>(kShell);
            if (!polishedMat) polishedMat = AssetDatabase.LoadAssetAtPath<Material>(kPolished);
            if (!boltMat) boltMat = AssetDatabase.LoadAssetAtPath<Material>(kBolt);
            if (!weldMat) weldMat = AssetDatabase.LoadAssetAtPath<Material>(kWeld);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Dimensions", EditorStyles.boldLabel);
            settings.diameter = EditorGUILayout.FloatField(new GUIContent("Upper shell diameter (m)",
                "D. Everything else is scaled from this. Total height is about 3.3 x D."), settings.diameter);
            settings.upperSegments = EditorGUILayout.IntSlider("Upper shell rings", settings.upperSegments, 1, 4);
            settings.radialSegments = EditorGUILayout.IntSlider("Radial segments", settings.radialSegments, 24, 128);
            EditorGUILayout.HelpBox("Total height ≈ " + (settings.diameter * (3.27f + 0.485f * (settings.upperSegments - 2))).ToString("0.00") + " m", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detail", EditorStyles.boldLabel);
            settings.ports = EditorGUILayout.Toggle("Ports / flanges / doors", settings.ports);
            settings.bolts = (BoltStyle)EditorGUILayout.EnumPopup(new GUIContent("Bolts",
                "Decal = flat painted-on bolts (2 triangles each, cheap). Mesh = real 3D bolts (about 25x more triangles)."), settings.bolts);
            settings.supports = EditorGUILayout.Toggle("Legs / floor plate", settings.supports);
            settings.lugs = EditorGUILayout.Toggle("Lugs / ribs / tabs", settings.lugs);
            settings.welds = EditorGUILayout.Toggle("Weld beads", settings.welds);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);
            shellMat = (Material)EditorGUILayout.ObjectField("Shell", shellMat, typeof(Material), false);
            polishedMat = (Material)EditorGUILayout.ObjectField("Flanges / polished", polishedMat, typeof(Material), false);
            boltMat = (Material)EditorGUILayout.ObjectField("Bolts", boltMat, typeof(Material), false);
            weldMat = (Material)EditorGUILayout.ObjectField("Welds", weldMat, typeof(Material), false);

            EditorGUILayout.Space();
            placeAt = EditorGUILayout.Vector3Field("Place at (world)", placeAt);

            EditorGUILayout.Space();
            if (GUILayout.Button("Build tower", GUILayout.Height(32)))
                BuildInScene();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Already built a tower with 3D bolts and edited it? Select it and use this to delete the 3D bolt geometry (faces using the Bolts material) without rebuilding.", MessageType.Info);
            if (GUILayout.Button("Remove 3D bolts from selection"))
                StripBolts();
        }

        Material Fallback(string name, Color c, float metallic, float smooth)
        {
            string path = "Assets/Models/Materials/ETpf_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { color = c };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (!AssetDatabase.IsValidFolder("Assets/Models")) AssetDatabase.CreateFolder("Assets", "Models");
            if (!AssetDatabase.IsValidFolder("Assets/Models/Materials")) AssetDatabase.CreateFolder("Assets/Models", "Materials");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        const string kDecalDir = "Assets/Models/Materials/Bolts";

        /// Loads (or creates) the cut-out bolt decal material from the two PNGs in Assets/Models/Materials/Bolts.
        static Material BoltDecalMaterial()
        {
            string matPath = kDecalDir + "/BoltDecal.mat";
            string albPath = kDecalDir + "/BoltDecal_Albedo.png";
            string nrmPath = kDecalDir + "/BoltDecal_Normal.png";

            // texture import settings: alpha cut-out that survives mipmapping, real normal map
            var ai = AssetImporter.GetAtPath(albPath) as TextureImporter;
            if (ai != null && (!ai.alphaIsTransparency || !ai.mipMapsPreserveCoverage || ai.wrapMode != TextureWrapMode.Clamp))
            {
                ai.alphaIsTransparency = true;
                ai.mipMapsPreserveCoverage = true;
                ai.alphaTestReferenceValue = 0.5f;
                ai.wrapMode = TextureWrapMode.Clamp;
                ai.SaveAndReimport();
            }
            var ni = AssetImporter.GetAtPath(nrmPath) as TextureImporter;
            if (ni != null && (ni.textureType != TextureImporterType.NormalMap || ni.wrapMode != TextureWrapMode.Clamp))
            {
                ni.textureType = TextureImporterType.NormalMap;
                ni.wrapMode = TextureWrapMode.Clamp;
                ni.SaveAndReimport();
            }

            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m) return m;

            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(albPath);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
            if (!alb) Debug.LogWarning("ETpf Tower: " + albPath + " not found - bolt decals will show as grey squares.");

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "BoltDecal" };
            if (alb) m.mainTexture = alb;                       // _BaseMap in URP
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (nrm && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", nrm);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
            // alpha clipping (cut-out), stays opaque/cheap
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            if (!AssetDatabase.IsValidFolder(kDecalDir)) AssetDatabase.CreateFolder("Assets/Models/Materials", "Bolts");
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        /// Deletes every face that uses the 'Bolts' material from the selected ProBuilder objects (and their children).
        void StripBolts()
        {
            int removed = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var pb in go.GetComponentsInChildren<ProBuilderMesh>(true))
                {
                    var mats = pb.GetComponent<MeshRenderer>().sharedMaterials;
                    var del = new List<Face>();
                    foreach (var f in pb.faces)
                    {
                        int si = Mathf.Clamp(f.submeshIndex, 0, mats.Length - 1);
                        var m = mats.Length > 0 ? mats[si] : null;
                        if (m != null && (m == boltMat || m.name.ToLower().Contains("bolt")) && !m.name.Contains("Decal"))
                            del.Add(f);
                    }
                    if (del.Count == 0) continue;
                    Undo.RegisterCompleteObjectUndo(new Object[] { pb, pb.GetComponent<MeshRenderer>() }, "Remove 3D bolts");
                    pb.DeleteFaces(del);
                    pb.ToMesh();
                    pb.Refresh();
                    removed += del.Count;
                }
            Debug.Log("ETpf Tower: removed " + removed + " bolt faces.");
        }

        void BuildInScene()
        {
            var mats = new Material[5];
            mats[(int)Mat.BoltDecal] = BoltDecalMaterial();
            mats[(int)Mat.Shell] = shellMat ? shellMat : Fallback("Shell", new Color(0.72f, 0.67f, 0.57f), 0.85f, 0.45f);
            mats[(int)Mat.Polished] = polishedMat ? polishedMat : Fallback("Polished", new Color(0.8f, 0.8f, 0.82f), 1f, 0.8f);
            mats[(int)Mat.Bolt] = boltMat ? boltMat : Fallback("Bolt", new Color(0.6f, 0.6f, 0.62f), 1f, 0.6f);
            mats[(int)Mat.Weld] = weldMat ? weldMat : mats[(int)Mat.Shell];

            List<Part> parts = TowerGeometry.Build(settings);

            var root = new GameObject("ETpf_Tower_Generated");
            Undo.RegisterCreatedObjectUndo(root, "Build ETpf Tower");
            root.transform.position = placeAt;
            var groups = new Dictionary<string, Transform>();

            int done = 0;
            try
            {
                foreach (var part in parts)
                {
                    EditorUtility.DisplayProgressBar("ETpf Tower", part.name, (float)done++ / parts.Count);
                    if (part.faces.Count == 0) continue;
                    if (!groups.TryGetValue(part.group, out Transform g))
                    {
                        g = new GameObject(part.group).transform;
                        g.SetParent(root.transform, false);
                        groups[part.group] = g;
                    }
                    CreateProBuilderObject(part, mats, g);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        static void CreateProBuilderObject(Part part, Material[] allMats, Transform parent)
        {
            // map the materials used by this part onto consecutive submeshes
            var used = new List<Mat>();
            foreach (var m in part.mats) if (!used.Contains(m)) used.Add(m);

            var faces = new List<Face>(part.faces.Count);
            for (int i = 0; i < part.faces.Count; i++)
            {
                var f = new Face(part.faces[i]);
                f.smoothingGroup = part.smooth[i];
                f.submeshIndex = used.IndexOf(part.mats[i]);
                faces.Add(f);
            }

            ProBuilderMesh pb = ProBuilderMesh.Create();
            pb.gameObject.name = part.name;
            var mr = pb.GetComponent<MeshRenderer>();
            var shared = new Material[used.Count];
            for (int i = 0; i < used.Count; i++) shared[i] = allMats[(int)used[i]];
            mr.sharedMaterials = shared;

            pb.RebuildWithPositionsAndFaces(part.pos, faces);
            pb.textures = part.uv;
            foreach (var f in pb.faces) f.manualUV = true;
            pb.ToMesh();
            pb.Refresh();

            pb.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(pb.gameObject, "Build ETpf Tower");
        }
    }
#endif
}

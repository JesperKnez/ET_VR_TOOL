using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Draws the traced beam: one pooled LineRenderer per straight segment and one glowing quad per
    /// spot where the beam lights up a surface. Owned and driven by <see cref="LaserEmitter"/>.
    /// </summary>
    internal sealed class LaserBeamVisuals
    {
        private const string BeamShaderName = "EinsteinTelescope/LaserBeam";
        private const float SpotSurfaceOffset = 0.002f;
        private const float MinimumBrightness = 0.08f;
        private const float BrightnessExponent = 0.4f;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_RadialId = Shader.PropertyToID("_Radial");
        private static Mesh s_QuadMesh;

        private readonly Transform m_Root;
        private readonly Material m_BeamMaterial;
        private readonly Material m_SpotMaterial;
        private readonly bool m_OwnsBeamMaterial;
        private readonly bool m_OwnsSpotMaterial;
        private readonly List<LineRenderer> m_Lines = new List<LineRenderer>();
        private readonly List<MeshRenderer> m_Spots = new List<MeshRenderer>();
        private readonly MaterialPropertyBlock m_PropertyBlock = new MaterialPropertyBlock();

        public LaserBeamVisuals(Transform owner, Material beamMaterial, Material spotMaterial)
        {
            var rootObject = new GameObject("Beam Visuals (runtime)");
            m_Root = rootObject.transform;
            m_Root.SetParent(owner, false);

            m_OwnsBeamMaterial = beamMaterial == null;
            m_OwnsSpotMaterial = spotMaterial == null;
            m_BeamMaterial = beamMaterial != null ? beamMaterial : CreateFallbackMaterial(false);
            m_SpotMaterial = spotMaterial != null ? spotMaterial : CreateFallbackMaterial(true);
        }

        public void Render(IReadOnlyList<LaserSegment> segments, IReadOnlyList<LaserSpot> spots, Color beamColor,
            float referencePower, float widthScale, float spotScale, bool drawSpots)
        {
            Render(segments, spots, beamColor, referencePower, widthScale, spotScale, drawSpots, beamColor);
        }

        /// <summary>As above; highlighted segments and spots (light from the selected mirror) use <paramref name="highlightColor"/>.</summary>
        public void Render(IReadOnlyList<LaserSegment> segments, IReadOnlyList<LaserSpot> spots, Color beamColor,
            float referencePower, float widthScale, float spotScale, bool drawSpots, Color highlightColor)
        {
            m_PropertyBlock.Clear();

            for (int i = 0; i < segments.Count; i++)
            {
                LineRenderer line = GetLine(i);
                LaserSegment segment = segments[i];
                Color brightness = BrightnessColor(segment.Power, referencePower);

                line.SetPosition(0, segment.Start);
                line.SetPosition(1, segment.End);
                line.startWidth = Mathf.Max(0.0005f, 2f * segment.StartRadius * widthScale);
                line.endWidth = Mathf.Max(0.0005f, 2f * segment.EndRadius * widthScale);
                line.startColor = brightness;
                line.endColor = brightness;
                Color color = segment.Highlighted ? highlightColor : beamColor;
                m_PropertyBlock.SetColor(s_BaseColorId, color);
                m_PropertyBlock.SetColor(s_ColorId, color);
                line.SetPropertyBlock(m_PropertyBlock);
                line.enabled = true;
            }

            for (int i = segments.Count; i < m_Lines.Count; i++)
            {
                m_Lines[i].enabled = false;
            }

            int spotCount = drawSpots ? spots.Count : 0;
            for (int i = 0; i < spotCount; i++)
            {
                MeshRenderer spotRenderer = GetSpot(i);
                LaserSpot spot = spots[i];
                Vector3 normal = spot.Normal.sqrMagnitude > 1e-6f ? spot.Normal.normalized : Vector3.up;
                Transform spotTransform = spotRenderer.transform;

                spotTransform.SetPositionAndRotation(spot.Point + normal * SpotSurfaceOffset, Quaternion.LookRotation(normal));
                spotTransform.localScale = Vector3.one * Mathf.Max(0.002f, 2f * spot.Radius * spotScale);

                float brightness = BrightnessColor(spot.Power, referencePower).r;
                Color spotColor = spot.Highlighted ? highlightColor : beamColor;
                m_PropertyBlock.SetColor(s_BaseColorId, spotColor * brightness);
                m_PropertyBlock.SetColor(s_ColorId, spotColor * brightness);
                spotRenderer.SetPropertyBlock(m_PropertyBlock);
                spotRenderer.enabled = true;
            }

            for (int i = spotCount; i < m_Spots.Count; i++)
            {
                m_Spots[i].enabled = false;
            }
        }

        public void Hide()
        {
            // Null checks: during scene unload the pooled children can be destroyed before the laser.
            foreach (LineRenderer line in m_Lines)
            {
                if (line != null)
                {
                    line.enabled = false;
                }
            }

            foreach (MeshRenderer spot in m_Spots)
            {
                if (spot != null)
                {
                    spot.enabled = false;
                }
            }
        }

        public void Dispose()
        {
            if (m_Root != null)
            {
                Object.Destroy(m_Root.gameObject);
            }

            if (m_OwnsBeamMaterial && m_BeamMaterial != null)
            {
                Object.Destroy(m_BeamMaterial);
            }

            if (m_OwnsSpotMaterial && m_SpotMaterial != null)
            {
                Object.Destroy(m_SpotMaterial);
            }
        }

        private static Color BrightnessColor(float power, float referencePower)
        {
            float fraction = referencePower > 0f ? power / referencePower : 0f;
            float brightness = Mathf.Clamp(Mathf.Pow(Mathf.Clamp01(fraction), BrightnessExponent), MinimumBrightness, 1f);
            return new Color(brightness, brightness, brightness, 1f);
        }

        private LineRenderer GetLine(int index)
        {
            while (m_Lines.Count <= index)
            {
                var lineObject = new GameObject("Beam Segment " + m_Lines.Count);
                lineObject.transform.SetParent(m_Root, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.sharedMaterial = m_BeamMaterial;
                line.enabled = false;
                m_Lines.Add(line);
            }

            return m_Lines[index];
        }

        private MeshRenderer GetSpot(int index)
        {
            while (m_Spots.Count <= index)
            {
                var spotObject = new GameObject("Beam Spot " + m_Spots.Count);
                spotObject.transform.SetParent(m_Root, false);
                spotObject.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
                var spotRenderer = spotObject.AddComponent<MeshRenderer>();
                spotRenderer.sharedMaterial = m_SpotMaterial;
                spotRenderer.shadowCastingMode = ShadowCastingMode.Off;
                spotRenderer.receiveShadows = false;
                spotRenderer.lightProbeUsage = LightProbeUsage.Off;
                spotRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                spotRenderer.enabled = false;
                m_Spots.Add(spotRenderer);
            }

            return m_Spots[index];
        }

        private static Mesh GetQuadMesh()
        {
            if (s_QuadMesh != null)
            {
                return s_QuadMesh;
            }

            s_QuadMesh = new Mesh { name = "Laser Spot Quad" };
            s_QuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            s_QuadMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            s_QuadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            s_QuadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_QuadMesh.RecalculateBounds();
            return s_QuadMesh;
        }

        private static Material CreateFallbackMaterial(bool radial)
        {
            Shader shader = Shader.Find(BeamShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = radial ? "Laser Spot (runtime)" : "Laser Beam (runtime)" };
            if (material.HasProperty(s_RadialId))
            {
                material.SetFloat(s_RadialId, radial ? 1f : 0f);
            }

            return material;
        }
    }
}

// Lumi's painted shell: satin white paint over metal, with bare metal showing on worn edges.
//
// Where the wear goes comes from T_Lumi_EdgeWear (baked in Blender: brightest on the sharpest edges and
// corners, fading away from them). Noise turns that gradient into ragged chips, a slow pattern leaves
// some stretches of edge clean, and small random specks appear anywhere on the shell.
// Everything else is URP Lit (see LumiForwardPass.hlsl).
Shader "EinsteinTelescope/Lumi/Shell"
{
    Properties
    {
        [Header(Paint)]
        _BaseColor("Paint Colour", Color) = (0.945, 0.961, 0.969, 1)
        _Smoothness("Paint Smoothness", Range(0, 1)) = 0.55

        [Header(Bare Metal on Worn Edges)]
        _MetalColor("Metal Colour", Color) = (0.62, 0.64, 0.66, 1)
        _MetalSmoothness("Metal Smoothness", Range(0, 1)) = 0.7
        _MetalMetallic("Metal Metallic (high = mirrors the room, so it looks black in a dark room)", Range(0, 1)) = 0.4

        [Header(Edge Wear)]
        [NoScaleOffset] _EdgeWearMap("Edge Wear Mask (T_Lumi_EdgeWear)", 2D) = "black" {}
        _WearAmount("Wear Amount (0 = new, higher = spreads from the sharpest edges)", Range(0, 1)) = 0.5
        _WearSoftness("Chip Edge Softness", Range(0.01, 0.3)) = 0.05
        _NoiseScale("Chip Pattern Scale (higher = smaller chips)", Range(20, 600)) = 180
        _ChipBreakup("Chip Raggedness", Range(0, 1)) = 0.55
        _PatchStrength("Clean Stretches (0 = wear along every edge)", Range(0, 1)) = 0.5
        _PatchScale("Clean Stretch Size (higher = shorter stretches)", Range(1, 30)) = 7

        [Header(Random Specks)]
        _SpeckDensity("Speck Density (0 = none)", Range(0, 0.5)) = 0.05
        _SpeckScale("Speck Scale (higher = smaller specks)", Range(20, 400)) = 110

        // URP Lit internals (kept so URP's shadow/depth passes and SRP code find what they expect)
        [HideInInspector] _Metallic("Metallic", Range(0, 1)) = 0
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1
        [HideInInspector] _OcclusionStrength("Occlusion", Float) = 1
        [HideInInspector] _Cutoff("Alpha Cutoff", Float) = 0.5
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #include_with_pragmas "LumiForwardPass.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

            TEXTURE2D(_EdgeWearMap);
            SAMPLER(sampler_EdgeWearMap);
            half4 _MetalColor;
            half _MetalSmoothness;
            half _MetalMetallic;
            half _WearAmount;
            half _WearSoftness;
            float _NoiseScale;
            half _ChipBreakup;
            half _PatchStrength;
            float _PatchScale;
            half _SpeckDensity;
            float _SpeckScale;

            float LumiHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth random pattern (0..1) that varies over about one unit of p.
            float LumiValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = LumiHash(i);
                float b = LumiHash(i + float2(1, 0));
                float c = LumiHash(i + float2(0, 1));
                float d = LumiHash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // At most one speck per grid cell, with a random size and position inside it (0..1 = how much speck).
            float LumiSpecks(float2 uv)
            {
                float2 p = uv * _SpeckScale;
                float blur = max(0.02, length(fwidth(p))); // measured before the early return: GPUs need it for every pixel
                float2 cell = floor(p);
                if (LumiHash(cell) > _SpeckDensity) return 0.0;

                float size = LumiHash(cell + 11.3);
                float radius = 0.05 + 0.2 * size * size * size; // mostly tiny, a few bigger
                float2 center = radius + float2(LumiHash(cell + 23.7), LumiHash(cell + 41.1)) * (1.0 - 2.0 * radius);
                float dist = length(p - cell - center) + (LumiValueNoise(p * 5.0) - 0.5) * 0.14; // not quite round

                // Smaller than a pixel far away: fade out instead of shimmering.
                return (1.0 - smoothstep(radius - blur, radius + blur, dist)) * saturate(radius / blur);
            }

            void LumiShellSurface(float2 uv, out SurfaceData surfaceData)
            {
                InitializeStandardLitSurfaceData(uv, surfaceData);

                // UV0 is the shared Blender unwrap; the mask covers it exactly once (never tiled).
                half edge = SAMPLE_TEXTURE2D(_EdgeWearMap, sampler_EdgeWearMap, uv).r;

                // A slow pattern weakens some stretches, so not every edge wears the same.
                edge *= 1.0 - _PatchStrength * (1.0 - smoothstep(0.3, 0.7, LumiValueNoise(uv * _PatchScale)));

                // Two layers of noise: big chips plus small ragged detail.
                float2 p = uv * _NoiseScale;
                half noise = 0.6 * LumiValueNoise(p) + 0.4 * LumiValueNoise(p * 2.7 + 17.0);

                // Worn where the mask, roughened by the noise, rises above the threshold.
                half threshold = 1.0 - _WearAmount;
                half wear = smoothstep(threshold - _WearSoftness, threshold + _WearSoftness, edge + (noise - 0.5) * _ChipBreakup);
                wear = max(wear, LumiSpecks(uv));

                surfaceData.albedo = lerp(surfaceData.albedo, _MetalColor.rgb, wear);
                surfaceData.metallic = lerp(surfaceData.metallic, _MetalMetallic, wear);
                surfaceData.smoothness = lerp(surfaceData.smoothness, _MetalSmoothness, wear);
            }

            #define InitializeStandardLitSurfaceData LumiShellSurface
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }

        // Shadow, depth, depth-normal (SSAO) and motion-vector passes, copied unchanged from URP 17.6's
        // Lit.shader. (Borrowing them with UsePass made Unity drop the whole shader for Android/Quest.)
        // They read the same material properties as URP Lit, which is why the hidden Lit internals stay.

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SCREENSPACEREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // -------------------------------------
            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ _WRITE_SMOOTHNESS
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode" = "XRMotionVectors" }
            ColorMask RGBA

            // Stencil write for obj motion pixels
            Stencil
            {
                WriteMask 1
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ APPLICATION_SPACE_WARP_MOTION_TRANSPARENT
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #define APPLICATION_SPACE_WARP_MOTION 1

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

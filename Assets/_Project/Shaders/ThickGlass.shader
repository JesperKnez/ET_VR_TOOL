// Thick glass for URP (Unity 6) - VR-safe
// - Beer-Lambert absorption: greener/darker at grazing angles, like a train window
// - Fresnel reflections from reflection probes + main-light highlight
// - Optional world-space slab refraction, projected per eye (no screen-space wobble)
//
// Two modes:
//  * Default (Refraction OFF): two passes. "ThickGlassTint" multiplies what is behind the glass by the
//    absorption colour, "ThickGlassForward" adds reflection + highlight. Everything behind the glass stays
//    visible, also TRANSPARENT objects (towers made clear by TowerTransparency, laser beams).
//  * Refraction ON: the background is read from the Opaque Texture and shifted slightly. That texture is
//    captured BEFORE transparent objects are drawn, so transparent objects behind the glass disappear.
//    Needs URP Asset -> Opaque Texture ON. Without it (Quest build) ThickGlassSceneColor.cs enables
//    _THICKGLASS_NO_SCENE_COLOR and the default mode is used anyway.
// Use on a SOLID mesh (front + back faces modeled, e.g. Solidify in Blender).
// Give the edge faces a separate opaque material (see M_GlassEdge).

Shader "XR/ThickGlass"
{
    Properties
    {
        _Thickness ("Thickness (m) - match your model", Range(0.001, 0.1)) = 0.02
        _IOR ("Index of Refraction", Range(1.0, 2.0)) = 1.5
        // How much of each colour channel is ABSORBED per meter of glass.
        // Higher R and B than G = greenish glass. (Not the visible colour!)
        _Absorption ("Absorption per meter (R, G, B, -)", Vector) = (3, 1.5, 2.5, 0)
        _AbsorptionScale ("Absorption Strength", Range(0, 5)) = 0.4
        _Smoothness ("Smoothness", Range(0, 1)) = 0.97
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 0.5
        _SpecularStrength ("Sun Highlight Strength", Range(0, 5)) = 1
        [Toggle(_REFRACTION_ON)] _Refraction ("Refraction (hides transparent objects behind the glass!)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

        // Refraction only when the material asks for it AND this camera has an opaque texture
        #if defined(_REFRACTION_ON) && !defined(_THICKGLASS_NO_SCENE_COLOR)
            #define THICKGLASS_USE_SCENE_COLOR 1
        #endif

        CBUFFER_START(UnityPerMaterial)
            float  _Thickness;
            float  _IOR;
            float4 _Absorption;
            float  _AbsorptionScale;
            float  _Smoothness;
            float  _ReflectionStrength;
            float  _SpecularStrength;
            float  _Refraction;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert (Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }

        // Everything both passes need about the light path through the slab
        struct GlassTerms
        {
            float3 N, V, R;
            float  pathLength;
            half   F;
            half3  transmittance;
        };

        GlassTerms ComputeGlass (Varyings i)
        {
            GlassTerms g;
            g.N = normalize(i.normalWS);
            g.V = GetWorldSpaceNormalizeViewDir(i.positionWS);   // surface -> camera
            float NdotV = saturate(dot(g.N, g.V));

            // --- Slab refraction (world space) ---
            g.R = refract(-g.V, g.N, 1.0 / _IOR);
            float cosT = max(dot(-g.N, g.R), 0.05);
            g.pathLength = _Thickness / cosT;

            // --- Absorption (Beer-Lambert) ---
            g.transmittance = exp(-_Absorption.rgb * _AbsorptionScale * g.pathLength);

            // --- Fresnel (Schlick, F0 = 0.04 for glass) ---
            g.F = 0.04h + 0.96h * pow(1.0h - NdotV, 5.0h);
            return g;
        }
        ENDHLSL

        // Pass 1: multiply the background by the glass tint (skipped in refraction mode)
        Pass
        {
            Name "ThickGlassTint"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend DstColor Zero
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragTint
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _REFRACTION_ON
            #pragma multi_compile_fragment _ _THICKGLASS_NO_SCENE_COLOR

            half4 fragTint (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(THICKGLASS_USE_SCENE_COLOR)
                return half4(1, 1, 1, 1);   // the forward pass replaces the background itself
            #else
                GlassTerms g = ComputeGlass(i);
                return half4(g.transmittance * (1.0h - g.F), 1.0h);
            #endif
            }
            ENDHLSL
        }

        // Pass 2: reflection + highlight (and in refraction mode: the refracted background)
        Pass
        {
            Name "ThickGlassForward"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha: alpha 1 = replace (refraction mode), alpha 0 = add on top (default mode)
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma shader_feature_local_fragment _REFRACTION_ON
            #pragma multi_compile_fragment _ _THICKGLASS_NO_SCENE_COLOR

            // Projects a world position to screen UV for the CURRENT eye
            // (UNITY_MATRIX_VP is per eye in single-pass instanced).
            float2 WorldToScreenUV (float3 positionWS)
            {
                float4 cs = TransformWorldToHClip(positionWS);
                float2 ndc = cs.xy / cs.w;
                return float2(ndc.x, ndc.y * _ProjectionParams.x) * 0.5 + 0.5;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                GlassTerms g = ComputeGlass(i);
                float2 baseUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // --- Environment reflection (reflection probes) ---
                half  perceptualRoughness = 1.0h - _Smoothness;
                half3 reflDir = reflect(-g.V, g.N);
                half3 env = GlossyEnvironmentReflection(reflDir, i.positionWS, perceptualRoughness, 1.0h, baseUV);

                // --- Main light highlight (GGX) ---
                Light mainLight = GetMainLight();
                float3 H     = normalize(mainLight.direction + g.V);
                float  NdotH = saturate(dot(g.N, H));
                float  NdotL = saturate(dot(g.N, mainLight.direction));
                float  rough = max(perceptualRoughness * perceptualRoughness, 0.02);
                float  a2    = rough * rough;
                float  d     = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float  D     = a2 / (PI * d * d);
                half3  spec  = mainLight.color * min(D * g.F * NdotL * 0.25, 20.0) * _SpecularStrength;

                half3 surface = env * g.F * _ReflectionStrength + spec;

            #if defined(THICKGLASS_USE_SCENE_COLOR)
                // Apply the (tiny) lateral shift as a UV delta on top of this
                // pixel's own screen UV - robust against platform UV flips.
                float  cosI        = max(saturate(dot(g.N, g.V)), 0.05);
                float3 exitPos     = i.positionWS + g.R * g.pathLength;
                float3 straightPos = i.positionWS - g.V * (_Thickness / cosI);
                float3 samplePos   = i.positionWS + (exitPos - straightPos);
                float2 uv = saturate(baseUV + WorldToScreenUV(samplePos) - WorldToScreenUV(i.positionWS));

                half3 background = SampleSceneColor(uv);
                return half4(background * g.transmittance * (1.0h - g.F) + surface, 1.0h);
            #else
                return half4(surface, 0.0h);   // background was already tinted by ThickGlassTint
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}

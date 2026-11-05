// Shared "ForwardLit" setup for Lumi's shaders (Shell, FakeGlass, Screen).
//
// Lumi's shaders are URP Lit with ONE step replaced: how the surface (colour, metal, smoothness,
// emission) is worked out. Everything else (lighting, shadows, reflection probes, the room's probe
// volumes, fog, Quest/XR single-pass rendering) is URP's own code, so Lumi is lit exactly like
// everything else in the scene.
//
// The keyword list below is copied from URP 17.6's Lit.shader (ForwardLit pass), minus the
// material keywords Lumi doesn't use. If a URP update changes Lit.shader, compare it with this list.
//
// How a shader uses this file (the target/vertex/fragment pragmas stay in the .shader itself,
// like in URP's Lit.shader, because Unity needs to see them in the shader file):
//   #pragma target 2.0
//   #pragma vertex LitPassVertex
//   #pragma fragment LitPassFragment
//   #include_with_pragmas "LumiForwardPass.hlsl"
//   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
//   ...own properties + a function  void MySurface(float2 uv, out SurfaceData s)
//      that calls InitializeStandardLitSurfaceData(uv, s) and then changes s...
//   #define InitializeStandardLitSurfaceData MySurface
//   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
// The #define makes URP's fragment shader call MySurface instead of the standard function.

// Universal Pipeline keywords
#if defined(UNITY_PLATFORM_META_QUEST)
#pragma multi_compile _ META_QUEST_LIGHTUNROLL
#endif
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
#pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
#pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile _ _LIGHT_LAYERS
#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#if defined(UNITY_PLATFORM_META_QUEST)
#pragma multi_compile _ META_QUEST_ORTHO_PROJ
#pragma multi_compile _ META_QUEST_NO_SPOTLIGHTS_LIGHT_LOOP
#endif
#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

// Unity defined keywords
#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
#pragma multi_compile _ SHADOWS_SHADOWMASK
#pragma multi_compile _ DIRLIGHTMAP_COMBINED
#pragma multi_compile _ LIGHTMAP_ON
#pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
#pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
#pragma multi_compile _ DYNAMICLIGHTMAP_ON
#pragma multi_compile _ USE_LEGACY_LIGHTMAPS
#pragma multi_compile _ LOD_FADE_CROSSFADE
#pragma multi_compile_fragment _ DEBUG_DISPLAY
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

// GPU instancing (also needed for single-pass instanced stereo on Quest)
#pragma multi_compile_instancing
#pragma instancing_options renderinglayer
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

// Lumi's mouth screen: glossy black glass showing one glowing frame of the mouth atlas.
//
// The mouth atlas is a grid of drawings (default 4 x 4), white or coloured line art on a
// transparent background, frame 0 in the top-left, counting left to right, top to bottom.
// _MouthColor tints the drawing, so the drawings themselves can stay plain white.
// The screen face's UVs fill the 0..1 square but the screen is about 2.5x wider than tall,
// so draw each cell in that wide shape (e.g. 512 x 200 px per cell).
// A script picks the frame by setting _Frame (expressions, lip sync).
Shader "EinsteinTelescope/Lumi/Screen"
{
    Properties
    {
        [Header(Screen Glass)]
        _BaseColor("Screen Colour (off)", Color) = (0.0196, 0.0235, 0.0314, 1)
        _Smoothness("Glossiness", Range(0, 1)) = 0.9

        [Header(Mouth)]
        [NoScaleOffset] _MouthAtlas("Mouth Atlas", 2D) = "black" {}
        [IntRange] _Columns("Atlas Columns", Range(1, 8)) = 4
        [IntRange] _Rows("Atlas Rows", Range(1, 8)) = 4
        [IntRange] _Frame("Frame (0 = top-left)", Range(0, 63)) = 0
        [HDR] _MouthColor("Mouth Colour", Color) = (0.73, 1.76, 1.55, 1)

        [Header(Screen Look)]
        _Backlight("Backlight (screen is on)", Range(0, 0.2)) = 0.02
        _Scanlines("Scanline Strength", Range(0, 1)) = 0.25
        _ScanlineCount("Scanline Count", Range(10, 200)) = 60

        // URP Lit internals
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
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #include_with_pragmas "LumiForwardPass.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

            TEXTURE2D(_MouthAtlas);
            SAMPLER(sampler_MouthAtlas);
            float _Columns;
            float _Rows;
            float _Frame;
            half4 _MouthColor;
            half _Backlight;
            half _Scanlines;
            float _ScanlineCount;

            void LumiScreenSurface(float2 uv, out SurfaceData surfaceData)
            {
                InitializeStandardLitSurfaceData(uv, surfaceData);

                float cols = max(1.0, round(_Columns));
                float rows = max(1.0, round(_Rows));
                float frame = round(_Frame);
                float col = fmod(frame, cols);
                float row = floor(frame / cols);

                // Texture V runs bottom-up, frames run top-down, hence (rows - 1 - row).
                // The small inset keeps neighbouring cells from bleeding in at the borders.
                float2 cellUV = (lerp(0.02, 0.98, uv) + float2(col, rows - 1.0 - row)) / float2(cols, rows);
                half4 mouth = SAMPLE_TEXTURE2D(_MouthAtlas, sampler_MouthAtlas, cellUV);
                half shape = max(mouth.r, max(mouth.g, mouth.b)) * mouth.a;

                half lines = 1.0 - _Scanlines * (0.5 + 0.5 * sin(uv.y * _ScanlineCount * 6.2831853));
                surfaceData.emission = _MouthColor.rgb * (shape + _Backlight) * lines;
            }

            #define InitializeStandardLitSurfaceData LumiScreenSurface
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

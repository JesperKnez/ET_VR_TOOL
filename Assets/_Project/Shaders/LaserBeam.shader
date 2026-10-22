// Additive glow for laser beams (LineRenderer) and laser spots (quads), for URP.
// The brightness follows a Gaussian profile, like the TEM00 mode of a real interferometer laser:
// I(r) = exp(-2 r^2 / w^2). With _Radial = 0 the profile runs across the line width,
// with _Radial = 1 it is a round spot. Works with VR single-pass instanced rendering.
Shader "EinsteinTelescope/LaserBeam"
{
    Properties
    {
        [HDR] _BaseColor ("Color (HDR)", Color) = (0, 2, 0, 1)
        _Profile ("Gaussian sharpness", Range(0.5, 20)) = 4.5
        _CoreBoost ("White-hot core", Range(0, 2)) = 0.6
        [MaterialToggle] _Radial ("Radial (spot)", Float) = 0
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

        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LaserGlow"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Profile;
                float _CoreBoost;
                float _Radial;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centred = input.uv * 2.0 - 1.0;
                float r2 = lerp(centred.y * centred.y, dot(centred, centred), _Radial);
                float glow = exp(-_Profile * r2);
                float colorStrength = saturate(max(max(_BaseColor.r, _BaseColor.g), _BaseColor.b));
                float core = exp(-_Profile * 6.0 * r2) * _CoreBoost * colorStrength;

                half3 rgb = (_BaseColor.rgb * glow + core.xxx) * input.color.rgb;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Anime/DollPupil"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _ToonProximityAmbience ("Toon Proximity Ambience", Color) = (1, 1, 1, 1)
        _PupilShrink ("Pupil Shrink", Range(1., 2.)) = 1.0
        _SideMultiplier ("Side Multiplier", Range(-1., 1.)) = 1.0
        _PupilRight ("Pupil Right", Range(-1., 1.)) = 0.0
        _PupilUp ("Pupil Up", Range(-1., 1.)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/AnimeCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _ToonProximityAmbience;
                float _PupilShrink;
                float _SideMultiplier;
                float _PupilRight;
                float _PupilUp;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;

                float2 uv = TRANSFORM_TEX(input.uv, _MainTex);
                uv = (uv - 0.5) * _PupilShrink + 0.5;
                uv.x += _PupilRight * _SideMultiplier;
                uv.y -= _PupilUp;
                output.uv = uv;

                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 worldNorm = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float atten = saturate((mainLight.shadowAttenuation * mainLight.distanceAttenuation - 0.5) * 2.0);
                float sun = atten;

                float3 ambient = SampleSH(worldNorm);
                float3 ambience = AnimeCalculateAmbience(input.positionWS, worldNorm, ambient, mainLight.color, sun, _ToonProximityAmbience.rgb);

                color.rgb *= ambience;
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}

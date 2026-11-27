Shader "Anime/DollBody"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _SkinColor ("Skin color", Color) = (1., 0.86, 0.82)
        _SkinShadeColor ("Skin Shade color", Color) = (1., 0.86, 0.82)
        _ToonProximityAmbience ("Toon Proximity Ambience", Color) = (0., 0., 0., 1.)
        _OutlineColor ("Outline color", Color) = (0., 0., 0.)
        _OutSizeMin ("Outline Size Min", Range(0., 0.003)) = 0.001
        _OutSizeMax ("Outline Size Max", Range(0., 0.003)) = 0.001
        _FingerNailColor ("Finger Nail Color", Color) = (1., 1., 1., 1.)
        _ToeNailColor ("Toe Nail Color", Color) = (1., 1., 1., 1.)
        _PhotoDataColor ("Photo Data Color", Color) = (0., 1., 0., 1.)
        _Dirt ("Dirt", Range(0., 1)) = 0
        _NeckColor ("Neck Transition Color", Color) = (1., 0.86, 0.82, 1.)
        _NeckBlendStrength ("Neck Blend Strength", Range(0., 1.)) = 0.0
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
            Name "Outline"
            Tags { "LightMode" = "Outline" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SkinColor;
                float4 _SkinShadeColor;
                float4 _ToonProximityAmbience;
                float4 _OutlineColor;
                float _OutSizeMin;
                float _OutSizeMax;
                float4 _FingerNailColor;
                float4 _ToeNailColor;
                float4 _PhotoDataColor;
                float _Dirt;
                float4 _NeckColor;
                float _NeckBlendStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 diff = GetCameraPositionWS() - positionWS;
                float dist = dot(diff, diff);
                float outlineScale = max(_OutSizeMin, min(_OutSizeMax, dist));
                float3 posWithOutline = input.positionOS.xyz + input.normalOS * outlineScale;
                output.positionCS = TransformObjectToHClip(posWithOutline);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 ambient = SampleSH(float3(0, 1, 0));
                return half4(_OutlineColor.rgb * ambient, 1.0);
            }
            ENDHLSL
        }

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
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float neckBlend : TEXCOORD4;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SkinColor;
                float4 _SkinShadeColor;
                float4 _ToonProximityAmbience;
                float4 _OutlineColor;
                float _OutSizeMin;
                float _OutSizeMax;
                float4 _FingerNailColor;
                float4 _ToeNailColor;
                float4 _PhotoDataColor;
                float _Dirt;
                float4 _NeckColor;
                float _NeckBlendStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                output.neckBlend = input.color.r;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 worldNorm = normalize(input.normalWS);
                float3 worldPos = input.positionWS;

                float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                Light mainLight = GetMainLight(shadowCoord);

                float worldRim = saturate(dot(mainLight.direction, worldNorm));
                float2 bodyTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rg;
                float3 color = _SkinColor.rgb * float3(1.0, bodyTex.rr);

                float isHand = step(0.75, bodyTex.g);
                float isFinger = step(0.15, bodyTex.g) * (1.0 - isHand);
                float neither = step(isHand + isFinger, 0.0);
                color = _FingerNailColor.rgb * isHand + _ToeNailColor.rgb * isFinger + color * neither;

                float neckBlend = saturate(input.neckBlend * _NeckBlendStrength);
                color = lerp(color, _NeckColor.rgb, neckBlend);

                color = ApplyGradualDirt(color, _Dirt, input.uv * 0.25);

                float3 worldViewDir = normalize(GetCameraPositionWS() - worldPos);
                float camRim = dot(worldViewDir, worldNorm);
                float atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                float sun = saturate(atten * worldRim) * 0.4;

                float3 ambient = SampleSH(worldNorm);
                float3 ambience = AnimeCalculateAmbience(worldPos, worldNorm, ambient, mainLight.color, sun, _ToonProximityAmbience.rgb);

                color *= saturate(ambience);
                color = ApplyColorFromLight(color, mainLight.color, sun, camRim, worldRim);
                color = MixFog(color, input.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
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

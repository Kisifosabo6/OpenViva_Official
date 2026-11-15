Shader "Custom/Sky/StylizedMoon"
{
    Properties
    {
        [MainTexture] _MainTex ("Moon Texture (RGBA)", 2D) = "white" {}
        [MainColor] _BaseColor ("Color Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Surface Brightness", Range(0.1, 2.0)) = 0.85
        _Contrast ("Detail Contrast", Range(0.5, 2.0)) = 1.15

        [Header(Atmospheric Halo)]
        _HaloColor ("Halo Glow Tint", Color) = (0.7, 0.85, 1.0, 1.0)
        _HaloIntensity ("Halo Glow Intensity", Range(0.0, 3.0)) = 1.0

        [Header(Limb Darkening)]
        _LimbDarkening ("Limb Darkening", Range(0.0, 1.0)) = 0.2
        _LimbPower ("Limb Power", Range(0.5, 3.0)) = 1.2

        [Header(Rendering)]
        [Toggle] _Billboard ("Always Face Camera (Billboard)", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Brightness;
                float _Contrast;
                float4 _HaloColor;
                float _HaloIntensity;
                float _LimbDarkening;
                float _LimbPower;
                float _Billboard;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                if (_Billboard > 0.5)
                {
                    // Calculate center position in world space
                    float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));

                    // Camera basis vectors in world space
                    float3 cameraRightWS = UNITY_MATRIX_V[0].xyz;
                    float3 cameraUpWS    = UNITY_MATRIX_V[1].xyz;

                    // Calculate scale factors from object-to-world matrix
                    float scaleX = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float scaleY = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));

                    float3 worldPos = centerWS 
                                    + cameraRightWS * (input.positionOS.x * scaleX)
                                    + cameraUpWS    * (input.positionOS.y * scaleY);

                    output.positionCS = TransformWorldToHClip(worldPos);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                }

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Distance from center of UV space [0, 1] -> normalized r where disc is ~0.40
                float2 centeredUV = input.uv - float2(0.5, 0.5);
                float r = length(centeredUV);
                float normR = r / 0.40;

                // Adjust contrast around midpoint 0.65
                float3 surface = texCol.rgb;
                surface = (surface - 0.65) * _Contrast + 0.65;
                surface *= _Brightness * _BaseColor.rgb;

                // Subtle limb darkening towards the edge of the disc
                if (normR <= 1.0)
                {
                    float limbFactor = pow(saturate(1.0 - normR), _LimbPower);
                    surface *= lerp(1.0 - _LimbDarkening, 1.0, limbFactor);
                }

                // If outside disc, apply halo tint and intensity
                if (normR > 1.0)
                {
                    surface = _HaloColor.rgb * _HaloIntensity * texCol.rgb;
                }

                float alpha = texCol.a * _BaseColor.a;

                return half4(surface, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

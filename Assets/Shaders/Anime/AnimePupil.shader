Shader "Anime/AnimePupilShader"
{
	Properties
	{
		_MainTex ("Main Texture", 2D) = "white" {}
		_ToonProximityAmbience ("Toon Proximity Ambience", Color) = (1., 1., 1.)
		_PupilShrink ("Pupil Shrink", Range(1., 2.)) = 1.0
		_SideMultiplier ("Side Multipler", Range(-1., 1.)) = 1.0
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

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include "AnimeShading.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float2 uv           : TEXCOORD0;
				float3 normalOS     : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS   : SV_POSITION;
				float2 uv           : TEXCOORD0;
				float3 worldNorm    : TEXCOORD1;
				float3 worldPos     : TEXCOORD2;
				float fogFactor     : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float3 _ToonProximityAmbience;
				float _PupilShrink;
				float _PupilRight;
				float _SideMultiplier;
				float _PupilUp;
			CBUFFER_END

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
				output.worldNorm = TransformObjectToWorldNormal(input.normalOS);

				float2 uv = input.uv;
				uv = (uv - 0.5) * _PupilShrink + 0.5;
				uv.x += _PupilRight * _SideMultiplier;
				uv.y -= _PupilUp;
				output.uv = TRANSFORM_TEX(uv, _MainTex);

				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float3 normalWS = normalize(input.worldNorm);
				float3 color = _MainTex.Sample(sampler_MainTex, input.uv).rgb;

				float4 shadowCoord = TransformWorldToShadowCoord(input.worldPos);
				Light mainLight = GetMainLight(shadowCoord);
				float atten = (mainLight.distanceAttenuation * mainLight.shadowAttenuation - 0.5) * 2.0;
				float sun = saturate(atten);

				float3 ambience = CalculateAdditionalLights(input.worldPos, normalWS);
				float3 envAmbient = SampleSH(normalWS);
				color *= ambience + saturate(lerp(envAmbient, mainLight.color, sun) + _ToonProximityAmbience);

				color = MixFog(color, input.fogFactor);
				return float4(color, 1.0);
			}
			ENDHLSL
		}

		// DepthOnly Pass
		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ZWrite On
			ColorMask R

			HLSLPROGRAM
			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthOnlyFragment
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS   : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings DepthOnlyVertex(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			float4 DepthOnlyFragment(Varyings input) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}

	Fallback "Universal Render Pipeline/Lit"
}

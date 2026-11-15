Shader "Anime/AnimeBody"
{
	Properties
	{
		_MainTex ("Main Texture", 2D) = "white" {}
		_SkinColor ("Skin color", Color) = (1., 0.86, 0.82)
		_SkinShadeColor ("Skin Shade color", Color) = (1., 0.86, 0.82)
		_ToonProximityAmbience ("Toon Proximity Ambience", Color) = (1., 1., 1.)
		_OutlineColor ("Outline color", Color) = (1., 1., 1.)
		_OutSizeMin ("Outline Size Min", Range(0., 0.003)) = 0.001
		_OutSizeMax ("Outline Size Max", Range(0., 0.003)) = 0.001
		_FingerNailColor ("Finger Nail Color", Color) = (1., 1., 1., 1.)
		_ToeNailColor ("Toe Nail Color", Color) = (1., 1., 1., 1.)
		_PhotoDataColor ("Photo Data Color", Color) = (0., 1., 0., 1.)
		_Dirt ("Dirt", Range(0., 1)) = 0
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

		// Outline Pass (Cull Front)
		Pass
		{
			Name "Outline"
			Cull Front
			ZWrite On

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
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

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float3 _SkinColor;
				float3 _SkinShadeColor;
				float3 _ToonProximityAmbience;
				float3 _OutlineColor;
				float _OutSizeMin;
				float _OutSizeMax;
				float4 _FingerNailColor;
				float4 _ToeNailColor;
				float4 _PhotoDataColor;
				float _Dirt;
			CBUFFER_END

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
				float3 diff = GetCameraPositionWS() - worldPos;
				float dist = dot(diff, diff);
				float outlineScale = max(_OutSizeMin, min(_OutSizeMax, dist));

				float3 positionExtruded = input.positionOS.xyz + input.normalOS * outlineScale;
				output.positionCS = TransformObjectToHClip(positionExtruded);
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float3 ambient = SampleSH(float3(0.0, 1.0, 0.0));
				return float4(_OutlineColor * ambient, 1.0);
			}
			ENDHLSL
		}

		// ForwardLit Pass
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
				float3 normalOS     : NORMAL;
				float2 uv           : TEXCOORD0;
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
			Texture2D _GlobalDirtTex;
			SamplerState sampler_GlobalDirtTex;

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float3 _SkinColor;
				float3 _SkinShadeColor;
				float3 _ToonProximityAmbience;
				float3 _OutlineColor;
				float _OutSizeMin;
				float _OutSizeMax;
				float4 _FingerNailColor;
				float4 _ToeNailColor;
				float4 _PhotoDataColor;
				float _Dirt;
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
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float3 normalWS = normalize(input.worldNorm);
				float3 worldViewDir = normalize(GetCameraPositionWS() - input.worldPos);
				float camRim = dot(worldViewDir, normalWS);

				float4 shadowCoord = TransformWorldToShadowCoord(input.worldPos);
				Light mainLight = GetMainLight(shadowCoord);
				float worldRim = saturate(dot(mainLight.direction, normalWS));
				float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
				float sun = saturate(atten * worldRim) * 0.4;

				float2 bodyTex = _MainTex.Sample(sampler_MainTex, input.uv).rg;
				float3 color = _SkinColor * float3(1.0, bodyTex.rr);

				// Apply nail colors:
				float isHand = step(0.75, bodyTex.g);
				float isFinger = step(0.15, bodyTex.g) * (1.0 - isHand);
				float neither = step(isHand + isFinger, 0.0);
				color = _FingerNailColor.rgb * isHand + _ToeNailColor.rgb * isFinger + color * neither;

				// Dirt:
				color = ApplyGradualDirt(color, _GlobalDirtTex, sampler_GlobalDirtTex, _Dirt, input.uv * 0.25);

				// Additional lights & Ambient:
				float3 ambience = CalculateAdditionalLights(input.worldPos, normalWS);
				float3 envAmbient = SampleSH(normalWS);
				ambience += lerp(envAmbient, mainLight.color, sun) + _ToonProximityAmbience;

				color *= saturate(ambience);
				color = ApplyColorFromLight(color, _SkinShadeColor, sun, camRim, worldRim, mainLight.color);

				color = MixFog(color, input.fogFactor);
				return float4(color, 1.0);
			}
			ENDHLSL
		}

		// ShadowCaster Pass
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex ShadowPassVertex
			#pragma fragment ShadowPassFragment
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS   : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float3 _LightDirection;
			float3 _LightPosition;

			Varyings ShadowPassVertex(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
				float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
				float3 lightDirectionWS = _LightDirection;
#endif
				output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
				output.positionCS = ApplyShadowClamping(output.positionCS);
				return output;
			}

			float4 ShadowPassFragment(Varyings input) : SV_Target
			{
				return 0;
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
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

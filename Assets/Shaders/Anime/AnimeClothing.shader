Shader "Anime/AnimeClothing"
{
	Properties
	{
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_ToonProximityAmbience ("Toon Proximity Ambience", Color) = (1., 1., 1.)
		_Cutoff ("Cutout", Range(0, 1)) = 0.5
		_OutSizeMin ("Outline Size Min", Range(0., 0.003)) = 0.001
		_OutSizeMax ("Outline Size Max", Range(0., 0.003)) = 0.001
		_PhotoDataColor ("Photo Data Color", Color) = (0., 1., 0., 1.)
		_Dirt ("Dirt", Range(0., 1)) = 0
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"Queue" = "AlphaTest-1"
			"IgnoreProjector" = "True"
			"RenderType" = "TransparentCutout"
		}
		LOD 200

		// ShadowCaster Pass
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZWrite On
			Cull Off

			HLSLPROGRAM
			#pragma vertex ShadowPassVertex
			#pragma fragment ShadowPassFragment
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float3 _ToonProximityAmbience;
				float _Cutoff;
				float _OutSizeMin;
				float _OutSizeMax;
				float4 _PhotoDataColor;
				float _Dirt;
			CBUFFER_END

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
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				return output;
			}

			float4 ShadowPassFragment(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float4 col = _MainTex.Sample(sampler_MainTex, input.uv);
				clip(col.a - _Cutoff);
				return 0;
			}
			ENDHLSL
		}

		// ForwardLit Pass
		Pass
		{
			Name "UniversalForward"
			Tags { "LightMode" = "UniversalForward" }
			Cull Off

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
				float4 tangentOS    : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS   : SV_POSITION;
				float2 uv           : TEXCOORD0;
				float3 worldPos     : TEXCOORD1;
				float3 worldNorm    : TEXCOORD2;
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
				float3 _ToonProximityAmbience;
				float _Cutoff;
				float _OutSizeMin;
				float _OutSizeMax;
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

			float4 frag(Varyings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float4 color = _MainTex.Sample(sampler_MainTex, input.uv);
				clip(color.a - _Cutoff);

				// Calculate fragment outline
				float3 shadedColor = saturate(color.rgb * 0.7 - 0.2);
				float edgeDetected = saturate(
					step(_MainTex.Sample(sampler_MainTex, input.uv + float2(-0.001, 0.001)).a, 0.5) +
					step(_MainTex.Sample(sampler_MainTex, input.uv + float2(0.001, 0.001)).a, 0.5) +
					step(_MainTex.Sample(sampler_MainTex, input.uv + float2(0.0, -0.001)).a, 0.5)
				);
				color.rgb = shadedColor * edgeDetected + color.rgb * (1.0 - edgeDetected);

				// Two-sided normal adjustment
				float3 normalWS = normalize(input.worldNorm);
				normalWS = IS_FRONT_VFACE(isFrontFace, normalWS, -normalWS);

				float3 worldViewDir = normalize(GetCameraPositionWS() - input.worldPos);
				float camRim = dot(worldViewDir, normalWS);

				// Shadows & Lighting
				float4 shadowCoord = TransformWorldToShadowCoord(input.worldPos);
				Light mainLight = GetMainLight(shadowCoord);
				float worldRim = saturate(dot(mainLight.direction, normalWS));
				float atten = (mainLight.distanceAttenuation * mainLight.shadowAttenuation - 0.5) * 2.0;
				float sun = saturate(atten * worldRim);

				// Point / Spot Lights
				float3 ambience = CalculateAdditionalLights(input.worldPos, normalWS);

				// Gradual Dirt
				float3 final = color.rgb;
				final = ApplyGradualDirt(final, _GlobalDirtTex, sampler_GlobalDirtTex, _Dirt, input.uv * 0.25);

				// Ambient & Light application
				float3 envAmbient = SampleSH(normalWS);
				final *= ambience + saturate(lerp(envAmbient, mainLight.color, sun) + _ToonProximityAmbience);
				final = ApplyColorFromLight(final, color.rgb, sun, camRim, worldRim, mainLight.color);

				final = MixFog(final, input.fogFactor);
				return float4(final, 1.0);
			}
			ENDHLSL
		}

		// DepthOnly Pass
		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ZWrite On
			Cull Off
			ColorMask R

			HLSLPROGRAM
			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthOnlyFragment
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

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float3 _ToonProximityAmbience;
				float _Cutoff;
				float _OutSizeMin;
				float _OutSizeMax;
				float4 _PhotoDataColor;
				float _Dirt;
			CBUFFER_END

			Varyings DepthOnlyVertex(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				return output;
			}

			float4 DepthOnlyFragment(Varyings input) : SV_Target
			{
				float4 col = _MainTex.Sample(sampler_MainTex, input.uv);
				clip(col.a - _Cutoff);
				return 0;
			}
			ENDHLSL
		}
	}

	Fallback "Universal Render Pipeline/Lit"
}

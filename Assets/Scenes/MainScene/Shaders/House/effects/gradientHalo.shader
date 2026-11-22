Shader "Effects/GradientHalo"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Size ("Size", Range(0.001, 16.0)) = 0.4
		_Color ("Color", Color) = (1.0, 0.92, 0.61, 1.0)
	}
	SubShader
	{
		Tags
		{
			"Queue" = "Transparent+1"
			"RenderType" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
		}
		LOD 100
		Blend One One
		ZWrite Off
		Cull Off

		Pass
		{
			Name "URPGradientHalo"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			CBUFFER_START(UnityPerMaterial)
				float4 _Color;
				float _Size;
			CBUFFER_END

			Varyings vert (Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				// Center of the object in view space (camera relative)
				float3 worldCenter = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
				float3 viewCenter = TransformWorldToView(worldCenter);

				// Offset the quad facing the camera in view space
				float3 viewPos = viewCenter + float3(input.positionOS.xy * _Size, 0.0);

				output.positionCS = TransformWViewToHClip(viewPos);
				output.uv = input.uv;
				return output;
			}

			half4 frag (Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half2 data = _MainTex.Sample(sampler_MainTex, input.uv).rg;
				return half4(_Color.rgb * data.r + data.g, 1.0);
			}
			ENDHLSL
		}
	}
	SubShader
	{
		Tags
		{
			"Queue" = "Transparent+1"
			"RenderType" = "Transparent"
			"IgnoreProjector" = "True"
		}
		LOD 100
		Blend One One
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			fixed _Size;
			fixed4 _Color;

			v2f vert (appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float4 worldCoord = float4(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23, 1.0);
				float4 viewCenter = mul(UNITY_MATRIX_V, worldCoord);
				float4 viewPos = viewCenter + float4(v.vertex.xy * _Size, 0.0, 0.0);

				o.vertex = mul(UNITY_MATRIX_P, viewPos);
				o.uv = v.uv;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i)
				fixed2 data = tex2D(_MainTex, i.uv).rg;
				return fixed4(_Color.rgb * data.r + data.g, 1.0);
			}
			ENDCG
		}
	}
}

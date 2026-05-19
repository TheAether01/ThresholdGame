Shader "SFS/WallpaperMaker_Lite"
{
	Properties
	{
		[Header(BASE PROPERTIES)]
		[Space(5)]
		_Metallic("Metallic", Range(0 , 1)) = 0
		_Glossiness("Smoothness", Range(0 , 1)) = 0
		_Contrast("Contrast", Range(0 , 2)) = 1
		_BaseColor("Base Color", Color) = (1,1,1,1)

		[Space(30)]
		[Header(LAYER 1)]
		[Space(5)]
		_Patern1Color("Color", Color) = (0,0,0,0)
		_TilingPatern1("Tiling", Range(0 , 5)) = 1
		[NoScaleOffset]_Patern1Sprite("Pattern", 2D) = "black" {}

		[Header(LAYER 2)]
		[Space(5)]
		_Patern2Color("Color", Color) = (0,0,0,0)
		_TilingPatern2("Tiling", Range(0 , 5)) = 1
		[NoScaleOffset]_Patern2Sprite("Pattern", 2D) = "black" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" }
		Cull Back
		CGINCLUDE
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float3 worldNormal;
			float3 worldPos;
		};

		uniform float _Contrast;
		uniform float4 _BaseColor;
		uniform sampler2D _Patern1Sprite;
		uniform float _TilingPatern1;
		uniform sampler2D _Patern2Sprite;
		uniform float _TilingPatern2;
		uniform float4 _Patern2Color;
		uniform float4 _Patern1Color;
		uniform float _Metallic;
		uniform float _Smoothness;


		float4 CalculateContrast( float contrastValue, float4 colorTarget )
		{
			float t = 0.5 * ( 1.0 - contrastValue );
			return mul( float4x4( contrastValue,0,0,t, 0,contrastValue,0,t, 0,0,contrastValue,t, 0,0,0,1 ), colorTarget );
		}

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float4 color123 = IsGammaSpace() ? float4(0,0,0,0) : float4(0,0,0,0);
			float3 ase_worldNormal = i.worldNormal;
			float temp_output_86_0 = abs( ase_worldNormal.z );
			float3 ase_worldPos = i.worldPos;
			float4 appendResult73 = (float4(ase_worldPos.x , ase_worldPos.y , 0.0 , 0.0));
			float temp_output_83_0 = abs( ase_worldNormal.x );
			float4 appendResult76 = (float4(ase_worldPos.y , ase_worldPos.z , 0.0 , 0.0));
			float2 _PaternAnchor = float2(0.5,0.5);
			float temp_output_56_0 = radians( -90.0 );
			float cos62 = cos( temp_output_56_0 );
			float sin62 = sin( temp_output_56_0 );
			float2 rotator62 = mul( ( appendResult76 * _TilingPatern1 ).xy - _PaternAnchor , float2x2( cos62 , -sin62 , sin62 , cos62 )) + _PaternAnchor;
			float4 appendResult82 = (float4(ase_worldPos.x , ase_worldPos.z , 0.0 , 0.0));
			float4 tex2DNode16 = tex2D( _Patern1Sprite, ( temp_output_86_0 > 0.5 ? ( appendResult73 * _TilingPatern1 ) : ( temp_output_83_0 > 0.5 ? float4( rotator62, 0.0 , 0.0 ) : ( appendResult82 * _TilingPatern1 ) ) ).xy );
			float cos84 = cos( temp_output_56_0 );
			float sin84 = sin( temp_output_56_0 );
			float2 rotator84 = mul( ( appendResult76 * _TilingPatern2 ).xy - _PaternAnchor , float2x2( cos84 , -sin84 , sin84 , cos84 )) + _PaternAnchor;
			float4 tex2DNode89 = tex2D( _Patern2Sprite, ( temp_output_86_0 > 0.5 ? ( appendResult73 * _TilingPatern2 ) : ( temp_output_83_0 > 0.5 ? float4( rotator84, 0.0 , 0.0 ) : ( appendResult82 * _TilingPatern2 ) ) ).xy );
			float4 lerpResult138 = lerp( _BaseColor , color123 , saturate( ( tex2DNode16.a + tex2DNode89.a ) ));
			float4 lerpResult193 = lerp( lerpResult138 , ( _Patern2Color * tex2DNode89 ) , tex2DNode89.a);
			float4 lerpResult194 = lerp( lerpResult193 , ( _Patern1Color * tex2DNode16 ) , tex2DNode16.a);
			o.Albedo = CalculateContrast(_Contrast,lerpResult194).rgb;
			o.Metallic = _Metallic;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard keepalpha fullforwardshadows 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float3 worldPos : TEXCOORD1;
				float3 worldNormal : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				o.worldNormal = worldNormal;
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				surfIN.worldNormal = IN.worldNormal;
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
}
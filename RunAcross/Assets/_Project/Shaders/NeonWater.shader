// Dark mirror-like water for the neon look. Reflections come from PlanarReflection.cs,
// which renders a mirrored camera into _PlanarReflectionTex (HDR, so emissive reflections still bloom).
Shader "RunAcross/NeonWater"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.01, 0.012, 0.03, 1)
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 0.9
        _MinReflect ("Reflectivity Facing Camera", Range(0, 1)) = 0.35
        _FresnelPower ("Fresnel Power", Range(0.1, 8)) = 4
        [Normal] _BumpMap ("Ripple Normal", 2D) = "bump" {}
        _BumpTiling ("Ripple Tiling (world)", Float) = 0.12
        _Distortion ("Ripple Distortion", Range(0, 0.1)) = 0.012
        _ScrollSpeed ("Ripple Speed", Float) = 0.015
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _ReflectionStrength, _MinReflect, _FresnelPower, _BumpTiling, _Distortion, _ScrollSpeed;
            float4 _BumpMap_ST;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            TEXTURE2D(_PlanarReflectionTex); SAMPLER(sampler_PlanarReflectionTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                float2 worldUV = i.positionWS.xz * _BumpTiling;
                float t = _Time.y * _ScrollSpeed;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, worldUV + float2(t, t * 0.7)));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, worldUV * 1.7 - float2(t * 0.8, t)));
                half2 offset = (n1.xy + n2.xy) * _Distortion;

                half3 refl = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, screenUV + offset).rgb;
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel = _MinReflect + (1 - _MinReflect) * pow(1 - saturate(dot(normalize(i.normalWS), viewDir)), _FresnelPower);

                half3 color = _BaseColor.rgb + refl * _ReflectionStrength * fresnel;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}

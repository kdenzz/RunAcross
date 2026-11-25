// Three-colour gradient skybox. Fog does not reach the skybox, so keep _HorizonColor equal to the fog colour.
Shader "RunAcross/GradientSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.03, 0.04, 0.14, 1)
        _HorizonColor ("Horizon (match fog)", Color) = (0.17, 0.18, 0.42, 1)
        _BottomColor ("Below Horizon", Color) = (0.05, 0.05, 0.14, 1)
        _Exponent ("Falloff", Range(0.1, 4)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _BottomColor;
                half _Exponent;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = i.positionOS.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float y = normalize(i.dir).y;
                half3 c = y >= 0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(y), _Exponent))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-y * 4));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}

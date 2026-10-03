Shader "Firefly/MoonlitWing"
{
    Properties
    {
        _BaseColor("Moonlit membrane", Color) = (0.65,0.73,0.82,0.58)
        _RimColor("Cool reflection", Color) = (0.18,0.3,0.44,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.65
        _Metallic("Metallic", Range(0,1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _Smoothness;
                half _Metallic;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                half fog:TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                normal *= dot(normal, view) < 0 ? -1 : 1;
                // Delicate wings reflect the moon and environment. Nearby abdomen lights
                // do not turn the entire membrane into a second green luminous organ.
                Light moon = GetMainLight();
                half transmittedMoon = abs(dot(normal, moon.direction));
                half3 illumination = max(SampleSH(normal), 0) + moon.color * transmittedMoon;
                half3 halfway = SafeNormalize(moon.direction + view);
                half specular = pow(saturate(dot(normal, halfway)), lerp(16, 96, _Smoothness));
                half rim = pow(1 - saturate(dot(normal, view)), 3) * transmittedMoon;
                half3 color = _BaseColor.rgb * illumination;
                color += moon.color * specular * 0.18;
                color += moon.color * _RimColor.rgb * rim * 0.16;
                return half4(MixFog(color, input.fog), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}

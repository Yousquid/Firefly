Shader "Firefly/Night Sky"
{
    Properties
    {
        _Horizon("Horizon",Color)=(0.025,0.065,0.075,1)
        _Zenith("Zenith",Color)=(0.006,0.017,0.028,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Horizon, _Zenith;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.direction=i.positionOS.xyz; return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float3 d=normalize(i.direction);
                float t=pow(saturate(d.y),0.65);
                float3 color=lerp(_Horizon.rgb,_Zenith.rgb,t);
                float moonDot=dot(d,normalize(float3(-0.5,0.75,0.5)));
                color+=float3(0.28,0.34,0.37)*pow(saturate(moonDot),180);
                color+=float3(0.6,0.7,0.68)*smoothstep(0.9995,0.99985,moonDot);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}

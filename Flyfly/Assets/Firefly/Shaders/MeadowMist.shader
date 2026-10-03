Shader "Firefly/Meadow Mist"
{
    Properties
    {
        [MainColor] _BaseColor("Mist scattering tint", Color) = (0.14, 0.22, 0.24, 0.055)
        _NoiseTex("Mist noise", 2D) = "white" {}
        _Drift("Drift", Float) = 0.012
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MistVertex
            #pragma fragment MistFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _NoiseTex_ST;
                float _Drift;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half fog : TEXCOORD2; };
            Varyings MistVertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 MistFragment(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2 - 1;
                float mask = pow(saturate(1 - dot(centered, centered)), 2);
                float2 noiseUV = input.positionWS.xz * 0.095 + float2(_Time.y * _Drift, _Time.y * _Drift * 0.31);
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
                noise *= SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV * 2.11 - float2(_Time.y * _Drift * 0.4, 0)).r;
                half alpha = _BaseColor.a * mask * smoothstep(0.07, 0.47, noise);
                clip(alpha - 0.0005h);

                // These cards represent very thin scattering wisps, not luminous
                // surfaces. With no incident light, they have no fixed bright color.
                half3 incident = SampleSH(half3(0, 1, 0));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                incident += mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation * 0.16h;
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_FORWARD_PLUS
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                            incident += light.color * light.distanceAttenuation * light.shadowAttenuation * 0.18h;
                        }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                        incident += light.color * light.distanceAttenuation * light.shadowAttenuation * 0.18h;
                    LIGHT_LOOP_END
                #endif
                return half4(MixFog(_BaseColor.rgb * incident, input.fog), alpha);
            }
            ENDHLSL
        }
    }
}

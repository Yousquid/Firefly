Shader "Firefly/Meadow Foliage"
{
    Properties
    {
        [MainColor] _BaseColor("Leaf tint", Color) = (1, 1, 1, 1)
        _WindStrength("Wind strength", Range(0, 0.2)) = 0.035
        _WindSpeed("Wind speed", Range(0, 4)) = 0.8
        _Translucency("Leaf translucency", Range(0, 1)) = 0.24
        _Smoothness("Dew sheen", Range(0, 1)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _WindStrength;
            float _WindSpeed;
            half _Translucency;
            half _Smoothness;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 LeafPositionWS(Attributes input)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float t = _Time.y * _WindSpeed;
            float gust = sin(positionWS.x * 0.61 + positionWS.z * 0.37 + t);
            gust += sin(positionWS.x * 1.7 - positionWS.z * 0.4 + t * 1.31) * 0.27;
            positionWS.xz += float2(gust, gust * 0.38) * _WindStrength * input.color.a;
            return positionWS;
        }

        half3 LeafLighting(Light light, half3 normalWS, half3 viewDirectionWS, half3 albedo)
        {
            half nDotL = dot(normalWS, light.direction);
            half diffuse = saturate(nDotL) * 0.84h + saturate(-nDotL) * _Translucency;
            half3 halfDirection = SafeNormalize(light.direction + viewDirectionWS);
            half sheen = pow(saturate(dot(normalWS, halfDirection)), 48.0h) * _Smoothness * 0.2h;
            return (albedo * diffuse + sheen) * light.color * light.distanceAttenuation * light.shadowAttenuation;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LeafVertex
            #pragma fragment LeafFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
                half3 vertexLight : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LeafVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = LeafPositionWS(input);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.vertexLight = VertexLighting(output.positionWS, output.normalWS);
                return output;
            }

            half4 LeafFragment(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo = input.color.rgb * _BaseColor.rgb;
                half3 color = albedo * SampleSH(normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                color += LeafLighting(mainLight, normalWS, viewDirectionWS, albedo);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_FORWARD_PLUS
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                            color += LeafLighting(light, normalWS, viewDirectionWS, albedo);
                        }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                        color += LeafLighting(light, normalWS, viewDirectionWS, albedo);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    color += input.vertexLight * albedo;
                #endif
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LeafShadowVertex
            #pragma fragment LeafShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            float4 LeafShadowVertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = LeafPositionWS(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 LeafShadowFragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LeafDepthVertex
            #pragma fragment LeafDepthFragment
            #pragma multi_compile_instancing
            float4 LeafDepthVertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(LeafPositionWS(input));
            }
            half4 LeafDepthFragment() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}

Shader "SPH/ParticleShader"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.5, 1.0, 1.0)
        _Smoothness ("Smoothness", Range(0,1)) = 0.8
        _Metallic ("Metallic", Range(0,1)) = 0.1
        _ParticleRadius ("Particle Radius", Float) = 0.05
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Particle
            {
                float3 position;
                float3 velocity;
                float3 force;
                float density;
                float pressure;
                int isActive;
            };

            // StructuredBuffer containing the SPH particle data
            StructuredBuffer<Particle> particles;

            // SRP Batcher compatible constant buffer
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _ParticleRadius;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID; // Raw SV_InstanceID semantic
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float density : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                Particle p = particles[input.instanceID];
                
                // If particle is inactive, collapse its geometry
                if (p.isActive == 0)
                {
                    output.positionCS = float4(0, 0, 0, 0);
                    output.positionWS = float3(0, 0, 0);
                    output.normalWS = float3(0, 1, 0);
                    output.density = 0.0;
                    return output;
                }
                
                // Calculate scale and position in world space
                float3 scaledPos = input.positionOS.xyz * _ParticleRadius;
                float3 worldPos = scaledPos + p.position;
                
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.density = p.density;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Discard rendering if the particle is inactive
                if (input.density <= 0.0)
                    discard;
                
                // Directional lighting
                Light mainLight = GetMainLight();
                float3 normal = normalize(input.normalWS);
                float3 lightDir = normalize(mainLight.direction);
                
                float NdotL = saturate(dot(normal, lightDir));
                float3 diffuse = mainLight.color * NdotL;
                
                // Ambient lighting
                float3 ambient = SampleSH(normal);
                
                // Shift color slightly based on SPH density
                float densityFactor = saturate(input.density / 1500.0);
                float3 colorVariation = lerp(_Color.rgb, _Color.rgb * 0.5, densityFactor);
                
                float3 finalColor = colorVariation * (diffuse + ambient * 0.4);
                
                return half4(finalColor, _Color.a);
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Particle
            {
                float3 position;
                float3 velocity;
                float3 force;
                float density;
                float pressure;
                int isActive;
            };

            StructuredBuffer<Particle> particles;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _ParticleRadius;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                Particle p = particles[input.instanceID];
                
                if (p.isActive == 0)
                {
                    output.positionCS = float4(0, 0, 0, 0);
                    return output;
                }
                
                float3 scaledPos = input.positionOS.xyz * _ParticleRadius;
                float3 worldPos = scaledPos + p.position;
                output.positionCS = TransformWorldToHClip(worldPos);
                
                return output;
            }

            half4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
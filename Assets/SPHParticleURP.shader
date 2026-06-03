Shader "Custom/SPHParticleURP"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 0.4, 0.9, 1)
        _SpeedColor ("Fast Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Glossiness / Shininess", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

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

            struct Particle {
                float3 position;
                float3 velocity;
                float density;
                float pressure;
                float lifetime;
                uint active;
            };

            StructuredBuffer<Particle> _ParticleBuffer;
            float _ParticleRadius;
            float4 _BaseColor;
            float4 _SpeedColor;
            float _Glossiness;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                float4 color        : COLOR;
            };

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;
                Particle p = _ParticleBuffer[instanceID];
                
                float3 worldPos;
                float3 normalWS;
                if (p.active == 0)
                {
                    worldPos = float3(0.0, -99999.0, 0.0);
                    normalWS = float3(0, 1, 0);
                }
                else
                {
                    worldPos = input.positionOS.xyz * _ParticleRadius + p.position;
                    normalWS = TransformObjectToWorldNormal(input.normalOS);
                }

                output.positionWS = worldPos;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(worldPos);
                
                float speed = length(p.velocity);
                output.color = lerp(_BaseColor, _SpeedColor, saturate(speed * 0.15));
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                
                float3 normal = normalize(input.normalWS);
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                
                float NdotL = saturate(dot(normal, lightDir));
                float3 diffuse = NdotL * mainLight.color;

                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, halfDir));
                float specularIntensity = pow(NdotH, _Glossiness * 128.0) * _Glossiness;
                float3 specular = specularIntensity * mainLight.color;

                float3 ambient = float3(0.15, 0.15, 0.18) * input.color.rgb;

                float3 finalColor = ambient + (diffuse * input.color.rgb) + specular;
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
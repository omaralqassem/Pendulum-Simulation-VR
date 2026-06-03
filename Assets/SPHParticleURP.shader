Shader "Custom/SPHParticleURP_Clean"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 1, 1)
        _SpeedColor ("Speed Color", Color) = (1, 1, 1, 1)
        _ParticleRadius ("Particle Radius", Float) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            struct Particle
            {
                float3 position;
                float3 velocity;
                float density;
                float pressure;
                float lifetime;
                uint active;
            };

            StructuredBuffer<Particle> _ParticleBuffer;

            float4 _BaseColor;
            float4 _SpeedColor;
            float _ParticleRadius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);

                Particle p = _ParticleBuffer[instanceID];

                if (p.active == 0)
                {
                    output.positionCS = float4(0, 0, 0, 0);
                    output.normalWS = float3(0, 1, 0);
                    output.color = float4(0, 0, 0, 0);
                    return output;
                }

                float3 worldPos = p.position + input.positionOS.xyz * _ParticleRadius;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                float speed = length(p.velocity);

                output.color = lerp(_BaseColor, _SpeedColor, saturate(speed * 0.15));

                return output;
            }

 
            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);

                float3 lightDir = normalize(float3(0.3, 1, 0.2));

                float NdotL = saturate(dot(normal, lightDir));

                float3 diffuse = input.color.rgb * NdotL;

                float3 ambient = input.color.rgb * 0.25;

                float3 finalColor = diffuse + ambient;

                return float4(finalColor, 1);
            }

            ENDHLSL
        }
    }
}
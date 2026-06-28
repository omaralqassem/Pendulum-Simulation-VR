Shader "Custom/FluidParticleSphere"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "DepthPass"
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            struct Particle {
                float3 position;
                float3 velocity;
                float3 force;
                float density;
                float pressure;
                float lifetime;
                float dryState;
            };

            StructuredBuffer<Particle> _Particles;
            float _ParticleRadius;
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                Particle p = _Particles[instanceID];

                if (p.lifetime <= 0.0f || p.position.x > 90000.0f)
                {
                    o.pos = float4(99999.0f, 99999.0f, 99999.0f, 1.0f);
                    o.viewPos = float3(0,0,0);
                    o.uv = float2(0,0);
                    return o;
                }

                float2 offsets[6] = {
                    float2(-1.0f, -1.0f),
                    float2( 1.0f, -1.0f),
                    float2(-1.0f,  1.0f),
                    float2(-1.0f,  1.0f),
                    float2( 1.0f, -1.0f),
                    float2( 1.0f,  1.0f)
                };

                float2 quadUV = offsets[vertexID % 6];
                o.uv = quadUV;

                float4 wPos = float4(p.position, 1.0f);
                float3 viewPos = mul(_ViewMatrix, wPos).xyz;

                viewPos.xy += quadUV * _ParticleRadius;
                o.viewPos = viewPos;
                o.pos = mul(_ProjMatrix, float4(viewPos, 1.0f));

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0f)
                    discard;

                // Calculate positive depth (closer particles have smaller positive values)
                float zOffset = sqrt(1.0f - r2) * _ParticleRadius;
                float positiveViewDepth = -i.viewPos.z - zOffset;

                return float4(positiveViewDepth, 0, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ThicknessPass"
            ZWrite Off
            ZTest Always
            Blend One One 
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            struct Particle {
                float3 position;
                float3 velocity;
                float3 force;
                float density;
                float pressure;
                float lifetime;
                float dryState;
            };

            StructuredBuffer<Particle> _Particles;
            float _ParticleRadius;
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                Particle p = _Particles[instanceID];

                if (p.lifetime <= 0.0f || p.position.x > 90000.0f)
                {
                    o.pos = float4(99999.0f, 99999.0f, 99999.0f, 1.0f);
                    o.uv = float2(0,0);
                    return o;
                }

                float2 offsets[6] = {
                    float2(-1.0f, -1.0f),
                    float2( 1.0f, -1.0f),
                    float2(-1.0f,  1.0f),
                    float2(-1.0f,  1.0f),
                    float2( 1.0f, -1.0f),
                    float2( 1.0f,  1.0f)
                };

                float2 quadUV = offsets[vertexID % 6];
                o.uv = quadUV;

                float4 wPos = float4(p.position, 1.0f);
                float3 viewPos = mul(_ViewMatrix, wPos).xyz;
                viewPos.xy += quadUV * _ParticleRadius * 1.5f; 
                o.pos = mul(_ProjMatrix, float4(viewPos, 1.0f));

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0f)
                    discard;

                float thickness = sqrt(1.0f - r2);
                return float4(thickness * 0.2f, 0.0f, 0.0f, 0.0f);
            }
            ENDHLSL
        }
    }
}
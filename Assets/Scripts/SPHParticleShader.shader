Shader "Custom/SPHParticleRender"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scale ("Scale", Float) = 0.1
        _Color ("Base Color", Color) = (0.2, 0.5, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "UnityCG.cginc"

            struct Particle {
                float3 position;
                float3 velocity;
                float3 force;
                float density;
                float pressure;
                float lifetime;
            };

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<Particle> Particles;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Scale;
            float4 _Color;

            void setup()
            {
                // Required by procedural pipeline layout definition
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 worldPos = v.vertex.xyz;
                float4 particleColor = _Color;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                Particle p = Particles[unity_InstanceID];
                
                float targetScale = p.lifetime > 0.0f ? _Scale : 0.0f;
                worldPos = (v.vertex.xyz * targetScale) + p.position;

                float speed = length(p.velocity);
                particleColor = lerp(_Color, float4(1.0, 0.4, 0.1, 1.0), saturate(speed * 0.08));
                #endif

                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = particleColor;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                return col;
            }
            ENDCG
        }
    }
}
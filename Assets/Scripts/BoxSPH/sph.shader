Shader "Custom/SPHParticleRender"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scale ("Scale", Float) = 0.1
        _Color ("Dry Base Color", Color) = (0.2, 0.5, 1.0, 1.0)
        _WetColor ("Wet Base Color", Color) = (0.05, 0.25, 0.7, 1.0)
        _SpecularWetness ("Wet Specular Strength", Range(0.0, 2.0)) = 1.0
        _Glossiness ("Glossiness (Shininess)", Range(1.0, 128.0)) = 45.0
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
                float dryState; 
            };

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<Particle> Particles;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL; 
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float4 color : COLOR;
                float dryFactor : TEXCOORD3; 
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Scale;
            float4 _Color;
            float4 _WetColor;
            float _SpecularWetness;
            float _Glossiness;

            float4x4 _LocalToWorld;

            void setup()
            {
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 localPos = v.vertex.xyz;
                float4 particleColor = _WetColor;
                float dryVal = 0.0f;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                Particle p = Particles[unity_InstanceID];
                
                float targetScale = p.lifetime > 0.0f ? _Scale : 0.0f;
                
                localPos = (v.vertex.xyz * targetScale) + p.position;

                dryVal = saturate(p.dryState);
                particleColor = lerp(_WetColor, _Color, dryVal);

                float speed = length(p.velocity);
                particleColor = lerp(particleColor, float4(1.0, 0.4, 0.1, 1.0), saturate(speed * 0.08) * (1.0f - dryVal));
                #endif

                float3 worldPos = mul(_LocalToWorld, float4(localPos, 1.0)).xyz;

                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                float3 worldNormal = mul((float3x3)_LocalToWorld, v.normal);
                o.normal = normalize(worldNormal);
                
                o.color = particleColor;
                o.dryFactor = dryVal;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, i.uv) * i.color;

                float3 N = normalize(i.normal);
                float3 L = normalize(float3(0.5, 1.0, 0.3));
                float diffuseShading = saturate(dot(N, L)) * 0.6 + 0.4; 

                float3 V = normalize(float3(0.0, 0.0, 1.0)); 
                float3 H = normalize(L + V);
                
                float specReflection = pow(saturate(dot(N, H)), _Glossiness);
                float specScale = _SpecularWetness * (1.0f - i.dryFactor);
                float3 specularHighlight = specReflection * specScale * float3(1.0, 1.0, 1.0);

                fixed4 finalColor = baseColor;
                finalColor.rgb = (finalColor.rgb * diffuseShading) + specularHighlight;

                return finalColor;
            }
            ENDCG
        }
    }
}
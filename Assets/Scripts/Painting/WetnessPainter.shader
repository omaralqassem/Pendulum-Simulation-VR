Shader "Hidden/WetnessPainter"
{
    Properties {
        _PainterPosition ("Painter Position", Vector) = (0,0,0,0)
        _Radius ("Radius", Float) = 0.0008
        _Hardness ("Hardness", Float) = 1
        _Strength ("Strength", Float) = 1
    }
    SubShader {
        Cull Off ZWrite Off ZTest Off
        BlendOp Max
        Blend One One

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float3 _PainterPosition;
            float _Radius;
            float _Hardness;
            float _Strength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 worldPos : TEXCOORD1; };

            float mask(float3 position, float3 center, float radius, float hardness){
                float m = distance(center, position);
                return 1 - smoothstep(radius * hardness, radius, m);    
            }

            v2f vert (appdata v) {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.uv = v.uv;
                float4 uv = float4(0, 0, 0, 1);
                uv.xy = float2(1, _ProjectionParams.x) * (v.uv.xy * float2( 2, 2) - float2(1, 1));
                o.vertex = uv; 
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {   
                float f = mask(i.worldPos, _PainterPosition, _Radius, _Hardness);
                // Clamp to 1.0 so it doesn't over-saturate, and only output Red
                float wetness = min(1.0, f * _Strength);
                return float4(wetness, 0, 0, 0); 
            }
            ENDCG
        }
    }
}
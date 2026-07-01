Shader "Hidden/LineConnectShader"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always

        // PASS 1: DILATE (Expands dots to connect them)
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target {
                fixed4 col = tex2D(_MainTex, i.uv);
                float maxAlpha = col.a;
                // Check 8 neighboring pixels. If any have paint, this pixel gets paint.
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(-1, 0) * _MainTex_TexelSize.xy).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(1, 0) * _MainTex_TexelSize.xy).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(0, -1) * _MainTex_TexelSize.xy).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(0, 1) * _MainTex_TexelSize.xy).a);
                col.a = maxAlpha;
                return col;
            }
            ENDCG
        }

        // PASS 2: ERODE (Shrinks the connected line back to thin)
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target {
                fixed4 col = tex2D(_MainTex, i.uv);
                float minAlpha = col.a;
                // Check 8 neighboring pixels. If they are empty, remove paint from this pixel.
                minAlpha = min(minAlpha, tex2D(_MainTex, i.uv + float2(-1, 0) * _MainTex_TexelSize.xy).a);
                minAlpha = min(minAlpha, tex2D(_MainTex, i.uv + float2(1, 0) * _MainTex_TexelSize.xy).a);
                minAlpha = min(minAlpha, tex2D(_MainTex, i.uv + float2(0, -1) * _MainTex_TexelSize.xy).a);
                minAlpha = min(minAlpha, tex2D(_MainTex, i.uv + float2(0, 1) * _MainTex_TexelSize.xy).a);
                col.a = minAlpha;
                return col;
            }
            ENDCG
        }
    }
}
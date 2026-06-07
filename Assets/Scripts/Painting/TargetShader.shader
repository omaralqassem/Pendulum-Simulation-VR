Shader "Custom/TargetShader" {
    Properties {
        _MainTex ("Base Texture", 2D) = "white" {}
        _MaskTexture ("Painted Mask", 2D) = "black" {} 
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _MaskTexture;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 mask = tex2D(_MaskTexture, i.uv);
                // Blend the base color with the painted mask
                return lerp(col, mask, mask.a); 
            }
            ENDCG
        }
    }
}
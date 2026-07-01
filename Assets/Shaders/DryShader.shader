Shader "Hidden/DryShader"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Blend Off // Forces exact math, prevents instant drying/alpha bugs

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float DryingRate;

            struct appdata { 
                float4 vertex : POSITION; 
                float2 uv : TEXCOORD0; 
            };
            
            struct v2f { 
                float2 uv : TEXCOORD0; 
                float4 vertex : SV_POSITION; 
            };

            v2f vert (appdata v) { 
                v2f o; 
                o.vertex = UnityObjectToClipPos(v.vertex); 
                o.uv = v.uv; 
                return o; 
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Read current wetness (0 to 1)
                float wetness = tex2D(_MainTex, i.uv).r;
                
                // Decrease wetness over time, clamped at 0
                wetness = max(0.0, wetness - DryingRate);
                
                // Output back to the Red channel
                return float4(wetness, 0, 0, 1);
            }
            ENDCG
        }
    }
}
Shader "Custom/CanvasDisplay"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _MaskTexture ("Painted Mask", 2D) = "black" {}
        
        _WetnessMap ("Wetness Map", 2D) = "black" {}
        _WetColorStrength ("Wet Strength", Range(0,2)) = 1.0
        _DryColorStrength ("Dry Strength", Range(0,2)) = 0.1
        
        [Toggle] _DebugWetness ("Debug Wetness Map", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _MaskTexture;
            sampler2D _WetnessMap;
            float _WetColorStrength;
            float _DryColorStrength;
            float _DebugWetness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // DEBUG MODE
                if (_DebugWetness > 0.5)
                {
                    return float4(tex2D(_WetnessMap, i.uv).rrr, 1.0);
                }

                // 1. Read base and painted mask
                fixed4 baseCol = tex2D(_MainTex, i.uv);
                fixed4 paintCol = tex2D(_MaskTexture, i.uv);

                // 2. Read wetness
                float wetness = tex2D(_WetnessMap, i.uv).r;

                // 3. Calculate strength
                float strength = lerp(_DryColorStrength, _WetColorStrength, wetness);

                // 4. Apply strength ONLY to the Alpha channel.
                // This preserves the perfectly smooth TNTC edges!
                paintCol.a *= strength;

                // 5. Your exact original blending math
                return lerp(baseCol, paintCol, paintCol.a);
            }
            ENDCG
        }
    }
}
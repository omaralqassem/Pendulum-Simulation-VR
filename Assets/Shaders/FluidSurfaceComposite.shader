Shader "Custom/FluidSurfaceComposite"
{
    Properties
    {
        _MainTex ("Source Background", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _FluidDepthTex;
            sampler2D _RawDepthTex;
            sampler2D _ThicknessTex;

            float4 _MainTex_TexelSize;
            float4 _FluidDepthTex_TexelSize;
            float4x4 _ProjMatrix;

            // Shading variables
            float4 _PaintBaseColor;
            float4 _PaintDeepColor;
            float _PaintDensity;
            float _Roughness;
            float _Metallic;
            float _RefractiveIndex;
            float3 _LightDir; 
            int _DebugMode;

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 GetViewPos(float2 uv, float depth)
            {
                float4 clipPos = float4(uv * 2.0f - 1.0f, 1.0f, 1.0f);
                float x_mult = 1.0f / _ProjMatrix._m00;
                float y_mult = 1.0f / _ProjMatrix._m11;
                
                // Camera views down negative Z axis
                float3 viewRay = float3(clipPos.x * x_mult, clipPos.y * y_mult, -1.0f);
                return viewRay * depth;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Align texture coordinates between our custom targets and the screen buffer
                float2 uv = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                {
                    uv.y = 1.0f - uv.y;
                }
                #endif

                float depth = tex2D(_FluidDepthTex, uv).r;

                // Debug Output Pass
                if (_DebugMode == 1) // Raw Depth Map
                {
                    float rawD = tex2D(_RawDepthTex, uv).r;
                    return (rawD > 9999.0f) ? float4(0,0,0,1) : float4(frac(rawD).xxx, 1.0f);
                }
                if (_DebugMode == 2) // Smoothed Depth Map
                {
                    return (depth > 9999.0f) ? float4(0,0,0,1) : float4(frac(depth).xxx, 1.0f);
                }
                if (_DebugMode == 3) // Thickness Map
                {
                    float thick = tex2D(_ThicknessTex, uv).r;
                    return float4(thick.xxx, 1.0f);
                }

                // If no fluid is present, return the background
                if (depth > 9999.0f)
                {
                    return tex2D(_MainTex, i.uv);
                }

                // Reconstruct Position and Adjacent Pixels to derive normals
                float3 posEye = GetViewPos(uv, depth);
                float2 texelSize = _FluidDepthTex_TexelSize.xy;

                float depthR = tex2D(_FluidDepthTex, uv + float2(texelSize.x, 0.0f)).r;
                float depthL = tex2D(_FluidDepthTex, uv - float2(texelSize.x, 0.0f)).r;
                float depthD = tex2D(_FluidDepthTex, uv + float2(0.0f, texelSize.y)).r;
                float depthU = tex2D(_FluidDepthTex, uv - float2(0.0f, texelSize.y)).r;

                float3 posR = GetViewPos(uv + float2(texelSize.x, 0.0f), depthR);
                float3 posL = GetViewPos(uv - float2(texelSize.x, 0.0f), depthL);
                float3 posD = GetViewPos(uv + float2(0.0f, texelSize.y), depthD);
                float3 posU = GetViewPos(uv - float2(0.0f, texelSize.y), depthU);

                float3 dR = (depthR < 9999.0f) ? (posR - posEye) : (posEye - posL);
                float3 dL = (depthL < 9999.0f) ? (posEye - posL) : (posR - posEye);
                float3 dD = (depthD < 9999.0f) ? (posD - posEye) : (posEye - posU);
                float3 dU = (depthU < 9999.0f) ? (posEye - posU) : (posD - posEye);

                float3 tangentX = (abs(dR.z) < abs(dL.z)) ? dR : dL;
                float3 tangentY = (abs(dD.z) < abs(dU.z)) ? dD : dU;

                float3 normal = normalize(cross(tangentX, tangentY));

                if (_DebugMode == 4) // Reconstructed Normals
                {
                    return float4(normal * 0.5f + 0.5f, 1.0f);
                }

                // --- Final Shading Pass (Thick Wet Paint) ---
                float3 viewDir = normalize(-posEye);
                float3 halfDir = normalize(_LightDir + viewDir);

                // Specular 
                float specPower = lerp(512.0f, 32.0f, _Roughness);
                float specular = pow(max(dot(normal, halfDir), 0.0f), specPower) * (1.0f - _Roughness);

                // Wrap lighting (
                float diffuse = max(dot(normal, _LightDir), 0.0f) * 0.7f + 0.3f;

                // Color Absorption 
                float thickness = tex2D(_ThicknessTex, uv).r;
                float transmission = exp(-_PaintDensity * thickness);
                float4 paintLitColor = lerp(_PaintDeepColor, _PaintBaseColor, transmission) * diffuse;

                // Reflection
                float F0 = pow((1.0f - _RefractiveIndex) / (1.0f + _RefractiveIndex), 2.0f);
                float fresnel = F0 + (1.0f - F0) * pow(1.0f - max(dot(normal, viewDir), 0.0f), 5.0f);

                float2 refractOffset = normal.xy * thickness * 0.05f;
                // Background sample remains mapped to i.uv
                float4 backgroundSample = tex2D(_MainTex, i.uv - refractOffset);

                float4 finalColor = lerp(paintLitColor, backgroundSample, transmission * (1.0f - _Metallic));
                finalColor.rgb += specular * lerp(float3(1, 1, 1), _PaintBaseColor.rgb, _Metallic);
                finalColor.rgb = lerp(finalColor.rgb, float3(0.95f, 0.95f, 1.0f) * diffuse, fresnel * (1.0f - _Roughness));

                return finalColor;
            }
            ENDHLSL
        }
    }
}
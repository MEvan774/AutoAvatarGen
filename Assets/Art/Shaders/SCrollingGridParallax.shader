Shader "Custom/ScrollingGridParallax"
{
    Properties
    {
        // Layer 1 - Far (small, fast, faint)
        _GridColor1    ("Layer 1 Color",      Color)       = (0.3, 0.4, 0.8, 1.0)
        _GridScale1    ("Layer 1 Scale",      Float)       = 20.0
        _ScrollSpeed1  ("Layer 1 Speed",      Float)       = 0.4
        _LineWidth1    ("Layer 1 Line Width", Range(0.005, 0.1)) = 0.04
        _Opacity1      ("Layer 1 Opacity",    Range(0, 1)) = 0.06

        // Layer 2 - Mid
        _GridColor2    ("Layer 2 Color",      Color)       = (0.4, 0.5, 0.9, 1.0)
        _GridScale2    ("Layer 2 Scale",      Float)       = 10.0
        _ScrollSpeed2  ("Layer 2 Speed",      Float)       = 0.2
        _LineWidth2    ("Layer 2 Line Width", Range(0.005, 0.1)) = 0.03
        _Opacity2      ("Layer 2 Opacity",    Range(0, 1)) = 0.09

        // Layer 3 - Near (large, slow, bright)
        _GridColor3    ("Layer 3 Color",      Color)       = (0.5, 0.6, 1.0, 1.0)
        _GridScale3    ("Layer 3 Scale",      Float)       = 5.0
        _ScrollSpeed3  ("Layer 3 Speed",      Float)       = 0.1
        _LineWidth3    ("Layer 3 Line Width", Range(0.005, 0.1)) = 0.025
        _Opacity3      ("Layer 3 Opacity",    Range(0, 1)) = 0.14

        // Shared
        _FadeStart     ("Fade Start",         Float)       = 0.0
        _FadeEnd       ("Fade End",           Float)       = 0.7

        // Perspective squeeze
        _PerspectiveStrength ("Perspective Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue"      = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            // Layer 1
            fixed4 _GridColor1;
            float  _GridScale1, _ScrollSpeed1, _LineWidth1, _Opacity1;

            // Layer 2
            fixed4 _GridColor2;
            float  _GridScale2, _ScrollSpeed2, _LineWidth2, _Opacity2;

            // Layer 3
            fixed4 _GridColor3;
            float  _GridScale3, _ScrollSpeed3, _LineWidth3, _Opacity3;

            // Shared
            float _FadeStart, _FadeEnd, _PerspectiveStrength;

            // ------------------------------------------------
            // Draws one grid layer, returns alpha on that layer
            // ------------------------------------------------
            float DrawGrid(float2 uv, float scale, float lineWidth)
            {
                float2 gridUV   = frac(uv * scale);
                float2 fw       = fwidth(uv * scale);
                float2 gridLine = smoothstep(lineWidth - fw, lineWidth + fw, gridUV)
                                * smoothstep(lineWidth - fw, lineWidth + fw, 1.0 - gridUV);
                return 1.0 - min(gridLine.x, gridLine.y);
            }

            // ------------------------------------------------
            // Perspective warp: squeezes X toward center at
            // the top (far), spreads X at the bottom (near)
            // ------------------------------------------------
            float2 ApplyPerspective(float2 uv, float strength)
            {
                // uv.y = 0 bottom (near), 1 top (far)
                // At top: X is squeezed toward 0.5
                // At bottom: X is full width
                float squeeze = lerp(1.0, uv.y, strength);
                uv.x = (uv.x - 0.5) * squeeze + 0.5;
                return uv;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Apply perspective warp to UVs
                float2 warpedUV = ApplyPerspective(uv, _PerspectiveStrength);

                // Vertical fade (transparent near bottom horizon)
                float fadeT = smoothstep(_FadeStart, _FadeEnd, 1.0 - uv.y);

                // ---- Layer 1: Far ----
                float2 uv1 = warpedUV;
                uv1.x -= _Time.x * _ScrollSpeed1;
                float g1  = DrawGrid(uv1, _GridScale1, _LineWidth1);
                float a1  = g1 * fadeT * _Opacity1;

                // ---- Layer 2: Mid ----
                float2 uv2 = warpedUV;
                uv2.x -= _Time.x * _ScrollSpeed2;
                float g2  = DrawGrid(uv2, _GridScale2, _LineWidth2);
                float a2  = g2 * fadeT * _Opacity2;

                // ---- Layer 3: Near ----
                float2 uv3 = warpedUV;
                uv3.x -= _Time.x * _ScrollSpeed3;
                float g3  = DrawGrid(uv3, _GridScale3, _LineWidth3);
                float a3  = g3 * fadeT * _Opacity3;

                // ---- Composite layers ----
                // Each layer blends its color over the result
                float3 col = float3(0, 0, 0);
                float  a   = 0;

                // Additive blend between layers for that glowing depth look
                col += _GridColor1.rgb * a1;
                col += _GridColor2.rgb * a2;
                col += _GridColor3.rgb * a3;
                a    = saturate(a1 + a2 + a3);

                return fixed4(col, a);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
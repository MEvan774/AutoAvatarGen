Shader "Synthwave/Grid"
{
    // Pseudo-3D scrolling grid drawn on a VERTICAL quad. The perspective is
    // computed in the shader from UVs (top edge of the quad = horizon), so it
    // looks identical through orthographic and perspective cameras. The
    // recording scene uses an orthographic camera, which cannot see a real
    // horizontal ground plane edge-on - that is why this is faked.
    Properties
    {
        _BaseColor    ("Ground Color",                 Color) = (0.05, 0.012, 0.08, 1)
        _LineColor    ("Line Core Color",              Color) = (0.98, 0.88, 1.0, 1)
        _GlowColor    ("Line Glow Color",              Color) = (0.85, 0.38, 0.93, 1)
        _HorizonColor ("Horizon Fade Color",           Color) = (1.0, 0.93, 1.0, 1)
        _WidthCells   ("Cells Across Quad Bottom",     Float) = 16.0
        _DepthCells   ("Row Density",                  Float) = 2.7
        _LineWidth    ("Line Half-Width (cell frac)",  Float) = 0.017
        _GlowWidth    ("Glow Width (cell frac)",       Float) = 0.045
        _ScrollSpeed  ("Scroll Speed (rows/sec)",      Float) = 1.1
        _FadeStart    ("Fade Start (depth, bottom=1)", Float) = 3.0
        _FadeEnd      ("Fade End (depth)",             Float) = 18.0
        _Brightness   ("Brightness",                   Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Cull Off: the recording camera renders the scene mirrored, which
        // flips triangle winding - back-face culling would drop the quad.
        Cull Off
        ZWrite On

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

            fixed4 _BaseColor;
            fixed4 _LineColor;
            fixed4 _GlowColor;
            fixed4 _HorizonColor;
            float  _WidthCells;
            float  _DepthCells;
            float  _LineWidth;
            float  _GlowWidth;
            float  _ScrollSpeed;
            float  _FadeStart;
            float  _FadeEnd;
            float  _Brightness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // v: 0 at the quad's top edge (horizon), 1 at its bottom.
                float v  = 1.0 - i.uv.y;
                float zn = 1.0 / max(v, 0.002); // virtual depth: 1 at bottom, ->inf at horizon

                // Virtual ground coordinates in grid-cell units.
                // Rows scroll toward the viewer over time.
                float2 g;
                g.x = (i.uv.x - 0.5) * _WidthCells * zn;
                g.y = zn * _DepthCells + _Time.y * _ScrollSpeed;

                // Distance to the nearest grid line, in cell fractions
                float2 distToLine = 0.5 - abs(frac(g) - 0.5);

                // Screen-space footprint for anti-aliasing
                float2 aa = max(fwidth(g), 1e-5);

                // Per-axis shimmer damping: rows go sub-pixel near the horizon
                // long before columns do - damp only the axis that collapsed.
                float2 shimmerFix = saturate(1.0 / (aa * 4.0));

                float2 core2 = (1.0 - smoothstep(_LineWidth - aa, _LineWidth + aa, distToLine)) * shimmerFix;
                float  core  = max(core2.x, core2.y);

                float2 glow2 = exp(-distToLine / _GlowWidth) * shimmerFix;
                float  glow  = max(glow2.x, glow2.y);

                float3 col = _BaseColor.rgb;
                col += _GlowColor.rgb * glow * 0.85;
                col  = lerp(col, _LineColor.rgb, core);
                col *= _Brightness;

                // Dissolve into the horizon glow with virtual depth
                float fade = smoothstep(_FadeStart, _FadeEnd, zn);
                col = lerp(col, _HorizonColor.rgb, fade);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

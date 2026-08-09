Shader "Synthwave/Grid"
{
    Properties
    {
        _BaseColor    ("Ground Color",              Color) = (0.05, 0.012, 0.08, 1)
        _LineColor    ("Line Core Color",           Color) = (0.98, 0.88, 1.0, 1)
        _GlowColor    ("Line Glow Color",           Color) = (0.85, 0.38, 0.93, 1)
        _HorizonColor ("Horizon Fade Color",        Color) = (1.0, 0.93, 1.0, 1)
        _CellSize     ("Cell Size (world units)",   Float) = 2.0
        _LineWidth    ("Line Half-Width (world)",   Float) = 0.06
        _GlowWidth    ("Glow Width (world)",        Float) = 0.35
        _ScrollSpeed  ("Scroll Speed (units/sec)",  Float) = 4.0
        _FadeStart    ("Fade Start Distance",       Float) = 70.0
        _FadeEnd      ("Fade End Distance",         Float) = 220.0
        _Brightness   ("Brightness",                Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Cull Off: the recording camera renders the scene mirrored, which
        // flips triangle winding - back-face culling would drop the plane.
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
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            fixed4 _BaseColor;
            fixed4 _LineColor;
            fixed4 _GlowColor;
            fixed4 _HorizonColor;
            float  _CellSize;
            float  _LineWidth;
            float  _GlowWidth;
            float  _ScrollSpeed;
            float  _FadeStart;
            float  _FadeEnd;
            float  _Brightness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // World-space grid so mesh scale/UVs don't matter.
                // Adding time to Z makes the pattern flow toward -Z (the camera).
                float2 p = i.worldPos.xz;
                p.y += _Time.y * _ScrollSpeed;
                float2 g = p / _CellSize;

                // Distance (world units) to the nearest grid line on each axis
                float2 distToLine = (0.5 - abs(frac(g) - 0.5)) * _CellSize;

                // Screen-space footprint in world units, for anti-aliasing
                float2 aa = max(fwidth(g) * _CellSize, 1e-4);

                // Per-axis shimmer damping: when cells shrink below a few
                // pixels along one axis (horizontal lines vanish first at
                // grazing angles), damp only that axis instead of both.
                float2 shimmerFix = saturate(_CellSize / (aa * 4.0));

                float2 core2 = (1.0 - smoothstep(_LineWidth - aa, _LineWidth + aa, distToLine)) * shimmerFix;
                float  core  = max(core2.x, core2.y);

                float2 glow2 = exp(-distToLine / _GlowWidth) * shimmerFix;
                float  glow  = max(glow2.x, glow2.y);

                float3 col = _BaseColor.rgb;
                col += _GlowColor.rgb * glow * 0.85;
                col  = lerp(col, _LineColor.rgb, core);
                col *= _Brightness;

                // Dissolve into the horizon glow with distance
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                float fade = smoothstep(_FadeStart, _FadeEnd, dist);
                col = lerp(col, _HorizonColor.rgb, fade);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

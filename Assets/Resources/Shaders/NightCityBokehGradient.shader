// NightCityBokeh background — layer 1 of 2 (gradient base).
//
// Abstract night-city-through-a-window ambience: a low-contrast vertical
// gradient (deep night blue at the top, slightly lighter dark blue at the
// bottom) with a faint warm haze band in the lower third suggesting a distant
// skyline glow. Two optional window-glass touches — a dark vignette and a
// faint diagonal sheen — ship OFF (strength 0) and are exposed as parameters.
//
// The gradient itself is completely static: all the life in this backdrop
// (twinkle, light swaps) belongs to the bokeh particle layer driven by
// NightCityBokehBackground. Mood crossfades lerp the colors/intensities from
// C#, matching how the sibling backgrounds are driven.
//
// Lives in Resources/Shaders because the material is created at runtime by
// NightCityBokehBackground (Resources.Load, same story as
// LateNightDeskGradient / PostProcessOverlay) — nothing else references it,
// so only its Resources placement gets it into a build.
Shader "NightCityBokeh/Gradient"
{
    Properties
    {
        _TopColor      ("Top (deep night blue)",          Color) = (0.039, 0.055, 0.094, 1)
        _BottomColor   ("Bottom (lighter dark blue)",     Color) = (0.063, 0.082, 0.122, 1)
        _HazeColor     ("Skyline Haze",                   Color) = (1.0, 0.702, 0.361, 1)
        _HazeIntensity ("Skyline Haze Intensity",         Range(0, 1)) = 0.10
        _HazeCenterY   ("Haze Band Center (UV Y)",        Range(0, 1)) = 0.30
        _HazeWidth     ("Haze Band Width (UV)",           Range(0.02, 0.6)) = 0.16
        _VignetteStrength ("Window Vignette (0 = off)",   Range(0, 1)) = 0
        _SheenStrength ("Glass Sheen (0 = off)",          Range(0, 0.2)) = 0
        _SheenCenter   ("Sheen Diagonal Center (0-1)",    Range(0, 1)) = 0.62
        _SheenWidth    ("Sheen Band Width",               Range(0.02, 0.6)) = 0.22
        _Aspect        ("Quad Aspect (w/h)",              Float) = 1.7333
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Cull Off: survives the mirrored recording camera (flipped winding).
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

            fixed4 _TopColor, _BottomColor, _HazeColor;
            float  _HazeIntensity, _HazeCenterY, _HazeWidth;
            float  _VignetteStrength, _SheenStrength, _SheenCenter, _SheenWidth;
            float  _Aspect;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
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
                // --- Vertical gradient: deep blue at the TOP, lighter at the BOTTOM ---
                float3 col = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(i.uv.y));

                // --- Skyline haze: a soft warm band in the lower third, like
                // a distant city glow bleeding into the sky. Gaussian around
                // the band center so it feathers out both up and down.
                float hd = (i.uv.y - _HazeCenterY) / max(_HazeWidth, 1e-3);
                float haze = exp(-hd * hd);
                col += _HazeColor.rgb * (haze * _HazeIntensity);

                // --- Optional window vignette (default 0 = off): a very
                // subtle radial darkening toward the frame corners.
                float2 vc = float2((i.uv.x - 0.5) * _Aspect, i.uv.y - 0.5);
                float vd = dot(vc, vc) / (0.25 * _Aspect * _Aspect + 0.25);
                col *= 1.0 - _VignetteStrength * saturate(vd);

                // --- Optional glass sheen (default 0 = off): one faint
                // diagonal highlight band, like light catching the pane.
                float sd = ((i.uv.x * _Aspect + i.uv.y) / (_Aspect + 1.0) - _SheenCenter)
                           / max(_SheenWidth, 1e-3);
                col += _SheenStrength * exp(-sd * sd);

                // --- Static hash dither: the gradient lives in the darkest
                // few percent of the range, where 8-bit output bands visibly.
                float n = hash21(i.uv * float2(1923.14, 1084.53));
                col += (n - 0.5) * (1.6 / 255.0);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

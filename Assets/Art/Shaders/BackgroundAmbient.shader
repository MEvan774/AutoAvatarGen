// ============================================================================
// BackgroundAmbient.shader — built-in RP procedural background for MugsTech.
//
// Matches the "Calm / Neutral" reference image: a soft periwinkle/lavender
// radial gradient with a drifting bright center-of-mass and 5 low-opacity
// sine-based ribbons that sweep across the frame. No noise functions, no
// texture samples — everything is smooth gradients and sinusoids so the
// result is clean, graphic, and GPU-cheap (< 0.5 ms target).
//
// Mood states are driven entirely by property lerps from C# (see
// BackgroundMoodController.cs). The shader itself is mood-agnostic.
// ============================================================================
Shader "Custom/BackgroundAmbient"
{
    Properties
    {
        [Header(Gradient)]
        _ColorCool      ("Cool Color (top-left)",       Color)            = (0.784, 0.831, 0.941, 1)   // #C8D4F0
        _ColorWarm      ("Warm Color (bottom-right)",   Color)            = (0.831, 0.784, 0.910, 1)   // #D4C8E8
        _CenterBrightness ("Center Brightness",         Range(0.8, 1.0))  = 0.97
        _CenterOffset   ("Center Position",             Vector)           = (0.55, 0.55, 0, 0)

        [Header(Ribbons)]
        _RibbonTint     ("Ribbon Tint (xyz=color, w=base opacity)", Color) = (1, 1, 1, 0.06)
        _RibbonOpacity  ("Ribbon Opacity Multiplier",   Range(0, 2))      = 1.0
        _RibbonScale    ("Ribbon Scale",                Range(0.5, 3.0))  = 1.0
        _RibbonSpeed    ("Animation Speed",             Range(0, 3))      = 1.0

        [Header(Animation)]
        _DriftSpeed     ("Gradient Drift Speed",        Range(0, 0.2))    = 0.08
        _DriftAmount    ("Gradient Drift Amount",       Range(0, 0.1))    = 0.03
    }

    SubShader
    {
        // Background queue renders before geometry. ZTest Always + ZWrite Off
        // keeps this fullscreen and out of the depth buffer.
        Tags
        {
            "RenderType"       = "Opaque"
            "Queue"            = "Background"
            "IgnoreProjector"  = "True"
        }

        Pass
        {
            Name "BackgroundAmbient"
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _ColorCool;
            float4 _ColorWarm;
            float  _CenterBrightness;
            float4 _CenterOffset;
            float4 _RibbonTint;
            float  _RibbonOpacity;
            float  _RibbonScale;
            float  _RibbonSpeed;
            float  _DriftSpeed;
            float  _DriftAmount;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Sine-based ribbon: evaluate vertical distance from a curve
            //     y(x) = amplitude * sin(frequency * x + phase) + offset
            // and return a soft band (0..1) around that curve with `feather` falloff.
            float Ribbon(float2 uv, float amplitude, float frequency, float phase,
                         float offset, float feather)
            {
                float curveY = amplitude * sin(frequency * uv.x + phase) + offset;
                float dist = abs(uv.y - curveY);
                // smoothstep(0, feather, dist) → 0 at the curve, 1 at the edge.
                // Invert to get a band with soft falloff.
                return 1.0 - smoothstep(0.0, feather, dist);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t   = _Time.y;

                // ---- Base gradient ----
                // Drifting center-of-mass — a Lissajous path at ±drift amount, slow speeds.
                float2 centerDrift = float2(
                    _DriftAmount        * sin(t * _DriftSpeed),
                    _DriftAmount * 0.67 * sin(t * _DriftSpeed * 0.625)
                );
                float2 center = _CenterOffset.xy + centerDrift;

                // Diagonal warm/cool axis from top-left (cool) → bottom-right (warm).
                // `coolWarmT` is 0 at top-left, 1 at bottom-right.
                float coolWarmT = saturate(0.5 + 0.5 * (uv.x - (1.0 - uv.y)));
                float3 gradient = lerp(_ColorCool.rgb, _ColorWarm.rgb, coolWarmT);

                // Near-white luminous core at the drifting center.
                float distToCenter = length(uv - center);
                float centerGlow = 1.0 - smoothstep(0.0, 0.7, distToCenter);
                float3 nearWhite = float3(1, 1, 1) * _CenterBrightness;
                // Mix in the white toward the center (up to 55%).
                float3 col = lerp(gradient, nearWhite, centerGlow * 0.55);

                // ---- Ribbons ----
                // Frequencies near PI = half a sine period across the full frame width.
                // Each ribbon animates phase at a unique speed so they never sync.
                // Back group (slower, thinner):
                float r1 = Ribbon(uv, 0.08, 2.6 * _RibbonScale,
                                  0.3 + t * 0.020 * _RibbonSpeed, 0.88, 0.10); // upper-left sweep
                float r2 = Ribbon(uv, 0.07, 2.2 * _RibbonScale,
                                  1.7 + t * 0.025 * _RibbonSpeed, 0.78, 0.11); // upper-center diagonal
                float r3 = Ribbon(uv, 0.10, 1.8 * _RibbonScale,
                                  2.4 + t * 0.018 * _RibbonSpeed, 0.35, 0.12); // bottom-left curve

                // Front group (~1.3× faster, slightly bolder):
                float r4 = Ribbon(uv, 0.13, 2.0 * _RibbonScale,
                                  0.9 + t * 0.040 * _RibbonSpeed, 0.28, 0.14); // lower-right sweep
                float r5 = Ribbon(uv, 0.18, 1.5 * _RibbonScale,
                                  4.1 + t * 0.050 * _RibbonSpeed, 0.10, 0.16); // bottom-right bulge

                // Slightly cooler / warmer tints so the ribbons read as translucent fabric
                // rather than flat white overlays.
                float3 tintCool  = saturate(_ColorCool.rgb * 0.85 + 0.18);
                float3 tintWhite = float3(1, 1, 1);
                float3 tintWarm  = saturate(_ColorWarm.rgb * 0.85 + 0.15);

                // Each ribbon's "base" opacity below is multiplied by the global
                // _RibbonOpacity so moods like Minimal-Focus can fade them out.
                col += tintCool  * r1 * 0.05 * _RibbonOpacity;
                col += tintWhite * r2 * 0.04 * _RibbonOpacity;
                col += tintCool  * r3 * 0.05 * _RibbonOpacity;
                col += tintWarm  * r4 * 0.06 * _RibbonOpacity;
                col += tintWarm  * r5 * 0.08 * _RibbonOpacity;

                // Optional global tint modulation via _RibbonTint (alpha is extra scale).
                col += _RibbonTint.rgb * (_RibbonTint.a - 0.06) * _RibbonOpacity;

                col = saturate(col);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}

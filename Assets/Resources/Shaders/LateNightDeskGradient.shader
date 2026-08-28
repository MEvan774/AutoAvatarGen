// LateNightDesk background — layer 1 of 3 (gradient base).
//
// Abstract 2 AM home-office ambience: a low-contrast vertical gradient
// (near-black navy at the top, dark charcoal-blue at the bottom) with a soft
// warm glow bleeding in from ONE side of the frame, like an off-screen desk
// lamp. The glow "breathes" on a slow sine cycle.
//
// Lives in Resources/Shaders because the material is created at runtime by
// LateNightDeskBackground (Resources.Load, same story as PostProcessOverlay
// and SpriteShadowBlur) — nothing else references it, so only its Resources
// placement gets it into a build.
//
// The breathe phases are ACCUMULATED IN C# (LateNightDeskBackground) rather
// than derived from _Time here: mood transitions lerp the cycle LENGTH, and
// sin(t * w) with a changing w would jump phase discontinuously — a visible
// pop in glow intensity. Integrating phase += dt * w on the CPU keeps the
// breathing continuous through any cycle-length crossfade. When nothing
// drives the phases (e.g. material preview) they sit at 0 and the glow is
// simply steady.
Shader "LateNightDesk/Gradient"
{
    Properties
    {
        _TopColor      ("Top (near-black navy)",         Color) = (0.043, 0.059, 0.102, 1)
        _BottomColor   ("Bottom (dark charcoal blue)",   Color) = (0.082, 0.106, 0.149, 1)
        _GlowColor     ("Lamp Glow",                     Color) = (1.0, 0.702, 0.361, 1)
        _GlowIntensity ("Lamp Glow Intensity",           Range(0, 2)) = 0.4
        _GlowSide      ("Glow Side (0=Left, 1=Right)",   Float) = 1
        _GlowWidth     ("Glow Falloff Radius (frac of quad width)", Range(0.03, 0.5)) = 0.17
        _GlowCenterY   ("Glow Center Height (UV Y)",     Range(0, 1)) = 0.42
        _GlowStretchY  ("Glow Vertical Stretch",         Range(0.5, 3)) = 1.35
        _BreatheAmount ("Breathe Amplitude",             Range(0, 0.5)) = 0.12
        _BreathePhase  ("Breathe Phase (rad, C#-driven)",   Float) = 0
        _BreathePhase2 ("Breathe Phase 2 (rad, C#-driven)", Float) = 0
        _Aspect        ("Quad Aspect (w/h)",             Float) = 1.7333
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

            fixed4 _TopColor, _BottomColor, _GlowColor;
            float  _GlowIntensity, _GlowSide, _GlowWidth, _GlowCenterY, _GlowStretchY;
            float  _BreatheAmount, _BreathePhase, _BreathePhase2;
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
                // --- Vertical gradient: navy at the TOP, charcoal at the BOTTOM ---
                float3 col = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(i.uv.y));

                // --- Off-screen lamp glow, centered on one quad edge ---
                // The quad over-covers the visible frame (26x15 vs ~17.8x10 at
                // ortho size 5), so the glow center at uv.x = 0/1 already sits
                // OFF-SCREEN; the default falloff radius is tuned so the spill
                // occupies roughly the outer 25-35% of the VISIBLE frame.
                // Aspect-corrected coords keep the falloff circular; the
                // vertical stretch elongates it a little, like lamp spill.
                float  edgeX = _GlowSide; // 0 -> left quad edge, 1 -> right
                float2 d = float2((i.uv.x - edgeX) * _Aspect,
                                  (i.uv.y - _GlowCenterY) / max(_GlowStretchY, 0.01));
                float sigma = max(_GlowWidth * _Aspect, 1e-3);
                float glow  = exp(-dot(d, d) / (2.0 * sigma * sigma));

                // --- Breathing: two incommensurate sines (weights sum to 1) so
                // the swing stays within +/- _BreatheAmount but the pattern
                // never exactly repeats. Phases integrated in C# (see header).
                float breathe = 1.0 + _BreatheAmount * (0.72 * sin(_BreathePhase)
                                                     + 0.28 * sin(_BreathePhase2));

                col += _GlowColor.rgb * (glow * _GlowIntensity * breathe);

                // --- Static hash dither: this gradient lives entirely in the
                // darkest few percent of the range, where 8-bit output bands
                // visibly. Half an LSB of spatial noise hides the steps.
                float n = hash21(i.uv * float2(1923.14, 1084.53));
                col += (n - 0.5) * (1.6 / 255.0);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

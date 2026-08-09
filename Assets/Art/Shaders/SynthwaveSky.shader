Shader "Synthwave/Sky"
{
    Properties
    {
        _TopColor         ("Sky Top",                 Color) = (0.10, 0.02, 0.14, 1)
        _MidColor         ("Sky Above Horizon",       Color) = (0.33, 0.10, 0.40, 1)
        _GroundColor      ("Below Horizon",           Color) = (0.05, 0.012, 0.08, 1)
        _HorizonGlowColor ("Horizon Glow Core",       Color) = (1.0, 0.95, 1.0, 1)
        _GlowTint         ("Horizon Glow Spread",     Color) = (0.85, 0.40, 0.93, 1)
        _HorizonY         ("Horizon Line (UV Y)",     Range(0, 1)) = 0.5
        _GlowHeight       ("Glow Core Height (UV)",   Float) = 0.008
        _GlowSpread       ("Glow Spread (UV)",        Float) = 0.035
        _MountainColor1   ("Mountains Far (hazy)",    Color) = (0.24, 0.09, 0.30, 1)
        _MountainColor2   ("Mountains Near (dark)",   Color) = (0.13, 0.04, 0.18, 1)
        _MountainHeight   ("Mountain Height (UV)",    Float) = 0.16
        _StarDensity      ("Star Density",            Float) = 60.0
        _StarBrightness   ("Star Brightness",         Float) = 1.0
        _TwinkleSpeed     ("Star Twinkle Speed",      Float) = 2.0
        _StarDrift        ("Star Drift Speed",        Float) = 0.002
        _HazeColor        ("Drifting Haze Color",     Color) = (0.48, 0.23, 0.56, 1)
        _HazeStrength     ("Haze Strength",           Range(0, 1)) = 0.22
        _HazeSpeed        ("Haze Drift Speed",        Float) = 0.012
        _Aspect           ("Quad Aspect (w/h)",       Float) = 1.7333
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

            fixed4 _TopColor, _MidColor, _GroundColor;
            fixed4 _HorizonGlowColor, _GlowTint;
            fixed4 _MountainColor1, _MountainColor2;
            fixed4 _HazeColor;
            float  _HorizonY, _GlowHeight, _GlowSpread;
            float  _MountainHeight;
            float  _StarDensity, _StarBrightness, _TwinkleSpeed, _StarDrift;
            float  _HazeStrength, _HazeSpeed;
            float  _Aspect;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += amp * vnoise(p);
                    p *= 2.03;
                    amp *= 0.5;
                }
                return v;
            }

            float stars(float2 suv, float t)
            {
                float2 g  = suv * _StarDensity;
                float2 id = floor(g);
                float2 f  = frac(g);

                float h = hash21(id);
                float present = step(0.72, h); // ~28% of cells hold a star

                float2 starPos = float2(hash21(id + 7.13), hash21(id + 3.71));
                float d    = length(f - starPos);
                float size = lerp(0.04, 0.11, hash21(id + 11.7));
                float star = smoothstep(size, 0.0, d);

                float tw     = 0.55 + 0.45 * sin(t * _TwinkleSpeed + h * 40.0);
                float bright = lerp(0.25, 1.0, hash21(id + 5.5));
                return star * tw * bright * present;
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
                float t = _Time.y;
                // Aspect-corrected uv so noise/stars aren't stretched
                float2 auv = float2(i.uv.x * _Aspect, i.uv.y);
                float dy = i.uv.y - _HorizonY;

                // --- Sky gradient (above horizon) ---
                float grad = saturate(dy / max(1.0 - _HorizonY, 1e-3));
                float3 sky = lerp(_MidColor.rgb, _TopColor.rgb, pow(grad, 0.7));

                // --- Drifting haze, strongest just above the horizon ---
                float haze = fbm(auv * 3.0 + float2(t * _HazeSpeed, 0.0));
                sky += _HazeColor.rgb * (haze * _HazeStrength * exp(-max(dy, 0.0) * 6.0));

                // --- Mountain silhouettes (two ridge layers) ---
                float ridge1 = fbm(float2(auv.x * 3.0 + 17.0, 17.0));
                float h1 = _HorizonY + _MountainHeight * (0.30 + 0.70 * ridge1);
                float m1 = smoothstep(h1 + 0.004, h1 - 0.004, i.uv.y);

                float ridge2 = fbm(float2(auv.x * 4.7 + 47.0, 47.0));
                float h2 = _HorizonY + _MountainHeight * 0.55 * (0.25 + 0.75 * ridge2);
                float m2 = smoothstep(h2 + 0.003, h2 - 0.003, i.uv.y);

                // --- Stars: twinkle + slow sideways drift, hidden behind mountains ---
                float2 suv = auv + float2(t * _StarDrift, 0.0);
                float starFade = smoothstep(0.02, 0.14, dy);
                float s = stars(suv, t) * _StarBrightness * starFade * (1.0 - m1);
                sky += s;

                sky = lerp(sky, _MountainColor1.rgb, m1);
                sky = lerp(sky, _MountainColor2.rgb, m2);

                // --- Below the horizon: dark ground (grid plane covers most of it) ---
                float3 col = lerp(sky, _GroundColor.rgb, step(i.uv.y, _HorizonY));

                // --- Horizon glow, additive over everything ---
                float glowCore   = exp(-abs(dy) / max(_GlowHeight, 1e-4));
                float glowSpread = exp(-abs(dy) / max(_GlowSpread, 1e-4));
                col += _HorizonGlowColor.rgb * glowCore * 1.15;
                col += _GlowTint.rgb * glowSpread * 0.4;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

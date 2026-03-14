Shader "Custom/PostProcessOverlay"
{
    Properties
    {
        // Vignette
        _VignetteColor     ("Vignette Color",      Color)        = (0, 0, 0, 1)
        _VignetteIntensity ("Vignette Intensity",  Range(0, 1))  = 0.35
        _VignetteSmoothness("Vignette Smoothness", Range(0.01, 1)) = 0.4
        _VignetteRoundness ("Vignette Roundness",  Range(0, 1))  = 0.8

        // Bloom (screen-space glow approximation)
        _BloomColor        ("Bloom Color",         Color)        = (0.4, 0.3, 0.8, 1)
        _BloomIntensity    ("Bloom Intensity",      Range(0, 2))  = 0.4
        _BloomRadius       ("Bloom Radius",         Range(0, 1))  = 0.5

        // Film Grain
        _GrainIntensity    ("Grain Intensity",     Range(0, 1))  = 0.06
        _GrainSize         ("Grain Size",          Range(1, 8))  = 2.0
        _GrainColored      ("Colored Grain",       Range(0, 1))  = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue"      = "Overlay+100"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
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

            // Vignette
            fixed4 _VignetteColor;
            float  _VignetteIntensity;
            float  _VignetteSmoothness;
            float  _VignetteRoundness;

            // Bloom
            fixed4 _BloomColor;
            float  _BloomIntensity;
            float  _BloomRadius;

            // Grain
            float  _GrainIntensity;
            float  _GrainSize;
            float  _GrainColored;

            // ---- Noise helpers ----
            float hash1(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float hash1f(float2 p, float t)
            {
                return frac(sin(dot(p + frac(t), float2(127.1, 311.7))) * 43758.5453);
            }

            float3 hash3(float2 p, float t)
            {
                return float3(
                    hash1f(p,              t),
                    hash1f(p + 17.3,       t),
                    hash1f(p + float2(0.5, 0.7) * 31.1, t)
                );
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
                float4 result = float4(0, 0, 0, 0);

                // ------------------------------------------------
                // VIGNETTE
                // ------------------------------------------------
                float2 vigUV  = uv * 2.0 - 1.0;
                // Roundness blends between circular and rectangular
                vigUV.x      *= lerp(1.0, _VignetteRoundness + 0.2, _VignetteRoundness);
                float vigDist = length(vigUV);
                float inner   = 1.0 - _VignetteIntensity;
                float outer   = inner + _VignetteSmoothness;
                float vignette = smoothstep(inner, outer, vigDist);

                result.rgb = _VignetteColor.rgb;
                result.a   = vignette * _VignetteColor.a;

                // ------------------------------------------------
                // BLOOM (soft glow toward screen center/edges)
                // ------------------------------------------------
                // Radial glow from center — simulates light bleeding
                float2 centered  = uv - 0.5;
                float  bloomDist = length(centered);
                float  bloomMask = exp(-bloomDist * (1.0 / max(_BloomRadius, 0.01)) * 2.0);
                float3 bloomCol  = _BloomColor.rgb * bloomMask * _BloomIntensity;

                // Add bloom as additive on top of vignette layer
                result.rgb += bloomCol;
                result.a    = saturate(result.a + dot(bloomCol, float3(0.299, 0.587, 0.114)) * 0.5);

                // ------------------------------------------------
                // FILM GRAIN
                // ------------------------------------------------
                // Tile UVs by grain size, animate per frame
                float  t         = _Time.y * 8.0; // grain changes ~8x per second
                float2 grainUV   = floor(uv * (_ScreenParams.xy / _GrainSize)) / (_ScreenParams.xy / _GrainSize);

                float  grainMono = hash1f(grainUV, t) * 2.0 - 1.0;
                float3 grainRGB  = hash3(grainUV,  t) * 2.0 - 1.0;
                float3 grain     = lerp(float3(grainMono, grainMono, grainMono), grainRGB, _GrainColored);

                // Grain is stronger in midtones, weaker in darks/highlights
                float grainAlpha = _GrainIntensity;
                result.rgb += grain * grainAlpha;
                result.a    = saturate(result.a + _GrainIntensity * 0.5);

                return result;
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
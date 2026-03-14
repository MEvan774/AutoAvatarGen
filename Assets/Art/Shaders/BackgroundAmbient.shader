Shader "Custom/BackgroundAmbient"
{
    Properties
    {
        _ColorA ("Color A (Dark)", Color) = (0.08, 0.08, 0.25, 1)
        _ColorB ("Color B (Mid)", Color) = (0.15, 0.1, 0.4, 1)
        _ColorC ("Color C (Accent)", Color) = (0.3, 0.2, 0.6, 1)
        _WaveSpeed ("Wave Speed", Float) = 0.08
        _WaveScale ("Wave Scale", Float) = 1.5
        _GlowIntensity ("Glow Intensity", Float) = 0.6
        _GlowX ("Glow Center X", Float) = 0.5
        _GlowY ("Glow Center Y", Float) = 0.5
        _Time2 ("Time Override", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f    { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            float4 _ColorA, _ColorB, _ColorC;
            float _WaveSpeed, _WaveScale, _GlowIntensity, _GlowX, _GlowY;

            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            // Smooth noise
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), u.x),
                            lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), u.x), u.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y * _WaveSpeed;

                // Flowing wave layers
                float wave1 = noise(uv * _WaveScale + float2(t * 0.3, t * 0.1));
                float wave2 = noise(uv * _WaveScale * 1.8 + float2(-t * 0.2, t * 0.15) + 3.7);
                float wave3 = noise(uv * _WaveScale * 0.7 + float2(t * 0.1, -t * 0.08) + 7.3);

                float blend = wave1 * 0.5 + wave2 * 0.3 + wave3 * 0.2;

                // Base gradient
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, blend);
                col = lerp(col, _ColorC.rgb, wave2 * 0.5);

                // Central glow (the bright blue-purple core in your image)
                float2 glowCenter = float2(_GlowX, _GlowY);
                float dist = length(uv - glowCenter);
                float glow = exp(-dist * dist * 3.5) * _GlowIntensity;
                col += _ColorC.rgb * glow * 0.8;

                // Subtle vignette
                float vignette = 1.0 - smoothstep(0.4, 1.0, length(uv - 0.5) * 1.4);
                col *= vignette;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
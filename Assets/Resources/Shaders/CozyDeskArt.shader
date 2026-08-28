// CozyDeskNight background — the artwork base layer.
//
// Draws the painted lofi desk illustration (Assets/Art/Backgrounds/
// CozyDeskNight.png) unlit and opaque on a frame-fit quad; every animated
// element (window bokeh, steam, lamp glow, star twinkle) is layered in front
// by CozyDeskNightBackground. _Tint lets the whole artwork be dimmed or
// warmed without touching the source image.
//
// Lives in Resources/Shaders because the material is created at runtime
// (Resources.Load, same story as the other background shaders) — nothing
// else references it, so only its Resources placement gets it into a build.
Shader "CozyDeskNight/Art"
{
    Properties
    {
        _MainTex ("Artwork", 2D) = "white" {}
        _Tint    ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Cull Off: survives the mirrored recording camera (flipped winding)
        // AND the negative x-scale the controller uses for flipHorizontal.
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Tint;
                return fixed4(c.rgb, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}

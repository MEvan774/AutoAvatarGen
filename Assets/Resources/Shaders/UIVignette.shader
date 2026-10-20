// ============================================================================
// UIVignette.shader — UI-compatible edge darkening.
//
// Used by BigCenterCard on top of the overlay panel + scrolling pattern (and
// by the visuals menu's Big Headline preview). The Graphic's color is the
// vignette color; its alpha is the opacity at full darkness. Lives under
// Resources because nothing references it from an asset — only Resources
// placement gets it into a build (same story as UIScrollingPattern).
// ============================================================================
Shader "Custom/UIVignette"
{
    Properties
    {
        _MainTex  ("Unused (UI texture slot)",                   2D)              = "white" {}
        _Size     ("Size — how far it reaches toward the center", Range(0, 1))    = 0.35
        _Softness ("Softness — width of the fade band",          Range(0.01, 1)) = 0.6
        _Shape    ("Shape — 0 round, 1 follows the frame",       Range(0, 1))    = 0.5
        _Aspect   ("Rect width / height",                        Float)           = 1.7777778
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "RenderType"        = "Transparent"
            "IgnoreProjector"   = "True"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite   Off
        Cull     Off
        Lighting Off

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
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            float _Size;
            float _Softness;
            float _Shape;
            float _Aspect;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Distance from the center, 1 at the top/bottom edges. Shape 0
                // measures a true circle (x scaled by the aspect, so the sides
                // darken more); shape 1 a rounded rectangle that reaches all
                // four edges evenly (superellipse in frame-normalized space).
                float2 q = abs(i.uv * 2.0 - 1.0);
                q.x *= lerp(_Aspect, 1.0, _Shape);
                float n = lerp(2.0, 6.0, _Shape);
                float d = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n);

                float inner = 1.0 - _Size;
                float v     = smoothstep(inner, inner + _Softness, d);

                // ±half an 8-bit step of screen-space noise inside the fade so
                // the long, dark gradient doesn't band; the clear center stays
                // exactly clear.
                float noise = frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                float a     = v > 0.0 ? saturate(v * i.color.a + noise / 255.0) : 0.0;
                return fixed4(i.color.rgb, a);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}

// ============================================================================
// UIScrollingPattern.shader — UI-compatible repeating pattern that scrolls
// the texture UVs along a configurable direction over time.
//
// Used by BigCenterCard to put a subtly-animated pattern on top of the
// overlay panel. The texture's wrap mode must be Repeat (set at runtime by
// the card). The scroll direction is the direction the visual pattern moves;
// default (1, -1) scrolls diagonally down and to the right.
// ============================================================================
Shader "Custom/UIScrollingPattern"
{
    Properties
    {
        _MainTex     ("Pattern Texture",           2D)             = "white" {}
        _Color       ("Tint (RGB) + Opacity (A)",  Color)          = (1, 1, 1, 1)
        _ScrollSpeed ("Scroll Speed",              Float)          = 0.08
        _ScrollDir   ("Visual Scroll Direction",   Vector)         = (1, -1, 0, 0)
        _TileScale   ("Tile Scale",                Float)          = 4.0
        _TilePadding ("Tile Padding (0-0.49)",     Range(0, 0.49)) = 0.0
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

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _ScrollSpeed;
            float4    _ScrollDir;
            float     _TileScale;
            float     _TilePadding;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // _ScrollDir is the direction the visual pattern should appear
                // to move. With `uv_sample = uv - dir*t` the content shifts
                // toward `dir` on screen, so default (1, -1) moves down-right.
                float2 dir      = normalize(_ScrollDir.xy);
                float2 scrollUV = i.uv * _TileScale - _Time.y * _ScrollSpeed * dir;

                // Within each tile, remap the inner [_TilePadding, 1-_TilePadding]
                // range back to [0, 1] and mask out the padding ring so tiles
                // sit in transparent space rather than touching edge-to-edge.
                float2 cellUV   = frac(scrollUV);
                float  inner    = 1.0 - 2.0 * _TilePadding;
                float2 sampleUV = (cellUV - _TilePadding) / max(inner, 1e-5);

                float mask = step(0.0, sampleUV.x) * step(sampleUV.x, 1.0)
                           * step(0.0, sampleUV.y) * step(sampleUV.y, 1.0);

                fixed4 tex = tex2D(_MainTex, sampleUV);
                return fixed4(tex.rgb * i.color.rgb, tex.a * i.color.a * mask);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}

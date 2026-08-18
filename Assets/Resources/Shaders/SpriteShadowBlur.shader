// Soft drop-shadow variant of Sprites/Default: renders the sprite's BLURRED
// alpha silhouette in a flat shadow color instead of the sprite itself.
// PresenterShadow drives it — a shadow SpriteRenderer shows the presenter's
// current sprite through this shader, offset a few pixels down, giving the
// CSS box-shadow elevation (0px 4px 6px -1px rgba(0,0,0,0.1)) the content
// cards get from their pre-blurred 9-slice sprite.
//
// The blur is a 17-tap ring filter (center + 8 taps at half radius + 8 at full
// radius) over the sprite's alpha — cheap, and at the ~3-texel radii a 6px
// screen blur works out to, indistinguishable from a true Gaussian.
// Built-in render pipeline, linear color space (project default).
Shader "MugsTech/SpriteShadowBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.1)
        _BlurTexels ("Blur Radius (texels)", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _ShadowColor;
            float _BlurTexels;

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                static const float2 DIRS[8] =
                {
                    float2( 1,  0), float2( 0.7071,  0.7071),
                    float2( 0,  1), float2(-0.7071,  0.7071),
                    float2(-1,  0), float2(-0.7071, -0.7071),
                    float2( 0, -1), float2( 0.7071, -0.7071)
                };

                float2 texel = _MainTex_TexelSize.xy;
                float r = max(_BlurTexels, 0.0);

                // Center 0.20 + inner ring 8×0.0625 + outer ring 8×0.0375 = 1.
                float a = tex2D(_MainTex, IN.texcoord).a * 0.20;
                for (int i = 0; i < 8; i++)
                {
                    float2 d = DIRS[i] * texel;
                    a += tex2D(_MainTex, IN.texcoord + d * (r * 0.5)).a * 0.0625;
                    a += tex2D(_MainTex, IN.texcoord + d * r).a * 0.0375;
                }

                fixed4 col;
                col.rgb = _ShadowColor.rgb;
                col.a = a * _ShadowColor.a * IN.color.a;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}

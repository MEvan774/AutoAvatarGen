// Sprites/Default with a mood recolour, for the Violet Doodles floating
// doodles (VioletDoodlesMood drives it). The doodle art is painted lavender,
// so a plain multiply tint could only darken it — pink or aqua doodles would
// come out muddy purple. Instead the sprite's colour is replaced by
// _MoodTint's hue at the sprite's OWN luminance: depth darkening, blur and
// painted shading survive, only the colour changes.
//
//   _MoodAmount 0 = exactly Sprites/Default (Calm), 1 = fully recoloured.
//   _MoodAlpha    = opacity multiplier.
//
// Uses Unity's own sprite vertex stage (UnitySprites.cginc: renderer colour,
// flip, pixel snap) and the same premultiplied blend as Sprites/Default.
// Built-in render pipeline, linear colour space (project default).
Shader "MugsTech/MoodTintSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0

        _MoodTint ("Mood Tint", Color) = (1,1,1,1)
        _MoodAmount ("Mood Amount", Range(0, 1)) = 0
        _MoodAlpha ("Mood Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment MoodFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed4 _MoodTint;
            half _MoodAmount;
            half _MoodAlpha;

            fixed4 MoodFrag(v2f IN) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(IN.texcoord) * IN.color;

                // Rec.709 luminance — correct in linear space.
                const half3 LUMA = half3(0.2126, 0.7152, 0.0722);
                half lum = dot(c.rgb, LUMA);
                half tintLum = max(dot(_MoodTint.rgb, LUMA), 0.001);
                half3 recoloured = saturate(_MoodTint.rgb * (lum / tintLum));

                c.rgb = lerp(c.rgb, recoloured, _MoodAmount);
                c.a *= _MoodAlpha;
                c.rgb *= c.a;   // premultiply, as SpriteFrag does
                return c;
            }
            ENDCG
        }
    }
}

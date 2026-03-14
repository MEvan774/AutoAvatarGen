Shader "Custom/UIBloomLayer"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color   ("Tint",           Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
            "PreviewType"    = "Plane"
        }

        Blend SrcAlpha One
        ZWrite   Off
        Cull     Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4    _Color;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);

                // Use sprite's own alpha so shape is preserved exactly
                float spriteAlpha = tex.a;

                // Radial falloff from center — smooth glow, not hard edge
                float2 centered = i.uv - 0.5;
                float  dist     = length(centered) * 2.0;
                float  falloff  = 1.0 - saturate(dist);
                falloff         = pow(falloff, 0.6); // lower = wider softer glow

                // Combine: glow only where sprite has shape
                float finalAlpha = spriteAlpha * falloff * i.color.a;

                return fixed4(i.color.rgb, finalAlpha);
            }
            ENDCG
        }
    }
}
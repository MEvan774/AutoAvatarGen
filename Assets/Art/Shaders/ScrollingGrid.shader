Shader "Custom/ScrollingGrid"
{
    Properties
    {
        _GridColor   ("Grid Line Color",  Color)        = (0.4, 0.5, 0.9, 1.0)
        _LineWidth   ("Line Width",       Range(0.005, 0.1)) = 0.02
        _GridScale   ("Grid Scale",       Float)        = 8.0
        _ScrollSpeed ("Scroll Speed",     Float)        = 0.15
        _FadeStart   ("Fade Start (0-1)", Float)        = 0.0
        _FadeEnd     ("Fade End (0-1)",   Float)        = 0.7
        _Opacity     ("Overall Opacity",  Range(0, 1))  = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue"      = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
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

            fixed4 _GridColor;
            float  _LineWidth;
            float  _GridScale;
            float  _ScrollSpeed;
            float  _FadeStart;
            float  _FadeEnd;
            float  _Opacity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Scroll forward on V axis
                float2 uv = i.uv;
                uv.y -= _Time.y * _ScrollSpeed;

                // Scale into grid space
                float2 gridUV = frac(uv * _GridScale);

                // Anti-aliased lines
                float2 fw       = fwidth(uv * _GridScale);
                float2 gridLine = smoothstep(_LineWidth - fw, _LineWidth + fw, gridUV)
                    * smoothstep(_LineWidth - fw, _LineWidth + fw, 1.0 - gridUV);

                float onLine = 1.0 - min(gridLine.x, gridLine.y);

                // Fade: transparent at bottom (uv.y=0), visible at top (uv.y=1)
                float fadeT = smoothstep(_FadeStart, _FadeEnd, i.uv.x);
                float alpha = onLine * fadeT * _Opacity;

                fixed4 col = _GridColor;
                col.a      = alpha;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
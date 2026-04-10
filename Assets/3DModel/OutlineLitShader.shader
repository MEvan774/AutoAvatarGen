Shader "Custom/OutlineLit"
{
    Properties
    {
        // ── Main Surface ────────────────────────────────────────────────
        _BaseMap        ("Albedo Texture (RGB)", 2D)    = "white" {}
        _BaseColor      ("Color Tint", Color)           = (1,1,1,1)

        // ── Black Outline ────────────────────────────────────────────────
        _OutlineColor   ("Outline Color", Color)        = (0,0,0,1)
        _OutlineWidth   ("Outline Width", Range(0, 0.1))= 0.02

        // ── Sticker Silhouette Outline ───────────────────────────────────
        _SilhouetteColor ("Silhouette Color", Color)   = (1,1,1,1)
        _SilhouetteWidth ("Silhouette Width", Range(0, 0.2)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"  = "Opaque"
            "Queue"       = "Geometry"
        }
        LOD 300

        // ════════════════════════════════════════════════════════════════
        // PASS 1 — STENCIL PREPASS
        //          Renders the real character surface into the stencil
        //          buffer (value = 1). No color output. This marks every
        //          pixel the character occupies, including all overlapping
        //          body parts.
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "StencilPrepass"

            Cull  Back
            ZTest LEqual
            ZWrite On
            ColorMask 0

            Stencil
            {
                Ref  1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }

        // ════════════════════════════════════════════════════════════════
        // PASS 2 — WHITE SILHOUETTE (sticker border)
        //          Extruded back-face shell. Only draws where stencil
        //          is NOT 1, meaning the shell pixels that stick out
        //          into empty space beyond the real mesh. Hidden where
        //          body parts overlap or where other geometry exists.
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "SilhouetteOutline"

            Cull   Front
            ZTest  LEqual
            ZWrite On

            Stencil
            {
                Ref  1
                Comp NotEqual      // Only draw where stencil != 1 (empty space)
                Pass Keep
            }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SilhouetteColor;
            float  _SilhouetteWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 normal      = normalize(v.normal);
                float3 extrudedPos = v.vertex.xyz + normal * _SilhouetteWidth;
                o.pos              = UnityObjectToClipPos(float4(extrudedPos, 1.0));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return _SilhouetteColor;
            }
            ENDCG
        }

        // ════════════════════════════════════════════════════════════════
        // PASS 3 — BLACK OUTLINE
        //          Normal back-face extrusion with no stencil masking.
        //          Appears around all mesh edges including between
        //          overlapping parts (arms, accessories, hanging items).
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "BlackOutline"

            Cull  Front
            ZTest LEqual
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _OutlineColor;
            float  _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 normal      = normalize(v.normal);
                float3 extrudedPos = v.vertex.xyz + normal * _OutlineWidth;
                o.pos              = UnityObjectToClipPos(float4(extrudedPos, 1.0));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }

        // ════════════════════════════════════════════════════════════════
        // PASS 4 — UNLIT SURFACE (texture at full brightness)
        //          No lighting, no shadows — just the texture * tint.
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "UnlitSurface"
            Tags { "LightMode" = "ForwardBase" }

            Cull  Back
            ZTest LEqual
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            float4    _BaseColor;

            struct appdata
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.texcoord, _BaseMap);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = tex2D(_BaseMap, i.uv) * _BaseColor;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        // ════════════════════════════════════════════════════════════════
        // PASS 5 — Shadow caster
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest  LEqual
            ColorMask 0
            Cull Back

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}

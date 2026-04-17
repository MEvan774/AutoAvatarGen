// ============================================================================
// ScrollingShapeLayer.shader — SDF-based procedural shape renderer.
//
// One shader for all 5 shape variants (circle, dash, pill, plus, ring).
// Per-shape values (type, color+opacity) are passed via MaterialPropertyBlock
// from ScrollingShapeController.cs. No textures, no shadows, alpha-blended,
// unlit. Built-in render pipeline compatible.
//
// Shape types (via _ShapeType property):
//   0 → solid circle (dot)
//   1 → horizontal short line / dash (rounded ends)
//   2 → rounded rectangle / pill
//   3 → plus / cross
//   4 → hollow circle / ring
// ============================================================================
Shader "Custom/ScrollingShapeLayer"
{
    Properties
    {
        _ShapeType ("Shape Type (0-4)", Range(0, 4)) = 0
        _Color     ("Tint (RGB) + Opacity (A)",     Color) = (0.85, 0.83, 0.94, 0.05)
        _Softness  ("Edge Softness",                Range(0.0001, 0.05)) = 0.01
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            float  _ShapeType;
            float4 _Color;
            float  _Softness;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // --- SDF primitives (all evaluated in centered [-0.5, 0.5] space) ---

            float sdCircle(float2 p, float r)
            {
                return length(p) - r;
            }

            float sdBox(float2 p, float2 halfSize)
            {
                float2 d = abs(p) - halfSize;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Capsule (rounded-end line) horizontal, length=2*halfLen, thickness=2*thick
            float sdCapsuleH(float2 p, float halfLen, float thick)
            {
                p.x -= clamp(p.x, -halfLen, halfLen);
                return length(p) - thick;
            }

            // Rounded box with uniform corner radius r
            float sdRoundedBox(float2 p, float2 halfSize, float r)
            {
                return sdBox(p, halfSize - r) - r;
            }

            // Plus / cross = union (min) of two thin boxes
            float sdPlus(float2 p, float armLen, float armThick)
            {
                float a = sdBox(p, float2(armLen, armThick));
                float b = sdBox(p, float2(armThick, armLen));
                return min(a, b);
            }

            // Ring = abs(circleSDF) minus thickness = distance from the ring line
            float sdRing(float2 p, float r, float thick)
            {
                return abs(length(p) - r) - thick;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // UV 0..1 → centered -0.5..0.5
                float2 p = i.uv - 0.5;

                float d = 0.0;
                int shape = (int)floor(_ShapeType + 0.5);

                // Each shape is sized to leave a ~5% margin inside the quad so soft
                // edges don't clip at the quad boundary.
                if (shape == 0)      d = sdCircle     (p, 0.42);
                else if (shape == 1) d = sdCapsuleH   (p, 0.40, 0.06);
                else if (shape == 2) d = sdRoundedBox (p, float2(0.42, 0.18), 0.17);
                else if (shape == 3) d = sdPlus       (p, 0.42, 0.08);
                else                 d = sdRing       (p, 0.40, 0.035);

                // Convert signed distance to alpha using smooth edge.
                float alpha = 1.0 - smoothstep(0.0, _Softness, d);

                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}

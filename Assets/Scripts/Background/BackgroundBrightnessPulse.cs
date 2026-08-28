using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Whole-surface brightness pulse for a backdrop: slowly dims and
    /// restores the renderer it sits on (±a few percent over ~20 s), so a
    /// static background image never reads as a frozen still. Complements
    /// <see cref="BackgroundBreathingGlow"/> — that one is a localized
    /// additive hotspot; this one modulates the entire surface.
    ///
    /// Attach to whatever renders the backdrop:
    ///   • SpriteRenderer — tints the sprite color (the NewBackGround use),
    ///   • MeshRenderer   — tints the material's _Color / _Tint / _TintColor
    ///     on a per-object material instance.
    ///
    /// The pulse only ever DARKENS from the captured base color (colors
    /// can't exceed 1), swinging between full brightness and (1 - amount).
    /// Two golden-ratio-paced sines, phases accumulated in C#, so it never
    /// visibly repeats and Inspector tweaks never pop — the same technique
    /// every breathing effect in this project uses.
    /// </summary>
    public class BackgroundBrightnessPulse : MonoBehaviour
    {
        [Range(0f, 0.3f)]
        [Tooltip("Maximum dimming as a fraction of the base brightness (0.06 = the " +
                 "surface breathes between 100% and 94%).")]
        public float amount = 0.06f;

        [Tooltip("Seconds per pulse cycle.")]
        public float cycleSeconds = 20f;

        private SpriteRenderer spriteRenderer;
        private Material meshMaterial;    // instance created for the mesh path only
        private string meshColorProp;
        private Color baseColor = Color.white;
        private float phase;
        private float phase2;

        void Start()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                baseColor = spriteRenderer.color;
                return;
            }

            var meshRenderer = GetComponent<Renderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                // Instance the material so pulsing one backdrop doesn't pulse
                // every object sharing it.
                meshMaterial = meshRenderer.material;
                meshColorProp = meshMaterial.HasProperty("_Color")     ? "_Color"
                              : meshMaterial.HasProperty("_Tint")      ? "_Tint"
                              : meshMaterial.HasProperty("_TintColor") ? "_TintColor"
                              : null;
                if (meshColorProp != null)
                {
                    baseColor = meshMaterial.GetColor(meshColorProp);
                    return;
                }
                Debug.LogWarning($"[BackgroundBrightnessPulse] '{name}' material has no " +
                                 "color property — pulse disabled.");
            }
            else
            {
                Debug.LogWarning($"[BackgroundBrightnessPulse] '{name}' has no SpriteRenderer " +
                                 "or MeshRenderer — pulse disabled.");
            }
            enabled = false;
        }

        void Update()
        {
            float w = (2f * Mathf.PI) / Mathf.Max(cycleSeconds, 0.5f);
            phase  += Time.deltaTime * w;
            phase2 += Time.deltaTime * w * 0.618f; // incommensurate — never loops

            // Signal in [-1, 1], mapped to a multiplier in [1 - amount, 1].
            float s = 0.72f * Mathf.Sin(phase) + 0.28f * Mathf.Sin(phase2);
            float m = 1f - amount * 0.5f * (1f + s);

            Color c = baseColor;
            c.r *= m; c.g *= m; c.b *= m; // alpha untouched

            if (spriteRenderer != null) spriteRenderer.color = c;
            else if (meshMaterial != null) meshMaterial.SetColor(meshColorProp, c);
        }

        void OnDisable()
        {
            // Hand the surface back at full base brightness (matters when the
            // recording modes toggle objects, or the user disables the pulse).
            if (spriteRenderer != null) spriteRenderer.color = baseColor;
            else if (meshMaterial != null && meshColorProp != null)
                meshMaterial.SetColor(meshColorProp, baseColor);
        }

        void OnDestroy()
        {
            if (meshMaterial != null)
            {
                if (Application.isPlaying) Destroy(meshMaterial);
                else DestroyImmediate(meshMaterial);
            }
        }
    }
}

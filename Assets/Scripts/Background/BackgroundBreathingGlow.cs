using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// A soft additive glow blob that "breathes" — its brightness swells and
    /// dims on a slow two-sine cycle — placed over a backdrop's hotspot to
    /// keep an otherwise-static gradient feeling alive. Not trackable: no
    /// movement, just a gentle luminance pulse.
    ///
    /// Self-building (same pattern as the sibling background pieces): drop
    /// this component on an empty GameObject positioned where the glow
    /// should sit (e.g. over the bright violet area of the NewBackGround
    /// backdrop) and it creates its quad + material + texture in Start.
    /// The transform's position/z picks where and in front of what it draws;
    /// depth does the sorting, as everywhere else.
    ///
    /// The two sine phases run at a golden-ratio pace of each other and are
    /// accumulated in C#, so the breathing never visibly repeats and cycle
    /// tweaks in the Inspector never pop (the LateNightDesk technique).
    /// </summary>
    public class BackgroundBreathingGlow : MonoBehaviour
    {
        [Header("Glow")]
        [Tooltip("Glow tint. Alpha is ignored — brightness comes from intensity.")]
        public Color glowColor = new Color(0.72f, 0.62f, 0.95f, 1f); // soft violet-white
        [Tooltip("Diameter of the glow quad in world units.")]
        public float size = 9f;
        [Range(0f, 0.5f)]
        [Tooltip("Base additive strength — keep low; this is an ambience pulse, not a lamp.")]
        public float intensity = 0.07f;

        [Header("Breathing")]
        [Tooltip("Seconds per breathing cycle.")]
        public float breatheCycleSeconds = 25f;
        [Range(0f, 1f)]
        [Tooltip("Swing ± as a fraction of intensity (0.35 = ±35%).")]
        public float breatheAmplitude = 0.35f;

        private Material glowMaterial;
        private Texture2D glowTexture;
        private Transform glowQuad;
        private bool usesTintColor;
        private float breathePhase;
        private float breathePhase2;

        void Start()
        {
            EnsureBuilt();
        }

        /// <summary>Builds the quad + material once. Safe to call repeatedly.</summary>
        public void EnsureBuilt()
        {
            if (glowQuad != null) return;

            glowTexture = MakeSoftGaussianTexture(128, 0.26f);
            glowMaterial = CreateAdditiveMaterial();
            glowMaterial.mainTexture = glowTexture;
            usesTintColor = glowMaterial.HasProperty("_TintColor");

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BreathingGlowQuad";
            quad.transform.SetParent(transform, false);
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyRuntimeObject(col);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial    = glowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            glowQuad = quad.transform;
        }

        void Update()
        {
            if (glowQuad == null || glowMaterial == null) return;

            float w = (2f * Mathf.PI) / Mathf.Max(breatheCycleSeconds, 0.5f);
            breathePhase  += Time.deltaTime * w;
            breathePhase2 += Time.deltaTime * w * 0.618f; // incommensurate — never loops
            float breathe = 1f + breatheAmplitude * (0.72f * Mathf.Sin(breathePhase)
                                                  + 0.28f * Mathf.Sin(breathePhase2));

            glowQuad.localScale = Vector3.one * Mathf.Max(0.01f, size);
            Color c = glowColor;
            c.a = Mathf.Clamp01(intensity * breathe);
            if (usesTintColor) glowMaterial.SetColor("_TintColor", c);
            else glowMaterial.color = c;
        }

        void OnDestroy()
        {
            DestroyRuntimeObject(glowMaterial);
            DestroyRuntimeObject(glowTexture);
        }

        static void DestroyRuntimeObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // Additive so the pulse only ever brightens the backdrop beneath it.
        // Same fallback chain as the sibling backgrounds.
        static Material CreateAdditiveMaterial()
        {
            string[] candidates =
            {
                "Legacy Shaders/Particles/Additive",
                "Particles/Additive",
                "Legacy Shaders/Particles/Alpha Blended",
                "Sprites/Default",
                "Unlit/Transparent",
            };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                {
                    return new Material(shader)
                    {
                        name = "BackgroundBreathingGlowMat",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }
            Debug.LogWarning("[BackgroundBreathingGlow] No compatible shader found.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        static Texture2D MakeSoftGaussianTexture(int size, float sigmaFrac)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float sigma = Mathf.Max(1f, sigmaFrac * size);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d2 = (new Vector2(x + 0.5f, y + 0.5f) - c).sqrMagnitude;
                float a = Mathf.Exp(-d2 / (2f * sigma * sigma));
                a = Mathf.Clamp01((a - 0.01f) / 0.99f); // truly transparent edge
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}

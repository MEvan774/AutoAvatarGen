using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Configures a <see cref="ParticleSystem"/> to produce the scrolling shape
    /// background layer: a continuous diagonal flow of small faint geometric
    /// shapes (circles, dashes, pills, plus signs, rings) at 2–7% opacity.
    ///
    /// Attach this to a GameObject. A ParticleSystem + ParticleSystemRenderer
    /// are auto-added via RequireComponent. All modules are configured in
    /// Awake from the Inspector values, and the mood API updates them live.
    ///
    /// Same public API as the mesh-pool version, so BackgroundMoodController
    /// works without changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem), typeof(ParticleSystemRenderer))]
    public class ScrollingShapeController : MonoBehaviour
    {
        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Camera (for screen-size calculations)")]
        [Tooltip("Leave empty to auto-use Camera.main.")]
        public Camera referenceCamera;

        [Header("Sorting")]
        public string sortingLayerName = "Default";
        [Tooltip("Order in Layer — between background and floating objects (e.g. -50).")]
        public int sortingOrder = -50;

        [Header("Scroll Direction & Speed")]
        [Range(0f, 360f)]
        [Tooltip("Scroll direction in degrees. 35° = bottom-left → top-right.")]
        public float scrollAngleDeg = 35f;

        [Tooltip("Base scroll speed as fraction of SCREEN HEIGHT per second.")]
        public float scrollSpeed = 0.04f;

        [Range(0f, 0.5f)]
        [Tooltip("Per-particle speed variation ±. 0.3 = 0.7x–1.3x base speed.")]
        public float speedVariation = 0.3f;

        [Header("Shape Appearance")]
        public Color shapeTint = new Color(0.85f, 0.83f, 0.94f, 1f);
        [Range(0f, 0.15f)] public float opacityMin = 0.02f;
        [Range(0f, 0.15f)] public float opacityMax = 0.07f;
        [Tooltip("Shape size as fraction of SCREEN HEIGHT.")]
        [Range(0.002f, 0.04f)] public float shapeSizeMin = 0.006f;
        [Range(0.002f, 0.04f)] public float shapeSizeMax = 0.012f;

        [Header("Density")]
        [Range(10, 120)]
        [Tooltip("Target number of shapes visible at any time.")]
        public int shapeCount = 40;

        [Header("Mood Overrides (driven by BackgroundMoodController)")]
        [Range(0f, 3f)] public float speedMultiplier   = 1f;
        [Range(0f, 3f)] public float opacityMultiplier = 1f;
        [Range(0f, 3f)] public float densityMultiplier = 1f;

        [Header("Material (optional)")]
        [Tooltip("Material used by the ParticleSystemRenderer. If empty, a default 'Sprites/Default' material is created at runtime.")]
        public Material particleMaterial;

        // -------------------------------------------------------------------
        // Private
        // -------------------------------------------------------------------

        private ParticleSystem ps;
        private ParticleSystemRenderer psr;
        private List<Sprite> shapeSprites;
        private Texture2D combinedTexture; // kept alive so GC doesn't destroy our sprite textures

        // -------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------

        void OnEnable()
        {
            // Runs in both Edit mode (via [ExecuteAlways]) and Play mode.
            ps = GetComponent<ParticleSystem>();
            if (ps == null) ps = gameObject.AddComponent<ParticleSystem>();
            psr = GetComponent<ParticleSystemRenderer>();
            if (psr == null) psr = gameObject.AddComponent<ParticleSystemRenderer>();

            if (referenceCamera == null) referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                // Edit mode sometimes has no Camera.main — grab any camera in the scene as fallback.
                var cams = FindObjectsOfType<Camera>();
                if (cams.Length > 0) referenceCamera = cams[0];
            }

            shapeSprites = BuildShapeSprites();
            ConfigureParticleSystem();
        }

        void OnValidate()
        {
            if (ps != null && isActiveAndEnabled) ApplyLiveParams();
        }

        void Update()
        {
            if (ps == null) return;
            ApplyLiveParams();
        }

        // -------------------------------------------------------------------
        // Public mood API (unchanged — BackgroundMoodController calls these)
        // -------------------------------------------------------------------

        public void SetSpeedMultiplier(float m)   { speedMultiplier   = Mathf.Max(0f, m); }
        public void SetOpacityMultiplier(float m) { opacityMultiplier = Mathf.Max(0f, m); }
        public void SetDensityMultiplier(float m) { densityMultiplier = Mathf.Clamp(m, 0f, 3f); }

        [ContextMenu("Respawn All Shapes")]
        public void RespawnAll()
        {
            if (ps == null) return;
            ps.Clear(true);
            ps.Play(true);
        }

        // -------------------------------------------------------------------
        // Configuration
        // -------------------------------------------------------------------

        private void ConfigureParticleSystem()
        {
            // Stop the system before changing time-related params (duration, lifetime, prewarm).
            // Unity disallows changing those while the PS is running.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // ---- Renderer ----
            // Reject materials using the custom SDF shader — that shader was
            // designed for MeshRenderer, not particle rendering.
            bool isIncompatibleSDFShader =
                particleMaterial != null && particleMaterial.shader != null &&
                particleMaterial.shader.name == "Custom/ScrollingShapeLayer";

            if (particleMaterial == null || particleMaterial.shader == null || isIncompatibleSDFShader)
            {
                if (isIncompatibleSDFShader)
                    Debug.LogWarning(
                        "[ScrollingShapeController] Assigned Particle Material uses the " +
                        "'Custom/ScrollingShapeLayer' shader which doesn't work with particle " +
                        "systems. Replacing with default particle material.");
                particleMaterial = CreateDefaultParticleMaterial();
            }
            psr.sharedMaterial = particleMaterial;
            psr.sortingLayerName = sortingLayerName;
            psr.sortingOrder = sortingOrder;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;

            float worldH = SafeWorldHeight();
            float lifetime = SafeTraversalSeconds(worldH);

            // ---- Main module ----
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;                  // we call Play() manually at the end
            main.prewarm = true;                       // pre-fill the screen at Start
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startRotation3D = false;
            main.duration = Mathf.Max(1f, lifetime);
            main.maxParticles = Mathf.Max(200, shapeCount * 4);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime);
            main.startSpeed = 0f; // velocity comes from Velocity over Lifetime
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, shapeSizeMin * worldH),
                Mathf.Max(0.0001f, shapeSizeMax * worldH));
            main.startColor = new ParticleSystem.MinMaxGradient(
                WithAlpha(shapeTint, opacityMin * opacityMultiplier),
                WithAlpha(shapeTint, opacityMax * opacityMultiplier));

            // ---- Emission ----
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, (shapeCount * densityMultiplier) / lifetime);

            // ---- Shape: 2D rectangle covering the full camera rect ----
            // Rectangle is a pure 2D shape — no z-depth → no NaN sort distances.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            Rect bounds = GetWorldBounds(0f);
            shape.position = new Vector3(
                bounds.center.x - transform.position.x,
                bounds.center.y - transform.position.y,
                0f);
            shape.scale = new Vector3(
                Mathf.Max(0.01f, bounds.width),
                Mathf.Max(0.01f, bounds.height),
                1f);
            shape.rotation = Vector3.zero;

            // ---- Velocity over Lifetime ----
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            ApplyVelocity(worldH);

            // ---- Texture Sheet Animation ----
            if (shapeSprites != null && shapeSprites.Count > 0)
            {
                var tex = ps.textureSheetAnimation;
                tex.enabled = true;
                tex.mode = ParticleSystemAnimationMode.Sprites;
                while (tex.spriteCount > 0) tex.RemoveSprite(tex.spriteCount - 1);
                foreach (var s in shapeSprites) tex.AddSprite(s);
                tex.startFrame = new ParticleSystem.MinMaxCurve(0f, shapeSprites.Count - 0.001f);
                tex.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            }

            // Disable modules we don't want (module props return structs by value → copy first).
            var colMod   = ps.colorOverLifetime;        colMod.enabled   = false;
            var szMod    = ps.sizeOverLifetime;         szMod.enabled    = false;
            var rotMod   = ps.rotationOverLifetime;     rotMod.enabled   = false;
            var forceMod = ps.forceOverLifetime;        forceMod.enabled = false;
            var noiseMod = ps.noise;                    noiseMod.enabled = false;
            var limMod   = ps.limitVelocityOverLifetime; limMod.enabled  = false;

            // Now start running.
            ps.Play(true);
        }

        private void ApplyVelocity(float worldH)
        {
            if (ps == null) return;
            float rad = scrollAngleDeg * Mathf.Deg2Rad;
            float v = Mathf.Max(0f, scrollSpeed) * Mathf.Max(0f, speedMultiplier) * worldH;
            float sv = Mathf.Clamp(speedVariation, 0f, 0.95f);
            var vel = ps.velocityOverLifetime;
            // All three curves MUST use the same MinMaxCurveMode (TwoConstants here)
            // or Unity throws "Particle Velocity curves must all be in the same mode".
            vel.x = new ParticleSystem.MinMaxCurve(Mathf.Cos(rad) * v * (1f - sv),
                                                    Mathf.Cos(rad) * v * (1f + sv));
            vel.y = new ParticleSystem.MinMaxCurve(Mathf.Sin(rad) * v * (1f - sv),
                                                    Mathf.Sin(rad) * v * (1f + sv));
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        /// <summary>
        /// Recompute the subset of values Unity allows changing while the PS is running:
        /// start color (new particles), start size (new particles), emission rate,
        /// velocity, sorting. Time-related params (duration, lifetime, prewarm, shape
        /// bounds) are only set once in ConfigureParticleSystem.
        /// </summary>
        private void ApplyLiveParams()
        {
            if (referenceCamera == null || ps == null) return;

            float worldH = SafeWorldHeight();

            // Main: color + start size (affects new particles only)
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                WithAlpha(shapeTint, opacityMin * opacityMultiplier),
                WithAlpha(shapeTint, opacityMax * opacityMultiplier));
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, shapeSizeMin * worldH),
                Mathf.Max(0.0001f, shapeSizeMax * worldH));

            // Emission rate (safe to change live)
            var emission = ps.emission;
            float lifetime = Mathf.Max(0.1f, SafeTraversalSeconds(worldH));
            emission.rateOverTime = Mathf.Max(0f, (shapeCount * densityMultiplier) / lifetime);

            // Velocity (safe to change live)
            ApplyVelocity(worldH);

            // Renderer sorting (safe)
            psr.sortingLayerName = sortingLayerName;
            psr.sortingOrder = sortingOrder;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        private float SafeTraversalSeconds(float worldH)
        {
            float worldW = Mathf.Max(0.01f, worldH * SafeAspect());
            float diag = Mathf.Sqrt(worldW * worldW + worldH * worldH);
            float v = Mathf.Max(0.0001f, scrollSpeed) * worldH;
            float t = diag / v;
            return float.IsFinite(t) ? Mathf.Clamp(t, 1f, 600f) : 30f;
        }

        private float SafeWorldHeight()
        {
            if (referenceCamera == null) return 10f;
            float h;
            if (referenceCamera.orthographic)
            {
                h = referenceCamera.orthographicSize * 2f;
            }
            else
            {
                float dz = Mathf.Abs(referenceCamera.transform.position.z - transform.position.z);
                if (dz < 0.01f) dz = 10f;
                h = 2f * Mathf.Tan(referenceCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * dz;
            }
            return (float.IsFinite(h) && h > 0.01f) ? h : 10f;
        }

        private float SafeAspect()
        {
            if (referenceCamera == null) return 16f / 9f;
            float a = referenceCamera.aspect;
            return (float.IsFinite(a) && a > 0.01f) ? a : 16f / 9f;
        }

        private Rect GetWorldBounds(float margin)
        {
            if (referenceCamera == null) return new Rect(-10, -10, 20, 20);
            Vector3 camPos = referenceCamera.transform.position;
            float h = SafeWorldHeight();
            float w = h * SafeAspect();
            float mX = w * margin;
            float mY = h * margin;
            return new Rect(camPos.x - w * 0.5f - mX,
                            camPos.y - h * 0.5f - mY,
                            w + 2f * mX,
                            h + 2f * mY);
        }

        // -------------------------------------------------------------------
        // Default particle material (tries multiple shaders for compatibility)
        // -------------------------------------------------------------------

        private static Material CreateDefaultParticleMaterial()
        {
            string[] candidates =
            {
                "Legacy Shaders/Particles/Alpha Blended",
                "Particles/Alpha Blended",
                "Legacy Shaders/Particles/Alpha Blended Premultiply",
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
                        name = "ScrollingShapesDefault",
                        hideFlags = HideFlags.HideAndDontSave, // survive edit-mode GC
                    };
                }
            }
            Debug.LogWarning("[ScrollingShapeController] No compatible particle shader found. " +
                             "Particles may render magenta. Assign a custom material in the Inspector.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // -------------------------------------------------------------------
        // Procedural sprite generation (5 shape types)
        // -------------------------------------------------------------------

        private List<Sprite> BuildShapeSprites()
        {
            var list = new List<Sprite>();
            list.Add(WrapSprite(MakeCircleSprite(64, 0.45f)));                              // 0: dot
            list.Add(WrapSprite(MakeCapsuleSprite(128, 64, 0.45f, 0.08f)));                  // 1: dash
            list.Add(WrapSprite(MakeRoundedRectSprite(128, 64, 0.45f, 0.18f, 0.17f)));       // 2: pill
            list.Add(WrapSprite(MakePlusSprite(64, 0.45f, 0.1f)));                           // 3: plus
            list.Add(WrapSprite(MakeRingSprite(64, 0.4f, 0.035f)));                          // 4: ring
            return list;
        }

        private static Sprite MakeCircleSprite(int size, float r)
        {
            Texture2D t = NewTex(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float rPx = r * size;
            var px = t.GetPixels();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(rPx - d + 0.5f));
            }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeCapsuleSprite(int w, int h, float halfLenN, float thickN)
        {
            Texture2D t = NewTex(w, h);
            float halfLen = halfLenN * w;
            float thick = thickN * h;
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
            var px = t.GetPixels();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c;
                p.x -= Mathf.Clamp(p.x, -halfLen, halfLen);
                float d = p.magnitude - thick;
                px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(-d + 0.5f));
            }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeRoundedRectSprite(int w, int h, float halfWN, float halfHN, float rN)
        {
            Texture2D t = NewTex(w, h);
            float halfW = halfWN * w, halfH = halfHN * h, r = rN * h;
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
            var px = t.GetPixels();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c;
                Vector2 d = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(halfW - r, halfH - r);
                float dd = Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - r;
                px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(-dd + 0.5f));
            }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakePlusSprite(int size, float armLenN, float armThickN)
        {
            Texture2D t = NewTex(size, size);
            float armLen = armLenN * size, armThick = armThickN * size;
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            var px = t.GetPixels();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c;
                float dH = Box(p, armLen, armThick);
                float dV = Box(p, armThick, armLen);
                float d = Mathf.Min(dH, dV);
                px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(-d + 0.5f));
            }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeRingSprite(int size, float rN, float thickN)
        {
            Texture2D t = NewTex(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float r = rN * size, thick = thickN * size;
            var px = t.GetPixels();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float d = Mathf.Abs(dist - r) - thick;
                px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(-d + 0.5f));
            }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave, // survive edit-mode GC
            };
            var clear = new Color[w * h];
            for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
            t.SetPixels(clear);
            return t;
        }

        private static Sprite WrapSprite(Sprite s)
        {
            if (s != null) s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static float Box(Vector2 p, float halfW, float halfH)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x) - halfW, Mathf.Abs(p.y) - halfH);
            return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }
    }
}

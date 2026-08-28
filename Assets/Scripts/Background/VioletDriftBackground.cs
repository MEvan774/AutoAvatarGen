using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// "VioletDrift" background — the abstract blue-violet gradient artwork
    /// (Assets/Art/BackGround.png) as a static, frame-covering base with two
    /// gentle particle layers in front:
    ///
    ///   1. Dust motes — tiny pale specks drifting very slowly upward with a
    ///      slight noise wander.
    ///   2. Floating shapes — faint geometric shapes (circle / square /
    ///      triangle / diamond / star, the same family the menu's floating
    ///      shapes use — the atlas drawing is copied from
    ///      FloatingShapeSprites so they match pixel-for-pixel) that DRIFT IN
    ///      PLACE: no net travel, just a slow noise wander and a lazy spin,
    ///      each fading in, lingering 30–60 s, and fading out.
    ///
    /// EDIT-MODE VISIBLE: unlike the other backdrops, the artwork quad is
    /// AUTHORED in the prefab (Assets/Prefabs/VioletDriftBackground.prefab —
    /// child "Artwork" with the VioletDriftArt material), so the background
    /// shows in the Scene view without entering play mode. This component is
    /// [ExecuteAlways], so the particle layers exist in edit mode too; the
    /// children it builds are marked DontSave and are torn down in OnDisable,
    /// so they never dirty the prefab or scene.
    ///
    /// Standalone by design (like CozyDeskNight): not an IAnimatedBackground
    /// / Background Style entry, no mood presets — that wiring is a separate
    /// later step if wanted.
    /// </summary>
    [ExecuteAlways]
    public class VioletDriftBackground : MonoBehaviour
    {
        /// <summary>Root-object name, following the sibling backdrops' pattern.</summary>
        public const string RootObjectName = "VioletDriftBackground";

        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Scene Hookup")]
        [Tooltip("Leave empty to auto-use Camera.main (any camera as fallback).")]
        public Camera referenceCamera;

        [Tooltip("Local z of both particle layers — just in front of the authored " +
                 "Artwork quad (z 9); depth handles the sorting, as everywhere else.")]
        public float particleDepth = 8.5f;

        [Header("Dust Motes")]
        [Range(5, 80)]
        [Tooltip("Target number of motes visible at any time.")]
        public int dustCount = 24;
        [Tooltip("Mote size as fraction of SCREEN HEIGHT (tiny).")]
        [Range(0.001f, 0.02f)] public float dustSizeMin = 0.003f;
        [Range(0.001f, 0.02f)] public float dustSizeMax = 0.008f;
        [Range(0f, 0.3f)] public float dustAlphaMin = 0.03f;
        [Range(0f, 0.3f)] public float dustAlphaMax = 0.10f;
        public Color dustTint = new Color(0.839f, 0.824f, 0.933f, 1f); // pale lavender
        [Tooltip("Upward drift as fraction of SCREEN HEIGHT per second — very slow.")]
        public float dustRiseSpeed = 0.008f;
        [Tooltip("Noise wander strength in world units/s.")]
        public float dustWander = 0.05f;
        public Vector2 dustLifetimeRange = new Vector2(20f, 40f);

        [Header("Floating Shapes (drift in place)")]
        [Range(2, 40)]
        [Tooltip("Target number of shapes visible at any time.")]
        public int shapeCount = 14;
        [Tooltip("Shape size as fraction of SCREEN HEIGHT.")]
        [Range(0.005f, 0.15f)] public float shapeSizeMin = 0.020f;
        [Range(0.005f, 0.15f)] public float shapeSizeMax = 0.060f;
        [Range(0f, 0.3f)] public float shapeAlphaMin = 0.04f;
        [Range(0f, 0.3f)] public float shapeAlphaMax = 0.10f;
        [Tooltip("Random pick along this gradient — pale blue-violet tones from the artwork.")]
        public Gradient shapePalette = DefaultShapePalette();
        [Tooltip("Noise wander strength in world units/s — the 'drift in place' motion. " +
                 "No velocity is ever applied, so shapes never march across the frame.")]
        public float shapeWander = 0.12f;
        [Tooltip("Noise frequency — lower = broader, lazier wandering arcs.")]
        public float shapeWanderFrequency = 0.06f;
        [Tooltip("Max spin speed, degrees/second (each shape rolls its own within ±).")]
        [Range(0f, 45f)] public float shapeSpinDegPerSec = 6f;
        public Vector2 shapeLifetimeRange = new Vector2(30f, 60f);

        // -------------------------------------------------------------------
        // Runtime state (never serialized — rebuilt in OnEnable)
        // -------------------------------------------------------------------

        private ParticleSystem dustPs;
        private ParticleSystem shapesPs;
        private Material dustMaterial;
        private Material shapesMaterial;
        private Texture2D dustTexture;
        private Texture2D shapeAtlas;
        private readonly List<Sprite> shapeSprites = new List<Sprite>();

        private const string DustChildName   = "VioletDriftDust";
        private const string ShapesChildName = "VioletDriftShapes";

        // -------------------------------------------------------------------
        // Unity lifecycle — ExecuteAlways: build on enable, tear down on
        // disable, so edit mode, play mode and domain reloads all stay clean.
        // -------------------------------------------------------------------

        void OnEnable()
        {
            EnsureBuilt();
        }

        void OnDisable()
        {
            TearDown();
        }

        void Update()
        {
            // Re-assert the live-safe params every tick (same discipline as
            // the sibling backgrounds) so Inspector tweaks apply immediately —
            // in edit mode too.
            if (dustPs != null) ApplyDustLiveParams();
            if (shapesPs != null) ApplyShapesLiveParams();
        }

        // -------------------------------------------------------------------
        // Build / teardown
        // -------------------------------------------------------------------

        /// <summary>Builds both particle layers. Safe to call repeatedly.</summary>
        public void EnsureBuilt()
        {
            if (dustPs != null && shapesPs != null) return;

            if (referenceCamera == null) referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                var cams = FindObjectsOfType<Camera>();
                if (cams.Length > 0) referenceCamera = cams[0];
            }

            // A domain reload nulls our fields but can leave last session's
            // DontSave children in the hierarchy — clear them and start fresh.
            DestroyStaleChildren();

            dustTexture = MakeSoftCircleTexture(64);
            dustMaterial = CreateParticleMaterial("VioletDriftDustMat");
            dustMaterial.mainTexture = dustTexture;
            BuildDustSystem();

            BuildShapeSpriteAtlas();
            shapesMaterial = CreateParticleMaterial("VioletDriftShapesMat");
            BuildShapesSystem();
        }

        void TearDown()
        {
            if (dustPs != null)   DestroyRuntimeObject(dustPs.gameObject);
            if (shapesPs != null) DestroyRuntimeObject(shapesPs.gameObject);
            dustPs = null;
            shapesPs = null;
            DestroyRuntimeObject(dustMaterial);   dustMaterial = null;
            DestroyRuntimeObject(shapesMaterial); shapesMaterial = null;
            DestroyRuntimeObject(dustTexture);    dustTexture = null;
            foreach (var s in shapeSprites) DestroyRuntimeObject(s);
            shapeSprites.Clear();
            DestroyRuntimeObject(shapeAtlas);     shapeAtlas = null;
        }

        void DestroyStaleChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == DustChildName || child.name == ShapesChildName)
                    DestroyRuntimeObject(child.gameObject);
            }
        }

        static void DestroyRuntimeObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject NewLayerChild(string childName)
        {
            var go = new GameObject(childName)
            {
                // Visible + inspectable in the hierarchy while tweaking, but
                // never serialized into the prefab or scene.
                hideFlags = HideFlags.DontSave,
            };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, particleDepth);
            return go;
        }

        static void ConfigureRenderer(ParticleSystemRenderer psr, Material mat)
        {
            psr.sharedMaterial = mat;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment  = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }

        // Shared fade-in/hold/fade-out alpha envelope so nothing ever pops.
        static ParticleSystem.MinMaxGradient FadeEnvelope()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f),
                        new GradientAlphaKey(1f, 0.90f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        void BuildDustSystem()
        {
            var go = NewLayerChild(DustChildName);
            dustPs = go.AddComponent<ParticleSystem>();
            dustPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>(), dustMaterial);

            float worldH = SafeWorldHeight();

            var main = dustPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = dustLifetimeRange.y;
            main.startLifetime = new ParticleSystem.MinMaxCurve(dustLifetimeRange.x, dustLifetimeRange.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, dustSizeMin * worldH),
                Mathf.Max(0.0001f, dustSizeMax * worldH));
            main.startColor = new ParticleSystem.MinMaxGradient(
                WithAlpha(dustTint, dustAlphaMin), WithAlpha(dustTint, dustAlphaMax));
            main.maxParticles = Mathf.Max(80, dustCount * 4);

            var emission = dustPs.emission;
            emission.enabled = true;
            emission.rateOverTime = dustCount / Mathf.Max(1f, (dustLifetimeRange.x + dustLifetimeRange.y) * 0.5f);

            var shape = dustPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            ApplyShapeBounds(dustPs, 0.1f);

            var vel = dustPs.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            ApplyDustVelocity(worldH);

            var noise = dustPs.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(dustWander);
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.03f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var col = dustPs.colorOverLifetime;
            col.enabled = true;
            col.color = FadeEnvelope();

            dustPs.Play(true);
        }

        void BuildShapesSystem()
        {
            var go = NewLayerChild(ShapesChildName);
            shapesPs = go.AddComponent<ParticleSystem>();
            shapesPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>(), shapesMaterial);

            float worldH = SafeWorldHeight();

            var main = shapesPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = shapeLifetimeRange.y;
            main.startLifetime = new ParticleSystem.MinMaxCurve(shapeLifetimeRange.x, shapeLifetimeRange.y);
            main.startSpeed = 0f;   // drift-in-place: the noise module is the ONLY motion
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, shapeSizeMin * worldH),
                Mathf.Max(0.0001f, shapeSizeMax * worldH));
            main.startColor = BuildShapeStartColor();
            main.maxParticles = Mathf.Max(40, shapeCount * 4);

            var emission = shapesPs.emission;
            emission.enabled = true;
            emission.rateOverTime = shapeCount / Mathf.Max(1f, (shapeLifetimeRange.x + shapeLifetimeRange.y) * 0.5f);

            var shape = shapesPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            ApplyShapeBounds(shapesPs, 0.05f);

            var noise = shapesPs.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(shapeWander);
            noise.frequency = shapeWanderFrequency;
            noise.scrollSpeed = 0.015f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            float spin = shapeSpinDegPerSec * Mathf.Deg2Rad;
            var rot = shapesPs.rotationOverLifetime;
            rot.enabled = spin > 0f;
            rot.z = new ParticleSystem.MinMaxCurve(-spin, spin);

            var col = shapesPs.colorOverLifetime;
            col.enabled = true;
            col.color = FadeEnvelope();

            // The 5-shape atlas — each particle picks a random frame.
            var tex = shapesPs.textureSheetAnimation;
            tex.enabled = true;
            tex.mode = ParticleSystemAnimationMode.Sprites;
            while (tex.spriteCount > 0) tex.RemoveSprite(tex.spriteCount - 1);
            foreach (var s in shapeSprites) tex.AddSprite(s);
            tex.startFrame = new ParticleSystem.MinMaxCurve(0f, shapeSprites.Count - 0.001f);
            tex.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            shapesPs.Play(true);
        }

        // -------------------------------------------------------------------
        // Live params (safe to change while running)
        // -------------------------------------------------------------------

        void ApplyDustLiveParams()
        {
            float worldH = SafeWorldHeight();
            var main = dustPs.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                WithAlpha(dustTint, dustAlphaMin), WithAlpha(dustTint, dustAlphaMax));
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, dustSizeMin * worldH),
                Mathf.Max(0.0001f, dustSizeMax * worldH));
            var emission = dustPs.emission;
            emission.rateOverTime = dustCount / Mathf.Max(1f, (dustLifetimeRange.x + dustLifetimeRange.y) * 0.5f);
            ApplyDustVelocity(worldH);
            var noise = dustPs.noise;
            noise.strength = new ParticleSystem.MinMaxCurve(dustWander);
        }

        void ApplyShapesLiveParams()
        {
            float worldH = SafeWorldHeight();
            var main = shapesPs.main;
            main.startColor = BuildShapeStartColor();
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, shapeSizeMin * worldH),
                Mathf.Max(0.0001f, shapeSizeMax * worldH));
            var emission = shapesPs.emission;
            emission.rateOverTime = shapeCount / Mathf.Max(1f, (shapeLifetimeRange.x + shapeLifetimeRange.y) * 0.5f);
            var noise = shapesPs.noise;
            noise.strength = new ParticleSystem.MinMaxCurve(shapeWander);
            noise.frequency = shapeWanderFrequency;
            float spin = shapeSpinDegPerSec * Mathf.Deg2Rad;
            var rot = shapesPs.rotationOverLifetime;
            rot.enabled = spin > 0f;
            rot.z = new ParticleSystem.MinMaxCurve(-spin, spin);
        }

        void ApplyDustVelocity(float worldH)
        {
            float v = Mathf.Max(0f, dustRiseSpeed) * worldH;
            var vel = dustPs.velocityOverLifetime;
            // All curves must share one MinMaxCurveMode (TwoConstants).
            vel.x = new ParticleSystem.MinMaxCurve(-v * 0.25f, v * 0.25f);
            vel.y = new ParticleSystem.MinMaxCurve(v * 0.6f, v * 1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        void ApplyShapeBounds(ParticleSystem ps, float margin)
        {
            Rect bounds = GetWorldBounds(margin);
            var shape = ps.shape;
            shape.position = new Vector3(bounds.center.x - ps.transform.position.x,
                                         bounds.center.y - ps.transform.position.y, 0f);
            shape.scale = new Vector3(Mathf.Max(0.01f, bounds.width),
                                      Mathf.Max(0.01f, bounds.height), 1f);
        }

        // Random point on the palette gradient → a pale artwork-matched tint
        // per shape; alpha rides the same random pick between min and max.
        ParticleSystem.MinMaxGradient BuildShapeStartColor()
        {
            var g = new Gradient();
            var src = shapePalette ?? DefaultShapePalette();
            g.SetKeys(src.colorKeys, new[]
            {
                new GradientAlphaKey(shapeAlphaMin, 0f),
                new GradientAlphaKey(shapeAlphaMax, 1f),
            });
            return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        }

        static Gradient DefaultShapePalette()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.749f, 0.784f, 0.941f), 0f),    // pale blue #BFC8F0
                    new GradientColorKey(new Color(0.686f, 0.769f, 0.933f), 0.35f), // powder blue #AFC4EE
                    new GradientColorKey(new Color(0.796f, 0.749f, 0.941f), 0.7f),  // pale violet #CBBFF0
                    new GradientColorKey(new Color(0.847f, 0.824f, 0.957f), 1f),    // lavender white #D8D2F4
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // -------------------------------------------------------------------
        // Test hooks (same ContextMenu pattern as the sibling backgrounds)
        // -------------------------------------------------------------------

        [ContextMenu("Test: Respawn All Particles")]
        void TestRespawnAll()
        {
            if (dustPs != null)   { dustPs.Clear(true);   dustPs.Play(true); }
            if (shapesPs != null) { shapesPs.Clear(true); shapesPs.Play(true); }
            Debug.Log("[VioletDriftBackground] Respawned dust and shapes.");
        }

        [ContextMenu("Rebuild Layers")]
        void TestRebuild()
        {
            TearDown();
            DestroyStaleChildren();
            EnsureBuilt();
            Debug.Log("[VioletDriftBackground] Rebuilt both particle layers.");
        }

        // -------------------------------------------------------------------
        // Helpers (camera sizing copied from the sibling backgrounds)
        // -------------------------------------------------------------------

        static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        float SafeWorldHeight()
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

        float SafeAspect()
        {
            if (referenceCamera == null) return 16f / 9f;
            float a = referenceCamera.aspect;
            return (float.IsFinite(a) && a > 0.01f) ? a : 16f / 9f;
        }

        Rect GetWorldBounds(float margin)
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

        static Material CreateParticleMaterial(string matName)
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
                        name = matName,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }
            Debug.LogWarning("[VioletDriftBackground] No compatible particle shader found.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        static Texture2D MakeSoftCircleTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float sigma = 0.18f * size;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d2 = (new Vector2(x + 0.5f, y + 0.5f) - c).sqrMagnitude;
                float a = Mathf.Exp(-d2 / (2f * sigma * sigma));
                a = Mathf.Clamp01((a - 0.01f) / 0.99f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // -------------------------------------------------------------------
        // Shape atlas — the drawing below is copied from FloatingShapeSprites
        // (one shared texture, five 64px cells) so these shapes render
        // IDENTICALLY to the menu's floating shapes. Done synchronously here
        // (no coroutine) so it also completes in edit mode.
        // -------------------------------------------------------------------

        void BuildShapeSpriteAtlas()
        {
            const int cell = 64;
            const int count = 5;
            shapeAtlas = new Texture2D(cell * count, cell, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var clear = new Color[cell * count * cell];
            for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
            shapeAtlas.SetPixels(clear);

            DrawCircle(shapeAtlas,   0 * cell, cell);
            DrawSquare(shapeAtlas,   1 * cell, cell);
            DrawTriangle(shapeAtlas, 2 * cell, cell);
            DrawDiamond(shapeAtlas,  3 * cell, cell);
            DrawStar(shapeAtlas,     4 * cell, cell);
            shapeAtlas.Apply();

            string[] names = { "Circle", "Square", "Triangle", "Diamond", "Star" };
            shapeSprites.Clear();
            for (int i = 0; i < count; i++)
            {
                var sp = Sprite.Create(shapeAtlas,
                    new Rect(i * cell, 0, cell, cell),
                    new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect);
                sp.name = names[i];
                sp.hideFlags = HideFlags.HideAndDontSave;
                shapeSprites.Add(sp);
            }
        }

        static void SetPx(Texture2D atlas, int xOff, int y, int x, int cell, Color c)
        {
            atlas.SetPixel(xOff + x, y, c);
        }

        static void DrawCircle(Texture2D atlas, int xOff, int cell)
        {
            var center = new Vector2(cell * 0.5f, cell * 0.5f);
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                SetPx(atlas, xOff, y, x, cell,
                      new Color(1, 1, 1, Mathf.Clamp01(cell * 0.44f - d + .5f)));
            }
        }

        static void DrawSquare(Texture2D atlas, int xOff, int cell)
        {
            int pad = 8;
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
                SetPx(atlas, xOff, y, x, cell,
                      (x >= pad && x < cell - pad && y >= pad && y < cell - pad)
                          ? Color.white : Color.clear);
        }

        static void DrawTriangle(Texture2D atlas, int xOff, int cell)
        {
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float ny = (y - 6f) / (cell - 12f);
                float halfW = (1f - ny) * 0.5f;
                float nx = (x + 0.5f) / cell - 0.5f;
                SetPx(atlas, xOff, y, x, cell,
                      (ny >= 0f && ny <= 1f && Mathf.Abs(nx) < halfW)
                          ? Color.white : Color.clear);
            }
        }

        static void DrawDiamond(Texture2D atlas, int xOff, int cell)
        {
            var center = new Vector2(cell * 0.5f, cell * 0.5f);
            float r = cell * 0.4f;
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float dx = Mathf.Abs(x + .5f - center.x);
                float dy = Mathf.Abs(y + .5f - center.y);
                SetPx(atlas, xOff, y, x, cell,
                      (dx / r + dy / r <= 1f) ? Color.white : Color.clear);
            }
        }

        static void DrawStar(Texture2D atlas, int xOff, int cell)
        {
            var center = new Vector2(cell * 0.5f, cell * 0.5f);
            float outerR = cell * 0.44f, innerR = outerR * 0.4f;
            int pts = 5;
            var verts = new Vector2[pts * 2];
            for (int i = 0; i < pts * 2; i++)
            {
                float a = Mathf.PI * 2f * i / (pts * 2) - Mathf.PI / 2f;
                float rv = (i % 2 == 0) ? outerR : innerR;
                verts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rv;
            }
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
                SetPx(atlas, xOff, y, x, cell,
                      PtInPoly(new Vector2(x + .5f, y + .5f), verts)
                          ? Color.white : Color.clear);
        }

        static bool PtInPoly(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            int j = poly.Length - 1;
            for (int i = 0; i < poly.Length; i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
                j = i;
            }
            return inside;
        }
    }
}

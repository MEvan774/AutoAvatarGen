using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// "LateNightDesk" animated background — an abstract 2 AM home-office
    /// ambience. Three layers, all built by this one component at runtime:
    ///
    ///   1. Gradient base — a quad using the LateNightDesk/Gradient shader:
    ///      near-black navy → charcoal vertical gradient with a warm
    ///      off-screen desk-lamp glow breathing on one side of the frame.
    ///   2. Dust motes — 15–30 tiny soft particles drifting up-diagonally
    ///      with noise wander; motes nearer the lamp side render slightly
    ///      brighter ("catching the light").
    ///   3. Steam wisps — 1–2 barely-there wisps at a time rising from the
    ///      bottom corner on the lamp side, dissipating over 6–10 s with
    ///      noise-driven curl. Implied coffee; no mug, no objects.
    ///
    /// Self-contained: drop this component on an empty GameObject (name it
    /// <see cref="RootObjectName"/> so a future BackgroundModeManager hookup
    /// can toggle it by name, the way the synthwave backdrop is) and it
    /// builds the quad + two particle systems in Start. One material, two
    /// particle systems, no post-processing. NOT yet wired into any menu,
    /// scene, or mode manager — that's a later step.
    ///
    /// Layering matches the synthwave prefab: the quad sits at the same
    /// depth/scale as the synthwave Sky quad (opaque, ZWrite On, Cull Off for
    /// the mirrored recording camera), particles hang just in front of it,
    /// and everything stays far behind the presenter (~z 0) and the content
    /// zone's high-order canvases — depth does the sorting, like the
    /// BackgroundVignette comment explains.
    ///
    /// Mood handling mirrors BackgroundMoodController exactly: five presets
    /// in an Inspector list keyed by the shared MoodType enum, SetMood
    /// SmoothStep-lerps EVERY parameter over the crossfade duration (the
    /// 2–4 s MediaPresentationSystem.moodCrossfadeSeconds), ApplyMoodInstant
    /// snaps. Never hard-swaps mid-show.
    /// </summary>
    public class LateNightDeskBackground : MonoBehaviour, IAnimatedBackground
    {
        /// <summary>
        /// Root-object name a future BackgroundModeManager hookup should look
        /// for (same find-by-name pattern as SynthwaveObjectName).
        /// </summary>
        public const string RootObjectName = "LateNightDeskBackground";

        public enum LampSide { Left, Right }

        [System.Serializable]
        public class MoodSettings
        {
            public BackgroundMoodController.MoodType mood;

            [Header("Gradient Base")]
            public Color gradientTop    = new Color(0.043f, 0.059f, 0.102f, 1f);
            public Color gradientBottom = new Color(0.082f, 0.106f, 0.149f, 1f);

            [Header("Lamp Glow")]
            public Color glowColor = new Color(1f, 0.702f, 0.361f, 1f);
            [Range(0f, 2f)]   public float glowIntensity = 0.4f;
            [Tooltip("Seconds per breathing cycle (the glow's slow sine).")]
            public float breatheCycleSeconds = 25f;
            [Range(0f, 0.5f)] public float breatheAmplitude = 0.12f;

            [Header("Dust Motes (multipliers on the base values below)")]
            [Range(0f, 3f)] public float dustSpeed   = 1f;
            [Range(0f, 3f)] public float dustOpacity = 1f;
            [Range(0f, 3f)] public float dustSize    = 1f;
            [Tooltip("Scales the noise wander AND the per-particle speed spread — " +
                     "high for varied playful motion, near 0 for a frozen field.")]
            [Range(0f, 3f)] public float dustWander  = 1f;

            [Header("Steam Wisps")]
            [Tooltip("0 = wisps off (existing ones dissipate naturally).")]
            [Range(0f, 1f)] public float steamOpacity = 1f;
        }

        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Scene Hookup")]
        [Tooltip("Leave empty to auto-use Camera.main (any camera as fallback).")]
        public Camera referenceCamera;

        [Tooltip("Which side of the frame the off-screen lamp sits on, in SCENE " +
                 "space. NOTE: the recording camera mirrors the frame horizontally " +
                 "(see the Cull Off comments on the background shaders), so the " +
                 "finished video shows the glow on the OPPOSITE side.")]
        public LampSide lampSide = LampSide.Right;

        [Tooltip("Starting mood. Applied instantly (no transition) on Start.")]
        public BackgroundMoodController.MoodType startingMood =
            BackgroundMoodController.MoodType.CalmNeutral;

        [Header("Layout (matches the SynthwaveBackground Sky quad)")]
        [Tooltip("Local position of the gradient quad — same depth the synthwave Sky quad uses.")]
        public Vector3 backdropLocalPosition = new Vector3(0f, -0.8f, 9f);
        [Tooltip("Local scale of the gradient quad. Over-covers the ~17.8x10 visible " +
                 "frame (ortho size 5) so zoom-outs and the mirrored camera never show an edge.")]
        public Vector2 backdropSize = new Vector2(26f, 15f);
        [Tooltip("Local z of both particle systems — just in front of the backdrop, " +
                 "far behind the presenter; depth handles the sorting.")]
        public float particleDepth = 8f;

        [Header("Dust Motes")]
        [Range(5, 60)]
        [Tooltip("Target number of motes visible at any time (spec: 15–30).")]
        public int dustCount = 22;
        [Range(0f, 360f)]
        [Tooltip("Drift direction in degrees. 60° = up and to the right (upward-diagonal).")]
        public float dustDriftAngleDeg = 60f;
        [Tooltip("Base drift speed as fraction of SCREEN HEIGHT per second. Very slow — " +
                 "perceptible, never followable.")]
        public float dustDriftSpeed = 0.012f;
        [Range(0f, 0.9f)]
        [Tooltip("Per-particle speed spread ±. Scaled further by the mood's dustWander.")]
        public float dustSpeedVariation = 0.35f;
        public Color dustTint = new Color(0.95f, 0.93f, 0.88f, 1f);
        [Range(0f, 0.3f)] public float dustOpacityMin = 0.05f;
        [Range(0f, 0.3f)] public float dustOpacityMax = 0.14f;
        [Tooltip("Mote size as fraction of SCREEN HEIGHT (tiny: ~4–10 px at 1080p).")]
        [Range(0.001f, 0.02f)] public float dustSizeMin = 0.0035f;
        [Range(0.001f, 0.02f)] public float dustSizeMax = 0.009f;
        [Tooltip("Noise wander strength in world units/s (slight per-particle meander).")]
        public float dustNoiseStrength = 0.05f;
        [Range(0f, 1f)]
        [Tooltip("How much brighter motes get toward the lamp side (fake 'catching the light').")]
        public float dustLightBias = 0.5f;

        [Header("Steam Wisps")]
        [Tooltip("Wisps per second. 0.18 with 6–10 s lifetimes keeps 1–2 alive at a time.")]
        public float steamRatePerSecond = 0.18f;
        public Vector2 steamLifetimeRange = new Vector2(6f, 10f);
        [Tooltip("Rise speed in world units/s.")]
        public float steamRiseSpeed = 0.35f;
        [Range(0f, 0.2f)]
        [Tooltip("Peak wisp alpha — very low, barely there.")]
        public float steamPeakAlpha = 0.05f;
        [Tooltip("Noise curl strength (world units/s), mostly horizontal.")]
        public float steamNoiseStrength = 0.35f;
        [Range(0f, 0.45f)]
        [Tooltip("How far in from the lamp-side edge the wisps spawn, as fraction of frame width.")]
        public float steamCornerInset = 0.12f;
        [Tooltip("Wisp size as fraction of SCREEN HEIGHT (grows ~2.6x over its life).")]
        public Vector2 steamSizeRange = new Vector2(0.07f, 0.11f);

        [Header("Mood Presets")]
        [Tooltip("One entry per mood, same pattern as BackgroundMoodController.presets.")]
        public List<MoodSettings> presets = new List<MoodSettings>()
        {
            // Calm/Neutral — the values from the visual spec, verbatim.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.CalmNeutral,
                gradientTop = Hex("#0B0F1A"), gradientBottom = Hex("#151B26"),
                glowColor = Hex("#FFB35C"), glowIntensity = 0.40f,
                breatheCycleSeconds = 25f, breatheAmplitude = 0.12f,
                dustSpeed = 1f, dustOpacity = 1f, dustSize = 1f, dustWander = 1f,
                steamOpacity = 1f,
            },
            // Energetic — monitor light instead of lamp: ~30% brighter, cooler/
            // whiter glow; dust drift x1.75; background lifted slightly.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.Energetic,
                gradientTop = Hex("#101623"), gradientBottom = Hex("#1B2231"),
                glowColor = Hex("#D6E2F5"), glowIntensity = 0.52f,
                breatheCycleSeconds = 21f, breatheAmplitude = 0.12f,
                dustSpeed = 1.75f, dustOpacity = 1.15f, dustSize = 1f, dustWander = 1.2f,
                steamOpacity = 1f,
            },
            // Tense — red-orange glow, darker frame, slower/heavier dust,
            // slower breathing.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.TenseDramatic,
                gradientTop = Hex("#06080F"), gradientBottom = Hex("#0E1219"),
                glowColor = Hex("#FF6B3D"), glowIntensity = 0.42f,
                breatheCycleSeconds = 34f, breatheAmplitude = 0.10f,
                dustSpeed = 0.55f, dustOpacity = 1f, dustSize = 1.4f, dustWander = 0.7f,
                steamOpacity = 0.8f,
            },
            // Playful — palette warmed slightly, dust motion more varied in
            // direction and speed, glow amplitude a touch higher.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.PlayfulLight,
                gradientTop = Hex("#120F1B"), gradientBottom = Hex("#1D1927"),
                glowColor = Hex("#FFC272"), glowIntensity = 0.46f,
                breatheCycleSeconds = 24f, breatheAmplitude = 0.17f,
                dustSpeed = 1.25f, dustOpacity = 1.1f, dustSize = 1.05f, dustWander = 2.2f,
                steamOpacity = 1f,
            },
            // Minimal/Focus — glow dimmed ~40%, dust nearly frozen, steam off,
            // lowest contrast; near-static.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.MinimalFocus,
                gradientTop = Hex("#0D1118"), gradientBottom = Hex("#11151C"),
                glowColor = Hex("#FFB35C"), glowIntensity = 0.24f,
                breatheCycleSeconds = 30f, breatheAmplitude = 0.06f,
                dustSpeed = 0.05f, dustOpacity = 0.65f, dustSize = 1f, dustWander = 0.15f,
                steamOpacity = 0f,
            },
        };

        [Header("Live Mood State (lerped during transitions — debug view)")]
        [Range(0f, 3f)] public float dustSpeedMultiplier   = 1f;
        [Range(0f, 3f)] public float dustOpacityMultiplier = 1f;
        [Range(0f, 3f)] public float dustSizeMultiplier    = 1f;
        [Range(0f, 3f)] public float dustWanderMultiplier  = 1f;
        [Range(0f, 1f)] public float steamOpacityMultiplier = 1f;
        public float breatheCycleSeconds = 25f;

        // Shader property IDs (cached for speed)
        private static readonly int PropTop           = Shader.PropertyToID("_TopColor");
        private static readonly int PropBottom        = Shader.PropertyToID("_BottomColor");
        private static readonly int PropGlowColor     = Shader.PropertyToID("_GlowColor");
        private static readonly int PropGlowIntensity = Shader.PropertyToID("_GlowIntensity");
        private static readonly int PropGlowSide      = Shader.PropertyToID("_GlowSide");
        private static readonly int PropBreatheAmount = Shader.PropertyToID("_BreatheAmount");
        private static readonly int PropBreathePhase  = Shader.PropertyToID("_BreathePhase");
        private static readonly int PropBreathePhase2 = Shader.PropertyToID("_BreathePhase2");
        private static readonly int PropAspect        = Shader.PropertyToID("_Aspect");

        private BackgroundMoodController.MoodType currentMood;
        private Coroutine transitionCoroutine;

        private Material gradientMaterial;
        private ParticleSystem dustPs;
        private ParticleSystemRenderer dustPsr;
        private ParticleSystem steamPs;
        private Material particleMaterial;
        private Sprite dustSprite;
        private Sprite steamSprite;
        private ParticleSystem.Particle[] dustBuffer;

        // Breathe phases integrated on the CPU so a mood transition can lerp
        // the cycle LENGTH without the phase (and thus the glow) jumping —
        // sin(t * w) with a changing w pops; phase += dt * w doesn't. The
        // second phase runs at a golden-ratio multiple of the first so the
        // combined breathing never exactly repeats (no perceivable loop).
        private float breathePhase;
        private float breathePhase2;

        // -------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------

        void Start()
        {
            EnsureBuilt();
            ApplyMoodInstant(startingMood);
        }

        void Update()
        {
            if (gradientMaterial == null) return;

            // Advance the breathing (cycle length is a lerped mood value).
            float w = (2f * Mathf.PI) / Mathf.Max(breatheCycleSeconds, 0.5f);
            breathePhase  += Time.deltaTime * w;
            breathePhase2 += Time.deltaTime * w * 0.618f; // incommensurate — never loops
            gradientMaterial.SetFloat(PropBreathePhase,  breathePhase);
            gradientMaterial.SetFloat(PropBreathePhase2, breathePhase2);

            // Re-assert the live-safe particle params every frame (same
            // discipline as ScrollingShapeController.ApplyLiveParams — makes
            // the Inspector live-tweakable and applies the lerping multipliers).
            ApplyLiveParticleParams();
        }

        void LateUpdate()
        {
            UpdateDustLighting();
        }

        void OnDestroy()
        {
            // Runtime-created assets — same cleanup as BackgroundVignette.
            DestroyRuntimeObject(gradientMaterial);
            DestroyRuntimeObject(particleMaterial);
            if (dustSprite  != null) { DestroyRuntimeObject(dustSprite.texture);  DestroyRuntimeObject(dustSprite); }
            if (steamSprite != null) { DestroyRuntimeObject(steamSprite.texture); DestroyRuntimeObject(steamSprite); }
        }

        static void DestroyRuntimeObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // -------------------------------------------------------------------
        // IAnimatedBackground
        // -------------------------------------------------------------------

        /// <summary>Current mood (last one applied or transitioned to).</summary>
        public BackgroundMoodController.MoodType CurrentMood => currentMood;

        /// <summary>
        /// Smoothly transition to the given mood over `transitionDuration`
        /// seconds. Interrupts any in-flight transition.
        /// </summary>
        public void SetMood(BackgroundMoodController.MoodType mood, float transitionDuration = 3f)
        {
            EnsureBuilt();
            if (gradientMaterial == null) return;
            if (!isActiveAndEnabled) { ApplyMoodInstant(mood); return; } // can't run a coroutine
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            transitionCoroutine = StartCoroutine(TransitionTo(mood, Mathf.Max(0.01f, transitionDuration)));
        }

        /// <summary>Instantly snap to the given mood with no transition.</summary>
        public void ApplyMoodInstant(BackgroundMoodController.MoodType mood)
        {
            EnsureBuilt();
            // An instant apply must WIN: kill any in-flight crossfade so the
            // zombie lerp can't keep mutating the material a frame later
            // (matters when BackgroundStyleManager parks rigs on a style
            // switch). Safe from TransitionTo's own final snap.
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }
            var preset = GetPreset(mood);
            if (preset == null || gradientMaterial == null) return;
            currentMood = mood;

            gradientMaterial.SetColor(PropTop,           preset.gradientTop);
            gradientMaterial.SetColor(PropBottom,        preset.gradientBottom);
            gradientMaterial.SetColor(PropGlowColor,     preset.glowColor);
            gradientMaterial.SetFloat(PropGlowIntensity, preset.glowIntensity);
            gradientMaterial.SetFloat(PropBreatheAmount, preset.breatheAmplitude);
            breatheCycleSeconds = preset.breatheCycleSeconds;

            dustSpeedMultiplier    = preset.dustSpeed;
            dustOpacityMultiplier  = preset.dustOpacity;
            dustSizeMultiplier     = preset.dustSize;
            dustWanderMultiplier   = preset.dustWander;
            steamOpacityMultiplier = preset.steamOpacity;
            ApplyLiveParticleParams();
        }

        /// <summary>IAnimatedBackground: show/hide the whole background rig.</summary>
        public void SetActive(bool active) => gameObject.SetActive(active);

        private IEnumerator TransitionTo(BackgroundMoodController.MoodType target, float duration)
        {
            var targetPreset = GetPreset(target);
            if (targetPreset == null) yield break;
            currentMood = target;

            // Capture starting values directly from the material so we lerp
            // correctly even if someone poked the values from outside.
            Color startTop       = gradientMaterial.GetColor(PropTop);
            Color startBottom    = gradientMaterial.GetColor(PropBottom);
            Color startGlow      = gradientMaterial.GetColor(PropGlowColor);
            float startIntensity = gradientMaterial.GetFloat(PropGlowIntensity);
            float startAmplitude = gradientMaterial.GetFloat(PropBreatheAmount);
            float startCycle     = breatheCycleSeconds;

            float startDustSpeed = dustSpeedMultiplier;
            float startDustOpac  = dustOpacityMultiplier;
            float startDustSize  = dustSizeMultiplier;
            float startDustWand  = dustWanderMultiplier;
            float startSteamOpac = steamOpacityMultiplier;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration); // ease-in-out

                gradientMaterial.SetColor(PropTop,           Color.Lerp(startTop,    targetPreset.gradientTop,    t));
                gradientMaterial.SetColor(PropBottom,        Color.Lerp(startBottom, targetPreset.gradientBottom, t));
                gradientMaterial.SetColor(PropGlowColor,     Color.Lerp(startGlow,   targetPreset.glowColor,      t));
                gradientMaterial.SetFloat(PropGlowIntensity, Mathf.Lerp(startIntensity, targetPreset.glowIntensity,   t));
                gradientMaterial.SetFloat(PropBreatheAmount, Mathf.Lerp(startAmplitude, targetPreset.breatheAmplitude, t));
                breatheCycleSeconds = Mathf.Lerp(startCycle, targetPreset.breatheCycleSeconds, t);

                dustSpeedMultiplier    = Mathf.Lerp(startDustSpeed, targetPreset.dustSpeed,    t);
                dustOpacityMultiplier  = Mathf.Lerp(startDustOpac,  targetPreset.dustOpacity,  t);
                dustSizeMultiplier     = Mathf.Lerp(startDustSize,  targetPreset.dustSize,     t);
                dustWanderMultiplier   = Mathf.Lerp(startDustWand,  targetPreset.dustWander,   t);
                steamOpacityMultiplier = Mathf.Lerp(startSteamOpac, targetPreset.steamOpacity, t);

                yield return null;
            }

            // Snap to exact target values to avoid drift.
            ApplyMoodInstant(target);
            transitionCoroutine = null;
        }

        private MoodSettings GetPreset(BackgroundMoodController.MoodType mood)
        {
            foreach (var p in presets)
                if (p.mood == mood) return p;
            Debug.LogWarning($"[LateNightDeskBackground] No preset defined for mood '{mood}'.");
            return null;
        }

        // -------------------------------------------------------------------
        // Build (runtime, once) — quad + two particle systems
        // -------------------------------------------------------------------

        /// <summary>Builds the quad + particle systems once. Safe to call repeatedly.</summary>
        public void EnsureBuilt()
        {
            if (gradientMaterial != null) return;

            if (referenceCamera == null) referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                // The recording flow runs with Camera.main disabled — grab any
                // camera (same fallback as ScrollingShapeController).
                var cams = FindObjectsOfType<Camera>();
                if (cams.Length > 0) referenceCamera = cams[0];
            }

            // Resources.Load rather than a bare Shader.Find — nothing else
            // references this shader, so only its Resources placement gets it
            // into a build (same story as Custom/PostProcessOverlay).
            Shader shader = Resources.Load<Shader>("Shaders/LateNightDeskGradient");
            if (shader == null) shader = Shader.Find("LateNightDesk/Gradient");
            if (shader == null)
            {
                Debug.LogWarning("[LateNightDeskBackground] LateNightDesk/Gradient shader not found — background disabled.");
                enabled = false;
                return;
            }

            gradientMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            gradientMaterial.SetFloat(PropAspect,   backdropSize.x / Mathf.Max(backdropSize.y, 0.01f));
            gradientMaterial.SetFloat(PropGlowSide, lampSide == LampSide.Right ? 1f : 0f);

            BuildGradientQuad();

            particleMaterial = CreateDefaultParticleMaterial();
            dustSprite  = MakeSoftCircleSprite(64,  0.16f);
            steamSprite = MakeSoftCircleSprite(128, 0.26f);
            BuildDustMotes();
            BuildSteamWisps();
        }

        private void BuildGradientQuad()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "LateNightDeskGradientQuad";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = backdropLocalPosition;
            quad.transform.localScale    = new Vector3(backdropSize.x, backdropSize.y, 1f);

            Collider col = quad.GetComponent<Collider>();
            if (col != null) DestroyRuntimeObject(col);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial   = gradientMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
        }

        private void BuildDustMotes()
        {
            var go = new GameObject("LateNightDeskDustMotes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, particleDepth);

            dustPs  = go.AddComponent<ParticleSystem>();
            dustPsr = go.GetComponent<ParticleSystemRenderer>();
            dustPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            dustPsr.sharedMaterial = particleMaterial;
            dustPsr.renderMode = ParticleSystemRenderMode.Billboard;
            dustPsr.alignment  = ParticleSystemRenderSpace.View;
            dustPsr.maxParticleSize = 1f;
            dustPsr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dustPsr.receiveShadows = false;

            float worldH = SafeWorldHeight();
            const float lifeMin = 22f, lifeMax = 38f; // long — motes dissolve mid-air, never pop

            var main = dustPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;                        // field pre-populated at Start
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = lifeMax;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;                       // velocity comes from Velocity over Lifetime
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, dustSizeMin * worldH),
                Mathf.Max(0.0001f, dustSizeMax * worldH));
            main.startColor = WithAlpha(dustTint, dustOpacityMax);
            main.maxParticles = Mathf.Max(60, dustCount * 4);

            var emission = dustPs.emission;
            emission.enabled = true;
            emission.rateOverTime = dustCount / ((lifeMin + lifeMax) * 0.5f);

            // 2D rectangle over the full camera rect + margin (pure 2D shape —
            // no z-depth, same reasoning as ScrollingShapeController).
            var shape = dustPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            Rect bounds = GetWorldBounds(0.1f);
            shape.position = new Vector3(bounds.center.x - go.transform.position.x,
                                         bounds.center.y - go.transform.position.y, 0f);
            shape.scale = new Vector3(Mathf.Max(0.01f, bounds.width),
                                      Mathf.Max(0.01f, bounds.height), 1f);

            var vel = dustPs.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            ApplyDustVelocity(worldH);

            // Slight per-particle wander.
            var noise = dustPs.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(dustNoiseStrength);
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.03f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            // Fade in/out at the lifetime edges so motes never pop. The
            // per-position light bias is layered on top via startColor in
            // UpdateDustLighting (renderer multiplies the two).
            var col = dustPs.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                        new GradientAlphaKey(1f, 0.88f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var tex = dustPs.textureSheetAnimation;
            tex.enabled = true;
            tex.mode = ParticleSystemAnimationMode.Sprites;
            while (tex.spriteCount > 0) tex.RemoveSprite(tex.spriteCount - 1);
            tex.AddSprite(dustSprite);
            tex.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            dustBuffer = new ParticleSystem.Particle[main.maxParticles];
            dustPs.Play(true);
        }

        private void BuildSteamWisps()
        {
            var go = new GameObject("LateNightDeskSteamWisps");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, particleDepth);

            steamPs = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            steamPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            psr.sharedMaterial = particleMaterial;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment  = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            float worldH = SafeWorldHeight();

            var main = steamPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;                        // a wisp may already be mid-rise at Start
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = Mathf.Max(1f, steamLifetimeRange.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(steamLifetimeRange.x, steamLifetimeRange.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, steamSizeRange.x * worldH),
                Mathf.Max(0.0001f, steamSizeRange.y * worldH));
            main.maxParticles = 8;

            var emission = steamPs.emission;
            emission.enabled = true;
            emission.rateOverTime = steamRatePerSecond;

            // Small box at the bottom corner on the LAMP side, starting just
            // below the frame edge so wisps enter, not appear.
            var shape = steamPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.2f, 0.4f, 0.01f);
            ApplySteamCorner();

            var vel = steamPs.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            float driftX = (lampSide == LampSide.Right ? -1f : 1f) * 0.05f; // slight lean toward center
            vel.x = new ParticleSystem.MinMaxCurve(driftX * 0.5f, driftX * 1.5f);
            vel.y = new ParticleSystem.MinMaxCurve(steamRiseSpeed * 0.8f, steamRiseSpeed * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Gentle curl — noise mostly horizontal so wisps waver as they rise.
            var noise = steamPs.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(steamNoiseStrength);
            noise.strengthY = new ParticleSystem.MinMaxCurve(steamNoiseStrength * 0.25f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0f);
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.08f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Fade in, hold low, dissipate. The peak alpha itself is applied
            // via startColor in ApplyLiveParticleParams (mood-lerped).
            var col = steamPs.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                        new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            // Wisps swell as they dissipate.
            var size = steamPs.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.6f));

            var tex = steamPs.textureSheetAnimation;
            tex.enabled = true;
            tex.mode = ParticleSystemAnimationMode.Sprites;
            while (tex.spriteCount > 0) tex.RemoveSprite(tex.spriteCount - 1);
            tex.AddSprite(steamSprite);
            tex.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            steamPs.Play(true);
        }

        // -------------------------------------------------------------------
        // Live parameter application (every frame, live-safe subset only)
        // -------------------------------------------------------------------

        private void ApplyLiveParticleParams()
        {
            float worldH = SafeWorldHeight();

            if (dustPs != null)
            {
                ApplyDustVelocity(worldH);
                var noise = dustPs.noise;
                noise.strength = new ParticleSystem.MinMaxCurve(dustNoiseStrength * dustWanderMultiplier);
            }

            if (steamPs != null)
            {
                // Rate scales with the mood's steam opacity so Minimal/Focus
                // stops emitting; live wisps keep their colorOverLifetime
                // envelope and dissipate naturally within <=10 s.
                var emission = steamPs.emission;
                emission.rateOverTime = steamRatePerSecond * Mathf.Clamp01(steamOpacityMultiplier);

                // New wisps pick up the current alpha and a hint of the lamp color.
                Color glow = gradientMaterial != null
                    ? gradientMaterial.GetColor(PropGlowColor) : Color.white;
                Color steamColor = Color.Lerp(Hex("#D8CFC4"), glow, 0.4f);
                var main = steamPs.main;
                main.startColor = WithAlpha(steamColor, steamPeakAlpha * steamOpacityMultiplier);
            }

            // Keep side/aspect honest if someone flips them in the Inspector.
            if (gradientMaterial != null)
            {
                gradientMaterial.SetFloat(PropGlowSide, lampSide == LampSide.Right ? 1f : 0f);
                gradientMaterial.SetFloat(PropAspect, backdropSize.x / Mathf.Max(backdropSize.y, 0.01f));
            }
        }

        private void ApplyDustVelocity(float worldH)
        {
            if (dustPs == null) return;
            float rad = dustDriftAngleDeg * Mathf.Deg2Rad;
            float v = Mathf.Max(0f, dustDriftSpeed) * Mathf.Max(0f, dustSpeedMultiplier) * worldH;
            // Wander also widens the per-particle speed spread (Playful's
            // "varied in speed"); clamped so min stays positive.
            float sv = Mathf.Clamp(dustSpeedVariation * dustWanderMultiplier, 0f, 0.95f);
            var vel = dustPs.velocityOverLifetime;
            // All curves must share one MinMaxCurveMode (TwoConstants) — Unity
            // throws otherwise (see ScrollingShapeController.ApplyVelocity).
            vel.x = new ParticleSystem.MinMaxCurve(Mathf.Cos(rad) * v * (1f - sv),
                                                   Mathf.Cos(rad) * v * (1f + sv));
            vel.y = new ParticleSystem.MinMaxCurve(Mathf.Sin(rad) * v * (1f - sv),
                                                   Mathf.Sin(rad) * v * (1f + sv));
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        private void ApplySteamCorner()
        {
            if (steamPs == null) return;
            Rect frame = GetWorldBounds(0f);
            float x = lampSide == LampSide.Right
                ? frame.xMax - steamCornerInset * frame.width
                : frame.xMin + steamCornerInset * frame.width;
            var shape = steamPs.shape;
            var t = steamPs.transform;
            shape.position = new Vector3(x - t.position.x,
                                         (frame.yMin - 0.4f) - t.position.y, 0f);
        }

        /// <summary>
        /// Fake "catching the light": motes nearer the lamp side render
        /// slightly brighter and pick up a hint of the glow color. Runs over
        /// the live particle buffer (~20–90 entries — cheap). Base alpha and
        /// size are re-derived from each particle's stable randomSeed every
        /// frame, so overwriting startColor/startSize never compounds and the
        /// mood multipliers apply to EXISTING motes, not just new ones.
        /// </summary>
        private void UpdateDustLighting()
        {
            if (dustPs == null || dustBuffer == null) return;
            int n = dustPs.GetParticles(dustBuffer);
            if (n == 0) return;

            Rect frame = GetWorldBounds(0.1f);
            Color glow = gradientMaterial != null
                ? gradientMaterial.GetColor(PropGlowColor) : Color.white;
            float worldH = SafeWorldHeight();

            for (int i = 0; i < n; i++)
            {
                float nx = Mathf.InverseLerp(frame.xMin, frame.xMax, dustBuffer[i].position.x);
                float prox = lampSide == LampSide.Right ? nx : 1f - nx;
                prox *= prox; // bias the effect toward the lamp-side edge

                uint seed = dustBuffer[i].randomSeed;
                float baseAlpha = Mathf.Lerp(dustOpacityMin, dustOpacityMax, Hash01(seed));
                float baseSize  = Mathf.Lerp(dustSizeMin,    dustSizeMax,    Hash01(seed ^ 0x9E3779B9u)) * worldH;

                float alpha = Mathf.Clamp01(baseAlpha * dustOpacityMultiplier * (1f + dustLightBias * prox));
                Color c = Color.Lerp(dustTint, glow, 0.35f * prox);
                c.a = alpha;
                dustBuffer[i].startColor = c;
                dustBuffer[i].startSize  = baseSize * dustSizeMultiplier;
            }
            dustPs.SetParticles(dustBuffer, n);
        }

        // xorshift on the particle's stable random seed → deterministic 0..1.
        private static float Hash01(uint seed)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFFu) / 16777215f;
        }

        // -------------------------------------------------------------------
        // Test hooks (same ContextMenu pattern as ScrollingShapeController)
        // -------------------------------------------------------------------

        [ContextMenu("Test: Cycle All 5 Moods (play mode)")]
        private void TestCycleAllMoods()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[LateNightDeskBackground] Mood cycle test needs play mode.");
                return;
            }
            StartCoroutine(CycleAllMoodsRoutine());
        }

        [ContextMenu("Test: Next Mood (3s crossfade, play mode)")]
        private void TestNextMood()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[LateNightDeskBackground] Mood test needs play mode.");
                return;
            }
            var next = (BackgroundMoodController.MoodType)(((int)currentMood + 1) % 5);
            Debug.Log($"[LateNightDeskBackground] Crossfading {currentMood} → {next} over 3s.");
            SetMood(next, 3f);
        }

        private IEnumerator CycleAllMoodsRoutine()
        {
            var order = new[]
            {
                BackgroundMoodController.MoodType.CalmNeutral,
                BackgroundMoodController.MoodType.Energetic,
                BackgroundMoodController.MoodType.TenseDramatic,
                BackgroundMoodController.MoodType.PlayfulLight,
                BackgroundMoodController.MoodType.MinimalFocus,
                BackgroundMoodController.MoodType.CalmNeutral,
            };
            foreach (var mood in order)
            {
                Debug.Log($"[LateNightDeskBackground] Crossfading to {mood} over 3s.");
                SetMood(mood, 3f);
                yield return new WaitForSeconds(6f); // 3s fade + 3s hold
            }
            Debug.Log("[LateNightDeskBackground] Mood cycle complete.");
        }

        // -------------------------------------------------------------------
        // Helpers (camera sizing copied from ScrollingShapeController)
        // -------------------------------------------------------------------

        private static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;
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

        // Same shader-candidate fallback chain as ScrollingShapeController.
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
                        name = "LateNightDeskParticles",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }
            Debug.LogWarning("[LateNightDeskBackground] No compatible particle shader found. " +
                             "Particles may render magenta.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // Soft-circle sprite with a gaussian alpha falloff — sigma as a
        // fraction of the texture size. Small sigma = defined mote core,
        // large sigma = diffuse steam blob.
        private static Sprite MakeSoftCircleSprite(int size, float sigmaFrac)
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
                // Cut the far tail so the sprite edge is genuinely transparent.
                a = Mathf.Clamp01((a - 0.01f) / 0.99f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
    }
}

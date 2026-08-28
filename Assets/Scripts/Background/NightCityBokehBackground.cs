using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// "NightCityBokeh" animated background — an abstract view of blurred
    /// city lights at night through a window. Two layers, all built by this
    /// one component at runtime:
    ///
    ///   1. Gradient base — a quad using the NightCityBokeh/Gradient shader:
    ///      deep night blue → lighter dark blue with a faint warm skyline
    ///      haze band in the lower third. Optional (off by default) window
    ///      vignette and glass sheen are exposed as parameters.
    ///   2. Bokeh lights — 20–40 soft out-of-focus discs, mostly warm amber
    ///      and warm white with occasional cool/teal accents, denser in the
    ///      lower two-thirds, brightest kept away from dead center.
    ///
    /// MOTION RULE: light positions are STATIC — no drift, no scrolling, no
    /// parallax, nothing the eye can follow. The frame stays alive through
    /// (a) per-light twinkle on independent random 8–20 s cycles with random
    /// phases, and (b) life-cycle swaps: every ~10–20 s one light fades out
    /// over 3–5 s while a new one fades in elsewhere, like windows turning on
    /// and off across a city. Random cycles, phases and swap timing mean the
    /// frame never visibly repeats. Lights added or removed by a mood change
    /// converge through the same fades — they never pop.
    ///
    /// Self-contained: drop this component on an empty GameObject (name it
    /// <see cref="RootObjectName"/> — BackgroundModeManager toggles it by
    /// that name, the way it does the other backdrops) and it builds the quad
    /// + one particle system in Start. One material, one particle system, no
    /// post-processing. Layering matches the sibling backdrops: opaque quad
    /// at the synthwave Sky depth, particles just in front of it, everything
    /// far behind the presenter — depth does the sorting.
    ///
    /// Mood handling mirrors LateNightDeskBackground exactly: five presets in
    /// an Inspector list keyed by the shared MoodType enum, SetMood
    /// SmoothStep-lerps EVERY parameter over the crossfade duration,
    /// ApplyMoodInstant snaps (and cancels any in-flight crossfade). The
    /// warm/cool ratio and accent variety apply to newly spawned lights;
    /// the frame-wide shift a crossfade needs comes from the lerped
    /// lightTint, so existing lights recolor smoothly too.
    /// </summary>
    public class NightCityBokehBackground : MonoBehaviour, IAnimatedBackground
    {
        /// <summary>
        /// Root-object name BackgroundModeManager looks for (same
        /// find-by-name pattern as the synthwave / LateNightDesk backdrops).
        /// </summary>
        public const string RootObjectName = "NightCityBokehBackground";

        [System.Serializable]
        public class MoodSettings
        {
            public BackgroundMoodController.MoodType mood;

            [Header("Gradient Base")]
            public Color gradientTop    = new Color(0.039f, 0.055f, 0.094f, 1f);
            public Color gradientBottom = new Color(0.063f, 0.082f, 0.122f, 1f);
            public Color hazeColor      = new Color(1f, 0.702f, 0.361f, 1f);
            [Range(0f, 1f)] public float hazeIntensity = 0.10f;

            [Header("Bokeh Lights (multipliers on the base values below)")]
            [Tooltip("Whole-frame tint over every light — this is what makes a " +
                     "mood's palette shift crossfade smoothly on EXISTING lights.")]
            public Color lightTint = Color.white;
            [Range(0f, 3f)] public float lightCount      = 1f;
            [Range(0f, 3f)] public float lightBrightness = 1f;
            [Range(0f, 3f)] public float twinkleSpeed    = 1f;
            [Range(0f, 3f)] public float twinkleAmount   = 1f;
            [Tooltip("Scales the 10–20 s swap interval — above 1 = rarer swaps.")]
            [Range(0.25f, 4f)] public float swapInterval = 1f;

            [Header("Palette For Newly Spawned Lights")]
            [Tooltip("Chance a new light rolls warm (amber/warm white) instead of cool.")]
            [Range(0f, 1f)] public float warmRatio = 0.65f;
            [Tooltip("Chance a new light rolls an accent color (teal / soft pink).")]
            [Range(0f, 1f)] public float accentVariety = 0.15f;
        }

        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Scene Hookup")]
        [Tooltip("Leave empty to auto-use Camera.main (any camera as fallback).")]
        public Camera referenceCamera;

        [Tooltip("Starting mood. Applied instantly (no transition) on Start.")]
        public BackgroundMoodController.MoodType startingMood =
            BackgroundMoodController.MoodType.CalmNeutral;

        [Header("Layout (matches the sibling backdrops)")]
        [Tooltip("Local position of the gradient quad — same depth the synthwave Sky quad uses.")]
        public Vector3 backdropLocalPosition = new Vector3(0f, -0.8f, 9f);
        [Tooltip("Local scale of the gradient quad. Over-covers the ~17.8x10 visible frame.")]
        public Vector2 backdropSize = new Vector2(26f, 15f);
        [Tooltip("Local z of the bokeh particle system — just in front of the backdrop, " +
                 "far behind the presenter; depth handles the sorting.")]
        public float particleDepth = 8f;

        [Header("Bokeh Lights")]
        [Range(5, 60)]
        [Tooltip("Target number of lights at the Calm/Neutral baseline (spec: 20–40).")]
        public int baseLightCount = 30;
        [Tooltip("Disc size as fraction of SCREEN HEIGHT (rolled per light, biased small).")]
        [Range(0.005f, 0.3f)] public float lightSizeMin = 0.030f;
        [Range(0.005f, 0.3f)] public float lightSizeMax = 0.110f;
        [Tooltip("Per-light base alpha range (additive blending — keep modest).")]
        [Range(0f, 1f)] public float lightAlphaMin = 0.10f;
        [Range(0f, 1f)] public float lightAlphaMax = 0.30f;
        [Tooltip("Per-light twinkle cycle length range, seconds (random per light).")]
        public Vector2 twinkleCycleRange = new Vector2(8f, 20f);
        [Range(0f, 1f)]
        [Tooltip("Base twinkle swing ± as fraction of the light's brightness.")]
        public float twinkleAmountBase = 0.30f;
        [Tooltip("Seconds between life-cycle swaps (random in range, mood-scaled).")]
        public Vector2 swapIntervalRange = new Vector2(10f, 20f);
        [Tooltip("Fade-in / fade-out duration range for every light, seconds.")]
        public Vector2 fadeSecondsRange = new Vector2(3f, 5f);
        [Tooltip("Vertical distribution bias — higher = denser toward the bottom " +
                 "(1 = uniform; 1.6 puts roughly two-thirds below the midline).")]
        public float lowerBiasExponent = 1.6f;
        [Range(0f, 0.6f)]
        [Tooltip("Center exclusion zone radius (fraction of frame height): the " +
                 "brightest/biggest lights are re-rolled away from dead center so " +
                 "they never compete with the character or content zone.")]
        public float centerExclusionRadius = 0.30f;

        [Header("Bokeh Palette")]
        public Color warmAmber  = new Color(1f, 0.702f, 0.361f, 1f);      // #FFB35C
        public Color warmWhite  = new Color(1f, 0.906f, 0.769f, 1f);      // #FFE7C4
        public Color coolWhite  = new Color(0.863f, 0.910f, 1f, 1f);      // #DCE8FF
        public Color tealAccent = new Color(0.498f, 0.878f, 0.839f, 1f);  // #7FE0D6
        public Color pinkAccent = new Color(1f, 0.702f, 0.784f, 1f);      // #FFB3C8

        [Header("Window Glass (structural, not mood-driven — off by default)")]
        [Range(0f, 1f)]   public float windowVignetteStrength = 0f;
        [Range(0f, 0.2f)] public float glassSheenStrength     = 0f;

        [Header("Mood Presets")]
        [Tooltip("One entry per mood, same pattern as the sibling backgrounds.")]
        public List<MoodSettings> presets = new List<MoodSettings>()
        {
            // Calm/Neutral — the values from the visual spec, verbatim.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.CalmNeutral,
                gradientTop = Hex("#0A0E18"), gradientBottom = Hex("#10151F"),
                hazeColor = Hex("#FFB35C"), hazeIntensity = 0.10f,
                lightTint = Color.white,
                lightCount = 1f, lightBrightness = 1f,
                twinkleSpeed = 1f, twinkleAmount = 1f, swapInterval = 1f,
                warmRatio = 0.65f, accentVariety = 0.15f,
            },
            // Energetic — the city is awake: ~25% more lights, slightly
            // brighter, faster twinkle, mix shifted toward cool whites.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.Energetic,
                gradientTop = Hex("#0D1220"), gradientBottom = Hex("#141A28"),
                hazeColor = Hex("#E8EDF8"), hazeIntensity = 0.12f,
                lightTint = Hex("#EDF3FF"),
                lightCount = 1.25f, lightBrightness = 1.15f,
                twinkleSpeed = 1.5f, twinkleAmount = 1f, swapInterval = 0.85f,
                warmRatio = 0.35f, accentVariety = 0.2f,
            },
            // Tense — fewer lights, red/orange bias, darker frame, slower
            // heavier twinkle, rarer swaps.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.TenseDramatic,
                gradientTop = Hex("#070A12"), gradientBottom = Hex("#0C0F17"),
                hazeColor = Hex("#FF6B3D"), hazeIntensity = 0.08f,
                lightTint = Hex("#FFA582"),
                lightCount = 0.6f, lightBrightness = 0.9f,
                twinkleSpeed = 0.55f, twinkleAmount = 1.3f, swapInterval = 1.8f,
                warmRatio = 0.95f, accentVariety = 0.05f,
            },
            // Playful — wider color variety (teal + soft pink accents),
            // livelier twinkle, slightly more frequent swaps.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.PlayfulLight,
                gradientTop = Hex("#0C0F1C"), gradientBottom = Hex("#131624"),
                hazeColor = Hex("#FFC272"), hazeIntensity = 0.12f,
                lightTint = Hex("#FFF4EC"),
                lightCount = 1.1f, lightBrightness = 1.05f,
                twinkleSpeed = 1.3f, twinkleAmount = 1.15f, swapInterval = 0.7f,
                warmRatio = 0.5f, accentVariety = 0.55f,
            },
            // Minimal/Focus — most lights faded out, remainder dimmed and
            // near-static, lowest contrast.
            new MoodSettings {
                mood = BackgroundMoodController.MoodType.MinimalFocus,
                gradientTop = Hex("#0B0E15"), gradientBottom = Hex("#0E1118"),
                hazeColor = Hex("#FFB35C"), hazeIntensity = 0.05f,
                lightTint = Hex("#EFEFEF"),
                lightCount = 0.35f, lightBrightness = 0.55f,
                twinkleSpeed = 0.4f, twinkleAmount = 0.3f, swapInterval = 2.5f,
                warmRatio = 0.65f, accentVariety = 0.05f,
            },
        };

        [Header("Live Mood State (lerped during transitions — debug view)")]
        public Color lightTintCurrent = Color.white;
        [Range(0f, 3f)] public float lightCountMultiplier      = 1f;
        [Range(0f, 3f)] public float lightBrightnessMultiplier = 1f;
        [Range(0f, 3f)] public float twinkleSpeedMultiplier    = 1f;
        [Range(0f, 3f)] public float twinkleAmountMultiplier   = 1f;
        [Range(0.25f, 4f)] public float swapIntervalMultiplier = 1f;
        [Range(0f, 1f)] public float warmRatioCurrent     = 0.65f;
        [Range(0f, 1f)] public float accentVarietyCurrent = 0.15f;

        // Shader property IDs (cached for speed)
        private static readonly int PropTop           = Shader.PropertyToID("_TopColor");
        private static readonly int PropBottom        = Shader.PropertyToID("_BottomColor");
        private static readonly int PropHazeColor     = Shader.PropertyToID("_HazeColor");
        private static readonly int PropHazeIntensity = Shader.PropertyToID("_HazeIntensity");
        private static readonly int PropVignette      = Shader.PropertyToID("_VignetteStrength");
        private static readonly int PropSheen         = Shader.PropertyToID("_SheenStrength");
        private static readonly int PropAspect        = Shader.PropertyToID("_Aspect");

        private BackgroundMoodController.MoodType currentMood;
        private Coroutine transitionCoroutine;

        private Material gradientMaterial;
        private ParticleSystem bokehPs;
        private Material particleMaterial;
        private Texture2D bokehTexture;
        private ParticleSystem.Particle[] bokehBuffer;

        // Per-light bookkeeping, keyed by the randomSeed we hand each emitted
        // particle. Positions are static and everything else (base color/
        // size/alpha, twinkle cycle + accumulated phase, fade clocks) lives
        // here — the per-frame pass re-derives the rendered color from this,
        // so overwriting startColor never loses information.
        private class BokehLight
        {
            public Color baseColor;
            public float baseAlpha;
            public float baseSize;
            public float spawnTime;
            public float fadeInSecs;
            public float retireTime = -1f;   // < 0 = alive
            public float fadeOutSecs;
            public float twinkleCycle;
            public float twinklePhase;       // accumulated in C# so a lerped
                                             // twinkle-speed change never pops
        }

        private readonly Dictionary<uint, BokehLight> lights = new Dictionary<uint, BokehLight>();
        private static readonly List<uint> scratchSeeds = new List<uint>(8);
        private uint nextLightId = 1;
        private float nextSwapAt = -1f;

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
            if (gradientMaterial == null || bokehPs == null) return;

            // Life-cycle swap: one light fades out, a new one fades in
            // elsewhere. Interval re-rolled every time (and mood-scaled) so
            // the rhythm never settles into a visible pattern.
            if (Time.time >= nextSwapAt)
            {
                if (RetireRandomLight()) SpawnLight();
                nextSwapAt = Time.time + Random.Range(swapIntervalRange.x, swapIntervalRange.y)
                                       * Mathf.Max(0.25f, swapIntervalMultiplier);
            }

            // Converge the population toward the mood's target, one light per
            // frame — a mood transition lerps the target, so arrivals and
            // departures spread across the crossfade, each with its own
            // 3–5 s fade. Never a pop.
            int alive = CountAliveLights();
            int target = Mathf.RoundToInt(baseLightCount * lightCountMultiplier);
            if (alive < target) SpawnLight();
            else if (alive > target) RetireRandomLight();

            // Keep the structural glass params honest if tweaked in the
            // Inspector (same live-assert discipline as the siblings).
            gradientMaterial.SetFloat(PropVignette, windowVignetteStrength);
            gradientMaterial.SetFloat(PropSheen,    glassSheenStrength);
            gradientMaterial.SetFloat(PropAspect,   backdropSize.x / Mathf.Max(backdropSize.y, 0.01f));
        }

        void LateUpdate()
        {
            UpdateBokehLights();
        }

        void OnDestroy()
        {
            DestroyRuntimeObject(gradientMaterial);
            DestroyRuntimeObject(particleMaterial);
            DestroyRuntimeObject(bokehTexture);
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
            gradientMaterial.SetColor(PropHazeColor,     preset.hazeColor);
            gradientMaterial.SetFloat(PropHazeIntensity, preset.hazeIntensity);

            lightTintCurrent          = preset.lightTint;
            lightCountMultiplier      = preset.lightCount;
            lightBrightnessMultiplier = preset.lightBrightness;
            twinkleSpeedMultiplier    = preset.twinkleSpeed;
            twinkleAmountMultiplier   = preset.twinkleAmount;
            swapIntervalMultiplier    = preset.swapInterval;
            warmRatioCurrent          = preset.warmRatio;
            accentVarietyCurrent      = preset.accentVariety;
            // The light population itself converges via fades in Update —
            // "instant" applies the parameters; a city can't teleport.
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
            Color startTop     = gradientMaterial.GetColor(PropTop);
            Color startBottom  = gradientMaterial.GetColor(PropBottom);
            Color startHaze    = gradientMaterial.GetColor(PropHazeColor);
            float startHazeInt = gradientMaterial.GetFloat(PropHazeIntensity);

            Color startTint    = lightTintCurrent;
            float startCount   = lightCountMultiplier;
            float startBright  = lightBrightnessMultiplier;
            float startTwSpeed = twinkleSpeedMultiplier;
            float startTwAmt   = twinkleAmountMultiplier;
            float startSwap    = swapIntervalMultiplier;
            float startWarm    = warmRatioCurrent;
            float startAccent  = accentVarietyCurrent;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration); // ease-in-out

                gradientMaterial.SetColor(PropTop,           Color.Lerp(startTop,    targetPreset.gradientTop,    t));
                gradientMaterial.SetColor(PropBottom,        Color.Lerp(startBottom, targetPreset.gradientBottom, t));
                gradientMaterial.SetColor(PropHazeColor,     Color.Lerp(startHaze,   targetPreset.hazeColor,      t));
                gradientMaterial.SetFloat(PropHazeIntensity, Mathf.Lerp(startHazeInt, targetPreset.hazeIntensity, t));

                lightTintCurrent          = Color.Lerp(startTint,   targetPreset.lightTint,     t);
                lightCountMultiplier      = Mathf.Lerp(startCount,  targetPreset.lightCount,    t);
                lightBrightnessMultiplier = Mathf.Lerp(startBright, targetPreset.lightBrightness, t);
                twinkleSpeedMultiplier    = Mathf.Lerp(startTwSpeed, targetPreset.twinkleSpeed,  t);
                twinkleAmountMultiplier   = Mathf.Lerp(startTwAmt,  targetPreset.twinkleAmount, t);
                swapIntervalMultiplier    = Mathf.Lerp(startSwap,   targetPreset.swapInterval,  t);
                warmRatioCurrent          = Mathf.Lerp(startWarm,   targetPreset.warmRatio,     t);
                accentVarietyCurrent      = Mathf.Lerp(startAccent, targetPreset.accentVariety, t);

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
            Debug.LogWarning($"[NightCityBokehBackground] No preset defined for mood '{mood}'.");
            return null;
        }

        // -------------------------------------------------------------------
        // Build (runtime, once) — quad + one particle system
        // -------------------------------------------------------------------

        /// <summary>Builds the quad + particle system once. Safe to call repeatedly.</summary>
        public void EnsureBuilt()
        {
            if (gradientMaterial != null) return;

            if (referenceCamera == null) referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                // The recording flow runs with Camera.main disabled — grab any
                // camera (same fallback as the sibling backgrounds).
                var cams = FindObjectsOfType<Camera>();
                if (cams.Length > 0) referenceCamera = cams[0];
            }

            // Resources.Load rather than a bare Shader.Find — nothing else
            // references this shader, so only its Resources placement gets it
            // into a build (same story as LateNightDeskGradient).
            Shader shader = Resources.Load<Shader>("Shaders/NightCityBokehGradient");
            if (shader == null) shader = Shader.Find("NightCityBokeh/Gradient");
            if (shader == null)
            {
                Debug.LogWarning("[NightCityBokehBackground] NightCityBokeh/Gradient shader not found — background disabled.");
                enabled = false;
                return;
            }

            gradientMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            gradientMaterial.SetFloat(PropAspect, backdropSize.x / Mathf.Max(backdropSize.y, 0.01f));

            BuildGradientQuad();

            particleMaterial = CreateBokehParticleMaterial();
            bokehTexture = MakeBokehDiscTexture(96);
            particleMaterial.mainTexture = bokehTexture;
            BuildBokehSystem();
            PrewarmLights();
        }

        private void BuildGradientQuad()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "NightCityBokehGradientQuad";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = backdropLocalPosition;
            quad.transform.localScale    = new Vector3(backdropSize.x, backdropSize.y, 1f);

            Collider col = quad.GetComponent<Collider>();
            if (col != null) DestroyRuntimeObject(col);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial    = gradientMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
        }

        private void BuildBokehSystem()
        {
            var go = new GameObject("NightCityBokehLights");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, particleDepth);

            bokehPs = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            bokehPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            psr.sharedMaterial = particleMaterial;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment  = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            // Everything is manual: no emission module, no shape, no
            // velocity, no noise — light positions are STATIC by design, and
            // spawning/fading is driven from Update/LateUpdate via Emit +
            // the per-light bookkeeping.
            var main = bokehPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = LightLifetime;
            main.maxParticles = Mathf.Max(64, baseLightCount * 4);

            var emission = bokehPs.emission;  emission.enabled = false;
            var shape    = bokehPs.shape;     shape.enabled    = false;
            var vel      = bokehPs.velocityOverLifetime; vel.enabled  = false;
            var noiseMod = bokehPs.noise;               noiseMod.enabled = false;
            var colMod   = bokehPs.colorOverLifetime;   colMod.enabled   = false;
            var szMod    = bokehPs.sizeOverLifetime;    szMod.enabled    = false;
            var rotMod   = bokehPs.rotationOverLifetime; rotMod.enabled  = false;

            bokehBuffer = new ParticleSystem.Particle[main.maxParticles];
            bokehPs.Play(true);
        }

        // Pre-populate the frame so the city is already alive on the first
        // recorded frame: spawn times are backdated so most lights are past
        // their fade-in, and twinkle phases are random from the start.
        private void PrewarmLights()
        {
            int target = Mathf.RoundToInt(baseLightCount * lightCountMultiplier);
            for (int i = 0; i < target; i++)
                SpawnLight(backdateSeconds: Random.Range(0f, 15f));
            nextSwapAt = Time.time + Random.Range(swapIntervalRange.x, swapIntervalRange.y);
        }

        // -------------------------------------------------------------------
        // Light life-cycle (spawn / retire / per-frame pass)
        // -------------------------------------------------------------------

        // Effectively-infinite particle lifetime: lights die only when the
        // per-frame pass zeroes remainingLifetime after their fade-out ends.
        private const float LightLifetime = 100000f;

        private int CountAliveLights()
        {
            int n = 0;
            foreach (var kv in lights)
                if (kv.Value.retireTime < 0f) n++;
            return n;
        }

        private void SpawnLight(float backdateSeconds = 0f)
        {
            if (bokehPs == null) return;

            float worldH = SafeWorldHeight();
            Rect frame = GetWorldBounds(0.06f);

            // Size: biased small so big blurry discs stay occasional.
            float sizeRoll = Random.value;
            float size = Mathf.Lerp(lightSizeMin, lightSizeMax, sizeRoll * sizeRoll) * worldH;
            float alpha = Mathf.Lerp(lightAlphaMin, lightAlphaMax, Random.value);
            bool isBright = sizeRoll > 0.7f || alpha > Mathf.Lerp(lightAlphaMin, lightAlphaMax, 0.7f);

            // Position: uniform x, y biased toward the lower two-thirds. The
            // brightest lights re-roll away from the center zone (a few
            // tries, then dim as a fallback) so they never compete with the
            // character or the content zone.
            Vector2 pos = Vector2.zero;
            float exclusion = centerExclusionRadius * frame.height;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                pos.x = Mathf.Lerp(frame.xMin, frame.xMax, Random.value);
                pos.y = frame.yMin + frame.height * Mathf.Pow(Random.value, Mathf.Max(1f, lowerBiasExponent));
                if (!isBright) break;
                if (Vector2.Distance(pos, frame.center) > exclusion) break;
                if (attempt == 4) alpha *= 0.5f; // couldn't escape the center — dim instead
            }

            var data = new BokehLight
            {
                baseColor    = RollLightColor(),
                baseAlpha    = alpha,
                baseSize     = size,
                spawnTime    = Time.time - backdateSeconds,
                fadeInSecs   = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                fadeOutSecs  = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                twinkleCycle = Random.Range(twinkleCycleRange.x, twinkleCycleRange.y),
                twinklePhase = Random.Range(0f, 2f * Mathf.PI), // random offset — nothing syncs
            };
            uint id = nextLightId++;
            lights[id] = data;

            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y,
                    transform.position.z + particleDepth),
                applyShapeToPosition = false,
                startSize = size,
                startLifetime = LightLifetime,
                startColor = Color.clear,   // the same-frame LateUpdate pass sets the real color
                randomSeed = id,
            };
            bokehPs.Emit(ep, 1);
        }

        // Picks one alive (not already fading out) light and starts its
        // 3–5 s fade-out. Returns false when there is nothing to retire.
        private bool RetireRandomLight()
        {
            scratchSeeds.Clear();
            foreach (var kv in lights)
                if (kv.Value.retireTime < 0f) scratchSeeds.Add(kv.Key);
            if (scratchSeeds.Count == 0) return false;

            var data = lights[scratchSeeds[Random.Range(0, scratchSeeds.Count)]];
            data.retireTime = Time.time;
            return true;
        }

        /// <summary>
        /// The per-frame pass over the live particles (≤ ~40 — cheap): each
        /// light's rendered color = its rolled base color × the mood tint,
        /// alpha = base × brightness × twinkle × fade envelopes. Twinkle
        /// phases are accumulated here so a mood's twinkle-speed lerp changes
        /// pace without a phase pop. Positions are never touched.
        /// </summary>
        private void UpdateBokehLights()
        {
            if (bokehPs == null || bokehBuffer == null) return;
            int n = bokehPs.GetParticles(bokehBuffer);
            if (n == 0) return;

            float now = Time.time;
            float dt = Time.deltaTime;
            float twinkleSwing = twinkleAmountBase * twinkleAmountMultiplier;

            scratchSeeds.Clear(); // reused here to collect seeds seen this pass
            for (int i = 0; i < n; i++)
            {
                if (!lights.TryGetValue(bokehBuffer[i].randomSeed, out BokehLight data))
                {
                    bokehBuffer[i].remainingLifetime = 0f; // orphan — should not happen
                    continue;
                }
                scratchSeeds.Add(bokehBuffer[i].randomSeed);

                float envIn = Mathf.Clamp01((now - data.spawnTime) / data.fadeInSecs);
                float envOut = data.retireTime < 0f
                    ? 1f
                    : 1f - Mathf.Clamp01((now - data.retireTime) / data.fadeOutSecs);
                if (envOut <= 0f)
                {
                    bokehBuffer[i].remainingLifetime = 0f; // fade complete — free the slot
                    continue;
                }

                data.twinklePhase += dt * (2f * Mathf.PI / data.twinkleCycle) * twinkleSpeedMultiplier;
                float twinkle = 1f + twinkleSwing * Mathf.Sin(data.twinklePhase);

                Color c = data.baseColor * lightTintCurrent;
                c.a = Mathf.Clamp01(data.baseAlpha * lightBrightnessMultiplier * twinkle)
                      * envIn * envOut;
                bokehBuffer[i].startColor = c;
                bokehBuffer[i].startSize  = data.baseSize;
            }
            bokehPs.SetParticles(bokehBuffer, n);

            // Prune bookkeeping for lights whose particles are gone. The
            // scratch list holds every seed still alive; anything else in the
            // dictionary is finished. (Counts are tiny; this stays cheap.)
            if (lights.Count > scratchSeeds.Count)
            {
                var dead = new List<uint>();
                foreach (var kv in lights)
                    if (!scratchSeeds.Contains(kv.Key)) dead.Add(kv.Key);
                foreach (var seed in dead) lights.Remove(seed);
            }
        }

        // Rolls a color for a NEW light from the current (lerped) palette
        // knobs: warm amber/white vs cool white, with an accentVariety chance
        // of a teal or soft-pink accent. Existing lights keep their rolled
        // color — the mood's frame-wide shift rides lightTintCurrent instead.
        private Color RollLightColor()
        {
            if (Random.value < accentVarietyCurrent)
                return Random.value < 0.5f ? tealAccent : pinkAccent;
            if (Random.value < warmRatioCurrent)
                return Color.Lerp(warmAmber, warmWhite, Random.value);
            return Color.Lerp(coolWhite, tealAccent, Random.value * 0.35f);
        }

        // -------------------------------------------------------------------
        // Test hooks (same ContextMenu pattern as the sibling backgrounds)
        // -------------------------------------------------------------------

        [ContextMenu("Test: Cycle All 5 Moods (play mode)")]
        private void TestCycleAllMoods()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NightCityBokehBackground] Mood cycle test needs play mode.");
                return;
            }
            StartCoroutine(CycleAllMoodsRoutine());
        }

        [ContextMenu("Test: Next Mood (3s crossfade, play mode)")]
        private void TestNextMood()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NightCityBokehBackground] Mood test needs play mode.");
                return;
            }
            var next = (BackgroundMoodController.MoodType)(((int)currentMood + 1) % 5);
            Debug.Log($"[NightCityBokehBackground] Crossfading {currentMood} → {next} over 3s.");
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
                Debug.Log($"[NightCityBokehBackground] Crossfading to {mood} over 3s.");
                SetMood(mood, 3f);
                yield return new WaitForSeconds(6f); // 3s fade + 3s hold
            }
            Debug.Log("[NightCityBokehBackground] Mood cycle complete.");
        }

        // -------------------------------------------------------------------
        // Helpers (camera sizing copied from the sibling backgrounds)
        // -------------------------------------------------------------------

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

        // Additive first — overlapping out-of-focus discs should sum to a
        // brighter glow, which alpha blending turns muddy. (The siblings use
        // alpha-blended particles; this is a deliberate, documented
        // difference for the bokeh look.) Falls back down the same chain.
        private static Material CreateBokehParticleMaterial()
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
                        name = "NightCityBokehParticles",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }
            Debug.LogWarning("[NightCityBokehBackground] No compatible particle shader found. " +
                             "Particles may render magenta.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // Soft-edged bokeh disc: solid-ish core with a wide feathered rim —
        // reads as a heavily out-of-focus light, not a gaussian star.
        private static Texture2D MakeBokehDiscTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float rn = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / radius;
                float a = 1f - Mathf.SmoothStep(0.35f, 0.95f, rn);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}

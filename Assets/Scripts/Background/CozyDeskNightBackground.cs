using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// "CozyDeskNight" background — the painted lofi desk-at-night
    /// illustration (Assets/Art/Backgrounds/CozyDeskNight.png) brought to
    /// life the way animated lofi wallpapers are: the artwork stays a static,
    /// frame-fit base layer, and the living elements are rendered in front:
    ///
    ///   1. Window bokeh — extra city lights twinkling on independent random
    ///      8–20 s cycles, with life-cycle swaps (one fades out over 3–5 s
    ///      while another fades in elsewhere) confined to the window's city
    ///      band. Positions are STATIC — nothing drifts.
    ///   2. Star twinkle — a few tiny cool points in the upper sky, twinkle
    ///      only (no swaps).
    ///   3. Coffee steam — barely-there wisps rising from the mug, 6–10 s
    ///      each with gentle noise curl.
    ///   4. Lamp glow breathing — a soft additive pool over the lamp that
    ///      swells and dims on a slow two-sine cycle (phases integrated in
    ///      C# so the rhythm never pops or visibly repeats).
    ///
    /// Every element is placed in NORMALIZED ARTWORK COORDINATES (0–1 across
    /// the image, origin bottom-left) — the region rects and anchor points in
    /// the Inspector line the effects up with the painted window, mug and
    /// lamp, and can be nudged freely if the artwork changes.
    ///
    /// Standalone by design: not yet an IAnimatedBackground / Background
    /// Style entry (no mood presets) — that wiring is a later step, same as
    /// the other backdrops went through. Ships as a prefab
    /// (Assets/Prefabs/CozyDeskNightBackground.prefab) that carries the
    /// artwork reference; the component builds everything else at runtime.
    ///
    /// NOTE on the mirrored recording camera: the recording flow flips the
    /// frame horizontally, so the finished video shows the lamp on the RIGHT
    /// unless <see cref="flipHorizontal"/> is on (which pre-flips the whole
    /// scene so the mirror restores the painted orientation).
    /// </summary>
    public class CozyDeskNightBackground : MonoBehaviour
    {
        /// <summary>Root-object name, following the sibling backdrops' pattern.</summary>
        public const string RootObjectName = "CozyDeskNightBackground";

        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Artwork")]
        [Tooltip("The painted base illustration. Wired in the prefab; falls back to " +
                 "Resources.Load(\"Backgrounds/CozyDeskNight\") if empty.")]
        public Texture2D artworkTexture;
        [Tooltip("Extra scale beyond an exact frame fit, so tiny zooms and the mirrored " +
                 "camera never reveal an artwork edge. 1.06 crops ~3% per side.")]
        [Range(1f, 1.3f)] public float overscan = 1.06f;
        [Tooltip("Pre-flips the scene horizontally. The recording camera mirrors the " +
                 "frame, so OFF = recorded video shows the lamp on the right; ON = the " +
                 "recording matches the painted orientation (lamp left).")]
        public bool flipHorizontal = false;
        [Tooltip("Whole-artwork tint — dim or warm the base image without editing it.")]
        public Color artTint = Color.white;
        [Tooltip("World z of the artwork quad (same depth the other backdrops use).")]
        public float artDepth = 9f;
        [Tooltip("How far in FRONT of the artwork the animated layers sit.")]
        public float overlayDepthOffset = 0.1f;

        [Header("Scene Hookup")]
        [Tooltip("Leave empty to auto-use Camera.main (any camera as fallback).")]
        public Camera referenceCamera;

        [Header("Window Bokeh Lights (regions in 0-1 artwork coords)")]
        [Tooltip("Spawn zones for the extra city lights — the window panes' skyline " +
                 "band, dodging the mullion, frame and the painted books/plant.")]
        public List<Rect> cityLightRegions = new List<Rect>()
        {
            new Rect(0.46f,  0.14f, 0.265f, 0.30f),  // left pane, skyline band
            new Rect(0.775f, 0.26f, 0.175f, 0.20f),  // right pane, above the books
            new Rect(0.775f, 0.14f, 0.08f,  0.12f),  // right pane, sliver left of the books
        };
        [Range(0, 40)]
        [Tooltip("Extra animated lights over the painted ones.")]
        public int cityLightCount = 12;
        [Tooltip("Light size as fraction of SCREEN HEIGHT — matched to the painted bokeh.")]
        [Range(0.002f, 0.06f)] public float lightSizeMin = 0.006f;
        [Range(0.002f, 0.06f)] public float lightSizeMax = 0.018f;
        [Range(0f, 1f)] public float lightAlphaMin = 0.15f;
        [Range(0f, 1f)] public float lightAlphaMax = 0.35f;
        [Tooltip("Uniform random pick — duplicate entries to weight the mix (matched " +
                 "to the artwork's palette: ambers, blues, teal, the occasional red).")]
        public List<Color> cityLightColors = new List<Color>()
        {
            new Color(1f, 0.702f, 0.361f),      // amber  #FFB35C
            new Color(1f, 0.702f, 0.361f),      // amber again (weight)
            new Color(1f, 0.875f, 0.682f),      // warm white #FFDFAE
            new Color(0.435f, 0.627f, 0.910f),  // cool blue #6FA0E8
            new Color(0.435f, 0.627f, 0.910f),  // cool blue again (weight)
            new Color(0.498f, 0.847f, 0.847f),  // teal #7FD8D8
            new Color(0.878f, 0.333f, 0.282f),  // red #E05548
        };
        [Tooltip("Per-light twinkle cycle length range, seconds (random per light).")]
        public Vector2 twinkleCycleRange = new Vector2(8f, 20f);
        [Range(0f, 1f)]
        [Tooltip("Twinkle swing ± as fraction of the light's brightness.")]
        public float twinkleAmount = 0.35f;
        [Tooltip("Seconds between life-cycle swaps (random in range).")]
        public Vector2 swapIntervalRange = new Vector2(10f, 20f);
        [Tooltip("Fade-in / fade-out duration range for every light, seconds.")]
        public Vector2 fadeSecondsRange = new Vector2(3f, 5f);

        [Header("Star Twinkle (regions in 0-1 artwork coords)")]
        public List<Rect> starRegions = new List<Rect>()
        {
            new Rect(0.47f, 0.55f, 0.25f, 0.38f),   // left pane sky
            new Rect(0.78f, 0.55f, 0.17f, 0.38f),   // right pane sky
        };
        [Range(0, 20)] public int starCount = 7;
        [Range(0.001f, 0.02f)] public float starSizeMin = 0.004f;
        [Range(0.001f, 0.02f)] public float starSizeMax = 0.008f;
        [Range(0f, 1f)] public float starAlphaMin = 0.20f;
        [Range(0f, 1f)] public float starAlphaMax = 0.45f;
        public Color starColor = new Color(0.804f, 0.847f, 0.941f); // pale blue-white
        [Tooltip("Stars twinkle a little faster and deeper than the city lights.")]
        public Vector2 starTwinkleCycleRange = new Vector2(6f, 14f);
        [Range(0f, 1f)] public float starTwinkleAmount = 0.6f;

        [Header("Coffee Steam (anchor in 0-1 artwork coords)")]
        [Tooltip("Where wisps are born — just above the painted mug's rim.")]
        public Vector2 steamOrigin = new Vector2(0.428f, 0.30f);
        [Tooltip("Wisps per second. 0.16 with 6–10 s lifetimes keeps ~1 alive.")]
        public float steamRatePerSecond = 0.16f;
        public Vector2 steamLifetimeRange = new Vector2(6f, 10f);
        [Tooltip("Rise speed in world units/s — slow, mug-scale.")]
        public float steamRiseSpeed = 0.22f;
        [Range(0f, 0.2f)]
        [Tooltip("Peak wisp alpha — very low, barely there.")]
        public float steamPeakAlpha = 0.05f;
        [Tooltip("Noise curl strength (world units/s), mostly horizontal.")]
        public float steamNoiseStrength = 0.18f;
        [Tooltip("Wisp size as fraction of SCREEN HEIGHT (grows ~2.4x over its life).")]
        public Vector2 steamSizeRange = new Vector2(0.030f, 0.048f);

        [Header("Lamp Glow Breathing (anchor in 0-1 artwork coords)")]
        [Tooltip("Center of the warm pool — the lamp bulb / lit wall patch.")]
        public Vector2 lampGlowCenter = new Vector2(0.125f, 0.51f);
        public Color lampGlowColor = new Color(1f, 0.702f, 0.361f); // #FFB35C
        [Tooltip("Glow quad size as fraction of SCREEN HEIGHT.")]
        [Range(0.05f, 1f)] public float lampGlowSize = 0.40f;
        [Range(0f, 0.5f)]
        [Tooltip("Base glow alpha (additive over the already-painted glow — keep subtle).")]
        public float lampGlowIntensity = 0.12f;
        [Tooltip("Seconds per breathing cycle.")]
        public float breatheCycleSeconds = 25f;
        [Range(0f, 1f)]
        [Tooltip("Breathing swing ± as fraction of the glow intensity.")]
        public float breatheAmplitude = 0.35f;

        // -------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------

        private static readonly int PropTint = Shader.PropertyToID("_Tint");

        private Material artMaterial;
        private Transform artQuad;
        private ParticleSystem bokehPs;
        private Material bokehMaterial;
        private Texture2D bokehTexture;
        private ParticleSystem steamPs;
        private Material steamMaterial;
        private Texture2D softTexture;
        private Transform glowQuad;
        private Material glowMaterial;
        private bool glowUsesTintColor;
        private ParticleSystem.Particle[] bokehBuffer;

        // Same manual bookkeeping as NightCityBokehBackground: positions are
        // static; twinkle phases accumulate so nothing pops or syncs up.
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
            public float twinkleAmount;
            public float twinklePhase;
            public bool  isStar;             // stars twinkle but never swap
        }

        private readonly Dictionary<uint, BokehLight> lights = new Dictionary<uint, BokehLight>();
        private static readonly List<uint> scratchSeeds = new List<uint>(16);
        private uint nextLightId = 1;
        private float nextSwapAt = -1f;

        // Two incommensurate sines (golden-ratio paced) so the lamp breathing
        // never exactly repeats; phases integrated so cycle tweaks stay smooth.
        private float breathePhase;
        private float breathePhase2;

        private const float LightLifetime = 100000f;

        // -------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------

        void Start()
        {
            EnsureBuilt();
        }

        void Update()
        {
            if (artMaterial == null) return;

            // Keep the art layer honest with the Inspector (live-tweakable).
            artMaterial.SetColor(PropTint, artTint);
            PlaceArtQuad();

            // Life-cycle swap for the city lights (stars are exempt).
            if (Time.time >= nextSwapAt)
            {
                if (RetireRandomCityLight()) SpawnCityLight();
                nextSwapAt = Time.time + Random.Range(swapIntervalRange.x, swapIntervalRange.y);
            }

            // Converge populations toward the Inspector counts, one per frame,
            // always through fades.
            int aliveCity = 0, aliveStars = 0;
            foreach (var kv in lights)
            {
                if (kv.Value.retireTime >= 0f) continue;
                if (kv.Value.isStar) aliveStars++; else aliveCity++;
            }
            if (aliveCity < cityLightCount) SpawnCityLight();
            else if (aliveCity > cityLightCount) RetireRandomCityLight();
            if (aliveStars < starCount) SpawnStar();

            // Lamp glow breathing.
            float w = (2f * Mathf.PI) / Mathf.Max(breatheCycleSeconds, 0.5f);
            breathePhase  += Time.deltaTime * w;
            breathePhase2 += Time.deltaTime * w * 0.618f;
            float breathe = 1f + breatheAmplitude * (0.72f * Mathf.Sin(breathePhase)
                                                  + 0.28f * Mathf.Sin(breathePhase2));
            if (glowQuad != null)
            {
                float worldH = SafeWorldHeight();
                glowQuad.position = ArtToWorld(lampGlowCenter);
                glowQuad.localScale = Vector3.one * (lampGlowSize * worldH);
                Color c = lampGlowColor;
                c.a = Mathf.Clamp01(lampGlowIntensity * breathe);
                if (glowUsesTintColor) glowMaterial.SetColor("_TintColor", c);
                else glowMaterial.color = c;
            }

            // Steam live params (rate + anchor follow the Inspector).
            if (steamPs != null)
            {
                var emission = steamPs.emission;
                emission.rateOverTime = steamRatePerSecond;
                var shape = steamPs.shape;
                Vector3 origin = ArtToWorld(steamOrigin);
                shape.position = origin - steamPs.transform.position;
            }
        }

        void LateUpdate()
        {
            UpdateBokehLights();
        }

        void OnDestroy()
        {
            DestroyRuntimeObject(artMaterial);
            DestroyRuntimeObject(bokehMaterial);
            DestroyRuntimeObject(steamMaterial);
            DestroyRuntimeObject(glowMaterial);
            DestroyRuntimeObject(bokehTexture);
            DestroyRuntimeObject(softTexture);
        }

        static void DestroyRuntimeObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // -------------------------------------------------------------------
        // Build (runtime, once)
        // -------------------------------------------------------------------

        /// <summary>Builds every layer once. Safe to call repeatedly.</summary>
        public void EnsureBuilt()
        {
            if (artMaterial != null) return;

            if (referenceCamera == null) referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                var cams = FindObjectsOfType<Camera>();
                if (cams.Length > 0) referenceCamera = cams[0];
            }

            if (artworkTexture == null)
                artworkTexture = Resources.Load<Texture2D>("Backgrounds/CozyDeskNight");
            if (artworkTexture == null)
            {
                Debug.LogWarning("[CozyDeskNightBackground] No artwork texture assigned or found — background disabled.");
                enabled = false;
                return;
            }

            Shader artShader = Resources.Load<Shader>("Shaders/CozyDeskArt");
            if (artShader == null) artShader = Shader.Find("CozyDeskNight/Art");
            if (artShader == null)
            {
                Debug.LogWarning("[CozyDeskNightBackground] CozyDeskNight/Art shader not found — background disabled.");
                enabled = false;
                return;
            }

            artMaterial = new Material(artShader) { hideFlags = HideFlags.HideAndDontSave };
            artMaterial.mainTexture = artworkTexture;
            artMaterial.SetColor(PropTint, artTint);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CozyDeskArtQuad";
            quad.transform.SetParent(transform, false);
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyRuntimeObject(col);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial    = artMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            artQuad = quad.transform;
            PlaceArtQuad();

            bokehTexture = MakeBokehDiscTexture(96);
            softTexture  = MakeSoftGaussianTexture(128, 0.26f);

            bokehMaterial = CreateParticleMaterial(additive: true, "CozyDeskBokehParticles");
            bokehMaterial.mainTexture = bokehTexture;
            BuildBokehSystem();

            steamMaterial = CreateParticleMaterial(additive: false, "CozyDeskSteamParticles");
            steamMaterial.mainTexture = softTexture;
            BuildSteamSystem();

            BuildGlowQuad();
            PrewarmLights();
        }

        // The artwork fills the camera frame (+ overscan); a negative x scale
        // implements flipHorizontal (the shader culls Off, so winding is fine).
        private void PlaceArtQuad()
        {
            if (artQuad == null) return;
            float h = SafeWorldHeight() * overscan;
            float w = h * ArtAspect();
            Vector3 c = CamCenter();
            artQuad.position = new Vector3(c.x, c.y, artDepth);
            artQuad.localScale = new Vector3(flipHorizontal ? -w : w, h, 1f);
        }

        private void BuildBokehSystem()
        {
            var go = new GameObject("CozyDeskWindowLights");
            go.transform.SetParent(transform, false);

            bokehPs = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            bokehPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            psr.sharedMaterial = bokehMaterial;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment  = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            // Fully manual (same as NightCityBokehBackground): no modules —
            // static positions, spawning and fading driven from C#.
            var main = bokehPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = LightLifetime;
            main.maxParticles = Mathf.Max(64, (cityLightCount + starCount) * 4);

            var emission = bokehPs.emission;             emission.enabled = false;
            var shape    = bokehPs.shape;                shape.enabled    = false;
            var vel      = bokehPs.velocityOverLifetime; vel.enabled      = false;
            var noiseMod = bokehPs.noise;                noiseMod.enabled = false;
            var colMod   = bokehPs.colorOverLifetime;    colMod.enabled   = false;
            var szMod    = bokehPs.sizeOverLifetime;     szMod.enabled    = false;

            bokehBuffer = new ParticleSystem.Particle[main.maxParticles];
            bokehPs.Play(true);
        }

        private void BuildSteamSystem()
        {
            var go = new GameObject("CozyDeskSteam");
            go.transform.SetParent(transform, false);

            steamPs = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            steamPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            psr.sharedMaterial = steamMaterial;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment  = ParticleSystemRenderSpace.View;
            psr.maxParticleSize = 1f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            float worldH = SafeWorldHeight();

            var main = steamPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;      // a wisp may already be mid-rise at Start
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = Mathf.Max(1f, steamLifetimeRange.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(steamLifetimeRange.x, steamLifetimeRange.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.0001f, steamSizeRange.x * worldH),
                Mathf.Max(0.0001f, steamSizeRange.y * worldH));
            main.startColor = WithAlpha(new Color(0.847f, 0.812f, 0.769f), steamPeakAlpha);
            main.maxParticles = 8;

            var emission = steamPs.emission;
            emission.enabled = true;
            emission.rateOverTime = steamRatePerSecond;

            // Tiny box just above the painted mug rim (position re-asserted
            // every Update from steamOrigin).
            var shape = steamPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.12f, 0.05f, 0.01f);
            go.transform.position = ArtToWorld(steamOrigin);
            shape.position = Vector3.zero;

            var vel = steamPs.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(steamRiseSpeed * 0.8f, steamRiseSpeed * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var noise = steamPs.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(steamNoiseStrength);
            noise.strengthY = new ParticleSystem.MinMaxCurve(steamNoiseStrength * 0.25f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0f);
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.08f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var colOverLife = steamPs.colorOverLifetime;
            colOverLife.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                        new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colOverLife.color = new ParticleSystem.MinMaxGradient(g);

            var size = steamPs.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.4f));

            steamPs.Play(true);
        }

        private void BuildGlowQuad()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CozyDeskLampGlow";
            quad.transform.SetParent(transform, false);
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyRuntimeObject(col);

            glowMaterial = CreateParticleMaterial(additive: true, "CozyDeskLampGlowMat");
            glowMaterial.mainTexture = softTexture;
            glowUsesTintColor = glowMaterial.HasProperty("_TintColor");

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial    = glowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            glowQuad = quad.transform;
        }

        // -------------------------------------------------------------------
        // Light life-cycle (spawn / retire / per-frame pass)
        // -------------------------------------------------------------------

        private void PrewarmLights()
        {
            for (int i = 0; i < cityLightCount; i++)
                SpawnCityLight(backdateSeconds: Random.Range(0f, 15f));
            for (int i = 0; i < starCount; i++)
                SpawnStar(backdateSeconds: Random.Range(0f, 15f));
            nextSwapAt = Time.time + Random.Range(swapIntervalRange.x, swapIntervalRange.y);
        }

        private void SpawnCityLight(float backdateSeconds = 0f)
        {
            if (bokehPs == null || cityLightRegions.Count == 0) return;
            float worldH = SafeWorldHeight();

            float sizeRoll = Random.value;
            var data = new BokehLight
            {
                baseColor    = RollCityColor(),
                baseAlpha    = Mathf.Lerp(lightAlphaMin, lightAlphaMax, Random.value),
                baseSize     = Mathf.Lerp(lightSizeMin, lightSizeMax, sizeRoll * sizeRoll) * worldH,
                spawnTime    = Time.time - backdateSeconds,
                fadeInSecs   = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                fadeOutSecs  = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                twinkleCycle = Random.Range(twinkleCycleRange.x, twinkleCycleRange.y),
                twinkleAmount = twinkleAmount,
                twinklePhase = Random.Range(0f, 2f * Mathf.PI),
                isStar       = false,
            };
            EmitLight(data, SampleRegion(cityLightRegions));
        }

        private void SpawnStar(float backdateSeconds = 0f)
        {
            if (bokehPs == null || starRegions.Count == 0) return;
            float worldH = SafeWorldHeight();

            var data = new BokehLight
            {
                baseColor    = starColor,
                baseAlpha    = Mathf.Lerp(starAlphaMin, starAlphaMax, Random.value),
                baseSize     = Mathf.Lerp(starSizeMin, starSizeMax, Random.value) * worldH,
                spawnTime    = Time.time - backdateSeconds,
                fadeInSecs   = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                fadeOutSecs  = Random.Range(fadeSecondsRange.x, fadeSecondsRange.y),
                twinkleCycle = Random.Range(starTwinkleCycleRange.x, starTwinkleCycleRange.y),
                twinkleAmount = starTwinkleAmount,
                twinklePhase = Random.Range(0f, 2f * Mathf.PI),
                isStar       = true,
            };
            EmitLight(data, SampleRegion(starRegions));
        }

        private void EmitLight(BokehLight data, Vector2 artPos)
        {
            uint id = nextLightId++;
            lights[id] = data;
            var ep = new ParticleSystem.EmitParams
            {
                position = ArtToWorld(artPos),
                applyShapeToPosition = false,
                startSize = data.baseSize,
                startLifetime = LightLifetime,
                startColor = Color.clear,   // the same-frame LateUpdate pass sets the real color
                randomSeed = id,
            };
            bokehPs.Emit(ep, 1);
        }

        private bool RetireRandomCityLight()
        {
            scratchSeeds.Clear();
            foreach (var kv in lights)
                if (!kv.Value.isStar && kv.Value.retireTime < 0f) scratchSeeds.Add(kv.Key);
            if (scratchSeeds.Count == 0) return false;
            lights[scratchSeeds[Random.Range(0, scratchSeeds.Count)]].retireTime = Time.time;
            return true;
        }

        private void UpdateBokehLights()
        {
            if (bokehPs == null || bokehBuffer == null) return;
            int n = bokehPs.GetParticles(bokehBuffer);
            if (n == 0) return;

            float now = Time.time;
            float dt = Time.deltaTime;

            scratchSeeds.Clear(); // reused to collect seeds seen this pass
            for (int i = 0; i < n; i++)
            {
                if (!lights.TryGetValue(bokehBuffer[i].randomSeed, out BokehLight data))
                {
                    bokehBuffer[i].remainingLifetime = 0f;
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

                data.twinklePhase += dt * (2f * Mathf.PI / data.twinkleCycle);
                float twinkle = 1f + data.twinkleAmount * Mathf.Sin(data.twinklePhase);

                Color c = data.baseColor;
                c.a = Mathf.Clamp01(data.baseAlpha * twinkle) * envIn * envOut;
                bokehBuffer[i].startColor = c;
            }
            bokehPs.SetParticles(bokehBuffer, n);

            // Prune bookkeeping for finished lights.
            if (lights.Count > scratchSeeds.Count)
            {
                var dead = new List<uint>();
                foreach (var kv in lights)
                    if (!scratchSeeds.Contains(kv.Key)) dead.Add(kv.Key);
                foreach (var seed in dead) lights.Remove(seed);
            }
        }

        private Color RollCityColor()
        {
            if (cityLightColors.Count == 0) return Color.white;
            Color c = cityLightColors[Random.Range(0, cityLightColors.Count)];
            // Slight per-light variation so duplicates don't look cloned.
            return Color.Lerp(c, Color.white, Random.value * 0.15f);
        }

        // -------------------------------------------------------------------
        // Test hooks (same ContextMenu pattern as the sibling backgrounds)
        // -------------------------------------------------------------------

        [ContextMenu("Test: Force A Light Swap (play mode)")]
        private void TestForceSwap()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[CozyDeskNightBackground] Needs play mode."); return; }
            if (RetireRandomCityLight()) SpawnCityLight();
            Debug.Log("[CozyDeskNightBackground] Forced one light swap (3–5s crossfade).");
        }

        [ContextMenu("Test: Respawn All Lights (play mode)")]
        private void TestRespawnAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[CozyDeskNightBackground] Needs play mode."); return; }
            if (bokehPs != null) bokehPs.Clear(true);
            lights.Clear();
            PrewarmLights();
            Debug.Log("[CozyDeskNightBackground] Respawned all window lights and stars.");
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        private float ArtAspect()
        {
            if (artworkTexture == null) return 16f / 9f;
            return (float)artworkTexture.width / Mathf.Max(1, artworkTexture.height);
        }

        private Vector3 CamCenter()
        {
            return referenceCamera != null ? referenceCamera.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Normalized artwork coords (0-1, origin bottom-left) → world position
        /// on the overlay plane, honoring overscan and flipHorizontal.
        /// </summary>
        public Vector3 ArtToWorld(Vector2 art)
        {
            float h = SafeWorldHeight() * overscan;
            float w = h * ArtAspect();
            Vector3 c = CamCenter();
            float x = (art.x - 0.5f) * w * (flipHorizontal ? -1f : 1f) + c.x;
            float y = (art.y - 0.5f) * h + c.y;
            return new Vector3(x, y, artDepth - Mathf.Abs(overlayDepthOffset));
        }

        // Uniform point inside one of the rects, rects weighted by area.
        private Vector2 SampleRegion(List<Rect> regions)
        {
            float total = 0f;
            foreach (var r in regions) total += Mathf.Max(0f, r.width * r.height);
            float pick = Random.value * Mathf.Max(total, 1e-5f);
            foreach (var r in regions)
            {
                float a = Mathf.Max(0f, r.width * r.height);
                if (pick <= a || r == regions[regions.Count - 1])
                    return new Vector2(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMax));
                pick -= a;
            }
            return new Vector2(0.5f, 0.5f);
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

        // Additive for the bokeh/glow (overlapping lights sum to a brighter
        // glow), alpha-blended for the smoke-like steam. Same fallback chains
        // as the sibling backgrounds.
        private static Material CreateParticleMaterial(bool additive, string matName)
        {
            string[] candidates = additive
                ? new[]
                {
                    "Legacy Shaders/Particles/Additive",
                    "Particles/Additive",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Sprites/Default",
                    "Unlit/Transparent",
                }
                : new[]
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
            Debug.LogWarning("[CozyDeskNightBackground] No compatible particle shader found.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // Soft-edged bokeh disc (solid core, feathered rim) — reads as an
        // out-of-focus light, matching the painted window bokeh.
        private static Texture2D MakeBokehDiscTexture(int size)
        {
            var t = NewTexture(size);
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

        // Gaussian blob — used by the steam wisps and the lamp glow pool.
        private static Texture2D MakeSoftGaussianTexture(int size, float sigmaFrac)
        {
            var t = NewTexture(size);
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

        private static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}

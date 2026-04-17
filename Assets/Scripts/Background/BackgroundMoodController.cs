using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Drives the BackgroundAmbient shader's exposed properties to transition
    /// between 5 mood presets. C# owns the lerp timing; the shader just
    /// responds to per-frame property changes.
    ///
    /// Setup:
    ///   1. Assign `backgroundMaterial` to the material that uses Custom/BackgroundAmbient.
    ///   2. Edit mood preset values in the Inspector if you want to tweak.
    ///   3. Call SetMood(MoodType.Tense, 3.0f) from anywhere in your pipeline.
    /// </summary>
    public class BackgroundMoodController : MonoBehaviour
    {
        public enum MoodType
        {
            CalmNeutral,    // default, ~60% of airtime — exposition
            Energetic,      // analysis, positive arguments
            TenseDramatic,  // escalation, serious topics
            PlayfulLight,   // humor, light segments
            MinimalFocus,   // breather scenes, dense foreground
        }

        [System.Serializable]
        public class MoodSettings
        {
            public MoodType mood;
            public Color colorCool = new Color(0.784f, 0.831f, 0.941f, 1f);
            public Color colorWarm = new Color(0.831f, 0.784f, 0.910f, 1f);
            [Range(0.8f, 1.0f)] public float centerBrightness = 0.97f;
            [Range(0f, 2f)]    public float ribbonOpacity = 1f;
            [Range(0f, 3f)]    public float ribbonSpeed = 1f;

            [Header("Scrolling Shape Layer")]
            [Range(0f, 3f)] public float shapeSpeed   = 1f;
            [Range(0f, 3f)] public float shapeOpacity = 1f;
            [Range(0f, 3f)] public float shapeDensity = 1f;
        }

        [Header("Material")]
        [Tooltip("Material using the Custom/BackgroundAmbient shader.")]
        public Material backgroundMaterial;

        [Tooltip("Optional. If assigned, mood transitions also drive the scrolling shape layer.")]
        public ScrollingShapeController scrollingShapes;

        [Tooltip("Starting mood. Applied instantly (no transition) on Start.")]
        public MoodType startingMood = MoodType.CalmNeutral;

        [Header("Mood Presets")]
        [Tooltip("One entry per mood. The values here override the shader's inspector values " +
                 "when the mood is activated.")]
        public List<MoodSettings> presets = new List<MoodSettings>()
        {
            new MoodSettings {
                mood = MoodType.CalmNeutral,
                colorCool = Hex("#C8D4F0"),
                colorWarm = Hex("#D4C8E8"),
                centerBrightness = 0.97f,
                ribbonOpacity = 1.0f,
                ribbonSpeed = 1.0f,
                shapeSpeed = 1.0f, shapeOpacity = 1.0f, shapeDensity = 1.0f,
            },
            new MoodSettings {
                mood = MoodType.Energetic,
                colorCool = Hex("#C8D8F0"),
                colorWarm = Hex("#E0D0E8"),
                centerBrightness = 0.98f,
                ribbonOpacity = 1.1f,
                ribbonSpeed = 1.4f,
                shapeSpeed = 1.4f, shapeOpacity = 1.2f, shapeDensity = 1.2f,
            },
            new MoodSettings {
                mood = MoodType.TenseDramatic,
                colorCool = Hex("#B8C0DC"),
                colorWarm = Hex("#C8B0D0"),
                centerBrightness = 0.90f,
                ribbonOpacity = 1.3f,
                ribbonSpeed = 0.7f,
                shapeSpeed = 0.6f, shapeOpacity = 1.4f, shapeDensity = 0.8f,
            },
            new MoodSettings {
                mood = MoodType.PlayfulLight,
                colorCool = Hex("#D0E0F8"),
                colorWarm = Hex("#E8D8F0"),
                centerBrightness = 0.99f,
                ribbonOpacity = 0.9f,
                ribbonSpeed = 1.6f,
                shapeSpeed = 1.6f, shapeOpacity = 0.9f, shapeDensity = 1.3f,
            },
            new MoodSettings {
                mood = MoodType.MinimalFocus,
                colorCool = Hex("#D8DCF0"),
                colorWarm = Hex("#DCD8EC"),
                centerBrightness = 0.95f,
                ribbonOpacity = 0.4f,
                ribbonSpeed = 0.5f,
                shapeSpeed = 0.5f, shapeOpacity = 0.4f, shapeDensity = 0.6f,
            },
        };

        // Shader property IDs (cached for speed)
        private static readonly int PropCool       = Shader.PropertyToID("_ColorCool");
        private static readonly int PropWarm       = Shader.PropertyToID("_ColorWarm");
        private static readonly int PropBrightness = Shader.PropertyToID("_CenterBrightness");
        private static readonly int PropOpacity    = Shader.PropertyToID("_RibbonOpacity");
        private static readonly int PropSpeed      = Shader.PropertyToID("_RibbonSpeed");

        private MoodType currentMood;
        private Coroutine transitionCoroutine;

        // -------------------------------------------------------------------

        void Start()
        {
            if (backgroundMaterial == null)
            {
                Debug.LogError("[BackgroundMoodController] backgroundMaterial is not assigned!");
                return;
            }
            ApplyMoodInstant(startingMood);
        }

        /// <summary>
        /// Smoothly transition to the given mood over `transitionDuration` seconds.
        /// Interrupts any in-flight transition.
        /// </summary>
        public void SetMood(MoodType mood, float transitionDuration = 3f)
        {
            if (backgroundMaterial == null) return;
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            transitionCoroutine = StartCoroutine(TransitionTo(mood, Mathf.Max(0.01f, transitionDuration)));
        }

        /// <summary>
        /// Instantly snap to the given mood with no transition.
        /// </summary>
        public void ApplyMoodInstant(MoodType mood)
        {
            var preset = GetPreset(mood);
            if (preset == null) return;
            currentMood = mood;
            backgroundMaterial.SetColor(PropCool, preset.colorCool);
            backgroundMaterial.SetColor(PropWarm, preset.colorWarm);
            backgroundMaterial.SetFloat(PropBrightness, preset.centerBrightness);
            backgroundMaterial.SetFloat(PropOpacity, preset.ribbonOpacity);
            backgroundMaterial.SetFloat(PropSpeed, preset.ribbonSpeed);

            if (scrollingShapes != null)
            {
                scrollingShapes.SetSpeedMultiplier(preset.shapeSpeed);
                scrollingShapes.SetOpacityMultiplier(preset.shapeOpacity);
                scrollingShapes.SetDensityMultiplier(preset.shapeDensity);
            }
        }

        private IEnumerator TransitionTo(MoodType target, float duration)
        {
            var targetPreset = GetPreset(target);
            if (targetPreset == null) yield break;

            // Capture starting values directly from the material so we lerp
            // correctly even if someone poked the values from outside.
            Color startCool       = backgroundMaterial.GetColor(PropCool);
            Color startWarm       = backgroundMaterial.GetColor(PropWarm);
            float startBrightness = backgroundMaterial.GetFloat(PropBrightness);
            float startOpacity    = backgroundMaterial.GetFloat(PropOpacity);
            float startSpeed      = backgroundMaterial.GetFloat(PropSpeed);

            // Capture scroll-layer starting values too (if wired up).
            float startShapeSpeed = scrollingShapes != null ? scrollingShapes.speedMultiplier : 1f;
            float startShapeOpac  = scrollingShapes != null ? scrollingShapes.opacityMultiplier : 1f;
            float startShapeDens  = scrollingShapes != null ? scrollingShapes.densityMultiplier : 1f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

                backgroundMaterial.SetColor(PropCool,      Color.Lerp(startCool, targetPreset.colorCool, t));
                backgroundMaterial.SetColor(PropWarm,      Color.Lerp(startWarm, targetPreset.colorWarm, t));
                backgroundMaterial.SetFloat(PropBrightness, Mathf.Lerp(startBrightness, targetPreset.centerBrightness, t));
                backgroundMaterial.SetFloat(PropOpacity,    Mathf.Lerp(startOpacity,    targetPreset.ribbonOpacity,    t));
                backgroundMaterial.SetFloat(PropSpeed,      Mathf.Lerp(startSpeed,      targetPreset.ribbonSpeed,      t));

                if (scrollingShapes != null)
                {
                    scrollingShapes.SetSpeedMultiplier  (Mathf.Lerp(startShapeSpeed, targetPreset.shapeSpeed,   t));
                    scrollingShapes.SetOpacityMultiplier(Mathf.Lerp(startShapeOpac,  targetPreset.shapeOpacity, t));
                    scrollingShapes.SetDensityMultiplier(Mathf.Lerp(startShapeDens,  targetPreset.shapeDensity, t));
                }

                yield return null;
            }

            // Snap to exact target values to avoid drift.
            ApplyMoodInstant(target);
            transitionCoroutine = null;
        }

        private MoodSettings GetPreset(MoodType mood)
        {
            foreach (var p in presets)
                if (p.mood == mood) return p;
            Debug.LogWarning($"[BackgroundMoodController] No preset defined for mood '{mood}'.");
            return null;
        }

        // -------------------------------------------------------------------

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;
        }

        /// <summary>Current mood (last one applied or transitioned to).</summary>
        public MoodType CurrentMood => currentMood;
    }
}

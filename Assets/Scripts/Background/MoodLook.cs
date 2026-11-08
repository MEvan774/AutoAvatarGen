using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// What one {Mood:X} looks like on the Violet Doodles backdrop, minus the
    /// background picture itself (that is a texture, kept on
    /// <see cref="MoodBackgroundSet"/>). Field names follow the "runtime"
    /// blocks of MugsTech's MediaFiles/Backgrounds/Moods/moods.json so the two
    /// are easy to keep in sync.
    /// </summary>
    [System.Serializable]
    public struct MoodProfile
    {
        [Tooltip("Recolour the floating doodles toward Icon Tint. Off = they keep their painted lavender (Calm).")]
        public bool tintIcons;
        [Tooltip("Hue/chroma the doodles take on. Their brightness (depth darkening, painted shading) is kept.")]
        public Color iconTint;
        [Range(0f, 1f)]
        [Tooltip("Multiplier on the doodles' opacity.")]
        public float iconAlpha;
        [Tooltip("Multiplier on doodle motion (drift, wander, bob, sway) and on the dust particles' simulation speed.")]
        public float particleSpeed;
        [Tooltip("Multiplier on the dust particles' emission rate.")]
        public float particleEmission;
        [Range(0f, 1f)]
        [Tooltip("Extra edge darkening on top of the rig's own vignette: 0 = as authored, 1 = fully dark corners.")]
        public float vignette;

        public static MoodProfile Neutral => new MoodProfile
        {
            tintIcons = false, iconTint = Color.white, iconAlpha = 1f,
            particleSpeed = 1f, particleEmission = 1f, vignette = 0f,
        };
    }

    /// <summary>
    /// Pure helpers behind the mood crossfade — no scene access, so
    /// TtsPipelineSelfTests can check them offline.
    /// </summary>
    public static class MoodLook
    {
        public const int MoodCount = 5;

        /// <summary>The script's own name for a mood (what {Mood:X} spells).</summary>
        public static string ScriptName(BackgroundMoodController.MoodType mood)
        {
            switch (mood)
            {
                case BackgroundMoodController.MoodType.CalmNeutral:   return "Calm";
                case BackgroundMoodController.MoodType.Energetic:     return "Energetic";
                case BackgroundMoodController.MoodType.TenseDramatic: return "Tense";
                case BackgroundMoodController.MoodType.PlayfulLight:  return "Playful";
                case BackgroundMoodController.MoodType.MinimalFocus:  return "Minimal";
                default:                                              return mood.ToString();
            }
        }

        /// <summary>
        /// Built-in look per mood — moods.json's "runtime" blocks as of
        /// 2026-11-08. Used when Resources/MoodBackgrounds.asset is missing; the
        /// asset carries the same numbers and is the place to tune them.
        /// </summary>
        public static MoodProfile DefaultProfile(BackgroundMoodController.MoodType mood)
        {
            switch (mood)
            {
                case BackgroundMoodController.MoodType.Energetic:
                    return Profile(Rgb(0xff, 0xb8, 0xe0), 1.0f, 1.4f, 1.3f, 0.0f);
                case BackgroundMoodController.MoodType.TenseDramatic:
                    return Profile(Rgb(0xa7, 0xb0, 0xc4), 0.6f, 0.7f, 0.8f, 0.35f);
                case BackgroundMoodController.MoodType.PlayfulLight:
                    return Profile(Rgb(0xb8, 0xff, 0xf0), 1.0f, 1.2f, 1.2f, 0.0f);
                case BackgroundMoodController.MoodType.MinimalFocus:
                    return Profile(Rgb(0xcf, 0xd5, 0xdf), 0.7f, 0.6f, 0.5f, 0.2f);
                default:
                    return MoodProfile.Neutral;   // Calm: the artwork as painted
            }
        }

        static MoodProfile Profile(Color tint, float alpha, float speed, float emission, float vignette)
            => new MoodProfile
            {
                tintIcons = true, iconTint = tint, iconAlpha = alpha,
                particleSpeed = speed, particleEmission = emission, vignette = vignette,
            };

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        /// <summary>
        /// Uniform scale that makes a <paramref name="srcW"/>×<paramref name="srcH"/>
        /// picture cover a <paramref name="frameW"/>×<paramref name="frameH"/>
        /// area, centre-cropping the overflow: max of the two axis ratios, the
        /// same factor on both axes, so nothing is ever stretched. Any aspect
        /// (16:9, 3:2, 4:3, 21:9) lands fully covering the frame.
        /// </summary>
        public static float CoverScale(float srcW, float srcH, float frameW, float frameH)
        {
            if (srcW <= 0f || srcH <= 0f) return 1f;
            return Mathf.Max(frameW / srcW, frameH / srcH);
        }

        /// <summary>
        /// The vignette's corner alpha for a mood: <paramref name="extra"/> of
        /// the way from the rig's authored alpha to fully dark. 0 = unchanged.
        /// </summary>
        public static float VignetteAlpha(float authoredAlpha, float extra)
        {
            extra = Mathf.Clamp01(extra);
            return authoredAlpha + extra * (1f - authoredAlpha);
        }

        /// <summary>
        /// Weighted mix of profiles — what the doodles, dust and vignette show
        /// while the background is part-way through a crossfade. The tint is
        /// averaged over the tinting moods only, and <paramref name="tintAmount"/>
        /// says how much of it applies (0 = painted colours, 1 = fully recoloured).
        /// </summary>
        public static MoodProfile Blend(IList<MoodProfile> profiles, IList<float> weights, out float tintAmount)
        {
            float total = 0f, tintWeight = 0f;
            float r = 0f, g = 0f, b = 0f, alpha = 0f, speed = 0f, emission = 0f, vignette = 0f;
            for (int i = 0; i < profiles.Count; i++)
            {
                float w = weights[i];
                MoodProfile p = profiles[i];
                total    += w;
                alpha    += w * p.iconAlpha;
                speed    += w * p.particleSpeed;
                emission += w * p.particleEmission;
                vignette += w * p.vignette;
                if (p.tintIcons)
                {
                    tintWeight += w;
                    r += w * p.iconTint.r; g += w * p.iconTint.g; b += w * p.iconTint.b;
                }
            }

            if (total <= 0f) { tintAmount = 0f; return MoodProfile.Neutral; }

            tintAmount = Mathf.Clamp01(tintWeight / total);
            Color tint = tintWeight > 0f ? new Color(r / tintWeight, g / tintWeight, b / tintWeight, 1f) : Color.white;
            return new MoodProfile
            {
                tintIcons        = tintWeight > 0f,
                iconTint         = tint,
                iconAlpha        = alpha / total,
                particleSpeed    = speed / total,
                particleEmission = emission / total,
                vignette         = vignette / total,
            };
        }
    }

    /// <summary>
    /// The crossfade as a stack of layers, bottom first. The bottom layer is
    /// always fully opaque; each newer mood is a layer on top fading in from 0.
    /// What the viewer sees is the usual "over" composite, so a mood arriving
    /// mid-crossfade simply fades in over whatever is on screen right now —
    /// never a pop. Once a layer reaches full opacity everything under it is
    /// hidden and gets dropped.
    ///
    /// Pure: <see cref="MoodLayerStack.Weight"/> is all the renderer needs
    /// (one SpriteRenderer per layer, alpha = layer alpha; the self-tests check
    /// the composite through the weights).
    /// </summary>
    public sealed class MoodLayerStack
    {
        public struct Layer
        {
            public BackgroundMoodController.MoodType Mood;
            public float Alpha;   // 0..1, this layer's own opacity
            public float Rate;    // alpha per second while fading in; 0 = settled
        }

        readonly List<Layer> layers = new List<Layer>();

        public MoodLayerStack(BackgroundMoodController.MoodType initial) => Reset(initial);

        public int Count => layers.Count;
        public Layer this[int i] => layers[i];

        /// <summary>The mood being shown or faded toward (the top layer).</summary>
        public BackgroundMoodController.MoodType Target => layers[layers.Count - 1].Mood;

        public bool IsFading => layers.Count > 1;

        /// <summary>Snap to one mood: a single opaque layer, no fade in flight.</summary>
        public void Reset(BackgroundMoodController.MoodType mood)
        {
            layers.Clear();
            layers.Add(new Layer { Mood = mood, Alpha = 1f, Rate = 0f });
        }

        /// <summary>
        /// Start fading to <paramref name="mood"/> over <paramref name="seconds"/>.
        /// Returns false (and changes nothing) when it is already the target.
        /// A non-positive duration snaps.
        /// </summary>
        public bool Push(BackgroundMoodController.MoodType mood, float seconds)
        {
            if (Target == mood) return false;
            if (seconds <= 0f) { Reset(mood); return true; }

            // Freeze whatever is mid-fade: the picture on screen right now
            // becomes the still starting point of the new fade, so it is one
            // plain A-to-B crossfade from here (never two fades stacked, which
            // could move faster than a normal crossfade does).
            for (int i = 0; i < layers.Count; i++)
            {
                Layer l = layers[i];
                l.Rate = 0f;
                layers[i] = l;
            }
            layers.Add(new Layer { Mood = mood, Alpha = 0f, Rate = 1f / seconds });
            return true;
        }

        /// <summary>Advance every fading layer by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            if (dt <= 0f || layers.Count <= 1) return;

            int topOpaque = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                Layer l = layers[i];
                if (l.Rate > 0f)
                {
                    l.Alpha = Mathf.Min(1f, l.Alpha + l.Rate * dt);
                    if (l.Alpha >= 1f) l.Rate = 0f;
                    layers[i] = l;
                }
                if (l.Alpha >= 1f) topOpaque = i;
            }

            if (topOpaque > 0) layers.RemoveRange(0, topOpaque);
        }

        /// <summary>
        /// How much layer <paramref name="i"/> contributes to the picture:
        /// its alpha times the transparency of every layer above it. The
        /// weights always sum to 1 (the bottom layer is opaque).
        /// </summary>
        public float Weight(int i)
        {
            float w = layers[i].Alpha;
            for (int j = i + 1; j < layers.Count; j++) w *= 1f - layers[j].Alpha;
            return w;
        }
    }
}

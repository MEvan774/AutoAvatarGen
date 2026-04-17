using UnityEngine;
using TMPro;

namespace MugsTech.Style
{
    /// <summary>
    /// A channel-specific visual preset. Holds every styling knob that
    /// content cards, typography, and entrance animations should respect.
    ///
    /// Create via right-click in the Project window:
    ///   Create > MugsTech > Channel Style Preset
    ///
    /// Edit any field directly in the Inspector. To make a preset active,
    /// either drag it into the StyleManager component's `Default Preset`
    /// field or use the editor window (MugsTech > Style > Style Presets).
    ///
    /// JSON serialization (`ToJson` / `FromJsonOverwrite`) preserves all
    /// numeric, color, enum, and string values. Unity Object references
    /// (`headlineFont`, `accentDecorations`) are NOT preserved across
    /// projects since they're internal asset GUIDs.
    /// </summary>
    [CreateAssetMenu(fileName = "ChannelStylePreset", menuName = "MugsTech/Channel Style Preset")]
    public class ChannelStylePreset : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Human-readable channel name shown in the editor window.")]
        public string channelName = "New Channel";
        [Tooltip("Short identifier used in logs / file names.")]
        public string identifier = "channel";

        [Header("Card Style")]
        [Tooltip("Background color applied to all card panels (alpha is overridden by `opacity` below).")]
        public Color cardBackgroundColor = new Color(250f / 255f, 243f / 255f, 224f / 255f, 1f); // #FAF3E0
        [Range(0f, 32f)]
        [Tooltip("Corner radius in pixels. 4 = sharp corporate, 22 = soft whimsical.")]
        public float cornerRadiusPx = 18f;
        [Range(0.5f, 1f)]
        [Tooltip("Final card opacity. Lower values let the background bleed through.")]
        public float opacity = 0.9f;
        [Range(0f, 1f)]
        [Tooltip("Soft drop shadow strength (0 = none, 1 = strong diffuse).")]
        public float shadowSoftness = 0.5f;
        [Tooltip("Random Z-rotation range applied per card spawn (in degrees).")]
        public Vector2 rotationVarianceRange = new Vector2(-3f, 3f);
        [Range(0f, 1f)]
        [Tooltip("Wobble intensity on entry. 0 = no overshoot, 1 = strong elastic bounce.")]
        public float wobbleIntensity = 0.6f;

        [Header("Typography")]
        [Tooltip("Headline font. FLAGGED: leave null to use TMP default. Replace with your casual-weighted font.")]
        public TMP_FontAsset headlineFont;
        [Tooltip("Headline font size in pixels (1080p reference).")]
        public float headlineSize = 48f;
        [Tooltip("Body font size in pixels (1080p reference).")]
        public float bodySize = 28f;
        [Tooltip("Accent color used for highlights, underlines, and decorations.")]
        public Color accentColor = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 1f); // brand red
        [Tooltip("If false, no decorative elements are rendered (stars, underlines, circles).")]
        public bool accentDecorationsEnabled = true;
        [Tooltip("Optional override sprites for accent decorations. If empty, procedural fallbacks are used. " +
                 "FLAGGED: drop in your hand-drawn / stamped sprites here.")]
        public Sprite[] accentDecorations;

        [Header("Card Entrance")]
        [Tooltip("How the entry start position is chosen.")]
        public EntryDirectionMode entryDirection = EntryDirectionMode.CharacterFacing;
        [Tooltip("Easing curve for the slide-in animation.")]
        public EntryAnimationCurve entryCurve = EntryAnimationCurve.Elastic;

        [Header("Background Mood")]
        [Tooltip("Name of the background mood variant to load on scene start. " +
                 "FLAGGED: applied via your existing background system if you have one; ignored otherwise.")]
        public string defaultBackgroundMoodName = "";

        [Header("Register")]
        [Tooltip("High-level register tag. Use this in code for quick conditional logic.")]
        public StyleRegister register = StyleRegister.Whimsical;

        // -------------------------------------------------------------------
        // JSON helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Serializes this preset's values to a JSON string. Object references
        /// (font, sprites) are stored as instance IDs and won't carry across
        /// projects — re-assign them after import.
        /// </summary>
        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        /// <summary>
        /// Overwrites this preset's values with the given JSON. Object
        /// references that don't resolve in the current project will
        /// fall back to null.
        /// </summary>
        public void FromJson(string json)
        {
            JsonUtility.FromJsonOverwrite(json, this);
        }

        /// <summary>
        /// Returns a random Z rotation in degrees within `rotationVarianceRange`.
        /// </summary>
        public float GetRandomEntryRotation()
        {
            return Random.Range(rotationVarianceRange.x, rotationVarianceRange.y);
        }
    }
}

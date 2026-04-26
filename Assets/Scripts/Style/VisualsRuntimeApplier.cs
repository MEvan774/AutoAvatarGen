using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MugsTech.Style
{
    /// <summary>
    /// Bridge between the user-edited VisualsSave (per-emotion character images
    /// + card style) and the runtime systems that consume them. On every scene
    /// load, the active save (selected from the main menu) is loaded; embedded
    /// emotion images are decoded and pushed into HybridAvatarSystem; card
    /// background color and corner radius are pushed into the active
    /// ChannelStylePreset so ContentCard panels reflect the user's choices.
    ///
    /// The user's chosen text color and font style aren't representable in
    /// ChannelStylePreset (text color is luminance-derived, font style is
    /// hardcoded per card), so they're exposed as static overrides that
    /// ContentCardUIBuilder consults when building text elements.
    ///
    /// No scene wiring required — this hooks itself onto SceneManager.sceneLoaded
    /// at startup via [RuntimeInitializeOnLoadMethod].
    /// </summary>
    public static class VisualsRuntimeApplier
    {
        // Override slots read by ContentCardUIBuilder. Null = no override (use
        // the existing preset-derived value or the static default).
        public static Color?        CardTextColorOverride;
        public static FontStyle?    CardFontStyleOverride;
        public static TMP_FontAsset CardFontOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            // AfterSceneLoad fires after the first scene's Awake/Start, so
            // sceneLoaded won't be raised for it — apply once explicitly.
            ApplyToActiveScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToActiveScene();

        public static void ApplyToActiveScene()
        {
            VisualsSaveFile save = LoadActiveSave();

            // Reset overrides to a clean slate — leftover static state from a
            // previous scene/recording would otherwise bleed in if the user
            // switched their active save to "(none)".
            CardTextColorOverride = null;
            CardFontStyleOverride = null;
            CardFontOverride      = null;

            if (save == null) return;

            ApplyCardStyle(save);
            ApplyAvatarSprites(save);
        }

        /// <summary>
        /// Returns the named save selected as active in the main menu, or null
        /// when "(none)" is selected (or the named file no longer exists).
        /// </summary>
        static VisualsSaveFile LoadActiveSave()
        {
            string activeName = PlayerPrefs.GetString(VisualsMenuController.ActiveSaveNameKey, "");
            if (string.IsNullOrEmpty(activeName)) return null;
            return VisualsSaveStore.Load(activeName);
        }

        // -------------------------------------------------------------------
        // Card style → StyleManager.ActivePreset
        // -------------------------------------------------------------------

        static void ApplyCardStyle(VisualsSaveFile save)
        {
            CardTextColorOverride = TryParseHex(save.card.textColorHex);
            CardFontStyleOverride = (FontStyle)save.card.fontStyle;
            CardFontOverride      = FontRegistry.Resolve(save.card.fontName)?.Asset;

            // Card bg color + corner radius are already first-class fields on
            // ChannelStylePreset, so we push them through the existing pipeline
            // by cloning the active preset, overwriting those two fields, and
            // re-loading. ContentCardUIBuilder.CreateBackground will then read
            // the user's values.
            StyleManager sm = StyleManager.Instance;
            if (sm == null) return;

            ChannelStylePreset target = ScriptableObject.CreateInstance<ChannelStylePreset>();
            ChannelStylePreset source = sm.ActivePreset;
            if (source != null)
            {
                target.FromJson(source.ToJson(false));
                // JsonUtility doesn't preserve object refs — copy them across
                // so headline font / accent decorations survive the clone.
                target.headlineFont      = source.headlineFont;
                target.accentDecorations = source.accentDecorations;
            }
            else
            {
                target.channelName = "VisualsSave";
                target.identifier  = "visuals-save";
            }

            Color? bg = TryParseHex(save.card.bgColorHex);
            if (bg.HasValue) target.cardBackgroundColor = bg.Value;
            target.cornerRadiusPx = save.card.cornerRadius;

            sm.LoadPreset(target);
        }

        // -------------------------------------------------------------------
        // Per-emotion sprites → HybridAvatarSystem
        // -------------------------------------------------------------------

        static void ApplyAvatarSprites(VisualsSaveFile save)
        {
            HybridAvatarSystem avatar = UnityEngine.Object.FindObjectOfType<HybridAvatarSystem>();
            if (avatar == null) return;

            var overrides = new Dictionary<string, Sprite>();
            foreach (EmotionImageData emo in save.emotions)
            {
                Sprite spr = LoadEmotionSprite(emo);
                if (spr != null) overrides[emo.emotion] = spr;
            }

            if (overrides.Count > 0)
                avatar.ApplyEmotionOverrides(overrides);
        }

        static Sprite LoadEmotionSprite(EmotionImageData emo)
        {
            byte[] bytes = null;

            // Prefer the live disk path (so source-file edits propagate);
            // fall back to the embedded base64 if the path no longer resolves.
            if (!string.IsNullOrEmpty(emo.originalPath) && File.Exists(emo.originalPath))
            {
                try { bytes = File.ReadAllBytes(emo.originalPath); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsRuntimeApplier] Read failed for {emo.emotion}: {e.Message}");
                }
            }
            if (bytes == null && !string.IsNullOrEmpty(emo.imageBase64))
            {
                try { bytes = Convert.FromBase64String(emo.imageBase64); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsRuntimeApplier] base64 decode failed for {emo.emotion}: {e.Message}");
                }
            }
            if (bytes == null) return null;

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        static Color? TryParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out Color c) ? (Color?)c : null;
        }
    }
}

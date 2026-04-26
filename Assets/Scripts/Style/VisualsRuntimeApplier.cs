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

        /// <summary>
        /// BigText overlay style, read by BigTextCard. Active = HasValue;
        /// when null/false, the card uses its hardcoded defaults.
        /// </summary>
        public static class BigText
        {
            public static Color?    TextColor;
            public static FontStyle FontStyle         = UnityEngine.FontStyle.Bold;
            public static Color?    OutlineColor;
            public static float     OutlineWidth      = 0.10f;
            public static bool      ShadowEnabled;
            public static Color     ShadowColor       = new Color(0f, 0f, 0f, 0.75f);
            public static float     ShadowSoftness    = 0.5f;
            public static bool      BackgroundEnabled;
            public static Color     BackgroundColor   = new Color(0f, 0f, 0f, 0.6f);
            public static float     BackgroundCornerRadius = 18f;

            public static void Reset()
            {
                TextColor              = null;
                FontStyle              = UnityEngine.FontStyle.Bold;
                OutlineColor           = null;
                OutlineWidth           = 0.10f;
                ShadowEnabled          = false;
                ShadowColor            = new Color(0f, 0f, 0f, 0.75f);
                ShadowSoftness         = 0.5f;
                BackgroundEnabled      = false;
                BackgroundColor        = new Color(0f, 0f, 0f, 0.6f);
                BackgroundCornerRadius = 18f;
            }
        }

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
            string activeName = PlayerPrefs.GetString(VisualsMenuController.ActiveSaveNameKey, "");
            string sceneName  = SceneManager.GetActiveScene().name;
            Debug.Log($"[BgVideoDiag] VisualsRuntimeApplier.ApplyToActiveScene scene='{sceneName}' " +
                      $"activeSaveName='{activeName}'");
            VisualsSaveFile save = LoadActiveSave();

            // Reset overrides to a clean slate — leftover static state from a
            // previous scene/recording would otherwise bleed in if the user
            // switched their active save to "(none)".
            CardTextColorOverride = null;
            CardFontStyleOverride = null;
            CardFontOverride      = null;
            BigText.Reset();
            // Clear the preset video pref unconditionally — ApplyBackgroundVideo
            // (below) re-writes it from whichever source has a path.
            PlayerPrefs.DeleteKey(BackgroundVideoLoop.PresetPathPrefKey);

            if (save != null)
            {
                ApplyCardStyle(save);
                ApplyBigTextStyle(save);
                ApplyAvatarSprites(save);
            }

            // Bg video is intentionally not preset-bound: even without an
            // active named save, a path picked in the visuals menu should
            // apply at recording time. Active save's path wins; otherwise
            // fall back to the visuals-menu working state PlayerPref.
            ApplyBackgroundVideo(save);
            PlayerPrefs.Save();

            // Explicitly hand off to the override hijacker AFTER the prefs are
            // written, so ordering relative to its own sceneLoaded subscription
            // doesn't matter — by the time it runs here, PresetPathPrefKey is
            // current.
            MugsTech.Background.BackgroundVideoOverride.ApplyToActiveScene();
        }

        static void ApplyBackgroundVideo(VisualsSaveFile save)
        {
            // Active save is authoritative — its path wins even when empty
            // (an empty path on an active save means "no override"). When no
            // save is active, fall through to the visuals-menu working state
            // so the user's pick still applies without requiring Save As.
            string source;
            string path;
            if (save != null)
            {
                source = $"save '{save.name}'";
                path   = save.backgroundVideoPath;
            }
            else
            {
                source = "PlayerPrefs working state";
                path   = PlayerPrefs.GetString(VisualsMenuController.BgVideoPathKey, "");
            }

            Debug.Log($"[BgVideoDiag] ApplyBackgroundVideo: source={source} path='{path}'");

            if (!string.IsNullOrWhiteSpace(path))
            {
                PlayerPrefs.SetString(BackgroundVideoLoop.PresetPathPrefKey, path.Trim());
                Debug.Log($"[BgVideoDiag]   wrote PresetPathPrefKey='{path.Trim()}'");
            }
            else
            {
                Debug.Log("[BgVideoDiag]   no path to write; PresetPathPrefKey stays cleared.");
            }
        }

        static void ApplyBigTextStyle(VisualsSaveFile save)
        {
            BigTextStyleData s = save.bigText;
            if (s == null) return;

            BigText.TextColor              = TryParseHex(s.textColorHex);
            BigText.FontStyle              = (FontStyle)s.fontStyle;
            BigText.OutlineColor           = TryParseHex(s.outlineColorHex);
            BigText.OutlineWidth           = s.outlineWidth;
            BigText.ShadowEnabled          = s.shadowEnabled;
            if (TryParseHex(s.shadowColorHex) is Color sc) BigText.ShadowColor = sc;
            BigText.ShadowSoftness         = s.shadowSoftness;
            BigText.BackgroundEnabled      = s.backgroundEnabled;
            if (TryParseHex(s.backgroundColorHex) is Color bc) BigText.BackgroundColor = bc;
            BigText.BackgroundCornerRadius = s.backgroundCornerRadius;
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

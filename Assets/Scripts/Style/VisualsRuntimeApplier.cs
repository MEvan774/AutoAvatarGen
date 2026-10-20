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

        /// <summary>
        /// Fullscreen headline (BigCenterCard) style, read by the card
        /// when it builds. Reset = the card's original hard-coded look, which
        /// is also what renders when no named save is active.
        /// </summary>
        public static class BigHeadline
        {
            public static BigHeadlineLook Look = BigHeadlineLook.Legacy;

            public static void Reset() => Look = BigHeadlineLook.Legacy;
        }

        // Pattern texture decoded from the active save, kept across applies
        // so the menu → recording scene swap doesn't decode the same image
        // twice. Keyed by the embedded bytes (or the path when nothing is
        // embedded).
        static string    s_PatternKey;
        static Texture2D s_PatternTexture;

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
            BigHeadline.Reset();
            // Clear preset prefs unconditionally — the Apply* methods below
            // re-write them from whichever source has a value.
            PlayerPrefs.DeleteKey(MugsTech.Background.BackgroundVideoOverride.PresetPathPrefKey);
            PlayerPrefs.DeleteKey(MugsTech.Background.BackgroundMusicPlayer.PresetListPrefKey);
            PlayerPrefs.DeleteKey(MugsTech.Background.BackgroundMusicPlayer.PresetVolumePrefKey);

            if (save != null)
            {
                ApplyCardStyle(save);
                ApplyBigTextStyle(save);
                ApplyBigHeadlineStyle(save);
                ApplyAvatarSprites(save);
            }
            else
            {
                // Even without a named save, user-added emotions from the
                // visuals menu's Quick Save should still flow into the
                // recording — same fallback pattern as background music and
                // background video below. Without this, a user who added a
                // new emotion and used it in their script wouldn't see the
                // sprite swap unless they also did Save As.
                ApplyAvatarSpritesFromWorkingPrefs();
            }

            // Bg video and music are intentionally not strictly preset-bound:
            // even without an active named save, a path/playlist picked in
            // the visuals menu should apply at recording time.
            ApplyBackgroundVideo(save);
            ApplyBackgroundMusic(save);
            PlayerPrefs.Save();

            // Explicitly hand off to the runtime hijackers AFTER the prefs are
            // written, so ordering relative to their own sceneLoaded callbacks
            // doesn't matter — by the time they run here, the prefs are current.
            MugsTech.Background.BackgroundVideoOverride.ApplyToActiveScene();
            MugsTech.Background.BackgroundMusicPlayer.ApplyToActiveScene();
        }

        static void ApplyBackgroundMusic(VisualsSaveFile save)
        {
            // Active save's playlist is authoritative; otherwise fall back to
            // the visuals-menu working state PlayerPref so untitled edits
            // still apply.
            string serializedList;
            float  volume;
            string source;
            if (save != null && save.music != null)
            {
                serializedList = MugsTech.Background.BackgroundMusicPlayer.SerializePathList(save.music.filePaths);
                volume         = save.music.volume;
                source         = $"save '{save.name}'";
            }
            else
            {
                serializedList = PlayerPrefs.GetString(VisualsMenuController.MusicListWorkingKey, "");
                volume         = PlayerPrefs.GetFloat (VisualsMenuController.MusicVolumeWorkingKey,
                                                       MugsTech.Background.BackgroundMusicPlayer.DefaultVolume);
                source         = "PlayerPrefs working state";
            }

            Debug.Log($"[BgMusic] ApplyBackgroundMusic: source={source} volume={volume:F2} " +
                      $"playlist=\"{serializedList.Replace("\n", " | ")}\"");

            if (!string.IsNullOrWhiteSpace(serializedList))
            {
                PlayerPrefs.SetString(MugsTech.Background.BackgroundMusicPlayer.PresetListPrefKey,   serializedList);
                PlayerPrefs.SetFloat (MugsTech.Background.BackgroundMusicPlayer.PresetVolumePrefKey, volume);
            }
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
                PlayerPrefs.SetString(MugsTech.Background.BackgroundVideoOverride.PresetPathPrefKey, path.Trim());
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

        static void ApplyBigHeadlineStyle(VisualsSaveFile save)
        {
            BigHeadlineStyleData s = save.bigHeadline;
            if (s == null) return;
            BigHeadline.Look = BigHeadlineLook.FromStyle(s, ResolvePatternTexture(s));
        }

        static Texture2D ResolvePatternTexture(BigHeadlineStyleData s)
        {
            string key = !string.IsNullOrEmpty(s.patternImageBase64) ? s.patternImageBase64
                       : !string.IsNullOrEmpty(s.patternOriginalPath) ? "path:" + s.patternOriginalPath
                       : "";
            if (key.Length == 0) return null; // built-in default pattern
            if (key == s_PatternKey && s_PatternTexture != null) return s_PatternTexture;

            byte[] bytes = ReadPatternBytes(s);
            Texture2D tex = bytes != null ? DecodePatternTexture(bytes) : null;
            if (tex == null)
                Debug.LogWarning("[VisualsRuntimeApplier] Big headline pattern image unreadable — using the built-in pattern.");

            // The previous texture is deliberately not destroyed: a card built
            // before this apply may still be drawing it. Once nothing holds
            // it, the next single-mode scene load's UnloadUnusedAssets frees it.
            s_PatternKey     = tex != null ? key : null;
            s_PatternTexture = tex;
            return tex;
        }

        /// <summary>
        /// Raw bytes of a save's pattern image. The embedded copy wins so a
        /// recording always matches what the visuals menu previewed; the
        /// original path is only a fallback for data with nothing embedded.
        /// </summary>
        public static byte[] ReadPatternBytes(BigHeadlineStyleData s)
        {
            if (s == null) return null;
            if (!string.IsNullOrEmpty(s.patternImageBase64))
            {
                try { return Convert.FromBase64String(s.patternImageBase64); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsRuntimeApplier] Pattern base64 decode failed: {e.Message}");
                }
            }
            if (!string.IsNullOrEmpty(s.patternOriginalPath) && File.Exists(s.patternOriginalPath))
            {
                try { return File.ReadAllBytes(s.patternOriginalPath); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsRuntimeApplier] Pattern read failed '{s.patternOriginalPath}': {e.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// PNG/JPG bytes → a texture ready for Custom/UIScrollingPattern
        /// (Repeat wrap, no mips — matching the built-in pattern's import).
        /// Returns null when the bytes don't decode.
        /// </summary>
        public static Texture2D DecodePatternTexture(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name       = "BigHeadlinePattern";
            tex.wrapMode   = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
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
            // The avatar's Inspector emotion-image array fully replaces the
            // save/menu images when its override toggle is on — don't push these.
            if (avatar.useEmotionArrayOverride) return;

            var overrides = new Dictionary<string, Sprite>();
            foreach (EmotionImageData emo in save.emotions)
            {
                Sprite spr = LoadEmotionSprite(emo);
                if (spr != null) overrides[emo.emotion] = spr;
            }

            if (overrides.Count > 0)
                avatar.ApplyEmotionOverrides(overrides);
        }

        // Loads emotion sprites from the visuals menu's working PlayerPrefs
        // state — uses the EmotionNamesKey list (newline-separated) to know
        // which slots the user has configured, then reads each one's image
        // path from CharacterPathKey. Mirrors ApplyAvatarSprites' behaviour
        // but pulls from disk paths directly (no base64 fallback because the
        // user is still actively editing — the file is expected to exist).
        static void ApplyAvatarSpritesFromWorkingPrefs()
        {
            HybridAvatarSystem avatar = UnityEngine.Object.FindObjectOfType<HybridAvatarSystem>();
            if (avatar == null) return;
            // Inspector emotion-image array is authoritative when its toggle is on.
            if (avatar.useEmotionArrayOverride) return;

            string serializedNames = PlayerPrefs.GetString(VisualsMenuController.EmotionNamesKey, "");
            if (string.IsNullOrWhiteSpace(serializedNames)) return;

            var overrides = new Dictionary<string, Sprite>();
            foreach (string rawLine in serializedNames.Split('\n'))
            {
                string name = (rawLine ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string path = PlayerPrefs.GetString(VisualsMenuController.CharacterPathKey(name), "");
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                Sprite spr = LoadSpriteFromFile(path);
                if (spr != null) overrides[name] = spr;
            }

            if (overrides.Count > 0)
                avatar.ApplyEmotionOverrides(overrides);
        }

        static Sprite LoadSpriteFromFile(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VisualsRuntimeApplier] read failed '{path}': {e.Message}");
                return null;
            }

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

    /// <summary>
    /// Decoded big-headline style, consumed by BigCenterCard's drawing
    /// code — both the recording card and the visuals-menu preview build one
    /// from <see cref="BigHeadlineStyleData"/> via <see cref="FromStyle"/>, so
    /// the two can't interpret the data differently.
    /// </summary>
    public struct BigHeadlineLook
    {
        public Color?    BackgroundColor;  // null = side-card background (original look)
        public bool      PatternEnabled;
        public Texture2D PatternTexture;   // null = built-in Resources/Patterns/Default
        public Color     PatternTint;      // alpha = pattern opacity
        public float     TileScale;
        public float     TilePadding;
        public float     ScrollSpeed;
        public float     ScrollAngle;      // degrees the pattern travels toward

        // Text — null / false = the side cards' text style (original look).
        public Color?        HeadlineColor;
        public Color?        SourceColor;
        public TMP_FontAsset Font;
        public FontStyle?    FontStyle;      // headline's style; source adds italic
        public bool          OutlineEnabled;
        public Color         OutlineColor;
        public float         OutlineWidth;
        public bool          ShadowEnabled;
        public Color         ShadowColor;
        public float         ShadowSoftness;

        // Vignette over panel + pattern — off in the original look.
        public bool          VignetteEnabled;
        public Color         VignetteColor;    // alpha = strength
        public float         VignetteSize;
        public float         VignetteSoftness;
        public float         VignetteShape;    // 0 = round, 1 = follows the frame

        /// <summary>The card's original hard-coded look.</summary>
        public static BigHeadlineLook Legacy => new BigHeadlineLook
        {
            BackgroundColor = null,
            PatternEnabled  = true,
            PatternTexture  = null,
            PatternTint     = new Color(1f, 1f, 1f, 0.35f),
            TileScale       = 12f,
            TilePadding     = 0.05f,
            ScrollSpeed     = 0.36f,
            ScrollAngle     = 135f,
            OutlineColor    = Color.black,
            OutlineWidth    = 0.15f,
            ShadowColor     = new Color(0f, 0f, 0f, 0.75f),
            ShadowSoftness  = 0.5f,
            VignetteEnabled  = false,
            VignetteColor    = new Color(0f, 0f, 0f, 0.6f),
            VignetteSize     = 0.35f,
            VignetteSoftness = 0.6f,
            VignetteShape    = 0.5f,
        };

        public static BigHeadlineLook FromStyle(BigHeadlineStyleData s, Texture2D patternTexture)
        {
            BigHeadlineLook look = Legacy;
            if (s == null) return look;

            look.BackgroundColor = ParseHex(s.backgroundColorHex);
            look.PatternEnabled  = s.patternEnabled;
            look.PatternTexture  = patternTexture;
            Color tint = ParseHex(s.patternTintHex) ?? Color.white;
            tint.a = Mathf.Clamp01(s.patternOpacity);
            look.PatternTint     = tint;
            look.TileScale       = Mathf.Max(0.5f, s.patternTileScale);
            look.TilePadding     = Mathf.Clamp(s.patternTilePadding, 0f, 0.49f);
            look.ScrollSpeed     = s.patternScrollSpeed;
            look.ScrollAngle     = s.patternScrollDirection;

            look.HeadlineColor   = ParseHex(s.headlineColorHex);
            look.SourceColor     = ParseHex(s.sourceColorHex);
            look.Font            = FontRegistry.Resolve(s.fontName)?.Asset; // null when unset or unresolvable
            look.FontStyle       = s.fontStyle >= 0 ? (FontStyle?)(FontStyle)s.fontStyle : null;
            look.OutlineEnabled  = s.outlineEnabled;
            look.OutlineColor    = ParseHex(s.outlineColorHex) ?? look.OutlineColor;
            look.OutlineWidth    = Mathf.Clamp01(s.outlineWidth);
            look.ShadowEnabled   = s.shadowEnabled;
            look.ShadowColor     = ParseHex(s.shadowColorHex) ?? look.ShadowColor;
            look.ShadowSoftness  = Mathf.Clamp01(s.shadowSoftness);

            look.VignetteEnabled  = s.vignetteEnabled;
            Color vignette = ParseHex(s.vignetteColorHex) ?? Color.black;
            vignette.a = Mathf.Clamp01(s.vignetteStrength);
            look.VignetteColor    = vignette;
            look.VignetteSize     = Mathf.Clamp01(s.vignetteSize);
            look.VignetteSoftness = Mathf.Clamp(s.vignetteSoftness, 0.01f, 1f);
            look.VignetteShape    = Mathf.Clamp01(s.vignetteShape);
            return look;
        }

        /// <summary>Unit vector the pattern visibly travels along (UV space, +y up).</summary>
        public Vector2 ScrollDirection =>
            new Vector2(Mathf.Cos(ScrollAngle * Mathf.Deg2Rad), Mathf.Sin(ScrollAngle * Mathf.Deg2Rad));

        static Color? ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out Color c) ? (Color?)c : null;
        }
    }
}

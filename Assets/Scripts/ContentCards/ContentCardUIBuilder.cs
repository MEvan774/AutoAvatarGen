using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MugsTech.Style;

/// <summary>
/// Static helper for building content card UI elements consistently.
/// Encapsulates brand colors, font sizes, and layout defaults.
/// </summary>
public static class ContentCardUIBuilder
{
    // Fallback panel when no style preset is active.
    public static readonly Color BackgroundColor = new Color(15f / 255f, 15f / 255f, 20f / 255f, 0.85f);

    // Brand coral — the decorative accent on every backdrop that doesn't
    // define its own palette (see BackdropPalette).
    private static readonly Color BrandCoral = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 1f);

    /// <summary>Decorative accent, themed per active backdrop.</summary>
    public static Color AccentColor => BackdropPalette.CardAccent(BrandCoral);
    public static Color AccentColor40 { get { Color c = AccentColor; c.a = 0.4f; return c; } }

    // Semantic stat colors — NOT themed: up must stay green and down must stay
    // red-ish no matter which backdrop is active.
    public static readonly Color PositiveGreen = new Color(0x4C / 255f, 0xAF / 255f, 0x50 / 255f, 1f);
    public static readonly Color NegativeRed   = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 1f);

    // Dark text colors for light backgrounds (office-paper look)
    private static readonly Color DarkPrimary   = new Color(0.12f, 0.12f, 0.14f, 1f);    // ~#1F1F24 charcoal
    private static readonly Color DarkSecondary = new Color(0.22f, 0.22f, 0.25f, 0.90f); // softer charcoal
    private static readonly Color DarkTertiary  = new Color(0.34f, 0.34f, 0.38f, 0.85f); // muted attribution grey

    // Light text colors for dark backgrounds (original)
    private static readonly Color LightPrimary   = Color.white;
    private static readonly Color LightSecondary = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color LightTertiary  = new Color(1f, 1f, 1f, 0.6f);

    /// <summary>
    /// Primary text color — user override from the active VisualsSave wins;
    /// otherwise switches to charcoal on light preset backgrounds (luminance
    /// > 0.5) and white on dark backgrounds.
    /// </summary>
    public static Color TextPrimary
    {
        get
        {
            if (VisualsRuntimeApplier.CardTextColorOverride.HasValue)
                return VisualsRuntimeApplier.CardTextColorOverride.Value;
            return IsActivePresetLight() ? DarkPrimary : LightPrimary;
        }
    }
    public static Color TextSecondary
    {
        get
        {
            if (VisualsRuntimeApplier.CardTextColorOverride.HasValue)
            {
                Color c = VisualsRuntimeApplier.CardTextColorOverride.Value;
                c.a *= 0.85f;
                return c;
            }
            return IsActivePresetLight() ? DarkSecondary : LightSecondary;
        }
    }
    public static Color TextTertiary
    {
        get
        {
            if (VisualsRuntimeApplier.CardTextColorOverride.HasValue)
            {
                Color c = VisualsRuntimeApplier.CardTextColorOverride.Value;
                c.a *= 0.70f;
                return c;
            }
            return IsActivePresetLight() ? DarkTertiary : LightTertiary;
        }
    }

    private static bool IsActivePresetLight()
    {
        var preset = MugsTech.Style.StyleManager.Instance != null
            ? MugsTech.Style.StyleManager.Instance.ActivePreset
            : null;
        if (preset == null) return false;
        // Judge the EFFECTIVE panel color (the backdrop palette may override
        // the preset's paper), or text contrast breaks under an override.
        Color c = BackdropPalette.CardPaper(preset.cardBackgroundColor);
        // Perceived luminance (Rec. 601)
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        return lum > 0.5f;
    }

    public const float CardPadding = 24f;

    // ---- Card drop shadow ----
    // Modeled on the CSS `box-shadow: 0px 4px 6px -1px rgba(0,0,0,0.1)` from the
    // channel's web-style comps, but scaled up for the video: the project
    // composites in LINEAR color space, which reads at roughly half the
    // perceptual strength of a browser's sRGB blend, and the animated backdrop
    // is busier and darker than a web page — at the literal CSS values the
    // shadow was invisible in a finished take. Pixels are 1080p canvas pixels.
    public const float ShadowOffsetYPx = 6f;
    public const int   ShadowBlurPx    = 12;
    public const float ShadowSpreadPx  = -1f;
    public static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.30f);

    /// <summary>
    /// How far the shadow Image's rect extends past the element it sits under,
    /// per side: the sprite's blur padding (the solid shape is inset that far
    /// from the sprite edge) plus the CSS spread (negative = shadow shape
    /// slightly smaller than the element).
    /// </summary>
    public const float ShadowGrowPx = ShadowBlurPx + ShadowSpreadPx;

    // Inter SDF assets shipped under Resources/Fonts/RecordingText/. Loaded
    // once on first access and reused — TMP_FontAssets are immutable runtime
    // resources, so caching them is safe.
    private const string InterRegularPath  = "Fonts/RecordingText/Inter_18pt-Regular SDF";
    private const string InterSemiBoldPath = "Fonts/RecordingText/Inter_18pt-SemiBold SDF";
    private const string InterBoldPath     = "Fonts/RecordingText/Inter_18pt-Bold SDF";

    private static TMP_FontAsset s_InterRegular;
    private static TMP_FontAsset s_InterSemiBold;
    private static TMP_FontAsset s_InterBold;

    /// <summary>Inter Regular SDF from Resources/Fonts/RecordingText/ (cached). Null if not found.</summary>
    public static TMP_FontAsset InterRegular
        => s_InterRegular != null ? s_InterRegular : (s_InterRegular = Resources.Load<TMP_FontAsset>(InterRegularPath));

    /// <summary>Inter SemiBold SDF from Resources/Fonts/RecordingText/ (cached). Null if not found.</summary>
    public static TMP_FontAsset InterSemiBold
        => s_InterSemiBold != null ? s_InterSemiBold : (s_InterSemiBold = Resources.Load<TMP_FontAsset>(InterSemiBoldPath));

    /// <summary>Inter Bold SDF from Resources/Fonts/RecordingText/ (cached). Null if not found.</summary>
    public static TMP_FontAsset InterBold
        => s_InterBold != null ? s_InterBold : (s_InterBold = Resources.Load<TMP_FontAsset>(InterBoldPath));

    /// <summary>Creates a child RectTransform filling its parent.</summary>
    public static RectTransform CreateChild(RectTransform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>
    /// Creates a soft drop shadow filling the parent (grown by the blur padding,
    /// shifted 4px down — see the Shadow* constants). Add it BEFORE the element
    /// it should sit under, so it renders behind. The sprite's corner radius
    /// should match the element's so the silhouettes agree.
    /// </summary>
    public static Image CreateShadow(RectTransform parent, float cornerRadiusPx)
    {
        RectTransform rt = CreateChild(parent, "Shadow");
        rt.offsetMin = new Vector2(-ShadowGrowPx, -ShadowGrowPx - ShadowOffsetYPx);
        rt.offsetMax = new Vector2( ShadowGrowPx,  ShadowGrowPx - ShadowOffsetYPx);

        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = StyleSpriteFactory.GetRoundedRectShadow(
            Mathf.RoundToInt(cornerRadiusPx), ShadowBlurPx);
        img.type = Image.Type.Sliced;
        img.color = ShadowColor;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// Creates the card background filling the parent, with a soft drop shadow
    /// behind it (the CSS-style card elevation — see the Shadow* constants).
    /// If a <see cref="StyleManager"/> with an active preset exists, the background
    /// uses the preset's cream color, corner radius, and opacity. Otherwise it
    /// falls back to the original dark semi-transparent panel.
    /// Pass <paramref name="withShadow"/> = false for a background that ISN'T an
    /// elevated card — BigCenter's fullscreen overlay panel slides independently
    /// of the card root, so a root-anchored shadow would wash the screen before
    /// the panel arrives (and a fullscreen cover has no elevation to express).
    /// </summary>
    public static Image CreateBackground(RectTransform parent, bool withShadow = true)
    {
        ChannelStylePreset preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;
        float radius = preset != null ? preset.cornerRadiusPx : 0f;

        // Shadow first, so it sits behind the background in sibling order.
        if (withShadow)
            CreateShadow(parent, radius);

        RectTransform rt = CreateChild(parent, "Background");
        Image img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;

        if (preset != null)
        {
            img.sprite = StyleSpriteFactory.GetRoundedRect(Mathf.RoundToInt(preset.cornerRadiusPx));
            img.type = Image.Type.Sliced;
            Color c = BackdropPalette.CardPaper(preset.cardBackgroundColor);
            c.a = preset.opacity;
            img.color = c;
        }
        else
        {
            img.color = BackgroundColor;
        }
        return img;
    }

    /// <summary>Creates a thin accent bar anchored to the top of the parent.</summary>
    public static Image CreateAccentBar(RectTransform parent, float heightPx = 4f)
    {
        GameObject go = new GameObject("AccentBar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, heightPx);

        Image img = go.AddComponent<Image>();
        img.color = AccentColor;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Creates a TMP text element as a child of the parent.</summary>
    public static TextMeshProUGUI CreateText(RectTransform parent, string name, Color color, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (VisualsRuntimeApplier.CardFontOverride != null)
            tmp.font = VisualsRuntimeApplier.CardFontOverride;
        tmp.color = color;
        tmp.fontSize = fontSize;
        tmp.fontStyle = ResolveFontStyle(style);
        tmp.alignment = alignment;
        tmp.raycastTarget = false;

        return tmp;
    }

    // User VisualsSave font style (when set) wins over the per-call default.
    // Bold/italic flags from the caller are preserved by ORing them in, so a
    // headline asking for Bold stays bold even if the user picks Italic.
    static FontStyles ResolveFontStyle(FontStyles requested)
    {
        if (!VisualsRuntimeApplier.CardFontStyleOverride.HasValue) return requested;
        FontStyles userMask = ConvertFontStyle(VisualsRuntimeApplier.CardFontStyleOverride.Value);
        return requested | userMask;
    }

    static FontStyles ConvertFontStyle(FontStyle f)
    {
        switch (f)
        {
            case FontStyle.Bold:          return FontStyles.Bold;
            case FontStyle.Italic:        return FontStyles.Italic;
            case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
            default:                      return FontStyles.Normal;
        }
    }

    /// <summary>Creates an Image child.</summary>
    public static Image CreateImage(RectTransform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Anchors and positions a RectTransform with explicit offsets.</summary>
    public static void SetStretch(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    // Cached arrow sprite (generated once, shared across all StatCards)
    private static Sprite s_ArrowSprite;

    /// <summary>
    /// Returns a procedurally-generated arrow sprite matching the silhouette of the
    /// icon-icons.com download arrow: ~43% wide shaft on top, full-width triangular
    /// head pointing down. Flip vertically (localScale.y = -1) for an up arrow.
    /// The texture is generated once and cached.
    /// </summary>
    public static Sprite GetArrowSprite(int size = 128)
    {
        if (s_ArrowSprite != null) return s_ArrowSprite;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        // Shaft: 12/28 wide, centered horizontally, occupying top half of texture
        float shaftHalfWNorm = 6f / 28f;
        int shaftL = Mathf.RoundToInt((0.5f - shaftHalfWNorm) * size);
        int shaftR = Mathf.RoundToInt((0.5f + shaftHalfWNorm) * size);
        int shaftBotY = Mathf.RoundToInt(size * 14f / 28f); // middle of texture

        for (int y = shaftBotY; y < size; y++)
        {
            for (int x = shaftL; x < shaftR; x++)
                pixels[y * size + x] = Color.white;
        }

        // Arrowhead: full width at base (y=shaftBotY), narrowing to apex at y=0 (bottom)
        for (int y = 0; y < shaftBotY; y++)
        {
            float t = (float)y / Mathf.Max(1, shaftBotY - 1); // 0 at apex, 1 at base
            float halfW = size * 0.5f * t;
            int xMin = Mathf.RoundToInt(size * 0.5f - halfW);
            int xMax = Mathf.RoundToInt(size * 0.5f + halfW);
            for (int x = xMin; x < xMax; x++)
            {
                if (x >= 0 && x < size)
                    pixels[y * size + x] = Color.white;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        s_ArrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        s_ArrowSprite.name = "GeneratedArrowSprite";
        return s_ArrowSprite;
    }
}

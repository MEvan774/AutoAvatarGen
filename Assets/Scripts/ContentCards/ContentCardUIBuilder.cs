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
    // Brand / static colors (don't change based on background)
    public static readonly Color BackgroundColor = new Color(15f / 255f, 15f / 255f, 20f / 255f, 0.85f);
    public static readonly Color AccentColor = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 1f);
    public static readonly Color AccentColor40 = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 0.4f);
    public static readonly Color PositiveGreen = new Color(0x4C / 255f, 0xAF / 255f, 0x50 / 255f, 1f);

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
        Color c = preset.cardBackgroundColor;
        // Perceived luminance (Rec. 601)
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        return lum > 0.5f;
    }

    public const float CardPadding = 24f;

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
    /// Creates the card background filling the parent.
    /// If a <see cref="StyleManager"/> with an active preset exists, the background
    /// uses the preset's cream color, corner radius, and opacity. Otherwise it
    /// falls back to the original dark semi-transparent panel.
    /// </summary>
    public static Image CreateBackground(RectTransform parent)
    {
        RectTransform rt = CreateChild(parent, "Background");
        Image img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;

        ChannelStylePreset preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;
        if (preset != null)
        {
            img.sprite = StyleSpriteFactory.GetRoundedRect(Mathf.RoundToInt(preset.cornerRadiusPx));
            img.type = Image.Type.Sliced;
            Color c = preset.cardBackgroundColor;
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

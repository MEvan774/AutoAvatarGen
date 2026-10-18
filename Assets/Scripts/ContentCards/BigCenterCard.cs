using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// Fullscreen headline overlay — big centered text on a panel that covers the screen.
/// Tag: {Headline:"headline","source",T=X,D=Y,bigCenter}
///
/// Visuals: the overlay panel eases up from below the screen to fully cover it,
/// while the headline drops in from above the screen with the same CSS overshoot
/// curve used by the regular card entry. Sits in the feature-media (fullscreen)
/// zone so it renders on top of the character.
/// </summary>
public class BigCenterCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigCenter;

    private RectTransform overlayPanel;
    private RectTransform headlineContainer;
    private TextMeshProUGUI headlineText;
    private TextMeshProUGUI sourceText;
    private RawImage patternImage;
    private Material patternMaterial;
    private BigHeadlineLook look;

    // Tracked separately from currentSequence: managing two parallel tweens
    // directly avoids the DOTween bug where Sequence.Join silently drops an
    // AnimationCurve ease (which kills the headline overshoot).
    private Tween panelTween;
    private Tween headlineTween;
    private Tween headlineFadeTween;

    // Fades the headline independently of the card root (which BigCenter
    // forces fully visible so the screen-covering panel is never dimmed).
    private CanvasGroup headlineGroup;

    // =====================================================================
    // ALL TIMING / DISTANCE / EASE KNOBS LIVE IN THE INSPECTOR
    //
    // Open the ContentZoneController GameObject → CardEntryAnimator
    // component. There are three groups for this card:
    //
    //   • "Per-Card Settings" → BigCenter row
    //         Direction the headline slides in from
    //         Override Direction / Slide Duration / Fade-In Duration / Slide Distance Factor
    //
    //   • "BigCenter Card" group
    //         Panel Slide Duration / Distance / Ease
    //         Headline Travel Base
    //
    //   • "Overshoot Curve" at the top of the component
    //         The shared overshoot curve used by the headline.
    //
    // The shorthand getters below just route to those inspector fields so
    // this script stays readable.
    // =====================================================================
    private CardEntryAnimator.BigCenterSettings BigCenterCfg
        => CardEntryAnimator.Instance.bigCenter;

    protected override void BuildUI()
    {
        // Background style from the active Visuals save; the static default
        // (no save applied, e.g. the recording scene run without the menu) is
        // the original hard-coded look.
        look = VisualsRuntimeApplier.BigHeadline.Look;

        // 1. Fullscreen overlay panel (first child → drawn behind the headline).
        //    Uses the same preset-aware background as every other card (so it
        //    keeps the cards' corner sprite), recolored by the big-headline
        //    style and forced to full alpha — BigCenter takes over the whole
        //    screen and the character behind should be fully masked. No drop
        //    shadow: the panel slides independently of the card root, and a
        //    fullscreen cover has no elevation to express.
        Image panelImg = ContentCardUIBuilder.CreateBackground(rectTransform, withShadow: false);
        panelImg.gameObject.name = "OverlayPanel";
        panelImg.color = ResolvePanelColor(look, panelImg.color);
        overlayPanel = panelImg.rectTransform;

        // 1b. Scrolling pattern layer — sits on top of the solid overlay and
        //     inside the HeadlineContainer's z-order (child of overlayPanel,
        //     HeadlineContainer is a later sibling at the card level so it
        //     still renders above the pattern). Skipped entirely when off.
        if (look.PatternEnabled)
        {
            patternImage = CreatePatternLayer(overlayPanel);
            if (patternImage != null)
            {
                patternMaterial = patternImage.material;
                ApplyPatternLook(patternImage, look);
            }
        }

        // 2. Centered headline + source, built in the side cards' text style,
        //    then restyled by whatever the big-headline style overrides.
        headlineContainer = CreateHeadlineBlock(rectTransform,
            ContentCardUIBuilder.TextPrimary, ContentCardUIBuilder.TextTertiary,
            out headlineText, out sourceText);
        ApplyTextLook(headlineText, sourceText, look, CardTextStyle.Capture(headlineText, sourceText));
        headlineGroup = EnsureGroup(headlineContainer);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        headlineText.text = data.primaryText;
        sourceText.text = data.secondaryText;
    }

    public override void Show()
    {
        KillCurrentSequence();
        KillSlideTweens();

        // The entrance is the overlay + headline sliding in, not a fade. Flip
        // to fully visible immediately (base Awake set alpha to 0).
        canvasGroup.alpha = 1f;

        // Flatten any preset rotation — a big centered overlay reads cleanest
        // straight, same rationale as BigMediaCard.
        rectTransform.localEulerAngles = Vector3.zero;
        headlineContainer.localEulerAngles = Vector3.zero;

        // Re-apply now the card is surely laid out — a custom pattern's tile
        // aspect is derived from the panel's rect.
        if (patternImage != null) ApplyPatternLook(patternImage, look);

        var cfg = BigCenterCfg;

        // ----- Background panel — settings come from CardEntryAnimator
        // → BigCenter Card group. Uses a smooth (non-overshoot) ease.
        // Starts fully below the viewport (panelSlideDistance pixels). Fixed
        // pixel distance so it's reliable on the first frame.
        overlayPanel.anchoredPosition = new Vector2(0f, -cfg.panelSlideDistance);

        // ----- Headline text — direction & distance factor come from the
        // BigCenter row in Per-Card Settings; actual pixel distance is
        // headlineTravelBase × that factor.
        Vector2 headlineOffset = HeadlineEntryOffset(
            ResolvedEntryDirection, cfg.headlineTravelBase, SlideDistanceFactor);
        headlineContainer.anchoredPosition = headlineOffset;

        // Run the two tweens in parallel WITHOUT a Sequence. DOTween's
        // Sequence.Join/Insert can silently drop an AnimationCurve ease,
        // which would kill the headline overshoot. Independent tweens are
        // immune to that — the curve is reliably applied.
        panelTween = overlayPanel
            .DOAnchorPosY(0f, cfg.panelSlideDuration)
            .SetEase(cfg.panelSlideEase);

        headlineTween = ApplyEntryEase(headlineContainer
            .DOAnchorPos(Vector2.zero, SlideDuration))
            .OnComplete(() => StartIdleFloat(headlineContainer));   // the panel covers the screen — only the headline drifts

        // 'Ease in + fade' dissolves the headline in over the slide; Overshoot
        // keeps it fully opaque from the first frame, as before.
        if (UseOvershootEntry)
        {
            headlineGroup.alpha = 1f;
        }
        else
        {
            headlineGroup.alpha = 0f;
            headlineFadeTween = headlineGroup.DOFade(1f, EntryFadeDuration).SetEase(EntryFadeEase);
        }
    }

    private void KillSlideTweens()
    {
        if (panelTween        != null && panelTween.IsActive())        panelTween.Kill();
        if (headlineTween     != null && headlineTween.IsActive())     headlineTween.Kill();
        if (headlineFadeTween != null && headlineFadeTween.IsActive()) headlineFadeTween.Kill();
        panelTween = null;
        headlineTween = null;
        headlineFadeTween = null;
    }

    // Resolves the headline's off-screen start offset. Uses the configured
    // travel base (from CardEntryAnimator → BigCenter Card group), scaled by
    // the per-card Slide Distance Factor.
    private static Vector2 HeadlineEntryOffset(EntryDirection dir, float travelBase, float factor)
    {
        float d = travelBase * factor;
        switch (dir)
        {
            case EntryDirection.FromLeft:   return new Vector2(-d, 0f);
            case EntryDirection.FromRight:  return new Vector2( d, 0f);
            case EntryDirection.FromBottom: return new Vector2(0f, -d);
            case EntryDirection.FromTop:    return new Vector2(0f,  d);
            default:                        return new Vector2(0f,  d);
        }
    }

    // =====================================================================
    // Shared drawing code — BigHeadlineStylePopup builds its live preview
    // from these same calls (on a 1920×1080 rect scaled down), so the menu
    // preview can't drift from what the recording renders.
    // =====================================================================

    // Default pattern, used when the style names no custom image. Drop a
    // seamlessly tileable texture at Assets/Resources/Patterns/Default.png.
    private const string PATTERN_RESOURCE_PATH = "Patterns/Default";

    // The shader lives under Resources so it ships in builds: nothing
    // references it from an asset, and a bare Shader.Find comes back null in
    // the player (same story as MugsTech/SpriteShadowBlur).
    private const string PATTERN_SHADER_RESOURCE = "Shaders/UIScrollingPattern";
    private const string PATTERN_SHADER_NAME = "Custom/UIScrollingPattern";

    // Requested text styles; the user's card font style is ORed on top.
    public const FontStyles HeadlineFontStyles = FontStyles.Bold;
    public const FontStyles SourceFontStyles   = FontStyles.Italic;

    /// <summary>
    /// The overlay panel color: the style's own color, else the side cards'
    /// background (the original look). Always fully opaque.
    /// </summary>
    public static Color ResolvePanelColor(BigHeadlineLook look, Color cardBackground)
    {
        Color c = look.BackgroundColor ?? cardBackground;
        c.a = 1f;
        return c;
    }

    /// <summary>
    /// Adds a RawImage child that fills <paramref name="parent"/> with its
    /// own UIScrollingPattern material instance (the caller destroys it).
    /// Returns null if the shader is missing — the plain panel still works.
    /// </summary>
    public static RawImage CreatePatternLayer(RectTransform parent)
    {
        Shader shader = Resources.Load<Shader>(PATTERN_SHADER_RESOURCE);
        if (shader == null) shader = Shader.Find(PATTERN_SHADER_NAME);
        if (shader == null)
        {
            Debug.LogWarning($"[BigCenter] Shader \"{PATTERN_SHADER_NAME}\" not found — skipping pattern layer.");
            return null;
        }

        GameObject patternGO = new GameObject("OverlayPattern", typeof(RectTransform));
        patternGO.transform.SetParent(parent, false);
        RectTransform prt = patternGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        RawImage patternImg = patternGO.AddComponent<RawImage>();
        patternImg.raycastTarget = false;
        // Per-instance material so tweaking one card doesn't mutate a shared asset.
        patternImg.material = new Material(shader);
        return patternImg;
    }

    /// <summary>
    /// Pushes a look onto a layer from <see cref="CreatePatternLayer"/>.
    /// Hides the layer when the pattern is off or no texture resolves.
    /// </summary>
    public static void ApplyPatternLook(RawImage patternImg, BigHeadlineLook look)
    {
        Texture2D patternTex = look.PatternEnabled ? look.PatternTexture : null;
        if (look.PatternEnabled && patternTex == null)
        {
            patternTex = Resources.Load<Texture2D>(PATTERN_RESOURCE_PATH);
            if (patternTex == null)
                Debug.Log($"[BigCenter] No pattern texture at Resources/{PATTERN_RESOURCE_PATH} — skipping pattern layer.");
            else
                patternTex.wrapMode = TextureWrapMode.Repeat;
        }

        patternImg.enabled = patternTex != null;
        if (!patternImg.enabled) return;

        patternImg.texture = patternTex;
        patternImg.color   = look.PatternTint;
        patternImg.uvRect  = PatternUvRect(patternImg.rectTransform.rect, look.PatternTexture);

        Material mat = patternImg.material;
        mat.SetFloat("_TileScale", look.TileScale);
        mat.SetFloat("_TilePadding", look.TilePadding);
        mat.SetFloat("_ScrollSpeed", look.ScrollSpeed);
        mat.SetVector("_ScrollDir", look.ScrollDirection);
    }

    // The shader tiles UVs TileScale times on both axes. The built-in pattern
    // keeps that original stretch (its cells are panel-shaped); a custom
    // image gets a longer/shorter V range so its tiles keep the image's own
    // aspect ratio and a picked icon isn't squashed to 16:9.
    private static Rect PatternUvRect(Rect panel, Texture2D customTex)
    {
        if (customTex == null || panel.width <= 0f || panel.height <= 0f || customTex.height <= 0)
            return new Rect(0f, 0f, 1f, 1f);
        float v = (panel.height / panel.width) * ((float)customTex.width / customTex.height);
        return new Rect(0f, 0f, 1f, v);
    }

    /// <summary>
    /// Builds the centered headline + source block in 1920×1080 canvas units
    /// (sized for comfortable wrapping; auto-sizing adapts to the text).
    /// </summary>
    public static RectTransform CreateHeadlineBlock(RectTransform parent,
        Color primary, Color tertiary,
        out TextMeshProUGUI headline, out TextMeshProUGUI source)
    {
        GameObject containerGO = new GameObject("HeadlineContainer", typeof(RectTransform));
        containerGO.transform.SetParent(parent, false);
        RectTransform container = containerGO.GetComponent<RectTransform>();
        container.anchorMin = new Vector2(0.5f, 0.5f);
        container.anchorMax = new Vector2(0.5f, 0.5f);
        container.pivot = new Vector2(0.5f, 0.5f);
        container.anchoredPosition = Vector2.zero;
        container.sizeDelta = new Vector2(1600f, 640f);

        // Big headline — auto-sizes between 72 and 200 px.
        headline = ContentCardUIBuilder.CreateText(
            container, "BigHeadlineText",
            primary,
            160f, TextAlignmentOptions.Center,
            HeadlineFontStyles);
        ContentCardUIBuilder.SetStretch(headline.rectTransform, 40f, 40f, 40f, 96f);
        headline.enableAutoSizing = true;
        headline.fontSizeMin = 72f;
        headline.fontSizeMax = 200f;
        headline.maxVisibleLines = 4;
        headline.overflowMode = TextOverflowModes.Ellipsis;
        headline.enableWordWrapping = true;

        // Source attribution, pinned to the bottom of the container.
        source = ContentCardUIBuilder.CreateText(
            container, "SourceText",
            tertiary,
            40f, TextAlignmentOptions.Center,
            SourceFontStyles);
        RectTransform srt = source.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = new Vector2(0f, 24f);
        srt.sizeDelta = new Vector2(-80f, 48f);
        return container;
    }

    /// <summary>
    /// The side cards' text style as the headline block would use it — what
    /// every unset big-headline text option falls back to.
    /// </summary>
    public struct CardTextStyle
    {
        public TMP_FontAsset Font;
        public FontStyles    HeadlineStyle;
        public FontStyles    SourceStyle;
        public Color         HeadlineColor;
        public Color         SourceColor;

        /// <summary>Reads it back from a block just built by <see cref="CreateHeadlineBlock"/>.</summary>
        public static CardTextStyle Capture(TMP_Text headline, TMP_Text source) => new CardTextStyle
        {
            Font          = headline.font,
            HeadlineStyle = headline.fontStyle,
            SourceStyle   = source.fontStyle,
            HeadlineColor = headline.color,
            SourceColor   = source.color,
        };
    }

    /// <summary>
    /// Applies the look's text options over the card text style. With
    /// nothing set this re-applies the card style unchanged and leaves the
    /// font's shared material alone, so the default look renders as before.
    /// </summary>
    public static void ApplyTextLook(TextMeshProUGUI headline, TextMeshProUGUI source,
                                     BigHeadlineLook look, CardTextStyle card)
    {
        TMP_FontAsset font = look.Font != null ? look.Font : card.Font;
        if (headline.font != font) headline.font = font;
        if (source.font   != font) source.font   = font;

        if (look.FontStyle.HasValue)
        {
            // The pick replaces the headline's style outright; the source
            // line keeps its italic attribution look on top of it.
            FontStyles picked = ToTmpFontStyles(look.FontStyle.Value);
            headline.fontStyle = picked;
            source.fontStyle   = SourceFontStyles | picked;
        }
        else
        {
            headline.fontStyle = card.HeadlineStyle;
            source.fontStyle   = card.SourceStyle;
        }

        headline.color = look.HeadlineColor ?? card.HeadlineColor;
        source.color   = look.SourceColor   ?? card.SourceColor;

        ApplyTextEffects(headline, look);
        ApplyTextEffects(source, look);
    }

    // Letter outline + drop shadow, done the same way as BigTextCard: touching
    // fontMaterial clones the shared material so only this text changes, and
    // OUTLINE_ON / UNDERLAY_ON are the shader toggles that make the values
    // take effect on every shader variant.
    private static void ApplyTextEffects(TMP_Text text, BigHeadlineLook look)
    {
        Material fontDefault = text.font.material;

        // Neither effect: (back to) the font's own shared material, i.e.
        // exactly the original look — also undoes a previous preview edit.
        if (!look.OutlineEnabled && !look.ShadowEnabled)
        {
            if (text.fontSharedMaterial != fontDefault) text.fontSharedMaterial = fontDefault;
            return;
        }

        Material mat = text.fontMaterial;

        // An effect that's off takes the font material's own values back
        // rather than zero: the SDF shader draws _OutlineWidth even without
        // the keyword, so "off" must mean "as the font ships".
        Color outlineColor = look.OutlineEnabled ? look.OutlineColor : fontDefault.GetColor("_OutlineColor");
        float outlineWidth = look.OutlineEnabled ? look.OutlineWidth : fontDefault.GetFloat("_OutlineWidth");
        SetKeyword(mat, "OUTLINE_ON", look.OutlineEnabled || fontDefault.IsKeywordEnabled("OUTLINE_ON"));
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineWidth", outlineWidth);
        text.outlineColor = outlineColor;
        text.outlineWidth = outlineWidth;

        if (look.ShadowEnabled)
        {
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor",    look.ShadowColor);
            mat.SetFloat("_UnderlayOffsetX",  1.0f);
            mat.SetFloat("_UnderlayOffsetY", -1.0f);
            mat.SetFloat("_UnderlayDilate",   0.5f);
            mat.SetFloat("_UnderlaySoftness", look.ShadowSoftness);
        }
        else
        {
            SetKeyword(mat, "UNDERLAY_ON", fontDefault.IsKeywordEnabled("UNDERLAY_ON"));
        }
        text.UpdateMeshPadding();
    }

    private static void SetKeyword(Material mat, string keyword, bool on)
    {
        if (on) mat.EnableKeyword(keyword);
        else    mat.DisableKeyword(keyword);
    }

    private static FontStyles ToTmpFontStyles(FontStyle s)
    {
        switch (s)
        {
            case FontStyle.Bold:          return FontStyles.Bold;
            case FontStyle.Italic:        return FontStyles.Italic;
            case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
            default:                      return FontStyles.Normal;
        }
    }

    public override void Hide(bool fast = false)
    {
        if (fast)
        {
            // Fast-hide on card queuing just fades out — no time for a slide.
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();
        KillSlideTweens();
        StopIdleFloat();

        var cfg = BigCenterCfg;
        float fadeDur = FadeOutDuration;

        // Mirror the entrance: panel exits straight down by panelSlideDistance
        // (smooth ease, no overshoot), headline exits in the same direction it
        // came from (so it leaves the way it arrived).
        Vector2 headlineExit = HeadlineEntryOffset(
            ResolvedEntryDirection, cfg.headlineTravelBase, SlideDistanceFactor);

        currentSequence = DOTween.Sequence()
            .Join(canvasGroup.DOFade(0f, fadeDur).SetEase(Ease.InQuad))
            .Join(overlayPanel.DOAnchorPosY(-cfg.panelSlideDistance, fadeDur).SetEase(Ease.InQuad))
            .Join(headlineContainer.DOAnchorPos(headlineExit, fadeDur).SetEase(Ease.InQuad))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    protected override void OnDestroy()
    {
        KillSlideTweens();
        if (patternMaterial != null)
        {
            Destroy(patternMaterial);
            patternMaterial = null;
        }
        base.OnDestroy();
    }
}

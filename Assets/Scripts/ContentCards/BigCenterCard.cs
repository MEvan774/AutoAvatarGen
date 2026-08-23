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
    private Material patternMaterial;

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

    // Pattern texture is loaded from Resources at runtime. Drop a seamlessly
    // tileable texture at Assets/Resources/Patterns/Default.png (or change
    // PATTERN_RESOURCE_PATH) to enable the scrolling pattern overlay.
    private const string PATTERN_RESOURCE_PATH = "Patterns/Default";
    private const float PATTERN_TINT_ALPHA = 0.35f;
    private const float PATTERN_TILE_SCALE = 12f;
    private const float PATTERN_TILE_PADDING = 0.05f;
    private const float PATTERN_SCROLL_SPEED = -0.36f;

    protected override void BuildUI()
    {
        // 1. Fullscreen overlay panel (first child → drawn behind the headline).
        //    Uses the same preset-aware background as every other card, but
        //    forced to full alpha — BigCenter takes over the whole screen and
        //    the character behind should be fully masked. No drop shadow: the
        //    panel slides independently of the card root, and a fullscreen
        //    cover has no elevation to express anyway.
        Image panelImg = ContentCardUIBuilder.CreateBackground(rectTransform, withShadow: false);
        panelImg.gameObject.name = "OverlayPanel";
        Color panelColor = panelImg.color;
        panelColor.a = 1f;
        panelImg.color = panelColor;
        overlayPanel = panelImg.rectTransform;

        // 1b. Scrolling pattern layer — sits on top of the solid overlay and
        //     inside the HeadlineContainer's z-order (child of overlayPanel,
        //     HeadlineContainer is a later sibling at the card level so it
        //     still renders above the pattern).
        BuildPatternLayer(overlayPanel);

        // 2. Centered headline container — sized for comfortable wrapping on a
        //    1920×1080 canvas, but auto-sizing will adapt to the rendered text.
        GameObject containerGO = new GameObject("HeadlineContainer", typeof(RectTransform));
        containerGO.transform.SetParent(rectTransform, false);
        headlineContainer = containerGO.GetComponent<RectTransform>();
        headlineContainer.anchorMin = new Vector2(0.5f, 0.5f);
        headlineContainer.anchorMax = new Vector2(0.5f, 0.5f);
        headlineContainer.pivot = new Vector2(0.5f, 0.5f);
        headlineContainer.anchoredPosition = Vector2.zero;
        headlineContainer.sizeDelta = new Vector2(1600f, 640f);
        headlineGroup = EnsureGroup(headlineContainer);

        // Big headline — auto-sizes between 72 and 200 px.
        headlineText = ContentCardUIBuilder.CreateText(
            headlineContainer, "BigHeadlineText",
            ContentCardUIBuilder.TextPrimary,
            160f, TextAlignmentOptions.Center,
            FontStyles.Bold);
        ContentCardUIBuilder.SetStretch(headlineText.rectTransform, 40f, 40f, 40f, 96f);
        headlineText.enableAutoSizing = true;
        headlineText.fontSizeMin = 72f;
        headlineText.fontSizeMax = 200f;
        headlineText.maxVisibleLines = 4;
        headlineText.overflowMode = TextOverflowModes.Ellipsis;
        headlineText.enableWordWrapping = true;

        // Source attribution, pinned to the bottom of the container.
        sourceText = ContentCardUIBuilder.CreateText(
            headlineContainer, "SourceText",
            ContentCardUIBuilder.TextTertiary,
            40f, TextAlignmentOptions.Center,
            FontStyles.Italic);
        RectTransform srt = sourceText.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = new Vector2(0f, 24f);
        srt.sizeDelta = new Vector2(-80f, 48f);
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
            .DOAnchorPos(Vector2.zero, SlideDuration));

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

    // Adds a RawImage child that fills the parent and samples a repeating
    // pattern texture through the UIScrollingPattern shader. Silently skips
    // if the shader or texture can't be found — the plain overlay still works.
    private void BuildPatternLayer(RectTransform parent)
    {
        Texture2D patternTex = Resources.Load<Texture2D>(PATTERN_RESOURCE_PATH);
        if (patternTex == null)
        {
            Debug.Log($"[BigCenter] No pattern texture at Resources/{PATTERN_RESOURCE_PATH} — skipping pattern layer.");
            return;
        }

        Shader shader = Shader.Find("Custom/UIScrollingPattern");
        if (shader == null)
        {
            Debug.LogWarning("[BigCenter] Shader \"Custom/UIScrollingPattern\" not found — skipping pattern layer.");
            return;
        }

        patternTex.wrapMode = TextureWrapMode.Repeat;

        GameObject patternGO = new GameObject("OverlayPattern", typeof(RectTransform));
        patternGO.transform.SetParent(parent, false);
        RectTransform prt = patternGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        RawImage patternImg = patternGO.AddComponent<RawImage>();
        patternImg.raycastTarget = false;
        patternImg.texture = patternTex;
        patternImg.color = new Color(1f, 1f, 1f, PATTERN_TINT_ALPHA);

        // Per-instance material so tweaking one card doesn't mutate a shared asset.
        patternMaterial = new Material(shader);
        patternMaterial.SetFloat("_TileScale", PATTERN_TILE_SCALE);
        patternMaterial.SetFloat("_TilePadding", PATTERN_TILE_PADDING);
        patternMaterial.SetFloat("_ScrollSpeed", PATTERN_SCROLL_SPEED);
        patternImg.material = patternMaterial;
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

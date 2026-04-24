using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

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
    private RectTransform overlayPanel;
    private RectTransform headlineContainer;
    private TextMeshProUGUI headlineText;
    private TextMeshProUGUI sourceText;
    private Material patternMaterial;

    private const float OVERLAY_SLIDE_DURATION = 0.5f;
    private const float HEADLINE_DROP_DURATION = 0.55f;

    // Pattern texture is loaded from Resources at runtime. Drop a seamlessly
    // tileable texture at Assets/Resources/Patterns/Default.png (or change
    // PATTERN_RESOURCE_PATH) to enable the scrolling pattern overlay.
    private const string PATTERN_RESOURCE_PATH = "Patterns/Default";
    private const float PATTERN_TINT_ALPHA = 0.35f;
    private const float PATTERN_TILE_SCALE = 12f;
    private const float PATTERN_TILE_PADDING = 0.05f;
    private const float PATTERN_SCROLL_SPEED = 0.36f;

    protected override void BuildUI()
    {
        // 1. Fullscreen overlay panel (first child → drawn behind the headline).
        //    Uses the same preset-aware background as every other card, but
        //    forced to full alpha — BigCenter takes over the whole screen and
        //    the character behind should be fully masked.
        Image panelImg = ContentCardUIBuilder.CreateBackground(rectTransform);
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
            32f, TextAlignmentOptions.Center,
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

        // The entrance is the overlay+headline sliding in, not a fade. Flip to
        // fully visible immediately (base Awake set alpha to 0).
        canvasGroup.alpha = 1f;

        // Flatten any preset rotation — a big centered overlay reads cleanest
        // straight, same rationale as BigMediaCard.
        rectTransform.localEulerAngles = Vector3.zero;
        headlineContainer.localEulerAngles = Vector3.zero;

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float headlineDropDistance = screenHeight * 0.5f + 240f;

        // Overlay panel starts fully below the screen, headline fully above it.
        overlayPanel.anchoredPosition = new Vector2(0f, -screenHeight);
        headlineContainer.anchoredPosition = new Vector2(0f, headlineDropDistance);

        currentSequence = DOTween.Sequence()
            .Join(overlayPanel.DOAnchorPosY(0f, OVERLAY_SLIDE_DURATION).SetEase(Ease.OutQuad))
            .Join(headlineContainer.DOAnchorPos(Vector2.zero, HEADLINE_DROP_DURATION).SetEase(OVERSHOOT_CURVE));
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

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float headlineExit = screenHeight * 0.5f + 240f;

        currentSequence = DOTween.Sequence()
            .Join(canvasGroup.DOFade(0f, FADE_OUT_DURATION).SetEase(Ease.InQuad))
            .Join(overlayPanel.DOAnchorPosY(-screenHeight, FADE_OUT_DURATION).SetEase(Ease.InQuad))
            .Join(headlineContainer.DOAnchorPosY(headlineExit, FADE_OUT_DURATION).SetEase(Ease.InQuad))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    protected override void OnDestroy()
    {
        if (patternMaterial != null)
        {
            Destroy(patternMaterial);
            patternMaterial = null;
        }
        base.OnDestroy();
    }
}

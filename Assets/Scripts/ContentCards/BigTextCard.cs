using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Large centered text card. One or more lines slide up from off-screen
/// to a vertically-stacked, centered group with the same CSS-derived
/// overshoot the rest of the cards use.
///
/// Tag: {BigText:line,duration}  or  {BigText:line1+line2+...,duration}
///
/// Lines joined by '+' produce up to <see cref="MAX_LINES"/> stacked lines.
/// Each line slides in from below the screen with the OVERSHOOT_CURVE,
/// staggered by <see cref="STAGGER_DELAY"/> so additional lines appear to
/// "count up" beneath the first — the same rhythm as <see cref="BigMediaCard"/>.
///
/// Lives in the fullscreen feature-media zone so the text renders over
/// the character, matching <see cref="BigCenterCard"/> and <see cref="BigMediaCard"/>.
/// </summary>
public class BigTextCard : ContentCard
{
    private const int MAX_LINES = 4;
    private const float STAGGER_DELAY = 0.35f;
    private const float SLIDE_DURATION = 0.55f;

    private const float LINE_HEIGHT = 200f;
    private const float LINE_GAP = 24f;
    private const float LINE_HORIZONTAL_PADDING = 80f;

    private readonly List<RectTransform> lineContainers = new List<RectTransform>(MAX_LINES);
    private readonly List<TextMeshProUGUI> lineTexts = new List<TextMeshProUGUI>(MAX_LINES);

    private int activeLineCount;

    protected override void BuildUI()
    {
        // Pre-build MAX_LINES slots; Initialize() decides how many to activate
        // based on the number of '+'-separated lines in the tag.
        for (int i = 0; i < MAX_LINES; i++)
        {
            GameObject containerGO = new GameObject($"BigTextLine_{i}", typeof(RectTransform));
            containerGO.transform.SetParent(rectTransform, false);

            RectTransform containerRT = containerGO.GetComponent<RectTransform>();
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0.5f, 0.5f);
            containerRT.sizeDelta = new Vector2(1600f, LINE_HEIGHT);

            TextMeshProUGUI tmp = ContentCardUIBuilder.CreateText(
                containerRT, "Text",
                Color.white,
                160f, TextAlignmentOptions.Center,
                FontStyles.Bold);
            ContentCardUIBuilder.SetStretch(tmp.rectTransform,
                LINE_HORIZONTAL_PADDING, 0f, LINE_HORIZONTAL_PADDING, 0f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 64f;
            tmp.fontSizeMax = 200f;
            tmp.maxVisibleLines = 2;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableWordWrapping = true;

            ApplyDarkOutline(tmp);

            containerGO.SetActive(false);

            lineContainers.Add(containerRT);
            lineTexts.Add(tmp);
        }
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        // Multi-line syntax: lines joined by '+' — e.g. "100M Users+$50B Revenue".
        // A single line (no '+') collapses to a single centered slot.
        string raw = data.primaryText ?? string.Empty;
        string[] lines = raw.Split('+');
        activeLineCount = Mathf.Clamp(lines.Length, 0, MAX_LINES);

        for (int i = 0; i < activeLineCount; i++)
        {
            lineTexts[i].text = lines[i].Trim();
            lineContainers[i].gameObject.SetActive(true);
        }
        for (int i = activeLineCount; i < MAX_LINES; i++)
            lineContainers[i].gameObject.SetActive(false);

        LayoutLines(activeLineCount);
    }

    // Vertical stack centered on the parent. Stack height grows with line
    // count; the group is always centered around y=0.
    private void LayoutLines(int count)
    {
        if (count <= 0) return;

        float totalHeight = count * LINE_HEIGHT + Mathf.Max(0, count - 1) * LINE_GAP;
        float topCenter = totalHeight * 0.5f - LINE_HEIGHT * 0.5f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = lineContainers[i];
            float y = topCenter - i * (LINE_HEIGHT + LINE_GAP);
            rt.anchoredPosition = new Vector2(0f, y);
        }
    }

    public override void Show()
    {
        KillCurrentSequence();

        // BigText owns its own entry — flatten any preset rotation and force
        // the CanvasGroup fully visible, then slide each line in turn from
        // below the screen up to its stacked resting position.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float offscreenDrop = screenHeight * 0.5f + 240f;

        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            rt.localEulerAngles = Vector3.zero;

            Vector2 endPos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(endPos.x, endPos.y - offscreenDrop);

            seq.Insert(STAGGER_DELAY * i,
                rt.DOAnchorPos(endPos, SLIDE_DURATION).SetEase(OVERSHOOT_CURVE));
        }

        currentSequence = seq;
    }

    // Crisp dark outline around each glyph so white text stays readable on
    // light backgrounds. Touching fontMaterial first forces TMP to clone the
    // shared material so this only affects this card's text instances. The
    // OUTLINE_ON keyword is the shader-side toggle that the "Outline"
    // checkbox in the TMP material inspector flips — without it, setting
    // _OutlineColor / _OutlineWidth on some shader variants is a no-op.
    private static void ApplyDarkOutline(TextMeshProUGUI tmp)
    {
        Material mat = tmp.fontMaterial;
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.75f));
        mat.SetFloat("_OutlineWidth", 0.1f);

        tmp.outlineColor = new Color(0f, 0f, 0f, 0.75f);
        tmp.outlineWidth = 0.1f;
        tmp.UpdateMeshPadding();
    }

    public override void Hide(bool fast = false)
    {
        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float offscreenDrop = screenHeight * 0.5f + 240f;

        Sequence seq = DOTween.Sequence();
        seq.Join(canvasGroup.DOFade(0f, FADE_OUT_DURATION).SetEase(Ease.InQuad));

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            Vector2 startPos = rt.anchoredPosition;
            seq.Join(rt.DOAnchorPosY(startPos.y - offscreenDrop, FADE_OUT_DURATION)
                .SetEase(Ease.InQuad));
        }

        seq.OnComplete(() => OnHideComplete?.Invoke());
        currentSequence = seq;
    }
}

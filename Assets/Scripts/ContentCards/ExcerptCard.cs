using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Displays excerpt text with a highlighted phrase that wipes in.
/// Tag: {Excerpt:"full text","highlighted phrase","source name",duration}
///
/// The highlighted phrase is emphasized with a brand-color background wipe
/// using TMP's native <mark> rich text tag, revealing character-by-character
/// left-to-right. Works correctly even when the phrase wraps across lines.
/// The highlight is a TMP mark, so it scales with the text. TMP draws a mark
/// OVER its own glyphs (which tinted the phrase coral), so the mark lives on a
/// copy of the passage laid out identically BEHIND it, whose glyphs are
/// invisible — the phrase stays in the text colour on top of the highlight.
///
/// Typography (see <see cref="CardTypography"/>): the passage, left-aligned,
/// auto-fits 56–80 px (warning under 44) with the source beneath it, as one
/// vertically centred group. No line cap — the passage renders in full.
/// </summary>
public class ExcerptCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Excerpt;

    [Header("Highlight Settings")]
    public float highlightWipeDuration = 0.4f;
    public float autoHighlightDelay = 1.5f;

    [Tooltip("Hex color (no #) used behind the highlighted phrase. Alpha is animated.")]
    public string highlightColorRGB = "E85D4A";
    [Range(0, 255)]
    public int highlightFinalAlpha = 170; // 0xAA = ~66% opacity

    private TextMeshProUGUI excerptText;
    private TextMeshProUGUI highlightLayer; // same passage, invisible glyphs, carries the <mark>
    private TextMeshProUGUI sourceText;

    private string fullText;
    private string highlightPhrase;
    private bool highlightTriggered = false;
    private Coroutine autoHighlightCoroutine;

    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Created first so it renders BEHIND the passage.
        highlightLayer = ContentCardUIBuilder.CreateText(
            rectTransform, "HighlightLayer",
            ContentCardUIBuilder.TextPrimary,
            CardTypography.BodyMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(highlightLayer, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
        highlightLayer.richText = true;
        highlightLayer.text = string.Empty;

        excerptText = ContentCardUIBuilder.CreateText(
            rectTransform, "ExcerptText",
            ContentCardUIBuilder.TextPrimary,
            CardTypography.BodyMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(excerptText, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
        excerptText.richText = true;

        sourceText = ContentCardUIBuilder.CreateText(
            rectTransform, "SourceText",
            ContentCardUIBuilder.TextTertiary,
            CardTypography.ExcerptSourcePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(sourceText, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        fullText = data.primaryText ?? string.Empty;
        highlightPhrase = data.secondaryText;
        excerptText.text = fullText; // plain text, no highlight yet
        sourceText.text = data.tertiaryText ?? string.Empty;
        LayoutCard(log: true);
    }

    void LateUpdate()
    {
        // Only when the card's size changed after Initialize — the slide moves
        // the card, it never resizes it.
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
            LayoutCard(log: false);
    }

    private void LayoutCard(bool log)
    {
        laidOutSize = CardTypography.CardSize(rectTransform);

        // Measure the PLAIN passage: the <mark> the wipe adds never changes the layout.
        string shown = excerptText.text;
        excerptText.text = fullText;

        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Excerpt", fullText) };
        var body = new CardTypography.Row { Name = "passage", Text = excerptText };
        var rows = new List<CardTypography.Row>
        {
            new CardTypography.Row { Name = "source", Text = sourceText, SizePx = CardTypography.ExcerptSourcePx,
                                     FloorPx = CardTypography.ExcerptSourceFloorPx,
                                     GapAbovePx = CardTypography.BodyToAttributionGapPx },
        };
        CardTypography.LayoutBodyGroup(rectTransform, report, body,
            CardTypography.BodyMinPx, CardTypography.BodyMaxPx, CardTypography.BodyWarnFloorPx, rows);

        excerptText.text = shown;
        MirrorHighlightLayer();
        Layout = report;
        if (log) report.LogSummary();
    }

    public override void Show()
    {
        base.Show();
        autoHighlightCoroutine = StartCoroutine(AutoHighlightFallback());
    }

    /// <summary>
    /// Trigger the highlight wipe. Can be called manually to sync with the narrator's voice.
    /// Auto-triggers after autoHighlightDelay seconds if not called manually.
    /// </summary>
    public void TriggerHighlight()
    {
        if (highlightTriggered) return;
        highlightTriggered = true;

        if (autoHighlightCoroutine != null)
        {
            StopCoroutine(autoHighlightCoroutine);
            autoHighlightCoroutine = null;
        }

        StartCoroutine(WipeHighlight());
    }

    private IEnumerator AutoHighlightFallback()
    {
        yield return new WaitForSeconds(autoHighlightDelay);
        if (!highlightTriggered)
            TriggerHighlight();
    }

    private IEnumerator WipeHighlight()
    {
        if (string.IsNullOrEmpty(highlightPhrase))
            yield break;

        int startIdx = fullText.IndexOf(highlightPhrase);
        if (startIdx < 0)
        {
            Debug.LogWarning($"ExcerptCard: highlight phrase \"{highlightPhrase}\" not found in excerpt");
            yield break;
        }

        int phraseLen = highlightPhrase.Length;
        int endIdx = startIdx + phraseLen;
        string alphaHex = highlightFinalAlpha.ToString("X2");
        string markOpen = $"<mark=#{highlightColorRGB}{alphaHex}>";
        const string markClose = "</mark>";

        string before = fullText.Substring(0, startIdx);
        string phrase = fullText.Substring(startIdx, phraseLen);
        string after = fullText.Substring(endIdx);

        float elapsed = 0f;
        while (elapsed < highlightWipeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / highlightWipeDuration);
            // Ease-out
            t = 1f - (1f - t) * (1f - t);

            int revealedChars = Mathf.Clamp(Mathf.RoundToInt(t * phraseLen), 0, phraseLen);
            string markedPart = phrase.Substring(0, revealedChars);
            string plainPart = phrase.Substring(revealedChars);

            highlightLayer.text = HiddenGlyphs + before + ShownGlyphs + markOpen + markedPart + markClose +
                                  HiddenGlyphs + plainPart + after;
            yield return null;
        }

        highlightLayer.text = HiddenGlyphs + before + ShownGlyphs + markOpen + phrase + markClose +
                              HiddenGlyphs + after;
    }

    // TMP caps a mark's alpha at the current <alpha>, so the layer can't hide
    // ALL its glyphs: it hides those outside the mark and keeps the marked
    // ones, which sit exactly under the passage's identical glyphs on top.
    private const string HiddenGlyphs = "<alpha=#00>";
    private const string ShownGlyphs = "<alpha=#FF>";

    // Same font, size, style, alignment and rect as the passage, so the
    // layer's lines — and therefore its mark — sit exactly under it.
    private void MirrorHighlightLayer()
    {
        highlightLayer.font = excerptText.font;
        highlightLayer.fontStyle = excerptText.fontStyle;
        highlightLayer.fontSize = excerptText.fontSize;
        highlightLayer.alignment = excerptText.alignment;
        highlightLayer.lineSpacing = excerptText.lineSpacing;
        highlightLayer.characterSpacing = excerptText.characterSpacing;
        highlightLayer.wordSpacing = excerptText.wordSpacing;

        RectTransform src = excerptText.rectTransform, dst = highlightLayer.rectTransform;
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot = src.pivot;
        dst.offsetMin = src.offsetMin;
        dst.offsetMax = src.offsetMax;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (autoHighlightCoroutine != null)
            StopCoroutine(autoHighlightCoroutine);
    }
}

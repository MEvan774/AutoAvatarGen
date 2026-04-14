using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Displays excerpt text with a highlighted phrase that wipes in.
/// Tag: {Excerpt:"full text","highlighted phrase","source name",duration}
///
/// The highlighted phrase is emphasized with a brand-color background wipe
/// using TMP's native <mark> rich text tag, revealing character-by-character
/// left-to-right. Works correctly even when the phrase wraps across lines.
/// </summary>
public class ExcerptCard : ContentCard
{
    [Header("Highlight Settings")]
    public float highlightWipeDuration = 0.4f;
    public float autoHighlightDelay = 1.5f;

    [Tooltip("Hex color (no #) used behind the highlighted phrase. Alpha is animated.")]
    public string highlightColorRGB = "E85D4A";
    [Range(0, 255)]
    public int highlightFinalAlpha = 170; // 0xAA = ~66% opacity

    private TextMeshProUGUI excerptText;
    private TextMeshProUGUI sourceText;

    private string fullText;
    private string highlightPhrase;
    private bool highlightTriggered = false;
    private Coroutine autoHighlightCoroutine;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Excerpt text
        excerptText = ContentCardUIBuilder.CreateText(
            rectTransform, "ExcerptText",
            ContentCardUIBuilder.TextPrimary,
            32f, TextAlignmentOptions.Center);
        ContentCardUIBuilder.SetStretch(excerptText.rectTransform, 32f, 32f, 32f, 80f);
        excerptText.enableAutoSizing = true;
        excerptText.fontSizeMin = 24f;
        excerptText.fontSizeMax = 34f;
        excerptText.maxVisibleLines = 3;
        excerptText.overflowMode = TextOverflowModes.Ellipsis;
        excerptText.richText = true;

        // Source text at bottom
        sourceText = ContentCardUIBuilder.CreateText(
            rectTransform, "SourceText",
            ContentCardUIBuilder.TextTertiary,
            20f, TextAlignmentOptions.MidlineLeft);
        sourceText.rectTransform.anchorMin = new Vector2(0f, 0f);
        sourceText.rectTransform.anchorMax = new Vector2(1f, 0f);
        sourceText.rectTransform.pivot = new Vector2(0f, 0f);
        sourceText.rectTransform.anchoredPosition = new Vector2(24f, 24f);
        sourceText.rectTransform.sizeDelta = new Vector2(-48f, 32f);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        fullText = data.primaryText;
        highlightPhrase = data.secondaryText;
        excerptText.text = fullText; // plain text, no highlight yet
        sourceText.text = data.tertiaryText;
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

            excerptText.text = before + markOpen + markedPart + markClose + plainPart + after;
            yield return null;
        }

        excerptText.text = before + markOpen + phrase + markClose + after;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (autoHighlightCoroutine != null)
            StopCoroutine(autoHighlightCoroutine);
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

/// <summary>
/// Displays excerpt text with a highlighted phrase that wipes in.
/// Tag: {Excerpt:"full text","highlighted phrase","source name",duration}
///
/// Prefab hierarchy:
///   ExcerptCard [RectTransform + CanvasGroup + ExcerptCard]
///     Background [Image: 9-slice, rgba(15,15,20,0.85)]
///       ExcerptText [TMP: white, 28-34px, auto-size 24-34, max 3 lines]
///       HighlightBar [Image: #E85D4A at 40% alpha, initially width=0]
///       SourceContainer [bottom-left]
///         SourceLogo [Image: optional]
///         SourceText [TMP: #FFFFFF99, 18-22px]
/// </summary>
public class ExcerptCard : ContentCard
{
    [Header("UI References")]
    public TextMeshProUGUI excerptText;
    public RectTransform highlightBar;
    public TextMeshProUGUI sourceText;
    public Image sourceLogo;

    [Header("Highlight Settings")]
    public float highlightWipeDuration = 0.4f;
    public float autoHighlightDelay = 1.5f;

    private string highlightPhrase;
    private bool highlightTriggered = false;
    private Coroutine autoHighlightCoroutine;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        highlightPhrase = data.secondaryText;

        if (excerptText != null)
        {
            excerptText.text = data.primaryText;
            excerptText.enableAutoSizing = true;
            excerptText.fontSizeMin = 24f;
            excerptText.fontSizeMax = 34f;
            excerptText.maxVisibleLines = 3;
            excerptText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (sourceText != null)
            sourceText.text = data.tertiaryText;

        if (sourceLogo != null)
        {
            Sprite logo = assets != null ? assets.GetLogo(data.tertiaryText) : null;
            if (logo != null)
            {
                sourceLogo.sprite = logo;
                sourceLogo.gameObject.SetActive(true);
            }
            else
            {
                sourceLogo.gameObject.SetActive(false);
            }
        }

        // Start invisible
        if (highlightBar != null)
            highlightBar.sizeDelta = new Vector2(0f, highlightBar.sizeDelta.y);
    }

    public override void Show()
    {
        base.Show();

        // Start auto-highlight fallback timer
        autoHighlightCoroutine = StartCoroutine(AutoHighlightFallback());
    }

    /// <summary>
    /// Call this manually to trigger the highlight wipe, timed to the narrator's voice.
    /// If not called, auto-triggers after autoHighlightDelay seconds.
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

        StartCoroutine(PositionAndWipeHighlight());
    }

    private IEnumerator AutoHighlightFallback()
    {
        yield return new WaitForSeconds(autoHighlightDelay);

        if (!highlightTriggered)
            TriggerHighlight();
    }

    private IEnumerator PositionAndWipeHighlight()
    {
        if (excerptText == null || highlightBar == null || string.IsNullOrEmpty(highlightPhrase))
            yield break;

        // Force mesh update so TMP_TextInfo is populated
        excerptText.ForceMeshUpdate();
        yield return null;

        TMP_TextInfo textInfo = excerptText.textInfo;
        string fullText = excerptText.text;

        int startIndex = fullText.IndexOf(highlightPhrase);
        if (startIndex < 0)
        {
            Debug.LogWarning($"ExcerptCard: Highlight phrase \"{highlightPhrase}\" not found in text");
            yield break;
        }

        int endIndex = startIndex + highlightPhrase.Length - 1;

        // Clamp to valid character range
        if (endIndex >= textInfo.characterCount)
            endIndex = textInfo.characterCount - 1;
        if (startIndex >= textInfo.characterCount)
            yield break;

        // Get bounds of the highlight region
        TMP_CharacterInfo startCharInfo = textInfo.characterInfo[startIndex];
        TMP_CharacterInfo endCharInfo = textInfo.characterInfo[endIndex];

        // Get the first visible character's bottom-left and last character's top-right
        Vector3 bottomLeft = excerptText.transform.TransformPoint(startCharInfo.bottomLeft);
        Vector3 topRight = excerptText.transform.TransformPoint(endCharInfo.topRight);

        // Convert to local space of the highlight bar's parent
        Vector3 localBL = highlightBar.parent.InverseTransformPoint(bottomLeft);
        Vector3 localTR = highlightBar.parent.InverseTransformPoint(topRight);

        float targetWidth = localTR.x - localBL.x;
        float height = localTR.y - localBL.y;

        // Position the highlight bar at the start of the phrase
        highlightBar.anchoredPosition = new Vector2(localBL.x, localBL.y);
        highlightBar.sizeDelta = new Vector2(0f, height + 4f); // Slight padding
        highlightBar.pivot = new Vector2(0f, 0f); // Anchor left edge

        // Wipe from left to right
        highlightBar.DOSizeDelta(
            new Vector2(targetWidth, height + 4f),
            highlightWipeDuration
        ).SetEase(Ease.OutQuad);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (autoHighlightCoroutine != null)
            StopCoroutine(autoHighlightCoroutine);
    }
}

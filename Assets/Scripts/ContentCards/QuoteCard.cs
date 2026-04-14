using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays a quote with decorative quotation marks and attribution.
/// Tag: {Quote:"quote text","person name","role/title",duration}
/// Self-building: constructs its own UI hierarchy in Awake.
/// </summary>
public class QuoteCard : ContentCard
{
    private TextMeshProUGUI quoteText;
    private TextMeshProUGUI personName;
    private TextMeshProUGUI roleTitle;
    private RectTransform openQuoteMark;
    private RectTransform closeQuoteMark;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Open quote mark (top-left)
        TextMeshProUGUI openQuote = ContentCardUIBuilder.CreateText(
            rectTransform, "OpenQuote", ContentCardUIBuilder.AccentColor,
            80f, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        openQuote.text = "\u201C";
        openQuoteMark = openQuote.rectTransform;
        openQuoteMark.anchorMin = new Vector2(0f, 1f);
        openQuoteMark.anchorMax = new Vector2(0f, 1f);
        openQuoteMark.pivot = new Vector2(0f, 1f);
        openQuoteMark.anchoredPosition = new Vector2(24f, -16f);
        openQuoteMark.sizeDelta = new Vector2(80f, 80f);

        // Close quote mark (bottom-right)
        TextMeshProUGUI closeQuote = ContentCardUIBuilder.CreateText(
            rectTransform, "CloseQuote", ContentCardUIBuilder.AccentColor,
            80f, TextAlignmentOptions.BottomRight, FontStyles.Bold);
        closeQuote.text = "\u201D";
        closeQuoteMark = closeQuote.rectTransform;
        closeQuoteMark.anchorMin = new Vector2(1f, 0f);
        closeQuoteMark.anchorMax = new Vector2(1f, 0f);
        closeQuoteMark.pivot = new Vector2(1f, 0f);
        closeQuoteMark.anchoredPosition = new Vector2(-24f, 16f);
        closeQuoteMark.sizeDelta = new Vector2(80f, 80f);

        // Quote text (centered)
        quoteText = ContentCardUIBuilder.CreateText(
            rectTransform, "QuoteText",
            ContentCardUIBuilder.TextPrimary,
            36f, TextAlignmentOptions.Center, FontStyles.Italic);
        ContentCardUIBuilder.SetStretch(quoteText.rectTransform, 48f, 64f, 48f, 140f);
        quoteText.enableAutoSizing = true;
        quoteText.fontSizeMin = 28f;
        quoteText.fontSizeMax = 40f;

        // Person name (below quote)
        personName = ContentCardUIBuilder.CreateText(
            rectTransform, "PersonName",
            ContentCardUIBuilder.TextPrimary,
            26f, TextAlignmentOptions.Center, FontStyles.Bold);
        personName.rectTransform.anchorMin = new Vector2(0f, 0f);
        personName.rectTransform.anchorMax = new Vector2(1f, 0f);
        personName.rectTransform.pivot = new Vector2(0.5f, 0f);
        personName.rectTransform.anchoredPosition = new Vector2(0f, 80f);
        personName.rectTransform.sizeDelta = new Vector2(-96f, 32f);

        // Role title (below name)
        roleTitle = ContentCardUIBuilder.CreateText(
            rectTransform, "RoleTitle",
            ContentCardUIBuilder.TextSecondary,
            20f, TextAlignmentOptions.Center);
        roleTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
        roleTitle.rectTransform.anchorMax = new Vector2(1f, 0f);
        roleTitle.rectTransform.pivot = new Vector2(0.5f, 0f);
        roleTitle.rectTransform.anchoredPosition = new Vector2(0f, 48f);
        roleTitle.rectTransform.sizeDelta = new Vector2(-96f, 28f);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        quoteText.text = data.primaryText;
        personName.text = data.secondaryText;
        roleTitle.text = data.tertiaryText;
    }

    public override void Show()
    {
        if (openQuoteMark != null)
        {
            openQuoteMark.localScale = Vector3.one * 0.95f;
            openQuoteMark.DOScale(Vector3.one, FADE_IN_DURATION).SetEase(Ease.OutQuad);
        }
        if (closeQuoteMark != null)
        {
            closeQuoteMark.localScale = Vector3.one * 0.95f;
            closeQuoteMark.DOScale(Vector3.one, FADE_IN_DURATION).SetEase(Ease.OutQuad);
        }

        base.Show();
    }
}

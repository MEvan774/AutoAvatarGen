using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays a quote with decorative quotation marks and attribution.
/// Tag: {Quote:"quote text","person name","role/title",duration}
///
/// Prefab hierarchy:
///   QuoteCard [RectTransform + CanvasGroup + QuoteCard]
///     Background [Image: 9-slice, rgba(15,15,20,0.85)]
///       OpenQuote [TMP: U+201C, #E85D4A, 80px, top-left]
///       CloseQuote [TMP: U+201D, #E85D4A, 80px, bottom-right]
///       QuoteText [TMP: white, italic, auto-size 28-40px, centered]
///       PersonName [TMP: white, bold, 24-28px, centered below quote]
///       RoleTitle [TMP: #FFFFFFB3, 18-22px, centered below name]
/// </summary>
public class QuoteCard : ContentCard
{
    [Header("UI References")]
    public TextMeshProUGUI quoteText;
    public TextMeshProUGUI personName;
    public TextMeshProUGUI roleTitle;
    public RectTransform openQuoteMark;
    public RectTransform closeQuoteMark;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        if (quoteText != null)
        {
            quoteText.text = data.primaryText;
            quoteText.enableAutoSizing = true;
            quoteText.fontSizeMin = 28f;
            quoteText.fontSizeMax = 40f;
        }

        if (personName != null)
            personName.text = data.secondaryText;

        if (roleTitle != null)
            roleTitle.text = data.tertiaryText;
    }

    public override void Show()
    {
        // Scale quote marks from 95% to 100% for subtle emphasis
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

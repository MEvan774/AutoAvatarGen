using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays a bold headline with source attribution.
/// Tag: {Headline:"headline text","source name",duration}
///
/// Prefab hierarchy:
///   HeadlineCard [RectTransform + CanvasGroup + HeadlineCard]
///     Background [Image: 9-slice, rgba(15,15,20,0.85)]
///       AccentBar [Image: #E85D4A, 4px tall, top-stretch]
///       HeadlineText [TMP: white, bold, auto-size 40-56px, max 2 lines]
///       SourceContainer [HorizontalLayoutGroup, bottom-left]
///         SourceLogo [Image: 24-32px, optional]
///         SourceText [TMP: #FFFFFF99, 18-22px]
/// </summary>
public class HeadlineCard : ContentCard
{
    [Header("UI References")]
    public TextMeshProUGUI headlineText;
    public TextMeshProUGUI sourceText;
    public Image sourceLogo;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        if (headlineText != null)
        {
            headlineText.text = data.primaryText;
            headlineText.enableAutoSizing = true;
            headlineText.fontSizeMin = 40f;
            headlineText.fontSizeMax = 56f;
            headlineText.maxVisibleLines = 2;
            headlineText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (sourceText != null)
            sourceText.text = data.secondaryText;

        // Try to load source logo from assets
        if (sourceLogo != null)
        {
            Sprite logo = assets != null ? assets.GetLogo(data.secondaryText) : null;
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
    }
}

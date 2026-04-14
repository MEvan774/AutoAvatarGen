using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays a bold headline with source attribution.
/// Tag: {Headline:"headline text","source name",duration}
/// Self-building: constructs its own UI hierarchy in Awake.
/// </summary>
public class HeadlineCard : ContentCard
{
    private TextMeshProUGUI headlineText;
    private TextMeshProUGUI sourceText;
    private Image sourceLogo;

    protected override void BuildUI()
    {
        // Dark background
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Accent bar at top
        ContentCardUIBuilder.CreateAccentBar(rectTransform, 4f);

        // Headline (fills upper portion)
        headlineText = ContentCardUIBuilder.CreateText(
            rectTransform, "HeadlineText",
            ContentCardUIBuilder.TextPrimary,
            48f, TextAlignmentOptions.TopLeft,
            FontStyles.Bold);
        ContentCardUIBuilder.SetStretch(headlineText.rectTransform, 24f, 24f, 24f, 100f);
        headlineText.enableAutoSizing = true;
        headlineText.fontSizeMin = 40f;
        headlineText.fontSizeMax = 56f;
        headlineText.maxVisibleLines = 2;
        headlineText.overflowMode = TextOverflowModes.Ellipsis;

        // Source container at bottom-left
        RectTransform sourceContainer = ContentCardUIBuilder.CreateChild(rectTransform, "SourceContainer");
        sourceContainer.anchorMin = new Vector2(0f, 0f);
        sourceContainer.anchorMax = new Vector2(1f, 0f);
        sourceContainer.pivot = new Vector2(0f, 0f);
        sourceContainer.anchoredPosition = new Vector2(24f, 24f);
        sourceContainer.sizeDelta = new Vector2(-48f, 40f);

        // Source logo (hidden by default)
        GameObject logoGO = new GameObject("SourceLogo", typeof(RectTransform));
        logoGO.transform.SetParent(sourceContainer, false);
        RectTransform logoRT = logoGO.GetComponent<RectTransform>();
        logoRT.anchorMin = new Vector2(0f, 0.5f);
        logoRT.anchorMax = new Vector2(0f, 0.5f);
        logoRT.pivot = new Vector2(0f, 0.5f);
        logoRT.sizeDelta = new Vector2(32f, 32f);
        logoRT.anchoredPosition = Vector2.zero;
        sourceLogo = logoGO.AddComponent<Image>();
        sourceLogo.preserveAspect = true;
        sourceLogo.raycastTarget = false;
        logoGO.SetActive(false);

        // Source text
        sourceText = ContentCardUIBuilder.CreateText(
            sourceContainer, "SourceText",
            ContentCardUIBuilder.TextTertiary,
            22f, TextAlignmentOptions.MidlineLeft);
        sourceText.rectTransform.anchorMin = new Vector2(0f, 0f);
        sourceText.rectTransform.anchorMax = new Vector2(1f, 1f);
        sourceText.rectTransform.offsetMin = new Vector2(40f, 0f);
        sourceText.rectTransform.offsetMax = Vector2.zero;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        headlineText.text = data.primaryText;
        sourceText.text = data.secondaryText;

        Sprite logo = assets != null ? assets.GetLogo(data.secondaryText) : null;
        if (logo != null)
        {
            sourceLogo.sprite = logo;
            sourceLogo.gameObject.SetActive(true);
            sourceText.rectTransform.offsetMin = new Vector2(40f, 0f);
        }
        else
        {
            sourceLogo.gameObject.SetActive(false);
            sourceText.rectTransform.offsetMin = Vector2.zero;
        }
    }
}

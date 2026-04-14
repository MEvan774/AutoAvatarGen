using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays a company logo centered in the content zone with text fallback.
/// Tag: {Logo:company_name,duration}
/// Self-building: constructs its own UI hierarchy in Awake.
/// </summary>
public class LogoDisplay : ContentCard
{
    private Image logoImage;
    private TextMeshProUGUI fallbackText;
    private RectTransform logoRect;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Logo image (centered, 60% width / 50% height)
        GameObject logoGO = new GameObject("LogoImage", typeof(RectTransform));
        logoGO.transform.SetParent(rectTransform, false);
        logoRect = logoGO.GetComponent<RectTransform>();
        logoRect.anchorMin = new Vector2(0.2f, 0.25f);
        logoRect.anchorMax = new Vector2(0.8f, 0.75f);
        logoRect.offsetMin = Vector2.zero;
        logoRect.offsetMax = Vector2.zero;

        logoImage = logoGO.AddComponent<Image>();
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;
        logoGO.SetActive(false);

        // Fallback text (shown when logo sprite not found)
        fallbackText = ContentCardUIBuilder.CreateText(
            rectTransform, "FallbackText",
            ContentCardUIBuilder.TextPrimary,
            48f, TextAlignmentOptions.Center, FontStyles.Bold);
        ContentCardUIBuilder.SetStretch(fallbackText.rectTransform, 24f, 24f, 24f, 24f);
        fallbackText.gameObject.SetActive(false);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        Sprite logo = assets != null ? assets.GetLogo(data.primaryText) : null;

        if (logo != null)
        {
            logoImage.sprite = logo;
            logoImage.gameObject.SetActive(true);
            fallbackText.gameObject.SetActive(false);
        }
        else
        {
            logoImage.gameObject.SetActive(false);
            fallbackText.gameObject.SetActive(true);
            string name = data.primaryText;
            if (name.Length > 0)
                name = char.ToUpper(name[0]) + name.Substring(1);
            fallbackText.text = name;
        }
    }

    public override void Show()
    {
        if (logoImage.gameObject.activeSelf)
        {
            logoRect.localScale = Vector3.one * 0.95f;
            logoRect.DOScale(Vector3.one, FADE_IN_DURATION).SetEase(Ease.OutQuad);
        }

        base.Show();
    }
}

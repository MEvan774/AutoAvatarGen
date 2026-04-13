using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays a company logo centered in the content zone with text fallback.
/// Tag: {Logo:company_name,duration}
///
/// Prefab hierarchy:
///   LogoDisplay [RectTransform + CanvasGroup + LogoDisplay]
///     Background [Image: 9-slice, rgba(15,15,20,0.85)]
///       LogoImage [Image: preserveAspect, centered, max 60% width / 50% height]
///       FallbackText [TMP: white, bold, 40px, centered, hidden by default]
/// </summary>
public class LogoDisplay : ContentCard
{
    [Header("UI References")]
    public Image logoImage;
    public TextMeshProUGUI fallbackText;

    private RectTransform logoRect;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        Sprite logo = assets != null ? assets.GetLogo(data.primaryText) : null;

        if (logo != null && logoImage != null)
        {
            logoImage.sprite = logo;
            logoImage.preserveAspect = true;
            logoImage.gameObject.SetActive(true);
            logoRect = logoImage.GetComponent<RectTransform>();

            if (fallbackText != null)
                fallbackText.gameObject.SetActive(false);
        }
        else
        {
            // Fallback: show company name as text
            if (logoImage != null)
                logoImage.gameObject.SetActive(false);

            if (fallbackText != null)
            {
                fallbackText.gameObject.SetActive(true);
                // Title-case the company name
                string name = data.primaryText;
                if (name.Length > 0)
                    name = char.ToUpper(name[0]) + name.Substring(1);
                fallbackText.text = name;
            }
        }
    }

    public override void Show()
    {
        // Scale 95% -> 100% on the logo for subtle emphasis
        if (logoRect != null && logoImage.gameObject.activeSelf)
        {
            logoRect.localScale = Vector3.one * 0.95f;
            logoRect.DOScale(Vector3.one, FADE_IN_DURATION).SetEase(Ease.OutQuad);
        }

        base.Show();
    }
}

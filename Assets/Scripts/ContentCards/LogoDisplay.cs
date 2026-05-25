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
    protected override ContentCardType CardType => ContentCardType.Logo;

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
        // Tier 1 — ContentCardAssets logo dictionary (if an SO is wired up).
        Sprite logo = assets != null ? assets.GetLogo(data.primaryText) : null;

        // Tier 2 — direct Resources lookup. Drops the ContentCardAssets
        // requirement entirely: stick Brave.png in Assets/Resources/Media/
        // and {Logo:Brave,...} just works.
        if (logo == null)
            logo = LoadSpriteFromResources(data.primaryText, assets);

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

    // Resources-folder fallback shared with BigMediaCard. Tries Texture2D
    // FIRST because Resources.Load<Sprite> returns null for PNGs that were
    // imported with spriteMode = Multiple (Unity returns the sub-sprite name
    // like "Brave_0", not the asset name). Texture2D loads the underlying
    // image regardless of importer settings, and we wrap it in a Sprite.
    private static Sprite LoadSpriteFromResources(string name, ContentCardAssets assets)
    {
        string folder = (assets != null && !string.IsNullOrEmpty(assets.bigMediaResourcesFolder))
            ? assets.bigMediaResourcesFolder
            : "Media";
        string path = $"{folder}/{name}";

        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) return sprite;

        Sprite[] sprites = Resources.LoadAll<Sprite>(path);
        if (sprites != null && sprites.Length > 0) return sprites[0];

        return null;
    }

    public override void Show()
    {
        if (logoImage.gameObject.activeSelf)
        {
            logoRect.localScale = Vector3.one * 0.95f;
            logoRect.DOScale(Vector3.one, FadeInDuration).SetEase(Ease.OutQuad);
        }

        base.Show();
    }
}

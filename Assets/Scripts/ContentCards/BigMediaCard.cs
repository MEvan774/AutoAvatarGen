using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Large centered image/logo card that appears in front of the character.
/// Tag: {BigMedia:name,duration}
///
/// Visuals: no panel background — just a large, centered, aspect-preserving
/// image occupying ~70% of the parent. Parent is the fullscreen feature-media
/// zone (see <see cref="ContentZoneController"/>), so the asset sits on top of
/// the scene, in front of the character.
///
/// Animation: inherits the base ContentCard slide-in-from-left with the
/// CSS-derived overshoot curve, and fades out on Hide.
/// </summary>
public class BigMediaCard : ContentCard
{
    private Image bigImage;
    private TextMeshProUGUI fallbackText;

    protected override void BuildUI()
    {
        // Centered image — ~70% of parent width/height, preserves aspect.
        GameObject imgGO = new GameObject("BigMediaImage", typeof(RectTransform));
        imgGO.transform.SetParent(rectTransform, false);
        RectTransform imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0.15f, 0.15f);
        imgRT.anchorMax = new Vector2(0.85f, 0.85f);
        imgRT.offsetMin = Vector2.zero;
        imgRT.offsetMax = Vector2.zero;

        bigImage = imgGO.AddComponent<Image>();
        bigImage.preserveAspect = true;
        bigImage.raycastTarget = false;
        imgGO.SetActive(false);

        // Fallback text for missing asset
        fallbackText = ContentCardUIBuilder.CreateText(
            rectTransform, "FallbackText",
            ContentCardUIBuilder.TextPrimary,
            96f, TextAlignmentOptions.Center, FontStyles.Bold);
        ContentCardUIBuilder.SetStretch(fallbackText.rectTransform, 48f, 48f, 48f, 48f);
        fallbackText.gameObject.SetActive(false);
    }

    public override void Show()
    {
        base.Show();
        // BigMedia reads cleanest without the preset's random rotation — the
        // base slide/fade/overshoot is kept, rotation is flattened back to zero.
        rectTransform.localEulerAngles = Vector3.zero;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        Sprite sprite = assets != null ? assets.GetBigMedia(data.primaryText) : null;

        if (sprite != null)
        {
            bigImage.sprite = sprite;
            bigImage.gameObject.SetActive(true);
            fallbackText.gameObject.SetActive(false);
        }
        else
        {
            bigImage.gameObject.SetActive(false);
            fallbackText.gameObject.SetActive(true);
            string name = data.primaryText;
            if (name.Length > 0)
                name = char.ToUpper(name[0]) + name.Substring(1);
            fallbackText.text = name;
        }
    }
}

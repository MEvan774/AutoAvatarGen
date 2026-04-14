using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Text.RegularExpressions;

/// <summary>
/// Displays a large statistic number with a prominent win/loss arrow and color coding.
/// Tag: {Stat:"number","label","context",duration}
///
/// Direction is inferred from the context string:
///   - Contains "↑" or "+" → big green upward triangle + green number
///   - Contains "↓" or "-" → big red downward triangle + red number
///   - Otherwise           → no arrow, number in default orange accent
/// </summary>
public class StatCard : ContentCard
{
    [Header("Count-Up Settings")]
    public bool useCountUp = true;
    public float countUpDuration = 0.6f;

    [Header("Direction Arrow")]
    [Tooltip("If true, show a big ▲/▼ graphic next to the number and color it green/red.")]
    public bool showDirectionArrow = true;

    private TextMeshProUGUI numberText;
    private TextMeshProUGUI labelText;
    private TextMeshProUGUI contextText;
    private Image arrow;
    private RectTransform arrowRect;

    // Parsed number parts
    private string prefix;
    private string suffix;
    private float numericValue;
    private bool isNumeric;
    private string rawNumber;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // ---- Upper row: Arrow + Number ----
        // All layout uses proportional anchors so it scales with any content zone size.

        GameObject arrowGO = new GameObject("DirectionArrow", typeof(RectTransform));
        arrowGO.transform.SetParent(rectTransform, false);
        arrowRect = arrowGO.GetComponent<RectTransform>();
        // Anchor: single point at left-edge, vertically centered on where the text sits.
        // Text RT is anchored 0.40 to 1.00, so its vertical center is at 0.70.
        // Size is set dynamically in LateUpdate to match the number's actual font size.
        arrowRect.anchorMin = new Vector2(0f, 0.70f);
        arrowRect.anchorMax = new Vector2(0f, 0.70f);
        arrowRect.pivot = new Vector2(0f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(24f, 0f); // 24px padding from left
        arrowRect.sizeDelta = new Vector2(100f, 100f); // placeholder; LateUpdate sizes this

        arrow = arrowGO.AddComponent<Image>();
        arrow.sprite = ContentCardUIBuilder.GetArrowSprite();
        arrow.preserveAspect = true;
        arrow.raycastTarget = false;

        // Keeps the arrow square: width = height × 1.0.
        // Combined with LateUpdate's sizeDelta.y = fontSize * scale, this gives a
        // square arrow whose side length tracks the rendered text height.
        var fitter = arrowGO.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        fitter.aspectRatio = 1f;

        arrowGO.SetActive(false); // hidden until direction detected

        numberText = ContentCardUIBuilder.CreateText(
            rectTransform, "NumberText",
            ContentCardUIBuilder.AccentColor,
            84f, TextAlignmentOptions.Center, FontStyles.Bold);
        numberText.rectTransform.anchorMin = new Vector2(0.05f, 0.40f);
        numberText.rectTransform.anchorMax = new Vector2(0.95f, 1f);
        numberText.rectTransform.offsetMin = Vector2.zero;
        numberText.rectTransform.offsetMax = Vector2.zero;
        numberText.enableAutoSizing = true;
        numberText.fontSizeMin = 60f;
        numberText.fontSizeMax = 96f;

        // Label
        labelText = ContentCardUIBuilder.CreateText(
            rectTransform, "LabelText",
            ContentCardUIBuilder.TextPrimary,
            26f, TextAlignmentOptions.Center);
        labelText.rectTransform.anchorMin = new Vector2(0f, 0.2f);
        labelText.rectTransform.anchorMax = new Vector2(1f, 0.35f);
        labelText.rectTransform.offsetMin = new Vector2(24f, 0f);
        labelText.rectTransform.offsetMax = new Vector2(-24f, 0f);

        // Context
        contextText = ContentCardUIBuilder.CreateText(
            rectTransform, "ContextText",
            ContentCardUIBuilder.TextSecondary,
            22f, TextAlignmentOptions.Center);
        contextText.rectTransform.anchorMin = new Vector2(0f, 0.05f);
        contextText.rectTransform.anchorMax = new Vector2(1f, 0.2f);
        contextText.rectTransform.offsetMin = new Vector2(24f, 0f);
        contextText.rectTransform.offsetMax = new Vector2(-24f, 0f);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        rawNumber = data.primaryText;
        ParseNumericValue(rawNumber);

        Direction dir = DetectDirection(data.tertiaryText);
        Color directionColor = GetDirectionColor(dir);

        // Configure the arrow graphic
        if (showDirectionArrow && dir != Direction.None)
        {
            arrow.gameObject.SetActive(true);
            arrow.color = directionColor;
            // Sprite is drawn as down-pointing; flip Y for up-pointing
            arrowRect.localScale = (dir == Direction.Up)
                ? new Vector3(1f, -1f, 1f)
                : Vector3.one;

            // Number is sized & positioned in LateUpdate — here we just set anchors
            // so LateUpdate's pivot-based placement works cleanly.
            numberText.rectTransform.anchorMin = new Vector2(0f, 0.40f);
            numberText.rectTransform.anchorMax = new Vector2(0f, 1f);
            numberText.rectTransform.pivot = new Vector2(0f, 0.5f);
            numberText.alignment = TextAlignmentOptions.Left;
        }
        else
        {
            arrow.gameObject.SetActive(false);
            // No arrow: number uses the full width, centered
            numberText.rectTransform.anchorMin = new Vector2(0f, 0.40f);
            numberText.rectTransform.anchorMax = new Vector2(1f, 1f);
            numberText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            numberText.rectTransform.offsetMin = new Vector2(24f, 0f);
            numberText.rectTransform.offsetMax = new Vector2(-24f, 0f);
            numberText.alignment = TextAlignmentOptions.Center;
        }

        numberText.color = directionColor;
        numberText.text = (!useCountUp || !isNumeric) ? rawNumber : prefix + "0" + suffix;

        labelText.text = data.secondaryText;
        contextText.text = data.tertiaryText;
        contextText.color = directionColor;
    }

    public override void Show()
    {
        base.Show();

        // Scale arrow 90% → 100% for subtle pop (preserve Y-flip for up arrow)
        if (arrow.gameObject.activeSelf)
        {
            Vector3 targetScale = arrowRect.localScale; // may be (1,-1,1) for up
            arrowRect.localScale = targetScale * 0.9f;
            arrowRect.DOScale(targetScale, FADE_IN_DURATION).SetEase(Ease.OutBack);
        }

        if (useCountUp && isNumeric)
        {
            DOTween.To(
                () => 0f,
                value => numberText.text = prefix + FormatNumber(value) + suffix,
                numericValue,
                countUpDuration
            ).SetEase(Ease.OutQuad);
        }
    }

    // Multiplier applied to the number's fontSize to get the arrow's pixel height.
    // 1.0 = exactly the text height; slightly larger (e.g. 1.1) makes the arrow
    // slightly bulkier than the text glyphs so it reads as an icon.
    [SerializeField] private float arrowSizeMultiplier = 1.0f;

    // Spacing between arrow and number text in pixels.
    private const float ARROW_TEXT_SPACING = 24f;

    void LateUpdate()
    {
        if (arrow == null || !arrow.gameObject.activeSelf || numberText == null)
            return;

        // --- 1. Size the arrow to match the rendered text height ---
        float arrowSide = numberText.fontSize * arrowSizeMultiplier;
        arrowRect.sizeDelta = new Vector2(arrowSide, arrowSide);

        // --- 2. Measure the text's width using the FINAL value (not the count-up
        //       intermediate) so the layout stays stable during the count animation.
        string measureText = BuildFinalNumberText();
        Vector2 textSize = numberText.GetPreferredValues(measureText);
        float textWidth = textSize.x;

        // --- 3. Center the (arrow + spacing + text) group horizontally in the card ---
        float totalWidth = arrowSide + ARROW_TEXT_SPACING + textWidth;
        float cardWidth = rectTransform.rect.width;
        float startX = Mathf.Max(24f, (cardWidth - totalWidth) * 0.5f);

        arrowRect.anchoredPosition = new Vector2(startX, 0f);

        var textRT = numberText.rectTransform;
        textRT.anchoredPosition = new Vector2(startX + arrowSide + ARROW_TEXT_SPACING, 0f);
        textRT.sizeDelta = new Vector2(textWidth + 8f, textRT.sizeDelta.y);
    }

    /// <summary>Reconstructs the final fully-counted number string for measurement.</summary>
    private string BuildFinalNumberText()
    {
        if (!useCountUp || !isNumeric) return rawNumber;
        return prefix + FormatNumber(numericValue) + suffix;
    }

    private void ParseNumericValue(string raw)
    {
        var match = Regex.Match(raw, @"^([^\d]*?)([\d,.]+)([^\d]*)$");
        if (match.Success)
        {
            prefix = match.Groups[1].Value;
            suffix = match.Groups[3].Value;
            string numStr = match.Groups[2].Value.Replace(",", "");
            isNumeric = float.TryParse(numStr, out numericValue);
        }
        else
        {
            isNumeric = false;
            prefix = "";
            suffix = "";
        }
    }

    private string FormatNumber(float value)
    {
        if (numericValue >= 1000)
            return value.ToString("N0");
        if (numericValue == Mathf.Floor(numericValue))
            return Mathf.RoundToInt(value).ToString();
        return value.ToString("F1");
    }

    private enum Direction { None, Up, Down }

    private Direction DetectDirection(string context)
    {
        if (string.IsNullOrEmpty(context)) return Direction.None;
        if (context.Contains("\u2191") || context.Contains("\u25B2") || context.Contains("+"))
            return Direction.Up;
        if (context.Contains("\u2193") || context.Contains("\u25BC") || context.Contains("-"))
            return Direction.Down;
        return Direction.None;
    }

    private Color GetDirectionColor(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up:   return ContentCardUIBuilder.PositiveGreen;
            case Direction.Down: return ContentCardUIBuilder.AccentColor;
            default:             return ContentCardUIBuilder.AccentColor;
        }
    }
}

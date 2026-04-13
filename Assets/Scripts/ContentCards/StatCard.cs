using UnityEngine;
using TMPro;
using DG.Tweening;
using System.Text.RegularExpressions;

/// <summary>
/// Displays a large statistic number with optional count-up animation.
/// Tag: {Stat:"number","label","context",duration}
///
/// Prefab hierarchy:
///   StatCard [RectTransform + CanvasGroup + StatCard]
///     Background [Image: 9-slice, rgba(15,15,20,0.85)]
///       NumberText [TMP: #E85D4A, bold, auto-size 60-96px, centered upper]
///       LabelText [TMP: white, 24-28px, centered, 12px below number]
///       ContextText [TMP: 20-24px, centered, 8px below label]
/// </summary>
public class StatCard : ContentCard
{
    [Header("UI References")]
    public TextMeshProUGUI numberText;
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI contextText;

    [Header("Count-Up Settings")]
    public bool useCountUp = true;
    public float countUpDuration = 0.6f;

    // Colors for context indicators
    private static readonly Color PositiveColor = new Color(0.298f, 0.686f, 0.314f); // #4CAF50
    private static readonly Color NegativeColor = new Color(0.910f, 0.365f, 0.290f); // #E85D4A
    private static readonly Color NeutralColor = new Color(1f, 1f, 1f, 0.7f);        // #FFFFFFB3

    private string prefix;
    private string suffix;
    private float numericValue;
    private bool isNumeric;
    private string rawNumber;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        rawNumber = data.primaryText;
        ParseNumericValue(rawNumber);

        if (numberText != null)
        {
            numberText.enableAutoSizing = true;
            numberText.fontSizeMin = 60f;
            numberText.fontSizeMax = 96f;

            if (!useCountUp || !isNumeric)
                numberText.text = rawNumber;
            else
                numberText.text = prefix + "0" + suffix;
        }

        if (labelText != null)
            labelText.text = data.secondaryText;

        if (contextText != null)
        {
            contextText.text = data.tertiaryText;
            contextText.color = GetContextColor(data.tertiaryText);
        }
    }

    public override void Show()
    {
        base.Show();

        if (useCountUp && isNumeric && numberText != null)
        {
            DOTween.To(
                () => 0f,
                value => numberText.text = prefix + FormatNumber(value) + suffix,
                numericValue,
                countUpDuration
            ).SetEase(Ease.OutQuad);
        }
    }

    private void ParseNumericValue(string raw)
    {
        // Extract prefix (currency symbols, etc.), numeric part, and suffix (B, M, K, %)
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
        // Preserve the original number format
        if (numericValue >= 1000)
            return value.ToString("N0"); // Comma-separated
        else if (numericValue == Mathf.Floor(numericValue))
            return Mathf.RoundToInt(value).ToString();
        else
            return value.ToString("F1");
    }

    private Color GetContextColor(string context)
    {
        if (string.IsNullOrEmpty(context)) return NeutralColor;

        if (context.Contains("\u2191") || context.Contains("+"))
            return PositiveColor;
        if (context.Contains("\u2193") || context.Contains("-"))
            return NegativeColor;

        return NeutralColor;
    }
}

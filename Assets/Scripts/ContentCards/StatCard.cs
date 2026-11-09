using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Text.RegularExpressions;

/// <summary>
/// Displays a large statistic number with a prominent win/loss arrow and color coding.
/// Tag: {Stat:"number","label","context",duration[,Win|,Loss|,NoArrow][,Left|,Right]}
///
/// The arrow is chosen by the tag's keyword (ContentCardEvent.statArrow):
///   - Win               → big green upward arrow + green number
///   - Loss              → big red downward arrow + red number
///   - NoArrow / omitted → no arrow
/// It is never inferred from the text — a "-" in the context ("year-over-year",
/// "2019-2024") used to read as a loss and put a down arrow on neutral stats.
///
/// Typography (see <see cref="CardTypography"/>): the value in the heavy
/// weight, sized to ~70 % of the inner width with a 160–260 px cap height
/// (widening to 85 % to reach the floor; a value too long for that logs a
/// warning); the label (bold) and context beneath it; the three lines are one
/// group centred in the card. The arrow is as tall as the value's capitals and
/// sits beside it, the pair centred.
/// </summary>
public class StatCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Stat;

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

    private Vector2 laidOutSize = new Vector2(-1f, -1f);
    private Vector3 arrowTargetScale = Vector3.one;

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        GameObject arrowGO = new GameObject("DirectionArrow", typeof(RectTransform));
        arrowGO.transform.SetParent(rectTransform, false);
        arrowRect = arrowGO.GetComponent<RectTransform>();
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0f, 1f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f); // centre pivot: the Y-flip for "up" stays in place

        arrow = arrowGO.AddComponent<Image>();
        arrow.sprite = ContentCardUIBuilder.GetArrowSprite();
        arrow.preserveAspect = true;
        arrow.raycastTarget = false;
        arrowGO.SetActive(false); // shown for Win / Loss only

        numberText = ContentCardUIBuilder.CreateText(
            rectTransform, "NumberText",
            ContentCardUIBuilder.AccentColor,
            CardTypography.StatCapMinPx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(numberText, CardTypography.Weight.Heavy, TextAlignmentOptions.Top);
        numberText.textWrappingMode = TextWrappingModes.NoWrap;

        labelText = ContentCardUIBuilder.CreateText(
            rectTransform, "LabelText",
            ContentCardUIBuilder.TextPrimary,
            CardTypography.StatLabelPx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(labelText, CardTypography.Weight.Bold, TextAlignmentOptions.Top);

        contextText = ContentCardUIBuilder.CreateText(
            rectTransform, "ContextText",
            ContentCardUIBuilder.TextSecondary,
            CardTypography.StatContextPx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(contextText, CardTypography.Weight.Regular, TextAlignmentOptions.Top);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        rawNumber = data.primaryText ?? string.Empty;
        ParseNumericValue(rawNumber);

        StatArrow dir = data.statArrow;
        Color directionColor = GetDirectionColor(dir);

        bool showArrow = showDirectionArrow && dir != StatArrow.None;
        arrow.gameObject.SetActive(showArrow);
        if (showArrow)
        {
            arrow.color = directionColor;
            // Sprite is drawn as down-pointing; flip Y for up-pointing
            arrowTargetScale = (dir == StatArrow.Win) ? new Vector3(1f, -1f, 1f) : Vector3.one;
            arrowRect.localScale = arrowTargetScale;
        }

        numberText.color = directionColor;
        labelText.text = data.secondaryText ?? string.Empty;
        contextText.text = data.tertiaryText ?? string.Empty;
        contextText.color = directionColor;

        LayoutCard(log: true);

        // Lay out with the FINAL value, then show the count-up's starting text.
        numberText.text = (!useCountUp || !isNumeric) ? rawNumber : prefix + "0" + suffix;
    }

    void LateUpdate()
    {
        // Only when the card's size changed after Initialize — the slide moves
        // the card, it never resizes it.
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
        {
            string shown = numberText.text;
            LayoutCard(log: false);
            numberText.text = shown;
        }
    }

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad;
        float innerH = size.y - 2f * pad;
        bool hasArrow = arrow.gameObject.activeSelf;

        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Stat", rawNumber) };

        // ---- Value: measure the FINAL text (the count-up runs at this layout) ----
        numberText.text = BuildFinalNumberText();
        float capRatio = CardTypography.CapHeight(numberText.font, 100f) / 100f;
        float widthPer100 = CardTypography.MeasureLineWidth(numberText, 100f);
        float valueSize = CardTypography.StatValueSize(widthPer100, capRatio, innerW, hasArrow, k);

        // ---- Label + context: fixed sizes, wrapped to the inner width ----
        float labelSize = CardTypography.StatLabelPx * k;
        float contextSize = CardTypography.StatContextPx * k;
        float labelH = labelText.text.Length > 0 ? CardTypography.MeasureHeight(labelText, labelSize, innerW) : 0f;
        float contextH = contextText.text.Length > 0 ? CardTypography.MeasureHeight(contextText, contextSize, innerW) : 0f;
        float gap1 = labelH > 0f ? CardTypography.StatValueToLabelGapPx * k : 0f;
        float gap2 = (contextH > 0f && labelH > 0f) ? CardTypography.StatLabelToContextGapPx * k
                   : (contextH > 0f ? CardTypography.StatValueToLabelGapPx * k : 0f);
        float rowsH = gap1 + labelH + gap2 + contextH;

        // The value's visible band (its ink) is what the group stacks — the
        // TMP line box has a lot of air above the capitals at these sizes.
        // Shrink the value if the group would not fit the inner height.
        Rect valueInk = MeasureValueInk(valueSize, pad, innerW);
        float maxInk = innerH - rowsH;
        if (valueInk.height > maxInk && valueInk.height > 0f)
        {
            valueSize = Mathf.Max(CardTypography.HardFloorPx * k, valueSize * maxInk / valueInk.height);
            valueInk = MeasureValueInk(valueSize, pad, innerW);
        }

        float valueCap = CardTypography.CapHeight(numberText.font, valueSize);
        // The value is reported by its CAP HEIGHT — that is what its floor is.
        CardTypography.Element valueEl = report.Add("value cap", numberText, valueCap / k, CardTypography.StatCapMinPx);
        if (valueEl.BelowFloor)
            report.Warn($"{report.Card}: value cap height {valueCap / k:F0} px — below the {CardTypography.StatCapMinPx:F0} px floor " +
                        "(too wide for the card at that size).");

        // ---- Stack the group, centred vertically ----
        float groupH = valueInk.height + rowsH;
        float top = pad + Mathf.Max(0f, (innerH - groupH) * 0.5f);
        report.GroupHeight = groupH;
        report.InnerHeight = innerH;

        // Move the value so its ink top lands at the group top.
        RectTransform nrt = numberText.rectTransform;
        float valueRectTop = -nrt.anchoredPosition.y; // PlaceBox anchors at the card top
        float valueShift = top - valueInk.yMin;
        nrt.anchoredPosition = new Vector2(nrt.anchoredPosition.x, -(valueRectTop + valueShift));
        float y = top + valueInk.height;

        if (labelH > 0f)
        {
            y += gap1;
            labelText.fontSize = labelSize;
            CardTypography.PlaceRow(labelText, pad, y, labelH);
            report.Add("label", labelText, CardTypography.StatLabelPx, CardTypography.StatLabelFloorPx);
            y += labelH;
        }
        if (contextH > 0f)
        {
            y += gap2;
            contextText.fontSize = contextSize;
            CardTypography.PlaceRow(contextText, pad, y, contextH);
            report.Add("context", contextText, CardTypography.StatContextPx, CardTypography.StatContextFloorPx);
        }

        // ---- Arrow: as tall as the capitals, centred on them, left of the value ----
        if (hasArrow)
        {
            float baseline = CardTypography.BaselineInCard(numberText, rectTransform);
            float side = valueCap * CardTypography.StatArrowToCap;
            float arrowGap = valueCap * CardTypography.StatArrowGapToCap;
            float valueW = CardTypography.MeasureLineWidth(numberText, valueSize);
            float rowW = side + arrowGap + valueW;
            float startX = (size.x - rowW) * 0.5f;

            arrowRect.sizeDelta = new Vector2(side, side);
            arrowRect.anchoredPosition = new Vector2(startX + side * 0.5f, -(baseline - valueCap * 0.5f));

            numberText.alignment = TextAlignmentOptions.TopLeft;
            float valueTop = -nrt.anchoredPosition.y;
            CardTypography.PlaceBox(numberText, startX + side + arrowGap, valueTop, valueW + 8f * k, nrt.sizeDelta.y);
        }

        Layout = report;
        if (log) report.LogSummary();
    }

    // Places the value (full inner width, centred) at the card's inner top
    // at the given size and returns its ink in card space.
    private Rect MeasureValueInk(float valueSize, float pad, float innerW)
    {
        numberText.alignment = TextAlignmentOptions.Top;
        numberText.fontSize = valueSize;
        float lineH = numberText.GetPreferredValues(100000f, 100000f).y;
        CardTypography.PlaceBox(numberText, pad, pad, innerW, lineH);
        return CardTypography.InkInCard(numberText, rectTransform);
    }

    public override void Show()
    {
        base.Show();

        // Scale arrow 90% → 100% for subtle pop (preserve Y-flip for up arrow)
        if (arrow.gameObject.activeSelf)
        {
            arrowRect.localScale = arrowTargetScale * 0.9f;
            // OutBack is a mini-overshoot; drop it with the rest when the menu
            // picks the smooth 'Ease in + fade' entry style.
            arrowRect.DOScale(arrowTargetScale, EntryFadeDuration)
                .SetEase(UseOvershootEntry ? Ease.OutBack : EntryFadeEase);
        }

        if (useCountUp && isNumeric)
        {
            // One quiet click per step of the count (at most 12, the last on
            // the final value) — the sound-effect layer; silent when it's off.
            var clicker = new MugsTech.Sound.CountUpClicker(countUpDuration);
            DOTween.To(
                () => 0f,
                value =>
                {
                    numberText.text = prefix + FormatNumber(value) + suffix;
                    if (clicker.Step(numberText.text, Time.time))
                        MugsTech.Sound.SoundLayer.Fire(MugsTech.Sound.SfxKind.CountClick);
                },
                numericValue,
                countUpDuration
            ).SetEase(Ease.OutQuad).SetTarget(numberText) // so KillAllTweens finds it
             .OnComplete(() =>
             {
                 if (clicker.Finish(numberText.text, Time.time))
                     MugsTech.Sound.SoundLayer.Fire(MugsTech.Sound.SfxKind.CountClick);
             });
        }
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

    private Color GetDirectionColor(StatArrow dir)
    {
        switch (dir)
        {
            case StatArrow.Win:  return ContentCardUIBuilder.PositiveGreen;
            case StatArrow.Loss: return ContentCardUIBuilder.NegativeRed;
            default:             return ContentCardUIBuilder.NegativeRed;
        }
    }
}

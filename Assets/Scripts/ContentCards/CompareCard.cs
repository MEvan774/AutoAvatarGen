using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MugsTech.Style;

/// <summary>
/// Two numbers with proportional bars — "about four times the Bybit hack" said
/// at a glance.
/// Tag: {Compare:"value A","label A","value B","label B","source",duration|Start[,Left|,Right]}
///      {Compare:End}
///
/// Two rows, A above B: each a bold label over a bar with its value (heavy) at
/// the bar's end — inside the bar when it's long enough, just past its end
/// otherwise; the source beneath. Bar A (the story's subject) is in the card
/// accent, B in neutral slate. Honest scale (<see cref="CompareMath"/>): linear
/// from zero, the larger value fills the track, the smaller sits at its true
/// ratio (a 3 % sliver at least). The bars grow from zero once the card has
/// landed (0.6 s, B 0.25 s after A).
///
/// Values that can't share a scale (not numbers, or different kinds) get no
/// bars: both values big, side by side, with a small "vs" between them.
/// Every word shown is one of the five fields as written (plus that "vs").
/// </summary>
public class CompareCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Compare;

    // ---- Sizes (px of the 1000-px-wide reference card) -------------------------
    public const float LabelPx = 44f, LabelFloorPx = 40f;
    public const float ValueMaxPx = 120f, ValueMinPx = 90f;
    public const float FallbackValueMaxPx = 140f, FallbackValueMinPx = 56f;
    public const float SourcePx = 34f, SourceFloorPx = 32f;
    public const float VsPx = 40f;
    /// <summary>Bar thickness, as a multiple of the value's cap height.</summary>
    public const float BarToCap = 1.6f;
    public const float LabelToBarGapPx = 10f, RowGapPx = 36f, SourceGapPx = 40f;
    public const float BarRadiusPx = 10f;
    public static readonly Color Slate = new Color(0x64 / 255f, 0x74 / 255f, 0x8B / 255f, 1f); // #64748B

    private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] values = new TextMeshProUGUI[2];
    private readonly Image[] bars = new Image[2];
    private TextMeshProUGUI source;
    private TextMeshProUGUI vs;

    private string[] fields;
    private CompareMath.Bars ratios;
    private CompareMath.Placement placement;
    private float barHeight;
    private readonly float[] barLeft = new float[2];
    private readonly float[] barTop = new float[2];
    // Value x relative to the bar's CURRENT end (inside: −padIn−width; outside: +gap),
    // so a value travels with its bar while it grows instead of hanging off it.
    private readonly float[] valueFromBarEnd = new float[2];

    private float clock;
    private float growStart = float.NaN;
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }
    public bool IsFallback => !ratios.Ok;
    public float TrackWidth => placement.Track;
    public float BarFraction(int i) => i == 0 ? ratios.A : ratios.B;
    public bool ValueInside(int i) => i == 0 ? placement.InsideA : placement.InsideB;
    public Image Bar(int i) => bars[i];
    public TextMeshProUGUI ValueText(int i) => values[i];
    public TextMeshProUGUI VsText => vs;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        for (int i = 0; i < 2; i++)
        {
            labels[i] = ContentCardUIBuilder.CreateText(rectTransform, i == 0 ? "LabelA" : "LabelB",
                ContentCardUIBuilder.TextPrimary, LabelPx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(labels[i], CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);

            bars[i] = ContentCardUIBuilder.CreateImage(rectTransform, i == 0 ? "BarA" : "BarB",
                i == 0 ? ContentCardUIBuilder.AccentColor : Slate);
            bars[i].sprite = StyleSpriteFactory.GetRoundedRect(Mathf.RoundToInt(BarRadiusPx));
            bars[i].type = Image.Type.Sliced;
            RectTransform brt = bars[i].rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
        }
        // Values after the bars so they draw on top of them.
        for (int i = 0; i < 2; i++)
        {
            values[i] = ContentCardUIBuilder.CreateText(rectTransform, i == 0 ? "ValueA" : "ValueB",
                ContentCardUIBuilder.TextPrimary, ValueMaxPx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(values[i], CardTypography.Weight.Heavy, TextAlignmentOptions.TopLeft);
            values[i].textWrappingMode = TextWrappingModes.NoWrap;
        }

        vs = ContentCardUIBuilder.CreateText(rectTransform, "Vs", ContentCardUIBuilder.TextSecondary, VsPx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(vs, CardTypography.Weight.Bold, TextAlignmentOptions.Top);
        vs.textWrappingMode = TextWrappingModes.NoWrap;
        vs.text = "vs"; // the one fixed word this card may show
        vs.gameObject.SetActive(false);

        source = ContentCardUIBuilder.CreateText(rectTransform, "Source", ContentCardUIBuilder.TextTertiary, SourcePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(source, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        fields = data.fields != null && data.fields.Length == 5
            ? data.fields
            : new[] { data.primaryText ?? "", "", data.secondaryText ?? "", "", data.tertiaryText ?? "" };
        ratios = CompareMath.Ratios(fields[0], fields[2]);

        values[0].text = fields[0];
        labels[0].text = fields[1];
        values[1].text = fields[2];
        labels[1].text = fields[3];
        source.text = fields[4];

        LayoutCard(log: true);
    }

    void LateUpdate()
    {
        if (CardTypography.CardSize(rectTransform) != laidOutSize) LayoutCard(log: false);
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        Layout = ratios.Ok ? LayoutBars(size) : LayoutFallback(size);
        ApplyGrowth();
        if (log) Layout.LogSummary();
    }

    private CardTypography.Report LayoutBars(Vector2 size)
    {
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad, innerH = size.y - 2f * pad;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Compare", fields[0] + " vs " + fields[2]) };

        vs.gameObject.SetActive(false);
        for (int i = 0; i < 2; i++) bars[i].gameObject.SetActive(true);

        // One value size for both rows: as large as fits half the row, floor 90.
        float valueSize = ValueMaxPx * k;
        for (float s = ValueMaxPx * k; s >= ValueMinPx * k; s -= 2f * k)
        {
            valueSize = s;
            if (Mathf.Max(CardTypography.MeasureLineWidth(values[0], s), CardTypography.MeasureLineWidth(values[1], s)) <= innerW * 0.5f) break;
        }
        float cap = CardTypography.CapHeight(values[0].font, valueSize);
        barHeight = cap * BarToCap;
        float padIn = cap * 0.3f, gap = cap * 0.3f;
        float wA = CardTypography.MeasureLineWidth(values[0], valueSize);
        float wB = CardTypography.MeasureLineWidth(values[1], valueSize);
        placement = CompareMath.Place(innerW, ratios.A, ratios.B, wA, wB, padIn, gap);
        if (placement.Overflows)
            report.Warn($"{report.Card}: a value is too wide for the card even at {ValueMinPx:F0} px — it runs past the padding.");

        float labelSize = LabelPx * k, sourceSize = SourcePx * k;
        float hLabA = labels[0].text.Length > 0 ? CardTypography.MeasureHeight(labels[0], labelSize, innerW) : 0f;
        float hLabB = labels[1].text.Length > 0 ? CardTypography.MeasureHeight(labels[1], labelSize, innerW) : 0f;
        float hSrc = source.text.Length > 0 ? CardTypography.MeasureHeight(source, sourceSize, innerW) : 0f;
        float g1 = LabelToBarGapPx * k, gRow = RowGapPx * k, gSrc = hSrc > 0f ? SourceGapPx * k : 0f;
        float group = hLabA + g1 + barHeight + gRow + hLabB + g1 + barHeight + gSrc + hSrc;
        float top = pad + Mathf.Max(0f, (innerH - group) * 0.5f);
        report.GroupHeight = group;
        report.InnerHeight = innerH;
        if (group > innerH + 0.5f)
            report.Warn($"{report.Card}: the labels are too long to fit the card's height — it runs past the padding.");

        float y = top;
        float[] labelH = { hLabA, hLabB };
        for (int i = 0; i < 2; i++)
        {
            labels[i].fontSize = labelSize;
            CardTypography.PlaceRow(labels[i], pad, y, labelH[i]);
            report.Add(i == 0 ? "label A" : "label B", labels[i], LabelPx, LabelFloorPx);
            y += labelH[i] + g1;

            barLeft[i] = pad;
            barTop[i] = y;
            float frac = i == 0 ? ratios.A : ratios.B;
            bool inside = i == 0 ? placement.InsideA : placement.InsideB;
            float barW = frac * placement.Track;
            float valueW = i == 0 ? wA : wB;

            values[i].fontSize = valueSize;
            values[i].color = inside ? Color.white : ContentCardUIBuilder.TextPrimary;
            values[i].alignment = TextAlignmentOptions.TopLeft;
            float lineH = values[i].GetPreferredValues(100000f, 100000f).y;
            valueFromBarEnd[i] = inside ? -padIn - valueW : gap;
            float x = pad + barW + valueFromBarEnd[i];
            // Centre the capitals on the bar: TMP's line box has air above them.
            float capTop = CapTopOffset(values[i], valueSize);
            CardTypography.PlaceBox(values[i], x, y + (barHeight - cap) * 0.5f - capTop, valueW + 4f * k, lineH);
            report.Add(i == 0 ? "value A" : "value B", values[i], valueSize / k, ValueMinPx);

            y += barHeight + (i == 0 ? gRow : 0f);
        }
        if (hSrc > 0f)
        {
            y += gSrc;
            source.fontSize = sourceSize;
            CardTypography.PlaceRow(source, pad, y, hSrc);
            report.Add("source", source, SourcePx, SourceFloorPx);
        }
        return report;
    }

    private CardTypography.Report LayoutFallback(Vector2 size)
    {
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad, innerH = size.y - 2f * pad;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Compare", fields[0] + " vs " + fields[2]) };

        for (int i = 0; i < 2; i++) bars[i].gameObject.SetActive(false);
        vs.gameObject.SetActive(true);
        vs.fontSize = VsPx * k;
        float vsW = CardTypography.MeasureLineWidth(vs, vs.fontSize);
        float colGap = 28f * k;
        float colW = (innerW - vsW - 2f * colGap) * 0.5f;

        // One size for both values, as large as fits a column.
        float valueSize = FallbackValueMinPx * k;
        for (float s = FallbackValueMaxPx * k; s >= FallbackValueMinPx * k; s -= 2f * k)
        {
            valueSize = s;
            if (Mathf.Max(CardTypography.MeasureLineWidth(values[0], s), CardTypography.MeasureLineWidth(values[1], s)) <= colW) break;
        }
        float cap = CardTypography.CapHeight(values[0].font, valueSize);
        float labelSize = LabelPx * k, sourceSize = SourcePx * k;
        float hLab = 0f;
        for (int i = 0; i < 2; i++)
            if (labels[i].text.Length > 0) hLab = Mathf.Max(hLab, CardTypography.MeasureHeight(labels[i], labelSize, colW));
        float hSrc = source.text.Length > 0 ? CardTypography.MeasureHeight(source, sourceSize, innerW) : 0f;
        float g1 = 18f * k, gSrc = hSrc > 0f ? SourceGapPx * k : 0f;
        float group = cap + g1 + hLab + gSrc + hSrc;
        float top = pad + Mathf.Max(0f, (innerH - group) * 0.5f);
        report.GroupHeight = group;
        report.InnerHeight = innerH;

        for (int i = 0; i < 2; i++)
        {
            float colLeft = pad + (i == 0 ? 0f : colW + vsW + 2f * colGap);
            values[i].fontSize = valueSize;
            values[i].color = ContentCardUIBuilder.TextPrimary;
            values[i].alignment = TextAlignmentOptions.Top;
            float lineH = values[i].GetPreferredValues(100000f, 100000f).y;
            CardTypography.PlaceBox(values[i], colLeft, top - CapTopOffset(values[i], valueSize), colW, lineH);
            report.Add(i == 0 ? "value A" : "value B", values[i], valueSize / k, ValueMinPx);

            labels[i].fontSize = labelSize;
            labels[i].alignment = TextAlignmentOptions.Top;
            float h = labels[i].text.Length > 0 ? CardTypography.MeasureHeight(labels[i], labelSize, colW) : 0f;
            CardTypography.PlaceBox(labels[i], colLeft, top + cap + g1, colW, h);
            report.Add(i == 0 ? "label A" : "label B", labels[i], LabelPx, LabelFloorPx);
        }
        if (valueSize / k < ValueMinPx - CardTypography.FloorGracePx)
            report.Warn($"{report.Card}: value A and value B only fit side by side at {valueSize / k:F0} px — under the {ValueMinPx:F0} px floor.");

        // "vs" centred between the columns, on the values' capitals.
        float vsCap = CardTypography.CapHeight(vs.font, vs.fontSize);
        float vsLineH = vs.GetPreferredValues(100000f, 100000f).y;
        CardTypography.PlaceBox(vs, pad + colW + colGap, top + (cap - vsCap) * 0.5f - CapTopOffset(vs, vs.fontSize), vsW, vsLineH);

        if (hSrc > 0f)
        {
            source.fontSize = sourceSize;
            CardTypography.PlaceRow(source, pad, top + cap + g1 + hLab + gSrc, hSrc);
            report.Add("source", source, SourcePx, SourceFloorPx);
        }
        return report;
    }

    // Distance from the top of TMP's line box down to the capitals' top.
    private static float CapTopOffset(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        float scale = size / Mathf.Max(1f, fi.pointSize) * Mathf.Max(0.0001f, fi.scale);
        return (fi.ascentLine - fi.capLine) * scale;
    }

    // =====================================================================
    // Growth (driven per frame on the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        base.Show();
        // The bars start growing once the card has landed.
        growStart = clock + SlideDuration;
        ApplyGrowth();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame; public so the self-tests can step the card in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        ApplyGrowth();
    }

    /// <summary>Starts the growth now (self-tests: no slide in edit mode).</summary>
    public void StartGrowthForTest() { growStart = clock; ApplyGrowth(); }

    private void ApplyGrowth()
    {
        if (!ratios.Ok) return;
        for (int i = 0; i < 2; i++)
        {
            float age = float.IsNaN(growStart) ? 0f : clock - growStart - (i == 0 ? 0f : CompareMath.BDelaySeconds);
            float g = float.IsNaN(growStart) ? 0f : CompareMath.GrowAt(age);
            float frac = i == 0 ? ratios.A : ratios.B;
            RectTransform brt = bars[i].rectTransform;
            brt.anchoredPosition = new Vector2(barLeft[i], -barTop[i]);
            float barW = frac * placement.Track * g;
            brt.sizeDelta = new Vector2(barW, barHeight);
            bars[i].enabled = g > 0f;
            RectTransform vrt = values[i].rectTransform;
            vrt.anchoredPosition = new Vector2(barLeft[i] + barW + valueFromBarEnd[i], vrt.anchoredPosition.y);
            Color c = values[i].color;
            c.a = CompareMath.ValueAlphaAt(g);
            values[i].color = c;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// A chart, full screen: "Services revenue: $31.6B, up from $28.75B" drawn to
/// scale instead of said.
/// Tag: {Graph:"title","label=value;label=value;…","source",duration|Start[,Bars|,Line|,Share][,Cropped]}
///      {Graph:End}
///
/// The BigText dim comes up and the chart is drawn straight onto the dimmed
/// scene in white — no panel. The title (bold, 64 → 44 px, two lines at most)
/// sits above the chart, the source (36 px) below it, everything inside 6 %
/// title-safe margins and centred as one group.
///   Bars  — 2–6 horizontal bars, linear from zero, the largest filling the
///           track; the FIRST point (the story's subject) in the card accent,
///           the rest in slate. Each row: label, bar, value at the bar's end
///           (inside when it fits, just past it otherwise). They grow one
///           after another once the card has landed.
///   Line  — 3–12 evenly spaced points joined by one accent line that draws
///           left to right; every label under the axis (staggered on two rows
///           before going under 32 px), the first and last values beside the
///           line's ends. The axis starts at zero unless the tag says
///           ",Cropped" — and then the card prints AXIS DOES NOT START AT ZERO.
///   Share — 2–5 parts of a whole on a ring filling clockwise from twelve
///           o'clock, a legend beside it; what is left to 100 % is a dark
///           unlabelled segment.
/// Data that can't be drawn honestly (<see cref="GraphMath.Parse"/>: a value
/// that isn't a number, mixed kinds, too few / too many points, shares over
/// 100 %) gets no chart: the points are listed as rows, label left, value
/// right. The tag parser logs the reason; this card only warns about layout.
///
/// Every word shown is one of the tag's fields as written, plus that one
/// fixed line. Sizes, scales, meshes and timing: <see cref="GraphMath"/>.
/// </summary>
public class GraphCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Graph;

    /// <summary>The one fixed line this card may print (a cropped Line chart only).</summary>
    public const string AxisNoteText = "AXIS DOES NOT START AT ZERO";

    public static Color Slate => CompareCard.Slate;
    static readonly Color TextColor = Color.white;
    static readonly Color AxisLabelColor = new Color(1f, 1f, 1f, 0.90f);
    static readonly Color SourceColor = new Color(1f, 1f, 1f, 0.72f);
    /// <summary>The line chart's baseline: a light neutral from Slate.</summary>
    public static Color AxisColor
    {
        get { Color c = Color.Lerp(Slate, Color.white, 0.5f); c.a = 0.9f; return c; }
    }

    private Image dim;
    private RectTransform content, chart;
    private CanvasGroup contentGroup;
    private TextMeshProUGUI title, source, axisNote;

    // One entry per point, in script order. values[i] is null where the chart
    // shows no value (Line: every point but the first and the last).
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> values = new List<TextMeshProUGUI>();
    private readonly List<Image> bars = new List<Image>();       // Bars
    private readonly List<Image> dots = new List<Image>();       // Line
    private readonly List<Image> swatches = new List<Image>();   // Share
    private Image axis;                                           // Line
    private GraphLineGraphic line;                                // Line
    private GraphRingGraphic ring;                                // Share

    private string[] fields = { "", "", "" };
    private GraphMath.Data graph;

    // ---- Bars ----
    private float[] fractions = new float[0];
    private float[] barTops = new float[0];
    // Value x relative to its bar's CURRENT end (inside: −padIn−width; outside: +gap),
    // so a value travels with its bar while it grows (Compare's rule).
    private float[] valueFromBarEnd = new float[0];
    private GraphMath.BarPlacement placement;
    private float barLeft, barHeight;

    // ---- Line ----
    private float[] reach = new float[0];        // each point's share of the line's length
    private Vector2[] pointAt = new Vector2[0];  // each point in the chart's space (y down)
    private int labelRows = 1;

    // ---- Share ----
    private List<GraphMath.RingSegment> segments = new List<GraphMath.RingSegment>();

    private BigTextTypography.DimEnvelope dimEnvelope;
    private float clock;                 // scaled time, like DOTween
    private float shownAt = float.NaN;   // NaN until Show
    private bool entranceRunning;
    private bool closing;                // Hide owns the content's alpha from here
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    // ---- The brief's API -----------------------------------------------------------
    /// <summary>Drawn as text rows (no chart).</summary>
    public bool IsFallback => graph == null || !graph.Ok;
    public GraphMath.Data Data => graph;
    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }
    public float DimAlpha => dim != null ? dim.color.a : 0f;

    // ---- Read by the self-tests ------------------------------------------------------
    public BigTextTypography.DimEnvelope Dim => dimEnvelope;
    public float ContentAlpha => contentGroup != null ? contentGroup.alpha : 0f;
    /// <summary>True once the chart has finished animating in.</summary>
    public bool EntranceDone => !float.IsNaN(shownAt) && !entranceRunning;
    public TextMeshProUGUI Title => title;
    public TextMeshProUGUI Source => source;
    /// <summary>The "AXIS DOES NOT START AT ZERO" line; null unless the chart is a cropped Line.</summary>
    public TextMeshProUGUI AxisNote => axisNote;
    public int PointCount => labels.Count;
    public TextMeshProUGUI PointLabel(int i) => labels[i];
    /// <summary>Point i's value text; null where the chart shows none (Line: all but the first and last).</summary>
    public TextMeshProUGUI PointValue(int i) => values[i];
    public RectTransform Chart => chart;
    /// <summary>The chart's rect in frame space (x from the left edge, y DOWN from the top).</summary>
    public Rect ChartRect { get; private set; }
    /// <summary>The 6 % title-safe rect in frame space (y down).</summary>
    public Rect SafeRect { get; private set; }
    public float TitleSize { get; private set; }
    public float LabelSize { get; private set; }
    public float ValueSize { get; private set; }

    public Image Bar(int i) => bars[i];
    public float BarFraction(int i) => fractions[i];
    public bool ValueInside(int i) => placement.Inside[i];
    public float TrackWidth => placement.Track;

    public GraphLineGraphic Line => line;
    public Image Axis => axis;
    public Image Dot(int i) => dots[i];
    /// <summary>1, or 2 when the Line chart's labels are staggered.</summary>
    public int LabelRows => labelRows;
    /// <summary>Point i of the Line chart in the chart's space (x from its left edge, y DOWN from its top).</summary>
    public Vector2 PointInChart(int i) => pointAt[i];

    public GraphRingGraphic Ring => ring;
    public Image Swatch(int i) => swatches[i];

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        dimEnvelope = new BigTextTypography.DimEnvelope(
            BigTextTypography.DimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear));

        // Full-screen dim — first child, so it renders behind everything.
        dim = ContentCardUIBuilder.CreateImage(rectTransform, "Dim", new Color(0f, 0f, 0f, 0f));
        ContentCardUIBuilder.SetStretch(dim.rectTransform, 0f, 0f, 0f, 0f);

        content = ContentCardUIBuilder.CreateChild(rectTransform, "Content");
        contentGroup = EnsureGroup(content);
        contentGroup.alpha = 0f;

        title = ContentCardUIBuilder.CreateText(content, "Title", TextColor, GraphMath.TitlePx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(title, CardTypography.Weight.Bold, TextAlignmentOptions.Top);

        // The chart's own space: every mark and label of it is placed from this
        // rect's top-left corner, so re-centring the card moves one transform.
        GameObject cgo = new GameObject("Chart", typeof(RectTransform));
        cgo.transform.SetParent(content, false);
        chart = cgo.GetComponent<RectTransform>();
        chart.anchorMin = chart.anchorMax = new Vector2(0f, 1f);
        chart.pivot = new Vector2(0f, 1f);

        source = ContentCardUIBuilder.CreateText(content, "Source", SourceColor, GraphMath.SourcePx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(source, CardTypography.Weight.Regular, TextAlignmentOptions.Top);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        string[] f = data != null ? data.fields : null;
        fields = new[] { Field(f, 0), Field(f, 1), Field(f, 2) };
        graph = GraphMath.Parse(fields[1], data != null ? data.graphKind : GraphKind.Bars, data != null && data.graphCropped);

        title.text = fields[0];
        source.text = fields[2];
        BuildChart();

        LayoutCard(log: true);
        Pose(); // nothing grown, drawn or filled — until Show
    }

    static string Field(string[] f, int i) => f != null && i < f.Length && f[i] != null ? f[i] : string.Empty;

    private void BuildChart()
    {
        for (int i = chart.childCount - 1; i >= 0; i--)
        {
            GameObject old = chart.GetChild(i).gameObject;
            old.SetActive(false);
            if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
        }
        labels.Clear(); values.Clear(); bars.Clear(); dots.Clear(); swatches.Clear();
        axis = null; line = null; ring = null; axisNote = null;
        segments = new List<GraphMath.RingSegment>();

        int n = graph.Points.Count;
        fractions = new float[n];
        barTops = new float[n];
        valueFromBarEnd = new float[n];
        reach = new float[n];
        pointAt = new Vector2[n];
        placement = new GraphMath.BarPlacement { Inside = new bool[n] };
        labelRows = 1;

        if (!graph.Ok) BuildRows();
        else if (graph.Kind == GraphKind.Line) BuildLine();
        else if (graph.Kind == GraphKind.Share) BuildShare();
        else BuildBars();
    }

    private void BuildBars()
    {
        Color accent = ContentCardUIBuilder.AccentColor;
        for (int i = 0; i < graph.Points.Count; i++)
        {
            // The first point is the story's subject (Compare's value A).
            Image bar = ContentCardUIBuilder.CreateImage(chart, "Bar " + (i + 1), i == 0 ? accent : Slate);
            bar.sprite = StyleSpriteFactory.GetRoundedRect(Mathf.RoundToInt(GraphMath.BarRadiusPx));
            bar.type = Image.Type.Sliced;
            TopLeft(bar.rectTransform);
            bars.Add(bar);
        }
        for (int i = 0; i < graph.Points.Count; i++)
            labels.Add(NewText("Label " + (i + 1), graph.Points[i].Label, TextColor, CardTypography.Weight.Bold, TextAlignmentOptions.TopRight, wrap: true));
        // Values after the bars so they draw on top of them.
        for (int i = 0; i < graph.Points.Count; i++)
            values.Add(NewText("Value " + (i + 1), graph.Points[i].ValueText, TextColor, CardTypography.Weight.Heavy, TextAlignmentOptions.TopLeft, wrap: false));
    }

    private void BuildLine()
    {
        Color accent = ContentCardUIBuilder.AccentColor;
        int n = graph.Points.Count;

        axis = ContentCardUIBuilder.CreateImage(chart, "Axis", AxisColor);
        TopLeft(axis.rectTransform);

        GameObject lgo = new GameObject("Line", typeof(RectTransform));
        lgo.transform.SetParent(chart, false);
        TopLeft(lgo.GetComponent<RectTransform>());
        line = lgo.AddComponent<GraphLineGraphic>();
        line.color = accent;
        line.raycastTarget = false;
        line.Drawn = 0f;

        for (int i = 0; i < n; i++)
        {
            Image dot = ContentCardUIBuilder.CreateImage(chart, "Dot " + (i + 1), accent);
            dot.sprite = ProceduralSprites.Circle();
            RectTransform drt = dot.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            dots.Add(dot);
        }
        for (int i = 0; i < n; i++)
            labels.Add(NewText("Label " + (i + 1), graph.Points[i].Label, AxisLabelColor, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft, wrap: false));
        // Values on the first and the last point only.
        for (int i = 0; i < n; i++)
            values.Add(i == 0 || i == n - 1
                ? NewText("Value " + (i + 1), graph.Points[i].ValueText, TextColor, CardTypography.Weight.Heavy, TextAlignmentOptions.TopLeft, wrap: false)
                : null);

        if (graph.Cropped)
        {
            axisNote = NewText("AxisNote", AxisNoteText, accent, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft, wrap: false);
            axisNote.characterSpacing = GraphMath.LineNoteTrackingEm;
        }
    }

    private void BuildShare()
    {
        int n = graph.Points.Count;
        segments = GraphMath.RingSegments(graph.Points);

        GameObject rgo = new GameObject("Ring", typeof(RectTransform));
        rgo.transform.SetParent(chart, false);
        TopLeft(rgo.GetComponent<RectTransform>());
        ring = rgo.AddComponent<GraphRingGraphic>();
        ring.color = Color.white;
        ring.raycastTarget = false;
        ring.Fill = 0f;

        for (int i = 0; i < n; i++)
        {
            Image sw = ContentCardUIBuilder.CreateImage(chart, "Swatch " + (i + 1), Color.white);
            sw.sprite = StyleSpriteFactory.GetRoundedRect(Mathf.RoundToInt(GraphMath.ShareSwatchRadiusPx));
            sw.type = Image.Type.Sliced;
            TopLeft(sw.rectTransform);
            swatches.Add(sw);
        }
        for (int i = 0; i < n; i++)
            labels.Add(NewText("Label " + (i + 1), graph.Points[i].Label, TextColor, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft, wrap: true));
        for (int i = 0; i < n; i++)
            values.Add(NewText("Value " + (i + 1), graph.Points[i].ValueText, TextColor, CardTypography.Weight.Heavy, TextAlignmentOptions.TopRight, wrap: false));
    }

    // No chart: the points as rows, label left and value right. Both may wrap
    // (a value that isn't a number can be any text).
    private void BuildRows()
    {
        for (int i = 0; i < graph.Points.Count; i++)
            labels.Add(NewText("Label " + (i + 1), graph.Points[i].Label, TextColor, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft, wrap: true));
        for (int i = 0; i < graph.Points.Count; i++)
            values.Add(NewText("Value " + (i + 1), graph.Points[i].ValueText, TextColor, CardTypography.Weight.Heavy, TextAlignmentOptions.TopRight, wrap: true));
    }

    private TextMeshProUGUI NewText(string name, string text, Color color, CardTypography.Weight weight, TextAlignmentOptions alignment, bool wrap)
    {
        TextMeshProUGUI t = ContentCardUIBuilder.CreateText(chart, name, color, 40f, alignment);
        CardTypography.PrepareText(t, weight, alignment);
        t.richText = false; // a value may start with '<' or '>' — shown exactly as written
        if (!wrap) t.textWrappingMode = TextWrappingModes.NoWrap;
        t.text = text;
        return t;
    }

    private static void TopLeft(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
    }

    void LateUpdate()
    {
        if (graph != null && FrameSize() != laidOutSize)
        {
            LayoutCard(log: false);
            Pose();
        }
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private Vector2 FrameSize()
    {
        Rect r = rectTransform.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    private void LayoutCard(bool log)
    {
        Vector2 frame = FrameSize();
        laidOutSize = frame;
        Layout = Lay(frame);
        if (log) Layout.LogSummary();
    }

    private CardTypography.Report Lay(Vector2 frame)
    {
        float k = GraphMath.ScaleFor(frame.y);
        Rect safe = GraphMath.SafeRect(frame.x, frame.y);
        float width = GraphMath.ContentWidth(frame.x, frame.y);
        float left = (frame.x - width) * 0.5f;
        float floor = CardTypography.HardFloorPx * k;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Graph", fields[0]) };
        SafeRect = safe;

        // Title: as large as fits two lines, 64 → 44 px (lower only with a warning, never under 32).
        float titleSize = GraphMath.TitlePx * k, titleH = 0f;
        if (title.text.Length > 0)
        {
            CardTypography.BodyFit fit = CardTypography.FitBody(
                s => LinesAt(title, s, width) <= GraphMath.TitleMaxLines,
                GraphMath.TitleMinPx * k, GraphMath.TitlePx * k, GraphMath.TitleMinPx * k, floor);
            titleSize = fit.Size;
            if (fit.Overflows)
                report.Warn($"{report.Card}: the title doesn't fit {GraphMath.TitleMaxLines} lines even at {CardTypography.HardFloorPx:F0} px — " +
                            $"shown at {CardTypography.HardFloorPx:F0} px on more lines.");
            else if (fit.BelowWarnFloor)
                report.Warn($"{report.Card}: the title only fits {GraphMath.TitleMaxLines} lines at {titleSize / k:F0} px — " +
                            $"under the {GraphMath.TitleMinPx:F0} px floor.");
            titleH = CardTypography.MeasureHeight(title, titleSize, width);
        }
        TitleSize = titleSize;

        float sourceSize = GraphMath.SourcePx * k;
        float sourceH = source.text.Length > 0 ? CardTypography.MeasureHeight(source, sourceSize, width) : 0f;

        bool hasChart = graph.Points.Count > 0;
        float gapT = titleH > 0f && (hasChart || sourceH > 0f) ? GraphMath.TitleGapPx * k : 0f;
        float gapS = sourceH > 0f && hasChart ? GraphMath.SourceGapPx * k : 0f;
        float availH = Mathf.Max(safe.height - titleH - gapT - gapS - sourceH, GraphMath.MinChartHeightPx * k);

        float chartH = 0f;
        if (hasChart)
        {
            if (!graph.Ok) chartH = PlanRows(k, width, availH, report);
            else if (graph.Kind == GraphKind.Line) chartH = PlanLine(k, width, availH, report);
            else if (graph.Kind == GraphKind.Share) chartH = PlanShare(k, width, availH, report);
            else chartH = PlanBars(k, width, availH, report);
        }

        // Title, chart and source as one group, centred in the title-safe height.
        float group = titleH + gapT + chartH + gapS + sourceH;
        float top = safe.y + Mathf.Max(0f, (safe.height - group) * 0.5f);
        report.GroupHeight = group;
        report.InnerHeight = safe.height;
        if (group > safe.height + 0.5f)
            report.Warn($"{report.Card}: the title, chart and source are too tall for the frame — the card runs past title-safe.");

        if (titleH > 0f)
        {
            title.fontSize = titleSize;
            CardTypography.PlaceBox(title, left, top, width, titleH);
            report.Add("title", title, titleSize / k, GraphMath.TitleMinPx);
        }
        float chartTop = top + titleH + gapT;
        chart.anchoredPosition = new Vector2(left, -chartTop);
        chart.sizeDelta = new Vector2(width, chartH);
        ChartRect = new Rect(left, chartTop, width, chartH);
        if (sourceH > 0f)
        {
            source.fontSize = sourceSize;
            CardTypography.PlaceBox(source, left, chartTop + chartH + gapS, width, sourceH);
            report.Add("source", source, GraphMath.SourcePx, GraphMath.SourceFloorPx);
        }
        return report;
    }

    // ---- Bars -------------------------------------------------------------------------

    private float PlanBars(float k, float width, float availH, CardTypography.Report report)
    {
        int n = graph.Points.Count;
        float floor = CardTypography.HardFloorPx * k;
        fractions = GraphMath.BarFractions(graph.Points, out _);
        barHeight = GraphMath.BarHeight(n, availH, k);
        float pitch = barHeight * (1f + GraphMath.BarGapToHeight);

        // Labels: one size, in a column on the left — at most BarLabelColumnFraction
        // of the width, wrapping while the block still clears the rows above and below.
        float colMax = width * GraphMath.BarLabelColumnFraction;
        float labelRoom = pitch - 4f * k;
        bool LabelsFit(float s)
        {
            for (int i = 0; i < n; i++)
            {
                if (CardTypography.WidestWordWidth(labels[i].font, labels[i].text, s) > colMax) return false;
                if (CardTypography.MeasureHeight(labels[i], s, colMax) > labelRoom) return false;
            }
            return true;
        }
        float labelMax = Mathf.Max(floor, Mathf.Min(GraphMath.BarLabelPx * k, barHeight * 0.55f));
        CardTypography.BodyFit lf = FitDown(LabelsFit, floor, labelMax);
        float labelSize = lf.Size;
        LabelSize = labelSize;
        if (lf.Overflows)
            report.Warn($"{report.Card}: a label is too long for its column even at {CardTypography.HardFloorPx:F0} px — the labels run into each other.");

        float colW = Mathf.Min(colMax, WidestLine(labels, labelSize) + 2f * k);
        barLeft = colW + GraphMath.BarLabelGapPx * k;
        float rowW = width - barLeft;

        // Values: one size, from the bar's height (Compare's 1.6 × cap), shrinking
        // while the widest is over 45 % of the row.
        TMP_FontAsset valueFont = values[0].font;
        float capRatio = Mathf.Max(0.1f, CardTypography.CapHeight(valueFont, 100f) / 100f);
        float vMax = Mathf.Min(GraphMath.BarValuePx * k, barHeight / (GraphMath.BarToCap * capRatio));
        float vMin = Mathf.Min(vMax, GraphMath.BarValueMinPx * k);
        float valueSize = vMax;
        for (float s = vMax; s >= vMin - 0.01f; s -= 2f * k)
        {
            valueSize = s;
            if (WidestLine(values, s) <= rowW * GraphMath.BarValueMaxRowFraction) break;
        }
        ValueSize = valueSize;

        float cap = CardTypography.CapHeight(valueFont, valueSize);
        float padIn = cap * GraphMath.BarValuePadToCap, gap = cap * GraphMath.BarValueGapToCap;
        var widths = new float[n];
        for (int i = 0; i < n; i++) widths[i] = CardTypography.MeasureLineWidth(values[i], valueSize);
        placement = GraphMath.PlaceBars(rowW, fractions, widths, padIn, gap);
        if (placement.Overflows)
            report.Warn($"{report.Card}: a value is too wide for its row even at {valueSize / k:F0} px — it runs past the chart.");

        for (int i = 0; i < n; i++)
        {
            float top = GraphMath.BarTop(i, barHeight);
            barTops[i] = top;

            // The label's lines centred on the bar, right-aligned against it.
            labels[i].fontSize = labelSize;
            float labelTop = CentredTop(labels[i], labelSize, colW, top, barHeight);
            float labelH = CardTypography.MeasureHeight(labels[i], labelSize, colW);
            CardTypography.PlaceBox(labels[i], 0f, labelTop, colW, labelH);
            report.Add("label " + (i + 1), labels[i], labelSize / k, CardTypography.HardFloorPx);

            // The value at the bar's end, its capitals centred on the bar.
            bool inside = placement.Inside[i];
            values[i].fontSize = valueSize;
            float lineH = values[i].GetPreferredValues(100000f, 100000f).y;
            valueFromBarEnd[i] = inside ? -padIn - widths[i] : gap;
            CardTypography.PlaceBox(values[i], barLeft + fractions[i] * placement.Track + valueFromBarEnd[i],
                top + (barHeight - cap) * 0.5f - CapTop(values[i], valueSize), widths[i] + 4f * k, lineH);
            report.Add("value " + (i + 1), values[i], valueSize / k, CardTypography.HardFloorPx);
        }
        return GraphMath.BarsHeight(n, barHeight);
    }

    // ---- Line -------------------------------------------------------------------------

    private float PlanLine(float k, float width, float availH, CardTypography.Report report)
    {
        int n = graph.Points.Count;
        float floor = CardTypography.HardFloorPx * k;
        TextMeshProUGUI first = values[0], last = values[n - 1];

        // The two end values sit BESIDE the line (left of its first point, right
        // of its last), so the line can never run through them. They set how far
        // the line is inset; they shrink before it loses more than 22 % a side.
        float dotR = GraphMath.LineDotPx * k * 0.5f;
        float sideGap = dotR + GraphMath.LineValueGapPx * k;
        float maxInset = width * GraphMath.LineMaxInsetFraction;
        CardTypography.BodyFit vf = FitDown(
            s => Mathf.Max(CardTypography.MeasureLineWidth(first, s), CardTypography.MeasureLineWidth(last, s)) + sideGap <= maxInset,
            floor, GraphMath.LineValuePx * k);
        float valueSize = vf.Size;
        ValueSize = valueSize;
        float wFirst = CardTypography.MeasureLineWidth(first, valueSize);
        float wLast = CardTypography.MeasureLineWidth(last, valueSize);
        float inset = Mathf.Clamp(Mathf.Max(wFirst, wLast) + sideGap, GraphMath.LineInsetMinPx * k, width * 0.4f);
        if (vf.Overflows)
            report.Warn($"{report.Card}: a value is too wide to sit beside the line even at {CardTypography.HardFloorPx:F0} px — the line is squeezed.");
        float plotW = width - 2f * inset;
        float[] xs = GraphMath.LineXs(n, width, inset);

        // Labels: one size, side by side under their points; staggered on two
        // rows before going under 32 px.
        float minGap = GraphMath.LineLabelGapPx * k;
        var widths = new float[n];
        bool Clear(float s, int stride)
        {
            for (int i = 0; i < n; i++) widths[i] = CardTypography.MeasureLineWidth(labels[i], s);
            return GraphMath.LabelsClear(GraphMath.CentredLefts(xs, widths, width), widths, minGap, stride);
        }
        float labelMax = GraphMath.LineLabelPx * k;
        float labelSize = CardTypography.LargestFitting(s => Clear(s, 1), floor, labelMax);
        labelRows = 1;
        if (float.IsNaN(labelSize))
        {
            labelRows = 2;
            labelSize = CardTypography.LargestFitting(s => Clear(s, 2), floor, labelMax);
            if (float.IsNaN(labelSize))
            {
                labelSize = floor;
                report.Warn($"{report.Card}: the labels are too long to sit under their points even on two rows at {CardTypography.HardFloorPx:F0} px — they overlap.");
            }
        }
        LabelSize = labelSize;
        for (int i = 0; i < n; i++) widths[i] = CardTypography.MeasureLineWidth(labels[i], labelSize);
        float[] lefts = GraphMath.CentredLefts(xs, widths, width);

        // Top to bottom: headroom (half an end value / a dot), the plot, the
        // axis, the labels, and — cropped — the fixed line.
        float valueCap = CardTypography.CapHeight(first.font, valueSize);
        float valueLineH = SingleLineHeight(first, valueSize);
        float labelLineH = SingleLineHeight(labels[0], labelSize);
        float headroom = Mathf.Max(valueCap * 0.5f, dotR) + GraphMath.LineHeadroomPx * k;
        float axisGap = GraphMath.LineAxisGapPx * k;
        float noteSize = GraphMath.LineNotePx * k, noteGap = 0f, noteH = 0f;
        if (axisNote != null)
        {
            noteGap = GraphMath.LineNoteGapPx * k;
            noteH = SingleLineHeight(axisNote, noteSize);
        }
        float below = axisGap + labelRows * labelLineH + noteGap + noteH;
        float chartH = Mathf.Min(availH, GraphMath.LineChartPx * k);
        float plotH = chartH - headroom - below;
        if (plotH < GraphMath.LinePlotMinPx * k)
        {
            plotH = GraphMath.LinePlotMinPx * k;
            chartH = headroom + plotH + below;
        }
        float axisY = headroom + plotH;

        // The line over the plot; the axis under it, the chart's full width.
        GraphMath.Axis scale = GraphMath.LineAxis(graph.Points, graph.Cropped);
        Vector2[] pts = GraphMath.LinePoints(graph.Points, scale);
        Place(line.rectTransform, inset, headroom, plotW, plotH);
        line.SetLine(pts, GraphMath.LineWidthPx * k);
        reach = GraphMath.LengthFractions(pts, new Vector2(plotW, plotH));
        float axisPx = GraphMath.LineAxisPx * k;
        Place(axis.rectTransform, 0f, axisY - axisPx * 0.5f, width, axisPx);

        for (int i = 0; i < n; i++)
        {
            pointAt[i] = new Vector2(xs[i], axisY - pts[i].y * plotH);
            RectTransform drt = dots[i].rectTransform;
            drt.sizeDelta = new Vector2(dotR * 2f, dotR * 2f);
            drt.anchoredPosition = new Vector2(pointAt[i].x, -pointAt[i].y);

            labels[i].fontSize = labelSize;
            float row = labelRows == 2 ? i % 2 : 0;
            CardTypography.PlaceBox(labels[i], lefts[i], axisY + axisGap + row * labelLineH, widths[i] + 4f * k, labelLineH);
            report.Add("label " + (i + 1), labels[i], labelSize / k, CardTypography.HardFloorPx);
        }

        // End values: capitals centred on their point's height, but never
        // lower than just above the axis (the labels live under it).
        float capTop = CapTop(first, valueSize);
        float lowestCapTop = axisY - GraphMath.LineValueFloorGapPx * k - valueCap;
        first.fontSize = valueSize;
        last.fontSize = valueSize;
        CardTypography.PlaceBox(first, xs[0] - sideGap - wFirst,
            Mathf.Min(pointAt[0].y - valueCap * 0.5f, lowestCapTop) - capTop, wFirst + 4f * k, valueLineH);
        CardTypography.PlaceBox(last, xs[n - 1] + sideGap,
            Mathf.Min(pointAt[n - 1].y - valueCap * 0.5f, lowestCapTop) - capTop, wLast + 4f * k, valueLineH);
        report.Add("value 1", first, valueSize / k, CardTypography.HardFloorPx);
        report.Add("value " + n, last, valueSize / k, CardTypography.HardFloorPx);

        if (axisNote != null)
        {
            axisNote.fontSize = noteSize;
            float noteW = CardTypography.MeasureLineWidth(axisNote, noteSize);
            CardTypography.PlaceBox(axisNote, 0f, axisY + axisGap + labelRows * labelLineH + noteGap, noteW + 4f * k, noteH);
            report.Add("axis note", axisNote, GraphMath.LineNotePx, CardTypography.HardFloorPx);
        }
        return chartH;
    }

    // ---- Share ------------------------------------------------------------------------

    private float PlanShare(float k, float width, float availH, CardTypography.Report report)
    {
        int n = graph.Points.Count;
        float floor = CardTypography.HardFloorPx * k;
        float d = Mathf.Min(Mathf.Min(availH, GraphMath.ShareRingPx * k), width * GraphMath.ShareRingMaxWidthFraction);
        float ringGap = GraphMath.ShareRingGapPx * k;
        float legendMax = width - d - ringGap;
        const float fixedToSize = GraphMath.ShareSwatchToSize + GraphMath.ShareSwatchGapToSize + GraphMath.ShareValueGapToSize;

        // Legend: one size for every label and value. Swatch, label, value.
        float ValueCol(float s) => WidestLine(values, s) + 2f * k;
        float LabelColMax(float s) => legendMax - s * fixedToSize - ValueCol(s);
        float LegendHeight(float s, float labelCol)
        {
            float h = (n - 1) * s * GraphMath.ShareRowGapToSize;
            for (int i = 0; i < n; i++)
                h += Mathf.Max(CardTypography.MeasureHeight(labels[i], s, labelCol), SingleLineHeight(values[i], s));
            return h;
        }
        bool OneLine(float s)
        {
            float col = LabelColMax(s);
            return col > 0f && WidestLine(labels, s) <= col && LegendHeight(s, col) <= availH;
        }
        bool Wrapped(float s, bool twoLinesAtMost)
        {
            float col = LabelColMax(s);
            if (col <= 0f) return false;
            for (int i = 0; i < n; i++)
            {
                if (CardTypography.WidestWordWidth(labels[i].font, labels[i].text, s) > col) return false;
                if (twoLinesAtMost && CardTypography.MeasureHeight(labels[i], s, col) > 2f * SingleLineHeight(labels[i], s) + 1f) return false;
            }
            return LegendHeight(s, col) <= availH;
        }
        // One line a label down to 40 px; then two lines, down to 32; then as many as it takes.
        float legendTop = GraphMath.ShareLegendPx * k;
        float size = CardTypography.LargestFitting(OneLine, Mathf.Min(legendTop, GraphMath.ShareLegendOneLineMinPx * k), legendTop);
        if (float.IsNaN(size)) size = CardTypography.LargestFitting(s => Wrapped(s, true), floor, legendTop);
        if (float.IsNaN(size))
        {
            CardTypography.BodyFit fit = FitDown(s => Wrapped(s, false), floor, legendTop);
            size = fit.Size;
            if (fit.Overflows)
                report.Warn($"{report.Card}: the legend doesn't fit beside the ring even at {CardTypography.HardFloorPx:F0} px — it runs past the chart.");
        }
        LabelSize = size;
        ValueSize = size;

        float sw = size * GraphMath.ShareSwatchToSize, g1 = size * GraphMath.ShareSwatchGapToSize, g2 = size * GraphMath.ShareValueGapToSize;
        float valueCol = ValueCol(size);
        float labelCol = Mathf.Max(1f, Mathf.Min(LabelColMax(size), WidestLine(labels, size) + 2f * k));
        float rowGap = size * GraphMath.ShareRowGapToSize;
        var rowH = new float[n];
        float legendH = (n - 1) * rowGap;
        for (int i = 0; i < n; i++)
        {
            rowH[i] = Mathf.Max(CardTypography.MeasureHeight(labels[i], size, labelCol), SingleLineHeight(values[i], size));
            legendH += rowH[i];
        }

        // Ring and legend as one group, centred in the chart's width; each centred on the other's height.
        float legendW = sw + g1 + labelCol + g2 + valueCol;
        float chartH = Mathf.Max(d, legendH);
        float x = Mathf.Max(0f, (width - (d + ringGap + legendW)) * 0.5f);
        Place(ring.rectTransform, x, (chartH - d) * 0.5f, d, d);

        Color accent = ContentCardUIBuilder.AccentColor;
        Color rest = GraphMath.RemainderColor(Slate);
        var arcs = new List<GraphMath.ColoredArc>(segments.Count);
        foreach (GraphMath.RingSegment s in segments)
            arcs.Add(new GraphMath.ColoredArc(s.Start, s.End, s.Point < 0 ? rest : GraphMath.SegmentColor(s.Point, accent, Slate)));
        ring.SetSegments(arcs, GraphMath.ShareInnerRadius, 1f);
        ring.UnfilledColor = rest;
        ring.GapPx = GraphMath.ShareSegmentGapPx * k;

        float lx = x + d + ringGap, y = (chartH - legendH) * 0.5f;
        float cap = CardTypography.CapHeight(labels[0].font, size);
        for (int i = 0; i < n; i++)
        {
            labels[i].fontSize = size;
            values[i].fontSize = size;
            // The swatch on the label's first line, centred on its capitals.
            float capCentre = y + CapTop(labels[i], size) + cap * 0.5f;
            Place(swatches[i].rectTransform, lx, capCentre - sw * 0.5f, sw, sw);
            Color c = GraphMath.SegmentColor(i, accent, Slate);
            c.a = swatches[i].color.a;
            swatches[i].color = c;
            CardTypography.PlaceBox(labels[i], lx + sw + g1, y, labelCol, rowH[i]);
            CardTypography.PlaceBox(values[i], lx + sw + g1 + labelCol + g2, y, valueCol, rowH[i]);
            report.Add("label " + (i + 1), labels[i], size / k, CardTypography.HardFloorPx);
            report.Add("value " + (i + 1), values[i], size / k, CardTypography.HardFloorPx);
            y += rowH[i] + rowGap;
        }
        return chartH;
    }

    // ---- Fallback rows ----------------------------------------------------------------

    private float PlanRows(float k, float width, float availH, CardTypography.Report report)
    {
        int n = graph.Points.Count;
        float floor = CardTypography.HardFloorPx * k;
        float tableW = Mathf.Min(width, GraphMath.RowsWidthPx * k);
        float colGap = GraphMath.RowsColumnGapPx * k;
        float valueColMax = tableW * GraphMath.RowsValueColumnFraction;

        float ValueCol(float s) => Mathf.Min(valueColMax, WidestLine(values, s) + 2f * k);
        float RowsHeight(float s, float labelCol, float valueCol)
        {
            float h = (n - 1) * s * GraphMath.RowsGapToSize;
            for (int i = 0; i < n; i++)
                h += Mathf.Max(CardTypography.MeasureHeight(labels[i], s, labelCol), CardTypography.MeasureHeight(values[i], s, valueCol));
            return h;
        }
        bool Fits(float s)
        {
            float vc = ValueCol(s), lc = tableW - colGap - vc;
            for (int i = 0; i < n; i++)
            {
                if (CardTypography.WidestWordWidth(labels[i].font, labels[i].text, s) > lc) return false;
                // (against the column's limit: vc itself is the widest value, which a one-word value equals)
                if (CardTypography.WidestWordWidth(values[i].font, values[i].text, s) > valueColMax) return false;
            }
            return RowsHeight(s, lc, vc) <= availH;
        }
        CardTypography.BodyFit fit = FitDown(Fits, floor, GraphMath.RowsPx * k);
        float size = fit.Size;
        LabelSize = size;
        ValueSize = size;
        if (fit.Overflows)
            report.Warn($"{report.Card}: the rows don't fit the frame even at {CardTypography.HardFloorPx:F0} px — they run past title-safe.");

        float valueCol = ValueCol(size);
        float labelCol = Mathf.Min(tableW - colGap - valueCol, WidestLine(labels, size) + 2f * k);
        float x = (width - (labelCol + colGap + valueCol)) * 0.5f;
        float rowGap = size * GraphMath.RowsGapToSize;
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float h = Mathf.Max(CardTypography.MeasureHeight(labels[i], size, labelCol), CardTypography.MeasureHeight(values[i], size, valueCol));
            labels[i].fontSize = size;
            values[i].fontSize = size;
            CardTypography.PlaceBox(labels[i], x, y, labelCol, h);
            CardTypography.PlaceBox(values[i], x + labelCol + colGap, y, valueCol, h);
            report.Add("label " + (i + 1), labels[i], size / k, CardTypography.HardFloorPx);
            report.Add("value " + (i + 1), values[i], size / k, CardTypography.HardFloorPx);
            y += h + (i < n - 1 ? rowGap : 0f);
        }
        return y;
    }

    // ---- Measuring helpers ------------------------------------------------------------

    // A rect placed from its parent's top-left corner (y down).
    private static void Place(RectTransform rt, float left, float top, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(left, -top);
    }

    // The largest whole size in [floor, max] that fits; the floor (flagged) when none does.
    // (A range too narrow to hold a whole size — a fractional scale — is just the floor.)
    private static CardTypography.BodyFit FitDown(Func<float, bool> fits, float floor, float max)
    {
        if (Mathf.FloorToInt(max) < Mathf.CeilToInt(floor))
            return new CardTypography.BodyFit { Size = floor, Overflows = !fits(floor) };
        return CardTypography.FitBody(fits, floor, max, floor, floor);
    }

    // The widest of the texts' single lines at a size (texts a chart doesn't show are null).
    private static float WidestLine(List<TextMeshProUGUI> texts, float size)
    {
        float widest = 0f;
        foreach (TextMeshProUGUI t in texts)
            if (t != null) widest = Mathf.Max(widest, CardTypography.MeasureLineWidth(t, size));
        return widest;
    }

    private static float SingleLineHeight(TMP_Text t, float size)
    {
        t.fontSize = size;
        return t.GetPreferredValues(100000f, 100000f).y;
    }

    private static int LinesAt(TMP_Text t, float size, float width)
    {
        if (string.IsNullOrEmpty(t.text)) return 0;
        t.fontSize = size;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, 10000f);
        t.ForceMeshUpdate(true, true);
        return t.textInfo.lineCount;
    }

    private static float FaceScale(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        return size / Mathf.Max(1f, fi.pointSize) * Mathf.Max(0.0001f, fi.scale);
    }

    // Distance from the top of TMP's line box down to the capitals' top.
    private static float CapTop(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        return (fi.ascentLine - fi.capLine) * FaceScale(t, size);
    }

    // Where a top-aligned text box starts so that its ink — the first line's
    // capitals down to the last line's baseline — is centred on a band.
    private static float CentredTop(TMP_Text t, float size, float width, float bandTop, float bandHeight)
    {
        int lines = Mathf.Max(1, LinesAt(t, size, width));
        float ink = CardTypography.CapHeight(t.font, size) + (lines - 1) * t.font.faceInfo.lineHeight * FaceScale(t, size);
        return bandTop + (bandHeight - ink) * 0.5f - CapTop(t, size);
    }

    private static void SetAlpha(Graphic g, float alpha)
    {
        Color c = g.color;
        if (Mathf.Approximately(c.a, alpha)) return;
        c.a = alpha;
        g.color = c;
    }

    // =====================================================================
    // Entrance (driven per frame on the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        KillCurrentSequence();

        // Graph owns its entrance — no slide, no preset tilt, root fully
        // visible; the content fades in and the chart draws itself.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;
        dimEnvelope.Open(clock);

        shownAt = clock;
        entranceRunning = true;
        closing = false;
        Pose();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame: the card clock, the chart and the dim. Public so the
    /// self-tests can step a card frame by frame in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        Pose();
        if (dim != null)
        {
            Color c = dim.color;
            c.a = dimEnvelope.AlphaAt(clock);
            dim.color = c;
        }
    }

    // Puts the content and the chart where they are `clock` into the entrance
    // (before Show: hidden, nothing grown).
    private void Pose()
    {
        if (graph == null) return;
        bool shown = !float.IsNaN(shownAt);
        float age = shown ? clock - shownAt : -1f;
        if (!closing) contentGroup.alpha = shown ? GraphMath.ContentAlphaAt(age) : 0f;

        // The chart starts once the card has landed.
        float since = shown ? age - GraphMath.ChartDelaySeconds : -1f;
        if (graph.Ok)
        {
            if (graph.Kind == GraphKind.Line) PoseLine(since);
            else if (graph.Kind == GraphKind.Share) PoseShare(since);
            else PoseBars(since);
        }

        if (entranceRunning && shown && since >= GraphMath.ChartSeconds(graph))
        {
            entranceRunning = false;
            // The slow drift moves the title, chart and source only — never the dim.
            StartIdleFloat(content);
        }
    }

    private void PoseBars(float since)
    {
        for (int i = 0; i < bars.Count; i++)
        {
            float g = GraphMath.BarGrowAt(since - GraphMath.BarStart(i));
            float w = fractions[i] * placement.Track * g;
            RectTransform brt = bars[i].rectTransform;
            brt.anchoredPosition = new Vector2(barLeft, -barTops[i]);
            brt.sizeDelta = new Vector2(w, barHeight);
            bars[i].enabled = g > 0f;

            RectTransform vrt = values[i].rectTransform;
            vrt.anchoredPosition = new Vector2(barLeft + w + valueFromBarEnd[i], vrt.anchoredPosition.y);
            SetAlpha(values[i], GraphMath.ValueAlphaAt(g));
        }
    }

    private void PoseLine(float since)
    {
        int n = dots.Count;
        line.Drawn = GraphMath.LineDrawnAt(since);
        for (int i = 0; i < n; i++)
        {
            // A point's dot pops as the line's head reaches it.
            float dotAge = since - GraphMath.LineReachSeconds(reach[i]);
            dots[i].rectTransform.localScale = Vector3.one * GraphMath.DotScaleAt(dotAge);
            dots[i].enabled = dotAge > 0f;
        }
        if (n == 0) return;
        // The first value comes in as the line sets off, the last as it arrives.
        SetAlpha(values[0], GraphMath.FadeAt(since));
        SetAlpha(values[n - 1], GraphMath.FadeAt(since - GraphMath.LineDrawSeconds));
    }

    private void PoseShare(float since)
    {
        ring.Fill = GraphMath.RingFillAt(since, segments);
        for (int i = 0; i < swatches.Count; i++)
        {
            // A point's legend row comes in as its segment starts to fill.
            float a = GraphMath.FadeAt(since - i * GraphMath.SegmentSeconds);
            SetAlpha(swatches[i], a);
            SetAlpha(labels[i], a);
            SetAlpha(values[i], a);
        }
    }

    // =====================================================================
    // Hide
    // =====================================================================

    public override void Hide(bool fast = false)
    {
        dimEnvelope.Close(clock);
        entranceRunning = false; // on its way out: no idle drift to start

        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();
        StopIdleFloat();
        closing = true; // the content's alpha is the fade's now; the chart stays as it stands

        // Title, chart and source fade out together; the dim on its own envelope.
        currentSequence = DOTween.Sequence()
            .Join(contentGroup.DOFade(0f, GraphMath.ContentFadeOutSeconds).SetEase(Ease.InQuad))
            .AppendInterval(Mathf.Max(0f, BigTextTypography.DimFadeOutSeconds - GraphMath.ContentFadeOutSeconds))
            .OnComplete(() => OnHideComplete?.Invoke());
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// {Graph:…} maths — the data field ("label=value;label=value;…"), the three
/// charts' honest scales (bars from zero, a line on a zero or an openly
/// cropped axis, parts of a whole on a ring), where things sit, the meshes
/// and the timing. Pure, so the self-tests run it with no engine;
/// <see cref="GraphCard"/> does the TMP measuring and drawing.
///
/// Values use <see cref="CompareMath"/>'s grammar (currency, thousands commas,
/// K/M/B/T, %, x/×, a trailing +) and may also START with &lt;, &gt; or ~, which
/// the number ignores. The card only ever SHOWS labels and values as written;
/// the numbers parsed here only size the marks.
///
/// Sizes are px of a 1080-px-tall frame, scaled by the frame's real height
/// (<see cref="ScaleFor"/>). Rects are y DOWN from the top edge unless a
/// summary says otherwise.
/// </summary>
public static class GraphMath
{
    // =========================================================================
    // The data field
    // =========================================================================

    public sealed class Point
    {
        public string Label;       // as written (outer spaces trimmed)
        public string ValueText;   // as written (outer spaces trimmed)
        public double Value;       // magnitude applied: "$1.5B" → 1.5e9; 0 when it didn't parse
        public bool Parsed;
    }

    public sealed class Data
    {
        public GraphKind Kind;
        /// <summary>True only when a Line chart is drawn on an axis that does NOT start at zero
        /// (",Cropped" was asked for, the chart is Ok, and cropping really lifts the axis).</summary>
        public bool Cropped;
        /// <summary>In script order (as written); at most <see cref="MaxFallbackRows"/>.</summary>
        public List<Point> Points = new List<Point>();
        /// <summary>A chart can be drawn. False → the points are listed as text rows.</summary>
        public bool Ok;
        /// <summary>When !Ok: why — one sentence without a trailing full stop.</summary>
        public string Problem;
        /// <summary>Not fatal: ",Cropped is ignored on Bars", a 3 % sliver, dropped rows…</summary>
        public List<string> Notes = new List<string>();
    }

    public const int MinBars = 2, MaxBars = 6, MinLine = 3, MaxLine = 12, MinShare = 2, MaxShare = 5;
    /// <summary>The text-row fallback lists at most this many points; the rest are dropped (and noted).</summary>
    public const int MaxFallbackRows = 12;
    /// <summary>Smallest bar / ring segment drawn, as a share of the scale (Compare's 3 % sliver).</summary>
    public const float MinFraction = CompareMath.MinBarFraction;
    /// <summary>Shares may add up to this much (rounding) before the chart is refused.</summary>
    public const double ShareSumLimit = 100.5;
    /// <summary>Shares within this of 100 close the ring (no hairline remainder from rounding).</summary>
    public const double ShareRounding = 0.5;
    /// <summary>A cropped axis starts this share of the values' range under the smallest value.</summary>
    public const double CropMarginOfRange = 0.10;

    /// <summary>Compare's value grammar, plus one leading &lt;, &gt; or ~ (shown, ignored for the number).</summary>
    public static CompareMath.Value ParseValue(string text)
    {
        string t = (text ?? string.Empty).TrimStart();
        if (t.Length > 0 && (t[0] == '<' || t[0] == '>' || t[0] == '~')) t = t.Substring(1);
        return CompareMath.Parse(t);
    }

    /// <summary>How many points a chart takes.</summary>
    public static void PointRange(GraphKind kind, out int min, out int max)
    {
        switch (kind)
        {
            case GraphKind.Line: min = MinLine; max = MaxLine; break;
            case GraphKind.Share: min = MinShare; max = MaxShare; break;
            default: min = MinBars; max = MaxBars; break;
        }
    }

    /// <summary>
    /// Reads "label=value;label=value;…" (each point split at its FIRST '=';
    /// empty pieces between two ';' are skipped) and decides whether
    /// <paramref name="kind"/> can be drawn from it. Never throws.
    /// </summary>
    public static Data Parse(string dataField, GraphKind kind, bool cropped)
    {
        var d = new Data { Kind = kind };
        var parsed = new List<CompareMath.Value>();
        string problem = null;
        int written = 0; // label=value pairs read, before the row limit

        string field = dataField ?? string.Empty;
        if (field.Trim().Length == 0) problem = "the data field is empty";
        foreach (string raw in field.Split(';'))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue; // "a=1;;b=2", or a ';' at the end
            int eq = part.IndexOf('=');
            if (eq < 0)
            {
                if (problem == null) problem = $"\"{part}\" has no \"=\" (each point is label=value)";
                continue;
            }
            string label = part.Substring(0, eq).Trim();
            string value = part.Substring(eq + 1).Trim();
            if (label.Length == 0)
            {
                if (problem == null) problem = $"\"{part}\" has no label before its \"=\"";
                continue;
            }
            if (value.Length == 0)
            {
                if (problem == null) problem = $"\"{label}\" has no value after its \"=\"";
                continue;
            }

            written++;
            if (d.Points.Count >= MaxFallbackRows) continue;
            CompareMath.Value v = ParseValue(value);
            parsed.Add(v);
            d.Points.Add(new Point { Label = label, ValueText = value, Value = v.Ok ? v.Amount : 0.0, Parsed = v.Ok });
        }
        if (problem == null && d.Points.Count == 0) problem = "the data field has no label=value point";

        // A value that isn't a number.
        if (problem == null)
            foreach (Point p in d.Points)
                if (!p.Parsed)
                {
                    problem = $"\"{p.ValueText}\" isn't a number (the value of \"{p.Label}\")";
                    break;
                }

        // One kind for every value: the same currency, all %, all multiples, or all plain.
        if (problem == null)
            for (int i = 1; i < parsed.Count; i++)
                if (!CompareMath.SameKind(parsed[0], parsed[i]))
                {
                    problem = $"the values aren't the same kind ({KindToken(parsed[0])} and {KindToken(parsed[i])})";
                    break;
                }

        PointRange(kind, out int min, out int max);
        if (problem == null && (written < min || written > max))
            problem = $"{kind} takes {min} to {max} points (got {written})";

        if (problem == null && kind == GraphKind.Share)
        {
            if (parsed[0].Unit != CompareMath.Unit.Percent || parsed[0].Currency != '\0')
                problem = $"Share takes percentages (these are {KindToken(parsed[0])})";
            else
            {
                double sum = 0.0;
                foreach (Point p in d.Points) sum += p.Value;
                if (sum > ShareSumLimit) problem = $"the shares add up to {Num(sum)} %";
            }
        }

        if (problem == null)
        {
            double top = 0.0;
            foreach (Point p in d.Points) top = Math.Max(top, p.Value);
            if (top <= 0.0) problem = "every value is zero";
        }

        d.Problem = problem;
        d.Ok = problem == null;

        // ---- Notes (not fatal) --------------------------------------------------------
        if (written > MaxFallbackRows)
            d.Notes.Add($"{written} points — only the first {MaxFallbackRows} are listed");

        if (cropped)
        {
            if (kind != GraphKind.Line)
                d.Notes.Add($",Cropped is ignored on {kind}");
            else if (d.Ok)
            {
                if (LineAxis(d.Points, true).Min > 0.0) d.Cropped = true;
                else d.Notes.Add(",Cropped changes nothing here — the axis starts at zero anyway");
            }
        }

        if (d.Ok && kind != GraphKind.Line)
        {
            bool[] sliver;
            if (kind == GraphKind.Share)
            {
                sliver = new bool[d.Points.Count];
                foreach (RingSegment s in RingSegments(d.Points))
                    if (s.Point >= 0) sliver[s.Point] = s.Sliver;
            }
            else BarFractions(d.Points, out sliver);
            for (int i = 0; i < sliver.Length; i++)
                if (sliver[i])
                    d.Notes.Add($"\"{d.Points[i].Label}\" is under {Num(MinFraction * 100f)} % of the scale — drawn as a {Num(MinFraction * 100f)} % sliver");
        }
        return d;
    }

    // "$", "€", "%", "x", or "plain numbers" — for the mixed-kinds sentence.
    static string KindToken(CompareMath.Value v)
    {
        string s = v.Currency != '\0' ? v.Currency.ToString() : string.Empty;
        if (v.Unit == CompareMath.Unit.Percent) s += "%";
        else if (v.Unit == CompareMath.Unit.Multiple) s += "x";
        return s.Length > 0 ? s : "plain numbers";
    }

    static string Num(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    // =========================================================================
    // Frame
    // =========================================================================

    public const float ReferenceFrameHeight = 1080f;
    /// <summary>Title-safe margin on every side, as a share of the frame.</summary>
    public const float SafeMarginFraction = 0.06f;
    /// <summary>The card's column (title, chart, source) is at most this wide.</summary>
    public const float ContentMaxWidthPx = 1560f;
    /// <summary>The chart is planned for at least this height, however long the title and source run.</summary>
    public const float MinChartHeightPx = 240f;

    // ---- Title / source ---------------------------------------------------------------
    public const float TitlePx = 64f, TitleMinPx = 44f;
    public const int TitleMaxLines = 2;
    public const float SourcePx = 36f, SourceFloorPx = 32f;
    public const float TitleGapPx = 52f, SourceGapPx = 40f;

    public static float ScaleFor(float frameHeight) => frameHeight / ReferenceFrameHeight;

    /// <summary>The frame inside its 6 % title-safe margins.</summary>
    public static Rect SafeRect(float frameW, float frameH)
        => new Rect(frameW * SafeMarginFraction, frameH * SafeMarginFraction,
                    frameW * (1f - 2f * SafeMarginFraction), frameH * (1f - 2f * SafeMarginFraction));

    /// <summary>Width of the card's column: the safe width, at most <see cref="ContentMaxWidthPx"/>.</summary>
    public static float ContentWidth(float frameW, float frameH)
        => Mathf.Min(frameW * (1f - 2f * SafeMarginFraction), ContentMaxWidthPx * ScaleFor(frameH));

    // =========================================================================
    // Bars — linear from zero, the largest fills the track
    // =========================================================================

    public const float BarHeightPx = 128f, BarMinHeightPx = 56f;
    /// <summary>Gap between two bars, × the bar's height.</summary>
    public const float BarGapToHeight = 0.38f;
    public const float BarRadiusPx = 10f;
    public const float BarLabelPx = 52f;
    /// <summary>The label column takes at most this share of the chart's width.</summary>
    public const float BarLabelColumnFraction = 0.30f;
    public const float BarLabelGapPx = 28f;
    public const float BarValuePx = 110f, BarValueMinPx = 40f;
    /// <summary>Bar thickness, as a multiple of the value's cap height (Compare's).</summary>
    public const float BarToCap = 1.6f;
    /// <summary>The values shrink while the widest is over this share of the bar row.</summary>
    public const float BarValueMaxRowFraction = 0.45f;
    public const float BarValuePadToCap = 0.40f, BarValueGapToCap = 0.35f;

    /// <summary>
    /// Each point's share of the track: value ÷ the largest value, raised to
    /// <see cref="MinFraction"/> when it would be thinner (flagged in
    /// <paramref name="sliver"/>). Script order — never sorted.
    /// </summary>
    public static float[] BarFractions(IList<Point> points, out bool[] sliver)
    {
        int n = points != null ? points.Count : 0;
        var f = new float[n];
        sliver = new bool[n];
        double top = 0.0;
        for (int i = 0; i < n; i++) top = Math.Max(top, points[i].Value);
        for (int i = 0; i < n; i++)
        {
            f[i] = top > 0.0 ? (float)(points[i].Value / top) : 0f;
            if (f[i] < MinFraction) { f[i] = MinFraction; sliver[i] = true; }
        }
        return f;
    }

    public struct BarPlacement
    {
        public float Track;      // bar track width (the largest bar's length)
        public bool[] Inside;    // value i sits inside its bar
        public bool Overflows;   // a value is wider than the row can hold
    }

    /// <summary>
    /// <see cref="CompareMath.Place"/> for any number of bars: a value sits
    /// inside its bar (right end, <paramref name="padIn"/>) when the bar is long
    /// enough, else just past its end after <paramref name="gap"/>; the track
    /// starts at the full row and shrinks only as far as an outside value needs
    /// to stay inside the row.
    /// </summary>
    public static BarPlacement PlaceBars(float rowWidth, IList<float> fractions, IList<float> valueWidths, float padIn, float gap)
    {
        int n = fractions != null ? fractions.Count : 0;
        var p = new BarPlacement { Track = rowWidth, Inside = new bool[n] };
        for (int pass = 0; pass < n + 2; pass++)
        {
            float track = p.Track;
            for (int i = 0; i < n; i++)
            {
                bool inside = fractions[i] * p.Track >= valueWidths[i] + 2f * padIn;
                if (!inside && fractions[i] * track + gap + valueWidths[i] > rowWidth)
                    track = Mathf.Min(track, (rowWidth - gap - valueWidths[i]) / Mathf.Max(1e-4f, fractions[i]));
            }
            if (Mathf.Approximately(track, p.Track)) break;
            p.Track = Mathf.Max(0f, track);
        }
        for (int i = 0; i < n; i++)
        {
            p.Inside[i] = fractions[i] * p.Track >= valueWidths[i] + 2f * padIn;
            if (!p.Inside[i] && fractions[i] * p.Track + gap + valueWidths[i] > rowWidth + 0.5f) p.Overflows = true;
        }
        if (p.Track <= 0f) p.Overflows = true;
        return p;
    }

    /// <summary>One bar's height when <paramref name="n"/> bars and their gaps share <paramref name="availableHeight"/>.</summary>
    public static float BarHeight(int n, float availableHeight, float k)
    {
        float units = n + Mathf.Max(0, n - 1) * BarGapToHeight;
        return Mathf.Clamp(availableHeight / Mathf.Max(1f, units), BarMinHeightPx * k, BarHeightPx * k);
    }

    /// <summary>Bar <paramref name="i"/>'s top inside the chart.</summary>
    public static float BarTop(int i, float barHeight) => i * barHeight * (1f + BarGapToHeight);

    /// <summary>The chart's height: <paramref name="n"/> bars and the gaps between them.</summary>
    public static float BarsHeight(int n, float barHeight) => n * barHeight + Mathf.Max(0, n - 1) * barHeight * BarGapToHeight;

    // =========================================================================
    // Line — evenly spaced points on a zero (or openly cropped) axis
    // =========================================================================

    public const float LineChartPx = 640f, LinePlotMinPx = 200f;
    public const float LineWidthPx = 8f, LineDotPx = 24f, LineAxisPx = 3f;
    public const float LineValuePx = 56f;
    /// <summary>Gap between an end point's dot and its value, which sits beside the line's end.</summary>
    public const float LineValueGapPx = 16f;
    /// <summary>An end value's baseline stays this far above the axis.</summary>
    public const float LineValueFloorGapPx = 8f;
    /// <summary>The end values shrink while they would push the line in further than this share of the width per side.</summary>
    public const float LineMaxInsetFraction = 0.22f;
    public const float LineInsetMinPx = 48f;
    public const float LineLabelPx = 40f, LineLabelGapPx = 20f;
    public const float LineAxisGapPx = 16f, LineHeadroomPx = 8f;
    public const float LineNotePx = 34f, LineNoteGapPx = 10f;
    public const float LineNoteTrackingEm = 4f;   // TMP characterSpacing (1/100 em)

    public struct Axis
    {
        public double Min, Max;
        /// <summary>The axis doesn't start at zero — the card must say so.</summary>
        public bool Cropped => Min > 0.0;
    }

    /// <summary>
    /// The value axis: from zero to the largest value. With
    /// <paramref name="cropped"/> it may start <see cref="CropMarginOfRange"/> of
    /// the range under the smallest value — never below zero, so values that
    /// reach down to zero keep a zero axis.
    /// </summary>
    public static Axis LineAxis(IList<Point> points, bool cropped)
    {
        int n = points != null ? points.Count : 0;
        double lo = double.MaxValue, hi = 0.0;
        for (int i = 0; i < n; i++)
        {
            lo = Math.Min(lo, points[i].Value);
            hi = Math.Max(hi, points[i].Value);
        }
        var a = new Axis { Min = 0.0, Max = hi };
        if (cropped && n > 0 && hi > lo)
        {
            double start = lo - CropMarginOfRange * (hi - lo);
            if (start > 0.0) a.Min = start;
        }
        return a;
    }

    /// <summary>A value's height on the axis: 0 = the axis line, 1 = the largest value.</summary>
    public static float LineY(double value, Axis axis)
    {
        double span = axis.Max - axis.Min;
        if (span <= 0.0) return 0f;
        return Mathf.Clamp01((float)((value - axis.Min) / span));
    }

    /// <summary>The polyline, normalised over the plot: x 0→1 left→right in script order, y 0→1 bottom→top.</summary>
    public static Vector2[] LinePoints(IList<Point> points, Axis axis)
    {
        int n = points != null ? points.Count : 0;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Vector2(n > 1 ? i / (float)(n - 1) : 0.5f, LineY(points[i].Value, axis));
        return pts;
    }

    /// <summary><paramref name="n"/> evenly spaced x positions from <paramref name="inset"/> to width − inset.</summary>
    public static float[] LineXs(int n, float width, float inset)
    {
        var xs = new float[Mathf.Max(0, n)];
        for (int i = 0; i < n; i++)
            xs[i] = n > 1 ? inset + i * (width - 2f * inset) / (n - 1) : width * 0.5f;
        return xs;
    }

    /// <summary>Left edges of boxes centred on <paramref name="xs"/>, each kept inside [0, width].</summary>
    public static float[] CentredLefts(IList<float> xs, IList<float> widths, float width)
    {
        int n = xs != null ? xs.Count : 0;
        var lefts = new float[n];
        for (int i = 0; i < n; i++)
            lefts[i] = Mathf.Clamp(xs[i] - widths[i] * 0.5f, 0f, Mathf.Max(0f, width - widths[i]));
        return lefts;
    }

    /// <summary>
    /// True when no label comes within <paramref name="gap"/> of the next one
    /// on its row. <paramref name="stride"/> 1 = one row; 2 = staggered on two
    /// rows (label i shares a row with i + 2).
    /// </summary>
    public static bool LabelsClear(IList<float> lefts, IList<float> widths, float gap, int stride)
    {
        int n = lefts != null ? lefts.Count : 0;
        stride = Mathf.Max(1, stride);
        for (int i = 0; i + stride < n; i++)
            if (lefts[i + stride] < lefts[i] + widths[i] + gap) return false;
        return true;
    }

    /// <summary>
    /// Each vertex's distance along the polyline as a share of its whole length
    /// (0 at the first, 1 at the last), for normalised points stretched over
    /// <paramref name="size"/> — the unit <see cref="BuildLine"/>'s drawn
    /// fraction is measured in.
    /// </summary>
    public static float[] LengthFractions(IList<Vector2> normalised, Vector2 size)
    {
        int n = normalised != null ? normalised.Count : 0;
        var f = new float[n];
        float total = 0f;
        for (int i = 1; i < n; i++)
        {
            total += Vector2.Distance(Vector2.Scale(normalised[i - 1], size), Vector2.Scale(normalised[i], size));
            f[i] = total;
        }
        for (int i = 1; i < n; i++) f[i] = total > 0f ? f[i] / total : 1f;
        return f;
    }

    // =========================================================================
    // Share — parts of a whole on a ring, clockwise from twelve o'clock
    // =========================================================================

    public const float ShareRingPx = 560f;
    /// <summary>The ring's diameter is at most this share of the chart's width.</summary>
    public const float ShareRingMaxWidthFraction = 0.40f;
    /// <summary>Inner radius ÷ outer radius.</summary>
    public const float ShareInnerRadius = 0.60f;
    public const float ShareRingGapPx = 96f;
    /// <summary>The hairline between two segments, px along the ring's middle.</summary>
    public const float ShareSegmentGapPx = 4f;
    public const float ShareLegendPx = 52f;
    /// <summary>The legend goes down to this before a label may wrap onto a second line.</summary>
    public const float ShareLegendOneLineMinPx = 40f;
    public const float ShareSwatchToSize = 0.62f, ShareSwatchGapToSize = 0.45f, ShareValueGapToSize = 0.8f, ShareRowGapToSize = 0.5f;
    public const float ShareSwatchRadiusPx = 6f;
    /// <summary>Each neutral segment is this much nearer white than the one before it.</summary>
    public const float ShareTintStep = 0.22f;
    /// <summary>What is left to 100 %: Slate this far toward black, at this alpha.</summary>
    public const float RemainderShade = 0.8f, RemainderAlpha = 0.5f;

    public struct RingSegment
    {
        /// <summary>Turns (0–1) clockwise from twelve o'clock.</summary>
        public float Start, End;
        /// <summary>Index into the points; −1 = what is left to 100 % (unlabelled).</summary>
        public int Point;
        /// <summary>Under 3 % of the ring — drawn at 3 %.</summary>
        public bool Sliver;
    }

    /// <summary>
    /// One segment per point in script order, then what is left to 100 % (if
    /// anything). A point under <see cref="MinFraction"/> is drawn at it; the
    /// room comes out of the remainder, and only when there is none out of the
    /// other segments (in proportion). Shares within
    /// <see cref="ShareRounding"/> of 100 close the ring exactly.
    /// </summary>
    public static List<RingSegment> RingSegments(IList<Point> points)
    {
        int n = points != null ? points.Count : 0;
        var list = new List<RingSegment>(n + 1);
        if (n == 0) return list;

        double sum = 0.0;
        for (int i = 0; i < n; i++) sum += Math.Max(0.0, points[i].Value);
        double whole = sum >= 100.0 - ShareRounding ? sum : 100.0;

        var f = new double[n];
        var atFloor = new bool[n];
        var sliver = new bool[n];
        for (int i = 0; i < n; i++)
        {
            f[i] = Math.Max(0.0, points[i].Value) / whole;
            sliver[i] = atFloor[i] = f[i] < MinFraction;
        }

        double scale = 1.0;
        for (int pass = 0; pass <= n; pass++)
        {
            double floorSum = 0.0, freeSum = 0.0;
            for (int i = 0; i < n; i++)
                if (atFloor[i]) floorSum += MinFraction; else freeSum += f[i];
            double room = Math.Max(0.0, 1.0 - floorSum);
            scale = freeSum > room && freeSum > 0.0 ? room / freeSum : 1.0;
            bool changed = false;
            for (int i = 0; i < n; i++)
                if (!atFloor[i] && f[i] * scale < MinFraction) { atFloor[i] = true; changed = true; }
            if (!changed) break;
        }

        double at = 0.0;
        for (int i = 0; i < n; i++)
        {
            double len = atFloor[i] ? MinFraction : f[i] * scale;
            list.Add(new RingSegment { Start = (float)at, End = (float)Math.Min(1.0, at + len), Point = i, Sliver = sliver[i] });
            at += len;
        }
        if (at < 1.0 - 1e-6)
            list.Add(new RingSegment { Start = (float)at, End = 1f, Point = -1 });
        else
        {
            RingSegment last = list[n - 1];
            last.End = 1f;
            list[n - 1] = last;
        }
        return list;
    }

    /// <summary>Point <paramref name="index"/>'s colour: the accent for the first (the story's
    /// subject), then <paramref name="slate"/>, lighter each step.</summary>
    public static Color SegmentColor(int index, Color accent, Color slate)
    {
        if (index <= 0) return accent;
        Color c = Color.Lerp(slate, Color.white, Mathf.Min(1f, (index - 1) * ShareTintStep));
        c.a = 1f;
        return c;
    }

    /// <summary>The unlabelled rest of the ring: dark and translucent, so it reads as the unlit part.</summary>
    public static Color RemainderColor(Color slate)
    {
        Color c = Color.Lerp(slate, Color.black, RemainderShade);
        c.a = RemainderAlpha;
        return c;
    }

    // =========================================================================
    // Timing (seconds on the card's clock)
    // =========================================================================

    /// <summary>Title, labels and source fade in over this from Show…</summary>
    public const float ContentFadeInSeconds = 0.25f;
    /// <summary>…and the chart starts this long after Show: the card has landed.</summary>
    public const float ChartDelaySeconds = 0.30f;
    public const float ContentFadeOutSeconds = 0.20f;
    /// <summary>A value or legend row fades in over this.</summary>
    public const float FadeSeconds = 0.20f;

    public const float BarGrowSeconds = 0.5f, BarStaggerSeconds = 0.15f;
    public const float LineDrawSeconds = 1.2f;
    public const float DotPopSeconds = 0.2f, DotPopOvershoot = 1.7f;
    public const float SegmentSeconds = 0.35f;

    public static float ContentAlphaAt(float age)
    {
        if (age <= 0f) return 0f;
        float x = Mathf.Clamp01(age / ContentFadeInSeconds);
        return x * x * (3f - 2f * x);
    }

    /// <summary>0 → 1 over <see cref="FadeSeconds"/> from <paramref name="age"/> 0.</summary>
    public static float FadeAt(float age) => age <= 0f ? 0f : Mathf.Clamp01(age / FadeSeconds);

    /// <summary>When bar <paramref name="i"/> starts growing, after the chart starts.</summary>
    public static float BarStart(int i) => i * BarStaggerSeconds;

    /// <summary>A bar's growth 0→1 <paramref name="age"/> s after it starts (ease-out cubic).</summary>
    public static float BarGrowAt(float age)
    {
        if (age <= 0f) return 0f;
        return OutCubic(age / BarGrowSeconds);
    }

    /// <summary>A bar's value fades in over the last part of its growth (Compare's curve).</summary>
    public static float ValueAlphaAt(float grow) => CompareMath.ValueAlphaAt(grow);

    public static float BarsSeconds(int n) => Mathf.Max(0, n - 1) * BarStaggerSeconds + BarGrowSeconds;

    /// <summary>How much of the line is drawn (by length) <paramref name="age"/> s after it starts: ease in and out.</summary>
    public static float LineDrawnAt(float age)
    {
        if (age <= 0f) return 0f;
        if (age >= LineDrawSeconds) return 1f;
        return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * age / LineDrawSeconds);
    }

    /// <summary>When the line's head reaches the point <paramref name="lengthFraction"/> along it
    /// (the inverse of <see cref="LineDrawnAt"/>).</summary>
    public static float LineReachSeconds(float lengthFraction)
        => LineDrawSeconds * Mathf.Acos(1f - 2f * Mathf.Clamp01(lengthFraction)) / Mathf.PI;

    /// <summary>A point's dot <paramref name="sinceReached"/> s after the line reaches it: 0 → 1 with a small overshoot.</summary>
    public static float DotScaleAt(float sinceReached)
    {
        if (sinceReached <= 0f) return 0f;
        if (sinceReached >= DotPopSeconds) return 1f;
        float t = sinceReached / DotPopSeconds - 1f, s = DotPopOvershoot;
        return 1f + (s + 1f) * t * t * t + s * t * t;
    }

    public static float LineSeconds => LineDrawSeconds + Mathf.Max(DotPopSeconds, FadeSeconds);

    /// <summary>
    /// The ring's fill 0–1 (a share of the whole sequence, by angle)
    /// <paramref name="age"/> s after it starts: one point's segment after
    /// another, <see cref="SegmentSeconds"/> each, eased out. What is left to
    /// 100 % isn't a beat — the fill is 1 once the last point's segment is in.
    /// </summary>
    public static float RingFillAt(float age, IList<RingSegment> segments)
    {
        int n = segments != null ? segments.Count : 0;
        if (n == 0 || age <= 0f) return 0f;
        float total = segments[n - 1].End;
        if (total <= 0f) return 0f;

        float turns = 0f;
        int beat = 0;
        for (int i = 0; i < n; i++)
        {
            RingSegment s = segments[i];
            if (s.Point < 0) continue;
            float local = age - beat * SegmentSeconds;
            beat++;
            if (local < SegmentSeconds - 1e-4f) // (n × 0.35 f isn't exactly n beats)
            {
                if (local > 0f) turns = s.Start + (s.End - s.Start) * OutCubic(local / SegmentSeconds);
                return Mathf.Clamp01(turns / total);
            }
            turns = s.End;
        }
        return 1f;
    }

    public static float ShareSeconds(int points) => Mathf.Max(0, points) * SegmentSeconds;

    /// <summary>How long the chart animates once it starts (0 for the text rows).</summary>
    public static float ChartSeconds(Data data)
    {
        if (data == null || !data.Ok) return 0f;
        switch (data.Kind)
        {
            case GraphKind.Line: return LineSeconds;
            case GraphKind.Share: return ShareSeconds(data.Points.Count);
            default: return BarsSeconds(data.Points.Count);
        }
    }

    /// <summary>The whole entrance, from Show.</summary>
    public static float EntranceSeconds(Data data) => ChartDelaySeconds + ChartSeconds(data);

    static float OutCubic(float x)
    {
        float u = 1f - Mathf.Clamp01(x);
        return 1f - u * u * u;
    }

    // =========================================================================
    // Fallback rows (no chart: label left, value right)
    // =========================================================================

    public const float RowsPx = 60f, RowsWidthPx = 1200f, RowsColumnGapPx = 64f;
    /// <summary>The value column takes at most this share of the table.</summary>
    public const float RowsValueColumnFraction = 0.5f;
    public const float RowsGapToSize = 0.35f;

    // =========================================================================
    // Meshes (plain lists — the Graphics copy them into a VertexHelper)
    // =========================================================================

    /// <summary>A UI mesh holds at most 65,000 vertices (MapFillGraphic's cap).</summary>
    public const int MaxMeshVertices = 64000;
    /// <summary>Arc steps for a full turn of the ring.</summary>
    public const int RingStepsPerTurn = 128;
    /// <summary>Steps round a line join / cap.</summary>
    public const int DiscSteps = 20;
    /// <summary>UI meshes get no anti-aliasing: every outer edge gets a strip this wide fading to
    /// clear instead (0 = hard edges).</summary>
    public const float FeatherPx = 1f;

    public sealed class Mesh2D
    {
        public readonly List<Vector2> Vertices = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();

        public void Clear() { Vertices.Clear(); Colors.Clear(); Triangles.Clear(); }

        public int Add(Vector2 position, Color32 color)
        {
            Vertices.Add(position);
            Colors.Add(color);
            return Vertices.Count - 1;
        }

        public void Triangle(int a, int b, int c) { Triangles.Add(a); Triangles.Add(b); Triangles.Add(c); }

        public void Quad(int a, int b, int c, int d) { Triangle(a, b, c); Triangle(a, c, d); }

        /// <summary>Area of the triangles whose three corners are all at least <paramref name="minAlpha"/> opaque
        /// (255 = the solid shape without its feathered edge; overlaps count twice).</summary>
        public float Area(byte minAlpha = 0)
        {
            float area = 0f;
            for (int i = 0; i + 2 < Triangles.Count; i += 3)
            {
                int a = Triangles[i], b = Triangles[i + 1], c = Triangles[i + 2];
                if (Colors[a].a < minAlpha || Colors[b].a < minAlpha || Colors[c].a < minAlpha) continue;
                Vector2 u = Vertices[b] - Vertices[a], v = Vertices[c] - Vertices[a];
                area += Mathf.Abs(u.x * v.y - u.y * v.x) * 0.5f;
            }
            return area;
        }

        /// <summary>Bounds of the vertices at least <paramref name="minAlpha"/> opaque (y as given).</summary>
        public Rect Bounds(byte minAlpha = 0)
        {
            bool any = false;
            float x0 = 0f, x1 = 0f, y0 = 0f, y1 = 0f;
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Colors[i].a < minAlpha) continue;
                Vector2 p = Vertices[i];
                if (!any) { x0 = x1 = p.x; y0 = y1 = p.y; any = true; continue; }
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
            }
            return any ? Rect.MinMaxRect(x0, y0, x1, y1) : new Rect(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>
    /// A polyline of <paramref name="width"/> px through <paramref name="points"/>
    /// (px, y up), drawn from its first point for <paramref name="drawn"/> (0–1)
    /// of its length: a quad per segment, a disc on every vertex reached and on
    /// the moving head, so joins never gap and both ends are round.
    /// </summary>
    public static void BuildLine(IList<Vector2> points, float width, float drawn, float feather, Color32 color, Mesh2D mesh)
    {
        mesh.Clear();
        int n = points != null ? points.Count : 0;
        if (n == 0 || width <= 0f || drawn <= 0f) return;

        // The drawn part of the path, cut by length.
        float total = 0f;
        for (int i = 1; i < n; i++) total += Vector2.Distance(points[i - 1], points[i]);
        float left = Mathf.Clamp01(drawn) * total;
        var path = new List<Vector2>(n) { points[0] };
        for (int i = 1; i < n; i++)
        {
            float len = Vector2.Distance(points[i - 1], points[i]);
            if (drawn >= 1f || len <= left)
            {
                path.Add(points[i]);
                left -= len;
                continue;
            }
            if (len > 1e-5f && left > 0f) path.Add(Vector2.Lerp(points[i - 1], points[i], left / len));
            break;
        }

        float half = width * 0.5f;
        bool soft = feather > 0f;
        Color32 clear = color;
        clear.a = 0;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1], b = path[i], d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) continue;
            Vector2 side = new Vector2(-d.y, d.x) / len;
            int a1 = mesh.Add(a + side * half, color), a2 = mesh.Add(a - side * half, color);
            int b1 = mesh.Add(b + side * half, color), b2 = mesh.Add(b - side * half, color);
            mesh.Quad(a1, a2, b2, b1);
            if (!soft) continue;
            int a0 = mesh.Add(a + side * (half + feather), clear), a3 = mesh.Add(a - side * (half + feather), clear);
            int b0 = mesh.Add(b + side * (half + feather), clear), b3 = mesh.Add(b - side * (half + feather), clear);
            mesh.Quad(a0, a1, b1, b0);
            mesh.Quad(a2, a3, b3, b2);
        }
        for (int i = 0; i < path.Count; i++) AddDisc(mesh, path[i], half, feather, color);
    }

    static void AddDisc(Mesh2D mesh, Vector2 centre, float radius, float feather, Color32 color)
    {
        int hub = mesh.Add(centre, color);
        int rim = mesh.Vertices.Count;
        for (int j = 0; j < DiscSteps; j++)
        {
            float a = j * 2f * Mathf.PI / DiscSteps;
            mesh.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color);
        }
        for (int j = 0; j < DiscSteps; j++) mesh.Triangle(hub, rim + j, rim + (j + 1) % DiscSteps);
        if (feather <= 0f) return;

        Color32 clear = color;
        clear.a = 0;
        int halo = mesh.Vertices.Count;
        for (int j = 0; j < DiscSteps; j++)
        {
            float a = j * 2f * Mathf.PI / DiscSteps;
            mesh.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + feather), clear);
        }
        for (int j = 0; j < DiscSteps; j++)
        {
            int next = (j + 1) % DiscSteps;
            mesh.Quad(rim + j, halo + j, halo + next, rim + next);
        }
    }

    /// <summary>An arc of the ring in one colour; turns clockwise from twelve o'clock.</summary>
    public struct ColoredArc
    {
        public float Start, End;
        public Color Color;

        public ColoredArc(float start, float end, Color color) { Start = start; End = end; Color = color; }
    }

    /// <summary>
    /// What to draw of <paramref name="segments"/> when the sequence is
    /// <paramref name="fill"/> (0–1, by angle) full: each segment's filled part
    /// in its own colour and — when <paramref name="unfilled"/> isn't clear —
    /// the rest of it in that colour. Every segment is drawn
    /// <paramref name="gapTurns"/> short (half at each end), which leaves the
    /// hairline between neighbours.
    /// </summary>
    public static List<ColoredArc> DrawnArcs(IList<ColoredArc> segments, float fill, float gapTurns, Color unfilled)
    {
        var arcs = new List<ColoredArc>();
        int n = segments != null ? segments.Count : 0;
        float total = 0f;
        for (int i = 0; i < n; i++) total += Mathf.Max(0f, segments[i].End - segments[i].Start);
        float left = Mathf.Clamp01(fill) * total;
        float half = Mathf.Max(0f, gapTurns) * 0.5f;

        for (int i = 0; i < n; i++)
        {
            ColoredArc s = segments[i];
            float len = Mathf.Max(0f, s.End - s.Start);
            float filled = fill >= 1f ? len : Mathf.Min(len, left);
            left -= filled;

            float a = s.Start + half, b = s.End - half;
            if (b <= a) continue; // thinner than the hairline
            float cut = Mathf.Clamp(s.Start + filled, a, b);
            if (cut > a + 1e-6f) arcs.Add(new ColoredArc(a, cut, s.Color));
            if (unfilled.a > 0f && b > cut + 1e-6f) arcs.Add(new ColoredArc(cut, b, unfilled));
        }
        return arcs;
    }

    /// <summary>
    /// Ring arcs between <paramref name="innerRadius"/> and
    /// <paramref name="outerRadius"/> (px) round <paramref name="centre"/>
    /// (y up): <see cref="RingStepsPerTurn"/> steps a turn, at least two an arc.
    /// </summary>
    public static void BuildRing(IList<ColoredArc> arcs, Vector2 centre, float innerRadius, float outerRadius, float feather, Mesh2D mesh)
    {
        mesh.Clear();
        if (arcs == null || outerRadius <= 0f || outerRadius <= innerRadius) return;
        innerRadius = Mathf.Max(0f, innerRadius);
        bool soft = feather > 0f;
        float middle = (innerRadius + outerRadius) * 0.5f;
        float edge = soft ? feather / (2f * Mathf.PI * Mathf.Max(1f, middle)) : 0f; // the feather, in turns

        for (int k = 0; k < arcs.Count; k++)
        {
            ColoredArc arc = arcs[k];
            float span = arc.End - arc.Start;
            if (span <= 0f) continue;
            int steps = Mathf.Max(2, Mathf.CeilToInt(RingStepsPerTurn * span));
            Color32 color = arc.Color;

            int previous = -1;
            if (soft) previous = AddRingColumn(mesh, centre, arc.Start - edge, innerRadius, outerRadius, feather, color, false);
            for (int i = 0; i <= steps; i++)
            {
                int column = AddRingColumn(mesh, centre, arc.Start + span * i / steps, innerRadius, outerRadius, feather, color, true);
                if (previous >= 0) JoinRingColumns(mesh, previous, column, soft);
                previous = column;
            }
            if (soft)
            {
                int column = AddRingColumn(mesh, centre, arc.End + edge, innerRadius, outerRadius, feather, color, false);
                JoinRingColumns(mesh, previous, column, true);
            }
        }
    }

    // One radial column of the ring: [inner − feather, clear] inner, outer [outer + feather, clear].
    // Returns its first vertex.
    static int AddRingColumn(Mesh2D mesh, Vector2 centre, float turn, float inner, float outer, float feather, Color32 color, bool solid)
    {
        float a = turn * 2f * Mathf.PI;
        var direction = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); // clockwise from twelve o'clock
        Color32 clear = color;
        clear.a = 0;
        Color32 body = solid ? color : clear;
        int first = mesh.Vertices.Count;
        if (feather > 0f) mesh.Add(centre + direction * Mathf.Max(0f, inner - feather), clear);
        mesh.Add(centre + direction * inner, body);
        mesh.Add(centre + direction * outer, body);
        if (feather > 0f) mesh.Add(centre + direction * (outer + feather), clear);
        return first;
    }

    static void JoinRingColumns(Mesh2D mesh, int a, int b, bool soft)
    {
        int rows = soft ? 4 : 2;
        for (int r = 0; r + 1 < rows; r++) mesh.Quad(a + r, a + r + 1, b + r + 1, b + r);
    }
}

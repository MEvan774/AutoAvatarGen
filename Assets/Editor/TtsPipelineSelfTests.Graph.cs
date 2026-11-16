using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// {Graph:…} — the data field, the three charts' honest scales, the meshes
/// and the timing (pure: these run with no engine), then the real card laid
/// out with TMP at each kind's smallest and largest point count, the text-row
/// fallback, and the chart stepped frame by frame.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Graph Self-Tests")]
    public static void RunGraph()
    {
        var log = new Log();
        RunGraphGroup(log);
        log.Report();
    }

    static void RunGraphGroup(Log log)
    {
        Graph_ParsePointCounts(log);
        Graph_ParseValues(log);
        Graph_ParseKeepsTextAsWritten(log);
        Graph_ParseProblems(log);
        Graph_CroppedIsLineOnly(log);
        Graph_BarFractions(log);
        Graph_BarPlacement(log);
        Graph_LineAxis(log);
        Graph_LineLabels(log);
        Graph_RingSegments(log);
        Graph_RingColours(log);
        Graph_Timing(log);
        Graph_FrameAndSizes(log);
        Graph_LineMesh(log);
        Graph_RingMesh(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Graph_BarsLayout(log);
            Graph_BarsGrowOneAfterAnother(log);
            Graph_LineLayout(log);
            Graph_ShareLayout(log);
            Graph_FallbackLayout(log);
            Graph_MissingFields(log);
            Graph_RelayoutOnFrameChange(log);
            Graph_HideClosesTheDim(log);
        }
        catch (Exception e)
        {
            // TMP needs the engine — offline harnesses land here.
            log.IsTrue(false, $"Graph layout checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // Pure: the data field
    // -----------------------------------------------------------------------

    // "L1=v;L2=v;…" with n points.
    static string GraphSeries(int n, Func<int, string> value)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append('L').Append(i + 1).Append('=').Append(value(i));
        }
        return sb.ToString();
    }

    static readonly Color GraphSlate = new Color(0x64 / 255f, 0x74 / 255f, 0x8B / 255f, 1f);   // CompareCard.Slate, without loading a MonoBehaviour offline
    static readonly Color GraphCoral = new Color(0xE8 / 255f, 0x5D / 255f, 0x4A / 255f, 1f);

    static void Graph_ParsePointCounts(Log log)
    {
        var kinds = new[]
        {
            (kind: GraphKind.Bars, min: GraphMath.MinBars, max: GraphMath.MaxBars),
            (kind: GraphKind.Line, min: GraphMath.MinLine, max: GraphMath.MaxLine),
            (kind: GraphKind.Share, min: GraphMath.MinShare, max: GraphMath.MaxShare),
        };
        foreach (var k in kinds)
        {
            // Shares of 10 % each never pass 100 %.
            Func<int, string> value = i => k.kind == GraphKind.Share ? "10%" : "$" + (i + 1) + "B";

            GraphMath.Data lo = GraphMath.Parse(GraphSeries(k.min, value), k.kind, false);
            log.IsTrue(lo.Ok && lo.Problem == null && lo.Points.Count == k.min && lo.Kind == k.kind,
                $"{k.kind}: {k.min} points draw a chart ({lo.Problem})");
            GraphMath.Data hi = GraphMath.Parse(GraphSeries(k.max, value), k.kind, false);
            log.IsTrue(hi.Ok && hi.Points.Count == k.max, $"{k.kind}: {k.max} points draw a chart ({hi.Problem})");

            GraphMath.Data over = GraphMath.Parse(GraphSeries(k.max + 1, value), k.kind, false);
            log.IsTrue(!over.Ok, $"{k.kind}: {k.max + 1} points draw no chart");
            log.AreEqual($"{k.kind} takes {k.min} to {k.max} points (got {k.max + 1})", over.Problem, $"{k.kind}: one over the maximum says so");
            log.AreEqual(Mathf.Min(k.max + 1, GraphMath.MaxFallbackRows), over.Points.Count,
                $"{k.kind}: …and the points are still all there for the rows");

            GraphMath.Data under = GraphMath.Parse(GraphSeries(k.min - 1, value), k.kind, false);
            log.AreEqual($"{k.kind} takes {k.min} to {k.max} points (got {k.min - 1})", under.Problem, $"{k.kind}: one under the minimum says so");

            log.IsTrue(hi.Points[0].Label == "L1" && hi.Points[k.max - 1].Label == "L" + k.max, $"{k.kind}: points stay in script order");
            GraphMath.PointRange(k.kind, out int min, out int max);
            log.IsTrue(min == k.min && max == k.max, $"{k.kind}: PointRange is {k.min}–{k.max}");
        }

        log.IsTrue(GraphMath.MinBars == 2 && GraphMath.MaxBars == 6 && GraphMath.MinLine == 3 && GraphMath.MaxLine == 12 &&
                   GraphMath.MinShare == 2 && GraphMath.MaxShare == 5, "the brief's point counts: Bars 2–6, Line 3–12, Share 2–5");

        // More than twelve points: twelve rows, the rest dropped and noted.
        GraphMath.Data many = GraphMath.Parse(GraphSeries(15, i => (i + 1).ToString()), GraphKind.Line, false);
        log.IsTrue(!many.Ok && many.Points.Count == GraphMath.MaxFallbackRows, "15 points: only twelve rows are kept");
        log.AreEqual("Line takes 3 to 12 points (got 15)", many.Problem, "15 points: the problem counts all fifteen");
        log.IsTrue(many.Notes.Exists(s => s.Contains("15 points") && s.Contains("first 12")), $"15 points: the dropped rows are noted ({string.Join(" | ", many.Notes)})");
        log.IsTrue(GraphMath.Parse(GraphSeries(12, i => (i + 1).ToString()), GraphKind.Line, false).Notes.Count == 0, "12 points: nothing dropped, no note");
    }

    static void Graph_ParseValues(Log log)
    {
        void Is(string text, double amount, char cur, CompareMath.Unit unit, bool plus = false)
        {
            CompareMath.Value v = GraphMath.ParseValue(text);
            log.IsTrue(v.Ok && Math.Abs(v.Amount - amount) < amount * 1e-9 + 1e-9 && v.Currency == cur && v.Unit == unit && v.Plus == plus,
                $"\"{text}\" parses as {amount:G} {(cur == '\0' ? "" : cur.ToString())} {unit}{(plus ? " +" : "")} (got {(v.Ok ? $"{v.Amount:G} {v.Currency} {v.Unit} {v.Plus}" : v.Error)})");
        }
        Is("<1%", 1, '\0', CompareMath.Unit.Percent);
        Is("$6B+", 6e9, '$', CompareMath.Unit.Plain, plus: true);
        Is("~2.4M", 2.4e6, '\0', CompareMath.Unit.Plain);
        Is(">500", 500, '\0', CompareMath.Unit.Plain);
        Is("~$1.5B", 1.5e9, '$', CompareMath.Unit.Plain);
        Is("<€360,000", 360000, '€', CompareMath.Unit.Plain);
        Is(">4x", 4, '\0', CompareMath.Unit.Multiple);
        Is(" < 1% ", 1, '\0', CompareMath.Unit.Percent);
        foreach (string bad in new[] { "<<1%", "<", "~", "1%<", "≈5", "-5%", "five", "", "$ 6B", "50°C" })
            log.IsTrue(!GraphMath.ParseValue(bad).Ok, $"\"{bad}\" isn't a value a chart can scale");

        // Without a prefix the result is Compare's, field for field.
        foreach (string s in new[] { "$6B+", "$1.5B", "€360,000", "€360K", "95%", "4x", "4×", "1.567B", "12,345.67", "6b", "£2.5T", "¥800M",
                                     "50°C", "1,5", "$", "", "six", "1,23,456", "0" })
        {
            CompareMath.Value a = CompareMath.Parse(s), b = GraphMath.ParseValue(s);
            log.IsTrue(a.Ok == b.Ok && a.Amount == b.Amount && a.Currency == b.Currency && a.Unit == b.Unit && a.Plus == b.Plus,
                $"\"{s}\" parses exactly as Compare parses it");
        }

        // In a data field: the prefix is kept in the text shown, ignored for the number.
        GraphMath.Data d = GraphMath.Parse("Before=<1%;After=~24%;Peak=>60%", GraphKind.Bars, false);
        log.IsTrue(d.Ok, $"\"<1%\", \"~24%\" and \">60%\" are one kind and draw bars ({d.Problem})");
        log.IsTrue(d.Points.Count == 3 && d.Points[0].ValueText == "<1%" && d.Points[1].ValueText == "~24%" && d.Points[2].ValueText == ">60%",
            "…shown with their prefix");
        log.IsTrue(d.Points.Count == 3 && d.Points[0].Value == 1.0 && d.Points[1].Value == 24.0 && d.Points[2].Value == 60.0 && d.Points[0].Parsed,
            "…scaled without it");
        GraphMath.Data plus = GraphMath.Parse("Binance=$6B+;Bybit=$1.5B", GraphKind.Bars, false);
        log.IsTrue(plus.Ok && plus.Points[0].ValueText == "$6B+" && plus.Points[0].Value == 6e9, "\"$6B+\" keeps its + on screen and scales as 6 billion");
        GraphMath.Data approx = GraphMath.Parse("Jan=2.1M;Feb=~2.4M;Mar=3.9M", GraphKind.Line, false);
        log.IsTrue(approx.Ok && approx.Points[1].ValueText == "~2.4M" && approx.Points[1].Value == 2.4e6, "\"~2.4M\" scales as 2.4 million");
    }

    static void Graph_ParseKeepsTextAsWritten(Log log)
    {
        GraphMath.Data d = GraphMath.Parse("Apple, Inc. stores=40%;Third-party sellers = <1% ; Résumé & co.=~2.4%", GraphKind.Bars, false);
        log.IsTrue(d.Ok, $"labels with spaces, commas and accents draw a chart ({d.Problem})");
        log.AreEqual(3, d.Points.Count, "three points");
        if (d.Points.Count == 3)
        {
            log.AreEqual("Apple, Inc. stores", d.Points[0].Label, "a label with a space and a comma is kept whole");
            log.AreEqual("40%", d.Points[0].ValueText, "…and its value as written");
            log.AreEqual("Third-party sellers", d.Points[1].Label, "spaces round the '=' and the ';' aren't part of the text");
            log.AreEqual("<1%", d.Points[1].ValueText, "\"<1%\" is shown as written");
            log.AreEqual("Résumé & co.", d.Points[2].Label, "accents and symbols are kept");
            log.AreEqual("~2.4%", d.Points[2].ValueText, "\"~2.4%\" is shown as written");
        }

        // Text is never rewritten: no upper-casing, no reformatted numbers, no sorting.
        GraphMath.Data e = GraphMath.Parse("zebra=€1,234,567;Alpha=€360K;mIxEd=€2.50M", GraphKind.Bars, false);
        var shown = new List<string>();
        foreach (GraphMath.Point p in e.Points) shown.Add(p.Label + "=" + p.ValueText);
        log.AreEqual("zebra=€1,234,567;Alpha=€360K;mIxEd=€2.50M", string.Join(";", shown), "labels and values come back exactly as written, in script order");

        // The first '=' splits a point.
        GraphMath.Data f = GraphMath.Parse("a=b=1;c=2", GraphKind.Bars, false);
        log.IsTrue(f.Points.Count == 2 && f.Points[0].Label == "a" && f.Points[0].ValueText == "b=1" && !f.Points[0].Parsed && !f.Ok,
            "a point is split at its first '=' (the rest is the value — here not a number)");
    }

    static void Graph_ParseProblems(Log log)
    {
        GraphMath.Data mixed = GraphMath.Parse("Fine=$360K;Share=40%", GraphKind.Bars, false);
        log.IsTrue(!mixed.Ok && mixed.Points.Count == 2, "mixed kinds: no chart, both points kept for the rows");
        log.AreEqual("the values aren't the same kind ($ and %)", mixed.Problem, "mixed kinds: the problem names both");
        log.AreEqual("the values aren't the same kind (€ and $)", GraphMath.Parse("A=€360K;B=$1.5B", GraphKind.Bars, false).Problem, "two currencies don't share a scale");
        log.AreEqual("the values aren't the same kind (% and plain numbers)", GraphMath.Parse("A=40%;B=12;C=3", GraphKind.Line, false).Problem, "% and plain numbers don't either");
        log.AreEqual("the values aren't the same kind (x and %)", GraphMath.Parse("A=4x;B=12%", GraphKind.Bars, false).Problem, "nor multiples and %");

        GraphMath.Data nan = GraphMath.Parse("Inside=50°C;Outside=40", GraphKind.Bars, false);
        log.IsTrue(!nan.Ok && nan.Problem == "\"50°C\" isn't a number (the value of \"Inside\")", $"a value that isn't a number is named ({nan.Problem})");
        log.IsTrue(nan.Points.Count == 2 && !nan.Points[0].Parsed && nan.Points[1].Parsed && nan.Points[0].ValueText == "50°C",
            "…and still listed, as written");

        GraphMath.Data noEq = GraphMath.Parse("A=5;just words;C=7", GraphKind.Bars, false);
        log.IsTrue(!noEq.Ok && noEq.Problem.Contains("\"just words\" has no \"=\""), $"a point with no '=' is a problem ({noEq.Problem})");
        log.IsTrue(noEq.Points.Count == 2 && noEq.Points[0].Label == "A" && noEq.Points[1].Label == "C", "…and the pairs that did parse make the rows");

        GraphMath.Data noLabel = GraphMath.Parse("=5;B=6;C=7", GraphKind.Bars, false);
        log.IsTrue(!noLabel.Ok && noLabel.Problem.Contains("no label") && noLabel.Points.Count == 2, $"an empty label is a problem ({noLabel.Problem})");
        GraphMath.Data noValue = GraphMath.Parse("A=;B=6;C=7", GraphKind.Bars, false);
        log.IsTrue(!noValue.Ok && noValue.Problem.Contains("\"A\" has no value") && noValue.Points.Count == 2, $"an empty value is a problem ({noValue.Problem})");

        foreach (string empty in new[] { "", null, "   ", ";;", " ; " })
        {
            GraphMath.Data none = GraphMath.Parse(empty, GraphKind.Share, true);
            log.IsTrue(!none.Ok && none.Points.Count == 0 && !string.IsNullOrEmpty(none.Problem) && !none.Cropped,
                $"an empty data field ({(empty == null ? "null" : "\"" + empty + "\"")}): no points, a problem ({none.Problem})");
        }
        GraphMath.Data nothing = GraphMath.Parse("no pairs here at all", GraphKind.Bars, false);
        log.IsTrue(!nothing.Ok && nothing.Points.Count == 0, "no pair can be read: Points is empty (the card shows title and source only)");

        log.IsTrue(GraphMath.Parse("A=5;;B=6;", GraphKind.Bars, false).Ok, "an empty piece between two ';' (or a ';' at the end) is skipped, not a problem");
        log.AreEqual("every value is zero", GraphMath.Parse("A=0;B=0", GraphKind.Bars, false).Problem, "all zeros: nothing to scale");
        GraphMath.Data notShares = GraphMath.Parse("A=5;B=6", GraphKind.Share, false);
        log.IsTrue(!notShares.Ok && notShares.Problem.StartsWith("Share takes percentages"), $"Share needs % values ({notShares.Problem})");

        // No problem ends with a full stop (the integrator adds its own punctuation).
        foreach (GraphMath.Data d in new[] { mixed, nan, noEq, noLabel, noValue, nothing, notShares, GraphMath.Parse("", GraphKind.Bars, false),
                                             GraphMath.Parse("A=60%;B=44%", GraphKind.Share, false), GraphMath.Parse("A=1", GraphKind.Bars, false) })
            log.IsTrue(d.Problem != null && !d.Problem.EndsWith(".") && !d.Problem.Contains("\n"), $"a problem is one sentence without a full stop ({d.Problem})");
    }

    static void Graph_CroppedIsLineOnly(Log log)
    {
        const string trend = "Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M";

        GraphMath.Data line = GraphMath.Parse(trend, GraphKind.Line, true);
        log.IsTrue(line.Ok && line.Cropped && line.Notes.Count == 0, $"Line keeps ,Cropped ({string.Join(" | ", line.Notes)})");
        GraphMath.Data plain = GraphMath.Parse(trend, GraphKind.Line, false);
        log.IsTrue(plain.Ok && !plain.Cropped && plain.Notes.Count == 0, "without the keyword the axis is never cropped");

        GraphMath.Data bars = GraphMath.Parse(trend, GraphKind.Bars, true);
        log.IsTrue(bars.Ok && !bars.Cropped, "Bars ignores ,Cropped");
        log.IsTrue(bars.Notes.Contains(",Cropped is ignored on Bars"), $"…and notes it ({string.Join(" | ", bars.Notes)})");
        GraphMath.Data share = GraphMath.Parse("A=60%;B=30%", GraphKind.Share, true);
        log.IsTrue(share.Ok && !share.Cropped, "Share ignores ,Cropped");
        log.IsTrue(share.Notes.Contains(",Cropped is ignored on Share"), $"…and notes it ({string.Join(" | ", share.Notes)})");

        // Cropping that can't lift the axis off zero isn't cropping: no "AXIS DOES NOT START AT ZERO".
        GraphMath.Data toZero = GraphMath.Parse("A=0;B=5;C=10", GraphKind.Line, true);
        log.IsTrue(toZero.Ok && !toZero.Cropped && toZero.Notes.Exists(s => s.Contains("Cropped")),
            "values reaching zero keep a zero axis even with ,Cropped (noted)");
        GraphMath.Data near = GraphMath.Parse("A=1;B=50;C=100", GraphKind.Line, true);
        log.IsTrue(near.Ok && !near.Cropped, "…and so do values whose 10 % margin reaches under zero");
        GraphMath.Data flat = GraphMath.Parse("A=5;B=5;C=5", GraphKind.Line, true);
        log.IsTrue(flat.Ok && !flat.Cropped, "…and a flat line (no range to crop to)");
        GraphMath.Data broken = GraphMath.Parse("A=5;B=6", GraphKind.Line, true);
        log.IsTrue(!broken.Ok && !broken.Cropped, "no chart, no cropped axis");
    }

    // -----------------------------------------------------------------------
    // Pure: bars
    // -----------------------------------------------------------------------

    static void Graph_BarFractions(Log log)
    {
        GraphMath.Data d = GraphMath.Parse("Big=$100B;Half=$50B;Tiny=$1B", GraphKind.Bars, false);
        float[] f = GraphMath.BarFractions(d.Points, out bool[] sliver);
        log.Near(1f, f[0], 1e-6f, "the largest value fills the track");
        log.Near(0.5f, f[1], 1e-6f, "half the value is half the bar (linear from zero)");
        log.Near(GraphMath.MinFraction, f[2], 1e-6f, "100:1 — the 1 % bar is drawn as a 3 % sliver");
        log.IsTrue(!sliver[0] && !sliver[1] && sliver[2], "…and only that one is flagged");
        log.IsTrue(d.Ok && d.Notes.Count == 1 && d.Notes[0] == "\"Tiny\" is under 3 % of the scale — drawn as a 3 % sliver",
            $"…and noted by name ({string.Join(" | ", d.Notes)})");
        log.Near(0.03f, GraphMath.MinFraction, 1e-6f, "the sliver is Compare's 3 %");

        // Script order, never sorted: the first point can be the smallest.
        float[] g = GraphMath.BarFractions(GraphMath.Parse("A=1;B=4;C=2", GraphKind.Bars, false).Points, out _);
        log.IsTrue(Mathf.Approximately(g[0], 0.25f) && Mathf.Approximately(g[1], 1f) && Mathf.Approximately(g[2], 0.5f), "fractions stay in script order");

        // Magnitudes are applied before the ratio.
        float[] h = GraphMath.BarFractions(GraphMath.Parse("A=$1.5B;B=$750M", GraphKind.Bars, false).Points, out _);
        log.Near(0.5f, h[1], 1e-6f, "$750M is half of $1.5B");

        // Same numbers as Compare for two values.
        CompareMath.Bars c = CompareMath.Ratios("$6B+", "$1.5B");
        float[] same = GraphMath.BarFractions(GraphMath.Parse("A=$6B+;B=$1.5B", GraphKind.Bars, false).Points, out _);
        log.IsTrue(Mathf.Approximately(c.A, same[0]) && Mathf.Approximately(c.B, same[1]), "two bars get exactly Compare's ratios");

        // A zero among real values is a sliver too (as in Compare).
        GraphMath.Data z = GraphMath.Parse("A=10;B=0", GraphKind.Bars, false);
        float[] zf = GraphMath.BarFractions(z.Points, out bool[] zs);
        log.IsTrue(z.Ok && Mathf.Approximately(zf[1], GraphMath.MinFraction) && zs[1], "a zero beside real values is drawn as the 3 % sliver");

        log.Near(128f, GraphMath.BarHeight(2, 700f, 1f), 1e-3f, "two bars: the bar stops at its 128 px ceiling");
        float six = GraphMath.BarHeight(6, 660f, 1f);
        log.Near(660f, GraphMath.BarsHeight(6, six), 0.01f, "six bars: bars and gaps fill the height they are given");
        log.Near(GraphMath.BarMinHeightPx, GraphMath.BarHeight(6, 100f, 1f), 1e-3f, "…but a bar never goes under its floor");
        log.Near(GraphMath.BarsHeight(3, 100f) - 100f, GraphMath.BarTop(2, 100f), 1e-3f, "the last bar ends at the chart's height");
        log.Near(256f, GraphMath.BarHeight(2, 4000f, 2f), 1e-3f, "a 4K frame gets twice the bar");
    }

    static void Graph_BarPlacement(Log log)
    {
        // Two bars: Compare's placement, number for number.
        var cases = new[]
        {
            (row: 860f, a: 1f, b: 0.25f, wa: 300f, wb: 300f),
            (row: 860f, a: 1f, b: 0.5f, wa: 300f, wb: 520f),
            (row: 860f, a: 1f, b: 1f, wa: 300f, wb: 300f),
            (row: 860f, a: 1f, b: 0.5f, wa: 900f, wb: 300f),
            (row: 1200f, a: 0.03f, b: 1f, wa: 260f, wb: 310f),
        };
        foreach (var c in cases)
        {
            CompareMath.Placement want = CompareMath.Place(c.row, c.a, c.b, c.wa, c.wb, 21f, 21f);
            GraphMath.BarPlacement got = GraphMath.PlaceBars(c.row, new[] { c.a, c.b }, new[] { c.wa, c.wb }, 21f, 21f);
            log.IsTrue(Mathf.Abs(want.Track - got.Track) < 0.01f && want.InsideA == got.Inside[0] && want.InsideB == got.Inside[1] && want.Overflows == got.Overflows,
                $"two bars ({c.a}/{c.b}, values {c.wa}/{c.wb} px) place exactly as Compare places them (track {got.Track:F0} vs {want.Track:F0})");
        }

        // Six bars: long ones hold their value, short ones put it just past their end.
        float[] f = { 1f, 0.8f, 0.5f, 0.3f, 0.1f, 0.03f };
        float[] w = { 200f, 200f, 200f, 200f, 200f, 200f };
        GraphMath.BarPlacement p = GraphMath.PlaceBars(1000f, f, w, 20f, 20f);
        log.IsTrue(p.Inside[0] && p.Inside[1] && p.Inside[2] && p.Inside[3] && !p.Inside[4] && !p.Inside[5],
            "six bars: a value sits inside every bar long enough for it");
        log.IsTrue(Mathf.Approximately(p.Track, 1000f) && !p.Overflows, "…and the track keeps the whole row when nothing sticks out");

        // An outside value that would leave the row shrinks the track for every bar.
        GraphMath.BarPlacement q = GraphMath.PlaceBars(1000f, new[] { 1f, 0.9f, 0.2f }, new[] { 100f, 880f, 100f }, 20f, 20f);
        log.IsTrue(!q.Overflows && q.Track < 1000f && !q.Inside[1] && 0.9f * q.Track + 20f + 880f <= 1000.5f,
            $"a wide value past a long bar shrinks the track so it stays in the row ({q.Track:F0} px)");
        GraphMath.BarPlacement r = GraphMath.PlaceBars(1000f, new[] { 1f, 0.5f }, new[] { 100f, 1100f }, 20f, 20f);
        log.IsTrue(r.Overflows, "a value wider than the row is flagged");
        GraphMath.BarPlacement none = GraphMath.PlaceBars(1000f, new float[0], new float[0], 20f, 20f);
        log.IsTrue(none.Inside.Length == 0 && !none.Overflows, "no bars: nothing to place");
    }

    // -----------------------------------------------------------------------
    // Pure: line
    // -----------------------------------------------------------------------

    static void Graph_LineAxis(Log log)
    {
        GraphMath.Data d = GraphMath.Parse("Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M", GraphKind.Line, false);

        // Zero axis: every height is the value's true share of the largest.
        GraphMath.Axis zero = GraphMath.LineAxis(d.Points, false);
        log.IsTrue(zero.Min == 0.0 && zero.Max == 4.0e6 && !zero.Cropped, "without ,Cropped the axis starts at zero");
        Vector2[] z = GraphMath.LinePoints(d.Points, zero);
        log.Near(2.1f / 4.0f, z[0].y, 1e-5f, "zero axis: 2.1M sits at 52.5 % of 4.0M");
        log.Near(2.4f / 4.0f, z[1].y, 1e-5f, "zero axis: 2.4M at 60 %");
        log.Near(1f, z[3].y, 1e-6f, "zero axis: the largest value is the top");
        log.Near(0f, GraphMath.LineY(0.0, zero), 1e-6f, "zero axis: zero is on the axis line");
        log.IsTrue(Mathf.Approximately(z[0].x, 0f) && Mathf.Approximately(z[1].x, 1f / 3f) && Mathf.Approximately(z[2].x, 2f / 3f) && Mathf.Approximately(z[3].x, 1f),
            "the points are evenly spaced in script order");

        // Cropped: the axis starts 10 % of the range under the smallest value.
        GraphMath.Axis crop = GraphMath.LineAxis(d.Points, true);
        log.IsTrue(crop.Cropped && Math.Abs(crop.Min - (2.1e6 - 0.1 * 1.9e6)) < 1.0 && crop.Max == 4.0e6,
            $"cropped: the axis starts at min − 10 % of the range ({crop.Min:F0})");
        Vector2[] c = GraphMath.LinePoints(d.Points, crop);
        log.Near(0.1f / 1.1f, c[0].y, 1e-5f, "cropped: the smallest value sits near the bottom (9 % up)");
        log.Near(1f, c[3].y, 1e-6f, "cropped: the largest value is still the top");
        bool inRange = true, ordered = true;
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i].y < 0f || c[i].y > 1f || z[i].y < 0f || z[i].y > 1f) inRange = false;
            if (i > 0 && (c[i].y <= c[i - 1].y || z[i].y <= z[i - 1].y)) ordered = false;
        }
        log.IsTrue(inRange, "every height is within 0–1 on either axis");
        log.IsTrue(ordered, "a rising series rises on either axis (order preserved)");
        log.IsTrue(c[1].y - c[0].y > z[1].y - z[0].y, "cropping stretches the differences — which is why the card must say so");

        // Never below zero; never without a range.
        GraphMath.Data low = GraphMath.Parse("A=1;B=50;C=100", GraphKind.Line, true);
        log.IsTrue(!GraphMath.LineAxis(low.Points, true).Cropped && GraphMath.LineAxis(low.Points, true).Min == 0.0, "a crop that would start under zero starts at zero");
        GraphMath.Data flat = GraphMath.Parse("A=5;B=5;C=5", GraphKind.Line, true);
        GraphMath.Axis fa = GraphMath.LineAxis(flat.Points, true);
        log.IsTrue(!fa.Cropped && Mathf.Approximately(GraphMath.LineY(5.0, fa), 1f), "a flat series sits at the top of a zero axis");

        // A falling series keeps its order too.
        GraphMath.Data fall = GraphMath.Parse("Q1=90%;Q2=70%;Q3=65%", GraphKind.Line, true);
        Vector2[] fp = GraphMath.LinePoints(fall.Points, GraphMath.LineAxis(fall.Points, true));
        log.IsTrue(fall.Cropped && fp[0].y > fp[1].y && fp[1].y > fp[2].y && fp[2].y > 0f && Mathf.Approximately(fp[0].y, 1f), "a falling series falls; its last point stays above the axis");

        // The shares of the line's length the dots are timed by.
        float[] len = GraphMath.LengthFractions(new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 1f) }, new Vector2(200f, 100f));
        log.IsTrue(len.Length == 3 && len[0] == 0f && Mathf.Abs(len[1] - 100f / (100f + 141.4214f)) < 1e-4f && Mathf.Approximately(len[2], 1f),
            "length fractions are measured in px (a 100 px run, then a 141 px diagonal)");
    }

    static void Graph_LineLabels(Log log)
    {
        float[] xs = GraphMath.LineXs(4, 1000f, 100f);
        log.IsTrue(Mathf.Approximately(xs[0], 100f) && Mathf.Approximately(xs[3], 900f) &&
                   Mathf.Approximately(xs[1] - xs[0], xs[2] - xs[1]) && Mathf.Approximately(xs[2] - xs[1], xs[3] - xs[2]),
            "points run from the inset to the inset, evenly spaced");
        log.Near(500f, GraphMath.LineXs(1, 1000f, 100f)[0], 1e-3f, "a single point sits in the middle");

        float[] lefts = GraphMath.CentredLefts(xs, new[] { 80f, 80f, 80f, 80f }, 1000f);
        log.IsTrue(Mathf.Approximately(lefts[0], 60f) && Mathf.Approximately(lefts[3], 860f), "a label is centred under its point");
        float[] wide = GraphMath.CentredLefts(xs, new[] { 300f, 80f, 80f, 300f }, 1000f);
        log.IsTrue(Mathf.Approximately(wide[0], 0f) && Mathf.Approximately(wide[3], 700f), "…and pushed in when it would leave the chart");

        log.IsTrue(GraphMath.LabelsClear(lefts, new[] { 80f, 80f, 80f, 80f }, 20f, 1), "80 px labels 267 px apart sit side by side");
        float[] w260 = { 260f, 260f, 260f, 260f };
        float[] l260 = GraphMath.CentredLefts(xs, w260, 1000f);
        log.IsTrue(!GraphMath.LabelsClear(l260, w260, 20f, 1), "260 px labels 267 px apart are too close on one row");
        log.IsTrue(GraphMath.LabelsClear(l260, w260, 20f, 2), "…and clear each other staggered on two rows");
        float[] w600 = { 600f, 600f, 600f, 600f };
        log.IsTrue(!GraphMath.LabelsClear(GraphMath.CentredLefts(xs, w600, 1000f), w600, 20f, 2), "600 px labels overlap even staggered");

        // Twelve points across the card's column: what fits at 32 px side by side, and what needs two rows.
        float[] x12 = GraphMath.LineXs(12, 1560f, 180f);
        var w = new float[12];
        for (int i = 0; i < 12; i++) w[i] = 70f;
        log.IsTrue(GraphMath.LabelsClear(GraphMath.CentredLefts(x12, w, 1560f), w, 20f, 1), "twelve 70 px labels (\"Jan\") fit on one row");
        for (int i = 0; i < 12; i++) w[i] = 150f;
        log.IsTrue(!GraphMath.LabelsClear(GraphMath.CentredLefts(x12, w, 1560f), w, 20f, 1) &&
                   GraphMath.LabelsClear(GraphMath.CentredLefts(x12, w, 1560f), w, 20f, 2), "twelve 150 px labels (\"Q1 2023\") need the second row — and fit on it");
    }

    // -----------------------------------------------------------------------
    // Pure: share
    // -----------------------------------------------------------------------

    static void Graph_RingSegments(Log log)
    {
        // 86 %: three segments and an unlabelled remainder.
        GraphMath.Data d = GraphMath.Parse("A=69%;B=16%;C=1%", GraphKind.Share, false);
        log.IsTrue(d.Ok, $"shares adding up to 86 % draw a ring ({d.Problem})");
        List<GraphMath.RingSegment> s = GraphMath.RingSegments(d.Points);
        log.AreEqual(4, s.Count, "86 %: three segments and what is left");
        if (s.Count == 4)
        {
            log.Near(0f, s[0].Start, 1e-6f, "the ring starts at twelve o'clock");
            log.Near(0.69f, s[0].End - s[0].Start, 1e-5f, "69 % is 0.69 of a turn");
            log.Near(0.16f, s[1].End - s[1].Start, 1e-5f, "16 % is 0.16 of a turn");
            log.Near(GraphMath.MinFraction, s[2].End - s[2].Start, 1e-5f, "1 % is drawn as a 3 % sliver");
            log.IsTrue(s[2].Sliver && !s[0].Sliver && !s[1].Sliver, "…and flagged");
            log.IsTrue(s[3].Point == -1 && Mathf.Approximately(s[3].End, 1f), "the last segment is the unlabelled remainder, ending the turn");
            log.Near(1f - 0.69f - 0.16f - 0.03f, s[3].End - s[3].Start, 1e-5f, "…the sliver's extra room comes out of the remainder");
            bool clockwise = true;
            float sum = 0f;
            for (int i = 0; i < s.Count; i++)
            {
                if (s[i].End <= s[i].Start || (i > 0 && !Mathf.Approximately(s[i].Start, s[i - 1].End))) clockwise = false;
                if (i < 3 && s[i].Point != i) clockwise = false;
                sum += s[i].End - s[i].Start;
            }
            log.IsTrue(clockwise, "segments follow each other clockwise in script order, with no gap or overlap");
            log.Near(1f, sum, 1e-5f, "the segments and the remainder make one whole turn");
        }
        log.IsTrue(d.Notes.Count == 1 && d.Notes[0] == "\"C\" is under 3 % of the scale — drawn as a 3 % sliver", $"the sliver is noted ({string.Join(" | ", d.Notes)})");

        // 100 %: the ring closes, no remainder.
        GraphMath.Data full = GraphMath.Parse("Yes=60%;No=40%", GraphKind.Share, false);
        List<GraphMath.RingSegment> f = GraphMath.RingSegments(full.Points);
        log.IsTrue(full.Ok && f.Count == 2 && f[1].Point == 1 && f[1].End == 1f && Mathf.Abs(f[0].End - 0.6f) < 1e-5f, "100 %: two segments close the ring, no remainder");

        // Rounding: 99.7 % and 100.4 % are the whole too.
        List<GraphMath.RingSegment> r1 = GraphMath.RingSegments(GraphMath.Parse("A=33.3%;B=33.2%;C=33.2%", GraphKind.Share, false).Points);
        log.IsTrue(r1.Count == 3 && r1[2].End == 1f, "99.7 % closes the ring (no hairline remainder from rounding)");
        GraphMath.Data r2 = GraphMath.Parse("A=50.2%;B=50.2%", GraphKind.Share, false);
        List<GraphMath.RingSegment> r2s = GraphMath.RingSegments(r2.Points);
        log.IsTrue(r2.Ok && r2s.Count == 2 && Mathf.Abs(r2s[0].End - 0.5f) < 1e-5f && r2s[1].End == 1f, "100.4 % is rounding: still a ring, halves stay halves");
        List<GraphMath.RingSegment> r3 = GraphMath.RingSegments(GraphMath.Parse("A=50%;B=49%", GraphKind.Share, false).Points);
        log.IsTrue(r3.Count == 3 && Mathf.Abs((r3[2].End - r3[2].Start) - 0.01f) < 1e-5f, "99 % leaves a true 1 % remainder (it is not a data point: no sliver)");

        // 104 %: no chart.
        GraphMath.Data over = GraphMath.Parse("A=60%;B=44%", GraphKind.Share, false);
        log.IsTrue(!over.Ok && over.Points.Count == 2, "104 %: no ring, the points are listed");
        log.AreEqual("the shares add up to 104 %", over.Problem, "104 %: the problem says the sum");
        log.AreEqual("the shares add up to 100.6 %", GraphMath.Parse("A=60.3%;B=40.3%", GraphKind.Share, false).Problem, "100.6 % is past rounding");

        // A sliver with no remainder to take from: the other segments make room in proportion.
        List<GraphMath.RingSegment> tight = GraphMath.RingSegments(GraphMath.Parse("A=66%;B=33%;C=1%", GraphKind.Share, false).Points);
        log.IsTrue(tight.Count == 3 && tight[2].End == 1f, "100 % with a sliver still closes the ring");
        if (tight.Count == 3)
        {
            float a = tight[0].End - tight[0].Start, b = tight[1].End - tight[1].Start, c = tight[2].End - tight[2].Start;
            log.Near(GraphMath.MinFraction, c, 1e-5f, "…the sliver gets its 3 %");
            log.Near(2f, a / b, 1e-3f, "…and the others keep their ratio to each other (66 : 33)");
            log.Near(1f, a + b + c, 1e-5f, "…inside one turn");
        }
        // Even four slivers leave the big one most of the ring; nothing ever drops under 3 %.
        List<GraphMath.RingSegment> four = GraphMath.RingSegments(GraphMath.Parse("A=3%;B=0.5%;C=0.5%;D=0.5%;E=95.5%", GraphKind.Share, false).Points);
        bool floorKept = four.Count == 5;
        foreach (GraphMath.RingSegment g in four) if (g.End - g.Start < GraphMath.MinFraction - 1e-5f) floorKept = false;
        log.IsTrue(floorKept && four[4].End == 1f, "no segment is ever thinner than 3 %, however the room is found");
        log.AreEqual(0, GraphMath.RingSegments(new List<GraphMath.Point>()).Count, "no points, no segments");
    }

    static void Graph_RingColours(Log log)
    {
        log.IsTrue(GraphMath.SegmentColor(0, GraphCoral, GraphSlate) == GraphCoral, "the first point is the accent (the story's subject)");
        log.IsTrue(GraphMath.SegmentColor(1, GraphCoral, GraphSlate) == GraphSlate, "the second is Slate itself");
        float last = GraphSlate.grayscale;
        bool lighter = true;
        for (int i = 2; i < GraphMath.MaxShare; i++)
        {
            Color c = GraphMath.SegmentColor(i, GraphCoral, GraphSlate);
            if (c.grayscale <= last + 0.02f || c.a != 1f) lighter = false;
            last = c.grayscale;
        }
        log.IsTrue(lighter, "each further neutral is a visibly lighter, opaque tint of Slate");
        log.IsTrue(GraphMath.SegmentColor(GraphMath.MaxShare - 1, GraphCoral, GraphSlate) != Color.white, "…and the last of five still isn't white");
        Color rest = GraphMath.RemainderColor(GraphSlate);
        log.IsTrue(rest.a > 0.2f && rest.a < 0.7f && rest.grayscale < GraphSlate.grayscale * 0.5f, $"what is left to 100 % is dark and translucent ({rest})");
    }

    // -----------------------------------------------------------------------
    // Pure: timing
    // -----------------------------------------------------------------------

    static void Graph_Timing(Log log)
    {
        // Content and chart start.
        log.AreEqual(0f, GraphMath.ContentAlphaAt(0f), "the content starts invisible");
        log.Near(1f, GraphMath.ContentAlphaAt(GraphMath.ContentFadeInSeconds), 1e-6f, "…and is in after 0.25 s");
        log.IsTrue(GraphMath.ChartDelaySeconds >= GraphMath.ContentFadeInSeconds, "the chart starts once the card has landed (after the fade-in)");
        log.AreEqual(0f, GraphMath.FadeAt(-1f), "a value is hidden before its beat");
        log.Near(1f, GraphMath.FadeAt(GraphMath.FadeSeconds), 1e-6f, "…and in 0.2 s after it");

        // Bars: 0.5 s each, 0.15 s apart, easing out.
        log.Near(0.5f, GraphMath.BarGrowSeconds, 1e-6f, "a bar takes 0.5 s");
        log.Near(0.15f, GraphMath.BarStart(1) - GraphMath.BarStart(0), 1e-6f, "bars start 0.15 s apart");
        log.Near(0.75f, GraphMath.BarStart(5), 1e-6f, "the sixth bar starts 0.75 s in");
        log.AreEqual(0f, GraphMath.BarGrowAt(0f), "a bar starts at zero");
        log.AreEqual(0f, GraphMath.BarGrowAt(-0.2f), "…and stays there before its turn");
        log.Near(1f, GraphMath.BarGrowAt(GraphMath.BarGrowSeconds), 1e-6f, "…and is full after 0.5 s");
        log.Near(1f, GraphMath.BarGrowAt(5f), 1e-6f, "…and stays full");
        log.IsTrue(GraphMath.BarGrowAt(0.1f) > 0.1f / GraphMath.BarGrowSeconds, "…easing out (fast first)");
        bool rising = true;
        for (float t = 0.01f; t <= 0.6f; t += 0.01f) if (GraphMath.BarGrowAt(t) < GraphMath.BarGrowAt(t - 0.01f)) rising = false;
        log.IsTrue(rising, "a bar never shrinks while it grows");
        log.Near(GraphMath.BarStart(5) + GraphMath.BarGrowSeconds, GraphMath.BarsSeconds(6), 1e-6f, "six bars are done 1.25 s after the first starts");
        log.AreEqual(CompareMath.ValueAlphaAt(0.5f), GraphMath.ValueAlphaAt(0.5f), "a value waits for its bar, as in Compare");
        log.Near(1f, GraphMath.ValueAlphaAt(1f), 1e-6f, "…and is there when the bar is full");

        // Line: 1.2 s, left to right; dots pop as the head reaches them.
        log.AreEqual(0f, GraphMath.LineDrawnAt(0f), "the line starts undrawn");
        log.Near(0.5f, GraphMath.LineDrawnAt(GraphMath.LineDrawSeconds * 0.5f), 1e-5f, "…is half drawn half-way");
        log.AreEqual(1f, GraphMath.LineDrawnAt(GraphMath.LineDrawSeconds), "…and whole after 1.2 s");
        bool forward = true, inverse = true;
        for (int i = 1; i <= 120; i++)
        {
            float t = i * 0.01f;
            if (GraphMath.LineDrawnAt(t) < GraphMath.LineDrawnAt(t - 0.01f)) forward = false;
            float f = i / 120f;
            if (Mathf.Abs(GraphMath.LineDrawnAt(GraphMath.LineReachSeconds(f)) - f) > 2e-3f) inverse = false;
        }
        log.IsTrue(forward, "the line only ever draws forward");
        log.IsTrue(inverse, "LineReachSeconds is when the head gets to a point (the inverse of LineDrawnAt)");
        log.Near(0f, GraphMath.LineReachSeconds(0f), 1e-6f, "the first point is reached at once");
        log.Near(GraphMath.LineDrawSeconds, GraphMath.LineReachSeconds(1f), 1e-4f, "the last when the line is whole");
        log.AreEqual(0f, GraphMath.DotScaleAt(0f), "a dot is hidden until the line reaches it");
        log.AreEqual(1f, GraphMath.DotScaleAt(GraphMath.DotPopSeconds), "…and settled 0.2 s later");
        float peak = 0f;
        for (float t = 0f; t <= GraphMath.DotPopSeconds; t += 0.002f) peak = Mathf.Max(peak, GraphMath.DotScaleAt(t));
        log.IsTrue(peak > 1.02f && peak < 1.25f, $"…with a small pop ({peak:F2}×)");

        // Share: 0.35 s a segment, one after another; the remainder isn't a beat.
        List<GraphMath.RingSegment> s = GraphMath.RingSegments(GraphMath.Parse("A=69%;B=16%;C=1%", GraphKind.Share, false).Points);
        log.AreEqual(0f, GraphMath.RingFillAt(0f, s), "the ring starts empty");
        log.Near(0.69f, GraphMath.RingFillAt(GraphMath.SegmentSeconds, s), 1e-5f, "after 0.35 s the first segment is in");
        log.Near(0.85f, GraphMath.RingFillAt(2f * GraphMath.SegmentSeconds, s), 1e-5f, "after 0.70 s the second");
        log.IsTrue(GraphMath.RingFillAt(2.5f * GraphMath.SegmentSeconds, s) > 0.85f && GraphMath.RingFillAt(2.5f * GraphMath.SegmentSeconds, s) < 0.88f, "the third is filling in between");
        log.AreEqual(1f, GraphMath.RingFillAt(3f * GraphMath.SegmentSeconds, s), "after 1.05 s the ring is full (the remainder needs no beat)");
        log.IsTrue(GraphMath.RingFillAt(0.5f * GraphMath.SegmentSeconds, s) > 0.345f, "a segment eases out (more than half-way at half-time)");
        bool filling = true;
        for (float t = 0.01f; t <= 1.2f; t += 0.01f) if (GraphMath.RingFillAt(t, s) < GraphMath.RingFillAt(t - 0.01f, s) - 1e-6f) filling = false;
        log.IsTrue(filling, "the ring only ever fills");
        log.Near(1.05f, GraphMath.ShareSeconds(3), 1e-6f, "three segments take 1.05 s");
        log.AreEqual(0f, GraphMath.RingFillAt(1f, new List<GraphMath.RingSegment>()), "no segments, nothing to fill");

        // The whole entrance per kind.
        log.Near(GraphMath.BarsSeconds(2), GraphMath.ChartSeconds(GraphMath.Parse("A=1;B=2", GraphKind.Bars, false)), 1e-6f, "ChartSeconds: bars");
        log.Near(GraphMath.LineSeconds, GraphMath.ChartSeconds(GraphMath.Parse("A=1;B=2;C=3", GraphKind.Line, false)), 1e-6f, "ChartSeconds: line");
        log.Near(GraphMath.ShareSeconds(2), GraphMath.ChartSeconds(GraphMath.Parse("A=60%;B=40%", GraphKind.Share, false)), 1e-6f, "ChartSeconds: share");
        log.AreEqual(0f, GraphMath.ChartSeconds(GraphMath.Parse("A=x;B=2", GraphKind.Bars, false)), "ChartSeconds: text rows don't animate");
        log.Near(GraphMath.ChartDelaySeconds, GraphMath.EntranceSeconds(GraphMath.Parse("", GraphKind.Bars, false)), 1e-6f, "EntranceSeconds: the landing alone when there is no chart");
        log.IsTrue(GraphMath.LineSeconds >= GraphMath.LineDrawSeconds && GraphMath.LineDrawSeconds == 1.2f && GraphMath.SegmentSeconds == 0.35f, "the brief's 1.2 s line and 0.35 s segments");
    }

    static void Graph_FrameAndSizes(Log log)
    {
        Rect safe = GraphMath.SafeRect(1920f, 1080f);
        log.IsTrue(Mathf.Abs(safe.xMin - 115.2f) < 0.01f && Mathf.Abs(safe.yMin - 64.8f) < 0.01f &&
                   Mathf.Abs(safe.xMax - 1804.8f) < 0.01f && Mathf.Abs(safe.yMax - 1015.2f) < 0.01f, "title-safe is the frame inside 6 % margins");
        float w = GraphMath.ContentWidth(1920f, 1080f);
        log.Near(GraphMath.ContentMaxWidthPx, w, 0.01f, "at 1080p the column is 1560 px");
        log.IsTrue((1920f - w) * 0.5f >= safe.xMin, "…centred well inside title-safe");
        log.Near(1440f * 0.88f, GraphMath.ContentWidth(1440f, 1080f), 0.01f, "a 4:3 frame gets the safe width");
        log.Near(2f, GraphMath.ScaleFor(2160f), 1e-6f, "sizes scale with the frame's height");
        log.Near(1040f, GraphMath.ContentWidth(1280f, 720f), 0.01f, "at 720p the column is two-thirds as wide");

        // Nothing the card sets by constant is under 32 px at 1080p.
        foreach (float px in new[] { GraphMath.TitleMinPx, GraphMath.SourcePx, GraphMath.SourceFloorPx, GraphMath.BarValueMinPx, GraphMath.LineNotePx,
                                     GraphMath.LineLabelPx, GraphMath.LineValuePx, GraphMath.ShareLegendOneLineMinPx, GraphMath.BarLabelPx, GraphMath.RowsPx })
            log.IsTrue(px >= CardTypography.HardFloorPx, $"a {px:F0} px size is at or above the 32 px floor");
        log.IsTrue(GraphMath.TitlePx == 64f && GraphMath.TitleMinPx == 44f && GraphMath.TitleMaxLines == 2 && GraphMath.SourcePx == 36f,
            "the brief's title (64 → 44 px, two lines) and source (36 px)");
    }

    // -----------------------------------------------------------------------
    // Pure: meshes
    // -----------------------------------------------------------------------

    static float GraphPolygonArea(int steps, float radius) => 0.5f * steps * radius * radius * Mathf.Sin(2f * Mathf.PI / steps);

    static void Graph_LineMesh(Log log)
    {
        var mesh = new GraphMath.Mesh2D();
        var white = new Color32(255, 255, 255, 255);
        var corner = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f) };
        float disc = GraphPolygonArea(GraphMath.DiscSteps, 5f);

        // Hard-edged: two 100 × 10 quads and a disc on each of the three vertices.
        GraphMath.BuildLine(corner, 10f, 1f, 0f, white, mesh);
        log.Near(2000f + 3f * disc, mesh.Area(), 0.5f, "a line is a quad per segment plus a disc per vertex (joins can't gap)");
        Rect b = mesh.Bounds();
        log.IsTrue(Mathf.Abs(b.xMin + 5f) < 0.01f && Mathf.Abs(b.xMax - 105f) < 0.01f && Mathf.Abs(b.yMin + 5f) < 0.01f && Mathf.Abs(b.yMax - 105f) < 0.01f,
            $"…reaching half its width past its ends (round caps) ({b})");
        log.AreEqual(2 * 4 + 3 * (1 + GraphMath.DiscSteps), mesh.Vertices.Count, "…with the vertices that takes");
        log.IsTrue(mesh.Triangles.Count % 3 == 0 && mesh.Colors.Count == mesh.Vertices.Count, "…a well-formed mesh");
        bool indexed = true;
        foreach (int t in mesh.Triangles) if (t < 0 || t >= mesh.Vertices.Count) indexed = false;
        log.IsTrue(indexed, "…every triangle points at a real vertex");

        // Drawn by length: half = the first segment only; three quarters = half-way up the second.
        GraphMath.BuildLine(corner, 10f, 0.5f, 0f, white, mesh);
        log.Near(1000f + 2f * disc, mesh.Area(), 0.5f, "half drawn: the first segment, a disc at each end of it");
        log.Near(5f, mesh.Bounds().yMax, 0.01f, "…nothing of the second segment yet");
        GraphMath.BuildLine(corner, 10f, 0.75f, 0f, white, mesh);
        log.Near(55f, mesh.Bounds().yMax, 0.01f, "three quarters drawn: the head is half-way up the second segment, with its round cap");
        log.Near(1500f + 3f * disc, mesh.Area(), 0.5f, "…a segment and a half");
        GraphMath.BuildLine(corner, 10f, 0.25f, 0f, white, mesh);
        log.Near(55f, mesh.Bounds().xMax, 0.01f, "a quarter drawn: half-way along the first segment");
        GraphMath.BuildLine(corner, 10f, 0f, 0f, white, mesh);
        log.AreEqual(0, mesh.Vertices.Count, "nothing drawn: an empty mesh");
        GraphMath.BuildLine(corner, 10f, 7f, 0f, white, mesh);
        log.Near(2000f + 3f * disc, mesh.Area(), 0.5f, "a drawn fraction past 1 is the whole line");

        // Feathered: the solid shape is unchanged, a 1 px strip fades to clear round it.
        GraphMath.BuildLine(corner, 10f, 1f, 1f, white, mesh);
        log.Near(2000f + 3f * disc, mesh.Area(255), 0.5f, "the feather adds nothing to the solid line");
        Rect soft = mesh.Bounds(), solid = mesh.Bounds(255);
        log.IsTrue(Mathf.Abs(soft.xMin - (solid.xMin - 1f)) < 0.01f && Mathf.Abs(soft.yMax - (solid.yMax + 1f)) < 0.01f, "…and reaches 1 px further out");
        bool edgeClear = true;
        for (int i = 0; i < mesh.Vertices.Count; i++)
            if (mesh.Colors[i].a != 0 && mesh.Colors[i].a != 255) edgeClear = false;
        log.IsTrue(edgeClear && mesh.Area() > mesh.Area(255), "…fading from the line's colour to clear");

        // Twelve points stay far under the UI mesh's vertex cap.
        var twelve = new List<Vector2>();
        for (int i = 0; i < 12; i++) twelve.Add(new Vector2(i * 100f, (i % 3) * 80f));
        GraphMath.BuildLine(twelve, 8f, 1f, GraphMath.FeatherPx, white, mesh);
        log.IsTrue(mesh.Vertices.Count > 0 && mesh.Vertices.Count < 2000 && mesh.Vertices.Count < GraphMath.MaxMeshVertices, $"twelve points: {mesh.Vertices.Count} vertices");

        // Degenerate input never throws.
        GraphMath.BuildLine(new[] { new Vector2(3f, 4f) }, 10f, 1f, 0f, white, mesh);
        log.Near(disc, mesh.Area(), 0.05f, "one point: a dot");
        GraphMath.BuildLine(new Vector2[0], 10f, 1f, 0f, white, mesh);
        log.AreEqual(0, mesh.Vertices.Count, "no points: nothing");
        GraphMath.BuildLine(new[] { new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(51f, 1f) }, 10f, 1f, 0f, white, mesh);
        log.Near(500f + 3f * disc, mesh.Area(), 0.5f, "a repeated point is skipped, not divided by");
    }

    static void Graph_RingMesh(Log log)
    {
        var mesh = new GraphMath.Mesh2D();
        Color red = Color.red, blue = Color.blue, grey = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        float ringArea = Mathf.PI * (100f * 100f - 60f * 60f);

        // A whole turn.
        var whole = new List<GraphMath.ColoredArc> { new GraphMath.ColoredArc(0f, 1f, red) };
        GraphMath.BuildRing(whole, new Vector2(500f, 300f), 60f, 100f, 0f, mesh);
        log.Near(ringArea, mesh.Area(), ringArea * 0.002f, "a full ring covers π(R² − r²)");
        Rect b = mesh.Bounds();
        log.IsTrue(Mathf.Abs(b.xMin - 400f) < 0.1f && Mathf.Abs(b.xMax - 600f) < 0.1f && Mathf.Abs(b.yMin - 200f) < 0.1f && Mathf.Abs(b.yMax - 400f) < 0.1f,
            $"…round its centre, out to the outer radius ({b})");
        log.IsTrue(GraphMath.RingStepsPerTurn >= 96 && mesh.Vertices.Count == 2 * (GraphMath.RingStepsPerTurn + 1), $"…in {GraphMath.RingStepsPerTurn} steps (at least 96 a turn)");

        // Clockwise from twelve o'clock: the first quarter is the top-right one (y up).
        GraphMath.BuildRing(new[] { new GraphMath.ColoredArc(0f, 0.25f, red) }, Vector2.zero, 60f, 100f, 0f, mesh);
        Rect q = mesh.Bounds();
        log.IsTrue(q.xMin > -0.01f && q.yMin > -0.01f && Mathf.Abs(q.xMax - 100f) < 0.01f && Mathf.Abs(q.yMax - 100f) < 0.01f,
            $"the first quarter turn is the top-right quadrant: clockwise from twelve o'clock ({q})");
        log.Near(ringArea * 0.25f, mesh.Area(), ringArea * 0.001f, "…a quarter of the ring");
        GraphMath.BuildRing(new[] { new GraphMath.ColoredArc(0.5f, 0.75f, red) }, Vector2.zero, 60f, 100f, 0f, mesh);
        Rect q3 = mesh.Bounds();
        log.IsTrue(q3.xMax < 0.01f && q3.yMax < 0.01f, "the third quarter is the bottom-left quadrant");

        // Fill: segments fill in order; the rest shows in the unfilled colour (or not at all).
        var two = new List<GraphMath.ColoredArc> { new GraphMath.ColoredArc(0f, 0.5f, red), new GraphMath.ColoredArc(0.5f, 1f, blue) };
        List<GraphMath.ColoredArc> a = GraphMath.DrawnArcs(two, 0.25f, 0f, Color.clear);
        log.IsTrue(a.Count == 1 && a[0].Start == 0f && Mathf.Approximately(a[0].End, 0.25f) && a[0].Color == red, "a quarter filled: half of the first segment, nothing else");
        a = GraphMath.DrawnArcs(two, 0.75f, 0f, Color.clear);
        log.IsTrue(a.Count == 2 && Mathf.Approximately(a[0].End, 0.5f) && a[1].Color == blue && Mathf.Approximately(a[1].Start, 0.5f) && Mathf.Approximately(a[1].End, 0.75f),
            "three quarters filled: the first segment whole, half of the second");
        a = GraphMath.DrawnArcs(two, 0.25f, 0f, grey);
        log.IsTrue(a.Count == 3 && a[1].Color == grey && Mathf.Approximately(a[1].Start, 0.25f) && Mathf.Approximately(a[1].End, 0.5f) &&
                   a[2].Color == grey && Mathf.Approximately(a[2].Start, 0.5f) && a[2].End == 1f,
            "with an unfilled colour the rest of the ring is drawn in it");
        a = GraphMath.DrawnArcs(two, 1f, 0f, grey);
        log.IsTrue(a.Count == 2 && a[0].Color == red && a[1].Color == blue && a[1].End == 1f, "full: every segment in its own colour, nothing unfilled");
        a = GraphMath.DrawnArcs(two, 0f, 0f, Color.clear);
        log.AreEqual(0, a.Count, "empty and no unfilled colour: nothing to draw");

        // The hairline between segments: each is drawn the gap short, half at each end.
        a = GraphMath.DrawnArcs(two, 1f, 0.02f, Color.clear);
        log.IsTrue(a.Count == 2 && Mathf.Approximately(a[0].Start, 0.01f) && Mathf.Approximately(a[0].End, 0.49f) &&
                   Mathf.Approximately(a[1].Start, 0.51f) && Mathf.Approximately(a[1].End, 0.99f), "a 0.02-turn gap leaves a hairline between neighbours");
        float drawn = 0f, total = 0f;
        foreach (GraphMath.ColoredArc arc in GraphMath.DrawnArcs(two, 0.6f, 0.02f, grey)) total += arc.End - arc.Start;
        foreach (GraphMath.ColoredArc arc in GraphMath.DrawnArcs(two, 0.6f, 0.02f, Color.clear)) drawn += arc.End - arc.Start;
        log.Near(0.96f, total, 1e-5f, "filled and unfilled parts together are the ring less its two hairlines");
        log.IsTrue(drawn > 0.55f && drawn < 0.6f, "…and the filled part stops where the fill is");
        log.AreEqual(0, GraphMath.DrawnArcs(new[] { new GraphMath.ColoredArc(0f, 0.01f, red) }, 1f, 0.02f, grey).Count, "a segment thinner than the hairline isn't drawn");

        // The card's own ring: three segments and the remainder, half filled.
        List<GraphMath.RingSegment> s = GraphMath.RingSegments(GraphMath.Parse("A=69%;B=16%;C=1%", GraphKind.Share, false).Points);
        var card = new List<GraphMath.ColoredArc>();
        Color rest = GraphMath.RemainderColor(GraphSlate);
        foreach (GraphMath.RingSegment g in s)
            card.Add(new GraphMath.ColoredArc(g.Start, g.End, g.Point < 0 ? rest : GraphMath.SegmentColor(g.Point, GraphCoral, GraphSlate)));
        float gap = GraphMath.ShareSegmentGapPx / (2f * Mathf.PI * 224f);
        GraphMath.BuildRing(GraphMath.DrawnArcs(card, 1f, gap, rest), new Vector2(280f, 280f), 168f, 280f, GraphMath.FeatherPx, mesh);
        float full = Mathf.PI * (280f * 280f - 168f * 168f);
        float solid = mesh.Area(255);
        log.IsTrue(solid > full * 0.69f && solid < full * 0.90f, "the three opaque segments cover their 88 % of the ring, less the hairlines (the remainder is translucent)");
        log.IsTrue(mesh.Vertices.Count < 2000 && mesh.Vertices.Count < GraphMath.MaxMeshVertices, $"the Share ring: {mesh.Vertices.Count} vertices");
        Rect rb = mesh.Bounds();
        log.IsTrue(rb.xMin >= -1.01f && rb.xMax <= 561.01f && rb.yMin >= -1.01f && rb.yMax <= 561.01f, "…inside its rect, give or take the 1 px feather");
        GraphMath.BuildRing(GraphMath.DrawnArcs(card, 0f, gap, Color.clear), new Vector2(280f, 280f), 168f, 280f, GraphMath.FeatherPx, mesh);
        log.AreEqual(0, mesh.Vertices.Count, "an empty ring with no unfilled colour draws nothing");

        // Degenerate input never throws.
        GraphMath.BuildRing(whole, Vector2.zero, 100f, 100f, 0f, mesh);
        log.AreEqual(0, mesh.Vertices.Count, "no thickness: nothing");
        GraphMath.BuildRing(null, Vector2.zero, 60f, 100f, 0f, mesh);
        log.AreEqual(0, mesh.Vertices.Count, "no arcs: nothing");
    }

    // -----------------------------------------------------------------------
    // The real card (TMP — these need the editor)
    // -----------------------------------------------------------------------

    static GraphCard BuildGraph(string title, string data, string source, GraphKind kind, bool cropped, out GameObject root, bool show = true)
    {
        var card = BuildCard<GraphCard>(out root);
        var zone = (RectTransform)card.transform.parent;
        zone.sizeDelta = new Vector2(1920f, 1080f);
        card.Initialize(new ContentCardEvent
        {
            cardType = ContentCardType.Graph, fields = new[] { title, data, source }, graphKind = kind, graphCropped = cropped, primaryText = title,
        }, null);
        if (show) ShowGraph(card);
        return card;
    }

    // Shows the card and steps it past its whole entrance at 30 fps.
    static void ShowGraph(GraphCard card)
    {
        card.Show();
        float end = GraphMath.EntranceSeconds(card.Data) + 0.5f;
        for (float t = 0f; t < end; t += Frame) card.Advance(Frame);
    }

    // What every Graph card must satisfy, whatever it draws.
    static void CheckGraph(Log log, GraphCard card, string what, bool expectClean)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        log.IsTrue(report != null, $"{what}: laid out");
        if (report == null) return;
        bool warned = report.Warnings.Count > 0;
        if (expectClean) log.IsTrue(!warned, $"{what}: no layout warning ({string.Join(" | ", report.Warnings)})");
        Rect safe = card.SafeRect;
        log.IsTrue(Mathf.Abs(safe.xMin - rt.rect.width * 0.06f) < 0.5f && Mathf.Abs(safe.yMax - rt.rect.height * 0.94f) < 0.5f, $"{what}: title-safe is 6 % in ({Fmt(safe)})");

        Transform dim = card.transform.Find("Dim");
        log.IsTrue(dim != null && dim.GetSiblingIndex() == 0, $"{what}: the dim is the bottom layer");
        log.Near(card.Dim.Peak, card.DimAlpha, 1e-4f, $"{what}: the dim is up (the BigText dim, {card.DimAlpha:F3})");
        log.Near(1f, card.ContentAlpha, 1e-4f, $"{what}: the content is fully in");
        log.IsTrue(card.EntranceDone, $"{what}: the entrance is over");
        log.IsTrue(card.transform.Find("Background") == null && card.transform.Find("Content/Background") == null, $"{what}: no panel behind the card");

        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name), $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        log.IsTrue(card.TitleSize <= GraphMath.TitlePx + 0.01f, $"{what}: title at most 64 px ({card.TitleSize:F0})");

        var summary = new List<string>();
        foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f, $"{what}: {t.name} at or above 32 px ({t.fontSize:F1})");
            log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} not truncated");
            Rect ink = CardTypography.InkInCard(t, rt);
            log.IsTrue(warned || Inside(ink, safe, 1f), $"{what}: {t.name} inside title-safe ({Fmt(ink)} in {Fmt(safe)})");
            if (t.name == "Title" || t.name == "Source" || t.name == "Label 1" || t.name == "Value 1" || t.name == "AxisNote")
                summary.Add($"{t.name} {t.fontSize:F0}px");
        }

        // Every label and value exactly as written; title and source too.
        log.AreEqual(card.Data.Points.Count, card.PointCount, $"{what}: one label per point");
        for (int i = 0; i < card.PointCount; i++)
        {
            log.AreEqual(card.Data.Points[i].Label, card.PointLabel(i).text, $"{what}: label {i + 1} as written");
            log.IsTrue(!card.PointLabel(i).richText, $"{what}: label {i + 1} isn't parsed for tags");
            if (card.PointValue(i) == null) continue;
            log.AreEqual(card.Data.Points[i].ValueText, card.PointValue(i).text, $"{what}: value {i + 1} as written");
            log.IsTrue(!card.PointValue(i).richText, $"{what}: value {i + 1} isn't parsed for tags (\"<1%\" stays \"<1%\")");
        }

        // Title above the chart, source below it.
        Rect chart = RectInCard(card.Chart, rt);
        log.IsTrue(Mathf.Abs(chart.xMin - card.ChartRect.xMin) < 0.5f && Mathf.Abs(chart.yMin - card.ChartRect.yMin) < 0.5f &&
                   Mathf.Abs(chart.width - card.ChartRect.width) < 0.5f, $"{what}: ChartRect is where the chart is ({Fmt(chart)})");
        if (card.PointCount > 0)
        {
            log.IsTrue(warned || Inside(chart, safe, 1f), $"{what}: the chart is inside title-safe ({Fmt(chart)})");
            if (card.Title.text.Length > 0)
                log.IsTrue(CardTypography.InkInCard(card.Title, rt).yMax <= chart.yMin + 1f, $"{what}: the title sits above the chart");
            if (card.Source.text.Length > 0)
                log.IsTrue(CardTypography.InkInCard(card.Source, rt).yMin >= chart.yMax - 1f, $"{what}: the source sits below it");
        }

        Debug.Log($"[Graph] self-test {what}: {string.Join(", ", summary)}; chart {Fmt(chart)}; group {report.GroupHeight:F0}/{report.InnerHeight:F0} px" +
                  (warned ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Graph_BarsLayout(Log log)
    {
        var cases = new[]
        {
            (t: "Apple Services revenue", d: "Last quarter=$31.6B;A year earlier=$28.75B", s: "Apple quarterly results", clean: true, what: "Apple, 2 bars"),
            (t: "A", d: "A=1;B=2", s: "S", clean: true, what: "shortest"),
            (t: "Six of them", d: "One=60%;Two=50%;Three=40%;Four=30%;Five=20%;Six=10%", s: "Survey, 2026", clean: true, what: "6 bars"),
            (t: "The largest crypto exchange hacks on record, by the value taken at the time of the theft",
             d: "Binance, November 2026=$6.3B+;Bybit, February 2025=$1.5B;Ronin Network bridge=$625M;Poly Network=$611M;The FTX collapse hack=$477M;Mt. Gox=<$470M",
             s: "Company statements; Chainalysis; TRM Labs; Elliptic research, 2026", clean: false, what: "6 bars, long everything"),
            (t: "A hundred to one", d: "Big=$100B;Small=$1B", s: "Src", clean: true, what: "100:1"),
            (t: "", d: "Only=4x;Bars=1x", s: "", clean: true, what: "no title, no source"),
        };
        foreach (var c in cases)
        {
            var card = BuildGraph(c.t, c.d, c.s, GraphKind.Bars, false, out GameObject root);
            try
            {
                string what = $"Graph bars ({c.what})";
                log.IsTrue(!card.IsFallback, $"{what}: a chart");
                CheckGraph(log, card, what, c.clean);
                CheckGraphBars(log, card, what);
                if (c.what == "100:1")
                    log.IsTrue(Mathf.Approximately(card.BarFraction(1), GraphMath.MinFraction) && !card.ValueInside(1), $"{what}: the 1 % bar is a 3 % sliver with its value past its end");
                if (c.what == "Apple, 2 bars")
                    log.IsTrue(card.ValueInside(0) && card.ValueInside(1) && card.ValueSize >= 60f, $"{what}: both values inside their bars, large ({card.ValueSize:F0} px)");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void CheckGraphBars(Log log, GraphCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        bool warned = card.Layout.Warnings.Count > 0;
        Rect safe = card.SafeRect;
        int n = card.PointCount;
        float longest = 0f;
        Rect previous = default;
        for (int i = 0; i < n; i++)
        {
            Rect bar = RectInCard(card.Bar(i).rectTransform, rt);
            longest = Mathf.Max(longest, bar.width);
            log.IsTrue(card.Bar(i).enabled, $"{what}: bar {i + 1} is drawn");
            log.Near(card.BarFraction(i) * card.TrackWidth, bar.width, 1f, $"{what}: bar {i + 1} is at its full {card.BarFraction(i):P0} of the track ({bar.width:F0} px)");
            log.IsTrue(bar.width >= GraphMath.MinFraction * card.TrackWidth - 0.5f, $"{what}: bar {i + 1} is at least a 3 % sliver");
            log.IsTrue(warned || Inside(bar, safe, 1f), $"{what}: bar {i + 1} inside title-safe ({Fmt(bar)})");
            log.IsTrue(card.Bar(i).color == (i == 0 ? ContentCardUIBuilder.AccentColor : CompareCard.Slate),
                $"{what}: bar {i + 1} is {(i == 0 ? "the accent (the story's subject)" : "slate")}");
            if (i > 0)
            {
                log.Near(previous.xMin, bar.xMin, 0.5f, $"{what}: bar {i + 1} starts at the same zero line");
                log.IsTrue(bar.yMin >= previous.yMax + 1f, $"{what}: bar {i + 1} is below bar {i} (script order, with a gap)");
                log.Near(previous.height, bar.height, 0.5f, $"{what}: bar {i + 1} is as thick as the others");
            }
            previous = bar;

            TMP_Text value = card.PointValue(i);
            Rect ink = CardTypography.InkInCard(value, rt);
            log.Near(1f, value.color.a, 1e-4f, $"{what}: value {i + 1} is fully in");
            if (card.ValueInside(i))
                log.IsTrue(Inside(ink, bar, 1.5f), $"{what}: value {i + 1} sits inside its bar ({Fmt(ink)} in {Fmt(bar)})");
            else
                log.IsTrue(ink.xMin >= bar.xMax - 1f, $"{what}: value {i + 1} sits just past its bar's end");
            log.IsTrue(ink.yMin >= bar.yMin - 2f && ink.yMax <= bar.yMax + 2f, $"{what}: value {i + 1} centred on its bar's height");

            Rect label = CardTypography.InkInCard(card.PointLabel(i), rt);
            log.IsTrue(label.xMax <= bar.xMin + 1f, $"{what}: label {i + 1} is left of its bar ({Fmt(label)})");
            log.IsTrue(Mathf.Abs(label.center.y - bar.center.y) <= bar.height * 0.35f, $"{what}: label {i + 1} is centred on its bar");
        }
        log.Near(card.TrackWidth, longest, 1f, $"{what}: the largest value fills the track ({card.TrackWidth:F0} px)");
        log.IsTrue(card.Ring == null && card.Line == null && card.AxisNote == null, $"{what}: no ring, no line, no axis note");
    }

    // Steps the entrance at 30 fps: nothing before Show; the bars start once
    // the card has landed, 0.15 s apart, 0.5 s each; a value rides its bar.
    static void Graph_BarsGrowOneAfterAnother(Log log)
    {
        var card = BuildGraph("Three", "One=$31.6B;Two=$28.75B;Three=$20B", "Src", GraphKind.Bars, false, out GameObject root, show: false);
        try
        {
            var rt = (RectTransform)card.transform;
            int n = card.PointCount;
            log.IsTrue(card.ContentAlpha == 0f && card.DimAlpha == 0f, "before Show: the content and the dim are hidden");
            bool anyBar = false;
            for (int i = 0; i < n; i++) anyBar |= card.Bar(i).enabled;
            log.IsTrue(!anyBar, "before Show: no bar is drawn");

            card.Show();
            var startedAt = new float[n];
            var fullAt = new float[n];
            var offset = new float?[n];
            bool rides = true, monotone = true;
            var lastWidth = new float[n];
            for (int i = 0; i < n; i++) startedAt[i] = fullAt[i] = float.NaN;
            float t = 0f, dimFullAt = float.NaN, contentFullAt = float.NaN;
            for (int f = 0; f < 75; f++)
            {
                card.Advance(Frame);
                t += Frame;
                if (float.IsNaN(dimFullAt) && card.DimAlpha >= card.Dim.Peak - 1e-4f) dimFullAt = t;
                if (float.IsNaN(contentFullAt) && card.ContentAlpha >= 1f - 1e-4f) contentFullAt = t;
                for (int i = 0; i < n; i++)
                {
                    if (!card.Bar(i).enabled) continue;
                    Rect bar = RectInCard(card.Bar(i).rectTransform, rt);
                    if (float.IsNaN(startedAt[i])) startedAt[i] = t;
                    if (bar.width < lastWidth[i] - 0.01f) monotone = false;
                    lastWidth[i] = bar.width;
                    if (float.IsNaN(fullAt[i]) && bar.width >= card.BarFraction(i) * card.TrackWidth - 0.5f) fullAt[i] = t;
                    Rect box = RectInCard(card.PointValue(i).rectTransform, rt);
                    float off = box.xMin - bar.xMax;
                    if (offset[i] == null) offset[i] = off;
                    else if (Mathf.Abs(off - offset[i].Value) > 0.5f) rides = false;
                }
            }
            log.IsTrue(dimFullAt >= BigTextTypography.DimFadeInSeconds - Frame && dimFullAt <= BigTextTypography.DimFadeInSeconds + Frame + 1e-3f,
                $"the dim reaches its peak after its 0.2 s fade-in ({dimFullAt:F3} s)");
            log.IsTrue(contentFullAt <= GraphMath.ContentFadeInSeconds + Frame + 1e-3f, $"the title and labels are in after 0.25 s ({contentFullAt:F3} s)");
            for (int i = 0; i < n; i++)
            {
                float due = GraphMath.ChartDelaySeconds + GraphMath.BarStart(i);
                log.IsTrue(startedAt[i] > due - 1e-3f && startedAt[i] <= due + Frame + 1e-3f,
                    $"bar {i + 1} starts {GraphMath.BarStart(i):F2} s after the card has landed ({startedAt[i]:F3} s, due {due:F3})");
                log.IsTrue(fullAt[i] <= due + GraphMath.BarGrowSeconds + Frame + 1e-3f && fullAt[i] > due + 0.1f,
                    $"bar {i + 1} is full about 0.5 s later ({fullAt[i] - due:F3} s)");
            }
            log.IsTrue(monotone, "no bar ever shrinks while it grows");
            log.IsTrue(rides, "a value keeps its place at its bar's growing end on every frame");
            log.IsTrue(card.EntranceDone, "the entrance is over once the last bar is full");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Graph_LineLayout(Log log)
    {
        string months = "Jan=1.0M;Feb=1.3M;Mar=1.2M;Apr=1.9M;May=2.4M;Jun=2.2M;Jul=2.9M;Aug=3.1M;Sep=3.0M;Oct=3.6M;Nov=4.1M;Dec=4.3M";
        string quarters = "Q1 2023=1.0M;Q2 2023=1.3M;Q3 2023=1.2M;Q4 2023=1.9M;Q1 2024=2.4M;Q2 2024=2.2M;Q3 2024=2.9M;Q4 2024=3.1M;Q1 2025=3.0M;Q2 2025=3.6M;Q3 2025=4.1M;Q4 2025=4.3M";
        var cases = new[]
        {
            (t: "Example trend", d: "Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M", s: "Source", cropped: false, rows: 1, what: "4 points"),
            (t: "Example trend", d: "Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M", s: "Source", cropped: true, rows: 1, what: "4 points, cropped"),
            (t: "Three", d: "2022=10;2023=20;2024=15", s: "S", cropped: false, rows: 1, what: "3 points"),
            (t: "Monthly active users over the year", d: months, s: "Company filings", cropped: false, rows: 1, what: "12 months"),
            (t: "Monthly active users, quarter by quarter, across three years of filings", d: quarters, s: "Company filings; analyst estimates", cropped: true, rows: 2, what: "12 quarters, cropped"),
            (t: "Falling", d: "Before=$6,300,000,000;During=$2,400,000,000;After=$1,100,000,000", s: "TRM Labs", cropped: false, rows: 1, what: "long values"),
        };
        foreach (var c in cases)
        {
            var card = BuildGraph(c.t, c.d, c.s, GraphKind.Line, c.cropped, out GameObject root);
            try
            {
                string what = $"Graph line ({c.what})";
                log.IsTrue(!card.IsFallback, $"{what}: a chart");
                CheckGraph(log, card, what, expectClean: true);
                CheckGraphLine(log, card, what);
                log.AreEqual(c.rows, card.LabelRows, $"{what}: labels on {c.rows} row(s)");
                log.AreEqual(c.cropped, card.Data.Cropped, $"{what}: cropped only when the tag says so");
                if (c.cropped)
                {
                    TMP_Text note = card.AxisNote;
                    log.IsTrue(note != null && note.gameObject.activeInHierarchy, $"{what}: the fixed line is shown");
                    if (note != null)
                    {
                        log.AreEqual("AXIS DOES NOT START AT ZERO", note.text, $"{what}: …and reads AXIS DOES NOT START AT ZERO");
                        log.IsTrue(note.fontSize >= CardTypography.HardFloorPx, $"{what}: …at 32 px or more ({note.fontSize:F0})");
                        log.IsTrue((note.fontStyle & FontStyles.SmallCaps) == 0 && note.text == note.text.ToUpperInvariant(), $"{what}: …in full capitals");
                        Color nc = note.color, accent = ContentCardUIBuilder.AccentColor;
                        log.IsTrue(Mathf.Approximately(nc.r, accent.r) && Mathf.Approximately(nc.g, accent.g) && Mathf.Approximately(nc.b, accent.b) && nc.a > 0.99f,
                            $"{what}: …in the accent colour");
                        Rect axis = RectInCard(card.Axis.rectTransform, (RectTransform)card.transform);
                        Rect ink = CardTypography.InkInCard(note, (RectTransform)card.transform);
                        log.IsTrue(ink.yMin >= axis.yMax - 1f && ink.xMin >= axis.xMin - 1f && ink.yMin - axis.yMax < 200f, $"{what}: …beside the axis, under its labels ({Fmt(ink)})");
                        for (int i = 0; i < card.PointCount; i++)
                            log.IsTrue(!ink.Overlaps(CardTypography.InkInCard(card.PointLabel(i), (RectTransform)card.transform)), $"{what}: …clear of label {i + 1}");
                    }
                }
                else
                {
                    log.IsTrue(card.AxisNote == null, $"{what}: no axis note on a zero axis");
                    bool said = false;
                    foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(true))
                        if (t.text.IndexOf("ZERO", StringComparison.OrdinalIgnoreCase) >= 0) said = true;
                    log.IsTrue(!said, $"{what}: …and nothing on the card says otherwise");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ,Cropped that changes nothing: a zero axis, so no fixed line.
        var zero = BuildGraph("To zero", "A=0;B=5;C=10", "S", GraphKind.Line, true, out GameObject zroot);
        try { log.IsTrue(!zero.IsFallback && zero.AxisNote == null && !zero.Data.Cropped, "Graph line (,Cropped down to zero): the axis starts at zero, so the card doesn't claim otherwise"); }
        finally { UnityEngine.Object.DestroyImmediate(zroot); }

        // Labels that can't fit even on two rows: warned, never under 32 px, never dropped.
        string longLabels = GraphSeries(12, i => (i + 1) + "M").Replace("L", "The reporting period number ");
        var crowded = BuildGraph("Crowded", longLabels, "S", GraphKind.Line, false, out GameObject croot);
        try
        {
            log.IsTrue(crowded.Layout.Warnings.Exists(w => w.Contains("labels")), "Graph line (overlong labels): warned");
            log.IsTrue(crowded.LabelRows == 2 && crowded.LabelSize >= CardTypography.HardFloorPx - 0.01f, $"Graph line (overlong labels): two rows, still 32 px ({crowded.LabelSize:F0})");
            log.IsTrue(crowded.PointCount == 12 && crowded.PointLabel(11).text == "The reporting period number 12", "Graph line (overlong labels): every label is still there, as written");
        }
        finally { UnityEngine.Object.DestroyImmediate(croot); }
    }

    static void CheckGraphLine(Log log, GraphCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        Rect safe = card.SafeRect;
        int n = card.PointCount;
        Rect axis = RectInCard(card.Axis.rectTransform, rt);
        Rect plot = RectInCard(card.Line.rectTransform, rt);
        float axisY = axis.center.y;
        GraphMath.Axis scale = GraphMath.LineAxis(card.Data.Points, card.Data.Cropped);

        log.IsTrue(card.Line.PointCount == n && Mathf.Approximately(card.Line.Drawn, 1f), $"{what}: the line is fully drawn through all {n} points");
        log.IsTrue(card.Line.color == ContentCardUIBuilder.AccentColor, $"{what}: one accent line");
        log.IsTrue(axis.height <= 6f && axis.width >= plot.width, $"{what}: a thin baseline under the whole plot ({Fmt(axis)})");
        log.Near(axisY, plot.yMax, 0.5f, $"{what}: the plot stands on the axis");
        float halfLine = card.Line.WidthPx * 0.5f + GraphMath.FeatherPx;
        log.IsTrue(plot.xMin - halfLine >= safe.xMin && plot.xMax + halfLine <= safe.xMax && plot.yMin - halfLine >= safe.yMin && axis.yMax <= safe.yMax,
            $"{what}: line and axis inside title-safe ({Fmt(plot)})");
        Canvas.ForceUpdateCanvases(); // builds the mesh
        log.IsTrue(card.Line.VertexCount > 0 && card.Line.VertexCount < GraphMath.MaxMeshVertices, $"{what}: the line's mesh is built ({card.Line.VertexCount} vertices)");

        var points = new Vector2[n];
        var labelInk = new Rect[n];
        float step = 0f;
        bool ordered = true;
        for (int i = 0; i < n; i++)
        {
            points[i] = card.ChartRect.position + card.PointInChart(i);
            if (i == 1) step = points[1].x - points[0].x;
            if (i > 1) log.Near(step, points[i].x - points[i - 1].x, 0.5f, $"{what}: point {i + 1} is evenly spaced");
            log.Near(GraphMath.LineY(card.Data.Points[i].Value, scale), (axisY - points[i].y) / plot.height, 0.003f,
                $"{what}: point {i + 1} is at its value's true height on the axis");
            log.IsTrue(points[i].y <= axisY + 0.5f && points[i].y >= plot.yMin - 0.5f, $"{what}: point {i + 1} is between the axis and the plot's top");
            for (int j = 0; j < i; j++)
            {
                double dv = card.Data.Points[i].Value - card.Data.Points[j].Value;
                float dy = points[j].y - points[i].y; // y runs down
                if ((dv > 0 && dy <= 0f) || (dv < 0 && dy >= 0f)) ordered = false;
            }

            Rect dot = RectInCard(card.Dot(i).rectTransform, rt);
            log.IsTrue(Vector2.Distance(dot.center, points[i]) < 0.5f && card.Dot(i).enabled && Mathf.Approximately(card.Dot(i).rectTransform.localScale.x, 1f),
                $"{what}: dot {i + 1} sits on its point, popped in");

            labelInk[i] = CardTypography.InkInCard(card.PointLabel(i), rt);
            log.IsTrue(labelInk[i].yMin >= axis.yMax - 1f, $"{what}: label {i + 1} sits under the axis ({Fmt(labelInk[i])})");
            if (i > 0 && i < n - 1)
                log.IsTrue(Mathf.Abs(labelInk[i].center.x - points[i].x) <= 4f, $"{what}: label {i + 1} is centred under its point");
            bool hasValue = i == 0 || i == n - 1;
            log.AreEqual(hasValue, card.PointValue(i) != null, $"{what}: point {i + 1} {(hasValue ? "shows" : "shows no")} value");
        }
        log.IsTrue(ordered, $"{what}: higher values sit higher");
        log.IsTrue(points[0].x < points[n - 1].x, $"{what}: script order runs left to right");
        if (card.LabelRows == 2)
            log.IsTrue(labelInk[1].center.y > labelInk[0].yMax - 2f && Mathf.Abs(labelInk[2].center.y - labelInk[0].center.y) < 3f,
                $"{what}: staggered labels alternate between two rows");
        bool overlap = false;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (labelInk[i].Overlaps(labelInk[j])) overlap = true;
        log.IsTrue(!overlap || card.Layout.Warnings.Count > 0, $"{what}: no two labels overlap");

        // The end values sit beside the line's ends, clear of the line and of the labels.
        float dotR = RectInCard(card.Dot(0).rectTransform, rt).width * 0.5f;
        Rect first = CardTypography.InkInCard(card.PointValue(0), rt), last = CardTypography.InkInCard(card.PointValue(n - 1), rt);
        log.IsTrue(first.xMax <= points[0].x - dotR + 1f, $"{what}: the first value is left of the line's start ({Fmt(first)})");
        log.IsTrue(last.xMin >= points[n - 1].x + dotR - 1f, $"{what}: the last value is right of the line's end ({Fmt(last)})");
        log.IsTrue(first.yMax <= axis.yMin + 1f + first.height * 0.3f && last.yMax <= axis.yMin + 1f + last.height * 0.3f, $"{what}: both stay above the axis");
        for (int i = 0; i < n; i++)
            log.IsTrue(!first.Overlaps(labelInk[i]) && !last.Overlaps(labelInk[i]), $"{what}: the end values are clear of label {i + 1}");
        log.Near(1f, card.PointValue(0).color.a, 1e-4f, $"{what}: the first value is fully in");
        log.Near(1f, card.PointValue(n - 1).color.a, 1e-4f, $"{what}: the last value is fully in");
        log.IsTrue(card.Ring == null && card.transform.Find("Content/Chart/Bar 1") == null, $"{what}: no ring, no bars");
    }

    static void Graph_ShareLayout(Log log)
    {
        var cases = new[]
        {
            (t: "Example share", d: "A=69%;B=16%;C=1%", s: "Source", clean: true, what: "86 %, a sliver"),
            (t: "Two", d: "Yes=60%;No=40%", s: "S", clean: true, what: "2 parts, 100 %"),
            (t: "Browser share worldwide", d: "Chrome=64%;Safari=19%;Edge=5%;Firefox=3%;Samsung Internet=2%", s: "StatCounter, October 2026", clean: true, what: "5 parts"),
            (t: "Who reads the terms of service before they agree to them, by their own account",
             d: "People who say they read the whole terms of service before agreeing=12%;People who say they scroll to the bottom and accept=71%;People who say it depends on the product=<9%",
             s: "A survey of two thousand adults, 2026", clean: true, what: "long labels"),
        };
        foreach (var c in cases)
        {
            var card = BuildGraph(c.t, c.d, c.s, GraphKind.Share, false, out GameObject root);
            try
            {
                string what = $"Graph share ({c.what})";
                var rt = (RectTransform)card.transform;
                log.IsTrue(!card.IsFallback, $"{what}: a chart");
                CheckGraph(log, card, what, c.clean);

                int n = card.PointCount;
                List<GraphMath.RingSegment> want = GraphMath.RingSegments(card.Data.Points);
                bool remainder = want[want.Count - 1].Point < 0;
                Rect ringRect = RectInCard(card.Ring.rectTransform, rt);
                Color accent = ContentCardUIBuilder.AccentColor, rest = GraphMath.RemainderColor(CompareCard.Slate);
                log.IsTrue(Mathf.Abs(ringRect.width - ringRect.height) < 0.5f && ringRect.width >= 300f, $"{what}: a round ring ({ringRect.width:F0} px)");
                log.IsTrue(Inside(ringRect, card.SafeRect, 1f + GraphMath.FeatherPx), $"{what}: the ring inside title-safe ({Fmt(ringRect)})");
                log.AreEqual(want.Count, card.Ring.SegmentCount, $"{what}: one segment per point{(remainder ? " and what is left to 100 %" : "")}");
                log.IsTrue(Mathf.Approximately(card.Ring.Fill, 1f), $"{what}: the ring is fully filled");
                log.IsTrue(card.Ring.Segment(0).Start == 0f && Mathf.Approximately(card.Ring.Segment(card.Ring.SegmentCount - 1).End, 1f), $"{what}: from twelve o'clock, one whole turn");
                log.IsTrue(card.Ring.InnerFraction > 0.3f && card.Ring.InnerFraction < 0.9f && Mathf.Approximately(card.Ring.OuterFraction, 1f), $"{what}: a ring, not a pie");
                log.IsTrue(card.Ring.Segment(0).Color == accent, $"{what}: the first segment is the accent");
                log.IsTrue(card.Ring.Segment(1).Color == CompareCard.Slate, $"{what}: the second is slate");
                for (int i = 0; i < n; i++)
                    log.Near(want[i].End - want[i].Start, card.Ring.Segment(i).End - card.Ring.Segment(i).Start, 1e-5f, $"{what}: segment {i + 1} is its point's share of the turn");
                if (remainder)
                    log.IsTrue(card.Ring.Segment(n).Color == rest && card.Ring.UnfilledColor == rest && rest.a < 1f, $"{what}: what is left is the dark translucent segment");
                Canvas.ForceUpdateCanvases(); // builds the mesh
                log.IsTrue(card.Ring.VertexCount > 0 && card.Ring.VertexCount < GraphMath.MaxMeshVertices, $"{what}: the ring's mesh is built ({card.Ring.VertexCount} vertices)");

                // Legend: swatch, label, value — one row per point, beside the ring, none for the remainder.
                Rect previous = default;
                for (int i = 0; i < n; i++)
                {
                    Rect sw = RectInCard(card.Swatch(i).rectTransform, rt);
                    Rect label = CardTypography.InkInCard(card.PointLabel(i), rt), value = CardTypography.InkInCard(card.PointValue(i), rt);
                    Color sc = card.Swatch(i).color, wantColour = GraphMath.SegmentColor(i, accent, CompareCard.Slate);
                    log.IsTrue(Mathf.Approximately(sc.r, wantColour.r) && Mathf.Approximately(sc.g, wantColour.g) && Mathf.Approximately(sc.b, wantColour.b) && sc.a > 0.99f,
                        $"{what}: swatch {i + 1} is its segment's colour");
                    log.IsTrue(sw.xMin >= ringRect.xMax && Inside(sw, card.SafeRect, 1f), $"{what}: swatch {i + 1} is beside the ring ({Fmt(sw)})");
                    log.IsTrue(sw.xMax <= label.xMin + 1f && label.xMax <= value.xMin + 1f, $"{what}: row {i + 1} reads swatch, label, value");
                    log.IsTrue(sw.center.y > label.yMin - 2f && sw.center.y < label.yMin + card.LabelSize, $"{what}: swatch {i + 1} is on its label's first line");
                    log.IsTrue(Mathf.Abs(RectInCard(card.PointLabel(i).rectTransform, rt).yMin - RectInCard(card.PointValue(i).rectTransform, rt).yMin) < 0.5f,
                        $"{what}: label {i + 1} and its value share a line");
                    log.Near(1f, card.PointLabel(i).color.a, 1e-4f, $"{what}: row {i + 1} is fully in");
                    Rect row = Rect.MinMaxRect(sw.xMin, Mathf.Min(label.yMin, value.yMin), value.xMax, Mathf.Max(label.yMax, value.yMax));
                    if (i > 0) log.IsTrue(row.yMin >= previous.yMax - 1f, $"{what}: row {i + 1} is below row {i} (script order)");
                    previous = row;
                }
                log.Near(card.PointLabel(0).fontSize, card.PointValue(n - 1).fontSize, 0.01f, $"{what}: one size for the whole legend ({card.LabelSize:F0} px)");
                log.IsTrue(card.Line == null && card.AxisNote == null && card.transform.Find("Content/Chart/Bar 1") == null, $"{what}: no line, no bars");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Stepped: the ring fills segment by segment and each legend row arrives with its segment.
        var stepped = BuildGraph("Example share", "A=69%;B=16%;C=1%", "Source", GraphKind.Share, false, out GameObject sroot, show: false);
        try
        {
            log.IsTrue(stepped.Ring.Fill == 0f && stepped.PointLabel(0).color.a == 0f, "Graph share: before Show the ring is empty and the legend hidden");
            stepped.Show();
            float t = 0f, last = 0f;
            bool filling = true, rowsInTurn = true;
            for (int f = 0; f < 60; f++)
            {
                stepped.Advance(Frame);
                t += Frame;
                if (stepped.Ring.Fill < last - 1e-5f) filling = false;
                last = stepped.Ring.Fill;
                float since = t - GraphMath.ChartDelaySeconds;
                for (int i = 0; i < stepped.PointCount; i++)
                    if (since < i * GraphMath.SegmentSeconds - 1e-3f && stepped.PointLabel(i).color.a > 0f) rowsInTurn = false;
                if (Mathf.Abs(since - GraphMath.SegmentSeconds) < Frame * 0.5f)
                    log.IsTrue(stepped.Ring.Fill > 0.6f && stepped.Ring.Fill < 0.75f, $"Graph share: after one beat the first segment is in ({stepped.Ring.Fill:F2})");
            }
            log.IsTrue(filling && Mathf.Approximately(stepped.Ring.Fill, 1f), "Graph share: the ring only fills, to the full turn");
            log.IsTrue(rowsInTurn, "Graph share: a legend row never shows before its segment's beat");
        }
        finally { UnityEngine.Object.DestroyImmediate(sroot); }
    }

    static void Graph_FallbackLayout(Log log)
    {
        var cases = new[]
        {
            (t: "Mixed kinds", d: "Fine=€360K;Stolen=$1.5B", k: GraphKind.Bars, rows: 2, what: "€ and $"),
            (t: "Not numbers", d: "Inside the freezer=50°C;Outside=40°C", k: GraphKind.Bars, rows: 2, what: "°C"),
            (t: "Seven bars", d: GraphSeries(7, i => "$" + (i + 1) + "B"), k: GraphKind.Bars, rows: 7, what: "7 bars"),
            (t: "Over the whole", d: "A=60%;B=44%", k: GraphKind.Share, rows: 2, what: "104 %"),
            (t: "Two points", d: "Before=10;After=20", k: GraphKind.Line, rows: 2, what: "2-point line"),
            (t: "Fifteen", d: GraphSeries(15, i => (i + 1) + "%"), k: GraphKind.Line, rows: 12, what: "15 points"),
            (t: "Long", d: "A label that goes on for quite a while, longer than one line can hold at this size=about three hundred and sixty thousand euros;B=<2", k: GraphKind.Bars, rows: 2, what: "long texts"),
            (t: "Malformed", d: "A=5;just words;C=7", k: GraphKind.Bars, rows: 2, what: "a point with no ="),
        };
        foreach (var c in cases)
        {
            var card = BuildGraph(c.t, c.d, "Source", c.k, true, out GameObject root);
            try
            {
                string what = $"Graph rows ({c.what})";
                var rt = (RectTransform)card.transform;
                log.IsTrue(card.IsFallback && !card.Data.Ok && !string.IsNullOrEmpty(card.Data.Problem), $"{what}: no chart ({card.Data.Problem})");
                log.AreEqual(c.rows, card.PointCount, $"{what}: {c.rows} rows");
                log.IsTrue(card.Ring == null && card.Line == null && card.Axis == null && card.AxisNote == null && card.Chart.Find("Bar 1") == null,
                    $"{what}: no bars, line, ring or axis note");
                CheckGraph(log, card, what, expectClean: true);

                Rect previous = default;
                for (int i = 0; i < card.PointCount; i++)
                {
                    Rect label = CardTypography.InkInCard(card.PointLabel(i), rt), value = CardTypography.InkInCard(card.PointValue(i), rt);
                    log.IsTrue(label.xMax < value.xMin, $"{what}: row {i + 1} has its label left and its value right ({Fmt(label)} | {Fmt(value)})");
                    log.IsTrue(Mathf.Abs(RectInCard(card.PointLabel(i).rectTransform, rt).yMin - RectInCard(card.PointValue(i).rectTransform, rt).yMin) < 0.5f,
                        $"{what}: row {i + 1}'s label and value share a top");
                    log.Near(card.PointLabel(0).fontSize, card.PointValue(i).fontSize, 0.01f, $"{what}: row {i + 1} at the rows' one size");
                    Rect row = Rect.MinMaxRect(label.xMin, Mathf.Min(label.yMin, value.yMin), value.xMax, Mathf.Max(label.yMax, value.yMax));
                    if (i > 0) log.IsTrue(row.yMin >= previous.yMax - 1f, $"{what}: row {i + 1} is below row {i}");
                    previous = row;
                }
                if (card.PointCount > 1)
                {
                    Rect v0 = CardTypography.InkInCard(card.PointValue(0), rt), v1 = CardTypography.InkInCard(card.PointValue(1), rt);
                    log.IsTrue(Mathf.Abs(v0.xMax - v1.xMax) <= card.LabelSize * 0.12f, $"{what}: the values share a right edge");
                }
                log.IsTrue(card.LabelSize >= CardTypography.HardFloorPx - 0.01f && card.LabelSize <= GraphMath.RowsPx + 0.01f, $"{what}: rows at {card.LabelSize:F0} px (32–60)");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // No pair can be read: title and source only.
        var empty = BuildGraph("Only a title", "", "And a source", GraphKind.Bars, false, out GameObject eroot);
        try
        {
            var rt = (RectTransform)empty.transform;
            log.IsTrue(empty.IsFallback && empty.PointCount == 0 && empty.Chart.childCount == 0, "Graph rows (empty field): no rows at all");
            CheckGraph(log, empty, "Graph rows (empty field)", expectClean: true);
            Rect title = CardTypography.InkInCard(empty.Title, rt), source = CardTypography.InkInCard(empty.Source, rt);
            log.IsTrue(title.width > 0f && source.width > 0f && title.yMax < source.yMin, $"Graph rows (empty field): the title over the source ({Fmt(title)} / {Fmt(source)})");
            log.IsTrue(Mathf.Abs((title.yMin + source.yMax) * 0.5f - 540f) < 40f, "Graph rows (empty field): …centred on the frame");
        }
        finally { UnityEngine.Object.DestroyImmediate(eroot); }
    }

    static void Graph_MissingFields(Log log)
    {
        foreach (string[] fields in new string[][] { null, new string[0], new[] { "Just a title" }, new[] { "A title", "A=1;B=2" }, new string[] { null, null, null } })
        {
            var card = BuildCard<GraphCard>(out GameObject root);
            try
            {
                ((RectTransform)card.transform.parent).sizeDelta = new Vector2(1920f, 1080f);
                card.Initialize(new ContentCardEvent { cardType = ContentCardType.Graph, fields = fields }, null);
                ShowGraph(card);
                int given = fields != null ? fields.Length : 0;
                string what = $"Graph ({given} field(s))";
                log.IsTrue(card.Layout != null && card.Data != null, $"{what}: laid out without throwing");
                if (given < 2 || fields[1] == null)
                    log.IsTrue(card.IsFallback && card.PointCount == 0, $"{what}: a missing data field is an empty one — the fallback, no rows");
                else
                    log.IsTrue(!card.IsFallback && card.PointCount == 2 && card.Source.text == "", $"{what}: a missing source is an empty one");
                log.AreEqual(given > 0 && fields[0] != null ? fields[0] : "", card.Title.text, $"{what}: the title is what was given");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    // The layout follows the frame: a 720p frame gets everything at two-thirds,
    // an unresolved (zero) rect falls back to 1920×1080.
    static void Graph_RelayoutOnFrameChange(Log log)
    {
        var card = BuildGraph("Apple Services revenue", "Last quarter=$31.6B;A year earlier=$28.75B", "Apple quarterly results", GraphKind.Bars, false, out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            var zone = (RectTransform)rt.parent;
            MethodInfo late = typeof(GraphCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            float title1080 = card.TitleSize, value1080 = card.ValueSize, track1080 = card.TrackWidth;

            zone.sizeDelta = new Vector2(1280f, 720f);
            late.Invoke(card, null);
            log.IsTrue(Mathf.Abs(card.SafeRect.xMin - 76.8f) < 0.01f && Mathf.Abs(card.SafeRect.yMax - 676.8f) < 0.01f, "Graph at 720p: title-safe follows the frame");
            log.IsTrue(Mathf.Abs(card.TitleSize - title1080 * 2f / 3f) <= 1.5f, $"Graph at 720p: the title is two-thirds the size ({card.TitleSize:F1} vs {title1080:F1})");
            log.IsTrue(Mathf.Abs(card.ValueSize - value1080 * 2f / 3f) <= 2.5f, $"Graph at 720p: the values too ({card.ValueSize:F1} vs {value1080:F1})");
            log.IsTrue(Mathf.Abs(card.TrackWidth - track1080 * 2f / 3f) <= 12f, $"Graph at 720p: and the bars ({card.TrackWidth:F0} vs {track1080:F0})");
            foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                Rect ink = CardTypography.InkInCard(t, rt);
                log.IsTrue(Inside(ink, card.SafeRect, 1f), $"Graph at 720p: {t.name} inside title-safe ({Fmt(ink)})");
            }
            for (int i = 0; i < card.PointCount; i++)
                log.Near(card.BarFraction(i) * card.TrackWidth, RectInCard(card.Bar(i).rectTransform, rt).width, 1f, $"Graph at 720p: bar {i + 1} is still full");

            zone.sizeDelta = Vector2.zero;
            late.Invoke(card, null);
            log.IsTrue(Mathf.Abs(card.SafeRect.xMin - 115.2f) < 0.01f && Mathf.Abs(card.TitleSize - title1080) < 0.01f && Mathf.Abs(card.TrackWidth - track1080) < 0.5f,
                "Graph with no rect yet: laid out for 1920×1080");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Held ({Graph:…,Start} … {Graph:End}) and timed cards both leave through
    // Hide; the dim closes on its envelope either way.
    static void Graph_HideClosesTheDim(Log log)
    {
        foreach (bool fast in new[] { false, true })
        {
            var card = BuildGraph("Example trend", "Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M", "Source", GraphKind.Line, false, out GameObject root);
            try
            {
                log.IsTrue(card.Dim.IsOpen, $"Graph {(fast ? "fast " : "")}hide: the dim was open");
                card.Hide(fast);
                log.IsTrue(card.Dim.IsClosed, $"Graph {(fast ? "fast " : "")}hide: the dim starts closing");
                for (int f = 0; f < 9; f++) card.Advance(Frame);
                log.Near(0f, card.DimAlpha, 1e-4f, $"Graph {(fast ? "fast " : "")}hide: the dim is gone 0.25 s later");
                log.IsTrue(Mathf.Approximately(card.Line.Drawn, 1f), $"Graph {(fast ? "fast " : "")}hide: the chart leaves as it stood (nothing rewinds)");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}

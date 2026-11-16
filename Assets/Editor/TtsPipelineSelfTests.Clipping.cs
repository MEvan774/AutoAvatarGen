using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// {Clipping:…} — the pile of press clippings. The timing of every move, the
/// pile's geometry, the limits and the mark rules (pure, no engine), then the
/// real card laid out with TMP: 1, 3 and 5 sheets and a refused 6th, the
/// phrase found / not found / across a line break, a 3-word and a 16-word
/// headline, the {Mark} verdicts, the entrance stepped frame by frame, the
/// lift on {Clipping:End}, and a frame that changes size.
/// (The tag's parser, routing and logging are the controller's — not here.)
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Clipping Self-Tests")]
    public static void RunClipping()
    {
        var log = new Log();
        RunClippingGroup(log);
        log.Report();
    }

    static void RunClippingGroup(Log log)
    {
        Clipping_Timing(log);
        Clipping_PushInAndTint(log);
        Clipping_PileGeometry(log);
        Clipping_Limits(log);
        Clipping_MarkRules(log);
        Clipping_HeadlineFit(log);
        Clipping_MarkerGeometry(log);
        Clipping_PhraseWarningText(log);
        Clipping_StrokeAge(log);
        Clipping_PileRunOfFive(log);
        Clipping_PileInstantAndQueued(log);
        Clipping_PileLift(log);
        Clipping_PileReanchor(log);

        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Clipping_CardLayout(log);
            Clipping_PileOfFive(log);
            Clipping_InstantAndQueuedSheets(log);
            Clipping_PhraseNotFoundWarnsOnce(log);
            Clipping_Marks(log);
            Clipping_EntranceFrameByFrame(log);
            Clipping_HideLiftsThePile(log);
            Clipping_FrameSizeChange(log);
        }
        catch (Exception e)
        {
            // TMP needs the engine — offline harnesses land here.
            log.IsTrue(false, $"Clipping layout checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Clipping_Timing(Log log)
    {
        // The drop: 0 → 1 over 0.25 s, decelerating.
        log.Near(0.25f, ClippingTypography.DropSeconds, 1e-6f, "a sheet takes 0.25 s to drop in");
        log.AreEqual(0f, ClippingTypography.DropAt(0f), "a drop starts at 0 (the sheet waits above the frame)");
        log.AreEqual(0f, ClippingTypography.DropAt(-1f), "…and is held there until it starts");
        log.Near(1f, ClippingTypography.DropAt(ClippingTypography.DropSeconds), 1e-6f, "…has landed after 0.25 s");
        log.AreEqual(1f, ClippingTypography.DropAt(9f), "…and stays landed");
        log.Near(0.875f, ClippingTypography.DropAt(ClippingTypography.DropSeconds * 0.5f), 1e-5f,
            "decelerating: 7/8 of the way down by half time");
        bool rising = true, slowing = true;
        float last = 0f, lastStep = float.MaxValue;
        for (int f = 1; f <= 25; f++)
        {
            float d = ClippingTypography.DropAt(f * 0.01f);
            if (d < last) rising = false;
            if (d - last > lastStep + 1e-6f) slowing = false;
            lastStep = d - last;
            last = d;
        }
        log.IsTrue(rising, "the drop never moves back up");
        log.IsTrue(slowing, "…and every step is smaller than the one before (it lands softly)");
        log.IsTrue(!ClippingTypography.HasLanded(0.2f) && ClippingTypography.HasLanded(0.25f) && ClippingTypography.HasLanded(3f),
            "a sheet has landed from 0.25 s on");
        log.IsTrue(ClippingTypography.HasLanded(3.7f - (3.7f - ClippingTypography.DropSeconds)),
            "…also when its age is a float subtraction away from 0.25 (an \"instant\" sheet)");

        // The tag's own highlight: 0.25 s after the landing, 0.3 s per line.
        log.Near(0.25f, ClippingTypography.LineSweepStart(0), 1e-6f, "the marker starts 0.25 s after the sheet lands");
        log.Near(0.55f, ClippingTypography.LineSweepStart(1), 1e-6f, "…0.3 s per line, the next line after it");
        log.Near(0.85f, ClippingTypography.LineSweepStart(2), 1e-6f, "…and the third after that");
        log.AreEqual(0f, ClippingTypography.LineSweepAt(0.24f, 0), "nothing before 0.25 s");
        log.Near(0.5f, ClippingTypography.LineSweepAt(0.40f, 0), 1e-4f, "halfway through its line at 0.40 s");
        log.Near(1f, ClippingTypography.LineSweepAt(0.55f, 0), 1e-5f, "the first line is done at 0.55 s");
        log.Near(0f, ClippingTypography.LineSweepAt(0.55f, 1), 1e-5f, "…as the second line starts");
        log.Near(1f, ClippingTypography.LineSweepAt(0.85f, 1), 1e-5f, "…and the second is done at 0.85 s");

        // A {Mark}: at once, 0.3 s per line.
        log.AreEqual(0f, ClippingTypography.SweepAt(0f, 0), "a mark's stroke starts from nothing");
        log.IsTrue(ClippingTypography.SweepAt(1f / 30f, 0) > 0f, "…and is moving on the next frame (it starts at once)");
        log.Near(0.5f, ClippingTypography.SweepAt(0.15f, 0), 1e-4f, "…halfway at 0.15 s");
        log.Near(1f, ClippingTypography.SweepAt(0.3f, 0), 1e-5f, "…done at 0.3 s");
        log.Near(0f, ClippingTypography.SweepAt(0.3f, 1), 1e-5f, "…when its second line starts");
        log.Near(1f, ClippingTypography.SweepAt(0.6f, 1), 1e-5f, "…which is done at 0.6 s");
        log.Near(0.6f, ClippingTypography.SweepSeconds(2), 1e-6f, "a two-line phrase takes 0.6 s");
        log.AreEqual(1f, ClippingTypography.SweepAt(ClippingTypography.Settled, 2), "a settled stroke is fully drawn on every line");
        float a = ClippingTypography.SweepAt(0.05f, 0), b = ClippingTypography.SweepAt(0.15f, 0), c = ClippingTypography.SweepAt(0.25f, 0);
        log.IsTrue(a < b && b < c && b - a > a && c - b < b - a + 1e-4f, "a stroke eases in and out, like a hand's");

        // The stroke's width and colour while it sweeps.
        log.Near(60f, ClippingTypography.StrokeWidth(500f, 100f, 0f), 1e-4f, "a stroke starts as a dab (0.6 of its height)");
        log.Near(500f, ClippingTypography.StrokeWidth(500f, 100f, 1f), 1e-4f, "…and ends on the whole line");
        log.Near(40f, ClippingTypography.StrokeWidth(40f, 100f, 0f), 1e-4f, "…never wider than a short phrase");
        Color full = ClippingTypography.StrokeColor(1f, 1f);
        log.IsTrue(full == DocumentTypography.Marker, "a finished stroke is the Document card's marker colour");
        log.AreEqual(0f, ClippingTypography.StrokeColor(0f, 1f).a, "…clear before it starts");
        log.Near(full.a, ClippingTypography.StrokeColor(0.125f, 1f).a, 1e-5f, "…and at full strength an eighth of the way in");

        // Backdrop and lift.
        log.AreEqual(0f, ClippingTypography.BackdropAt(0f), "the backdrop starts clear");
        log.Near(0.5f, ClippingTypography.BackdropAt(0.1f), 1e-5f, "…is half up at 0.1 s");
        log.AreEqual(1f, ClippingTypography.BackdropAt(0.2f), "…and covers the frame after 0.2 s");
        Color back = ClippingTypography.Backdrop;
        log.IsTrue(back.a == 1f && back.r == ContentCardUIBuilder.BackgroundColor.r && back.g == ContentCardUIBuilder.BackgroundColor.g &&
                   back.b == ContentCardUIBuilder.BackgroundColor.b,
            "the backdrop is the cards' panel colour, fully opaque (it hides the presenter)");
        log.AreEqual(0f, ClippingTypography.LiftAt(0f), "the lift starts at rest");
        log.Near(0.25f, ClippingTypography.LiftAt(0.125f), 1e-5f, "…accelerates (a quarter of the way at half time)");
        log.AreEqual(1f, ClippingTypography.LiftAt(ClippingTypography.LiftSeconds), "…and is done after 0.25 s");
        log.AreEqual(1f, ClippingTypography.LiftFadeAt(0f), "the pile is fully there as the lift starts");
        log.AreEqual(0f, ClippingTypography.LiftFadeAt(ClippingTypography.LiftSeconds), "…and gone when it ends");
        log.Near(0.25f, ClippingTypography.HideSeconds, 1e-6f, "the close takes 0.25 s");
    }

    static void Clipping_PushInAndTint(Log log)
    {
        log.AreEqual(1f, ClippingTypography.PushInScaleAt(0f), "the push-in starts at 1.00 as the sheet lands");
        log.AreEqual(1f, ClippingTypography.PushInScaleAt(-0.2f), "…not before (a dropping sheet is 1.00)");
        log.Near(1.012f, ClippingTypography.PushInScaleAt(1f), 1e-6f, "…grows 1.2 % in the first second");
        log.Near(0.012f, ClippingTypography.PushInScaleAt(4f) - ClippingTypography.PushInScaleAt(3f), 1e-5f, "…and 1.2 % in every later one");
        log.Near(1.06f, ClippingTypography.PushInScaleAt(5f), 1e-5f, "…1.06 after five seconds");
        log.IsTrue(ClippingTypography.PushInScaleAt(6.6f) < ClippingTypography.PushInMaxScale, "…still growing at 6.6 s");
        log.Near(1.08f, ClippingTypography.PushInScaleAt(6.7f), 1e-6f, "…capped at 1.08 from 6.7 s");
        log.Near(1.08f, ClippingTypography.PushInScaleAt(600f), 1e-6f, "…for as long as the sheet stays up");

        log.Near(1.05f, ClippingTypography.CoveredScaleAt(1.05f, 0f), 1e-6f, "a sheet being covered starts from the scale it had");
        log.Near(1f, ClippingTypography.CoveredScaleAt(1.05f, 1f), 1e-6f, "…and is back at 1.00 when its cover has landed");
        float mid = ClippingTypography.CoveredScaleAt(1.05f, 0.5f);
        log.Near(1.025f, mid, 1e-5f, "…easing between the two (no jump)");
        log.Near(1f, ClippingTypography.CoveredScaleAt(1f, 0.3f), 1e-6f, "a sheet covered before it pushed in just stays 1.00");

        log.AreEqual(1f, ClippingTypography.TintAt(-1f), "a highlight no mark has moved on from is at full strength");
        log.AreEqual(1f, ClippingTypography.TintAt(0f), "…still so on the frame the mark arrives");
        log.Near(0.7f, ClippingTypography.TintAt(0.1f), 1e-5f, "…halfway to its tint at 0.1 s");
        log.Near(0.4f, ClippingTypography.TintAt(0.2f), 1e-6f, "…at 40 % after 0.2 s");
        log.Near(0.4f, ClippingTypography.TintAt(30f), 1e-6f, "…where it stays");
        log.Near(DocumentTypography.Marker.a * 0.4f, ClippingTypography.StrokeColor(1f, ClippingTypography.TintAt(1f)).a, 1e-6f,
            "the pale tint is the marker colour at 40 % of its alpha");
        log.Near(0.8f, ClippingTypography.MinReadSeconds, 1e-6f, "a sheet covered within 0.8 s of landing is too short to read");
    }

    static void Clipping_PileGeometry(Log log)
    {
        // Tilt: a degree or two, the other way each time, the same every take.
        var want = new[] { -1.6f, 1.4f, -1.2f, 1.7f, -1.5f };
        for (int i = 0; i < ClippingTypography.MaxSheets; i++)
        {
            float t = ClippingTypography.Tilt(i);
            log.Near(want[i], t, 1e-6f, $"sheet {i} is tilted {want[i]:+0.0;-0.0}°");
            log.IsTrue(Mathf.Abs(t) >= 1f && Mathf.Abs(t) <= 2f, $"…a degree or two ({t})");
            log.AreEqual(t, ClippingTypography.Tilt(i), $"…the same every time it is asked (sheet {i})");
            if (i > 0)
                log.IsTrue(Mathf.Sign(t) != Mathf.Sign(ClippingTypography.Tilt(i - 1)), $"sheet {i} leans the other way from sheet {i - 1}");
        }

        // Which sheets the pile shows.
        var firstVisible = new[] { 0, 0, 0, 0, 1, 2 };
        for (int n = 1; n <= ClippingTypography.MaxSheets; n++)
        {
            log.AreEqual(Mathf.Min(n, 3), ClippingTypography.VisibleCount(n), $"{n} sheet(s): {Mathf.Min(n, 3)} in the pile");
            log.AreEqual(firstVisible[n], ClippingTypography.FirstVisible(n), $"{n} sheet(s): the oldest one shown is sheet {firstVisible[n]}");
            int shown = 0;
            for (int i = 0; i < n; i++)
            {
                bool vis = ClippingTypography.IsVisible(i, n);
                if (vis) shown++;
                log.AreEqual(i >= n - 3, vis, $"{n} sheet(s): sheet {i} is {(i >= n - 3 ? "in" : "off")} the pile");
            }
            log.AreEqual(ClippingTypography.VisibleCount(n), shown, $"{n} sheet(s): the count agrees");
            log.IsTrue(!ClippingTypography.IsVisible(n, n), $"{n} sheet(s): a sheet not added yet is not shown");
            log.AreEqual(0, ClippingTypography.Depth(n - 1, n), $"{n} sheet(s): nothing lies on the top one");
        }
        log.AreEqual(0, ClippingTypography.VisibleCount(0), "no sheets, nothing in the pile");
        log.AreEqual(2, ClippingTypography.Depth(0, 3), "two sheets lie on the first of three");

        // The shade: 25 % per sheet on top.
        log.AreEqual(0f, ClippingTypography.DimForDepth(0f), "the top sheet is not shaded");
        log.Near(0.25f, ClippingTypography.DimForDepth(1f), 1e-6f, "one sheet on top: 25 %");
        log.Near(0.50f, ClippingTypography.DimForDepth(2f), 1e-6f, "two: 50 %");
        log.Near(0.125f, ClippingTypography.DimForDepth(0.5f), 1e-6f, "half-landed cover: half the shade (it comes on with the drop)");
        log.IsTrue(ClippingTypography.DimForDepth(9f) < 1f, "never a solid black sheet");

        // The sheet in the frame.
        const float W = 1920f, H = 1080f;
        log.Near(0.76f * W, ClippingTypography.SheetWidth(W), 0.01f, "a sheet is 76 % of the frame's width");
        log.Near(400f, ClippingTypography.SheetHeight(400f, H), 1e-4f, "…as tall as its content");
        log.Near(0.70f * H, ClippingTypography.SheetHeight(2000f, H), 0.01f, "…up to 70 % of the frame's height");
        Rect r = ClippingTypography.SheetRect(W, H, 500f);
        log.Near(W * 0.5f, r.center.x, 0.01f, "a sheet is centred (x)");
        log.Near(H * 0.5f, r.center.y, 0.01f, "…(y)");
        log.Near(1f, ClippingTypography.ScaleFor(1080f), 1e-6f, "sizes are px of a 1080-px frame");
        log.Near(2f, ClippingTypography.ScaleFor(2160f), 1e-6f, "…doubled on a 4K one");

        // The start of a drop: wholly above the frame, tilt and shadow included.
        float sheetW = ClippingTypography.SheetWidth(W), sheetH = ClippingTypography.SheetMaxHeight(H);
        log.Near(sheetH, ClippingTypography.RotatedHeight(sheetW, sheetH, 0f), 1e-3f, "an untilted sheet's bounds are its own height");
        float tall = ClippingTypography.RotatedHeight(sheetW, sheetH, 1.7f);
        log.IsTrue(tall > sheetH + 30f && tall < sheetH + 60f, $"a 1.7° tilt adds ~43 px to its bounds ({tall - sheetH:F1})");
        log.Near(tall, ClippingTypography.RotatedHeight(sheetW, sheetH, -1.7f), 1e-3f, "…whichever way it leans");
        for (int i = 0; i < ClippingTypography.MaxSheets; i++)
        {
            float tilt = ClippingTypography.Tilt(i);
            float bounds = ClippingTypography.RotatedHeight(sheetW, sheetH, tilt);
            // Frame space, y down: the rest centre is H/2; the waiting sheet is `offset` higher.
            float bottom = H * 0.5f - ClippingTypography.DropOffset(sheetW, sheetH, tilt, H) + bounds * 0.5f;
            log.IsTrue(bottom < -(ContentCardUIBuilder.ShadowGrowPx + ContentCardUIBuilder.ShadowOffsetYPx),
                $"sheet {i} waits wholly above the frame, shadow included (its lowest point at y = {bottom:F0})");
            log.IsTrue(bottom > -100f, $"…and not far above it (sheet {i})");
        }

        // The lift: past the top edge even for the tallest sheet at its largest push-in.
        float gone = H * 0.5f + tall * ClippingTypography.PushInMaxScale * 0.5f - ClippingTypography.LiftDistance(H);
        log.IsTrue(gone < -(ContentCardUIBuilder.ShadowGrowPx + ContentCardUIBuilder.ShadowOffsetYPx),
            $"the lift carries the tallest, most pushed-in sheet out of the frame (its lowest point ends at y = {gone:F0})");
    }

    static void Clipping_Limits(Log log)
    {
        log.AreEqual(5, ClippingTypography.MaxSheets, "a run holds at most five sheets");
        log.AreEqual(3, ClippingTypography.MaxVisibleSheets, "…of which the pile shows three");
        log.AreEqual(3, ClippingTypography.MaxMarksPerSheet, "a sheet takes three marks on top of its own highlight");
        log.Near(110f, ClippingTypography.HeadlineMaxPx, 1e-6f, "the headline is at most 110 px");
        log.Near(64f, ClippingTypography.HeadlineMinPx, 1e-6f, "…and warns under 64 px");
        log.AreEqual(3, ClippingTypography.HeadlineMaxLines, "…on at most three lines");
        log.IsTrue(ClippingTypography.HeadlineMinPx >= CardTypography.HardFloorPx, "the headline's floor is above the 32 px hard floor");
        log.Near(40f, ClippingTypography.SourcePx, 1e-6f, "the source is 40 px");
        log.IsTrue(ClippingTypography.SourcePx * ClippingTypography.SmallCapsScale >= CardTypography.HardFloorPx - 1e-4f,
            $"…so its small-caps letters stay at 32 px ({ClippingTypography.SourcePx * ClippingTypography.SmallCapsScale:F1})");
        log.Near(DocumentTypography.HeaderTrackingEm, ClippingTypography.SourceTrackingEm, 1e-6f, "…tracked like the Document header");
        log.IsTrue(ClippingTypography.Paper == DocumentTypography.Paper && ClippingTypography.Ink == DocumentTypography.Ink &&
                   ClippingTypography.SourceInk == DocumentTypography.HeaderInk && ClippingTypography.Rule == DocumentTypography.HeaderRule &&
                   ClippingTypography.Marker == DocumentTypography.Marker,
            "paper, ink, source ink, rule and marker are the Document card's");
        // Two sheets side by side in height: the tallest content the fit allows still leaves the frame's margins.
        log.IsTrue(ClippingTypography.SheetWidthFraction * ClippingTypography.PushInMaxScale < 0.9f &&
                   ClippingTypography.SheetMaxHeightFraction * ClippingTypography.PushInMaxScale < 0.9f,
            "even fully pushed in, a sheet stays inside title-safe");
    }

    static void Clipping_MarkRules(Log log)
    {
        const string h = "Binance freezes assets after US$6.3 billion heist";
        log.AreEqual(29, ClippingTypography.FindPhrase(h, "US$6.3 billion heist"), "a phrase is found where it starts");
        log.AreEqual(0, ClippingTypography.FindPhrase(h, "Binance"), "…at the very start too");
        log.AreEqual(-1, ClippingTypography.FindPhrase(h, "binance"), "…exactly as written: case counts");
        log.AreEqual(-1, ClippingTypography.FindPhrase(h, "US$6.3 billion robbery"), "a phrase that isn't there isn't found");
        log.AreEqual(-1, ClippingTypography.FindPhrase(h, ""), "an empty phrase is never found");
        log.AreEqual(-1, ClippingTypography.FindPhrase(h, "  "), "…nor one of spaces");
        log.AreEqual(-1, ClippingTypography.FindPhrase(h, null), "…nor none at all");
        log.AreEqual(-1, ClippingTypography.FindPhrase(null, "heist"), "no headline, nothing found");

        var own = new List<string> { "US$6.3 billion heist" };
        log.AreEqual(MarkResult.Ok, ClippingTypography.JudgeMark(h, "freezes assets", own, 0), "a new phrase of the headline: Ok");
        log.AreEqual(MarkResult.NotFound, ClippingTypography.JudgeMark(h, "freezes the assets", own, 0), "a phrase that isn't in it: NotFound");
        log.AreEqual(MarkResult.NotFound, ClippingTypography.JudgeMark(h, "", own, 0), "an empty phrase: NotFound");
        log.AreEqual(MarkResult.Repeated, ClippingTypography.JudgeMark(h, "US$6.3 billion heist", own, 0), "the tag's own phrase again: Repeated");
        var three = new List<string> { "US$6.3 billion heist", "Binance", "freezes", "assets" };
        log.AreEqual(MarkResult.Repeated, ClippingTypography.JudgeMark(h, "freezes", three, 3), "an earlier mark again: Repeated (even on a full sheet)");
        log.AreEqual(MarkResult.TooMany, ClippingTypography.JudgeMark(h, "after", three, 3), "a 4th mark: TooMany");
        log.AreEqual(MarkResult.NotFound, ClippingTypography.JudgeMark(h, "not there", three, 3), "…but NotFound comes first");
        log.AreEqual(MarkResult.Ok, ClippingTypography.JudgeMark(h, "after", new List<string> { "Binance", "freezes" }, 2),
            "a 3rd mark on a sheet whose own phrase wasn't found: Ok");
        log.AreEqual(MarkResult.Ok, ClippingTypography.JudgeMark(h, "heist", null, 0), "no highlights yet: Ok");
        log.AreEqual(MarkResult.Ok, ClippingTypography.JudgeMark(h, "billion heist", own, 0),
            "a phrase inside an earlier one is a phrase of its own: Ok");
    }

    static void Clipping_HeadlineFit(Log log)
    {
        // A stand-in for TMP: a headline `em` wide wraps onto ceil(em × size / width) lines.
        const float innerW = 1920f * 0.76f - 2f * 84f;
        Func<float, bool> ThreeLines(float em, float width) => size => Mathf.CeilToInt(em * size / width) <= 3;

        CardTypography.BodyFit small = ClippingTypography.FitHeadline(ThreeLines(11.5f, innerW), 1f);
        log.AreEqual(110f, small.Size, "a 3-word headline takes the full 110 px");
        log.IsTrue(!small.BelowWarnFloor && !small.Overflows, "…with no warning");

        CardTypography.BodyFit mid = ClippingTypography.FitHeadline(ThreeLines(44.3f, innerW), 1f);
        log.AreEqual(87f, mid.Size, "a 12-word headline gets the largest whole size that fills three lines (87 px)");
        log.IsTrue(!mid.BelowWarnFloor && !mid.Overflows, "…inside 64–110, no warning");

        CardTypography.BodyFit edge = ClippingTypography.FitHeadline(ThreeLines(60.5f, innerW), 1f);
        log.AreEqual(64f, edge.Size, "a headline that just fits at 64 px stays at 64");
        log.IsTrue(!edge.BelowWarnFloor && !edge.Overflows, "…the floor itself is not a warning");

        CardTypography.BodyFit under = ClippingTypography.FitHeadline(ThreeLines(80f, innerW), 1f);
        log.AreEqual(48f, under.Size, "a longer one keeps shrinking under 64 px (48 px)");
        log.IsTrue(under.BelowWarnFloor && !under.Overflows, "…and is flagged for a warning");

        CardTypography.BodyFit over = ClippingTypography.FitHeadline(ThreeLines(200f, innerW), 1f);
        log.AreEqual(32f, over.Size, "nothing goes under the 32 px hard floor");
        log.IsTrue(over.Overflows, "…a headline that needs more than three lines there is flagged as overflowing");

        CardTypography.BodyFit uhd = ClippingTypography.FitHeadline(ThreeLines(11.5f, innerW * 2f), 2f);
        log.AreEqual(220f, uhd.Size, "on a 4K frame the same headline gets 220 px (110 at 1080p)");
        CardTypography.BodyFit uhdOver = ClippingTypography.FitHeadline(ThreeLines(200f, innerW * 2f), 2f);
        log.AreEqual(64f, uhdOver.Size, "…and its hard floor is 64 px (32 at 1080p)");
    }

    static void Clipping_MarkerGeometry(Log log)
    {
        // A 110 px headline whose box hangs from (84, 52) in its sheet. Noto
        // Serif: ascender 1.069 em, descender 0.293 em, capitals 0.714 em; the
        // card sets 1.262 em between baselines (line height 1.362 − 0.10).
        const float size = 110f, left = 84f, top = 52f;
        float baseline0 = -1.069f * size;
        float pitch = (1.362f + ClippingTypography.HeadlineLineSpacing * 0.01f) * size;
        float baseline1 = baseline0 - pitch;
        Rect m0 = ClippingTypography.MarkerRect(left, top, 100f, 400f, baseline0, size);
        Rect m1 = ClippingTypography.MarkerRect(left, top, 0f, 250f, baseline1, size);

        log.Near(left + 100f - 0.12f * size, m0.xMin, 1e-3f, "a stroke starts a little before the phrase");
        log.Near(left + 400f + 0.12f * size, m0.xMax, 1e-3f, "…and ends a little after it");
        log.Near(1.02f * size, m0.height, 1e-3f, "…from just under the baseline to just over the ascenders (1.02 em)");
        float baselineY = top - baseline0; // sheet space, y down
        log.IsTrue(m0.yMin < baselineY - 0.714f * size && m0.yMax > baselineY, "…covering the capitals and the baseline");
        log.IsTrue(m0.yMin >= top, "the first line's stroke stays inside the headline's box (top)");
        float boxBottom = top - baseline1 + 0.293f * size; // a two-line box ends at the last line's descender
        log.IsTrue(m1.yMax <= boxBottom, "…and the last line's too (bottom)");
        log.IsTrue(m1.yMin > m0.yMax, $"strokes on two lines never touch ({m1.yMin - m0.yMax:F1} px apart)");
        log.IsTrue(m0.xMin > 0f, "a stroke at the left edge stays on the sheet (the padding is wider than its overhang)");
        log.IsTrue(ClippingTypography.SheetPadXPx > (ClippingTypography.MarkerOverhangEm + 0.1f) * ClippingTypography.HeadlineMaxPx,
            "…with room for TMP's glyph padding as well, at the largest size");
    }

    static void Clipping_PhraseWarningText(Log log)
    {
        log.AreEqual(
            "[Card] Clipping \"Binance freezes assets after US$6.3 billion heist\": the phrase \"frozen\" isn't in the headline " +
            "(it must match exactly) — shown with no highlight.",
            ClippingTypography.PhraseWarning("Binance freezes assets after US$6.3 billion heist", "frozen"),
            "the phrase warning reads as agreed");

        const string longHeadline = "Tech Giant Quietly Changes Privacy Policy After Regulators Demand Answers Over Data Sharing With Advertisers";
        string shortened = ClippingTypography.ShortHeadline(longHeadline);
        log.IsTrue(shortened.Length <= ClippingTypography.LogHeadlineChars + 1 && shortened.EndsWith("…"),
            $"a long headline is cut to 60 characters in the log (\"{shortened}\")");
        log.IsTrue(longHeadline.StartsWith(shortened.TrimEnd('…')), "…its own first words, nothing else");
        log.IsTrue(ClippingTypography.PhraseWarning(longHeadline, "x").Contains("\"" + shortened + "\""), "…in the warning too");
        log.AreEqual("Short one", ClippingTypography.ShortHeadline("  Short\none "), "a short headline is kept whole, on one line");
        log.AreEqual("", ClippingTypography.ShortHeadline(null), "no headline, no crash");
    }

    static void Clipping_StrokeAge(Log log)
    {
        // The tag's own highlight: 0.25 s after the landing.
        log.Near(-0.25f, ClippingTypography.StrokeAge(true, false, 0f, 99f), 1e-6f, "the tag's highlight is 0.25 s from starting as the sheet lands");
        log.Near(0f, ClippingTypography.StrokeAge(true, false, 0.25f, 99f), 1e-6f, "…and starts 0.25 s later");
        log.AreEqual(0f, ClippingTypography.SweepAt(ClippingTypography.StrokeAge(true, false, -0.1f, 0f), 0), "…never while the sheet is still dropping");
        log.AreEqual(1f, ClippingTypography.SweepAt(ClippingTypography.StrokeAge(true, true, 0f, 0f), 2),
            "on a sheet that landed \"instant\" it is already drawn, every line");
        // A mark: from the moment it is asked for, but not before the sheet has landed.
        log.Near(0.1f, ClippingTypography.StrokeAge(false, false, 5f, 0.1f), 1e-6f, "a mark counts from the moment it was asked for");
        log.Near(0.1f, ClippingTypography.StrokeAge(false, true, 5f, 0.1f), 1e-6f, "…on an \"instant\" sheet too (only the tag's own highlight is pre-drawn)");
        log.Near(-0.1f, ClippingTypography.StrokeAge(false, false, -0.1f, 0.05f), 1e-6f, "…but waits for a sheet that is still dropping");
        log.Near(0.02f, ClippingTypography.StrokeAge(false, false, 0.02f, 0.2f), 1e-6f, "…and then starts from the landing");
    }

    // The run's clockwork on its own (no engine): 1 → 5 sheets at 30 fps, as
    // the card steps it. The engine tests below check the card draws this.
    static void Clipping_PileRunOfFive(Log log)
    {
        var pile = new ClippingTypography.Pile();
        void Step(int frames) { for (int f = 0; f < frames; f++) pile.Advance(Frame); }

        // ---- Before Show: the first sheet waits ----
        log.IsTrue(pile.Add(false, out bool landing) && !landing, "pile: the first sheet is taken and waits for Show (no landing yet, no sound)");
        log.IsTrue(pile.Count == 1 && !pile.IsShown && pile.Age(0) < 0f && !pile.Landed(0), "…it has not started");
        Step(10);
        log.IsTrue(pile.DropProgress(0) == 0f && pile.BackdropAlpha == 0f && pile.TopAge == 0f, "…however long Show takes to come");

        // ---- Show: backdrop up in 0.2 s, sheet down in 0.25 s ----
        log.IsTrue(pile.Show(), "Show starts the first sheet's landing (the paper sound)");
        log.IsTrue(!pile.Show(), "…a second Show starts nothing new");
        log.IsTrue(pile.DropProgress(0) == 0f && pile.BackdropAlpha == 0f && pile.TopAge == 0f, "Show's own frame is the start pose");
        int landedOn = -1, backdropOn = -1;
        float lastDrop = 0f;
        bool rising = true;
        for (int f = 1; f <= 60; f++)
        {
            pile.Advance(Frame);
            if (pile.DropProgress(0) < lastDrop) rising = false;
            lastDrop = pile.DropProgress(0);
            if (landedOn < 0 && pile.Landed(0)) landedOn = f;
            if (backdropOn < 0 && pile.BackdropAlpha >= 1f - 1e-4f) backdropOn = f;
            if (!pile.Landed(0)) log.AreEqual(1f, pile.Scale(0), $"a dropping sheet is 1.00 (frame {f})");
        }
        log.AreEqual(8, landedOn, "the sheet lands on the 8th frame (0.25 s at 30 fps)");
        log.IsTrue(backdropOn == 6 || backdropOn == 7, $"the backdrop covers the frame after 0.2 s (frame {backdropOn})");
        log.IsTrue(rising && pile.DropProgress(0) == 1f, "the drop only goes down, and ends at rest");
        log.Near(2f, pile.TopAge, 1e-3f, "TopAge counts from the start of the drop");
        float pushed = pile.Scale(0);
        log.Near(1f + 0.012f * 1.75f, pushed, 1e-4f, "two seconds in, the sheet has pushed in for 1.75 s (1.021)");
        log.IsTrue(pile.Dim(0) == 0f && pile.Drawn(0) && pile.DrawnCount == 1, "one sheet: drawn, not shaded");

        // ---- A second lands on it ----
        log.IsTrue(pile.Add(false, out landing) && landing, "a second sheet is taken and starts landing at once (the paper sound)");
        log.IsTrue(pile.Count == 2 && pile.TopAge == 0f && pile.DropProgress(1) == 0f, "…from its start pose");
        log.Near(pushed, pile.Scale(0), 1e-6f, "…the sheet under it holds its scale on that frame (no jump)");
        log.AreEqual(0f, pile.Dim(0), "…and is not shaded yet");
        pile.Advance(Frame);
        log.IsTrue(pile.Dim(0) > 0f && pile.Dim(0) < 0.25f && pile.Scale(0) < pushed && pile.Scale(0) > 1f,
            "as the cover comes down the sheet under it darkens and eases back");
        Step(59);
        log.Near(1f, pile.Scale(0), 1e-6f, "2 sheets: the covered one is back at 1.00");
        log.Near(0.25f, pile.Dim(0), 1e-6f, "2 sheets: …shaded 25 %");
        log.IsTrue(pile.Dim(1) == 0f && pile.Scale(1) > 1.015f, "2 sheets: the top one is clear and pushing in");

        // ---- Three ----
        log.IsTrue(pile.Add(false, out landing) && landing, "a third sheet is taken");
        Step(60);
        log.IsTrue(pile.Count == 3 && pile.DrawnCount == 3, "3 sheets: all three drawn");
        log.Near(0.50f, pile.Dim(0), 1e-6f, "3 sheets: shaded 50 %");
        log.Near(0.25f, pile.Dim(1), 1e-6f, "3 sheets: …25 %");
        log.AreEqual(0f, pile.Dim(2), "3 sheets: …and not at all, from the bottom");
        log.IsTrue(pile.Scale(0) == 1f && Mathf.Abs(pile.Scale(1) - 1f) < 1e-6f, "3 sheets: covered sheets are 1.00");

        // ---- A 4th: the oldest goes the moment it has landed ----
        log.IsTrue(pile.Add(false, out landing) && landing, "a 4th sheet is taken");
        log.AreEqual(4, pile.DrawnCount, "4 sheets: while the 4th drops the oldest is still under the pile");
        landedOn = -1;
        int hiddenOn = -1;
        for (int f = 1; f <= 15; f++)
        {
            pile.Advance(Frame);
            if (landedOn < 0 && pile.Landed(3)) landedOn = f;
            if (hiddenOn < 0 && !pile.Drawn(0)) hiddenOn = f;
            if (f == 1) log.IsTrue(pile.DrawnCount == 4 && pile.Dim(0) > 0.5f && pile.Dim(0) < 0.75f, "…getting darker as it comes down");
        }
        log.AreEqual(8, landedOn, "4 sheets: the 4th lands on the 8th frame");
        log.AreEqual(8, hiddenOn, "4 sheets: …and the oldest is hidden on that very frame, not before");
        log.IsTrue(pile.DrawnCount == 3 && !pile.Drawn(0) && pile.Drawn(1) && pile.Drawn(2) && pile.Drawn(3), "4 sheets: sheets 1–3 are the pile");
        log.IsTrue(Mathf.Abs(pile.Dim(1) - 0.5f) < 1e-6f && Mathf.Abs(pile.Dim(2) - 0.25f) < 1e-6f && pile.Dim(3) == 0f, "4 sheets: the shades move down the pile");
        Step(60);

        // ---- Five, and no sixth ----
        log.IsTrue(pile.Add(false, out landing) && landing, "a 5th sheet is taken");
        Step(60);
        log.IsTrue(pile.Count == 5 && pile.DrawnCount == 3 && !pile.Drawn(0) && !pile.Drawn(1) && pile.Drawn(2) && pile.Drawn(3) && pile.Drawn(4),
            "5 sheets: sheets 2–4 are the pile");
        log.IsTrue(!pile.Add(false, out landing) && !landing, "a 6th sheet is refused");
        log.IsTrue(!pile.Add(true, out landing) && !landing, "…instant or not");
        log.IsTrue(pile.Count == 5 && pile.DrawnCount == 3 && pile.Dim(4) == 0f, "…and nothing changes");
        log.IsTrue(!pile.Drawn(5) && !pile.Landed(5) && !pile.Drawn(-1), "a sheet that was never added is neither drawn nor landed");

        // ---- The push-in stops at 1.08 ----
        Step(240);
        log.Near(ClippingTypography.PushInMaxScale, pile.Scale(4), 1e-6f, "the top sheet's push-in stops at 1.08 (10 s in)");
        log.Near(1f, pile.Scale(3), 1e-6f, "…and only the top sheet pushes in");

        // ---- A fresh run on the same clock ----
        float clock = pile.Clock;
        pile.Reset();
        log.IsTrue(pile.Count == 0 && !pile.IsShown && !pile.IsLifting && pile.BackdropAlpha == 0f && pile.TopAge == 0f && pile.Clock == clock,
            "Reset empties the run and keeps the clock");
    }

    static void Clipping_PileInstantAndQueued(Log log)
    {
        void Step(ClippingTypography.Pile p, int frames) { for (int f = 0; f < frames; f++) p.Advance(Frame); }

        // A sheet whose beat passed while the card waited: on the pile at once.
        var pile = new ClippingTypography.Pile();
        pile.Add(false, out _);
        pile.Show();
        Step(pile, 60);
        log.IsTrue(pile.Add(true, out bool landing) && !landing, "instant: the sheet is taken, with no landing (no sound)");
        log.IsTrue(pile.Landed(1) && pile.IsInstant(1) && !pile.IsInstant(0), "instant: it has landed at once");
        log.Near(ClippingTypography.DropSeconds, pile.TopAge, 1e-4f, "instant: its age starts where a landing ends (0.25 s)");
        log.Near(1f, pile.DropProgress(1), 1e-4f, "instant: it is at rest");
        log.Near(1f, pile.Scale(1), 1e-4f, "instant: its push-in starts from 1.00");
        log.Near(0.25f, pile.Dim(0), 1e-4f, "instant: the sheet under it is shaded at once");
        log.Near(1f, pile.Scale(0), 1e-4f, "instant: …and back at 1.00 at once");
        Step(pile, 30);
        log.Near(1.012f, pile.Scale(1), 1e-4f, "instant: a second later it has pushed in 1.2 %");

        // Sheets added before Show (late ones, instant or not) drop in as one pile.
        var queued = new ClippingTypography.Pile();
        log.IsTrue(queued.Add(false, out landing) && !landing && queued.Add(true, out landing) && !landing && queued.Add(false, out landing) && !landing,
            "before Show: three sheets wait together, none landing");
        Step(queued, 5);
        log.IsTrue(queued.Age(0) < 0f && queued.Age(1) < 0f && queued.Age(2) < 0f && queued.TopAge == 0f, "before Show: nothing has started");
        log.IsTrue(queued.Show(), "Show: the pile starts landing (one sound)");
        log.IsTrue(queued.DropProgress(0) == 0f && queued.DropProgress(1) == 0f && queued.DropProgress(2) == 0f, "Show: every sheet from its start pose");
        Step(queued, 7);
        log.IsTrue(!queued.Landed(0) && !queued.Landed(2), "Show: still dropping on the 7th frame");
        Step(queued, 1);
        log.IsTrue(queued.Landed(0) && queued.Landed(1) && queued.Landed(2), "Show: the pile lands as one on the 8th");
        Step(queued, 30);
        log.IsTrue(queued.DrawnCount == 3 && Mathf.Abs(queued.Dim(0) - 0.5f) < 1e-6f && Mathf.Abs(queued.Dim(1) - 0.25f) < 1e-6f && queued.Dim(2) == 0f,
            "…shaded 50 % / 25 % / none from the bottom");
        log.IsTrue(queued.Scale(2) > 1.01f && queued.Scale(1) == 1f && queued.Scale(0) == 1f, "…the push-in on the top one only");

        // Five waiting sheets: all five come down, then the pile keeps three.
        var five = new ClippingTypography.Pile();
        for (int i = 0; i < 5; i++) five.Add(i > 0, out _);
        five.Show();
        Step(five, 1);
        log.AreEqual(5, five.DrawnCount, "five queued sheets all come down together");
        Step(five, 7);
        log.IsTrue(five.DrawnCount == 3 && !five.Drawn(0) && !five.Drawn(1) && five.Drawn(4), "…and the pile keeps the newest three once they have landed");
    }

    static void Clipping_PileLift(Log log)
    {
        void Step(ClippingTypography.Pile p, int frames) { for (int f = 0; f < frames; f++) p.Advance(Frame); }

        var pile = new ClippingTypography.Pile();
        pile.Add(false, out _);
        pile.Show();
        Step(pile, 30);
        pile.Add(false, out _);
        Step(pile, 30);
        log.IsTrue(!pile.IsLifting && pile.Lift == 0f && pile.PileAlpha == 1f && pile.BackdropAlpha == 1f, "an open run: no lift, pile and backdrop fully there");
        log.IsTrue(!pile.TakeLiftDone(), "…and nothing to report");

        pile.StartLift();
        log.IsTrue(pile.IsLifting && pile.Lift == 0f && pile.PileAlpha == 1f && pile.BackdropAlpha == 1f, "the lift's first frame is the pile as it was (no jump)");
        Step(pile, 3);
        float lifted = pile.Lift;
        log.Near(0.16f, lifted, 1e-4f, "0.1 s in the pile is 16 % of the way up (accelerating)");
        log.IsTrue(pile.PileAlpha < 1f && pile.PileAlpha > 0f && Mathf.Abs(pile.BackdropAlpha - pile.PileAlpha) < 1e-6f, "…fading, the backdrop with it");
        pile.StartLift();
        log.AreEqual(lifted, pile.Lift, "asked again, the lift doesn't start over");
        int doneOn = -1, reports = 0;
        for (int f = 4; f <= 30; f++)
        {
            pile.Advance(Frame);
            if (pile.TakeLiftDone()) { reports++; if (doneOn < 0) doneOn = f; }
        }
        log.AreEqual(8, doneOn, "the close is reported on the 8th frame (0.25 s at 30 fps)");
        log.AreEqual(1, reports, "…once");
        log.IsTrue(pile.Lift == 1f && pile.PileAlpha == 0f && pile.BackdropAlpha == 0f, "…with the pile lifted out and the backdrop gone");
        pile.StartLift();
        log.IsTrue(pile.Lift == 1f && pile.TakeLiftDone() && !pile.TakeLiftDone(), "a Hide after the lift is over is answered at once, without lifting again");

        // Closed while the backdrop is still fading in: it fades out from where it was.
        var early = new ClippingTypography.Pile();
        early.Add(false, out _);
        early.Show();
        Step(early, 3);
        float half = early.BackdropAlpha;
        log.Near(0.5f, half, 1e-3f, "0.1 s after Show the backdrop is half up");
        early.StartLift();
        log.Near(half, early.BackdropAlpha, 1e-6f, "closed then, it fades out from there (no jump to full)");
        Step(early, 8);
        log.IsTrue(early.BackdropAlpha == 0f && early.TakeLiftDone(), "…and is gone 0.25 s later");

        // A fast hide reports through its own fade; a slow hide after it still lifts.
        var fast = new ClippingTypography.Pile();
        fast.Add(false, out _);
        fast.Show();
        Step(fast, 30);
        fast.MuteLiftReport();
        Step(fast, 30);
        log.IsTrue(!fast.IsLifting && !fast.TakeLiftDone() && fast.Lift == 0f && fast.PileAlpha == 1f, "a fast hide never lifts the pile nor reports through it");
        fast.StartLift();
        Step(fast, 8);
        log.IsTrue(fast.TakeLiftDone(), "a slow hide asked for after it lifts and reports");
        fast.MuteLiftReport();
        Step(fast, 8);
        log.IsTrue(!fast.TakeLiftDone(), "a fast hide during a lift silences the lift's report");
    }

    // A tag that comes due BEFORE the card's own Update in a frame is posed at
    // its start and must still show that start pose when the frame is drawn:
    // the card moves this frame's starts along with the clock (Pile.Reanchor).
    static void Clipping_PileReanchor(Log log)
    {
        const float dt = Frame;
        var pile = new ClippingTypography.Pile();
        pile.Add(false, out _);
        pile.Show();
        for (int f = 0; f < 45; f++) pile.Advance(dt);
        float age0 = pile.Age(0);

        // The controller's Update adds a sheet; then the card's Update runs in the same frame.
        pile.Add(false, out _);
        pile.Reanchor(pile.Clock, pile.Clock + dt);
        pile.Advance(dt);
        log.AreEqual(0f, pile.Age(1), "a sheet added earlier in the frame is still at its start pose when that frame is drawn");
        log.AreEqual(0f, pile.DropProgress(1), "…wholly above the frame");
        log.Near(age0 + dt, pile.Age(0), 1e-5f, "…while everything older moves on by the frame");
        pile.Advance(dt);
        log.Near(dt, pile.Age(1), 1e-6f, "…and it is one frame into its drop on the next");

        // Added after the card's Update (no re-anchoring): the same two frames.
        pile.Add(false, out _);
        log.AreEqual(0f, pile.Age(2), "a sheet added after the card's Update is drawn at its start pose that frame");
        pile.Advance(dt);
        log.Near(dt, pile.Age(2), 1e-6f, "…and one frame in on the next — either way");

        // A sheet that landed "instant" has no start pose to protect.
        pile.Add(true, out _);
        pile.Reanchor(pile.Clock, pile.Clock + dt);
        pile.Advance(dt);
        log.Near(ClippingTypography.DropSeconds + dt, pile.Age(3), 1e-5f, "an instant sheet is not held back");

        // Show and the lift ride along the same way.
        var fresh = new ClippingTypography.Pile();
        fresh.Add(false, out _);
        fresh.Advance(1.5f);
        fresh.Show();
        fresh.Reanchor(fresh.Clock, fresh.Clock + dt);
        fresh.Advance(dt);
        log.IsTrue(fresh.BackdropAlpha == 0f && fresh.Age(0) == 0f, "Show earlier in the frame: backdrop clear and sheet waiting when it is drawn");
        fresh.Advance(1f);
        fresh.StartLift();
        fresh.Reanchor(fresh.Clock, fresh.Clock + dt);
        fresh.Advance(dt);
        log.IsTrue(fresh.Lift == 0f && fresh.PileAlpha == 1f, "Hide earlier in the frame: the pile still in place when it is drawn");

        float a = 2f, b = float.NaN, c = 3f;
        ClippingTypography.Shift(ref a, 2f, 2.5f);
        ClippingTypography.Shift(ref b, 2f, 2.5f);
        ClippingTypography.Shift(ref c, 2f, 2.5f);
        log.IsTrue(a == 2.5f && float.IsNaN(b) && c == 3f, "only a start stamped at exactly that moment moves (a waiting one, or an older one, doesn't)");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    const string ClipA = "Binance freezes assets after US$6.3 billion heist";
    const string ClipAPhrase = "US$6.3 billion heist";
    const string ClipB = "Exchange halts withdrawals after record hack";
    const string ClipBPhrase = "halts withdrawals";
    const string ClipC = "Regulators open probe into exchange after record hack";
    const string ClipCPhrase = "open probe";
    // 12 words: three lines at about 78 px, the phrase running over every line break.
    const string ClipLong = "Binance freezes customer withdrawals after hackers drain eleven hot wallets overnight";
    const string ClipLongPhrase = "customer withdrawals after hackers drain eleven hot wallets";
    const string Clip3 = "Markets React Sharply";
    const string Clip16 = "Tech Giant Quietly Changes Privacy Policy After Regulators Demand Answers Over Data Sharing With Advertisers";

    /// <summary>Two seconds at 30 fps: the sheet has landed and a three-line highlight is drawn.</summary>
    const int ClipSettleFrames = 60;

    static ClippingCard BuildClipping(string headline, string phrase, string source, out GameObject root, bool show = true)
    {
        var card = BuildCard<ClippingCard>(out root);
        var zone = (RectTransform)card.transform.parent;
        zone.sizeDelta = new Vector2(1920f, 1080f);
        card.Initialize(new ContentCardEvent
        {
            cardType = ContentCardType.Clipping, primaryText = headline, secondaryText = phrase, tertiaryText = source,
        }, null);
        if (show)
        {
            card.Show();
            StepClipping(card, ClipSettleFrames);
        }
        return card;
    }

    static void StepClipping(ClippingCard card, int frames)
    {
        for (int f = 0; f < frames; f++) card.Advance(Frame);
    }

    static float ClipTilt(RectTransform sheet)
    {
        float z = sheet.localEulerAngles.z;
        return z > 180f ? z - 360f : z;
    }

    // One sheet of a card: the paper, the type and its sizes, the padding, the
    // source under its rule, and (once the strokes have had time to sweep) the
    // tag's own highlight — one stroke per line, behind the words of that line.
    static void CheckClippingSheet(Log log, ClippingCard card, int i, string headline, string phrase, string source, string what)
    {
        var rt = (RectTransform)card.transform;
        Vector2 frame = rt.rect.size;
        float k = ClippingTypography.ScaleFor(frame.y);
        RectTransform sheet = card.SheetRoot(i);
        CardTypography.Report report = card.SheetLayout(i);
        log.IsTrue(report != null, $"{what}: laid out");
        if (report == null) return;
        bool sizeWarned = report.WarnedAbout("headline");
        bool overflowWarned = report.Warnings.Exists(w => w.Contains("doesn't fit") || w.Contains("runs past"));

        // ---- The sheet ----
        Vector2 size = sheet.rect.size;
        log.Near(frame.x * ClippingTypography.SheetWidthFraction, size.x, 0.5f, $"{what}: the sheet is 76 % of the frame's width");
        log.IsTrue(size.y <= frame.y * ClippingTypography.SheetMaxHeightFraction + 0.5f && size.y > 100f * k,
            $"{what}: …and as tall as its content, at most 70 % of the frame ({size.y:F0} px)");
        Rect rest = card.SheetRestRect(i);
        log.Near(frame.x * 0.5f, rest.center.x, 0.5f, $"{what}: the sheet rests centred (x)");
        log.Near(frame.y * 0.5f, rest.center.y, 0.5f, $"{what}: …(y)");
        log.Near(size.y, rest.height, 0.5f, $"{what}: its rest rect is the sheet's own size");
        log.IsTrue(card.Paper(i).color == DocumentTypography.Paper && card.Paper(i).sprite == null,
            $"{what}: the Document card's paper, square corners");
        log.IsTrue(card.Grain(i).texture == DocumentCard.PaperGrain(), $"{what}: …with its grain");
        log.Near(size.x / DocumentCard.PaperGrainSize, card.Grain(i).uvRect.width, 1e-3f, $"{what}: …tiled at 256 px, not stretched");
        log.IsTrue(sheet.GetChild(0).name == "Shadow", $"{what}: a soft drop shadow under the sheet");

        // ---- Type ----
        TextMeshProUGUI h = card.Headline(i), s = card.Source(i);
        log.AreEqual(headline, h.text, $"{what}: the headline is the field as written");
        log.IsTrue(!h.richText, $"{what}: …as plain text (character i of the field is character i on the sheet)");
        TMP_FontAsset serifBold = CardTypography.NotoSerifBold;
        log.IsTrue(serifBold == null || (h.font == serifBold && s.font == serifBold), $"{what}: headline and source in Noto Serif Bold");
        log.IsTrue(h.color == DocumentTypography.Ink, $"{what}: the headline in the Document ink");
        log.IsTrue((h.alignment & (TextAlignmentOptions)HorizontalAlignmentOptions.Left) != 0, $"{what}: the headline is left-aligned");

        float px = card.HeadlineSize(i);
        log.Near(px * k, h.fontSize, 0.01f, $"{what}: the headline is drawn at the size the layout chose ({px:F0} px at 1080p)");
        h.ForceMeshUpdate(true, true);
        int lines = h.textInfo.lineCount;
        log.AreEqual(lines, card.HeadlineLines(i), $"{what}: the layout's line count is the drawn one");
        log.IsTrue(lines <= ClippingTypography.HeadlineMaxLines || overflowWarned, $"{what}: the headline is on {lines} line(s), at most 3");
        log.IsTrue(px <= ClippingTypography.HeadlineMaxPx + 0.01f, $"{what}: headline at most 110 px ({px:F1})");
        log.IsTrue(px >= ClippingTypography.HeadlineMinPx - 0.01f || sizeWarned, $"{what}: headline {px:F0} px is at/above 64 px or warned");
        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name),
                $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");

        float padX = ClippingTypography.SheetPadXPx * k, padTop = ClippingTypography.SheetPadTopPx * k;
        float padBottom = ClippingTypography.SheetPadBottomPx * k;
        Rect inner = Rect.MinMaxRect(padX, padTop, size.x - padX, size.y - padBottom);
        foreach (TMP_Text t in new TMP_Text[] { h, s })
        {
            if (!t.gameObject.activeSelf || string.IsNullOrEmpty(t.text)) continue;
            log.IsTrue(t.fontSize >= CardTypography.HardFloorPx * k - 0.01f, $"{what}: {t.name} at or above 32 px ({t.fontSize / k:F1})");
            log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} not truncated");
            Rect ink = CardTypography.InkInCard(t, sheet);
            log.IsTrue(overflowWarned || Inside(ink, inner, 4f * k), $"{what}: {t.name} inside the sheet's padding ({Fmt(ink)} in {Fmt(inner)})");
        }

        // ---- Source, under a thin rule ----
        if (source.Length > 0)
        {
            log.AreEqual(source, s.text, $"{what}: the source is the field as written");
            log.IsTrue((s.fontStyle & FontStyles.SmallCaps) != 0, $"{what}: …in small caps");
            log.Near(ClippingTypography.SourcePx * k, s.fontSize, 0.01f, $"{what}: …at 40 px");
            log.Near(DocumentTypography.HeaderTrackingEm, s.characterSpacing, 1e-4f, $"{what}: …tracked like the Document header");
            log.IsTrue(s.color == DocumentTypography.HeaderInk && card.Rule(i).color == DocumentTypography.HeaderRule,
                $"{what}: …in the header ink, under a rule in the header-rule colour");
            Rect headInk = CardTypography.InkInCard(h, sheet), sourceInk = CardTypography.InkInCard(s, sheet);
            Rect rule = RectInCard(card.Rule(i).rectTransform, sheet);
            log.IsTrue(headInk.yMax < rule.yMin && rule.yMax < sourceInk.yMin,
                $"{what}: headline, then the rule, then the source (y {headInk.yMax:F0} < {rule.yMin:F0}–{rule.yMax:F0} < {sourceInk.yMin:F0})");
            log.IsTrue(rule.height <= 3f * k && rule.height >= 1f * k, $"{what}: the rule is thin ({rule.height:F1} px)");
            log.IsTrue(Mathf.Abs(rule.xMin - padX) < 0.5f && Mathf.Abs(rule.xMax - (size.x - padX)) < 0.5f, $"{what}: …and spans the padding");
        }
        else
        {
            log.IsTrue(!s.gameObject.activeSelf && !card.Rule(i).gameObject.activeSelf, $"{what}: no source, so no source row and no rule");
        }

        // ---- The tag's own highlight ----
        int start = ClippingTypography.FindPhrase(headline, phrase);
        int spans = start >= 0 ? LinesSpanned(h, headline, phrase) : 0;
        log.AreEqual(start >= 0, card.SheetPhraseFound(i), $"{what}: the phrase is {(start >= 0 ? "found" : "not found")}");
        log.AreEqual(start >= 0 ? 1 : 0, card.HighlightCount(i), $"{what}: {(start >= 0 ? "one highlight" : "no highlight")}");
        if (spans == 0) return;

        IReadOnlyList<Rect> rects = card.HighlightRects(i, 0);
        log.AreEqual(spans, rects.Count, $"{what}: one marker stroke per line of the phrase ({spans})");
        if (rects.Count != spans) return;
        log.IsTrue(card.MarkersRoot(i).GetSiblingIndex() < h.transform.GetSiblingIndex(), $"{what}: the marker is behind the headline");
        var whole = new Rect(0f, 0f, size.x, size.y);
        for (int l = 0; l < rects.Count; l++)
        {
            Image stroke = card.Stroke(i, 0, l);
            Rect drawn = RectInCard(stroke.rectTransform, sheet);
            log.IsTrue(Inside(rects[l], whole, 0.5f), $"{what}: stroke {l} is inside the sheet ({Fmt(rects[l])})");
            log.IsTrue(Mathf.Abs(drawn.xMin - rects[l].xMin) < 0.5f && Mathf.Abs(drawn.yMin - rects[l].yMin) < 0.5f &&
                       Mathf.Abs(drawn.yMax - rects[l].yMax) < 0.5f,
                $"{what}: stroke {l} is drawn where it was measured ({Fmt(drawn)} vs {Fmt(rects[l])})");
            log.Near(rects[l].width, drawn.width, 0.5f, $"{what}: stroke {l} has swept its whole line");
            log.IsTrue(stroke.enabled && stroke.gameObject.activeSelf && stroke.color.a > 0.2f, $"{what}: stroke {l} is visible");
            if (l > 0)
                log.IsTrue(rects[l].yMin > rects[l - 1].yMax, $"{what}: strokes {l - 1} and {l} are on separate lines, not touching");
        }

        // Every visible character of the phrase sits on the stroke of its own line.
        TMP_TextInfo info = h.textInfo;
        int firstLine = -1;
        for (int c = start; c < start + phrase.Length && c < info.characterCount; c++)
        {
            TMP_CharacterInfo ci = info.characterInfo[c];
            if (!ci.isVisible) continue;
            if (firstLine < 0) firstLine = ci.lineNumber;
            Vector3 mid = sheet.InverseTransformPoint(h.rectTransform.TransformPoint((ci.bottomLeft + ci.topRight) * 0.5f));
            var p = new Vector2(mid.x - sheet.rect.xMin, sheet.rect.yMax - mid.y);
            int line = ci.lineNumber - firstLine;
            if (line < 0 || line >= rects.Count || !rects[line].Contains(p))
            {
                log.IsTrue(false, $"{what}: \"{headline[c]}\" at {c} (line {ci.lineNumber}) is on the stroke of its own line");
                break;
            }
        }

        Debug.Log($"[Clipping] self-test {what}: headline {px:F0} px on {lines} line(s), sheet {size.x:F0}×{size.y:F0}, " +
                  $"{spans} marker line(s)" + (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Clipping_CardLayout(Log log)
    {
        var cases = new[]
        {
            (h: ClipA, p: ClipAPhrase, s: "iTnews (Reuters)", what: "Binance"),
            (h: Clip3, p: "React Sharply", s: "Bloomberg", what: "3 words"),
            (h: Clip16, p: "Privacy Policy", s: "The Verge, citing two people familiar with the matter", what: "16 words"),
            (h: ClipLong, p: ClipLongPhrase, s: "CoinDesk", what: "phrase across line breaks"),
            (h: ClipB, p: "", s: "", what: "no phrase, no source"),
            (h: ClipC, p: "Regulators open probe into exchange after record hack", s: "Bloomberg", what: "the whole headline marked"),
        };
        foreach (var c in cases)
        {
            var card = BuildClipping(c.h, c.p, c.s, out GameObject root);
            try
            {
                string what = $"Clipping ({c.what})";
                var rt = (RectTransform)card.transform;
                log.AreEqual(1, card.SheetCount, $"{what}: one sheet");
                log.AreEqual(1, card.VisibleSheetCount, $"{what}: …in the pile");
                log.IsTrue(card.Layout == card.SheetLayout(0) && card.Layout != null, $"{what}: Layout is the top sheet's");
                log.IsTrue(card.Layout != null && card.Layout.Elements.Exists(e => e.Name == "headline") &&
                           card.Layout.Elements.Exists(e => e.Name == "source") == (c.s.Length > 0),
                    $"{what}: the layout reports \"headline\"{(c.s.Length > 0 ? " and \"source\"" : " only")}");
                CheckClippingSheet(log, card, 0, c.h, c.p, c.s, what);

                // The full-frame backdrop: behind everything, opaque, in the panel colour.
                log.IsTrue(card.transform.GetChild(0) == card.Backdrop.transform, $"{what}: the backdrop is the bottom layer");
                log.Near(1f, card.BackdropAlpha, 1e-4f, $"{what}: …fully opaque once it has faded in (it hides the presenter)");
                Color bc = card.Backdrop.color;
                log.IsTrue(Mathf.Abs(bc.r - ContentCardUIBuilder.BackgroundColor.r) < 1e-4f && Mathf.Abs(bc.g - ContentCardUIBuilder.BackgroundColor.g) < 1e-4f &&
                           Mathf.Abs(bc.b - ContentCardUIBuilder.BackgroundColor.b) < 1e-4f, $"{what}: …in the cards' panel colour");
                Rect cover = RectInCard(card.Backdrop.rectTransform, rt);
                log.IsTrue(Mathf.Abs(cover.xMin) < 0.5f && Mathf.Abs(cover.yMin) < 0.5f && Mathf.Abs(cover.xMax - 1920f) < 0.5f &&
                           Mathf.Abs(cover.yMax - 1080f) < 0.5f, $"{what}: …over the whole frame ({Fmt(cover)})");

                if (c.what == "3 words")
                {
                    log.Near(ClippingTypography.HeadlineMaxPx, card.HeadlineSize(0), 0.01f, $"{what}: a short headline takes the full 110 px");
                    log.AreEqual(0, card.Layout.Warnings.Count, $"{what}: …with no warning");
                }
                if (c.what == "16 words")
                {
                    float px = card.HeadlineSize(0);
                    bool inRange = px >= ClippingTypography.HeadlineMinPx - 0.01f;
                    log.AreEqual(!inRange, card.Layout.Warnings.Exists(w => w.Contains("only fits")),
                        $"{what}: at {px:F0} px it is {(inRange ? "inside 64–110 with no warning" : "under 64 px, and says so")}");
                    log.IsTrue(card.HeadlineLines(0) == ClippingTypography.HeadlineMaxLines, $"{what}: …on exactly three lines ({card.HeadlineLines(0)})");
                }
                if (c.what == "phrase across line breaks")
                    log.IsTrue(card.HighlightRects(0, 0).Count >= 2,
                        $"{what}: the phrase spans a line break ({card.HighlightRects(0, 0).Count} strokes)");
                if (c.what == "the whole headline marked")
                    log.AreEqual(card.HeadlineLines(0), card.HighlightRects(0, 0).Count, $"{what}: one stroke on every line of the headline");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Far too long for three lines at any size: shown at 32 px on more lines, warned, never cut.
        string huge = Clip16 + " " + Clip16 + " " + Clip16 + " " + Clip16;
        var hcard = BuildClipping(huge, "Privacy Policy", "The Verge", out GameObject hroot);
        try
        {
            log.IsTrue(hcard.Layout.Warnings.Exists(w => w.Contains("doesn't fit")), "Clipping (overlong): warned");
            log.Near(CardTypography.HardFloorPx, hcard.HeadlineSize(0), 0.01f, "Clipping (overlong): the headline stops at 32 px");
            log.IsTrue(hcard.HeadlineLines(0) > ClippingTypography.HeadlineMaxLines, $"Clipping (overlong): …on more than three lines ({hcard.HeadlineLines(0)})");
            log.IsTrue(hcard.Headline(0).text == huge && !hcard.Headline(0).isTextTruncated, "Clipping (overlong): the headline is still all there");
            log.IsTrue(hcard.SheetRoot(0).rect.height <= 1080f * ClippingTypography.SheetMaxHeightFraction + 0.5f,
                $"Clipping (overlong): the sheet still stops at 70 % of the frame ({hcard.SheetRoot(0).rect.height:F0} px)");
            Debug.Log($"[Clipping] self-test overlong: {string.Join(" | ", hcard.Layout.Warnings)}");
        }
        finally { UnityEngine.Object.DestroyImmediate(hroot); }
    }

    // 1 → 3 → 5 sheets, each landing on the last: alternate tilts, the ones
    // underneath shaded 25 % per layer, never more than three in the pile (the
    // oldest goes the moment the 4th has landed), the push-in on the top one
    // only, and a 6th refused.
    static void Clipping_PileOfFive(Log log)
    {
        var card = BuildClipping(ClipA, ClipAPhrase, "iTnews (Reuters)", out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            void CheckAtRest(int i, string what)
            {
                Rect box = RectInCard(card.SheetRoot(i), rt);
                log.IsTrue(Vector2.Distance(box.center, new Vector2(960f, 540f)) < 0.5f, $"{what}: sheet {i} rests centred in the frame ({Fmt(box)})");
                log.Near(ClippingTypography.Tilt(i), ClipTilt(card.SheetRoot(i)), 1e-3f, $"{what}: sheet {i} is tilted {ClippingTypography.Tilt(i):+0.0;-0.0}°");
                log.IsTrue(card.SheetLanded(i), $"{what}: sheet {i} has landed");
            }

            // ---- One sheet ----
            log.AreEqual(1, card.SheetCount, "pile: the tag opens a run of one sheet");
            log.AreEqual(1, card.DrawnSheetCount, "…one sheet drawn");
            log.IsTrue(card.TopPhraseFound, "…its phrase found");
            CheckAtRest(0, "1 sheet");
            log.Near(0f, card.SheetDim(0), 1e-4f, "1 sheet: not shaded");
            float pushed = card.SheetScale(0);
            log.Near(ClippingTypography.PushInScaleAt(card.TopSheetAge - ClippingTypography.DropSeconds), pushed, 1e-5f,
                $"1 sheet: pushed in 1.2 % a second since it landed ({pushed:F4} after {card.TopSheetAge:F2} s)");
            log.IsTrue(pushed > 1.015f && pushed < 1.03f, "…about 1.02 two seconds in");

            // ---- A second lands on it ----
            log.IsTrue(card.AppendSheet(ClipB, ClipBPhrase, "Reuters"), "pile: a second {Clipping} is taken");
            log.AreEqual(2, card.SheetCount, "…two sheets");
            log.Near(0f, card.TopSheetAge, 1e-4f, "…the new top sheet's age starts at 0");
            log.IsTrue(RectInCard(card.SheetRoot(1), rt).yMax < 0f, "…it starts wholly above the frame");
            log.Near(pushed, card.SheetScale(0), 1e-5f, "…the sheet under it holds its scale on that frame (no jump)");
            log.IsTrue(card.SheetRoot(1).GetSiblingIndex() > card.SheetRoot(0).GetSiblingIndex(), "…and it draws over the first");
            StepClipping(card, ClipSettleFrames);
            CheckAtRest(1, "2 sheets");
            log.Near(1f, card.SheetScale(0), 1e-4f, "2 sheets: the covered sheet is back at 1.00");
            log.Near(0.25f, card.SheetDim(0), 1e-4f, "2 sheets: …and shaded 25 %");
            log.Near(0f, card.SheetDim(1), 1e-4f, "2 sheets: the top one is not");
            log.IsTrue(card.SheetScale(1) > 1.015f, "2 sheets: the push-in is on the top sheet now");

            // ---- Three ----
            log.IsTrue(card.AppendSheet(ClipC, ClipCPhrase, "Bloomberg"), "pile: a third {Clipping} is taken");
            StepClipping(card, ClipSettleFrames);
            log.AreEqual(3, card.SheetCount, "3 sheets: three added");
            log.AreEqual(3, card.VisibleSheetCount, "3 sheets: three in the pile");
            log.AreEqual(3, card.DrawnSheetCount, "3 sheets: three drawn");
            for (int i = 0; i < 3; i++) CheckAtRest(i, "3 sheets");
            log.IsTrue(Mathf.Sign(ClipTilt(card.SheetRoot(0))) != Mathf.Sign(ClipTilt(card.SheetRoot(1))) &&
                       Mathf.Sign(ClipTilt(card.SheetRoot(1))) != Mathf.Sign(ClipTilt(card.SheetRoot(2))),
                "3 sheets: each leans the other way from the one under it");
            log.Near(0.50f, card.SheetDim(0), 1e-4f, "3 sheets: the bottom sheet is shaded 50 %");
            log.Near(0.25f, card.SheetDim(1), 1e-4f, "3 sheets: the middle one 25 %");
            log.Near(0f, card.SheetDim(2), 1e-4f, "3 sheets: the top one not at all");
            log.Near(1f, card.SheetScale(0), 1e-4f, "3 sheets: covered sheets are 1.00");
            log.Near(1f, card.SheetScale(1), 1e-4f, "3 sheets: …both");
            CheckClippingSheet(log, card, 2, ClipC, ClipCPhrase, "Bloomberg", "Clipping (3rd sheet)");
            CheckClippingSheet(log, card, 0, ClipA, ClipAPhrase, "iTnews (Reuters)", "Clipping (1st sheet, covered)");

            // ---- A 4th: the oldest goes once it has landed ----
            log.IsTrue(card.AppendSheet(ClipLong, ClipLongPhrase, "CoinDesk"), "pile: a 4th {Clipping} is taken");
            log.AreEqual(3, card.VisibleSheetCount, "4 sheets: still three in the pile");
            int landedOn = -1, hiddenOn = -1;
            for (int f = 1; f <= 15; f++)
            {
                card.Advance(Frame);
                if (landedOn < 0 && card.SheetLanded(3)) landedOn = f;
                if (hiddenOn < 0 && !card.SheetDrawn(0)) hiddenOn = f;
                if (f == 1)
                {
                    log.AreEqual(4, card.DrawnSheetCount, "4 sheets: while the 4th is dropping the oldest is still under the pile");
                    log.IsTrue(card.SheetDim(0) > 0.5f && card.SheetDim(0) < 0.75f, "…getting darker as the 4th comes down");
                }
            }
            log.AreEqual(8, landedOn, "4 sheets: the 4th lands on the 8th frame (0.25 s at 30 fps)");
            log.AreEqual(landedOn, hiddenOn, "4 sheets: …and the oldest is hidden on that very frame");
            log.AreEqual(3, card.DrawnSheetCount, "4 sheets: three drawn again");
            log.IsTrue(!card.SheetDrawn(0) && card.SheetDrawn(1) && card.SheetDrawn(2) && card.SheetDrawn(3), "4 sheets: sheets 1–3 are the pile");
            log.Near(0.50f, card.SheetDim(1), 1e-4f, "4 sheets: shades move down the pile (50 %)");
            log.Near(0.25f, card.SheetDim(2), 1e-4f, "4 sheets: …(25 %)");
            log.Near(0f, card.SheetDim(3), 1e-4f, "4 sheets: …(none)");
            StepClipping(card, ClipSettleFrames);

            // ---- Five ----
            log.IsTrue(card.AppendSheet(Clip16, "Privacy Policy", "The Verge"), "pile: a 5th {Clipping} is taken");
            StepClipping(card, ClipSettleFrames);
            log.AreEqual(5, card.SheetCount, "5 sheets: five added");
            log.AreEqual(3, card.VisibleSheetCount, "5 sheets: three in the pile");
            log.AreEqual(3, card.DrawnSheetCount, "5 sheets: three drawn");
            log.IsTrue(!card.SheetDrawn(0) && !card.SheetDrawn(1) && card.SheetDrawn(2) && card.SheetDrawn(3) && card.SheetDrawn(4),
                "5 sheets: sheets 2–4 are the pile");
            for (int i = 2; i < 5; i++) CheckAtRest(i, "5 sheets");
            for (int i = 1; i < 5; i++)
                log.IsTrue(Mathf.Sign(card.SheetTilt(i)) != Mathf.Sign(card.SheetTilt(i - 1)), $"5 sheets: sheet {i} leans the other way from sheet {i - 1}");
            log.Near(0.50f, card.SheetDim(2), 1e-4f, "5 sheets: shaded 50 % / 25 % / none from the bottom");
            log.Near(0.25f, card.SheetDim(3), 1e-4f, "5 sheets: …25 %");
            log.Near(0f, card.SheetDim(4), 1e-4f, "5 sheets: …none");
            CheckClippingSheet(log, card, 4, Clip16, "Privacy Policy", "The Verge", "Clipping (5th sheet)");
            CheckClippingSheet(log, card, 3, ClipLong, ClipLongPhrase, "CoinDesk", "Clipping (4th sheet, covered)");
            log.IsTrue(card.Layout == card.SheetLayout(4), "5 sheets: Layout is the top sheet's");

            // ---- A 6th is refused ----
            log.IsTrue(!card.AppendSheet("One clipping too many", "too many", "Nobody"), "pile: a 6th {Clipping} is refused");
            log.IsTrue(!card.AppendSheet("One clipping too many", "too many", "Nobody", instant: true), "…instant or not");
            log.AreEqual(5, card.SheetCount, "…and adds nothing");
            log.AreEqual(3, card.DrawnSheetCount, "…the pile is as it was");
            log.AreEqual(Clip16, card.Headline(4).text, "…the 5th is still on top");

            // ---- The push-in stops at 1.08 ----
            StepClipping(card, 240);
            log.Near(ClippingTypography.PushInMaxScale, card.SheetScale(4), 1e-5f, "the top sheet's push-in stops at 1.08 (10 s in)");
            log.Near(card.SheetScale(4), card.SheetRoot(4).localScale.x, 1e-6f, "…the same on both axes");
            CheckAtRest(4, "pushed in");
            Rect big = RectInCard(card.SheetRoot(4), rt);
            log.IsTrue(big.xMin > 0f && big.xMax < 1920f && big.yMin > 0f && big.yMax < 1080f, $"…and it is still wholly in the frame ({Fmt(big)})");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // A sheet whose beat passed while the card waited: it lands at once, its
    // highlight already drawn. Sheets added before Show drop in as one pile.
    static void Clipping_InstantAndQueuedSheets(Log log)
    {
        var card = BuildClipping(ClipA, ClipAPhrase, "iTnews (Reuters)", out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            log.IsTrue(card.AppendSheet(ClipB, ClipBPhrase, "Reuters", instant: true), "instant: the sheet is taken");
            log.IsTrue(card.SheetLanded(1), "instant: it has landed at once");
            log.Near(ClippingTypography.DropSeconds, card.TopSheetAge, 1e-3f, "instant: its age starts where a landing ends (0.25 s)");
            log.IsTrue(Vector2.Distance(RectInCard(card.SheetRoot(1), rt).center, new Vector2(960f, 540f)) < 0.5f, "instant: it is on the pile, centred");
            log.Near(1f, card.SheetScale(1), 1e-3f, "instant: its push-in starts from 1.00");
            log.Near(0.25f, card.SheetDim(0), 1e-3f, "instant: the sheet under it is shaded at once");
            log.Near(1f, card.SheetScale(0), 1e-3f, "instant: …and back at 1.00 at once");
            Image stroke = card.Stroke(1, 0, 0);
            log.IsTrue(stroke.enabled && Mathf.Abs(RectInCard(stroke.rectTransform, card.SheetRoot(1)).width - card.HighlightRects(1, 0)[0].width) < 0.5f,
                "instant: its highlight is already drawn");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        var queued = BuildClipping(ClipA, ClipAPhrase, "iTnews (Reuters)", out GameObject qroot, show: false);
        try
        {
            var rt = (RectTransform)queued.transform;
            log.IsTrue(queued.AppendSheet(ClipB, ClipBPhrase, "Reuters", instant: true) &&
                       queued.AppendSheet(ClipC, ClipCPhrase, "Bloomberg", instant: true), "before Show: late sheets join the waiting pile");
            log.Near(0f, queued.TopSheetAge, 1e-6f, "before Show: nothing has started landing");
            for (int i = 0; i < 3; i++)
                log.IsTrue(RectInCard(queued.SheetRoot(i), rt).yMax < 0f && !queued.SheetLanded(i), $"before Show: sheet {i} waits above the frame");
            queued.Show();
            for (int i = 0; i < 3; i++)
                log.IsTrue(RectInCard(queued.SheetRoot(i), rt).yMax < 0f, $"Show: sheet {i} starts from above the frame");
            StepClipping(queued, 8);
            log.IsTrue(queued.SheetLanded(0) && queued.SheetLanded(1) && queued.SheetLanded(2), "Show: the pile lands as one, 0.25 s later");
            StepClipping(queued, 30);
            log.AreEqual(3, queued.DrawnSheetCount, "…three sheets in it");
            log.Near(0.50f, queued.SheetDim(0), 1e-4f, "…shaded 50 %");
            log.Near(0.25f, queued.SheetDim(1), 1e-4f, "…25 %");
            log.Near(0f, queued.SheetDim(2), 1e-4f, "…and not at all, from the bottom");
            log.IsTrue(queued.SheetScale(2) > 1.01f && Mathf.Abs(queued.SheetScale(1) - 1f) < 1e-4f, "…the push-in on the top one only");
        }
        finally { UnityEngine.Object.DestroyImmediate(qroot); }
    }

    // The phrase must be in the headline exactly. If it isn't: no highlight and
    // ONE warning, in the agreed words; an empty phrase is no warning at all.
    static void Clipping_PhraseNotFoundWarnsOnce(Log log)
    {
        var warnings = new List<string>();
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(msg);
        }
        int PhraseWarnings() => warnings.FindAll(w => w.Contains("isn't in the headline")).Count;

        Application.logMessageReceived += OnLog;
        GameObject root = null;
        try
        {
            const string wrong = "US$6.3 billion robbery";
            var card = BuildClipping(ClipA, wrong, "iTnews (Reuters)", out root);
            log.AreEqual(1, PhraseWarnings(), "a phrase that isn't in the headline is warned about once");
            log.IsTrue(warnings.Contains(ClippingTypography.PhraseWarning(ClipA, wrong)), "…in the agreed words");
            log.IsTrue(!card.TopPhraseFound, "…TopPhraseFound is false");
            log.AreEqual(0, card.HighlightCount(0), "…and the sheet has no highlight");
            foreach (Image m in card.MarkersRoot(0).GetComponentsInChildren<Image>(true))
                log.IsTrue(!m.gameObject.activeSelf || !m.enabled, "…no marker stroke is drawn");

            // Laying the sheet out again (the frame changed size) doesn't repeat it.
            ((RectTransform)card.transform.parent).sizeDelta = new Vector2(1280f, 720f);
            card.RefreshLayoutIfResized();
            StepClipping(card, 5);
            log.AreEqual(1, PhraseWarnings(), "…still once after the sheet is laid out again");

            // An empty phrase: no highlight, and that is not a mistake.
            warnings.Clear();
            log.IsTrue(card.AppendSheet(ClipB, "", "Reuters"), "a sheet with no phrase is taken");
            StepClipping(card, 30);
            log.AreEqual(0, PhraseWarnings(), "an empty phrase is not warned about");
            log.IsTrue(!card.TopPhraseFound && card.HighlightCount(1) == 0, "…it just has no highlight");

            // And one that is there.
            log.IsTrue(card.AppendSheet(ClipC, ClipCPhrase, "Bloomberg"), "a sheet with its phrase is taken");
            StepClipping(card, 30);
            log.AreEqual(0, PhraseWarnings(), "a phrase that is in the headline is not warned about");
            log.IsTrue(card.TopPhraseFound && card.HighlightCount(2) == 1, "…TopPhraseFound follows the top sheet");

            // Case counts.
            log.IsTrue(card.AppendSheet(ClipA, "binance freezes", "iTnews"), "a sheet with a wrongly-cased phrase is taken");
            log.AreEqual(1, PhraseWarnings(), "…and warned about: the match is exact");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // {Mark:"phrase"} on the top sheet: Ok / NotFound / Repeated / TooMany, the
    // new stroke sweeping at once and the earlier ones fading to the pale tint.
    static void Clipping_Marks(Log log)
    {
        var card = BuildClipping(ClipLong, "hackers drain", "CoinDesk", out GameObject root);
        try
        {
            RectTransform sheet = card.SheetRoot(0);
            float fullAlpha = DocumentTypography.Marker.a, paleAlpha = fullAlpha * ClippingTypography.PaleAlphaFraction;
            bool Swept(int s, int h)
            {
                IReadOnlyList<Rect> rects = card.HighlightRects(s, h);
                if (rects.Count == 0) return false;
                for (int l = 0; l < rects.Count; l++)
                {
                    Image m = card.Stroke(s, h, l);
                    if (!m.enabled || Mathf.Abs(RectInCard(m.rectTransform, card.SheetRoot(s)).width - rects[l].width) > 0.5f) return false;
                }
                return true;
            }

            log.IsTrue(card.TopPhraseFound && card.HighlightCount(0) == 1 && card.MarkCount(0) == 0, "marks: the sheet starts with its own highlight, no marks");
            log.IsTrue(Swept(0, 0), "marks: …fully drawn");

            log.AreEqual(MarkResult.NotFound, card.AddMark("hackers drained"), "marks: a phrase that isn't in the headline → NotFound");
            log.AreEqual(MarkResult.NotFound, card.AddMark(""), "marks: an empty phrase → NotFound");
            log.AreEqual(MarkResult.NotFound, card.AddMark(null), "marks: no phrase → NotFound");
            log.AreEqual(MarkResult.Repeated, card.AddMark("hackers drain"), "marks: the tag's own phrase again → Repeated");
            log.IsTrue(card.HighlightCount(0) == 1 && card.MarkCount(0) == 0, "marks: …none of which changed anything");
            log.Near(fullAlpha, card.Stroke(0, 0, 0).color.a, 1e-4f, "marks: …the highlight is still at full strength");

            // ---- The first mark ----
            Image own = card.Stroke(0, 0, 0);
            log.AreEqual(MarkResult.Ok, card.AddMark("customer withdrawals"), "marks: a new phrase of the headline → Ok");
            log.IsTrue(card.HighlightCount(0) == 2 && card.MarkCount(0) == 1, "marks: …one mark on the sheet");
            log.AreEqual("customer withdrawals", card.HighlightPhrase(0, 1), "marks: …that phrase");
            log.IsTrue(card.Stroke(0, 0, 0) == own, "marks: the earlier highlight keeps its stroke");
            log.Near(fullAlpha, own.color.a, 1e-4f, "marks: …at full strength on the frame the mark arrives");
            Image fresh = card.Stroke(0, 1, 0);
            log.IsTrue(!fresh.enabled, "marks: the new stroke starts from nothing");
            log.IsTrue(fresh.transform.GetSiblingIndex() > own.transform.GetSiblingIndex(), "marks: …and will draw over the earlier one");
            log.IsTrue(card.MarkersRoot(0).GetSiblingIndex() < card.Headline(0).transform.GetSiblingIndex(), "marks: …behind the headline");
            card.Advance(Frame);
            log.IsTrue(fresh.enabled && fresh.color.a > 0f, "marks: …and is sweeping on the next frame (it starts at once)");
            log.IsTrue(own.color.a < fullAlpha && own.color.a > paleAlpha, "marks: the earlier highlight has started to fade");
            StepClipping(card, 6); // 7 frames = 0.233 s since the mark
            log.Near(paleAlpha, own.color.a, 1e-4f, "marks: 0.2 s later it is at the pale tint (40 % of the marker's alpha)");
            log.IsTrue(Swept(0, 0), "marks: …and stays, at its full width");
            int markLines = card.HighlightRects(0, 1).Count;
            StepClipping(card, Mathf.CeilToInt(ClippingTypography.SweepSeconds(markLines) / Frame) + 1);
            log.IsTrue(Swept(0, 1), $"marks: the mark has swept its {markLines} line(s) after 0.3 s each");
            log.Near(fullAlpha, card.Stroke(0, 1, 0).color.a, 1e-4f, "marks: …at full strength");
            for (int l = 0; l < markLines; l++)
            {
                Rect drawn = RectInCard(card.Stroke(0, 1, l).rectTransform, sheet);
                log.IsTrue(Inside(drawn, new Rect(0f, 0f, sheet.rect.width, sheet.rect.height), 0.5f), $"marks: stroke {l} of the mark is inside the sheet");
            }

            // ---- Repeated, then up to three ----
            log.AreEqual(MarkResult.Repeated, card.AddMark("customer withdrawals"), "marks: an earlier mark again → Repeated");
            log.AreEqual(MarkResult.Ok, card.AddMark("eleven hot wallets"), "marks: a 2nd mark → Ok");
            log.AreEqual(MarkResult.Ok, card.AddMark("overnight"), "marks: a 3rd mark → Ok");
            log.IsTrue(card.MarkCount(0) == 3 && card.HighlightCount(0) == 4, "marks: three marks plus the tag's own highlight");
            log.AreEqual(MarkResult.TooMany, card.AddMark("Binance freezes"), "marks: a 4th mark → TooMany");
            log.AreEqual(MarkResult.NotFound, card.AddMark("not in the headline"), "marks: …NotFound still comes first on a full sheet");
            log.AreEqual(MarkResult.Repeated, card.AddMark("overnight"), "marks: …and so does Repeated");
            log.IsTrue(card.MarkCount(0) == 3 && card.HighlightCount(0) == 4, "marks: …none of which changed anything");
            StepClipping(card, 45);
            for (int h = 0; h < 4; h++)
            {
                log.IsTrue(Swept(0, h), $"marks: highlight {h} (\"{card.HighlightPhrase(0, h)}\") is drawn");
                log.Near(h == 3 ? fullAlpha : paleAlpha, card.Stroke(0, h, 0).color.a, 1e-4f,
                    $"marks: highlight {h} is {(h == 3 ? "the marker at full strength" : "pale")}");
            }

            // ---- Marks act on the TOP sheet ----
            log.IsTrue(card.AppendSheet(ClipB, ClipBPhrase, "Reuters"), "marks: the next sheet lands");
            StepClipping(card, 30);
            log.AreEqual(MarkResult.NotFound, card.AddMark("eleven hot wallets"), "marks: a phrase of the covered sheet → NotFound (the top sheet answers)");
            log.AreEqual(MarkResult.Ok, card.AddMark("record hack"), "marks: a phrase of the top sheet → Ok");
            log.IsTrue(card.HighlightCount(0) == 4 && card.MarkCount(0) == 3, "marks: the covered sheet is untouched");
            log.IsTrue(card.HighlightCount(1) == 2 && card.MarkCount(1) == 1, "marks: the top sheet has its own count");
            StepClipping(card, 30);
            log.IsTrue(Swept(1, 1), "marks: …and its mark is drawn");
            log.Near(paleAlpha, card.Stroke(1, 0, 0).color.a, 1e-4f, "marks: …the top sheet's own highlight pale under it");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Steps the first sheet's entrance at 30 fps: the backdrop up in 0.2 s, the
    // sheet down from above the frame in 0.25 s (never back up, every step
    // smaller), the marker 0.25 s after the landing, 0.3 s per line, each
    // stroke growing from its left end.
    static void Clipping_EntranceFrameByFrame(Log log)
    {
        var card = BuildClipping(ClipLong, ClipLongPhrase, "CoinDesk", out GameObject root, show: false);
        try
        {
            var rt = (RectTransform)card.transform;
            RectTransform sheet = card.SheetRoot(0);
            CanvasGroup group = card.GetComponent<CanvasGroup>();
            IReadOnlyList<Rect> rects = card.HighlightRects(0, 0);
            int lines = rects.Count;
            log.IsTrue(lines >= 2, $"entrance: the phrase is on {lines} lines");
            log.IsTrue(RectInCard(sheet, rt).yMax < 0f, "before Show: the sheet waits wholly above the frame");
            log.IsTrue(card.BackdropAlpha == 0f && group.alpha == 0f, "before Show: nothing is visible");
            log.IsTrue(!card.Stroke(0, 0, 0).enabled, "before Show: no marker");
            log.IsTrue(!card.IsClosing, "before Show: not closing");

            card.Show();
            log.Near(1f, group.alpha, 1e-6f, "Show: the card root is fully visible at once (no fade of the whole card)");
            log.IsTrue(card.transform.localEulerAngles == Vector3.zero, "Show: …and straight (the sheets carry the tilt)");
            log.IsTrue(RectInCard(sheet, rt).yMax < 0f && card.BackdropAlpha == 0f, "Show: the first drawn frame is the start pose");
            log.Near(0f, card.TopSheetAge, 1e-6f, "Show: the top sheet's age starts at 0");

            const float dt = Frame;
            float t = 0f, landedAt = float.NaN, backdropFullAt = float.NaN;
            var lineStart = new float[lines];
            var lineDone = new float[lines];
            for (int i = 0; i < lines; i++) { lineStart[i] = float.NaN; lineDone[i] = float.NaN; }
            float lastY = RectInCard(sheet, rt).center.y, lastStep = float.MaxValue;
            bool neverUp = true, slowing = true, growsLeftToRight = true, straightDown = true;
            float worstScaleWhileDropping = 1f;
            for (int f = 0; f < 60; f++)
            {
                card.Advance(dt);
                t += dt;
                Rect box = RectInCard(sheet, rt);
                float step = box.center.y - lastY; // y down: a drop is positive
                if (step < -0.01f) neverUp = false;
                if (step > lastStep + 0.01f) slowing = false;
                if (Mathf.Abs(box.center.x - 960f) > 0.5f) straightDown = false;
                lastStep = step;
                lastY = box.center.y;
                if (!card.SheetLanded(0)) worstScaleWhileDropping = Mathf.Max(worstScaleWhileDropping, card.SheetScale(0));
                if (float.IsNaN(landedAt) && card.SheetLanded(0)) landedAt = t;
                if (float.IsNaN(backdropFullAt) && card.BackdropAlpha >= 1f - 1e-4f) backdropFullAt = t;
                for (int i = 0; i < lines; i++)
                {
                    Image m = card.Stroke(0, 0, i);
                    Rect drawn = RectInCard(m.rectTransform, sheet);
                    if (m.enabled && float.IsNaN(lineStart[i])) lineStart[i] = t;
                    if (m.enabled && Mathf.Abs(drawn.xMin - rects[i].xMin) > 0.5f) growsLeftToRight = false;
                    if (m.enabled && float.IsNaN(lineDone[i]) && drawn.width >= rects[i].width - 0.5f) lineDone[i] = t;
                }
            }

            log.IsTrue(backdropFullAt >= ClippingTypography.BackdropFadeInSeconds - dt && backdropFullAt <= ClippingTypography.BackdropFadeInSeconds + dt + 1e-3f,
                $"the backdrop covers the frame after its 0.2 s fade-in ({backdropFullAt:F3} s)");
            log.IsTrue(landedAt >= ClippingTypography.DropSeconds - 1e-3f && landedAt <= ClippingTypography.DropSeconds + dt + 1e-3f,
                $"the sheet lands 0.25 s after Show ({landedAt:F3} s)");
            log.IsTrue(neverUp, "…it only ever moves down");
            log.IsTrue(slowing, "…every step smaller than the last (it decelerates onto the pile)");
            log.IsTrue(straightDown, "…straight down the middle of the frame");
            log.Near(1f, worstScaleWhileDropping, 1e-6f, "…at 1.00 until it has landed (the push-in starts there)");
            Rect end = RectInCard(sheet, rt);
            log.IsTrue(Vector2.Distance(end.center, new Vector2(960f, 540f)) < 0.5f, $"…and rests centred ({Fmt(end)})");
            log.Near(t, card.TopSheetAge, 1e-3f, "TopSheetAge counts from the start of the drop");
            for (int i = 0; i < lines; i++)
            {
                float s = ClippingTypography.DropSeconds + ClippingTypography.LineSweepStart(i); // after Show
                log.IsTrue(lineStart[i] > s - 1e-3f && lineStart[i] <= s + dt + 1e-3f,
                    $"marker line {i} starts {ClippingTypography.LineSweepStart(i):F2} s after the landing ({lineStart[i]:F3} s after Show)");
                log.IsTrue(lineDone[i] >= s + ClippingTypography.HighlightLineSeconds - dt - 1e-3f &&
                           lineDone[i] <= s + ClippingTypography.HighlightLineSeconds + dt + 1e-3f,
                    $"…and has swept its line 0.3 s later ({lineDone[i]:F3} s)");
            }
            log.IsTrue(growsLeftToRight, "every stroke grows from the phrase's left end");

            // The next sheet: the same drop, from the same start pose.
            log.IsTrue(card.AppendSheet(ClipB, ClipBPhrase, "Reuters"), "entrance: the next sheet is taken");
            RectTransform next = card.SheetRoot(1);
            log.IsTrue(RectInCard(next, rt).yMax < 0f, "the next sheet's first drawn frame is its start pose, above the frame");
            int landedOn = -1;
            float prevY = RectInCard(next, rt).center.y;
            bool nextNeverUp = true;
            for (int f = 1; f <= 12; f++)
            {
                card.Advance(dt);
                float y = RectInCard(next, rt).center.y;
                if (y < prevY - 0.01f) nextNeverUp = false;
                prevY = y;
                if (landedOn < 0 && card.SheetLanded(1)) landedOn = f;
            }
            log.AreEqual(8, landedOn, "…it lands 0.25 s later (the 8th frame at 30 fps)");
            log.IsTrue(nextNeverUp, "…moving down only");
            log.IsTrue(!card.Stroke(1, 0, 0).enabled, "…its marker waiting 0.25 s more");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // {Clipping:End}: IsClosing at once, the pile lifts out through the top of
    // the frame and fades with the backdrop, then OnHideComplete — once. A
    // fast hide (a transition, the next card) sets IsClosing too.
    static void Clipping_HideLiftsThePile(Log log)
    {
        var card = BuildClipping(ClipA, ClipAPhrase, "iTnews (Reuters)", out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            card.AppendSheet(ClipB, ClipBPhrase, "Reuters");
            StepClipping(card, ClipSettleFrames);
            int completed = 0;
            card.OnHideComplete = () => completed++;
            log.IsTrue(!card.IsClosing, "hide: an open run is not closing");

            card.Hide(fast: false);
            log.IsTrue(card.IsClosing, "hide: Hide sets IsClosing at once");
            log.IsTrue(card.PileAlpha == 1f && card.Pile.anchoredPosition.y == 0f && Mathf.Abs(card.BackdropAlpha - 1f) < 1e-4f,
                "hide: its first frame is the pile as it was (no jump)");
            log.AreEqual(0, completed, "hide: not reported yet");

            StepClipping(card, 3); // 0.1 s
            float y3 = card.Pile.anchoredPosition.y;
            log.IsTrue(y3 > 0f && card.PileAlpha < 1f && card.PileAlpha > 0f, $"hide: the pile is lifting and fading ({y3:F0} px up, alpha {card.PileAlpha:F2})");
            log.IsTrue(card.BackdropAlpha < 1f && card.BackdropAlpha > 0f, "hide: …the backdrop fading with it");
            card.Hide(fast: false);
            log.Near(y3, card.Pile.anchoredPosition.y, 0.01f, "hide: a second Hide doesn't start the lift over");
            log.AreEqual(0, completed, "hide: still not reported 0.1 s in");

            StepClipping(card, 5); // 8 frames = 0.267 s
            log.AreEqual(1, completed, "hide: OnHideComplete after the 0.25 s lift");
            log.Near(0f, card.PileAlpha, 1e-4f, "hide: the pile is gone");
            log.Near(0f, card.BackdropAlpha, 1e-4f, "hide: …and so is the backdrop");
            log.IsTrue(RectInCard(card.SheetRoot(1), rt).yMax < 0f && RectInCard(card.SheetRoot(0), rt).yMax < 0f,
                "hide: both sheets have left through the top of the frame");
            StepClipping(card, 15);
            log.AreEqual(1, completed, "hide: reported once, never again");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        var fast = BuildClipping(ClipA, ClipAPhrase, "iTnews (Reuters)", out GameObject froot);
        try
        {
            fast.Hide(fast: true);
            log.IsTrue(fast.IsClosing, "fast hide: IsClosing too");
            log.IsTrue(fast.Pile.anchoredPosition.y == 0f && fast.PileAlpha == 1f, "fast hide: no lift — the whole card just fades");
        }
        finally { UnityEngine.Object.DestroyImmediate(froot); }
    }

    // The frame changes size under an open card (720p, then 4K): every sheet
    // is laid out again in proportion, its strokes with it.
    static void Clipping_FrameSizeChange(Log log)
    {
        var card = BuildClipping(ClipLong, ClipLongPhrase, "CoinDesk", out GameObject root);
        try
        {
            var zone = (RectTransform)card.transform.parent;
            card.AppendSheet(ClipA, ClipAPhrase, "iTnews (Reuters)");
            StepClipping(card, ClipSettleFrames);
            float px0 = card.HeadlineSize(0), px1 = card.HeadlineSize(1);
            float width0 = card.SheetRoot(0).rect.width;
            card.RefreshLayoutIfResized();
            log.Near(width0, card.SheetRoot(0).rect.width, 1e-3f, "resize: nothing moves while the frame keeps its size");

            foreach (var (w, h, name) in new[] { (1280f, 720f, "720p"), (3840f, 2160f, "4K") })
            {
                zone.sizeDelta = new Vector2(w, h);
                card.RefreshLayoutIfResized();
                StepClipping(card, 1);
                float k = h / 1080f;
                log.Near(w * ClippingTypography.SheetWidthFraction, card.SheetRoot(1).rect.width, 0.5f, $"resize ({name}): the sheets follow the frame");
                log.IsTrue(Mathf.Abs(card.HeadlineSize(0) - px0) <= 3f && Mathf.Abs(card.HeadlineSize(1) - px1) <= 3f,
                    $"resize ({name}): the headlines keep their size in proportion ({card.HeadlineSize(0):F1} / {card.HeadlineSize(1):F1} px at 1080p, " +
                    $"were {px0:F0} / {px1:F0})");
                log.Near(card.HeadlineSize(1) * k, card.Headline(1).fontSize, 0.01f, $"resize ({name}): …drawn at ×{k:F2}");
                var rt = (RectTransform)card.transform;
                log.IsTrue(Vector2.Distance(RectInCard(card.SheetRoot(1), rt).center, new Vector2(w * 0.5f, h * 0.5f)) < 0.5f,
                    $"resize ({name}): the pile stays centred");
                CheckClippingSheet(log, card, 1, ClipA, ClipAPhrase, "iTnews (Reuters)", $"Clipping ({name}, top sheet)");
                CheckClippingSheet(log, card, 0, ClipLong, ClipLongPhrase, "CoinDesk", $"Clipping ({name}, covered sheet)");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}

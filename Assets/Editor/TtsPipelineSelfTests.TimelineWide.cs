using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// {Timeline:"date","event",Wide} — the horizontal, full-screen timeline.
/// Pure: where the nodes sit for 1–6 of them, the above / below alternation,
/// the numeral rule, the label widths (never into a same-side neighbour, never
/// past title-safe), the span bracket and the timing. With the engine: the
/// real card with 1, 2, 4 and 6 nodes, two-line events on neighbours, a 7th
/// node, the span (announced, unannounced, given twice, absent), numbered and
/// mixed runs, the entrance stepped frame by frame, a missed read-ahead and
/// the close. (The tag's parsing and routing are the integrator's.)
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Wide Timeline Self-Tests")]
    public static void RunTimelineWide()
    {
        var log = new Log();
        RunTimelineWideGroup(log);
        log.Report();
    }

    static void RunTimelineWideGroup(Log log)
    {
        TimelineWide_NodePlaces(log);
        TimelineWide_AboveAndBelow(log);
        TimelineWide_NumeralRule(log);
        TimelineWide_LabelWidths(log);
        TimelineWide_SpanGeometry(log);
        TimelineWide_Timing(log);

        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            TimelineWide_CardLayout(log);
            TimelineWide_SeventhNodeIsRefused(log);
            TimelineWide_Span(log);
            TimelineWide_NumberedRun(log);
            TimelineWide_MixedRun(log);
            TimelineWide_EntranceFrameByFrame(log);
            TimelineWide_ReadAheadAndRefit(log);
            TimelineWide_HideClosesTheTimeline(log);
        }
        catch (Exception e)
        {
            // TMP needs the engine — offline harnesses land here.
            log.IsTrue(false, $"TimelineWide layout checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    const float WideW = 1920f, WideH = 1080f;

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void TimelineWide_NodePlaces(Log log)
    {
        log.AreEqual(6, TimelineWideTypography.MaxNodes, "a wide timeline holds up to six nodes");
        float left = TimelineWideTypography.LineLeft(WideW), right = TimelineWideTypography.LineRight(WideW);
        log.Near(0.80f * WideW, right - left, 0.01f, "the line crosses 80 % of the frame's width");
        log.Near(WideW * 0.5f, (left + right) * 0.5f, 0.01f, "…centred");
        log.Near(WideH * 0.5f, TimelineWideTypography.LineY(WideH), 0.01f, "…at mid height");
        log.IsTrue(left >= TimelineWideTypography.SafeLeft(WideW) && right <= TimelineWideTypography.SafeRight(WideW),
            "…inside the 5 % title-safe margins");

        for (int n = 1; n <= TimelineWideTypography.MaxNodes; n++)
        {
            float slot = (right - left) / n;
            bool ordered = true, even = true, inside = true;
            for (int i = 0; i < n; i++)
            {
                float x = TimelineWideTypography.NodeX(i, n, WideW);
                if (x <= left || x >= right) inside = false;
                if (i == 0) continue;
                float step = x - TimelineWideTypography.NodeX(i - 1, n, WideW);
                if (step <= 0f) ordered = false;
                if (Mathf.Abs(step - slot) > 0.01f) even = false;
            }
            log.IsTrue(ordered, $"{n} node(s): left to right, in script order");
            log.IsTrue(even, $"{n} node(s): evenly spaced, one slot ({slot:F1} px) apart");
            log.IsTrue(inside, $"{n} node(s): every node on the line, inside its ends");
            log.Near(left + slot * 0.5f, TimelineWideTypography.NodeX(0, n, WideW), 0.01f,
                $"{n} node(s): the first sits half a slot in from the line's left end");
            log.Near(WideW, TimelineWideTypography.NodeX(0, n, WideW) + TimelineWideTypography.NodeX(n - 1, n, WideW), 0.01f,
                $"{n} node(s): the run is centred on the frame");
        }

        log.Near(WideW * 0.5f, TimelineWideTypography.NodeX(0, 1, WideW), 0.01f, "one node alone sits mid-line");
        log.AreEqual(TimelineWideTypography.MaxNodes, TimelineWideTypography.Places(9), "a run is never cut into more than six places");
        log.AreEqual(1, TimelineWideTypography.Places(0), "…nor fewer than one");
        log.Near(TimelineWideTypography.NodeX(5, 6, WideW), TimelineWideTypography.NodeX(5, 9, WideW), 0.01f,
            "…so a count past six lays out as six");
        log.Near(2f * TimelineWideTypography.NodeX(2, 5, WideW), TimelineWideTypography.NodeX(2, 5, 2f * WideW), 0.01f,
            "places scale with the frame's width");
    }

    static void TimelineWide_AboveAndBelow(Log log)
    {
        for (int i = 0; i < TimelineWideTypography.MaxNodes; i++)
            log.AreEqual(i % 2 == 0, TimelineWideTypography.IsAbove(i),
                $"node {i + 1} carries its label {(i % 2 == 0 ? "above" : "below")} the line");

        float lineY = TimelineWideTypography.LineY(WideH);
        float offset = TimelineWideTypography.LabelOffset(TimelineWideTypography.DotDiameter(false, 1f), 1f);
        float x0 = TimelineWideTypography.NodeX(0, 6, WideW), x1 = TimelineWideTypography.NodeX(1, 6, WideW);
        Rect above = TimelineWideTypography.LabelRect(0, x0, lineY, 448f, 120f, offset);
        Rect below = TimelineWideTypography.LabelRect(1, x1, lineY, 472f, 120f, offset);
        log.Near(x0, above.center.x, 0.01f, "a label is centred on its node's x");
        log.Near(x1, below.center.x, 0.01f, "…below the line too");
        log.Near(lineY - offset, above.yMax, 0.01f, "an above-line label ends the label distance above the line");
        log.Near(lineY + offset, below.yMin, 0.01f, "a below-line label starts the same distance under it");
        log.IsTrue(above.yMax < lineY - TimelineWideTypography.DotPx * 0.5f && below.yMin > lineY + TimelineWideTypography.DotPx * 0.5f,
            "…both clear of the dot");
        Rect taller = TimelineWideTypography.LabelRect(0, x0, lineY, 448f, 190f, offset);
        Rect tallerBelow = TimelineWideTypography.LabelRect(1, x1, lineY, 472f, 190f, offset);
        log.IsTrue(Mathf.Abs(taller.yMax - above.yMax) < 0.01f && taller.yMin < above.yMin, "a taller label grows away from the line (above: upward)");
        log.IsTrue(Mathf.Abs(tallerBelow.yMin - below.yMin) < 0.01f && tallerBelow.yMax > below.yMax, "…(below: downward)");

        // The room each side has, down to the largest dot a run can get.
        foreach (float dot in new[] { TimelineWideTypography.DotPx, TimelineWideTypography.DotPx * TimelineWideTypography.NumeralDotScale, TimelineWideTypography.NumeralDotMaxPx })
        {
            float off = TimelineWideTypography.LabelOffset(dot, 1f);
            float up = TimelineWideTypography.AboveRoom(WideH, off), down = TimelineWideTypography.BelowRoom(WideH, off);
            log.Near(TimelineWideTypography.SafeTop(WideH), lineY - off - up, 0.01f, $"dot {dot:F0}: the above-line room ends at the title-safe top");
            log.Near(TimelineWideTypography.SafeBottom(WideH),
                lineY + off + down + TimelineWideTypography.SpanGapPx + TimelineWideTypography.SpanPillHeightPx, 0.01f,
                $"dot {dot:F0}: under the below-line room, the span's gap and pill end at the title-safe bottom");
            // A two-line date over a two-line event at the largest size (rows ≤ 1.25 × the font size).
            float tallest = 2f * 1.25f * TimelineWideTypography.DatePx + TimelineWideTypography.DateToEventGapPx + 2f * 1.25f * TimelineWideTypography.EventMaxPx;
            log.IsTrue(tallest <= down && tallest <= up, $"dot {dot:F0}: the tallest label ({tallest:F0} px) fits either side ({up:F0} / {down:F0} px)");
        }
    }

    static void TimelineWide_NumeralRule(Log log)
    {
        foreach (string s in new[] { "1", "2", "10", "Q4", " 7 " })
            log.IsTrue(TimelineWideTypography.IsNumeral(s) && !TimelineWideTypography.HasDateLabel(s), $"\"{s}\" (one or two characters) is a numeral in the dot");
        foreach (string s in new[] { "Oct. 25", "2026", "Mar", "100", "Sun. 11 Oct." })
            log.IsTrue(!TimelineWideTypography.IsNumeral(s) && TimelineWideTypography.HasDateLabel(s), $"\"{s}\" is a date label");
        foreach (string s in new[] { "", "   ", null })
            log.IsTrue(!TimelineWideTypography.IsNumeral(s) && !TimelineWideTypography.HasDateLabel(s),
                $"{(s == null ? "null" : "\"" + s + "\"")} is no numeral — and there is no date to draw");

        float dot = TimelineWideTypography.DotDiameter(false, 1f), big = TimelineWideTypography.DotDiameter(true, 1f);
        log.Near(TimelineWideTypography.DotPx, dot, 0.001f, "a dated node's dot");
        log.Near(2.4f * dot, big, 0.01f, "a numbered node's dot is 2.4 × that");

        // Two heavy digits at 40 px: about 0.66 em each, capitals 0.727 em high.
        float two = 2f * 0.66f * TimelineWideTypography.NumeralPx, cap = 0.727f * TimelineWideTypography.NumeralPx;
        log.Near(big, TimelineWideTypography.NumeralDotDiameter(two, cap, 1f), 0.01f, "two digits fit the 2.4 × dot as it is");
        log.IsTrue(TimelineWideTypography.NumeralFits(two, cap, big), "…inside the circle, corner to corner");
        float wide = 2f * 0.95f * TimelineWideTypography.NumeralPx;
        float grown = TimelineWideTypography.NumeralDotDiameter(wide, cap, 1f);
        log.IsTrue(grown > big && grown <= TimelineWideTypography.NumeralDotMaxPx && TimelineWideTypography.NumeralFits(wide, cap, grown),
            $"a wide pair grows the dot until it fits ({grown:F0} px)");
        log.Near(TimelineWideTypography.NumeralDotMaxPx, TimelineWideTypography.NumeralDotDiameter(400f, cap, 1f), 0.01f, "…never past its ceiling");
        log.IsTrue(!TimelineWideTypography.NumeralFits(400f, cap, TimelineWideTypography.NumeralDotMaxPx), "…where the card warns instead");
        log.Near(2f * big, TimelineWideTypography.NumeralDotDiameter(2f * two, 2f * cap, 2f), 0.02f, "a 4K frame gets a 2× dot");
        log.Near(TimelineWideTypography.DotToLabelGapPx, TimelineWideTypography.LabelOffset(big, 1f) - big * 0.5f, 0.001f,
            "the labels start a fixed gap past the run's largest dot");

        log.IsTrue(TimelineWideTypography.DatePx * TimelineWideTypography.SmallCapsScale >= CardTypography.HardFloorPx - 0.001f,
            $"small-caps date letters stay at or above 32 px ({TimelineWideTypography.DatePx * TimelineWideTypography.SmallCapsScale:F1})");
        log.IsTrue(TimelineWideTypography.NumeralPx >= CardTypography.HardFloorPx && TimelineWideTypography.SpanPx >= CardTypography.HardFloorPx &&
                   TimelineWideTypography.EventMinPx >= 40f && TimelineWideTypography.EventMaxPx <= 56f,
            "numeral, span and event sizes respect their floors");
    }

    static void TimelineWide_LabelWidths(Log log)
    {
        foreach (var (w, h) in new[] { (1920f, 1080f), (1280f, 720f), (3840f, 2160f) })
        {
            float k = TimelineWideTypography.ScaleFor(h), gap = TimelineWideTypography.LabelGapPx * k;
            float safeL = TimelineWideTypography.SafeLeft(w), safeR = TimelineWideTypography.SafeRight(w);
            for (int n = 1; n <= TimelineWideTypography.MaxNodes; n++)
            {
                bool inside = true, clear = true, capped = true, readable = true;
                float narrowest = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float x = TimelineWideTypography.NodeX(i, n, w), lw = TimelineWideTypography.LabelMaxWidth(i, n, w, h);
                    narrowest = Mathf.Min(narrowest, lw);
                    if (x - lw * 0.5f < safeL - 0.05f || x + lw * 0.5f > safeR + 0.05f) inside = false;
                    if (lw > w * TimelineWideTypography.MaxLabelWidthFraction + 0.05f) capped = false;
                    if (lw < 400f * k) readable = false;
                    if (i + 2 >= n) continue;
                    float x2 = TimelineWideTypography.NodeX(i + 2, n, w), lw2 = TimelineWideTypography.LabelMaxWidth(i + 2, n, w, h);
                    if (x + lw * 0.5f + gap > x2 - lw2 * 0.5f + 0.05f) clear = false;
                }
                string what = $"{w:F0}×{h:F0}, {n} node(s)";
                log.IsTrue(inside, $"{what}: every label box inside the 5 % title-safe margins");
                log.IsTrue(clear, $"{what}: a label never reaches its same-side neighbour (two slots on), {gap:F0} px to spare");
                log.IsTrue(capped, $"{what}: no label wider than 40 % of the frame");
                log.IsTrue(readable, $"{what}: every label has at least {400f * k:F0} px ({narrowest:F0})");
            }
            log.Near(w * 0.40f, TimelineWideTypography.LabelMaxWidth(0, 1, w, h), 0.05f, $"{w:F0}×{h:F0}: one node's label may be 40 % of the frame wide");
            log.Near(w * 0.40f, TimelineWideTypography.LabelMaxWidth(1, 2, w, h), 0.05f, $"{w:F0}×{h:F0}: …and so may each of two");
            log.Near(2f * TimelineWideTypography.SlotWidth(6, w) - gap, TimelineWideTypography.LabelMaxWidth(2, 6, w, h), 0.05f,
                $"{w:F0}×{h:F0}: a middle label of six is two slots less the gap");
            log.Near(2f * (TimelineWideTypography.NodeX(0, 6, w) - safeL), TimelineWideTypography.LabelMaxWidth(0, 6, w, h), 0.05f,
                $"{w:F0}×{h:F0}: an end label of six is held by the title-safe edge");
        }
    }

    static void TimelineWide_SpanGeometry(Log log)
    {
        float lineY = TimelineWideTypography.LineY(WideH);
        float offset = TimelineWideTypography.LabelOffset(TimelineWideTypography.DotPx, 1f);
        float belowBottom = lineY + offset + 186f; // a date over a two-line event
        float top = TimelineWideTypography.SpanTop(belowBottom, WideH);
        float ruleY = TimelineWideTypography.SpanRuleY(top, TimelineWideTypography.SpanPillHeightPx);
        log.Near(belowBottom + TimelineWideTypography.SpanGapPx, top, 0.01f, "the span's room starts a gap under the lowest below-line label");
        log.Near(top + TimelineWideTypography.SpanPillHeightPx * 0.5f, ruleY, 0.01f, "the rule runs through the middle of the pill");

        for (int n = 1; n <= TimelineWideTypography.MaxNodes; n++)
        {
            Rect all = TimelineWideTypography.SpanBracket(n - 1, n, WideW, WideH, ruleY);
            log.Near(TimelineWideTypography.NodeX(0, n, WideW), all.xMin, 0.01f, $"{n} node(s): the bracket starts under the first node");
            log.Near(TimelineWideTypography.NodeX(n - 1, n, WideW), all.xMax, 0.01f, $"{n} node(s): …and ends under the last");
        }
        Rect part = TimelineWideTypography.SpanBracket(2, 6, WideW, WideH, ruleY);
        log.Near(TimelineWideTypography.NodeX(2, 6, WideW), part.xMax, 0.01f, "three of six shown: the bracket ends under the last node SHOWN");
        log.Near(TimelineWideTypography.NodeX(3, 4, WideW), TimelineWideTypography.SpanBracket(9, 4, WideW, WideH, ruleY).xMax, 0.01f,
            "…and never past the last place");
        log.Near(0f, TimelineWideTypography.SpanBracket(0, 1, WideW, WideH, ruleY).width, 0.01f, "one node: a bracket of no width (the pill alone)");
        log.Near(ruleY + TimelineWideTypography.SpanRulePx * 0.5f, part.yMax, 0.01f, "the bracket's foot is the rule's lower edge");
        log.Near(ruleY - TimelineWideTypography.SpanRulePx * 0.5f - TimelineWideTypography.SpanTickPx, part.yMin, 0.01f, "its ticks rise above the rule");
        log.IsTrue(part.yMin >= top && part.yMin > belowBottom, "…inside the span's room, under the below-line labels");
        log.IsTrue(part.yMin > lineY, "the whole bracket is under the line");

        // A below-line label that fills its room still leaves the pill title-safe.
        float fullBelow = lineY + offset + TimelineWideTypography.BelowRoom(WideH, offset);
        log.Near(TimelineWideTypography.SafeBottom(WideH), TimelineWideTypography.SpanTop(fullBelow, WideH) + TimelineWideTypography.SpanPillHeightPx, 0.01f,
            "under the tallest below-line label the pill still ends at the title-safe bottom");
        log.IsTrue(TimelineWideTypography.SpanPillHeightPx >= 1.25f * TimelineWideTypography.SpanPx + 2f * TimelineWideTypography.SpanPillPadYPx - 0.01f,
            "one row of 44 px text and its padding fit the pill");

        log.Near(960f, TimelineWideTypography.SpanPillCentreX(960f, 600f, WideW), 0.01f, "the pill sits on the bracket's centre");
        log.Near(TimelineWideTypography.SafeLeft(WideW) + 400f, TimelineWideTypography.SpanPillCentreX(448f, 800f, WideW), 0.01f,
            "…pushed in only as far as title-safe needs (left)");
        log.Near(TimelineWideTypography.SafeRight(WideW) - 400f, TimelineWideTypography.SpanPillCentreX(1700f, 800f, WideW), 0.01f, "…(right)");
        log.IsTrue(TimelineWideTypography.SpanPillMaxWidth(WideW) <= TimelineWideTypography.SafeRight(WideW) - TimelineWideTypography.SafeLeft(WideW),
            "the widest pill is narrower than the title-safe width");
    }

    static void TimelineWide_Timing(Log log)
    {
        float left = TimelineWideTypography.LineLeft(WideW);
        log.Near(left, TimelineWideTypography.LineEndAt(0, 6, WideW, 0f), 0.01f, "the first node's stretch of line starts at the line's left end");
        log.Near(TimelineWideTypography.NodeX(0, 6, WideW), TimelineWideTypography.LineEndAt(0, 6, WideW, 0.3f), 0.01f, "…and reaches the node in 0.3 s");
        log.Near(TimelineWideTypography.NodeX(2, 6, WideW), TimelineWideTypography.LineEndAt(3, 6, WideW, 0f), 0.01f, "a later node's stretch starts at the node before it");
        log.Near(TimelineWideTypography.NodeX(3, 6, WideW), TimelineWideTypography.LineEndAt(3, 6, WideW, TimelineTypography.LineDrawSeconds), 0.01f, "…and reaches it in 0.3 s");
        log.Near(TimelineWideTypography.NodeX(3, 6, WideW), TimelineWideTypography.LineEndAt(3, 6, WideW, 5f), 0.01f, "…and stays there");
        log.Near(TimelineWideTypography.NodeX(2, 6, WideW), TimelineWideTypography.LineEndAt(3, 6, WideW, -1f), 0.01f, "…not moving before its tag");
        bool forward = true;
        float lastEnd = float.MinValue;
        for (float t = 0f; t <= 0.4f; t += 0.01f)
        {
            float end = TimelineWideTypography.LineEndAt(3, 6, WideW, t);
            if (end < lastEnd - 0.001f) forward = false;
            lastEnd = end;
        }
        log.IsTrue(forward, "the line only ever grows");

        log.Near(TimelineTypography.EntranceSeconds, TimelineWideTypography.EntranceSeconds, 1e-6f, "a node's entrance is the side timeline's");
        log.Near(0.3f, TimelineTypography.PopStart, 1e-6f, "the dot pops as the line arrives");
        log.IsTrue(TimelineWideTypography.EntranceSeconds >= TimelineTypography.PopStart + TimelineTypography.DotPopSeconds - 1e-6f &&
                   TimelineWideTypography.EntranceSeconds >= TimelineTypography.PopStart + TimelineTypography.TextSlideSeconds - 1e-6f,
            "…and the entrance is over once dot and label are in");
        log.Near(0.7f, TimelineTypography.OlderAlpha, 1e-6f, "earlier nodes settle to 70 %");

        log.AreEqual(0f, TimelineWideTypography.SpanGrowAt(0f), "the bracket starts with no width");
        log.Near(1f, TimelineWideTypography.SpanGrowAt(TimelineWideTypography.SpanGrowSeconds), 1e-6f, "…and is full after 0.3 s");
        log.IsTrue(TimelineWideTypography.SpanGrowAt(0.1f) > 0f && TimelineWideTypography.SpanGrowAt(0.1f) < TimelineWideTypography.SpanGrowAt(0.2f) &&
                   TimelineWideTypography.SpanGrowAt(0.2f) < 1f, "…growing all the way");
        log.AreEqual(0f, TimelineWideTypography.SpanPillScaleAt(0f), "the pill is not there before it pops");
        float peak = 0f;
        for (float t = 0f; t <= TimelineWideTypography.SpanPillPopSeconds; t += 0.002f) peak = Mathf.Max(peak, TimelineWideTypography.SpanPillScaleAt(t));
        log.IsTrue(peak > 1.02f && peak < 1.2f, $"…it pops with a small overshoot (peak {peak:F3}×)");
        log.Near(1f, TimelineWideTypography.SpanPillScaleAt(TimelineWideTypography.SpanPillPopSeconds), 1e-6f, "…and settles at 1.0");
        log.Near(0.3f, TimelineWideTypography.SpanSeconds, 1e-6f, "the span is in after 0.3 s");
        log.AreEqual(0f, TimelineWideTypography.SpanAlphaAt(0f), "nothing of the span shows on its tag's own frame");
        log.Near(1f, TimelineWideTypography.SpanAlphaAt(TimelineWideTypography.SpanFadeSeconds), 1e-6f, "…and it is opaque a tenth of a second later");

        log.AreEqual(0f, TimelineWideTypography.TrackAlphaAt(0f), "the faint track fades in with the card");
        log.Near(TimelineWideTypography.TrackAlpha, TimelineWideTypography.TrackAlphaAt(1f), 1e-6f, "…to its resting alpha");
        log.IsTrue(TimelineWideTypography.TrackAlpha < 0.5f, "…well under the solid line");

        log.Near(0.2f, TimelineWideTypography.ContentFadeOutSeconds, 1e-6f, "the content fades out in 0.2 s");
        log.IsTrue(TimelineWideTypography.ContentFadeOutSeconds <= BigTextTypography.DimFadeOutSeconds, "…gone before the dim has closed");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    static readonly (string date, string evt)[] WideSix =
    {
        ("Oct. 21", "Atlas launches"),
        ("Oct. 28", "Comet goes free"),
        ("Nov. 18", "Gemini lands in Chrome"),
        ("Dec. 2", "Edge adds Copilot Mode"),
        ("Jan. 14", "Opera ships Neon"),
        ("Mar. 3", "Firefox tests an AI window"),
    };

    // A card over a full frame, opened the way the controller opens one:
    // read-ahead set, Initialize, Show, then the next nodes.
    static TimelineWideCard BuildWide(IList<(string date, string evt)> all, int show, out GameObject root,
        bool spanAnticipated = false, bool readAhead = true, bool settle = true, float frameW = WideW, float frameH = WideH)
    {
        var card = BuildCard<TimelineWideCard>(out root);
        ((RectTransform)card.transform.parent).sizeDelta = new Vector2(frameW, frameH);
        if (readAhead) card.AnticipatedNodes = new List<(string date, string evt)>(all);
        card.SpanAnticipated = spanAnticipated;
        card.Initialize(new ContentCardEvent
        {
            cardType = ContentCardType.TimelineWide, primaryText = all[0].date, secondaryText = all[0].evt,
        }, null);
        card.Show();
        for (int i = 1; i < show; i++) card.AppendNode(all[i].date, all[i].evt, instant: true);
        if (settle) SettleWide(card);
        return card;
    }

    // 1.5 s at 30 fps: every entrance is over.
    static void SettleWide(TimelineWideCard card)
    {
        for (int f = 0; f < 45; f++) card.Advance(1f / 30f);
    }

    static bool SameRect(Rect a, Rect b, float tolerance) =>
        Mathf.Abs(a.xMin - b.xMin) <= tolerance && Mathf.Abs(a.xMax - b.xMax) <= tolerance &&
        Mathf.Abs(a.yMin - b.yMin) <= tolerance && Mathf.Abs(a.yMax - b.yMax) <= tolerance;

    // Everything that must hold for a settled card, whatever it shows.
    // spanLast: the last node the span was drawn for (when the card has one).
    static void CheckWide(Log log, TimelineWideCard card, string what, int spanLast = -1, string spanText = null)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        log.IsTrue(report != null, $"{what}: laid out");
        if (report == null) return;

        Vector2 frame = card.Frame;
        float k = TimelineWideTypography.ScaleFor(frame.y);
        Rect safe = Rect.MinMaxRect(frame.x * 0.05f, frame.y * 0.05f, frame.x * 0.95f, frame.y * 0.95f);
        bool overflowWarned = report.Warnings.Exists(w => w.Contains("don't fit") || w.Contains("runs past"));
        int n = card.NodeCount, places = card.Places;
        float lineY = card.LineY;
        float floor = CardTypography.HardFloorPx * k;
        Color accent = ContentCardUIBuilder.AccentColor;
        Color olderDate = new Color(1f, 1f, 1f, TimelineWideTypography.DateAlpha);

        log.IsTrue(Mathf.Abs(rt.rect.width - frame.x) < 0.5f && Mathf.Abs(rt.rect.height - frame.y) < 0.5f,
            $"{what}: laid out for the frame it covers ({frame.x:F0}×{frame.y:F0})");
        Transform dim = card.transform.Find("Dim");
        log.IsTrue(dim != null && dim.GetSiblingIndex() == 0, $"{what}: the dim is the bottom layer");
        log.Near(card.Dim.Peak, card.DimAlpha, 1e-4f, $"{what}: the dim is up (the BigText dim, {card.DimAlpha:F3})");
        log.Near(1f, card.GetComponent<CanvasGroup>().alpha, 1e-4f, $"{what}: the card root is fully visible");
        log.IsTrue(card.transform.Find("Content/Background") == null && card.transform.Find("Background") == null,
            $"{what}: no panel behind the timeline — it is drawn on the dim");

        // The line: 80 % of the width at mid height, the solid part up to the newest node.
        Rect track = RectInCard(card.Track.rectTransform, rt);
        log.Near(frame.x * 0.80f, track.width, 0.5f, $"{what}: the line crosses 80 % of the frame");
        log.Near(frame.x * 0.5f, track.center.x, 0.5f, $"{what}: …centred");
        log.Near(frame.y * 0.5f, track.center.y, 0.5f, $"{what}: …at mid height");
        log.Near(lineY, track.center.y, 0.5f, $"{what}: …where the nodes are");
        Rect solid = RectInCard(card.Line.rectTransform, rt);
        log.IsTrue(card.Line.enabled, $"{what}: the solid line is drawn");
        log.Near(track.xMin, solid.xMin, 0.5f, $"{what}: the solid line starts at the line's left end");
        log.Near(card.NodeX(n - 1), solid.xMax, 0.5f, $"{what}: …and reaches the newest node");
        log.Near(card.NodeX(n - 1), card.LineEndX, 0.5f, $"{what}: …(LineEndX agrees)");
        log.Near(lineY, solid.center.y, 0.5f, $"{what}: …along the track");

        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name),
                $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        log.IsTrue(card.EventSize <= TimelineWideTypography.EventMaxPx * k + 0.01f && card.EventSize >= floor - 0.01f,
            $"{what}: events between 32 and 56 px ({card.EventSize / k:F0})");

        var boxes = new Rect[n];
        for (int i = 0; i < n; i++)
        {
            string node = $"{what}: node {i + 1}";
            bool above = i % 2 == 0, numeral = card.IsNumeral(i), dated = card.HasDate(i), newest = i == n - 1;

            // Its place, its dot.
            log.Near(TimelineWideTypography.NodeX(i, places, frame.x), card.NodeX(i), 0.05f, $"{node} in the middle of slot {i + 1} of {places}");
            Rect dot = RectInCard(card.Dot(i).rectTransform, rt);
            log.Near(card.NodeX(i), dot.center.x, 0.5f, $"{node}: its dot on its place");
            log.Near(lineY, dot.center.y, 0.5f, $"{node}: …on the line");
            log.Near(1f, card.DotScale(i), 1e-4f, $"{node}: its dot has popped");
            log.Near(card.DotDiameter(i), dot.width, 0.5f, $"{node}: …at its rest size ({dot.width:F0} px)");
            log.Near(1f, card.LabelAlpha(i), 1e-4f, $"{node}: its label is in");
            log.IsTrue(newest ? card.Dot(i).color == accent : card.Dot(i).color == TimelineWideCard.Slate,
                $"{node}: dot {(newest ? "in the accent (newest)" : "neutral (earlier)")}");
            log.Near(newest ? 1f : TimelineTypography.OlderAlpha, card.NodeAlpha(i), 1e-3f, $"{node} at {(newest ? "100" : "70")} %");

            // Its label box: at rest, centred on the node, on its side of the line, title-safe.
            boxes[i] = RectInCard(card.NodeLabel(i), rt);
            log.IsTrue(SameRect(boxes[i], card.LabelRect(i), 0.5f), $"{node}: label at rest ({Fmt(boxes[i])} vs {Fmt(card.LabelRect(i))})");
            log.Near(card.NodeX(i), boxes[i].center.x, 0.5f, $"{node}: label centred on its x");
            log.IsTrue(above ? boxes[i].yMax < dot.yMin : boxes[i].yMin > dot.yMax,
                $"{node}: label {(above ? "above" : "below")} the line, clear of its dot");
            log.IsTrue(overflowWarned || Inside(boxes[i], safe, 1f), $"{node}: label inside the 5 % margins ({Fmt(boxes[i])})");
            log.IsTrue(boxes[i].width <= frame.x * TimelineWideTypography.MaxLabelWidthFraction + 0.5f, $"{node}: label no wider than 40 % of the frame");

            // Its texts.
            log.IsTrue(!(numeral && dated), $"{node}: a numeral or a date, never both");
            log.AreEqual(dated, card.DateText(i).gameObject.activeSelf, $"{node}: {(dated ? "a date line" : "no date line")}");
            log.AreEqual(numeral, card.NumeralText(i).gameObject.activeSelf, $"{node}: {(numeral ? "a numeral in its dot" : "no numeral")}");
            var texts = new List<TMP_Text> { card.EventText(i) };
            if (dated) texts.Add(card.DateText(i));
            if (numeral) texts.Add(card.NumeralText(i));
            foreach (TMP_Text t in texts)
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                log.IsTrue(t.fontSize >= floor - 0.01f, $"{node}: {t.name} at or above 32 px ({t.fontSize / k:F1})");
                log.IsTrue(!t.isTextTruncated, $"{node}: {t.name} not truncated");
                Rect ink = CardTypography.InkInCard(t, rt);
                log.IsTrue(overflowWarned || Inside(ink, safe, 1f), $"{node}: {t.name} inside the 5 % margins ({Fmt(ink)})");
                if (t == card.NumeralText(i)) continue;
                log.IsTrue(overflowWarned || Inside(ink, boxes[i], 2f), $"{node}: {t.name} inside its label box ({Fmt(ink)} in {Fmt(boxes[i])})");
                log.Near(card.NodeX(i), ink.center.x, 8f * k, $"{node}: {t.name} centred on the node");
                log.IsTrue(t.color.r > 0.99f && t.color.g > 0.99f && t.color.b > 0.99f || t == card.DateText(i) && newest,
                    $"{node}: {t.name} in white (the newest date in the accent)");
            }

            log.Near(card.EventSize, card.EventText(i).fontSize, 0.01f, $"{node}: the one event size");
            card.EventText(i).ForceMeshUpdate(true, true);
            int lines = card.EventText(i).textInfo.lineCount;
            log.IsTrue(lines <= TimelineWideTypography.EventMaxLines || overflowWarned, $"{node}: event on {lines} line(s), at most two (or warned)");

            if (dated)
            {
                TMP_Text date = card.DateText(i);
                log.Near(TimelineWideTypography.DatePx * k, date.fontSize, 0.01f, $"{node}: date at 40 px");
                log.IsTrue((date.fontStyle & FontStyles.SmallCaps) != 0, $"{node}: date in small caps");
                log.IsTrue(newest ? date.color == accent : date.color == olderDate, $"{node}: date {(newest ? "in the accent" : "white at 85 %")}");
                log.Near(TimelineWideTypography.DotPx * k, card.DotDiameter(i), 0.01f, $"{node}: a dated node keeps the normal dot");
                if (!string.IsNullOrEmpty(card.EventText(i).text))
                    log.IsTrue(CardTypography.InkInCard(date, rt).yMax <= CardTypography.InkInCard(card.EventText(i), rt).yMin + 0.5f,
                        $"{node}: the date over its event");
            }
            if (numeral)
            {
                TMP_Text num = card.NumeralText(i);
                log.IsTrue(card.DotDiameter(i) >= TimelineWideTypography.DotPx * TimelineWideTypography.NumeralDotScale * k - 0.01f,
                    $"{node}: an enlarged dot ({card.DotDiameter(i):F0} px, at least 2.4 × the normal one)");
                Rect ink = CardTypography.InkInCard(num, rt);
                log.IsTrue(Inside(ink, dot, 0.5f), $"{node}: the numeral inside its dot ({Fmt(ink)} in {Fmt(dot)})");
                log.IsTrue(Vector2.Distance(ink.center, dot.center) <= 2f * k,
                    $"{node}: …centred on it (off by {Vector2.Distance(ink.center, dot.center):F1} px)");
                float reach = Mathf.Sqrt(ink.width * ink.width + ink.height * ink.height) * 0.5f + Vector2.Distance(ink.center, dot.center);
                log.IsTrue(reach <= dot.width * 0.5f + 0.5f || report.WarnedAbout("numerals"),
                    $"{node}: …inside the circle, not just its square (corner {reach:F1} of {dot.width * 0.5f:F1} px)");
                log.IsTrue(num.color == Color.white, $"{node}: the numeral in white");
                // No date line: the label is the event alone.
                Rect evt = RectInCard(card.EventText(i).rectTransform, rt);
                log.IsTrue(Mathf.Abs(evt.yMin - boxes[i].yMin) < 0.5f && Mathf.Abs(evt.yMax - boxes[i].yMax) < 0.5f,
                    $"{node}: its event stands alone in the label");
            }
            if (!dated && !numeral)
                log.Near(TimelineWideTypography.DotPx * k, card.DotDiameter(i), 0.01f, $"{node}: no first field — the normal dot");
        }

        // No two labels overlap; same-side neighbours keep their gap.
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                log.IsTrue(!boxes[i].Overlaps(boxes[j]), $"{what}: labels {i + 1} and {j + 1} don't overlap ({Fmt(boxes[i])} / {Fmt(boxes[j])})");
        for (int i = 0; i + 2 < n; i++)
            log.IsTrue(boxes[i].xMax + TimelineWideTypography.LabelGapPx * k <= boxes[i + 2].xMin + 0.5f,
                $"{what}: labels {i + 1} and {i + 3} (same side) keep {TimelineWideTypography.LabelGapPx * k:F0} px apart");

        if (!card.HasSpan)
        {
            log.IsTrue(!card.SpanRoot.gameObject.activeSelf, $"{what}: no span drawn");
            log.IsTrue(!report.Elements.Exists(e => e.Name == "span"), $"{what}: no span in the report");
        }
        else CheckWideSpan(log, card, what, boxes, spanLast, spanText);

        Debug.Log($"[TimelineWide] self-test {what}: {n} of {places} node(s), events {card.EventSize / k:F0} px" +
                  (card.HasSpan ? $", span \"{card.SpanString}\"" : "") +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void CheckWideSpan(Log log, TimelineWideCard card, string what, Rect[] boxes, int last, string text)
    {
        var rt = (RectTransform)card.transform;
        Vector2 frame = card.Frame;
        float k = TimelineWideTypography.ScaleFor(frame.y);
        Rect safe = Rect.MinMaxRect(frame.x * 0.05f, frame.y * 0.05f, frame.x * 0.95f, frame.y * 0.95f);
        Color accent = ContentCardUIBuilder.AccentColor;
        bool hasText = !string.IsNullOrWhiteSpace(card.SpanString);

        log.IsTrue(card.SpanRoot.gameObject.activeSelf, $"{what}: the span is drawn");
        if (text != null) log.AreEqual(text, card.SpanString, $"{what}: the span text is the tag's, as written");
        if (last < 0) last = card.NodeCount - 1;

        // The bracket: a rule from the first node to the last one shown, a tick rising from each end.
        Rect rule = RectInCard(card.SpanRule.rectTransform, rt);
        Rect tickL = RectInCard(card.SpanTick(0).rectTransform, rt), tickR = RectInCard(card.SpanTick(1).rectTransform, rt);
        log.Near(card.NodeX(0), rule.xMin, 0.5f, $"{what}: the bracket starts under the first node");
        log.Near(card.NodeX(last), rule.xMax, 0.5f, $"{what}: …and ends under the last node shown (node {last + 1})");
        log.Near(TimelineWideTypography.SpanRulePx * k, rule.height, 0.5f, $"{what}: a thin rule");
        log.Near(card.SpanRuleY, rule.center.y, 0.5f, $"{what}: …at the span's height");
        log.Near(card.NodeX(0), tickL.center.x, 0.5f, $"{what}: a tick at its left end");
        log.Near(card.NodeX(last), tickR.center.x, 0.5f, $"{what}: …and at its right end");
        log.IsTrue(Mathf.Abs(tickL.yMax - rule.yMax) < 0.5f && Mathf.Abs(tickR.yMax - rule.yMax) < 0.5f, $"{what}: the ticks stand on the rule");
        log.Near(TimelineWideTypography.SpanTickPx * k, rule.yMin - tickL.yMin, 0.5f, $"{what}: …and rise above it");
        log.Near(TimelineWideTypography.SpanTickPx * k, rule.yMin - tickR.yMin, 0.5f, $"{what}: …both of them");
        log.IsTrue(card.SpanRule.color == accent && card.SpanTick(0).color == accent && card.SpanTick(1).color == accent, $"{what}: the bracket in the accent");
        Rect bracket = Rect.MinMaxRect(tickL.xMin, tickL.yMin, tickR.xMax, rule.yMax);
        log.IsTrue(bracket.yMin > card.LineY, $"{what}: the bracket is under the line");
        log.IsTrue(Mathf.Abs(bracket.xMin - card.SpanBracketRect.xMin) <= TimelineWideTypography.SpanRulePx * k &&
                   Mathf.Abs(bracket.xMax - card.SpanBracketRect.xMax) <= TimelineWideTypography.SpanRulePx * k &&
                   Mathf.Abs(bracket.yMin - card.SpanBracketRect.yMin) < 0.5f && Mathf.Abs(bracket.yMax - card.SpanBracketRect.yMax) < 0.5f,
            $"{what}: …where SpanBracketRect says ({Fmt(bracket)} vs {Fmt(card.SpanBracketRect)})");

        Rect pill = hasText ? RectInCard(card.SpanPill, rt) : Rect.zero;
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i].height <= 0f) continue;
            log.IsTrue(!bracket.Overlaps(boxes[i]), $"{what}: the bracket clear of label {i + 1}");
            if (hasText) log.IsTrue(!pill.Overlaps(boxes[i]), $"{what}: the pill clear of label {i + 1}");
            if (i % 2 == 0) continue;
            log.IsTrue(bracket.yMin >= boxes[i].yMax, $"{what}: the bracket under below-line label {i + 1}");
            if (hasText) log.IsTrue(pill.yMin >= boxes[i].yMax, $"{what}: the pill under below-line label {i + 1}");
        }

        log.AreEqual(hasText, card.SpanPill.gameObject.activeSelf, $"{what}: {(hasText ? "a pill for the text" : "no text, no pill")}");
        log.AreEqual(hasText ? 1 : 0, card.Layout.Elements.FindAll(e => e.Name == "span").Count, $"{what}: the span is in the report once (when it has text)");
        if (!hasText) return;

        // The pill: on the rule, at the bracket's centre, title-safe, the text inside it.
        log.IsTrue(SameRect(pill, card.SpanPillRect, 0.5f), $"{what}: the pill at rest ({Fmt(pill)} vs {Fmt(card.SpanPillRect)})");
        log.Near(1f, card.SpanPill.localScale.x, 1e-4f, $"{what}: the pill has popped");
        log.Near(rule.center.y, pill.center.y, 0.5f, $"{what}: the pill sits on the rule");
        log.IsTrue(Inside(pill, safe, 1f) || card.Layout.Warnings.Exists(w => w.Contains("span runs past")), $"{what}: the pill inside the 5 % margins ({Fmt(pill)})");
        float centre = (card.NodeX(0) + card.NodeX(last)) * 0.5f;
        if (centre - pill.width * 0.5f >= safe.xMin && centre + pill.width * 0.5f <= safe.xMax)
            log.Near(centre, pill.center.x, 0.5f, $"{what}: the pill at the bracket's centre");
        log.IsTrue(pill.width >= pill.height - 0.5f, $"{what}: the pill is at least as wide as it is high");

        Transform fillT = card.SpanPill.Find("Fill");
        Image fill = fillT != null ? fillT.GetComponent<Image>() : null;
        log.IsTrue(fill != null && fill.color == accent, $"{what}: the pill in the accent");
        if (fill != null && fill.sprite != null)
        {
            log.IsTrue(fill.type == Image.Type.Sliced, $"{what}: the pill is a sliced rounded rect");
            float drawnRadius = fill.sprite.border.x / (fill.pixelsPerUnit * fill.pixelsPerUnitMultiplier);
            log.Near(pill.height * 0.5f, drawnRadius, 0.75f, $"{what}: …its corner radius half its height ({drawnRadius:F1} of {pill.height:F0} px)");
        }
        else log.IsTrue(false, $"{what}: the pill has its rounded sprite");

        TMP_Text label = card.SpanLabel;
        log.AreEqual(card.SpanString, label.text, $"{what}: the pill shows the span text");
        log.IsTrue(label.color == Color.white, $"{what}: …in white");
        log.IsTrue(label.fontSize >= CardTypography.HardFloorPx * k - 0.01f && label.fontSize <= TimelineWideTypography.SpanPx * k + 0.01f,
            $"{what}: …between 32 and 44 px ({label.fontSize / k:F0})");
        log.IsTrue(!label.isTextTruncated, $"{what}: …not truncated");
        Rect ink = CardTypography.InkInCard(label, rt);
        log.IsTrue(Inside(ink, pill, 1f), $"{what}: …inside the pill ({Fmt(ink)} in {Fmt(pill)})");
        log.Near(pill.center.x, ink.center.x, 4f * k, $"{what}: …centred in it");
        log.IsTrue(label.textInfo.lineCount == 1 || card.Layout.Warnings.Exists(w => w.Contains("wrapped")), $"{what}: …on one line (or warned)");
    }

    static void TimelineWide_CardLayout(Log log)
    {
        var neighbours = new[]
        {
            ("Oct. 21", "Atlas launches"),
            ("Oct. 28", "The second browser ships with an agent built in"),
            ("Nov. 18", "A third one follows three weeks later for everyone"),
            ("Dec. 2", "Edge adds a mode"),
        };
        var sixLong = new[]
        {
            ("Oct. 21, 2025", "OpenAI launches its Atlas browser"),
            ("Oct. 28, 2025", "Perplexity makes Comet free for all"),
            ("Nov. 18, 2025", "Gemini 3 arrives inside Chrome"),
            ("Dec. 2, 2025", "Edge adds Copilot Mode to every tab"),
            ("Jan. 14, 2026", "Opera ships Neon to subscribers"),
            ("Mar. 3, 2026", "Firefox tests an AI window"),
        };
        var cases = new List<(IList<(string date, string evt)> all, int show, string what)>
        {
            (WideSix, 1, "1 of 6 nodes"),
            (new[] { WideSix[0], WideSix[1] }, 2, "2 nodes"),
            (new[] { WideSix[0], WideSix[1], WideSix[2], WideSix[3] }, 4, "4 nodes"),
            (WideSix, 6, "6 nodes"),
            (neighbours, 4, "2-line events on neighbours"),
            (sixLong, 6, "6 nodes, longer events"),
        };
        float sixSize = 0f;
        foreach (var c in cases)
        {
            var card = BuildWide(c.all, c.show, out GameObject root);
            try
            {
                var rt = (RectTransform)card.transform;
                CheckWide(log, card, $"TimelineWide ({c.what})");
                log.AreEqual(c.show, card.NodeCount, $"TimelineWide ({c.what}): {c.show} node(s) shown");
                log.AreEqual(c.all.Count, card.Places, $"TimelineWide ({c.what}): a place for every node of the run");
                if (c.what == "1 of 6 nodes")
                {
                    log.Near(TimelineWideTypography.NodeX(0, 6, WideW), card.NodeX(0), 0.05f, "TimelineWide (1 of 6 nodes): the first node already sits in the first of six slots");
                    log.Near(TimelineWideTypography.TrackAlpha, card.Track.color.a, 1e-3f, "…and the faint track shows the whole line from the start");
                }
                if (c.what == "2 nodes")
                    log.Near(WideW * 0.40f, RectInCard(card.NodeLabel(0), rt).width, 0.5f, "TimelineWide (2 nodes): a label may be 40 % of the frame wide");
                if (c.what == "6 nodes")
                {
                    sixSize = card.EventSize;
                    log.IsTrue(card.Layout.Warnings.Count == 0 && card.EventSize >= TimelineWideTypography.EventMinPx,
                        $"TimelineWide (6 nodes): six short events at {card.EventSize:F0} px, no warning");
                    log.IsTrue(card.Layout.Elements.Exists(e => e.Name == "dates") && card.Layout.Elements.Exists(e => e.Name == "events"),
                        "TimelineWide (6 nodes): the report lists dates and events");
                }
                if (c.what == "2-line events on neighbours")
                {
                    card.EventText(1).ForceMeshUpdate(true, true);
                    card.EventText(2).ForceMeshUpdate(true, true);
                    log.IsTrue(card.EventText(1).textInfo.lineCount == 2 && card.EventText(2).textInfo.lineCount == 2,
                        $"TimelineWide ({c.what}): the two long events wrap onto two lines each " +
                        $"({card.EventText(1).textInfo.lineCount} / {card.EventText(2).textInfo.lineCount})");
                    log.IsTrue(card.Layout.Warnings.Count == 0 && card.EventSize >= TimelineWideTypography.EventMinPx,
                        $"TimelineWide ({c.what}): at {card.EventSize:F0} px, no warning");
                    Rect a = RectInCard(card.NodeLabel(1), rt), b = RectInCard(card.NodeLabel(2), rt);
                    log.IsTrue(a.yMin > card.LineY && b.yMax < card.LineY, $"TimelineWide ({c.what}): one under the line, the next over it");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Too long for two lines at any size: shown at 32 px on more lines, warned, never cut.
        var overlong = new[]
        {
            WideSix[0], WideSix[1],
            ("Nov. 18", "The third browser of the autumn arrives with an agent that books, buys and fills in forms on its own"),
            WideSix[3], WideSix[4], WideSix[5],
        };
        var ocard = BuildWide(overlong, 6, out GameObject oroot);
        try
        {
            log.IsTrue(ocard.Layout.Warnings.Exists(w => w.Contains("events")), "TimelineWide (overlong): warned, naming the events");
            log.Near(CardTypography.HardFloorPx, ocard.EventSize, 0.01f, $"TimelineWide (overlong): events at the 32 px floor, never under ({ocard.EventSize:F0})");
            log.IsTrue(ocard.EventText(2).text == overlong[2].Item2 && !ocard.EventText(2).isTextTruncated, "TimelineWide (overlong): the event is still all there");
            CheckWide(log, ocard, "TimelineWide (overlong)");
        }
        finally { UnityEngine.Object.DestroyImmediate(oroot); }

        // A 4K frame: the same card, twice the size.
        var big = BuildWide(WideSix, 6, out GameObject bigRoot, frameW: 2f * WideW, frameH: 2f * WideH);
        try
        {
            CheckWide(log, big, "TimelineWide (6 nodes, 3840×2160)");
            log.Near(2f * TimelineWideTypography.NodeX(3, 6, WideW), big.NodeX(3), 0.1f, "TimelineWide (3840×2160): places scale with the frame");
            log.IsTrue(Mathf.Abs(big.EventSize - 2f * sixSize) <= 3f, $"TimelineWide (3840×2160): events twice the 1080p size ({big.EventSize:F0} vs {sixSize:F0})");
        }
        finally { UnityEngine.Object.DestroyImmediate(bigRoot); }
    }

    static void TimelineWide_SeventhNodeIsRefused(Log log)
    {
        var card = BuildWide(WideSix, 6, out GameObject root);
        try
        {
            float x5 = card.NodeX(5), size = card.EventSize;
            log.IsTrue(!card.AppendNode("Apr. 1", "A seventh"), "TimelineWide: a 7th node is refused");
            SettleWide(card);
            log.AreEqual(6, card.NodeCount, "…the run keeps its six");
            log.AreEqual(6, card.Places, "…in six places");
            log.IsTrue(Mathf.Abs(card.NodeX(5) - x5) < 0.001f && Mathf.Abs(card.EventSize - size) < 0.001f, "…and nothing moved or resized");
            log.AreEqual(WideSix[5].evt, card.EventText(5).text, "…the sixth node still shows its own event");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // A read-ahead that lists more than six is cut at six.
        var eight = new List<(string date, string evt)>(WideSix) { ("Apr. 1", "A seventh"), ("May 5", "An eighth") };
        var many = BuildWide(eight, 1, out GameObject manyRoot);
        try
        {
            log.AreEqual(6, many.Places, "TimelineWide: a read-ahead of eight is laid out as six places");
            log.Near(TimelineWideTypography.NodeX(0, 6, WideW), many.NodeX(0), 0.05f, "…the first node in the first of six");
        }
        finally { UnityEngine.Object.DestroyImmediate(manyRoot); }
    }

    // The span: announced, given twice, absent, unannounced, after part of the
    // run, under one node, without text, too long for 44 px, and on a 4K frame.
    static void TimelineWide_Span(Log log)
    {
        const string text = "six months";
        const float dt = 1f / 30f;
        Rect announcedPill, announcedBracket;

        // ---- Announced: it grows in, and nothing else moves ---------------------------
        var card = BuildWide(WideSix, 6, out GameObject root, spanAnticipated: true);
        try
        {
            var rt = (RectTransform)card.transform;
            var before = new Rect[6];
            for (int i = 0; i < 6; i++) before[i] = RectInCard(card.NodeLabel(i), rt);
            float reserved = card.SpanTop;
            log.IsTrue(!card.HasSpan && !card.SpanRoot.gameObject.activeSelf, "TimelineWide span: nothing drawn before its tag");

            log.IsTrue(card.ShowSpan(text), "TimelineWide span: shown on its tag");
            log.IsTrue(card.HasSpan, "…HasSpan");
            log.Near(0f, card.SpanRule.rectTransform.sizeDelta.x, 0.01f, "…the bracket starts with no width");
            log.Near(0f, card.SpanPill.localScale.x, 1e-4f, "…the pill not yet popped");

            float full = card.NodeX(5) - card.NodeX(0), middle = (card.NodeX(0) + card.NodeX(5)) * 0.5f;
            float t = 0f, fullAt = float.NaN, poppedAt = float.NaN, lastWidth = 0f;
            bool grows = true, centred = true;
            for (int f = 0; f < 15; f++)
            {
                card.Advance(dt); t += dt;
                Rect r = RectInCard(card.SpanRule.rectTransform, rt);
                if (r.width < lastWidth - 0.01f) grows = false;
                lastWidth = r.width;
                if (Mathf.Abs(r.center.x - middle) > 0.5f) centred = false;
                if (float.IsNaN(fullAt) && r.width >= full - 0.5f) fullAt = t;
                if (float.IsNaN(poppedAt) && Mathf.Abs(card.SpanPill.localScale.x - 1f) < 1e-4f && t > 0.2f) poppedAt = t;
            }
            log.IsTrue(grows && centred, "TimelineWide span: the bracket grows from its centre outward");
            log.IsTrue(fullAt >= TimelineWideTypography.SpanGrowSeconds - dt - 1e-3f && fullAt <= TimelineWideTypography.SpanGrowSeconds + dt + 1e-3f,
                $"…reaching the first and last node after 0.3 s ({fullAt:F3})");
            log.IsTrue(poppedAt <= TimelineWideTypography.SpanPillPopSeconds + dt + 1e-3f, $"…the pill popped by then ({poppedAt:F3} s)");

            bool still = true;
            for (int i = 0; i < 6; i++) if (!SameRect(before[i], RectInCard(card.NodeLabel(i), rt), 0.01f)) still = false;
            log.IsTrue(still, "TimelineWide span: no label moved when it arrived");
            log.Near(reserved, card.SpanTop, 0.001f, "…it took the room held back for it");
            CheckWide(log, card, "TimelineWide (6 nodes + span)", 5, text);
            log.IsTrue(card.Layout.Warnings.Count == 0, "TimelineWide span: no warning");
            log.Near(TimelineWideTypography.SpanPx, card.SpanLabel.fontSize, 0.01f, "…its text at 44 px");
            announcedPill = card.SpanPillRect;
            announcedBracket = card.SpanBracketRect;

            // ---- Given twice: refused, nothing changes --------------------------------
            log.IsTrue(!card.ShowSpan("a second span"), "TimelineWide span: a second span is refused");
            SettleWide(card);
            log.AreEqual(text, card.SpanString, "…the first one's text stays");
            log.AreEqual(text, card.SpanLabel.text, "…on the pill too");
            log.AreEqual(1, card.Layout.Elements.FindAll(e => e.Name == "span").Count, "…and the report still lists one span");
            log.IsTrue(SameRect(announcedPill, card.SpanPillRect, 0.001f) && SameRect(announcedPill, RectInCard(card.SpanPill, rt), 0.5f),
                "…the pill where it was");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- Absent — and then unannounced: the same spot -------------------------------
        card = BuildWide(WideSix, 6, out root);
        try
        {
            var rt = (RectTransform)card.transform;
            CheckWide(log, card, "TimelineWide (6 nodes, no span)");
            float lowest = 0f;
            for (int i = 1; i < 6; i += 2) lowest = Mathf.Max(lowest, RectInCard(card.NodeLabel(i), rt).yMax);
            log.IsTrue(card.SpanTop >= lowest + TimelineWideTypography.SpanGapPx - 0.5f,
                $"TimelineWide (no span): its room is still held under the below-line labels ({card.SpanTop:F0} vs {lowest:F0})");
            log.IsTrue(card.SpanTop + TimelineWideTypography.SpanPillHeightPx <= WideH * 0.95f + 0.5f, "…inside title-safe");

            var before = new Rect[6];
            for (int i = 0; i < 6; i++) before[i] = RectInCard(card.NodeLabel(i), rt);
            log.IsTrue(card.ShowSpan(text), "TimelineWide span: one that was not announced is shown all the same");
            SettleWide(card);
            log.IsTrue(SameRect(announcedPill, card.SpanPillRect, 0.01f) && SameRect(announcedBracket, card.SpanBracketRect, 0.01f),
                $"…in the same spot as an announced one ({Fmt(card.SpanPillRect)} vs {Fmt(announcedPill)})");
            bool still = true;
            for (int i = 0; i < 6; i++) if (!SameRect(before[i], RectInCard(card.NodeLabel(i), rt), 0.01f)) still = false;
            log.IsTrue(still, "…and no label moved");
            CheckWide(log, card, "TimelineWide (6 nodes + unannounced span)", 5, text);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- After three of six: to the last node SHOWN; a later node doesn't stretch it -
        card = BuildWide(WideSix, 3, out root, spanAnticipated: true);
        try
        {
            var rt = (RectTransform)card.transform;
            log.IsTrue(card.ShowSpan("in four weeks"), "TimelineWide span (3 of 6): shown");
            SettleWide(card);
            CheckWide(log, card, "TimelineWide (3 of 6 nodes + span)", 2, "in four weeks");
            log.IsTrue(card.AppendNode(WideSix[3].date, WideSix[3].evt, instant: true), "TimelineWide span (3 of 6): a node can still arrive after it");
            SettleWide(card);
            log.Near(card.NodeX(2), RectInCard(card.SpanRule.rectTransform, rt).xMax, 0.5f, "…and the bracket stays on the nodes it was drawn for");
            CheckWide(log, card, "TimelineWide (4 of 6 nodes, span over 3)", 2, "in four weeks");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- One node: no width to bracket, the pill under the node ----------------------
        card = BuildWide(new[] { WideSix[0] }, 1, out root);
        try
        {
            log.IsTrue(card.ShowSpan("day one"), "TimelineWide span (1 node): shown");
            SettleWide(card);
            CheckWide(log, card, "TimelineWide (1 node + span)", 0, "day one");
            log.Near(card.NodeX(0), card.SpanPillRect.center.x, 0.5f, "TimelineWide span (1 node): the pill under the node");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- No text: the bracket alone ----------------------------------------------------
        card = BuildWide(new[] { WideSix[0], WideSix[1] }, 2, out root);
        try
        {
            log.IsTrue(card.ShowSpan(""), "TimelineWide span (no text): the bracket is still drawn");
            SettleWide(card);
            CheckWide(log, card, "TimelineWide (2 nodes + span without text)", 1, "");
            log.IsTrue(!card.ShowSpan(text), "…and it counts as the card's one span");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- Too long for 44 px: it shrinks (never under 32) and says so --------------------
        // (About 40 em of bold Inter: over the 1476 px a pill's text may take at
        // 44 px, under it at 32 px — with a tenth to spare either way.)
        const string longText = "five rival browsers reached the public in barely six months of work for everyone";
        card = BuildWide(WideSix, 6, out root, spanAnticipated: true);
        try
        {
            log.IsTrue(card.ShowSpan(longText), "TimelineWide span (long): shown");
            SettleWide(card);
            CheckWide(log, card, "TimelineWide (6 nodes + long span)", 5, longText);
            log.IsTrue(card.SpanLabel.fontSize < TimelineWideTypography.SpanPx - 0.5f && card.SpanLabel.fontSize >= CardTypography.HardFloorPx - 0.01f,
                $"TimelineWide span (long): the text shrinks to fit, never under 32 px ({card.SpanLabel.fontSize:F0})");
            log.IsTrue(card.Layout.Warnings.Exists(w => w.Contains("span")), "…with a warning naming the span");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // ---- A 4K frame: the pill keeps its shape past the sprite's largest radius ----------
        card = BuildWide(WideSix, 6, out root, spanAnticipated: true, frameW: 2f * WideW, frameH: 2f * WideH);
        try
        {
            log.IsTrue(card.ShowSpan(text), "TimelineWide span (3840×2160): shown");
            SettleWide(card);
            CheckWide(log, card, "TimelineWide (6 nodes + span, 3840×2160)", 5, text);
            log.IsTrue(SameRect(new Rect(announcedPill.x * 2f, announcedPill.y * 2f, announcedPill.width * 2f, announcedPill.height * 2f), card.SpanPillRect, 4f),
                $"…twice the 1080p pill ({Fmt(card.SpanPillRect)})");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // "1", "2", "3": numerals inside enlarged dots, no date labels.
    static void TimelineWide_NumberedRun(Log log)
    {
        var steps = new[] { ("1", "Open the browser"), ("2", "Hand it the task"), ("3", "Watch it click") };
        var card = BuildWide(steps, 3, out GameObject root);
        try
        {
            CheckWide(log, card, "TimelineWide (numbered 1–3)");
            for (int i = 0; i < 3; i++)
            {
                log.IsTrue(card.IsNumeral(i) && !card.HasDate(i), $"TimelineWide (numbered): \"{steps[i].Item1}\" is a numeral, not a date");
                log.AreEqual(steps[i].Item1, card.NumeralText(i).text, $"…shown as written in dot {i + 1}");
                log.IsTrue(card.NumeralText(i).gameObject.activeInHierarchy && !card.DateText(i).gameObject.activeSelf, $"…with no date label on node {i + 1}");
                log.AreEqual(steps[i].Item2, card.EventText(i).text, $"…and its event as written");
                log.Near(card.DotDiameter(0), card.DotDiameter(i), 0.01f, $"…dot {i + 1} the same size as the others");
            }
            log.IsTrue(!card.Layout.Elements.Exists(e => e.Name == "dates") && card.Layout.Elements.Exists(e => e.Name == "numerals") &&
                       card.Layout.Elements.Exists(e => e.Name == "events"),
                "TimelineWide (numbered): the report lists numerals and events, no dates");
            log.IsTrue(card.Layout.Warnings.Count == 0, "TimelineWide (numbered): no warning");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // Two digits fit the same dot.
        var late = new[] { ("9", "The ninth step"), ("10", "The tenth step") };
        card = BuildWide(late, 2, out root);
        try
        {
            CheckWide(log, card, "TimelineWide (numbered 9, 10)");
            log.IsTrue(card.IsNumeral(1) && card.NumeralText(1).text == "10", "TimelineWide (numbered): two digits are a numeral too");
            log.IsTrue(card.NumeralText(1).textInfo.lineCount == 1, "…on one line");
            log.IsTrue(card.Layout.Warnings.Count == 0, "…with no warning: they fit the dot");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // A run mixing numbers, a date and an empty first field.
    static void TimelineWide_MixedRun(Log log)
    {
        var mixed = new[] { ("1", "The first step"), ("Oct. 25", "A dated moment"), ("3", "The third step"), ("", "No first field") };
        var card = BuildWide(mixed, 4, out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            CheckWide(log, card, "TimelineWide (mixed run)");
            log.IsTrue(card.IsNumeral(0) && card.IsNumeral(2), "TimelineWide (mixed): \"1\" and \"3\" are numerals in their dots");
            log.IsTrue(!card.IsNumeral(1) && card.HasDate(1), "…\"Oct. 25\" is a date label");
            log.AreEqual("Oct. 25", card.DateText(1).text, "…shown as written");
            log.IsTrue(!card.IsNumeral(3) && !card.HasDate(3), "…and an empty first field is neither: its event stands alone");
            log.IsTrue(card.DotDiameter(0) > card.DotDiameter(1) && Mathf.Abs(card.DotDiameter(0) - card.DotDiameter(2)) < 0.01f &&
                       Mathf.Abs(card.DotDiameter(1) - card.DotDiameter(3)) < 0.01f,
                "…enlarged dots for the numbers, normal ones for the rest");

            // One distance from the line for the whole run, clear of the largest dot.
            Rect b0 = RectInCard(card.NodeLabel(0), rt), b1 = RectInCard(card.NodeLabel(1), rt);
            Rect b2 = RectInCard(card.NodeLabel(2), rt), b3 = RectInCard(card.NodeLabel(3), rt);
            log.Near(b0.yMax, b2.yMax, 0.5f, "TimelineWide (mixed): the above-line labels end at one height");
            log.Near(b1.yMin, b3.yMin, 0.5f, "…the below-line labels start at one height");
            log.Near(card.LineY - b0.yMax, b1.yMin - card.LineY, 0.5f, "…the same distance from the line on both sides");
            log.IsTrue(card.LineY - b0.yMax > card.DotDiameter(0) * 0.5f, "…clear of the enlarged dots");
            log.IsTrue(card.Layout.Elements.Exists(e => e.Name == "dates") && card.Layout.Elements.Exists(e => e.Name == "numerals"),
                "TimelineWide (mixed): the report lists dates and numerals");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // At 30 fps: the first node lands with the card (its entrance starts at
    // Show — line 0.3 s, then the dot pops and the label slides in — while the
    // dim fades in); the second, on its word, draws the line on from the first,
    // takes the accent as its dot pops, and the first settles to 70 %.
    static void TimelineWide_EntranceFrameByFrame(Log log)
    {
        var card = BuildWide(WideSix, 1, out GameObject root, settle: false);
        try
        {
            var rt = (RectTransform)card.transform;
            const float dt = 1f / 30f;
            Color accent = ContentCardUIBuilder.AccentColor;

            log.IsTrue(card.NodeCount == 1 && card.Places == 6, "at Show: one node of six");
            log.IsTrue(card.DotScale(0) == 0f && card.LabelAlpha(0) == 0f && card.LineLength == 0f,
                "at Show: nothing of the first node is drawn yet — its entrance starts now");
            log.Near(0f, card.DimAlpha, 1e-4f, "…and the dim is still down");
            log.Near(1f, card.GetComponent<CanvasGroup>().alpha, 1e-4f, "…the card root fully visible (it owns its entrance)");
            log.IsTrue(((RectTransform)card.transform).anchoredPosition == Vector2.zero, "…and not sliding in");

            float t = 0f, lineAt = float.NaN, popAt = float.NaN, labelAt = float.NaN, dimAt = float.NaN, lastLen = 0f;
            bool grows = true;
            for (int f = 0; f < 30; f++)
            {
                card.Advance(dt); t += dt;
                float len = card.LineLength;
                if (len < lastLen - 0.01f) grows = false;
                lastLen = len;
                if (float.IsNaN(lineAt) && card.LineEndX >= card.NodeX(0) - 0.5f) lineAt = t;
                if (float.IsNaN(popAt) && card.DotScale(0) > 0f) popAt = t;
                if (float.IsNaN(labelAt) && card.LabelAlpha(0) >= 1f - 1e-4f) labelAt = t;
                if (float.IsNaN(dimAt) && card.DimAlpha >= card.Dim.Peak - 1e-4f) dimAt = t;
            }
            log.IsTrue(grows, "first node: the line only ever grows");
            log.IsTrue(lineAt >= 0.3f - dt - 1e-3f && lineAt <= 0.3f + dt + 1e-3f, $"…reaching the first node in 0.3 s ({lineAt:F3})");
            log.IsTrue(popAt > 0.3f - 1e-3f && popAt <= 0.3f + dt + 1e-3f, $"its dot pops as the line arrives ({popAt:F3} s)");
            log.IsTrue(labelAt <= 0.3f + TimelineTypography.TextSlideSeconds + dt + 1e-3f, $"its label is in 0.3 s later ({labelAt:F3})");
            log.IsTrue(dimAt >= BigTextTypography.DimFadeInSeconds - dt && dimAt <= BigTextTypography.DimFadeInSeconds + dt + 1e-3f,
                $"the dim reaches its peak after its 0.2 s fade-in ({dimAt:F3} s)");
            log.IsTrue(card.Dot(0).color == accent && card.DateText(0).color == accent, "the only node holds the accent (dot and date)");
            log.Near(1f, card.NodeAlpha(0), 1e-4f, "…at full opacity");
            Rect box0 = RectInCard(card.NodeLabel(0), rt);
            log.IsTrue(SameRect(box0, card.LabelRect(0), 0.5f), "…its label at rest");
            float x0 = card.NodeX(0), size = card.EventSize;

            // The second node, on its word.
            log.IsTrue(card.AppendNode(WideSix[1].date, WideSix[1].evt), "the second node is added on its word");
            log.IsTrue(card.NodeCount == 2 && card.Places == 6, "…two of six");
            log.Near(card.NodeX(0), card.LineEndX, 0.5f, "…the line about to draw on from the first node");
            log.IsTrue(card.DotScale(1) == 0f && card.LabelAlpha(1) == 0f, "…its dot and label not there yet");

            t = 0f; lineAt = popAt = labelAt = float.NaN; lastLen = card.LineLength; grows = true;
            float settledAt = float.NaN, lastTop = float.MaxValue;
            bool accentHeld = true, stayedPut = true, slidesIn = true;
            for (int f = 0; f < 30; f++)
            {
                card.Advance(dt); t += dt;
                float len = card.LineLength;
                if (len < lastLen - 0.01f) grows = false;
                lastLen = len;
                if (float.IsNaN(lineAt) && card.LineEndX >= card.NodeX(1) - 0.5f) lineAt = t;
                if (float.IsNaN(popAt) && card.DotScale(1) > 0f) popAt = t;
                if (float.IsNaN(labelAt) && card.LabelAlpha(1) >= 1f - 1e-4f) labelAt = t;
                if (t < TimelineTypography.PopStart - 1e-3f && card.Dot(0).color != accent) accentHeld = false;
                if (float.IsNaN(settledAt) && Mathf.Abs(card.NodeAlpha(0) - TimelineTypography.OlderAlpha) < 1e-3f) settledAt = t;
                if (!SameRect(box0, RectInCard(card.NodeLabel(0), rt), 0.01f)) stayedPut = false;
                float top = RectInCard(card.NodeLabel(1), rt).yMin;
                if (top > lastTop + 0.01f) slidesIn = false;
                lastTop = top;
            }
            log.IsTrue(grows, "second node: the line only ever grows");
            log.IsTrue(lineAt >= 0.3f - dt - 1e-3f && lineAt <= 0.3f + dt + 1e-3f, $"…reaching the second node in 0.3 s ({lineAt:F3})");
            log.IsTrue(popAt > 0.3f - 1e-3f && popAt <= 0.3f + dt + 1e-3f, $"its dot pops as the line arrives ({popAt:F3} s)");
            log.IsTrue(labelAt <= 0.3f + TimelineTypography.TextSlideSeconds + dt + 1e-3f, $"its label is in 0.3 s later ({labelAt:F3})");
            log.IsTrue(slidesIn, "…sliding in toward the line (a below-line label only ever rises)");
            log.IsTrue(SameRect(RectInCard(card.NodeLabel(1), rt), card.LabelRect(1), 0.5f), "…to its rest");
            log.IsTrue(accentHeld, "the first node keeps the accent until the new dot pops");
            log.IsTrue(settledAt <= 0.3f + TimelineTypography.SettleSeconds + dt + 1e-3f, $"…then settles to 70 % ({settledAt:F3} s)");
            log.IsTrue(card.Dot(1).color == accent && card.DateText(1).color == accent, "the newest node holds the accent (dot and date)");
            log.IsTrue(card.Dot(0).color == TimelineWideCard.Slate && card.DateText(0).color == new Color(1f, 1f, 1f, TimelineWideTypography.DateAlpha),
                "…the first is neutral now: slate dot, white date");
            log.IsTrue(stayedPut && Mathf.Abs(card.NodeX(0) - x0) < 0.001f && Mathf.Abs(card.EventSize - size) < 0.001f,
                "nothing re-flowed when the node arrived: the first node, its label and the type size are where they were");
            CheckWide(log, card, "TimelineWide (2 of 6 nodes, stepped)");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // With the read-ahead every node has its place from the start; without it
    // (or with one that came up short) the card re-lays itself out for the
    // larger run instead of throwing, and says so.
    static void TimelineWide_ReadAheadAndRefit(Log log)
    {
        var three = new[] { WideSix[0], WideSix[1], WideSix[2] };

        var card = BuildWide(three, 1, out GameObject root);
        try
        {
            var rt = (RectTransform)card.transform;
            log.AreEqual(3, card.Places, "TimelineWide read-ahead: three places from the start");
            log.AreEqual(1, card.NodeCount, "…one node shown");
            for (int i = 0; i < 3; i++)
                log.Near(TimelineWideTypography.NodeX(i, 3, WideW), card.NodeX(i), 0.05f, $"…node {i + 1} already has its place");
            float x0 = card.NodeX(0), size = card.EventSize;
            Rect box0 = RectInCard(card.NodeLabel(0), rt);
            card.AppendNode(three[1].date, three[1].evt, instant: true);
            card.AppendNode(three[2].date, three[2].evt, instant: true);
            SettleWide(card);
            log.IsTrue(Mathf.Abs(card.NodeX(0) - x0) < 0.001f && Mathf.Abs(card.EventSize - size) < 0.001f &&
                       SameRect(box0, RectInCard(card.NodeLabel(0), rt), 0.01f),
                "TimelineWide read-ahead: adding the announced nodes moves and resizes nothing");
            CheckWide(log, card, "TimelineWide (3 nodes, read ahead)");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        var late = BuildWide(three, 1, out GameObject lateRoot, readAhead: false);
        var infos = new List<string>();
        void OnLog(string msg, string stack, LogType type) { if (type == LogType.Log) infos.Add(msg); }
        Application.logMessageReceived += OnLog;
        try
        {
            log.AreEqual(1, late.Places, "TimelineWide, no read-ahead: one place");
            log.Near(WideW * 0.5f, late.NodeX(0), 0.05f, "…the node alone mid-line");
            log.IsTrue(late.AppendNode(three[1].date, three[1].evt, instant: true), "…a node the read-ahead didn't see is still added");
            log.AreEqual(2, late.Places, "…the line re-cut into two places");
            log.IsTrue(infos.Exists(m => m.Contains("RE-FITTED for 2 nodes")), "…logged as RE-FITTED");
            log.IsTrue(late.AppendNode(three[2].date, three[2].evt, instant: true), "…and a third");
            SettleWide(late);
            log.AreEqual(3, late.Places, "…three places");
            for (int i = 0; i < 3; i++)
                log.Near(TimelineWideTypography.NodeX(i, 3, WideW), late.NodeX(i), 0.05f, $"…node {i + 1} where a three-node run puts it");
            CheckWide(log, late, "TimelineWide (3 nodes, re-fitted)");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            UnityEngine.Object.DestroyImmediate(lateRoot);
        }

        // A read-ahead that came up one short.
        var shortBy = BuildWide(new[] { WideSix[0], WideSix[1] }, 2, out GameObject shortRoot);
        try
        {
            log.AreEqual(2, shortBy.Places, "TimelineWide, short read-ahead: two places announced");
            log.IsTrue(shortBy.AppendNode(WideSix[2].date, WideSix[2].evt, instant: true), "…the third node is added all the same");
            SettleWide(shortBy);
            log.AreEqual(3, shortBy.Places, "…and the run re-laid out for three");
            CheckWide(log, shortBy, "TimelineWide (short read-ahead, re-fitted)");
        }
        finally { UnityEngine.Object.DestroyImmediate(shortRoot); }
    }

    // {Timeline:End} and a transition's fast hide: the card is closing at
    // once (a node arriving now is not this timeline's) and the dim goes with it.
    static void TimelineWide_HideClosesTheTimeline(Log log)
    {
        foreach (bool fast in new[] { false, true })
        {
            var card = BuildWide(new[] { WideSix[0], WideSix[1] }, 2, out GameObject root);
            try
            {
                string which = fast ? "fast hide" : "hide";
                log.IsTrue(!card.IsClosing && card.Dim.IsOpen, $"TimelineWide {which}: open before it");
                card.Hide(fast);
                log.IsTrue(card.IsClosing, $"TimelineWide {which}: IsClosing at once");
                log.IsTrue(card.Dim.IsClosed, $"TimelineWide {which}: the dim starts closing");
                for (int f = 0; f < 9; f++) card.Advance(1f / 30f);
                log.Near(0f, card.DimAlpha, 1e-4f, $"TimelineWide {which}: the dim is gone 0.25 s later");
                log.AreEqual(2, card.NodeCount, $"TimelineWide {which}: the run is as it was");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}

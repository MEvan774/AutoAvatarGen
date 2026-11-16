using UnityEngine;

/// <summary>
/// {Timeline:"date","event",Wide} — the horizontal, full-screen timeline: one
/// line across the frame, its nodes evenly spaced in SCRIPT ORDER along it,
/// labels alternating above and below. Sizes, where every node, label and the
/// closing span bracket sit, and the timing that <see cref="TimelineTypography"/>
/// doesn't already have. Pure maths only — <see cref="TimelineWideCard"/> does
/// the TMP measuring and drawing — so the self-tests run it with no engine.
///
/// Nothing here ever reads a date: a node's place comes from its index and the
/// number of nodes in the run, and from nothing else.
///
/// Sizes are px of a 1080-px-tall frame, scaled by the frame's real height
/// (<see cref="ScaleFor"/>). Positions and rects are in frame space: x from
/// the left edge, y DOWN from the top edge.
/// </summary>
public static class TimelineWideTypography
{
    public const int MaxNodes = 6;
    public const float ReferenceFrameHeight = 1080f;

    // ---- Frame ------------------------------------------------------------------------
    /// <summary>The line's share of the frame width, centred.</summary>
    public const float LineWidthFraction = 0.80f;
    /// <summary>The line's height in the frame (0 = top, 1 = bottom): mid height.</summary>
    public const float LineHeightFraction = 0.50f;
    /// <summary>Title-safe margin on every side; no label is laid past it.</summary>
    public const float SafeMarginFraction = 0.05f;

    // ---- Type -------------------------------------------------------------------------
    /// <summary>Date font size. Bold small caps, which TMP draws at 0.8× for the
    /// lower-case letters: 40 px keeps those at 32 px, the hard floor.</summary>
    public const float DatePx = 40f;
    public const float DateFloorPx = 40f;
    public const float SmallCapsScale = 0.8f;
    public const float DateTrackingEm = 4f;   // TMP characterSpacing (1/100 em)
    /// <summary>A date that is no longer the newest: white at this alpha.</summary>
    public const float DateAlpha = 0.85f;

    public const float EventMaxPx = 56f;      // bold, auto-fit down to the floor…
    public const float EventMinPx = 40f;      // …under it only with a warning (never under 32)
    public const int EventMaxLines = 2;
    public const float DateToEventGapPx = 2f;

    /// <summary>The numeral inside an enlarged dot: heavy, white.</summary>
    public const float NumeralPx = 40f;
    public const float NumeralFloorPx = 40f;

    // ---- Geometry ---------------------------------------------------------------------
    public const float DotPx = 30f;
    /// <summary>A numbered node's dot, × the normal dot (72 px): two digits at <see cref="NumeralPx"/> fit.</summary>
    public const float NumeralDotScale = 2.4f;
    /// <summary>The numeral's box (its width × its cap height) stays inside this share of the dot's diameter, corner to corner.</summary>
    public const float NumeralFill = 0.86f;
    /// <summary>A wide pair ("WW") grows the numbered dots, up to this.</summary>
    public const float NumeralDotMaxPx = 104f;

    public const float LineThicknessPx = 6f;
    /// <summary>The whole line is there from the start as a faint track at this
    /// alpha (0 = no track); the solid line draws along it, node by node.</summary>
    public const float TrackAlpha = 0.35f;
    public const float TrackFadeSeconds = 0.2f;

    /// <summary>From the edge of the run's largest dot to the labels.</summary>
    public const float DotToLabelGapPx = 20f;
    /// <summary>Labels on the same side of the line (two slots apart) keep at least this between their boxes.</summary>
    public const float LabelGapPx = 40f;
    /// <summary>No label is wider than this share of the frame — what one or two nodes get.</summary>
    public const float MaxLabelWidthFraction = 0.40f;
    /// <summary>A label slides in toward the line from this far away as it fades in.</summary>
    public const float LabelSlidePx = 28f;

    // ---- Span ({Timeline:Span,"text"}) --------------------------------------------------
    public const float SpanPx = 44f;          // bold, white, on the accent pill
    public const float SpanFloorPx = 44f;     // under it only with a warning (never under 32)
    /// <summary>The pill's height for one line of text — the room held back under the below-line labels.</summary>
    public const float SpanPillHeightPx = 80f;
    public const float SpanPillPadXPx = 30f;
    public const float SpanPillPadYPx = 12f;
    /// <summary>From the lowest below-line label to the top of the span's room.</summary>
    public const float SpanGapPx = 30f;
    public const float SpanRulePx = 4f;
    /// <summary>The two end ticks rise this far above the rule.</summary>
    public const float SpanTickPx = 18f;
    /// <summary>The pill (text and padding) is at most this share of the frame wide.</summary>
    public const float SpanPillMaxWidthFraction = 0.80f;

    public const float SpanGrowSeconds = 0.3f;
    public const float SpanPillPopSeconds = 0.3f;
    public const float SpanPillOvershoot = 1.7f;
    public const float SpanFadeSeconds = 0.1f;

    // ---- Exit -------------------------------------------------------------------------
    /// <summary>Line, nodes and span fade out over this long; the dim closes on its own envelope.</summary>
    public const float ContentFadeOutSeconds = 0.2f;

    public static float ScaleFor(float frameHeight) => frameHeight / ReferenceFrameHeight;

    // =========================================================================
    // The line and the nodes' places
    // =========================================================================

    public static float LineWidth(float frameW) => frameW * LineWidthFraction;
    public static float LineLeft(float frameW) => frameW * (1f - LineWidthFraction) * 0.5f;
    public static float LineRight(float frameW) => LineLeft(frameW) + LineWidth(frameW);
    public static float LineY(float frameH) => frameH * LineHeightFraction;

    public static float SafeLeft(float frameW) => frameW * SafeMarginFraction;
    public static float SafeRight(float frameW) => frameW * (1f - SafeMarginFraction);
    public static float SafeTop(float frameH) => frameH * SafeMarginFraction;
    public static float SafeBottom(float frameH) => frameH * (1f - SafeMarginFraction);

    /// <summary>The number of places a run of <paramref name="count"/> nodes is laid out for (1–6).</summary>
    public static int Places(int count) => Mathf.Clamp(count, 1, MaxNodes);

    /// <summary>The line is cut into one slot per node; each node sits in the middle of its slot.</summary>
    public static float SlotWidth(int count, float frameW) => LineWidth(frameW) / Places(count);

    /// <summary>
    /// Node <paramref name="i"/>'s x in a run of <paramref name="count"/>: the
    /// middle of its slot, so the nodes are evenly spaced, in order, and the
    /// first and last sit half a slot inside the line's ends.
    /// </summary>
    public static float NodeX(int i, int count, float frameW)
        => LineLeft(frameW) + (i + 0.5f) * SlotWidth(count, frameW);

    /// <summary>The 1st, 3rd and 5th node carry their label above the line; the 2nd, 4th and 6th below.</summary>
    public static bool IsAbove(int i) => (i & 1) == 0;

    // =========================================================================
    // Dated or numbered
    // =========================================================================

    /// <summary>
    /// A first field of one or two characters ("1", "2", "10") is drawn as a
    /// numeral inside an enlarged dot; anything longer — or nothing — is not.
    /// The field is never parsed: only its length decides.
    /// </summary>
    public static bool IsNumeral(string first)
    {
        if (first == null) return false;
        int n = first.Trim().Length;
        return n >= 1 && n <= 2;
    }

    /// <summary>The first field is drawn as the small-caps date over the event (longer than a numeral, and not empty).</summary>
    public static bool HasDateLabel(string first)
        => !string.IsNullOrWhiteSpace(first) && !IsNumeral(first);

    /// <summary>A node's dot at rest: the normal dot, or the enlarged one a numeral sits in.</summary>
    public static float DotDiameter(bool numeral, float k) => (numeral ? DotPx * NumeralDotScale : DotPx) * k;

    /// <summary>
    /// The diameter every numbered dot of a run gets: 2.4 × the normal dot,
    /// grown (to <see cref="NumeralDotMaxPx"/> at the most) when the run's
    /// widest numeral wouldn't otherwise fit. Widths and heights in frame px.
    /// </summary>
    public static float NumeralDotDiameter(float widestNumeralWidth, float capHeight, float k)
    {
        float needed = Mathf.Sqrt(widestNumeralWidth * widestNumeralWidth + capHeight * capHeight) / NumeralFill;
        return Mathf.Clamp(needed, DotDiameter(true, k), NumeralDotMaxPx * k);
    }

    /// <summary>Whether a numeral's box (width × cap height) is inside its dot.</summary>
    public static bool NumeralFits(float numeralWidth, float capHeight, float diameter)
        => Mathf.Sqrt(numeralWidth * numeralWidth + capHeight * capHeight) <= diameter * NumeralFill + 0.01f;

    // =========================================================================
    // Labels
    // =========================================================================

    /// <summary>
    /// The widest node <paramref name="i"/>'s label box may be. Labels on the
    /// same side of the line are two slots apart, so two slots less a gap; no
    /// wider than 40 % of the frame (which is what one or two nodes get); and
    /// — the box is centred on the node — no wider than twice the node's
    /// distance to the nearer title-safe edge, so an end label is never cut.
    /// </summary>
    public static float LabelMaxWidth(int i, int count, float frameW, float frameH)
    {
        float w = 2f * SlotWidth(count, frameW) - LabelGapPx * ScaleFor(frameH);
        w = Mathf.Min(w, frameW * MaxLabelWidthFraction);
        float x = NodeX(i, count, frameW);
        float toEdge = Mathf.Min(x - SafeLeft(frameW), SafeRight(frameW) - x);
        return Mathf.Max(0f, Mathf.Min(w, 2f * toEdge));
    }

    /// <summary>How far from the line's centre the labels start: clear of the run's largest dot.</summary>
    public static float LabelOffset(float largestDotDiameter, float k)
        => largestDotDiameter * 0.5f + DotToLabelGapPx * k;

    /// <summary>The height an above-line label has: from the title-safe top down to the labels' edge.</summary>
    public static float AboveRoom(float frameH, float labelOffset)
        => LineY(frameH) - labelOffset - SafeTop(frameH);

    /// <summary>
    /// The height a below-line label has. The span's room (its gap and one
    /// pill) is always held back under it — whether or not a span was
    /// announced — so a span can arrive at any time and move nothing.
    /// </summary>
    public static float BelowRoom(float frameH, float labelOffset)
    {
        float k = ScaleFor(frameH);
        return SafeBottom(frameH) - (LineY(frameH) + labelOffset) - (SpanGapPx + SpanPillHeightPx) * k;
    }

    /// <summary>
    /// Node <paramref name="i"/>'s label box: centred on the node's x; above
    /// the line its BOTTOM is <paramref name="labelOffset"/> from the line,
    /// below the line its TOP is — so a one-line and a two-line label both
    /// start at the same distance from the line.
    /// </summary>
    public static Rect LabelRect(int i, float nodeX, float lineY, float width, float height, float labelOffset)
    {
        float top = IsAbove(i) ? lineY - labelOffset - height : lineY + labelOffset;
        return new Rect(nodeX - width * 0.5f, top, width, height);
    }

    // =========================================================================
    // Span
    // =========================================================================

    /// <summary>The top of the span's room: a gap under the lowest below-line label
    /// (<paramref name="belowLabelsBottom"/>; the labels' edge when there is none).</summary>
    public static float SpanTop(float belowLabelsBottom, float frameH)
        => belowLabelsBottom + SpanGapPx * ScaleFor(frameH);

    /// <summary>The rule's centre line: through the middle of the pill, whose top is the top of the span's room.</summary>
    public static float SpanRuleY(float spanTop, float pillHeight) => spanTop + pillHeight * 0.5f;

    /// <summary>
    /// The bracket: a rule from the first node's x to the last SHOWN node's
    /// (<paramref name="lastShown"/>, an index), with a tick rising from each
    /// end. The rect runs from the ticks' tops to the rule's lower edge.
    /// </summary>
    public static Rect SpanBracket(int lastShown, int count, float frameW, float frameH, float ruleY)
    {
        float k = ScaleFor(frameH);
        int last = Mathf.Clamp(lastShown, 0, Places(count) - 1);
        float half = SpanRulePx * k * 0.5f;
        return Rect.MinMaxRect(NodeX(0, count, frameW), ruleY - half - SpanTickPx * k,
                               NodeX(last, count, frameW), ruleY + half);
    }

    public static float SpanPillMaxWidth(float frameW) => frameW * SpanPillMaxWidthFraction;

    /// <summary>The pill sits on the bracket's centre, pushed in only as far as it takes to stay title-safe.</summary>
    public static float SpanPillCentreX(float bracketCentreX, float pillWidth, float frameW)
    {
        float lo = SafeLeft(frameW) + pillWidth * 0.5f, hi = SafeRight(frameW) - pillWidth * 0.5f;
        return lo > hi ? frameW * 0.5f : Mathf.Clamp(bracketCentreX, lo, hi);
    }

    // =========================================================================
    // Timing (a node's own entrance is TimelineTypography's: line 0.3 s → dot pops → label in)
    // =========================================================================

    /// <summary>A node's entrance is over this long after its tag.</summary>
    public static float EntranceSeconds => TimelineTypography.EntranceSeconds;

    /// <summary>The span's entrance is over this long after its tag.</summary>
    public static float SpanSeconds => Mathf.Max(SpanGrowSeconds, SpanPillPopSeconds);

    /// <summary>Where the solid line starts drawing toward node <paramref name="i"/>: the
    /// previous node, or the line's left end for the first.</summary>
    public static float SegmentStartX(int i, int count, float frameW)
        => i <= 0 ? LineLeft(frameW) : NodeX(i - 1, count, frameW);

    /// <summary>The solid line's right end <paramref name="age"/> s after node <paramref name="i"/>'s tag.</summary>
    public static float LineEndAt(int i, int count, float frameW, float age)
        => Mathf.Lerp(SegmentStartX(i, count, frameW), NodeX(i, count, frameW), TimelineTypography.LineDrawAt(age));

    /// <summary>The faint full-length track's alpha <paramref name="sinceShow"/> s after the card opens.</summary>
    public static float TrackAlphaAt(float sinceShow) => TrackAlpha * Mathf.Clamp01(sinceShow / TrackFadeSeconds);

    /// <summary>How far the bracket has grown from its centre outward (OutQuad 0→1).</summary>
    public static float SpanGrowAt(float age)
    {
        float x = Mathf.Clamp01(age / SpanGrowSeconds);
        return 1f - (1f - x) * (1f - x);
    }

    /// <summary>The pill's scale: 0 → 1 with a small overshoot (OutBack).</summary>
    public static float SpanPillScaleAt(float age)
    {
        if (age <= 0f) return 0f;
        if (age >= SpanPillPopSeconds) return 1f;
        float t = age / SpanPillPopSeconds - 1f, s = SpanPillOvershoot;
        return 1f + (s + 1f) * t * t * t + s * t * t;
    }

    /// <summary>Bracket and pill fade in over the first tenth of a second (0 on the tag's own frame).</summary>
    public static float SpanAlphaAt(float age) => Mathf.Clamp01(age / SpanFadeSeconds);
}

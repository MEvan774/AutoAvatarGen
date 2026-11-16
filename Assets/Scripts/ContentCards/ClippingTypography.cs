using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// {Clipping:…} — a pile of press clippings, full screen. Sizes, colours, the
/// timing of every move (drop, marker sweep, push-in, lift) and the pile's
/// geometry. Pure maths only — <see cref="ClippingCard"/> does the TMP
/// measuring and drawing — so the self-tests run it with no engine.
///
/// Sizes are px of a 1080-px-tall frame, scaled by the frame's real height
/// (<see cref="ScaleFor"/>). Rects are in frame space or in sheet space: x
/// from the left edge, y DOWN from the top edge.
/// </summary>
public static class ClippingTypography
{
    public const float ReferenceFrameHeight = 1080f;

    // ---- The run ------------------------------------------------------------------
    /// <summary>A run holds at most this many sheets; a 6th {Clipping} is refused.</summary>
    public const int MaxSheets = 5;
    /// <summary>The pile shows the newest three; an older one is hidden once the sheet that buries it has landed.</summary>
    public const int MaxVisibleSheets = 3;
    /// <summary>{Mark}s a sheet takes on top of the tag's own highlight.</summary>
    public const int MaxMarksPerSheet = 3;
    /// <summary>A sheet covered sooner than this after it landed was too short to
    /// read. The card shows it all the same; the controller warns, from this and
    /// <see cref="ClippingCard.TopSheetAge"/> — which counts from the START of
    /// the drop, <see cref="DropSeconds"/> before the landing
    /// (<see cref="ClippingCard.TopSheetLandedSeconds"/> counts from the landing).
    /// For scale: a one-line highlight is finished 0.55 s after the landing, a
    /// two-line one 0.85 s after it.</summary>
    public const float MinReadSeconds = 0.8f;

    // ---- Sheet --------------------------------------------------------------------
    public const float SheetWidthFraction = 0.76f;
    /// <summary>A sheet is as tall as its content, up to this share of the frame.</summary>
    public const float SheetMaxHeightFraction = 0.70f;
    public const float SheetPadXPx = 84f;
    /// <summary>Less than the bottom padding: a serif line carries ~0.35 em of
    /// empty leading above its capitals, so the two margins read alike.</summary>
    public const float SheetPadTopPx = 52f;
    public const float SheetPadBottomPx = 68f;

    // ---- Type ---------------------------------------------------------------------
    /// <summary>The headline, Noto Serif Bold: the largest size from 110 px down
    /// to 64 at which it takes at most three lines (under 64 the card warns,
    /// never under 32).</summary>
    public const float HeadlineMaxPx = 110f;
    public const float HeadlineMinPx = 64f;
    public const int HeadlineMaxLines = 3;
    /// <summary>A headline is set tighter than a passage (TMP lineSpacing, 1/100 em):
    /// 1.26 em between baselines, which still leaves 0.24 em between two lines' marker strokes.</summary>
    public const float HeadlineLineSpacing = -10f;

    /// <summary>The source, Noto Serif Bold small caps like the Document header:
    /// TMP draws the lower-case letters at 0.8×, so 40 px keeps them at 32 px.</summary>
    public const float SourcePx = 40f;
    public const float SourceFloorPx = 40f;
    public const float SmallCapsScale = 0.8f;
    public const float SourceTrackingEm = DocumentTypography.HeaderTrackingEm;
    public const float HeadlineToRuleGapPx = 30f;
    public const float RulePx = DocumentTypography.HeaderRulePx;
    public const float RuleToSourceGapPx = 22f;

    // ---- Marker stroke (the Document card's, in em of the headline size) ----------
    /// <summary>From this far above the baseline…</summary>
    public const float MarkerRiseEm = 0.80f;
    /// <summary>…to this far below it…</summary>
    public const float MarkerDropEm = 0.22f;
    /// <summary>…and this far past the phrase at each end.</summary>
    public const float MarkerOverhangEm = 0.12f;

    // ---- Colours ------------------------------------------------------------------
    public static Color Paper => DocumentTypography.Paper;
    public static Color Ink => DocumentTypography.Ink;
    public static Color SourceInk => DocumentTypography.HeaderInk;
    public static Color Rule => DocumentTypography.HeaderRule;
    public static Color Marker => DocumentTypography.Marker;

    /// <summary>The full-frame backdrop behind the pile: the side cards' dark
    /// panel colour, opaque — it covers the presenter like a ,bigCenter headline.</summary>
    public static Color Backdrop
    {
        get { Color c = ContentCardUIBuilder.BackgroundColor; c.a = 1f; return c; }
    }

    /// <summary>The shade laid over a covered sheet (its alpha: <see cref="DimForDepth"/>).</summary>
    public static Color Shade => DocumentTypography.Ink;
    /// <summary>Each sheet lying on top of one darkens it by this much.</summary>
    public const float DimPerLayer = 0.25f;

    // ---- Timing (seconds on the card's clock) ---------------------------------------
    public const float BackdropFadeInSeconds = 0.20f;
    /// <summary>A sheet drops in from above the frame over this long, decelerating.</summary>
    public const float DropSeconds = 0.25f;
    /// <summary>The marker starts this long after its sheet has landed…</summary>
    public const float HighlightDelaySeconds = 0.25f;
    /// <summary>…and takes this long per line of the phrase, one line after another.</summary>
    public const float HighlightLineSeconds = 0.30f;
    /// <summary>An earlier highlight fades to its pale tint over this long once a {Mark} moves on…</summary>
    public const float PaleSeconds = 0.20f;
    /// <summary>…to this share of the marker's alpha, and stays.</summary>
    public const float PaleAlphaFraction = 0.40f;
    /// <summary>The top sheet's slow push-in: 1.2 % per second from the moment it lands…</summary>
    public const float PushInPerSecond = 0.012f;
    /// <summary>…never past this.</summary>
    public const float PushInMaxScale = 1.08f;
    /// <summary>{Clipping:End}: the pile slides up out of the frame and fades, the backdrop with it.</summary>
    public const float LiftSeconds = 0.25f;
    /// <summary>The close is over (pile gone, backdrop gone) this long after it starts.</summary>
    public const float HideSeconds = LiftSeconds;

    /// <summary>Clear space kept between a waiting sheet and the frame's top edge (its shadow reaches 17 px below it).</summary>
    public const float DropMarginPx = 40f;

    /// <summary>A sweep age that is certainly over (a sheet that lands "instant" has its highlight already drawn).</summary>
    public const float Settled = 1e6f;

    public static float ScaleFor(float frameHeight) => frameHeight / ReferenceFrameHeight;

    // =========================================================================
    // Timing
    // =========================================================================

    /// <summary>A sheet's drop 0→1, <paramref name="age"/> s after it starts landing
    /// (0 = waiting above the frame, 1 = at rest). OutCubic: fast in, soft landing.</summary>
    public static float DropAt(float age)
    {
        float r = 1f - Mathf.Clamp01(age / DropSeconds);
        return 1f - r * r * r;
    }

    /// <summary>Whether a sheet <paramref name="age"/> s into its drop is at rest
    /// (a hair of slack: an "instant" sheet's age is a float subtraction away from 0.25).</summary>
    public static bool HasLanded(float age) => age >= DropSeconds - 1e-4f;

    /// <summary>When the marker starts on line <paramref name="line"/> of the tag's phrase, after its sheet has landed.</summary>
    public static float LineSweepStart(int line) => HighlightDelaySeconds + line * HighlightLineSeconds;

    /// <summary>Line <paramref name="line"/>'s sweep 0→1 for the tag's own highlight,
    /// <paramref name="sinceLanding"/> s after the sheet landed.</summary>
    public static float LineSweepAt(float sinceLanding, int line) => SweepAt(sinceLanding - HighlightDelaySeconds, line);

    /// <summary>Line <paramref name="line"/>'s sweep 0→1, <paramref name="sinceStart"/> s
    /// after the stroke was started (a {Mark} starts at once). Ease in-out: a hand
    /// stroke speeds up, then slows into the end of the phrase.</summary>
    public static float SweepAt(float sinceStart, int line)
    {
        return Smooth((sinceStart - line * HighlightLineSeconds) / HighlightLineSeconds);
    }

    /// <summary>How long a phrase on <paramref name="lines"/> lines takes to sweep.</summary>
    public static float SweepSeconds(int lines) => Mathf.Max(0, lines) * HighlightLineSeconds;

    /// <summary>The top sheet's scale <paramref name="sinceLanding"/> s after it landed.</summary>
    public static float PushInScaleAt(float sinceLanding)
        => Mathf.Min(PushInMaxScale, 1f + PushInPerSecond * Mathf.Max(0f, sinceLanding));

    /// <summary>A covered sheet's scale: from the push-in it had reached
    /// (<paramref name="heldScale"/>) back to 1.0 while the sheet that covers it
    /// lands (<paramref name="coverProgress"/> 0→1 of that drop's time), so the
    /// pile stays one size without a jump.</summary>
    public static float CoveredScaleAt(float heldScale, float coverProgress)
        => Mathf.Lerp(heldScale, 1f, Smooth(coverProgress));

    /// <summary>An earlier highlight's strength <paramref name="sinceMovedOn"/> s after a
    /// {Mark} moved the marker on: 1 → <see cref="PaleAlphaFraction"/> (negative = not moved on).</summary>
    public static float TintAt(float sinceMovedOn)
    {
        if (sinceMovedOn <= 0f) return 1f;
        return Mathf.Lerp(1f, PaleAlphaFraction, Smooth(sinceMovedOn / PaleSeconds));
    }

    /// <summary>A stroke's colour: the Document marker, fading in over the first
    /// eighth of its sweep, times its <paramref name="tint"/> (<see cref="TintAt"/>).</summary>
    public static Color StrokeColor(float sweep, float tint)
    {
        Color c = Marker;
        c.a *= Mathf.Clamp01(sweep * 8f) * Mathf.Clamp01(tint);
        return c;
    }

    /// <summary>A stroke's width while it sweeps: it grows from a dab (0.6 of its height) to the full line.</summary>
    public static float StrokeWidth(float fullWidth, float height, float sweep)
        => Mathf.Lerp(Mathf.Min(fullWidth, height * 0.6f), fullWidth, Mathf.Clamp01(sweep));

    /// <summary>The backdrop's alpha <paramref name="sinceShown"/> s after Show (0→1 over 0.2 s).</summary>
    public static float BackdropAt(float sinceShown) => Smooth(sinceShown / BackdropFadeInSeconds);

    /// <summary>How far the pile has lifted 0→1, <paramref name="sinceHide"/> s into the close (accelerating).</summary>
    public static float LiftAt(float sinceHide)
    {
        float x = Mathf.Clamp01(sinceHide / LiftSeconds);
        return x * x;
    }

    /// <summary>The pile's (and the backdrop's) remaining alpha <paramref name="sinceHide"/> s into the close: 1 → 0.</summary>
    public static float LiftFadeAt(float sinceHide) => 1f - Smooth(sinceHide / LiftSeconds);

    // =========================================================================
    // Pile geometry
    // =========================================================================

    static readonly float[] Tilts = { -1.6f, 1.4f, -1.2f, 1.7f, -1.5f };

    /// <summary>Sheet <paramref name="sheetIndex"/>'s tilt in uGUI degrees (positive =
    /// counter-clockwise on screen): each sheet a degree or two the other way from
    /// the one under it. Fixed, never random — every take draws the same pile.</summary>
    public static float Tilt(int sheetIndex)
    {
        int n = Tilts.Length;
        return Tilts[((sheetIndex % n) + n) % n];
    }

    public static float SheetWidth(float frameW) => frameW * SheetWidthFraction;
    public static float SheetMaxHeight(float frameH) => frameH * SheetMaxHeightFraction;

    /// <summary>A sheet's height for its content (paddings included): no taller than <see cref="SheetMaxHeightFraction"/> of the frame.</summary>
    public static float SheetHeight(float contentHeight, float frameH) => Mathf.Min(contentHeight, SheetMaxHeight(frameH));

    /// <summary>A sheet at rest, before its tilt: centred in the frame.</summary>
    public static Rect SheetRect(float frameW, float frameH, float sheetH)
    {
        float w = SheetWidth(frameW);
        return new Rect((frameW - w) * 0.5f, (frameH - sheetH) * 0.5f, w, sheetH);
    }

    /// <summary>The height of a <paramref name="w"/>×<paramref name="h"/> sheet's bounds once tilted.</summary>
    public static float RotatedHeight(float w, float h, float degrees)
    {
        float a = Mathf.Abs(degrees) * Mathf.Deg2Rad;
        return w * Mathf.Sin(a) + h * Mathf.Cos(a);
    }

    /// <summary>How far above its rest a sheet waits before it drops: wholly above
    /// the frame, tilt and shadow included.</summary>
    public static float DropOffset(float sheetW, float sheetH, float tiltDegrees, float frameH)
        => frameH * 0.5f + RotatedHeight(sheetW, sheetH, tiltDegrees) * 0.5f + DropMarginPx * ScaleFor(frameH);

    /// <summary>How far the pile lifts on its way out: a whole frame — past the top
    /// edge even for the tallest sheet at its largest push-in.</summary>
    public static float LiftDistance(float frameH) => frameH;

    /// <summary>How many sheets the pile shows once <paramref name="sheets"/> have landed.</summary>
    public static int VisibleCount(int sheets) => Mathf.Clamp(sheets, 0, MaxVisibleSheets);

    /// <summary>The oldest sheet still in the pile of <paramref name="sheets"/>.</summary>
    public static int FirstVisible(int sheets) => Mathf.Max(0, sheets - MaxVisibleSheets);

    /// <summary>Whether sheet <paramref name="index"/> is one of the newest three of <paramref name="sheets"/>.</summary>
    public static bool IsVisible(int index, int sheets) => index >= FirstVisible(sheets) && index < sheets;

    /// <summary>How many sheets lie on sheet <paramref name="index"/> (0 = the top one).</summary>
    public static int Depth(int index, int sheets) => sheets - 1 - index;

    /// <summary>The shade's alpha on a sheet with <paramref name="depth"/> sheets on it
    /// (fractional while one is landing): 25 % per layer, so the headline underneath stops competing.</summary>
    public static float DimForDepth(float depth) => Mathf.Clamp(depth, 0f, MaxVisibleSheets) * DimPerLayer;

    /// <summary>
    /// A marker stroke on one line, in sheet space. The headline's box has its
    /// top-left corner at (<paramref name="boxLeft"/>, <paramref name="boxTop"/>);
    /// <paramref name="x0"/>…<paramref name="x1"/> and <paramref name="baseline"/>
    /// are TMP's, in that box's own space (x from its left edge, y UP from its top
    /// edge — so a baseline is negative).
    /// </summary>
    public static Rect MarkerRect(float boxLeft, float boxTop, float x0, float x1, float baseline, float size)
    {
        float ext = MarkerOverhangEm * size;
        return Rect.MinMaxRect(boxLeft + x0 - ext, boxTop - baseline - MarkerRiseEm * size,
                               boxLeft + x1 + ext, boxTop - baseline + MarkerDropEm * size);
    }

    // =========================================================================
    // Headline size, phrases and marks
    // =========================================================================

    /// <summary>
    /// The headline's size in frame px for a frame scale <paramref name="k"/>:
    /// the largest in 64–110 for which <paramref name="fits"/> holds (at most three
    /// lines, inside the sheet); failing that the largest down to the 32 px hard
    /// floor (flagged <c>BelowWarnFloor</c>); failing that 32 px, flagged <c>Overflows</c>.
    /// </summary>
    public static CardTypography.BodyFit FitHeadline(Func<float, bool> fits, float k)
        => CardTypography.FitBody(fits, HeadlineMinPx * k, HeadlineMaxPx * k, HeadlineMinPx * k, CardTypography.HardFloorPx * k);

    /// <summary>Where <paramref name="phrase"/> starts in <paramref name="headline"/> —
    /// exactly as written (ordinal) — or -1. An empty phrase is never found.</summary>
    public static int FindPhrase(string headline, string phrase)
    {
        if (headline == null || string.IsNullOrWhiteSpace(phrase)) return -1;
        return headline.IndexOf(phrase, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a {Mark:"phrase"} on a sheet comes to. <paramref name="highlighted"/>
    /// is every phrase the sheet already carries (the tag's own, if it was found,
    /// and the earlier marks); <paramref name="marksSoFar"/> counts the marks only.
    /// </summary>
    public static MarkResult JudgeMark(string headline, string phrase, IList<string> highlighted, int marksSoFar)
    {
        if (FindPhrase(headline, phrase) < 0) return MarkResult.NotFound;
        if (highlighted != null)
            foreach (string h in highlighted)
                if (string.Equals(h, phrase, StringComparison.Ordinal)) return MarkResult.Repeated;
        if (marksSoFar >= MaxMarksPerSheet) return MarkResult.TooMany;
        return MarkResult.Ok;
    }

    public const int LogHeadlineChars = 60;

    /// <summary>A headline for a log line: one line, at most 60 characters.</summary>
    public static string ShortHeadline(string headline)
    {
        string t = (headline ?? string.Empty).Replace('\n', ' ').Trim();
        return t.Length > LogHeadlineChars ? t.Substring(0, LogHeadlineChars).TrimEnd() + "…" : t;
    }

    /// <summary>The one warning a sheet logs when its phrase isn't in its headline.</summary>
    public static string PhraseWarning(string headline, string phrase)
        => $"[Card] Clipping \"{ShortHeadline(headline)}\": the phrase \"{phrase}\" isn't in the headline " +
           "(it must match exactly) — shown with no highlight.";

    /// <summary>
    /// How far into its sweep a highlight is (the <c>sinceStart</c> of
    /// <see cref="SweepAt"/>). The tag's own highlight starts
    /// <see cref="HighlightDelaySeconds"/> after its sheet lands — or is already
    /// drawn on a sheet that landed "instant"; a {Mark} starts the moment it is
    /// asked for (<paramref name="sinceMarked"/> s ago), but never before the
    /// sheet has landed.
    /// </summary>
    public static float StrokeAge(bool own, bool instantSheet, float sinceLanding, float sinceMarked)
    {
        if (own) return instantSheet ? Settled : sinceLanding - HighlightDelaySeconds;
        return Mathf.Min(sinceMarked, sinceLanding);
    }

    // =========================================================================
    // The pile over time
    // =========================================================================

    /// <summary>
    /// The run's clockwork, with no engine in it: the card's clock, when each
    /// sheet started landing, and from those — for the moment the clock shows —
    /// where every sheet is, how far it has pushed in, how dark it is and
    /// whether it is still drawn; the backdrop; the lift on the way out.
    /// <see cref="ClippingCard"/> poses its objects from this every frame; the
    /// self-tests step it with no engine.
    /// </summary>
    public sealed class Pile
    {
        readonly float[] dropStart = new float[MaxSheets];  // NaN while the sheet waits (before Show)
        readonly float[] heldScale = new float[MaxSheets];  // the push-in a sheet had reached when it was covered
        readonly bool[] instant = new bool[MaxSheets];
        float shownAt = float.NaN;
        float liftAt = float.NaN;
        float backdropAtLift = 1f;
        bool liftReported;

        /// <summary>Sheets added so far (0–5), buried ones included.</summary>
        public int Count { get; private set; }
        /// <summary>The card's clock, in seconds (scaled time, like DOTween).</summary>
        public float Clock { get; private set; }
        public bool IsShown => !float.IsNaN(shownAt);
        public bool IsLifting => !float.IsNaN(liftAt);

        /// <summary>A fresh run on the same clock.</summary>
        public void Reset()
        {
            Count = 0;
            shownAt = float.NaN;
            liftAt = float.NaN;
            backdropAtLift = 1f;
            liftReported = false;
        }

        public void Advance(float dt) => Clock += dt;

        /// <summary>
        /// Adds the next sheet on top. Before <see cref="Show"/> it waits with the
        /// others; after it, it starts landing now — or, with
        /// <paramref name="landInstantly"/>, has just landed. False (and nothing
        /// changes) when the run is full. <paramref name="startsLanding"/>: it
        /// begins its drop at this moment (the paper sound).
        /// </summary>
        public bool Add(bool landInstantly, out bool startsLanding)
        {
            startsLanding = false;
            if (Count >= MaxSheets) return false;

            // The sheet about to be covered keeps the push-in it has reached.
            if (Count > 0) heldScale[Count - 1] = PushInScaleAt(SinceLanding(Count - 1));

            int i = Count++;
            heldScale[i] = 1f;
            instant[i] = landInstantly;
            dropStart[i] = !IsShown ? float.NaN : landInstantly ? Clock - DropSeconds : Clock;
            startsLanding = IsShown && !landInstantly;
            return true;
        }

        /// <summary>The backdrop starts to fade in and every waiting sheet starts
        /// landing — as one pile. True when at least one sheet does.</summary>
        public bool Show()
        {
            if (!IsShown) shownAt = Clock;
            bool any = false;
            for (int i = 0; i < Count; i++)
            {
                if (!float.IsNaN(dropStart[i])) continue;
                dropStart[i] = Clock;
                any = true;
            }
            return any;
        }

        // ---- One sheet, now ------------------------------------------------------------

        /// <summary>Seconds since sheet <paramref name="i"/> started landing; negative while it waits.</summary>
        public float Age(int i) => float.IsNaN(dropStart[i]) ? -1f : Clock - dropStart[i];
        public float SinceLanding(int i) => Age(i) - DropSeconds;
        public bool Landed(int i) => i >= 0 && i < Count && HasLanded(Age(i));
        public bool IsInstant(int i) => instant[i];
        /// <summary>0 = waiting above the frame, 1 = at rest on the pile.</summary>
        public float DropProgress(int i) => DropAt(Age(i));
        /// <summary>Seconds since the top sheet started landing (0 while it waits).</summary>
        public float TopAge => Count == 0 ? 0f : Mathf.Max(0f, Age(Count - 1));

        /// <summary>The sheets lying on sheet <paramref name="i"/>, each counted as far as it has landed.</summary>
        public float Depth(int i)
        {
            float depth = 0f;
            for (int j = i + 1; j < Count; j++) depth += DropAt(Age(j));
            return depth;
        }

        public float Dim(int i) => DimForDepth(Depth(i));

        /// <summary>Whether sheet <paramref name="i"/> is still drawn: until three sheets have LANDED on it.</summary>
        public bool Drawn(int i)
        {
            if (i < 0 || i >= Count) return false;
            int landed = 0;
            for (int j = i + 1; j < Count; j++)
                if (HasLanded(Age(j))) landed++;
            return landed < MaxVisibleSheets;
        }

        public int DrawnCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Count; i++) if (Drawn(i)) n++;
                return n;
            }
        }

        /// <summary>The top sheet pushes in; a covered one eases back to 1.0 while its cover lands.</summary>
        public float Scale(int i)
        {
            if (i == Count - 1) return PushInScaleAt(SinceLanding(i));
            return CoveredScaleAt(heldScale[i], Mathf.Clamp01(Age(i + 1) / DropSeconds));
        }

        // ---- Backdrop and lift ---------------------------------------------------------

        public float BackdropAlpha
        {
            get
            {
                if (IsLifting) return backdropAtLift * LiftFadeAt(Clock - liftAt);
                return IsShown ? BackdropAt(Clock - shownAt) : 0f;
            }
        }

        /// <summary>How far the pile has lifted, 0→1 of <see cref="LiftDistance"/>.</summary>
        public float Lift => IsLifting ? LiftAt(Clock - liftAt) : 0f;
        public float PileAlpha => IsLifting ? LiftFadeAt(Clock - liftAt) : 1f;

        /// <summary>Starts the close. Asked again while lifting (or after) it does
        /// not start over — it only makes sure that call is answered too.</summary>
        public void StartLift()
        {
            liftReported = false;
            if (IsLifting) return;
            backdropAtLift = BackdropAlpha;
            liftAt = Clock;
        }

        /// <summary>A fast hide reports through its own fade: the lift stays silent.</summary>
        public void MuteLiftReport() => liftReported = true;

        /// <summary>True once, when the lift is over (the card then reports OnHideComplete).</summary>
        public bool TakeLiftDone()
        {
            if (!IsLifting || liftReported || Clock - liftAt < HideSeconds) return false;
            liftReported = true;
            return true;
        }

        /// <summary>
        /// Moves every start stamped at exactly <paramref name="from"/> to
        /// <paramref name="to"/>. The card calls it when something began earlier
        /// in the very frame its own Update is about to advance: the starts ride
        /// along with the clock, so that frame — the first they are drawn in —
        /// still shows their start pose.
        /// </summary>
        public void Reanchor(float from, float to)
        {
            Shift(ref shownAt, from, to);
            Shift(ref liftAt, from, to);
            for (int i = 0; i < Count; i++) Shift(ref dropStart[i], from, to);
        }
    }

    /// <summary>See <see cref="Pile.Reanchor"/>: a start that is exactly <paramref name="from"/> becomes <paramref name="to"/> (NaN never matches).</summary>
    public static void Shift(ref float t, float from, float to)
    {
        if (t == from) t = to;
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}

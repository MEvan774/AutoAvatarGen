using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen chapter titles: at each {Timestamp:"Label"} except the first,
/// a small caps label with a row of progress pips ("part 3 of 5") shows for a
/// moment at the top of the frame. The rules live here as pure functions so
/// the self-tests run them with no engine; <see cref="ChapterTitleController"/>
/// draws them in a take.
///
/// Geometry is in FRAME-NORMALISED coordinates: x 0..1 left→right as seen in
/// the video, y 0..1 measured DOWN from the top edge.
/// </summary>
public static class ChapterTitles
{
    // ---- Placement (frame-normalised) ------------------------------------------
    /// <summary>Title-safe margin on every side.</summary>
    public const float SafeMargin = 0.05f;
    /// <summary>The empty band above the cards: titles stay above this line.</summary>
    public const float BandBottom = 0.11f;
    /// <summary>YouTube's progress bar / captions: never this low.</summary>
    public const float BottomKeepOut = 0.85f;
    /// <summary>Widest a label may be, as a share of the frame width.</summary>
    public const float MaxLabelWidth = 0.40f;

    // ---- Look (px of a 1080-px-tall frame) -------------------------------------
    public const float LabelPx = 44f;
    public const float LabelMinPx = 32f;
    public const float LabelLetterSpacing = 6f;
    public const float PipDiameterPx = 12f;
    public const float PipGapPx = 10f;
    public const float LabelToPipsGapPx = 14f;
    public const float PipEmptyAlpha = 0.32f;

    // ---- Timing (seconds) -------------------------------------------------------------
    /// <summary>After a section-opening transition's cover has fully opened.</summary>
    public const float AfterRevealDelay = 0.2f;
    public const float FadeInSeconds = 0.25f;
    public const float HoldSeconds = 2.5f;
    public const float FadeOutSeconds = 0.3f;
    public const float SlidePx = 10f;
    /// <summary>A fullscreen card that appears this soon after the title was
    /// already due counts as "already up", not as an interruption.</summary>
    public const float FeatureCardGraceSeconds = 0.3f;
    /// <summary>How far before its Timestamp a transition may start and still
    /// be the one that opens the section (SegmentSequencer puts it ~0.9 s
    /// ahead, in the baked silence).</summary>
    public const float TransitionPairWindowBefore = 3.0f;
    public const float TransitionPairWindowAfter = 0.25f;
    /// <summary>If the paired transition's reveal never arrives (another
    /// transition was still running), show this long after the Timestamp.</summary>
    public const float RevealFallbackSeconds = 1.0f;

    public const string ShowPrefKey = "AutoAvatarGen.ShowChapterTitles";

    /// <summary>Main-menu switch, default on.</summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(ShowPrefKey, 1) == 1;
        set { PlayerPrefs.SetInt(ShowPrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // =========================================================================
    // What shows
    // =========================================================================

    /// <summary>The first chapter is the cold open — nothing sits on the hook.</summary>
    public static bool ShowsTitle(int chapterIndex) => chapterIndex > 0;

    /// <summary>How many titles a script with this many {Timestamp}s shows.</summary>
    public static int TitleCount(int timestampCount) => Mathf.Max(0, timestampCount - 1);

    /// <summary>One pip per {Timestamp}; filled up to and including the current one.</summary>
    public static bool[] Pips(int timestampCount, int chapterIndex)
    {
        var pips = new bool[Mathf.Max(0, timestampCount)];
        for (int i = 0; i < pips.Length; i++) pips[i] = i <= chapterIndex;
        return pips;
    }

    public static string DisplayLabel(string label) => (label ?? string.Empty).Trim().ToUpperInvariant();

    // =========================================================================
    // When
    // =========================================================================

    /// <summary>
    /// The transition that opens the chapter at <paramref name="timestampTime"/>:
    /// the latest one starting in [T − 3 s, T + 0.25 s]. -1 when the section
    /// doesn't open with one (the title then shows on the Timestamp's word).
    /// </summary>
    public static int OpeningTransition(float timestampTime, IList<float> transitionTimes)
    {
        int best = -1;
        if (transitionTimes == null) return best;
        for (int i = 0; i < transitionTimes.Count; i++)
        {
            float t = transitionTimes[i];
            if (t >= timestampTime - TransitionPairWindowBefore && t <= timestampTime + TransitionPairWindowAfter &&
                (best < 0 || t > transitionTimes[best]))
                best = i;
        }
        return best;
    }

    /// <summary>Visibility 0..1 at <paramref name="age"/> seconds after the title showed.</summary>
    public static float AlphaAt(float age)
    {
        if (age < 0f) return 0f;
        if (age < FadeInSeconds) return Smooth(age / FadeInSeconds);
        float outStart = FadeInSeconds + HoldSeconds;
        if (age < outStart) return 1f;
        return 1f - Smooth((age - outStart) / FadeOutSeconds);
    }

    /// <summary>Px still to slide (down onto its rest) at this age.</summary>
    public static float SlideAt(float age)
    {
        if (age <= 0f) return SlidePx;
        if (age >= FadeInSeconds) return 0f;
        float x = age / FadeInSeconds;
        return SlidePx * (1f - x) * (1f - x); // ease out
    }

    public static float TotalSeconds => FadeInSeconds + HoldSeconds + FadeOutSeconds;

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // =========================================================================
    // Where
    // =========================================================================

    public enum Side { Left, Center, Right }

    /// <summary>The side opposite Mugs; centred when he is at Center.</summary>
    public static Side OppositeOf(CharacterPosition mugs)
    {
        switch (mugs)
        {
            case CharacterPosition.Left:  return Side.Right;
            case CharacterPosition.Right: return Side.Left;
            default:                      return Side.Center;
        }
    }

    /// <summary>Spots to try, best first: the preferred side, then centre, then the other side.</summary>
    public static Side[] SpotOrder(Side preferred)
    {
        switch (preferred)
        {
            case Side.Left:  return new[] { Side.Left, Side.Center, Side.Right };
            case Side.Right: return new[] { Side.Right, Side.Center, Side.Left };
            default:         return new[] { Side.Center, Side.Left, Side.Right };
        }
    }

    /// <summary>
    /// The title block's rect (frame-normalised) for a side, given its size as
    /// a share of the frame. Top at the title-safe margin; left / right flush
    /// with the safe margins; centred otherwise.
    /// </summary>
    public static Rect BlockRect(Side side, float width, float height)
    {
        float x;
        switch (side)
        {
            case Side.Left:  x = SafeMargin; break;
            case Side.Right: x = 1f - SafeMargin - width; break;
            default:         x = 0.5f - width * 0.5f; break;
        }
        return new Rect(x, SafeMargin, width, height);
    }

    /// <summary>Inside title-safe, above the band line, clear of the bottom keep-out.</summary>
    public static bool InAllowedArea(Rect r) =>
        r.xMin >= SafeMargin - 1e-4f && r.xMax <= 1f - SafeMargin + 1e-4f &&
        r.yMin >= SafeMargin - 1e-4f && r.yMax <= BandBottom + 1e-4f && r.yMax <= BottomKeepOut;

    /// <summary>
    /// The first spot in <see cref="SpotOrder"/> whose block is in the allowed
    /// area and overlaps none of <paramref name="obstacles"/>; null when none is free.
    /// </summary>
    public static Side? ChooseSpot(Side preferred, float width, float height, IList<Rect> obstacles)
    {
        foreach (Side s in SpotOrder(preferred))
        {
            Rect r = BlockRect(s, width, height);
            if (!InAllowedArea(r)) continue;
            if (FirstOverlap(r, obstacles) < 0) return s;
        }
        return null;
    }

    public static int FirstOverlap(Rect r, IList<Rect> obstacles)
    {
        if (obstacles == null) return -1;
        for (int i = 0; i < obstacles.Count; i++)
            if (obstacles[i].width > 0f && obstacles[i].height > 0f && r.Overlaps(obstacles[i])) return i;
        return -1;
    }

    // =========================================================================
    // Label fit
    // =========================================================================

    public struct LabelFit
    {
        public float Size;
        public string Text;
        public bool Shrunk;
        public bool Truncated;
    }

    /// <summary>
    /// The label at <paramref name="maxPx"/> when it fits <paramref name="maxWidth"/>;
    /// else shrunk (down to <paramref name="minPx"/>); else at the minimum,
    /// cut back to whole words where possible and ended with "…".
    /// <paramref name="widthAt"/>(text, size) is its rendered single-line width.
    /// </summary>
    public static LabelFit FitLabel(string label, Func<string, float, float> widthAt,
        float maxWidth, float maxPx, float minPx)
    {
        var fit = new LabelFit { Size = maxPx, Text = label ?? string.Empty };
        float w = widthAt(fit.Text, maxPx);
        if (w <= maxWidth) return fit;

        fit.Shrunk = true;
        float s = maxPx * maxWidth / Mathf.Max(1f, w);
        while (s > minPx && widthAt(fit.Text, s) > maxWidth) s -= 0.5f; // width isn't exactly linear
        if (s >= minPx && widthAt(fit.Text, s) <= maxWidth)
        {
            fit.Size = Mathf.Floor(s * 2f) / 2f;
            return fit;
        }

        // Truncate at the minimum: longest whole-word prefix + "…", else character-wise.
        fit.Size = minPx;
        fit.Truncated = true;
        string text = fit.Text;
        string best = "…";
        for (int cut = text.Length - 1; cut > 0; cut--)
        {
            bool wordEnd = text[cut] == ' ';
            if (!wordEnd) continue;
            string candidate = text.Substring(0, cut).TrimEnd() + "…";
            if (widthAt(candidate, minPx) <= maxWidth) { best = candidate; break; }
        }
        if (best == "…")
        {
            for (int cut = text.Length - 1; cut > 0; cut--)
            {
                string candidate = text.Substring(0, cut).TrimEnd() + "…";
                if (widthAt(candidate, minPx) <= maxWidth) { best = candidate; break; }
            }
        }
        fit.Text = best;
        return fit;
    }

    // =========================================================================
    // Interruptions
    // =========================================================================

    public struct InterruptState
    {
        public bool BlackShowing;
        public bool CloseUp;               // {Zoom:ExtremeIn} held, or its cut is frames away
        public bool TransitionStarted;     // a new {Transition} since the title showed
        public bool FeatureCardArrived;    // a fullscreen card that wasn't up at the title's moment
        public string FeatureCardName;
    }

    /// <summary>Why the title must go now, or null. Checked every frame it's up.</summary>
    public static string InterruptReason(InterruptState s)
    {
        if (s.TransitionStarted) return "a {Transition} started";
        if (s.BlackShowing) return "{Black} started";
        if (s.CloseUp) return "{Zoom:ExtremeIn} close-up";
        if (s.FeatureCardArrived) return $"fullscreen {s.FeatureCardName ?? "card"} appeared";
        return null;
    }

    /// <summary>
    /// A fullscreen card counts as ARRIVING (an interruption) only when it
    /// wasn't up at the title's moment and appeared more than the grace after
    /// it — one that lands with the title (a section opening on a ,bigCenter
    /// headline) is the backdrop the title is shown over.
    /// </summary>
    public static bool IsArrival(bool wasUpAtTitle, float secondsSinceTitleDue) =>
        !wasUpAtTitle && secondsSinceTitleDue > FeatureCardGraceSeconds;
}

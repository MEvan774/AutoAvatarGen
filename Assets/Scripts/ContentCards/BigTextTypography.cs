using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The BigText moment: sizing of the stack, the scene dim behind it, and the
/// per-line punch. Pure maths only — <see cref="BigTextCard"/> does the TMP
/// measuring and drawing — so the self-tests run it with no engine.
///
/// Sizes are px of a 1080-px-tall frame, scaled by the frame's real height.
/// "Cap" is cap height (the capitals' height), not the TMP font size.
///
/// Sizing: one size for the whole stack — the largest at which the LONGEST
/// line fills at most <see cref="TargetWidthFraction"/> of the frame width,
/// with cap height ≤ <see cref="CapMaxPx"/>. Under <see cref="CapMinPx"/> the
/// size stops shrinking and the too-long lines wrap at word boundaries
/// instead. The stack must fit the title-safe height; only a stack that
/// can't, even at the floor, goes under it (and says so).
/// </summary>
public static class BigTextTypography
{
    public const float ReferenceFrameHeight = 1080f;

    public const float TargetWidthFraction = 0.85f;
    public const float CapMaxPx = 140f;
    public const float CapMinPx = 80f;
    /// <summary>Title-safe area: this share of the frame, centred (5 % margins).</summary>
    public const float TitleSafeFraction = 0.90f;

    /// <summary>Baseline-to-baseline step of a wrapped line's rows, × cap height.</summary>
    public const float RowAdvanceToCap = 1.32f;
    /// <summary>Gap between two stacked lines (baseline to the next cap top), × cap height.</summary>
    public const float LineGapToCap = 0.55f;

    // ---- Punch (each line's entrance) -----------------------------------------
    public const float PunchFromScale = 1.12f;
    public const float PunchDuration = 0.18f;
    /// <summary>DOTween OutBack overshoot: ~1 % under 1.0 before settling.</summary>
    public const float PunchOvershoot = 1.6f;
    /// <summary>The line is invisible before its beat and fully there on the
    /// first frame it's drawn (0 = no fade — the punch IS the entrance).</summary>
    public const float LineAppearSeconds = 0f;

    /// <summary>Already-visible lines glide to their re-centred slots this long.</summary>
    public const float LineShiftSeconds = 0.35f;

    /// <summary>
    /// The punch's scale <paramref name="age"/> seconds after the line's beat:
    /// <see cref="PunchFromScale"/> → 1.0 on an OutBack curve (dips ~1 % under
    /// 1.0, then settles) over <see cref="PunchDuration"/>. Driven per frame by
    /// the card — never by nested delayed tweens: DOTween adds a joined tween's
    /// delay to the sequence's insert point, so the next joined tween inherits
    /// it, and the old punch started one stagger AFTER its line had appeared
    /// (the 2026-11-08 test take: "LINES" hung at 1.12× for 0.33 s, the third
    /// line appeared 0.7 s late and hung there 0.7 s).
    /// </summary>
    public static float PunchScaleAt(float age)
    {
        if (age <= 0f) return PunchFromScale;
        if (age >= PunchDuration) return 1f;
        float t = age / PunchDuration - 1f;
        float s = PunchOvershoot;
        float f = 1f + (s + 1f) * t * t * t + s * t * t; // OutBack
        return PunchFromScale + (1f - PunchFromScale) * f;
    }

    /// <summary>The line's alpha: 0 before its beat, then in (at once when LineAppearSeconds is 0).</summary>
    public static float LineAlphaAt(float age)
    {
        if (age < 0f) return 0f;
        return LineAppearSeconds <= 0f ? 1f : Mathf.Clamp01(age / LineAppearSeconds);
    }

    /// <summary>OutQuad 0→1 for the re-centring glide.</summary>
    public static float GlideAt(float age)
    {
        float x = Mathf.Clamp01(age / LineShiftSeconds);
        return 1f - (1f - x) * (1f - x);
    }

    // ---- Exit --------------------------------------------------------------------
    public const float TextFadeOutSeconds = 0.25f;

    // ---- Dim --------------------------------------------------------------------------
    /// <summary>Brightness of the scene under the dim, as seen in the video (sRGB values).</summary>
    public const float DimBrightness = 0.45f;
    public const float DimFadeInSeconds = 0.20f;
    public const float DimFadeOutSeconds = 0.25f;

    /// <summary>
    /// Alpha of a black full-screen layer that leaves the scene at
    /// <see cref="DimBrightness"/> of its brightness in the encoded video. In
    /// linear colour space (this project) UI blends in linear light, so 55 %
    /// black would only take the sRGB values to ~70 %; the alpha is computed
    /// from the sRGB curve (γ 2.2) instead.
    /// </summary>
    public static float DimAlpha(bool linearColorSpace)
        => 1f - (linearColorSpace ? Mathf.Pow(DimBrightness, 2.2f) : DimBrightness);

    // =========================================================================
    // Stack sizing
    // =========================================================================

    public struct StackFit
    {
        /// <summary>TMP font size for every line of the stack.</summary>
        public float Size;
        /// <summary>Cap height at that size.</summary>
        public float Cap;
        /// <summary>Some line is wider than the target at this size and wraps.</summary>
        public bool Wrapped;
        /// <summary>The cap height ended under the floor (a word too long, or the stack too tall).</summary>
        public bool BelowFloor;
        /// <summary>Even shrunk, the stack is taller than the title-safe area.</summary>
        public bool TallerThanSafe;
        /// <summary>The stack's visible height (top of the first capitals to the last baseline).</summary>
        public float StackHeight;
    }

    /// <summary>
    /// Visible height of a stack: each line is one cap height plus a row
    /// advance per extra wrapped row; lines are separated by the line gap.
    /// </summary>
    public static float StackHeight(IList<int> rowsPerLine, float cap)
    {
        if (rowsPerLine == null || rowsPerLine.Count == 0) return 0f;
        float h = 0f;
        for (int i = 0; i < rowsPerLine.Count; i++)
        {
            int rows = Mathf.Max(1, rowsPerLine[i]);
            h += cap + (rows - 1) * RowAdvanceToCap * cap;
        }
        return h + (rowsPerLine.Count - 1) * LineGapToCap * cap;
    }

    /// <summary>
    /// Fits the stack. <paramref name="widthPer100"/> is each line's
    /// single-row width at font size 100, <paramref name="widestWordPer100"/>
    /// its widest word's; <paramref name="rowsAt"/>(line, size) the rows it
    /// wraps to at <see cref="TargetWidthFraction"/> of the frame width;
    /// <paramref name="capRatio"/> cap height ÷ font size.
    /// </summary>
    public static StackFit FitStack(IList<float> widthPer100, IList<float> widestWordPer100,
        Func<int, float, int> rowsAt, float capRatio, float frameWidth, float frameHeight)
    {
        var fit = new StackFit();
        int n = widthPer100 != null ? widthPer100.Count : 0;
        if (n == 0 || capRatio <= 0f) return fit;

        float k = frameHeight / ReferenceFrameHeight;
        float targetW = frameWidth * TargetWidthFraction;
        float safeH = frameHeight * TitleSafeFraction;
        float sMax = CapMaxPx * k / capRatio;
        float sMin = CapMinPx * k / capRatio;

        float widest = 0f, widestWord = 0f;
        for (int i = 0; i < n; i++)
        {
            widest = Mathf.Max(widest, widthPer100[i] / 100f);
            if (widestWordPer100 != null && i < widestWordPer100.Count)
                widestWord = Mathf.Max(widestWord, widestWordPer100[i] / 100f);
        }

        // 1. Width: the longest line at the target, never past the cap ceiling.
        float s = widest > 0f ? Mathf.Min(targetW / widest, sMax) : sMax;

        // 2. Floor: stop shrinking — the long lines wrap instead.
        if (s < sMin) s = sMin;

        // A single word wider than the line can't wrap — it has to shrink.
        if (widestWord > 0f && widestWord * s > targetW)
            s = targetW / widestWord;

        // 3. Height: the whole stack inside the title-safe area.
        Func<float, float> heightAt = size =>
        {
            var rows = new int[n];
            for (int i = 0; i < n; i++) rows[i] = rowsAt != null ? rowsAt(i, size) : 1;
            return StackHeight(rows, size * capRatio);
        };

        float h = heightAt(s);
        if (h > safeH)
        {
            // Largest size that fits, by bisection (taller stack ⇔ bigger size).
            float lo = s * 0.25f, hi = s;
            if (heightAt(lo) > safeH)
            {
                fit.TallerThanSafe = true;
                s = lo;
            }
            else
            {
                for (int it = 0; it < 20; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (heightAt(mid) <= safeH) lo = mid; else hi = mid;
                }
                s = lo;
            }
            h = heightAt(s);
        }

        fit.Size = s;
        fit.Cap = s * capRatio;
        fit.Wrapped = widest * s > targetW + 0.5f;
        fit.BelowFloor = fit.Cap < CapMinPx * k - 0.5f;
        fit.StackHeight = h;
        return fit;
    }

    // =========================================================================
    // Dim envelope
    // =========================================================================

    /// <summary>
    /// The dim's alpha over time: fades in over <see cref="DimFadeInSeconds"/>
    /// from <see cref="Open"/>, holds while the stack is up (lines joining don't
    /// touch it), and fades out over <see cref="DimFadeOutSeconds"/> from
    /// <see cref="Close"/> — from wherever it was, so closing mid-fade-in has no
    /// jump. Times are any monotonic clock (the card uses scaled time, like DOTween).
    /// </summary>
    public sealed class DimEnvelope
    {
        readonly float peak;
        float openedAt = float.NaN;
        float closedAt = float.NaN;
        float alphaAtClose;

        public DimEnvelope(float peakAlpha) { peak = Mathf.Clamp01(peakAlpha); }

        public bool IsOpen => !float.IsNaN(openedAt) && float.IsNaN(closedAt);
        public bool IsClosed => !float.IsNaN(closedAt);
        public float Peak => peak;

        /// <summary>Starts the fade-in. Ignored while already open or once closed.</summary>
        public void Open(float t)
        {
            if (!float.IsNaN(openedAt)) return;
            openedAt = t;
        }

        /// <summary>Starts the fade-out from the current alpha. Ignored unless open.</summary>
        public void Close(float t)
        {
            if (!IsOpen) return;
            alphaAtClose = AlphaAt(t);
            closedAt = t;
        }

        public float AlphaAt(float t)
        {
            if (float.IsNaN(openedAt) || t < openedAt) return 0f;
            if (!float.IsNaN(closedAt) && t >= closedAt)
                return alphaAtClose * (1f - Smooth((t - closedAt) / DimFadeOutSeconds));
            return peak * Smooth((t - openedAt) / DimFadeInSeconds);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}

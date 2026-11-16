using UnityEngine;

/// <summary>
/// The line-by-line logo run: {BigMedia:name} … {BigMedia:End}. Up to eight
/// logos, one row up to four and a second row beyond that; each logo pops in
/// on its word and the grid re-centres (eased) as it grows; a logo can be
/// struck out ({BigMedia:Strike,name}). Pure maths — <see cref="BigMediaCard"/>
/// draws it. The timed form ({BigMedia:a+b+c,4}) keeps its own fixed row and
/// never comes here unless a duration-less logo joins it.
///
/// Positions are in frame space: origin at the frame's centre, x right, y up,
/// in the frame's own px.
/// </summary>
public static class BigMediaGrid
{
    public const int MaxLogos = 8;
    public const int RowMax = 4;

    // ---- Geometry (fractions of the frame) -----------------------------------------
    /// <summary>The row's total width for 1–4 logos — the timed card's bands, so a run of three looks like a timed three.</summary>
    static readonly float[] BandWidths = { 0.70f, 0.80f, 0.88f, 0.92f };
    /// <summary>One row: the logos' height. Two rows: each row's.</summary>
    public const float OneRowHeight = 0.70f, TwoRowHeight = 0.36f;
    /// <summary>Clear space between the two rows.</summary>
    public const float RowGap = 0.06f;
    /// <summary>Px between sibling slots (the timed card's inset).</summary>
    public const float SlotPadding = 32f;

    // ---- Timing (seconds) -------------------------------------------------------------
    public const float PopSeconds = 0.20f;
    public const float PopFromScale = 0.70f;
    public const float RecentreSeconds = 0.30f;
    /// <summary>Each of the cross's two strokes; the second starts as the first ends.</summary>
    public const float StrikeStrokeSeconds = 0.10f;
    /// <summary>The struck logo dims to this alpha over the cross's length.</summary>
    public const float StruckAlpha = 0.50f;

    /// <summary>
    /// How many logos the grid is sized for: what is coming (read ahead from the
    /// script) and never fewer than what is shown — clamped to 1–8.
    /// </summary>
    public static int SizedFor(int shown, int anticipated) => Mathf.Clamp(Mathf.Max(shown, anticipated), 1, MaxLogos);

    /// <summary>
    /// One slot's size for a run of <paramref name="sizedFor"/> logos. It never
    /// changes while the run grows — only the positions do.
    /// </summary>
    public static Vector2 SlotSize(int sizedFor, Vector2 frame)
    {
        sizedFor = Mathf.Clamp(sizedFor, 1, MaxLogos);
        if (sizedFor <= RowMax)
        {
            float band = BandWidths[sizedFor - 1];
            return new Vector2(band * frame.x / sizedFor - SlotPadding, OneRowHeight * frame.y);
        }
        return new Vector2(BandWidths[RowMax - 1] * frame.x / RowMax - SlotPadding, TwoRowHeight * frame.y);
    }

    /// <summary>The distance between neighbouring slots' centres in a row.</summary>
    public static float Pitch(int sizedFor, Vector2 frame) => SlotSize(sizedFor, frame).x + SlotPadding;

    /// <summary>Row 0 holds the first four logos, row 1 the fifth to the eighth: a logo never changes row.</summary>
    public static int RowOf(int index) => index < RowMax ? 0 : 1;

    /// <summary>
    /// Where logo <paramref name="index"/> rests while <paramref name="shown"/>
    /// logos are up, in a run sized for <paramref name="sizedFor"/>. Each row is
    /// centred on what it holds now; with one row it sits at mid height, and
    /// when the fifth logo lands the first row moves up to make room.
    /// </summary>
    public static Vector2 SlotCentre(int index, int shown, int sizedFor, Vector2 frame)
    {
        shown = Mathf.Clamp(shown, 1, MaxLogos);
        index = Mathf.Clamp(index, 0, shown - 1);
        sizedFor = SizedFor(shown, sizedFor);

        int row = RowOf(index);
        int inRow = row == 0 ? Mathf.Min(shown, RowMax) : shown - RowMax;
        int col = row == 0 ? index : index - RowMax;
        float pitch = Pitch(sizedFor, frame);
        float x = (col - (inRow - 1) * 0.5f) * pitch;

        float y = 0f;
        if (shown > RowMax)
        {
            float rowH = SlotSize(sizedFor, frame).y;
            float half = (rowH + RowGap * frame.y) * 0.5f;
            y = row == 0 ? half : -half;
        }
        return new Vector2(x, y);
    }

    // ---- Timing ------------------------------------------------------------------------

    /// <summary>A logo's scale <paramref name="age"/> s after it lands: 0.7 → 1.0 with a small overshoot.</summary>
    public static float PopScaleAt(float age)
    {
        if (age <= 0f) return PopFromScale;
        if (age >= PopSeconds) return 1f;
        float t = age / PopSeconds - 1f;
        const float s = 1.6f;
        float f = 1f + (s + 1f) * t * t * t + s * t * t;   // OutBack
        return PopFromScale + (1f - PopFromScale) * f;
    }

    /// <summary>Re-centring 0→1 (ease-out cubic), <paramref name="age"/> s after the grid changed.</summary>
    public static float RecentreAt(float age)
    {
        float x = Mathf.Clamp01(age / RecentreSeconds);
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }

    /// <summary>Stroke <paramref name="stroke"/> (0 or 1) of the cross, drawn 0→1.</summary>
    public static float StrikeStrokeAt(float age, int stroke)
    {
        float x = Mathf.Clamp01((age - stroke * StrikeStrokeSeconds) / StrikeStrokeSeconds);
        return x * x * (3f - 2f * x);
    }

    /// <summary>The struck logo's alpha: 1 → <see cref="StruckAlpha"/> over the two strokes.</summary>
    public static float StruckAlphaAt(float age)
    {
        float x = Mathf.Clamp01(age / (2f * StrikeStrokeSeconds));
        return Mathf.Lerp(1f, StruckAlpha, x);
    }

    /// <summary>Whether two logo names are the same logo: as written, ignoring case and outer spaces.</summary>
    public static bool SameName(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase);
}

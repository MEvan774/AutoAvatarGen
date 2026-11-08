using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// {Compare:…} maths — value parsing, honest bar ratios, where each value
/// sits, and the bars' growth. Pure, so the self-tests run it with no engine.
/// The card only ever SHOWS the five fields as written; the numbers parsed
/// here only size the bars.
/// </summary>
public static class CompareMath
{
    public enum Unit { Plain, Percent, Multiple }

    public struct Value
    {
        public bool Ok;
        public double Amount;      // magnitude applied: "$1.5B" → 1.5e9
        public char Currency;      // '$' '€' '£' '¥', or '\0'
        public Unit Unit;
        public bool Plus;          // trailing '+' (shown as written; doesn't change the bar)
        public string Error;

        /// <summary>For warnings: "a $ amount", "a percentage", …</summary>
        public string Kind =>
            Unit == Unit.Percent ? "a percentage" :
            Unit == Unit.Multiple ? "a multiple" :
            Currency != '\0' ? $"a {Currency} amount" : "a plain number";
    }

    // optional currency · digits (with proper thousands commas) · optional
    // decimals · optional K/M/B/T · optional % or x/× · optional +
    static readonly Regex ValuePattern = new Regex(
        @"^\s*([$€£¥])?(\d{1,3}(?:,\d{3})+|\d+)(\.\d+)?([KkMmBbTt])?(%|[xX×])?(\+)?\s*$");

    public static Value Parse(string text)
    {
        var v = new Value();
        Match m = ValuePattern.Match(text ?? string.Empty);
        if (!m.Success)
        {
            v.Error = $"\"{text}\" isn't a number this card can scale (currency, digits, K/M/B/T, %, x)";
            return v;
        }
        string digits = m.Groups[2].Value.Replace(",", "") + m.Groups[3].Value;
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            v.Error = $"\"{text}\" has an unreadable number";
            return v;
        }
        switch (char.ToUpperInvariant(m.Groups[4].Success && m.Groups[4].Length > 0 ? m.Groups[4].Value[0] : ' '))
        {
            case 'K': amount *= 1e3; break;
            case 'M': amount *= 1e6; break;
            case 'B': amount *= 1e9; break;
            case 'T': amount *= 1e12; break;
        }
        v.Ok = true;
        v.Amount = amount;
        v.Currency = m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value[0] : '\0';
        v.Unit = !m.Groups[5].Success || m.Groups[5].Length == 0 ? Unit.Plain
               : m.Groups[5].Value == "%" ? Unit.Percent : Unit.Multiple;
        v.Plus = m.Groups[6].Success && m.Groups[6].Length > 0;
        return v;
    }

    /// <summary>Same currency, both %, both multiples, or both plain.</summary>
    public static bool SameKind(Value a, Value b) => a.Ok && b.Ok && a.Currency == b.Currency && a.Unit == b.Unit;

    // =========================================================================
    // Honest scale
    // =========================================================================

    /// <summary>Smallest bar drawn, as a share of the track; anything smaller is shown at this.</summary>
    public const float MinBarFraction = 0.03f;

    public struct Bars
    {
        public bool Ok;            // false → no bars: the side-by-side fallback
        public float A, B;         // share of the track, 0–1
        public bool SliverA, SliverB;
        public string Problem;     // why there are no bars
    }

    /// <summary>
    /// Linear from zero; the larger value fills the track, the smaller is at
    /// its true ratio, raised to <see cref="MinBarFraction"/> when it would be
    /// thinner (flagged).
    /// </summary>
    public static Bars Ratios(string textA, string textB)
    {
        Value a = Parse(textA), b = Parse(textB);
        var r = new Bars();
        if (!a.Ok) { r.Problem = a.Error; return r; }
        if (!b.Ok) { r.Problem = b.Error; return r; }
        if (!SameKind(a, b)) { r.Problem = $"\"{textA}\" is {a.Kind} and \"{textB}\" is {b.Kind}"; return r; }
        double max = Math.Max(a.Amount, b.Amount);
        if (max <= 0) { r.Problem = "both values are zero"; return r; }

        r.Ok = true;
        r.A = (float)(a.Amount / max);
        r.B = (float)(b.Amount / max);
        if (r.A < MinBarFraction) { r.A = MinBarFraction; r.SliverA = true; }
        if (r.B < MinBarFraction) { r.B = MinBarFraction; r.SliverB = true; }
        return r;
    }

    // =========================================================================
    // Where each value sits
    // =========================================================================

    public struct Placement
    {
        public float Track;         // bar track width (the larger bar's length)
        public bool InsideA, InsideB;
        public bool Overflows;      // a value is wider than the row can hold
    }

    /// <summary>
    /// A value sits inside its bar (right end, with <paramref name="padIn"/>)
    /// when the bar is long enough; else just past the bar's end after
    /// <paramref name="gap"/>. The track starts at the full row width and
    /// shrinks only as far as an outside value needs to stay inside the row.
    /// </summary>
    public static Placement Place(float rowWidth, float fracA, float fracB, float valueWA, float valueWB, float padIn, float gap)
    {
        var p = new Placement { Track = rowWidth };
        for (int pass = 0; pass < 4; pass++)
        {
            p.InsideA = fracA * p.Track >= valueWA + 2f * padIn;
            p.InsideB = fracB * p.Track >= valueWB + 2f * padIn;
            float track = p.Track;
            if (!p.InsideA && fracA * track + gap + valueWA > rowWidth) track = Mathf.Min(track, (rowWidth - gap - valueWA) / Mathf.Max(1e-4f, fracA));
            if (!p.InsideB && fracB * track + gap + valueWB > rowWidth) track = Mathf.Min(track, (rowWidth - gap - valueWB) / Mathf.Max(1e-4f, fracB));
            if (Mathf.Approximately(track, p.Track)) break;
            p.Track = Mathf.Max(0f, track);
        }
        p.InsideA = fracA * p.Track >= valueWA + 2f * padIn;
        p.InsideB = fracB * p.Track >= valueWB + 2f * padIn;
        p.Overflows = (!p.InsideA && fracA * p.Track + gap + valueWA > rowWidth + 0.5f) ||
                      (!p.InsideB && fracB * p.Track + gap + valueWB > rowWidth + 0.5f) || p.Track <= 0f;
        return p;
    }

    // =========================================================================
    // Growth
    // =========================================================================

    public const float GrowSeconds = 0.6f;
    public const float BDelaySeconds = 0.25f;
    /// <summary>A value fades in over the last part of its bar's growth.</summary>
    public const float ValueFadeFrom = 0.8f;

    /// <summary>A bar's growth 0→1 <paramref name="age"/> s after it starts (ease-out cubic).</summary>
    public static float GrowAt(float age)
    {
        if (age <= 0f) return 0f;
        float x = Mathf.Clamp01(age / GrowSeconds);
        float u = 1f - x;
        return 1f - u * u * u;
    }

    public static float ValueAlphaAt(float grow) => Mathf.Clamp01((grow - ValueFadeFrom) / (1f - ValueFadeFrom));
}

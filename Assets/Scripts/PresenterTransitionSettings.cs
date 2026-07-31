using UnityEngine;

/// <summary>
/// Persists the user's choice of presenter (avatar) emotion-transition
/// animation across the main menu and the recording scene — the same pattern
/// <see cref="MugsTech.Background.BackgroundModeManager"/> uses for the
/// background recording mode. The main menu writes the choice to PlayerPrefs;
/// <see cref="HybridAvatarSystem"/> reads it at scene load and runs the matching
/// transition when an emotion changes.
///
///   • SquashStretch — the classic squash-and-stretch pop on every emotion swap.
///   • Crossfade     — the new sprite fades in over the old one.
///   • Shake         — swap instantly, then shudder side-to-side (see MugsShake).
///   • Blink         — squash the height shut, swap while closed, reopen (see MugsEmotionTransition).
///   • BlinkHeavy    — a slower, tired slow-blink that holds the eyes shut a beat.
///   • Cut           — instant swap, no animation.
///   • Grow          — the presenter swells up, swaps at the peak, and settles
///                     back to the original size (0.9s total).
///
/// A single emotion change can also override the global choice for that one swap
/// via the {Emotion,Style} tag (e.g. {Serious,Cut}); see HybridAvatarSystem's
/// emotion parser. When no choice has been saved yet, callers pass a fallback
/// (the recording scene uses the HybridAvatarSystem `useCrossfade` inspector
/// toggle), so existing scenes behave exactly as before until the user picks
/// something here.
/// </summary>
public static class PresenterTransitionSettings
{
    public enum Style
    {
        SquashStretch = 0,
        Crossfade     = 1,
        Shake         = 2,
        Blink         = 3,
        BlinkHeavy    = 4,
        Cut           = 5,
        Grow          = 6,
    }

    public const string StylePrefKey = "AutoAvatarGen.PresenterTransitionStyle";

    /// <summary>
    /// The saved style, or <paramref name="fallback"/> when the user hasn't
    /// chosen one yet (or the stored value is out of range).
    /// </summary>
    public static Style LoadStyle(Style fallback)
    {
        int v = PlayerPrefs.GetInt(StylePrefKey, -1);
        int max = System.Enum.GetValues(typeof(Style)).Length - 1;
        if (v < 0 || v > max) return fallback;
        return (Style)v;
    }

    public static void SaveStyle(Style style)
    {
        PlayerPrefs.SetInt(StylePrefKey, (int)style);
        PlayerPrefs.Save();
    }

    public static string Label(Style s)
    {
        switch (s)
        {
            case Style.SquashStretch: return "Squash & Stretch";
            case Style.Crossfade:     return "Crossfade";
            case Style.Shake:         return "Shake";
            case Style.Blink:         return "Blink";
            case Style.BlinkHeavy:    return "Blink (Heavy)";
            case Style.Cut:           return "Cut (instant)";
            case Style.Grow:          return "Grow";
            default:                  return s.ToString();
        }
    }

    public static Style Cycle(Style current, int direction = +1)
    {
        int count = System.Enum.GetValues(typeof(Style)).Length;
        int next = (((int)current + direction) % count + count) % count;
        return (Style)next;
    }

    /// <summary>
    /// Parses a style keyword used in the {Emotion,Style} per-tag override
    /// (case-insensitive; accepts a couple of friendly aliases). Returns false
    /// for null / empty / unrecognised input.
    /// </summary>
    public static bool TryParse(string s, out Style style)
    {
        style = Style.SquashStretch;
        if (string.IsNullOrWhiteSpace(s)) return false;
        switch (s.Trim().ToLowerInvariant())
        {
            case "squashstretch":
            case "squash":   style = Style.SquashStretch; return true;
            case "crossfade":
            case "fade":     style = Style.Crossfade;     return true;
            case "shake":    style = Style.Shake;         return true;
            case "blink":    style = Style.Blink;         return true;
            case "blinkheavy":
            case "heavy":    style = Style.BlinkHeavy;    return true;
            case "cut":
            case "instant":  style = Style.Cut;           return true;
            case "grow":
            case "pop":      style = Style.Grow;          return true;
            default:         return false;
        }
    }
}

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
///
/// When no choice has been saved yet, callers pass a fallback (the recording
/// scene uses the HybridAvatarSystem `useCrossfade` inspector toggle), so
/// existing scenes behave exactly as before until the user picks something here.
/// </summary>
public static class PresenterTransitionSettings
{
    public enum Style
    {
        SquashStretch = 0,
        Crossfade     = 1,
        Shake         = 2,
    }

    public const string StylePrefKey = "AutoAvatarGen.PresenterTransitionStyle";

    /// <summary>
    /// The saved style, or <paramref name="fallback"/> when the user hasn't
    /// chosen one yet (or the stored value is out of range).
    /// </summary>
    public static Style LoadStyle(Style fallback)
    {
        int v = PlayerPrefs.GetInt(StylePrefKey, -1);
        if (v < 0 || v > (int)Style.Shake) return fallback;
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
            default:                  return s.ToString();
        }
    }

    public static Style Cycle(Style current, int direction = +1)
    {
        const int count = 3; // keep in sync with the Style enum size
        int next = (((int)current + direction) % count + count) % count;
        return (Style)next;
    }
}

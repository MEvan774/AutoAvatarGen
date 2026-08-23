using UnityEngine;

/// <summary>
/// Persists the user's choice of content-card / media entry animation across
/// the main menu and the recording scene — the same pattern
/// <see cref="PresenterTransitionSettings"/> uses for the presenter's emotion
/// transition. The main menu's "Card entry animation" cycle row writes the
/// choice to PlayerPrefs; <see cref="CardEntryAnimator"/> reads it at scene
/// load and every card (Headline, Quote, Stat, BigImage, BigText, BigCenter,
/// BigMedia) plus the {Image:}/{Video:} media display follow it.
///
///   • Overshoot — the shipped CSS-derived curve: the card snaps ~10% past its
///                 resting position and settles back (CardEntryAnimator.Curve).
///   • EaseFade  — a smooth decelerating ease (no overshoot, DOTween OutCubic by
///                 default) with the fade-in stretched over the whole slide so the
///                 card visibly dissolves into place instead of just sliding.
///
/// When nothing has been saved yet, callers pass a fallback (the recording
/// scene uses the CardEntryAnimator inspector value), so existing scenes behave
/// exactly as before until the user picks something in the menu.
/// </summary>
public static class CardEntrySettings
{
    public enum Style
    {
        Overshoot = 0,
        EaseFade  = 1,
    }

    public const string StylePrefKey = "AutoAvatarGen.CardEntryStyle";

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
            case Style.Overshoot: return "Overshoot";
            case Style.EaseFade:  return "Ease in + fade";
            default:              return s.ToString();
        }
    }

    public static Style Cycle(Style current, int direction = +1)
    {
        int count = System.Enum.GetValues(typeof(Style)).Length;
        int next = (((int)current + direction) % count + count) % count;
        return (Style)next;
    }
}

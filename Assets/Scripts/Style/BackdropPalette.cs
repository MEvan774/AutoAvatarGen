using UnityEngine;
using MugsTech.Background;

namespace MugsTech.Style
{
    /// <summary>
    /// Per-backdrop palette for the content-card accents, so the cards read
    /// as one set with whichever animated background is active. (The screen
    /// transitions are deliberately NOT themed here — they are hard editorial
    /// blacks on every backdrop; see ScreenTransitionController's colors.)
    ///
    /// Every getter takes the caller's own color (inspector field or brand
    /// constant) and returns it unchanged unless the active
    /// <see cref="BackgroundStyleManager"/> style defines an override here, so
    /// Synthwave / Late Night Desk / Night City keep the approved coral look
    /// untouched.
    ///
    /// Semantic colors are deliberately NOT routed through this class:
    /// StatCard's up-green / down-red must stay recognizable on every backdrop
    /// (see ContentCardUIBuilder.PositiveGreen / NegativeRed).
    /// </summary>
    public static class BackdropPalette
    {
        // ------------------------------------------------------------------
        // Violet Doodles — sampled from the VioletDrift gradient artwork
        // (deep indigo corners → vivid violet).
        // ------------------------------------------------------------------
        static readonly Color VioletCardAccent = new Color(0x7B / 255f, 0x5C / 255f, 0xE5 / 255f, 1f); // #7B5CE5
        static readonly Color VioletCardPaper  = new Color(0xF4 / 255f, 0xF2 / 255f, 0xFB / 255f, 1f); // #F4F2FB cool lavender-white

        static bool VioletActive =>
            BackgroundStyleManager.LoadStyle() == BackgroundStyleManager.Style.VioletDoodles;

        /// <summary>Decorative card accent (accent bar, quote marks, stat number).</summary>
        public static Color CardAccent(Color fallback) => VioletActive ? VioletCardAccent : fallback;

        /// <summary>
        /// Card panel color. Overrides the channel preset's warm cream with a
        /// cool lavender-white while the violet backdrop is active — warm
        /// paper on the cold violet gradient is the single loudest clash.
        /// </summary>
        public static Color CardPaper(Color fallback) => VioletActive ? VioletCardPaper : fallback;
    }
}

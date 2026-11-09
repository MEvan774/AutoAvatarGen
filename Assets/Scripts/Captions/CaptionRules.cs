using System;

namespace MugsTech.Captions
{
    /// <summary>
    /// When captions may show: only after the frame has been bare (no card,
    /// media, full-screen card, Echo, chapter title, zoom run, close-up, black
    /// or transition) for <see cref="HoldOffSeconds"/>, so short gaps between
    /// cards never flicker a caption in and out — and the instant anything
    /// arrives they're gone (no fade racing a card's slide-in). Pure.
    /// </summary>
    public sealed class CaptionGate
    {
        public const float HoldOffSeconds = 1.2f;
        float bareFor;

        public bool Open => bareFor >= HoldOffSeconds;

        /// <summary>One frame: <paramref name="bare"/> = nothing else on the frame. True = captions may show.</summary>
        public bool Step(float dt, bool bare)
        {
            if (!bare) { bareFor = 0f; return false; }
            bareFor += Math.Max(0f, dt);
            return Open;
        }

        public void Reset() => bareFor = 0f;
    }

    /// <summary>
    /// Where captions sit, as a share of the frame (x from the left, y from the
    /// top): one band along the bottom, under Mugs's feet (they end about 78 %
    /// down at every position), centred, at most 80 % of the width. Its lower
    /// edge stays 12 % above the frame's — clear of YouTube's progress bar and
    /// inside title-safe. One line when it fits; a longer phrase wraps onto a
    /// second line that grows upward. The same for Mugs Left, Center and Right.
    /// Pure.
    /// </summary>
    public static class CaptionZones
    {
        public const float BandTop = 0.79f, BandBottom = 0.88f;
        public const float MaxWidth = 0.80f;
        public const int MaxLines = 2;

        public struct Zone
        {
            public float XMin, XMax, YMin, YMax;   // shares of the frame, y from the top
            public float CenterX => (XMin + XMax) * 0.5f;
            public float CenterY => (YMin + YMax) * 0.5f;
            public float Width => XMax - XMin;
        }

        /// <summary>The bottom band.</summary>
        public static Zone Bottom => new Zone
        {
            XMin = 0.5f - MaxWidth / 2f, XMax = 0.5f + MaxWidth / 2f, YMin = BandTop, YMax = BandBottom,
        };
    }
}

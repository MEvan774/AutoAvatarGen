using System;
using System.Collections.Generic;

namespace MugsTech.Captions
{
    /// <summary>
    /// When captions show. Decided per PHRASE, not per frame, so a caption never
    /// pops in halfway through a line or flashes up just before a card. Pure.
    ///
    ///   • A phrase may appear only at its own start: within
    ///     <see cref="StartToleranceSeconds"/> of <see cref="CaptionPhrase.Start"/>.
    ///     A phrase whose start passed while something else held the frame is
    ///     skipped whole (<see cref="SkippedLate"/>); the next one gets its turn.
    ///   • It starts only when nothing is due before its words are said
    ///     (<see cref="CaptionPhrase.SpokenEnd"/>): the caller passes the next
    ///     stop (MediaPresentationSystem.NextCaptionStop: every zoom, media,
    ///     black, transition and card event, a chapter title, the Echo, a
    ///     smooth {Position} glide). Otherwise it is skipped
    ///     (<see cref="SkippedAhead"/>).
    ///   • Once up it stays until its End (the linger, or the bridge to the
    ///     next phrase). Something arriving unannounced still hides it on that
    ///     very frame (<see cref="Interrupted"/> counts the times that cut its
    ///     words short), and it does not come back.
    ///   • The frame must also have been bare (no card, media, Echo, chapter
    ///     title, zoom run, close-up, black, transition or presenter glide) for
    ///     <see cref="HoldOffSeconds"/>. With the look-ahead deciding what may
    ///     start, the hold-off only keeps a caption from arriving in the same
    ///     breath as something leaving. 0.4 s is a card's fade-out (0.3 s,
    ///     CardEntryAnimator) plus three frames: a caption never starts while
    ///     the eye is still on a card that just went. It was 1.2 s when it
    ///     also had to stand in for the missing look-ahead.
    /// </summary>
    public sealed class CaptionGate
    {
        public const float HoldOffSeconds = 0.4f;
        public const float StartToleranceSeconds = 0.2f;

        float bareFor;
        bool wasBare;
        CaptionPhrase current;
        float shownFor;
        readonly HashSet<CaptionPhrase> decided = new HashSet<CaptionPhrase>();

        /// <summary>Phrases put on screen.</summary>
        public int Shown { get; private set; }
        /// <summary>Phrases skipped because the frame came free only after their start.</summary>
        public int SkippedLate { get; private set; }
        /// <summary>Phrases skipped because something was due before their words were said.</summary>
        public int SkippedAhead { get; private set; }
        /// <summary>Phrases hidden by something unannounced before their words were said.</summary>
        public int Interrupted { get; private set; }
        /// <summary>The shortest time any phrase was on screen (NaN until one has left).</summary>
        public float ShortestShownSeconds { get; private set; } = float.NaN;

        /// <summary>The phrase on screen now (null when none).</summary>
        public CaptionPhrase Current => current;

        /// <summary>
        /// What happened to a phrase on the last <see cref="Step"/>, for the log
        /// ("up", "skipped late …", "skipped ahead …", "cut short …"), or null.
        /// </summary>
        public string LastVerdict { get; private set; }
        public CaptionPhrase LastVerdictPhrase { get; private set; }

        void Verdict(CaptionPhrase p, string what) { LastVerdictPhrase = p; LastVerdict = what; }

        /// <summary>
        /// One frame. <paramref name="bare"/>: nothing else on the frame now.
        /// <paramref name="t"/>: the voice clock. <paramref name="nextStop"/>
        /// gives the voice time of the next thing due after a time (+∞ for
        /// none); it is only asked when a phrase could start. Returns the phrase
        /// to show, or null.
        /// </summary>
        public CaptionPhrase Step(float dt, bool bare, float t, CaptionTrack track, Func<float, float> nextStop)
        {
            dt = Math.Max(0f, dt);
            LastVerdict = null;
            LastVerdictPhrase = null;
            if (!bare || float.IsNaN(t))
            {
                bareFor = 0f;
                wasBare = false;
                if (current != null) Leave(t);
                return null;
            }

            // Bare since the first bare frame: that frame counts as 0 s.
            bareFor = wasBare ? bareFor + dt : 0f;
            wasBare = true;
            if (current != null)
            {
                if (t < current.End) { shownFor += dt; return current; }
                Leave(t);
            }

            CaptionPhrase p = track != null ? track.At(t) : null;
            if (p == null || p.Excluded || decided.Contains(p)) return null;
            if (bareFor + 1e-4f < HoldOffSeconds) return null;   // not free yet; it may still come free in time

            decided.Add(p);
            if (t - p.Start > StartToleranceSeconds)
            {
                SkippedLate++;
                Verdict(p, $"skipped late (frame free {t - p.Start:F2} s after its first word)");
                return null;
            }
            float said = p.SpokenEnd > p.Start ? Math.Min(p.SpokenEnd, p.End) : p.End;
            float stop = nextStop != null ? nextStop(t) : float.PositiveInfinity;
            if (stop < said)
            {
                SkippedAhead++;
                Verdict(p, $"skipped ahead (something due at {stop:F2}, its last word ends {said:F2})");
                return null;
            }

            current = p;
            shownFor = dt;   // on screen for this frame
            Shown++;
            Verdict(p, "up");
            return p;
        }

        /// <summary>The take is over: the phrase still up (if any) leaves now.</summary>
        public void Finish(float t)
        {
            if (current != null) Leave(t);
        }

        void Leave(float t)
        {
            float said = current.SpokenEnd > current.Start ? Math.Min(current.SpokenEnd, current.End) : current.End;
            if (!float.IsNaN(t) && t < said - 1e-3f)
            {
                Interrupted++;
                Verdict(current, $"cut short at {t:F2} (its last word ends {said:F2})");
            }
            if (float.IsNaN(ShortestShownSeconds) || shownFor < ShortestShownSeconds) ShortestShownSeconds = shownFor;
            current = null;
            shownFor = 0f;
        }
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

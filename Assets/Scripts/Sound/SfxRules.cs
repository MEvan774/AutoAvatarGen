using System;
using System.Collections.Generic;

namespace MugsTech.Sound
{
    /// <summary>
    /// When the sound-effect layer may sound. Pure — no engine state, so the
    /// self-tests drive every rule directly:
    /// <list type="bullet">
    /// <item>never two effects within <see cref="MinSpacing"/> (count clicks are exempt);</item>
    /// <item>never on a word's attack — an effect that would land on one is
    /// pushed back 60 ms, or 120 ms, whichever clears it first;</item>
    /// <item>when several pictures change in the same frame (a BigText line as a
    /// card arrives) only the most important one sounds.</item>
    /// </list>
    /// </summary>
    public static class SfxRules
    {
        /// <summary>The rate limit: at most one effect per 150 ms.</summary>
        public const float MinSpacing = 0.15f;

        /// <summary>The delays tried, in order, to keep an effect off a word's attack.</summary>
        public static readonly float[] Delays = { 0f, 0.06f, 0.12f };

        /// <summary>A word's attack: from 25 ms before its onset to 50 ms after.</summary>
        public const float AttackLead = 0.025f, AttackHold = 0.05f;

        /// <summary>A Stat's count-up never clicks more than this.</summary>
        public const int MaxCountClicks = 12;

        /// <summary>Count clicks follow the digits exactly: no rate limit, no attack delay.</summary>
        public static bool Exempt(SfxKind k) => k == SfxKind.CountClick;

        /// <summary>Which effect wins when several fire in the same frame.</summary>
        public static int Priority(SfxKind k)
        {
            switch (k)
            {
                case SfxKind.BigTextLine: return 3;
                case SfxKind.BigCardIn:   return 2;
                case SfxKind.CardIn:      return 1;
                default:                  return 0;
            }
        }

        /// <summary>
        /// How long to hold an effect that would start at <paramref name="voiceTime"/>
        /// (seconds on the voice clip) so it doesn't land on a word's attack:
        /// the first of 0 / 60 / 120 ms that is clear of every onset's attack, or,
        /// if none is, the one furthest from an onset. NaN (the voice isn't
        /// playing) or no onsets → 0.
        /// </summary>
        public static float DelayFor(float voiceTime, float[] onsets)
        {
            if (float.IsNaN(voiceTime) || onsets == null || onsets.Length == 0) return 0f;
            float best = 0f, bestMargin = float.NegativeInfinity;
            foreach (float d in Delays)
            {
                float margin = Margin(voiceTime + d, onsets);
                if (margin > 0f) return d;
                if (margin > bestMargin) { bestMargin = margin; best = d; }
            }
            return best;
        }

        // How far x sits outside the nearest attack window (≤ 0 inside one).
        static float Margin(float x, float[] onsets)
        {
            int i = Array.BinarySearch(onsets, x);
            if (i < 0) i = ~i;
            float m = float.PositiveInfinity;
            for (int j = Math.Max(0, i - 2); j < Math.Min(onsets.Length, i + 2); j++)
            {
                float o = onsets[j];
                float outside = x < o ? (o - AttackLead) - x : x - (o + AttackHold);
                m = Math.Min(m, outside);
            }
            return m;
        }
    }

    /// <summary>At most one effect per <see cref="SfxRules.MinSpacing"/>; count clicks pass freely.</summary>
    public sealed class SfxRateLimiter
    {
        float last = float.NegativeInfinity;

        /// <summary>True if an effect of <paramref name="k"/> may start at <paramref name="t"/> (and records it).</summary>
        public bool Allow(SfxKind k, float t)
        {
            if (SfxRules.Exempt(k)) return true;
            if (t - last < SfxRules.MinSpacing - 1e-4f) return false;
            last = t;
            return true;
        }

        public void Reset() => last = float.NegativeInfinity;
    }

    /// <summary>
    /// Collects a frame's requests and settles them once per frame: the most
    /// important one plays — held off a word's attack if it would land on one —
    /// unless the rate limit says it's too soon; the rest are dropped.
    /// </summary>
    public sealed class SfxScheduler
    {
        readonly List<SfxKind> requests = new List<SfxKind>();
        readonly SfxRateLimiter limiter = new SfxRateLimiter();

        public int PendingCount => requests.Count;

        public void Request(SfxKind k) => requests.Add(k);

        /// <summary>
        /// Settles this frame's requests at <paramref name="now"/>. True with the
        /// effect to play and how long to hold it; false if nothing sounds
        /// (<paramref name="kind"/> then names the effect the rate limit dropped,
        /// if there was one).
        /// </summary>
        public bool Resolve(float now, float voiceTime, float[] onsets, out SfxKind kind, out float delay)
        {
            kind = default;
            delay = 0f;
            if (requests.Count == 0) return false;
            SfxKind best = requests[0];
            foreach (SfxKind k in requests)
                if (SfxRules.Priority(k) > SfxRules.Priority(best)) best = k;
            requests.Clear();
            kind = best;

            float d = SfxRules.DelayFor(voiceTime, onsets);
            if (!limiter.Allow(best, now + d)) return false;
            delay = d;
            return true;
        }

        public void Clear()
        {
            requests.Clear();
            limiter.Reset();
        }
    }

    /// <summary>
    /// Finds where words (and syllables) start in the voice: the level in 5 ms
    /// hops, an onset wherever a hop rises 9 dB above the quietest of the 40 ms
    /// before it, above a gate 35 dB under the loudest hop (never below
    /// −55 dBFS), at most one per 60 ms — an attack that lands inside those
    /// 60 ms is reported as they end if it's still rising over its lookback,
    /// so a word right after a click isn't lost. Fed in slices, read once at the end.
    /// </summary>
    public sealed class OnsetDetector
    {
        public const float HopSeconds = 0.005f, LookbackSeconds = 0.040f, RefractorySeconds = 0.060f;
        public const float RiseDb = 9f, GateBelowLoudestDb = 35f, FloorDbfs = -55f;

        readonly int channels, hop;
        readonly float rate;
        readonly List<float> hopDb = new List<float>();
        double acc;
        int accFrames;

        public OnsetDetector(int sampleRate, int channels)
        {
            rate = Math.Max(1, sampleRate);
            this.channels = Math.Max(1, channels);
            hop = Math.Max(1, (int)Math.Round(sampleRate * HopSeconds));
        }

        public void Add(float[] interleaved, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                double s = 0;
                for (int c = 0; c < channels; c++) { double v = interleaved[i * channels + c]; s += v * v; }
                acc += s / channels;
                if (++accFrames == hop)
                {
                    hopDb.Add((float)(10.0 * Math.Log10(acc / hop + 1e-12)));
                    acc = 0;
                    accFrames = 0;
                }
            }
        }

        /// <summary>The onsets, in seconds from the first sample, ascending.</summary>
        public float[] Finish()
        {
            var onsets = new List<float>();
            if (hopDb.Count == 0) return onsets.ToArray();
            float loudest = float.NegativeInfinity;
            foreach (float d in hopDb) loudest = Math.Max(loudest, d);
            float gate = Math.Max(loudest - GateBelowLoudestDb, FloorDbfs);
            int look = (int)Math.Round(LookbackSeconds / HopSeconds);
            int refractory = (int)Math.Round(RefractorySeconds / HopSeconds);
            int last = int.MinValue / 2;
            for (int i = 0; i < hopDb.Count; i++)
            {
                if (hopDb[i] < gate || i - last < refractory) continue;
                float min = float.PositiveInfinity;
                for (int j = i - look; j < i; j++) min = Math.Min(min, j >= 0 ? hopDb[j] : -120f);
                if (hopDb[i] - min >= RiseDb)
                {
                    onsets.Add(i * hop / rate);
                    last = i;
                }
            }
            return onsets.ToArray();
        }
    }

    /// <summary>
    /// One click per step of a Stat's count-up: a click whenever the shown
    /// number changes, no closer together than duration / 12, never more than
    /// 12 — the last one kept for the final value, which always clicks.
    /// </summary>
    public sealed class CountUpClicker
    {
        readonly float spacing;
        string shown, clicked;
        float lastClick = float.NegativeInfinity;

        public int Clicks { get; private set; }

        public CountUpClicker(float duration)
        {
            spacing = Math.Max(0f, duration) / SfxRules.MaxCountClicks;
        }

        /// <summary>The number on screen changed to <paramref name="text"/> at <paramref name="now"/>; true = click.</summary>
        public bool Step(string text, float now)
        {
            if (text == shown) return false;
            shown = text;
            if (Clicks >= SfxRules.MaxCountClicks - 1 || now - lastClick < spacing - 1e-4f) return false;
            return Click(text, now);
        }

        /// <summary>The count-up finished on <paramref name="text"/>; true = click (once, unless it just did).</summary>
        public bool Finish(string text, float now)
        {
            shown = text;
            if (text == clicked || Clicks >= SfxRules.MaxCountClicks) return false;
            return Click(text, now);
        }

        bool Click(string text, float now)
        {
            Clicks++;
            clicked = text;
            lastClick = now;
            return true;
        }
    }
}

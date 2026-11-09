using System;

namespace MugsTech.Background
{
    /// <summary>
    /// The pure rules behind the take's level and its ending: the gain that
    /// lands the mix on −14 LUFS without passing −1 dBTP, the music bed's
    /// volume at any moment (its own fades, the constant duck, and the fade
    /// after the last word), and where the last word ends. No engine state —
    /// the self-tests run it directly, and BackgroundMusicPlayer (playback) and
    /// TakeLoudness (the offline mix it measures) share <see cref="BedVolume"/>
    /// so what is measured is what plays.
    /// </summary>
    public static class TakeLoudnessMath
    {
        /// <summary>YouTube's reference level; it only ever turns louder takes down.</summary>
        public const float TargetLufs = -14f;
        /// <summary>The ceiling the gain may not push the mix's true peak past.</summary>
        public const float TruePeakCeilingDbtp = -1f;
        /// <summary>Never more than this up or down — a measurement that wants more is wrong.</summary>
        public const float MaxBoostDb = 12f, MaxCutDb = -20f;

        /// <summary>The bed's fade after the last word.</summary>
        public const float EndFadeSeconds = 1.5f;
        /// <summary>The bed's fade at each track's start and end (the music brief's 2 s).</summary>
        public const float TrackFadeSeconds = 2f;

        /// <summary>
        /// Unity plays a mono clip from a 2D AudioSource into each stereo channel
        /// at −3 dB (equal-power pan) — measured on the 2026-11-02 Binance take:
        /// each channel 2.9 dB under the mono voice, same integrated loudness.
        /// </summary>
        public const float MonoToStereoGain = 0.70710678f;

        public struct Gain
        {
            public float Db;
            public bool Capped;      // the true-peak ceiling lowered it
            public bool Unmeasured;  // nothing measurable — no gain
            public float PredictedLufs, PredictedTruePeakDbtp;
        }

        /// <summary>
        /// One gain for the whole mix: target − measured, lowered if the mix's
        /// true peak would pass the ceiling, never outside +12 / −20 dB.
        /// </summary>
        public static Gain GainFor(double mixLufs, double mixTruePeakDbtp,
                                   float target = TargetLufs, float ceiling = TruePeakCeilingDbtp)
        {
            var g = new Gain();
            if (double.IsNaN(mixLufs) || double.IsInfinity(mixLufs))
            {
                g.Unmeasured = true;
                g.PredictedLufs = float.NaN;
                g.PredictedTruePeakDbtp = (float)mixTruePeakDbtp;
                return g;
            }
            double db = target - mixLufs;
            if (!double.IsNegativeInfinity(mixTruePeakDbtp) && mixTruePeakDbtp + db > ceiling)
            {
                db = ceiling - mixTruePeakDbtp;
                g.Capped = true;
            }
            db = Math.Max(MaxCutDb, Math.Min(MaxBoostDb, db));
            g.Db = (float)db;
            g.PredictedLufs = (float)(mixLufs + db);
            g.PredictedTruePeakDbtp = (float)(mixTruePeakDbtp + db);
            return g;
        }

        public static float DbToLinear(float db) => (float)Math.Pow(10.0, db / 20.0);

        /// <summary>
        /// The bed after the last word: full until it, then a linear fade to 0
        /// over <see cref="EndFadeSeconds"/>. <paramref name="sinceLastWord"/> is
        /// negative while words are still coming.
        /// </summary>
        public static float EndEnvelope(float sinceLastWord)
        {
            if (sinceLastWord <= 0f) return 1f;
            return Math.Max(0f, 1f - sinceLastWord / EndFadeSeconds);
        }

        /// <summary>
        /// The bed's AudioSource volume: the constant duck × the track's
        /// equalising gain × its own 2 s fade-in and fade-out × the end fade.
        /// Clamped to an AudioSource's 0–1.
        /// </summary>
        public static float BedVolume(float duck, float trackGain, float posInTrack, float trackDuration, float sinceLastWord)
        {
            float fadeIn = Clamp01(posInTrack / TrackFadeSeconds);
            float fadeOut = Clamp01((trackDuration - posInTrack) / TrackFadeSeconds);
            return Clamp01(duck * trackGain * fadeIn * fadeOut * EndEnvelope(sinceLastWord));
        }

        /// <summary>
        /// Where the last word ends in a buffer of the voice's tail: the end of
        /// the last 20 ms window whose RMS is within 35 dB of the loudest window
        /// (and above −55 dBFS) — ElevenLabs' trailing silence sits far below.
        /// Returns seconds from the buffer's start, or NaN when it holds no speech.
        /// </summary>
        public static float LastSpeechEnd(float[] interleaved, int channels, int sampleRate)
        {
            if (interleaved == null || channels <= 0 || sampleRate <= 0) return float.NaN;
            int frames = interleaved.Length / channels;
            int win = Math.Max(1, sampleRate / 50);
            int count = frames / win;
            if (count == 0) return float.NaN;
            var rms = new double[count];
            double loudest = 0;
            for (int w = 0; w < count; w++)
            {
                double sum = 0;
                for (int i = w * win; i < (w + 1) * win; i++)
                    for (int c = 0; c < channels; c++) { double v = interleaved[i * channels + c]; sum += v * v; }
                rms[w] = Math.Sqrt(sum / (win * channels));
                if (rms[w] > loudest) loudest = rms[w];
            }
            if (loudest <= 0) return float.NaN;
            double threshold = Math.Max(loudest * Math.Pow(10, -35.0 / 20.0), Math.Pow(10, -55.0 / 20.0));
            for (int w = count - 1; w >= 0; w--)
                if (rms[w] >= threshold) return (float)((w + 1) * win) / sampleRate;
            return float.NaN;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}

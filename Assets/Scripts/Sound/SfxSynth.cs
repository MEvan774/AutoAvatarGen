using System;

namespace MugsTech.Sound
{
    /// <summary>The layer's sounds — one per picture change it scores.</summary>
    public enum SfxKind
    {
        CardIn,       // a side card (or side image/video) slides in
        CountClick,   // one step of a Stat's count-up
        BigTextLine,  // a BigText line lands
        BigCardIn,    // big headline / BigImage / BigMedia / Versus in
    }

    /// <summary>
    /// The sound-effect layer's clips, synthesised in code — no sound files, so
    /// no licences. Deterministic (seeded noise), so every take sounds the
    /// same. Each clip is peak-normalised to its table level at full volume
    /// (<see cref="PeakDbfs"/>); the main menu's "Sound effects" field scales
    /// them all. Pure — the self-tests check every clip.
    /// </summary>
    public static class SfxSynth
    {
        public const float MaxSeconds = 0.4f;

        /// <summary>
        /// Peak level at "Sound effects" = 1.0. The brief's first table (−16 to
        /// −28 dBFS) was inaudible in the first test render; it was raised
        /// 11–15 dB, then (by request, 2026-11-09) brought down 3 dB again. At
        /// the default 60 % and the take's gain the ticks and the thud peak
        /// about 6–7 dB under the transition whooshes, the count clicks well
        /// under that; a rare collision with a loud syllable is softened by the
        /// ceiling in <c>ListenerGain</c>.
        /// </summary>
        public static float PeakDbfs(SfxKind k)
        {
            switch (k)
            {
                case SfxKind.CardIn:      return -9f;
                case SfxKind.CountClick:  return -17f;
                case SfxKind.BigTextLine: return -8f;
                case SfxKind.BigCardIn:   return -9f;
                default:                  return -9f;
            }
        }

        /// <summary>The override file's name: <c>&lt;Media&gt;/SFX/&lt;name&gt;.wav</c>.</summary>
        public static string FileName(SfxKind k)
        {
            switch (k)
            {
                case SfxKind.CardIn:      return "card_in";
                case SfxKind.CountClick:  return "count_click";
                case SfxKind.BigTextLine: return "bigtext_line";
                case SfxKind.BigCardIn:   return "big_card_in";
                default:                  return k.ToString();
            }
        }

        /// <summary>The mono clip for <paramref name="k"/> at <paramref name="rate"/>, peak-normalised.</summary>
        public static float[] Make(SfxKind k, int rate)
        {
            float[] s;
            switch (k)
            {
                case SfxKind.CardIn:      s = Tick(rate, 2828f, 140f, 11); break;   // 2–4 kHz band, a tiny low click
                case SfxKind.BigCardIn:   s = Tick(rate, 1414f, 70f, 12); break;    // the same tick an octave lower
                case SfxKind.CountClick:  s = Click(rate); break;
                case SfxKind.BigTextLine: s = Thud(rate); break;
                default:                  s = new float[1]; break;
            }
            return Normalise(s, PeakDbfs(k));
        }

        /// <summary>Scales a clip so its largest |sample| sits at <paramref name="peakDbfs"/>.</summary>
        public static float[] Normalise(float[] s, float peakDbfs)
        {
            float peak = 0f;
            foreach (float v in s) peak = Math.Max(peak, Math.Abs(v));
            if (peak <= 0f) return s;
            float k = (float)Math.Pow(10.0, peakDbfs / 20.0) / peak;
            for (int i = 0; i < s.Length; i++) s[i] *= k;
            return s;
        }

        // ---- The sounds ------------------------------------------------------------------

        // 60 ms band-passed noise burst, 5 ms attack, quick decay, plus a tiny low click.
        static float[] Tick(int rate, float centre, float clickHz, int seed)
        {
            int n = (int)(0.060f * rate);
            var s = new float[n];
            var bp = new Biquad(rate, centre, 1.41f);
            var rng = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Math.Min(1f, t / 0.005f) * (float)Math.Exp(-Math.Max(0f, t - 0.005f) / 0.012f);
                float noise = bp.Process((float)(rng.NextDouble() * 2 - 1)) * env;
                float click = 0.2f * (float)Math.Sin(2 * Math.PI * clickHz * t) * (float)Math.Exp(-t / 0.004f) * Math.Min(1f, t / 0.0005f);
                s[i] = noise + click;
            }
            return s;
        }

        // 8 ms high, dry click for each count-up step.
        static float[] Click(int rate)
        {
            int n = (int)(0.008f * rate);
            var s = new float[n];
            var bp = new Biquad(rate, 4000f, 2f);
            var rng = new Random(21);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Math.Min(1f, t / 0.0005f) * (float)Math.Exp(-t / 0.002f);
                s[i] = bp.Process((float)(rng.NextDouble() * 2 - 1)) * env;
            }
            return s;
        }

        // A low wooden thud (120 Hz, 150 ms) with a short knock on top, plus the card tick.
        static float[] Thud(int rate)
        {
            int n = (int)(0.150f * rate);
            var s = new float[n];
            float[] tick = Tick(rate, 2828f, 140f, 41);
            var knock = new Biquad(rate, 900f, 1.2f);
            var rng = new Random(42);
            float tickPeak = 0f;
            foreach (float v in tick) tickPeak = Math.Max(tickPeak, Math.Abs(v));
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float body = (float)Math.Sin(2 * Math.PI * 120f * t) * Math.Min(1f, t / 0.002f) * (float)Math.Exp(-t / 0.035f);
                float k = knock.Process((float)(rng.NextDouble() * 2 - 1)) * (float)Math.Exp(-t / 0.006f) * 0.6f;
                float tk = i < tick.Length && tickPeak > 0f ? tick[i] / tickPeak * 0.5f : 0f;
                s[i] = body + k + tk;
            }
            return s;
        }

        // RBJ band-pass (constant 0 dB peak gain).
        sealed class Biquad
        {
            float b0, b2, a1, a2, x1, x2, y1, y2;
            public Biquad(int rate, float f0, float q) => Set(rate, f0, q);
            public void Set(int rate, float f0, float q)
            {
                double w0 = 2 * Math.PI * Math.Min(f0, rate * 0.45f) / rate;
                double alpha = Math.Sin(w0) / (2 * q);
                double a0 = 1 + alpha;
                b0 = (float)(alpha / a0);
                b2 = (float)(-alpha / a0);
                a1 = (float)(-2 * Math.Cos(w0) / a0);
                a2 = (float)((1 - alpha) / a0);
            }
            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}

using System;

namespace MugsTech.Background
{
    /// <summary>
    /// True peak per ITU-R BS.1770-4 Annex 2: the signal is oversampled 4× with
    /// a 48-tap interpolation filter (12 taps per phase) and the largest
    /// absolute value of the oversampled signal is the true peak — it catches
    /// the peaks that sit BETWEEN samples (a sine at a quarter of the sample
    /// rate, 45° off, has samples at −3 dB and a true peak at 0 dB).
    ///
    /// The filter is a Blackman-windowed sinc, each phase normalised to unity
    /// gain. Fed in pieces; per-channel history carries across pieces. Work is
    /// skipped (exactly — not as a guess) wherever the last 12 samples are too
    /// small for any phase to beat the peak already found: a phase's output is
    /// at most its taps' absolute sum times the window's largest sample.
    /// </summary>
    public sealed class TruePeakMeter
    {
        public const int Oversample = 4;
        public const int TapsPerPhase = 12;

        static readonly double[][] s_Phases = BuildPhases();
        static readonly double s_MaxPhaseL1 = MaxL1(s_Phases);

        readonly int channels;
        readonly double[][] history;   // per channel, ring of the last 12 input samples
        int pos;
        double peak;                   // linear, max |x| over samples and interpolated points

        public TruePeakMeter(int channels)
        {
            this.channels = Math.Max(1, channels);
            history = new double[this.channels][];
            for (int c = 0; c < this.channels; c++) history[c] = new double[TapsPerPhase];
        }

        public double PeakLinear => peak;
        public double PeakDb => peak > 0 ? 20.0 * Math.Log10(peak) : double.NegativeInfinity;

        public void Add(float[] interleaved, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    double x = interleaved[i * channels + c];
                    double[] h = history[c];
                    h[pos] = x;
                    double ax = Math.Abs(x);
                    if (ax > peak) peak = ax;

                    // Could any phase beat the peak? Bound by the window's largest |sample|.
                    double windowMax = 0;
                    for (int k = 0; k < TapsPerPhase; k++)
                    {
                        double v = Math.Abs(h[k]);
                        if (v > windowMax) windowMax = v;
                    }
                    if (windowMax * s_MaxPhaseL1 <= peak) continue;

                    for (int p = 0; p < Oversample; p++)
                    {
                        double[] taps = s_Phases[p];
                        double y = 0;
                        // taps[k] multiplies the sample k steps back
                        for (int k = 0; k < TapsPerPhase; k++)
                            y += taps[k] * h[(pos - k + TapsPerPhase) % TapsPerPhase];
                        double ay = Math.Abs(y);
                        if (ay > peak) peak = ay;
                    }
                }
                pos = (pos + 1) % TapsPerPhase;
            }
        }

        static double[][] BuildPhases()
        {
            int n = Oversample * TapsPerPhase;          // 48 taps
            double centre = (n - 1) / 2.0;
            var h = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = (i - centre) / Oversample;
                double sinc = Math.Abs(t) < 1e-12 ? 1.0 : Math.Sin(Math.PI * t) / (Math.PI * t);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
                h[i] = sinc * w;
            }
            var phases = new double[Oversample][];
            for (int p = 0; p < Oversample; p++)
            {
                phases[p] = new double[TapsPerPhase];
                double sum = 0;
                for (int k = 0; k < TapsPerPhase; k++) { phases[p][k] = h[p + Oversample * k]; sum += phases[p][k]; }
                for (int k = 0; k < TapsPerPhase; k++) phases[p][k] /= sum;   // unity gain at DC per phase
            }
            return phases;
        }

        static double MaxL1(double[][] phases)
        {
            double m = 0;
            foreach (double[] ph in phases)
            {
                double s = 0;
                foreach (double v in ph) s += Math.Abs(v);
                if (s > m) m = s;
            }
            return m;
        }
    }
}

using System;

namespace MugsTech.Background
{
    /// <summary>
    /// Integrated loudness (LUFS) measurement per ITU-R BS.1770-4 — the same
    /// quantity ffmpeg's `loudnorm print_format=json` reports as `input_i`.
    /// Used by the folder-based background-music mode to equalize every track
    /// to a common loudness target with one static gain, exactly like the
    /// Python mixer (music_mixer/add_music.py) did in post.
    ///
    /// Pipeline: K-weighting pre-filter (high-shelf + high-pass biquad
    /// cascade), 400 ms gating blocks with 75% overlap, −70 LKFS absolute
    /// gate, −10 LU relative gate. Pure math over a float[] — safe to run on
    /// a worker thread (and it should be: a 3-minute stereo track is ~16M
    /// frames, which would hitch the main thread for a noticeable moment).
    /// </summary>
    public static class LoudnessMeter
    {
        // K-weighting analog-prototype parameters (the values libebur128
        // derives the ITU 48 kHz table from). The coefficients published in
        // BS.1770 are only valid at 48 kHz; music libraries are mostly
        // 44.1 kHz, so the biquads are recomputed for the clip's actual rate.
        const double Stage1Freq = 1681.974450955533;  // high-shelf center
        const double Stage1Gain = 3.999843853973347;  // dB
        const double Stage1Q    = 0.7071752369554196;
        const double Stage2Freq = 38.13547087602444;  // high-pass corner
        const double Stage2Q    = 0.5003270373238773;

        const double AbsoluteGateLufs = -70.0;
        const double RelativeGateLu   = -10.0;
        const double MeasureOffset    = -0.691;

        struct Biquad
        {
            public double b0, b1, b2, a1, a2;
            public double x1, x2, y1, y2;

            public double Process(double x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }

        static Biquad MakeStage1(double sampleRate)
        {
            double k  = Math.Tan(Math.PI * Stage1Freq / sampleRate);
            double vh = Math.Pow(10.0, Stage1Gain / 20.0);
            double vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1.0 + k / Stage1Q + k * k;
            return new Biquad
            {
                b0 = (vh + vb * k / Stage1Q + k * k) / a0,
                b1 = 2.0 * (k * k - vh) / a0,
                b2 = (vh - vb * k / Stage1Q + k * k) / a0,
                a1 = 2.0 * (k * k - 1.0) / a0,
                a2 = (1.0 - k / Stage1Q + k * k) / a0,
            };
        }

        static Biquad MakeStage2(double sampleRate)
        {
            // The ITU table keeps the high-pass numerator at exactly {1,-2,1}
            // (passband gain ≈ 1.005, accepted by the spec) — reproduce that,
            // recomputing only the denominator for the actual sample rate.
            double k  = Math.Tan(Math.PI * Stage2Freq / sampleRate);
            double a0 = 1.0 + k / Stage2Q + k * k;
            return new Biquad
            {
                b0 = 1.0,
                b1 = -2.0,
                b2 = 1.0,
                a1 = 2.0 * (k * k - 1.0) / a0,
                a2 = (1.0 - k / Stage2Q + k * k) / a0,
            };
        }

        // Channel weights per BS.1770: L/R/C = 1.0, surround = 1.41, LFE
        // excluded. Music tracks are mono/stereo in practice; the 5.1 layout
        // (L R C LFE Ls Rs) is handled for completeness.
        static double ChannelWeight(int channel, int channelCount)
        {
            if (channelCount >= 6)
            {
                if (channel == 3) return 0.0;                      // LFE
                if (channel == 4 || channel == 5) return 1.41;     // Ls / Rs
            }
            return 1.0;
        }

        /// <summary>
        /// Integrated loudness of an interleaved sample buffer, in LUFS.
        /// Returns NaN when unmeasurable (silence, or shorter than one
        /// 400 ms gating block) — callers treat that as "mix as-is, gain 0".
        /// </summary>
        public static double MeasureIntegratedLufs(float[] interleaved, int channels, int sampleRate)
        {
            if (interleaved == null || channels <= 0 || sampleRate <= 0) return double.NaN;
            int frames = interleaved.Length / channels;

            int blockFrames = (int)Math.Round(0.400 * sampleRate);
            int hopFrames   = blockFrames / 4;   // 75% overlap
            if (blockFrames <= 0 || hopFrames <= 0 || frames < blockFrames) return double.NaN;

            var stage1 = new Biquad[channels];
            var stage2 = new Biquad[channels];
            for (int c = 0; c < channels; c++)
            {
                stage1[c] = MakeStage1(sampleRate);
                stage2[c] = MakeStage2(sampleRate);
            }

            // Energy of each 100 ms hop segment per channel; a gating block is
            // the sum of 4 consecutive hops. This streams the whole file in
            // one pass without a second filtered copy of the samples.
            int hopCount = frames / hopFrames;
            var hopEnergy = new double[channels, hopCount];

            for (int c = 0; c < channels; c++)
            {
                double sum = 0.0;
                int hop = 0, inHop = 0;
                Biquad f1 = stage1[c], f2 = stage2[c];
                for (int i = 0; i < frames && hop < hopCount; i++)
                {
                    double z = f2.Process(f1.Process(interleaved[i * channels + c]));
                    sum += z * z;
                    if (++inHop == hopFrames)
                    {
                        hopEnergy[c, hop++] = sum;
                        sum = 0.0;
                        inHop = 0;
                    }
                }
            }

            int blockCount = hopCount - 3;
            if (blockCount <= 0) return double.NaN;

            // Per-block, per-channel mean square over exactly 4 hops.
            double blockLen = 4.0 * hopFrames;
            var blockMs = new double[blockCount][];
            var blockLoudness = new double[blockCount];
            for (int j = 0; j < blockCount; j++)
            {
                var ms = new double[channels];
                double weighted = 0.0;
                for (int c = 0; c < channels; c++)
                {
                    double e = hopEnergy[c, j] + hopEnergy[c, j + 1] +
                               hopEnergy[c, j + 2] + hopEnergy[c, j + 3];
                    ms[c] = e / blockLen;
                    weighted += ChannelWeight(c, channels) * ms[c];
                }
                blockMs[j] = ms;
                blockLoudness[j] = weighted > 0.0
                    ? MeasureOffset + 10.0 * Math.Log10(weighted)
                    : double.NegativeInfinity;
            }

            // Absolute gate (−70 LKFS).
            double relThreshold = ComputeGatedLoudness(blockMs, blockLoudness, channels,
                                                       AbsoluteGateLufs, out int absCount);
            if (absCount == 0 || double.IsNaN(relThreshold)) return double.NaN;

            // Relative gate: −10 LU under the abs-gated mean.
            double integrated = ComputeGatedLoudness(blockMs, blockLoudness, channels,
                                                     relThreshold + RelativeGateLu, out int relCount);
            return relCount == 0 ? double.NaN : integrated;
        }

        // Mean loudness of all blocks whose loudness exceeds `threshold`:
        // per-channel mean squares are averaged over the surviving blocks
        // first, then weighted, summed and converted — the order BS.1770
        // prescribes (not a plain average of block loudness values).
        static double ComputeGatedLoudness(double[][] blockMs, double[] blockLoudness,
                                           int channels, double threshold, out int count)
        {
            var chSum = new double[channels];
            count = 0;
            for (int j = 0; j < blockMs.Length; j++)
            {
                if (blockLoudness[j] <= threshold) continue;
                for (int c = 0; c < channels; c++) chSum[c] += blockMs[j][c];
                count++;
            }
            if (count == 0) return double.NaN;

            double weighted = 0.0;
            for (int c = 0; c < channels; c++)
                weighted += ChannelWeight(c, channels) * (chSum[c] / count);
            return weighted > 0.0 ? MeasureOffset + 10.0 * Math.Log10(weighted) : double.NaN;
        }
    }
}

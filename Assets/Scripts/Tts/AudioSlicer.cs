using System;
using System.Collections.Generic;

namespace MugsTech.Tts
{
    /// <summary>
    /// Cuts one continuous take back into per-section audio, using the
    /// character-level alignment to find each seam.
    ///
    /// For every boundary the alignment says where section N's last character
    /// stopped and where section N+1's first character started. That window is
    /// the pause the blank line between sections bought us; the cut goes at its
    /// quietest point, so nothing audible is ever severed. Each section is then
    /// trimmed back to a small fixed pad of edge silence, because the gap
    /// between sections is Unity's to control — SegmentSequencer's
    /// interSegmentPause and transition beats must not be lengthened by
    /// whatever breath eleven_v3 happened to render at the boundary.
    ///
    /// Pure C# — no Unity types, no file IO — so the self-tests can drive it
    /// with a synthetic alignment and assert exact sample positions.
    /// </summary>
    public static class AudioSlicer
    {
        public class Options
        {
            /// <summary>Silence left in front of the first word and after the
            /// last one. Small and fixed, so Unity's own pauses set the pacing.</summary>
            public float EdgePadSeconds = 0.075f;

            /// <summary>Linear fade applied at every cut edge — without it a cut
            /// through a non-zero sample clicks.</summary>
            public float FadeSeconds = 0.010f;

            /// <summary>RMS window for the speech/silence envelope. Matches
            /// SegmentSequencer's analysis so both agree on where speech is.</summary>
            public float EnvelopeWindowSeconds = 0.02f;

            /// <summary>Finer window used only when hunting the quiet point
            /// inside a gap, where 20ms is coarser than the cut deserves.</summary>
            public float CutSearchWindowSeconds = 0.005f;

            // Speech threshold: noise floor + margin, clamped. Same numbers as
            // SegmentSequencer.AnalyzeSegmentAudio.
            public float SpanMarginDb       = 14f;
            public float SpanThresholdMinDb = -48f;
            public float SpanThresholdMaxDb = -30f;

            /// <summary>
            /// How far outside the alignment's gap to look for a real pause when
            /// the gap it points at turns out to be full of speech. eleven_v3's
            /// alignment has been measured drifting over a second on this
            /// project, and a cut placed on a drifted gap would sever a word —
            /// so the audio gets the final say on where the boundary is.
            /// </summary>
            public float BoundarySearchToleranceSeconds = 2.5f;

            /// <summary>Shortest quiet run that counts as a section pause.</summary>
            public float MinPauseSeconds = 0.12f;
        }

        /// <summary>One section's slice of the chunk.</summary>
        public class Cut
        {
            public TtsChunkAssembler.Span Span;

            public int   StartFrame, EndFrame;          // into the chunk audio
            public float StartSeconds, EndSeconds;      // chunk-global, = frames / rate

            /// <summary>Measured speech span, relative to this slice's start —
            /// what goes into manifest.json as speech_start / speech_end.</summary>
            public float SpeechStartSeconds, SpeechEndSeconds;

            public float[] Samples;                     // interleaved, fades applied
            public int     Channels, SampleRate;

            public float DurationSeconds => EndSeconds - StartSeconds;

            /// <summary>Chunk-global time -> time inside this section's file.</summary>
            public float ToLocal(float chunkTime)
            {
                float t = chunkTime - StartSeconds;
                if (t < 0f) t = 0f;
                float d = DurationSeconds;
                return t > d ? d : t;
            }
        }

        public class Result
        {
            /// <summary>One per non-overlap span, in order.</summary>
            public List<Cut>    Cuts     = new List<Cut>();
            /// <summary>Where each internal boundary landed (chunk-global seconds).</summary>
            public List<float>  Boundaries = new List<float>();

            /// <summary>Everything worth logging, including the routine rescues.</summary>
            public List<string> Warnings = new List<string>();

            /// <summary>
            /// The subset that means the OUTPUT is suspect, not just that the
            /// slicer had to work for it. A boundary moved to a real pause is
            /// the design working; a boundary with no pause anywhere near it is
            /// a take worth re-rendering. Callers that abort a run (the headless
            /// automation does) must key off this, never off Warnings.
            /// </summary>
            public List<string> Severe   = new List<string>();
        }

        // -------------------------------------------------------------------

        public static Result Split(
            WavCodec.AudioBuffer audio,
            TtsAlignment alignment,
            IList<TtsChunkAssembler.Span> spans,
            Options options = null)
        {
            options = options ?? new Options();
            var result = new Result();

            if (audio == null || audio.Samples == null || audio.Frames <= 0)
            {
                result.Warnings.Add("No audio to split.");
                return result;
            }
            if (spans == null || spans.Count == 0)
            {
                result.Warnings.Add("No spans to split on.");
                return result;
            }

            int   channels   = Math.Max(1, audio.Channels);
            int   sampleRate = Math.Max(1, audio.SampleRate);
            int   frames     = audio.Frames;
            float totalSecs  = frames / (float)sampleRate;

            // ---- 1. envelope + speech threshold for the whole chunk --------
            // Measured once over the take, so every section inherits the same
            // idea of "quiet" — sections cut from one chunk stay consistent.
            int windowFrames = Math.Max(1, (int)Math.Round(options.EnvelopeWindowSeconds * sampleRate));
            float[] windowDb = WindowDb(audio.Samples, channels, frames, windowFrames);
            float threshold  = SpeechThresholdDb(windowDb, options);

            // ---- 2. one cut per internal boundary --------------------------
            var boundarySeconds = new float[Math.Max(0, spans.Count - 1)];
            for (int i = 0; i + 1 < spans.Count; i++)
            {
                float gapStart = alignment.EndOfLastVisible(spans[i].Start, spans[i].End);
                float gapEnd   = alignment.StartOfFirstVisible(spans[i + 1].Start, spans[i + 1].End);

                boundarySeconds[i] = FindBoundary(
                    audio.Samples, channels, sampleRate, frames,
                    windowDb, windowFrames, threshold,
                    gapStart, gapEnd, options, out string note, out bool severe);

                if (note != null)
                {
                    string line = $"Boundary {Label(spans[i])} -> {Label(spans[i + 1])}: {note}";
                    result.Warnings.Add(line);
                    if (severe) result.Severe.Add(line);
                }

                result.Boundaries.Add(boundarySeconds[i]);
            }

            // ---- 3. one slice per section ---------------------------------
            int padFrames  = Math.Max(0, (int)Math.Round(options.EdgePadSeconds * sampleRate));
            int fadeFrames = Math.Max(0, (int)Math.Round(options.FadeSeconds    * sampleRate));

            for (int i = 0; i < spans.Count; i++)
            {
                float rangeStartSec = i == 0                ? 0f        : boundarySeconds[i - 1];
                float rangeEndSec   = i == spans.Count - 1  ? totalSecs : boundarySeconds[i];

                if (spans[i].IsOverlap) continue;   // seam conditioning — generated, then discarded

                int rangeStart = Clamp((int)Math.Round(rangeStartSec * sampleRate), 0, frames);
                int rangeEnd   = Clamp((int)Math.Round(rangeEndSec   * sampleRate), rangeStart, frames);

                // Trim the section back to a fixed pad around its real speech.
                FindSpeech(windowDb, windowFrames, threshold, rangeStart, rangeEnd,
                           out int firstLoud, out int lastLoud);

                int start, end;
                if (firstLoud < 0)
                {
                    string line = $"{Label(spans[i])}: no speech found in its slice " +
                                  $"({rangeStartSec:F2}s–{rangeEndSec:F2}s) — keeping the whole range.";
                    result.Warnings.Add(line);
                    result.Severe.Add(line);
                    start = rangeStart;
                    end   = rangeEnd;
                    firstLoud = rangeStart;
                    lastLoud  = rangeEnd;
                }
                else
                {
                    start = Math.Max(rangeStart, firstLoud - padFrames);
                    end   = Math.Min(rangeEnd,   lastLoud  + padFrames);
                }
                if (end <= start) end = Math.Min(frames, start + 1);

                var cut = new Cut {
                    Span         = spans[i],
                    StartFrame   = start,
                    EndFrame     = end,
                    StartSeconds = start / (float)sampleRate,
                    EndSeconds   = end   / (float)sampleRate,
                    Channels     = channels,
                    SampleRate   = sampleRate,
                    SpeechStartSeconds = (firstLoud - start) / (float)sampleRate,
                    SpeechEndSeconds   = (lastLoud  - start) / (float)sampleRate,
                    Samples      = Extract(audio.Samples, channels, start, end, fadeFrames),
                };
                result.Cuts.Add(cut);
            }

            return result;
        }

        // -------------------------------------------------------------------
        // Sample work
        // -------------------------------------------------------------------

        /// <summary>Copy [startFrame, endFrame) and fade both edges linearly.</summary>
        public static float[] Extract(
            float[] samples, int channels, int startFrame, int endFrame, int fadeFrames)
        {
            int count = Math.Max(0, endFrame - startFrame);
            var outBuf = new float[count * channels];
            Array.Copy(samples, startFrame * channels, outBuf, 0, count * channels);

            // A fade longer than half the slice would overlap itself.
            int fade = Math.Min(fadeFrames, count / 2);
            for (int f = 0; f < fade; f++)
            {
                float gain = (f + 1) / (float)(fade + 1);
                int inHead = f * channels;
                int inTail = (count - 1 - f) * channels;
                for (int c = 0; c < channels; c++)
                {
                    outBuf[inHead + c] *= gain;
                    outBuf[inTail + c] *= gain;
                }
            }
            return outBuf;
        }

        /// <summary>
        /// Where one section ends and the next begins.
        ///
        /// The alignment proposes the window; the AUDIO decides. If the window
        /// the alignment points at contains real silence, the cut goes at its
        /// quietest moment. If it doesn't — because the alignment drifted, which
        /// this model does — the nearest genuine pause within the tolerance is
        /// used instead, and <paramref name="note"/> explains the rescue.
        /// </summary>
        public static float FindBoundary(
            float[] samples, int channels, int sampleRate, int frames,
            float[] windowDb, int windowFrames, float thresholdDb,
            float gapStart, float gapEnd, Options options,
            out string note, out bool severe)
        {
            note   = null;
            severe = false;
            float totalSecs = frames / (float)sampleRate;
            gapStart = Clamp(gapStart, 0f, totalSecs);
            gapEnd   = Clamp(gapEnd,   0f, totalSecs);
            bool haveGap = gapEnd > gapStart;

            if (haveGap && HasQuietWindow(windowDb, windowFrames, sampleRate, thresholdDb, gapStart, gapEnd))
                return QuietestPoint(samples, channels, sampleRate, frames,
                                     gapStart, gapEnd, options.CutSearchWindowSeconds);

            float anchor = haveGap ? 0.5f * (gapStart + gapEnd) : gapStart;

            if (TryFindNearestPause(windowDb, windowFrames, sampleRate, thresholdDb,
                                    anchor, options, out float center, out float distance))
            {
                note = haveGap
                    ? $"the alignment's gap ({gapStart:F2}s–{gapEnd:F2}s) holds no silence — the " +
                      $"alignment has drifted. Cut moved {distance:F2}s to the real pause at {center:F2}s."
                    : $"alignment leaves no gap ({gapStart:F2}s -> {gapEnd:F2}s). Cut moved to the " +
                      $"real pause at {center:F2}s.";
                return center;
            }

            // Nothing quiet anywhere near where the section should end. Either
            // the alignment is badly wrong or the model never drew breath — both
            // mean this seam needs a human ear.
            severe = true;
            note = $"no pause found within ±{options.BoundarySearchToleranceSeconds:F1}s of " +
                   $"{anchor:F2}s — cutting on the alignment's word and relying on the fade. " +
                   "Check this seam.";
            return haveGap
                ? QuietestPoint(samples, channels, sampleRate, frames,
                                gapStart, gapEnd, options.CutSearchWindowSeconds)
                : anchor;
        }

        /// <summary>Does any envelope window in the range fall below the speech threshold?</summary>
        static bool HasQuietWindow(
            float[] windowDb, int windowFrames, int sampleRate, float thresholdDb,
            float fromSeconds, float toSeconds)
        {
            if (windowDb == null || windowDb.Length == 0) return false;
            int w0 = Clamp((int)(fromSeconds * sampleRate) / windowFrames, 0, windowDb.Length);
            int w1 = Clamp((int)Math.Ceiling(toSeconds * sampleRate / windowFrames), w0, windowDb.Length);
            for (int w = w0; w < w1; w++) if (windowDb[w] < thresholdDb) return true;
            return false;
        }

        /// <summary>
        /// Centre of the quiet run nearest <paramref name="anchorSeconds"/> that
        /// is long enough to be a real pause, within the configured tolerance.
        /// </summary>
        static bool TryFindNearestPause(
            float[] windowDb, int windowFrames, int sampleRate, float thresholdDb,
            float anchorSeconds, Options options, out float center, out float distance)
        {
            center = distance = 0f;
            if (windowDb == null || windowDb.Length == 0) return false;

            float perWindow = windowFrames / (float)sampleRate;
            int minRun = Math.Max(1, (int)Math.Round(options.MinPauseSeconds / perWindow));
            int w0 = Clamp((int)((anchorSeconds - options.BoundarySearchToleranceSeconds) / perWindow),
                           0, windowDb.Length);
            int w1 = Clamp((int)Math.Ceiling((anchorSeconds + options.BoundarySearchToleranceSeconds) / perWindow),
                           w0, windowDb.Length);

            bool  found = false;
            float best  = float.MaxValue;
            int   runStart = -1;

            for (int w = w0; w <= w1; w++)
            {
                bool quiet = w < w1 && windowDb[w] < thresholdDb;
                if (quiet) { if (runStart < 0) runStart = w; continue; }
                if (runStart < 0) continue;

                if (w - runStart >= minRun)
                {
                    float c = (runStart + w) * 0.5f * perWindow;
                    float d = Math.Abs(c - anchorSeconds);
                    if (d < best) { best = d; center = c; found = true; }
                }
                runStart = -1;
            }

            distance = found ? best : 0f;
            return found;
        }

        /// <summary>
        /// The quietest moment in [fromSeconds, toSeconds] — where a cut is
        /// least likely to clip speech or a breath tail. Falls back to the
        /// midpoint when the gap is too short to scan.
        /// </summary>
        public static float QuietestPoint(
            float[] samples, int channels, int sampleRate, int frames,
            float fromSeconds, float toSeconds, float windowSeconds)
        {
            float mid = 0.5f * (fromSeconds + toSeconds);

            int from = Clamp((int)Math.Round(fromSeconds * sampleRate), 0, frames);
            int to   = Clamp((int)Math.Round(toSeconds   * sampleRate), from, frames);
            int win  = Math.Max(1, (int)Math.Round(windowSeconds * sampleRate));
            if (to - from < win * 2) return mid;

            int windows = (to - from) / win;
            var energy  = new double[windows];
            double best = double.MaxValue;

            for (int w = 0; w < windows; w++)
            {
                double acc = 0;
                int begin = (from + w * win) * channels;
                int end   = begin + win * channels;
                for (int s = begin; s < end; s++) acc += samples[s] * (double)samples[s];
                energy[w] = acc;
                if (acc < best) best = acc;
            }

            // A rendered pause is often near-digital silence, so a whole run of
            // windows ties for quietest. Taking the first would push the cut
            // hard against the end of the previous section; take the one nearest
            // the middle of the gap instead, which leaves both sides room.
            double tolerance = best * 1.05 + 1e-9;
            float  midFrame  = 0.5f * (from + to);
            int    bestW     = -1;
            float  bestDist  = float.MaxValue;

            for (int w = 0; w < windows; w++)
            {
                if (energy[w] > tolerance) continue;
                float centre = from + (w + 0.5f) * win;
                float dist   = Math.Abs(centre - midFrame);
                if (dist < bestDist) { bestDist = dist; bestW = w; }
            }

            return bestW < 0 ? mid : (from + (bestW + 0.5f) * win) / sampleRate;
        }

        /// <summary>Per-window RMS in dBFS across all channels.</summary>
        public static float[] WindowDb(float[] samples, int channels, int frames, int windowFrames)
        {
            int count = frames / windowFrames;
            var db = new float[Math.Max(0, count)];
            for (int w = 0; w < count; w++)
            {
                int begin = w * windowFrames * channels;
                int end   = begin + windowFrames * channels;
                double acc = 0;
                for (int s = begin; s < end; s++) acc += samples[s] * (double)samples[s];
                double rms = Math.Sqrt(acc / (windowFrames * channels));
                db[w] = rms <= 1e-9 ? -120f : 20f * (float)Math.Log10(rms);
            }
            return db;
        }

        /// <summary>Noise floor (5th percentile) plus a margin, clamped.</summary>
        public static float SpeechThresholdDb(float[] windowDb, Options options)
        {
            if (windowDb == null || windowDb.Length == 0) return options.SpanThresholdMaxDb;

            var sorted = (float[])windowDb.Clone();
            Array.Sort(sorted);
            float floor = sorted[Clamp((int)Math.Round(sorted.Length * 0.05f), 0, sorted.Length - 1)];

            return Math.Min(options.SpanThresholdMaxDb,
                   Math.Max(floor + options.SpanMarginDb, options.SpanThresholdMinDb));
        }

        /// <summary>
        /// First and last frame above the speech threshold inside
        /// [startFrame, endFrame). Both are -1 when the range is silent.
        /// </summary>
        public static void FindSpeech(
            float[] windowDb, int windowFrames, float thresholdDb,
            int startFrame, int endFrame, out int firstLoud, out int lastLoud)
        {
            firstLoud = lastLoud = -1;
            if (windowDb == null || windowDb.Length == 0) return;

            int w0 = Clamp(startFrame / windowFrames, 0, windowDb.Length);
            int w1 = Clamp((endFrame + windowFrames - 1) / windowFrames, w0, windowDb.Length);

            for (int w = w0; w < w1; w++)
            {
                if (windowDb[w] < thresholdDb) continue;
                if (firstLoud < 0) firstLoud = w * windowFrames;
                lastLoud = (w + 1) * windowFrames;
            }

            if (firstLoud >= 0)
            {
                firstLoud = Math.Max(firstLoud, startFrame);
                lastLoud  = Math.Min(lastLoud,  endFrame);
            }
        }

        static string Label(TtsChunkAssembler.Span span)
            => span == null ? "?" : (span.IsOverlap ? "(overlap)" : (span.Slug ?? span.Name ?? "?"));

        static int   Clamp(int v, int lo, int hi)       => v < lo ? lo : (v > hi ? hi : v);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}

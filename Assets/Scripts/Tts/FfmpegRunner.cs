using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MugsTech.Tts
{
    /// <summary>
    /// External ffmpeg, driven as a coroutine so the app stays responsive.
    ///
    /// Three jobs in the chunked pipeline:
    ///   * decode — when the account's plan refuses raw PCM and the take comes
    ///     back as mp3, ffmpeg turns it into samples we can cut;
    ///   * loudness — a two-pass <c>loudnorm</c> per CHUNK (never per section:
    ///     normalising sections individually would flatten the quiet beats the
    ///     script deliberately writes in) in linear mode, so it is one uniform
    ///     gain rather than compression;
    ///   * encode — the per-section files go out as mp3 because that is what
    ///     ScriptFileReader and SegmentSequencer load.
    ///
    /// Every step degrades rather than fails: without ffmpeg the pipeline
    /// decodes through Unity, matches loudness with a plain RMS gain, and
    /// writes WAV sections (both loaders now accept either extension).
    /// </summary>
    public static class FfmpegRunner
    {
        /// <summary>Configurable path to the executable; empty means "on PATH".</summary>
        public const string FfmpegPathPrefKey = "AutoAvatarGen.FfmpegPath";

        public static string ExecutablePath
        {
            get
            {
                string p = PlayerPrefs.GetString(FfmpegPathPrefKey, "");
                return string.IsNullOrWhiteSpace(p) ? "ffmpeg" : p.Trim();
            }
            set => PlayerPrefs.SetString(FfmpegPathPrefKey, value ?? "");
        }

        // Verify() starts a process, so the answer is cached for the session.
        // Reset it after changing ExecutablePath.
        static bool? _available;

        /// <summary>Cached result of the last <see cref="Verify"/>.</summary>
        public static bool IsAvailable
        {
            get
            {
                if (!_available.HasValue) _available = Verify(out _, out _);
                return _available.Value;
            }
        }

        public static void ForgetAvailability() => _available = null;

        /// <summary>
        /// Is ffmpeg reachable? Checked when the tool opens so a missing binary
        /// is reported before a run spends credits, not after.
        /// </summary>
        public static bool Verify(out string version, out string error)
        {
            version = error = null;
            try
            {
                var (code, stdout, stderr) = RunBlocking("-hide_banner -version", 10000);
                if (code != 0)
                {
                    error = $"'{ExecutablePath} -version' exited {code}. {Head(stderr, 200)}";
                    _available = false;
                    return false;
                }
                version = Head(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout, 120).Trim();
                _available = true;
                return true;
            }
            catch (Exception e)
            {
                error = $"Could not start '{ExecutablePath}': {e.Message}. " +
                        "Install ffmpeg or set its full path in the TTS settings " +
                        $"(PlayerPref '{FfmpegPathPrefKey}').";
                _available = false;
                return false;
            }
        }

        // -------------------------------------------------------------------
        // Operations
        // -------------------------------------------------------------------

        /// <summary>Decode anything ffmpeg reads into 16-bit WAV at the given rate.</summary>
        public static IEnumerator DecodeToWav(
            string inputPath, string outputWav, int sampleRate, Action<bool, string> onDone)
        {
            string args = $"-hide_banner -nostdin -y -i {Q(inputPath)} " +
                          $"-vn -acodec pcm_s16le -ar {sampleRate} {Q(outputWav)}";
            yield return Run(args, 300000, (code, _, stderr) =>
                onDone?.Invoke(code == 0 && File.Exists(outputWav),
                               code == 0 ? null : Head(stderr, 400)));
        }

        /// <summary>Encode a WAV to constant-bitrate mp3 — the format the
        /// per-section contract has always used.</summary>
        public static IEnumerator EncodeMp3(
            string inputWav, string outputMp3, int kbps, Action<bool, string> onDone)
        {
            string args = $"-hide_banner -nostdin -y -i {Q(inputWav)} " +
                          $"-codec:a libmp3lame -b:a {kbps}k {Q(outputMp3)}";
            yield return Run(args, 300000, (code, _, stderr) =>
                onDone?.Invoke(code == 0 && File.Exists(outputMp3),
                               code == 0 ? null : Head(stderr, 400)));
        }

        public class LoudnormResult
        {
            public bool   Success;
            public string Error;
            public float  MeasuredLufs;
            public float  AppliedGainDb;      // target - measured, i.e. the uniform gain
            public bool   WasLinear;          // false = ffmpeg fell back to dynamic mode
        }

        /// <summary>
        /// Two-pass <c>loudnorm</c> to <paramref name="targetLufs"/>. Pass one
        /// measures, pass two applies with <c>linear=true</c> so the whole chunk
        /// moves by a single gain and the performance's own dynamics survive.
        /// </summary>
        public static IEnumerator LoudnormTwoPass(
            string inputWav, string outputWav, float targetLufs, Action<LoudnormResult> onDone)
        {
            var outcome = new LoudnormResult();
            string filterBase = $"I={F(targetLufs)}:TP=-1.5:LRA=11";

            // ---- pass 1: measure ------------------------------------------
            string measureJson = null;
            string measureErr  = null;
            yield return Run(
                $"-hide_banner -nostdin -y -i {Q(inputWav)} " +
                $"-af loudnorm={filterBase}:print_format=json -f null -",
                300000,
                (code, _, stderr) =>
                {
                    if (code != 0) measureErr = Head(stderr, 400);
                    else           measureJson = ExtractLastJsonObject(stderr);
                });

            if (measureErr != null)
            {
                outcome.Error = "loudnorm measure pass failed: " + measureErr;
                onDone?.Invoke(outcome);
                yield break;
            }

            var stats = ParseLoudnormStats(measureJson);
            if (stats == null)
            {
                outcome.Error = "Could not read loudnorm's measurement output.";
                onDone?.Invoke(outcome);
                yield break;
            }

            outcome.MeasuredLufs  = stats.InputI;
            outcome.AppliedGainDb = targetLufs - stats.InputI;

            // ---- pass 2: apply --------------------------------------------
            string applyErr = null;
            string applyLog = null;
            yield return Run(
                $"-hide_banner -nostdin -y -i {Q(inputWav)} " +
                $"-af loudnorm={filterBase}" +
                $":measured_I={F(stats.InputI)}:measured_TP={F(stats.InputTp)}" +
                $":measured_LRA={F(stats.InputLra)}:measured_thresh={F(stats.InputThresh)}" +
                $":offset={F(stats.TargetOffset)}:linear=true:print_format=summary " +
                $"-ar 44100 {Q(outputWav)}",
                300000,
                (code, _, stderr) =>
                {
                    if (code != 0) applyErr = Head(stderr, 400);
                    else           applyLog = stderr;
                });

            if (applyErr != null || !File.Exists(outputWav))
            {
                outcome.Error = "loudnorm apply pass failed: " + (applyErr ?? "no output written");
                onDone?.Invoke(outcome);
                yield break;
            }

            // ffmpeg silently switches to dynamic normalisation when the
            // measured range can't be hit with one gain. Worth knowing: dynamic
            // mode is exactly the compression we asked it not to do.
            outcome.WasLinear = applyLog == null ||
                                applyLog.IndexOf("normalization_type: dynamic",
                                                 StringComparison.OrdinalIgnoreCase) < 0;
            outcome.Success = true;
            onDone?.Invoke(outcome);
        }

        // -------------------------------------------------------------------
        // Pure-C# fallback for machines without ffmpeg
        // -------------------------------------------------------------------

        /// <summary>
        /// Uniform gain bringing the chunk's SPEECH rms to
        /// <paramref name="targetDbFs"/>. Silence is excluded from the
        /// measurement — otherwise a chunk with longer pauses reads as quieter
        /// and gets pushed louder than its neighbour, which is the exact
        /// mismatch this is meant to remove. Backed off if it would clip.
        /// </summary>
        public static float RmsMatchGain(
            float[] samples, int channels, int sampleRate, float targetDbFs)
        {
            if (samples == null || samples.Length == 0) return 1f;

            int frames       = samples.Length / Math.Max(1, channels);
            int windowFrames = Math.Max(1, (int)Math.Round(0.02f * sampleRate));
            float[] db       = AudioSlicer.WindowDb(samples, channels, frames, windowFrames);
            if (db.Length == 0) return 1f;

            float threshold = AudioSlicer.SpeechThresholdDb(db, new AudioSlicer.Options());

            double acc = 0; long counted = 0;
            for (int w = 0; w < db.Length; w++)
            {
                if (db[w] < threshold) continue;
                int begin = w * windowFrames * channels;
                int end   = begin + windowFrames * channels;
                for (int s = begin; s < end; s++) acc += samples[s] * (double)samples[s];
                counted += windowFrames * channels;
            }
            if (counted == 0) return 1f;

            double rms = Math.Sqrt(acc / counted);
            if (rms <= 1e-9) return 1f;

            float gain = (float)(Math.Pow(10.0, targetDbFs / 20.0) / rms);

            float peak = 0f;
            foreach (float s in samples) { float a = Math.Abs(s); if (a > peak) peak = a; }
            if (peak * gain > 0.99f) gain = peak > 0f ? 0.99f / peak : gain;

            return gain;
        }

        public static void ApplyGain(float[] samples, float gain)
        {
            if (samples == null || Math.Abs(gain - 1f) < 1e-4f) return;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i] * gain;
                samples[i] = v < -1f ? -1f : (v > 1f ? 1f : v);
            }
        }

        // -------------------------------------------------------------------
        // Process plumbing
        // -------------------------------------------------------------------

        /// <summary>
        /// Run ffmpeg, yielding until it exits. stdout/stderr are drained on
        /// background threads — a filled pipe buffer would deadlock the child.
        /// </summary>
        public static IEnumerator Run(
            string arguments, int timeoutMs, Action<int, string, string> onDone)
        {
            Process proc;
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                proc = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName               = ExecutablePath,
                        Arguments              = arguments,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                    }
                };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                onDone?.Invoke(-1, "", $"Could not start '{ExecutablePath}': {e.Message}");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + timeoutMs / 1000f;
            while (!proc.HasExited)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    try { proc.Kill(); } catch { /* already gone */ }
                    onDone?.Invoke(-1, stdout.ToString(),
                        $"ffmpeg timed out after {timeoutMs / 1000}s.");
                    proc.Dispose();
                    yield break;
                }
                yield return null;
            }

            // Give the async readers a frame to flush the tail of the output.
            yield return null;

            int exitCode = proc.ExitCode;
            string o, e2;
            lock (stdout) o  = stdout.ToString();
            lock (stderr) e2 = stderr.ToString();
            proc.Dispose();

            onDone?.Invoke(exitCode, o, e2);
        }

        static (int code, string stdout, string stderr) RunBlocking(string arguments, int timeoutMs)
        {
            using (var proc = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName               = ExecutablePath,
                    Arguments              = arguments,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                }
            })
            {
                proc.Start();
                string o = proc.StandardOutput.ReadToEnd();
                string e = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return (-1, o, "timed out");
                }
                return (proc.ExitCode, o, e);
            }
        }

        // -------------------------------------------------------------------
        // loudnorm's measurement block
        // -------------------------------------------------------------------

        class LoudnormStats
        {
            public float InputI, InputTp, InputLra, InputThresh, TargetOffset;
        }

        // print_format=json writes every value as a JSON *string*, so a plain
        // string-field DTO round-trips through JsonUtility unchanged.
        [Serializable]
        class LoudnormJson
        {
            public string input_i;
            public string input_tp;
            public string input_lra;
            public string input_thresh;
            public string target_offset;
        }

        static LoudnormStats ParseLoudnormStats(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var j = JsonUtility.FromJson<LoudnormJson>(json);
                if (j == null || j.input_i == null) return null;

                return new LoudnormStats {
                    InputI       = ParseFloat(j.input_i),
                    InputTp      = ParseFloat(j.input_tp),
                    InputLra     = ParseFloat(j.input_lra),
                    InputThresh  = ParseFloat(j.input_thresh),
                    TargetOffset = ParseFloat(j.target_offset),
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ffmpeg] Could not parse loudnorm stats: {e.Message}");
                return null;
            }
        }

        // loudnorm reports "-inf" for a silent input; treat it as very quiet
        // rather than letting a NaN poison the gain.
        static float ParseFloat(string s)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return (s ?? "").Contains("-inf") ? -99f : 0f;
        }

        /// <summary>The last {...} block in ffmpeg's stderr — loudnorm prints
        /// its measurements after all the progress noise.</summary>
        static string ExtractLastJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int close = text.LastIndexOf('}');
            if (close < 0) return null;
            int open = text.LastIndexOf('{', close);
            return open < 0 ? null : text.Substring(open, close - open + 1);
        }

        static string Q(string path) => "\"" + (path ?? "").Replace("\"", "\\\"") + "\"";
        static string F(float v)     => v.ToString("0.####", CultureInfo.InvariantCulture);

        static string Head(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}

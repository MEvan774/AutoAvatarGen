using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MugsTech.Tts
{
    /// <summary>
    /// Runs the full TTS pipeline for one script — splits into segments,
    /// renders each through ElevenLabs, writes mp3 + _timed.txt + manifest.
    /// Mirrors the Python <c>main()</c> + <c>process_segment()</c> flow.
    ///
    /// Owned by <see cref="TtsPanelController"/>. Reports progress, status
    /// text, and final result via Action callbacks so the UI never reaches
    /// into job internals.
    /// </summary>
    public class TtsGenerationJob
    {
        public class Config
        {
            public string ApiKey;
            public string OutputFolder;          // absolute or relative-to-Application.dataPath
            public string ScriptText;
            public bool   DryRun;
            public string VoiceId = ElevenLabsClient.DefaultVoiceId;
            public string ModelId = ElevenLabsClient.DefaultModelId;
            public ElevenLabsClient.VoiceSettings VoiceSettings
                = new ElevenLabsClient.VoiceSettings();

            // How many times to re-render a segment whose alignment came back
            // stalled (see DescribeAlignmentStall). eleven_v3 is non-deterministic,
            // so a straight retry of the same text usually comes back clean — but
            // each one costs characters, hence a small cap.
            public int MaxStallRetries = 2;

            // Measure word times against the rendered audio instead of trusting
            // the synthesis alignment. Needs the key's forced_alignment
            // permission; falls back automatically when it isn't granted.
            public bool UseForcedAlignment = true;

            /// <summary>Settings for the chunked pipeline (the default path).</summary>
            public ChunkedOptions Chunked = new ChunkedOptions();
        }

        /// <summary>
        /// The chunked pipeline: render a couple of long takes and slice them
        /// into the per-section files, instead of one API call per section.
        /// See <see cref="ChunkedTtsGenerationJob"/> for why.
        /// </summary>
        public class ChunkedOptions
        {
            /// <summary>Off = the original one-request-per-section path, kept as
            /// a fallback.</summary>
            public bool Enabled = true;

            public TtsChunkAssembler.Options Chunking = new TtsChunkAssembler.Options();
            public AudioSlicer.Options       Slicing  = new AudioSlicer.Options();

            /// <summary>One seed per video, sent on every chunk. Null generates
            /// one and records it in render_manifest.json.</summary>
            public int? Seed;

            /// <summary>v3 takes three discrete values; Creative keeps the
            /// performance the show is written for.</summary>
            public float Stability = ElevenLabsClient.StabilityCreative;

            /// <summary>Raw PCM is lossless and needs no decoder, but is
            /// plan-gated — the client falls back to mp3 by itself.</summary>
            public string PreferredOutputFormat = ElevenLabsClient.OutputFormatPcm44100;

            public bool  UseFfmpeg  = true;
            public float TargetLufs = -16f;
            public int   Mp3Kbps    = 128;

            /// <summary>Folder of a previous run's <c>chunks/</c> output. When a
            /// dry run points at one, the whole slice runs off the cached audio
            /// and alignment with no API call.</summary>
            public string DryRunCacheFolder;
        }

        public class Result
        {
            public bool   Success;
            public string ErrorMessage;
            public int    SegmentsProcessed;
            public int    SegmentsTotal;
            public string ManifestPath;
            public bool   WasDryRun;

            // Segments still stalled after every retry. Non-empty means the audio
            // was written but its markers won't line up — the run needs redoing.
            public List<string> StalledSegments = new List<string>();
        }

        readonly Config cfg;
        readonly Action<float>  onProgress;   // 0–1
        readonly Action<string> onStatus;     // human-readable line
        readonly Action<Result> onComplete;

        public TtsGenerationJob(Config c,
            Action<float> progress, Action<string> status, Action<Result> complete)
        {
            cfg        = c ?? throw new ArgumentNullException(nameof(c));
            onProgress = progress;
            onStatus   = status;
            onComplete = complete;
        }

        /// <summary>
        /// Drive the job. Designed to be StartCoroutine'd on a MonoBehaviour
        /// so the UI stays responsive while the API call is in flight.
        /// </summary>
        public IEnumerator Run()
        {
            // The chunked pipeline is the default: it renders the voice as a
            // couple of long takes and slices them back into the same
            // per-section files this method used to write one request at a
            // time. Flipping Chunked.Enabled off falls back to that older path.
            if (cfg.Chunked != null && cfg.Chunked.Enabled)
                return new ChunkedTtsGenerationJob(cfg, onProgress, onStatus, onComplete).Run();

            return RunPerSection();
        }

        /// <summary>
        /// The original path: one ElevenLabs request per <c>## SECTION</c>.
        /// Kept as a fallback — it is what the chunked pipeline replaced
        /// because v3 re-rolls the voice on every generation.
        /// </summary>
        IEnumerator RunPerSection()
        {
            // ---- validate ------------------------------------------------
            if (string.IsNullOrWhiteSpace(cfg.ScriptText))
            {
                Finish(false, "Script is empty.");
                yield break;
            }
            if (!cfg.DryRun && string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                Finish(false, "No ElevenLabs API key set. Open the API Key popup first.");
                yield break;
            }

            string outDir = ResolveOutputFolder(cfg.OutputFolder);
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Finish(false, $"Cannot create output folder '{outDir}': {e.Message}");
                yield break;
            }

            // ---- split ---------------------------------------------------
            var segments = TtsScriptProcessor.SplitIntoSegments(cfg.ScriptText);
            if (segments.Count == 0)
            {
                Finish(false, "No segments found in script (expected `## SECTION NAME` headings).");
                yield break;
            }

            AssignOrderAndSlugs(segments);

            Report(0f, $"Parsed {segments.Count} segment(s) from script.");

            // ---- run each segment ---------------------------------------
            var manifestEntries = new List<ManifestEntry>(segments.Count);
            var stalledSegments = new List<string>();
            int done = 0;
            foreach (var seg in segments)
            {
                Report(SegmentProgress(done, 0f, segments.Count),
                    $"[{seg.Slug}] ## {seg.Name}");

                var (markers, clean) = TtsScriptProcessor.ExtractMarkers(seg.Raw);
                Report(SegmentProgress(done, 0.05f, segments.Count),
                    $"[{seg.Slug}] {markers.Count} markers, {clean.Length} clean chars");

                if (cfg.DryRun)
                {
                    // Dry-run: log what WOULD be sent and skip the API call.
                    foreach (var m in markers)
                    {
                        Debug.Log($"[DryRun:{seg.Slug}] clean_idx={m.CleanIndex}  {Trunc(m.Text, 60)}");
                    }
                    done++;
                    Report(SegmentProgress(done, 0f, segments.Count),
                        $"[{seg.Slug}] dry-run OK (no API call).");
                    continue;
                }

                // ---- TTS round-trip (re-rendered on a stalled alignment) --
                //
                // eleven_v3 occasionally keeps generating audio that its own
                // alignment doesn't describe — one "word" ends up spanning tens
                // of seconds and every marker after it lands on the wrong moment.
                // It's non-deterministic, so the same text usually comes back
                // clean on a second attempt; retry rather than ship a take whose
                // cards drift out of sync.
                //
                // The synthesis alignment is unreliable even when it doesn't
                // stall (measured ±1.3s mid-segment, ~2s at the tail), so each
                // render is followed by a forced-alignment pass that measures
                // word times on the ACTUAL audio. When the API key lacks the
                // forced_alignment permission the synthesis alignment is kept,
                // cross-checked against the audio length (a cheap way to catch
                // the tail truncation that made the stitcher cut real speech
                // ahead of section transitions).
                ElevenLabsClient.TtsResult tts = null;
                string ttsError    = null;
                string stall       = null;
                int    maxAttempts = Math.Max(1, cfg.MaxStallRetries + 1);

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    tts      = null;
                    ttsError = null;

                    int    doneCapture  = done;
                    int    totalCapture = segments.Count;
                    string retryNote    = attempt == 1
                        ? "" : $" (retry {attempt - 1}/{maxAttempts - 1})";

                    yield return CoroutineHost.Instance.StartCoroutine(
                        ElevenLabsClient.GenerateTts(
                            clean, cfg.VoiceId, cfg.ModelId, cfg.VoiceSettings, cfg.ApiKey,
                            onProgress: p => Report(
                                SegmentProgress(doneCapture, 0.10f + 0.80f * p, totalCapture),
                                $"[{seg.Slug}] uploading / receiving…{retryNote} {(int)(p * 100f)}%"),
                            onSuccess: r => tts      = r,
                            onError:   e => ttsError = e));

                    if (ttsError != null)
                    {
                        Finish(false, ttsError);
                        yield break;
                    }

                    // Replace the synthesis word times with ones measured on the
                    // rendered audio, when the account allows it.
                    bool usedForcedAlignment = false;
                    if (!_forcedAlignmentUnavailable)
                    {
                        List<TtsScriptProcessor.WordTimestamp> fa = null;
                        string faError = null;

                        Report(SegmentProgress(done, 0.91f, segments.Count),
                            $"[{seg.Slug}] aligning against the rendered audio…");
                        yield return CoroutineHost.Instance.StartCoroutine(
                            ElevenLabsClient.GetForcedAlignment(
                                tts.AudioBytes, clean, cfg.ApiKey,
                                ok => fa = ok, e => faError = e));

                        if (fa != null && fa.Count > 0)
                        {
                            tts.WordTimestamps = fa;
                            usedForcedAlignment = true;
                        }
                        else if (faError != null && faError.Contains("missing_permissions"))
                        {
                            _forcedAlignmentUnavailable = true;
                            Debug.LogWarning(
                                "[Tts] Forced alignment is unavailable: the ElevenLabs API key " +
                                "lacks the 'forced_alignment' permission. Marker timings will use " +
                                "the less accurate synthesis alignment. To fix: ElevenLabs " +
                                "dashboard → API Keys → enable Forced Alignment for this key.");
                        }
                        else if (faError != null)
                        {
                            Debug.LogWarning($"[Tts:{seg.Slug}] Forced alignment failed " +
                                             $"({Trunc(faError, 200)}) — using synthesis alignment.");
                        }
                    }

                    stall = DescribeAlignmentStall(tts.WordTimestamps);
                    if (stall == null && !usedForcedAlignment)
                        stall = DescribeTailMismatch(tts.AudioBytes, tts.WordTimestamps);
                    if (stall == null) break;          // clean render — keep it

                    Debug.LogWarning($"[Tts:{seg.Slug}] {stall}");
                    if (attempt < maxAttempts)
                        Report(SegmentProgress(done, 0.92f, segments.Count),
                            $"[{seg.Slug}] ⚠ bad alignment — re-rendering " +
                            $"({attempt}/{maxAttempts - 1})…");
                }

                // Out of retries and still stalled: write it (so it can be
                // inspected) but remember it, so the run doesn't end looking green.
                if (stall != null)
                {
                    stalledSegments.Add(seg.Slug);
                    Report(SegmentProgress(done, 0.92f, segments.Count),
                        $"[{seg.Slug}] ⚠ {stall}");
                }

                // ---- map + rebuild + write -------------------------------
                var timedMarkers = TtsScriptProcessor.MapMarkersToTimestamps(
                    markers, tts.WordTimestamps, clean);
                string timedScript = TtsScriptProcessor.RebuildTimedScript(seg.Raw, timedMarkers);

                string audioPath  = Path.Combine(outDir, seg.Slug + ".mp3");
                string scriptPath = Path.Combine(outDir, seg.Slug + "_timed.txt");
                string wordsDir   = Path.Combine(outDir, "word_timestamps");
                string wordsPath  = Path.Combine(wordsDir, seg.Slug + "_words.json");

                try
                {
                    Directory.CreateDirectory(wordsDir);
                    File.WriteAllBytes(audioPath, tts.AudioBytes);
                    File.WriteAllText (scriptPath, timedScript, new UTF8Encoding(false));
                    File.WriteAllText (wordsPath,  WordsJson(tts.WordTimestamps), new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Finish(false, $"[{seg.Slug}] write failed: {e.Message}");
                    yield break;
                }

                float dur          = tts.WordTimestamps.Count > 0
                    ? tts.WordTimestamps[tts.WordTimestamps.Count - 1].End : 0f;
                float speechStart  = tts.WordTimestamps.Count > 0
                    ? tts.WordTimestamps[0].Start : 0f;
                float speechEnd    = dur;

                manifestEntries.Add(new ManifestEntry {
                    order        = seg.Order,
                    slug         = seg.Slug,
                    name         = seg.Name,
                    audio_file   = seg.Slug + ".mp3",
                    script_file  = seg.Slug + "_timed.txt",
                    duration     = (float)Math.Round(dur, 3),
                    speech_start = (float)Math.Round(speechStart, 3),
                    speech_end   = (float)Math.Round(speechEnd, 3),
                });

                done++;
                Report(SegmentProgress(done, 0f, segments.Count),
                    $"[{seg.Slug}] saved ({dur:F1}s, {markers.Count} markers).");
            }

            // ---- manifest ------------------------------------------------
            string manifestPath = null;
            if (!cfg.DryRun)
            {
                manifestPath = Path.Combine(outDir, "manifest.json");
                try
                {
                    File.WriteAllText(manifestPath,
                        BuildManifestJson(manifestEntries),
                        new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Finish(false, $"Manifest write failed: {e.Message}");
                    yield break;
                }
            }

            Report(1f, cfg.DryRun
                ? $"Dry run complete — {segments.Count} segment(s) parsed, no API calls."
                : stalledSegments.Count > 0
                    ? $"Saved to {outDir}, but {string.Join(", ", stalledSegments)} " +
                      "still stalled after retrying — re-run before recording."
                    : $"Done — {segments.Count} segment(s) saved to {outDir}");

            onComplete?.Invoke(new Result {
                Success           = true,
                SegmentsProcessed = done,
                SegmentsTotal     = segments.Count,
                ManifestPath      = manifestPath,
                WasDryRun         = cfg.DryRun,
                StalledSegments   = stalledSegments,
            });
        }

        // ---- helpers -----------------------------------------------------

        /// <summary>
        /// Multi-segment runs get an order-prefixed slug so Unity can pick them
        /// up in playback order. Single-segment keeps the bare slug (back-compat
        /// with Python behaviour). These slugs become the output filenames, so
        /// both pipelines assign them the same way.
        /// </summary>
        public static void AssignOrderAndSlugs(List<TtsScriptProcessor.Segment> segments)
        {
            if (segments == null || segments.Count == 0) return;

            if (segments.Count == 1) { segments[0].Order = 1; return; }

            int width = Math.Max(2, segments.Count.ToString().Length);
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].Order = i + 1;
                segments[i].Slug  = (i + 1).ToString("D" + width) + "_" + segments[i].Slug;
            }
        }

        // A spoken word is never this long. When one is, ElevenLabs stalled
        // mid-render: the audio keeps going (it is NOT silence — the stretch
        // measures the same loudness as speech) but the alignment attributes the
        // whole thing to a single word, so the narration and every T= after it
        // stop describing the same timeline. It's intermittent — the same
        // segment re-renders fine — so the only defence is to say so loudly
        // instead of letting a broken take reach the recorder.
        const float StallWordSeconds = 3f;

        // Once the API answers 401 missing_permissions for forced alignment, the
        // rest of the run (and session) skips the call instead of paying a
        // round-trip per segment to be told no again.
        static bool _forcedAlignmentUnavailable;

        // How much longer the audio may run past the alignment's last word
        // before the alignment is declared wrong. On a real generation the
        // synthesis alignment once ended 1.98s before the audio did — the
        // stitcher then trimmed at "speech_end" and cut the section's last
        // words right where a transition's silence begins.
        const float TailMismatchSeconds = 1.5f;

        // The TTS endpoint's default output is mp3_44100_128 — constant 128kbps,
        // so byte length is a ±1% duration estimate with no decode needed.
        static string DescribeTailMismatch(byte[] audioBytes, List<TtsScriptProcessor.WordTimestamp> words)
        {
            if (audioBytes == null || words == null || words.Count == 0) return null;

            float estimatedSeconds = audioBytes.Length * 8f / 128000f;
            if (estimatedSeconds < 5f) return null; // too short for the estimate to mean much

            float alignEnd = words[words.Count - 1].End;
            float overrun  = estimatedSeconds - alignEnd;
            if (overrun <= TailMismatchSeconds) return null;

            return $"Alignment ends {overrun:F1}s before the audio does " +
                   $"(last word at {alignEnd:F1}s, audio ≈{estimatedSeconds:F1}s). " +
                   "Markers after the drift point will not match the narration. Re-rendering.";
        }

        public static string DescribeAlignmentStall(List<TtsScriptProcessor.WordTimestamp> words)
        {
            if (words == null) return null;

            TtsScriptProcessor.WordTimestamp worst = null;
            float worstSpan = 0f;
            foreach (var w in words)
            {
                float span = w.End - w.Start;
                if (span > worstSpan) { worstSpan = span; worst = w; }
            }

            if (worst == null || worstSpan < StallWordSeconds) return null;

            return $"ElevenLabs stalled — '{worst.Word}' spans {worstSpan:F1}s "
                 + $"({worst.Start:F1}s → {worst.End:F1}s). Cards and emotions after "
                 + "that point will not match the narration. Re-render this script.";
        }

        // Overall progress = completed segments + fraction of in-flight one.
        static float SegmentProgress(int done, float current, int total)
            => total <= 0 ? 0f : Mathf.Clamp01((done + current) / total);

        void Report(float p, string msg)
        {
            onProgress?.Invoke(p);
            if (!string.IsNullOrEmpty(msg)) onStatus?.Invoke(msg);
        }

        void Finish(bool ok, string err)
        {
            onComplete?.Invoke(new Result {
                Success      = ok,
                ErrorMessage = err,
            });
        }

        // Same path-resolution rule as MainMenuController.OnPathBrowseClicked:
        // absolute paths pass through, relative paths anchor at Application.dataPath
        // so "Python/output" lands inside the Assets folder.
        public static string ResolveOutputFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return Path.Combine(Application.dataPath, "Python", "output");
            string trimmed = folder.Trim();
            return Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(Application.dataPath, trimmed);
        }

        static string Trunc(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        // ---- JSON emitters -----------------------------------------------
        //
        // JsonUtility can't serialise List<float>/List<string> fields with the
        // indentation we want for human-readable files, and it can't emit
        // generic top-level arrays. The two writers below match the Python
        // output byte-for-byte-ish (indent=2 style), so existing consumers
        // (manifest readers, debugging tools) stay compatible.

        [Serializable] public class ManifestEntry
        {
            public int    order;
            public string slug;
            public string name;
            public string audio_file;
            public string script_file;
            public float  duration;
            public float  speech_start;
            public float  speech_end;
        }

        public static string BuildManifestJson(List<ManifestEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"segments\": [\n");
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.Append("    {\n");
                sb.Append("      \"order\": ").Append(e.order).Append(",\n");
                sb.Append("      \"slug\": ").Append(JsonString(e.slug)).Append(",\n");
                sb.Append("      \"name\": ").Append(JsonString(e.name)).Append(",\n");
                sb.Append("      \"audio_file\": ").Append(JsonString(e.audio_file)).Append(",\n");
                sb.Append("      \"script_file\": ").Append(JsonString(e.script_file)).Append(",\n");
                sb.Append("      \"duration\": ").Append(F(e.duration)).Append(",\n");
                sb.Append("      \"speech_start\": ").Append(F(e.speech_start)).Append(",\n");
                sb.Append("      \"speech_end\": ").Append(F(e.speech_end)).Append("\n");
                sb.Append("    }").Append(i + 1 < entries.Count ? "," : "").Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        public static string WordsJson(List<TtsScriptProcessor.WordTimestamp> words)
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                sb.Append("  {\n");
                sb.Append("    \"word\": ").Append(JsonString(w.Word)).Append(",\n");
                sb.Append("    \"start\": ").Append(F(w.Start)).Append(",\n");
                sb.Append("    \"end\": ").Append(F(w.End)).Append("\n");
                sb.Append("  }").Append(i + 1 < words.Count ? "," : "").Append('\n');
            }
            sb.Append("]\n");
            return sb.ToString();
        }

        public static string JsonString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 8).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else          sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Hidden persistent MonoBehaviour that hosts the API-call coroutine so
    /// the job survives if the UI panel is closed mid-render. Lazy-created.
    /// </summary>
    public class CoroutineHost : MonoBehaviour
    {
        static CoroutineHost _instance;
        public static CoroutineHost Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[TtsCoroutineHost]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<CoroutineHost>();
                return _instance;
            }
        }
    }
}

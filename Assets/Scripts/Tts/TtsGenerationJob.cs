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
        }

        public class Result
        {
            public bool   Success;
            public string ErrorMessage;
            public int    SegmentsProcessed;
            public int    SegmentsTotal;
            public string ManifestPath;
            public bool   WasDryRun;
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

            // Multi-segment runs get an order-prefixed slug so Unity can pick
            // them up in playback order. Single-segment keeps the bare slug
            // (back-compat with Python behaviour).
            if (segments.Count > 1)
            {
                int width = Math.Max(2, segments.Count.ToString().Length);
                for (int i = 0; i < segments.Count; i++)
                {
                    segments[i].Order = i + 1;
                    segments[i].Slug  = (i + 1).ToString("D" + width)
                                      + "_" + segments[i].Slug;
                }
            }
            else
            {
                segments[0].Order = 1;
            }

            Report(0f, $"Parsed {segments.Count} segment(s) from script.");

            // ---- run each segment ---------------------------------------
            var manifestEntries = new List<ManifestEntry>(segments.Count);
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

                // ---- TTS round-trip --------------------------------------
                ElevenLabsClient.TtsResult tts = null;
                string ttsError = null;
                int doneCapture = done;
                int totalCapture = segments.Count;

                yield return CoroutineHost.Instance.StartCoroutine(
                    ElevenLabsClient.GenerateTts(
                        clean, cfg.VoiceId, cfg.ModelId, cfg.VoiceSettings, cfg.ApiKey,
                        onProgress: p => Report(
                            SegmentProgress(doneCapture, 0.10f + 0.80f * p, totalCapture),
                            $"[{seg.Slug}] uploading / receiving… {(int)(p * 100f)}%"),
                        onSuccess: r => tts      = r,
                        onError:   e => ttsError = e));

                if (ttsError != null)
                {
                    Finish(false, ttsError);
                    yield break;
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
                : $"Done — {segments.Count} segment(s) saved to {outDir}");

            onComplete?.Invoke(new Result {
                Success           = true,
                SegmentsProcessed = done,
                SegmentsTotal     = segments.Count,
                ManifestPath      = manifestPath,
                WasDryRun         = cfg.DryRun,
            });
        }

        // ---- helpers -----------------------------------------------------

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

        [Serializable] class ManifestEntry
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

        static string BuildManifestJson(List<ManifestEntry> entries)
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

        static string WordsJson(List<TtsScriptProcessor.WordTimestamp> words)
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

        static string JsonString(string s)
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

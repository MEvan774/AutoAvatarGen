using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MugsTech.Tts
{
    /// <summary>
    /// The chunked TTS pipeline: render the voice as a couple of LONG takes,
    /// then cut the audio back into the per-section files everything
    /// downstream already consumes.
    ///
    /// WHY: eleven_v3 is non-deterministic between generations, so one request
    /// per section gave every section a slightly different vocal character —
    /// an audible "different person" jolt at each seam. Request stitching
    /// isn't available for v3, so instead each take covers several sections at
    /// once (one take = one voice) and the seams are found afterwards using the
    /// character-level timestamps the API returns alongside the audio.
    ///
    /// The output contract is unchanged: <c>NN_SLUG.mp3</c> +
    /// <c>NN_SLUG_timed.txt</c> + <c>manifest.json</c> + <c>word_timestamps/</c>,
    /// same names, same folder. SegmentSequencer stitches them with its
    /// transition pauses exactly as before — that is the whole point of
    /// slicing rather than handing Unity one long file.
    ///
    /// Driven by <see cref="TtsGenerationJob"/>, which owns the config and can
    /// still run the old per-section path when the flag is off.
    /// </summary>
    public class ChunkedTtsGenerationJob
    {
        readonly TtsGenerationJob.Config cfg;
        readonly Action<float>  onProgress;
        readonly Action<string> onStatus;
        readonly Action<TtsGenerationJob.Result> onComplete;

        public ChunkedTtsGenerationJob(TtsGenerationJob.Config c,
            Action<float> progress, Action<string> status,
            Action<TtsGenerationJob.Result> complete)
        {
            cfg        = c ?? throw new ArgumentNullException(nameof(c));
            onProgress = progress;
            onStatus   = status;
            onComplete = complete;
        }

        // Per-chunk record kept for the render manifest — the debugging trail
        // for when a seam sounds wrong.
        class ChunkRecord
        {
            public string Id;
            public int    Seed;
            public int    TextLength;
            public string OutputFormat;
            public string RequestId;
            public float  AudioSeconds;
            public float  MeasuredLufs, AppliedGainDb;
            public string LoudnessMethod = "none";
            public string AlignmentSource = "synthesis";

            /// <summary>The take was still stalled when the retries ran out —
            /// the only render outcome that makes the output untrustworthy.
            /// A stall that a retry cleared is just a note.</summary>
            public bool StalledAfterRetries;
            public List<float>  Boundaries = new List<float>();
            public List<string> Notes      = new List<string>();
            public TtsChunkAssembler.Chunk Chunk;
            public List<AudioSlicer.Cut>   Cuts = new List<AudioSlicer.Cut>();
        }

        public IEnumerator Run()
        {
            var opts = cfg.Chunked ?? new TtsGenerationJob.ChunkedOptions();

            // ---- validate ------------------------------------------------
            if (string.IsNullOrWhiteSpace(cfg.ScriptText))
            {
                Finish(false, "Script is empty.");
                yield break;
            }
            bool replaying = cfg.DryRun && HasDryRunCache(opts);
            if (!cfg.DryRun && string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                Finish(false, "No ElevenLabs API key set. Open the API Key popup first.");
                yield break;
            }

            string outDir = TtsGenerationJob.ResolveOutputFolder(cfg.OutputFolder);
            string chunkDir = Path.Combine(outDir, "chunks");
            try { Directory.CreateDirectory(chunkDir); }
            catch (Exception e)
            {
                Finish(false, $"Cannot create output folder '{outDir}': {e.Message}");
                yield break;
            }

            // ---- plan ----------------------------------------------------
            var segments = TtsScriptProcessor.SplitIntoSegments(cfg.ScriptText);
            if (segments.Count == 0)
            {
                Finish(false, "No segments found in script (expected `## SECTION NAME` headings).");
                yield break;
            }
            TtsGenerationJob.AssignOrderAndSlugs(segments);

            var plan = TtsChunkAssembler.Assemble(segments, opts.Chunking);
            foreach (string w in plan.Warnings) Debug.LogWarning($"[Tts] {w}");

            if (plan.Chunks.Count == 0)
            {
                Finish(false, "Chunk assembly produced nothing to render.");
                yield break;
            }

            Report(0f, $"{segments.Count} section(s) -> {plan.Chunks.Count} take(s): " +
                       string.Join(", ", plan.Chunks.ConvertAll(
                           c => $"{c.Id} {c.Text.Length}c")));

            // ---- ffmpeg --------------------------------------------------
            string ffVersion = null, ffError = null;
            bool useFfmpeg = opts.UseFfmpeg && FfmpegRunner.Verify(out ffVersion, out ffError);
            if (opts.UseFfmpeg && !useFfmpeg)
                Debug.LogWarning($"[Tts] ffmpeg unavailable — {ffError}\n" +
                                 "Falling back to an RMS gain for loudness and WAV section files.");
            else if (useFfmpeg)
                Debug.Log($"[Tts] Using {ffVersion}");

            // One seed per video, sent on every chunk, so the takes sample from
            // the same place. Persisted in the render manifest so a good render
            // can be reproduced.
            int seed = opts.Seed ?? new System.Random().Next(1, int.MaxValue);

            var manifestEntries = new List<TtsGenerationJob.ManifestEntry>();
            var records         = new List<ChunkRecord>();
            var problems        = new List<string>();
            string sectionExt   = useFfmpeg ? ".mp3" : ".wav";

            // ---- render each chunk ---------------------------------------
            for (int ci = 0; ci < plan.Chunks.Count; ci++)
            {
                var chunk  = plan.Chunks[ci];
                var record = new ChunkRecord {
                    Id = chunk.Id, Seed = seed, TextLength = chunk.Text.Length, Chunk = chunk
                };
                records.Add(record);

                float chunkBase = ci / (float)plan.Chunks.Count;
                float chunkSpan = 1f / plan.Chunks.Count;

                // Pure dry run with nothing cached: show the plan, spend nothing.
                if (cfg.DryRun && !replaying)
                {
                    Debug.Log($"[DryRun:{chunk.Id}] {chunk.Text.Length} chars, " +
                              $"{CountSections(chunk)} section(s)\n" +
                              DescribeSpans(chunk));
                    Report(chunkBase + chunkSpan, $"[{chunk.Id}] dry run — no API call.");
                    continue;
                }

                // ---- audio + alignment -----------------------------------
                WavCodec.AudioBuffer audio = null;
                TtsAlignment alignment     = null;
                string       failure       = null;

                if (replaying)
                {
                    Report(chunkBase + 0.1f * chunkSpan, $"[{chunk.Id}] replaying cached take…");
                    LoadDryRunCache(opts, chunk, out audio, out alignment, out failure);
                    record.OutputFormat    = "cache";
                    record.AlignmentSource = "cache";
                }
                else
                {
                    yield return GenerateChunk(chunk, seed, opts, record,
                        p => Report(chunkBase + chunkSpan * (0.05f + 0.70f * p),
                                    $"[{chunk.Id}] rendering {chunk.Text.Length} chars…"),
                        (a, al, err) => { audio = a; alignment = al; failure = err; });
                }

                if (failure != null) { Finish(false, failure); yield break; }
                if (audio == null || audio.Frames == 0)
                {
                    Finish(false, $"[{chunk.Id}] produced no audio.");
                    yield break;
                }

                record.AudioSeconds = audio.Seconds;

                if (record.StalledAfterRetries)
                    problems.Add($"{chunk.Id} (alignment stalled)");

                // Keep the raw take: it is the dry-run cache for the next
                // iteration on the splitter, and the evidence when a seam is
                // wrong. Written on a replay too, so a dry run rehearses the
                // real file layout rather than a shortcut through it.
                string rawWav = Path.Combine(chunkDir, chunk.Id + "_raw.wav");
                try
                {
                    WavCodec.Write(rawWav, audio);
                    File.WriteAllText(Path.Combine(chunkDir, chunk.Id + "_alignment.json"),
                        (alignment ?? new TtsAlignment()).ToJson(), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(chunkDir, chunk.Id + "_text.txt"),
                        chunk.Text, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Tts:{chunk.Id}] Could not cache the raw take: {e.Message}");
                }

                // ---- loudness (per chunk, never per section) --------------
                Report(chunkBase + chunkSpan * 0.78f, $"[{chunk.Id}] matching loudness…");
                yield return MatchLoudness(audio, rawWav, chunkDir, chunk.Id, opts, useFfmpeg, record,
                                           normalized => audio = normalized);

                // ---- alignment sanity ------------------------------------
                if (alignment == null || !alignment.MatchesText(chunk.Text))
                {
                    string detail = alignment == null
                        ? "the response carried no alignment"
                        : $"it describes {alignment.Length} characters but {chunk.Text.Length} were sent";
                    string note = $"Alignment unusable ({detail}) — falling back to a proportional " +
                                  "map. Cut points and marker times are estimates; re-run this take.";
                    Debug.LogWarning($"[Tts:{chunk.Id}] {note}");
                    record.Notes.Add(note);
                    record.AlignmentSource = "proportional-fallback";
                    problems.Add($"{chunk.Id} (alignment)");
                    alignment = TtsAlignment.Proportional(chunk.Text, audio.Seconds);
                }

                // ---- split -----------------------------------------------
                Report(chunkBase + chunkSpan * 0.85f, $"[{chunk.Id}] cutting {CountSections(chunk)} section(s)…");
                var split = AudioSlicer.Split(audio, alignment, chunk.Spans, opts.Slicing);
                foreach (string w in split.Warnings)
                {
                    Debug.LogWarning($"[Tts:{chunk.Id}] {w}");
                    record.Notes.Add(w);
                }
                // Only the severe ones. A boundary that was moved to a real pause
                // is the slicer doing its job — flagging it would abort every
                // headless run for a routine correction.
                if (split.Severe.Count > 0) problems.Add($"{chunk.Id} (seam)");
                record.Boundaries.AddRange(split.Boundaries);
                record.Cuts.AddRange(split.Cuts);

                if (split.Cuts.Count != CountSections(chunk))
                {
                    Finish(false, $"[{chunk.Id}] expected {CountSections(chunk)} section slices " +
                                  $"but got {split.Cuts.Count}.");
                    yield break;
                }

                // ---- write the per-section contract ----------------------
                foreach (var cut in split.Cuts)
                {
                    string err = null;
                    yield return WriteSection(cut, alignment, outDir, sectionExt, opts,
                                              manifestEntries, e => err = e);
                    if (err != null) { Finish(false, err); yield break; }

                    // The cut's own samples are on disk now; the render manifest
                    // only needs its timings. A whole video's worth of decoded
                    // audio held twice over is tens of megabytes for nothing.
                    cut.Samples = null;
                }

                Report(chunkBase + chunkSpan,
                    $"[{chunk.Id}] {audio.Seconds:F1}s -> {split.Cuts.Count} section file(s).");
            }

            // ---- manifests -----------------------------------------------
            string manifestPath = null;
            if (!cfg.DryRun || replaying)
            {
                manifestEntries.Sort((a, b) => a.order.CompareTo(b.order));
                manifestPath = Path.Combine(outDir, "manifest.json");
                try
                {
                    File.WriteAllText(manifestPath,
                        TtsGenerationJob.BuildManifestJson(manifestEntries), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outDir, "render_manifest.json"),
                        BuildRenderManifest(seed, opts, records, plan.Warnings, sectionExt),
                        new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Finish(false, $"Manifest write failed: {e.Message}");
                    yield break;
                }
            }

            Report(1f, cfg.DryRun && !replaying
                ? $"Dry run complete — {plan.Chunks.Count} take(s) planned, no API calls."
                : problems.Count > 0
                    ? $"Saved to {outDir}, but {string.Join(", ", problems)} needs checking " +
                      "before recording."
                    : $"Done — {plan.Chunks.Count} take(s) -> {manifestEntries.Count} section(s) in {outDir}");

            onComplete?.Invoke(new TtsGenerationJob.Result {
                Success           = true,
                // A plan-only dry run writes nothing, so report what it parsed —
                // the panel prints this as "N/N segment(s)".
                SegmentsProcessed = cfg.DryRun && !replaying ? segments.Count : manifestEntries.Count,
                SegmentsTotal     = segments.Count,
                ManifestPath      = manifestPath,
                WasDryRun         = cfg.DryRun,
                StalledSegments   = problems,
            });
        }

        // -------------------------------------------------------------------
        // One chunk: API call, retries, decode, alignment choice
        // -------------------------------------------------------------------

        IEnumerator GenerateChunk(
            TtsChunkAssembler.Chunk chunk, int baseSeed, TtsGenerationJob.ChunkedOptions opts,
            ChunkRecord record, Action<float> progress,
            Action<WavCodec.AudioBuffer, TtsAlignment, string> done)
        {
            int maxAttempts = Math.Max(1, cfg.MaxStallRetries + 1);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // A stalled render is unusable, and re-sending the same seed
                // would reproduce it — so a retry deliberately moves the seed.
                // The overlap prefix still conditions the seam, and the render
                // manifest records which seed each take actually used.
                int seed = attempt == 1 ? baseSeed : baseSeed + attempt - 1;
                record.Seed = seed;

                var settings = (cfg.VoiceSettings ?? new ElevenLabsClient.VoiceSettings()).Clone();
                settings.stability = ElevenLabsClient.SnapStability(opts.Stability);

                ElevenLabsClient.TtsResult tts = null;
                string error = null;

                // Nested IEnumerator rather than CoroutineHost.StartCoroutine, so
                // the whole pipeline can also be pumped from the Editor window
                // (a MonoBehaviour can't run coroutines outside Play Mode).
                yield return ElevenLabsClient.GenerateTts(
                    new ElevenLabsClient.TtsRequest {
                        Text         = chunk.Text,
                        VoiceId      = cfg.VoiceId,
                        ModelId      = cfg.ModelId,
                        Settings     = settings,
                        Seed         = seed,
                        OutputFormat = opts.PreferredOutputFormat,
                    },
                    cfg.ApiKey, progress,
                    r => tts   = r,
                    e => error = e);

                if (error != null) { done(null, null, error); yield break; }

                record.OutputFormat = tts.OutputFormat;
                record.RequestId    = tts.RequestId;

                // ---- decode ------------------------------------------------
                WavCodec.AudioBuffer audio = null;
                string decodeError = null;
                yield return Decode(tts, opts, a => audio = a, e => decodeError = e);
                if (audio == null)
                {
                    done(null, null, $"[{chunk.Id}] could not decode the returned audio: {decodeError}");
                    yield break;
                }

                // ---- alignment ---------------------------------------------
                TtsAlignment alignment = tts.Alignment;
                record.AlignmentSource = alignment != null ? "synthesis" : "none";

                // The synthesis alignment does not reliably describe its own
                // audio (measured ±1.3s on this project), and it now decides
                // where the audio is CUT — so prefer times measured against the
                // rendered file when the account allows it.
                if (!_forcedAlignmentUnavailable && cfg.UseForcedAlignment)
                {
                    List<TtsScriptProcessor.WordTimestamp> words = null;
                    string faError = null;

                    byte[] wavBytes = ToWavBytes(audio);
                    yield return ElevenLabsClient.GetForcedAlignment(
                        wavBytes, chunk.Text, cfg.ApiKey,
                        ok => words = ok, e => faError = e,
                        fileName: chunk.Id + ".wav", mimeType: "audio/wav");

                    if (words != null && words.Count > 0)
                    {
                        var corrected = TtsAlignment.FromWords(chunk.Text, words);
                        if (corrected != null)
                        {
                            alignment = corrected;
                            record.AlignmentSource = "forced-alignment";
                        }
                        else
                        {
                            Debug.LogWarning($"[Tts:{chunk.Id}] Forced alignment came back but its " +
                                             "words don't match the sent text — keeping the synthesis " +
                                             "alignment.");
                        }
                    }
                    else if (faError != null && faError.Contains("missing_permissions"))
                    {
                        _forcedAlignmentUnavailable = true;
                        Debug.LogWarning(
                            "[Tts] Forced alignment is unavailable: the ElevenLabs API key lacks the " +
                            "'forced_alignment' permission. Cut points and marker timings will use the " +
                            "less accurate synthesis alignment. To fix: ElevenLabs dashboard -> API " +
                            "Keys -> enable Forced Alignment for this key.");
                    }
                    else if (faError != null)
                    {
                        Debug.LogWarning($"[Tts:{chunk.Id}] Forced alignment failed " +
                                         $"({Trunc(faError, 200)}) — using synthesis alignment.");
                    }
                }

                // ---- is this take usable? ----------------------------------
                string stall = alignment == null
                    ? null   // no alignment at all is handled by the caller's fallback
                    : TtsGenerationJob.DescribeAlignmentStall(alignment.ToWords())
                      ?? DescribeTailMismatch(audio.Seconds, alignment.TotalSeconds,
                                              record.AlignmentSource == "forced-alignment");

                if (stall == null) { done(audio, alignment, null); yield break; }

                Debug.LogWarning($"[Tts:{chunk.Id}] {stall}");
                record.Notes.Add($"attempt {attempt}: {stall}");

                if (attempt >= maxAttempts)
                {
                    // Out of retries: hand it back anyway so the take can be
                    // inspected, and let the caller mark the run as suspect.
                    record.StalledAfterRetries = true;
                    done(audio, alignment, null);
                    yield break;
                }
                Report(0f, $"[{chunk.Id}] bad alignment — re-rendering ({attempt}/{maxAttempts - 1})…");
            }
        }

        IEnumerator Decode(
            ElevenLabsClient.TtsResult tts, TtsGenerationJob.ChunkedOptions opts,
            Action<WavCodec.AudioBuffer> onAudio, Action<string> onError)
        {
            string format = tts.OutputFormat ?? ElevenLabsClient.OutputFormatMp3;

            // Raw PCM needs no decoder at all — just a header. This is why the
            // pipeline asks for pcm_44100 first.
            if (format.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            {
                int rate = 44100;
                int.TryParse(format.Substring(4), out rate);
                onAudio(WavCodec.FromPcm16(tts.AudioBytes, rate <= 0 ? 44100 : rate, 1));
                yield break;
            }

            string temp        = Path.Combine(Application.temporaryCachePath, "mugs_chunk_in.mp3");
            string tempWav     = Path.Combine(Application.temporaryCachePath, "mugs_chunk_in.wav");
            string stageError  = null;
            try
            {
                Directory.CreateDirectory(Application.temporaryCachePath);
                File.WriteAllBytes(temp, tts.AudioBytes);
            }
            catch (Exception e) { stageError = $"could not stage the audio: {e.Message}"; }
            if (stageError != null) { onError(stageError); yield break; }

            if (opts.UseFfmpeg && FfmpegRunner.IsAvailable)
            {
                bool ok = false; string err = null;
                yield return FfmpegRunner.DecodeToWav(temp, tempWav, 44100,
                    (success, e) => { ok = success; err = e; });

                if (ok)
                {
                    WavCodec.AudioBuffer buffer = null;
                    try { buffer = WavCodec.Read(tempWav); }
                    catch (Exception e) { err = e.Message; }
                    if (buffer != null) { onAudio(buffer); yield break; }
                }
                Debug.LogWarning($"[Tts] ffmpeg decode failed ({Trunc(err, 200)}) — " +
                                 "falling back to Unity's decoder.");
            }

            // Unity's own decoder. Same approach SegmentSequencer uses to read
            // segment mp3s, including the streamAudio=false requirement that
            // makes GetData work at all.
            WavCodec.AudioBuffer decoded = null;
            string uri = new Uri(temp).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
            {
                if (req.downloadHandler is DownloadHandlerAudioClip h) h.streamAudio = false;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError($"Unity could not decode the mp3: {req.error}");
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.samples == 0) { onError("decoded clip was empty."); yield break; }

                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                decoded = new WavCodec.AudioBuffer {
                    Samples = data, Channels = clip.channels, SampleRate = clip.frequency
                };
            }
            onAudio(decoded);
        }

        // -------------------------------------------------------------------
        // Loudness — one gain for the whole take
        // -------------------------------------------------------------------

        IEnumerator MatchLoudness(
            WavCodec.AudioBuffer audio, string rawWav, string chunkDir, string chunkId,
            TtsGenerationJob.ChunkedOptions opts, bool useFfmpeg, ChunkRecord record,
            Action<WavCodec.AudioBuffer> onNormalized)
        {
            if (useFfmpeg && File.Exists(rawWav))
            {
                string normWav = Path.Combine(chunkDir, chunkId + ".wav");
                FfmpegRunner.LoudnormResult loud = null;
                yield return FfmpegRunner.LoudnormTwoPass(
                    rawWav, normWav, opts.TargetLufs, r => loud = r);

                if (loud != null && loud.Success)
                {
                    WavCodec.AudioBuffer normalized = null;
                    try { normalized = WavCodec.Read(normWav); }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Tts:{chunkId}] Could not read the normalised take: {e.Message}");
                    }

                    if (normalized != null && normalized.Frames > 0)
                    {
                        record.LoudnessMethod = loud.WasLinear ? "loudnorm-linear" : "loudnorm-dynamic";
                        record.MeasuredLufs   = loud.MeasuredLufs;
                        record.AppliedGainDb  = loud.AppliedGainDb;

                        if (!loud.WasLinear)
                        {
                            string note = "loudnorm fell back to DYNAMIC mode — the take's loudness " +
                                          "range was too wide for a single gain, so its dynamics have " +
                                          "been compressed. Quiet beats may sound levelled.";
                            Debug.LogWarning($"[Tts:{chunkId}] {note}");
                            record.Notes.Add(note);
                        }
                        onNormalized(normalized);
                        yield break;
                    }
                }
                else
                {
                    Debug.LogWarning($"[Tts:{chunkId}] loudnorm failed " +
                                     $"({Trunc(loud?.Error, 200)}) — using an RMS gain instead.");
                }
            }

            // Fallback: one uniform gain, measured over speech only. LUFS is
            // K-weighted and dBFS RMS is not; for speech the two sit about 4dB
            // apart, which is close enough for matching two takes of the SAME
            // voice against each other.
            float gain = FfmpegRunner.RmsMatchGain(
                audio.Samples, audio.Channels, audio.SampleRate, opts.TargetLufs - 4f);
            FfmpegRunner.ApplyGain(audio.Samples, gain);

            record.LoudnessMethod = "rms";
            record.AppliedGainDb  = 20f * (float)Math.Log10(Math.Max(1e-6f, gain));
            onNormalized(audio);
        }

        // -------------------------------------------------------------------
        // Writing one section — the downstream contract
        // -------------------------------------------------------------------

        IEnumerator WriteSection(
            AudioSlicer.Cut cut, TtsAlignment alignment, string outDir, string ext,
            TtsGenerationJob.ChunkedOptions opts,
            List<TtsGenerationJob.ManifestEntry> entries, Action<string> onError)
        {
            var span = cut.Span;
            string audioName  = span.Slug + ext;
            string scriptName = span.Slug + "_timed.txt";
            string audioPath  = Path.Combine(outDir, audioName);
            string wavPath    = ext == ".wav"
                ? audioPath
                : Path.Combine(Application.temporaryCachePath, span.Slug + ".wav");

            // ---- markers -> section-local T= ------------------------------
            // Each marker's CleanIndex is a position in the CHUNK text, so its
            // time comes straight off the chunk alignment; subtracting the
            // slice's own start converts it to this file's timeline, which is
            // what _timed.txt has always described.
            var timed = new List<TtsScriptProcessor.TimedMarker>(span.Markers.Count);
            foreach (var m in span.Markers)
            {
                float chunkTime = alignment.StartAt(m.CleanIndex);
                timed.Add(new TtsScriptProcessor.TimedMarker {
                    Marker      = m,
                    TriggerTime = (float)Math.Round(cut.ToLocal(chunkTime), 3),
                });
            }
            string timedScript = TtsScriptProcessor.RebuildTimedScript(span.Raw, timed);

            // ---- write ----------------------------------------------------
            string writeError = null;
            try
            {
                WavCodec.Write(wavPath, cut.Samples, cut.SampleRate, cut.Channels);
                File.WriteAllText(Path.Combine(outDir, scriptName), timedScript, new UTF8Encoding(false));

                string wordsDir = Path.Combine(outDir, "word_timestamps");
                Directory.CreateDirectory(wordsDir);
                File.WriteAllText(
                    Path.Combine(wordsDir, span.Slug + "_words.json"),
                    TtsGenerationJob.WordsJson(
                        alignment.Slice(span.Start, span.End, cut.StartSeconds).ToWords()),
                    new UTF8Encoding(false));
            }
            catch (Exception e) { writeError = $"[{span.Slug}] write failed: {e.Message}"; }
            if (writeError != null) { onError(writeError); yield break; }

            if (ext == ".mp3")
            {
                bool ok = false; string err = null;
                yield return FfmpegRunner.EncodeMp3(wavPath, audioPath, opts.Mp3Kbps,
                    (success, e) => { ok = success; err = e; });

                if (!ok)
                {
                    onError($"[{span.Slug}] mp3 encode failed: {Trunc(err, 300)}");
                    yield break;
                }
                try { File.Delete(wavPath); } catch { /* temp file */ }
            }

            entries.Add(new TtsGenerationJob.ManifestEntry {
                order        = span.Order,
                slug         = span.Slug,
                name         = span.Name,
                audio_file   = audioName,
                script_file  = scriptName,
                duration     = (float)Math.Round(cut.DurationSeconds,    3),
                speech_start = (float)Math.Round(cut.SpeechStartSeconds, 3),
                speech_end   = (float)Math.Round(cut.SpeechEndSeconds,   3),
            });
        }

        // -------------------------------------------------------------------
        // Dry-run cache
        // -------------------------------------------------------------------

        static bool HasDryRunCache(TtsGenerationJob.ChunkedOptions opts)
            => !string.IsNullOrWhiteSpace(opts.DryRunCacheFolder)
               && Directory.Exists(opts.DryRunCacheFolder)
               && Directory.GetFiles(opts.DryRunCacheFolder, "*_alignment.json").Length > 0;

        static void LoadDryRunCache(
            TtsGenerationJob.ChunkedOptions opts, TtsChunkAssembler.Chunk chunk,
            out WavCodec.AudioBuffer audio, out TtsAlignment alignment, out string error)
        {
            audio = null; alignment = null; error = null;

            string wav  = Path.Combine(opts.DryRunCacheFolder, chunk.Id + "_raw.wav");
            string json = Path.Combine(opts.DryRunCacheFolder, chunk.Id + "_alignment.json");
            if (!File.Exists(wav) || !File.Exists(json))
            {
                error = $"[{chunk.Id}] dry-run cache is missing {Path.GetFileName(wav)} or " +
                        $"{Path.GetFileName(json)} in {opts.DryRunCacheFolder}. Run once for real " +
                        "to populate the chunks/ folder, then point the dry run at it.";
                return;
            }

            try
            {
                audio     = WavCodec.Read(wav);
                alignment = TtsAlignment.FromJson(File.ReadAllText(json));
            }
            catch (Exception e) { error = $"[{chunk.Id}] could not read the cached take: {e.Message}"; }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        static bool _forcedAlignmentUnavailable;

        // How far the audio may run past the alignment's last character before
        // the alignment is declared wrong. With real samples in hand this is an
        // exact comparison rather than the old bitrate estimate.
        const float TailMismatchSeconds = 1.5f;

        static string DescribeTailMismatch(float audioSeconds, float alignmentEnd, bool measured)
        {
            if (measured) return null;              // forced alignment describes the file by construction
            if (audioSeconds < 5f) return null;

            float overrun = audioSeconds - alignmentEnd;
            if (overrun <= TailMismatchSeconds) return null;

            return $"Alignment ends {overrun:F1}s before the audio does (last character at " +
                   $"{alignmentEnd:F1}s, audio {audioSeconds:F1}s). Section cuts and markers after " +
                   "the drift point would be wrong. Re-rendering.";
        }

        static byte[] ToWavBytes(WavCodec.AudioBuffer audio)
        {
            string temp = Path.Combine(Application.temporaryCachePath, "mugs_align.wav");
            WavCodec.Write(temp, audio);
            return File.ReadAllBytes(temp);
        }

        static int CountSections(TtsChunkAssembler.Chunk chunk)
        {
            int n = 0;
            foreach (var s in chunk.Spans) if (!s.IsOverlap) n++;
            return n;
        }

        static string DescribeSpans(TtsChunkAssembler.Chunk chunk)
        {
            var sb = new StringBuilder();
            foreach (var s in chunk.Spans)
                sb.Append("    ").Append(s.IsOverlap ? "(overlap)" : s.Slug)
                  .Append("  [").Append(s.Start).Append("..").Append(s.End).Append(")  ")
                  .Append(s.Markers.Count).Append(" markers\n");
            return sb.ToString();
        }

        void Report(float p, string msg)
        {
            if (p > 0f) onProgress?.Invoke(Mathf.Clamp01(p));
            if (!string.IsNullOrEmpty(msg)) onStatus?.Invoke(msg);
        }

        void Finish(bool ok, string err)
            => onComplete?.Invoke(new TtsGenerationJob.Result { Success = ok, ErrorMessage = err });

        static string Trunc(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        // -------------------------------------------------------------------
        // render_manifest.json — the debugging trail
        // -------------------------------------------------------------------

        string BuildRenderManifest(
            int seed, TtsGenerationJob.ChunkedOptions opts, List<ChunkRecord> records,
            List<string> planWarnings, string sectionExt)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"generated_at\": ").Append(J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append(",\n");
            sb.Append("  \"pipeline\": \"chunked\",\n");
            sb.Append("  \"seed\": ").Append(seed).Append(",\n");
            sb.Append("  \"model\": ").Append(J(cfg.ModelId)).Append(",\n");
            sb.Append("  \"voice_id\": ").Append(J(cfg.VoiceId)).Append(",\n");
            sb.Append("  \"section_format\": ").Append(J(sectionExt.TrimStart('.'))).Append(",\n");
            sb.Append("  \"voice_settings\": {\n");
            var vs = cfg.VoiceSettings ?? new ElevenLabsClient.VoiceSettings();
            sb.Append("    \"stability\": ").Append(F(ElevenLabsClient.SnapStability(opts.Stability))).Append(",\n");
            sb.Append("    \"similarity_boost\": ").Append(F(vs.similarity_boost)).Append(",\n");
            sb.Append("    \"style\": ").Append(F(vs.style)).Append(",\n");
            sb.Append("    \"use_speaker_boost\": ").Append(vs.use_speaker_boost ? "true" : "false").Append("\n");
            sb.Append("  },\n");
            sb.Append("  \"chunking\": {\n");
            sb.Append("    \"boundary_section\": ").Append(J(opts.Chunking.BoundarySection)).Append(",\n");
            sb.Append("    \"max_chunk_chars\": ").Append(opts.Chunking.MaxChunkChars).Append(",\n");
            sb.Append("    \"overlap_sentences\": ").Append(opts.Chunking.OverlapSentences).Append(",\n");
            sb.Append("    \"min_overlap_chars\": ").Append(opts.Chunking.MinOverlapChars).Append(",\n");
            sb.Append("    \"stage_directions_sent\": ")
              .Append(opts.Chunking.KeepStageDirections ? "true" : "false").Append("\n");
            sb.Append("  },\n");
            sb.Append("  \"slicing\": {\n");
            sb.Append("    \"edge_pad_seconds\": ").Append(F(opts.Slicing.EdgePadSeconds)).Append(",\n");
            sb.Append("    \"fade_seconds\": ").Append(F(opts.Slicing.FadeSeconds)).Append(",\n");
            sb.Append("    \"target_lufs\": ").Append(F(opts.TargetLufs)).Append("\n");
            sb.Append("  },\n");

            sb.Append("  \"chunks\": [\n");
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                sb.Append("    {\n");
                sb.Append("      \"id\": ").Append(J(r.Id)).Append(",\n");
                sb.Append("      \"seed\": ").Append(r.Seed).Append(",\n");
                sb.Append("      \"text_length\": ").Append(r.TextLength).Append(",\n");
                sb.Append("      \"output_format\": ").Append(J(r.OutputFormat)).Append(",\n");
                sb.Append("      \"request_id\": ").Append(J(r.RequestId)).Append(",\n");
                sb.Append("      \"audio_seconds\": ").Append(F(r.AudioSeconds)).Append(",\n");
                sb.Append("      \"alignment_source\": ").Append(J(r.AlignmentSource)).Append(",\n");
                sb.Append("      \"loudness_method\": ").Append(J(r.LoudnessMethod)).Append(",\n");
                sb.Append("      \"measured_lufs\": ").Append(F(r.MeasuredLufs)).Append(",\n");
                sb.Append("      \"applied_gain_db\": ").Append(F(r.AppliedGainDb)).Append(",\n");

                sb.Append("      \"spans\": [\n");
                var spans = r.Chunk != null ? r.Chunk.Spans : new List<TtsChunkAssembler.Span>();
                for (int s = 0; s < spans.Count; s++)
                {
                    sb.Append("        {\"slug\": ").Append(J(spans[s].IsOverlap ? "(overlap)" : spans[s].Slug))
                      .Append(", \"char_start\": ").Append(spans[s].Start)
                      .Append(", \"char_end\": ").Append(spans[s].End)
                      .Append(", \"markers\": ").Append(spans[s].Markers.Count)
                      .Append(", \"discarded\": ").Append(spans[s].IsOverlap ? "true" : "false")
                      .Append("}").Append(s + 1 < spans.Count ? "," : "").Append('\n');
                }
                sb.Append("      ],\n");

                sb.Append("      \"cut_seconds\": [");
                for (int b = 0; b < r.Boundaries.Count; b++)
                    sb.Append(F(r.Boundaries[b])).Append(b + 1 < r.Boundaries.Count ? ", " : "");
                sb.Append("],\n");

                sb.Append("      \"sections\": [\n");
                for (int c = 0; c < r.Cuts.Count; c++)
                {
                    var cut = r.Cuts[c];
                    sb.Append("        {\"slug\": ").Append(J(cut.Span.Slug))
                      .Append(", \"from\": ").Append(F(cut.StartSeconds))
                      .Append(", \"to\": ").Append(F(cut.EndSeconds))
                      .Append(", \"duration\": ").Append(F(cut.DurationSeconds))
                      .Append("}").Append(c + 1 < r.Cuts.Count ? "," : "").Append('\n');
                }
                sb.Append("      ],\n");

                sb.Append("      \"notes\": [");
                for (int nI = 0; nI < r.Notes.Count; nI++)
                    sb.Append(J(r.Notes[nI])).Append(nI + 1 < r.Notes.Count ? ", " : "");
                sb.Append("]\n");

                sb.Append("    }").Append(i + 1 < records.Count ? "," : "").Append('\n');
            }
            sb.Append("  ],\n");

            sb.Append("  \"plan_warnings\": [");
            for (int i = 0; i < planWarnings.Count; i++)
                sb.Append(J(planWarnings[i])).Append(i + 1 < planWarnings.Count ? ", " : "");
            sb.Append("]\n}\n");

            return sb.ToString();
        }

        static string J(string s) => TtsGenerationJob.JsonString(s);
        static string F(float v)  => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

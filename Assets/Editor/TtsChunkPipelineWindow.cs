using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor front-end for the chunked TTS pipeline: parse the script, assemble
/// the chunks, call ElevenLabs, slice the takes back into per-section files.
///
/// It drives the SAME runtime job the in-app panel and the headless
/// <c>--auto-script</c> automation use (<see cref="TtsGenerationJob"/>) rather
/// than a parallel editor-only implementation — the pipeline has to work in the
/// built player, so this window is a caller, not a second copy.
///
/// Menu: <b>MugsTech ▸ TTS ▸ Chunked Pipeline</b>.
/// </summary>
public class TtsChunkPipelineWindow : EditorWindow
{
    const string ScriptPathKey  = "AutoAvatarGen.Editor.TtsScriptPath";
    const string CacheFolderKey = "AutoAvatarGen.Editor.TtsDryRunCache";

    string scriptPath;
    string outputRoot;
    string dryRunCache;

    // Chunking
    string boundarySection   = "BREAKDOWN";
    int    maxChunkChars     = 3000;
    int    overlapSentences  = 2;
    int    minOverlapChars   = 120;
    bool   keepStageDirections = true;

    // Voice / audio
    int   stabilityIndex = 0;                 // Creative
    int   seed;                               // 0 = generate one per run
    float targetLufs     = -16f;
    float edgePadMs      = 75f;
    bool  preferPcm      = true;
    bool  useFfmpeg      = true;
    string ffmpegPath;

    // Run state
    readonly Pump pump = new Pump();
    string status = "Ready.";
    float  progress;
    string planPreview;
    Vector2 scroll;

    static readonly string[] StabilityNames = { "Creative (0.0)", "Natural (0.5)", "Robust (1.0)" };
    static readonly float[]  StabilityValues = {
        ElevenLabsClient.StabilityCreative,
        ElevenLabsClient.StabilityNatural,
        ElevenLabsClient.StabilityRobust,
    };

    [MenuItem("MugsTech/TTS/Chunked Pipeline")]
    public static void Open()
    {
        var window = GetWindow<TtsChunkPipelineWindow>("TTS Pipeline");
        window.minSize = new Vector2(460, 560);
    }

    void OnEnable()
    {
        scriptPath = EditorPrefs.GetString(ScriptPathKey,
            Path.Combine(Application.dataPath, "Python/script/Script.txt"));
        dryRunCache = EditorPrefs.GetString(CacheFolderKey, "");
        outputRoot  = PlayerPrefs.GetString(MainMenuController.PythonOutputFolderPrefKey,
                                            MainMenuController.DefaultPythonOutputFolder);
        ffmpegPath  = FfmpegRunner.ExecutablePath;
    }

    void OnDisable()
    {
        EditorPrefs.SetString(ScriptPathKey, scriptPath ?? "");
        EditorPrefs.SetString(CacheFolderKey, dryRunCache ?? "");
    }

    // -----------------------------------------------------------------------

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        DrawPathRow("Script file", ref scriptPath, pickFolder: false);
        DrawPathRow("Output library", ref outputRoot, pickFolder: true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chunking", EditorStyles.boldLabel);
        boundarySection  = EditorGUILayout.TextField(
            new GUIContent("Break after section", "Chunk A ends after this section. " +
                "Falls back to a balanced split when no section matches."), boundarySection);
        maxChunkChars    = EditorGUILayout.IntField(
            new GUIContent("Max chars / chunk", "Overflow re-splits into more chunks at section " +
                "boundaries. v3's documented limit is 5,000."), maxChunkChars);
        overlapSentences = EditorGUILayout.IntSlider(
            new GUIContent("Overlap sentences", "Trailing sentences of the previous chunk, " +
                "prepended so the model reads into this one from the same context. " +
                "Their audio is cut off afterwards."), overlapSentences, 0, 4);
        minOverlapChars  = EditorGUILayout.IntField("Min overlap chars", minOverlapChars);
        keepStageDirections = EditorGUILayout.Toggle(
            new GUIContent("Send [stage directions]", "v3 reads bracketed cues as audio tags. " +
                "Turn off if a take speaks them aloud."), keepStageDirections);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Voice", EditorStyles.boldLabel);
        stabilityIndex = EditorGUILayout.Popup("Stability", stabilityIndex, StabilityNames);
        seed = EditorGUILayout.IntField(
            new GUIContent("Seed", "0 generates one per run. The same seed goes on every chunk " +
                "of a video and is written into render_manifest.json."), seed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        preferPcm  = EditorGUILayout.Toggle(
            new GUIContent("Prefer raw PCM", "Lossless and needs no decoding. Requires Pro tier " +
                "or above; falls back to mp3 automatically."), preferPcm);
        targetLufs = EditorGUILayout.Slider("Target LUFS", targetLufs, -24f, -10f);
        edgePadMs  = EditorGUILayout.Slider(
            new GUIContent("Edge pad (ms)", "Silence left at each section edge. Unity's own " +
                "transition pauses set the actual gap."), edgePadMs, 0f, 250f);
        useFfmpeg  = EditorGUILayout.Toggle("Use ffmpeg", useFfmpeg);

        using (new EditorGUI.DisabledScope(!useFfmpeg))
        {
            EditorGUI.BeginChangeCheck();
            DrawPathRow("ffmpeg", ref ffmpegPath, pickFolder: false);
            if (EditorGUI.EndChangeCheck())
            {
                FfmpegRunner.ExecutablePath = ffmpegPath;
                FfmpegRunner.ForgetAvailability();
            }
            if (GUILayout.Button("Verify ffmpeg"))
            {
                FfmpegRunner.ForgetAvailability();
                status = FfmpegRunner.Verify(out string v, out string e)
                    ? "ffmpeg OK — " + v
                    : "ffmpeg NOT reachable — " + e;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dry run", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Point this at a previous run's chunks/ folder to replay its audio and alignment " +
            "through the splitter — no API call, no credits. Leave empty for a plan-only dry run.",
            MessageType.None);
        DrawPathRow("Cached chunks", ref dryRunCache, pickFolder: true);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(pump.Running))
        {
            if (GUILayout.Button("Preview chunk plan (offline)")) PreviewPlan();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(dryRunCache)))
                if (GUILayout.Button("Dry run from cached take")) StartRun(dryRun: true);

            GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
            if (GUILayout.Button("Generate — spends ElevenLabs credits", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("Generate narration",
                        "This sends the script to ElevenLabs and spends credits.\n\n" +
                        "Continue?", "Generate", "Cancel"))
                    StartRun(dryRun: false);
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Run pipeline self-tests")) TtsPipelineSelfTests.RunAll();
        }

        if (pump.Running)
        {
            var rect = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.ProgressBar(rect, progress, $"{Mathf.RoundToInt(progress * 100f)}%");
            if (GUILayout.Button("Cancel")) { pump.Stop(); status = "Cancelled."; }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(status, EditorStyles.wordWrappedLabel,
            GUILayout.MinHeight(34));

        if (!string.IsNullOrEmpty(planPreview))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Plan", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(planPreview, EditorStyles.wordWrappedMiniLabel,
                GUILayout.MinHeight(160));
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawPathRow(string label, ref string value, bool pickFolder)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.TextField(label, value);
        if (GUILayout.Button("…", GUILayout.Width(28)))
        {
            string picked = pickFolder
                ? EditorUtility.OpenFolderPanel(label, SafeDirectory(value), "")
                : EditorUtility.OpenFilePanel(label, SafeDirectory(value), "txt,md");
            if (!string.IsNullOrEmpty(picked)) { value = picked; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndHorizontal();
    }

    static string SafeDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return Application.dataPath;
            return Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? Application.dataPath);
        }
        catch { return Application.dataPath; }
    }

    // -----------------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------------

    void PreviewPlan()
    {
        if (!TryReadScript(out string text)) return;

        var segments = TtsScriptProcessor.SplitIntoSegments(text);
        if (segments.Count == 0)
        {
            status = "No `## SECTION` headings found in the script.";
            return;
        }
        TtsGenerationJob.AssignOrderAndSlugs(segments);

        var plan = TtsChunkAssembler.Assemble(segments, BuildChunkingOptions());

        var sb = new StringBuilder();
        sb.Append(segments.Count).Append(" section(s) -> ")
          .Append(plan.Chunks.Count).Append(" API call(s)\n\n");

        foreach (var chunk in plan.Chunks)
        {
            sb.Append(chunk.Id).Append("  ").Append(chunk.Text.Length).Append(" chars\n");
            foreach (var span in chunk.Spans)
            {
                sb.Append("    ")
                  .Append((span.IsOverlap ? "(overlap, discarded)" : span.Slug).PadRight(24))
                  .Append('[').Append(span.Start).Append("..").Append(span.End).Append(")  ")
                  .Append(span.Markers.Count).Append(" marker(s)\n");
            }
            sb.Append('\n');
        }

        foreach (string w in plan.Warnings) sb.Append("⚠ ").Append(w).Append('\n');

        planPreview = sb.ToString();
        status = $"Planned {plan.Chunks.Count} take(s) — nothing sent.";
        Repaint();
    }

    void StartRun(bool dryRun)
    {
        if (!TryReadScript(out string text)) return;

        string apiKey = TtsApiKeyPopup.LoadKey();
        if (!dryRun && string.IsNullOrWhiteSpace(apiKey))
        {
            status = "No ElevenLabs API key saved. Open the app once and store it via the " +
                     "TTS panel's API Key popup — this window reads the same PlayerPref.";
            return;
        }

        // Same per-generation folder naming the panel and the automation use, so
        // the output library and the recording scene pick this run up unchanged.
        string root = TtsGenerationJob.ResolveOutputFolder(outputRoot);
        string generation = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        for (int n = 1; Directory.Exists(generation); n++)
            generation = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + n);

        var cfg = new TtsGenerationJob.Config {
            ApiKey       = apiKey,
            OutputFolder = generation,
            ScriptText   = text,
            DryRun       = dryRun,
            Chunked      = new TtsGenerationJob.ChunkedOptions {
                Enabled   = true,
                Chunking  = BuildChunkingOptions(),
                Slicing   = new AudioSlicer.Options { EdgePadSeconds = edgePadMs / 1000f },
                Seed      = seed > 0 ? seed : (int?)null,
                Stability = StabilityValues[Mathf.Clamp(stabilityIndex, 0, StabilityValues.Length - 1)],
                PreferredOutputFormat = preferPcm
                    ? ElevenLabsClient.OutputFormatPcm44100
                    : ElevenLabsClient.OutputFormatMp3,
                UseFfmpeg  = useFfmpeg,
                TargetLufs = targetLufs,
                DryRunCacheFolder = dryRun ? dryRunCache : null,
            },
        };

        progress = 0f;
        status   = dryRun ? "Replaying cached take…" : "Rendering…";

        var job = new TtsGenerationJob(cfg,
            p => { progress = p; Repaint(); },
            s => { status   = s; Repaint(); },
            r =>
            {
                if (r.Success)
                {
                    status = $"{status}\n\n{(r.WasDryRun ? "Dry run" : "Saved")}: {generation}";
                    if (!r.WasDryRun && r.ManifestPath != null)
                    {
                        PlayerPrefs.SetString(TtsPanelController.SelectedGenerationPrefKey, generation);
                        PlayerPrefs.Save();
                        status += "\nSelected as the active generation for the next recording.";
                    }
                }
                else status = "FAILED: " + r.ErrorMessage;

                progress = 1f;
                Repaint();
            });

        pump.Start(job.Run(), Repaint);
    }

    TtsChunkAssembler.Options BuildChunkingOptions() => new TtsChunkAssembler.Options {
        BoundarySection     = boundarySection,
        MaxChunkChars       = Mathf.Max(500, maxChunkChars),
        OverlapSentences    = overlapSentences,
        MinOverlapChars     = Mathf.Max(0, minOverlapChars),
        KeepStageDirections = keepStageDirections,
    };

    bool TryReadScript(out string text)
    {
        text = null;
        try
        {
            if (!File.Exists(scriptPath))
            {
                status = $"Script not found: {scriptPath}";
                return false;
            }
            text = File.ReadAllText(scriptPath);
            return true;
        }
        catch (Exception e)
        {
            status = $"Could not read the script: {e.Message}";
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Editor coroutine pump
    //
    // The pipeline is runtime code driven by Unity's coroutine scheduler, which
    // doesn't run outside Play Mode — so step the IEnumerator by hand off
    // EditorApplication.update. It yields null, nested IEnumerators, and (in the
    // forced-alignment call) a UnityWebRequestAsyncOperation; those three are
    // all this needs to understand.
    // -----------------------------------------------------------------------

    class Pump
    {
        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        AsyncOperation pending;
        Action onTick;

        public bool Running => stack.Count > 0;

        public void Start(IEnumerator routine, Action repaint)
        {
            Stop();
            onTick = repaint;
            stack.Push(routine);
            EditorApplication.update += Tick;
        }

        public void Stop()
        {
            EditorApplication.update -= Tick;
            stack.Clear();
            pending = null;
        }

        void Tick()
        {
            if (pending != null)
            {
                if (!pending.isDone) return;
                pending = null;
            }

            if (stack.Count == 0) { Stop(); onTick?.Invoke(); return; }

            IEnumerator top = stack.Peek();
            bool moved;
            try { moved = top.MoveNext(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Stop();
                onTick?.Invoke();
                return;
            }

            if (!moved)
            {
                stack.Pop();
                if (stack.Count == 0) { Stop(); onTick?.Invoke(); }
                return;
            }

            switch (top.Current)
            {
                case IEnumerator nested:      stack.Push(nested); break;
                case AsyncOperation op:       pending = op;       break;
            }
        }
    }
}

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using MugsTech;
using MugsTech.Tts;
using UnityEngine;
using Diag = System.Diagnostics;

/// <summary>
/// One-shot command-line automation for the whole pipeline: script file in,
/// finished mp4 out, no clicks. Activated ONLY when the player is launched
/// with <c>--auto-script &lt;path&gt;</c>; a normal (double-clicked) run never
/// notices this class exists.
///
///   AutoAvatarGen.exe --auto-script C:\jobs\ep12.txt
///                     [--auto-result C:\jobs\ep12.result.json]
///                     [--auto-timeout-sec 5400]
///
/// Flow (mirrors what the menus do, via the same code paths):
///   1. TTS      — TtsGenerationJob renders the script into a fresh
///                 timestamped generation folder under the configured library
///                 root (same folder naming as TtsPanelController).
///   2. Select   — the new generation becomes the active one via the
///                 SelectedGeneration PlayerPref, exactly like the TTS panel.
///   3. Record   — RecordingSession.Begin() runs the take; we wait for its
///                 static LastResult to reach Saved/Failed (the session owns
///                 all mux/recovery logic — nothing is duplicated here).
///   4. Report   — a result JSON (path from --auto-result, default
///                 "&lt;script&gt;.result.json") is written atomically, then the
///                 app quits with exit code 0 (success) or 1 (failure).
///
/// While running, a small progress file ("&lt;result&gt;.progress") is rewritten
/// on every phase change so an external wrapper can show liveness without
/// parsing the player log.
///
/// Settings not covered by the script file (voice, visual style, avatar
/// emotions, output folders, API key) come from the app's saved state
/// (PlayerPrefs), i.e. a run behaves as if the user had pasted the script and
/// pressed Generate + Start themselves.
/// </summary>
public class AutomationRunner : MonoBehaviour
{
    const string ScriptArg  = "--auto-script";
    const string ResultArg  = "--auto-result";
    const string TimeoutArg = "--auto-timeout-sec";

    const float DefaultTimeoutSec = 5400f; // 90 min — TTS + realtime take + mux headroom

    // Kept in sync with ScriptFileReader / MainMenuController. When this pref
    // is 1 the recording scene reads bundled StreamingAssets TTS output and
    // would silently ignore the generation we just rendered — automation must
    // always run against the external library.
    const string UseBundledTtsOutputPrefKey = "AutoAvatarGen.UseBundledTtsOutput";

    string scriptPath;
    string resultPath;
    string progressPath;
    float  timeoutSec = DefaultTimeoutSec;

    DateTime startedUtc;
    float    startedRealtime;
    float    lastProgressWrite = -999f;
    string   lastProgressPhase;

    // Carried from the TTS phase into the final report.
    string generationFolder = "";
    TtsGenerationJob.Result ttsResult;

    // -----------------------------------------------------------------------
    // Bootstrap
    // -----------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        string script = GetArgValue(args, ScriptArg);
        if (string.IsNullOrWhiteSpace(script)) return;   // interactive run — stay dormant

        GameObject host = new GameObject("[AutomationRunner]");
        DontDestroyOnLoad(host);
        host.AddComponent<AutomationRunner>().Configure(args, script);
    }

    static string GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    void Configure(string[] args, string script)
    {
        scriptPath = Path.GetFullPath(script);

        string result = GetArgValue(args, ResultArg);
        resultPath = string.IsNullOrWhiteSpace(result)
            ? scriptPath + ".result.json"
            : Path.GetFullPath(result);
        progressPath = resultPath + ".progress";

        string timeout = GetArgValue(args, TimeoutArg);
        if (!string.IsNullOrWhiteSpace(timeout) &&
            float.TryParse(timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out float t) &&
            t > 0f)
        {
            timeoutSec = t;
        }

        startedUtc      = DateTime.UtcNow;
        startedRealtime = Time.realtimeSinceStartup;

        // The window may sit unfocused behind other work for the whole run —
        // the take must keep playing and capturing regardless.
        Application.runInBackground = true;

        // A stale result/progress pair from an earlier run must never be
        // mistaken for this run's outcome by the wrapper.
        TryDelete(resultPath);
        TryDelete(progressPath);

        Debug.Log($"[Automation] Run started. script='{scriptPath}' result='{resultPath}' " +
                  $"timeout={timeoutSec}s");
        StartCoroutine(RunPipeline());
    }

    // -----------------------------------------------------------------------
    // Pipeline
    // -----------------------------------------------------------------------

    IEnumerator RunPipeline()
    {
        WriteProgress("boot", "Automation starting");
        yield return null;   // let scene-0 objects finish their Start() work

#if !UNITY_EDITOR
        // Two instances would fight over PlayerPrefs, the output folders and
        // the GPU encoder. Refuse to start rather than corrupt a take.
        if (AnotherInstanceRunning())
        {
            Finish(false, "setup", "Another instance of the app is already running. " +
                                   "Close it and retry.");
            yield break;
        }
#endif

        // ---- setup: script text -----------------------------------------
        string scriptText;
        try
        {
            scriptText = File.ReadAllText(scriptPath);
        }
        catch (Exception e)
        {
            Finish(false, "setup", $"Cannot read script file '{scriptPath}': {e.Message}");
            yield break;
        }
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            Finish(false, "setup", $"Script file '{scriptPath}' is empty.");
            yield break;
        }

        // ---- setup: API key ---------------------------------------------
        string apiKey = TtsApiKeyPopup.LoadKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Finish(false, "setup",
                "No ElevenLabs API key. Open the app once and save it via the TTS panel's " +
                "Edit Key popup, or set the ELEVENLABS_API_KEY environment variable.");
            yield break;
        }

        // ---- setup: force the external output library --------------------
        if (PlayerPrefs.GetInt(UseBundledTtsOutputPrefKey, 0) == 1)
        {
            Debug.LogWarning("[Automation] 'Use bundled TTS output' was ON — turning it OFF " +
                             "so this run records the generation it just rendered.");
            PlayerPrefs.SetInt(UseBundledTtsOutputPrefKey, 0);
            PlayerPrefs.Save();
        }

        // ---- phase 1: TTS -------------------------------------------------
        // Same per-generation folder naming as TtsPanelController.StartJob so
        // the output library lists automated runs alongside manual ones.
        string outFolder = PlayerPrefs.GetString(
            MainMenuController.PythonOutputFolderPrefKey,
            MainMenuController.DefaultPythonOutputFolder);
        string rootAbs    = TtsGenerationJob.ResolveOutputFolder(outFolder);
        string baseFolder = Path.Combine(rootAbs, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        generationFolder  = baseFolder;
        for (int n = 1; Directory.Exists(generationFolder); n++)
            generationFolder = $"{baseFolder}_{n}";

        WriteProgress("tts", "Rendering script through ElevenLabs");

        ttsResult = null;
        var cfg = new TtsGenerationJob.Config
        {
            ApiKey       = apiKey,
            OutputFolder = generationFolder,
            ScriptText   = scriptText,
            DryRun       = false,
        };
        var job = new TtsGenerationJob(cfg,
            progress: p => WriteProgress("tts", $"Rendering — {Mathf.RoundToInt(p * 100f)}%"),
            status:   s => WriteProgress("tts", s),
            complete: r => ttsResult = r);
        CoroutineHost.Instance.StartCoroutine(job.Run());

        while (ttsResult == null)
        {
            if (TimedOut()) { FinishTimeout("tts"); yield break; }
            yield return null;
        }

        if (!ttsResult.Success)
        {
            Finish(false, "tts", ttsResult.ErrorMessage ?? "TTS generation failed.");
            yield break;
        }
        if (ttsResult.StalledSegments != null && ttsResult.StalledSegments.Count > 0)
        {
            // Audio exists but its word markers drifted — recording it would
            // burn a take on cards that miss their cues. Fail loudly instead
            // of silently re-spending ElevenLabs credits on an auto-retry.
            Finish(false, "tts",
                "TTS finished but these segments stalled (markers won't line up): " +
                string.Join(", ", ttsResult.StalledSegments) +
                ". Re-run the job; eleven_v3 is non-deterministic so a retry usually clears it.");
            yield break;
        }

        // ---- phase 2: make the fresh generation the active one ------------
        PlayerPrefs.SetString(TtsPanelController.SelectedGenerationPrefKey, generationFolder);
        PlayerPrefs.Save();
        Debug.Log($"[Automation] TTS done ({ttsResult.SegmentsProcessed}/{ttsResult.SegmentsTotal} " +
                  $"segments) -> '{generationFolder}'. Starting recording.");

        // ---- phase 3: record ---------------------------------------------
        WriteProgress("recording", "Recording take (realtime)");

        // LastResult is static and survives across runs in one process, so
        // remember what it pointed at before Begin() — only a NEW terminal
        // result belongs to this take.
        RecordingSession.RecordingResult stale = RecordingSession.LastResult;
        RecordingSession.Begin();

        while (true)
        {
            if (TimedOut()) { FinishTimeout("recording"); yield break; }

            RecordingSession.RecordingResult r = RecordingSession.LastResult;
            if (r != null && !ReferenceEquals(r, stale))
            {
                if (r.State == RecordingSession.RecordingResult.Status.Generating)
                    WriteProgress("finalizing", "Take done — muxing video");
                else
                    break;   // Saved or Failed — terminal
            }
            yield return null;
        }

        RecordingSession.RecordingResult rec = RecordingSession.LastResult;
        if (rec.State != RecordingSession.RecordingResult.Status.Saved)
        {
            Finish(false, "recording",
                string.IsNullOrEmpty(rec.ErrorMessage) ? "Recording failed." : rec.ErrorMessage);
            yield break;
        }
        if (string.IsNullOrEmpty(rec.SavePath) || !File.Exists(rec.SavePath))
        {
            Finish(false, "recording",
                $"Recording reported saved, but no file exists at '{rec.SavePath}'.");
            yield break;
        }

        Finish(true, "done", null, rec.SavePath);
    }

    bool TimedOut() => Time.realtimeSinceStartup - startedRealtime > timeoutSec;

    void FinishTimeout(string stage)
        => Finish(false, stage,
            $"Timed out after {timeoutSec:0}s during '{stage}'. " +
            "Raise --auto-timeout-sec for long scripts.");

    static bool AnotherInstanceRunning()
    {
        try
        {
            var current = Diag.Process.GetCurrentProcess();
            return Diag.Process.GetProcessesByName(current.ProcessName).Length > 1;
        }
        catch
        {
            return false;   // detection is best-effort — never block a run on it
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // -----------------------------------------------------------------------
    // Reporting
    // -----------------------------------------------------------------------

    // Rewritten on phase changes and at most once per second within a phase,
    // so the TTS per-frame progress callbacks don't hammer the disk.
    void WriteProgress(string phase, string detail)
    {
        bool phaseChanged = phase != lastProgressPhase;
        if (!phaseChanged && Time.realtimeSinceStartup - lastProgressWrite < 1f) return;
        lastProgressPhase = phase;
        lastProgressWrite = Time.realtimeSinceStartup;

        var sb = new StringBuilder(256);
        sb.Append("{\n");
        sb.Append("  \"phase\": ").Append(JsonStr(phase)).Append(",\n");
        sb.Append("  \"detail\": ").Append(JsonStr(detail)).Append(",\n");
        sb.Append("  \"elapsedSec\": ").Append(F(Time.realtimeSinceStartup - startedRealtime)).Append(",\n");
        sb.Append("  \"updatedUtc\": ").Append(JsonStr(DateTime.UtcNow.ToString("o"))).Append('\n');
        sb.Append("}\n");
        try { File.WriteAllText(progressPath, sb.ToString(), new UTF8Encoding(false)); }
        catch { }   // progress is advisory — never fail the run over it
    }

    void Finish(bool ok, string stage, string error, string videoPath = null)
    {
        long videoSize = 0;
        if (ok && !string.IsNullOrEmpty(videoPath))
        {
            try { videoSize = new FileInfo(videoPath).Length; } catch { }
        }

        var sb = new StringBuilder(2048);
        sb.Append("{\n");
        sb.Append("  \"schema\": \"autoavatargen.automation/1\",\n");
        sb.Append("  \"success\": ").Append(ok ? "true" : "false").Append(",\n");
        sb.Append("  \"stage\": ").Append(JsonStr(stage)).Append(",\n");
        sb.Append("  \"error\": ").Append(JsonStr(error)).Append(",\n");
        sb.Append("  \"scriptPath\": ").Append(JsonStr(scriptPath)).Append(",\n");
        sb.Append("  \"generationFolder\": ").Append(JsonStr(generationFolder)).Append(",\n");
        sb.Append("  \"manifestPath\": ").Append(JsonStr(ttsResult != null ? ttsResult.ManifestPath : null)).Append(",\n");
        sb.Append("  \"segmentsProcessed\": ").Append(ttsResult != null ? ttsResult.SegmentsProcessed : 0).Append(",\n");
        sb.Append("  \"segmentsTotal\": ").Append(ttsResult != null ? ttsResult.SegmentsTotal : 0).Append(",\n");
        sb.Append("  \"videoPath\": ").Append(JsonStr(videoPath)).Append(",\n");
        sb.Append("  \"videoSizeBytes\": ").Append(videoSize).Append(",\n");

        // Folder-mode background music (baked into the take by
        // BackgroundMusicPlayer): what played, where the credits sidecar is,
        // and — the loud flag — whether music failed while the take still
        // recorded. All null/empty when the Music folder pref is unset.
        sb.Append("  \"musicFolder\": ")
          .Append(JsonStr(MugsTech.Background.MusicTakeLog.FolderConfigured)).Append(",\n");
        sb.Append("  \"musicTracks\": [");
        var musicTracks = MugsTech.Background.MusicTakeLog.PlayedFileNames();
        for (int i = 0; i < musicTracks.Count; i++)
        {
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    ").Append(JsonStr(musicTracks[i]));
        }
        sb.Append(musicTracks.Count > 0 ? "\n  ],\n" : "],\n");
        sb.Append("  \"musicCreditsPath\": ")
          .Append(JsonStr(MugsTech.Background.MusicTakeLog.CreditsPath)).Append(",\n");
        sb.Append("  \"musicVolume\": ")
          .Append(F(MugsTech.Background.MusicTakeLog.DuckVolume)).Append(",\n");
        sb.Append("  \"musicError\": ")
          .Append(JsonStr(MugsTech.Background.MusicTakeLog.Error)).Append(",\n");

        // {Timestamp:"..."} chapter markers captured during the take — handed
        // through so the caller can build YouTube chapters without hunting for
        // the persistentDataPath JSON.
        sb.Append("  \"timestampsPath\": ").Append(JsonStr(TimestampMarkerLog.DiskPath)).Append(",\n");
        sb.Append("  \"timestamps\": [");
        var markers = TimestampMarkerLog.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    { \"label\": ").Append(JsonStr(markers[i].Label))
              .Append(", \"seconds\": ").Append(F(markers[i].Seconds)).Append(" }");
        }
        sb.Append(markers.Count > 0 ? "\n  ],\n" : "],\n");

        sb.Append("  \"playerLogPath\": ").Append(JsonStr(Application.consoleLogPath)).Append(",\n");
        sb.Append("  \"startedUtc\": ").Append(JsonStr(startedUtc.ToString("o"))).Append(",\n");
        sb.Append("  \"finishedUtc\": ").Append(JsonStr(DateTime.UtcNow.ToString("o"))).Append(",\n");
        sb.Append("  \"elapsedSec\": ").Append(F(Time.realtimeSinceStartup - startedRealtime)).Append('\n');
        sb.Append("}\n");

        // Atomic-enough hand-off: the wrapper only ever sees a complete file.
        try
        {
            string tmp = resultPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(resultPath)) File.Delete(resultPath);
            File.Move(tmp, resultPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Automation] Could not write result file '{resultPath}': {e}");
        }

        WriteProgress(ok ? "done" : "failed", ok ? videoPath : error);
        if (ok) Debug.Log($"[Automation] SUCCESS — video at '{videoPath}'.");
        else    Debug.LogError($"[Automation] FAILED at '{stage}': {error}");

        PlayerPrefs.Save();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(ok ? 0 : 1);
#endif
    }

    // ---- tiny JSON emit helpers (same conventions as TtsGenerationJob) ----

    static string JsonStr(string s)
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

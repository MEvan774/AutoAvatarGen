using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Evereal.VideoCapture;
using Diag = System.Diagnostics;

/// <summary>
/// Persistent owner of a single recording run. Spawned by the main menu when the
/// user presses "Start Recording", survives across the scene swap into the
/// recording scene, hosts a Screen-Space-Overlay "recording in progress" wheel
/// (so it is NOT captured by Evereal when the recorder is in Camera source
/// mode — overlay UI is excluded from camera captures), subscribes to the
/// VideoCapture OnComplete/OnError events, and routes the result back to the
/// main menu.
///
/// Note on the loading wheel: this works out-of-the-box because
/// CrossPlatformRecorder defaults to RecordingSource.Camera. If you switch it
/// to RecordingSource.Screen, the overlay WILL end up in the recording.
/// </summary>
public class RecordingSession : MonoBehaviour
{
    public const string RecordingSceneName = "SampleScene";
    public const string MainMenuSceneName  = "MainMenu";

    public class RecordingResult
    {
        public enum Status { Generating, Saved, Failed }
        public Status State = Status.Generating;
        public string SavePath;
        public string ErrorMessage;

        // Convenience for callers that only need a simple success/failure read.
        public bool Success => State == Status.Saved;
    }

    public static RecordingSession Instance { get; private set; }
    public static RecordingResult LastResult { get; private set; }

    /// <summary>
    /// Fired whenever <see cref="LastResult"/> changes (recording stopped and is
    /// generating, saved, failed). UI code can subscribe to refresh itself
    /// without polling.
    /// </summary>
    public static event System.Action ResultChanged;

    static void RaiseResultChanged()
    {
        var handler = ResultChanged;
        if (handler == null) return;
        try { handler(); }
        catch (System.Exception e)
        {
            Debug.LogError("[RecordingSession] ResultChanged handler threw: " + e);
        }
    }

    VideoCapture capture;
    bool subscribed;
    bool handedOff;   // capture has stopped and we've returned to the main menu
    bool finished;    // final result (saved/failed) has been delivered; session may be destroyed
    bool finalizing;          // OnComplete arrived; FinalizeAfterMux owns completion now
    bool muxRecoveryStarted;  // watchdog self-mux fallback owns completion now

    // The Evereal capture rig (the VideoCapture prefab's root GameObject) is
    // carried across the scene swap back to the main menu so its encoder
    // thread callbacks, muxer and event-delivering Update() keep running while
    // the video finalizes. Destroyed in OnDestroy with the session.
    GameObject preservedCaptureRoot;

    // Exact output paths snapshotted at hand-off time, while the encoder
    // objects are still alive, so the fallback mux never has to guess via
    // folder scans.
    string pendingVideoTempPath;   // encoder's video file (silent on wav+mux paths)
    string pendingWavPath;         // AudioRecorder's wav (null on GPU-native audio path)
    string pendingFinalVideoPath;  // where Evereal's muxer puts the finished file

    GameObject indicatorRoot;
    Image spinnerImage;
    Image recDot;
    Text headerText;
    Text percentText;

    // Progress tracking — percent is audio-playback-driven: (voiceAudio.time /
    // voiceAudio.clip.length). Cached so we can latch to 100% during the
    // "generating" phase after the audio stops.
    AudioSource voiceAudio;
    float displayedPercent;

    static Sprite cachedSolidSprite;
    static Sprite cachedRingSprite;
    static Sprite cachedDiskSprite;

    public static void Begin()
    {
        // Tear down any leftover session before starting a new one. A previous
        // take can still be alive here if it's finalizing a video in the
        // background, or if it got stuck because Evereal never raised
        // OnComplete/OnError (the watchdog otherwise keeps the session around
        // for up to 180 s). The Start button only exists on the main menu, so
        // reaching this point always means a fresh take is wanted — never let a
        // stale session silently block it. This is what lets the user record
        // repeatedly without restarting the app.
        if (Instance != null)
            Instance.DisposeSession();

        GameObject host = new GameObject("RecordingSession");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<RecordingSession>();
        SceneManager.LoadScene(RecordingSceneName);
    }

    // Fully tears down this session right now, so a new one can take its place.
    // Stops coroutines (watchdog / finalize), drops capture subscriptions,
    // removes the overlay, and clears the singleton BEFORE the replacement
    // session's Awake runs (otherwise Awake would see a non-null Instance that
    // isn't itself and destroy the new session instead).
    void DisposeSession()
    {
        finished = true;            // neutralise any late OnComplete/OnError
        StopAllCoroutines();
        UnsubscribeFromCapture();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (indicatorRoot != null) { Destroy(indicatorRoot); indicatorRoot = null; }
        if (Instance == this) Instance = null;
        Destroy(gameObject);
    }

    // If the game is started with the recording scene already active (typical
    // editor workflow: press Play while SampleScene is open), nobody will have
    // called Begin() — no RecordingSession exists, and when the video ends
    // there is no one to load the main menu, so the user gets stuck in the
    // recording scene. Auto-spawn one here so the return path is guaranteed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrapInRecordingScene()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name != RecordingSceneName) return;
        GameObject host = new GameObject("RecordingSession");
        DontDestroyOnLoad(host);
        host.AddComponent<RecordingSession>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildIndicator();
        SetIndicatorVisible(false);
        SceneManager.sceneLoaded += HandleSceneLoaded;

        // sceneLoaded does NOT fire for a scene that is already active when we
        // subscribe (auto-bootstrap path). Run the same setup manually so the
        // indicator shows and we subscribe to capture events.
        if (SceneManager.GetActiveScene().name == RecordingSceneName)
            EnterRecordingScene();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeFromCapture();
        // The indicator canvas is a separate DontDestroyOnLoad object, so it
        // isn't taken down with this GameObject automatically — destroy it here
        // so each finished take doesn't leave an orphan overlay canvas behind.
        if (indicatorRoot != null) { Destroy(indicatorRoot); indicatorRoot = null; }
        // Same for the preserved capture rig — every terminal path destroys
        // this session, so tearing the rig down here guarantees a stale
        // VideoCapture (and its AudioRecorder/FFmpegMuxer singletons) never
        // leaks into the next take.
        if (preservedCaptureRoot != null) { Destroy(preservedCaptureRoot); preservedCaptureRoot = null; }
        if (Instance == this) Instance = null;
    }

    // -----------------------------------------------------------------------
    // Scene transitions
    // -----------------------------------------------------------------------

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == RecordingSceneName)     EnterRecordingScene();
        else if (scene.name == MainMenuSceneName) LeaveRecordingScene();
    }

    void EnterRecordingScene()
    {
        finished = false;
        handedOff = false;
        finalizing = false;
        muxRecoveryStarted = false;
        pendingVideoTempPath = null;
        pendingWavPath = null;
        pendingFinalVideoPath = null;
        voiceAudio = null;
        displayedPercent = 0f;
        if (percentText != null) percentText.text = "0%";
        if (spinnerImage != null) spinnerImage.fillAmount = 0f;
        SetIndicatorVisible(true);
        StartCoroutine(SubscribeWhenReady());
    }

    void LeaveRecordingScene()
    {
        SetIndicatorVisible(false);
        // IMPORTANT: do NOT destroy here. When we hand off on capture stop we
        // may still be in the Generating state — the session stays alive so
        // OnComplete can still deliver the final file path. Destruction is
        // performed by HandleCaptureComplete / HandleCaptureError / the
        // watchdog timeout / FinishWithFailure instead.
    }

    IEnumerator SubscribeWhenReady()
    {
        const float timeout = 15f;
        float elapsed = 0f;
        VideoCapture found = null;
        while (elapsed < timeout)
        {
            found = FindAnyObjectByType<VideoCapture>();
            if (found != null) break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (found == null)
        {
            Debug.LogError("[RecordingSession] No VideoCapture found in recording scene — returning to menu.");
            FinishWithFailure("VideoCapture component not found in scene");
            yield break;
        }

        capture = found;
        capture.OnComplete += HandleCaptureComplete;
        capture.OnError    += HandleCaptureError;
        subscribed = true;

        // Seed the narration AudioSource reference. UpdateProgressPercent will
        // re-acquire each frame if this initial pick turns out to be null or
        // doesn't have a clip assigned yet (audio is loaded asynchronously, so
        // .clip is typically still null at this point).
        TryDiscoverVoiceAudio();

        // Safety net: even if Evereal fails to raise OnComplete (encoder hiccup,
        // handler exception elsewhere, etc.), we still want to return to the
        // main menu. Watch capture.status instead and force the transition.
        StartCoroutine(WatchdogReturnToMenu());
    }

    IEnumerator WatchdogReturnToMenu()
    {
        // Wait up to 30 s for capture to actually start.
        float startWait = 0f;
        while (!handedOff && startWait < 30f)
        {
            if (capture != null && capture.status == CaptureStatus.STARTED) break;
            startWait += Time.unscaledDeltaTime;
            yield return null;
        }
        if (handedOff) yield break;
        if (capture == null || capture.status != CaptureStatus.STARTED) yield break;

        // Capture is running — wait until its status leaves STARTED. Evereal
        // logs "[VideoCapture] Video capture session stopped, generating video..."
        // from StopCapture() at that exact moment, so this is our reliable cue
        // that the recording itself is done (file finalisation still follows).
        while (!handedOff && capture != null && capture.status == CaptureStatus.STARTED)
            yield return null;
        if (handedOff) yield break;

        // Hand off to the main menu immediately with a "Generating video..."
        // status. The session stays alive (DontDestroyOnLoad) so OnComplete /
        // OnError can still deliver the final save path and update the UI.
        Debug.Log("[RecordingSession] Capture stopped — generating video, returning to main menu.");
        HandOffAsGenerating();

        // With the capture rig preserved across the scene swap, OnComplete
        // normally arrives within seconds. If it never does (encoder hiccup,
        // muxer parked, event lost), don't just fail: the finished silent
        // video and the wav are sitting on disk and ffmpeg is bundled, so mux
        // them ourselves and still deliver one video WITH sound.
        const float finalizeTimeout = 60f;
        float elapsed = 0f;
        while (!finished && !finalizing && elapsed < finalizeTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (finished || finalizing) yield break;

        Debug.LogWarning("[RecordingSession] No completion event after " + finalizeTimeout +
                         "s — attempting manual audio/video mux fallback.");
        yield return SelfMuxRecover();
    }

    // Last-resort finalization when Evereal never raises OnComplete: combine
    // the snapshotted video + wav into the intended final file ourselves.
    IEnumerator SelfMuxRecover()
    {
        muxRecoveryStarted = true;
        // A late OnComplete must not start a second, concurrent finalize pass
        // over the same files.
        UnsubscribeFromCapture();

        // If Evereal's muxer actually finished and only the completion event
        // got lost, the final file on disk is the source of truth.
        if (!string.IsNullOrEmpty(pendingFinalVideoPath) && File.Exists(pendingFinalVideoPath))
        {
            FinishRecovery(true, pendingFinalVideoPath, null);
            yield break;
        }

        string video = pendingVideoTempPath;
        if (string.IsNullOrEmpty(video) || !File.Exists(video))
        {
            FinishRecovery(false, null,
                "Recording produced no video file (expected '" + video + "').");
            yield break;
        }

        // The encoder thread may still be flushing — wait until the file size
        // is stable across two consecutive checks (capped at 30s).
        long lastSize = -1;
        for (float waited = 0f; waited < 30f; waited += 1f)
        {
            long size;
            try { size = new FileInfo(video).Length; }
            catch { size = -1; }
            if (size > 0 && size == lastSize) break;
            lastSize = size;
            yield return new WaitForSecondsRealtime(1f);
        }

        string wav = (!string.IsNullOrEmpty(pendingWavPath) && File.Exists(pendingWavPath))
            ? pendingWavPath
            : FindRecentOrphanWav(Path.GetDirectoryName(video));
        string finalPath = !string.IsNullOrEmpty(pendingFinalVideoPath) ? pendingFinalVideoPath : video;

        if (wav == null)
        {
            // No separate audio file — GPU-native paths bake audio straight
            // into the video. Just give the file its intended final name.
            string dest = finalPath;
            try
            {
                if (!string.Equals(video, dest, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(video, dest);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RecordingSession] Could not rename '{video}' to '{dest}': {e.Message}");
                dest = video; // keep the original location — still a valid take
            }
            FinishRecovery(true, dest, null);
            yield break;
        }

        string ffmpegPath = GetBundledFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            FinishRecovery(false, null,
                $"ffmpeg not found at '{ffmpegPath}'. Video: '{video}', audio: '{wav}'.");
            yield break;
        }

        // If the video already carries sound (GPU-native encoding bakes the
        // audio in directly), the wav we found is stale or belongs to another
        // take — muxing it in would REPLACE the correct audio. Rename only.
        ProcResult probe = new ProcResult();
        yield return RunFfmpeg(ffmpegPath, $"-hide_banner -i \"{video}\"", probe);
        if (probe.started && probe.stderr != null && probe.stderr.Contains("Audio:"))
        {
            Debug.Log("[RecordingSession] Video already has an audio stream — renaming only.");
            string dest = finalPath;
            try
            {
                if (!string.Equals(video, dest, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(video, dest);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RecordingSession] Could not rename '{video}' to '{dest}': {e.Message}");
                dest = video;
            }
            FinishRecovery(true, dest, null);
            yield break;
        }

        string muxedTemp = finalPath + ".muxing.mp4";
        try { if (File.Exists(muxedTemp)) File.Delete(muxedTemp); }
        catch (Exception e)
        {
            FinishRecovery(false, null, $"Could not clear temp file '{muxedTemp}': {e.Message}");
            yield break;
        }

        string ffArgs = string.Format(
            "-y -hide_banner -loglevel warning -i \"{0}\" -i \"{1}\" " +
            "-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -shortest \"{2}\"",
            video, wav, muxedTemp);
        Debug.Log($"[RecordingSession] Mux fallback: '{Path.GetFileName(video)}' + " +
                  $"'{Path.GetFileName(wav)}' -> '{Path.GetFileName(finalPath)}'.");

        ProcResult mux = new ProcResult();
        yield return RunFfmpeg(ffmpegPath, ffArgs, mux);
        if (!mux.started || mux.exitCode != 0 || !File.Exists(muxedTemp))
        {
            try { if (File.Exists(muxedTemp)) File.Delete(muxedTemp); } catch { }
            FinishRecovery(false, null,
                $"Mux fallback failed (ffmpeg exit {mux.exitCode}). stderr: {mux.stderr} " +
                $"Files kept: video '{video}', audio '{wav}'.");
            yield break;
        }

        try
        {
            if (File.Exists(finalPath) &&
                !string.Equals(finalPath, muxedTemp, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(finalPath);
            }
            File.Move(muxedTemp, finalPath);
        }
        catch (Exception e)
        {
            FinishRecovery(false, null, $"Muxed file could not be moved to '{finalPath}': {e.Message}");
            yield break;
        }

        // Cleanup of the now-redundant inputs must never fail the take.
        if (!string.Equals(video, finalPath, StringComparison.OrdinalIgnoreCase))
            yield return DeleteFileWithRetry(video);
        yield return DeleteFileWithRetry(wav);

        FinishRecovery(true, finalPath, null);
    }

    void FinishRecovery(bool ok, string savePath, string error)
    {
        finished = true;
        if (ok)
        {
            Debug.Log($"[RecordingSession] Recovery produced '{savePath}'.");
            LastResult = new RecordingResult
            {
                State = RecordingResult.Status.Saved,
                SavePath = savePath
            };
        }
        else
        {
            Debug.LogError($"[RecordingSession] {error}");
            LastResult = new RecordingResult
            {
                State = RecordingResult.Status.Failed,
                ErrorMessage = error
            };
        }
        RaiseResultChanged();
        Destroy(gameObject); // OnDestroy tears down the preserved rig + indicator
    }

    void HandOffAsGenerating()
    {
        if (handedOff) return;
        handedOff = true;
        // Must run BEFORE the scene swap: loading the main menu destroys the
        // recording scene, and with it the Evereal capture rig whose encoder
        // thread is still finishing the video.
        PreserveCapturePipeline();
        LastResult = new RecordingResult { State = RecordingResult.Status.Generating };
        RaiseResultChanged();
        ReturnToMainMenu();
    }

    // Carries the Evereal capture rig across the scene swap. Destroying it
    // with the recording scene (the old behavior) fires VideoCapture.OnDisable,
    // which unsubscribes the encoder-thread completion callback BEFORE the
    // encoder finishes (~1s after StopCapture). The muxer thread then never
    // receives the video file and parks forever, leaving a silent .mp4 plus an
    // orphan audio_*.wav, and OnComplete never fires — THAT was the
    // split-audio/video bug. The rig must stay ACTIVE (deactivating triggers
    // the same OnDisable), so only the components that would interfere with
    // the main menu are disabled.
    void PreserveCapturePipeline()
    {
        if (capture == null) return;

        // Snapshot the exact output paths while the encoder objects are alive,
        // for the self-mux fallback and the orphan-wav cleanup.
        EncoderBase encoder = capture.GetEncoder();
        pendingVideoTempPath = encoder != null ? encoder.videoSavePath : null;

        if (AudioRecorder.singleton != null)
        {
            string wav = AudioRecorder.singleton.GetRecordedAudio();
            // Recency guard: the singleton can outlive a take, so only trust a
            // wav that was actually written by this session's recording.
            if (!string.IsNullOrEmpty(wav) && File.Exists(wav) &&
                File.GetLastWriteTime(wav) > DateTime.Now.AddMinutes(-10))
            {
                pendingWavPath = wav;
            }
        }

        if (FFmpegMuxer.singleton != null && encoder != null &&
            !string.IsNullOrEmpty(FFmpegMuxer.singleton.customFileName))
        {
            // Mirrors FFmpegMuxer.StartMux's output naming (saveFolderFullPath
            // already ends with a separator).
            pendingFinalVideoPath = FFmpegMuxer.singleton.saveFolderFullPath +
                                    FFmpegMuxer.singleton.customFileName + "." +
                                    Utils.GetEncoderPresetExt(encoder.encoderPreset);
        }

        GameObject root = capture.transform.root.gameObject;
        DontDestroyOnLoad(root);
        preservedCaptureRoot = root;
        foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            cam.enabled = false;
        foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        foreach (AudioSource src in root.GetComponentsInChildren<AudioSource>(true))
            src.Stop();

        Debug.Log("[RecordingSession] Capture rig preserved across scene swap. " +
                  $"video='{pendingVideoTempPath}', audio='{pendingWavPath}', " +
                  $"final='{pendingFinalVideoPath}'.");
    }

    void UnsubscribeFromCapture()
    {
        if (subscribed && capture != null)
        {
            capture.OnComplete -= HandleCaptureComplete;
            capture.OnError    -= HandleCaptureError;
        }
        subscribed = false;
    }

    // -----------------------------------------------------------------------
    // Capture callbacks
    // -----------------------------------------------------------------------

    void HandleCaptureComplete(object sender, CaptureCompleteEventArgs args)
    {
        if (finished || muxRecoveryStarted) return;
        finalizing = true; // the watchdog must not start a concurrent recovery
        Debug.Log($"[RecordingSession] Capture complete: {args.SavePath}");
        UnsubscribeFromCapture();
        // Fold a leftover audio_*.wav (Evereal's intermediate or a failed-mux
        // leftover) back into the .mp4 before we mark the result Saved, so the
        // user only ever sees a single combined file.
        StartCoroutine(FinalizeAfterMux(args.SavePath));
    }

    IEnumerator FinalizeAfterMux(string videoPath)
    {
        yield return CombineOrphanAudioIntoVideo(videoPath);

        if (finished) yield break;
        finished = true;
        LastResult = new RecordingResult
        {
            State = RecordingResult.Status.Saved,
            SavePath = videoPath
        };
        RaiseResultChanged();

        if (!handedOff && SceneManager.GetActiveScene().name == RecordingSceneName)
        {
            handedOff = true;
            ReturnToMainMenu();
        }
        Destroy(gameObject);
    }

    void HandleCaptureError(object sender, CaptureErrorEventArgs args)
    {
        if (finished) return;
        finished = true;
        LastResult = new RecordingResult
        {
            State = RecordingResult.Status.Failed,
            ErrorMessage = args.ErrorCode.ToString()
        };
        Debug.LogError($"[RecordingSession] Capture error: {args.ErrorCode}");
        RaiseResultChanged();
        UnsubscribeFromCapture();

        if (!handedOff && SceneManager.GetActiveScene().name == RecordingSceneName)
        {
            handedOff = true;
            ReturnToMainMenu();
        }
        Destroy(gameObject);
    }

    void FinishWithFailure(string message)
    {
        finished = true;
        handedOff = true;
        LastResult = new RecordingResult
        {
            State = RecordingResult.Status.Failed,
            ErrorMessage = message
        };
        RaiseResultChanged();
        UnsubscribeFromCapture();
        ReturnToMainMenu();
        Destroy(gameObject);
    }

    void ReturnToMainMenu()
    {
        SceneManager.LoadScene(MainMenuSceneName);
    }

    // -----------------------------------------------------------------------
    // Indicator UI — Screen Space Overlay so Camera-source recording skips it
    // -----------------------------------------------------------------------

    void BuildIndicator()
    {
        indicatorRoot = new GameObject("RecordingIndicatorCanvas", typeof(RectTransform));
        DontDestroyOnLoad(indicatorRoot);

        Canvas canvas = indicatorRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = indicatorRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        indicatorRoot.AddComponent<GraphicRaycaster>();

        GameObject plate = new GameObject("Plate", typeof(RectTransform));
        plate.transform.SetParent(indicatorRoot.transform, false);
        Image plateImg = plate.AddComponent<Image>();
        plateImg.sprite = GetSolidSprite();
        plateImg.type   = Image.Type.Simple;
        plateImg.color  = new Color(0f, 0f, 0f, 0.65f);
        RectTransform plateRect = plateImg.rectTransform;
        plateRect.anchorMin = new Vector2(1f, 0f);
        plateRect.anchorMax = new Vector2(1f, 0f);
        plateRect.pivot     = new Vector2(1f, 0f);
        plateRect.anchoredPosition = new Vector2(-40f, 40f);
        plateRect.sizeDelta = new Vector2(560f, 150f);

        GameObject spinObj = new GameObject("Spinner", typeof(RectTransform));
        spinObj.transform.SetParent(plate.transform, false);
        spinnerImage = spinObj.AddComponent<Image>();
        spinnerImage.sprite = GetRingSprite();
        spinnerImage.type       = Image.Type.Filled;
        spinnerImage.fillMethod = Image.FillMethod.Radial360;
        spinnerImage.fillOrigin = (int)Image.Origin360.Top;
        // fillAmount is driven each frame by UpdateProgressPercent — starts
        // empty and grows to a full circle as the take progresses 0%→100%.
        spinnerImage.fillClockwise = true;
        spinnerImage.fillAmount = 0f;
        spinnerImage.color      = new Color(1f, 0.32f, 0.28f, 1f);
        RectTransform spinRect = spinnerImage.rectTransform;
        spinRect.anchorMin = new Vector2(0f, 0.5f);
        spinRect.anchorMax = new Vector2(0f, 0.5f);
        spinRect.pivot     = new Vector2(0.5f, 0.5f);
        spinRect.anchoredPosition = new Vector2(65f, 0f);
        spinRect.sizeDelta = new Vector2(74f, 74f);

        GameObject dotObj = new GameObject("RecDot", typeof(RectTransform));
        dotObj.transform.SetParent(plate.transform, false);
        recDot = dotObj.AddComponent<Image>();
        recDot.sprite = GetDiskSprite();
        recDot.color  = new Color(1f, 0.18f, 0.18f, 1f);
        RectTransform dotRect = recDot.rectTransform;
        dotRect.anchorMin = new Vector2(0f, 0.5f);
        dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot     = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(65f, 0f);
        dotRect.sizeDelta = new Vector2(26f, 26f);

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.transform.SetParent(plate.transform, false);
        headerText = textObj.AddComponent<Text>();
        headerText.text = "RECORDING\nScene capture in progress…";
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.fontSize = 24;
        headerText.color = Color.white;
        headerText.alignment = TextAnchor.MiddleLeft;
        headerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        headerText.verticalOverflow   = VerticalWrapMode.Overflow;
        RectTransform textRect = headerText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(125f, 10f);
        textRect.offsetMax = new Vector2(-140f, -10f);

        // Big percentage on the right — updated each frame in Update() from the
        // currently-playing narration AudioSource. Bold amber to stand apart
        // from the white header text.
        GameObject pctObj = new GameObject("Percent", typeof(RectTransform));
        pctObj.transform.SetParent(plate.transform, false);
        percentText = pctObj.AddComponent<Text>();
        percentText.text = "0%";
        percentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        percentText.fontSize = 44;
        percentText.fontStyle = FontStyle.Bold;
        percentText.color = new Color(1f, 0.85f, 0.35f, 1f);
        percentText.alignment = TextAnchor.MiddleRight;
        percentText.horizontalOverflow = HorizontalWrapMode.Overflow;
        percentText.verticalOverflow   = VerticalWrapMode.Overflow;
        RectTransform pctRect = percentText.rectTransform;
        pctRect.anchorMin = new Vector2(1f, 0.5f);
        pctRect.anchorMax = new Vector2(1f, 0.5f);
        pctRect.pivot     = new Vector2(1f, 0.5f);
        pctRect.anchoredPosition = new Vector2(-18f, 0f);
        pctRect.sizeDelta = new Vector2(130f, 60f);
    }

    void SetIndicatorVisible(bool visible)
    {
        if (indicatorRoot != null) indicatorRoot.SetActive(visible);
    }

    void Update()
    {
        if (indicatorRoot == null || !indicatorRoot.activeSelf) return;

        if (spinnerImage != null)
            spinnerImage.rectTransform.Rotate(0f, 0f, -220f * Time.unscaledDeltaTime);

        if (recDot != null)
        {
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
            Color c = recDot.color;
            c.a = pulse;
            recDot.color = c;
        }

        UpdateProgressPercent();
    }

    // Drives the 0%-100% readout AND the spinner ring fill on the recording
    // indicator. Progress is measured against audio playback (voiceAudio.time
    // / clip.length) so it tracks the actual narrative length the user will
    // see in the output.
    //
    // State machine:
    //   - Before playback: 0%
    //   - While audio playing: monotonically increasing fraction of clip
    //   - After audio stops: latched at the last known value, then snapped to
    //     100% once capture is handed off (the "generating video" phase).
    void UpdateProgressPercent()
    {
        if (percentText == null) return;

        // If our voiceAudio reference is unusable (null, destroyed, or its
        // clip hasn't been assigned yet), try to re-acquire from the systems
        // that actually own the narration AudioSource. The reference cached
        // in SubscribeWhenReady can be stale if the recorder's inspector slot
        // was empty or pointed at a different AudioSource than the one that
        // ultimately gets the clip (HybridAvatarSystem.ProcessWithExistingAudio
        // is what assigns .clip and calls .Play). Without this re-acquisition
        // the percentage sticks at 0% the entire take.
        if (voiceAudio == null || voiceAudio.clip == null)
            TryDiscoverVoiceAudio();

        float pct = displayedPercent;

        if (voiceAudio != null && voiceAudio.clip != null && voiceAudio.clip.length > 0f)
        {
            float fraction = Mathf.Clamp01(voiceAudio.time / voiceAudio.clip.length);
            float live = fraction * 100f;

            if (voiceAudio.isPlaying)
            {
                // Don't go backwards — AudioSource.time can briefly reset to 0
                // on loop/seek boundaries which would cause a visual snap.
                pct = Mathf.Max(displayedPercent, live);
            }
            else if (handedOff || finished)
            {
                pct = 100f;
            }
            else if (live > 0f)
            {
                // Audio paused/ended but still in recording phase — hold where
                // it was rather than reset.
                pct = Mathf.Max(displayedPercent, live);
            }
        }
        else if (handedOff || finished)
        {
            pct = 100f;
        }

        displayedPercent = Mathf.Clamp(pct, 0f, 100f);
        percentText.text = Mathf.RoundToInt(displayedPercent) + "%";

        // Drive the ring fill so the spinner visually loads from empty to full
        // alongside the percent readout.
        if (spinnerImage != null)
            spinnerImage.fillAmount = displayedPercent * 0.01f;
    }

    // Walks the scene for the AudioSource that's actually carrying the
    // narration clip. We prefer the references wired on HybridAvatarSystem /
    // MediaPresentationSystem (which set .clip themselves) over the recorder
    // slot — the recorder's voiceAudio is sometimes left unset in scenes that
    // pre-date that field.
    void TryDiscoverVoiceAudio()
    {
        AudioSource candidate = voiceAudio;

        var avatarSystem = FindAnyObjectByType<HybridAvatarSystem>();
        if (avatarSystem != null && avatarSystem.voiceAudio != null)
            candidate = avatarSystem.voiceAudio;

        if (candidate == null || candidate.clip == null)
        {
            var mediaSystem = FindAnyObjectByType<MediaPresentationSystem>();
            if (mediaSystem != null && mediaSystem.voiceAudio != null &&
                (candidate == null || mediaSystem.voiceAudio.clip != null))
            {
                candidate = mediaSystem.voiceAudio;
            }
        }

        if (candidate == null)
        {
            var recorder = FindAnyObjectByType<CrossPlatformRecorder>();
            if (recorder != null && recorder.voiceAudio != null)
                candidate = recorder.voiceAudio;
        }

        if (candidate != null) voiceAudio = candidate;
    }

    // -----------------------------------------------------------------------
    // Post-capture: fold leftover audio_*.wav into the .mp4 so we end up with
    // a single combined file. Evereal usually handles this, but on some
    // GPU/driver paths its native muxer either silently skips the cleanup or
    // fails outright — this ensures we always converge on one output.
    // -----------------------------------------------------------------------

    IEnumerator CombineOrphanAudioIntoVideo(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            yield break;

        string folder = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            yield break;

        // Prefer the wav path snapshotted at hand-off; fall back to scanning
        // the video's folder for takes that never went through hand-off.
        string orphanWav = (!string.IsNullOrEmpty(pendingWavPath) && File.Exists(pendingWavPath))
            ? pendingWavPath
            : FindRecentOrphanWav(folder);
        if (orphanWav == null) yield break;

        string ffmpegPath = GetBundledFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            Debug.LogWarning(
                $"[RecordingSession] ffmpeg not found at '{ffmpegPath}' — leaving .wav beside the .mp4.");
            yield break;
        }

        // When Evereal's own muxer succeeded (the normal path now that the
        // capture rig survives the scene swap), the video already carries the
        // sound and the leftover wav is just the muxer thread's cleanup still
        // in flight — re-muxing would race that deletion for no benefit.
        ProcResult probe = new ProcResult();
        yield return RunFfmpeg(ffmpegPath, $"-hide_banner -i \"{videoPath}\"", probe);
        if (probe.started && probe.stderr != null && probe.stderr.Contains("Audio:"))
        {
            Debug.Log("[RecordingSession] Video already has an audio stream — no re-mux needed.");
            yield break;
        }

        string muxedTemp = videoPath + ".muxing.mp4";
        try { if (File.Exists(muxedTemp)) File.Delete(muxedTemp); }
        catch (Exception e)
        {
            Debug.LogError($"[RecordingSession] Could not clear temp file '{muxedTemp}': {e.Message}");
            yield break;
        }

        // -map 0:v:0 -map 1:a:0 pins the output to exactly one video + one
        // audio stream so we don't get duplicates even if Evereal already
        // embedded sound.
        string ffArgs = string.Format(
            "-y -hide_banner -loglevel warning -i \"{0}\" -i \"{1}\" " +
            "-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -shortest \"{2}\"",
            videoPath, orphanWav, muxedTemp);

        Debug.Log($"[RecordingSession] Combining '{Path.GetFileName(videoPath)}' + " +
                  $"'{Path.GetFileName(orphanWav)}' into one file.");

        ProcResult mux = new ProcResult();
        yield return RunFfmpeg(ffmpegPath, ffArgs, mux);

        if (!mux.started || mux.exitCode != 0 || !File.Exists(muxedTemp))
        {
            Debug.LogError($"[RecordingSession] ffmpeg mux failed (exit {mux.exitCode}). stderr: {mux.stderr}");
            try { if (File.Exists(muxedTemp)) File.Delete(muxedTemp); } catch { }
            yield break;
        }

        bool swapped = false;
        try
        {
            File.Delete(videoPath);
            File.Move(muxedTemp, videoPath);
            swapped = true;
            Debug.Log($"[RecordingSession] Combined into '{videoPath}'.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RecordingSession] Mux succeeded but swap failed: {e.Message}");
        }
        if (swapped)
            yield return DeleteFileWithRetry(orphanWav);
    }

    // -----------------------------------------------------------------------
    // ffmpeg process helpers
    // -----------------------------------------------------------------------

    class ProcResult
    {
        public bool started;
        public int exitCode = -1;
        public string stderr = "";
    }

    IEnumerator RunFfmpeg(string ffmpegPath, string args, ProcResult result)
    {
        Diag.Process process;
        System.Threading.Tasks.Task<string> stderrTask;
        try
        {
            process = Diag.Process.Start(new Diag.ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            // Drain both pipes asynchronously so ffmpeg can never block on a
            // full pipe buffer while we wait for it to exit.
            stderrTask = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEndAsync();
            result.started = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RecordingSession] Failed to start ffmpeg: {e.Message}");
            yield break;
        }

        float waited = 0f;
        while (!process.HasExited && waited < 180f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!process.HasExited)
        {
            Debug.LogError("[RecordingSession] ffmpeg did not finish within 180s — killing it.");
            try { process.Kill(); } catch { }
            process.Dispose();
            yield break;
        }

        result.exitCode = process.ExitCode;
        try { result.stderr = stderrTask.Result; } catch { result.stderr = ""; }
        process.Dispose();
    }

    // Deleting leftover inputs must never fail a take — the wav can still be
    // briefly locked by Evereal's muxer thread. Retry a few times, then leave
    // the file in place with a warning.
    IEnumerator DeleteFileWithRetry(string path)
    {
        if (string.IsNullOrEmpty(path)) yield break;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool done = false;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                done = true;
            }
            catch { }
            if (done) yield break;
            yield return new WaitForSecondsRealtime(1f);
        }
        Debug.LogWarning($"[RecordingSession] Could not delete '{path}' — leaving it in place.");
    }

    // Evereal writes "audio_<timestamp>_<rand>.wav" next to the video. Pick
    // the newest one written in the last few minutes so we don't accidentally
    // fold in a file from an earlier session.
    static string FindRecentOrphanWav(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;

        string newest = null;
        DateTime newestTime = DateTime.MinValue;
        DateTime cutoff = DateTime.Now.AddMinutes(-5);

        foreach (string wav in Directory.EnumerateFiles(folder, "audio_*.wav"))
        {
            DateTime t = File.GetLastWriteTime(wav);
            if (t < cutoff) continue;
            if (t > newestTime)
            {
                newestTime = t;
                newest = wav;
            }
        }
        return newest;
    }

    static string GetBundledFfmpegPath()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return Path.Combine(Application.streamingAssetsPath, "FFmpeg", "x86_64", "ffmpeg.exe");
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        return Path.Combine(Application.streamingAssetsPath, "FFmpeg", "ffmpeg");
#else
        return "ffmpeg";
#endif
    }

    // -----------------------------------------------------------------------
    // Sprite helpers — generate once, reuse
    // -----------------------------------------------------------------------

    static Sprite GetSolidSprite()
    {
        if (cachedSolidSprite != null) return cachedSolidSprite;
        Texture2D tex = Texture2D.whiteTexture;
        cachedSolidSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return cachedSolidSprite;
    }

    static Sprite GetRingSprite()
    {
        if (cachedRingSprite != null) return cachedRingSprite;
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r      = size * 0.5f;
        float rOuter = r - 2f;
        float rInner = r * 0.66f;
        Color opaque = Color.white;
        Color clear  = new Color(1, 1, 1, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r + 0.5f;
            float dy = y - r + 0.5f;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            tex.SetPixel(x, y, (d <= rOuter && d >= rInner) ? opaque : clear);
        }
        tex.Apply();
        cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return cachedRingSprite;
    }

    static Sprite GetDiskSprite()
    {
        if (cachedDiskSprite != null) return cachedDiskSprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;
        float rOuter = r - 1f;
        Color opaque = Color.white;
        Color clear  = new Color(1, 1, 1, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r + 0.5f;
            float dy = y - r + 0.5f;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            tex.SetPixel(x, y, d <= rOuter ? opaque : clear);
        }
        tex.Apply();
        cachedDiskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return cachedDiskSprite;
    }
}

using System;
using System.IO;
using System.Collections;
using UnityEngine;
using Evereal.VideoCapture;

/// <summary>
/// Drives Evereal VideoCapture (paid asset) for MugsTech scene recording.
/// Takes advantage of paid features: transparent alpha channel, GPU encoding,
/// custom save paths, flip options, and resolution/bitrate controls.
///
/// Usage: drop this on the same GameObject as the VideoCapture component,
/// assign references, and call StartRecordingWithAudio() from your pipeline
/// (already wired up in HybridAvatarSystem when autoRecord is true).
/// </summary>
public class CrossPlatformRecorder : MonoBehaviour
{
    public enum BackgroundMode
    {
        SceneDefault,       // Leave whatever camera clearFlags are already configured
        GreenScreen,        // Solid green (#00FF00) for chroma keying in post
        Transparent,        // Alpha channel (requires MOV output + transparent flag)
        SolidBlack,
    }

    [Header("References")]
    [Tooltip("The Evereal VideoCapture component from the Evereal prefab.")]
    public VideoCapture videoCaptureComponent;
    public AudioSource voiceAudio;

    public enum RecordingSource
    {
        Camera, // Records from a single camera (RegularCamera or custom). Doesn't include Screen Space - Overlay UI.
        Screen, // Records the whole Game view / window — includes every camera's output and all Overlay UI.
    }

    [Header("Source")]
    [Tooltip("Camera = record from one camera (missing Overlay UI). " +
             "Screen = record the whole Game view (includes Overlay UI, but resolution follows the Game window).")]
    public RecordingSource recordingSource = RecordingSource.Camera;

    [Header("Camera")]
    [Tooltip("Only used when Source = Camera. If true, uses whatever camera is already set on the " +
             "VideoCapture prefab (its built-in RegularCamera child). If false, overrides it with " +
             "`targetCamera` below (or Camera.main).")]
    public bool useEverealBuiltInCamera = true;

    [Tooltip("Only used when 'Use Evereal Built-In Camera' is OFF. Leave empty to fall back to Camera.main.")]
    public Camera targetCamera;

    [Header("Background")]
    public BackgroundMode backgroundMode = BackgroundMode.Transparent;

    [Header("Output")]
    [Tooltip("Output folder (relative to project or absolute). Leave empty for the default Evereal folder. " +
             "The main menu's 'Recording Output Folder' field overrides this via PlayerPrefs.")]
    public string saveFolder = "";
    [Tooltip("Optional filename prefix. Timestamp is always appended.")]
    public string fileNamePrefix = "MugsTech";

    // Kept in sync with MainMenuController.RecordingOutputFolderPrefKey. If
    // the pref is set, Awake() overrides the inspector saveFolder before
    // ConfigureVideoCapture() resolves it onto the VideoCapture component.
    const string RecordingOutputFolderPrefKey = "AutoAvatarGen.RecordingOutputFolder";

    [Header("Video Settings")]
    [Tooltip("1920 = 1080p, 1280 = 720p, 3840 = 4K. Must match what Evereal supports.")]
    public int frameWidth = 1920;
    public int frameHeight = 1080;
    [Range(24, 60)]
    public short frameRate = 30;

    [Tooltip("Upper bound on how long StartRecordingWithAudio waits for the video encoder's " +
             "frame pacing to settle before starting the narration. The wait normally ends in " +
             "well under a second (it takes 3 consecutive clean frames after the ffmpeg.exe " +
             "spawn stall); the cap only exists so a misbehaving encoder can't hold a take " +
             "hostage. Raising it is harmless — it is a limit, not a delay.")]
    public float maxCaptureWarmupSeconds = 2f;
    [Tooltip("Kbps. 8000 = broadcast quality, 4000 = YouTube default, 2000 = compact.")]
    public int bitrateKbps = 8000;

    [Header("Encoding (Paid Features)")]
    [Tooltip("Use GPU hardware encoding when available. Much faster than CPU encoding.")]
    public bool gpuEncoding = true;
    [Tooltip("NVIDIA NVENC encoder — requires NVIDIA GPU. Even faster than generic GPU.")]
    public bool nvidiaEncoding = false;

    [Header("Flip Compensation")]
    [Tooltip("Flip output horizontally. Use this if your scene renders mirrored for any reason.")]
    public bool horizontalFlip = false;
    [Tooltip("Flip output vertically.")]
    public bool verticalFlip = false;

    [Header("Capture Audio (optional)")]
    [Tooltip("Let Evereal capture audio into the video file. If false, audio plays live during capture but isn't saved — useful when you want to mux audio separately.")]
    public bool captureAudioIntoVideo = true;

    [Header("Live Preview")]
    [Tooltip("Show the recording on the Game view via Evereal's screen-blitter camera. " +
             "If false, Display 1 shows 'no cameras rendering' while capturing because " +
             "the Main Camera's output is routed to the recording texture.")]
    public bool showLivePreview = true;

    [Header("Debug")]
    public bool verboseLogging = true;

    void Awake()
    {
        // Smoking-gun log to prove Awake is actually firing in builds. If this
        // line doesn't appear in Player.log, Unity isn't running this script's
        // lifecycle — usually means a missing .meta MonoImporter section or a
        // GUID mismatch between scene and script. Remove once the path-override
        // pipeline is confirmed working.
        Debug.Log("[Recorder] Awake() entered. " +
                  $"GameObject='{gameObject.name}', activeInHierarchy={gameObject.activeInHierarchy}, " +
                  $"verboseLogging={verboseLogging}, saveFolder='{saveFolder}'.");

        ResolveTargetCamera();

        // The main-menu "Background recording mode" selector writes a
        // PlayerPref that ranks above the inspector default. This way the
        // user can flip Video / Green Screen / Transparent at runtime
        // without re-saving the scene.
        ApplyBackgroundModeOverrideFromPrefs();

        // Same pattern for the recording output folder — the main menu's
        // input field writes a PlayerPref that overrides the inspector
        // saveFolder. Must run before ConfigureVideoCapture() so the
        // override is in place when it resolves the path.
        ApplyRecordingOutputFolderOverrideFromPrefs();

        ApplyBackgroundMode();
        ConfigureVideoCapture();
    }

    // Picks the camera ApplyBackgroundMode operates on. If using the Evereal
    // prefab's built-in camera, read it from the component so the background
    // is applied to the camera that actually records.
    void ResolveTargetCamera()
    {
        if (useEverealBuiltInCamera && videoCaptureComponent != null)
        {
            targetCamera = videoCaptureComponent.regularCamera;
            if (targetCamera == null)
            {
                Debug.LogWarning(
                    "[Recorder] 'Use Evereal Built-In Camera' is ON but the VideoCapture's " +
                    "regularCamera is empty. Falling back to Camera.main.");
                targetCamera = Camera.main;
            }
        }
        else if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("[Recorder] No camera found! Assign one or add a Camera tagged 'MainCamera'.");
        }
    }

    // Reads the main menu's "Recording Output Folder" PlayerPref and, if
    // set, overrides the inspector saveFolder. Empty / whitespace leaves the
    // inspector value alone so users who never touch the field keep their
    // existing setup. The path-rooted-vs-relative resolution happens later
    // in ConfigureVideoCapture() — this method only swaps the string.
    //
    // Logging here is UNCONDITIONAL (not gated by verboseLogging) because
    // "my recordings landed somewhere else" is a top-3 user-reported issue
    // and Player.log needs to surface what the override actually did.
    void ApplyRecordingOutputFolderOverrideFromPrefs()
    {
        string overrideFolder = PlayerPrefs.GetString(RecordingOutputFolderPrefKey, "");
        if (string.IsNullOrWhiteSpace(overrideFolder))
        {
            Debug.Log($"[Recorder] No recording output override from menu " +
                      $"(pref '{RecordingOutputFolderPrefKey}' is empty). " +
                      $"Using inspector saveFolder='{saveFolder}'.");
            return;
        }
        Debug.Log($"[Recorder] Recording output folder override from menu: " +
                  $"'{overrideFolder}' (was inspector value '{saveFolder}').");
        saveFolder = overrideFolder;
    }

    // Resolves the current saveFolder (absolute or relative-to-project) and
    // assigns it to videoCaptureComponent.saveFolder so Evereal writes the
    // .mp4 there. Idempotent — safe to call multiple times. Both the Awake
    // path (ConfigureVideoCapture) and the StartRecordingWithAudio path call
    // this; the latter is the one that actually fires in this project.
    void ApplySaveFolderToVideoCapture()
    {
        if (videoCaptureComponent == null) return;
        if (string.IsNullOrEmpty(saveFolder))
        {
            Debug.Log("[Recorder] saveFolder is empty — leaving the Evereal " +
                      "VideoCapture inspector folder in place.");
            return;
        }
        string resolvedPath = Path.IsPathRooted(saveFolder)
            ? saveFolder
            : Path.Combine(Application.dataPath, "..", saveFolder);
        Directory.CreateDirectory(resolvedPath);
        videoCaptureComponent.saveFolder = resolvedPath;
        Debug.Log($"[Recorder] VideoCapture.saveFolder set to: '{resolvedPath}' " +
                  $"(from saveFolder='{saveFolder}', rooted={Path.IsPathRooted(saveFolder)}).");
    }

    // Reads MugsTech.Background.BackgroundModeManager's PlayerPref and, if
    // the user picked Green Screen / Transparent, overrides the inspector
    // backgroundMode. Normal mode leaves the inspector value alone — same
    // inspector setup as before for users who never open the menu option.
    void ApplyBackgroundModeOverrideFromPrefs()
    {
        var mode = MugsTech.Background.BackgroundModeManager.LoadMode();
        switch (mode)
        {
            case MugsTech.Background.BackgroundModeManager.Mode.GreenScreen:
                backgroundMode = BackgroundMode.GreenScreen;
                Log("Background mode override from menu: GreenScreen");
                break;
            case MugsTech.Background.BackgroundModeManager.Mode.Transparent:
                backgroundMode = BackgroundMode.Transparent;
                Log("Background mode override from menu: Transparent");
                break;
            // Video mode → inspector value is the source of truth, no override.
        }
    }


    // -----------------------------------------------------------------------
    // Public entry point — called by HybridAvatarSystem on playback start.
    // -----------------------------------------------------------------------

    public void StartRecordingWithAudio()
    {
        if (voiceAudio == null)
        {
            Debug.LogError("[Recorder] No AudioSource assigned!");
            return;
        }
        if (videoCaptureComponent == null)
        {
            Debug.LogError("[Recorder] VideoCapture component not assigned!");
            return;
        }

        // Apply the FULL configuration RIGHT HERE, not in Awake(). On this
        // project Awake() doesn't fire for the recorder (some Unity import
        // quirk we haven't been able to pin down — see the absent "Awake()
        // entered" log even with the smoking-gun Debug.Log in place).
        // StartRecordingWithAudio IS called directly by HybridAvatarSystem via
        // a serialized reference though, so configuring from inside this
        // method works. Until this ran the whole inspector setup — GPU
        // encoding, resolution, bitrate, background mode, captureAudio — was
        // silently ignored and Evereal recorded with its prefab defaults.
        // Everything below is idempotent, and runs before SetCustomFileName /
        // StartCapture so Evereal picks it all up when it resolves
        // saveFolderFullPath in its PrepareCapture.
        ApplyRecordingOutputFolderOverrideFromPrefs();
        ApplyBackgroundModeOverrideFromPrefs();
        ResolveTargetCamera();
        ApplyBackgroundMode();
        ConfigureVideoCapture(); // ends with ApplySaveFolderToVideoCapture()

        // Set a unique timestamped filename for this take. Format:
        //   <videoTitle>_<yyyy-MM-dd_HH-mm-ss>.mp4
        // The prefix is the ElevenLabs segment slug (set by ScriptFileReader
        // on auto-load) and stands in as the video title.
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = string.IsNullOrEmpty(fileNamePrefix)
            ? stamp
            : $"{fileNamePrefix}_{stamp}";
        videoCaptureComponent.SetCustomFileName(fileName);

        Log($"=== STARTING RECORDING ({fileName}) ===");

        // Lock engine framerate to recording framerate. On a high-refresh
        // monitor (144Hz/240Hz) the engine would otherwise render way more
        // frames than the encoder samples — wasted GPU work + uneven frame
        // pacing as the encoder picks 1-of-N. Forcing target FPS = recording
        // FPS and disabling vsync gives the encoder a steady stream that
        // matches its sample rate exactly. Stashed for restore in StopCapture.
        PushFrameRateLock(frameRate);

        // Playback must NOT begin on the same frame as StartCapture. StartCapture
        // stalls the main thread for however long the ffmpeg.exe encoder takes to
        // spawn (~0.7s measured on this machine), and the encoder pads the video's
        // head with catch-up frames for that stall — while the audio track begins
        // at Play(). The native muxer butts both streams together from zero, so a
        // take started the old way (StartCapture(); Play();) has every visual
        // land ~0.7s AFTER its narration in the finished mp4 — "the zoom fires a
        // word late". Stop any caller-started playback first (HybridAvatarSystem
        // Play()s before handing us control), warm the encoder up until frame
        // pacing is stable, and only then start the narration.
        voiceAudio.Stop();
        videoCaptureComponent.StartCapture();

        // Coroutines must run on an active GameObject. If our own is inactive
        // (e.g. this recorder is parented to Main Camera and Main Camera is
        // disabled), fall back to any active host among our refs.
        MonoBehaviour host = GetCoroutineHost();
        if (host != null)
        {
            host.StartCoroutine(PlayWhenCaptureWarm());
        }
        else
        {
            Debug.LogError(
                "[Recorder] Cannot start capture warm-up coroutine — no active GameObject available. " +
                "Move CrossPlatformRecorder to its own GameObject (not Main Camera if it's disabled), " +
                "or enable the Main Camera. Starting playback immediately; expect the recording's " +
                "visuals to lag the narration by the encoder spawn time.");
            voiceAudio.Play();
        }
    }

    /// <summary>
    /// Waits until the video encoder's frame pacing has settled (a few
    /// consecutive clean frames — the frame that spawned ffmpeg.exe carries the
    /// whole stall in its deltaTime), then starts the narration and the
    /// stop-on-audio-end watchdog. Keeping the narration out of the encoder's
    /// warm-up padding is what keeps audio and video aligned in the muxed file;
    /// the idle frames recorded before Play() are the same on both tracks, so
    /// they cost nothing but a moment of stillness at the head of the take.
    /// </summary>
    IEnumerator PlayWhenCaptureWarm()
    {
        float cleanFrame = 1.5f / Mathf.Max(1, frameRate);
        int stable = 0;
        float waited = 0f;

        while (stable < 3 && waited < maxCaptureWarmupSeconds)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
            stable = (Time.unscaledDeltaTime <= cleanFrame) ? stable + 1 : 0;
        }

        Log(waited >= maxCaptureWarmupSeconds
            ? $"Capture warm-up hit the {maxCaptureWarmupSeconds:F1}s cap — starting playback anyway."
            : $"Capture warm after {waited:F2}s — starting playback.");

        voiceAudio.Play();
        yield return StopWhenAudioEnds();
    }

    /// <summary>
    /// Returns an active MonoBehaviour that can host coroutines. Prefers `this`;
    /// falls back to voiceAudio, then videoCaptureComponent. Returns null if none
    /// are on active GameObjects — caller should warn the user.
    /// </summary>
    private MonoBehaviour GetCoroutineHost()
    {
        if (isActiveAndEnabled) return this;;
        if (videoCaptureComponent != null && videoCaptureComponent.isActiveAndEnabled) return videoCaptureComponent;
        return null;
    }

    public void StopRecording()
    {
        if (videoCaptureComponent != null && videoCaptureComponent.status == CaptureStatus.STARTED)
        {
            videoCaptureComponent.StopCapture();
            Log("Recording stopped manually");
        }
        PopFrameRateLock();
    }

    // -----------------------------------------------------------------------
    // Framerate lock — caps engine FPS to recording FPS during capture and
    // restores the previous Application.targetFrameRate / QualitySettings
    // vSyncCount when capture stops. Idempotent (push without matching pop
    // would leave the engine locked; we guard with savedTargetFrameRate < int.MinValue).
    // -----------------------------------------------------------------------

    int  savedTargetFrameRate = int.MinValue;
    int  savedVSyncCount      = int.MinValue;

    void PushFrameRateLock(int targetFps)
    {
        if (savedTargetFrameRate != int.MinValue) return; // already pushed
        savedTargetFrameRate    = Application.targetFrameRate;
        savedVSyncCount         = QualitySettings.vSyncCount;
        Application.targetFrameRate = targetFps;
        // vSyncCount must be 0 for targetFrameRate to take effect — Unity
        // silently ignores targetFrameRate when vsync is on.
        QualitySettings.vSyncCount  = 0;
        Log($"Framerate locked: targetFrameRate={targetFps}, vSyncCount=0 " +
            $"(was {savedTargetFrameRate}/{savedVSyncCount}).");
    }

    void PopFrameRateLock()
    {
        if (savedTargetFrameRate == int.MinValue) return; // nothing to restore
        Application.targetFrameRate = savedTargetFrameRate;
        QualitySettings.vSyncCount  = savedVSyncCount;
        Log($"Framerate restored: targetFrameRate={savedTargetFrameRate}, vSyncCount={savedVSyncCount}.");
        savedTargetFrameRate = int.MinValue;
        savedVSyncCount      = int.MinValue;
    }

    // -----------------------------------------------------------------------
    // Setup
    // -----------------------------------------------------------------------

    void ApplyBackgroundMode()
    {
        if (targetCamera == null) return;

        switch (backgroundMode)
        {
            case BackgroundMode.GreenScreen:
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0f, 1f, 0f, 1f);
                Log("Camera: GREEN SCREEN (#00FF00)");
                break;
            case BackgroundMode.Transparent:
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                Log("Camera: TRANSPARENT (alpha)");
                break;
            case BackgroundMode.SolidBlack:
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = Color.black;
                Log("Camera: SOLID BLACK");
                break;
            case BackgroundMode.SceneDefault:
                Log("Camera: scene defaults (unchanged)");
                break;
        }
    }

    void ConfigureVideoCapture()
    {
        if (videoCaptureComponent == null)
        {
            Debug.LogError("[Recorder] VideoCapture component not assigned!");
            return;
        }

        // Capture source: CAMERA (one camera's output) or SCREEN (whole Game view, includes Overlay UI)
        videoCaptureComponent.captureSource = (recordingSource == RecordingSource.Screen)
            ? CaptureSource.SCREEN
            : CaptureSource.CAMERA;
        videoCaptureComponent.captureMode = CaptureMode.REGULAR;

        // Only assign camera when capturing from one (screen capture doesn't use it).
        // When 'useEverealBuiltInCamera' is true, we leave whatever the prefab already has.
        if (recordingSource == RecordingSource.Camera && !useEverealBuiltInCamera)
            videoCaptureComponent.regularCamera = targetCamera;

        // Screen blitter copies the captured texture back to Display 1 so the Game
        // view shows the recording in progress. Without this, the Main Camera's
        // output goes only to the offscreen texture and Display 1 shows "no cameras
        // rendering" while capture is active.
        videoCaptureComponent.screenBlitter = showLivePreview;

        // Transparent alpha channel capture — a paid-only feature.
        // We set camera properties ourselves in ApplyBackgroundMode, so we don't
        // call TransparentCameraSettings() here (which would NRE if stereoCamera
        // isn't assigned — and we don't use stereo capture anyway).
        videoCaptureComponent.transparent = (backgroundMode == BackgroundMode.Transparent);

        // Flip compensation (handy if the scene is rendered mirrored)
        videoCaptureComponent.horizontalFlip = horizontalFlip;
        videoCaptureComponent.verticalFlip = verticalFlip;

        // GPU encoding (paid). On NVIDIA GPUs Evereal routes to its uNvEncoder
        // native plugin WITHOUT any support check, and that plugin only works
        // on D3D11 — under D3D12 (this project uses Auto Graphics API, which
        // is D3D12 on Unity 6/Windows) uNvEncoderCreateEncoder hard-crashes
        // the whole editor/player with an access violation (crash dumps
        // 2026-06-11). The non-NVIDIA GPUEncoder branch has its own
        // IsSupported fallback inside Evereal, so it needs no guard here.
        bool allowGpuEncoding = gpuEncoding;
        if (allowGpuEncoding && SystemInfo.graphicsDeviceVendor == "NVIDIA" &&
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
        {
            Debug.LogWarning("[Recorder] GPU encoding disabled: NVIDIA encoding requires D3D11 but " +
                             $"the current graphics API is {SystemInfo.graphicsDeviceType}. " +
                             "Using software encoding instead.");
            allowGpuEncoding = false;
        }
        videoCaptureComponent.gpuEncoding = allowGpuEncoding;

        // Resolution / framerate / bitrate
        videoCaptureComponent.resolutionPreset = ResolutionPreset.CUSTOM;
        videoCaptureComponent.frameWidth = frameWidth;
        videoCaptureComponent.frameHeight = frameHeight;
        videoCaptureComponent.frameRate = frameRate;
        videoCaptureComponent.bitrate = bitrateKbps;

        // Audio
        videoCaptureComponent.captureAudio = captureAudioIntoVideo;

        // Output folder — delegated so StartRecordingWithAudio can also call
        // it (Awake() doesn't fire on the recorder in this project, so the
        // Awake-time configure path can't be relied on).
        ApplySaveFolderToVideoCapture();

        Log($"VideoCapture configured: {frameWidth}x{frameHeight} @ {frameRate}fps, " +
            $"{bitrateKbps}kbps, transparent={videoCaptureComponent.transparent}, " +
            $"gpu={allowGpuEncoding}, flipH={horizontalFlip}, flipV={verticalFlip}");
    }

    // -----------------------------------------------------------------------
    // Stop logic: wait for audio to finish, then stop capture.
    // -----------------------------------------------------------------------

    IEnumerator StopWhenAudioEnds()
    {
        yield return new WaitForSeconds(0.1f);

        // Wait for the narration AND for anything still on screen. Anything
        // that stops the narration AudioSource mid-script (a {Video:} used to
        // Pause() it) makes isPlaying read FALSE, and waiting on isPlaying alone
        // would then end the capture mid-take and drop every later line.
        // IsShowingMedia is the same guard the tracking loops in
        // MediaPresentationSystem / ContentZoneController use.
        MediaPresentationSystem mediaSystem = FindObjectOfType<MediaPresentationSystem>();

        while (voiceAudio != null &&
               (voiceAudio.isPlaying || (mediaSystem != null && mediaSystem.IsShowingMedia)))
            yield return null;

        // Narration has ended. Tags placed on the script's final word — a
        // closing {Logo:...,D=8} end-card, a final {Black:2}, etc. — are flushed
        // by the tracking loops the moment playback stops. Give them a couple of
        // frames to start their coroutines, then keep recording until every
        // trailing visual has played out its full duration so it isn't clipped.
        yield return null;
        yield return null;

        if (mediaSystem != null)
        {
            const float maxTrailingHold = 30f; // safety cap — never record forever
            float held = 0f;
            while (held < maxTrailingHold && mediaSystem.HasActiveTrailingVisual)
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }
            Log($"Trailing visuals finished after {held:F1}s.");
        }

        yield return new WaitForSeconds(0.5f);

        if (videoCaptureComponent != null && videoCaptureComponent.status == CaptureStatus.STARTED)
        {
            videoCaptureComponent.StopCapture();
            Log("Recording stopped (audio finished + trailing visuals done)");
        }
        // Always restore framerate / vsync — both paths (manual StopRecording
        // and audio-end auto-stop) need this; without the unconditional pop
        // the engine would stay locked at recording FPS after the take ends.
        PopFrameRateLock();
    }

    // -----------------------------------------------------------------------
    // Events
    // -----------------------------------------------------------------------

    void OnEnable()
    {
        if (videoCaptureComponent != null)
        {
            videoCaptureComponent.OnComplete += OnVideoComplete;
            videoCaptureComponent.OnError += OnVideoError;
        }
    }

    void OnDisable()
    {
        if (videoCaptureComponent != null)
        {
            videoCaptureComponent.OnComplete -= OnVideoComplete;
            videoCaptureComponent.OnError -= OnVideoError;
        }
    }

    void OnVideoComplete(object sender, CaptureCompleteEventArgs args)
    {
        Debug.Log($"[Recorder] VIDEO SAVED: {args.SavePath}");
        if (backgroundMode == BackgroundMode.GreenScreen)
            Debug.Log("[Recorder] Green screen recording — apply chroma key in post.");
        else if (backgroundMode == BackgroundMode.Transparent)
            Debug.Log("[Recorder] Transparent recording — alpha channel preserved.");
    }

    void OnVideoError(object sender, CaptureErrorEventArgs args)
    {
        Debug.LogError($"[Recorder] CAPTURE ERROR: {args.ErrorCode}");
    }

    void Log(string msg)
    {
        if (verboseLogging) Debug.Log($"[Recorder] {msg}");
    }
}

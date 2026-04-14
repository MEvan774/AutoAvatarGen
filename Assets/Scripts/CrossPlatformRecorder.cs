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
    [Tooltip("Output folder (relative to project or absolute). Leave empty for the default Evereal folder.")]
    public string saveFolder = "";
    [Tooltip("Optional filename prefix. Timestamp is always appended.")]
    public string fileNamePrefix = "MugsTech";

    [Header("Video Settings")]
    [Tooltip("1920 = 1080p, 1280 = 720p, 3840 = 4K. Must match what Evereal supports.")]
    public int frameWidth = 1920;
    public int frameHeight = 1080;
    [Range(24, 60)]
    public short frameRate = 30;
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
        // If using the Evereal prefab's built-in camera, read it from the component
        // so ApplyBackgroundMode still works on the correct camera.
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

        ApplyBackgroundMode();
        ConfigureVideoCapture();
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

        // Set a unique timestamped filename for this take.
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = string.IsNullOrEmpty(fileNamePrefix)
            ? stamp
            : $"{fileNamePrefix}_{stamp}";
        videoCaptureComponent.SetCustomFileName(fileName);

        Log($"=== STARTING RECORDING ({fileName}) ===");
        videoCaptureComponent.StartCapture();
        voiceAudio.Play();

        // Coroutines must run on an active GameObject. If our own is inactive
        // (e.g. this recorder is parented to Main Camera and Main Camera is
        // disabled), fall back to any active host among our refs.
        MonoBehaviour host = GetCoroutineHost();
        if (host != null)
        {
            host.StartCoroutine(StopWhenAudioEnds());
        }
        else
        {
            Debug.LogError(
                "[Recorder] Cannot start stop-on-audio-end coroutine — no active GameObject available. " +
                "Move CrossPlatformRecorder to its own GameObject (not Main Camera if it's disabled), " +
                "or enable the Main Camera.");
        }
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

        // GPU encoding (paid)
        videoCaptureComponent.gpuEncoding = gpuEncoding;

        // Resolution / framerate / bitrate
        videoCaptureComponent.resolutionPreset = ResolutionPreset.CUSTOM;
        videoCaptureComponent.frameWidth = frameWidth;
        videoCaptureComponent.frameHeight = frameHeight;
        videoCaptureComponent.frameRate = frameRate;
        videoCaptureComponent.bitrate = bitrateKbps;

        // Audio
        videoCaptureComponent.captureAudio = captureAudioIntoVideo;

        // Output folder
        if (!string.IsNullOrEmpty(saveFolder))
        {
            string resolvedPath = Path.IsPathRooted(saveFolder)
                ? saveFolder
                : Path.Combine(Application.dataPath, "..", saveFolder);
            Directory.CreateDirectory(resolvedPath);
            videoCaptureComponent.saveFolder = resolvedPath;
        }

        Log($"VideoCapture configured: {frameWidth}x{frameHeight} @ {frameRate}fps, " +
            $"{bitrateKbps}kbps, transparent={videoCaptureComponent.transparent}, " +
            $"gpu={gpuEncoding}, flipH={horizontalFlip}, flipV={verticalFlip}");
    }

    // -----------------------------------------------------------------------
    // Stop logic: wait for audio to finish, then stop capture.
    // -----------------------------------------------------------------------

    IEnumerator StopWhenAudioEnds()
    {
        yield return new WaitForSeconds(0.1f);
        while (voiceAudio != null && voiceAudio.isPlaying)
            yield return null;
        yield return new WaitForSeconds(0.5f);

        if (videoCaptureComponent != null && videoCaptureComponent.status == CaptureStatus.STARTED)
        {
            videoCaptureComponent.StopCapture();
            Log("Recording stopped (audio finished)");
        }
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

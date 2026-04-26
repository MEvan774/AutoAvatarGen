using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Drop this on a UI GameObject (e.g. BackgroundPanel) to play a looping
/// video as a fullscreen background. Handles all setup automatically:
///   - Adds a VideoPlayer if missing
///   - Swaps Image for RawImage at runtime (video needs RawImage, not Image)
///   - Creates a RenderTexture and wires everything up
///   - Loops forever with no audio (character voice plays over it)
///
/// Source resolution at Start (highest priority first):
///   1. PlayerPrefs override at <see cref="OverridePathPrefKey"/> — set by the
///      main menu's "Background Video" field. Any absolute MP4 path on disk.
///   2. PlayerPrefs preset path at <see cref="PresetPathPrefKey"/> — written
///      by VisualsRuntimeApplier from the active VisualsSave's
///      <c>backgroundVideoPath</c>. Lets a saved preset travel with its own
///      background video while still allowing the main menu's ad-hoc field
///      to win.
///   3. The Inspector field <c>defaultVideoPath</c> — an absolute MP4 path.
///   4. The Inspector field <c>videoClip</c> — a Unity-imported VideoClip asset.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BackgroundVideoLoop : MonoBehaviour
{
    /// <summary>
    /// Shared with MainMenuController. Both keys must match — keep them in sync.
    /// </summary>
    public const string OverridePathPrefKey = "AutoAvatarGen.BackgroundVideoOverride";

    /// <summary>
    /// Written by VisualsRuntimeApplier when an active visuals save carries a
    /// <c>backgroundVideoPath</c>. Cleared on no-active-save. Loses to
    /// <see cref="OverridePathPrefKey"/> so the main menu's field still wins.
    /// </summary>
    public const string PresetPathPrefKey   = "AutoAvatarGen.BackgroundVideoPreset";

    [Header("Video — pick ONE source")]
    [Tooltip("Absolute file path to an MP4. Used as the default if no main-menu " +
             "override is set. Leave empty to fall back to the VideoClip below.")]
    public string defaultVideoPath = "";

    [Tooltip("Unity-imported VideoClip asset, used if no path is set above.")]
    public VideoClip videoClip;

    [Header("Playback")]
    [Tooltip("Playback speed (1 = normal).")]
    [Range(0.1f, 3f)]
    public float playbackSpeed = 1f;

    [Tooltip("Mute the video's audio track (recommended — Mugs talks over it).")]
    public bool muteAudio = true;

    [Header("Resolution")]
    [Tooltip("RenderTexture width. 1920 for 1080p, 1280 for 720p.")]
    public int textureWidth = 1920;
    public int textureHeight = 1080;

    private VideoPlayer videoPlayer;
    private RawImage rawImage;
    private RenderTexture renderTexture;
    private bool isReady;
    private string activeSourceLabel;

    /// <summary>
    /// True once the VideoPlayer has finished Prepare() and has had at least
    /// one frame rendered — safe to start recording at this point so the
    /// output doesn't begin with a blank/half-loaded background frame.
    /// </summary>
    public bool IsReady => isReady;

    void Start()
    {
        Debug.Log($"[BgVideoDiag] BackgroundVideoLoop.Start on '{gameObject.name}' " +
                  $"scene='{gameObject.scene.name}'");
        SetupRawImage();
        SetupVideoPlayer();
        Play();
    }

    void SetupRawImage()
    {
        // If there's an Image component, we need to remove it and add RawImage instead.
        // Image can only display Sprites; RawImage can display any Texture (including RenderTexture).
        rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            // Capture Image's color/raycast settings before removing
            var image = GetComponent<Image>();
            Color color = Color.white;
            bool raycast = false;
            if (image != null)
            {
                color = image.color;
                raycast = image.raycastTarget;
                Destroy(image);
            }

            rawImage = gameObject.AddComponent<RawImage>();
            rawImage.color = color;
            rawImage.raycastTarget = raycast;
        }
    }

    void SetupVideoPlayer()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        if (videoPlayer == null)
            videoPlayer = gameObject.AddComponent<VideoPlayer>();

        renderTexture = new RenderTexture(textureWidth, textureHeight, 0);
        renderTexture.name = "BackgroundVideoRT";

        videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;
        videoPlayer.isLooping     = true;
        videoPlayer.playbackSpeed = playbackSpeed;
        videoPlayer.playOnAwake   = false;
        videoPlayer.skipOnDrop    = true;

        if (muteAudio)
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

        rawImage.texture = renderTexture;

        ApplyResolvedSource();
    }

    /// <summary>Picks the highest-priority source available and configures the VideoPlayer.</summary>
    void ApplyResolvedSource()
    {
        string overridePath = PlayerPrefs.GetString(OverridePathPrefKey, "");
        string presetPath   = PlayerPrefs.GetString(PresetPathPrefKey,   "");
        Debug.Log($"[BgVideoDiag] BackgroundVideoLoop.ApplyResolvedSource " +
                  $"override='{overridePath}' preset='{presetPath}' " +
                  $"defaultPath='{defaultVideoPath}' clip='{(videoClip != null ? videoClip.name : "<null>")}'");

        if (TrySetUrl(overridePath, "user override")) return;
        if (TrySetUrl(presetPath,   "visuals preset")) return;
        if (TrySetUrl(defaultVideoPath, "default path")) return;

        if (videoClip != null)
        {
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip   = videoClip;
            activeSourceLabel  = "VideoClip:" + videoClip.name;
            Debug.Log($"[BgVideoDiag] Resolved to VideoClip:{videoClip.name}");
            return;
        }

        activeSourceLabel = null; // Play() will warn
        Debug.LogWarning("[BgVideoDiag] No source resolved (override/preset/default/clip all empty or missing).");
    }

    bool TrySetUrl(string path, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.Log($"[BgVideoDiag]   {sourceLabel}: empty, skipping.");
            return false;
        }
        string trimmed = path.Trim();
        bool exists = File.Exists(trimmed);
        Debug.Log($"[BgVideoDiag]   {sourceLabel}: '{trimmed}' (File.Exists={exists})");
        if (!exists)
        {
            Debug.LogWarning($"[BackgroundVideoLoop] {sourceLabel} path not found, " +
                             $"falling through: {trimmed}");
            return false;
        }
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url    = ToFileUrl(trimmed);
        activeSourceLabel  = sourceLabel + ":" + Path.GetFileName(trimmed);
        Debug.Log($"[BgVideoDiag] Resolved to {activeSourceLabel}, url='{videoPlayer.url}'");
        return true;
    }

    // Build a proper RFC-compliant file:// URL. Plain string concatenation
    // ("file://" + "C:\foo\bar.mp4") yields a malformed URL on Windows that
    // some Unity versions silently fail to load without raising errorReceived.
    static string ToFileUrl(string absolutePath)
    {
        try { return new System.Uri(absolutePath).AbsoluteUri; }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BgVideoDiag] Uri construction failed for '{absolutePath}': " +
                             $"{e.Message}; falling back to 'file://' + path.");
            return "file://" + absolutePath;
        }
    }

    void Play()
    {
        if (string.IsNullOrEmpty(activeSourceLabel))
        {
            Debug.LogError("[BackgroundVideoLoop] No video source resolved. Set " +
                           "PlayerPrefs key '" + OverridePathPrefKey + "', the " +
                           "'Default Video Path' field, or assign a 'Video Clip'.");
            return;
        }

        videoPlayer.errorReceived += (source, msg) =>
        {
            Debug.LogError($"[BgVideoDiag] VideoPlayer error: {msg} (url='{source.url}')");
        };
        videoPlayer.prepareCompleted += (source) =>
        {
            source.Play();
            Debug.Log($"[BackgroundVideoLoop] Playing {activeSourceLabel} " +
                      $"({textureWidth}x{textureHeight}, loop, speed {playbackSpeed}x)");
            StartCoroutine(MarkReadyAfterFirstFrame());
        };
        Debug.Log($"[BgVideoDiag] BackgroundVideoLoop.Prepare() — source={videoPlayer.source} " +
                  $"url='{videoPlayer.url}' clip='{(videoPlayer.clip != null ? videoPlayer.clip.name : "<null>")}'");
        videoPlayer.Prepare();
    }

    // Wait for the video to actually render its first frame to the
    // RenderTexture before declaring ready — isPrepared alone can fire a frame
    // before the texture is populated, which would cause the recording to
    // start on an empty background.
    System.Collections.IEnumerator MarkReadyAfterFirstFrame()
    {
        long startFrame = videoPlayer.frame;
        while (videoPlayer.frame <= startFrame)
            yield return null;
        yield return null;
        isReady = true;
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.Stop();
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}

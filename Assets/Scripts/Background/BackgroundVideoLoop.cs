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
///   2. The Inspector field <c>defaultVideoPath</c> — an absolute MP4 path.
///   3. The Inspector field <c>videoClip</c> — a Unity-imported VideoClip asset.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BackgroundVideoLoop : MonoBehaviour
{
    /// <summary>
    /// Shared with MainMenuController. Both keys must match — keep them in sync.
    /// </summary>
    public const string OverridePathPrefKey = "AutoAvatarGen.BackgroundVideoOverride";

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
        if (TrySetUrl(overridePath, "user override")) return;
        if (TrySetUrl(defaultVideoPath, "default path")) return;

        if (videoClip != null)
        {
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip   = videoClip;
            activeSourceLabel  = "VideoClip:" + videoClip.name;
            return;
        }

        activeSourceLabel = null; // Play() will warn
    }

    bool TrySetUrl(string path, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string trimmed = path.Trim();
        if (!File.Exists(trimmed))
        {
            Debug.LogWarning($"[BackgroundVideoLoop] {sourceLabel} path not found, " +
                             $"falling through: {trimmed}");
            return false;
        }
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url    = "file://" + trimmed;
        activeSourceLabel  = sourceLabel + ":" + Path.GetFileName(trimmed);
        return true;
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

        videoPlayer.Prepare();
        videoPlayer.prepareCompleted += (source) =>
        {
            source.Play();
            Debug.Log($"[BackgroundVideoLoop] Playing {activeSourceLabel} " +
                      $"({textureWidth}x{textureHeight}, loop, speed {playbackSpeed}x)");
            StartCoroutine(MarkReadyAfterFirstFrame());
        };
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

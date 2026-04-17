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
/// Just assign your video clip in the Inspector and press Play.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BackgroundVideoLoop : MonoBehaviour
{
    [Header("Video")]
    [Tooltip("The video clip to loop as the background.")]
    public VideoClip videoClip;

    [Tooltip("Playback speed (1 = normal).")]
    [Range(0.1f, 3f)]
    public float playbackSpeed = 1f;

    [Header("Audio")]
    [Tooltip("Mute the video's audio track (recommended — Mugs talks over it).")]
    public bool muteAudio = true;

    [Header("Resolution")]
    [Tooltip("RenderTexture width. 1920 for 1080p, 1280 for 720p.")]
    public int textureWidth = 1920;
    public int textureHeight = 1080;

    private VideoPlayer videoPlayer;
    private RawImage rawImage;
    private RenderTexture renderTexture;

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

        // Create a RenderTexture for the video to render into
        renderTexture = new RenderTexture(textureWidth, textureHeight, 0);
        renderTexture.name = "BackgroundVideoRT";

        videoPlayer.clip = videoClip;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;
        videoPlayer.isLooping = true;
        videoPlayer.playbackSpeed = playbackSpeed;
        videoPlayer.playOnAwake = false;
        videoPlayer.skipOnDrop = true;

        // Audio
        if (muteAudio)
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        // Connect the RawImage to display the RenderTexture
        rawImage.texture = renderTexture;
    }

    void Play()
    {
        if (videoClip == null)
        {
            Debug.LogError("[BackgroundVideoLoop] No video clip assigned! " +
                           "Drag a VideoClip into the 'Video Clip' field in the Inspector.");
            return;
        }

        videoPlayer.Prepare();
        videoPlayer.prepareCompleted += (source) =>
        {
            source.Play();
            Debug.Log($"[BackgroundVideoLoop] Playing '{videoClip.name}' " +
                      $"({textureWidth}x{textureHeight}, loop, speed {playbackSpeed}x)");
        };
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

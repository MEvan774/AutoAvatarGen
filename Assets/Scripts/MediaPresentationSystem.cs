using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// ============================================================================
// MediaPresentationSystem — expanded with character position markers.
//
// WHAT'S NEW:
//   - {Position:Left}, {Position:Right}, {Position:Center} markers in scripts
//   - Three position Transforms in Inspector (left, right, center)
//   - Character faces toward content zone automatically (flipX)
//   - Position changes are tracked against audio time, same as emotions/media
//
// WHAT'S UNCHANGED:
//   - {Image:name,duration} and {Video:name,duration} markers work identically
//   - MoveAvatar easing, DisplayMedia, audio pause/resume — all the same
//   - HybridAvatarSystem handles emotions/sway — completely untouched
//
// SCRIPT FORMAT EXAMPLE:
//   {Neutral}
//   {Position:Center}
//   Hello everyone, welcome to the show!
//   {Excited}
//   {Position:Left}
//   Breaking news in AI today!
//   {Image:ai_headline,3}
//   {Serious}
//   {Position:Right}
//   But there's a catch...
//   {Position:Center}
//   That's all for today, thanks for watching!
// ============================================================================

public class MediaPresentationSystem : MonoBehaviour
{
    [Header("Components")]
    public HybridAvatarSystem avatarSystem;
    public Transform avatarParent;
    public AudioSource voiceAudio;

    [Header("Character Positions")]
    [Tooltip("Where the character stands when on the left side.")]
    public Transform leftLocation;
    [Tooltip("Where the character stands when centered (your existing CenterLocation).")]
    public Transform centerLocation;
    [Tooltip("Where the character stands when on the right side (your existing PresentationLocation).")]
    public Transform rightLocation;

    [Header("Character Facing")]
    [Tooltip("The SpriteRenderer on the character — used to flip facing direction.")]
    public SpriteRenderer characterRenderer;

    [Header("Media Display")]
    public Canvas mediaCanvas;
    public RawImage mediaDisplay;
    public VideoPlayer videoPlayer;

    [Header("Avatar Positioning")]
    public float transitionDuration = 0.5f;
    [Tooltip("When true, position changes snap instantly. When false, smooth easing.")]
    public bool useHardCuts = false;

    [Header("Camera Zoom")]
    [Tooltip("Main camera — used for zoom in/out.")]
    public Camera mainCamera;
    [Tooltip("How long a zoom takes in seconds.")]
    public float zoomDuration = 2.5f;
    [Tooltip("How much to zoom in (1.12 = 112% zoom, blueprint says 110-115%).")]
    [Range(1.01f, 1.25f)]
    public float zoomInMultiplier = 1.12f;

    [Header("Content Cards")]
    [Tooltip("Content zone card system — displays branded text cards alongside media.")]
    public ContentZoneController contentZoneController;

    [Header("Media Settings")]
    public string mediaFolderPath = "Media";

    // --- Existing state (unchanged) ---
    private List<MediaMarkerData> mediaMarkers;
    private int lastTriggeredMediaMarker = -1;
    private bool isShowingMedia = false;
    private Coroutine currentMediaCoroutine;
    private Coroutine movementCoroutine;

    // --- New: position tracking ---
    private List<PositionMarkerData> positionMarkers;
    private int lastTriggeredPositionMarker = -1;
    private CharacterPosition currentPosition = CharacterPosition.Center;

    /// <summary>
    /// Read-only access to the character's current position. Used by
    /// ContentZoneController to compute character-aware card entry directions.
    /// </summary>
    public CharacterPosition CurrentPosition => currentPosition;

    // --- New: zoom tracking ---
    private List<ZoomMarkerData> zoomMarkers;
    private int lastTriggeredZoomMarker = -1;
    private float defaultCameraSize;
    private Coroutine zoomCoroutine;

    void Awake()
    {
        if (mediaDisplay != null)
            mediaDisplay.gameObject.SetActive(false);

        if (videoPlayer != null)
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.gameObject.SetActive(false);
        }

        // Auto-find ContentZoneController if not assigned
        if (contentZoneController == null)
        {
            contentZoneController = GetComponent<ContentZoneController>();
            if (contentZoneController == null)
                contentZoneController = FindObjectOfType<ContentZoneController>();
        }
    }

    void Start()
    {
        // Store default camera size for zoom reset
        if (mainCamera != null)
            defaultCameraSize = mainCamera.orthographicSize;

        // Start at center
        if (avatarParent != null && centerLocation != null)
        {
            avatarParent.position = centerLocation.position;
            avatarParent.rotation = centerLocation.rotation;
            currentPosition = CharacterPosition.Center;
            Debug.Log("Avatar positioned at center");
        }
    }

    // -----------------------------------------------------------------------
    // Entry Point (called by ScriptFileReader — same signature as before)
    // -----------------------------------------------------------------------

    public void ProcessScriptWithMedia(string scriptWithMarkers, AudioClip audio)
    {
        // Parse position markers first (strips {Position:X} from script)
        var posResult = ParsePositionMarkers(scriptWithMarkers, audio.length);
        string scriptAfterPositions = posResult.Item1;
        positionMarkers = posResult.Item2;

        // Parse zoom markers (strips {Zoom:X} from script)
        var zoomResult = ParseZoomMarkers(scriptAfterPositions, audio.length);
        string scriptAfterZoom = zoomResult.Item1;
        zoomMarkers = zoomResult.Item2;

        // Parse content card tags (strips {Headline:...}, {Quote:...}, etc.)
        var cardResult = ContentZoneTagParser.ParseContentTags(scriptAfterZoom, audio.length);
        string scriptAfterCards = cardResult.Item1;
        if (contentZoneController != null)
            contentZoneController.SetTimeline(cardResult.Item2, voiceAudio);

        // Then parse media markers (strips {Image:X} and {Video:X})
        var mediaResult = ParseMediaMarkers(scriptAfterCards, audio.length);
        string cleanScript = mediaResult.Item1;
        mediaMarkers = mediaResult.Item2;

        // Forward to avatar system for emotion processing (unchanged)
        avatarSystem.ProcessWithExistingAudio(cleanScript, audio);

        // Track media, positions, zoom, and content cards against audio time
        StartCoroutine(TrackMediaByTime());
        StartCoroutine(TrackPositionsByTime());
        StartCoroutine(TrackZoomByTime());

        if (contentZoneController != null)
            StartCoroutine(contentZoneController.TrackCardsByTime());
    }

    // -----------------------------------------------------------------------
    // Position Tracking (NEW — follows same pattern as emotion tracking)
    // -----------------------------------------------------------------------

    IEnumerator TrackPositionsByTime()
    {
        lastTriggeredPositionMarker = -1;

        while (voiceAudio.isPlaying)
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredPositionMarker + 1; i < positionMarkers.Count; i++)
            {
                if (currentTime >= positionMarkers[i].triggerTime)
                {
                    var marker = positionMarkers[i];
                    Debug.Log($"Triggering position: {marker.position} at {currentTime:F2}s");

                    MoveToPosition(marker.position, marker.hardCutOverride);
                    lastTriggeredPositionMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Moves the character to the target position.
    /// cutOverride: null = use global useHardCuts, true = force hard cut, false = force smooth.
    /// </summary>
    void MoveToPosition(CharacterPosition targetPosition, bool? cutOverride = null)
    {
        Transform target = GetTransformForPosition(targetPosition);
        if (target == null) return;

        // Stop any in-progress movement
        if (movementCoroutine != null)
            StopCoroutine(movementCoroutine);

        bool doHardCut = cutOverride ?? useHardCuts;

        if (doHardCut)
        {
            // Instant snap
            if (avatarParent != null)
            {
                avatarParent.position = target.position;
                avatarParent.rotation = target.rotation;

                if (avatarSystem != null)
                    avatarSystem.SetSwayBase(target.position, target.rotation);
            }
        }
        else
        {
            // Smooth eased movement
            Transform current = GetTransformForPosition(currentPosition);
            if (current == null) current = centerLocation;
            movementCoroutine = StartCoroutine(MoveAvatar(current, target));
        }

        currentPosition = targetPosition;
        UpdateFacing(targetPosition);

        // Pause/resume content cards based on character position
        if (contentZoneController != null)
        {
            if (targetPosition == CharacterPosition.Center)
                contentZoneController.PauseTimeline();
            else
                contentZoneController.ResumeTimeline();
        }
    }

    /// <summary>
    /// Sets sprite flipX so the character faces toward the content zone.
    /// Left = faces right, Right = faces left, Center = faces camera (no flip).
    /// </summary>
    void UpdateFacing(CharacterPosition pos)
    {
        if (characterRenderer == null) return;

        switch (pos)
        {
            case CharacterPosition.Left:
                characterRenderer.flipX = false; // Face right toward content
                break;
            case CharacterPosition.Right:
                characterRenderer.flipX = true;  // Face left toward content
                break;
            case CharacterPosition.Center:
                characterRenderer.flipX = false; // Face camera
                break;
        }
    }

    Transform GetTransformForPosition(CharacterPosition pos)
    {
        switch (pos)
        {
            case CharacterPosition.Left: return leftLocation;
            case CharacterPosition.Right: return rightLocation;
            case CharacterPosition.Center: return centerLocation;
            default: return centerLocation;
        }
    }

    // -----------------------------------------------------------------------
    // Zoom Tracking — follows same pattern as position tracking
    // -----------------------------------------------------------------------

    IEnumerator TrackZoomByTime()
    {
        lastTriggeredZoomMarker = -1;

        while (voiceAudio.isPlaying)
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredZoomMarker + 1; i < zoomMarkers.Count; i++)
            {
                if (currentTime >= zoomMarkers[i].triggerTime)
                {
                    var marker = zoomMarkers[i];
                    Debug.Log($"Triggering zoom: {marker.zoomType} at {currentTime:F2}s");

                    ApplyZoom(marker.zoomType);
                    lastTriggeredZoomMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }
    }

    void ApplyZoom(ZoomType type)
    {
        if (mainCamera == null) return;

        // Stop any in-progress zoom
        if (zoomCoroutine != null)
            StopCoroutine(zoomCoroutine);

        float targetSize;

        switch (type)
        {
            case ZoomType.In:
                targetSize = defaultCameraSize / zoomInMultiplier;
                break;
            case ZoomType.Out:
                targetSize = defaultCameraSize;
                break;
            case ZoomType.Reset:
                // Instant snap back
                mainCamera.orthographicSize = defaultCameraSize;
                return;
            default:
                return;
        }

        zoomCoroutine = StartCoroutine(AnimateZoom(targetSize));
    }

    IEnumerator AnimateZoom(float targetSize)
    {
        float startSize = mainCamera.orthographicSize;
        float elapsed = 0f;

        while (elapsed < zoomDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseInOutQuart(Mathf.Clamp01(elapsed / zoomDuration));
            mainCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t);
            yield return null;
        }

        mainCamera.orthographicSize = targetSize;
        Debug.Log($"Zoom complete: camera size = {targetSize:F2}");
    }

    // -----------------------------------------------------------------------
    // Parse Zoom Markers
    // Format: {Zoom:In}, {Zoom:Out}, {Zoom:Reset}
    // -----------------------------------------------------------------------

    (string, List<ZoomMarkerData>) ParseZoomMarkers(string script, float audioDuration)
    {
        List<ZoomMarkerData> markerList = new List<ZoomMarkerData>();
        string clean = script;

        Regex regex = new Regex(@"\{Zoom:(\w+)\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = scriptWithoutMarkers.Length;

        foreach (Match match in matches)
        {
            string textBeforeMarker = script.Substring(0, match.Index);
            string cleanTextBefore = regex.Replace(textBeforeMarker, "");
            int charsBeforeMarker = cleanTextBefore.Length;

            float markerTime = (charsBeforeMarker / (float)Mathf.Max(1, totalChars)) * audioDuration;

            string zoomStr = match.Groups[1].Value.ToLower();
            ZoomType zoomType = ZoomType.Reset;

            switch (zoomStr)
            {
                case "in": zoomType = ZoomType.In; break;
                case "out": zoomType = ZoomType.Out; break;
                case "reset": zoomType = ZoomType.Reset; break;
                default:
                    Debug.LogWarning($"Unknown zoom type: {zoomStr}, defaulting to Reset");
                    break;
            }

            markerList.Add(new ZoomMarkerData
            {
                triggerTime = markerTime,
                zoomType = zoomType
            });

            Debug.Log($"Zoom marker '{zoomType}' will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Media Tracking (unchanged logic)
    // -----------------------------------------------------------------------

    IEnumerator TrackMediaByTime()
    {
        lastTriggeredMediaMarker = -1;

        while (voiceAudio.isPlaying || isShowingMedia)
        {
            if (!isShowingMedia)
            {
                // Skip media if a content card is currently active
                if (contentZoneController != null && contentZoneController.IsCardActive)
                {
                    yield return null;
                    continue;
                }

                float currentTime = voiceAudio.time;

                for (int i = lastTriggeredMediaMarker + 1; i < mediaMarkers.Count; i++)
                {
                    if (currentTime >= mediaMarkers[i].triggerTime)
                    {
                        Debug.Log($"Triggering media: {mediaMarkers[i].mediaName} at {currentTime:F2}s");

                        if (currentMediaCoroutine != null)
                            StopCoroutine(currentMediaCoroutine);

                        currentMediaCoroutine = StartCoroutine(ShowMedia(mediaMarkers[i]));
                        lastTriggeredMediaMarker = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            yield return null;
        }
    }

    // -----------------------------------------------------------------------
    // Show Media (simplified — no longer moves avatar, position markers do that)
    // -----------------------------------------------------------------------

    IEnumerator ShowMedia(MediaMarkerData marker)
    {
        isShowingMedia = true;

        bool shouldPauseAudio = marker.mediaType == MediaType.VIDEO;
        float pausedTime = 0f;

        if (shouldPauseAudio && voiceAudio.isPlaying)
        {
            pausedTime = voiceAudio.time;
            voiceAudio.Pause();
            Debug.Log($"Audio paused at {pausedTime:F2}s for video");
        }

        // Display media
        yield return StartCoroutine(DisplayMedia(marker));

        if (shouldPauseAudio)
        {
            if (voiceAudio.clip != null)
            {
                voiceAudio.time = pausedTime;
                voiceAudio.Play();
                Debug.Log($"Audio resumed from {pausedTime:F2}s");
            }
        }

        isShowingMedia = false;
    }

    // -----------------------------------------------------------------------
    // Move Avatar (same easing as your original)
    // -----------------------------------------------------------------------

    IEnumerator MoveAvatar(Transform currentLocation, Transform targetLocation)
    {
        if (avatarParent == null || currentLocation == null || targetLocation == null)
            yield break;

        float time = 0f;
        Vector3 startPos = currentLocation.position;
        Vector3 targetPos = targetLocation.position;
        Quaternion startRot = currentLocation.rotation;
        Quaternion targetRot = targetLocation.rotation;

        Debug.Log($"Moving from {currentLocation.name} to {targetLocation.name}");

        while (time < transitionDuration)
        {
            float t = EaseInOutQuart(time / transitionDuration);

            avatarParent.position = Vector3.Lerp(startPos, targetPos, t);
            avatarParent.rotation = Quaternion.Slerp(startRot, targetRot, t);

            time += Time.deltaTime;
            yield return null;
        }

        avatarParent.position = targetPos;
        avatarParent.rotation = targetRot;

        // Update sway base so idle sway works at new position
        if (avatarSystem != null)
            avatarSystem.SetSwayBase(targetPos, targetRot);

        Debug.Log($"Avatar reached {targetLocation.name}");
    }

    float EaseInOutQuart(float x)
    {
        return x < 0.5f ? 8f * x * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 4f) / 2f;
    }

    // -----------------------------------------------------------------------
    // Display Media (unchanged)
    // -----------------------------------------------------------------------

    IEnumerator DisplayMedia(MediaMarkerData marker)
    {
        mediaDisplay.gameObject.SetActive(true);

        if (marker.mediaType == MediaType.IMAGE)
        {
            Texture2D image = Resources.Load<Texture2D>($"{mediaFolderPath}/{marker.mediaName}");

            if (image != null)
            {
                mediaDisplay.texture = image;
                videoPlayer.gameObject.SetActive(false);

                Debug.Log($"Displaying image: {marker.mediaName} for {marker.displayDuration}s");
                yield return new WaitForSeconds(marker.displayDuration);
            }
            else
            {
                Debug.LogError($"Image not found: {mediaFolderPath}/{marker.mediaName}");
            }
        }
        else if (marker.mediaType == MediaType.VIDEO)
        {
            VideoClip clip = Resources.Load<VideoClip>($"{mediaFolderPath}/{marker.mediaName}");

            if (clip != null)
            {
                videoPlayer.gameObject.SetActive(true);
                videoPlayer.clip = clip;

                RenderTexture rt = new RenderTexture(1920, 1080, 24);
                videoPlayer.targetTexture = rt;
                mediaDisplay.texture = rt;

                videoPlayer.Prepare();

                while (!videoPlayer.isPrepared)
                    yield return null;

                videoPlayer.Play();

                Debug.Log($"Playing video: {marker.mediaName}");

                float videoLength = (float)videoPlayer.length;
                float waitTime = marker.displayDuration > 0 ? Mathf.Min(videoLength, marker.displayDuration) : videoLength;

                float videoElapsed = 0f;
                while (videoElapsed < waitTime && videoPlayer.isPlaying)
                {
                    videoElapsed += Time.deltaTime;
                    yield return null;
                }

                videoPlayer.Stop();
                videoPlayer.gameObject.SetActive(false);

                if (rt != null)
                    Destroy(rt);
            }
            else
            {
                Debug.LogError($"Video not found: {mediaFolderPath}/{marker.mediaName}");
            }
        }

        mediaDisplay.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // Parse Position Markers
    // Format: {Position:Left}, {Position:Right,Cut}, {Position:Center,Smooth}
    // -----------------------------------------------------------------------

    (string, List<PositionMarkerData>) ParsePositionMarkers(string script, float audioDuration)
    {
        List<PositionMarkerData> markerList = new List<PositionMarkerData>();
        string clean = script;

        Regex regex = new Regex(@"\{Position:(\w+)(?:,(\w+))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = scriptWithoutMarkers.Length;

        foreach (Match match in matches)
        {
            string textBeforeMarker = script.Substring(0, match.Index);
            string cleanTextBefore = regex.Replace(textBeforeMarker, "");
            int charsBeforeMarker = cleanTextBefore.Length;

            float markerTime = (charsBeforeMarker / (float)Mathf.Max(1, totalChars)) * audioDuration;

            string posStr = match.Groups[1].Value;
            CharacterPosition pos = CharacterPosition.Center;

            switch (posStr.ToLower())
            {
                case "left": pos = CharacterPosition.Left; break;
                case "right": pos = CharacterPosition.Right; break;
                case "center": pos = CharacterPosition.Center; break;
                default:
                    Debug.LogWarning($"Unknown position: {posStr}, defaulting to Center");
                    break;
            }

            // Parse optional cut/smooth override
            bool? cutOverride = null;
            if (match.Groups[2].Success)
            {
                string transStr = match.Groups[2].Value.ToLower();
                if (transStr == "cut") cutOverride = true;
                else if (transStr == "smooth") cutOverride = false;
            }

            markerList.Add(new PositionMarkerData
            {
                triggerTime = markerTime,
                position = pos,
                hardCutOverride = cutOverride
            });

            Debug.Log($"Position marker '{pos}'{(cutOverride.HasValue ? (cutOverride.Value ? " (hard cut)" : " (smooth)") : "")} will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Parse Media Markers (unchanged)
    // -----------------------------------------------------------------------

    (string, List<MediaMarkerData>) ParseMediaMarkers(string script, float audioDuration)
    {
        List<MediaMarkerData> markerList = new List<MediaMarkerData>();
        string clean = script;

        Regex regex = new Regex(@"\{(Image|Video):([^,}]+)(?:,(\d+(?:\.\d+)?))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = scriptWithoutMarkers.Length;

        foreach (Match match in matches)
        {
            string textBeforeMarker = script.Substring(0, match.Index);
            string cleanTextBefore = regex.Replace(textBeforeMarker, "");
            int charsBeforeMarker = cleanTextBefore.Length;

            float markerTime = (charsBeforeMarker / (float)totalChars) * audioDuration;

            MediaType type = match.Groups[1].Value == "Image" ? MediaType.IMAGE : MediaType.VIDEO;
            string mediaName = match.Groups[2].Value.Trim();
            float duration = match.Groups[3].Success ? float.Parse(match.Groups[3].Value) : (type == MediaType.IMAGE ? 3f : 0f);

            markerList.Add(new MediaMarkerData
            {
                triggerTime = markerTime,
                mediaType = type,
                mediaName = mediaName,
                displayDuration = duration
            });

            Debug.Log($"Media marker '{mediaName}' ({type}) will trigger at {markerTime:F2}s for {duration}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }
}

// ============================================================================
// Enums & Data Classes
// ============================================================================

/// <summary>
/// The three character positions from Part 11 of the blueprint.
/// </summary>
public enum CharacterPosition
{
    Left,    // Left 25-30% of screen, faces right toward content
    Right,   // Right 25-30% of screen, faces left toward content
    Center   // Center of screen, faces camera
}

/// <summary>
/// Tracks when the character should move to a new position.
/// Same structure as TimeMarkerData and MediaMarkerData.
/// </summary>
[System.Serializable]
public class PositionMarkerData
{
    public float triggerTime;
    public CharacterPosition position;
    public bool? hardCutOverride; // null = use global, true = force cut, false = force smooth
}

// Existing types (unchanged)
public enum MediaType
{
    IMAGE,
    VIDEO
}

[System.Serializable]
public class MediaMarkerData
{
    public float triggerTime;
    public MediaType mediaType;
    public string mediaName;
    public float displayDuration;
}

/// <summary>
/// Zoom direction types from Part 12 (TRANS-02 / TRANS-03).
/// </summary>
public enum ZoomType
{
    In,     // Push in: 100% -> 110-115%. Signals focus/intensity.
    Out,    // Pull back: zoomed -> 100%. Signals de-escalation.
    Reset   // Instant snap back to default. No easing.
}

[System.Serializable]
public class ZoomMarkerData
{
    public float triggerTime;
    public ZoomType zoomType;
}
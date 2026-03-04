using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class MediaPresentationSystem : MonoBehaviour
{
    [Header("Components")]
    public HybridAvatarSystem avatarSystem;
    public Transform avatarParent; // The parent GameObject that contains the Pivot
    public Transform centerLocation; // Point A - center position
    public Transform presentationLocation; // Point B - presentation position
    public AudioSource voiceAudio;

    [Header("Media Display")]
    public Canvas mediaCanvas;
    public RawImage mediaDisplay;
    public VideoPlayer videoPlayer;

    [Header("Avatar Positioning")]
    public float transitionDuration = 0.5f;

    [Header("Media Settings")]
    public string mediaFolderPath = "Media";

    private List<MediaMarkerData> mediaMarkers;
    private int lastTriggeredMediaMarker = -1;
    private bool isShowingMedia = false;
    private bool isAtPresentation = false;
    private Coroutine currentMediaCoroutine;
    private Coroutine movementCoroutine;

    void Awake()
    {
        if (mediaDisplay != null)
        {
            mediaDisplay.gameObject.SetActive(false);
        }

        if (videoPlayer != null)
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        // Position avatar parent at center initially
        if (avatarParent != null && centerLocation != null)
        {
            avatarParent.position = centerLocation.position;
            avatarParent.rotation = centerLocation.rotation;
            isAtPresentation = false;
            Debug.Log("Avatar positioned at center");
        }
    }

    public void ProcessScriptWithMedia(string scriptWithMarkers, AudioClip audio)
    {
        var result = ParseMediaMarkers(scriptWithMarkers, audio.length);
        string cleanScript = result.Item1;
        mediaMarkers = result.Item2;

        avatarSystem.ProcessWithExistingAudio(cleanScript, audio);

        StartCoroutine(TrackMediaByTime());
    }

    IEnumerator TrackMediaByTime()
    {
        lastTriggeredMediaMarker = -1;

        while (voiceAudio.isPlaying || isShowingMedia)
        {
            if (!isShowingMedia)
            {
                float currentTime = voiceAudio.time;

                for (int i = lastTriggeredMediaMarker + 1; i < mediaMarkers.Count; i++)
                {
                    if (currentTime >= mediaMarkers[i].triggerTime)
                    {
                        Debug.Log($"Triggering media: {mediaMarkers[i].mediaName} at {currentTime:F2}s");

                        if (currentMediaCoroutine != null)
                        {
                            StopCoroutine(currentMediaCoroutine);
                        }

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

        // Ensure we return to center at the end
        if (isShowingMedia && isAtPresentation)
        {
            if (movementCoroutine != null) StopCoroutine(movementCoroutine);
            movementCoroutine = StartCoroutine(MoveAvatar(presentationLocation, centerLocation));
            yield return movementCoroutine;
        }
    }

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

        // Move to presentation position
        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        movementCoroutine = StartCoroutine(MoveAvatar(centerLocation, presentationLocation));
        yield return movementCoroutine;

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

        // Return to center
        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        movementCoroutine = StartCoroutine(MoveAvatar(presentationLocation, centerLocation));
        yield return movementCoroutine;

        isShowingMedia = false;
    }

    IEnumerator MoveAvatar(Transform currentLocation, Transform targetLocation)
    {
        if (avatarParent == null || currentLocation == null || targetLocation == null)
        {
            yield break;
        }

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

        // Ensure exact final position
        avatarParent.position = targetPos;
        avatarParent.rotation = targetRot;

        // Update position tracking
        isAtPresentation = (targetLocation == presentationLocation);

        Debug.Log($"Avatar reached {targetLocation.name}");
    }

    // Easing function - smooth acceleration and deceleration
    float EaseInOutQuart(float x)
    {
        return x < 0.5f ? 8f * x * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 4f) / 2f;
    }

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
                {
                    yield return null;
                }

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
                {
                    Destroy(rt);
                }
            }
            else
            {
                Debug.LogError($"Video not found: {mediaFolderPath}/{marker.mediaName}");
            }
        }

        mediaDisplay.gameObject.SetActive(false);
    }

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
/*
```

---

## **Setup:**

### **Hierarchy Structure:**
```
Scene
├── AvatarParent (empty GameObject - this moves between locations)
│   └── Pivot (has sway and squash/stretch - local movement only)
│       └── AvatarSprite
├── CenterLocation (empty GameObject at center)
├── PresentationLocation (empty GameObject on left)
└── MediaPresentationSystem
*/
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
    float zoomDuration = 0.8f;
    [Tooltip("How much to zoom in (1.12 = 112% zoom, blueprint says 110-115%).")]
    [Range(1.01f, 1.25f)]
    public float zoomInMultiplier = 1.12f;

    [Header("Pullback Effect ({Zoom:Pullback})")]
    [Tooltip("Initial wide framing — orthographicSize is snapped to defaultSize * this on trigger.")]
    [Range(1.1f, 4f)]
    public float pullbackStartMultiplier = 1.8f;
    [Tooltip("End of the slow drift. The camera linearly drifts from start → end, then jump-cuts back.")]
    [Range(1.1f, 4f)]
    public float pullbackEndMultiplier = 1.9f;
    [Tooltip("How long the slow drift lasts (seconds). Overridden per-marker by ',D=seconds'.")]
    public float pullbackDuration = 3f;

    [Tooltip("Black border planes (or any GameObjects) that frame the default camera view in " +
             "world space. Activated during {Zoom:Pullback} so anything outside the original " +
             "framing is cropped to black; deactivated when the effect ends. Position them just " +
             "outside the default camera frame edges and large enough to extend beyond the maximum " +
             "pullback view.")]
    public GameObject[] pullbackBorderPlanes;

    [Header("Content Cards")]
    [Tooltip("Content zone card system — displays branded text cards alongside media.")]
    public ContentZoneController contentZoneController;

    [Header("Black Panel")]
    [Tooltip("Fullscreen black panel controller — jump-cuts a black overlay via {Black:duration} markers.")]
    public BlackPanelController blackPanelController;

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
    private Coroutine pendingResetCoroutine;

    // --- Black panel tracking ---
    private List<BlackPanelMarkerData> blackPanelMarkers;
    private int lastTriggeredBlackPanelMarker = -1;

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

        // Auto-find or create BlackPanelController if not assigned
        if (blackPanelController == null)
        {
            blackPanelController = GetComponent<BlackPanelController>();
            if (blackPanelController == null)
                blackPanelController = FindObjectOfType<BlackPanelController>();
            if (blackPanelController == null)
                blackPanelController = gameObject.AddComponent<BlackPanelController>();
        }

        // Hand the recorded canvas to the black panel so it lands in the frame
        // the recorder actually captures. CrossPlatformRecorder's Camera source
        // explicitly skips Screen Space - Overlay canvases.
        //if (blackPanelController != null && mediaCanvas != null)
           // blackPanelController.SetHostCanvas(mediaCanvas);
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
        // Debug snapshot — lets you confirm what Unity actually parsed (not the
        // raw Script.txt, but whichever _timed.txt / stitched variant was loaded).
        string preview = scriptWithMarkers.Length > 400
            ? scriptWithMarkers.Substring(0, 400) + "..."
            : scriptWithMarkers;
        Debug.Log($"[MediaPresentation] Loaded script ({scriptWithMarkers.Length} chars). " +
                  $"Contains '{{Black': {scriptWithMarkers.Contains("{Black")}\n---\n{preview}\n---");

        // Parse position markers first (strips {Position:X} from script)
        var posResult = ParsePositionMarkers(scriptWithMarkers, audio.length);
        string scriptAfterPositions = posResult.Item1;
        positionMarkers = posResult.Item2;

        // Parse zoom markers (strips {Zoom:X} from script)
        var zoomResult = ParseZoomMarkers(scriptAfterPositions, audio.length);
        string scriptAfterZoom = zoomResult.Item1;
        zoomMarkers = zoomResult.Item2;

        // Parse black panel markers (strips {Black:duration} from script)
        var blackResult = ParseBlackPanelMarkers(scriptAfterZoom, audio.length);
        scriptAfterZoom = blackResult.Item1;
        blackPanelMarkers = blackResult.Item2;

        // Parse content card tags (strips {Headline:...}, {Quote:...}, etc.)
        var cardResult = ContentZoneTagParser.ParseContentTags(scriptAfterZoom, audio.length);
        string scriptAfterCards = cardResult.Item1;
        if (contentZoneController != null)
            contentZoneController.SetTimeline(cardResult.Item2, voiceAudio);

        // Then parse media markers (strips {Image:X} and {Video:X})
        var mediaResult = ParseMediaMarkers(scriptAfterCards, audio.length);
        string cleanScript = mediaResult.Item1;
        mediaMarkers = mediaResult.Item2;

        // Strip stage directions like [pause,T=4.7] / [sips coffee,T=6.8] —
        // baked in by the ElevenLabs pre-processor as narrative cues only.
        cleanScript = Regex.Replace(cleanScript, @"\[[^\]]*\]", "");

        StartCoroutine(BeginPlaybackWhenBackgroundReady(cleanScript, audio));
    }

    // Waits for the BackgroundVideoOverride hijacker to finish preparing any
    // runtime-loaded mp4 before kicking off playback. Without this, a
    // recording started while the swapped-in .mp4 is still preparing captures
    // a blank or half-decoded frame at the very start of the output. No-op if
    // the override didn't hijack anything (in which case the inspector-
    // configured VideoPlayer with PlayOnAwake handled itself before Start).
    IEnumerator BeginPlaybackWhenBackgroundReady(string cleanScript, AudioClip audio)
    {
        const float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout && !MugsTech.Background.BackgroundVideoOverride.AllPrepared)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (elapsed >= timeout)
            Debug.LogWarning($"[MediaPresentation] Background video(s) not ready after {timeout}s — starting anyway.");

        // Forward to avatar system for emotion processing (unchanged)
        avatarSystem.ProcessWithExistingAudio(cleanScript, audio);

        // Track media, positions, zoom, and content cards against audio time
        StartCoroutine(TrackMediaByTime());
        StartCoroutine(TrackPositionsByTime());
        StartCoroutine(TrackZoomByTime());
        StartCoroutine(TrackBlackPanelByTime());

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
                    string mods = (marker.cut ? " (cut)" : "") +
                                  (marker.holdDuration > 0f ? $" hold {marker.holdDuration:F2}s" : "");
                    Debug.Log($"Triggering zoom: {marker.zoomType}{mods} at {currentTime:F2}s");

                    ApplyZoom(marker.zoomType, marker.cut, marker.holdDuration);
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

    void ApplyZoom(ZoomType type, bool cut = false, float holdDuration = 0f)
    {
        if (mainCamera == null) return;

        // Stop any in-progress zoom + any pending auto-reset from a previous marker.
        // Also pop the pullback mask off — if the new zoom is itself a Pullback,
        // AnimatePullback will switch it back on at the start.
        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        if (pendingResetCoroutine != null) StopCoroutine(pendingResetCoroutine);
        SetPullbackMaskActive(false);

        // Pullback is a self-contained multi-stage effect — handle it on its own.
        if (type == ZoomType.Pullback)
        {
            float drift = holdDuration > 0f ? holdDuration : pullbackDuration;
            zoomCoroutine = StartCoroutine(AnimatePullback(drift));
            return;
        }

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
                // Reset is always an instant snap regardless of the cut flag —
                // that's its whole purpose.
                mainCamera.orthographicSize = defaultCameraSize;
                return;
            default:
                return;
        }

        if (cut)
            mainCamera.orthographicSize = targetSize;
        else
            zoomCoroutine = StartCoroutine(AnimateZoom(targetSize));

        // Auto-reset timer — only meaningful when we've actually changed away
        // from default (i.e. zoomed In). For Out we're already at default.
        if (holdDuration > 0f && type == ZoomType.In)
            pendingResetCoroutine = StartCoroutine(AutoResetAfter(holdDuration, cut));
    }

    // Pullback: snap to a wide framing, drift slightly wider over `drift` seconds
    // (linear so the motion reads as a steady push-out), then jump back to default.
    // The pullbackBorderPlanes (assigned in the Inspector) crop anything outside
    // the original camera framing so the output reads as a video shrinking on
    // a black canvas.
    IEnumerator AnimatePullback(float drift)
    {
        float startSize = defaultCameraSize * Mathf.Max(1.01f, pullbackStartMultiplier);
        float endSize   = defaultCameraSize * Mathf.Max(pullbackStartMultiplier, pullbackEndMultiplier);

        mainCamera.orthographicSize = startSize;
        SetPullbackMaskActive(true);

        float elapsed = 0f;
        while (elapsed < drift)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, drift));
            mainCamera.orthographicSize = Mathf.Lerp(startSize, endSize, t);
            yield return null;
        }

        mainCamera.orthographicSize = defaultCameraSize;
        SetPullbackMaskActive(false);
    }

    void SetPullbackMaskActive(bool active)
    {
        if (pullbackBorderPlanes == null) return;
        for (int i = 0; i < pullbackBorderPlanes.Length; i++)
        {
            GameObject go = pullbackBorderPlanes[i];
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }

    IEnumerator AutoResetAfter(float delay, bool cut)
    {
        yield return new WaitForSeconds(delay);

        if (mainCamera == null) yield break;

        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);

        if (cut)
            mainCamera.orthographicSize = defaultCameraSize;
        else
            zoomCoroutine = StartCoroutine(AnimateZoom(defaultCameraSize));

        pendingResetCoroutine = null;
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
    // Format: {Zoom:<In|Out|Reset|Pullback>[,Cut][,D=seconds][,T=seconds]}
    //   Cut       — instant snap instead of animating. (Ignored on Reset, which
    //               is always a snap, and on Pullback, which manages its own cuts.)
    //   D=seconds — In: auto-reset to default this many seconds after firing.
    //               Pullback: overrides the slow-drift duration.
    //               Ignored for Out / Reset.
    //   T=seconds — exact trigger time (appended by the ElevenLabs pre-processor).
    // Trailing options are order-independent.
    // -----------------------------------------------------------------------

    (string, List<ZoomMarkerData>) ParseZoomMarkers(string script, float audioDuration)
    {
        List<ZoomMarkerData> markerList = new List<ZoomMarkerData>();
        string clean = script;

        // Group 1 = type word; Group 2 = the rest of the comma-separated tokens
        // (including leading commas), parsed by hand below for order independence.
        Regex regex = new Regex(@"\{Zoom:(\w+)((?:,[^,}]+)*)\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            // Defaults
            float markerTime    = -1f;
            float holdDuration  = 0f;
            bool  cut           = false;

            // Walk the trailing tokens.
            string tail = match.Groups[2].Value;
            if (!string.IsNullOrEmpty(tail))
            {
                string[] tokens = tail.Split(',');
                foreach (string raw in tokens)
                {
                    string tok = raw.Trim();
                    if (tok.Length == 0) continue;

                    if (tok.Equals("Cut", System.StringComparison.OrdinalIgnoreCase))
                    {
                        cut = true;
                    }
                    else if (tok.StartsWith("T=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(tok.Substring(2),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out markerTime);
                    }
                    else if (tok.StartsWith("D=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(tok.Substring(2),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out holdDuration);
                    }
                    else
                    {
                        Debug.LogWarning($"Unknown zoom option '{tok}' in '{match.Value}' — ignored.");
                    }
                }
            }

            // Fall back to character-position estimate if no T= was supplied.
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            string zoomStr = match.Groups[1].Value.ToLower();
            ZoomType zoomType = ZoomType.Reset;

            switch (zoomStr)
            {
                case "in": zoomType = ZoomType.In; break;
                case "out": zoomType = ZoomType.Out; break;
                case "reset": zoomType = ZoomType.Reset; break;
                case "pullback": zoomType = ZoomType.Pullback; break;
                default:
                    Debug.LogWarning($"Unknown zoom type: {zoomStr}, defaulting to Reset");
                    break;
            }

            markerList.Add(new ZoomMarkerData
            {
                triggerTime  = markerTime,
                zoomType     = zoomType,
                cut          = cut,
                holdDuration = holdDuration
            });

            string mods = (cut ? " cut" : "") + (holdDuration > 0f ? $" hold={holdDuration:F2}s" : "");
            Debug.Log($"Zoom marker '{zoomType}'{mods} will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Black Panel Tracking — fullscreen jump-cut overlay
    // Format: {Black:duration}  (optional ,T=X.XXX or ,D=duration)
    // -----------------------------------------------------------------------

    IEnumerator TrackBlackPanelByTime()
    {
        lastTriggeredBlackPanelMarker = -1;

        Debug.Log($"[Black] TrackBlackPanelByTime started. markers={blackPanelMarkers?.Count ?? 0}, audio playing={voiceAudio != null && voiceAudio.isPlaying}");

        // Wait one frame so voiceAudio.isPlaying can latch to true if Play()
        // was called in the same frame as this coroutine was started.
        if (voiceAudio == null || !voiceAudio.isPlaying)
            yield return null;

        if (blackPanelMarkers == null || blackPanelMarkers.Count == 0)
        {
            Debug.Log("[Black] No black-panel markers to track — coroutine exiting.");
            yield break;
        }

        while (voiceAudio != null && voiceAudio.isPlaying)
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredBlackPanelMarker + 1; i < blackPanelMarkers.Count; i++)
            {
                if (currentTime >= blackPanelMarkers[i].triggerTime)
                {
                    var marker = blackPanelMarkers[i];
                    Debug.Log($"[Black] Triggering black panel for {marker.duration:F2}s at {currentTime:F2}s");

                    if (blackPanelController != null)
                        blackPanelController.Show(marker.duration);
                    else
                        Debug.LogError("[Black] blackPanelController is NULL — cannot show panel. Assign it in the Inspector.");

                    lastTriggeredBlackPanelMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }

        Debug.Log("[Black] TrackBlackPanelByTime loop ended (audio no longer playing).");
    }

    (string, List<BlackPanelMarkerData>) ParseBlackPanelMarkers(string script, float audioDuration)
    {
        List<BlackPanelMarkerData> markerList = new List<BlackPanelMarkerData>();
        string clean = script;

        // Accepts: {Black:3}, {Black:D=3}, {Black:3,T=4.5}, {Black:T=4.5,D=3}, {Black:D=3,T=4.5}
        Regex regex = new Regex(
            @"\{Black:(?:(?:T=(\d+(?:\.\d+)?),)?(?:D=)?(\d+(?:\.\d+)?)|(?:D=)?(\d+(?:\.\d+)?)(?:,T=(\d+(?:\.\d+)?))?)\}");
        MatchCollection matches = regex.Matches(script);

        // Also run a very loose probe — if the script contains "{Black" at all
        // but the strict regex didn't match, log the raw text so we can see
        // what form the marker actually took in _timed.txt.
        if (matches.Count == 0 && script.Contains("{Black"))
        {
            int idx = script.IndexOf("{Black", System.StringComparison.Ordinal);
            int end = script.IndexOf('}', idx);
            string sample = end > idx ? script.Substring(idx, System.Math.Min(end - idx + 1, 80)) : script.Substring(idx, System.Math.Min(40, script.Length - idx));
            Debug.LogWarning($"[Black] Found literal '{{Black' in script but strict regex did not match. Raw: \"{sample}\" — check the marker form.");
        }

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            // T= can be either group 1 (T-first form) or group 4 (duration-first form)
            Group tsGroup = match.Groups[1].Success ? match.Groups[1] : match.Groups[4];
            float markerTime = TryParseTimestamp(tsGroup);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            string durStr = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            float duration = float.Parse(durStr, System.Globalization.CultureInfo.InvariantCulture);

            markerList.Add(new BlackPanelMarkerData
            {
                triggerTime = markerTime,
                duration = duration
            });

            Debug.Log($"[Black] Parsed marker \"{match.Value}\" — trigger at {markerTime:F2}s for {duration:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        Debug.Log($"[Black] ParseBlackPanelMarkers found {markerList.Count} marker(s). blackPanelController={(blackPanelController != null ? blackPanelController.name : "NULL")}");

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
    //         (optional ,T=X.XXX appended by the ElevenLabs pre-processor)
    // -----------------------------------------------------------------------

    (string, List<PositionMarkerData>) ParsePositionMarkers(string script, float audioDuration)
    {
        List<PositionMarkerData> markerList = new List<PositionMarkerData>();
        string clean = script;

        Regex regex = new Regex(@"\{Position:(\w+)(?:,(Cut|Smooth))?(?:,T=(\d+(?:\.\d+)?))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[3]);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

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
    // Parse Media Markers
    // Format: {Image:name}, {Image:name,3}, or the pre-processed
    //         {Image:name,T=X.XXX,D=Y}. Also handles {Video:...}.
    // -----------------------------------------------------------------------

    (string, List<MediaMarkerData>) ParseMediaMarkers(string script, float audioDuration)
    {
        List<MediaMarkerData> markerList = new List<MediaMarkerData>();
        string clean = script;

        // Groups: 1=Image|Video, 2=name, 3=T (optional), 4=D= duration (optional),
        //         5=bare duration (optional, legacy pre-T= format)
        Regex regex = new Regex(
            @"\{(Image|Video):([^,}]+)(?:,T=(\d+(?:\.\d+)?))?(?:,D=(\d+(?:\.\d+)?))?(?:,(\d+(?:\.\d+)?))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[3]);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            MediaType type = match.Groups[1].Value == "Image" ? MediaType.IMAGE : MediaType.VIDEO;
            string mediaName = match.Groups[2].Value.Trim();

            float duration;
            if (match.Groups[4].Success)
                duration = float.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            else if (match.Groups[5].Success)
                duration = float.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);
            else
                duration = type == MediaType.IMAGE ? 3f : 0f;

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

    // -----------------------------------------------------------------------
    // Shared helper — parse a T=X.XXX capture. Returns -1 if the group is
    // empty or the value can't be parsed.
    // -----------------------------------------------------------------------

    static float TryParseTimestamp(Group group)
    {
        if (group == null || !group.Success) return -1f;
        if (float.TryParse(group.Value,
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out float t))
            return t;
        return -1f;
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
    In,         // Push in: 100% -> 110-115%. Signals focus/intensity.
    Out,        // Pull back: zoomed -> 100%. Signals de-escalation.
    Reset,      // Instant snap back to default. No easing.
    Pullback    // Snap wide, slowly drift wider, jump-cut back to default.
}

[System.Serializable]
public class ZoomMarkerData
{
    public float triggerTime;
    public ZoomType zoomType;

    // When true, the zoom snaps instantly instead of animating over zoomDuration.
    // Auto-reset (if scheduled) inherits this style.
    public bool cut;

    // Seconds after triggerTime to auto-reset the camera back to default. <= 0
    // means "no auto-reset" and the zoom stays until a later marker changes it.
    public float holdDuration;
}

/// <summary>
/// Fullscreen black panel marker. Jump-cuts in, holds for duration, jump-cuts out.
/// </summary>
[System.Serializable]
public class BlackPanelMarkerData
{
    public float triggerTime;
    public float duration;
}
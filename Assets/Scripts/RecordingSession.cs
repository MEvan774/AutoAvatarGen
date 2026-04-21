using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Evereal.VideoCapture;

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
        if (Instance != null) return;

        GameObject host = new GameObject("RecordingSession");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<RecordingSession>();
        SceneManager.LoadScene(RecordingSceneName);
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
        voiceAudio = null;
        displayedPercent = 0f;
        if (percentText != null) percentText.text = "0%";
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

        // Grab the narration AudioSource so we can drive the progress percent.
        // Prefer the one explicitly wired on CrossPlatformRecorder; otherwise
        // fall back to any AudioSource in the scene (there's typically only
        // one doing playback during a take).
        CrossPlatformRecorder recorder = capture.GetComponent<CrossPlatformRecorder>()
                                       ?? FindAnyObjectByType<CrossPlatformRecorder>();
        if (recorder != null) voiceAudio = recorder.voiceAudio;
        if (voiceAudio == null) voiceAudio = FindAnyObjectByType<AudioSource>();

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

        // If OnComplete/OnError never arrive, surface a failure so the menu
        // doesn't stay stuck on "Generating..." forever.
        const float finalizeTimeout = 60f;
        float elapsed = 0f;
        while (!finished && elapsed < finalizeTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (finished) yield break;

        Debug.LogWarning("[RecordingSession] Video generation timed out — no OnComplete after " + finalizeTimeout + "s.");
        finished = true;
        LastResult = new RecordingResult
        {
            State = RecordingResult.Status.Failed,
            ErrorMessage = "Video generation timed out (no completion event after " + finalizeTimeout + "s)."
        };
        RaiseResultChanged();
        UnsubscribeFromCapture();
        Destroy(gameObject);
    }

    void HandOffAsGenerating()
    {
        if (handedOff) return;
        handedOff = true;
        LastResult = new RecordingResult { State = RecordingResult.Status.Generating };
        RaiseResultChanged();
        ReturnToMainMenu();
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
        if (finished) return;
        finished = true;
        LastResult = new RecordingResult
        {
            State = RecordingResult.Status.Saved,
            SavePath = args.SavePath
        };
        Debug.Log($"[RecordingSession] Capture complete: {args.SavePath}");
        RaiseResultChanged();
        UnsubscribeFromCapture();

        // Normal path: the watchdog already handed off to the main menu on
        // capture-stop, so just clean up. Fallback path (rare): OnComplete
        // fired before capture.status changed — still need to go back to menu.
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
        spinnerImage.fillAmount = 0.72f;
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

    // Drives the 0%-100% readout on the recording indicator. Progress is
    // measured against audio playback (voiceAudio.time / clip.length) so it
    // tracks the actual narrative length the user will see in the output.
    //
    // State machine:
    //   - Before playback: 0%
    //   - While audio playing: monotonically increasing fraction of clip
    //   - After audio stops: latched at the last known value, then snapped to
    //     100% once capture is handed off (the "generating video" phase).
    void UpdateProgressPercent()
    {
        if (percentText == null) return;

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

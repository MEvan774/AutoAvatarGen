using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class HybridAvatarSystem : MonoBehaviour
{
    [Header("Components")]
    public AudioSource voiceAudio;
    public SpriteRenderer avatarRenderer;
    public GameObject pivot;

    [Header("Emotion Sprites")]
    public Sprite neutralSprite;
    public Sprite excitedSprite;
    public Sprite seriousSprite;
    public Sprite sadSprite;
    public Sprite concernedSprite;

    [Header("Emotion Image Array Override")]
    [Tooltip("When checked, the avatar's expressions are driven ENTIRELY by the " +
             "Emotion Images array below — overriding the five sprite fields above " +
             "AND any per-emotion images set in the in-app Visuals menu / active save. " +
             "Leave unchecked to use the normal menu/save pipeline.")]
    public bool useEmotionArrayOverride = false;

    [Tooltip("Name -> sprite pairs used when 'Use Emotion Array Override' is on. The " +
             "Name is the token your script (and Claude) fire as {Name}, e.g. {Hyped}. " +
             "Use only letters, digits, and underscores in names. Include a 'Neutral' " +
             "entry — it's the idle / fallback expression. Use the 'Copy Emotion Names " +
             "for Claude' button at the bottom of this component to hand the list to Claude.")]
    public EmotionImageEntry[] emotionImageOverrides;

    [Header("Timing Adjustment")]
    [Range(-2f, 2f)]
    [Tooltip("Adjust if emotions trigger too early (negative) or too late (positive)")]
    public float timingOffset = 0f;

    [Header("Transition Style")]
    [Tooltip("Fallback transition used only when the main-menu \"Presenter " +
             "transition\" selector has never been touched: on = Crossfade, " +
             "off = Squash & Stretch. Once the user picks a style in the main " +
             "menu, that choice (including Shake) wins.")]
    public bool useCrossfade = false;
    [Range(0.1f, 1.5f)]
    [Tooltip("Duration of the crossfade transition in seconds")]
    public float crossfadeDuration = 0.3f;
    [Tooltip("Optional MugsShake driven by the 'Shake' transition style. If left " +
             "empty, one is added to the pivot at runtime with default settings. " +
             "Assign your own (on the pivot, or a child that holds only the visual) " +
             "to tune amplitude / angle / duration in the Inspector.")]
    public MugsShake mugsShake;
    [Tooltip("Emotion changes scheduled within this many seconds of the start " +
             "jump-cut instead of animating, so no squash / crossfade / shake / " +
             "blink plays in the first moment of a recording. Set to 0 to animate " +
             "from the very first marker.")]
    public float instantCutStartWindow = 1f;
    [Tooltip("Optional MugsEmotionTransition driving the 'Blink' / 'Blink (Heavy)' " +
             "styles. If left empty, one is added to the pivot at runtime. Attach " +
             "your own on the centred visual holder to tune the blink timings.")]
    public MugsEmotionTransition mugsBlink;

    [Header("Sprite Size Normalization")]
    [Tooltip("If true, all emotion sprites render at the same size as the neutral sprite, regardless of source image dimensions.")]
    public bool normalizeSpriteSize = true;

    private float animationDuration = 0.15f;
    private float squashAmount = 1.6f;
    private SpriteRenderer crossfadeRenderer;

    // Captured at Awake — used as the reference size for all emotion swaps.
    private Vector3 initialAvatarScale;
    private float baselineSpriteHeight;

    private Coroutine currentAnimation;

    // Resolved in Awake from PresenterTransitionSettings (the main-menu choice),
    // falling back to the useCrossfade toggle when the user hasn't picked one.
    private PresenterTransitionSettings.Style activeTransition;

    private Dictionary<string, Sprite> emotionMap;
    private string cleanScript;
    private List<TimeMarkerData> timeMarkers; // Changed to time-based
    private int lastTriggeredMarker = -1;

    [Header("Recording")]
    public CrossPlatformRecorder recorder;
    public bool autoRecord = true;

    [Header("Idle Sway Settings")]
    public bool enableIdleSway = true;
    [Range(0.1f, 2f)]
    public float swaySpeed = 0.5f;
    [Range(0.01f, 0.5f)]
    public float swayAmountX = 0.1f;
    [Range(0.01f, 0.5f)]
    public float swayAmountY = 0.15f;
    [Range(0f, 10f)]
    public float rotationAmount = 3f;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float noiseOffsetX;
    private float noiseOffsetY;
    private float noiseOffsetRotation;

    private Vector3 swayBasePosition;
    private Quaternion swayBaseRotation;
    private bool useSwayBase = false;

    void Awake()
    {
        // Build the emotion lookup. With "Use Emotion Array Override" on, the
        // emotionImageOverrides array is authoritative: the five sprite fields
        // above — and any images pushed later by VisualsRuntimeApplier — are
        // ignored (ApplyEmotionOverrides early-outs while the toggle is on).
        Sprite initialSprite;
        if (useEmotionArrayOverride)
        {
            emotionMap = BuildEmotionMapFromArray(out initialSprite);
        }
        else
        {
            emotionMap = new Dictionary<string, Sprite>()
            {
                {"Neutral", neutralSprite},
                {"Excited", excitedSprite},
                {"Serious", seriousSprite},
                {"Sad", sadSprite},
                {"Concerned", concernedSprite}
            };
            initialSprite = neutralSprite;
        }

        // Capture baseline BEFORE setting the sprite — this preserves the scale the
        // user configured in the Inspector. The baseline sprite height is whatever
        // the initial (neutral) sprite's world-space height is.
        initialAvatarScale = avatarRenderer.transform.localScale;
        baselineSpriteHeight = (initialSprite != null) ? initialSprite.bounds.size.y : 1f;

        avatarRenderer.sprite = initialSprite;
        NormalizeSpriteSize(avatarRenderer);

        // Create a second SpriteRenderer for crossfade transitions.
        // It's a SIBLING of avatarRenderer (not a child) so we can scale each one
        // independently based on its current sprite's dimensions.
        GameObject crossfadeObj = new GameObject("CrossfadeRenderer");
        crossfadeObj.transform.SetParent(avatarRenderer.transform.parent, false);
        crossfadeObj.transform.localPosition = avatarRenderer.transform.localPosition;
        crossfadeObj.transform.localRotation = avatarRenderer.transform.localRotation;
        crossfadeObj.transform.localScale = initialAvatarScale;
        crossfadeRenderer = crossfadeObj.AddComponent<SpriteRenderer>();
        crossfadeRenderer.sortingLayerID = avatarRenderer.sortingLayerID;
        crossfadeRenderer.sortingOrder = avatarRenderer.sortingOrder + 1;
        crossfadeRenderer.color = new Color(1f, 1f, 1f, 0f);

        if (pivot != null)
        {
            originalPosition = pivot.transform.localPosition;
            originalRotation = pivot.transform.localRotation;

            // Initialize sway base to world position
            swayBasePosition = pivot.transform.position;
            swayBaseRotation = pivot.transform.rotation;
            useSwayBase = false; // Start with local sway
        }

        noiseOffsetX = Random.Range(0f, 100f);
        noiseOffsetY = Random.Range(0f, 100f);
        noiseOffsetRotation = Random.Range(0f, 100f);

        // Resolve the emotion-transition style the user picked in the main menu.
        // No saved choice → fall back to the inspector `useCrossfade` toggle so
        // existing scenes behave exactly as before.
        activeTransition = PresenterTransitionSettings.LoadStyle(
            useCrossfade ? PresenterTransitionSettings.Style.Crossfade
                         : PresenterTransitionSettings.Style.SquashStretch);

        // The Shake variant drives a MugsShake on the pivot — the whole-character
        // rig that idle sway and squash already move — so the shudder rides on the
        // rest pose without fighting Left/Right/Center positioning (which lives on
        // an ancestor). Auto-add one if the user didn't wire their own.
        if (mugsShake == null && pivot != null)
            mugsShake = pivot.GetComponent<MugsShake>() ?? pivot.AddComponent<MugsShake>();

        // The Blink / Blink (Heavy) styles are driven by MugsEmotionTransition,
        // which squashes the pivot's height around its centre. Auto-add one if the
        // user didn't wire their own. (Cut needs no component — it's an instant swap.)
        if (mugsBlink == null && pivot != null)
            mugsBlink = pivot.GetComponent<MugsEmotionTransition>() ?? pivot.AddComponent<MugsEmotionTransition>();
    }

    void Update()
    {
        if (enableIdleSway && pivot != null && currentAnimation == null)
        {
            ApplyIdleSway();
        }

        // Keep crossfade renderer flip in sync with main renderer
        if (crossfadeRenderer != null)
        {
            crossfadeRenderer.flipX = avatarRenderer.flipX;
        }
    }

    /// <summary>
    /// Replaces the per-emotion sprites with runtime-loaded ones (e.g. from a
    /// user's VisualsSave). Called after Awake by VisualsRuntimeApplier on
    /// scene load. Updates the inspector-assigned sprite fields (for the
    /// canonical five names, so the Inspector reflects the override), grows
    /// the live emotionMap with every entry — including user-named ones not
    /// covered by the hardcoded fields — and refreshes the avatar's baseline
    /// height when Neutral is overridden.
    /// </summary>
    public void ApplyEmotionOverrides(IDictionary<string, Sprite> overrides)
    {
        // The Inspector emotion-image array is authoritative while the override
        // toggle is on — ignore menu/save-driven sprite overrides entirely so the
        // array "fully replaces" them (see useEmotionArrayOverride).
        if (useEmotionArrayOverride) return;

        if (overrides == null || overrides.Count == 0) return;
        if (emotionMap == null) emotionMap = new Dictionary<string, Sprite>();

        foreach (var kv in overrides)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;

            // Mirror to the inspector-serialized sprite fields for the
            // canonical five names so existing scene saves / debug views stay
            // in sync. Any name beyond those lives only in emotionMap, which
            // is the actual lookup source for ChangeEmotion.
            switch (kv.Key)
            {
                case "Neutral":   neutralSprite   = kv.Value; break;
                case "Excited":   excitedSprite   = kv.Value; break;
                case "Serious":   seriousSprite   = kv.Value; break;
                case "Sad":       sadSprite       = kv.Value; break;
                case "Concerned": concernedSprite = kv.Value; break;
            }

            emotionMap[kv.Key] = kv.Value;
        }

        if (avatarRenderer != null && neutralSprite != null)
        {
            avatarRenderer.sprite = neutralSprite;
            baselineSpriteHeight  = neutralSprite.bounds.size.y;
            NormalizeSpriteSize(avatarRenderer);
        }
    }

    /// <summary>
    /// Builds the emotion name→sprite map from the inspector emotionImageOverrides
    /// array (used when useEmotionArrayOverride is on). Skips blank names / null
    /// sprites; on a duplicate name the later entry wins. Resolves the idle /
    /// fallback sprite via the out parameter: the entry literally named "Neutral",
    /// else the first valid entry, else the inspector neutralSprite. Always
    /// guarantees a "Neutral" key so idle / fallback lookups resolve.
    /// </summary>
    Dictionary<string, Sprite> BuildEmotionMapFromArray(out Sprite neutral)
    {
        var map = new Dictionary<string, Sprite>();
        neutral = null;

        if (emotionImageOverrides != null)
        {
            foreach (var entry in emotionImageOverrides)
            {
                if (entry == null) continue;
                string key = (entry.emotionName ?? "").Trim();
                if (string.IsNullOrEmpty(key) || entry.sprite == null) continue;

                map[key] = entry.sprite; // last wins on a duplicate name
                if (neutral == null && string.Equals(key, "Neutral", System.StringComparison.Ordinal))
                    neutral = entry.sprite;
            }
        }

        if (neutral == null)
            foreach (var kv in map) { neutral = kv.Value; break; } // first valid entry
        if (neutral == null)
            neutral = neutralSprite; // last resort — the inspector field

        if (neutral != null && !map.ContainsKey("Neutral"))
            map["Neutral"] = neutral; // idle / fallback must always resolve

        if (map.Count == 0)
            Debug.LogWarning("[HybridAvatarSystem] 'Use Emotion Array Override' is ON but the " +
                             "Emotion Images array has no valid Name + Sprite entries.");

        return map;
    }

    /// <summary>
    /// The emotion names declared in the inspector emotionImageOverrides array,
    /// in order, de-duplicated, blanks skipped. Independent of the override
    /// toggle. Empty when nothing is configured. Consumed by the custom inspector's
    /// "Copy Emotion Names for Claude" button.
    /// </summary>
    public List<string> GetEmotionArrayNames()
    {
        var names = new List<string>();
        if (emotionImageOverrides == null) return names;
        foreach (var entry in emotionImageOverrides)
        {
            if (entry == null) continue;
            string n = (entry.emotionName ?? "").Trim();
            if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
        }
        return names;
    }

    public void SetSwayBase(Vector3 basePosition, Quaternion baseRotation)
    {
        swayBasePosition = basePosition;
        swayBaseRotation = baseRotation;
        useSwayBase = true;

        Debug.Log($"Sway base set to: {basePosition}");
    }

    // NEW: Method to return to local sway mode
    public void ReturnToLocalSway()
    {
        useSwayBase = false;
        pivot.transform.localPosition = originalPosition;
        pivot.transform.localRotation = originalRotation;

        Debug.Log("Returned to local sway mode");
    }

    void ApplyIdleSway()
    {
        float time = Time.time * swaySpeed;

        float noiseX = (Mathf.PerlinNoise(time + noiseOffsetX, 0f) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(time + noiseOffsetY, 1f) - 0.5f) * 2f;
        float noiseRotation = (Mathf.PerlinNoise(time + noiseOffsetRotation, 2f) - 0.5f) * 2f;

        Vector3 swayPosition = originalPosition + new Vector3(
            noiseX * swayAmountX,
            noiseY * swayAmountY,
            0f
        );

        pivot.transform.localPosition = swayPosition;

        Quaternion swayRotation = originalRotation * Quaternion.Euler(0f, 0f, noiseRotation * rotationAmount);
        pivot.transform.localRotation = swayRotation;
    }

    // NEW: Time-based processing
    public void ProcessWithExistingAudio(string scriptWithMarkers, AudioClip audio)
    {
        (cleanScript, timeMarkers) = ParseScriptWithTimeMarkers(scriptWithMarkers, audio.length);

        voiceAudio.clip = audio;
        voiceAudio.Play();  // Always play audio, recording or not

        if (autoRecord && recorder != null)
        {
            recorder.StartRecordingWithAudio();  // Will re-play audio, that's fine
        }

        StartCoroutine(TrackEmotionsByTime());
    }

    // Resolved lazily by TrackEmotionsByTime. Anything that stops the narration
    // AudioSource mid-script (a {Video:} used to Pause() it) makes isPlaying read
    // FALSE, so the loop consults IsShowingMedia to tell a still-running take
    // apart from a finished one — and to keep firing while a trailing clip or
    // image is on screen after the audio ends.
    private MediaPresentationSystem mediaPresentation;

    // SIMPLIFIED: Pure time-based tracking
    IEnumerator TrackEmotionsByTime()
    {
        lastTriggeredMarker = -1;

        if (mediaPresentation == null)
            mediaPresentation = FindObjectOfType<MediaPresentationSystem>();

        // Without the `|| IsShowingMedia` guard this loop exits the moment the
        // AudioSource stops and every later emotion is silently dropped.
        while (voiceAudio.isPlaying ||
               (mediaPresentation != null && mediaPresentation.IsShowingMedia))
        {
            float currentTime = voiceAudio.time + timingOffset;

            // Check each marker
            for (int i = lastTriggeredMarker + 1; i < timeMarkers.Count; i++)
            {
                if (currentTime >= timeMarkers[i].triggerTime)
                {
                    var marker = timeMarkers[i];

                    // Precedence: an explicit per-tag override ({Emotion,Style})
                    // wins; otherwise markers inside the start window jump-cut
                    // (Cut) so nothing animates in the first moment of the
                    // recording; otherwise the global style from the main menu.
                    PresenterTransitionSettings.Style style =
                        marker.transitionOverride
                        ?? (marker.triggerTime < instantCutStartWindow
                                ? PresenterTransitionSettings.Style.Cut
                                : activeTransition);

                    Debug.Log($"Triggering emotion {marker.emotion} at {currentTime:F2}s ({PresenterTransitionSettings.Label(style)})");
                    ChangeEmotion(marker.emotion, style);
                    lastTriggeredMarker = i;
                }
                else
                {
                    break; // Haven't reached this marker yet
                }
            }

            yield return null;
        }

        Debug.Log("Audio finished playing");
    }

    /// <summary>
    /// Instantly swap to an emotion with no transition animation — a clean
    /// jump-cut. Used by ScreenTransitionController's onCovered so the expression
    /// is already changed while the screen is fully covered (the squash / crossfade
    /// / shake would otherwise play in view as the transition reveals). No-op for
    /// an unknown emotion name (logs a warning, same as the timed path).
    /// </summary>
    public void SetEmotionImmediate(string emotion)
    {
        ChangeEmotion(emotion, PresenterTransitionSettings.Style.Cut);
    }

    void ChangeEmotion(string emotion, PresenterTransitionSettings.Style style)
    {
        if (emotionMap == null || string.IsNullOrEmpty(emotion))
        {
            Debug.LogError("Invalid emotion or emotionMap!");
            return;
        }

        if (emotionMap.ContainsKey(emotion))
        {
            if (avatarRenderer != null)
            {
                if (currentAnimation != null)
                {
                    StopCoroutine(currentAnimation);
                }

                Sprite sprite = emotionMap[emotion];
                switch (style)
                {
                    case PresenterTransitionSettings.Style.Crossfade:
                        currentAnimation = StartCoroutine(CrossfadeAnimation(sprite));
                        break;
                    case PresenterTransitionSettings.Style.Shake:
                        currentAnimation = StartCoroutine(ShakeTransition(sprite));
                        break;
                    case PresenterTransitionSettings.Style.Blink:
                    case PresenterTransitionSettings.Style.BlinkHeavy:
                    case PresenterTransitionSettings.Style.Cut:
                        RouteThroughBlink(style, sprite);
                        break;
                    default:
                        currentAnimation = StartCoroutine(SquashStretchAnimation(sprite));
                        break;
                }
                Debug.Log($"Changed emotion to: {emotion} ({PresenterTransitionSettings.Label(style)})");
            }
        }
        else
        {
            Debug.LogWarning($"Emotion '{emotion}' not found!");
        }
    }

    // Routes Blink / Blink (Heavy) / Cut through MugsEmotionTransition: the actual
    // sprite swap (and crossfade-overlay cleanup) runs at the fully-closed instant
    // of the blink — or immediately, for Cut. The blink only scales the pivot's
    // height, so idle sway (position/rotation) composes with it and we leave
    // currentAnimation null. Falls back to an instant swap if no blink component
    // is available (e.g. pivot unassigned).
    void RouteThroughBlink(PresenterTransitionSettings.Style style, Sprite newSprite)
    {
        System.Action apply = () =>
        {
            avatarRenderer.sprite = newSprite;
            NormalizeSpriteSize(avatarRenderer);

            if (crossfadeRenderer != null)
            {
                crossfadeRenderer.color  = new Color(1f, 1f, 1f, 0f);
                crossfadeRenderer.sprite = null;
            }
        };

        if (mugsBlink != null)
        {
            EmotionTransition t =
                style == PresenterTransitionSettings.Style.BlinkHeavy ? EmotionTransition.BlinkHeavy :
                style == PresenterTransitionSettings.Style.Cut        ? EmotionTransition.Cut        :
                                                                        EmotionTransition.Blink;
            mugsBlink.ChangeEmotion(apply, t);
        }
        else
        {
            apply();
        }

        currentAnimation = null;
    }

    IEnumerator SquashStretchAnimation(Sprite newSprite)
    {
        Transform avatarTransform = pivot.transform;
        Vector3 originalScale = avatarTransform.localScale;

        float elapsed = 0f;
        float phaseDuration = animationDuration / 3f;

        // Phase 1: Squash down
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;
            t = t * t;

            float scaleY = Mathf.Lerp(1f, 1f / squashAmount, t);
            float scaleX = Mathf.Lerp(1f, squashAmount, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        avatarRenderer.sprite = newSprite;
        NormalizeSpriteSize(avatarRenderer);

        elapsed = 0f;

        // Phase 2: Stretch up
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;
            t = 1f - (1f - t) * (1f - t);

            float scaleY = Mathf.Lerp(1f / squashAmount, squashAmount, t);
            float scaleX = Mathf.Lerp(squashAmount, 1f / squashAmount, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        elapsed = 0f;

        // Phase 3: Settle back
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;
            t = t * t * t;

            float scaleY = Mathf.Lerp(squashAmount, 1f, t);
            float scaleX = Mathf.Lerp(1f / squashAmount, 1f, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        avatarTransform.localScale = originalScale;

        currentAnimation = null;
    }

    IEnumerator CrossfadeAnimation(Sprite newSprite)
    {
        // Place the new sprite on the overlay renderer and fade it in
        // while the old sprite remains fully visible underneath
        crossfadeRenderer.sprite = newSprite;
        crossfadeRenderer.flipX = avatarRenderer.flipX;
        NormalizeSpriteSize(crossfadeRenderer);

        float elapsed = 0f;
        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / crossfadeDuration);
            // Smooth step for a nicer blend
            t = t * t * (3f - 2f * t);

            crossfadeRenderer.color = new Color(1f, 1f, 1f, t);
            yield return null;
        }

        // Swap: the main renderer takes over the new sprite, overlay goes invisible
        avatarRenderer.sprite = newSprite;
        NormalizeSpriteSize(avatarRenderer);
        crossfadeRenderer.color = new Color(1f, 1f, 1f, 0f);
        crossfadeRenderer.sprite = null;

        currentAnimation = null;
    }

    // Shake variant — reproduces the MugsTech preview's "Shake" transition:
    // swap the expression instantly, then let MugsShake shudder the character
    // side-to-side and settle. We hold currentAnimation non-null for the shake's
    // length so idle sway (which writes the same pivot transform) stays
    // suppressed until the shudder settles back to rest. Falls back to a plain
    // instant swap if no MugsShake is available (e.g. pivot unassigned).
    IEnumerator ShakeTransition(Sprite newSprite)
    {
        avatarRenderer.sprite = newSprite;
        NormalizeSpriteSize(avatarRenderer);

        if (mugsShake != null)
        {
            mugsShake.Play();
            yield return new WaitForSeconds(mugsShake.duration);
        }
        else
        {
            // No shake component — yield one frame so ChangeEmotion's
            // currentAnimation assignment lands before we clear it below
            // (otherwise idle sway would stay suppressed).
            yield return null;
        }

        currentAnimation = null;
    }

    /// <summary>
    /// Rescales the given renderer's transform so its sprite renders at the same
    /// world-space height as the baseline (neutral) sprite — regardless of the
    /// source image's pixel dimensions. Preserves the scale the user configured
    /// on avatarRenderer in the Inspector by applying a multiplicative ratio.
    /// </summary>
    void NormalizeSpriteSize(SpriteRenderer renderer)
    {
        if (!normalizeSpriteSize) return;
        if (renderer == null || renderer.sprite == null) return;
        if (baselineSpriteHeight <= 0f) return;

        float currentHeight = renderer.sprite.bounds.size.y;
        if (currentHeight <= 0f) return;

        float ratio = baselineSpriteHeight / currentHeight;
        renderer.transform.localScale = initialAvatarScale * ratio;
    }

    // Time-based markers. Prefers exact T=X.XXX timestamps baked in by the
    // ElevenLabs pre-processor; falls back to proportional char-count timing
    // when no T= is present (scripts that haven't been pre-processed).
    (string, List<TimeMarkerData>) ParseScriptWithTimeMarkers(string script, float audioDuration)
    {
        List<TimeMarkerData> markerList = new List<TimeMarkerData>();
        string clean = script;

        // {Emotion}, {Emotion,T=X.XXX}, or with a per-tag transition override:
        // {Emotion,Style} where Style is one of Cut / Blink / BlinkHeavy /
        // SquashStretch / Crossfade / Shake (BlinkHeavy is listed before Blink so
        // the longer keyword wins). The Style modifier is accepted either before
        // (group 2) or after (group 4) the pre-processor's appended ,T= so all of
        // {Emotion,Style}, {Emotion,Style,T=X} and {Emotion,T=X,Style} parse.
        // Position/Zoom/Media markers carry a ':' and are stripped before this runs.
        const string styleAlt = "Cut|BlinkHeavy|Blink|SquashStretch|Crossfade|Shake";
        Regex regex = new Regex(
            @"\{(\w+)(?:,(" + styleAlt + @"))?(?:,T=(\d+(?:\.\d+)?))?(?:,(" + styleAlt + @"))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            string emotion = match.Groups[1].Value;

            // Optional per-tag transition override, accepted before (group 2) or
            // after (group 4) the appended ,T=. Null when absent → the marker uses
            // the global / start-window style chosen at trigger time.
            string modifier = match.Groups[2].Success ? match.Groups[2].Value
                            : match.Groups[4].Success ? match.Groups[4].Value
                            : null;
            PresenterTransitionSettings.Style? styleOverride = null;
            if (modifier != null &&
                PresenterTransitionSettings.TryParse(modifier,
                                                     out PresenterTransitionSettings.Style parsedStyle))
            {
                styleOverride = parsedStyle;
            }

            float markerTime;
            if (match.Groups[3].Success &&
                float.TryParse(match.Groups[3].Value,
                               System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out float parsed))
            {
                markerTime = parsed;
            }
            else
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            markerList.Add(new TimeMarkerData
            {
                triggerTime = markerTime,
                emotion = emotion,
                transitionOverride = styleOverride
            });

            Debug.Log($"Marker '{emotion}'{(styleOverride.HasValue ? $" [{styleOverride.Value}]" : "")} will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }
}

[System.Serializable]
public class TimeMarkerData
{
    public float triggerTime; // Time in seconds when emotion should trigger
    public string emotion;

    // Optional per-tag transition override from {Emotion,Style}. Null = use the
    // global style (or the start-window jump-cut) chosen at trigger time.
    public PresenterTransitionSettings.Style? transitionOverride;
}

/// <summary>
/// One Name → Sprite pair for HybridAvatarSystem.emotionImageOverrides, the
/// inspector-authored emotion set used when "Use Emotion Array Override" is on.
/// </summary>
[System.Serializable]
public class EmotionImageEntry
{
    [Tooltip("The {Name} your script / Claude fires for this expression, e.g. " +
             "Hyped → {Hyped}. Letters, digits, and underscores only (this is what " +
             "the script tag parser matches).")]
    public string emotionName;

    [Tooltip("The sprite shown for this emotion.")]
    public Sprite sprite;
}
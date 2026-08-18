using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// Manages content card timeline playback in the content zone.
/// Self-building: creates card GameObjects programmatically — no prefabs needed.
/// Auto-setup: if contentZone is unassigned, falls back to mediaDisplay's RectTransform.
/// </summary>
public class ContentZoneController : MonoBehaviour
{
    [Header("Content Zone")]
    [Tooltip("RectTransform where cards appear. If left empty, falls back to mediaDisplay's RectTransform.")]
    public RectTransform contentZone;

    [Tooltip("Fullscreen RectTransform where BigMedia cards appear (in front of the character). " +
             "If left empty, a screen-space overlay canvas is auto-created at sort order 31000.")]
    public RectTransform featureMediaZone;

    [Header("Assets")]
    [Tooltip("Optional — maps company names to logos, and b-roll descriptions to video clips.")]
    public ContentCardAssets cardAssets;

    [Header("Media Coexistence")]
    [Tooltip("Reference to the existing media display — hidden while a card is active. If left empty, auto-finds from MediaPresentationSystem.")]
    public RawImage mediaDisplay;

    [Header("Character Awareness")]
    [Tooltip("Reference to MediaPresentationSystem for reading character position. " +
             "Auto-found in Awake if left empty.")]
    public MediaPresentationSystem mediaPresentationSystem;

    // Timeline state
    private List<ContentCardEvent> timeline;
    private int lastTriggeredIndex = -1;
    private AudioSource voiceAudio;
    private bool isPaused = false;

    // Active card state
    private ContentCard activeCard;
    private ContentCardType activeCardType;
    private Coroutine durationCoroutine;
    private Coroutine hideAndShowCoroutine;

    // True while the active card is a duration-less (held) card — a persistent
    // BigText stack, or a side card opened with ",Start" in its duration slot.
    // It has no duration timer and stays up until its {Tag:End}, a transition
    // clears it, or the end-of-narration safety net closes it.
    private bool activeCardPersistent;

    // Cards triggered while another card is on screen wait here and play one
    // after another (each for its full duration) instead of cutting each other
    // off. Fixes co-timed tags — e.g. a {BRoll} and {Quote} mapped to the same
    // word — which used to clobber so only the last one showed.
    private readonly Queue<ContentCardEvent> cardQueue = new Queue<ContentCardEvent>();

    // Lazily-built mirror of `contentZone` on the right side of the screen. Side
    // cards tagged ",Right" rest here so they land on the right, mirroring how
    // default/",Left" cards sit in `contentZone` on the left.
    private RectTransform contentZoneRight;

    /// <summary>True when a content card is currently visible.</summary>
    public bool IsCardActive => activeCard != null;

    /// <summary>True while a card is visible OR more are queued to play. The
    /// recorder polls this so the take is held open until the queue drains.</summary>
    public bool HasActiveOrQueuedCard => activeCard != null || cardQueue.Count > 0;

    void Awake()
    {
        // Make sure a CardEntryAnimator exists in the scene before any card
        // tries to read from it. Adding it to ourselves keeps the timing/curve
        // settings visible right next to the controller in the inspector.
        if (FindObjectOfType<CardEntryAnimator>() == null)
            gameObject.AddComponent<CardEntryAnimator>();

        // Auto-wire MediaPresentationSystem if not set
        if (mediaPresentationSystem == null)
        {
            mediaPresentationSystem = GetComponent<MediaPresentationSystem>();
            if (mediaPresentationSystem == null)
                mediaPresentationSystem = FindObjectOfType<MediaPresentationSystem>();
        }

        // Auto-wire mediaDisplay from MediaPresentationSystem if not set
        if (mediaDisplay == null)
        {
            if (mediaPresentationSystem != null && mediaPresentationSystem.mediaDisplay != null)
            {
                mediaDisplay = mediaPresentationSystem.mediaDisplay;
                Debug.Log("ContentZoneController: auto-wired mediaDisplay from MediaPresentationSystem");
            }
        }

        // If no content zone assigned, create one as a SIBLING of the media display
        // (same parent, same size/position — but always active, independent of media display state)
        if (contentZone == null && mediaDisplay != null)
        {
            GameObject zoneGO = new GameObject("ContentZone_Cards", typeof(RectTransform));
            zoneGO.transform.SetParent(mediaDisplay.transform.parent, false);

            RectTransform zoneRT = zoneGO.GetComponent<RectTransform>();
            RectTransform mediaRT = mediaDisplay.rectTransform;

            // Copy layout from the media display
            zoneRT.anchorMin = mediaRT.anchorMin;
            zoneRT.anchorMax = mediaRT.anchorMax;
            zoneRT.pivot = mediaRT.pivot;
            zoneRT.anchoredPosition = mediaRT.anchoredPosition;
            zoneRT.sizeDelta = mediaRT.sizeDelta;
            zoneRT.localScale = mediaRT.localScale;

            // Render cards above the media display
            zoneGO.transform.SetSiblingIndex(mediaDisplay.transform.GetSiblingIndex() + 1);

            contentZone = zoneRT;
            Debug.Log("ContentZoneController: created content zone as sibling of mediaDisplay");
        }

        if (contentZone == null)
        {
            Debug.LogError("ContentZoneController: no contentZone assigned and no mediaDisplay to fall back to. Cards will not appear!");
        }

        // Give the side content zone its own high sorting order so its cards
        // render ABOVE the green-screen backdrop, the same way the fullscreen
        // feature zone (order 31000) already does — without this, side cards sit
        // at the canvas default (0) and the green plane occludes them. Kept just
        // below the feature zone so feature cards still layer on top.
        if (contentZone != null)
            EnsureSortingCanvas(contentZone, 30000);

        // Build a fullscreen zone for BigMedia cards if none is assigned.
        // Parent into the existing media canvas (the one the recorder's camera
        // captures) — a standalone Screen Space - Overlay canvas would NOT be
        // captured by CrossPlatformRecorder in Camera source mode.
        if (featureMediaZone == null)
        {
            Canvas hostCanvas = null;
            if (mediaPresentationSystem != null && mediaPresentationSystem.mediaCanvas != null)
                hostCanvas = mediaPresentationSystem.mediaCanvas;
            else if (mediaDisplay != null)
                hostCanvas = mediaDisplay.GetComponentInParent<Canvas>();

            Transform parent;
            if (hostCanvas != null)
            {
                // Nested Canvas sub-container so we can override sort order
                // without fighting sibling indices, while still rendering
                // through the host canvas (and therefore into the recording).
                GameObject wrapper = new GameObject("FeatureMediaZone_Container",
                    typeof(RectTransform), typeof(Canvas));
                wrapper.transform.SetParent(hostCanvas.transform, false);

                RectTransform wrt = wrapper.GetComponent<RectTransform>();
                wrt.anchorMin = Vector2.zero;
                wrt.anchorMax = Vector2.one;
                wrt.offsetMin = Vector2.zero;
                wrt.offsetMax = Vector2.zero;

                Canvas sub = wrapper.GetComponent<Canvas>();
                sub.overrideSorting = true;
                sub.sortingOrder = 31000;

                parent = wrapper.transform;
                Debug.Log("ContentZoneController: FeatureMediaZone parented to mediaCanvas (captured by recorder)");
            }
            else
            {
                // Fallback — no host canvas found. This overlay canvas will NOT
                // be captured by the Camera-source recorder; warn loudly.
                GameObject canvasGO = new GameObject("FeatureMedia_FallbackOverlayCanvas",
                    typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                Canvas canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 31000;
                parent = canvasGO.transform;
                Debug.LogWarning("ContentZoneController: no host canvas found for FeatureMediaZone — " +
                                 "falling back to Screen Space - Overlay. BigMedia cards will NOT be " +
                                 "captured by the camera-source recorder until mediaCanvas is assigned.");
            }

            GameObject zoneGO = new GameObject("FeatureMediaZone", typeof(RectTransform));
            zoneGO.transform.SetParent(parent, false);
            RectTransform zoneRT = zoneGO.GetComponent<RectTransform>();
            zoneRT.anchorMin = Vector2.zero;
            zoneRT.anchorMax = Vector2.one;
            zoneRT.offsetMin = Vector2.zero;
            zoneRT.offsetMax = Vector2.zero;

            featureMediaZone = zoneRT;
        }
    }

    /// <summary>
    /// Stores the timeline and audio reference for time-based tracking.
    /// Called by MediaPresentationSystem after parsing.
    /// </summary>
    public void SetTimeline(List<ContentCardEvent> events, AudioSource audio)
    {
        timeline = events;
        voiceAudio = audio;
        lastTriggeredIndex = -1;
        isPaused = false;
        cardQueue.Clear();

        Debug.Log($"ContentZoneController: Timeline set with {events.Count} events");
    }

    /// <summary>
    /// Coroutine that checks voiceAudio.time each frame and triggers cards.
    /// </summary>
    public IEnumerator TrackCardsByTime()
    {
        lastTriggeredIndex = -1;

        // When a recording is being made, playback starts a few frames AFTER
        // this coroutine: the recorder holds voiceAudio.Play() until the video
        // encoder's frame pacing settles (CrossPlatformRecorder.PlayWhenCaptureWarm),
        // so sampling isPlaying on the first frame would exit before the take
        // began. Wait for playback, bounded so a failed take can't hang us.
        float waitedForStart = 0f;
        while ((voiceAudio == null || !voiceAudio.isPlaying) && waitedForStart < 10f)
        {
            waitedForStart += Time.unscaledDeltaTime;
            yield return null;
        }

        // `|| IsShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen after the narration ends, so the flush
        // below only fires once nothing is left to display.
        while (voiceAudio != null && (voiceAudio.isPlaying ||
               (mediaPresentationSystem != null && mediaPresentationSystem.IsShowingMedia)))
        {
            if (timeline != null)
            {
                float currentTime = voiceAudio.time;

                for (int i = lastTriggeredIndex + 1; i < timeline.Count; i++)
                {
                    if (currentTime < timeline[i].triggerTime)
                        break;

                    // While paused (character centered) only fullscreen feature
                    // cards may appear; a side card would overlap the centered
                    // character, so skip it (advancing past it so it doesn't burst
                    // out later when a side position resumes).
                    if (isPaused && !IsFeatureCard(timeline[i].cardType))
                    {
                        lastTriggeredIndex = i;
                        continue;
                    }

                    Debug.Log($"Triggering card: {timeline[i].cardType} at {currentTime:F2}s");
                    ShowCard(timeline[i]);
                    lastTriggeredIndex = i;
                }
            }

            yield return null;
        }

        // Narration finished. An end-card placed on the script's final word
        // (a closing {Logo:...} or {Headline:...,bigCenter}) is clamped to the
        // clip-end time, and the loop above stops the moment playback ends — so
        // without this it would be skipped. Flush any still-pending cards now
        // (the recorder holds the take open to capture them). If the character
        // is centered, the per-card check below still lets feature cards through.
        if (timeline != null)
        {
            for (int i = lastTriggeredIndex + 1; i < timeline.Count; i++)
            {
                // Same rule as the live loop: while centered, only feature cards.
                if (isPaused && !IsFeatureCard(timeline[i].cardType))
                {
                    lastTriggeredIndex = i;
                    continue;
                }
                Debug.Log($"Flushing end-of-audio card: {timeline[i].cardType}");
                ShowCard(timeline[i]);
                lastTriggeredIndex = i;
            }
        }

        // Safety net: a held card whose {Tag:End} was never written (a
        // persistent BigText stack, or a side card opened with ",Start") has no
        // duration timer, and the recorder holds the take open while a card is
        // active (HasActiveOrQueuedCard) — it would hang the recording forever.
        // Watch the queue drain and close any held card that is (or becomes)
        // the active card. The flag is cleared before hiding so the close
        // fires exactly once per card.
        while (HasActiveOrQueuedCard)
        {
            if (activeCard != null && activeCardPersistent)
            {
                Debug.LogWarning($"Narration ended with a held {activeCardType} card still open — " +
                                 $"missing {{{activeCardType}:End}}. Closing it so the take can finish.");
                activeCardPersistent = false;
                HideCurrentCard();
            }
            yield return null;
        }
    }

    /// <summary>
    /// Display a card, or queue it behind any card that's already on screen so
    /// they play one after another (each for its full duration).
    /// </summary>
    public void ShowCard(ContentCardEvent evt)
    {
        RectTransform zone = GetZoneForCard(evt);
        if (zone == null)
        {
            Debug.LogError($"ContentZoneController: Cannot show {evt.cardType} card — no zone available!");
            return;
        }

        // The closing edge of a held pair — {BigText:End}, or a side card's
        // {Headline:End}/{Quote:End}/{Logo:End}/… . Closes the active card of
        // the same type (silently — the cut IN already got the sfx). A held
        // card that never made it on screen (crowded out into the queue while
        // its End passed) is dropped from the queue instead, so it can't pop
        // up later with nothing left to ever close it.
        if (evt.dismissesCard)
        {
            if (activeCard != null && activeCardType == evt.cardType)
            {
                activeCardPersistent = false; // hand the close to the normal hide flow
                HideCurrentCard();
            }
            else if (!RemoveQueuedHeldCard(evt.cardType))
                Debug.LogWarning($"{{{evt.cardType}:End}} fired with no {evt.cardType} on screen — ignored.");
            return;
        }

        // Persistent BigText flow ({BigText:LINE}…{BigText:End}, no durations):
        // a line tag lands on the open BigText stack instead of queueing behind
        // it. With no BigText on screen a line tag falls through and opens the
        // stack like a normal card.
        if (evt.cardType == ContentCardType.BigText && evt.duration <= 0f &&
            activeCard is BigTextCard stack)
        {
            if (stack.AppendLines(evt.primaryText))
                TagSfxPlayer.Instance.Play(ContentCardType.BigText);
            return;
        }

        // If a card is already on screen (or others are waiting), queue this one
        // so co-timed / overlapping cards play back-to-back instead of cutting
        // each other off. The queue drains as each card finishes its duration.
        if (activeCard != null || cardQueue.Count > 0)
        {
            cardQueue.Enqueue(evt);
            Debug.Log($"Queued card: {evt.cardType} (queue depth {cardQueue.Count})");
            return;
        }

        // A card taking the zone ends any {Video:} on screen — clips no longer
        // have a fixed lifetime, they run until the next beat, and this is one.
        if (mediaPresentationSystem != null)
            mediaPresentationSystem.DismissActiveMedia();

        hideAndShowCoroutine = StartCoroutine(HideAndShowSequence(evt, zone));
    }

    /// <summary>
    /// Shows the next queued card once the current one has finished. While the
    /// timeline is paused (character centered) it skips — and drops — side cards
    /// (only fullscreen feature cards may appear centered), so they don't pile up
    /// and burst out on resume.
    /// </summary>
    private void ShowNextQueued()
    {
        while (cardQueue.Count > 0)
        {
            ContentCardEvent next = cardQueue.Dequeue();

            if (isPaused && !IsFeatureCard(next.cardType))
                continue; // centered — drop pending side cards

            RectTransform zone = GetZoneForCard(next);
            if (zone == null)
                continue;

            hideAndShowCoroutine = StartCoroutine(HideAndShowSequence(next, zone));
            return;
        }
    }

    // Drops the first queued held card (duration-less opening edge) of the
    // given type. Called when its {Tag:End} arrives while the card is still
    // waiting behind another — the beat it belonged to has passed, and showing
    // it later would leave it with no End tag ever coming to close it.
    private bool RemoveQueuedHeldCard(ContentCardType type)
    {
        if (cardQueue.Count == 0) return false;

        bool removed = false;
        int count = cardQueue.Count;
        for (int i = 0; i < count; i++)
        {
            ContentCardEvent e = cardQueue.Dequeue();
            if (!removed && e.cardType == type && e.duration <= 0f && !e.dismissesCard)
            {
                removed = true;
                Debug.Log($"Dropped queued held {type} card — its {{{type}:End}} already fired.");
                continue;
            }
            cardQueue.Enqueue(e);
        }
        return removed;
    }

    private RectTransform GetZoneForCard(ContentCardEvent evt)
    {
        if (IsFeatureCard(evt.cardType))
            return featureMediaZone != null ? featureMediaZone : contentZone;

        // A side card tagged ",Right" rests on the right: route it to the
        // mirrored right-hand zone (built lazily). Default and ",Left" cards
        // stay in the original left-hand contentZone.
        if (evt.entryDirectionOverride == EntryDirection.FromRight)
        {
            RectTransform right = EnsureRightContentZone();
            if (right != null) return right;
        }
        return contentZone;
    }

    // Lazily builds the right-side content zone as a horizontal mirror of
    // `contentZone` about their shared parent's center, so a side card tagged
    // ",Right" comes to rest on the right of the screen — an exact mirror of how
    // default cards sit on the left. The card fills this zone the same way it
    // fills the left one, and the text/slide mirror-correction in
    // HideAndShowSequence reads THIS zone's matrix (same handedness as the left
    // zone), so glyphs read correctly and a FromRight slide still enters from the
    // right. Returns null only if there is no left zone to mirror.
    private RectTransform EnsureRightContentZone()
    {
        if (contentZoneRight != null) return contentZoneRight;
        if (contentZone == null) return null;

        GameObject go = new GameObject("ContentZone_Cards_Right", typeof(RectTransform));
        go.transform.SetParent(contentZone.parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        // Reflect the X anchors, pivot and position about the parent's center;
        // leave the vertical layout and the size untouched.
        rt.anchorMin        = new Vector2(1f - contentZone.anchorMax.x, contentZone.anchorMin.y);
        rt.anchorMax        = new Vector2(1f - contentZone.anchorMin.x, contentZone.anchorMax.y);
        rt.pivot            = new Vector2(1f - contentZone.pivot.x,     contentZone.pivot.y);
        rt.sizeDelta        = contentZone.sizeDelta;
        rt.anchoredPosition = new Vector2(-contentZone.anchoredPosition.x, contentZone.anchoredPosition.y);
        rt.localScale       = contentZone.localScale;

        // Same render order as the left zone (above the green-screen backdrop).
        go.transform.SetSiblingIndex(contentZone.GetSiblingIndex() + 1);
        EnsureSortingCanvas(rt, 30000);

        contentZoneRight = rt;
        Debug.Log("ContentZoneController: created mirrored right content zone");
        return contentZoneRight;
    }

    /// <summary>
    /// Fullscreen feature cards (BigMedia / BigCenter / BigText) render in the
    /// featureMediaZone in FRONT of the character, so they don't clash with a
    /// centered character — they're allowed to appear even while the timeline is
    /// paused (character centered). Side cards share the character's space and
    /// stay suppressed at Center.
    /// </summary>
    private static bool IsFeatureCard(ContentCardType type)
        => type == ContentCardType.BigMedia
        || type == ContentCardType.BigCenter
        || type == ContentCardType.BigText
        || type == ContentCardType.BigImage;

    // Gives a zone its own override-sorting canvas so its cards render at a fixed
    // order regardless of the parent canvas — used to lift side cards above the
    // green-screen backdrop. Idempotent.
    private static void EnsureSortingCanvas(RectTransform zone, int sortingOrder)
    {
        if (zone == null) return;
        var canvas = zone.GetComponent<Canvas>();
        if (canvas == null) canvas = zone.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
    }

    private IEnumerator HideAndShowSequence(ContentCardEvent evt, RectTransform zone)
    {
        // If a card is already showing, fast-hide it first
        if (activeCard != null)
        {
            if (durationCoroutine != null)
            {
                StopCoroutine(durationCoroutine);
                durationCoroutine = null;
            }

            bool hideComplete = false;
            activeCard.OnHideComplete = () => hideComplete = true;
            activeCard.Hide(fast: true);

            while (!hideComplete)
                yield return null;

            if (activeCard != null)
            {
                Destroy(activeCard.gameObject);
                activeCard = null;
            }
        }

        // Create a GameObject and add the appropriate card component
        GameObject cardObj = new GameObject(
            evt.cardType + "Card",
            typeof(RectTransform),
            typeof(CanvasGroup));
        cardObj.transform.SetParent(zone, false);

        // Counter any mirror in the parent hierarchy so text reads left-to-right.
        // Compute the 2D determinant of the XY-plane portion of the parent's
        // localToWorld matrix — if it's negative, the parent chain contains a
        // reflection (from a negative scale OR a 180° Y-rotation), and we flip
        // the card's X scale to counter it.
        Matrix4x4 parentMatrix = zone.localToWorldMatrix;
        float det2D = parentMatrix.m00 * parentMatrix.m11 - parentMatrix.m01 * parentMatrix.m10;
        float xSign = det2D < 0f ? -1f : 1f;
        cardObj.transform.localScale = new Vector3(xSign, 1f, 1f);

        switch (evt.cardType)
        {
            case ContentCardType.Headline: activeCard = cardObj.AddComponent<HeadlineCard>(); break;
            case ContentCardType.Excerpt: activeCard = cardObj.AddComponent<ExcerptCard>(); break;
            case ContentCardType.Quote: activeCard = cardObj.AddComponent<QuoteCard>(); break;
            case ContentCardType.Stat: activeCard = cardObj.AddComponent<StatCard>(); break;
            case ContentCardType.Logo: activeCard = cardObj.AddComponent<LogoDisplay>(); break;
            case ContentCardType.BRoll: activeCard = cardObj.AddComponent<BRollDisplay>(); break;
            case ContentCardType.BigMedia: activeCard = cardObj.AddComponent<BigMediaCard>(); break;
            case ContentCardType.BigCenter: activeCard = cardObj.AddComponent<BigCenterCard>(); break;
            case ContentCardType.BigText: activeCard = cardObj.AddComponent<BigTextCard>(); break;
            case ContentCardType.BigImage: activeCard = cardObj.AddComponent<BigImageCard>(); break;
            default:
                Debug.LogWarning($"Unknown card type: {evt.cardType}");
                Destroy(cardObj);
                yield break;
        }

        activeCardType = evt.cardType;
        activeCard.Initialize(evt, cardAssets);

        // Compute and apply entry direction based on the active style preset
        // (falls back to FromBottom if no preset is active).
        activeCard.SetEntryDirection(ComputeEntryDirection());

        // A per-tag ",Left"/",Right" suffix (side cards only) forces the side the
        // card flies in from, overriding the preset/animator default above. null
        // when the tag had no side modifier, leaving the default untouched.
        activeCard.SetDirectionOverride(evt.entryDirectionOverride);

        // The reflection we just countered on the card's localScale (so text
        // reads correctly) ALSO reverses a horizontal entry slide, because the
        // slide animates anchoredPosition in the still-mirrored zone space. Hand
        // the card the same sign so it flips the slide to enter from the intended
        // side instead of easing in backwards.
        activeCard.SetParentMirrorSign(xSign);

        activeCard.Show();

        // Per-tag sound effect — fires as the card actually appears. Queued or
        // centered-suppressed cards never reach here, so there are no phantom
        // sounds for cards that don't visibly show.
        TagSfxPlayer.Instance.Play(evt.cardType);

        // Start duration timer. A duration-less card is the held form — the
        // persistent BigText stack, or a side card opened with ",Start" — no
        // timer; it stays until its {Tag:End}, a transition, or the
        // end-of-narration safety net in TrackCardsByTime.
        activeCardPersistent = evt.duration <= 0f;
        if (!activeCardPersistent)
            durationCoroutine = StartCoroutine(DurationTimer(evt.duration));
    }

    private IEnumerator DurationTimer(float duration)
    {
        yield return new WaitForSeconds(duration);
        HideCurrentCard();
    }

    /// <summary>Fade out the currently active card and destroy it.</summary>
    public void HideCurrentCard()
    {
        if (activeCard == null) return;

        if (durationCoroutine != null)
        {
            StopCoroutine(durationCoroutine);
            durationCoroutine = null;
        }

        activeCard.OnHideComplete = () =>
        {
            if (activeCard != null)
            {
                Destroy(activeCard.gameObject);
                activeCard = null;
            }
            ShowNextQueued();
        };

        activeCard.Hide(fast: false);
    }

    /// <summary>
    /// Immediately clear the content zone for a whole-screen transition: drop any
    /// queued cards, stop the active card's timers, and destroy whatever is on
    /// screen with no fade. Called from ScreenTransitionController's onCovered, so
    /// the old card is gone the instant the screen reveals (the cover hides the
    /// pop-out). Does NOT touch the future timeline — later cards still fire.
    /// </summary>
    public void ClearForTransition()
    {
        cardQueue.Clear();

        if (durationCoroutine != null) { StopCoroutine(durationCoroutine); durationCoroutine = null; }
        if (hideAndShowCoroutine != null) { StopCoroutine(hideAndShowCoroutine); hideAndShowCoroutine = null; }

        if (activeCard != null)
        {
            ContentCard card = activeCard;
            activeCard = null;
            card.OnHideComplete = null;
            Destroy(card.gameObject);
        }
    }

    /// <summary>
    /// Pause the side-card timeline (character centered). Hides an active SIDE
    /// card, but leaves a fullscreen feature card (BigText/BigMedia/BigCenter)
    /// running — those sit in front of the character and don't conflict with it.
    /// </summary>
    public void PauseTimeline()
    {
        isPaused = true;

        if (activeCard != null && !IsFeatureCard(activeCardType))
        {
            if (durationCoroutine != null)
            {
                StopCoroutine(durationCoroutine);
                durationCoroutine = null;
            }

            activeCard.OnHideComplete = () =>
            {
                if (activeCard != null)
                {
                    Destroy(activeCard.gameObject);
                    activeCard = null;
                }
                // Drain the queue — feature cards may still play centered; the
                // skip logic in ShowNextQueued drops any queued side cards.
                ShowNextQueued();
            };

            activeCard.Hide(fast: true);
        }

        Debug.Log("ContentZoneController: Timeline paused (character centered)");
    }

    /// <summary>Resume the card timeline.</summary>
    public void ResumeTimeline()
    {
        isPaused = false;
        Debug.Log("ContentZoneController: Timeline resumed");
    }

    /// <summary>
    /// Decide which side the card should slide in from based on the active
    /// style preset and the character's current position.
    /// </summary>
    private EntryDirection ComputeEntryDirection()
    {
        var preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;
        if (preset == null) return EntryDirection.FromBottom;

        switch (preset.entryDirection)
        {
            case EntryDirectionMode.FromLeft:    return EntryDirection.FromLeft;
            case EntryDirectionMode.FromRight:   return EntryDirection.FromRight;
            case EntryDirectionMode.FromBottom:  return EntryDirection.FromBottom;
            case EntryDirectionMode.FromTop:     return EntryDirection.FromTop;

            case EntryDirectionMode.CharacterFacing:
                if (mediaPresentationSystem != null)
                {
                    switch (mediaPresentationSystem.CurrentPosition)
                    {
                        case CharacterPosition.Left:  return EntryDirection.FromLeft;
                        case CharacterPosition.Right: return EntryDirection.FromRight;
                        default:                      return EntryDirection.FromBottom;
                    }
                }
                return EntryDirection.FromBottom;

            default:
                return EntryDirection.FromBottom;
        }
    }
}

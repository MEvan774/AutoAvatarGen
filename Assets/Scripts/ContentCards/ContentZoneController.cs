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
    // BigText stack, or a side card / big headline opened with ",Start" in its
    // duration slot.
    // It has no duration timer and stays up until its {Tag:End}, a transition
    // clears it, or the end-of-narration safety net closes it.
    private bool activeCardPersistent;

    // Cards triggered while another card is on screen wait here and play one
    // after another (each for its full duration) instead of cutting each other
    // off. Fixes co-timed tags — e.g. a {BRoll} and {Quote} mapped to the same
    // word — which used to clobber so only the last one showed.
    private readonly Queue<ContentCardEvent> cardQueue = new Queue<ContentCardEvent>();

    // Held cards a {Zoom:ExtremeIn} cut away (or blocked) before their
    // {Tag:End} arrived. That End is then expected, not a script error, so it
    // is consumed as a quiet no-op instead of a warning.
    private readonly HashSet<ContentCardType> closeUpCutHeldTypes = new HashSet<ContentCardType>();

    // The side card waiting for the presenter to land, and how long it has
    // waited (seconds on Time.deltaTime, the clock the glide runs on).
    private int heldCardIndex = -1;
    private float heldCardSeconds;

    // {Tag:End} events whose card was skipped because the End came due while
    // the card was still waiting — there is nothing left for them to close.
    private readonly HashSet<int> skippedEndIndices = new HashSet<int>();

    // True once a Timeline was cleared (a transition, Center, a close-up) before
    // its {Timeline:End}: its later nodes are ignored, and the End is a quiet
    // no-op that resets this. Warned once, when it happens.
    private bool timelineCleared;

    // The same for the two full-screen line-by-line runs: a wide Timeline and a
    // Clipping pile cleared (a transition, a close-up, for a Clipping also a
    // {Black}) before their End. Later nodes / sheets / the Span are ignored
    // until that End, which is then a quiet no-op.
    private readonly HashSet<ContentCardType> clearedRuns = new HashSet<ContentCardType>();

    // Lazily-built mirror of `contentZone` on the right side of the screen. Side
    // cards tagged ",Right" rest here so they land on the right, mirroring how
    // default/",Left" cards sit in `contentZone` on the left.
    private RectTransform contentZoneRight;

    // The wide stage (Mugs off or peeking — see MugsTech.Stage): side cards are
    // drawn here, centred and about 75 % of the frame wide, whatever ",Left" /
    // ",Right" they carry. Lazily built, like the right-hand zone.
    private RectTransform contentZoneWide;
    private bool stageWide;

    // The event the card on screen was opened by (its side, when it has to
    // glide back from the wide slot).
    private ContentCardEvent activeCardEvent;

    /// <summary>True while side cards are drawn in the wide slot.</summary>
    public bool IsStageWide => stageWide;

    /// <summary>
    /// True when the card on screen hides Mugs completely: a big headline's
    /// panel or a clipping's backdrop covers the frame edge to edge. (BigText,
    /// Versus and the other dimmed cards leave him visible behind them.)
    /// </summary>
    public bool ActiveCardCoversPresenter =>
        activeCard != null && (activeCardType == ContentCardType.BigCenter || activeCardType == ContentCardType.Clipping);

    /// <summary>
    /// Mugs left the frame (or came back): from now on side cards open in the
    /// wide slot (or their side slot again). A side card that is open glides
    /// there and grows / shrinks — at once when <paramref name="instant"/>
    /// (a ,Cut, or under a transition's cover).
    /// </summary>
    public void SetStageWide(bool wide, bool instant)
    {
        if (stageWide == wide) return;
        stageWide = wide;
        if (activeCard == null || IsFeatureCard(activeCardType)) return;

        // A card on its way out stays where it is: re-fitting it for the other
        // layout while it fades would make it drift and change size in its last
        // frames (a wide Stat would even jump larger). Same for a held card
        // whose End is due on this very beat — {Stat:End} written with the
        // {Position:…} that brings him back.
        // (isPaused: he is at Center, where a side card is hidden or left for a
        // close-up's cut — either way it is not staying.)
        if (activeCard.IsHiding || isPaused) return;
        if (activeCardEvent != null && voiceAudio != null && !float.IsNaN(activeCardEvent.endTime) &&
            activeCardEvent.endTime <= voiceAudio.time + 0.05f)
            return;

        RectTransform target = wide ? EnsureWideContentZone() : SideZoneFor(activeCardEvent);
        if (target == null) return;
        activeCard.SetStageLayout(wide, FrameHeight());
        activeCard.MoveToZone(target, instant ? 0f : MugsTech.Stage.StageMath.CardGlideSeconds);
        Debug.Log($"[Card] {activeCardType} card {(wide ? "glides to the centre and grows (wide stage)" : "glides back to its side")}" +
                  (instant ? " — at once" : ""));
    }

    private RectTransform SideZoneFor(ContentCardEvent evt)
    {
        if (evt != null && evt.entryDirectionOverride == EntryDirection.FromRight)
        {
            RectTransform right = EnsureRightContentZone();
            if (right != null) return right;
        }
        return contentZone;
    }

    // The frame's height in canvas px (the zones' parent is the canvas).
    private float FrameHeight()
    {
        RectTransform parent = contentZone != null ? contentZone.parent as RectTransform : null;
        float h = parent != null ? parent.rect.height : 0f;
        return h > 1f ? h : 1080f;
    }

    // Centred, 75 % × 78 % of the frame: clear of the strip above the cards
    // where the chapter titles and the Echo sit.
    private RectTransform EnsureWideContentZone()
    {
        if (contentZoneWide != null) return contentZoneWide;
        if (contentZone == null) return null;

        GameObject go = new GameObject("ContentZone_Cards_Wide", typeof(RectTransform));
        go.transform.SetParent(contentZone.parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(MugsTech.Stage.StageMath.WideXMin, 1f - MugsTech.Stage.StageMath.WideYMax);
        rt.anchorMax = new Vector2(MugsTech.Stage.StageMath.WideXMax, 1f - MugsTech.Stage.StageMath.WideYMin);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = contentZone.localScale;

        go.transform.SetSiblingIndex(contentZone.GetSiblingIndex() + 1);
        EnsureSortingCanvas(rt, 30000);

        contentZoneWide = rt;
        Debug.Log("ContentZoneController: created the wide content zone");
        return contentZoneWide;
    }

    // -----------------------------------------------------------------------
    // The handover ({Position:…,Over}): Mugs comes up IN FRONT of the card that
    // is up. The zone's rules follow his new position for everything that
    // comes next — but that one card stays exactly as it is until it ends.
    // -----------------------------------------------------------------------

    private ContentCard handoverCard;

    /// <summary>True while the card Mugs came up in front of is still there (fading out included).</summary>
    public bool HasHandoverCard => handoverCard != null;

    /// <summary>True once that card is on its way out.</summary>
    public bool HandoverCardIsHiding => handoverCard != null && handoverCard.IsHiding;

    /// <summary>The tag name of the card on screen ("Versus", "Headline", "Timeline", …), or null.</summary>
    public string ActiveCardName =>
        activeCard == null ? null
        : activeCardType == ContentCardType.BigCenter ? "Headline"
        : RunTagName(activeCardType);

    /// <summary>
    /// He arrives in front of the card that is up. From here on the zone
    /// behaves as his position says (Center hides side cards, a full position
    /// means side slots) for anything NEW — the card under him is not hidden
    /// and not moved. (Nothing is touched when no card is up: he is over an
    /// image or a clip.)
    /// </summary>
    public void BeginHandover(bool centred, bool wide)
    {
        handoverCard = activeCard;
        isPaused = centred;
        stageWide = wide;
        Debug.Log(handoverCard != null
            ? $"[Card] {ActiveCardName} card stays as it is — Mugs is in front of it"
            : "[Card] Mugs is in front of the picture — no card is up");
    }

    /// <summary>He moved to another full position while in front of it: only Center's rule for new cards changes.</summary>
    public void MoveHandover(bool centred) => isPaused = centred;

    /// <summary>
    /// He left again ({Position:Off} or a peek) and it carries on without
    /// him: the stage is wide again, and the card is where it already was.
    /// </summary>
    public void LeaveHandover(bool wide)
    {
        handoverCard = null;
        isPaused = false;
        stageWide = wide;
    }

    /// <summary>The handover is over (what was under him has gone, or his next move follows the normal rules).</summary>
    public void EndHandover() => handoverCard = null;

    /// <summary>
    /// Something new comes due while he is in front of this card: it closes
    /// first, as it would for any new beat (a held card would otherwise keep
    /// the newcomer waiting for its End).
    /// </summary>
    public void CloseHandoverCard()
    {
        if (handoverCard == null || activeCard != handoverCard || activeCard.IsHiding) return;
        activeCardPersistent = false;
        HideCurrentCard();
    }

    // An End, a mark, a strike or the next line of a line-by-line run acts on
    // the card that is up; anything else is a new beat.
    private bool OpensSomethingNew(ContentCardEvent evt)
    {
        if (evt.dismissesCard || evt.isMark || evt.strikesLogo) return false;
        if (activeCard == null) return true;
        if (evt.cardType == ContentCardType.Timeline && OpenTimeline != null) return false;
        bool run = evt.cardType == ContentCardType.BigText || evt.cardType == ContentCardType.TimelineWide ||
                   evt.cardType == ContentCardType.Clipping || evt.cardType == ContentCardType.BigMedia;
        return !(run && evt.duration <= 0f && evt.cardType == activeCardType);
    }

    // The End (or the next node) of the side card he is standing in front of
    // at Center must reach it — Center's "skip side cards" is for new ones.
    private bool ActsOnHandoverCard(ContentCardEvent evt)
    {
        if (handoverCard == null || activeCard != handoverCard) return false;
        if (evt.dismissesCard) return EndCloses(evt.cardType, activeCardType);
        return !OpensSomethingNew(evt);
    }

    /// <summary>True when a content card is currently visible.</summary>
    public bool IsCardActive => activeCard != null;

    /// <summary>The card on screen now (null when none) — read by the chapter titles.</summary>
    public ContentCard ActiveCard => activeCard;

    /// <summary>True when the card on screen is a fullscreen one (BigText / ,bigCenter / BigMedia / BigImage / Versus).</summary>
    public bool ActiveCardIsFeature => activeCard != null && IsFeatureCard(activeCardType);

    /// <summary>True while a card is visible OR more are queued to play. The
    /// recorder polls this so the take is held open until the queue drains.</summary>
    public bool HasActiveOrQueuedCard => activeCard != null || cardQueue.Count > 0;

    /// <summary>
    /// The trigger time of the first card event after <paramref name="now"/>
    /// (+∞ when none) — the idle camera drift eases home before it, so a card
    /// slides into a still frame.
    /// </summary>
    public float NextCardTime(float now)
    {
        float next = float.PositiveInfinity;
        if (timeline == null) return next;
        foreach (ContentCardEvent e in timeline)
            if (e.triggerTime > now && e.triggerTime < next) next = e.triggerTime;
        return next;
    }

    void Awake()
    {
        // Make sure a CardEntryAnimator exists in the scene before any card
        // tries to read from it. Adding it to ourselves keeps the timing/curve
        // settings visible right next to the controller in the inspector.
        if (FindObjectOfType<CardEntryAnimator>() == null)
            gameObject.AddComponent<CardEntryAnimator>();

        // Load the card fonts now, before the recorder starts — the Inter SDF
        // assets are 33 MB each and the heavy one is built at runtime, so
        // loading them on the take's first card would stall a captured frame.
        CardTypography.Prewarm();
        MapData.Prewarm(); // the baked country shapes (~0.9 MB), read before the take starts

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
        handoverCard = null;
        cardQueue.Clear();
        closeUpCutHeldTypes.Clear();
        heldCardIndex = -1;
        heldCardSeconds = 0f;
        skippedEndIndices.Clear();
        timelineCleared = false;
        clearedRuns.Clear();
        runItemsLanded.Clear();

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

                    if (skippedEndIndices.Contains(i))
                    {
                        lastTriggeredIndex = i;
                        continue;
                    }

                    // A new card that comes due while Mugs stands in front of
                    // one ({Position:…,Over}) closes that one first — a new
                    // beat; it then opens (or is skipped) by the rules below.
                    if (handoverCard != null && activeCard == handoverCard && !activeCard.IsHiding &&
                        OpensSomethingNew(timeline[i]) && mediaPresentationSystem != null)
                        mediaPresentationSystem.HandoverNewBeat($"a {timeline[i].cardType} card");

                    // While paused (character centered) only fullscreen feature
                    // cards may appear; a side card would overlap the centered
                    // character, so skip it (advancing past it so it doesn't burst
                    // out later when a side position resumes).
                    if (SkippedWhileCentered(timeline[i]))
                    {
                        BlockIfInCloseUp(timeline[i]); // skipped either way — this names the mistake
                        NoteTimelineSkippedAtCenter(timeline[i]);
                        lastTriggeredIndex = i;
                        continue;
                    }

                    // A side card waits for the presenter to finish a smooth
                    // move, or they walk across it. Retried next frame.
                    if (HoldForPresenter(i))
                        break;

                    // A card that comes due during a {Pop:} run waits behind it
                    // (as cards wait behind a big headline) and lands after {Pop:End}.
                    if (HoldBehindPop(timeline[i]))
                        break;

                    if (ReleaseHeldCard(i, currentTime))
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
                if (skippedEndIndices.Contains(i))
                {
                    lastTriggeredIndex = i;
                    continue;
                }

                // Same rule as the live loop: while centered, only feature cards.
                if (SkippedWhileCentered(timeline[i]))
                {
                    BlockIfInCloseUp(timeline[i]);
                    NoteTimelineSkippedAtCenter(timeline[i]);
                    lastTriggeredIndex = i;
                    continue;
                }
                Debug.Log($"Flushing end-of-audio card: {timeline[i].cardType}");
                ShowCard(timeline[i]);
                lastTriggeredIndex = i;
            }
        }

        // Safety net: a held card whose {Tag:End} was never written (a
        // persistent BigText stack, or a card opened with ",Start") has no
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

    // True while the event at this index must wait for the presenter to land.
    // Only side cards wait: fullscreen cards sit in front of the character and
    // closing something is always safe. Capped so the timeline can't stall.
    private bool HoldForPresenter(int index)
    {
        if (heldCardIndex != index)
        {
            heldCardIndex = index;
            heldCardSeconds = 0f;
        }

        ContentCardEvent evt = timeline[index];
        if (mediaPresentationSystem == null || evt.dismissesCard || evt.isMark || IsFeatureCard(evt.cardType) ||
            (evt.cardType == ContentCardType.Timeline && (OpenTimeline != null || timelineCleared)) ||
            !mediaPresentationSystem.IsPresenterGliding ||
            heldCardSeconds >= mediaPresentationSystem.PresenterLandCap)
            return false;

        heldCardSeconds += Time.deltaTime;
        return true;
    }

    // True while a {Pop:} run holds the frame and this event would put
    // something new on it. Ends, marks and strikes act on what is already up,
    // so they pass — unless a new card is already being held ahead of them:
    // the tracker stops at the first held event, so script order is kept and
    // they land with it at {Pop:End}. The run always ends: its End, or the
    // narration's.
    private bool HoldBehindPop(ContentCardEvent evt)
    {
        if (mediaPresentationSystem == null || !mediaPresentationSystem.IsPopRunning) return false;
        return !evt.dismissesCard && !evt.isMark && !evt.strikesLogo;
    }

    // Reports a hold that just ended. Returns true when the card should be
    // skipped: a held card whose own End came due while it waited would only
    // open and close, so both go.
    private bool ReleaseHeldCard(int index, float currentTime)
    {
        if (heldCardSeconds <= 0f) return false;

        ContentCardEvent evt = timeline[index];
        Debug.Log($"Held {evt.cardType} card {heldCardSeconds:F2}s for the presenter to land");
        MugsTech.Stage.PresenterHolds.Add(heldCardSeconds);
        heldCardSeconds = 0f;

        if (evt.duration > 0f) return false; // timed — it has no End tag

        for (int i = index + 1; i < timeline.Count && timeline[i].triggerTime <= currentTime; i++)
        {
            if (timeline[i].dismissesCard && timeline[i].cardType == evt.cardType)
            {
                skippedEndIndices.Add(i);
                // A Timeline's later nodes go with it — they'd open a timeline of their own.
                if (evt.cardType == ContentCardType.Timeline)
                    for (int j = index + 1; j < i; j++)
                        if (timeline[j].cardType == ContentCardType.Timeline) skippedEndIndices.Add(j);
                Debug.Log($"Skipped held {evt.cardType} card — its {{{evt.cardType}:End}} " +
                          "came due while it waited for the presenter.");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Display a card, or queue it behind any card that's already on screen so
    /// they play one after another (each for its full duration).
    /// </summary>
    public void ShowCard(ContentCardEvent evt)
    {
        // Not cards: a {Mark:…} moves the open text card's highlighter, a
        // {BigMedia:Strike,…} crosses out a logo of the open run. Neither is
        // ever queued, and neither shows anything of its own.
        if (evt.isMark) { ApplyMark(evt); return; }
        if (evt.strikesLogo) { StrikeLogo(evt); return; }

        RectTransform zone = GetZoneForCard(evt);
        if (zone == null)
        {
            Debug.LogError($"ContentZoneController: Cannot show {evt.cardType} card — no zone available!");
            return;
        }

        // The closing edge of a held pair — {BigText:End}, or a card's
        // {Headline:End}/{Quote:End}/{Logo:End}/… . Closes the active card of
        // the same type (silently — the cut IN already got the sfx). A held
        // card that never made it on screen (crowded out into the queue while
        // its End passed) is dropped from the queue instead, so it can't pop
        // up later with nothing left to ever close it.
        if (evt.dismissesCard)
        {
            if (evt.cardType == ContentCardType.Timeline && timelineCleared)
            {
                timelineCleared = false;
                closeUpCutHeldTypes.Remove(ContentCardType.Timeline);
                Debug.Log("{Timeline:End} — its timeline was already cleared; nothing to close.");
                return;
            }
            if (clearedRuns.Remove(evt.cardType))
            {
                closeUpCutHeldTypes.Remove(evt.cardType);
                Debug.Log($"{{{RunTagName(evt.cardType)}:End}} — it was already cleared; nothing to close.");
                return;
            }
            if (EndClosesActiveCard(evt.cardType))
            {
                activeCardPersistent = false; // hand the close to the normal hide flow
                HideCurrentCard();
            }
            else if (closeUpCutHeldTypes.Remove(evt.cardType) ||
                     (evt.cardType == ContentCardType.Headline &&
                      closeUpCutHeldTypes.Remove(ContentCardType.BigCenter)))
                Debug.Log($"{{{evt.cardType}:End}} — the card was already cut away by " +
                          "{Zoom:ExtremeIn}; nothing to close.");
            else if (!RemoveQueuedHeldCard(evt.cardType))
                Debug.LogWarning($"{{{evt.cardType}:End}} fired with no {evt.cardType} on screen — ignored.");
            return;
        }

        if (BlockIfInCloseUp(evt))
            return;

        if (!IsFeatureCard(evt.cardType) && mediaPresentationSystem != null)
            mediaPresentationSystem.WarnIfZoomRunActive($"{evt.cardType} card");

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

        // A {Timeline:…} node: added to the open timeline, ignored after the
        // timeline was cleared, folded into one still waiting in the queue
        // (it catches up when it shows) — or it opens a new timeline below.
        if (evt.cardType == ContentCardType.Timeline && RouteTimelineNode(evt))
            return;

        // The other line-by-line runs work the same way: a wide Timeline's
        // nodes and Span, a Clipping pile's sheets, a logo run's logos.
        if (evt.cardType == ContentCardType.TimelineWide && RouteWideTimeline(evt))
            return;
        if (evt.cardType == ContentCardType.Clipping && RouteClipping(evt))
            return;
        if (evt.cardType == ContentCardType.BigMedia && evt.duration <= 0f && RouteLogo(evt))
            return;

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

    // True while a {Zoom:ExtremeIn} close-up is held, or its cut is a frame or
    // two away — anything shown now would be on top of his face, or flash up
    // just to be cut.
    private bool CloseUpCutComingOrHeld =>
        mediaPresentationSystem != null &&
        (mediaPresentationSystem.IsCloseUpHeld || mediaPresentationSystem.IsCloseUpCutImminent);

    // Nothing appears over the extreme close-up. Scripts never place cards
    // between ExtremeIn and ExtremeOut, so one that comes due there is a script
    // mistake: block it (and say so) rather than put it over his face. End
    // tags pass — closing something is always safe.
    private bool BlockIfInCloseUp(ContentCardEvent evt)
    {
        if (evt.dismissesCard || !CloseUpCutComingOrHeld) return false;

        Debug.LogWarning($"{evt.cardType} card came due on a {{Zoom:ExtremeIn}} " +
                         "close-up — blocked. Move it outside the ExtremeIn…ExtremeOut pair.");
        if (evt.duration <= 0f)
            closeUpCutHeldTypes.Add(evt.cardType); // so its End lands quietly
        return true;
    }

    // ---- Timeline nodes ------------------------------------------------------------

    // True when the node was handled here (added, folded or ignored); false
    // when it opens a new timeline.
    private bool RouteTimelineNode(ContentCardEvent evt)
    {
        if (timelineCleared)
        {
            Debug.Log($"[Card] Timeline node \"{evt.primaryText}\" ignored — its timeline was cleared before {{Timeline:End}}.");
            return true;
        }
        if (OpenTimeline is TimelineCard open)
        {
            AddTimelineNode(open, evt, instant: false);
            return true;
        }
        foreach (ContentCardEvent q in cardQueue)
            if (q.cardType == ContentCardType.Timeline && !q.dismissesCard)
                return true; // the queued timeline reads it ahead and adds it as it shows
        return false;
    }

    private void AddTimelineNode(TimelineCard open, ContentCardEvent evt, bool instant)
    {
        if (evt.entryDirectionOverride != null)
            Debug.LogWarning($"[Card] Timeline node \"{evt.primaryText}\": " +
                             $",{(evt.entryDirectionOverride == EntryDirection.FromRight ? "Right" : "Left")} on a later node is " +
                             "ignored — the first node's side applies.");
        if (!open.AppendNode(evt.primaryText, evt.secondaryText, instant))
        {
            Debug.LogWarning($"[Card] Timeline node \"{evt.primaryText}\" ignored — a timeline holds at most " +
                             $"{TimelineTypography.MaxNodes} nodes.");
            return;
        }
        float t = voiceAudio != null ? voiceAudio.time : evt.triggerTime;
        Debug.Log($"[Card] t={t:F2}s Timeline node {open.NodeCount} shown (\"{evt.primaryText}\")");
        if (!instant) TagSfxPlayer.Instance.Play(ContentCardType.Timeline);
    }

    /// <summary>
    /// Every node the Timeline opened by <paramref name="opening"/> will hold:
    /// itself, then the Timeline nodes after it up to its {Timeline:End} (at
    /// most five). An event that isn't in the timeline gets only itself.
    /// </summary>
    public static List<ContentCardEvent> AnticipateTimelineNodes(IList<ContentCardEvent> timeline, ContentCardEvent opening)
    {
        var nodes = new List<ContentCardEvent> { opening };
        int idx = timeline != null ? timeline.IndexOf(opening) : -1;
        if (idx < 0) return nodes;
        for (int i = idx + 1; i < timeline.Count && nodes.Count < TimelineTypography.MaxNodes; i++)
        {
            ContentCardEvent e = timeline[i];
            if (e.cardType != ContentCardType.Timeline) continue;
            if (e.dismissesCard) break;
            nodes.Add(e);
        }
        return nodes;
    }

    // The timeline on screen (or waiting in the queue) is gone before its End:
    // say so once; its later nodes are ignored until that End.
    private void NoteTimelineCleared(string by)
    {
        if (timelineCleared) return;
        timelineCleared = true;
        Debug.LogWarning($"[Card] Timeline cleared by {by} before {{Timeline:End}} — its later nodes are ignored.");
    }

    // The timeline on screen that a node can still join (not one fading out after its End).
    private TimelineCard OpenTimeline => activeCard is TimelineCard t && !t.IsClosing ? t : null;

    private bool TimelineOnScreenOrQueued()
    {
        if (OpenTimeline != null) return true;
        foreach (ContentCardEvent q in cardQueue)
            if (q.cardType == ContentCardType.Timeline && !q.dismissesCard) return true;
        return false;
    }

    // Centered, side cards are skipped as they come due. A Timeline's first
    // node skipped there takes its whole timeline with it; its End (skipped
    // too) still ends that.
    private void NoteTimelineSkippedAtCenter(ContentCardEvent evt)
    {
        if (evt.cardType != ContentCardType.Timeline) return;
        if (evt.dismissesCard) { timelineCleared = false; return; }
        if (!TimelineOnScreenOrQueued()) NoteTimelineCleared("Center (the presenter was centred as it came due)");
    }

    // ---- The other line-by-line runs: wide Timeline, Clipping, logo run ----------

    private static string RunTagName(ContentCardType type) =>
        type == ContentCardType.TimelineWide ? "Timeline" : type.ToString();

    private static bool IsRunType(ContentCardType type) =>
        type == ContentCardType.TimelineWide || type == ContentCardType.Clipping;

    private bool RunQueued(ContentCardType type)
    {
        foreach (ContentCardEvent q in cardQueue)
            if (q.cardType == type && !q.dismissesCard && !q.timelineSpan && !q.strikesLogo) return true;
        return false;
    }

    private readonly HashSet<ContentCardEvent> runItemsLanded = new HashSet<ContentCardEvent>();
    private TimelineWideCard OpenWideTimeline => activeCard is TimelineWideCard t && !t.IsClosing ? t : null;
    private ClippingCard OpenClipping => activeCard is ClippingCard c && !c.IsClosing ? c : null;
    private BigMediaCard OpenLogos => activeCard is BigMediaCard b && !b.IsClosing ? b : null;

    // True when the event was handled here (added, folded or ignored); false
    // when it opens a new wide timeline.
    private bool RouteWideTimeline(ContentCardEvent evt)
    {
        if (clearedRuns.Contains(ContentCardType.TimelineWide))
        {
            Debug.Log($"[Card] Timeline {(evt.timelineSpan ? "span" : "node")} \"{evt.primaryText}\" ignored — its timeline was cleared before {{Timeline:End}}.");
            return true;
        }
        if (OpenWideTimeline != null) { AddToRun(evt, instant: false); return true; }
        if (RunQueued(ContentCardType.TimelineWide)) return true;   // read ahead when it shows
        if (evt.timelineSpan)
        {
            Debug.LogWarning($"[Card] {{Timeline:Span,\"{evt.primaryText}\"}} fired with no wide timeline on screen — ignored.");
            return true;
        }
        return false;
    }

    private bool RouteClipping(ContentCardEvent evt)
    {
        if (clearedRuns.Contains(ContentCardType.Clipping))
        {
            Debug.Log($"[Card] Clipping \"{Shorten(evt.primaryText)}\" ignored — its run was cleared before {{Clipping:End}}.");
            return true;
        }
        if (OpenClipping != null) { AddToRun(evt, instant: false); return true; }
        return RunQueued(ContentCardType.Clipping);
    }

    // A duration-less {BigMedia:name}: lands on the open grid — a run, or a
    // timed BigMedia (which keeps its timed lifetime, the BigText rule).
    private bool RouteLogo(ContentCardEvent evt)
    {
        if (OpenLogos != null) { AddToRun(evt, instant: false); return true; }
        foreach (ContentCardEvent q in cardQueue)
            if (q.cardType == ContentCardType.BigMedia && q.duration <= 0f && !q.dismissesCard && !q.strikesLogo)
                return true;   // its run is still queued: read ahead when it shows
        return false;
    }

    // Adds one item to the run on screen: a wide-timeline node or its Span, a
    // clipping, a logo. Logs the line the render report reads, and the
    // warnings the limits call for.
    private void AddToRun(ContentCardEvent evt, bool instant)
    {
        // One item lands once. When a run opens straight from the tracker, the
        // catch-up in HideAndShowSequence and the tracker itself can both reach
        // an item written on the opener's beat, on the same frame.
        if (!runItemsLanded.Add(evt)) return;

        float t = voiceAudio != null ? voiceAudio.time : evt.triggerTime;

        if (activeCard is TimelineWideCard wide)
        {
            if (evt.timelineSpan)
            {
                if (wide.ShowSpan(evt.primaryText))
                    Debug.Log($"[Card] t={t:F2}s Timeline span shown (\"{evt.primaryText}\")");
                else
                    Debug.LogWarning($"[Card] {{Timeline:Span,\"{evt.primaryText}\"}} ignored — this timeline already shows its Span.");
                return;
            }
            if (!wide.AppendNode(evt.primaryText, evt.secondaryText, instant))
            {
                Debug.LogWarning($"[Card] Timeline node \"{evt.primaryText}\" ignored — a wide timeline holds at most " +
                                 $"{TimelineWideTypography.MaxNodes} nodes.");
                return;
            }
            Debug.Log($"[Card] t={t:F2}s Timeline shown wide (node {wide.NodeCount} of {Mathf.Max(wide.NodeCount, RunLength(activeCardEvent))}: \"{evt.primaryText}\")");
            if (!instant) TagSfxPlayer.Instance.Play(ContentCardType.Timeline);
            return;
        }

        if (activeCard is ClippingCard pile)
        {
            float age = pile.TopSheetLandedSeconds;   // since it LANDED — its drop isn't reading time
            if (!pile.AppendSheet(evt.primaryText, evt.secondaryText, evt.tertiaryText, instant))
            {
                Debug.LogWarning($"[Card] Clipping \"{Shorten(evt.primaryText)}\" ignored — a run holds at most {ClippingTypography.MaxSheets} clippings.");
                return;
            }
            if (!instant && age < ClippingTypography.MinReadSeconds)
                Debug.LogWarning($"[Card] t={t:F2}s the clipping before \"{Shorten(evt.primaryText)}\" was covered {age:F2}s after it landed — " +
                                 $"under {ClippingTypography.MinReadSeconds:F1}s is too short to read.");
            Debug.Log($"[Card] t={t:F2}s Clipping shown ({pile.SheetCount} of {Mathf.Max(pile.SheetCount, RunLength(activeCardEvent))})");
            return;
        }

        if (activeCard is BigMediaCard logos)
        {
            foreach (string part in (evt.primaryText ?? "").Split('+'))
            {
                string name = part.Trim();
                if (!logos.AppendLogo(name, instant))
                {
                    Debug.LogWarning($"[Card] BigMedia logo \"{name}\" ignored — a logo run holds at most {BigMediaGrid.MaxLogos}.");
                    continue;
                }
                Debug.Log($"[Card] t={t:F2}s BigMedia logo shown ({logos.LogoCount} of " +
                          $"{Mathf.Max(logos.LogoCount, logos.AnticipatedCount)}: \"{name}\")");
                if (!instant) TagSfxPlayer.Instance.Play(ContentCardType.BigMedia);
            }
        }
    }

    /// <summary>
    /// Every event of the run <paramref name="opening"/> starts, itself
    /// included, in script order up to its End: a wide Timeline's nodes and
    /// Span, a Clipping pile's sheets, a logo run's duration-less logos (for a
    /// logo run that stops at a timed BigMedia too). An event that isn't in
    /// the timeline (a card claimed by a transition line) gets only itself.
    /// </summary>
    public static List<ContentCardEvent> AnticipateRun(IList<ContentCardEvent> timeline, ContentCardEvent opening)
    {
        var run = new List<ContentCardEvent> { opening };
        int idx = timeline != null ? timeline.IndexOf(opening) : -1;
        if (idx < 0) return run;
        for (int i = idx + 1; i < timeline.Count; i++)
        {
            ContentCardEvent e = timeline[i];
            if (e.cardType != opening.cardType || e.isMark || e.strikesLogo) continue;
            if (e.dismissesCard) break;
            if (opening.cardType == ContentCardType.BigMedia && e.duration > 0f) break;   // the next timed row
            run.Add(e);
        }
        return run;
    }

    // How many items the run opened by this event holds (nodes, sheets or logos; a Span isn't one).
    private int RunLength(ContentCardEvent opening)
    {
        if (opening == null) return 1;
        int n = 0;
        foreach (ContentCardEvent e in AnticipateRun(timeline, opening))
        {
            if (e.timelineSpan) continue;
            n += opening.cardType == ContentCardType.BigMedia ? (e.primaryText ?? "").Split('+').Length : 1;
        }
        return Mathf.Max(1, n);
    }

    // When the active held card's {Tag:End} is due on the voice clock (NaN when
    // the script has none after it).
    private float FindEndTime(ContentCardEvent opening)
    {
        if (timeline == null) return float.NaN;
        float from = opening.triggerTime;
        int idx = timeline.IndexOf(opening);
        for (int i = idx >= 0 ? idx + 1 : 0; i < timeline.Count; i++)
        {
            ContentCardEvent e = timeline[i];
            if (!e.dismissesCard || e.triggerTime < from) continue;
            if (EndCloses(e.cardType, opening.cardType)) return e.triggerTime;
        }
        return float.NaN;
    }

    // A run on screen (or waiting in the queue) is gone before its End: say so
    // once; its later items are ignored until that End.
    private void NoteRunsCleared(string by)
    {
        foreach (ContentCardType type in new[] { ContentCardType.TimelineWide, ContentCardType.Clipping })
        {
            bool onScreen = type == ContentCardType.TimelineWide ? OpenWideTimeline != null : OpenClipping != null;
            if (!onScreen && !RunQueued(type)) continue;
            if (clearedRuns.Add(type))
                Debug.LogWarning($"[Card] {(type == ContentCardType.TimelineWide ? "Wide timeline" : "Clipping run")} cleared by {by} " +
                                 $"before {{{RunTagName(type)}:End}} — its later items are ignored.");
        }
    }

    /// <summary>
    /// A {Black} closes a Clipping pile early (the other full-screen cards stay
    /// under the black, as they always have). Later sheets are ignored and the
    /// pile's End is a quiet no-op.
    /// </summary>
    public void ClearClippingForBlack()
    {
        // A pile that is already lifting has had its End: nothing left to clear
        // (flagging it would swallow the NEXT run's sheets).
        if (OpenClipping == null && !RunQueued(ContentCardType.Clipping)) return;
        if (clearedRuns.Add(ContentCardType.Clipping))
            Debug.LogWarning("[Card] Clipping run cleared by a {Black} before {Clipping:End} — its later items are ignored.");

        // Drop the pile's queued opener, keep everything else that is waiting.
        int count = cardQueue.Count;
        for (int i = 0; i < count; i++)
        {
            ContentCardEvent e = cardQueue.Dequeue();
            if (e.cardType != ContentCardType.Clipping) cardQueue.Enqueue(e);
        }
        if (!(activeCard is ClippingCard)) return;

        if (durationCoroutine != null) { StopCoroutine(durationCoroutine); durationCoroutine = null; }
        ContentCard card = activeCard;
        activeCard = null;
        activeCardPersistent = false;
        card.OnHideComplete = null;
        card.KillAllTweens();
        if (Application.isPlaying) Destroy(card.gameObject);
        else DestroyImmediate(card.gameObject);
        ShowNextQueued();
    }

    // ---- {Mark:"phrase"} and {BigMedia:Strike,name} ---------------------------------

    private void ApplyMark(ContentCardEvent evt)
    {
        float t = voiceAudio != null ? voiceAudio.time : evt.triggerTime;
        string phrase = evt.primaryText ?? "";
        if (!(activeCard is IMarkable markable) || (activeCard is ClippingCard c && c.IsClosing))
        {
            Debug.LogWarning($"[Card] t={t:F2}s {{Mark:\"{Shorten(phrase)}\"}} fired with no Document, Excerpt or Clipping open — ignored.");
            return;
        }
        switch (markable.AddMark(phrase))
        {
            case MarkResult.Ok:
                Debug.Log($"[Card] t={t:F2}s Mark shown (\"{Shorten(phrase)}\" on the {activeCardType})");
                break;
            case MarkResult.NotFound:
                Debug.LogWarning($"[Card] t={t:F2}s {{Mark:\"{Shorten(phrase)}\"}}: the phrase isn't in the open {activeCardType}'s " +
                                 "text (it must match exactly) — ignored.");
                break;
            case MarkResult.TooMany:
                Debug.LogWarning($"[Card] t={t:F2}s {{Mark:\"{Shorten(phrase)}\"}} ignored — a card takes at most three marks.");
                break;
            case MarkResult.Repeated:
                Debug.LogWarning($"[Card] t={t:F2}s {{Mark:\"{Shorten(phrase)}\"}} ignored — that phrase is already marked.");
                break;
        }
    }

    private void StrikeLogo(ContentCardEvent evt)
    {
        float t = voiceAudio != null ? voiceAudio.time : evt.triggerTime;
        string name = evt.primaryText ?? "";
        BigMediaCard logos = OpenLogos;
        if (logos == null || !logos.IsRun)
        {
            Debug.LogWarning($"[Card] t={t:F2}s {{BigMedia:Strike,{name}}} fired with no logo run open — ignored. " +
                             "(A strike only works inside {BigMedia:name} … {BigMedia:End}.)");
            return;
        }
        switch (logos.Strike(name))
        {
            case MarkResult.Ok:
                Debug.Log($"[Card] t={t:F2}s BigMedia strike (\"{name}\")");
                break;
            case MarkResult.Repeated:
                Debug.LogWarning($"[Card] t={t:F2}s {{BigMedia:Strike,{name}}} ignored — that logo is already struck out.");
                break;
            default:
                Debug.LogWarning($"[Card] t={t:F2}s {{BigMedia:Strike,{name}}}: no logo called \"{name}\" is in the grid — ignored.");
                break;
        }
    }

    private static string Shorten(string text)
    {
        text = (text ?? "").Replace('\n', ' ');
        return text.Length <= 48 ? text : text.Substring(0, 45) + "…";
    }

    /// <summary>
    /// Shows the next queued card once the current one has finished. While the
    /// timeline is paused (character centered) it skips — and drops — side cards
    /// (only fullscreen feature cards may appear centered), so they don't pile up
    /// and burst out on resume.
    /// </summary>
    private void ShowNextQueued()
    {
        // A {Zoom:ExtremeIn} is about to cut the zone and the queue goes with
        // it — popping the next card now would flash it up for a frame or two
        // before the snap. Cut the queue here instead.
        if (cardQueue.Count > 0 && mediaPresentationSystem != null &&
            mediaPresentationSystem.IsCloseUpCutImminent)
        {
            CutForCloseUp();
            return;
        }

        while (cardQueue.Count > 0)
        {
            ContentCardEvent next = cardQueue.Dequeue();

            if (isPaused && !IsFeatureCard(next.cardType))
            {
                if (next.cardType == ContentCardType.Timeline && !next.dismissesCard) NoteTimelineCleared("Center");
                continue; // centered — drop pending side cards
            }

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
            if (!removed && EndCloses(type, e.cardType) && e.duration <= 0f && !e.dismissesCard)
            {
                removed = true;
                Debug.Log($"Dropped queued held {type} card — its {{{type}:End}} already fired.");
                continue;
            }
            cardQueue.Enqueue(e);
        }
        return removed;
    }

    // Which held card a {Tag:End} closes. A ",bigCenter" headline is still a
    // {Headline:} tag — the modifier only promotes it to the fullscreen
    // BigCenter card — so {Headline:End} closes a held big headline too.
    private static bool EndCloses(ContentCardType endType, ContentCardType cardType)
        => cardType == endType ||
           (endType == ContentCardType.Headline && cardType == ContentCardType.BigCenter);

    // True when the active card is the one this End closes. A timed big
    // headline doesn't take a {Headline:End} — that End belongs to a held side
    // headline still waiting in the queue behind it.
    private bool EndClosesActiveCard(ContentCardType endType)
    {
        if (activeCard == null || !EndCloses(endType, activeCardType)) return false;
        return activeCardType == endType || activeCardPersistent;
    }

    // While centered only fullscreen feature cards may appear — a side card
    // would overlap the character. A {Headline:End} still goes through when a
    // held big headline is waiting for it: that card is fullscreen, so Center
    // never hid it, and skipping the End would leave it up until the narration
    // ends.
    private bool SkippedWhileCentered(ContentCardEvent evt)
        => isPaused && !evt.isMark && !IsFeatureCard(evt.cardType) && !ClosesHeldBigHeadline(evt) &&
           !ActsOnHandoverCard(evt);

    private bool ClosesHeldBigHeadline(ContentCardEvent evt)
    {
        if (!evt.dismissesCard || evt.cardType != ContentCardType.Headline) return false;
        if (activeCard != null && activeCardType == ContentCardType.BigCenter && activeCardPersistent) return true;
        if (closeUpCutHeldTypes.Contains(ContentCardType.BigCenter)) return true;
        foreach (ContentCardEvent e in cardQueue)
            if (e.cardType == ContentCardType.BigCenter && e.duration <= 0f && !e.dismissesCard)
                return true;
        return false;
    }

    private RectTransform GetZoneForCard(ContentCardEvent evt)
    {
        if (IsFeatureCard(evt.cardType))
            return featureMediaZone != null ? featureMediaZone : contentZone;

        // Mugs is off or peeking: every side card is drawn centred and wide
        // (a ",Left" / ",Right" on it is accepted and ignored).
        if (stageWide)
        {
            RectTransform wide = EnsureWideContentZone();
            if (wide != null) return wide;
        }

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
    /// Fullscreen feature cards (BigMedia / BigCenter / BigText / BigImage /
    /// Versus) render in the
    /// featureMediaZone in FRONT of the character, so they don't clash with a
    /// centered character — they're allowed to appear even while the timeline is
    /// paused (character centered). Side cards share the character's space and
    /// stay suppressed at Center.
    /// </summary>
    public static bool IsFeatureCard(ContentCardType type)
        => type == ContentCardType.BigMedia
        || type == ContentCardType.BigCenter
        || type == ContentCardType.BigText
        || type == ContentCardType.BigImage
        || type == ContentCardType.Versus
        || type == ContentCardType.Clipping
        || type == ContentCardType.TimelineWide
        || type == ContentCardType.Graph;

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

        // The stage may have changed while the old card faded (Mugs left or came back).
        zone = GetZoneForCard(evt) ?? zone;

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
            case ContentCardType.Compare: activeCard = cardObj.AddComponent<CompareCard>(); break;
            case ContentCardType.Versus: activeCard = cardObj.AddComponent<VersusCard>(); break;
            case ContentCardType.Document: activeCard = cardObj.AddComponent<DocumentCard>(); break;
            case ContentCardType.Timeline: activeCard = cardObj.AddComponent<TimelineCard>(); break;
            case ContentCardType.Map: activeCard = cardObj.AddComponent<MapCard>(); break;
            case ContentCardType.Clipping: activeCard = cardObj.AddComponent<ClippingCard>(); break;
            case ContentCardType.TimelineWide: activeCard = cardObj.AddComponent<TimelineWideCard>(); break;
            case ContentCardType.Graph: activeCard = cardObj.AddComponent<GraphCard>(); break;
            default:
                Debug.LogWarning($"Unknown card type: {evt.cardType}");
                Destroy(cardObj);
                yield break;
        }

        activeCardType = evt.cardType;
        activeCardEvent = evt;
        activeCard.EnsureBuilt();
        activeCard.SetStageLayout(stageWide && zone == contentZoneWide, FrameHeight());

        // A BigText stack is sized once, from its longest line — including the
        // held flow's later {BigText:LINE} tags, read ahead from the timeline.
        if (activeCard is BigTextCard bigText)
            bigText.AnticipatedLines = AnticipateBigTextLines(timeline, evt);

        // A Timeline likewise, from all its nodes up to its End.
        List<ContentCardEvent> timelineNodes = null;
        if (activeCard is TimelineCard timelineCard)
        {
            timelineNodes = AnticipateTimelineNodes(timeline, evt);
            timelineCard.AnticipatedNodes = timelineNodes.ConvertAll(n => (n.primaryText, n.secondaryText));
        }

        // The other runs are sized once too, from what the script still holds.
        List<ContentCardEvent> runEvents = null;
        if (activeCard is TimelineWideCard wideTimeline)
        {
            runEvents = AnticipateRun(timeline, evt);
            var nodes = new List<(string, string)>();
            bool span = false;
            foreach (ContentCardEvent r in runEvents)
            {
                if (r.timelineSpan) span = true;
                else if (nodes.Count < TimelineWideTypography.MaxNodes) nodes.Add((r.primaryText, r.secondaryText));
            }
            wideTimeline.AnticipatedNodes = nodes;
            wideTimeline.SpanAnticipated = span;
        }
        else if (activeCard is ClippingCard)
        {
            runEvents = AnticipateRun(timeline, evt);
        }
        else if (activeCard is BigMediaCard logos && evt.duration <= 0f)
        {
            runEvents = AnticipateRun(timeline, evt);
            int count = 0;
            foreach (ContentCardEvent r in runEvents) count += (r.primaryText ?? "").Split('+').Length;
            logos.AnticipatedCount = Mathf.Min(count, BigMediaGrid.MaxLogos);
        }

        // A held card may use the time its End is due (a wide Quote spreads its type-on up to it).
        if (evt.duration <= 0f && float.IsNaN(evt.endTime)) evt.endTime = FindEndTime(evt);

        activeCard.Initialize(evt, cardAssets);

        // Nodes whose beat passed while this timeline waited in the queue land at once.
        if (activeCard is TimelineCard late && timelineNodes != null && voiceAudio != null)
            for (int i = 1; i < timelineNodes.Count && timelineNodes[i].triggerTime <= voiceAudio.time; i++)
                AddTimelineNode(late, timelineNodes[i], instant: true);

        // …and so do a run's later items (wide nodes, the Span, sheets, logos).
        if (runEvents != null && voiceAudio != null)
            for (int i = 1; i < runEvents.Count && runEvents[i].triggerTime <= voiceAudio.time; i++)
                AddToRun(runEvents[i], instant: true);

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

        EndZoomRunForFeatureCard(evt.cardType);

        activeCard.Show();
        LogCardShown(evt);

        // Per-tag sound effect — fires as the card actually appears. Queued or
        // centered-suppressed cards never reach here, so there are no phantom
        // sounds for cards that don't visibly show.
        TagSfxPlayer.Instance.Play(evt.cardType);

        // Start duration timer. A duration-less card is the held form — the
        // persistent BigText stack, or a card opened with ",Start" — no
        // timer; it stays until its {Tag:End}, a transition, or the
        // end-of-narration safety net in TrackCardsByTime.
        activeCardPersistent = evt.duration <= 0f;
        if (!activeCardPersistent)
            durationCoroutine = StartCoroutine(DurationTimer(evt.duration));
    }

    /// <summary>
    /// Every line the BigText stack opened by <paramref name="opening"/> will
    /// hold: its own '+'-joined lines, then the duration-less BigText tags after
    /// it in the timeline up to its {BigText:End} (for a timed BigText: those
    /// that come due within its duration). Stops at a new timed BigText (it
    /// queues as its own card) and at the 4-line limit. An event that isn't in
    /// the timeline (a card claimed by a transition line) gets its own lines.
    /// </summary>
    public static List<string> AnticipateBigTextLines(IList<ContentCardEvent> timeline, ContentCardEvent opening)
    {
        const int maxLines = 4;
        var lines = new List<string>(maxLines);
        void Add(string raw)
        {
            foreach (string part in (raw ?? string.Empty).Split('+'))
                if (lines.Count < maxLines) lines.Add(part.Trim());
        }

        Add(opening.primaryText);
        int idx = timeline != null ? timeline.IndexOf(opening) : -1;
        if (idx < 0) return lines;

        float end = opening.duration > 0f ? opening.triggerTime + opening.duration : float.MaxValue;
        for (int i = idx + 1; i < timeline.Count && lines.Count < maxLines; i++)
        {
            ContentCardEvent e = timeline[i];
            if (e.triggerTime > end) break;
            if (e.cardType != ContentCardType.BigText) continue;
            if (e.dismissesCard || e.duration > 0f) break; // its End, or the next timed card
            Add(e.primaryText);
        }
        return lines;
    }

    // Card types added by the 2026-11 visual-devices brief log one line as they
    // appear — "[Card] t=12.40s Compare shown (side=Left)" — which the MugsTech
    // render report reads from Player.log. The older cards keep their own lines.
    static readonly HashSet<ContentCardType> s_LoggedShown = new HashSet<ContentCardType>
        { ContentCardType.Compare, ContentCardType.Versus, ContentCardType.Document, ContentCardType.Timeline, ContentCardType.Map };

    private void LogCardShown(ContentCardEvent evt)
    {
        float t = voiceAudio != null ? voiceAudio.time : evt.triggerTime;

        // The evidence-first devices (2026-11): what appeared, and how much of a run it is.
        switch (evt.cardType)
        {
            case ContentCardType.Clipping:
                Debug.Log($"[Card] t={t:F2}s Clipping shown (1 of {RunLength(evt)})");
                return;
            case ContentCardType.TimelineWide:
                Debug.Log($"[Card] t={t:F2}s Timeline shown wide (node 1 of {RunLength(evt)}: \"{evt.primaryText}\")");
                return;
            case ContentCardType.Graph:
                Debug.Log($"[Card] t={t:F2}s Graph shown ({(activeCard is GraphCard g && g.IsFallback ? "as text rows" : evt.graphKind.ToString())}" +
                          $"{(evt.graphCropped && evt.graphKind == GraphKind.Line ? ", cropped axis" : "")})");
                return;
            case ContentCardType.BigMedia when evt.duration <= 0f:
                Debug.Log($"[Card] t={t:F2}s BigMedia logo shown (1 of {RunLength(evt)}: \"{evt.primaryText}\")");
                return;
        }
        if (stageWide && !IsFeatureCard(evt.cardType))
        {
            Debug.Log($"[Card] t={t:F2}s {evt.cardType} shown (wide, centred)");
            return;
        }

        if (!s_LoggedShown.Contains(evt.cardType)) return;
        string side = IsFeatureCard(evt.cardType) ? "Full" :
                      evt.entryDirectionOverride == EntryDirection.FromRight ? "Right" : "Left";
        Debug.Log($"[Card] t={t:F2}s {evt.cardType} shown (side={side})");
    }

    // A fullscreen feature card ends any {Zoom:In} run as it appears. Only
    // the big headline's panel is opaque edge to edge, and it slides up over
    // panelSlideDuration — so the camera snaps back once the panel lands,
    // unseen behind it. BigText and BigMedia float over the visible scene and
    // BigImage covers 3/4 of it, so with those the camera glides back as the
    // card comes in. A BigText line appended to an open stack never gets
    // here — only the stack's first line, or a timed BigText, ends a run.
    private void EndZoomRunForFeatureCard(ContentCardType type)
    {
        if (mediaPresentationSystem == null) return;

        switch (type)
        {
            case ContentCardType.BigCenter:
                if (activeCard is BigCenterCard big)
                    big.OnPanelCovered = () =>
                    {
                        if (mediaPresentationSystem != null)
                            mediaPresentationSystem.EndZoomRun("{Headline:…,bigCenter}", hiddenBehindCover: true);
                    };
                break;
            case ContentCardType.BigText:
            case ContentCardType.BigMedia:
            case ContentCardType.BigImage:
            case ContentCardType.Versus:
            case ContentCardType.Clipping:
            case ContentCardType.TimelineWide:
            case ContentCardType.Graph:
                mediaPresentationSystem.EndZoomRun($"{{{type}}}", hiddenBehindCover: false);
                break;
        }
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

        // An End (or an expiring duration) right on a {Zoom:ExtremeIn} is part
        // of that cut: don't start a fade the close-up would chop mid-way.
        if (LeaveForCloseUpCut(HideCurrentCard))
            return;

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
    public void ClearForTransition() => ClearZone("a transition");

    private void ClearZone(string by)
    {
        if (TimelineOnScreenOrQueued()) NoteTimelineCleared(by);
        NoteRunsCleared(by);
        cardQueue.Clear();

        if (durationCoroutine != null) { StopCoroutine(durationCoroutine); durationCoroutine = null; }
        if (hideAndShowCoroutine != null) { StopCoroutine(hideAndShowCoroutine); hideAndShowCoroutine = null; }

        if (activeCard != null)
        {
            ContentCard card = activeCard;
            activeCard = null;
            card.OnHideComplete = null;
            card.KillAllTweens(); // nothing may keep animating the destroyed hierarchy
            if (Application.isPlaying) Destroy(card.gameObject);
            else DestroyImmediate(card.gameObject); // the self-tests drive the controller in edit mode
        }
    }

    /// <summary>
    /// The {Zoom:ExtremeIn} jump cut: everything in the content zone — the
    /// active card of any type and the whole queue behind it — goes on this
    /// frame with no fade, exactly like a transition clear. Held cards cut
    /// here are remembered so their later {Tag:End} is a quiet no-op, and
    /// nothing comes back on ExtremeOut. The future timeline is untouched.
    /// </summary>
    public void CutForCloseUp()
    {
        // An active card still owed an End (one whose End was already handed
        // to this cut cleared the flag), plus any held card waiting in the queue.
        if (activeCard != null && activeCardPersistent)
            closeUpCutHeldTypes.Add(activeCardType);
        foreach (ContentCardEvent e in cardQueue)
            if (e.duration <= 0f && !e.dismissesCard)
                closeUpCutHeldTypes.Add(e.cardType);

        if (activeCard != null || cardQueue.Count > 0)
            Debug.Log($"ContentZoneController: {{Zoom:ExtremeIn}} cut away " +
                      (activeCard != null ? $"the {activeCardType} card" : "no active card") +
                      (cardQueue.Count > 0 ? $" and {cardQueue.Count} queued" : "") + ".");

        ClearZone("a {Zoom:ExtremeIn} close-up");
        activeCardPersistent = false;
    }

    // When a {Zoom:ExtremeIn} is about to cut (MediaPresentationSystem.
    // IsCloseUpCutImminent), the active card is left at full opacity for that
    // cut instead of starting a fade: writers stack the End tag,
    // {Position:Center,Cut} and the ExtremeIn on one word, and those can fire a
    // frame or two apart. The cut stops the watcher (it lives in
    // durationCoroutine); should the close-up never fire, the window lapses and
    // `close` runs as it would have. Returns false — do the close now — when no
    // cut is coming.
    private bool LeaveForCloseUpCut(System.Action close)
    {
        if (mediaPresentationSystem == null || !mediaPresentationSystem.IsCloseUpCutImminent)
            return false;

        if (durationCoroutine != null) StopCoroutine(durationCoroutine);
        durationCoroutine = StartCoroutine(CloseWhenCloseUpCutMisses(close));
        return true;
    }

    private IEnumerator CloseWhenCloseUpCutMisses(System.Action close)
    {
        while (mediaPresentationSystem != null && mediaPresentationSystem.IsCloseUpCutImminent)
            yield return null;

        durationCoroutine = null;
        if (activeCard != null)
            close();
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

            // A {Position:Center,Cut} right on a {Zoom:ExtremeIn} leaves the
            // side card for the close-up's cut rather than fading it.
            if (!LeaveForCloseUpCut(FastHideSideCard))
                FastHideSideCard();
        }

        Debug.Log("ContentZoneController: Timeline paused (character centered)");
    }

    private void FastHideSideCard()
    {
        if (activeCard == null || IsFeatureCard(activeCardType)) return;
        if (OpenTimeline != null) NoteTimelineCleared("Center");

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

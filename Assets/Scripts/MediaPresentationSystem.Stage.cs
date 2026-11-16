using System.Collections;
using DG.Tweening;
using MugsTech.Sound;
using MugsTech.Stage;
using MugsTech.Style;
using UnityEngine;

// ============================================================================
// MediaPresentationSystem — the evidence-first stage.
//
//   {Position:Off}                 Mugs leaves the frame (slides out; ,Cut = gone on the frame)
//   {Position:PeekLeft|PeekRight}  only his head comes up over the bottom edge
//   {Position:Left|Right|Center}   from either of those: he pops up from below
//
// While he is off or peeking the stage is WIDE: side cards are drawn centred
// and larger (ContentZoneController.SetStageWide), {Image:}/{Video:} fill the
// frame (cover-cropped), and a held image or clip arriving while another
// full-frame one is up CUTS to it (a media run). Left ↔ Right ↔ Center moves,
// and every existing script, run through the untouched code in
// MediaPresentationSystem.cs.
//
// The maths (where he goes, the easing, the screen-time count) is in
// MugsTech.Stage.StageMath, pure, so the self-tests cover it offline.
// ============================================================================
public partial class MediaPresentationSystem
{
    // ---- The presenter on the stage ---------------------------------------------------

    // True while one of the stage's own moves runs (slide-out, pop-up, peek
    // rise or drop). presenterGliding is set as well only when he is ARRIVING
    // at Left / Right / Center — that is what cards and media wait for.
    private bool stageMoving;

    // The script's first {Position:…} was Off: he was taken off before the
    // recorder started, so Start() must not put him back at Center.
    private bool openedWithoutPresenter;

    // {Zoom:ExtremeIn} tags ignored while he was away, still owed a quiet ExtremeOut.
    private int ignoredExtremeIns;
    private int stageIgnoredZooms;

    private Vector3 presenterRestScale = Vector3.one;
    private bool presenterRestKnown;
    private Vector2 stageCameraCentre;
    private bool stageCameraKnown;

    /// <summary>True while Mugs is off or only peeking: cards go centred and wide, media fills the frame.</summary>
    public bool IsStageWide => StageMath.IsWide(currentPosition);

    /// <summary>True while Mugs is out of frame ({Position:Off}).</summary>
    public bool IsPresenterOff => currentPosition == CharacterPosition.Off;

    /// <summary>True while only his head shows ({Position:PeekLeft/PeekRight}).</summary>
    public bool IsPresenterPeeking => StageMath.IsPeek(currentPosition);

    /// <summary>
    /// True while he is travelling in any way — a glide between sides, a
    /// slide-out, a pop-up, a peek rising or dropping. The captions stay off
    /// the frame meanwhile (he can pass through their band).
    /// </summary>
    public bool IsPresenterMoving => presenterGliding || stageMoving;

    /// <summary>True while a {Pop:…} run softens the frame (its clearing fade included).</summary>
    public bool IsPopShowing => pop != null && pop.IsShowing;

    /// <summary>True from a {Pop:…} run's first phrase to its {Pop:End}: cards that come due wait behind it.</summary>
    public bool IsPopRunning => pop != null && pop.IsRunning;

    /// <summary>True while a {Name:…} label is on screen.</summary>
    public bool IsNameShowing => nameLabel != null && nameLabel.IsShowing;

    float StageClock() => voiceAudio != null && voiceAudio.isPlaying ? voiceAudio.time : 0f;

    // The frame at rest, in world units. The centre is read once, before the
    // take (the idle drift and the zooms move the camera later).
    StageMath.Frame StageFrame()
    {
        if (!stageCameraKnown && mainCamera != null)
        {
            stageCameraCentre = mainCamera.transform.position;
            stageCameraKnown = true;
        }
        float half = defaultCameraSize > 0f ? defaultCameraSize
                   : mainCamera != null ? mainCamera.orthographicSize : 5f;
        float aspect = mainCamera != null && mainCamera.aspect > 0.1f ? mainCamera.aspect : 16f / 9f;
        return new StageMath.Frame { Centre = stageCameraCentre, HalfHeight = half, HalfWidth = half * aspect };
    }

    float PresenterScale()
    {
        if (avatarParent == null) return 1f;
        return Mathf.Abs(presenterRestScale.x) > 1e-5f ? avatarParent.localScale.x / presenterRestScale.x : 1f;
    }

    // His drawn outline relative to the transform that is moved, at scale 1 —
    // the sprite's tight mesh, not its texture rectangle (the emotion sprites
    // carry transparent canvas above his ears; see ChapterTitleController).
    StageMath.Body PresenterBody()
    {
        if (avatarParent == null || characterRenderer == null || characterRenderer.sprite == null)
            return StageMath.DefaultBody;

        var corners = new Vector3[4];
        Vector2 min, max;
        if (ChapterTitleController.SpriteOutlineWorld(characterRenderer, corners))
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 c in corners)
            {
                min = Vector2.Min(min, c);
                max = Vector2.Max(max, c);
            }
        }
        else
        {
            Bounds b = characterRenderer.bounds;
            min = b.min;
            max = b.max;
        }

        float s = Mathf.Max(1e-4f, Mathf.Abs(PresenterScale()));
        Vector2 centre = (min + max) * 0.5f - (Vector2)avatarParent.position;
        Vector2 size = max - min;
        if (size.x < 0.05f || size.y < 0.05f) return StageMath.DefaultBody;
        return new StageMath.Body { CentreOffset = centre / s, Size = size / s };
    }

    void SetPresenter(Vector2 at, float scale, bool peekSorting)
    {
        if (avatarParent == null) return;
        avatarParent.position = new Vector3(at.x, at.y, avatarParent.position.z);
        avatarParent.localScale = presenterRestScale * scale;
        if (avatarSystem != null) avatarSystem.SetPeekSorting(peekSorting);
    }

    // A peek faces into the frame.
    void SetPeekFacing(CharacterPosition peek)
    {
        if (characterRenderer != null)
            characterRenderer.flipX = peek == CharacterPosition.PeekRight;
    }

    /// <summary>
    /// A new take: the stage starts narrow with the count at zero. If the
    /// script's first {Position:…} is Off (with or without ,Cut) he is taken
    /// off NOW — before the recorder starts — so he isn't drawn on the first
    /// frame and nothing slides out; his first Left / Right / Center is then
    /// his entrance. (Every other opening position still lands on the first
    /// word, as it always has.)
    /// </summary>
    void BeginStageRun()
    {
        stageMeter = new StageMeter();
        emptyStage = new EmptyStageWatch();
        stageRunning = false;
        stageReported = false;
        stageLastVoiceTime = float.NaN;
        ignoredExtremeIns = 0;
        stageIgnoredZooms = 0;
        StageReport.Last = null;

        if (avatarParent != null && !presenterRestKnown)
        {
            presenterRestScale = avatarParent.localScale;
            presenterRestKnown = true;
        }
        StageFrame(); // reads the camera's rest centre while nothing has moved it

        if (positionMarkers == null || positionMarkers.Count == 0 ||
            positionMarkers[0].position != CharacterPosition.Off) return;

        // A {Position:…} on an earlier {Transition} line would be the script's first.
        float first = positionMarkers[0].triggerTime;
        if (transitionMarkers != null)
            foreach (TransitionMarkerData t in transitionMarkers)
                if (t.hasPosition && t.triggerTime < first) return;

        openedWithoutPresenter = true;
        if (avatarParent != null)
        {
            // Start() may not have run yet: park him from the centre mark.
            Vector2 at = centerLocation != null ? (Vector2)centerLocation.position : (Vector2)avatarParent.position;
            Vector2 gone = StageMath.Park(CharacterPosition.Center,
                StageMath.SlideOutTarget(CharacterPosition.Center, at, PresenterBody(), StageFrame()));
            SetPresenter(gone, 1f, peekSorting: false);
        }
        currentPosition = CharacterPosition.Off;
        if (contentZoneController != null)
        {
            contentZoneController.ResumeTimeline();
            contentZoneController.SetStageWide(true, instant: true);
        }
        Debug.Log("[Stage] t=0.00s the take opens without Mugs (the script's first {Position:} is Off).");
    }

    /// <summary>
    /// True when the next {Position:} is due on the voice clock and changes the
    /// layout (wide ↔ side). Media that comes due on the same word waits a
    /// frame for it — see ShowMedia.
    /// </summary>
    bool StageChangeIsDue()
    {
        int next = lastTriggeredPositionMarker + 1;
        if (positionMarkers == null || next >= positionMarkers.Count || voiceAudio == null) return false;
        PositionMarkerData m = positionMarkers[next];
        return voiceAudio.time >= m.triggerTime && StageMath.IsWide(m.position) != IsStageWide;
    }

    // In, Pullback and ExtremeIn need a face to frame. While he is off or
    // peeking each is ignored with one warning; the ExtremeOut that pairs with
    // an ignored ExtremeIn is then a quiet no-op (even if he is back by then).
    bool StageIgnoresZoom(ZoomType type)
    {
        if (type == ZoomType.ExtremeOut && float.IsNaN(extremeRestoreSize) && ignoredExtremeIns > 0)
        {
            ignoredExtremeIns--;
            Debug.Log("[Stage] {Zoom:ExtremeOut} — its ExtremeIn was ignored while Mugs was away; nothing to undo.");
            return true;
        }
        if (!IsStageWide) return false;
        if (type != ZoomType.In && type != ZoomType.Pullback && type != ZoomType.ExtremeIn) return false;

        stageIgnoredZooms++;
        if (type == ZoomType.ExtremeIn) ignoredExtremeIns++;
        Debug.LogWarning($"[Stage] t={StageClock():F2}s {{Zoom:{type}}} ignored — Mugs is " +
                         (IsPresenterOff ? "off screen" : "only peeking") + ", so there is no face to frame.");
        return true;
    }

    // -----------------------------------------------------------------------
    // A move that involves Off or a peek (MoveToPosition hands these over).
    // -----------------------------------------------------------------------
    void MoveOnStage(CharacterPosition target, bool? cutOverride)
    {
        CharacterPosition from = currentPosition;
        if (target == from)
        {
            Debug.Log($"[Stage] {{Position:{target}}} — he is already there; nothing to do.");
            return;
        }

        bool cut = cutOverride ?? useHardCuts;
        bool wasWide = StageMath.IsWide(from), nowWide = StageMath.IsWide(target);
        float t = StageClock();

        // The layout changing is a new beat for whatever {Image:}/{Video:} is up,
        // exactly as a change of side is. Off ↔ peek keeps the stage wide, so
        // the media stays.
        if (StageMath.MoveEndsMedia(from, target))
        {
            DismissActiveMedia();
            if (!isShowingMedia) ReleaseMediaHandover(hide: true);
        }

        // No face left to lean in on.
        if (nowWide && !wasWide)
            EndZoomRun($"{{Position:{target}}}", hiddenBehindCover: cut);

        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        presenterGliding = false;
        stageMoving = false;

        currentPosition = target;

        // Cards: Center still hides the side card; anything else lets them
        // show, in the wide slot while he is away. An open side card glides
        // to its new slot (or snaps there on a cut / under a transition cover).
        if (contentZoneController != null)
        {
            if (target == CharacterPosition.Center) contentZoneController.PauseTimeline();
            else contentZoneController.ResumeTimeline();
            contentZoneController.SetStageWide(nowWide, instant: cut);
        }

        string how;
        if (avatarParent == null)
        {
            how = "no presenter object";
        }
        else if (cut)
        {
            PlacePresenter(target, from);
            how = "cut";
        }
        else
        {
            presenterGliding = StageMath.CardsWaitFor(from, target); // cards wait for an ARRIVAL, never for him leaving
            stageMoving = true;
            movementCoroutine = StartCoroutine(StageTravel(from, target));
            SoundLayer.Fire(target == CharacterPosition.Off ? SfxKind.StageOut : SfxKind.StageIn);
            how = target == CharacterPosition.Off ? "slides out"
                : StageMath.IsPeek(target) ? "head comes up"
                : "pops up";
        }

        Debug.Log(target == CharacterPosition.Off
            ? $"[Stage] t={t:F2}s Mugs off ({how}) — the stage is wide"
            : StageMath.IsPeek(target)
                ? $"[Stage] t={t:F2}s Mugs peeks {(target == CharacterPosition.PeekRight ? "right" : "left")} ({how}) — the stage is wide"
                : $"[Stage] t={t:F2}s Mugs back at {target} ({how})");
    }

    // The pose a position ends in, set on this frame (a cut, or under a transition's cover).
    void PlacePresenter(CharacterPosition target, CharacterPosition from)
    {
        StageMath.Frame frame = StageFrame();
        StageMath.Body body = PresenterBody();

        if (target == CharacterPosition.Off)
        {
            Vector2 gone = StageMath.Park(from,
                StageMath.SlideOutTarget(from, avatarParent.position, body, frame, PresenterScale()));
            SetPresenter(gone, 1f, peekSorting: false);
        }
        else if (StageMath.IsPeek(target))
        {
            StageMath.PeekPose pose = StageMath.Peek(target == CharacterPosition.PeekRight, body, frame);
            SetPresenter(pose.Rest, pose.Scale, peekSorting: true);
            SetPeekFacing(target);
        }
        else
        {
            Transform rest = GetTransformForPosition(target);
            if (rest == null) return;
            SetPresenter(rest.position, 1f, peekSorting: false);
            avatarParent.rotation = rest.rotation;
            if (avatarSystem != null) avatarSystem.SetSwayBase(rest.position, rest.rotation);
            UpdateFacing(target);
        }
    }

    // Leaves where he is (a slide off the nearest edge, or a drop from a peek),
    // then arrives: parked out of reach, head rising for a peek, or popping up
    // from below the frame at a full position. Time.deltaTime, like MoveAvatar.
    // A Left/Right/Center move that arrives while he is still on his way back
    // from Off or a peek: the old move code stops the coroutine, so the return
    // is completed here first — rest pose, full scale, rest sorting, and the
    // stage no longer "moving" (captions and waiting cards read that flag).
    void FinishStageTravelNow()
    {
        if (!stageMoving) return;
        if (movementCoroutine != null) { StopCoroutine(movementCoroutine); movementCoroutine = null; }
        stageMoving = false;
        presenterGliding = false;
        if (avatarParent != null && StageMath.IsFull(currentPosition))
            PlacePresenter(currentPosition, currentPosition);
    }

    IEnumerator StageTravel(CharacterPosition from, CharacterPosition target)
    {
        StageMath.Frame frame = StageFrame();
        StageMath.Body body = PresenterBody();

        foreach (StageLeg leg in StageMath.PlanTravel(from, target))
        {
            Vector2 a = avatarParent.position;
            switch (leg)
            {
                case StageLeg.SlideOut:
                    yield return StageLegMove(a, StageMath.SlideOutTarget(from, a, body, frame, PresenterScale()),
                                              StageMath.SlideOutSeconds, StageMath.SlideOutEase);
                    break;

                case StageLeg.DropFromPeek:
                {
                    StageMath.PeekPose was = StageMath.Peek(from == CharacterPosition.PeekRight, body, frame);
                    yield return StageLegMove(a, new Vector2(a.x, was.Hidden.y), StageMath.PeekDropSeconds, StageMath.DropEase);
                    break;
                }

                case StageLeg.Park:
                    SetPresenter(StageMath.Park(from, a), 1f, peekSorting: false);
                    break;

                case StageLeg.RiseToPeek:
                {
                    StageMath.PeekPose pose = StageMath.Peek(target == CharacterPosition.PeekRight, body, frame);
                    SetPresenter(pose.Hidden, pose.Scale, peekSorting: true);
                    SetPeekFacing(target);
                    yield return StageLegMove(pose.Hidden, pose.Rest, StageMath.PeekRiseSeconds, StageMath.PopEase);
                    break;
                }

                case StageLeg.PopUp:
                {
                    Transform rest = GetTransformForPosition(target);
                    if (rest == null) break;
                    Vector2 r = rest.position;
                    Vector2 below = StageMath.BelowFrame(r, body, frame);
                    // In front of the card zones while he rises: a wide card that is
                    // closing (or gliding to its side) on this beat fades BEHIND him —
                    // on the first real take he came up behind a translucent Stat.
                    SetPresenter(below, 1f, peekSorting: true);
                    avatarParent.rotation = rest.rotation;
                    UpdateFacing(target);
                    yield return StageLegMove(below, r, StageMath.PopUpSeconds, StageMath.PopEase);
                    SetPresenter(r, 1f, peekSorting: false);
                    if (avatarSystem != null) avatarSystem.SetSwayBase(rest.position, rest.rotation);
                    break;
                }
            }
        }

        stageMoving = false;
        presenterGliding = false;
    }

    IEnumerator StageLegMove(Vector2 a, Vector2 b, float seconds, System.Func<float, float> ease)
    {
        float z = avatarParent.position.z;
        float t = 0f;
        while (t < seconds)
        {
            Vector2 p = Vector2.LerpUnclamped(a, b, ease(t / seconds));
            avatarParent.position = new Vector3(p.x, p.y, z);
            t += Time.deltaTime;
            yield return null;
        }
        avatarParent.position = new Vector3(b.x, b.y, z);
    }

    // A {Black} is a hard cut to nothing: the devices that sit on top of the
    // scene for a beat end with it — a Clipping pile, a {Name:} label, a {Pop:}
    // phrase. (The older full-screen cards stay under the black, as before.)
    void StageOnBlack()
    {
        if (contentZoneController != null) contentZoneController.ClearClippingForBlack();
        CloseStageOverlays("{Black}");
    }

    // The {Name:} label and the {Pop:} phrase both end early on a transition
    // (at its covered midpoint), a {Black} and an extreme close-up.
    void CloseStageOverlays(string by, float startedBefore = float.PositiveInfinity)
    {
        // startedBefore: a transition passes its own trigger time, so a label
        // or phrase written ON the transition line (same T=) is left alone —
        // judged by the tag's own T=, not by the clock: the cover lands about
        // a third of a second after the transition's T=.
        if (nameLabel != null) nameLabel.CloseNow(by, startedBefore);
        if (pop != null) pop.EndNow(by, startedBefore);
    }

    // -----------------------------------------------------------------------
    // His screen time, and the empty-stage fault
    // -----------------------------------------------------------------------

    private StageMeter stageMeter;
    private EmptyStageWatch emptyStage;
    private bool stageRunning, stageReported;
    private float stageLastVoiceTime = float.NaN;
    private int mediaHandoverIdleFrames;

    void Update()
    {
        StepStageMeter();

        // A held-over image whose successor never came (blocked, missing) must not stay up.
        if (mediaHandoverPending && !isShowingMedia)
        {
            if (++mediaHandoverIdleFrames > 3) ReleaseMediaHandover(hide: true);
        }
        else mediaHandoverIdleFrames = 0;
    }

    void StepStageMeter()
    {
        if (stageMeter == null || stageReported || voiceAudio == null) return;

        if (!voiceAudio.isPlaying)
        {
            // Not the recorder's warm-up gap (the voice restarts from 0 after
            // it): the clip has run to its end.
            AudioClip clip = voiceAudio.clip;
            if (stageRunning && clip != null && stageLastVoiceTime >= clip.length - 0.5f)
                FinishStageRun();
            return;
        }

        float t = voiceAudio.time;
        if (stageRunning && t < stageLastVoiceTime - 0.05f)
        {
            // The voice restarted (the recorder's warm-up): count from the real start.
            stageMeter = new StageMeter();
            emptyStage = new EmptyStageWatch();
            stageRunning = false;
        }
        float dt = stageRunning ? Mathf.Max(0f, t - stageLastVoiceTime) : 0f;
        stageRunning = true;
        stageLastVoiceTime = t;

        stageMeter.Add(dt, StageState());
        string warning = emptyStage.Step(t, dt, StageIsEmpty());
        if (warning != null) Debug.LogWarning(warning);
    }

    MugsOnScreen StageState()
    {
        if (currentPosition == CharacterPosition.Off) return MugsOnScreen.Off;
        if (StageMath.IsPeek(currentPosition)) return MugsOnScreen.Peek;
        bool covered = IsBlackShowing ||
                       (contentZoneController != null && contentZoneController.ActiveCardCoversPresenter);
        return covered ? MugsOnScreen.Covered : MugsOnScreen.Full;
    }

    // He is off and nothing carries the frame: no card (up or queued), no
    // image or clip, no black, no transition, no Echo, no Pop. A {Name:} label
    // on its own is not content, and the captions aren't either.
    bool StageIsEmpty()
    {
        if (!IsPresenterOff) return false;
        if (contentZoneController != null && contentZoneController.HasActiveOrQueuedCard) return false;
        if (isShowingMedia || mediaHandoverPending || IsBlackShowing || IsEchoShowing || IsPopShowing) return false;
        if (screenTransitionController != null && screenTransitionController.IsBusy) return false;
        return true;
    }

    void FinishStageRun()
    {
        if (stageReported || stageMeter == null) return;
        stageReported = true;

        string warning = emptyStage != null ? emptyStage.Finish() : null;
        if (warning != null) Debug.LogWarning(warning);

        Debug.Log(stageMeter.Line());
        float total = Mathf.Max(stageMeter.Total, 1e-6f);
        StageReport.Last = new StageReport
        {
            Seconds = stageMeter.Total,
            OnScreenShare = stageMeter.OnScreenShare,
            PeekShare = stageMeter.PeekShare,
            OffShare = stageMeter.OffSeconds / total,
            CoveredShare = stageMeter.CoveredSeconds / total,
            OffCount = stageMeter.OffCount,
            PeekCount = stageMeter.PeekCount,
            EmptyStageWarnings = emptyStage != null ? emptyStage.Warnings : 0,
            LongestEmptySeconds = emptyStage != null ? emptyStage.LongestSeconds : 0f,
            IgnoredZooms = stageIgnoredZooms,
            OpenedWithoutMugs = openedWithoutPresenter,
        };
    }

    // -----------------------------------------------------------------------
    // Media on the wide stage: full frame, runs that cut, the push-in, ,Scroll
    // -----------------------------------------------------------------------

    private enum MediaMotion { None, PushIn, Scroll }

    private bool mediaIsFullFrame;
    private MediaMotion mediaMotion;
    private Rect mediaCoverUv = new Rect(0f, 0f, 1f, 1f);
    private float mediaMotionClock;      // seconds this media has been up
    private float mediaMotionDuration;   // > 0: timed; 0: held
    private float mediaVisibleShare = 1f;
    private Vector2 mediaFocus = MediaMotionMath.CentreFocus;

    // A run: the full-frame media that just ended stays on the slot until the
    // one that replaces it has a texture to show, so the change is a cut with
    // no frame of background between them.
    private bool mediaHandoverPending;
    private Object mediaHandoverOwned;   // a disk texture or a clip's RenderTexture to free on the cut

    /// <summary>True while a full-frame image or clip is on screen (or held over for a cut).</summary>
    public bool IsMediaFullFrame => (isShowingMedia && mediaIsFullFrame) || mediaHandoverPending;

    // The slot for this media: the frame while the stage is wide, else the
    // authored side slot. Called from ShowDisplay once the texture is set.
    void ShowDisplayOnStage(MediaMarkerData marker, bool onRight)
    {
        RectTransform rt = mediaDisplay.rectTransform;
        Texture tex = mediaDisplay.texture;
        bool isImage = marker.mediaType == MediaType.IMAGE;
        float timed = marker.holdsOpen || !isImage ? 0f
                    : marker.displayDuration > 0f ? marker.displayDuration : DefaultMediaSeconds;

        mediaMotion = MediaMotion.None;
        mediaMotionClock = 0f;
        mediaMotionDuration = timed;
        mediaCoverUv = new Rect(0f, 0f, 1f, 1f);
        mediaFocus = MediaMotionMath.FocusFor(marker.mediaName);

        if (IsStageWide)
        {
            // The frame.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            mediaIsFullFrame = true;
        }
        else
        {
            ApplyMediaSide(onRight);
            mediaIsFullFrame = false;
            // A page that scrolls keeps the whole authored slot (it is fitted
            // to the slot's width, not letterboxed to its own shape).
            if (marker.scroll && isImage && mediaDisplayBaseSize.x > 0f)
                rt.sizeDelta = mediaDisplayBaseSize;
        }

        Rect slot = rt.rect;
        float slotW = slot.width > 1f ? slot.width : 1920f, slotH = slot.height > 1f ? slot.height : 1080f;

        if (marker.scroll && isImage)
        {
            float share = MediaMotionMath.VisibleShare(slotW, slotH, tex.width, tex.height);
            if (MediaMotionMath.CanScroll(share))
            {
                mediaMotion = MediaMotion.Scroll;
                mediaVisibleShare = share;
            }
            else
            {
                Debug.LogWarning($"[Media] {{Image:{marker.mediaName},…,Scroll}}: the image ({tex.width}×{tex.height}) is not " +
                                 "taller than its slot at that width — shown without scrolling.");
                if (!mediaIsFullFrame) FitDisplayToAspect(tex.width, tex.height);
            }
        }

        if (mediaMotion != MediaMotion.Scroll && mediaIsFullFrame)
        {
            mediaCoverUv = MediaMotionMath.CoverUv((float)tex.width / Mathf.Max(1, tex.height), slotW / slotH);
            if (isImage && StagePrefs.PushIn) mediaMotion = MediaMotion.PushIn;
        }
        ApplyMediaMotion();

        bool cut = mediaHandoverPending && mediaIsFullFrame;
        ReleaseMediaHandover(hide: false);
        mediaDisplay.gameObject.SetActive(true);

        string what = $"{{{(isImage ? "Image" : "Video")}:{marker.mediaName}}}";
        if (cut)
        {
            // The next item of a run: on the frame, no slide, no fade.
            if (mediaEntrySequence != null && mediaEntrySequence.IsActive()) mediaEntrySequence.Kill();
            mediaEntrySequence = null;
            if (mediaDisplayGroup != null) mediaDisplayGroup.alpha = 1f;
            Debug.Log($"[Media] t={StageClock():F2}s {what} cut in (full frame, a media run)");
        }
        else if (mediaIsFullFrame)
        {
            PlayMediaEntryFullFrame(onRight ? EntryDirection.FromRight : EntryDirection.FromLeft);
            Debug.Log($"[Media] t={StageClock():F2}s {what} shown full frame" +
                      (mediaMotion == MediaMotion.Scroll ? " (scrolling)" : mediaMotion == MediaMotion.PushIn ? " (push-in)" : ""));
        }
        else
        {
            PlayMediaEntry(onRight ? EntryDirection.FromRight : EntryDirection.FromLeft);
            if (mediaMotion == MediaMotion.Scroll)
                Debug.Log($"[Media] t={StageClock():F2}s {what} shown scrolling ({(onRight ? "right" : "left")} slot)");
        }
    }

    // The side entry without its overshoot or idle float: an image that fills
    // the frame must never bare the frame's edge once it has landed.
    void PlayMediaEntryFullFrame(EntryDirection dir)
    {
        if (mediaEntrySequence != null && mediaEntrySequence.IsActive())
            mediaEntrySequence.Kill();

        CardEntryAnimator anim = CardEntryAnimator.Instance;
        RectTransform rt = mediaDisplay.rectTransform;
        float width = rt.rect.width > 1f ? rt.rect.width : 1920f;
        Vector2 endPos = rt.anchoredPosition;
        Vector2 startOffset = new Vector2(dir == EntryDirection.FromRight ? width : -width, 0f);

        RectTransform parent = rt.parent as RectTransform;
        if (parent != null)
        {
            Matrix4x4 m = parent.localToWorldMatrix;
            if (m.m00 * m.m11 - m.m01 * m.m10 < 0f) startOffset.x = -startOffset.x;
        }

        rt.anchoredPosition = endPos + startOffset;
        if (mediaDisplayGroup != null) mediaDisplayGroup.alpha = 0f;

        Sequence seq = DOTween.Sequence()
            .Join(rt.DOAnchorPos(endPos, anim.defaultSlideDuration).SetEase(Ease.OutCubic));
        if (mediaDisplayGroup != null)
            seq.Join(mediaDisplayGroup.DOFade(1f, anim.DefaultEntryFadeDuration).SetEase(Ease.OutQuad));
        mediaEntrySequence = seq;
    }

    // One frame of the media's own motion (called from the display loops).
    void StepMediaMotion(float dt)
    {
        mediaMotionClock += Mathf.Max(0f, dt);
        if (mediaMotion != MediaMotion.None) ApplyMediaMotion();
    }

    void ApplyMediaMotion()
    {
        if (mediaDisplay == null) return;
        switch (mediaMotion)
        {
            case MediaMotion.PushIn:
                mediaDisplay.uvRect = MediaMotionMath.PushUv(mediaCoverUv,
                    MediaMotionMath.PushScaleAt(mediaMotionClock, mediaMotionDuration, MediaMotionMath.PushInMax), mediaFocus);
                break;
            case MediaMotion.Scroll:
                mediaDisplay.uvRect = MediaMotionMath.ScrollUv(mediaVisibleShare,
                    MediaMotionMath.ScrollProgress(mediaMotionClock, mediaMotionDuration, mediaVisibleShare));
                break;
            default:
                mediaDisplay.uvRect = mediaCoverUv;
                break;
        }
    }

    // True when the media ending now should stay up for the one that is due:
    // both full frame, the stage still wide, nothing else claiming the frame.
    bool NextMediaCutsIn()
    {
        if (!mediaIsFullFrame || !IsStageWide) return false;
        int next = lastTriggeredMediaMarker + 1;
        if (mediaMarkers == null || next >= mediaMarkers.Count || voiceAudio == null) return false;
        MediaMarkerData m = mediaMarkers[next];
        if (m.endsMedia || voiceAudio.time < m.triggerTime) return false;
        if (IsCloseUpHeld || IsCloseUpCutImminent || StageChangeIsDue()) return false;
        if (contentZoneController != null && contentZoneController.IsCardActive) return false;
        return true;
    }

    // A timed full-frame image is cut by a held image or a clip arriving (a
    // run); in the side layout a timed image still holds for its duration.
    bool TimedImageIsCutByARun()
    {
        if (!NextMediaCutsIn()) return false;
        MediaMarkerData m = mediaMarkers[lastTriggeredMediaMarker + 1];
        return m.holdsOpen || m.mediaType == MediaType.VIDEO;
    }

    void HoldMediaForCut(Object owned, MediaMarkerData marker)
    {
        if (mediaMotionClock < MediaMotionMath.MinRunImageSeconds)
            Debug.LogWarning($"[Media] {{{(marker.mediaType == MediaType.IMAGE ? "Image" : "Video")}:{marker.mediaName}}} was replaced " +
                             $"{mediaMotionClock:F2}s after it appeared — under {MediaMotionMath.MinRunImageSeconds:F1}s is too short to read. Shown anyway.");
        ReleaseMediaHandover(hide: false);
        mediaHandoverPending = true;
        mediaHandoverOwned = owned;
        mediaHandoverIdleFrames = 0;
        mediaMotion = MediaMotion.None;   // it holds its last frame until the cut
    }

    void ReleaseMediaHandover(bool hide)
    {
        if (mediaHandoverOwned != null) Destroy(mediaHandoverOwned);
        mediaHandoverOwned = null;
        if (!mediaHandoverPending) return;
        mediaHandoverPending = false;
        if (hide) ResetMediaSlot();
    }

    // Puts the slot back as the scene authored it (left side, full size, whole
    // texture, opaque) and hides it.
    void ResetMediaSlot()
    {
        if (mediaHandoverOwned != null) Destroy(mediaHandoverOwned);
        mediaHandoverOwned = null;
        mediaHandoverPending = false;
        mediaIsFullFrame = false;
        mediaMotion = MediaMotion.None;
        if (mediaDisplay == null) return;

        ApplyMediaSide(false);
        if (mediaDisplayBaseSize.x > 0f && mediaDisplayBaseSize.y > 0f)
            mediaDisplay.rectTransform.sizeDelta = mediaDisplayBaseSize;
        mediaDisplay.uvRect = new Rect(0f, 0f, 1f, 1f);
        if (mediaDisplayGroup != null) mediaDisplayGroup.alpha = 1f;

        // Drop the texture before hiding: whatever it points at is about to be
        // destroyed, and a RawImage holding a dead texture is the white quad
        // again the next time something activates it.
        mediaDisplay.texture = null;
        mediaDisplay.gameObject.SetActive(false);
    }
}

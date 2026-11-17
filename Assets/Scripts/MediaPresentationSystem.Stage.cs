using System.Collections;
using System.Collections.Generic;
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

    // His size on the stage (1, or the peek's scale) — kept apart from the
    // transform, whose scale also carries a leg's squash and stretch.
    private float presenterUniform = 1f;

    // True while a leg's pose (StageMoves) is on the transform: its scale and
    // rotation are then not his own, and stageShapeRotation is what to go back to.
    private bool stageShaped;
    private Quaternion stageShapeRotation = Quaternion.identity;

    // The leg that is running (null between moves). A {Position:…} that
    // arrives meanwhile plans from it: where he is, how big, how he leans.
    private StageLegRun stageRun;

    // His outline as last measured in his own shape (a squash would distort it).
    private StageMath.Body stageBody;
    private bool stageBodyKnown;
    private Vector2 stageCameraCentre;
    private bool stageCameraKnown;

    /// <summary>True while Mugs is off or only peeking: cards go centred and wide, media fills the frame.</summary>
    public bool IsStageWide => StageMath.IsWide(currentPosition);

    /// <summary>True while Mugs is out of frame ({Position:Off}).</summary>
    public bool IsPresenterOff => currentPosition == CharacterPosition.Off;

    /// <summary>True while only his head shows ({Position:PeekLeft/PeekRight}).</summary>
    public bool IsPresenterPeeking => StageMath.IsSmall(currentPosition);

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

    float PresenterScale() => avatarParent == null ? 1f : presenterUniform;

    // His drawn outline relative to the transform that is moved, at scale 1 —
    // the sprite's tight mesh, not its texture rectangle (the emotion sprites
    // carry transparent canvas above his ears; see ChapterTitleController).
    StageMath.Body PresenterBody()
    {
        if (avatarParent == null || characterRenderer == null || characterRenderer.sprite == null)
            return StageMath.DefaultBody;

        // Measured in the moved transform's OWN space, so it is the same
        // outline however he is turned (a lean), sized (a peek) or squashed.
        var corners = new Vector3[4];
        if (!ChapterTitleController.SpriteOutlineWorld(characterRenderer, corners))
        {
            Bounds wb = characterRenderer.bounds;
            corners[0] = new Vector3(wb.min.x, wb.min.y, wb.center.z);
            corners[1] = new Vector3(wb.min.x, wb.max.y, wb.center.z);
            corners[2] = new Vector3(wb.max.x, wb.max.y, wb.center.z);
            corners[3] = new Vector3(wb.max.x, wb.min.y, wb.center.z);
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 c in corners)
        {
            Vector2 local = avatarParent.InverseTransformPoint(c);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        // Local units → world units at his full size.
        var rest = new Vector2(Mathf.Abs(presenterRestScale.x), Mathf.Abs(presenterRestScale.y));
        Vector2 centre = Vector2.Scale((min + max) * 0.5f, rest);
        Vector2 size = Vector2.Scale(max - min, rest);
        if (size.x < 0.05f || size.y < 0.05f) return StageMath.DefaultBody;
        return new StageMath.Body { CentreOffset = centre, Size = size };
    }

    void SetPresenter(Vector2 at, float scale, bool peekSorting)
    {
        if (avatarParent == null) return;
        avatarParent.position = new Vector3(at.x, at.y, avatarParent.position.z);
        presenterUniform = scale;
        avatarParent.localScale = presenterRestScale * scale;
        SetPresenterLayer(peekSorting);
    }

    // A leg was cut short: his own shape again, upright, on this frame.
    void EndStageShape()
    {
        if (stageShaped && avatarParent != null)
        {
            avatarParent.localScale = presenterRestScale * presenterUniform;
            avatarParent.rotation = stageShapeRotation;
        }
        stageShaped = false;
    }

    // ---- Arrive on the word -----------------------------------------------------------

    // The stage's own change for a return that started early, kept for the tag's time.
    struct PendingStageChange
    {
        public bool active;
        public float at;          // the tag's time
        public bool endsMedia;    // his return ends the {Image:}/{Video:} that is up
    }
    private PendingStageChange pendingStage;

    // Set by the position tracker for the one marker it fires early: that tag's own time.
    private float positionLedUntil = float.NaN;
    private int stageLedArrivals, stageShortenedLeads;

    // A led arrival lands BEFORE the stage changes for it: the wide card he
    // rose in front of is still up, then closes or glides to its side. He
    // keeps the peek's layer (in front of the card zones) until it has — on
    // the first take he stood behind it for three frames after every landing.
    private bool frontHeld;
    private float frontHeldUntil = float.PositiveInfinity;   // voice time; +∞ until the stage has changed
    // How far short of its planned lead an arrival may start and still count as on time.
    const float LeadSlackSeconds = 0.05f;

    // What the stage does for his return, on the tag's time: the picture
    // ends, the zone follows his position (Center hides side cards, a wide
    // card glides to its side). If his lead was cut short he is still rising:
    // what comes due now waits for the rest of it.
    void ApplyPendingStageChange()
    {
        if (!pendingStage.active) return;
        pendingStage.active = false;

        if (pendingStage.endsMedia)
        {
            DismissActiveMedia();
            if (!isShowingMedia) ReleaseMediaHandover(hide: true);
        }
        if (contentZoneController != null && !handover)
        {
            if (currentPosition == CharacterPosition.Center) contentZoneController.PauseTimeline();
            else contentZoneController.ResumeTimeline();
            contentZoneController.SetStageWide(StageMath.IsWide(currentPosition), instant: false);
        }
        if (stageMoving && StageMath.IsFull(currentPosition)) presenterGliding = true;

        // From here the card that was up closes (0.3 s) or glides aside (0.35 s).
        if (frontHeld) frontHeldUntil = StageClock() + StageMath.CardGlideSeconds + 0.05f;
    }

    // Each frame: he goes back to his own layer once nothing is left in front of him.
    void StepFrontHold()
    {
        if (!frontHeld) return;
        bool cardUp = contentZoneController != null && contentZoneController.IsCardActive;
        if (pendingStage.active || (cardUp && StageClock() < frontHeldUntil)) return;
        frontHeld = false;
        frontHeldUntil = float.PositiveInfinity;
        if (stageRun == null && StageMath.IsFull(currentPosition)) SetPresenterLayer(false);
    }

    // Which arrivals start early, from the parsed (final) times. The
    // positions on {Transition} lines are part of the chain: they decide
    // where he is before the next standalone tag.
    void PlanArrivalLeads(float takeSeconds)
    {
        if (positionMarkers == null || positionMarkers.Count == 0) return;

        var cues = new List<PositionCue>();
        var owner = new List<PositionMarkerData>();   // null for a transition line's position
        foreach (PositionMarkerData m in positionMarkers)
        {
            cues.Add(new PositionCue { Time = m.triggerTime, Position = m.position, Cut = m.hardCutOverride ?? useHardCuts });
            owner.Add(m);
        }
        if (transitionMarkers != null)
            foreach (TransitionMarkerData tr in transitionMarkers)
                if (tr.hasPosition)
                {
                    cues.Add(new PositionCue { Time = tr.triggerTime, Position = tr.position, Cut = true, OnTransition = true });
                    owner.Add(null);
                }
        var order = new List<int>();
        for (int i = 0; i < cues.Count; i++) order.Add(i);
        order.Sort((a, b) => cues[a].Time != cues[b].Time ? cues[a].Time.CompareTo(cues[b].Time) : a.CompareTo(b));
        var sorted = new List<PositionCue>();
        foreach (int i in order) sorted.Add(cues[i]);

        var blacks = new List<(float time, float seconds, bool held, bool isEnd)>();
        if (blackPanelMarkers != null)
            foreach (BlackPanelMarkerData b in blackPanelMarkers)
                blacks.Add((b.triggerTime, b.duration, b.holdsOpen, b.endsPanel));

        ArrivalLead[] leads = ArrivalLeads.Plan(sorted, ArrivalLeads.BlackStretches(blacks, takeSeconds));
        for (int k = 0; k < leads.Length; k++)
        {
            PositionMarkerData m = owner[order[k]];
            if (m == null) continue;
            m.led = leads[k].Led;
            m.startTime = leads[k].Start;
            m.leadShortened = leads[k].Shortened;
            if (m.led)
                Debug.Log($"[Stage] {{Position:{m.position}}} at {m.triggerTime:F2}s: his move starts {leads[k].Seconds:F2} s early (at {m.startTime:F2}s) so he has landed on the word" +
                          (m.leadShortened ? " — shortened: the move that took him off ends then" : ""));
            else if (m.position != CharacterPosition.Off && sorted[k].Time > 0f && leads[k].Why != null &&
                     leads[k].Why != "he is on screen, so he moves on the word" && leads[k].Why != "he is already there")
                Debug.Log($"[Stage] {{Position:{m.position}}} at {m.triggerTime:F2}s starts on its tag ({leads[k].Why})");
        }
    }

    /// <summary>The arrivals that start early, as (start of the move, the tag's time): HybridAvatarSystem puts his arrival face on the first.</summary>
    public List<Vector2> LedArrivalWindows()
    {
        var windows = new List<Vector2>();
        if (positionMarkers != null)
            foreach (PositionMarkerData m in positionMarkers)
                if (m.led) windows.Add(new Vector2(m.startTime, m.triggerTime));
        return windows;
    }

    // His rotation with no lean on it (the marks all share it).
    Quaternion UprightRotation() => centerLocation != null ? centerLocation.rotation : Quaternion.identity;

    // His outline as it will be once he faces that way: the sprite is mirrored
    // about the moved transform, and his outline is not centred on it.
    StageMath.Body FacingBody(StageMath.Body body, bool mirrored) =>
        characterRenderer != null && characterRenderer.flipX != mirrored ? StageMath.Mirrored(body) : body;

    // A peek faces into the frame.
    void SetPeekFacing(CharacterPosition peek)
    {
        if (characterRenderer != null)
            characterRenderer.flipX = peek == CharacterPosition.PeekRight || peek == CharacterPosition.LeanRight;
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
        PresenterHolds.Reset();
        pendingStage = default;
        frontHeld = false;
        frontHeldUntil = float.PositiveInfinity;
        positionLedUntil = float.NaN;
        stageLedArrivals = 0;
        stageShortenedLeads = 0;
        handover = false;
        handoverTail = false;
        handoverMedia = false;
        handoverMediaFading = false;
        handoverClosing = false;
        handoverCount = 0;
        handoverSeconds = 0f;
        positionIsOver = false;
        if (handoverDim != null) handoverDim.FadeTo(false, 0f);

        // The main-menu Side move, for the whole take.
        sideMoveDash = StagePrefs.SideMoveIsDash;
        Debug.Log(sideMoveDash
            ? $"[Stage] Side move: Dash ({SideMoveSeconds:F2} s between Left, Right and Center)"
            : $"[Stage] Side move: Glide ({SideMoveSeconds:F2} s between Left, Right and Center)");

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
        // (A led arrival's own stage change, kept for its tag's time, counts too.)
        if (pendingStage.active && voiceAudio != null && voiceAudio.time >= pendingStage.at) return true;
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
        // …and while he stands in front of a card or picture ({Position:…,Over})
        // the camera would move him and not what is under him.
        if (!IsStageWide && !handover) return false;
        if (type != ZoomType.In && type != ZoomType.Pullback && type != ZoomType.ExtremeIn) return false;

        stageIgnoredZooms++;
        if (type == ZoomType.ExtremeIn) ignoredExtremeIns++;
        Debug.LogWarning($"[Stage] t={StageClock():F2}s {{Zoom:{type}}} ignored — Mugs is " +
                         (IsPresenterOff ? "off screen, so there is no face to frame."
                          : IsStageWide ? "only peeking, so there is no face to frame."
                          : $"in front of {handoverOver ?? "a card"}, and the camera would move him and not it — zoom once it has closed."));
        return true;
    }

    // -----------------------------------------------------------------------
    // A move that involves Off or a peek (MoveToPosition hands these over).
    // -----------------------------------------------------------------------
    void MoveOnStage(CharacterPosition target, bool? cutOverride, bool over = false, float ledUntil = float.NaN)
    {
        CharacterPosition from = currentPosition;
        if (target == from)
        {
            Debug.Log($"[Stage] {{Position:{target}}} — he is already there; nothing to do.");
            return;
        }

        bool cut = cutOverride ?? useHardCuts;
        float t = StageClock();

        // A led arrival that is told to leave again before its tag's time: the
        // stage never changed for it, so there is nothing to change back.
        bool stageStillWide = false;
        if (pendingStage.active)
        {
            if (StageMath.IsWide(target)) { pendingStage.active = false; stageStillWide = true; }
            else ApplyPendingStageChange();
        }
        bool wasWide = stageStillWide || StageMath.IsWide(from), nowWide = StageMath.IsWide(target);

        // "Arrive on the word": his travel starts now, ahead of the tag; what
        // the stage does for his return (the picture ending, the wide card
        // gliding to its side, Center hiding side cards) still happens on the
        // tag's own time — ApplyPendingStageChange, from the position tracker.
        bool led = !float.IsNaN(ledUntil) && !cut && from == CharacterPosition.Off && target != CharacterPosition.Off;

        // ",Over": he comes up in front of what has the frame, and it stays as
        // it is. With nothing there he moves as he would without the word.
        string under = null;
        bool handOver = false;
        if (over)
        {
            handOver = StageMath.CanHandOver(from, WhatHasTheFrame(out under), handover);
            if (!handOver)
                Debug.LogWarning($"[Stage] t={t:F2}s {{Position:{target},Over}} — nothing has the frame for him to come up in front of; " +
                                 "he moves as he would without \",Over\".");
        }
        // A move without ",Over" while he stands in front of something: to Off
        // or a peek he leaves it there (it carries on); to a full position the
        // normal rules take over.
        bool leaves = handover && !handOver;
        bool leavesItUp = leaves && nowWide;

        // The layout changing is a new beat for whatever {Image:}/{Video:} is up,
        // exactly as a change of side is. Off ↔ peek keeps the stage wide, so
        // the media stays — and so does what a handover is in front of.
        bool endsMedia = StageMath.MoveEndsMedia(stageStillWide ? CharacterPosition.Off : from, target) && !handOver && !leavesItUp;
        if (endsMedia && !led)
        {
            DismissActiveMedia();
            if (!isShowingMedia) ReleaseMediaHandover(hide: true);
        }

        // No face left to lean in on.
        if (nowWide && !wasWide)
            EndZoomRun($"{{Position:{target}}}", hiddenBehindCover: cut);

        // What the move is made of — planned from where he IS: a move that was
        // running is not finished first and not jumped out of.
        List<StageLeg> legs = null;
        CharacterPosition exitAs = from;
        StageLegRun finishFirst = null;   // he was leaving: that leg carries on, then these legs
        if (!cut && avatarParent != null) legs = PlanStageMove(from, target, out exitAs, out finishFirst);

        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        bool interrupted = presenterGliding || stageMoving;
        stageRun = null;
        presenterGliding = false;
        stageMoving = false;

        currentPosition = target;

        // Cards: Center still hides the side card; anything else lets them
        // show, in the wide slot while he is away. An open side card glides
        // to its new slot (or snaps there on a cut / under a transition cover).
        if (contentZoneController != null)
        {
            if (handOver)
                contentZoneController.BeginHandover(centred: target == CharacterPosition.Center, wide: nowWide);
            else if (leavesItUp)
                contentZoneController.LeaveHandover(wide: true);
            else if (!led)
            {
                if (leaves) contentZoneController.EndHandover();
                if (target == CharacterPosition.Center) contentZoneController.PauseTimeline();
                else contentZoneController.ResumeTimeline();
                contentZoneController.SetStageWide(nowWide, instant: cut);
            }
        }
        if (led && !handOver)
        {
            pendingStage = new PendingStageChange { active = true, at = ledUntil, endsMedia = endsMedia };
            if (StageMath.IsFull(target)) { frontHeld = true; frontHeldUntil = float.PositiveInfinity; }
        }
        else if (!led) { frontHeld = false; frontHeldUntil = float.PositiveInfinity; }

        // The handover itself, before he moves: his layer is set as his legs begin.
        if (handOver)
            BeginHandover(target, under, legs != null && legs.Count > 0 ? StageMath.LegSeconds(legs[0]) : 0f, cut);
        else if (leavesItUp)
            LeaveHandover();
        else if (leaves)
            FinishHandover("his next move follows the normal rules", atOnce: cut);

        string how;
        if (avatarParent == null)
        {
            how = "no presenter object";
        }
        else if (cut)
        {
            EndStageShape();
            PlacePresenter(target, from);
            how = "cut";
        }
        else
        {
            // Cards wait for an ARRIVAL, never for him leaving — and nothing waits
            // for a handover, or for an arrival that started early (he has landed
            // by the tag's time; a shortened lead holds only for what is left).
            presenterGliding = !handOver && !led && StageMath.CardsWaitFor(from, target);
            stageMoving = true;
            movementCoroutine = StartCoroutine(StageTravel(legs, target, exitAs, finishFirst));
            SoundLayer.Fire(target == CharacterPosition.Off ? SfxKind.StageOut : SfxKind.StageIn);
            StageLeg first = legs.Count > 0 ? legs[0] : StageLeg.Park;
            how = first == StageLeg.ToFull ? "comes back from where he was"
                : first == StageLeg.ToPeek ? "comes back up from where he was"
                : first == StageLeg.ToLean ? "comes back in from where he was"
                : StageMath.IsLean(target) ? "leans in"
                : target == CharacterPosition.Off ? (finishFirst != null ? "he was leaving already" : interrupted ? "leaves from where he was" : "slides out")
                : StageMath.IsPeek(target) ? "head comes up"
                : "pops up";
            if (finishFirst != null && target != CharacterPosition.Off) how += " once he has finished leaving";
        }
        if (handOver) how += " over " + under;
        if (led)
        {
            float early = ledUntil - t, full = legs != null && legs.Count > 0 ? StageMath.LegSeconds(legs[0]) : 0f;
            stageLedArrivals++;
            // (The voice clock moves in ~21 ms steps against 33 ms frames, so a
            // start within a frame or so of the planned one is a full lead.)
            bool shortened = early < full - LeadSlackSeconds;
            if (shortened) stageShortenedLeads++;
            how += $", started {early:F2} s early" + (shortened ? $" — {full - early:F2} s short of his {full:F2} s move, so he lands that late" : "");
            t = ledUntil;   // the log gives the tag's time
        }

        Debug.Log(target == CharacterPosition.Off
            ? $"[Stage] t={t:F2}s Mugs off ({how}) — the stage is wide"
            : StageMath.IsLean(target)
                ? $"[Stage] t={t:F2}s Mugs leans in from the {(target == CharacterPosition.LeanRight ? "right" : "left")} ({how}) — the stage is wide"
            : StageMath.IsPeek(target)
                ? $"[Stage] t={t:F2}s Mugs peeks {(target == CharacterPosition.PeekRight ? "right" : "left")} ({how}) — the stage is wide"
                : $"[Stage] t={t:F2}s Mugs back at {target} ({how})");
    }

    // The legs of a stage move from where he is now, and the edge he leaves
    // through (exitAs: Left / Right = that side, anything else = down).
    // At rest that is StageMath.PlanTravel and his position's own edge. With a
    // move still running:
    //   - he is on his way OUT (StageMath.PlanWhileLeaving): back to the place
    //     he is leaving, still in view → he comes back from where he is;
    //     anywhere else → that leg carries on untouched (finishFirst) and the
    //     arrival follows it;
    //   - he is popping up → he goes back down the way he came;
    //   - he is between his marks → he leaves through the nearest edge.
    List<StageLeg> PlanStageMove(CharacterPosition from, CharacterPosition target,
                                 out CharacterPosition exitAs, out StageLegRun finishFirst)
    {
        StageMath.Frame frame = StageFrame();
        StageMath.Body body = StageBody();
        Vector2 here = avatarParent.position;
        bool inView = StageMath.InView(here, body, frame, presenterUniform);

        if (PlanWhileLeaving(target, inView, here, frame, out List<StageLeg> legs, out exitAs, out finishFirst))
            return legs;

        exitAs = from;
        if (StageMath.IsFull(from) && (presenterGliding || stageMoving))
            exitAs = stageRun != null && stageRun.Leg == StageLeg.PopUp
                ? CharacterPosition.Center
                : StageMath.ExitFrom(here.x, frame);

        return StageMath.PlanTravel(from, target, inView);
    }

    // True when a leg that takes him out is running: the plan for a move that
    // arrives now (see StageMath.PlanWhileLeaving), the edge that leg takes him
    // through, and the leg itself when it has to be finished first.
    bool PlanWhileLeaving(CharacterPosition target, bool inView, Vector2 here, StageMath.Frame frame,
                          out List<StageLeg> legs, out CharacterPosition exitAs, out StageLegRun finishFirst)
    {
        legs = null;
        exitAs = target;
        finishFirst = null;
        StageLegRun run = stageMoving ? stageRun : null;
        if (run == null || !StageMath.IsLeaving(run.Leg)) return false;

        // What he is leaving: the peek or the lean; for a slide-out, the mark he is nearest to.
        CharacterPosition leaving = run.Leg == StageLeg.SlideOut ? StageMath.ExitFrom(here.x, frame) : run.ExitAs;
        legs = StageMath.PlanWhileLeaving(leaving, target, inView, out bool finish);
        exitAs = run.ExitAs;
        if (finish) finishFirst = run;
        return true;
    }

    // A Left / Right / Center move that arrives while he is still on his way
    // in from Off, a peek or a lean (MoveToPosition asks; never true for an
    // old script). While he is still dropping out of the peek (or pulling out
    // of the lean) that leg is finished and he pops up at the new mark. Once
    // his pop-up is running it is turned towards the new mark from where he
    // is — or, if he hasn't come into view yet, he simply pops up there.
    bool RedirectStageArrival(CharacterPosition target)
    {
        if (!stageMoving || avatarParent == null || GetTransformForPosition(target) == null) return false;

        List<StageLeg> legs = PlanStageRedirect(target, out CharacterPosition exitAs, out StageLegRun finishFirst);

        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        stageRun = null;
        presenterGliding = true;
        stageMoving = true;
        movementCoroutine = StartCoroutine(StageTravel(legs, target, exitAs, finishFirst));
        Debug.Log($"[Stage] t={StageClock():F2}s Mugs turns to {target} " +
                  (finishFirst != null ? "(he finishes leaving, then pops up there)"
                   : legs.Count > 0 && legs[0] == StageLeg.ToFull ? "from where he was (his arrival was still running)"
                   : "(pops up there instead)"));
        return true;
    }

    // What that redirect is made of, from where he is now.
    List<StageLeg> PlanStageRedirect(CharacterPosition target, out CharacterPosition exitAs, out StageLegRun finishFirst)
    {
        StageMath.Frame frame = StageFrame();
        Vector2 here = avatarParent.position;
        bool inView = StageMath.InView(here, StageBody(), frame, presenterUniform);
        if (PlanWhileLeaving(target, inView, here, frame, out List<StageLeg> legs, out exitAs, out finishFirst))
            return legs;
        exitAs = target;
        return StageMath.PlanTravel(CharacterPosition.Off, target, inView);
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
            avatarParent.rotation = UprightRotation();   // (he may have been leaning)
        }
        else if (StageMath.IsPeek(target))
        {
            bool right = target == CharacterPosition.PeekRight;
            StageMath.PeekPose pose = StageMath.Peek(right, body, frame);
            SetPresenter(pose.Rest, pose.Scale, peekSorting: true);
            avatarParent.rotation = UprightRotation();
            SetPeekFacing(target);
        }
        else if (StageMath.IsLean(target))
        {
            // His head and one shoulder over the side edge, tilted into the
            // frame, facing its centre — so his back (the tail) is outside.
            bool right = target == CharacterPosition.LeanRight;
            StageMath.LeanPose pose = StageMath.Lean(right, FacingBody(body, right), frame);
            SetPresenter(pose.Rest, pose.Scale, peekSorting: true);
            avatarParent.rotation = UprightRotation() * Quaternion.Euler(0f, 0f, pose.Tilt);
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
        stageRun = null;
        EndStageShape();
        stageMoving = false;
        presenterGliding = false;
        if (avatarParent != null && StageMath.IsFull(currentPosition))
            PlacePresenter(currentPosition, currentPosition);
    }

    // His outline at scale 1, measured while he has his own shape (a leg's
    // squash or lean would distort it — then the last good one is used).
    StageMath.Body StageBody()
    {
        if (!stageShaped || !stageBodyKnown)
        {
            stageBody = PresenterBody();
            stageBodyKnown = true;
        }
        return stageBody;
    }

    // One leg of a stage move, set up from where he is.
    sealed class StageLegRun
    {
        public StageLeg Leg;
        public Vector2 A, B;                    // the moved transform: where the leg starts and ends
        public float SizeA = 1f, SizeB = 1f;    // his size on the stage at each end (1, or a peek's scale)
        public bool EdgeIsFrame;                // the line that holds still: the frame's bottom edge (a peek) or his feet
        public float Edge;                      // the frame's bottom, or his feet below the transform at size 1
        public bool Sideways;                   // a slide-out through a side edge (he leans)
        public float Lean;                      // +1: leaning into the travel turns him counter-clockwise (to the left)
        public Quaternion Upright = Quaternion.identity;   // his rotation without any lean
        public bool EdgeIsSide;                 // a lean: the line that holds still is the frame's SIDE edge (Edge = its x)
        public float TiltA, TiltB;              // the tilt he RESTS at, at each end (a lean's 35°; 0 anywhere else)
        public Vector2 Carried = Vector2.one;   // the squash / stretch he had when the leg began…
        public float CarriedTilt;               // …and the lean: both eased away (StageMoves.CarryLeft)
        public System.Action AtHalf;            // a change of facing, made halfway
        public bool HalfDone;
        public bool EndsAtFull;                 // lands at Left / Right / Center: rest sorting, sway base
        public Transform Rest;
        public CharacterPosition ExitAs;        // the edge a leaving leg takes him through (Park needs it)
        public float T;                         // how far into the leg its NEXT frame is
        public int PosedFrame = -1;             // the frame it was last posed on
    }

    // Leaves where he is, then arrives. Every leg starts from the transform
    // as it is: his place, his size, and whatever squash and lean a cut-short
    // move left on him. finishFirst: the leg that was taking him out when this
    // move arrived — it carries on from the frame it had reached, as if nothing
    // had happened, and the legs follow it. Time.deltaTime, like MoveAvatar.
    IEnumerator StageTravel(List<StageLeg> legs, CharacterPosition target, CharacterPosition exitAs,
                            StageLegRun finishFirst = null)
    {
        StageMath.Frame frame = StageFrame();
        StageMath.Body body = StageBody();

        for (int i = finishFirst != null ? -1 : 0; i < legs.Count; i++)
        {
            StageLegRun run;
            float t = 0f;
            if (i < 0)
            {
                run = finishFirst;
                t = run.T;
                // (Already posed on this frame by the move it belonged to: its next frame is the next one.)
                if (run.PosedFrame == Time.frameCount) { stageRun = run; yield return null; }
            }
            else
            {
                StageLeg leg = legs[i];
                if (leg == StageLeg.Park)
                {
                    EndStageShape();
                    SetPresenter(StageMath.Park(exitAs, avatarParent.position), 1f, peekSorting: false);
                    avatarParent.rotation = UprightRotation();   // (a lean leaves tilted)
                    continue;
                }

                run = BeginStageLeg(leg, target, exitAs, frame, body);
                if (run == null) continue;
            }
            stageRun = run;

            float seconds = StageMath.LegSeconds(run.Leg);
            while (t < seconds)
            {
                PoseStageLeg(run, t);
                run.PosedFrame = Time.frameCount;
                t += Time.deltaTime;
                run.T = t;
                yield return null;
            }
            PoseStageLeg(run, seconds);
            stageShaped = false;
            stageRun = null;

            if (run.EndsAtFull)
            {
                SetPresenter(run.B, 1f, peekSorting: frontHeld);   // (a led arrival stays in front of the card that is still up)
                if (avatarSystem != null) avatarSystem.SetSwayBase(run.Rest.position, run.Rest.rotation);
            }
        }

        stageMoving = false;
        presenterGliding = false;
    }

    // Sets a leg up from the transform as it is now. Legs that start out of
    // sight (a pop-up, a peek's rise) place him at their hidden start first;
    // every other leg starts exactly where and how he is.
    StageLegRun BeginStageLeg(StageLeg leg, CharacterPosition target, CharacterPosition exitAs,
                              StageMath.Frame frame, StageMath.Body body)
    {
        Vector2 here = avatarParent.position;
        Quaternion upright = stageShaped ? stageShapeRotation : avatarParent.rotation;
        var run = new StageLegRun
        {
            Leg = leg, A = here, B = here, SizeA = presenterUniform, SizeB = presenterUniform,
            Edge = body.CentreOffset.y - body.Size.y * 0.5f,   // his feet: the line a squash holds still
            Upright = upright,
            ExitAs = exitAs,
        };

        switch (leg)
        {
            case StageLeg.SlideOut:
                // Through a side edge (he leans into it), or down.
                run.Sideways = exitAs == CharacterPosition.Left || exitAs == CharacterPosition.Right;
                run.Lean = exitAs == CharacterPosition.Left ? 1f : -1f;
                run.B = StageMath.SlideOutTarget(exitAs, here, body, frame, presenterUniform);
                CarryShape(run);
                break;

            case StageLeg.DropFromPeek:
                run.EdgeIsFrame = true;
                run.Edge = frame.Bottom;
                run.B = StageMath.BelowFrame(here, body, frame, presenterUniform);
                CarryShape(run);
                break;

            case StageLeg.RiseToPeek:
            {
                StageMath.PeekPose pose = StageMath.Peek(target == CharacterPosition.PeekRight, body, frame);
                run.Upright = UprightRotation();
                SetPresenter(pose.Hidden, pose.Scale, peekSorting: true);
                avatarParent.rotation = run.Upright;
                SetPeekFacing(target);
                run.A = pose.Hidden;
                run.B = pose.Rest;
                run.SizeA = run.SizeB = pose.Scale;
                run.EdgeIsFrame = true;
                run.Edge = frame.Bottom;
                break;
            }

            case StageLeg.PopUp:
            {
                Transform rest = GetTransformForPosition(target);
                if (rest == null) return null;
                Vector2 r = rest.position;
                Vector2 below = StageMath.BelowFrame(r, body, frame);
                // In front of the card zones while he rises: a wide card that is
                // closing (or gliding to its side) on this beat fades BEHIND him —
                // on the first real take he came up behind a translucent Stat.
                SetPresenter(below, 1f, peekSorting: true);
                avatarParent.rotation = rest.rotation;
                UpdateFacing(target);
                run.A = below;
                run.B = r;
                run.SizeA = run.SizeB = 1f;
                run.Upright = rest.rotation;
                run.EndsAtFull = true;
                run.Rest = rest;
                break;
            }

            case StageLeg.ToFull:
            {
                // From where he is — sliding out of this mark, or popping up
                // at another — to his mark (never out of a peek or a lean: he
                // leaves those first). He keeps the peek's sorting order until
                // he lands.
                Transform rest = GetTransformForPosition(target);
                if (rest == null) return null;
                SetPresenterLayer(true);
                run.B = rest.position;
                run.SizeB = 1f;
                run.Upright = rest.rotation;
                run.AtHalf = () => UpdateFacing(target);
                run.EndsAtFull = true;
                run.Rest = rest;
                CarryShape(run);
                TiltFromHereTo(run, 0f);
                break;
            }

            case StageLeg.ToPeek:
            {
                // Back up into the peek he was dropping out of, from where he is.
                StageMath.PeekPose pose = StageMath.Peek(target == CharacterPosition.PeekRight, body, frame);
                SetPresenterLayer(true);
                run.B = pose.Rest;
                run.SizeB = pose.Scale;
                run.EdgeIsFrame = true;
                run.Edge = frame.Bottom;
                run.AtHalf = () => SetPeekFacing(target);
                CarryShape(run);
                TiltFromHereTo(run, 0f);
                break;
            }

            case StageLeg.LeanIn:
            {
                // His head and one shoulder come in over the side edge, already tilted.
                bool right = target == CharacterPosition.LeanRight;
                StageMath.LeanPose pose = StageMath.Lean(right, FacingBody(body, right), frame);
                run.Upright = UprightRotation();
                SetPresenter(pose.Hidden, pose.Scale, peekSorting: true);
                avatarParent.rotation = run.Upright * Quaternion.Euler(0f, 0f, pose.Tilt);
                SetPeekFacing(target);
                run.A = pose.Hidden;
                run.B = pose.Rest;
                run.SizeA = run.SizeB = pose.Scale;
                run.TiltA = run.TiltB = pose.Tilt;
                run.EdgeIsSide = true;
                run.Edge = right ? frame.Right : frame.Left;
                break;
            }

            case StageLeg.LeanOut:
            {
                // Back out through the edge he leans over, as he is.
                bool right = exitAs == CharacterPosition.LeanRight || exitAs == CharacterPosition.Right;
                run.Upright = UprightRotation();
                run.B = StageMath.SlideOutTarget(right ? CharacterPosition.Right : CharacterPosition.Left,
                                                 here, body, frame, presenterUniform);
                run.EdgeIsSide = true;
                run.Edge = right ? frame.Right : frame.Left;
                CarryShape(run);
                TiltFromHereTo(run, run.CarriedTilt);   // he keeps the lean's tilt all the way out
                break;
            }

            case StageLeg.ToLean:
            {
                // Back into the lean he was pulling out of, from where he is.
                bool right = target == CharacterPosition.LeanRight;
                StageMath.LeanPose pose = StageMath.Lean(right, FacingBody(body, right), frame);
                SetPresenterLayer(true);
                run.Upright = UprightRotation();
                run.B = pose.Rest;
                run.SizeB = pose.Scale;
                run.EdgeIsSide = true;
                run.Edge = right ? frame.Right : frame.Left;
                run.AtHalf = () => SetPeekFacing(target);
                CarryShape(run);
                TiltFromHereTo(run, pose.Tilt);
                break;
            }

            default:
                return null;
        }

        stageShapeRotation = run.Upright;
        stageShaped = true;
        return run;
    }

    // What is on him as the leg begins — the squash, stretch and lean of a
    // move that was cut short — so the leg's first frame is the frame he is on.
    void CarryShape(StageLegRun run)
    {
        Vector3 own = presenterRestScale * presenterUniform, now = avatarParent.localScale;
        run.Carried = new Vector2(Mathf.Abs(own.x) > 1e-5f ? now.x / own.x : 1f,
                                  Mathf.Abs(own.y) > 1e-5f ? now.y / own.y : 1f);
        run.CarriedTilt = Mathf.DeltaAngle(run.Upright.eulerAngles.z, avatarParent.eulerAngles.z);
    }

    // The tilt he has now becomes the leg's own, turning into the tilt he
    // rests at where the leg ends (instead of being eased away as a leftover).
    static void TiltFromHereTo(StageLegRun run, float restTilt)
    {
        run.TiltA = run.CarriedTilt;
        run.TiltB = restTilt;
        run.CarriedTilt = 0f;
    }

    // One frame of a leg: StageMoves' pose for that time (how far along, how
    // stretched, how far leaning) — and, at the leg's end, exactly its end
    // with his own shape.
    void PoseStageLeg(StageLegRun run, float t)
    {
        float seconds = StageMath.LegSeconds(run.Leg);
        ApplyStagePose(run, t >= seconds ? StageMoves.Rest : StageMoves.For(run.Leg, t, run.Sideways),
                       t >= seconds ? 0f : StageMoves.CarryLeft(t));
    }

    // Puts one pose of a leg on the moved transform. His shape and lean ride
    // on the same transform as his place, so the Hop / Grow / Shrink and the
    // idle sway on the rig below it carry on underneath; the drop shadow is a
    // child of the sprite and follows. carry: how much of the shape he started
    // the leg with is still on him (1 → 0).
    void ApplyStagePose(StageLegRun run, StageMoves.Pose pose, float carry)
    {
        Vector2 p = Vector2.LerpUnclamped(run.A, run.B, pose.Travel);
        float size = Mathf.Lerp(run.SizeA, run.SizeB, pose.Travel);
        float tilt = pose.Tilt * run.Lean;
        float restTilt = Mathf.Lerp(run.TiltA, run.TiltB, pose.Travel);
        // The line that holds still while his shape changes: the frame's side
        // edge where a lean crosses it, its bottom edge under a peek, else his feet.
        p = run.EdgeIsSide ? StageMoves.HoldSideEdge(p, run.Edge, restTilt, pose.ScaleY)
          : StageMoves.HoldEdge(p, run.EdgeIsFrame ? run.Edge : p.y + run.Edge * size, pose.ScaleY, tilt);

        presenterUniform = size;
        avatarParent.position = new Vector3(p.x, p.y, avatarParent.position.z);
        Vector3 own = presenterRestScale * size;
        avatarParent.localScale = new Vector3(own.x * pose.ScaleX * Mathf.Lerp(1f, run.Carried.x, carry),
                                              own.y * pose.ScaleY * Mathf.Lerp(1f, run.Carried.y, carry), own.z);
        avatarParent.rotation = run.Upright * Quaternion.Euler(0f, 0f, restTilt + tilt + run.CarriedTilt * carry);

        if (!run.HalfDone && pose.Travel >= 0.5f)
        {
            run.HalfDone = true;
            run.AtHalf?.Invoke();
        }
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
        StepHandover();
        StepFrontHold();
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
        if (handover) handoverSeconds += dt;
        string warning = emptyStage.Step(t, dt, StageIsEmpty());
        if (warning != null) Debug.LogWarning(warning);
    }

    MugsOnScreen StageState()
    {
        if (currentPosition == CharacterPosition.Off) return MugsOnScreen.Off;
        if (StageMath.IsLean(currentPosition)) return MugsOnScreen.Lean;
        if (StageMath.IsPeek(currentPosition)) return MugsOnScreen.Peek;
        // In front of a card he is on screen even where that card would cover him. Black still does.
        if (handover) return IsBlackShowing ? MugsOnScreen.Covered : MugsOnScreen.Full;
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
            LeanCount = stageMeter.LeanCount,
            EmptyStageWarnings = emptyStage != null ? emptyStage.Warnings : 0,
            LongestEmptySeconds = emptyStage != null ? emptyStage.LongestSeconds : 0f,
            IgnoredZooms = stageIgnoredZooms,
            OpenedWithoutMugs = openedWithoutPresenter,
            SideMove = sideMoveDash ? "dash" : "glide",
            HoldCount = PresenterHolds.Count,
            HoldSeconds = PresenterHolds.TotalSeconds,
            LongestHoldSeconds = PresenterHolds.LongestSeconds,
            HandoverCount = handoverCount,
            HandoverSeconds = handoverSeconds,
            LedArrivals = stageLedArrivals,
            ShortenedLeads = stageShortenedLeads,
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
        mediaShownIsImage = isImage;
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

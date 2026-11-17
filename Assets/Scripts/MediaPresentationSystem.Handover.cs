using System.Collections;
using MugsTech.Stage;
using UnityEngine;

// ============================================================================
// MediaPresentationSystem — the handover: {Position:Left|Right|Center,Over}
// (and {Position:PeekLeft|PeekRight,Over}).
//
// Mugs comes up IN FRONT of whatever has the frame — a full-screen card, a
// wide card, a full-frame image or clip — and talks while it is still there.
// It ends by its own End or duration and fades behind him; then the stage is
// his and his position follows its normal rules.
//
//   - He is drawn above the feature zone and the {Name:} label, below the
//     {Pop:} phrase, the captions, the Echo and black (StageMath.OverSortingOrder).
//   - What is under him dims to 60 % as he pops up (HandoverDim). A ,Over peek
//     dims nothing.
//   - The stage does not rearrange for him: the wide card stays wide, the
//     full-frame picture stays, Center hides nothing. Nothing waits for him.
//   - A NEW card or picture that comes due first closes what is under him.
//   - {Position:Off} (or a peek without ,Over): he leaves, it brightens and
//     carries on.
//   - Zooms are ignored meanwhile: the camera would move him and not the card.
//
// Reached only through the word "Over". With nothing under him, or when he is
// already on stage, he moves as he would without it (one warning).
// ============================================================================
public partial class MediaPresentationSystem
{
    // True while he stands in front of what has the frame.
    private bool handover;
    // A full-frame image or clip was under him when he came up (and still is).
    private bool handoverMedia;
    // What he came up in front of, for the log.
    private string handoverOver;
    // The new-beat close has been asked for (said once).
    private bool handoverClosing;
    // The image under him is fading out behind him (FadeMediaBehindPresenter).
    private bool handoverMediaFading;
    // The handover is over but its dim is still fading: he keeps its layer
    // until the sheet has gone, or he would be under it for those frames.
    private bool handoverTail;
    private int handoverCount;
    private float handoverSeconds;
    private HandoverDim handoverDim;

    // The layer last asked for by the stage's moves (a peek's order, or rest):
    // what he goes back to when a handover ends.
    private bool presenterLayerPeek;

    // Set by the position tracker for the one {Position:…,Over} it is firing.
    private bool positionIsOver;

    // What the picture on the media slot is, for the log.
    private bool mediaShownIsImage = true;

    /// <summary>True while Mugs stands in front of a card or picture that still has the frame ({Position:…,Over}).</summary>
    public bool IsHandover => handover;

    /// <summary>The sheet that dims what is under him (null when no media canvas, or never needed).</summary>
    public HandoverDim HandoverDimSheet => handoverDim;

    // A {Position:…,Over} from the script (TrackPositionsByTime).
    void MoveToPositionOver(CharacterPosition target, bool? cutOverride)
    {
        positionIsOver = true;
        try { MoveToPosition(target, cutOverride); }
        finally { positionIsOver = false; }
    }

    // His sprites' layer: in front of everything a card can be while a
    // handover lasts, else the peek's order or the scene's own.
    void SetPresenterLayer(bool peekLayer)
    {
        presenterLayerPeek = peekLayer;
        if (avatarSystem != null) avatarSystem.SetStageSorting(StageMath.PresenterOrder(handover || handoverTail, peekLayer));
    }

    /// <summary>The sorting order the stage wants for him now (null = the scene's own) — for the self-tests.</summary>
    public int? PresenterOrderNow => StageMath.PresenterOrder(handover || handoverTail, presenterLayerPeek);

    // Whether something has the frame, and what to call it in the log.
    bool WhatHasTheFrame(out string what)
    {
        what = null;
        if (contentZoneController != null && contentZoneController.ActiveCard != null)
        {
            what = contentZoneController.ActiveCardName;
            return true;
        }
        if (isShowingMedia && mediaIsFullFrame)
        {
            what = mediaShownIsImage ? "the image" : "the clip";
            return true;
        }
        return false;
    }

    // He comes up in front of it (or, already there, moves on in front of it).
    // dims: a full position dims what is under him over his arrival; a peek doesn't.
    void BeginHandover(CharacterPosition target, string over, float arrivalSeconds, bool cut)
    {
        if (!handover) handoverCount++;
        handover = true;
        handoverTail = false;
        handoverClosing = false;
        handoverOver = over;
        handoverMedia = isShowingMedia && mediaIsFullFrame;

        bool dims = StageMath.IsFull(target);
        if (dims && handoverDim == null && mediaCanvas != null)
        {
            handoverDim = HandoverDim.CreateOn(mediaCanvas.transform);
            handoverDim.transform.SetParent(transform, false);
        }
        if (handoverDim != null) handoverDim.FadeTo(dims, cut ? 0f : arrivalSeconds);
        SetPresenterLayer(presenterLayerPeek);
    }

    // He leaves ({Position:Off}, or a peek without ,Over): what was under him
    // comes back to full brightness and carries on. He stays in front of it
    // until he is out (parking him, or the peek's own leg, sets his layer).
    void LeaveHandover()
    {
        if (!handover) return;
        handover = false;
        handoverTail = false;
        handoverMedia = false;
        if (handoverDim != null) handoverDim.FadeTo(false, StageMath.HandoverDimOutSeconds);
        Debug.Log($"[Stage] t={StageClock():F2}s Mugs leaves {handoverOver ?? "it"} — it comes back to full brightness and carries on");
    }

    // What was under him has gone (or a transition took it): the stage is his.
    void FinishHandover(string why, bool atOnce = false)
    {
        if (!handover) return;
        handover = false;
        handoverMedia = false;
        handoverClosing = false;
        if (contentZoneController != null) contentZoneController.EndHandover();
        if (handoverDim != null) handoverDim.FadeTo(false, atOnce ? 0f : StageMath.HandoverDimOutSeconds);
        // Back to his own layer — the peek's while he peeks or is still rising —
        // once the dim has gone (it normally has: it fades with what was under him).
        handoverTail = handoverDim != null && handoverDim.IsShowing;
        SetPresenterLayer(stageRun != null || StageMath.IsSmall(currentPosition));
        Debug.Log($"[Stage] t={StageClock():F2}s {why} — " +
                  (StageMath.IsFull(currentPosition) ? "the stage is his" : "he is back to a plain peek"));
    }

    // Each frame: the dim follows what is under him, and the handover ends on
    // the frame the last of it has finished fading.
    void StepHandover()
    {
        if (!handover)
        {
            if (handoverTail && (handoverDim == null || !handoverDim.IsShowing))
            {
                handoverTail = false;
                SetPresenterLayer(presenterLayerPeek);
            }
            return;
        }

        bool card = contentZoneController != null && contentZoneController.HasHandoverCard;
        bool cardLeaving = card && contentZoneController.HandoverCardIsHiding;
        if (handoverMedia && !(isShowingMedia && mediaIsFullFrame)) handoverMedia = false;

        if (!card && !handoverMedia)
        {
            FinishHandover($"{Capitalised(handoverOver)} has closed behind him");
            return;
        }
        if (handoverDim != null && StageMath.IsFull(currentPosition))
            handoverDim.FadeTo((card && !cardLeaving) || (handoverMedia && !handoverMediaFading), StageMath.HandoverDimOutSeconds);
    }

    // An image that ends while he stands in front of it fades away behind
    // him (media otherwise ends on a cut), and the dim goes with it.
    bool MediaFadesBehindPresenter => handover && handoverMedia;

    IEnumerator FadeMediaBehindPresenter()
    {
        handoverMediaFading = true;
        float from = mediaDisplayGroup != null ? mediaDisplayGroup.alpha : 1f, t = 0f;
        while (t < StageMath.HandoverDimOutSeconds && mediaDisplayGroup != null)
        {
            t += Time.deltaTime;
            mediaDisplayGroup.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / StageMath.HandoverDimOutSeconds));
            StepMediaMotion(Time.deltaTime);
            yield return null;
        }
        handoverMediaFading = false;
    }

    static string Capitalised(string s) =>
        string.IsNullOrEmpty(s) ? "What was under him" : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>
    /// A new card or picture comes due while he stands in front of something:
    /// that closes first, as it does for any new beat. The newcomer then opens
    /// by the normal rules for his position.
    /// </summary>
    public void HandoverNewBeat(string by)
    {
        if (!handover) return;
        if (!handoverClosing)
            Debug.Log($"[Stage] t={StageClock():F2}s {by} comes due while Mugs is in front of {handoverOver ?? "it"} — that closes first");
        handoverClosing = true;
        if (contentZoneController != null) contentZoneController.CloseHandoverCard();
        if (handoverMedia) DismissActiveMedia();
    }

    // True when the next {Image:}/{Video:} that would SHOW (not an End) is due.
    bool NextMediaWouldShow()
    {
        int next = lastTriggeredMediaMarker + 1;
        return mediaMarkers != null && next < mediaMarkers.Count && voiceAudio != null &&
               !mediaMarkers[next].endsMedia && voiceAudio.time >= mediaMarkers[next].triggerTime;
    }
}

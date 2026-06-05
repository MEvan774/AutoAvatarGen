using UnityEngine;
using DG.Tweening;
using System;
using MugsTech.Style;

/// <summary>
/// Abstract base class for all content zone cards.
/// Self-building: each card constructs its own UI hierarchy in Awake.
/// Handles fade in/out animations via DOTween, duration timing, and self-cleanup.
///
/// Animation timing & overshoot curve come from <see cref="CardEntryAnimator"/>.
/// To tweak the curve, slide duration, fade duration, or pick an entry direction
/// per card type, edit the CardEntryAnimator component (lives on the
/// ContentZoneController GameObject by default).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public abstract class ContentCard : MonoBehaviour
{
    protected CanvasGroup canvasGroup;
    protected RectTransform rectTransform;

    /// <summary>Called by ContentZoneController after hide animation finishes.</summary>
    public Action OnHideComplete;

    protected Sequence currentSequence;

    // Direction set by ContentZoneController before Show() is called. Used as
    // a fallback when the animator's per-card row has Override Direction off.
    private EntryDirection runtimeDirection = EntryDirection.FromBottom;

    // +1 normally; -1 when the parent zone is horizontally mirrored (a 180°
    // Y-rotation or negative X-scale somewhere above it — the scene renders
    // mirrored for recording). ContentZoneController sets this from the SAME
    // reflection test it uses to flip the card's own X scale so text reads
    // correctly. That counter-scale fixes the card's graphics but NOT its slide:
    // the slide animates anchoredPosition in the still-mirrored zone space, so a
    // "FromLeft" entry would visually come in from the right. We negate the
    // horizontal start offset to bring the on-screen motion back in line.
    private float parentMirrorSign = 1f;

    /// <summary>
    /// Concrete card type — used by <see cref="CardEntryAnimator"/> to look up
    /// per-card direction & timing.
    /// </summary>
    protected abstract ContentCardType CardType { get; }

    // ---- Convenience accessors that route through the central animator ----
    protected AnimationCurve OvershootCurve => CardEntryAnimator.Instance.Curve;
    protected float FadeInDuration  => CardEntryAnimator.Instance.GetFadeInDuration(CardType);
    protected float SlideDuration   => CardEntryAnimator.Instance.GetSlideDuration(CardType);
    protected float FadeOutDuration => CardEntryAnimator.Instance.fadeOutDuration;
    protected float FastFadeDuration => CardEntryAnimator.Instance.fastFadeOutDuration;
    protected float SlideDistanceFactor => CardEntryAnimator.Instance.GetSlideDistanceFactor(CardType);
    protected EntryDirection ResolvedEntryDirection
        => CardEntryAnimator.Instance.ResolveDirection(CardType, runtimeDirection);

    protected virtual void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();

        // Stretch to fill parent
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        // Start invisible
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;

        BuildUI();
    }

    /// <summary>
    /// Construct the card's UI hierarchy programmatically.
    /// Called from Awake — implementations should create all child GameObjects
    /// (background, text, images) and store references for Initialize().
    /// </summary>
    protected abstract void BuildUI();

    /// <summary>
    /// Populate the card's UI elements from event data.
    /// Called once after instantiation, before Show().
    /// </summary>
    public abstract void Initialize(ContentCardEvent data, ContentCardAssets assets);

    /// <summary>
    /// Set the entry direction for the next Show() call. Called by
    /// ContentZoneController based on the active preset and character position.
    /// Used only when the animator's per-card row has Override Direction off.
    /// </summary>
    public void SetEntryDirection(EntryDirection direction)
    {
        runtimeDirection = direction;
    }

    /// <summary>
    /// Tells the card whether its parent zone is horizontally mirrored so the
    /// entry slide can be flipped to enter from the visually-correct side. Pass
    /// the parent's X-scale sign (+1 or -1). Set by ContentZoneController from the
    /// same reflection test that counters the card's text mirroring, so the slide
    /// and the text always agree.
    /// </summary>
    public void SetParentMirrorSign(float sign)
    {
        parentMirrorSign = sign < 0f ? -1f : 1f;
    }

    /// <summary>
    /// Fade in + slide from the resolved direction with the central overshoot
    /// curve. With an active style preset, also applies a small random Z
    /// rotation and an optional elastic wobble.
    /// </summary>
    public virtual void Show()
    {
        KillCurrentSequence();

        ChannelStylePreset preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;

        EntryDirection dir = ResolvedEntryDirection;
        Vector2 endPos = rectTransform.anchoredPosition;
        Vector2 startOffset = ComputeStartOffset(dir, rectTransform, SlideDistanceFactor);

        // Counter a horizontally-mirrored parent zone. anchoredPosition's +X then
        // points to screen-LEFT, so a horizontal slide enters from the wrong side
        // (the bug: cards easing in right-to-left during recording). Negating X
        // makes the on-screen motion match the intended direction. Vertical
        // entries have x == 0, so they're untouched. (This lives only here, on the
        // card-root slide — the Big* cards slide child containers that sit under
        // the card's own counter-scale, which already cancels the mirror.)
        startOffset.x *= parentMirrorSign;

        rectTransform.anchoredPosition = endPos + startOffset;

        if (preset != null)
        {
            float rotationZ = preset.GetRandomEntryRotation();
            rectTransform.localEulerAngles = new Vector3(0f, 0f, rotationZ);
        }

        Sequence seq = DOTween.Sequence()
            .Join(canvasGroup.DOFade(1f, FadeInDuration).SetEase(Ease.OutQuad))
            .Join(rectTransform.DOAnchorPos(endPos, SlideDuration).SetEase(OvershootCurve));

        if (preset != null && preset.wobbleIntensity > 0.001f)
        {
            float w = preset.wobbleIntensity * 0.15f;
            seq.Join(rectTransform
                .DOPunchScale(new Vector3(w, w, 0f), FadeInDuration + 0.1f, vibrato: 4, elasticity: 0.7f));
        }

        currentSequence = seq;
    }

    /// <summary>
    /// Fade out. Normal vs fast duration come from the animator.
    /// Invokes OnHideComplete when done.
    /// </summary>
    public virtual void Hide(bool fast = false)
    {
        KillCurrentSequence();

        float duration = fast ? FastFadeDuration : FadeOutDuration;

        currentSequence = DOTween.Sequence()
            .Append(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Translate an EntryDirection into an off-screen offset. Horizontal
    /// directions use the rect's width; vertical directions use its height.
    /// The factor scales the off-screen distance (1 = exactly off the edge).
    /// </summary>
    protected static Vector2 ComputeStartOffset(EntryDirection dir, RectTransform rt, float factor)
    {
        float w = rt.rect.width  > 1f ? rt.rect.width  : 400f;
        float h = rt.rect.height > 1f ? rt.rect.height : 400f;

        switch (dir)
        {
            case EntryDirection.FromLeft:   return new Vector2(-w * factor, 0f);
            case EntryDirection.FromRight:  return new Vector2( w * factor, 0f);
            case EntryDirection.FromTop:    return new Vector2(0f,  h * factor);
            case EntryDirection.FromBottom: return new Vector2(0f, -h * factor);
            default:                        return new Vector2(0f, -h * factor);
        }
    }

    protected void KillCurrentSequence()
    {
        if (currentSequence != null && currentSequence.IsActive())
        {
            currentSequence.Kill();
            currentSequence = null;
        }
    }

    protected virtual void OnDestroy()
    {
        KillCurrentSequence();
    }
}

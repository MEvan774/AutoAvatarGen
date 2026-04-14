using UnityEngine;
using DG.Tweening;
using System;

/// <summary>
/// Abstract base class for all content zone cards.
/// Self-building: each card constructs its own UI hierarchy in Awake.
/// Handles fade in/out animations via DOTween, duration timing, and self-cleanup.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public abstract class ContentCard : MonoBehaviour
{
    protected const float FADE_IN_DURATION = 0.3f;
    protected const float FADE_OUT_DURATION = 0.3f;
    protected const float FAST_FADE_DURATION = 0.15f;
    protected const float SLIDE_UP_DISTANCE = 20f;

    protected CanvasGroup canvasGroup;
    protected RectTransform rectTransform;

    /// <summary>
    /// Called by ContentZoneController after hide animation finishes.
    /// </summary>
    public Action OnHideComplete;

    private Sequence currentSequence;

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
    /// Fade in with slide-up animation. 0.3s ease-out.
    /// </summary>
    public virtual void Show()
    {
        KillCurrentSequence();

        Vector2 startPos = rectTransform.anchoredPosition;
        rectTransform.anchoredPosition = startPos - new Vector2(0f, SLIDE_UP_DISTANCE);

        currentSequence = DOTween.Sequence()
            .Join(canvasGroup.DOFade(1f, FADE_IN_DURATION).SetEase(Ease.OutQuad))
            .Join(rectTransform.DOAnchorPos(startPos, FADE_IN_DURATION).SetEase(Ease.OutQuad));
    }

    /// <summary>
    /// Fade out. Normal = 0.3s, fast = 0.15s. Invokes OnHideComplete when done.
    /// </summary>
    public virtual void Hide(bool fast = false)
    {
        KillCurrentSequence();

        float duration = fast ? FAST_FADE_DURATION : FADE_OUT_DURATION;

        currentSequence = DOTween.Sequence()
            .Append(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad))
            .OnComplete(() => OnHideComplete?.Invoke());
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

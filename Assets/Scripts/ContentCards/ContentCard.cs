using UnityEngine;
using DG.Tweening;
using System;
using MugsTech.Style;

/// <summary>
/// Abstract base class for all content zone cards.
/// Self-building: each card constructs its own UI hierarchy in Awake.
/// Handles fade in/out animations via DOTween, duration timing, and self-cleanup.
///
/// When a <see cref="StyleManager"/> with an active preset is present, the card
/// applies the preset's rotation variance, wobble, and entry direction.
/// Without a preset, falls back to the original slide-up entry.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public abstract class ContentCard : MonoBehaviour
{
    protected const float FADE_IN_DURATION = 0.3f;
    protected const float FADE_OUT_DURATION = 0.3f;
    protected const float FAST_FADE_DURATION = 0.15f;
    protected const float SLIDE_UP_DISTANCE = 20f;
    protected const float ENTRY_SLIDE_DISTANCE = 80f; // distance for preset-driven entries

    protected CanvasGroup canvasGroup;
    protected RectTransform rectTransform;

    /// <summary>Called by ContentZoneController after hide animation finishes.</summary>
    public Action OnHideComplete;

    private Sequence currentSequence;

    // Entry direction set by ContentZoneController before Show() is called.
    // Defaults to FromBottom if not explicitly set.
    private EntryDirection entryDirection = EntryDirection.FromBottom;
    private bool entryDirectionExplicitlySet = false;

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
    /// </summary>
    public void SetEntryDirection(EntryDirection direction)
    {
        entryDirection = direction;
        entryDirectionExplicitlySet = true;
    }

    /// <summary>
    /// Fade in. With no active preset: slide up 20px, ease-out, 0.3s.
    /// With active preset: random Z rotation, slide from preset's entry direction
    /// using preset's animation curve, optional elastic wobble.
    /// </summary>
    public virtual void Show()
    {
        KillCurrentSequence();

        ChannelStylePreset preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;

        if (preset == null)
        {
            // ----- Original behavior (no preset) -----
            Vector2 startPos = rectTransform.anchoredPosition;
            rectTransform.anchoredPosition = startPos - new Vector2(0f, SLIDE_UP_DISTANCE);

            currentSequence = DOTween.Sequence()
                .Join(canvasGroup.DOFade(1f, FADE_IN_DURATION).SetEase(Ease.OutQuad))
                .Join(rectTransform.DOAnchorPos(startPos, FADE_IN_DURATION).SetEase(Ease.OutQuad));
        }
        else
        {
            // ----- Preset-driven entry -----
            Vector2 endPos = rectTransform.anchoredPosition;
            Vector2 offset = GetEntryOffset(entryDirection, ENTRY_SLIDE_DISTANCE);
            rectTransform.anchoredPosition = endPos + offset;

            // Random Z rotation (imperfect "tossed onto a corkboard" feel)
            float rotationZ = preset.GetRandomEntryRotation();
            rectTransform.localEulerAngles = new Vector3(0f, 0f, rotationZ);

            Ease ease = ConvertCurve(preset.entryCurve);

            Sequence seq = DOTween.Sequence()
                .Join(canvasGroup.DOFade(1f, FADE_IN_DURATION).SetEase(Ease.OutQuad))
                .Join(rectTransform.DOAnchorPos(endPos, FADE_IN_DURATION).SetEase(ease));

            // Wobble: scale 1 -> 1+wobble -> 1 with elastic out
            if (preset.wobbleIntensity > 0.001f)
            {
                Vector3 baseScale = rectTransform.localScale;
                float w = preset.wobbleIntensity * 0.15f; // max 15% scale boost at intensity=1
                seq.Join(rectTransform
                    .DOPunchScale(new Vector3(w, w, 0f), FADE_IN_DURATION + 0.1f, vibrato: 4, elasticity: 0.7f));
            }

            currentSequence = seq;
        }
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Vector2 GetEntryOffset(EntryDirection direction, float distance)
    {
        switch (direction)
        {
            case EntryDirection.FromLeft:   return new Vector2(-distance, 0f);
            case EntryDirection.FromRight:  return new Vector2(distance, 0f);
            case EntryDirection.FromTop:    return new Vector2(0f, distance);
            case EntryDirection.FromBottom: return new Vector2(0f, -distance);
            default:                        return new Vector2(0f, -distance);
        }
    }

    private static Ease ConvertCurve(EntryAnimationCurve c)
    {
        switch (c)
        {
            case EntryAnimationCurve.Elastic:     return Ease.OutElastic;
            case EntryAnimationCurve.EaseOut:     return Ease.OutQuad;
            case EntryAnimationCurve.EaseOutBack: return Ease.OutBack;
            case EntryAnimationCurve.Linear:      return Ease.Linear;
            default:                              return Ease.OutQuad;
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

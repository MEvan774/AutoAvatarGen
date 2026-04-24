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
/// Without a preset, slides in from off-screen left with a small overshoot.
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
    protected const float ENTRY_SLIDE_DURATION = 0.35f; // from-left overshoot entry duration

    // Overshoot curve copied from the CSS linear() keyframes:
    //   linear(0, 0.221 2.5%, 0.421 5.2%, …, 1.109 32.8%, 1.109 36.1%, …, 1 87.9%, 1)
    // Peaks at ~1.109 around 33% of the duration, then settles back to 1.0 by 88%.
    // Built once and reused across every card.
    protected static readonly AnimationCurve OVERSHOOT_CURVE = BuildOvershootCurve();

    protected CanvasGroup canvasGroup;
    protected RectTransform rectTransform;

    /// <summary>Called by ContentZoneController after hide animation finishes.</summary>
    public Action OnHideComplete;

    protected Sequence currentSequence;

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
    /// Fade in. With no active preset: slide in from off-screen left with a
    /// small overshoot (CSS linear() curve, peaks ~13% past the rest position
    /// around 32% of the duration, then settles back). Fade runs in 0.3s, slide
    /// in 0.5s. With active preset: random Z rotation, slide from preset's
    /// entry direction using preset's animation curve, optional elastic wobble.
    /// </summary>
    public virtual void Show()
    {
        KillCurrentSequence();

        ChannelStylePreset preset = StyleManager.Instance != null ? StyleManager.Instance.ActivePreset : null;

        // Always slide in from off-screen left with the CSS-derived overshoot
        // curve, whether or not a preset is active. Preset only contributes
        // the optional rotation and wobble.
        Vector2 endPos = rectTransform.anchoredPosition;
        float slideDistance = rectTransform.rect.width > 1f
            ? rectTransform.rect.width
            : 400f; // fallback if layout hasn't resolved yet
        rectTransform.anchoredPosition = endPos - new Vector2(slideDistance, 0f);

        if (preset != null)
        {
            float rotationZ = preset.GetRandomEntryRotation();
            rectTransform.localEulerAngles = new Vector3(0f, 0f, rotationZ);
        }

        Sequence seq = DOTween.Sequence()
            .Join(canvasGroup.DOFade(1f, FADE_IN_DURATION).SetEase(Ease.OutQuad))
            .Join(rectTransform.DOAnchorPos(endPos, ENTRY_SLIDE_DURATION).SetEase(OVERSHOOT_CURVE));

        if (preset != null && preset.wobbleIntensity > 0.001f)
        {
            float w = preset.wobbleIntensity * 0.15f;
            seq.Join(rectTransform
                .DOPunchScale(new Vector3(w, w, 0f), FADE_IN_DURATION + 0.1f, vibrato: 4, elasticity: 0.7f));
        }

        currentSequence = seq;
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

    // Translates the CSS linear() keyframes into a Unity AnimationCurve. Each
    // keyframe's in/out tangents are set to the slope of the surrounding
    // segment so Unity's Hermite interpolation collapses to (near-)linear
    // between keys, matching CSS linear() semantics.
    private static AnimationCurve BuildOvershootCurve()
    {
        // (time 0..1, value) — lifted 1:1 from the CSS snippet.
        float[,] pts =
        {
            { 0.000f, 0.000f },
            { 0.025f, 0.221f },
            { 0.052f, 0.421f },
            { 0.080f, 0.592f },
            { 0.109f, 0.733f },
            { 0.140f, 0.852f },
            { 0.156f, 0.901f },
            { 0.173f, 0.946f },
            { 0.190f, 0.984f },
            { 0.208f, 1.017f },
            { 0.227f, 1.045f },
            { 0.247f, 1.068f },
            { 0.272f, 1.089f },
            { 0.299f, 1.102f },
            { 0.328f, 1.109f },
            { 0.361f, 1.109f },
            { 0.391f, 1.105f },
            { 0.425f, 1.096f },
            { 0.547f, 1.052f },
            { 0.598f, 1.035f },
            { 0.642f, 1.024f },
            { 0.686f, 1.015f },
            { 0.743f, 1.007f },
            { 0.807f, 1.002f },
            { 0.879f, 1.000f },
            { 1.000f, 1.000f },
        };

        int n = pts.GetLength(0);
        Keyframe[] frames = new Keyframe[n];
        for (int i = 0; i < n; i++)
        {
            float t = pts[i, 0];
            float v = pts[i, 1];

            float inTangent = 0f;
            if (i > 0)
            {
                float dt = t - pts[i - 1, 0];
                if (dt > 0f) inTangent = (v - pts[i - 1, 1]) / dt;
            }

            float outTangent = 0f;
            if (i < n - 1)
            {
                float dt = pts[i + 1, 0] - t;
                if (dt > 0f) outTangent = (pts[i + 1, 1] - v) / dt;
            }

            frames[i] = new Keyframe(t, v, inTangent, outTangent);
        }

        return new AnimationCurve(frames);
    }

    protected virtual void OnDestroy()
    {
        KillCurrentSequence();
    }
}

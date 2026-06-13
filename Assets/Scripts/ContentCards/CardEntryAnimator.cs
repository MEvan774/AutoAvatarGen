using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// Central settings for every content card's entry animation. One overshoot
/// curve + one set of timing values, shared by every card type. Add per-card
/// rows to override direction or timing for an individual card type.
///
/// Auto-bootstrapping: if no CardEntryAnimator is in the scene when the first
/// card appears, one is created automatically (with default values). The
/// expected setup is for ContentZoneController to add this component to its
/// own GameObject so the values are tweakable in the inspector.
/// </summary>
[DisallowMultipleComponent]
public class CardEntryAnimator : MonoBehaviour
{
    private static CardEntryAnimator _instance;

    /// <summary>
    /// Singleton accessor. If no animator exists in the scene, a default one is
    /// auto-created so cards always have a settings source.
    /// </summary>
    public static CardEntryAnimator Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindObjectOfType<CardEntryAnimator>();
            if (_instance != null) return _instance;

            GameObject go = new GameObject("CardEntryAnimator (auto)");
            _instance = go.AddComponent<CardEntryAnimator>();
            return _instance;
        }
    }

    [Serializable]
    public class CardSettings
    {
        public ContentCardType cardType;

        [Tooltip("If checked, this card type slides in from the chosen Direction below. " +
                 "Uncheck to use the runtime-computed direction (style preset / character position).")]
        public bool overrideDirection = true;

        [Tooltip("Side this card slides in from (when Override Direction is checked).")]
        public EntryDirection direction = EntryDirection.FromLeft;

        [Tooltip("Slide-in duration in seconds. Leave at 0 to use Default Slide Duration.")]
        [Min(0f)]
        public float slideDuration = 0f;

        [Tooltip("Fade-in duration in seconds. Leave at 0 to use Default Fade-In Duration.")]
        [Min(0f)]
        public float fadeInDuration = 0f;

        [Tooltip("How far off-screen the card starts, as a multiplier of the card's own width " +
                 "(horizontal slides) or height (vertical slides). 1.0 = exactly off the card's " +
                 "edge. Increase for a longer travel.")]
        [Min(0.1f)]
        public float slideDistanceFactor = 1.0f;
    }

    [Header("Overshoot Curve (used by every card entry)")]
    [Tooltip("Master overshoot curve. Time goes 0→1 over the slide duration; value 1.0 = card " +
             "at its final resting position. The default peaks ~10% past the rest position " +
             "around 33% of the way through, then settles back to 1.0 by 88%.")]
    public AnimationCurve overshootCurve = BuildDefaultOvershootCurve();

    [Header("Global Timing")]
    [Tooltip("Slide-in duration used when a per-card override is left at 0.")]
    [Min(0.05f)]
    public float defaultSlideDuration = 0.55f;

    [Tooltip("Fade-in duration used when a per-card override is left at 0.")]
    [Min(0.05f)]
    public float defaultFadeInDuration = 0.3f;

    [Tooltip("Fade-out duration applied to every card.")]
    [Min(0.05f)]
    public float fadeOutDuration = 0.3f;

    [Tooltip("Fast fade-out duration used when a card is replaced by another.")]
    [Min(0.05f)]
    public float fastFadeOutDuration = 0.15f;

    [Header("Per-Card Settings")]
    [Tooltip("Per-card-type direction & timing overrides. Every card type has a row here so " +
             "you can pick a different entry direction for each one.")]
    public List<CardSettings> perCardSettings = new List<CardSettings>();

    // ---- BigCenter-specific knobs ----------------------------------------
    [Serializable]
    public class BigCenterSettings
    {
        [Tooltip("Seconds the dark background panel takes to slide up and cover the screen.")]
        [Min(0.05f)] public float panelSlideDuration = 0.5f;

        [Tooltip("Pixels the background panel travels. Must be ≥ screen height — 1500 covers up to a 4K viewport.")]
        [Min(100f)]  public float panelSlideDistance = 1500f;

        [Tooltip("Easing for the background panel slide. Smooth, NON-overshoot eases work best (OutExpo / OutQuad / OutCubic / OutSine). Avoid OutBack/OutElastic — bouncing a screen-covering panel feels wrong.")]
        public Ease panelSlideEase = Ease.OutExpo;

        [Tooltip("Pixels the headline text starts off-screen, BEFORE the per-card Slide Distance Factor is applied. Fixed pixel value (instead of reading the rect) so the entrance works on the first frame.")]
        [Min(100f)]  public float headlineTravelBase = 1200f;
    }

    [Header("BigCenter Card")]
    [Tooltip("Knobs specific to the BigCenter card — its background panel and headline travel distance. Direction / Slide Duration / Fade-In Duration for the headline come from the 'BigCenter' row in Per-Card Settings above.")]
    public BigCenterSettings bigCenter = new BigCenterSettings();

    // ---- BigText-specific knobs ------------------------------------------
    [Serializable]
    public class BigTextSettings
    {
        [Tooltip("Seconds between consecutive lines starting their slide. Larger = more 'counting' feel.")]
        [Min(0f)]    public float staggerDelay = 0.35f;

        [Tooltip("Pixels each line starts off-screen, BEFORE the per-card Slide Distance Factor is applied. Fixed pixel value so the entrance works on the first frame.")]
        [Min(100f)]  public float lineTravelBase = 1200f;
    }

    [Header("BigText Card")]
    [Tooltip("Knobs specific to the BigText card — line stagger and travel distance. Direction / Slide Duration / Fade-In Duration come from the 'BigText' row in Per-Card Settings above.")]
    public BigTextSettings bigText = new BigTextSettings();

    // ---- BigMedia-specific knobs -----------------------------------------
    [Serializable]
    public class BigMediaSettings
    {
        [Tooltip("Seconds between consecutive logos popping in. Larger = more 'counting' feel.")]
        [Min(0f)]    public float staggerDelay = 0.35f;
    }

    [Header("BigMedia Card")]
    [Tooltip("Knobs specific to the BigMedia card — slot stagger. Pop duration & overshoot curve come from the 'BigMedia' row in Per-Card Settings above.")]
    public BigMediaSettings bigMedia = new BigMediaSettings();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        EnsureCurveValid();
        EnsureAllCardTypesPresent();
    }

    void Reset()
    {
        overshootCurve = BuildDefaultOvershootCurve();
        perCardSettings.Clear();
        EnsureAllCardTypesPresent();
    }

    void OnValidate()
    {
        EnsureCurveValid();
        EnsureAllCardTypesPresent();
    }

    private void EnsureCurveValid()
    {
        if (overshootCurve == null || overshootCurve.length < 2)
            overshootCurve = BuildDefaultOvershootCurve();
    }

    // Adds rows for any missing card type so the inspector always shows them
    // all. Doesn't touch existing rows, so user edits are preserved.
    private void EnsureAllCardTypesPresent()
    {
        var present = new HashSet<ContentCardType>();
        foreach (var s in perCardSettings)
            if (s != null) present.Add(s.cardType);

        foreach (ContentCardType t in Enum.GetValues(typeof(ContentCardType)))
        {
            if (present.Contains(t)) continue;
            perCardSettings.Add(new CardSettings
            {
                cardType = t,
                direction = DefaultDirectionFor(t),
            });
        }
    }

    private static EntryDirection DefaultDirectionFor(ContentCardType t)
    {
        switch (t)
        {
            case ContentCardType.BigImage:
                return EntryDirection.FromTop;
            case ContentCardType.BigCenter:
            case ContentCardType.BigText:
                return EntryDirection.FromBottom;
            default:
                return EntryDirection.FromLeft;
        }
    }

    public CardSettings GetSettings(ContentCardType type)
    {
        for (int i = 0; i < perCardSettings.Count; i++)
        {
            var s = perCardSettings[i];
            if (s != null && s.cardType == type) return s;
        }
        return null;
    }

    public AnimationCurve Curve => overshootCurve;

    public float GetSlideDuration(ContentCardType type)
    {
        var s = GetSettings(type);
        return (s != null && s.slideDuration > 0f) ? s.slideDuration : defaultSlideDuration;
    }

    public float GetFadeInDuration(ContentCardType type)
    {
        var s = GetSettings(type);
        return (s != null && s.fadeInDuration > 0f) ? s.fadeInDuration : defaultFadeInDuration;
    }

    public float GetSlideDistanceFactor(ContentCardType type)
    {
        var s = GetSettings(type);
        return s != null ? s.slideDistanceFactor : 1f;
    }

    /// <summary>
    /// Returns the direction the given card should slide in from. If the per-card
    /// row has Override Direction checked, that value wins; otherwise the runtime
    /// direction (chosen by ContentZoneController from preset/character) is used.
    /// </summary>
    public EntryDirection ResolveDirection(ContentCardType type, EntryDirection runtime)
    {
        var s = GetSettings(type);
        return (s != null && s.overrideDirection) ? s.direction : runtime;
    }

    // Translates the CSS linear() keyframes into a Unity AnimationCurve. Each
    // keyframe's in/out tangents are the slope of the surrounding segment so
    // Unity's Hermite interpolation collapses to (near-)linear between keys,
    // matching CSS linear() semantics.
    public static AnimationCurve BuildDefaultOvershootCurve()
    {
        float[,] pts =
        {
            { 0.000f, 0.000f }, { 0.025f, 0.221f }, { 0.052f, 0.421f },
            { 0.080f, 0.592f }, { 0.109f, 0.733f }, { 0.140f, 0.852f },
            { 0.156f, 0.901f }, { 0.173f, 0.946f }, { 0.190f, 0.984f },
            { 0.208f, 1.017f }, { 0.227f, 1.045f }, { 0.247f, 1.068f },
            { 0.272f, 1.089f }, { 0.299f, 1.102f }, { 0.328f, 1.109f },
            { 0.361f, 1.109f }, { 0.391f, 1.105f }, { 0.425f, 1.096f },
            { 0.547f, 1.052f }, { 0.598f, 1.035f }, { 0.642f, 1.024f },
            { 0.686f, 1.015f }, { 0.743f, 1.007f }, { 0.807f, 1.002f },
            { 0.879f, 1.000f }, { 1.000f, 1.000f },
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
}

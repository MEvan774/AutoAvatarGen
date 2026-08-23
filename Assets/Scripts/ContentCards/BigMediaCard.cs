using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Large centered image/logo card that appears in front of the character.
/// Tag: {BigMedia:name,duration}  or  {BigMedia:name1+name2+...,duration}
///
/// Names joined by '+' produce up to <see cref="MAX_LOGOS"/> logos arranged
/// in a horizontal row. They pop in one after another with a small stagger,
/// like someone counting on their fingers. A single name collapses to a
/// single centered pop.
///
/// Visuals: no panel background — each logo is centered in its slot with
/// preserveAspect. The row's total width widens with count so each entry
/// stays readable. Parent is the fullscreen feature-media zone (see
/// <see cref="ContentZoneController"/>), so the row sits in front of the
/// character.
///
/// Animation: each visible slot scales 0 → 1 with the CSS-derived overshoot
/// curve (peaks ~10% past full size before settling), staggered by
/// <see cref="STAGGER_DELAY"/> so the entrance reads as a count.
/// </summary>
public class BigMediaCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigMedia;

    // =====================================================================
    // LAYOUT — slot count and horizontal spacing.
    //
    //   MAX_LOGOS              : maximum number of '+'-joined logos.
    //   SLOT_HORIZONTAL_PADDING: pixel inset between sibling slots.
    //   BAND_WIDTHS            : how much horizontal space the row occupies
    //                            for each logo count (1 → 70%, 4 → 92%).
    // =====================================================================
    private const int   MAX_LOGOS               = 4;
    private const float SLOT_HORIZONTAL_PADDING = 32f;

    // =====================================================================
    // ALL TIMING / STAGGER KNOBS LIVE IN THE INSPECTOR
    //
    // Open the ContentZoneController GameObject → CardEntryAnimator
    // component. There are three groups for this card:
    //
    //   • "Per-Card Settings" → BigMedia row
    //         Pop duration override (Slide Duration) & Fade-In Duration
    //         (Direction & Slide Distance Factor don't apply — BigMedia uses
    //          a scale-pop, not a slide.)
    //
    //   • "BigMedia Card" group
    //         Stagger Delay (seconds between consecutive logos popping in)
    //
    //   • "Overshoot Curve" at the top of the component
    //         The shared overshoot curve used by every pop.
    // =====================================================================
    private CardEntryAnimator.BigMediaSettings BigMediaCfg
        => CardEntryAnimator.Instance.bigMedia;

    // Per-count horizontal band of the parent: 1 logo gets 70%, 4 gets 92%.
    // Wider bands for higher counts keep individual logos legible.
    private static readonly float[] BAND_WIDTHS = { 0.70f, 0.80f, 0.88f, 0.92f };

    private readonly List<RectTransform> slotContainers = new List<RectTransform>(MAX_LOGOS);
    // Per-slot CanvasGroups so 'Ease in + fade' can dissolve each logo on its
    // own stagger (the card root stays fully visible during the entrance).
    private readonly List<CanvasGroup> slotGroups = new List<CanvasGroup>(MAX_LOGOS);
    private readonly List<Image> slotImages = new List<Image>(MAX_LOGOS);
    private readonly List<TextMeshProUGUI> slotFallbacks = new List<TextMeshProUGUI>(MAX_LOGOS);

    private int activeSlotCount;

    protected override void BuildUI()
    {
        // Pre-build MAX_LOGOS slots; Initialize() decides how many to activate
        // based on the number of '+'-separated names in the tag.
        for (int i = 0; i < MAX_LOGOS; i++)
        {
            GameObject slotGO = new GameObject($"BigMediaSlot_{i}", typeof(RectTransform));
            slotGO.transform.SetParent(rectTransform, false);
            RectTransform slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.pivot = new Vector2(0.5f, 0.5f);

            // Image fills the slot, aspect preserved so logos never stretch.
            GameObject imgGO = new GameObject("Image", typeof(RectTransform));
            imgGO.transform.SetParent(slotRT, false);
            RectTransform imgRT = imgGO.GetComponent<RectTransform>();
            imgRT.anchorMin = Vector2.zero;
            imgRT.anchorMax = Vector2.one;
            imgRT.offsetMin = Vector2.zero;
            imgRT.offsetMax = Vector2.zero;

            Image img = imgGO.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;

            // Fallback text shown when the sprite can't be resolved.
            TextMeshProUGUI fallback = ContentCardUIBuilder.CreateText(
                slotRT, "FallbackText",
                ContentCardUIBuilder.TextPrimary,
                72f, TextAlignmentOptions.Center, FontStyles.Bold);
            ContentCardUIBuilder.SetStretch(fallback.rectTransform, 16f, 16f, 16f, 16f);
            fallback.enableAutoSizing = true;
            fallback.fontSizeMin = 36f;
            fallback.fontSizeMax = 96f;

            slotGO.SetActive(false);

            slotContainers.Add(slotRT);
            slotGroups.Add(EnsureGroup(slotRT));
            slotImages.Add(img);
            slotFallbacks.Add(fallback);
        }
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        // Multi-logo syntax: names joined by '+' — e.g. "Google+Apple+Meta".
        // A single name (no '+') collapses to a single-slot layout.
        string raw = data.primaryText ?? string.Empty;
        string[] names = raw.Split('+');
        activeSlotCount = Mathf.Clamp(names.Length, 0, MAX_LOGOS);

        for (int i = 0; i < activeSlotCount; i++)
        {
            ApplySlotContent(i, names[i].Trim(), assets);
            slotContainers[i].gameObject.SetActive(true);
        }
        for (int i = activeSlotCount; i < MAX_LOGOS; i++)
            slotContainers[i].gameObject.SetActive(false);

        LayoutSlots(activeSlotCount);
    }

    private void ApplySlotContent(int index, string name, ContentCardAssets assets)
    {
        Sprite sprite = ResolveSprite(name, assets, out string usedPath);
        Image img = slotImages[index];
        TextMeshProUGUI fallback = slotFallbacks[index];

        if (sprite != null)
        {
            Debug.Log($"[BigMedia] slot {index} resolved \"{name}\" via {usedPath}");
            img.sprite = sprite;
            img.gameObject.SetActive(true);
            fallback.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"[BigMedia] slot {index} could not resolve \"{name}\". " +
                             $"Looked in ContentCardAssets.logos and Resources/<folder>/{name}. " +
                             $"Showing fallback text.");
            img.gameObject.SetActive(false);
            fallback.gameObject.SetActive(true);
            string display = name;
            if (display.Length > 0)
                display = char.ToUpper(display[0]) + display.Substring(1);
            fallback.text = display;
        }
    }

    private Sprite ResolveSprite(string name, ContentCardAssets assets, out string usedPath)
    {
        usedPath = null;

        // Tier 1 — ContentCardAssets (logo dictionary, then the SO's own Resources fallback)
        if (assets != null)
        {
            Sprite s = assets.GetBigMedia(name);
            if (s != null) { usedPath = "ContentCardAssets.GetBigMedia"; return s; }
        }

        // Tier 2 — direct Resources lookup, so dropping a file into
        // Assets/Resources/Media/ is enough even without a ContentCardAssets SO.
        //
        // Important: try Texture2D BEFORE Sprite. Resources.Load<Sprite>(path)
        // returns null for PNGs imported with spriteMode = Multiple (the sprite
        // is named "X_0", not "X", so the load-by-asset-name fails). Texture2D
        // loads the underlying image regardless of importer mode, and we wrap
        // it in a Sprite — this is why ArticleTemp.jpeg worked but X.png /
        // Brave.png / Google.png didn't.
        string folder = (assets != null && !string.IsNullOrEmpty(assets.bigMediaResourcesFolder))
            ? assets.bigMediaResourcesFolder
            : "Media";
        string path = $"{folder}/{name}";

        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
        {
            usedPath = $"Resources.Load<Texture2D>(\"{path}\") + Sprite.Create";
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) { usedPath = $"Resources.Load<Sprite>(\"{path}\")"; return sprite; }

        // Last resort — Multiple-mode atlases where the sub-sprite is the
        // first/only one in the file.
        Sprite[] all = Resources.LoadAll<Sprite>(path);
        if (all != null && all.Length > 0)
        {
            usedPath = $"Resources.LoadAll<Sprite>(\"{path}\")[0]";
            return all[0];
        }

        return null;
    }

    // Horizontal row, centered within the parent. Total band widens with
    // count so each slot stays readable. Vertical band is fixed at 70% of
    // the parent so logos have a consistent visual weight.
    private void LayoutSlots(int count)
    {
        if (count <= 0) return;

        float totalWidth = BAND_WIDTHS[Mathf.Clamp(count - 1, 0, BAND_WIDTHS.Length - 1)];
        float perWidth = totalWidth / count;
        float xStart = 0.5f - totalWidth * 0.5f;
        const float yMin = 0.15f;
        const float yMax = 0.85f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.anchorMin = new Vector2(xStart + perWidth * i, yMin);
            rt.anchorMax = new Vector2(xStart + perWidth * (i + 1), yMax);
            rt.offsetMin = new Vector2(SLOT_HORIZONTAL_PADDING * 0.5f, 0f);
            rt.offsetMax = new Vector2(-SLOT_HORIZONTAL_PADDING * 0.5f, 0f);
        }
    }

    public override void Show()
    {
        KillCurrentSequence();

        // BigMedia owns its own entry — flatten any preset rotation and force
        // the CanvasGroup fully visible, then pop each slot in turn. Single
        // logo collapses to a single pop (no stagger to wait through).
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        Sequence seq = DOTween.Sequence();
        float dur = SlideDuration;
        float stagger = BigMediaCfg.staggerDelay;

        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.localEulerAngles = Vector3.zero;
            rt.localScale = Vector3.zero;

            // Build the per-slot tween fully — ease + delay — BEFORE handing
            // it to the sequence. Sequence.Insert with an AnimationCurve ease
            // has been observed to silently drop the curve in some DOTween
            // builds; Join + SetDelay applies the curve reliably.
            Tween popTween = ApplyEntryEase(rt.DOScale(Vector3.one, dur))
                .SetDelay(stagger * i);

            seq.Join(popTween);

            // 'Ease in + fade': the logo dissolves in while it grows, on the
            // same stagger. Overshoot: opaque from the first frame, as before.
            CanvasGroup g = slotGroups[i];
            if (UseOvershootEntry)
            {
                g.alpha = 1f;
            }
            else
            {
                g.alpha = 0f;
                seq.Join(g.DOFade(1f, EntryFadeDuration).SetEase(EntryFadeEase).SetDelay(stagger * i));
            }
        }

        seq.OnComplete(() => StartIdleFloat());
        currentSequence = seq;
    }
}

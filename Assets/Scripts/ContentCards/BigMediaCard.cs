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
    private const int MAX_LOGOS = 4;
    private const float STAGGER_DELAY = 0.35f;
    private const float POP_DURATION = 0.55f;
    private const float SLOT_HORIZONTAL_PADDING = 32f;

    // Per-count horizontal band of the parent: 1 logo gets 70%, 4 gets 92%.
    // Wider bands for higher counts keep individual logos legible.
    private static readonly float[] BAND_WIDTHS = { 0.70f, 0.80f, 0.88f, 0.92f };

    private readonly List<RectTransform> slotContainers = new List<RectTransform>(MAX_LOGOS);
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
        string folder = (assets != null && !string.IsNullOrEmpty(assets.bigMediaResourcesFolder))
            ? assets.bigMediaResourcesFolder
            : "Media";
        string path = $"{folder}/{name}";

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) { usedPath = $"Resources.Load<Sprite>(\"{path}\")"; return sprite; }

        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
        {
            usedPath = $"Resources.Load<Texture2D>(\"{path}\") + Sprite.Create";
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
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

        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.localEulerAngles = Vector3.zero;
            rt.localScale = Vector3.zero;
            seq.Insert(STAGGER_DELAY * i,
                rt.DOScale(Vector3.one, POP_DURATION).SetEase(OVERSHOOT_CURVE));
        }

        currentSequence = seq;
    }
}

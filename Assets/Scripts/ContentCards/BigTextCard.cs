using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// Large centered text card. One or more lines slide up from off-screen
/// to a vertically-stacked, centered group with the same CSS-derived
/// overshoot the rest of the cards use.
///
/// Tag: {BigText:line,duration}  or  {BigText:line1+line2+...,duration}
///
/// Lines joined by '+' produce up to <see cref="MAX_LINES"/> stacked lines.
/// Each line slides in using the central overshoot curve from
/// <see cref="CardEntryAnimator"/>, from the configured direction, staggered
/// by <see cref="STAGGER_DELAY"/> so additional lines appear to "count up"
/// beneath the first — the same rhythm as <see cref="BigMediaCard"/>.
///
/// Lives in the fullscreen feature-media zone so the text renders over
/// the character, matching <see cref="BigCenterCard"/> and <see cref="BigMediaCard"/>.
/// </summary>
public class BigTextCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigText;

    // =====================================================================
    // LAYOUT — sizes of each line and the spacing between them.
    //
    //   MAX_LINES                : maximum number of '+'-joined lines.
    //   LINE_HEIGHT              : pixel height of each line container.
    //   LINE_GAP                 : pixel gap between stacked lines.
    //   LINE_HORIZONTAL_PADDING  : pixel inset on the left/right of each line.
    // =====================================================================
    private const int   MAX_LINES               = 4;
    private const float LINE_HEIGHT             = 200f;
    private const float LINE_GAP                = 24f;
    private const float LINE_HORIZONTAL_PADDING = 80f;

    // How long already-visible lines take to glide to their re-centered slots
    // when AppendLines grows the stack (the persistent {BigText:LINE} flow).
    private const float LINE_SHIFT_DURATION     = 0.35f;

    // =====================================================================
    // ALL TIMING / DISTANCE KNOBS LIVE IN THE INSPECTOR
    //
    // Open the ContentZoneController GameObject → CardEntryAnimator
    // component. There are three groups for this card:
    //
    //   • "Per-Card Settings" → BigText row
    //         Direction the lines slide in from
    //         Override Direction / Slide Duration / Fade-In Duration / Slide Distance Factor
    //
    //   • "BigText Card" group
    //         Stagger Delay (seconds between lines)
    //         Line Travel Base (pixel off-screen distance, before factor)
    //
    //   • "Overshoot Curve" at the top of the component
    //         The shared overshoot curve used by every line.
    // =====================================================================
    private CardEntryAnimator.BigTextSettings BigTextCfg
        => CardEntryAnimator.Instance.bigText;

    private readonly List<RectTransform> lineContainers = new List<RectTransform>(MAX_LINES);
    private readonly List<TextMeshProUGUI> lineTexts = new List<TextMeshProUGUI>(MAX_LINES);
    private readonly List<Image> lineBackgrounds = new List<Image>(MAX_LINES);
    // Per-line CanvasGroups so 'Ease in + fade' can dissolve each line on its
    // own stagger (the card root stays fully visible during the entrance).
    private readonly List<CanvasGroup> lineGroups = new List<CanvasGroup>(MAX_LINES);

    private int activeLineCount;

    protected override void BuildUI()
    {
        // Pre-build MAX_LINES slots; Initialize() decides how many to activate
        // based on the number of '+'-separated lines in the tag.
        for (int i = 0; i < MAX_LINES; i++)
        {
            GameObject containerGO = new GameObject($"BigTextLine_{i}", typeof(RectTransform));
            containerGO.transform.SetParent(rectTransform, false);

            RectTransform containerRT = containerGO.GetComponent<RectTransform>();
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0.5f, 0.5f);
            containerRT.sizeDelta = new Vector2(1600f, LINE_HEIGHT);

            // Optional background plate — sits behind the text on every line.
            // Sibling order matters: this is added FIRST so it renders below
            // the text. Disabled until VisualsRuntimeApplier.BigText opts in.
            GameObject bgGO = new GameObject("Background", typeof(RectTransform));
            bgGO.transform.SetParent(containerRT, false);
            RectTransform bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            Image bg = bgGO.AddComponent<Image>();
            bg.raycastTarget = false;
            bg.gameObject.SetActive(false);
            lineBackgrounds.Add(bg);

            TextMeshProUGUI tmp = ContentCardUIBuilder.CreateText(
                containerRT, "Text",
                Color.white,
                160f, TextAlignmentOptions.Center,
                FontStyles.Bold);
            ContentCardUIBuilder.SetStretch(tmp.rectTransform,
                LINE_HORIZONTAL_PADDING, 0f, LINE_HORIZONTAL_PADDING, 0f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 64f;
            tmp.fontSizeMax = 200f;
            tmp.maxVisibleLines = 2;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableWordWrapping = true;

            ApplyBigTextStyle(tmp, bg);

            containerGO.SetActive(false);

            lineContainers.Add(containerRT);
            lineTexts.Add(tmp);
            lineGroups.Add(EnsureGroup(containerRT));
        }
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        // Multi-line syntax: lines joined by '+' — e.g. "100M Users+$50B Revenue".
        // A single line (no '+') collapses to a single centered slot.
        string raw = data.primaryText ?? string.Empty;
        string[] lines = raw.Split('+');
        activeLineCount = Mathf.Clamp(lines.Length, 0, MAX_LINES);

        for (int i = 0; i < activeLineCount; i++)
        {
            lineTexts[i].text = lines[i].Trim();
            lineContainers[i].gameObject.SetActive(true);
        }
        for (int i = activeLineCount; i < MAX_LINES; i++)
            lineContainers[i].gameObject.SetActive(false);

        LayoutLines(activeLineCount);
    }

    // Vertical stack centered on the parent. Stack height grows with line
    // count; the group is always centered around y=0.
    private void LayoutLines(int count)
    {
        if (count <= 0) return;

        float totalHeight = count * LINE_HEIGHT + Mathf.Max(0, count - 1) * LINE_GAP;
        float topCenter = totalHeight * 0.5f - LINE_HEIGHT * 0.5f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = lineContainers[i];
            float y = topCenter - i * (LINE_HEIGHT + LINE_GAP);
            rt.anchoredPosition = new Vector2(0f, y);
        }
    }

    public override void Show()
    {
        KillCurrentSequence();

        // BigText owns its own entry — flatten any preset rotation and force
        // the CanvasGroup fully visible, then slide each line in turn from
        // the resolved direction up to its stacked resting position.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        var cfg = BigTextCfg;

        // Off-screen offset uses lineTravelBase × the per-card Slide Distance
        // Factor (both from CardEntryAnimator). Fixed base distance keeps the
        // slide reliable even on the first frame (when rect.height isn't
        // resolved).
        Vector2 offset = LineEntryOffset(ResolvedEntryDirection, cfg.lineTravelBase, SlideDistanceFactor);
        float dur = SlideDuration;
        float stagger = cfg.staggerDelay;

        // The offset alone measures the start from each line's RESTING slot,
        // but the slots form a stack — on a vertical entry the stack's leading
        // lines would start a whole stack-height closer to the screen than its
        // trailing line (a 4-line stack put the first two lines in full view).
        // Instead every line departs from one shared start on the travel axis:
        // `offset` beyond the stack's trailing slot, and never nearer than
        // fully outside the view (see EntryStartFor).
        float minRestY = float.MaxValue, maxRestY = float.MinValue;
        for (int i = 0; i < activeLineCount; i++)
        {
            float y = lineContainers[i].anchoredPosition.y;
            if (y < minRestY) minRestY = y;
            if (y > maxRestY) maxRestY = y;
        }

        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            rt.localEulerAngles = Vector3.zero;

            Vector2 endPos = rt.anchoredPosition;
            rt.anchoredPosition = EntryStartFor(endPos, offset, minRestY, maxRestY);

            // Build the per-line tween fully — ease + delay — BEFORE handing it
            // to the sequence. Sequence.Insert with an AnimationCurve ease has
            // been observed to silently drop the curve in some DOTween builds;
            // Join + SetDelay applies the curve reliably.
            Tween lineTween = ApplyEntryEase(rt.DOAnchorPos(endPos, dur))
                .SetDelay(stagger * i);

            seq.Join(lineTween);
            JoinLineFade(seq, i, stagger * i);
        }

        currentSequence = seq;
    }

    // 'Ease in + fade': the line starts invisible and dissolves in on the same
    // delay as its slide. Overshoot: the line is simply opaque, as before.
    private void JoinLineFade(Sequence seq, int lineIndex, float delay)
    {
        CanvasGroup g = lineGroups[lineIndex];
        if (UseOvershootEntry)
        {
            g.alpha = 1f;
            return;
        }
        g.alpha = 0f;
        seq.Join(g.DOFade(1f, EntryFadeDuration).SetEase(EntryFadeEase).SetDelay(delay));
    }

    /// <summary>
    /// Adds one or more '+'-joined lines to the stack already on screen — the
    /// persistent {BigText:LINE} flow, where each tag lands its line on the
    /// narration beat it sits on. Lines already visible glide to their
    /// re-centered slots while the new lines slide in from off-screen with the
    /// same overshoot entrance as Show(). Returns false when nothing fit
    /// (the stack is already at MAX_LINES).
    /// </summary>
    public bool AppendLines(string raw)
    {
        string[] parts = (raw ?? string.Empty).Split('+');
        int room = MAX_LINES - activeLineCount;
        if (room <= 0)
        {
            Debug.LogWarning($"BigText: stack already has {MAX_LINES} lines — '{raw}' dropped.");
            return false;
        }
        int adding = Mathf.Min(parts.Length, room);
        if (adding < parts.Length)
            Debug.LogWarning($"BigText: only {adding} of {parts.Length} appended lines fit — the rest are dropped.");

        int firstNew = activeLineCount;
        for (int i = 0; i < adding; i++)
        {
            lineTexts[firstNew + i].text = parts[i].Trim();
            lineContainers[firstNew + i].gameObject.SetActive(true);
        }
        activeLineCount += adding;

        // Slot Ys of the grown, re-centered stack (same math as LayoutLines).
        float totalHeight = activeLineCount * LINE_HEIGHT + (activeLineCount - 1) * LINE_GAP;
        float topCenter = totalHeight * 0.5f - LINE_HEIGHT * 0.5f;

        var cfg = BigTextCfg;
        Vector2 offset = LineEntryOffset(ResolvedEntryDirection, cfg.lineTravelBase, SlideDistanceFactor);
        float minRestY = topCenter - (activeLineCount - 1) * (LINE_HEIGHT + LINE_GAP);
        float maxRestY = topCenter;
        float dur = SlideDuration;

        // Killing a still-running entrance leaves lines mid-flight; the shift
        // tween below picks each one up from wherever it is.
        KillCurrentSequence();
        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            rt.localEulerAngles = Vector3.zero;
            Vector2 endPos = new Vector2(0f, topCenter - i * (LINE_HEIGHT + LINE_GAP));

            if (i < firstNew)
            {
                // Already on screen — glide to the re-centered slot. A line
                // whose fade-in was cut short by the kill above finishes it
                // during the glide.
                seq.Join(rt.DOAnchorPos(endPos, LINE_SHIFT_DURATION).SetEase(Ease.OutQuad));
                if (lineGroups[i].alpha < 1f)
                    seq.Join(lineGroups[i].DOFade(1f, LINE_SHIFT_DURATION).SetEase(Ease.OutQuad));
            }
            else
            {
                rt.anchoredPosition = EntryStartFor(endPos, offset, minRestY, maxRestY);
                float delay = cfg.staggerDelay * (i - firstNew);
                seq.Join(ApplyEntryEase(rt.DOAnchorPos(endPos, dur)).SetDelay(delay));
                JoinLineFade(seq, i, delay);
            }
        }

        currentSequence = seq;
        return true;
    }

    // Start point for a line entering toward endPos: `offset` beyond the
    // stack's trailing slot on the travel axis (minRestY/maxRestY are the
    // stack's resting extremes), clamped to fully outside the view. The card
    // rect isn't resolved on the first frame, so the view half-size falls back
    // to the 1920x1080 reference the layout constants are authored in.
    private Vector2 EntryStartFor(Vector2 endPos, Vector2 offset, float minRestY, float maxRestY)
    {
        float halfH = rectTransform.rect.height > 1f ? rectTransform.rect.height * 0.5f : 540f;
        float halfW = rectTransform.rect.width  > 1f ? rectTransform.rect.width  * 0.5f : 960f;
        float outY = halfH + LINE_HEIGHT * 0.5f;   // |y| beyond this = fully hidden
        float outX = halfW + 800f;                 // line containers are 1600 wide

        Vector2 startPos = endPos + offset;
        if      (offset.y < 0f) startPos.y = Mathf.Min(minRestY + offset.y, -outY);
        else if (offset.y > 0f) startPos.y = Mathf.Max(maxRestY + offset.y,  outY);
        else if (offset.x < 0f) startPos.x = Mathf.Min(startPos.x, -outX);
        else if (offset.x > 0f) startPos.x = Mathf.Max(startPos.x,  outX);
        return startPos;
    }

    // Each line's off-screen start offset = travelBase × per-card factor,
    // applied along the resolved direction. Fixed base distance (from the
    // animator's BigText group) ensures the entrance works on the first
    // frame regardless of layout state.
    private static Vector2 LineEntryOffset(EntryDirection dir, float travelBase, float factor)
    {
        float d = travelBase * factor;
        switch (dir)
        {
            case EntryDirection.FromLeft:   return new Vector2(-d, 0f);
            case EntryDirection.FromRight:  return new Vector2( d, 0f);
            case EntryDirection.FromTop:    return new Vector2(0f,  d);
            case EntryDirection.FromBottom: return new Vector2(0f, -d);
            default:                        return new Vector2(0f, -d);
        }
    }

    // Reads VisualsRuntimeApplier.BigText overrides (populated from the
    // active VisualsSave) and applies them to the line's text + background.
    // Touching fontMaterial first forces TMP to clone the shared material so
    // edits only affect this card's text instances. OUTLINE_ON / UNDERLAY_ON
    // are the shader-side toggles that the "Outline" / "Underlay" checkboxes
    // in the TMP material inspector flip — without them, setting outline /
    // shadow fields is a no-op on some shader variants.
    private static void ApplyBigTextStyle(TextMeshProUGUI tmp, Image bg)
    {
        Material mat = tmp.fontMaterial;

        // Text color
        Color textColor = VisualsRuntimeApplier.BigText.TextColor ?? Color.white;
        tmp.color = textColor;

        // Font style — BigText has its own pick (independent of card style),
        // so replace whatever ContentCardUIBuilder.CreateText set.
        switch (VisualsRuntimeApplier.BigText.FontStyle)
        {
            case UnityEngine.FontStyle.Bold:          tmp.fontStyle = FontStyles.Bold; break;
            case UnityEngine.FontStyle.Italic:        tmp.fontStyle = FontStyles.Italic; break;
            case UnityEngine.FontStyle.BoldAndItalic: tmp.fontStyle = FontStyles.Bold | FontStyles.Italic; break;
            default:                                  tmp.fontStyle = FontStyles.Normal; break;
        }

        // Outline (always enabled; user picks color + width)
        Color outlineColor = VisualsRuntimeApplier.BigText.OutlineColor ?? new Color(0f, 0f, 0f, 0.75f);
        float outlineWidth = VisualsRuntimeApplier.BigText.OutlineWidth;
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineWidth", outlineWidth);
        tmp.outlineColor = outlineColor;
        tmp.outlineWidth = outlineWidth;

        // Shadow (TMP underlay) — opt-in
        if (VisualsRuntimeApplier.BigText.ShadowEnabled)
        {
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor",    VisualsRuntimeApplier.BigText.ShadowColor);
            mat.SetFloat("_UnderlayOffsetX",  1.0f);
            mat.SetFloat("_UnderlayOffsetY", -1.0f);
            mat.SetFloat("_UnderlayDilate",   0.5f);
            mat.SetFloat("_UnderlaySoftness", VisualsRuntimeApplier.BigText.ShadowSoftness);
        }
        else
        {
            mat.DisableKeyword("UNDERLAY_ON");
        }

        tmp.UpdateMeshPadding();

        // Background plate behind the text — opt-in
        if (bg != null)
        {
            if (VisualsRuntimeApplier.BigText.BackgroundEnabled)
            {
                bg.gameObject.SetActive(true);
                bg.color  = VisualsRuntimeApplier.BigText.BackgroundColor;
                int radius = Mathf.Max(0, Mathf.RoundToInt(
                    VisualsRuntimeApplier.BigText.BackgroundCornerRadius));
                if (radius > 0)
                {
                    bg.sprite = StyleSpriteFactory.GetRoundedRect(radius);
                    bg.type   = Image.Type.Sliced;
                }
                else
                {
                    bg.sprite = null;
                    bg.type   = Image.Type.Simple;
                }
            }
            else
            {
                bg.gameObject.SetActive(false);
            }
        }
    }

    public override void Hide(bool fast = false)
    {
        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();

        // Mirror the entrance: lines exit in the same direction they came
        // from (using the same fixed travel base × per-card factor).
        Vector2 offset = LineEntryOffset(
            ResolvedEntryDirection, BigTextCfg.lineTravelBase, SlideDistanceFactor);
        float fadeDur = FadeOutDuration;

        Sequence seq = DOTween.Sequence();
        seq.Join(canvasGroup.DOFade(0f, fadeDur).SetEase(Ease.InQuad));

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            Vector2 startPos = rt.anchoredPosition;
            seq.Join(rt.DOAnchorPos(startPos + offset, fadeDur)
                .SetEase(Ease.InQuad));
        }

        seq.OnComplete(() => OnHideComplete?.Invoke());
        currentSequence = seq;
    }
}

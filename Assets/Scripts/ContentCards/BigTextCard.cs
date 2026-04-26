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
/// Each line slides in from below the screen with the OVERSHOOT_CURVE,
/// staggered by <see cref="STAGGER_DELAY"/> so additional lines appear to
/// "count up" beneath the first — the same rhythm as <see cref="BigMediaCard"/>.
///
/// Lives in the fullscreen feature-media zone so the text renders over
/// the character, matching <see cref="BigCenterCard"/> and <see cref="BigMediaCard"/>.
/// </summary>
public class BigTextCard : ContentCard
{
    private const int MAX_LINES = 4;
    private const float STAGGER_DELAY = 0.35f;
    private const float SLIDE_DURATION = 0.55f;

    private const float LINE_HEIGHT = 200f;
    private const float LINE_GAP = 24f;
    private const float LINE_HORIZONTAL_PADDING = 80f;

    private readonly List<RectTransform> lineContainers = new List<RectTransform>(MAX_LINES);
    private readonly List<TextMeshProUGUI> lineTexts = new List<TextMeshProUGUI>(MAX_LINES);
    private readonly List<Image> lineBackgrounds = new List<Image>(MAX_LINES);

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
        // below the screen up to its stacked resting position.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float offscreenDrop = screenHeight * 0.5f + 240f;

        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            rt.localEulerAngles = Vector3.zero;

            Vector2 endPos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(endPos.x, endPos.y - offscreenDrop);

            seq.Insert(STAGGER_DELAY * i,
                rt.DOAnchorPos(endPos, SLIDE_DURATION).SetEase(OVERSHOOT_CURVE));
        }

        currentSequence = seq;
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

        float screenHeight = rectTransform.rect.height > 1f ? rectTransform.rect.height : 1080f;
        float offscreenDrop = screenHeight * 0.5f + 240f;

        Sequence seq = DOTween.Sequence();
        seq.Join(canvasGroup.DOFade(0f, FADE_OUT_DURATION).SetEase(Ease.InQuad));

        for (int i = 0; i < activeLineCount; i++)
        {
            RectTransform rt = lineContainers[i];
            Vector2 startPos = rt.anchoredPosition;
            seq.Join(rt.DOAnchorPosY(startPos.y - offscreenDrop, FADE_OUT_DURATION)
                .SetEase(Ease.InQuad));
        }

        seq.OnComplete(() => OnHideComplete?.Invoke());
        currentSequence = seq;
    }
}

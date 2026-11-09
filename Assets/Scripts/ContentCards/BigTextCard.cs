using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// Large centred text — the video's loudest moment. One or more lines stack
/// in the middle of the frame over a full-screen dim.
///
/// Tag: {BigText:line,duration}  or  {BigText:line1+line2+...,duration}
///      {BigText:LINE} … {BigText:LINE} … {BigText:End}   (held stack)
///
/// Lines joined by '+' produce up to <see cref="MAX_LINES"/> stacked lines;
/// in the held flow each duration-less tag adds its line on its word and the
/// stack re-centres.
///
/// Look (see <see cref="BigTextTypography"/>):
///   • a black layer behind the text dims the whole scene (background, Mugs,
///     side cards) to ~45 % — fading in over 0.2 s with the first line and out
///     over 0.25 s with the End / the timed card's end;
///   • Inter ExtraBold, white, the visuals save's outline drawn OUTSIDE the
///     letters (so it no longer thins them) and its optional shadow / plate;
///   • one size for the whole stack, from its longest line: ≤ 85 % of the frame
///     width, cap height 80–140 px; under 80 a line wraps (balanced rows) at a
///     word boundary; the stack fits the title-safe height;
///   • each line enters with a scale punch (1.12 → 1.0, slight overshoot,
///     0.18 s) exactly on its beat, instead of the slide.
///
/// Lives in the fullscreen feature-media zone (sort order 31000), so the dim
/// and text are above the scene and side cards and below transitions / Black.
/// </summary>
public class BigTextCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigText;

    private const int MAX_LINES = 4;

    // Stagger between the '+'-joined lines of one tag comes from
    // CardEntryAnimator → BigText Card → Stagger Delay. The BigText row's
    // direction / slide settings no longer apply (the punch replaced the slide).
    private CardEntryAnimator.BigTextSettings BigTextCfg => CardEntryAnimator.Instance.bigText;

    private readonly List<RectTransform> lineContainers = new List<RectTransform>(MAX_LINES);
    private readonly List<TextMeshProUGUI> lineTexts = new List<TextMeshProUGUI>(MAX_LINES);
    private readonly List<Image> lineBackgrounds = new List<Image>(MAX_LINES);
    private readonly List<CanvasGroup> lineGroups = new List<CanvasGroup>(MAX_LINES);
    private readonly List<int> lineRows = new List<int>(MAX_LINES);

    private Image dim;
    private RectTransform linesRoot;
    private CanvasGroup linesGroup;
    private TextMeshProUGUI measure; // never drawn — sizes lines that aren't on screen yet

    private BigTextTypography.DimEnvelope dimEnvelope;
    private float clock; // scaled time, like DOTween

    private int activeLineCount;
    private float stackSize;
    private float stackCap;

    // Per-line entrance, stepped every frame on `clock` (see StepEntrance):
    // when the punch starts (NaN = settled), and the re-centring glide.
    private readonly float[] punchStart = { float.NaN, float.NaN, float.NaN, float.NaN };
    private readonly bool[] punchDrawn = new bool[4];
    private readonly float[] glideStart = { float.NaN, float.NaN, float.NaN, float.NaN };
    private readonly Vector2[] glideFrom = new Vector2[4];
    private readonly Vector2[] glideTo = new Vector2[4];
    private bool entranceRunning;

    /// <summary>
    /// Every line this stack will hold, set by ContentZoneController before
    /// Initialize (the held flow's later {BigText:LINE} tags, read ahead from
    /// the timeline) — so the stack is sized once, from its longest line,
    /// instead of jumping when a longer line arrives.
    /// </summary>
    public List<string> AnticipatedLines { get; set; }

    /// <summary>The last fit (read by the self-tests).</summary>
    public BigTextTypography.StackFit LastFit { get; private set; }

    public float DimAlpha => dim != null ? dim.color.a : 0f;
    public BigTextTypography.DimEnvelope Dim => dimEnvelope;

    protected override void BuildUI()
    {
        dimEnvelope = new BigTextTypography.DimEnvelope(
            BigTextTypography.DimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear));

        // Full-screen dim — first child, so it renders behind the text.
        dim = ContentCardUIBuilder.CreateImage(rectTransform, "Dim", new Color(0f, 0f, 0f, 0f));
        ContentCardUIBuilder.SetStretch(dim.rectTransform, 0f, 0f, 0f, 0f);

        linesRoot = ContentCardUIBuilder.CreateChild(rectTransform, "Lines");
        linesGroup = EnsureGroup(linesRoot);

        for (int i = 0; i < MAX_LINES; i++)
        {
            GameObject containerGO = new GameObject($"BigTextLine_{i}", typeof(RectTransform));
            containerGO.transform.SetParent(linesRoot, false);
            RectTransform containerRT = containerGO.GetComponent<RectTransform>();
            containerRT.anchorMin = containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0.5f, 0.5f);

            // Optional plate behind the line (visuals save opt-in). Added first
            // so it renders below the text.
            GameObject bgGO = new GameObject("Background", typeof(RectTransform));
            bgGO.transform.SetParent(containerRT, false);
            RectTransform bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            Image bg = bgGO.AddComponent<Image>();
            bg.raycastTarget = false;
            bg.gameObject.SetActive(false);
            lineBackgrounds.Add(bg);

            TextMeshProUGUI tmp = ContentCardUIBuilder.CreateText(
                containerRT, "Text", Color.white, 160f, TextAlignmentOptions.Top);
            PrepareLineText(tmp);
            ApplyBigTextStyle(tmp, bg);

            containerGO.SetActive(false);
            lineContainers.Add(containerRT);
            lineTexts.Add(tmp);
            lineGroups.Add(EnsureGroup(containerRT));
            lineRows.Add(1);
        }

        measure = ContentCardUIBuilder.CreateText(rectTransform, "Measure", Color.clear, 100f, TextAlignmentOptions.Top);
        PrepareLineText(measure);
        measure.enabled = false;
    }

    private static void PrepareLineText(TextMeshProUGUI tmp)
    {
        CardTypography.ApplyWeight(tmp, CardTypography.Weight.Heavy);
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.maxVisibleLines = 99999;
        tmp.alignment = TextAlignmentOptions.Top;
        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        string[] lines = (data.primaryText ?? string.Empty).Split('+');
        activeLineCount = Mathf.Clamp(lines.Length, 0, MAX_LINES);
        if (lines.Length > MAX_LINES)
            Debug.LogWarning($"BigText: only {MAX_LINES} of {lines.Length} lines fit — the rest are dropped.");

        for (int i = 0; i < activeLineCount; i++)
        {
            lineTexts[i].text = lines[i].Trim();
            lineContainers[i].gameObject.SetActive(true);
        }
        for (int i = activeLineCount; i < MAX_LINES; i++)
            lineContainers[i].gameObject.SetActive(false);

        // Rest every line at its slot (Show punches them in there).
        List<float> slots = FitAndLayout(log: true);
        for (int i = 0; i < activeLineCount; i++)
            lineContainers[i].anchoredPosition = new Vector2(0f, slots[i]);
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame: the card clock, the line entrances and the dim.
    /// Public so the self-tests can step a card frame by frame in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        StepEntrance();
        if (dim != null)
        {
            Color c = dim.color;
            c.a = dimEnvelope.AlphaAt(clock);
            dim.color = c;
        }
    }

    // Punches and glides, from their start times. Each line's punch begins
    // exactly when the line appears — nothing is chained, so nothing waits.
    private void StepEntrance()
    {
        if (!entranceRunning) return;
        bool anyRunning = false;
        for (int i = 0; i < activeLineCount; i++)
        {
            if (!float.IsNaN(punchStart[i]))
            {
                float age = clock - punchStart[i];
                // Anchor the punch to the first frame the line is drawn, so that
                // frame shows the full 1.12× — a beat landing mid-frame no longer
                // eats into the punch before anyone sees it.
                if (!punchDrawn[i] && age >= 0f)
                {
                    punchDrawn[i] = true;
                    punchStart[i] = clock;
                    age = 0f;
                    MugsTech.Sound.SoundLayer.Fire(MugsTech.Sound.SfxKind.BigTextLine); // the line lands
                }
                lineContainers[i].localScale = Vector3.one * BigTextTypography.PunchScaleAt(age);
                lineGroups[i].alpha = BigTextTypography.LineAlphaAt(age);
                if (punchDrawn[i] && age >= Mathf.Max(BigTextTypography.PunchDuration, BigTextTypography.LineAppearSeconds))
                {
                    lineContainers[i].localScale = Vector3.one;
                    lineGroups[i].alpha = 1f;
                    punchStart[i] = float.NaN;
                }
                else anyRunning = true;
            }
            if (!float.IsNaN(glideStart[i]))
            {
                float age = clock - glideStart[i];
                lineContainers[i].anchoredPosition = Vector2.LerpUnclamped(glideFrom[i], glideTo[i], BigTextTypography.GlideAt(age));
                if (age >= BigTextTypography.LineShiftSeconds)
                {
                    lineContainers[i].anchoredPosition = glideTo[i];
                    glideStart[i] = float.NaN;
                }
                else anyRunning = true;
            }
        }
        if (!anyRunning)
        {
            entranceRunning = false;
            // The slow drift moves the text only — never the full-screen dim.
            StartIdleFloat(linesRoot);
        }
    }

    // The line waits invisible at PunchFromScale until `at`, then appears
    // within a frame and settles to 1.0 with a slight overshoot.
    private void StartPunch(int i, float at)
    {
        lineContainers[i].localEulerAngles = Vector3.zero;
        lineContainers[i].localScale = Vector3.one * BigTextTypography.PunchFromScale;
        lineGroups[i].alpha = 0f;
        punchStart[i] = at;
        punchDrawn[i] = false;
        entranceRunning = true;
    }

    private void StartGlide(int i, Vector2 to)
    {
        glideFrom[i] = lineContainers[i].anchoredPosition;
        glideTo[i] = to;
        glideStart[i] = clock;
        entranceRunning = true;
    }

    /// <summary>Line i's container (self-tests read its scale / alpha / slot).</summary>
    public RectTransform LineContainer(int i) => lineContainers[i];
    public float LineAlpha(int i) => lineGroups[i].alpha;

    // =====================================================================
    // Fit + layout
    // =====================================================================

    private Vector2 FrameSize()
    {
        Rect r = rectTransform.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    // The lines the stack is sized for: what's on screen plus what's coming.
    private List<string> SizingLines()
    {
        var all = new List<string>(MAX_LINES);
        for (int i = 0; i < activeLineCount; i++) all.Add(lineTexts[i].text);
        if (AnticipatedLines != null)
            foreach (string l in AnticipatedLines)
            {
                string t = (l ?? string.Empty).Trim();
                if (t.Length > 0 && !all.Contains(t) && all.Count < MAX_LINES) all.Add(t);
            }
        return all;
    }

    /// <summary>Fits the stack's size, wraps and places every active line.
    /// Returns the slot centre Y of each active line.</summary>
    private List<float> FitAndLayout(bool log)
    {
        Vector2 frame = FrameSize();
        float targetW = frame.x * BigTextTypography.TargetWidthFraction;
        List<string> sizing = SizingLines();

        TMP_FontAsset font = lineTexts[0].font;
        float capRatio = CardTypography.CapHeight(font, 100f) / 100f;

        var widthPer100 = new List<float>(sizing.Count);
        var wordPer100 = new List<float>(sizing.Count);
        foreach (string line in sizing)
        {
            measure.text = line;
            widthPer100.Add(CardTypography.MeasureLineWidth(measure, 100f));
            wordPer100.Add(CardTypography.WidestWordWidth(measure.font, line, 100f));
        }

        BigTextTypography.StackFit fit = BigTextTypography.FitStack(widthPer100, wordPer100,
            (i, size) => RowsAt(sizing[i], size, targetW), capRatio, frame.x, frame.y);
        LastFit = fit;

        bool resized = stackSize > 0f && Mathf.Abs(fit.Size - stackSize) > 0.5f;
        stackSize = fit.Size;
        stackCap = fit.Cap;

        // Place each active line in its own container, cap band centred.
        var heights = new List<float>(activeLineCount);
        for (int i = 0; i < activeLineCount; i++)
            heights.Add(LayoutLine(i, fit.Size, fit.Cap, targetW, frame.x));

        float gap = BigTextTypography.LineGapToCap * fit.Cap;
        float total = 0f;
        foreach (float h in heights) total += h;
        total += Mathf.Max(0, activeLineCount - 1) * gap;

        var slots = new List<float>(activeLineCount);
        float top = total * 0.5f;
        for (int i = 0; i < activeLineCount; i++)
        {
            slots.Add(top - heights[i] * 0.5f);
            top -= heights[i] + gap;
        }

        float k = frame.y / BigTextTypography.ReferenceFrameHeight;
        if (log || resized)
        {
            string first = activeLineCount > 0 ? lineTexts[0].text : "";
            var rows = new System.Text.StringBuilder();
            for (int i = 0; i < activeLineCount; i++) rows.Append(i == 0 ? "" : "+").Append(lineRows[i]);
            Debug.Log($"[BigText] \"{first}\"{(activeLineCount > 1 ? $" (+{activeLineCount - 1})" : "")}: " +
                      $"cap {fit.Cap / k:F0} px (font {fit.Size / k:F0}), rows {rows}, " +
                      $"stack {total / k:F0} of {frame.y * BigTextTypography.TitleSafeFraction / k:F0} px" +
                      (sizing.Count > activeLineCount ? $", sized for {sizing.Count} lines" : "") +
                      (resized ? " — RE-FITTED: a line longer than the ones read ahead arrived" : ""));
        }
        if (log && fit.BelowFloor)
            Debug.LogWarning($"[BigText] \"{lineTexts[0].text}\": cap height {fit.Cap / k:F0} px — under the " +
                             $"{BigTextTypography.CapMinPx:F0} px floor ({(fit.TallerThanSafe ? "stack taller than title-safe" : "a word too long for the line")}).");
        return slots;
    }

    private int RowsAt(string line, float size, float wrapWidth)
    {
        measure.text = line;
        measure.fontSize = size;
        ApplyRowAdvance(measure, size);
        measure.rectTransform.sizeDelta = new Vector2(wrapWidth, 10000f);
        measure.ForceMeshUpdate(true, true);
        return Mathf.Max(1, measure.textInfo.lineCount);
    }

    // TMP's row step is the font's line height + lineSpacing (1/100 em); set it
    // so wrapped rows sit RowAdvanceToCap cap heights apart.
    private static void ApplyRowAdvance(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        float scale = size / Mathf.Max(1f, fi.pointSize) * Mathf.Max(0.0001f, fi.scale);
        float lineH = fi.lineHeight * scale;
        float cap = CardTypography.CapHeight(t.font, size);
        float want = BigTextTypography.RowAdvanceToCap * cap;
        t.lineSpacing = (want - lineH) / (size * 0.01f);
    }

    // Sizes, wraps (balanced) and centres one line; returns its visible height
    // (cap top of the first row to the baseline of the last).
    private float LayoutLine(int i, float size, float cap, float targetW, float frameW)
    {
        TextMeshProUGUI t = lineTexts[i];
        RectTransform container = lineContainers[i];

        t.fontSize = size;
        ApplyRowAdvance(t, size);

        int rows = RowsAt(t.text, size, targetW);
        float wrapW = targetW;
        if (rows > 1)
        {
            // Balanced rows: the narrowest wrap width that keeps the same row
            // count (no long first row over a one-word second row).
            float lo = Mathf.Min(targetW, CardTypography.WidestWordWidth(t.font, t.text, size)), hi = targetW;
            for (int it = 0; it < 14; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (RowsAt(t.text, size, mid) <= rows) hi = mid; else lo = mid;
            }
            wrapW = hi + 2f;
        }
        lineRows[i] = rows;

        RectTransform trt = t.rectTransform;
        trt.localScale = Vector3.one;
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(wrapW, size * 1.5f * rows + size);
        t.ForceMeshUpdate(true, true);

        TMP_TextInfo info = t.textInfo;
        float blockTop = 0f, blockBottom = 0f;
        if (info.lineCount > 0)
        {
            blockTop = info.lineInfo[0].baseline + cap;
            blockBottom = info.lineInfo[info.lineCount - 1].baseline;
        }
        float blockH = Mathf.Max(cap, blockTop - blockBottom);

        // Shift the text so its cap block is centred on the container.
        trt.anchoredPosition = new Vector2(0f, -(blockTop + blockBottom) * 0.5f);
        container.sizeDelta = new Vector2(frameW * BigTextTypography.TitleSafeFraction, blockH);

        // Plate (if the save enables one) hugs the cap block with some air.
        RectTransform bgRT = lineBackgrounds[i].rectTransform;
        float padY = cap * 0.35f, padX = cap * 0.5f;
        float plateHalfW = Mathf.Min(frameW * BigTextTypography.TitleSafeFraction, BlockWidth(t) + 2f * padX) * 0.5f;
        bgRT.anchorMin = bgRT.anchorMax = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta = new Vector2(plateHalfW * 2f, blockH + 2f * padY);
        bgRT.anchoredPosition = Vector2.zero;

        return blockH;
    }

    private static float BlockWidth(TMP_Text t)
    {
        float w = 0f;
        TMP_TextInfo info = t.textInfo;
        for (int l = 0; l < info.lineCount; l++) w = Mathf.Max(w, info.lineInfo[l].width);
        return w;
    }

    // =====================================================================
    // Show / append / hide
    // =====================================================================

    public override void Show()
    {
        KillCurrentSequence();

        // BigText owns its entrance — no preset tilt, root fully visible, lines
        // revealed one by one with the punch.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;
        linesGroup.alpha = 1f;
        dimEnvelope.Open(clock);

        List<float> slots = FitAndLayout(log: false);
        float stagger = BigTextCfg.staggerDelay;

        // '+'-joined lines of one tag land stagger apart; each punch starts
        // the moment its own line appears.
        for (int i = 0; i < activeLineCount; i++)
        {
            glideStart[i] = float.NaN;
            lineContainers[i].anchoredPosition = new Vector2(0f, slots[i]);
            StartPunch(i, clock + stagger * i);
        }
        StepEntrance(); // the first frame already shows the first line's punch start
    }

    /// <summary>
    /// Adds one or more '+'-joined lines to the stack already on screen — the
    /// persistent {BigText:LINE} flow, where each tag lands its line on the
    /// narration beat it sits on. Lines already visible glide to their
    /// re-centred slots while the new lines punch in at theirs. The dim is
    /// untouched. Returns false when nothing fit (the stack is already at
    /// MAX_LINES).
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

        // Lines already up keep any punch they're in and glide to their
        // re-centred slots; the new lines punch in at theirs.
        StopIdleFloat();
        List<float> slots = FitAndLayout(log: false);
        float stagger = BigTextCfg.staggerDelay;

        for (int i = 0; i < activeLineCount; i++)
        {
            Vector2 endPos = new Vector2(0f, slots[i]);
            if (i < firstNew)
            {
                StartGlide(i, endPos);
            }
            else
            {
                glideStart[i] = float.NaN;
                lineContainers[i].anchoredPosition = endPos;
                StartPunch(i, clock + stagger * (i - firstNew));
            }
        }
        StepEntrance();
        return true;
    }

    public override void Hide(bool fast = false)
    {
        dimEnvelope.Close(clock);

        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();
        StopIdleFloat();
        entranceRunning = false; // lines fade from wherever their punch is

        // Text and dim fade out together (the dim on its own envelope).
        currentSequence = DOTween.Sequence()
            .Join(linesGroup.DOFade(0f, BigTextTypography.TextFadeOutSeconds).SetEase(Ease.InQuad))
            .AppendInterval(Mathf.Max(0f, BigTextTypography.DimFadeOutSeconds - BigTextTypography.TextFadeOutSeconds))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    // =====================================================================
    // Visuals-save style
    // =====================================================================

    // Reads VisualsRuntimeApplier.BigText overrides (populated from the
    // active VisualsSave) and applies them to the line's text + background.
    // Touching fontMaterial first forces TMP to clone the shared material so
    // edits only affect this card's text instances. OUTLINE_ON / UNDERLAY_ON
    // are the shader-side toggles that the "Outline" / "Underlay" checkboxes
    // in the TMP material inspector flip.
    //
    // The face is dilated by the outline width: TMP centres an outline on the
    // glyph edge, so without it half the outline ate into the letters (the
    // thin strokes of the old BigText). Now the white keeps the font's full
    // weight and the outline sits entirely outside it.
    private static void ApplyBigTextStyle(TextMeshProUGUI tmp, Image bg)
    {
        Material mat = tmp.fontMaterial;

        tmp.color = VisualsRuntimeApplier.BigText.TextColor ?? Color.white;

        // The weight comes from the heavy face; the save's Bold would add the
        // shader's faux bold on top. Its italic is kept.
        bool italic = VisualsRuntimeApplier.BigText.FontStyle == UnityEngine.FontStyle.Italic ||
                      VisualsRuntimeApplier.BigText.FontStyle == UnityEngine.FontStyle.BoldAndItalic;
        tmp.fontStyle = italic ? FontStyles.Italic : FontStyles.Normal;

        Color outlineColor = VisualsRuntimeApplier.BigText.OutlineColor ?? new Color(0f, 0f, 0f, 0.75f);
        float outlineWidth = Mathf.Clamp01(VisualsRuntimeApplier.BigText.OutlineWidth);
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineWidth", outlineWidth);
        mat.SetFloat("_FaceDilate", outlineWidth);
        tmp.outlineColor = outlineColor;
        tmp.outlineWidth = outlineWidth;

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

        if (bg != null)
        {
            if (VisualsRuntimeApplier.BigText.BackgroundEnabled)
            {
                bg.gameObject.SetActive(true);
                bg.color = VisualsRuntimeApplier.BigText.BackgroundColor;
                int radius = Mathf.Max(0, Mathf.RoundToInt(VisualsRuntimeApplier.BigText.BackgroundCornerRadius));
                if (radius > 0)
                {
                    bg.sprite = StyleSpriteFactory.GetRoundedRect(radius);
                    bg.type = Image.Type.Sliced;
                }
                else
                {
                    bg.sprite = null;
                    bg.type = Image.Type.Simple;
                }
            }
            else
            {
                bg.gameObject.SetActive(false);
            }
        }
    }
}

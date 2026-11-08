using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// The claim against the evidence, full screen: "Binance says / No private
/// key leaked" ≠ "The on-chain record says / Eleven of its own keys signed".
/// Tag: {Versus:"left label","left text","right label","right text",duration|Start[,Vs]}
///      {Versus:End}
///
/// The BigText dim comes up behind two panels side by side (42 % of the width
/// each, in the side cards' background). Each panel: its label in bold small
/// caps (left in slate, right in the card accent) over its text (bold, one
/// size for both panels, 60–96 px, up to 4 lines). The left panel slides in
/// from the left, the right from the right 0.15 s later, then a large ≠ (VS
/// with ",Vs") stamps in on a round accent badge between them with a scale
/// punch. Sizes and timing: <see cref="VersusTypography"/>.
///
/// A fullscreen feature card: shows at any position (Center too), lives in the
/// feature-media zone like BigText, is wiped by a transition and cut by a
/// {Zoom:ExtremeIn}. Every word shown is one of the four fields as written,
/// plus the fixed ≠ / VS.
/// </summary>
public class VersusCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Versus;

    public static Color Slate => CompareCard.Slate;
    static readonly Color BadgeShadowColor = new Color(0f, 0f, 0f, 0.30f);

    private Image dim;
    private RectTransform content;
    private CanvasGroup contentGroup;

    private readonly RectTransform[] panels = new RectTransform[2];
    private readonly CanvasGroup[] panelGroups = new CanvasGroup[2];
    private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] texts = new TextMeshProUGUI[2];

    private RectTransform badge;
    private CanvasGroup badgeGroup;
    private Image badgeFill;
    private TextMeshProUGUI glyph;

    private string[] fields = { "", "", "", "" };
    private bool vsGlyph;

    private BigTextTypography.DimEnvelope dimEnvelope;
    private float clock;                 // scaled time, like DOTween
    private float shownAt = float.NaN;   // NaN until Show
    private float stampStart = float.NaN;
    private bool stampDrawn;
    private bool entranceRunning;

    private readonly Rect[] panelRects = new Rect[2];
    private Rect badgeRect;
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }
    public float TextSize { get; private set; }
    public float DimAlpha => dim != null ? dim.color.a : 0f;
    public BigTextTypography.DimEnvelope Dim => dimEnvelope;
    public float ContentAlpha => contentGroup != null ? contentGroup.alpha : 0f;

    /// <summary>Panel i's rest rect in frame space (y down).</summary>
    public Rect PanelRestRect(int i) => panelRects[i];
    public RectTransform Panel(int i) => panels[i];
    public float PanelAlpha(int i) => panelGroups[i].alpha;
    public RectTransform Badge => badge;
    public float BadgeAlpha => badgeGroup.alpha;
    public Rect BadgeRestRect => badgeRect;
    public TextMeshProUGUI Label(int i) => labels[i];
    public TextMeshProUGUI Text(int i) => texts[i];
    public TextMeshProUGUI Glyph => glyph;

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        dimEnvelope = new BigTextTypography.DimEnvelope(
            BigTextTypography.DimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear));

        // Full-screen dim — first child, so it renders behind everything.
        dim = ContentCardUIBuilder.CreateImage(rectTransform, "Dim", new Color(0f, 0f, 0f, 0f));
        ContentCardUIBuilder.SetStretch(dim.rectTransform, 0f, 0f, 0f, 0f);

        content = ContentCardUIBuilder.CreateChild(rectTransform, "Content");
        contentGroup = EnsureGroup(content);

        for (int i = 0; i < 2; i++)
        {
            GameObject go = new GameObject(i == 0 ? "PanelLeft" : "PanelRight", typeof(RectTransform));
            go.transform.SetParent(content, false);
            RectTransform prt = go.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            panels[i] = prt;
            panelGroups[i] = EnsureGroup(prt);

            ContentCardUIBuilder.CreateBackground(prt);

            labels[i] = ContentCardUIBuilder.CreateText(prt, "Label",
                i == 0 ? Slate : ContentCardUIBuilder.AccentColor, VersusTypography.LabelPx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(labels[i], CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
            labels[i].fontStyle |= FontStyles.SmallCaps;
            labels[i].characterSpacing = VersusTypography.LabelTrackingEm;

            texts[i] = ContentCardUIBuilder.CreateText(prt, "Text",
                ContentCardUIBuilder.TextPrimary, VersusTypography.TextMaxPx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(texts[i], CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
        }

        // The badge last, so it draws over the panels' inner edges.
        GameObject bgo = new GameObject("Badge", typeof(RectTransform));
        bgo.transform.SetParent(content, false);
        badge = bgo.GetComponent<RectTransform>();
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
        badge.pivot = new Vector2(0.5f, 0.5f);
        badgeGroup = EnsureGroup(badge);

        Image shadow = ContentCardUIBuilder.CreateImage(badge, "Shadow", BadgeShadowColor);
        shadow.sprite = ProceduralSprites.Circle(soft: true);
        RectTransform srt = shadow.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(-14f, -20f); srt.offsetMax = new Vector2(14f, 8f);

        badgeFill = ContentCardUIBuilder.CreateImage(badge, "Fill", ContentCardUIBuilder.AccentColor);
        badgeFill.sprite = ProceduralSprites.Circle(soft: false);
        ContentCardUIBuilder.SetStretch(badgeFill.rectTransform, 0f, 0f, 0f, 0f);

        glyph = ContentCardUIBuilder.CreateText(badge, "Glyph", Color.white, 120f, TextAlignmentOptions.Center);
        CardTypography.PrepareText(glyph, CardTypography.Weight.Heavy, TextAlignmentOptions.Center);
        glyph.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform grt = glyph.rectTransform;
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.pivot = new Vector2(0.5f, 0.5f);

        badgeGroup.alpha = 0f;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        fields = data.fields != null && data.fields.Length == 4
            ? data.fields
            : new[] { "", data.primaryText ?? "", "", data.secondaryText ?? "" };
        vsGlyph = data.versusVs;

        labels[0].text = fields[0];
        texts[0].text = fields[1];
        labels[1].text = fields[2];
        texts[1].text = fields[3];
        glyph.text = vsGlyph ? VersusTypography.VsGlyph : VersusTypography.NotEqualGlyph;

        LayoutCard(log: true);
        PoseEntrance(); // off-screen, glyph hidden — until Show
    }

    void LateUpdate()
    {
        if (FrameSize() != laidOutSize)
        {
            LayoutCard(log: false);
            PoseEntrance();
        }
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private Vector2 FrameSize()
    {
        Rect r = rectTransform.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    private void LayoutCard(bool log)
    {
        Vector2 frame = FrameSize();
        laidOutSize = frame;
        Layout = Lay(frame);
        if (log) Layout.LogSummary();
    }

    private CardTypography.Report Lay(Vector2 frame)
    {
        float k = VersusTypography.ScaleFor(frame.y);
        float panelW = frame.x * VersusTypography.PanelWidthFraction;
        float pad = VersusTypography.PanelPaddingPx * k;
        float innerW = panelW - 2f * pad;
        float maxPanelH = frame.y * VersusTypography.TitleSafeFraction;
        var report = new CardTypography.Report
        {
            Card = CardTypography.CardLabel("Versus", fields[1] + " vs " + fields[3]),
        };

        // Labels: fixed size, wrapping at word boundaries if they must.
        float labelSize = VersusTypography.LabelPx * k;
        float labelH = 0f;
        for (int i = 0; i < 2; i++)
            if (labels[i].text.Length > 0)
                labelH = Mathf.Max(labelH, CardTypography.MeasureHeight(labels[i], labelSize, innerW));
        float gap = VersusTypography.LabelToTextGapPx * k;

        // Texts: one size for both panels, the largest at which each is at most
        // four lines and the group fits the title-safe height.
        bool Fits(float s)
        {
            float tallest = 0f;
            for (int i = 0; i < 2; i++)
            {
                if (LinesAt(texts[i], s, innerW) > VersusTypography.TextMaxLines) return false;
                tallest = Mathf.Max(tallest, CardTypography.MeasureHeight(texts[i], s, innerW));
            }
            return labelH + gap + tallest + 2f * pad <= maxPanelH;
        }
        CardTypography.BodyFit fit = CardTypography.FitBody(Fits,
            VersusTypography.TextMinPx * k, VersusTypography.TextMaxPx * k,
            VersusTypography.TextMinPx * k, CardTypography.HardFloorPx * k);
        float textSize = fit.Size;
        TextSize = textSize;
        // Which text is too long, for the warning (measured now: it resizes the
        // texts' boxes, which are placed below). Both are named after it
        // because they share one size.
        string culprit = fit.Overflows || fit.BelowWarnFloor
            ? TooLongAtFloor(innerW, VersusTypography.TextMinPx * k) : null;

        float textH = 0f;
        var textHeights = new float[2];
        for (int i = 0; i < 2; i++)
        {
            textHeights[i] = CardTypography.MeasureHeight(texts[i], textSize, innerW);
            textH = Mathf.Max(textH, textHeights[i]);
        }
        float groupH = labelH + gap + textH;
        float panelH = VersusTypography.PanelHeight(groupH, frame.y);
        // Both groups share one top, so the labels line up across the screen.
        float groupTop = Mathf.Max(pad, (panelH - groupH) * 0.5f);
        report.GroupHeight = groupH;
        report.InnerHeight = panelH - 2f * pad;

        for (int i = 0; i < 2; i++)
        {
            Rect r = VersusTypography.PanelRect(i, frame.x, frame.y, panelH);
            panelRects[i] = r;
            panels[i].sizeDelta = r.size;
            string side = i == 0 ? "left" : "right";

            labels[i].fontSize = labelSize;
            float lh = labels[i].text.Length > 0 ? CardTypography.MeasureHeight(labels[i], labelSize, innerW) : 0f;
            CardTypography.PlaceBox(labels[i], pad, groupTop, innerW, Mathf.Max(lh, 1f));
            report.Add(side + " label", labels[i], VersusTypography.LabelPx, VersusTypography.LabelFloorPx);

            texts[i].fontSize = textSize;
            CardTypography.PlaceBox(texts[i], pad, groupTop + labelH + gap, innerW, Mathf.Max(textHeights[i], 1f));
            report.Add(side + " text", texts[i], textSize / k, VersusTypography.TextMinPx);
        }

        if (fit.Overflows)
            report.Warn($"{report.Card}: {culprit} doesn't fit {VersusTypography.TextMaxLines} lines even at " +
                        $"{CardTypography.HardFloorPx:F0} px — left text and right text shown at {CardTypography.HardFloorPx:F0} px on more lines.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: {culprit} only fits {VersusTypography.TextMaxLines} lines at {textSize / k:F0} px — " +
                        $"left text and right text share that size, under the {VersusTypography.TextMinPx:F0} px floor.");
        if (groupH + 2f * pad > maxPanelH + 0.5f)
            report.Warn($"{report.Card}: the texts are too long for the screen — the panels run past title-safe.");

        LayGlyph(frame);
        return report;
    }

    private string TooLongAtFloor(float innerW, float floorSize)
    {
        bool l = LinesAt(texts[0], floorSize, innerW) > VersusTypography.TextMaxLines;
        bool r = LinesAt(texts[1], floorSize, innerW) > VersusTypography.TextMaxLines;
        return l && r ? "each text" : l ? "the left text" : r ? "the right text" : "the texts' height";
    }

    private static int LinesAt(TMP_Text t, float size, float width)
    {
        if (string.IsNullOrEmpty(t.text)) return 0;
        t.fontSize = size;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, 10000f);
        t.ForceMeshUpdate(true, true);
        return t.textInfo.lineCount;
    }

    // The glyph on its badge: sized from its own ink (≠ sits on the maths axis,
    // VS on the baseline, so neither is centred by the line box), then shifted
    // so the ink's centre is the badge's centre.
    private void LayGlyph(Vector2 frame)
    {
        badgeRect = VersusTypography.BadgeRect(frame.x, frame.y);
        float d = badgeRect.width;
        badge.sizeDelta = new Vector2(d, d);
        badge.localScale = Vector3.one; // measured unscaled; the punch scales it after

        RectTransform grt = glyph.rectTransform;
        grt.sizeDelta = new Vector2(d * 3f, d * 3f);
        grt.anchoredPosition = Vector2.zero;
        glyph.fontSize = 100f;
        Rect ink = CardTypography.InkInCard(glyph, badge);
        float size = 100f;
        if (ink.width > 0f && ink.height > 0f)
        {
            if (vsGlyph)
            {
                float cap = CardTypography.CapHeight(glyph.font, 100f);
                size = 100f * Mathf.Min(d * VersusTypography.VsCapFraction / cap,
                                        d * VersusTypography.VsInkWidthFraction / ink.width);
            }
            else
            {
                size = 100f * Mathf.Min(d * VersusTypography.NotEqualInkFraction / ink.width,
                                        d * VersusTypography.NotEqualInkFraction / ink.height);
            }
        }
        glyph.fontSize = size;
        ink = CardTypography.InkInCard(glyph, badge);
        if (ink.width > 0f)
            grt.anchoredPosition = new Vector2(d * 0.5f - ink.center.x, ink.center.y - d * 0.5f);
    }

    // =====================================================================
    // Entrance (driven per frame on the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        KillCurrentSequence();

        // Versus owns its entrance — no preset tilt, root fully visible; the
        // panels slide and the glyph stamps.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;
        contentGroup.alpha = 1f;
        dimEnvelope.Open(clock);

        shownAt = clock;
        stampStart = clock + VersusTypography.GlyphStart;
        stampDrawn = false;
        entranceRunning = true;
        PoseEntrance();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame: the card clock, the entrance and the dim. Public so
    /// the self-tests can step a card frame by frame in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        if (entranceRunning) PoseEntrance();
        if (dim != null)
        {
            Color c = dim.color;
            c.a = dimEnvelope.AlphaAt(clock);
            dim.color = c;
        }
    }

    // Puts the panels and the glyph where they are `clock` into the entrance
    // (before Show: off-screen and hidden).
    private void PoseEntrance()
    {
        Vector2 frame = laidOutSize;
        CardEntryAnimator anim = CardEntryAnimator.Instance;
        bool done = !float.IsNaN(shownAt);

        for (int i = 0; i < 2; i++)
        {
            Rect r = panelRects[i];
            float off = VersusTypography.OffscreenOffset(i, r, frame.x, frame.y);
            float age = float.IsNaN(shownAt) ? -1f : clock - shownAt - VersusTypography.PanelStart(i);
            float x = Mathf.Clamp01(age / VersusTypography.PanelSlideSeconds);
            float e = age < 0f ? 0f : anim.UseOvershoot
                ? anim.Curve.Evaluate(x)
                : DOVirtual.EasedValue(0f, 1f, x, anim.easedSlideEase);
            float shift = VersusTypography.SlideOffset(e, off, VersusTypography.MaxOvershootPx(frame.x));
            panels[i].anchoredPosition = new Vector2(r.x + shift, -r.y);
            panelGroups[i].alpha = age < 0f ? 0f : anim.UseOvershoot ? 1f : DOVirtual.EasedValue(0f, 1f, x, anim.EntryFadeEase);
            if (x < 1f) done = false;
        }

        badge.anchoredPosition = new Vector2(badgeRect.center.x, -badgeRect.center.y);
        if (float.IsNaN(stampStart) || clock < stampStart)
        {
            badge.localScale = Vector3.one * VersusTypography.StampFromScale;
            badgeGroup.alpha = 0f;
            done = false;
        }
        else
        {
            // Anchor the punch to the first frame the glyph is drawn, so that
            // frame shows the full 1.6× (a beat landing mid-frame no longer
            // eats into the punch before anyone sees it).
            if (!stampDrawn) { stampDrawn = true; stampStart = clock; }
            float age = clock - stampStart;
            badge.localScale = Vector3.one * VersusTypography.StampScaleAt(age);
            badgeGroup.alpha = 1f;
            if (age < VersusTypography.StampSeconds) done = false;
        }

        if (done && entranceRunning)
        {
            entranceRunning = false;
            badge.localScale = Vector3.one;
            // The slow drift moves the panels and glyph only — never the dim.
            StartIdleFloat(content);
        }
    }

    // =====================================================================
    // Hide
    // =====================================================================

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
        entranceRunning = false; // panels fade from wherever they are

        // Panels, glyph and dim fade out together (the dim on its own envelope).
        currentSequence = DOTween.Sequence()
            .Join(contentGroup.DOFade(0f, VersusTypography.ContentFadeOutSeconds).SetEase(Ease.InQuad))
            .AppendInterval(Mathf.Max(0f, BigTextTypography.DimFadeOutSeconds - VersusTypography.ContentFadeOutSeconds))
            .OnComplete(() => OnHideComplete?.Invoke());
    }
}

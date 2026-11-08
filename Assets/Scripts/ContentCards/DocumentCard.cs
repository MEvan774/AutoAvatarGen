using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A filing, complaint, alert or report quoted on screen — on paper, so it no
/// longer looks like a person's quote, and stamped when it is an allegation.
/// Tag: {Document:"passage","phrase to highlight","source",duration|Start[,Allegation|,Filing|,Ruling|,Report][,Left|,Right]}
///      {Document:End}
///
/// Look (sizes and timing: <see cref="DocumentTypography"/>):
///   • warm off-white paper with a fine procedural grain (no texture asset),
///     square corners, a thin header strip carrying the source in Noto Serif
///     Bold small caps;
///   • the passage in Noto Serif, auto-fit 52 → 44 px;
///   • a yellow marker sweeps left to right BEHIND the phrase 0.5 s after the
///     card lands, 0.4 s per line (the phrase must be in the passage exactly,
///     as with Excerpt — otherwise no highlight);
///   • with a keyword, a rubber stamp (fixed words per keyword, worn edges)
///     slams in 0.6 s after the highlight, rotated −10°. It sits in a band
///     under the passage when the passage still fits at ≥ 44 px beside it (it
///     may shrink to make room); otherwise where it covers the least passage
///     text — never the highlight. If no spot avoids the highlight, there is
///     no stamp (warned).
/// Every word shown is one of the three fields as written, or a stamp's fixed words.
/// </summary>
public class DocumentCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Document;

    private Image paper;
    private RawImage grain;
    private Image strip, rule;
    private TextMeshProUGUI header;
    private RectTransform markersRoot;
    private readonly List<Image> markers = new List<Image>();
    private TextMeshProUGUI passage;

    private RectTransform stamp;
    private CanvasGroup stampGroup;
    private Image stampFrame;
    private TextMeshProUGUI stampWord, stampLine;
    private RawImage stampWear;

    private string passageText = "", phrase = "", source = "";
    private DocumentStamp stampKind;

    private float clock;
    private float landAt = float.NaN;
    private float stampDrawnAt = float.NaN;
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    private readonly List<Rect> highlightRects = new List<Rect>();
    private readonly List<Rect> lineInkRects = new List<Rect>();
    private Vector2[] stampPolygon;

    // ---- Read by the self-tests -------------------------------------------------------
    public CardTypography.Report Layout { get; private set; }
    public float BodySize { get; private set; }
    /// <summary>True when the stamp is on the card (a keyword, and a place that keeps it off the highlight).</summary>
    public bool StampPlaced { get; private set; }
    /// <summary>True when the stamp sits in its own band under the passage (covers no text at all).</summary>
    public bool StampInBand { get; private set; }
    /// <summary>True when the stamp had to overlap passage text: it is drawn under the words, in lighter ink.</summary>
    public bool StampBehindText { get; private set; }
    public float StampInk => StampBehindText ? DocumentTypography.StampUnderTextAlpha : DocumentTypography.StampInkAlpha;
    public float StampFitScale { get; private set; } = 1f;
    /// <summary>The stamp at rest, in card space (y down).</summary>
    public Vector2[] StampPolygon => stampPolygon;
    /// <summary>The marker's full extent on each line of the phrase, in card space (y down).</summary>
    public IReadOnlyList<Rect> HighlightRects => highlightRects;
    public IReadOnlyList<Rect> LineInkRects => lineInkRects;
    public float StripBottom { get; private set; }
    public TextMeshProUGUI Passage => passage;
    public TextMeshProUGUI Header => header;
    public TextMeshProUGUI StampWord => stampWord;
    public TextMeshProUGUI StampLine => stampLine;
    public RectTransform Stamp => stamp;
    public float StampAlpha => stampGroup.alpha;
    public Image Marker(int i) => markers[i];
    public int MarkerCount => highlightRects.Count;
    public RectTransform MarkersRoot => markersRoot;

    /// <summary>Starts the highlight / stamp timeline now (self-tests: no slide in edit mode).</summary>
    public void BeginTimelineForTest() { landAt = clock; stampDrawnAt = float.NaN; StepTimeline(); }

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateShadow(rectTransform, 0f);
        paper = ContentCardUIBuilder.CreateImage(rectTransform, "Background", DocumentTypography.Paper);
        ContentCardUIBuilder.SetStretch(paper.rectTransform, 0f, 0f, 0f, 0f);

        grain = new GameObject("Grain", typeof(RectTransform)).AddComponent<RawImage>();
        grain.transform.SetParent(rectTransform, false);
        ContentCardUIBuilder.SetStretch(grain.rectTransform, 0f, 0f, 0f, 0f);
        grain.texture = GrainTexture();
        grain.raycastTarget = false;

        strip = ContentCardUIBuilder.CreateImage(rectTransform, "HeaderStrip", DocumentTypography.HeaderStrip);
        TopBand(strip.rectTransform);
        rule = ContentCardUIBuilder.CreateImage(rectTransform, "HeaderRule", DocumentTypography.HeaderRule);
        TopBand(rule.rectTransform);

        header = ContentCardUIBuilder.CreateText(rectTransform, "Source", DocumentTypography.HeaderInk,
            DocumentTypography.HeaderPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(header, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
        TMP_FontAsset serifBold = CardTypography.NotoSerifBold;
        if (serifBold != null) { header.font = serifBold; header.fontStyle = FontStyles.Normal; }
        header.fontStyle |= FontStyles.SmallCaps;
        header.characterSpacing = DocumentTypography.HeaderTrackingEm;
        header.color = DocumentTypography.HeaderInk;

        // Created before the passage, so the marker sits BEHIND its glyphs.
        markersRoot = ContentCardUIBuilder.CreateChild(rectTransform, "Markers");

        passage = ContentCardUIBuilder.CreateText(rectTransform, "Passage", DocumentTypography.Ink,
            DocumentTypography.BodyMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(passage, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
        TMP_FontAsset serif = CardTypography.NotoSerif;
        if (serif != null) { passage.font = serif; passage.fontStyle = FontStyles.Normal; }
        passage.richText = false; // character i of the text is character i of the layout
        passage.lineSpacing = DocumentTypography.BodyLineSpacing;
        passage.color = DocumentTypography.Ink;

        GameObject sgo = new GameObject("Stamp", typeof(RectTransform));
        sgo.transform.SetParent(rectTransform, false);
        stamp = sgo.GetComponent<RectTransform>();
        stamp.anchorMin = stamp.anchorMax = new Vector2(0f, 1f);
        stamp.pivot = new Vector2(0.5f, 0.5f);
        stampGroup = EnsureGroup(stamp);
        stampGroup.alpha = 0f;

        stampFrame = ContentCardUIBuilder.CreateImage(stamp, "Frame", Color.white);
        stampFrame.sprite = FrameSprite();
        stampFrame.type = Image.Type.Sliced;
        ContentCardUIBuilder.SetStretch(stampFrame.rectTransform, 0f, 0f, 0f, 0f);

        stampWord = ContentCardUIBuilder.CreateText(stamp, "Word", Color.white, DocumentTypography.StampWordPx, TextAlignmentOptions.CenterGeoAligned);
        CardTypography.PrepareText(stampWord, CardTypography.Weight.Heavy, TextAlignmentOptions.CenterGeoAligned);
        stampWord.textWrappingMode = TextWrappingModes.NoWrap;
        stampWord.characterSpacing = DocumentTypography.StampWordTrackingEm;

        stampLine = ContentCardUIBuilder.CreateText(stamp, "Line", Color.white, DocumentTypography.StampLinePx, TextAlignmentOptions.CenterGeoAligned);
        CardTypography.PrepareText(stampLine, CardTypography.Weight.Bold, TextAlignmentOptions.CenterGeoAligned);
        stampLine.textWrappingMode = TextWrappingModes.NoWrap;
        stampLine.characterSpacing = DocumentTypography.StampLineTrackingEm;

        // Paper-coloured specks over the ink: a worn rubber stamp.
        stampWear = new GameObject("Wear", typeof(RectTransform)).AddComponent<RawImage>();
        stampWear.transform.SetParent(stamp, false);
        ContentCardUIBuilder.SetStretch(stampWear.rectTransform, 0f, 0f, 0f, 0f);
        stampWear.texture = WearTexture();
        stampWear.color = DocumentTypography.Paper;
        stampWear.raycastTarget = false;

        stamp.gameObject.SetActive(false);
    }

    private static void TopBand(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        passageText = data.primaryText ?? "";
        phrase = data.secondaryText ?? "";
        source = data.tertiaryText ?? "";
        stampKind = data.documentStamp;

        header.text = source;
        passage.text = passageText;

        string word = DocumentTypography.StampWords(stampKind, out string sub);
        stampWord.text = word ?? "";
        stampLine.text = sub ?? "";
        stampLine.gameObject.SetActive(sub != null);
        Color ink = DocumentTypography.StampColor(stampKind);
        stampFrame.color = ink;
        stampWord.color = ink;
        stampLine.color = ink;

        LayoutCard(log: true);
        StepTimeline();
    }

    void LateUpdate()
    {
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
        {
            LayoutCard(log: false);
            StepTimeline();
        }
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Document", passageText) };

        grain.uvRect = new Rect(0f, 0f, size.x / GrainSize, size.y / GrainSize);

        // ---- Header strip ----
        float hs = DocumentTypography.HeaderPx * k;
        header.fontSize = hs;
        float padY = DocumentTypography.HeaderPadYPx * k;
        float hH = source.Length > 0 ? CardTypography.MeasureHeight(header, hs, innerW) : hs;
        float stripH = hH + 2f * padY;
        strip.rectTransform.sizeDelta = new Vector2(0f, stripH);
        rule.rectTransform.sizeDelta = new Vector2(0f, DocumentTypography.HeaderRulePx * k);
        rule.rectTransform.anchoredPosition = new Vector2(0f, -stripH);
        CardTypography.PlaceBox(header, pad, padY, innerW, hH);
        if (source.Length > 0) report.Add("source", header, DocumentTypography.HeaderPx, DocumentTypography.HeaderFloorPx);
        StripBottom = stripH + DocumentTypography.HeaderRulePx * k;

        Rect area = Rect.MinMaxRect(pad, StripBottom + DocumentTypography.BodyTopGapPx * k, size.x - pad, size.y - pad);

        // ---- Stamp band: under the passage, if the passage still fits at ≥ 44 px ----
        bool wantStamp = stampKind != DocumentStamp.None;
        stamp.gameObject.SetActive(wantStamp); // measured while active
        StampPlaced = false;
        StampInBand = false;
        StampFitScale = 1f;
        Rect passageArea = area;
        Vector2 stampBox = Vector2.zero;
        if (wantStamp)
        {
            for (float s = 1f; s >= MinStampScale() - 1e-4f; s -= 0.05f)
            {
                Vector2 box = StampBoxSize(s, k);
                Rect bb = DocumentTypography.Bounds(DocumentTypography.RotatedBox(Vector2.zero, box, DocumentTypography.StampRotationDegrees));
                float avail = area.height - bb.height - DocumentTypography.StampBandGapPx * k;
                if (bb.width > area.width + 0.5f || avail <= 0f) continue;
                float fitsAt = CardTypography.LargestFitting(
                    sz => CardTypography.MeasureHeight(passage, sz, innerW) <= avail,
                    DocumentTypography.BodyMinPx * k, DocumentTypography.BodyMaxPx * k);
                if (float.IsNaN(fitsAt)) continue;

                StampInBand = true;
                StampFitScale = s;
                stampBox = box;
                passageArea = Rect.MinMaxRect(area.xMin, area.yMin, area.xMax, area.yMax - bb.height - DocumentTypography.StampBandGapPx * k);
                PlaceStamp(new Vector2(area.xMax - bb.width * 0.5f, area.yMax - bb.height * 0.5f), box);
                break;
            }
        }

        // ---- Passage ----
        CardTypography.BodyFit fit = CardTypography.FitBody(
            sz => CardTypography.MeasureHeight(passage, sz, innerW) <= passageArea.height,
            DocumentTypography.BodyMinPx * k, DocumentTypography.BodyMaxPx * k,
            DocumentTypography.BodyMinPx * k, CardTypography.HardFloorPx * k);
        // No band for the stamp: the passage goes no larger than the 44 px
        // floor and to the top of the page, which frees the most paper under
        // it for the stamp to sit on.
        bool stampOverPassage = wantStamp && !StampInBand;
        float bodySize = stampOverPassage ? Mathf.Min(fit.Size, DocumentTypography.BodyMinPx * k) : fit.Size;
        BodySize = bodySize;
        passage.fontSize = bodySize;
        float ph = CardTypography.MeasureHeight(passage, bodySize, innerW);
        float top = stampOverPassage ? passageArea.yMin : passageArea.yMin + Mathf.Max(0f, (passageArea.height - ph) * 0.5f);
        CardTypography.PlaceBox(passage, pad, top, innerW, ph);
        report.Add("passage", passage, bodySize / k, DocumentTypography.BodyMinPx);
        if (fit.Overflows)
            report.Warn($"{report.Card}: the passage doesn't fit the card even at {CardTypography.HardFloorPx:F0} px — it runs past the padding.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: the passage only fits at {fit.Size / k:F0} px — under the {DocumentTypography.BodyMinPx:F0} px floor.");
        report.GroupHeight = ph;
        report.InnerHeight = passageArea.height;

        MeasureTextRects();
        BuildMarkers();

        // ---- Stamp over the passage: where it covers the least text, never the highlight ----
        StampBehindText = false;
        if (stampOverPassage)
        {
            if (!PlaceStampOverPassage(area, k))
                report.Warn($"{report.Card}: no place for the {stampKind} stamp that keeps it off the highlighted phrase — shown without the stamp.");
            else if (StampCoversText())
            {
                // Overprinted: the words stay on top, the ink lighter beneath them.
                StampBehindText = true;
                report.Warn($"{report.Card}: the passage leaves no room beside the {stampKind} stamp — it is printed under " +
                            "part of the passage, in lighter ink (never under the highlight).");
            }
        }
        stamp.SetAsLastSibling();
        if (StampBehindText) stamp.SetSiblingIndex(passage.transform.GetSiblingIndex()); // just under the passage
        stamp.gameObject.SetActive(StampPlaced);
        if (StampPlaced)
        {
            report.Add("stamp", stampWord, stampWord.fontSize / k, DocumentTypography.StampWordMinPx);
            if (stampLine.gameObject.activeSelf)
                report.Add("stamp line", stampLine, stampLine.fontSize / k, DocumentTypography.StampLineFloorPx);
        }

        Layout = report;
        if (log) report.LogSummary();
    }

    private float MinStampScale()
    {
        float s = DocumentTypography.StampWordMinPx / DocumentTypography.StampWordPx;
        if (stampLine.gameObject.activeSelf)
            s = Mathf.Max(s, DocumentTypography.StampLineFloorPx / DocumentTypography.StampLinePx);
        return s;
    }

    // The stamp box at a fit scale: its words' widths and cap heights plus the
    // padding and the double rule.
    private Vector2 StampBoxSize(float s, float k)
    {
        float wordPx = DocumentTypography.StampWordPx * k * s;
        float linePx = DocumentTypography.StampLinePx * k * s;
        stampWord.fontSize = wordPx;
        stampLine.fontSize = linePx;
        float w = CardTypography.MeasureLineWidth(stampWord, wordPx);
        float h = CardTypography.CapHeight(stampWord.font, wordPx);
        if (stampLine.gameObject.activeSelf)
        {
            w = Mathf.Max(w, CardTypography.MeasureLineWidth(stampLine, linePx));
            h += DocumentTypography.StampLineGapPx * k * s + CardTypography.CapHeight(stampLine.font, linePx);
        }
        float frame = (DocumentTypography.StampFramePx) * k;
        return new Vector2(w + 2f * (DocumentTypography.StampPadXPx * k * s + frame),
                           h + 2f * (DocumentTypography.StampPadYPx * k * s + frame));
    }

    // Puts the stamp's box at a centre (card space) and lays its words out
    // inside it, cap blocks centred.
    private void PlaceStamp(Vector2 centre, Vector2 box)
    {
        StampPlaced = true;
        stamp.sizeDelta = box;
        stamp.anchoredPosition = new Vector2(centre.x, -centre.y);
        stamp.localEulerAngles = new Vector3(0f, 0f, DocumentTypography.StampRotationDegrees);
        stampPolygon = DocumentTypography.RotatedBox(centre, box, DocumentTypography.StampRotationDegrees);

        float capW = CardTypography.CapHeight(stampWord.font, stampWord.fontSize);
        float capL = stampLine.gameObject.activeSelf ? CardTypography.CapHeight(stampLine.font, stampLine.fontSize) : 0f;
        float gap = stampLine.gameObject.activeSelf ? DocumentTypography.StampLineGapPx * stampLine.fontSize / DocumentTypography.StampLinePx : 0f;
        float total = capW + gap + capL;
        PlaceStampText(stampWord, box.x, total * 0.5f - capW * 0.5f, capW);
        if (stampLine.gameObject.activeSelf)
            PlaceStampText(stampLine, box.x, -total * 0.5f + capL * 0.5f, capL);
    }

    private static void PlaceStampText(TMP_Text t, float width, float centreY, float cap)
    {
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, cap * 3f);
        rt.anchoredPosition = new Vector2(0f, centreY);
        rt.localEulerAngles = Vector3.zero;
    }

    // Tries spots around the passage area at a few stamp sizes: never one that
    // touches the marker; of the rest, the one covering the least passage ink
    // (a tie goes to the larger stamp).
    private bool PlaceStampOverPassage(Rect area, float k)
    {
        float min = MinStampScale();
        var scales = new[] { 1f, Mathf.Lerp(min, 1f, 0.5f), min };
        float bestScore = float.MaxValue, bestScale = 0f;
        Vector2 bestSpot = Vector2.zero, bestBox = Vector2.zero;
        bool any = false;
        foreach (float s in scales)
        {
            Vector2 box = StampBoxSize(s, k);
            Rect bb = DocumentTypography.Bounds(DocumentTypography.RotatedBox(Vector2.zero, box, DocumentTypography.StampRotationDegrees));
            if (bb.width > area.width + 0.5f || bb.height > area.height + 0.5f) continue;
            float l = area.xMin + bb.width * 0.5f, r = area.xMax - bb.width * 0.5f;
            float t = area.yMin + bb.height * 0.5f, b = area.yMax - bb.height * 0.5f, m = area.center.y;
            var spots = new[] { new Vector2(r, b), new Vector2(l, b), new Vector2(r, t), new Vector2(l, t), new Vector2(r, m), new Vector2(l, m) };
            foreach (Vector2 spot in spots)
            {
                Vector2[] poly = DocumentTypography.RotatedBox(spot, box, DocumentTypography.StampRotationDegrees);
                bool hitsHighlight = false;
                foreach (Rect h in highlightRects)
                    if (DocumentTypography.Overlaps(poly, h)) { hitsHighlight = true; break; }
                if (hitsHighlight) continue;
                float score = InkCovered(poly);
                if (score < bestScore - 0.5f)
                {
                    bestScore = score; bestScale = s; bestSpot = spot; bestBox = box; any = true;
                }
            }
        }
        if (!any) return false;
        StampBoxSize(bestScale, k); // its words at that size
        StampFitScale = bestScale;
        PlaceStamp(bestSpot, bestBox);
        return true;
    }

    // How much passage ink a stamp polygon sits on (its bounds against each line's ink).
    private float InkCovered(Vector2[] poly)
    {
        float score = 0f;
        Rect pb = DocumentTypography.Bounds(poly);
        foreach (Rect ink in lineInkRects)
        {
            if (!DocumentTypography.Overlaps(poly, ink)) continue;
            float ox = Mathf.Min(pb.xMax, ink.xMax) - Mathf.Max(pb.xMin, ink.xMin);
            float oy = Mathf.Min(pb.yMax, ink.yMax) - Mathf.Max(pb.yMin, ink.yMin);
            score += Mathf.Max(1f, Mathf.Max(0f, ox) * Mathf.Max(0f, oy));
        }
        return score;
    }

    private bool StampCoversText()
    {
        foreach (Rect ink in lineInkRects)
            if (DocumentTypography.Overlaps(stampPolygon, ink)) return true;
        return false;
    }

    // Each passage line's ink, and the marker's extent on each line of the
    // phrase — both in card space.
    private void MeasureTextRects()
    {
        lineInkRects.Clear();
        highlightRects.Clear();
        passage.ForceMeshUpdate(true, true);
        TMP_TextInfo info = passage.textInfo;
        float size = passage.fontSize;

        for (int l = 0; l < info.lineCount; l++)
        {
            TMP_LineInfo li = info.lineInfo[l];
            float x0 = float.MaxValue, x1 = float.MinValue;
            for (int c = li.firstCharacterIndex; c <= li.lastCharacterIndex && c < info.characterCount; c++)
            {
                TMP_CharacterInfo ci = info.characterInfo[c];
                if (!ci.isVisible) continue;
                x0 = Mathf.Min(x0, ci.bottomLeft.x);
                x1 = Mathf.Max(x1, ci.topRight.x);
            }
            if (x0 > x1) continue;
            lineInkRects.Add(ToCardRect(new Vector2(x0, li.ascender), new Vector2(x1, li.descender)));
        }

        int start = phrase.Length > 0 ? passageText.IndexOf(phrase, System.StringComparison.Ordinal) : -1;
        if (start < 0) return;
        int end = Mathf.Min(start + phrase.Length, info.characterCount);
        int line = -1;
        float lx0 = 0f, lx1 = 0f, baseline = 0f;
        for (int c = start; c < end; c++)
        {
            TMP_CharacterInfo ci = info.characterInfo[c];
            if (!ci.isVisible) continue;
            if (ci.lineNumber != line)
            {
                if (line >= 0) highlightRects.Add(MarkerRect(lx0, lx1, baseline, size));
                line = ci.lineNumber;
                lx0 = ci.bottomLeft.x;
                baseline = ci.baseLine;
            }
            lx1 = ci.topRight.x;
        }
        if (line >= 0) highlightRects.Add(MarkerRect(lx0, lx1, baseline, size));
    }

    // A marker stroke on one line: from just below the baseline to just above
    // the ascenders, a little past the phrase at each end.
    private Rect MarkerRect(float x0, float x1, float baseline, float size)
    {
        float ext = 0.12f * size;
        return ToCardRect(new Vector2(x0 - ext, baseline + 0.80f * size), new Vector2(x1 + ext, baseline - 0.22f * size));
    }

    // Two corners in the passage's local space → a card-space rect (y down).
    private Rect ToCardRect(Vector2 a, Vector2 b)
    {
        Rect cr = rectTransform.rect;
        Vector3 pa = rectTransform.InverseTransformPoint(passage.rectTransform.TransformPoint(a));
        Vector3 pb = rectTransform.InverseTransformPoint(passage.rectTransform.TransformPoint(b));
        float ax = pa.x - cr.xMin, bx = pb.x - cr.xMin, ay = cr.yMax - pa.y, by = cr.yMax - pb.y;
        return Rect.MinMaxRect(Mathf.Min(ax, bx), Mathf.Min(ay, by), Mathf.Max(ax, bx), Mathf.Max(ay, by));
    }

    private void BuildMarkers()
    {
        while (markers.Count < highlightRects.Count)
        {
            Image m = ContentCardUIBuilder.CreateImage(markersRoot, "Marker" + markers.Count, DocumentTypography.Marker);
            m.sprite = MarkerSprite();
            m.type = Image.Type.Sliced;
            RectTransform rt = m.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            markers.Add(m);
        }
        for (int i = 0; i < markers.Count; i++)
        {
            bool used = i < highlightRects.Count;
            markers[i].gameObject.SetActive(used);
            if (!used) continue;
            Rect r = highlightRects[i];
            markers[i].rectTransform.anchoredPosition = new Vector2(r.xMin, -r.yMin);
            markers[i].rectTransform.sizeDelta = new Vector2(r.width, r.height);
        }
    }

    // =====================================================================
    // Timeline (driven per frame on the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        base.Show();
        landAt = clock + SlideDuration; // the highlight waits for the card to land
        stampDrawnAt = float.NaN;
        StepTimeline();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame; public so the self-tests can step the card in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        StepTimeline();
    }

    private void StepTimeline()
    {
        float since = float.IsNaN(landAt) ? -1f : clock - landAt;
        for (int i = 0; i < highlightRects.Count; i++)
        {
            float p = since < 0f ? 0f : DocumentTypography.LineSweepAt(since, i);
            Image m = markers[i];
            m.enabled = p > 0f;
            Rect r = highlightRects[i];
            float minW = Mathf.Min(r.width, r.height * 0.6f);
            m.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(minW, r.width, p), r.height);
            Color c = DocumentTypography.Marker;
            c.a *= Mathf.Clamp01(p * 8f);
            m.color = c;
        }

        if (!StampPlaced) return;
        float at = DocumentTypography.StampStart(highlightRects.Count);
        if (since < at)
        {
            stampGroup.alpha = 0f;
            stamp.localScale = Vector3.one * DocumentTypography.StampFromScale;
            return;
        }
        // Anchored to the first frame it is drawn, so that frame shows the full 1.5×.
        if (float.IsNaN(stampDrawnAt)) stampDrawnAt = clock;
        stamp.localScale = Vector3.one * DocumentTypography.StampScaleAt(clock - stampDrawnAt);
        stampGroup.alpha = StampInk;
    }

    // =====================================================================
    // Procedural textures — drawn here, no texture assets. Seeded, so every
    // take draws the same paper, marker and stamp wear.
    // =====================================================================

    private const int GrainSize = 256;
    private static Texture2D s_Grain, s_Wear;
    private static Sprite s_Frame, s_Marker;

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // Smooth value noise on a `cell`-pixel lattice that wraps every `period` px.
    private static float ValueNoise(float x, float y, int cell, int period, int seed)
    {
        int n = Mathf.Max(1, period / cell);
        float fx = x / cell, fy = y / cell;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
        float a = Hash(((x0 % n) + n) % n, ((y0 % n) + n) % n, seed);
        float b = Hash((((x0 + 1) % n) + n) % n, ((y0 % n) + n) % n, seed);
        float c = Hash(((x0 % n) + n) % n, (((y0 + 1) % n) + n) % n, seed);
        float d = Hash((((x0 + 1) % n) + n) % n, (((y0 + 1) % n) + n) % n, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static Texture2D NewTexture(int w, int h, TextureWrapMode wrap)
        => new Texture2D(w, h, TextureFormat.RGBA32, false)
           { filterMode = FilterMode.Bilinear, wrapMode = wrap, hideFlags = HideFlags.DontSave };

    // Fine paper grain: soft mottling, faint fibres and the odd fleck, tiling.
    private static Texture2D GrainTexture()
    {
        if (s_Grain != null) return s_Grain;
        const int n = GrainSize;
        var tex = NewTexture(n, n, TextureWrapMode.Repeat);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float mottle = ValueNoise(x, y, 32, n, 11) * 0.6f + ValueNoise(x, y, 8, n, 12) * 0.4f;
                float fibre = ValueNoise(x, y * 6f, 64, n, 13);
                float a = 0.025f * mottle + (fibre > 0.86f ? (fibre - 0.86f) * 0.35f : 0f);
                if (Hash(x, y, 14) > 0.9975f) a += 0.10f;
                float lift = Hash(x, y, 15) * 0.012f;
                px[y * n + x] = new Color32(92, 72, 44, (byte)Mathf.RoundToInt(Mathf.Clamp01(a + lift) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return s_Grain = tex;
    }

    // Where the rubber missed: paper-coloured specks, denser toward the edges.
    private static Texture2D WearTexture()
    {
        if (s_Wear != null) return s_Wear;
        const int w = 512, h = 160;
        var tex = NewTexture(w, h, TextureWrapMode.Clamp);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float edge = Mathf.Min(Mathf.Min(x, w - 1 - x) / (float)w, Mathf.Min(y, h - 1 - y) / (float)h);
                float blotch = ValueNoise(x, y, 18, w * 4, 21) * 0.65f + ValueNoise(x, y, 5, w * 4, 22) * 0.35f;
                float threshold = 0.70f + Mathf.Clamp01(edge * 6f) * 0.10f; // worn edges
                float a = Mathf.Clamp01((blotch - threshold) * 9f);
                if (Hash(x, y, 23) > 0.985f) a = Mathf.Max(a, 0.9f);   // pinholes
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return s_Wear = tex;
    }

    // The stamp's double rule (6 px, 5 px gap, 2 px), as a 9-slice.
    private static Sprite FrameSprite()
    {
        if (s_Frame != null) return s_Frame;
        const int n = 64, border = 20;
        var tex = NewTexture(n, n, TextureWrapMode.Clamp);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Distance in from the nearest edge, with 6 px rounded outer corners.
                float dx = Mathf.Min(x + 0.5f, n - x - 0.5f), dy = Mathf.Min(y + 0.5f, n - y - 0.5f);
                const float r = 6f;
                float d = dx < r && dy < r ? r - Vector2.Distance(new Vector2(dx, dy), new Vector2(r, r)) : Mathf.Min(dx, dy);
                float outer = Mathf.Clamp01(d) * Mathf.Clamp01(6f - d + 0.5f);
                float inner = Mathf.Clamp01(d - 11f + 0.5f) * Mathf.Clamp01(13f - d + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Max(outer, inner) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_Frame = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                new Vector4(border, border, border, border));
        s_Frame.hideFlags = HideFlags.DontSave;
        return s_Frame;
    }

    // A highlighter stroke: slightly ragged top and bottom edges, a chisel tip
    // at each end, faint streaks along it. 9-sliced so the tips keep their shape.
    private static Sprite MarkerSprite()
    {
        if (s_Marker != null) return s_Marker;
        const int w = 128, h = 48, cap = 22;
        var tex = NewTexture(w, h, TextureWrapMode.Clamp);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float top = h - 3f - ValueNoise(x, 0, 6, w * 2, 31) * 3f;
                float bottom = 2f + ValueNoise(x, 7, 6, w * 2, 32) * 3f;
                // Chisel tips: the stroke's ends slant in.
                float tipL = Mathf.Clamp01((x + 0.5f) / cap), tipR = Mathf.Clamp01((w - x - 0.5f) / cap);
                float slantTop = Mathf.Lerp(bottom + 6f, top, Mathf.Sqrt(tipR));
                float slantBottom = Mathf.Lerp(top - 6f, bottom, Mathf.Sqrt(tipL));
                float yy = y + 0.5f;
                float a = Mathf.Clamp01(yy - Mathf.Max(bottom, slantBottom)) * Mathf.Clamp01(Mathf.Min(top, slantTop) - yy);
                a *= 0.90f + 0.10f * ValueNoise(x * 0.25f, y, 3, w, 33);    // streaks
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_Marker = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                 new Vector4(cap, 0, cap, 0));
        s_Marker.hideFlags = HideFlags.DontSave;
        return s_Marker;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MugsTech.Sound;

/// <summary>
/// "Three outlets reported it": press clippings dropped on a pile, full
/// screen, each with its key phrase marked.
/// Tag: {Clipping:"headline","phrase to highlight","source"}   ← the first opens the run
///      {Clipping:"headline","phrase to highlight","source"}   ← each next one lands on top
///      {Clipping:End}
///
/// Look (sizes, timing and pile geometry: <see cref="ClippingTypography"/>):
///   • a backdrop in the side cards' dark panel colour covers the whole frame
///     (and the presenter), like a ,bigCenter headline's panel;
///   • a sheet is the Document card's paper — same colour, grain and marker —
///     76 % of the frame wide and as tall as its content: the headline in Noto
///     Serif Bold (the largest size from 110 px down to 64 that takes at most
///     three lines), a thin rule, the source in small caps;
///   • a sheet drops in from above the frame (0.25 s), tilted a degree or two;
///     the next one lands on it tilted the other way, the ones underneath dim
///     (25 % per layer), and the pile never shows more than three;
///   • the marker sweeps BEHIND the phrase 0.25 s after the sheet lands, 0.3 s
///     per line (the phrase must be in the headline exactly — otherwise no
///     highlight, and one warning);
///   • the top sheet pushes in slowly (1.2 % a second, at most 1.08×);
///   • {Mark:"phrase"} moves the marker on, on the top sheet: the earlier
///     highlight fades to a pale tint and stays.
///
/// A fullscreen feature card: the root is stretched over the frame and never
/// slides. The run's clockwork is <see cref="ClippingTypography.Pile"/> (pure);
/// this class measures the text with TMP and poses its objects from that clock
/// every frame (<see cref="Advance"/>) — no chained tweens. Every word shown is
/// one of the three fields as written.
/// </summary>
public class ClippingCard : ContentCard, IMarkable
{
    protected override ContentCardType CardType => ContentCardType.Clipping;

    // One marked phrase on a sheet: the tag's own, or a later {Mark}.
    private sealed class Highlight
    {
        public string Phrase;
        public int Start;                    // where the phrase begins in the headline
        public bool Own;                     // the tag's own highlight: it waits for the sheet to land
        public float StartAt = float.NaN;    // a mark: the card clock when it was asked for
        public float PaleAt = float.NaN;     // the card clock when a later mark took the marker on
        public readonly List<Rect> Rects = new List<Rect>();      // one per line, sheet space (y down)
        public readonly List<Image> Strokes = new List<Image>();  // one per rect, from the sheet's pool
    }

    private sealed class Sheet
    {
        public RectTransform Root;
        public Image Paper;
        public RawImage Grain;
        public RectTransform MarkersRoot;
        public TextMeshProUGUI Headline, Source;
        public Image Rule, Dim;
        public readonly List<Image> StrokePool = new List<Image>();
        public readonly List<Highlight> Highlights = new List<Highlight>();

        public string HeadlineText = "", PhraseText = "", SourceText = "";
        public bool PhraseFound;
        public int Marks;
        public float Tilt;
        public Vector2 Size;                      // frame px
        public float HeadlineLeft, HeadlineTop;   // the headline box's top-left corner, sheet space
        public float HeadlineSize;                // frame px
        public int HeadlineLines;
        public CardTypography.Report Report;
    }

    private Image backdrop;
    private RectTransform pile;
    private CanvasGroup pileGroup;
    private readonly Sheet[] sheets = new Sheet[ClippingTypography.MaxSheets];

    // The clock and every sheet's timing (scaled time, like DOTween).
    private readonly ClippingTypography.Pile run = new ClippingTypography.Pile();
    private int startedFrame = -1;       // play mode: the frame something last began in
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    // ---- The integrator's API ---------------------------------------------------------

    /// <summary>Sheets added so far (1–5), hidden ones included.</summary>
    public int SheetCount => run.Count;
    /// <summary>Sheets in the pile — the newest three at most. (For the quarter
    /// second a 4th or 5th takes to land, the one it buries is still drawn
    /// under it: <see cref="DrawnSheetCount"/>.)</summary>
    public int VisibleSheetCount => ClippingTypography.VisibleCount(run.Count);
    /// <summary>Seconds since the top sheet started landing, on the card's clock
    /// (0 while it still waits for Show; 0.25 for one that landed "instant").</summary>
    public float TopSheetAge => run.TopAge;
    /// <summary>Seconds the top sheet has been at rest (0 while it waits or is still
    /// dropping) — what <see cref="ClippingTypography.MinReadSeconds"/> is about.</summary>
    public float TopSheetLandedSeconds => Mathf.Max(0f, run.TopAge - ClippingTypography.DropSeconds);
    /// <summary>The top sheet's phrase was found in its headline (false for an empty phrase too).</summary>
    public bool TopPhraseFound => run.Count > 0 && sheets[run.Count - 1].PhraseFound;
    /// <summary>True once the run is on its way out (its End, or a fast hide):
    /// a {Clipping} arriving now belongs to the next run, not this one.</summary>
    public bool IsClosing { get; private set; }
    /// <summary>The TOP sheet's last layout (elements "headline" and "source"; warnings).</summary>
    public CardTypography.Report Layout => run.Count > 0 ? sheets[run.Count - 1].Report : null;

    // ---- Read by the self-tests -------------------------------------------------------

    /// <summary>Sheet objects active right now (one more than the pile while a 4th or 5th is landing).</summary>
    public int DrawnSheetCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < run.Count; i++) if (sheets[i].Root.gameObject.activeSelf) n++;
            return n;
        }
    }
    public Image Backdrop => backdrop;
    public float BackdropAlpha => backdrop != null ? backdrop.color.a : 0f;
    public RectTransform Pile => pile;
    public float PileAlpha => pileGroup != null ? pileGroup.alpha : 0f;
    public RectTransform SheetRoot(int i) => sheets[i].Root;
    public bool SheetDrawn(int i) => i < run.Count && sheets[i].Root.gameObject.activeSelf;
    public bool SheetLanded(int i) => run.Landed(i);
    /// <summary>Sheet i at rest, before its tilt, in frame space (y down).</summary>
    public Rect SheetRestRect(int i) => ClippingTypography.SheetRect(laidOutSize.x, laidOutSize.y, sheets[i].Size.y);
    public float SheetTilt(int i) => sheets[i].Tilt;
    public float SheetScale(int i) => sheets[i].Root.localScale.y;
    /// <summary>The shade over sheet i (0 = none).</summary>
    public float SheetDim(int i) => sheets[i].Dim.enabled ? sheets[i].Dim.color.a : 0f;
    public Image Paper(int i) => sheets[i].Paper;
    public RawImage Grain(int i) => sheets[i].Grain;
    public TextMeshProUGUI Headline(int i) => sheets[i].Headline;
    public TextMeshProUGUI Source(int i) => sheets[i].Source;
    public Image Rule(int i) => sheets[i].Rule;
    public RectTransform MarkersRoot(int i) => sheets[i].MarkersRoot;
    /// <summary>Sheet i's headline size in px of a 1080-px-tall frame.</summary>
    public float HeadlineSize(int i) => sheets[i].HeadlineSize / ClippingTypography.ScaleFor(laidOutSize.y);
    public int HeadlineLines(int i) => sheets[i].HeadlineLines;
    public bool SheetPhraseFound(int i) => sheets[i].PhraseFound;
    public CardTypography.Report SheetLayout(int i) => i < run.Count ? sheets[i].Report : null;
    /// <summary>Marked phrases on sheet i: the tag's own (when found) plus its marks.</summary>
    public int HighlightCount(int i) => sheets[i].Highlights.Count;
    /// <summary>{Mark}s taken by sheet i (0–3).</summary>
    public int MarkCount(int i) => sheets[i].Marks;
    public string HighlightPhrase(int i, int h) => sheets[i].Highlights[h].Phrase;
    /// <summary>The marker's full extent on each line of highlight h of sheet i, in sheet space (y down).</summary>
    public IReadOnlyList<Rect> HighlightRects(int i, int h) => sheets[i].Highlights[h].Rects;
    public Image Stroke(int i, int h, int line) => sheets[i].Highlights[h].Strokes[line];

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        // The backdrop first, so it renders behind the pile. It covers the
        // whole frame — presenter included — once it has faded in.
        backdrop = ContentCardUIBuilder.CreateImage(rectTransform, "Backdrop", WithAlpha(ClippingTypography.Backdrop, 0f));
        ContentCardUIBuilder.SetStretch(backdrop.rectTransform, 0f, 0f, 0f, 0f);

        pile = ContentCardUIBuilder.CreateChild(rectTransform, "Pile");
        pileGroup = EnsureGroup(pile);

        // All five sheets up front (hidden), in pile order: nothing is built
        // mid-take, and sheet i+1 always draws over sheet i.
        for (int i = 0; i < sheets.Length; i++) sheets[i] = BuildSheet(i);
    }

    private Sheet BuildSheet(int index)
    {
        var s = new Sheet();
        GameObject go = new GameObject("Sheet" + index, typeof(RectTransform));
        go.transform.SetParent(pile, false);
        s.Root = go.GetComponent<RectTransform>();
        // Centred on the frame, turning and growing about its own middle.
        s.Root.anchorMin = s.Root.anchorMax = new Vector2(0.5f, 0.5f);
        s.Root.pivot = new Vector2(0.5f, 0.5f);

        ContentCardUIBuilder.CreateShadow(s.Root, 0f);
        s.Paper = ContentCardUIBuilder.CreateImage(s.Root, "Paper", ClippingTypography.Paper);
        ContentCardUIBuilder.SetStretch(s.Paper.rectTransform, 0f, 0f, 0f, 0f);

        s.Grain = new GameObject("Grain", typeof(RectTransform)).AddComponent<RawImage>();
        s.Grain.transform.SetParent(s.Root, false);
        ContentCardUIBuilder.SetStretch(s.Grain.rectTransform, 0f, 0f, 0f, 0f);
        s.Grain.texture = DocumentCard.PaperGrain();
        s.Grain.raycastTarget = false;

        // Created before the headline, so the marker sits BEHIND its glyphs.
        s.MarkersRoot = ContentCardUIBuilder.CreateChild(s.Root, "Markers");

        TMP_FontAsset serifBold = CardTypography.NotoSerifBold;

        s.Headline = ContentCardUIBuilder.CreateText(s.Root, "Headline", ClippingTypography.Ink,
            ClippingTypography.HeadlineMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(s.Headline, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
        if (serifBold != null) { s.Headline.font = serifBold; s.Headline.fontStyle = FontStyles.Normal; }
        s.Headline.richText = false;            // character i of the text is character i of the layout
        s.Headline.parseCtrlCharacters = false; // …a typed "\n" stays two characters too
        s.Headline.lineSpacing = ClippingTypography.HeadlineLineSpacing;
        s.Headline.color = ClippingTypography.Ink;

        s.Rule = ContentCardUIBuilder.CreateImage(s.Root, "Rule", ClippingTypography.Rule);
        RectTransform rrt = s.Rule.rectTransform;
        rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
        rrt.pivot = new Vector2(0f, 1f);

        s.Source = ContentCardUIBuilder.CreateText(s.Root, "Source", ClippingTypography.SourceInk,
            ClippingTypography.SourcePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(s.Source, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
        if (serifBold != null) { s.Source.font = serifBold; s.Source.fontStyle = FontStyles.Normal; }
        s.Source.fontStyle |= FontStyles.SmallCaps;
        s.Source.richText = false;              // the source as written, angle brackets and all
        s.Source.parseCtrlCharacters = false;
        s.Source.characterSpacing = ClippingTypography.SourceTrackingEm;
        s.Source.color = ClippingTypography.SourceInk;

        // Last, so it shades the paper, the marker and the words alike.
        s.Dim = ContentCardUIBuilder.CreateImage(s.Root, "Dim", WithAlpha(ClippingTypography.Shade, 0f));
        ContentCardUIBuilder.SetStretch(s.Dim.rectTransform, 0f, 0f, 0f, 0f);
        s.Dim.enabled = false;

        go.SetActive(false);
        return s;
    }

    private Image NewStroke(Sheet s)
    {
        Image m = ContentCardUIBuilder.CreateImage(s.MarkersRoot, "Marker" + s.StrokePool.Count, ClippingTypography.Marker);
        m.sprite = DocumentCard.MarkerStrokeSprite();
        m.type = Image.Type.Sliced;
        RectTransform rt = m.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        m.enabled = false;
        s.StrokePool.Add(m);
        return m;
    }

    // =====================================================================
    // The run: first sheet, next sheets, marks
    // =====================================================================

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        // A fresh run (Initialize is called once per card; this keeps a second call sane).
        for (int i = 0; i < run.Count; i++) sheets[i].Root.gameObject.SetActive(false);
        run.Reset();
        IsClosing = false;

        // The first sheet. It waits above the frame until Show.
        AppendSheet(data.primaryText, data.secondaryText, data.tertiaryText);
    }

    /// <summary>
    /// Lands the next clipping on the pile: it drops in from above the frame,
    /// tilted the other way from the one it covers, which dims.
    /// <paramref name="instant"/> lands it at once, its highlight already drawn
    /// (a sheet whose beat passed while the card waited in the queue). Before
    /// Show, a sheet simply joins the pile that Show drops in. False — and
    /// nothing is added — when the run already holds
    /// <see cref="ClippingTypography.MaxSheets"/> sheets (the caller logs).
    /// </summary>
    public bool AppendSheet(string headline, string phrase, string source, bool instant = false)
    {
        if (run.Count >= ClippingTypography.MaxSheets) return false;

        Sheet s = sheets[run.Count];
        s.HeadlineText = headline ?? string.Empty;
        s.PhraseText = phrase ?? string.Empty;
        s.SourceText = source ?? string.Empty;
        s.Tilt = ClippingTypography.Tilt(run.Count);
        s.Marks = 0;
        s.Highlights.Clear();

        int at = ClippingTypography.FindPhrase(s.HeadlineText, s.PhraseText);
        s.PhraseFound = at >= 0;
        if (s.PhraseFound)
            s.Highlights.Add(new Highlight { Phrase = s.PhraseText, Start = at, Own = true });
        else if (!string.IsNullOrWhiteSpace(s.PhraseText))
            Debug.LogWarning(ClippingTypography.PhraseWarning(s.HeadlineText, s.PhraseText));

        s.Headline.text = s.HeadlineText;
        s.Source.text = s.SourceText;
        s.Root.gameObject.SetActive(true); // measured while active

        // On the clock: it waits (before Show), starts landing now, or has just landed.
        run.Add(instant, out bool startsLanding);

        Vector2 frame = FrameSize();
        if (frame != laidOutSize) LayoutAll();
        else LaySheet(s, frame);
        s.Report.LogSummary();

        if (startsLanding)
        {
            SoundLayer.Fire(SfxKind.Paper);
            NoteStart();
        }
        Step();
        return true;
    }

    /// <summary>
    /// {Mark:"phrase"} on the TOP sheet: the marker sweeps the new phrase at
    /// once (0.3 s per line; never before the sheet has landed) and the sheet's
    /// earlier highlights fade to a pale tint and stay. NotFound (the phrase
    /// isn't in the top headline exactly), Repeated (it is already marked
    /// there) and TooMany (the sheet has its three marks) change nothing. The
    /// caller logs.
    /// </summary>
    public MarkResult AddMark(string phrase)
    {
        if (run.Count == 0) return MarkResult.NotFound;
        Sheet s = sheets[run.Count - 1];

        var marked = new List<string>(s.Highlights.Count);
        foreach (Highlight h in s.Highlights) marked.Add(h.Phrase);
        MarkResult verdict = ClippingTypography.JudgeMark(s.HeadlineText, phrase, marked, s.Marks);
        if (verdict != MarkResult.Ok) return verdict;

        foreach (Highlight h in s.Highlights)
            if (float.IsNaN(h.PaleAt)) h.PaleAt = run.Clock;
        s.Highlights.Add(new Highlight
        {
            Phrase = phrase,
            Start = ClippingTypography.FindPhrase(s.HeadlineText, phrase),
            Own = false,
            StartAt = run.Clock,
        });
        s.Marks++;

        MeasureHighlights(s);
        BuildStrokes(s);
        NoteStart();
        Step();
        return MarkResult.Ok;
    }

    void LateUpdate() => RefreshLayoutIfResized();

    /// <summary>Lays every sheet out again when the frame's size has changed
    /// (LateUpdate calls it; public so the self-tests can, in edit mode).</summary>
    public void RefreshLayoutIfResized()
    {
        if (run.Count == 0 || FrameSize() == laidOutSize) return;
        LayoutAll();
        Step();
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private Vector2 FrameSize()
    {
        Rect r = rectTransform.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    private void LayoutAll()
    {
        Vector2 frame = FrameSize();
        laidOutSize = frame;
        // A sheet that has come off the pile never returns: it keeps its old layout.
        for (int i = 0; i < run.Count; i++)
            if (run.Drawn(i)) LaySheet(sheets[i], frame);
    }

    // One sheet: the source at its fixed size, the headline at the largest
    // size that takes at most three lines, the sheet as tall as the two need.
    // Everything inside it is placed from its top-left corner (sheet space).
    private void LaySheet(Sheet s, Vector2 frame)
    {
        laidOutSize = frame;
        float k = ClippingTypography.ScaleFor(frame.y);
        float sheetW = ClippingTypography.SheetWidth(frame.x);
        float padX = ClippingTypography.SheetPadXPx * k;
        float padTop = ClippingTypography.SheetPadTopPx * k;
        float padBottom = ClippingTypography.SheetPadBottomPx * k;
        float innerW = sheetW - 2f * padX;
        float maxH = ClippingTypography.SheetMaxHeight(frame.y);
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Clipping", s.HeadlineText) };

        // ---- Source (with its rule): fixed size, wrapping if it must ----
        bool hasSource = s.SourceText.Length > 0;
        s.Source.gameObject.SetActive(hasSource);
        s.Rule.gameObject.SetActive(hasSource);
        float sourceSize = ClippingTypography.SourcePx * k;
        float sourceH = hasSource ? CardTypography.MeasureHeight(s.Source, sourceSize, innerW) : 0f;
        float ruleH = ClippingTypography.RulePx * k;
        float under = hasSource
            ? ClippingTypography.HeadlineToRuleGapPx * k + ruleH + ClippingTypography.RuleToSourceGapPx * k + sourceH
            : 0f;

        // ---- Headline: the largest size at which it takes at most three lines ----
        string text = s.HeadlineText;
        TMP_FontAsset font = s.Headline.font;
        bool Fits(float size) =>
            LinesAt(s.Headline, size, innerW) <= ClippingTypography.HeadlineMaxLines &&
            CardTypography.WidestWordWidth(font, text, size) <= innerW &&
            padTop + CardTypography.MeasureHeight(s.Headline, size, innerW) + under + padBottom <= maxH + 0.5f;
        CardTypography.BodyFit fit = ClippingTypography.FitHeadline(Fits, k);

        float headSize = fit.Size;
        float headH = text.Length > 0 ? CardTypography.MeasureHeight(s.Headline, headSize, innerW) : 0f;
        s.Headline.fontSize = headSize;
        s.HeadlineSize = headSize;
        s.HeadlineLeft = padX;
        s.HeadlineTop = padTop;

        float contentH = padTop + headH + under + padBottom;
        float sheetH = ClippingTypography.SheetHeight(contentH, frame.y);
        s.Size = new Vector2(sheetW, sheetH);
        s.Root.sizeDelta = s.Size;
        s.Grain.uvRect = new Rect(0f, 0f, sheetW / DocumentCard.PaperGrainSize, sheetH / DocumentCard.PaperGrainSize);

        CardTypography.PlaceBox(s.Headline, padX, padTop, innerW, Mathf.Max(headH, 1f));
        report.Add("headline", s.Headline, headSize / k, ClippingTypography.HeadlineMinPx);
        if (fit.Overflows)
            report.Warn($"{report.Card}: the headline doesn't fit {ClippingTypography.HeadlineMaxLines} lines even at " +
                        $"{CardTypography.HardFloorPx:F0} px — shown at {CardTypography.HardFloorPx:F0} px on more lines.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: the headline only fits {ClippingTypography.HeadlineMaxLines} lines at {headSize / k:F0} px — " +
                        $"under the {ClippingTypography.HeadlineMinPx:F0} px floor.");
        if (contentH > maxH + 0.5f)
            report.Warn($"{report.Card}: the headline and source are taller than the sheet — the text runs past its padding.");

        if (hasSource)
        {
            float y = padTop + headH + ClippingTypography.HeadlineToRuleGapPx * k;
            RectTransform rrt = s.Rule.rectTransform;
            rrt.anchoredPosition = new Vector2(padX, -y);
            rrt.sizeDelta = new Vector2(innerW, ruleH);
            y += ruleH + ClippingTypography.RuleToSourceGapPx * k;
            s.Source.fontSize = sourceSize;
            CardTypography.PlaceBox(s.Source, padX, y, innerW, sourceH);
            report.Add("source", s.Source, ClippingTypography.SourcePx, ClippingTypography.SourceFloorPx);
        }
        report.GroupHeight = contentH - padTop - padBottom;
        report.InnerHeight = maxH - padTop - padBottom;
        s.Report = report;

        MeasureHighlights(s);
        BuildStrokes(s);
    }

    // How many lines a text takes at a size and width (it resizes the text's
    // box; the caller places it afterwards).
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

    // The marker's extent on each line of each marked phrase, in sheet space.
    // The headline's box hangs from its top-left corner (pivot 0,1), so TMP's
    // character positions are already measured from that corner: x to the
    // right, y up (negative down the box).
    private void MeasureHighlights(Sheet s)
    {
        foreach (Highlight h in s.Highlights) h.Rects.Clear();
        s.HeadlineLines = 0;
        if (s.HeadlineText.Length == 0) return;

        s.Headline.ForceMeshUpdate(true, true);
        TMP_TextInfo info = s.Headline.textInfo;
        s.HeadlineLines = info.lineCount;
        float size = s.Headline.fontSize;

        foreach (Highlight h in s.Highlights)
        {
            int end = Mathf.Min(h.Start + h.Phrase.Length, info.characterCount);
            int line = -1;
            float x0 = 0f, x1 = 0f, baseline = 0f;
            for (int c = h.Start; c < end; c++)
            {
                TMP_CharacterInfo ci = info.characterInfo[c];
                if (!ci.isVisible) continue;
                if (ci.lineNumber != line)
                {
                    if (line >= 0)
                        h.Rects.Add(ClippingTypography.MarkerRect(s.HeadlineLeft, s.HeadlineTop, x0, x1, baseline, size));
                    line = ci.lineNumber;
                    x0 = ci.bottomLeft.x;
                    baseline = ci.baseLine;
                }
                x1 = ci.topRight.x;
            }
            if (line >= 0)
                h.Rects.Add(ClippingTypography.MarkerRect(s.HeadlineLeft, s.HeadlineTop, x0, x1, baseline, size));
        }
    }

    // One stroke per rect, handed out from the sheet's pool in highlight
    // order — so a later mark draws over an earlier, paler one.
    private void BuildStrokes(Sheet s)
    {
        int used = 0;
        float spriteH = DocumentCard.MarkerStrokeSprite().rect.height;
        foreach (Highlight h in s.Highlights)
        {
            h.Strokes.Clear();
            foreach (Rect r in h.Rects)
            {
                Image m = used < s.StrokePool.Count ? s.StrokePool[used] : NewStroke(s);
                used++;
                m.gameObject.SetActive(true);
                m.rectTransform.anchoredPosition = new Vector2(r.xMin, -r.yMin);
                m.rectTransform.sizeDelta = new Vector2(r.width, r.height);
                // The sprite's chisel tips are 9-slice caps of a fixed width;
                // scaled with the stroke's height they keep their shape on a
                // 110 px headline as on a 52 px passage.
                if (r.height > 0.01f) m.pixelsPerUnitMultiplier = spriteH / r.height;
                h.Strokes.Add(m);
            }
        }
        for (int i = used; i < s.StrokePool.Count; i++) s.StrokePool[i].gameObject.SetActive(false);
    }

    // =====================================================================
    // Show / per frame (the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        KillCurrentSequence();

        // The clipping owns its entrance — no slide of the whole card, no
        // preset tilt on the root; the root is fully visible and the backdrop
        // and the sheets do the arriving. (Sheets added before Show drop in as
        // one pile.)
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        if (run.Show()) SoundLayer.Fire(SfxKind.Paper);
        NoteStart();
        Step(); // the start pose: backdrop clear, the sheet above the frame
    }

    void Update()
    {
        float dt = Time.deltaTime;
        // Something began earlier in this very frame (the controller's Update
        // ran before this one) and was posed at its start. Carry those starts
        // along with the clock, so this frame — the first it is drawn in —
        // still shows the start pose and the next one is dt into it.
        if (startedFrame == Time.frameCount) Reanchor(run.Clock, run.Clock + dt);
        startedFrame = -1;
        Advance(dt);
    }

    /// <summary>One frame: the card clock, then every sheet, stroke, the
    /// backdrop and the lift posed for it. Public so the self-tests can step a
    /// card frame by frame in edit mode.</summary>
    public void Advance(float dt)
    {
        run.Advance(dt);
        Step();
        if (run.TakeLiftDone()) OnHideComplete?.Invoke(); // last: the controller destroys the card from it
    }

    private void NoteStart()
    {
        if (Application.isPlaying) startedFrame = Time.frameCount;
    }

    // Every start stamped at `from` (the clock when this frame began) moves to `to`.
    private void Reanchor(float from, float to)
    {
        run.Reanchor(from, to);
        for (int i = 0; i < run.Count; i++)
            foreach (Highlight h in sheets[i].Highlights)
            {
                ClippingTypography.Shift(ref h.StartAt, from, to);
                ClippingTypography.Shift(ref h.PaleAt, from, to);
            }
    }

    // Poses everything for the clock as it stands.
    private void Step()
    {
        if (backdrop == null) return;
        Vector2 frame = laidOutSize;

        for (int i = 0; i < run.Count; i++)
        {
            Sheet s = sheets[i];

            // A sheet comes off the pile once three have landed on it.
            bool drawn = run.Drawn(i);
            if (s.Root.gameObject.activeSelf != drawn) s.Root.gameObject.SetActive(drawn);
            if (!drawn) continue;

            // The drop: straight down from above the frame onto the pile's centre.
            float wait = ClippingTypography.DropOffset(s.Size.x, s.Size.y, s.Tilt, frame.y);
            s.Root.anchoredPosition = new Vector2(0f, wait * (1f - run.DropProgress(i)));
            s.Root.localEulerAngles = new Vector3(0f, 0f, s.Tilt);

            // The top sheet pushes in; a covered one eases back to 1.0 as its cover lands.
            float scale = run.Scale(i);
            s.Root.localScale = new Vector3(scale, scale, 1f);

            // The sheets lying on it shade it, each as it lands.
            float dim = run.Dim(i);
            s.Dim.enabled = dim > 0.001f;
            s.Dim.color = WithAlpha(ClippingTypography.Shade, dim);

            StepStrokes(s, i);
        }

        // The backdrop fades in with Show; on the way out it fades with the
        // pile, which lifts off through the top of the frame.
        pile.anchoredPosition = new Vector2(0f, run.Lift * ClippingTypography.LiftDistance(frame.y));
        pileGroup.alpha = run.PileAlpha;
        backdrop.color = WithAlpha(ClippingTypography.Backdrop, run.BackdropAlpha);
    }

    // A sheet's strokes: each line's stroke grows from the phrase's left end
    // (ClippingTypography.StrokeAge says how far into its sweep a highlight is).
    private void StepStrokes(Sheet s, int index)
    {
        float sinceLanding = run.SinceLanding(index);
        bool instant = run.IsInstant(index);
        foreach (Highlight h in s.Highlights)
        {
            float since = ClippingTypography.StrokeAge(h.Own, instant, sinceLanding, h.Own ? 0f : run.Clock - h.StartAt);
            float tint = ClippingTypography.TintAt(float.IsNaN(h.PaleAt) ? -1f : run.Clock - h.PaleAt);

            int n = Mathf.Min(h.Rects.Count, h.Strokes.Count);
            for (int line = 0; line < n; line++)
            {
                float p = ClippingTypography.SweepAt(since, line);
                Rect r = h.Rects[line];
                Image m = h.Strokes[line];
                m.enabled = p > 0f;
                m.rectTransform.sizeDelta = new Vector2(ClippingTypography.StrokeWidth(r.width, r.height, p), r.height);
                m.color = ClippingTypography.StrokeColor(p, tint);
            }
        }
    }

    // =====================================================================
    // Hide
    // =====================================================================

    public override void Hide(bool fast = false)
    {
        IsClosing = true;

        if (fast)
        {
            // A transition or the next card: a plain quick fade of the whole
            // card. It reports through its own tween, not through the lift.
            run.MuteLiftReport();
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence(); // a fast fade under way reports through the lift instead
        StopIdleFloat();

        // {Clipping:End}: the pile lifts away and the backdrop fades with it,
        // on the card's clock; Advance reports OnHideComplete when both are
        // gone. Asked again while lifting, it doesn't start over.
        bool starting = !run.IsLifting;
        run.StartLift();
        if (!starting) return;
        NoteStart();
        Step();
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}

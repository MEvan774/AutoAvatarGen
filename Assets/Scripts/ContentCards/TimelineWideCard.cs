using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// A sequence read left to right, full screen — "five browsers in six months",
/// drawn.
/// Tag: {Timeline:"date","event",Wide}   ← opens the card with its first node
///      {Timeline:"date","event"}        ← each next one adds a node on its word
///      {Timeline:Span,"text"}           ← optional: a bracket under the run and a label
///      {Timeline:End}
///
/// The BigText dim comes up and the timeline is drawn straight on it — no
/// panel. One line crosses 80 % of the frame at mid height; the nodes are
/// evenly spaced along it IN SCRIPT ORDER (never from their dates) and the
/// whole run is known when the card opens (<see cref="AnticipatedNodes"/>), so
/// every node has its place from the start and nothing re-flows. On its word a
/// node's stretch of line draws (0.3 s), its dot pops and its label slides in:
/// the date in bold small caps over the event in bold (one size for every
/// node, 56 → 40 px, up to two lines), above the line for the 1st / 3rd / 5th
/// node and below for the 2nd / 4th / 6th, centred on the node. The newest
/// node holds the accent (dot and date); earlier ones settle to 70 %.
///
/// Dated or numbered: a first field of one or two characters is drawn as a
/// white numeral inside an enlarged dot and its event stands alone; anything
/// longer is the date label. The field is shown as written and never parsed.
///
/// The span: a thin rule under the below-line labels from the first node to
/// the last one shown, a tick rising from each end, and the text in an accent
/// pill on its middle. Its room is always held back, so it moves nothing
/// whenever it comes. One per card.
///
/// A fullscreen feature card (any presenter position). Every word shown is a
/// tag field as written. Sizes, places and timing: <see cref="TimelineWideTypography"/>
/// (and <see cref="TimelineTypography"/> for a node's entrance).
/// </summary>
public class TimelineWideCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.TimelineWide;

    public static Color Slate => TimelineCard.Slate;

    // The age of a node (or span) that has no entrance to play: long landed.
    const float Landed = 1000f;

    private sealed class Node
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public Image Dot;
        public TextMeshProUGUI Numeral;
        public RectTransform Label;
        public CanvasGroup LabelGroup;
        public TextMeshProUGUI Date;
        public TextMeshProUGUI Event;

        public bool IsNumeral;      // the first field sits in the dot
        public bool HasDate;        // the first field is the small-caps date
        public float X;             // rest, frame space
        public float Diameter;      // the dot at rest
        public Rect LabelRect;      // rest, frame space (y down)
        public float AddedAt;       // NaN: landed, no entrance
        public float HandedOverAt;  // NaN until the next node takes the accent
    }

    private Image dim;
    private RectTransform content;
    private CanvasGroup contentGroup;
    private Image track, line;
    private TextMeshProUGUI probe; // never drawn — the height of two event lines at a size

    private RectTransform spanRoot;
    private CanvasGroup spanGroup;
    private Image spanRule, spanTickLeft, spanTickRight, spanPillFill;
    private RectTransform spanPill;
    private TextMeshProUGUI spanLabel;

    private readonly Node[] nodes = new Node[TimelineWideTypography.MaxNodes];
    private readonly List<(string date, string evt)> run = new List<(string, string)>();
    private int shown;

    private BigTextTypography.DimEnvelope dimEnvelope;
    private float clock;                 // scaled time, like DOTween
    private float shownAt = float.NaN;   // NaN until Show
    private bool settling;               // an entrance is playing; the idle drift waits for it

    private Vector2 laidOutSize = new Vector2(-1f, -1f);
    private List<(string date, string evt)> laidOutNodes;
    private int laidOutFor;
    private float lineY, labelOffset, spanTop;

    private string spanString = "";
    private int spanLast;
    private float spanAt = float.NaN;
    private Rect spanBracket, spanPillRect;
    private float spanRuleY;

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>Every node this run will hold, the first included (at most
    /// <see cref="TimelineWideTypography.MaxNodes"/>) — read ahead from the
    /// timeline and set BEFORE Initialize, so every node has its place from the
    /// start. Null or too short: the card re-lays itself out when the extra
    /// node arrives (and says so).</summary>
    public List<(string date, string evt)> AnticipatedNodes { get; set; }

    /// <summary>A {Timeline:Span} is coming — set BEFORE Initialize. The span's
    /// room is held back either way (an unannounced span lands in the same
    /// spot), so this only shows in the layout log.</summary>
    public bool SpanAnticipated { get; set; }

    public int NodeCount => shown;
    public bool HasSpan { get; private set; }

    /// <summary>True once the timeline is on its way out (its End, or a fast hide):
    /// a node arriving now belongs to the next timeline, not this one.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>The last layout: elements "dates" (when the run has a dated node),
    /// "events", "numerals" (when it has a numbered one) and "span" (once
    /// shown, when it has text); warnings.</summary>
    public CardTypography.Report Layout { get; private set; }

    public float DimAlpha => dim != null ? dim.color.a : 0f;

    // ---- Read by the self-tests -------------------------------------------------------
    public BigTextTypography.DimEnvelope Dim => dimEnvelope;
    public float ContentAlpha => contentGroup != null ? contentGroup.alpha : 0f;
    /// <summary>The frame the card is laid out for (1920×1080 when its rect isn't resolved).</summary>
    public Vector2 Frame => laidOutSize;
    /// <summary>How many places the line is cut into: the nodes shown plus those still to come.</summary>
    public int Places => laidOutFor;
    /// <summary>One event size for every node (frame px).</summary>
    public float EventSize { get; private set; }
    public float DateSize { get; private set; }

    /// <summary>Node i's place on the line, frame space.</summary>
    public float NodeX(int i) => nodes[i].X;
    public float LineY => lineY;
    /// <summary>Node i's label box at rest, frame space (y down).</summary>
    public Rect LabelRect(int i) => nodes[i].LabelRect;
    public bool IsNumeral(int i) => nodes[i].IsNumeral;
    public bool HasDate(int i) => nodes[i].HasDate;
    public float DotDiameter(int i) => nodes[i].Diameter;
    public float DotScale(int i) => nodes[i].Dot.rectTransform.localScale.x;
    public Image Dot(int i) => nodes[i].Dot;
    public float NodeAlpha(int i) => nodes[i].Group.alpha;
    public float LabelAlpha(int i) => nodes[i].LabelGroup.alpha;
    public RectTransform NodeLabel(int i) => nodes[i].Label;
    public TextMeshProUGUI DateText(int i) => nodes[i].Date;
    public TextMeshProUGUI EventText(int i) => nodes[i].Event;
    public TextMeshProUGUI NumeralText(int i) => nodes[i].Numeral;

    public Image Line => line;
    public Image Track => track;
    /// <summary>How much of the solid line is drawn, from the line's left end.</summary>
    public float LineLength => line != null && line.enabled ? line.rectTransform.sizeDelta.x : 0f;
    /// <summary>Where the solid line ends now, frame space.</summary>
    public float LineEndX => TimelineWideTypography.LineLeft(laidOutSize.x) + LineLength;

    /// <summary>The top of the room held back for the span (frame space) — there with or without one.</summary>
    public float SpanTop => spanTop;
    /// <summary>The span text as given.</summary>
    public string SpanString => spanString;
    /// <summary>The bracket at rest: first node → last node shown, tick tops → rule (frame space).</summary>
    public Rect SpanBracketRect => spanBracket;
    /// <summary>The pill at rest (frame space); empty when the span has no text.</summary>
    public Rect SpanPillRect => spanPillRect;
    public float SpanRuleY => spanRuleY;
    public RectTransform SpanRoot => spanRoot;
    public RectTransform SpanPill => spanPill;
    public Image SpanRule => spanRule;
    public Image SpanTick(int i) => i == 0 ? spanTickLeft : spanTickRight;
    public TextMeshProUGUI SpanLabel => spanLabel;

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

        // Everything else lives in frame space under Content: anchored to its
        // top-left corner, x right, y down (as -y).
        content = ContentCardUIBuilder.CreateChild(rectTransform, "Content");
        contentGroup = EnsureGroup(content);

        track = ContentCardUIBuilder.CreateImage(content, "Track", WithAlpha(Slate, 0f));
        FromTopLeft(track.rectTransform, new Vector2(0f, 0.5f));
        line = ContentCardUIBuilder.CreateImage(content, "Line", Slate);
        FromTopLeft(line.rectTransform, new Vector2(0f, 0.5f));
        line.enabled = false;

        BuildSpan();

        for (int i = 0; i < TimelineWideTypography.MaxNodes; i++)
        {
            var n = new Node { AddedAt = float.NaN, HandedOverAt = float.NaN };

            // The node's origin is its place on the line; dot and label hang off it.
            GameObject ngo = new GameObject("Node" + i, typeof(RectTransform));
            ngo.transform.SetParent(content, false);
            n.Root = ngo.GetComponent<RectTransform>();
            FromTopLeft(n.Root, new Vector2(0.5f, 0.5f));
            n.Root.sizeDelta = Vector2.zero;
            n.Group = EnsureGroup(n.Root);

            n.Dot = ContentCardUIBuilder.CreateImage(n.Root, "Dot", Slate);
            n.Dot.sprite = ProceduralSprites.Circle();
            RectTransform drt = n.Dot.rectTransform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = Vector2.zero;

            // In the dot, so it pops with it.
            n.Numeral = ContentCardUIBuilder.CreateText(drt, "Numeral", Color.white,
                TimelineWideTypography.NumeralPx, TextAlignmentOptions.Center);
            CardTypography.PrepareText(n.Numeral, CardTypography.Weight.Heavy, TextAlignmentOptions.Center);
            n.Numeral.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform nrt = n.Numeral.rectTransform;
            nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = Vector2.zero;

            // The label: a box centred on the node, positioned by its top edge.
            GameObject lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(n.Root, false);
            n.Label = lgo.GetComponent<RectTransform>();
            n.Label.anchorMin = n.Label.anchorMax = new Vector2(0.5f, 0.5f);
            n.Label.pivot = new Vector2(0.5f, 1f);
            n.LabelGroup = EnsureGroup(n.Label);

            n.Date = ContentCardUIBuilder.CreateText(n.Label, "Date", Color.white,
                TimelineWideTypography.DatePx, TextAlignmentOptions.Top);
            CardTypography.PrepareText(n.Date, CardTypography.Weight.Bold, TextAlignmentOptions.Top);
            n.Date.fontStyle |= FontStyles.SmallCaps;
            n.Date.characterSpacing = TimelineWideTypography.DateTrackingEm;

            n.Event = ContentCardUIBuilder.CreateText(n.Label, "Event", Color.white,
                TimelineWideTypography.EventMaxPx, TextAlignmentOptions.Top);
            CardTypography.PrepareText(n.Event, CardTypography.Weight.Bold, TextAlignmentOptions.Top);

            ngo.SetActive(false);
            nodes[i] = n;
        }

        probe = ContentCardUIBuilder.CreateText(content, "Probe", Color.clear,
            TimelineWideTypography.EventMaxPx, TextAlignmentOptions.Top);
        CardTypography.PrepareText(probe, CardTypography.Weight.Bold, TextAlignmentOptions.Top);
        probe.enabled = false;
    }

    private void BuildSpan()
    {
        spanRoot = ContentCardUIBuilder.CreateChild(content, "Span");
        spanGroup = EnsureGroup(spanRoot);
        Color accent = ContentCardUIBuilder.AccentColor;

        spanRule = ContentCardUIBuilder.CreateImage(spanRoot, "Rule", accent);
        FromTopLeft(spanRule.rectTransform, new Vector2(0.5f, 0.5f));
        // A tick stands on the rule's lower edge and rises past its upper one.
        spanTickLeft = ContentCardUIBuilder.CreateImage(spanRoot, "TickLeft", accent);
        FromTopLeft(spanTickLeft.rectTransform, new Vector2(0.5f, 0f));
        spanTickRight = ContentCardUIBuilder.CreateImage(spanRoot, "TickRight", accent);
        FromTopLeft(spanTickRight.rectTransform, new Vector2(0.5f, 0f));

        // The pill last, so it draws over the middle of the rule.
        GameObject pgo = new GameObject("Pill", typeof(RectTransform));
        pgo.transform.SetParent(spanRoot, false);
        spanPill = pgo.GetComponent<RectTransform>();
        FromTopLeft(spanPill, new Vector2(0.5f, 0.5f));

        spanPillFill = ContentCardUIBuilder.CreateImage(spanPill, "Fill", accent);
        ContentCardUIBuilder.SetStretch(spanPillFill.rectTransform, 0f, 0f, 0f, 0f);

        spanLabel = ContentCardUIBuilder.CreateText(spanPill, "Text", Color.white,
            TimelineWideTypography.SpanPx, TextAlignmentOptions.Center);
        CardTypography.PrepareText(spanLabel, CardTypography.Weight.Bold, TextAlignmentOptions.Center);
        spanLabel.textWrappingMode = TextWrappingModes.NoWrap;

        spanGroup.alpha = 0f;
        spanRoot.gameObject.SetActive(false);
    }

    // First node: data.primaryText = its date (or number), data.secondaryText = its event.
    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        run.Clear();
        run.Add((data.primaryText ?? "", data.secondaryText ?? ""));
        shown = 1;
        foreach (Node n in nodes) { n.AddedAt = float.NaN; n.HandedOverAt = float.NaN; }
        HasSpan = false;
        spanString = "";
        spanAt = float.NaN;

        LayoutCard(log: true);
        StepNodes(); // the first node at rest until Show starts its entrance
    }

    /// <summary>
    /// Adds the next node on its beat: the line draws on to its place, its dot
    /// pops, its label slides in, and it takes the accent from the node before
    /// it. <paramref name="instant"/> lands it at once (its beat passed while
    /// the card waited). False when the run already holds
    /// <see cref="TimelineWideTypography.MaxNodes"/> nodes — nothing changes.
    /// </summary>
    public bool AppendNode(string date, string evt, bool instant = false)
    {
        if (shown >= TimelineWideTypography.MaxNodes) return false;
        run.Add((date ?? "", evt ?? ""));
        Node n = nodes[shown];
        Node prev = shown > 0 ? nodes[shown - 1] : null;
        shown++;

        // A node the read-ahead announced has had its place since the card
        // opened: nothing to lay out, nothing moves.
        if (!IsLaidOutFor(FrameSize(), SizingNodes())) LayoutCard(log: false);

        n.AddedAt = instant ? clock - TimelineWideTypography.EntranceSeconds : clock;
        n.HandedOverAt = float.NaN;
        if (prev != null) prev.HandedOverAt = n.AddedAt + TimelineTypography.PopStart;
        settling = true;
        StepNodes();
        return true;
    }

    /// <summary>
    /// Draws the bracket under the run — first node to the last node shown NOW
    /// — and <paramref name="text"/> in a pill on its middle (no pill when the
    /// text is empty). The bracket grows from its centre outward and the pill
    /// pops, over 0.3 s. One span per card: false when one is already shown,
    /// and nothing changes.
    /// </summary>
    public bool ShowSpan(string text) => ShowSpan(text, false);

    /// <summary>As above; <paramref name="instant"/> lands the span at once
    /// (its beat passed while the card waited).</summary>
    public bool ShowSpan(string text, bool instant)
    {
        if (HasSpan || shown == 0) return false;
        HasSpan = true;
        spanString = text ?? "";
        spanLast = shown - 1;
        spanAt = instant ? clock - TimelineWideTypography.SpanSeconds : clock;

        if (Layout == null || FrameSize() != laidOutSize) LayoutCard(log: false); // lays the span too
        else LaySpan(Layout);
        settling = true;
        StepNodes();
        return true;
    }

    void LateUpdate()
    {
        if (shown > 0 && FrameSize() != laidOutSize)
        {
            LayoutCard(log: false);
            StepNodes();
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

    // The nodes the line is cut for: what's shown plus what's coming.
    private List<(string date, string evt)> SizingNodes()
    {
        var all = new List<(string date, string evt)>(run);
        if (AnticipatedNodes != null)
            for (int i = all.Count; i < AnticipatedNodes.Count && all.Count < TimelineWideTypography.MaxNodes; i++)
                all.Add((AnticipatedNodes[i].date ?? "", AnticipatedNodes[i].evt ?? ""));
        return all;
    }

    private bool IsLaidOutFor(Vector2 frame, List<(string date, string evt)> sizing)
    {
        if (laidOutNodes == null || frame != laidOutSize || sizing.Count != laidOutNodes.Count) return false;
        for (int i = 0; i < sizing.Count; i++)
            if (sizing[i].date != laidOutNodes[i].date || sizing[i].evt != laidOutNodes[i].evt) return false;
        return true;
    }

    private void LayoutCard(bool log)
    {
        if (run.Count == 0) return;
        Vector2 frame = FrameSize();
        laidOutSize = frame;
        float k = TimelineWideTypography.ScaleFor(frame.y);

        List<(string date, string evt)> sizing = SizingNodes();
        int count = sizing.Count;
        int refitted = laidOutFor > 0 && count > laidOutFor ? count : 0;
        laidOutFor = count;
        laidOutNodes = sizing;

        var report = new CardTypography.Report
        {
            Card = CardTypography.CardLabel("TimelineWide", sizing[0].date + " — " + sizing[0].evt),
        };

        // ---- What each place holds (places not shown yet stay hidden) ----------------
        float dateSize = TimelineWideTypography.DatePx * k;
        float numeralSize = TimelineWideTypography.NumeralPx * k;
        int firstDate = -1, firstNumeral = -1;
        float widestNumeral = 0f;
        for (int i = 0; i < TimelineWideTypography.MaxNodes; i++)
        {
            Node n = nodes[i];
            bool used = i < count;
            string first = used ? sizing[i].date : "";
            n.IsNumeral = used && TimelineWideTypography.IsNumeral(first);
            n.HasDate = used && TimelineWideTypography.HasDateLabel(first);
            n.Date.text = n.HasDate ? first : "";
            n.Numeral.text = n.IsNumeral ? first.Trim() : "";
            n.Event.text = used ? sizing[i].evt : "";
            if (n.Date.gameObject.activeSelf != n.HasDate) n.Date.gameObject.SetActive(n.HasDate);
            if (n.Numeral.gameObject.activeSelf != n.IsNumeral) n.Numeral.gameObject.SetActive(n.IsNumeral);

            if (n.HasDate && firstDate < 0) firstDate = i;
            if (n.IsNumeral)
            {
                if (firstNumeral < 0) firstNumeral = i;
                widestNumeral = Mathf.Max(widestNumeral, CardTypography.MeasureLineWidth(n.Numeral, numeralSize));
            }
        }

        // ---- Dots, and how far from the line the labels start --------------------------
        // One size for every numbered dot, and one label distance for the whole
        // run (clear of its largest dot), so the labels line up across the frame.
        float dot = TimelineWideTypography.DotDiameter(false, k);
        float numeralCap = CardTypography.CapHeight(nodes[0].Numeral.font, numeralSize);
        float numeralDot = firstNumeral >= 0
            ? TimelineWideTypography.NumeralDotDiameter(widestNumeral, numeralCap, k)
            : TimelineWideTypography.DotDiameter(true, k);
        labelOffset = TimelineWideTypography.LabelOffset(firstNumeral >= 0 ? numeralDot : dot, k);
        lineY = TimelineWideTypography.LineY(frame.y);
        float aboveRoom = TimelineWideTypography.AboveRoom(frame.y, labelOffset);
        float belowRoom = TimelineWideTypography.BelowRoom(frame.y, labelOffset);

        // ---- Label widths and the dates (fixed size, wrapping only if they must) -------
        float gap = TimelineWideTypography.DateToEventGapPx * k;
        var widths = new float[count];
        var dateHeights = new float[count];
        for (int i = 0; i < count; i++)
        {
            widths[i] = TimelineWideTypography.LabelMaxWidth(i, count, frame.x, frame.y);
            dateHeights[i] = nodes[i].HasDate ? CardTypography.MeasureHeight(nodes[i].Date, dateSize, widths[i]) : 0f;
        }

        // ---- Events: one size for every node — the largest at which each is at
        // most two lines in its own label width (no word broken) and its label
        // fits its side of the line.
        bool Fits(float size)
        {
            float twoLines = LinesHeight(size, TimelineWideTypography.EventMaxLines);
            for (int i = 0; i < count; i++)
            {
                Node n = nodes[i];
                if (string.IsNullOrEmpty(n.Event.text)) continue;
                if (CardTypography.WidestWordWidth(n.Event.font, n.Event.text, size) > widths[i]) return false;
                float h = CardTypography.MeasureHeight(n.Event, size, widths[i]);
                if (h > twoLines + 0.5f) return false;
                float room = TimelineWideTypography.IsAbove(i) ? aboveRoom : belowRoom;
                if (dateHeights[i] + (n.HasDate ? gap : 0f) + h > room + 0.5f) return false;
            }
            return true;
        }
        CardTypography.BodyFit fit = CardTypography.FitBody(Fits,
            TimelineWideTypography.EventMinPx * k, TimelineWideTypography.EventMaxPx * k,
            TimelineWideTypography.EventMinPx * k, CardTypography.HardFloorPx * k);
        EventSize = fit.Size;
        DateSize = dateSize;

        // ---- Place every node, shown or still to come ----------------------------------
        float tallestAbove = 0f, tallestBelow = 0f;
        float belowBottom = lineY + labelOffset;
        for (int i = 0; i < count; i++)
        {
            Node n = nodes[i];
            float w = widths[i];
            n.X = TimelineWideTypography.NodeX(i, count, frame.x);
            n.Diameter = n.IsNumeral ? numeralDot : dot;
            n.Root.anchoredPosition = new Vector2(n.X, -lineY);
            n.Dot.rectTransform.sizeDelta = new Vector2(n.Diameter, n.Diameter);

            float dh = dateHeights[i];
            float eh = string.IsNullOrEmpty(n.Event.text) ? 0f : CardTypography.MeasureHeight(n.Event, fit.Size, w);
            float between = n.HasDate && eh > 0f ? gap : 0f;
            float h = dh + between + eh;
            n.LabelRect = TimelineWideTypography.LabelRect(i, n.X, lineY, w, h, labelOffset);
            n.Label.sizeDelta = new Vector2(w, h);
            n.Date.fontSize = dateSize;
            if (n.HasDate) CardTypography.PlaceBox(n.Date, 0f, 0f, w, Mathf.Max(dh, 1f));
            n.Event.fontSize = fit.Size;
            CardTypography.PlaceBox(n.Event, 0f, dh + between, w, Mathf.Max(eh, 1f));

            if (TimelineWideTypography.IsAbove(i)) tallestAbove = Mathf.Max(tallestAbove, h);
            else
            {
                tallestBelow = Mathf.Max(tallestBelow, h);
                belowBottom = Mathf.Max(belowBottom, n.LabelRect.yMax);
            }

            if (n.IsNumeral) LayNumeral(n, numeralSize);
        }
        // The span goes under the lowest below-line label the run will have.
        spanTop = TimelineWideTypography.SpanTop(belowBottom, frame.y);

        // ---- The line ------------------------------------------------------------------
        float thickness = TimelineWideTypography.LineThicknessPx * k;
        float lineLeft = TimelineWideTypography.LineLeft(frame.x);
        track.rectTransform.anchoredPosition = new Vector2(lineLeft, -lineY);
        track.rectTransform.sizeDelta = new Vector2(TimelineWideTypography.LineWidth(frame.x), thickness);
        line.rectTransform.anchoredPosition = new Vector2(lineLeft, -lineY);

        // ---- Report --------------------------------------------------------------------
        if (firstDate >= 0)
            report.Add("dates", nodes[firstDate].Date, dateSize / k, TimelineWideTypography.DateFloorPx);
        report.Add("events", nodes[0].Event, fit.Size / k, TimelineWideTypography.EventMinPx);
        if (firstNumeral >= 0)
            report.Add("numerals", nodes[firstNumeral].Numeral, numeralSize / k, TimelineWideTypography.NumeralFloorPx);
        // Top of the tallest label above the line to the bottom of the tallest below it.
        report.GroupHeight = tallestAbove + 2f * labelOffset + tallestBelow;
        report.InnerHeight = TimelineWideTypography.SafeBottom(frame.y) - TimelineWideTypography.SafeTop(frame.y);

        if (fit.Overflows)
            report.Warn($"{report.Card}: the events don't fit {TimelineWideTypography.EventMaxLines} lines of their labels even at " +
                        $"{CardTypography.HardFloorPx:F0} px — shown at {CardTypography.HardFloorPx:F0} px on more lines.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: the events only fit {TimelineWideTypography.EventMaxLines} lines at {fit.Size / k:F0} px — " +
                        $"under the {TimelineWideTypography.EventMinPx:F0} px floor.");
        if (tallestAbove > aboveRoom + 0.5f || tallestBelow > belowRoom + 0.5f)
            report.Warn($"{report.Card}: a label is taller than its side of the line — the timeline runs past title-safe.");
        if (firstNumeral >= 0 && !TimelineWideTypography.NumeralFits(widestNumeral, numeralCap, numeralDot))
            report.Warn($"{report.Card}: one of the numerals is wider than its dot.");

        if (HasSpan) LaySpan(report);

        Layout = report;
        if (log || refitted > 0)
        {
            report.LogSummary();
            if (log)
                Debug.Log($"[Cards] {report.Card}: {count} place(s) on the line" + (SpanAnticipated ? ", a span to come." : "."));
            if (refitted > 0)
                Debug.Log($"[Cards] {report.Card}: RE-FITTED for {refitted} nodes — a node the read-ahead didn't see arrived.");
        }
    }

    // The height of `lines` rows of event text at a size — a text wraps to at
    // most that many rows when its preferred height is no more. From TMP's
    // preferred values, so it holds for nodes not shown yet (their mesh isn't built).
    private float LinesHeight(float size, int lines)
    {
        var rows = new System.Text.StringBuilder("Ag");
        for (int i = 1; i < lines; i++) rows.Append("\nAg");
        probe.fontSize = size;
        return probe.GetPreferredValues(rows.ToString(), 100000f, 0f).y;
    }

    // The numeral in its dot: placed by its own ink (digits sit on the
    // baseline, so the line box doesn't centre them), as the Versus glyph is.
    private static void LayNumeral(Node n, float size)
    {
        RectTransform drt = n.Dot.rectTransform, nrt = n.Numeral.rectTransform;
        float d = n.Diameter;
        drt.localScale = Vector3.one; // measured unscaled; the pop scales it after
        nrt.sizeDelta = new Vector2(d * 3f, d * 3f);
        nrt.anchoredPosition = Vector2.zero;
        n.Numeral.fontSize = size;
        Rect ink = CardTypography.InkInCard(n.Numeral, drt);
        if (ink.width > 0f && ink.height > 0f)
            nrt.anchoredPosition = new Vector2(d * 0.5f - ink.center.x, ink.center.y - d * 0.5f);
    }

    // The bracket and its pill, in the room held back under the below-line
    // labels. Adds "span" to the report (when there is text).
    private void LaySpan(CardTypography.Report report)
    {
        Vector2 frame = laidOutSize;
        float k = TimelineWideTypography.ScaleFor(frame.y);
        Color accent = ContentCardUIBuilder.AccentColor;
        bool hasText = !string.IsNullOrWhiteSpace(spanString);

        float pillH = TimelineWideTypography.SpanPillHeightPx * k, pillW = 0f;
        float size = TimelineWideTypography.SpanPx * k;
        bool wraps = false;
        if (spanPill.gameObject.activeSelf != hasText) spanPill.gameObject.SetActive(hasText);
        if (hasText)
        {
            float padX = TimelineWideTypography.SpanPillPadXPx * k, padY = TimelineWideTypography.SpanPillPadYPx * k;
            float maxTextW = TimelineWideTypography.SpanPillMaxWidth(frame.x) - 2f * padX;
            float floor = CardTypography.HardFloorPx * k;
            spanLabel.text = spanString;

            // One line at 44 px; a long one shrinks toward 32, and only then wraps.
            if (CardTypography.MeasureLineWidth(spanLabel, size) > maxTextW)
            {
                float s = CardTypography.LargestFitting(x => CardTypography.MeasureLineWidth(spanLabel, x) <= maxTextW, floor, size);
                wraps = float.IsNaN(s);
                size = wraps ? floor : s;
            }
            spanLabel.textWrappingMode = wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            spanLabel.fontSize = size;
            float textW = wraps ? maxTextW : CardTypography.MeasureLineWidth(spanLabel, size);
            float textH = CardTypography.MeasureHeight(spanLabel, size, wraps ? maxTextW : 100000f);
            pillH = Mathf.Max(pillH, textH + 2f * padY);
            pillW = Mathf.Max(pillH, textW + 2f * padX);
            ContentCardUIBuilder.SetStretch(spanLabel.rectTransform, padX, 0f, padX, 0f);

            // A true pill: the corner radius is half the height, whatever the frame's scale.
            float radius = pillH * 0.5f;
            int spriteRadius = Mathf.Clamp(Mathf.RoundToInt(radius), 1, 64);
            spanPillFill.sprite = StyleSpriteFactory.GetRoundedRect(spriteRadius);
            spanPillFill.type = Image.Type.Sliced;
            float ppu = spanPillFill.pixelsPerUnit;
            spanPillFill.pixelsPerUnitMultiplier = spriteRadius / Mathf.Max(0.01f, radius * (ppu > 0f ? ppu : 1f));
            spanPillFill.color = accent;
        }

        spanRuleY = TimelineWideTypography.SpanRuleY(spanTop, pillH);
        spanBracket = TimelineWideTypography.SpanBracket(spanLast, laidOutFor, frame.x, frame.y, spanRuleY);
        float pillX = TimelineWideTypography.SpanPillCentreX(spanBracket.center.x, pillW, frame.x);
        spanPillRect = hasText ? new Rect(pillX - pillW * 0.5f, spanRuleY - pillH * 0.5f, pillW, pillH) : Rect.zero;
        spanPill.sizeDelta = new Vector2(pillW, pillH);
        spanPill.anchoredPosition = new Vector2(pillX, -spanRuleY);

        // The rule's width and the ticks' x are set every frame (the bracket grows).
        float thickness = TimelineWideTypography.SpanRulePx * k;
        spanRule.rectTransform.anchoredPosition = new Vector2(spanBracket.center.x, -spanRuleY);
        Vector2 tick = new Vector2(thickness, TimelineWideTypography.SpanTickPx * k + thickness);
        spanTickLeft.rectTransform.sizeDelta = tick;
        spanTickRight.rectTransform.sizeDelta = tick;
        spanRule.color = accent;
        spanTickLeft.color = accent;
        spanTickRight.color = accent;

        if (!hasText) return;
        CardTypography.Element e = report.Add("span", spanLabel, size / k, TimelineWideTypography.SpanFloorPx);
        if (wraps)
            report.Warn($"{report.Card}: the span text doesn't fit one line even at {CardTypography.HardFloorPx:F0} px — wrapped.");
        else if (e.BelowFloor)
            report.Warn($"{report.Card}: the span text only fits at {size / k:F0} px — under its {TimelineWideTypography.SpanFloorPx:F0} px.");
        if (spanPillRect.yMax > TimelineWideTypography.SafeBottom(frame.y) + 0.5f)
            report.Warn($"{report.Card}: the span runs past title-safe.");
    }

    // =====================================================================
    // Entrance and per frame (the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        KillCurrentSequence();

        // The timeline owns its entrance — no preset tilt, no root slide, root
        // fully visible: the dim opens and the first node lands with the card.
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;
        contentGroup.alpha = 1f;
        dimEnvelope.Open(clock);

        shownAt = clock;
        // (Nodes that landed at once while the card waited keep the first one at rest too.)
        if (shown == 1) nodes[0].AddedAt = clock;
        settling = true;
        StepNodes();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame: the card clock, every entrance and the dim. Public so
    /// the self-tests can step a card frame by frame in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        StepNodes();
        if (dim != null)
        {
            Color c = dim.color;
            c.a = dimEnvelope.AlphaAt(clock);
            dim.color = c;
        }
    }

    // Puts the line, every node and the span where they are at `clock`.
    private void StepNodes()
    {
        if (shown == 0 || laidOutFor == 0) return;
        float k = TimelineWideTypography.ScaleFor(laidOutSize.y);
        Color accent = ContentCardUIBuilder.AccentColor;
        Color olderDate = WithAlpha(Color.white, TimelineWideTypography.DateAlpha);
        float lineLeft = TimelineWideTypography.LineLeft(laidOutSize.x);
        float lineEnd = lineLeft;
        bool entering = false;

        for (int i = 0; i < TimelineWideTypography.MaxNodes; i++)
        {
            Node n = nodes[i];
            bool on = i < shown;
            if (n.Root.gameObject.activeSelf != on) n.Root.gameObject.SetActive(on);
            if (!on) continue;

            // Line to the node (0.3 s) → the dot pops → the label slides in.
            float age = float.IsNaN(n.AddedAt) ? Landed : clock - n.AddedAt;
            float sincePop = age - TimelineTypography.PopStart;
            n.Dot.rectTransform.localScale = Vector3.one * TimelineTypography.DotScaleAt(sincePop);
            float slide = TimelineTypography.TextAt(sincePop);
            n.LabelGroup.alpha = sincePop <= 0f ? 0f : slide;
            // It comes in toward the line: from higher up above it, from lower down below it.
            float away = (1f - slide) * TimelineWideTypography.LabelSlidePx * k;
            float restY = lineY - n.LabelRect.yMin;
            n.Label.anchoredPosition = new Vector2(0f, TimelineWideTypography.IsAbove(i) ? restY + away : restY - away);

            // The newest node holds the accent; one handed over settles to 70 %.
            float settled = float.IsNaN(n.HandedOverAt) ? 0f : TimelineTypography.SettleAt(clock - n.HandedOverAt);
            n.Dot.color = Color.Lerp(accent, Slate, settled);
            n.Date.color = Color.Lerp(accent, olderDate, settled);
            n.Group.alpha = Mathf.Lerp(1f, TimelineTypography.OlderAlpha, settled);

            // The solid line: as far as any node's stretch has drawn.
            float from = i == 0 ? lineLeft : nodes[i - 1].X;
            lineEnd = Mathf.Max(lineEnd, Mathf.Lerp(from, n.X, TimelineTypography.LineDrawAt(age)));
            if (age < TimelineWideTypography.EntranceSeconds) entering = true;
        }

        float drawn = lineEnd - lineLeft;
        line.enabled = drawn > 0.5f;
        line.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, drawn), TimelineWideTypography.LineThicknessPx * k);
        track.color = WithAlpha(Slate, float.IsNaN(shownAt)
            ? TimelineWideTypography.TrackAlpha
            : TimelineWideTypography.TrackAlphaAt(clock - shownAt));

        if (spanRoot.gameObject.activeSelf != HasSpan) spanRoot.gameObject.SetActive(HasSpan);
        if (HasSpan)
        {
            // The bracket grows from its centre outward; the pill pops on it.
            float age = float.IsNaN(spanAt) ? Landed : clock - spanAt;
            float thickness = TimelineWideTypography.SpanRulePx * k;
            float half = spanBracket.width * 0.5f * TimelineWideTypography.SpanGrowAt(age);
            float cx = spanBracket.center.x;
            float ruleBottom = -(spanRuleY + thickness * 0.5f);
            spanRule.rectTransform.sizeDelta = new Vector2(half * 2f, thickness);
            spanTickLeft.rectTransform.anchoredPosition = new Vector2(cx - half, ruleBottom);
            spanTickRight.rectTransform.anchoredPosition = new Vector2(cx + half, ruleBottom);
            spanPill.localScale = Vector3.one * TimelineWideTypography.SpanPillScaleAt(age);
            spanGroup.alpha = TimelineWideTypography.SpanAlphaAt(age);
            if (age < TimelineWideTypography.SpanSeconds) entering = true;
        }

        if (settling && !entering && !float.IsNaN(shownAt) && !IsClosing)
        {
            settling = false;
            // The slow drift moves the timeline only — never the dim.
            StartIdleFloat(content);
        }
    }

    // =====================================================================
    // Hide
    // =====================================================================

    public override void Hide(bool fast = false)
    {
        IsClosing = true;
        dimEnvelope.Close(clock);

        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();
        StopIdleFloat();

        // Line, nodes and span fade together from wherever they are; the dim
        // closes on its own envelope, and the card is done when it has.
        currentSequence = DOTween.Sequence()
            .Join(contentGroup.DOFade(0f, TimelineWideTypography.ContentFadeOutSeconds).SetEase(Ease.InQuad))
            .AppendInterval(Mathf.Max(0f, BigTextTypography.DimFadeOutSeconds - TimelineWideTypography.ContentFadeOutSeconds))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    // Anchors a rect to Content's top-left corner, so its anchoredPosition is
    // (x, -y) in frame space.
    private static void FromTopLeft(RectTransform rt, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = pivot;
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}

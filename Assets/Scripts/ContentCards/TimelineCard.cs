using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MugsTech.Style;

/// <summary>
/// Dated events that appear as they're spoken — "Two Sundays, three weeks
/// apart" said once and then on screen.
/// Tag: {Timeline:"date","event"[,Left|,Right]}   ← the first opens the card (its side applies)
///      {Timeline:"date","event"}                  ← each next one adds a node
///      {Timeline:End}
///
/// Vertical: a line runs down the left of the card; each node is a dot with
/// its date (bold, 44 px) and its event (40–48 px, up to two lines) to the
/// right. A new node: the line draws down to it (0.3 s), its dot pops, its
/// text slides in; the stack re-centres (eased, like BigText's); the newest
/// node takes the accent and the older ones settle to 70 %. Up to five nodes.
/// The node rules — which tag opens a card, the 5-node cap, a side on a later
/// node, a timeline cleared before its End — live in ContentZoneController.
/// Sizes and timing: <see cref="TimelineTypography"/>.
/// </summary>
public class TimelineCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Timeline;

    public static Color Slate => CompareCard.Slate;

    private sealed class Node
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public Image Dot;
        public RectTransform Text;
        public CanvasGroup TextGroup;
        public TextMeshProUGUI Date;
        public TextMeshProUGUI Event;
        public float Height;
        public float DotY;          // dot centre, from the node's top
        public float AddedAt;       // NaN: the opening node, there as the card lands
        public float HandedOverAt;  // NaN until the next node takes the accent
    }

    private RectTransform stackRoot;
    private Image line;
    private readonly Node[] nodes = new Node[TimelineTypography.MaxNodes];
    private readonly List<(string date, string evt)> content = new List<(string, string)>();
    private int shown;

    private float clock;
    private float glideFrom, glideTo, glideStart = float.NaN;
    private Vector2 laidOutSize = new Vector2(-1f, -1f);
    private int laidOutFor;
    private float layoutGap; // the gap between nodes the last layout used

    /// <summary>Every node this timeline will hold — its later tags read ahead
    /// from the timeline by ContentZoneController before Initialize — so it is
    /// sized once and doesn't jump when a longer event arrives.</summary>
    public List<(string date, string evt)> AnticipatedNodes { get; set; }

    // ---- Read by the self-tests -------------------------------------------------------
    public CardTypography.Report Layout { get; private set; }
    public int NodeCount => shown;
    public float EventSize { get; private set; }
    public float DateSize { get; private set; }
    public RectTransform StackRoot => stackRoot;
    public Image Line => line;
    public RectTransform NodeRoot(int i) => nodes[i].Root;
    public float NodeAlpha(int i) => nodes[i].Group.alpha;
    public Image Dot(int i) => nodes[i].Dot;
    public TextMeshProUGUI DateText(int i) => nodes[i].Date;
    public TextMeshProUGUI EventText(int i) => nodes[i].Event;
    public RectTransform NodeText(int i) => nodes[i].Text;
    public float TextAlpha(int i) => nodes[i].TextGroup.alpha;
    /// <summary>Where the stack's top rests for the nodes now shown (card space, y down).</summary>
    public float StackRestTop => glideTo;
    /// <summary>Dot i's centre at rest, in stack space (y down from the stack's top).</summary>
    public float DotCentre(int i) => NodeTop(i) + nodes[i].DotY;

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        GameObject sgo = new GameObject("Stack", typeof(RectTransform));
        sgo.transform.SetParent(rectTransform, false);
        stackRoot = sgo.GetComponent<RectTransform>();
        stackRoot.anchorMin = stackRoot.anchorMax = new Vector2(0f, 1f);
        stackRoot.pivot = new Vector2(0f, 1f);

        line = ContentCardUIBuilder.CreateImage(stackRoot, "Line", WithAlpha(Slate, 0.55f));
        RectTransform lrt = line.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);

        for (int i = 0; i < TimelineTypography.MaxNodes; i++)
        {
            var n = new Node { AddedAt = float.NaN, HandedOverAt = float.NaN };
            GameObject ngo = new GameObject("Node" + i, typeof(RectTransform));
            ngo.transform.SetParent(stackRoot, false);
            n.Root = ngo.GetComponent<RectTransform>();
            n.Root.anchorMin = n.Root.anchorMax = new Vector2(0f, 1f);
            n.Root.pivot = new Vector2(0f, 1f);
            n.Group = EnsureGroup(n.Root);

            n.Dot = ContentCardUIBuilder.CreateImage(n.Root, "Dot", Slate);
            n.Dot.sprite = ProceduralSprites.Circle();
            RectTransform drt = n.Dot.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f);
            drt.pivot = new Vector2(0.5f, 0.5f);

            GameObject tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(n.Root, false);
            n.Text = tgo.GetComponent<RectTransform>();
            n.Text.anchorMin = n.Text.anchorMax = new Vector2(0f, 1f);
            n.Text.pivot = new Vector2(0f, 1f);
            n.TextGroup = EnsureGroup(n.Text);

            n.Date = ContentCardUIBuilder.CreateText(n.Text, "Date", ContentCardUIBuilder.TextPrimary, TimelineTypography.DatePx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(n.Date, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
            n.Event = ContentCardUIBuilder.CreateText(n.Text, "Event", ContentCardUIBuilder.TextPrimary, TimelineTypography.EventMaxPx, TextAlignmentOptions.TopLeft);
            CardTypography.PrepareText(n.Event, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);

            ngo.SetActive(false);
            nodes[i] = n;
        }
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        content.Clear();
        content.Add((data.primaryText ?? "", data.secondaryText ?? ""));
        shown = 1;
        nodes[0].AddedAt = float.NaN; // the opening node lands with the card
        glideStart = float.NaN;       // so the layout puts the stack at rest
        LayoutCard(log: true);
        StepNodes();
    }

    /// <summary>
    /// Adds the next node on its beat: the line draws down to it, its dot
    /// pops, its text slides in, and the stack re-centres. <paramref name="instant"/>
    /// lands it at once (a node whose beat passed while the card waited in the
    /// queue). False when the timeline already holds five nodes.
    /// </summary>
    public bool AppendNode(string date, string evt, bool instant = false)
    {
        if (shown >= TimelineTypography.MaxNodes) return false;
        content.Add((date ?? "", evt ?? ""));
        Node prev = nodes[shown - 1];
        Node n = nodes[shown];
        shown++;

        float before = stackRoot.anchoredPosition.y; // keep the glide continuous
        LayoutCard(log: false);
        n.AddedAt = instant ? clock - TimelineTypography.EntranceSeconds : clock;
        prev.HandedOverAt = n.AddedAt + TimelineTypography.PopStart;
        glideFrom = -before;
        glideStart = instant ? float.NaN : clock;
        if (instant) stackRoot.anchoredPosition = new Vector2(stackRoot.anchoredPosition.x, -glideTo);
        StepNodes();
        return true;
    }

    void LateUpdate()
    {
        if (CardTypography.CardSize(rectTransform) != laidOutSize) LayoutCard(log: false);
    }

    // =====================================================================
    // Layout
    // =====================================================================

    // The nodes the card is sized for: what's shown plus what's coming.
    private List<(string date, string evt)> SizingNodes()
    {
        var all = new List<(string, string)>(content);
        if (AnticipatedNodes != null)
            for (int i = all.Count; i < AnticipatedNodes.Count && all.Count < TimelineTypography.MaxNodes; i++)
                all.Add(AnticipatedNodes[i]);
        return all;
    }

    private float NodeTop(int i)
    {
        float y = 0f;
        for (int j = 0; j < i; j++) y += nodes[j].Height + layoutGap;
        return y;
    }

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad, innerH = size.y - 2f * pad;
        float dot = TimelineTypography.DotPx * k;
        float textX = dot + TimelineTypography.DotToTextGapPx * k;
        float textW = innerW - textX;
        float gap = TimelineTypography.NodeGapPx * k;
        float dateGap = TimelineTypography.DateToEventGapPx * k;
        float dateSize = TimelineTypography.DatePx * k;

        List<(string date, string evt)> sizing = SizingNodes();
        int resized = laidOutFor > 0 && sizing.Count > laidOutFor ? sizing.Count : 0;
        laidOutFor = sizing.Count;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Timeline", sizing[0].date + " — " + sizing[0].evt) };

        // Fill the node texts the sizing needs (unshown ones stay hidden).
        for (int i = 0; i < sizing.Count; i++)
        {
            nodes[i].Date.text = sizing[i].date;
            nodes[i].Event.text = sizing[i].evt;
        }

        float NodeHeight(int i, float ds, float ev) =>
            CardTypography.MeasureHeight(nodes[i].Date, ds, textW) + dateGap +
            CardTypography.MeasureHeight(nodes[i].Event, ev, textW);

        // One event size for every node: the largest at which each event is at
        // most two lines and the whole (anticipated) stack fits the card.
        bool Fits(float ds, float ev)
        {
            float h = 0f;
            for (int i = 0; i < sizing.Count; i++)
            {
                if (!FitsLines(nodes[i].Event, ev, textW, TimelineTypography.EventMaxLines)) return false;
                h += NodeHeight(i, ds, ev);
            }
            return h + (sizing.Count - 1) * gap <= innerH;
        }
        CardTypography.BodyFit fit = CardTypography.FitBody(ev => Fits(dateSize, ev),
            TimelineTypography.EventMinPx * k, TimelineTypography.EventMaxPx * k,
            TimelineTypography.EventMinPx * k, CardTypography.HardFloorPx * k);
        // Five long events can't fit even at 32 px: the dates shrink too (to
        // 32 at the least) and the gaps between nodes with them (to 12), before
        // anything is allowed past the padding.
        bool datesShrunk = false;
        if (fit.Overflows)
        {
            float date0 = dateSize, gap0 = gap, floor = CardTypography.HardFloorPx * k;
            for (float ds = date0 - 2f * k; ds >= floor - 0.01f; ds -= 2f * k)
            {
                float d = ds;
                gap = Mathf.Lerp(gap0, TimelineTypography.MinNodeGapPx * k, Mathf.InverseLerp(date0, floor, ds));
                float ev = CardTypography.LargestFitting(e => Fits(d, e), floor, Mathf.Min(d, TimelineTypography.EventMinPx * k));
                if (float.IsNaN(ev)) continue;
                dateSize = ds;
                fit = new CardTypography.BodyFit { Size = ev, BelowWarnFloor = true };
                datesShrunk = true;
                break;
            }
            if (!datesShrunk) gap = gap0;
        }
        EventSize = fit.Size;
        DateSize = dateSize;
        layoutGap = gap;

        var heights = new List<float>(sizing.Count);
        for (int i = 0; i < sizing.Count; i++)
        {
            Node n = nodes[i];
            n.Date.fontSize = dateSize;
            n.Event.fontSize = fit.Size;
            float dh = CardTypography.MeasureHeight(n.Date, dateSize, textW);
            float eh = CardTypography.MeasureHeight(n.Event, fit.Size, textW);
            n.Text.sizeDelta = new Vector2(textW, dh + dateGap + eh);
            n.Text.anchoredPosition = new Vector2(textX, 0f);
            CardTypography.PlaceBox(n.Date, 0f, 0f, textW, dh);
            CardTypography.PlaceBox(n.Event, 0f, dh + dateGap, textW, eh);
            n.Height = dh + dateGap + eh;
            heights.Add(n.Height);

            // The dot on the date's capitals.
            float capTop = CapTopOffset(n.Date, dateSize);
            float cap = CardTypography.CapHeight(n.Date.font, dateSize);
            n.DotY = capTop + cap * 0.5f;
            n.Dot.rectTransform.sizeDelta = new Vector2(dot, dot);
            n.Dot.rectTransform.anchoredPosition = new Vector2(dot * 0.5f, -n.DotY);
        }
        for (int i = 0; i < TimelineTypography.MaxNodes; i++)
            nodes[i].Root.anchoredPosition = new Vector2(0f, -TimelineTypography.NodeTop(heights, i, gap));

        report.Add("dates", nodes[0].Date, dateSize / k, TimelineTypography.DateFloorPx);
        report.Add("events", nodes[0].Event, fit.Size / k, TimelineTypography.EventMinPx);
        float full = TimelineTypography.StackHeight(heights, sizing.Count, gap);
        report.GroupHeight = full;
        report.InnerHeight = innerH;
        if (fit.Overflows)
            report.Warn($"{report.Card}: the events don't fit {TimelineTypography.EventMaxLines} lines each even at {CardTypography.HardFloorPx:F0} px — shown on more lines.");
        else if (datesShrunk)
            report.Warn($"{report.Card}: {sizing.Count} nodes only fit with the dates at {dateSize / k:F0} px and the events at " +
                        $"{fit.Size / k:F0} px — under the {TimelineTypography.DateFloorPx:F0} / {TimelineTypography.EventMinPx:F0} px floors.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: the events only fit at {fit.Size / k:F0} px — under the {TimelineTypography.EventMinPx:F0} px floor.");
        if (full > innerH + 0.5f)
            report.Warn($"{report.Card}: {sizing.Count} nodes are taller than the card — the timeline runs past the padding.");

        // The line runs down the dots' centre.
        line.rectTransform.anchoredPosition = new Vector2(dot * 0.5f, -nodes[0].DotY);

        glideTo = TimelineTypography.StackTop(pad, innerH, TimelineTypography.StackHeight(heights, shown, gap));
        stackRoot.sizeDelta = new Vector2(innerW, full);
        float x = pad;
        stackRoot.anchoredPosition = new Vector2(x, stackRoot.anchoredPosition.y);
        if (float.IsNaN(glideStart)) stackRoot.anchoredPosition = new Vector2(x, -glideTo);

        Layout = report;
        if (log || resized > 0)
        {
            report.LogSummary();
            if (resized > 0)
                Debug.Log($"[Cards] {report.Card}: RE-FITTED for {resized} nodes — a node the read-ahead didn't see arrived.");
        }
    }

    // Whether a text wraps to at most `maxLines` rows at a size and width —
    // from TMP's preferred height against the same number of rows of a probe,
    // so it holds for nodes not shown yet (their mesh isn't built).
    private static bool FitsLines(TMP_Text t, float size, float width, int maxLines)
    {
        if (string.IsNullOrEmpty(t.text)) return true;
        t.fontSize = size;
        float h = t.GetPreferredValues(t.text, width, 0f).y;
        var probe = new System.Text.StringBuilder("Ag");
        for (int i = 1; i < maxLines; i++) probe.Append("\nAg");
        float limit = t.GetPreferredValues(probe.ToString(), width, 0f).y;
        return h <= limit + 0.5f;
    }

    private static float CapTopOffset(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        float scale = size / Mathf.Max(1f, fi.pointSize) * Mathf.Max(0.0001f, fi.scale);
        return (fi.ascentLine - fi.capLine) * scale;
    }

    // =====================================================================
    // Per frame (the card's clock — no chained tweens)
    // =====================================================================

    /// <summary>True once the timeline is on its way out (its End, or a fast hide):
    /// a node arriving now belongs to the next timeline, not this one.</summary>
    public bool IsClosing { get; private set; }

    public override void Hide(bool fast = false)
    {
        IsClosing = true;
        base.Hide(fast);
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame; public so the self-tests can step the card in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        StepNodes();
    }

    private void StepNodes()
    {
        float k = CardTypography.ScaleFor(laidOutSize);
        Color accent = ContentCardUIBuilder.AccentColor;
        Color text = ContentCardUIBuilder.TextPrimary;

        for (int i = 0; i < TimelineTypography.MaxNodes; i++)
        {
            Node n = nodes[i];
            bool on = i < shown;
            n.Root.gameObject.SetActive(on);
            if (!on) continue;

            float sincePop = float.IsNaN(n.AddedAt) ? 10f : clock - n.AddedAt - TimelineTypography.PopStart;
            n.Dot.rectTransform.localScale = Vector3.one * TimelineTypography.DotScaleAt(sincePop);
            float slide = TimelineTypography.TextAt(sincePop);
            n.TextGroup.alpha = sincePop <= 0f ? 0f : slide;
            n.Text.anchoredPosition = new Vector2(
                TimelineTypography.DotPx * k + TimelineTypography.DotToTextGapPx * k + (1f - slide) * TimelineTypography.TextSlidePx * k, 0f);

            // The newest node holds the accent; one handed over settles to 70 %.
            float settled = float.IsNaN(n.HandedOverAt) ? 0f : TimelineTypography.SettleAt(clock - n.HandedOverAt);
            n.Dot.color = Color.Lerp(accent, Slate, settled);
            n.Date.color = Color.Lerp(accent, text, settled);
            n.Group.alpha = Mathf.Lerp(1f, TimelineTypography.OlderAlpha, settled);
        }

        // The line: down to the newest dot, drawing the last stretch on its beat.
        float lineLen = 0f;
        if (shown >= 2)
        {
            float prevDot = DotCentre(shown - 2), lastDot = DotCentre(shown - 1);
            Node last = nodes[shown - 1];
            float drawn = float.IsNaN(last.AddedAt) ? 1f : TimelineTypography.LineDrawAt(clock - last.AddedAt);
            lineLen = (prevDot - DotCentre(0)) + (lastDot - prevDot) * drawn;
        }
        line.enabled = lineLen > 0.5f;
        line.rectTransform.sizeDelta = new Vector2(TimelineTypography.LineWidthPx * k, lineLen);

        // Re-centre the stack, eased.
        if (!float.IsNaN(glideStart))
        {
            float g = TimelineTypography.RecentreAt(clock - glideStart);
            stackRoot.anchoredPosition = new Vector2(stackRoot.anchoredPosition.x, -Mathf.Lerp(glideFrom, glideTo, g));
            if (g >= 1f) glideStart = float.NaN;
        }
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}

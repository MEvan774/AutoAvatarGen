using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Tts;
using MugsTech.Style;

/// <summary>
/// {Timeline:…} — the tag through every registration site (runtime parser,
/// TTS strip + stamp, tag-only unstamp), the entrance curves and stack maths
/// (pure), the real card with 1, 3 and 5 nodes and a 2-line event, a node
/// stepped in frame by frame, and the controller's node rules — a side on a
/// later node, a 6th node, a timeline wiped by a transition before its End.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Timeline Self-Tests")]
    public static void RunTimeline()
    {
        var log = new Log();
        RunTimelineGroup(log);
        log.Report();
    }

    static void RunTimelineGroup(Log log)
    {
        Timeline_TagParsing(log);
        Timeline_StampUnstampRoundTrip(log);
        Timeline_Curves(log);
        Timeline_ReadAhead(log);

        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Timeline_CardLayout(log);
            Timeline_SixthNodeIsRefused(log);
            Timeline_NodeEntranceFrameByFrame(log);
            Timeline_ControllerRules(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"Timeline checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
            GameObject sfx = GameObject.Find("TagSfxPlayer (auto)");
            if (sfx != null) UnityEngine.Object.DestroyImmediate(sfx);
        }
    }

    // -----------------------------------------------------------------------
    // The tag through the registration sites
    // -----------------------------------------------------------------------

    static readonly (string date, string evt)[] FreezerNodes =
    {
        ("Sun. 11 Oct.", "Freezer controls taken over overnight"),
        ("Mon. 12 Oct.", "Stores throw the food out"),
        ("Oct.", "CERT-FR: attacks probably still under way"),
    };

    static void Timeline_TagParsing(Log log)
    {
        string script =
            "{Timeline:\"Sun. 11 Oct.\",\"Freezer controls taken over overnight\",Right} words\n" +
            "{Timeline:\"Mon. 12 Oct.\",\"Stores throw the food out\"} more\n" +
            "{Timeline:\"Oct.\",\"CERT-FR: attacks probably still under way\",Left}\n" +
            "{Timeline:End}\n" +
            "{Timeline:\"stamped\",\"node\",T=12.500,D=0,Left}\n" +
            "{Timeline:\"only one\"}\n" +
            "{Timeline:\"a\",\"b\",5}\n" +
            "{Timeline:\"\",\"empty date\"}\n";
        var (clean, events) = ContentZoneTagParser.ParseContentTags(script, 30f);
        var nodes = events.FindAll(e => e.cardType == ContentCardType.Timeline && !e.dismissesCard);
        var ends = events.FindAll(e => e.cardType == ContentCardType.Timeline && e.dismissesCard);

        log.AreEqual(4, nodes.Count, "four valid Timeline nodes (three malformed tags are skipped)");
        log.AreEqual(1, ends.Count, "{Timeline:End} closes the timeline");
        log.IsTrue(!clean.Contains("{Timeline"), "every Timeline tag — node, End, malformed — is stripped from the text");
        ContentCardEvent first = nodes.Find(e => e.primaryText == "Sun. 11 Oct.");
        log.IsTrue(first != null && first.secondaryText == "Freezer controls taken over overnight" && first.duration == 0f &&
                   first.entryDirectionOverride == EntryDirection.FromRight, "a node keeps its date, event, no timer, and its side");
        log.IsTrue(nodes.Exists(e => e.primaryText == "Mon. 12 Oct." && e.entryDirectionOverride == null), "a node with no side");
        log.IsTrue(nodes.Exists(e => e.primaryText == "Oct." && e.entryDirectionOverride == EntryDirection.FromLeft),
            "a later node's side parses (the controller ignores it, with a warning)");
        ContentCardEvent stamped = nodes.Find(e => e.primaryText == "stamped");
        log.IsTrue(stamped != null && Mathf.Approximately(stamped.triggerTime, 12.5f) && stamped.entryDirectionOverride == EntryDirection.FromLeft,
            "a stamped node (T=…, D=0, Left) fires at its T=");

        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Timeline:\"Sun. 11 Oct.\",\"Freezer controls taken over overnight\",Right}"),
            "the TTS processor recognises a node with a side");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Timeline:\"Mon. 12 Oct.\",\"Stores throw the food out\"}"), "…one without");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Timeline:End}"), "…and its End");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Timeline:\"a\",\"b\",5}") && !TtsScriptProcessor.IsRecognisedMarker("{Timeline:\"only one\"}"),
            "…but not a malformed one (no duration slot; two fields)");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch("{Timeline:\"Oct.\",\"x\"}") && !HybridAvatarSystem.EmotionTagRegex.IsMatch("{Timeline:End}"),
            "the emotion parser can't mistake it for an emotion");
        log.IsTrue(!ContentZoneController.IsFeatureCard(ContentCardType.Timeline), "Timeline is a side card (hidden at Center)");
    }

    static void Timeline_StampUnstampRoundTrip(Log log)
    {
        string script =
            "## TWO SUNDAYS\n" +
            "Sunday number one. {Timeline:\"Sun. 11 Oct.\",\"Freezer controls taken over overnight\",Right}\n" +
            "Someone took over the freezer controls.\n" +
            "{Timeline:\"Mon. 12 Oct.\",\"Stores throw the food out\"}\n" +
            "By Monday, stores were throwing the food out.\n" +
            "{Timeline:\"Oct.\",\"CERT-FR: attacks probably still under way\"}\n" +
            "Attack campaigns are probably still under way.\n" +
            "{Timeline:End}\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains("overnight\",T=") && section.TimedScript.Contains(",D=0,Right}") &&
                       section.TimedScript.Contains("food out\",T="), "stamping gives every node a T= and D=0, keeping the first node's side");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "Unstamp(Stamp(x)) gives back the Timeline tags as written");

            var (_, events) = ContentZoneTagParser.ParseContentTags(section.TimedScript, 30f);
            var nodes = events.FindAll(e => e.cardType == ContentCardType.Timeline && !e.dismissesCard);
            log.AreEqual(3, nodes.Count, "the stamped nodes parse at playback");
            log.IsTrue(nodes.Count == 3 && nodes[0].triggerTime < nodes[1].triggerTime && nodes[1].triggerTime < nodes[2].triggerTime,
                "…each on its own beat, in order");
            log.AreEqual(1, events.FindAll(e => e.cardType == ContentCardType.Timeline && e.dismissesCard).Count, "…and so does the End");
        }
    }

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Timeline_Curves(Log log)
    {
        log.AreEqual(0f, TimelineTypography.LineDrawAt(0f), "the line starts drawing from the previous dot");
        log.Near(1f, TimelineTypography.LineDrawAt(0.3f), 1e-6f, "…and reaches the new one in 0.3 s");
        log.AreEqual(0f, TimelineTypography.DotScaleAt(0f), "the dot is not there before it pops");
        float peak = 0f;
        for (float t = 0f; t <= TimelineTypography.DotPopSeconds; t += 0.002f) peak = Mathf.Max(peak, TimelineTypography.DotScaleAt(t));
        log.IsTrue(peak > 1.02f && peak < 1.2f, $"…it pops with a small overshoot (peak {peak:F3}×)");
        log.Near(1f, TimelineTypography.DotScaleAt(TimelineTypography.DotPopSeconds), 1e-6f, "…and settles at 1.0");
        log.Near(1f, TimelineTypography.TextAt(TimelineTypography.TextSlideSeconds), 1e-6f, "the text has slid in after 0.3 s");
        log.Near(0.5f, TimelineTypography.SettleAt(0.15f), 1e-5f, "an older node is halfway settled at 0.15 s");
        log.Near(0.3f, TimelineTypography.PopStart, 1e-6f, "the dot pops as the line arrives");

        var heights = new List<float> { 100f, 140f, 120f };
        log.Near(100f + 140f + 30f, TimelineTypography.StackHeight(heights, 2, 30f), 1e-4f, "two nodes stack with one gap");
        log.Near(100f + 30f + 140f + 30f, TimelineTypography.NodeTop(heights, 2, 30f), 1e-4f, "the third node starts below two nodes and two gaps");
        log.Near(70f + (660f - 270f) * 0.5f, TimelineTypography.StackTop(70f, 660f, 270f), 1e-4f, "the stack is centred in the inner area");
        log.Near(70f, TimelineTypography.StackTop(70f, 660f, 900f), 1e-4f, "…never above its top");
    }

    static ContentCardEvent TL(string date, string evt, float t, bool end = false, EntryDirection? side = null) =>
        new ContentCardEvent { cardType = ContentCardType.Timeline, primaryText = date, secondaryText = evt, triggerTime = t, dismissesCard = end, entryDirectionOverride = side };

    static void Timeline_ReadAhead(Log log)
    {
        var opener = TL("A", "a", 1f, side: EntryDirection.FromRight);
        var timeline = new List<ContentCardEvent>
        {
            opener,
            new ContentCardEvent { cardType = ContentCardType.Stat, triggerTime = 1.5f, primaryText = "4x" },
            TL("B", "b", 2f), TL("C", "c", 3f), TL(null, null, 4f, end: true), TL("D", "next timeline", 5f),
        };
        List<ContentCardEvent> nodes = ContentZoneController.AnticipateTimelineNodes(timeline, opener);
        log.AreEqual(3, nodes.Count, "read-ahead: the opener and its nodes up to its End (other cards skipped)");
        log.IsTrue(nodes[2].primaryText == "C", "…in order, stopping at the End");

        var many = new List<ContentCardEvent> { opener };
        for (int i = 0; i < 7; i++) many.Add(TL("N" + i, "n", 2f + i));
        log.AreEqual(TimelineTypography.MaxNodes, ContentZoneController.AnticipateTimelineNodes(many, opener).Count,
            "read-ahead stops at five nodes");
        log.AreEqual(1, ContentZoneController.AnticipateTimelineNodes(many, TL("X", "x", 0f)).Count,
            "an opener not in the timeline gets only itself");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    static TimelineCard BuildTimeline(IList<(string date, string evt)> all, int show, out GameObject root, bool instant = true)
    {
        var card = BuildCard<TimelineCard>(out root);
        card.AnticipatedNodes = new List<(string, string)>(all);
        card.Initialize(new ContentCardEvent { cardType = ContentCardType.Timeline, primaryText = all[0].date, secondaryText = all[0].evt }, null);
        for (int i = 1; i < show; i++) card.AppendNode(all[i].date, all[i].evt, instant);
        if (instant) for (int f = 0; f < 30; f++) card.Advance(1f / 30f);
        return card;
    }

    static void CheckTimeline(Log log, TimelineCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        Rect inner = InnerRect(card);
        bool tallWarned = report.Warnings.Exists(w => w.Contains("taller than the card") || w.Contains("don't fit"));
        int n = card.NodeCount;

        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name), $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        log.IsTrue(card.EventSize <= TimelineTypography.EventMaxPx + 0.01f, $"{what}: events at most 48 px ({card.EventSize:F0})");

        for (int i = 0; i < n; i++)
        {
            foreach (TMP_Text t in new TMP_Text[] { card.DateText(i), card.EventText(i) })
            {
                log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f, $"{what}: {t.name} {i} at or above 32 px ({t.fontSize:F1})");
                log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} {i} not truncated");
                Rect ink = CardTypography.InkInCard(t, rt);
                log.IsTrue(tallWarned || Inside(ink, inner, 1f), $"{what}: {t.name} {i} inside the padding ({Fmt(ink)} in {Fmt(inner)})");
            }
            log.IsTrue(Mathf.Abs(card.DateText(i).fontSize - TimelineTypography.DatePx) < 0.01f || report.WarnedAbout("dates"),
                $"{what}: date {i} is 44 px (or the shrink is logged) ({card.DateText(i).fontSize:F0})");
            card.EventText(i).ForceMeshUpdate(true, true);
            log.IsTrue(card.EventText(i).textInfo.lineCount <= TimelineTypography.EventMaxLines || tallWarned,
                $"{what}: event {i} on at most two lines ({card.EventText(i).textInfo.lineCount})");

            Rect dot = RectInCard(card.Dot(i).rectTransform, rt);
            Rect dot0 = RectInCard(card.Dot(0).rectTransform, rt);
            log.Near(dot0.center.x, dot.center.x, 0.5f, $"{what}: dot {i} on the line");
            log.Near(1f, card.Dot(i).rectTransform.localScale.x, 1e-4f, $"{what}: dot {i} has popped");
            log.Near(1f, card.TextAlpha(i), 1e-4f, $"{what}: text {i} has slid in");
            Rect date = CardTypography.InkInCard(card.DateText(i), rt);
            log.IsTrue(dot.center.y > date.yMin && dot.center.y < date.yMax, $"{what}: dot {i} sits level with its date's capitals");
            log.IsTrue(dot.xMax < date.xMin, $"{what}: date {i} is to the dot's right");

            bool newest = i == n - 1;
            log.IsTrue(newest ? card.Dot(i).color == ContentCardUIBuilder.AccentColor : card.Dot(i).color == TimelineCard.Slate,
                $"{what}: dot {i} {(newest ? "in the accent (newest)" : "neutral (older)")}");
            log.Near(newest ? 1f : TimelineTypography.OlderAlpha, card.NodeAlpha(i), 1e-3f, $"{what}: node {i} at {(newest ? "100" : "70")} %");
        }

        // The stack is centred for the nodes shown; the line runs dot to dot.
        Rect top = RectInCard(card.NodeText(0), rt), bottom = RectInCard(card.NodeText(n - 1), rt);
        float stackH = bottom.yMax - top.yMin;
        if (stackH <= inner.height)
            log.Near(inner.center.y, (top.yMin + bottom.yMax) * 0.5f, 1.5f, $"{what}: the {n}-node stack is centred");
        if (n >= 2)
        {
            Rect lineR = RectInCard(card.Line.rectTransform, rt);
            log.Near(RectInCard(card.Dot(0).rectTransform, rt).center.y, lineR.yMin, 0.5f, $"{what}: the line starts at the first dot");
            log.Near(RectInCard(card.Dot(n - 1).rectTransform, rt).center.y, lineR.yMax, 0.5f, $"{what}: …and reaches the newest");
        }
        else log.IsTrue(!card.Line.enabled, $"{what}: one node, no line yet");

        Debug.Log($"[Timeline] self-test {what}: {n} node(s), events {card.EventSize:F0} px, stack {stackH:F0}/{inner.height:F0} px" +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Timeline_CardLayout(Log log)
    {
        var five = new[]
        {
            ("2019", "Founded in Shanghai"), ("2021", "Moves to the Cayman Islands"), ("Mar. 2023", "First regulator warning"),
            ("Oct. 2026", "Hot wallets drained"), ("Nov. 2", "FBI names the attackers"),
        };
        var fiveLong = new[]
        {
            ("Sun. 11 Oct. 2026", "Freezer controls in hundreds of supermarkets are taken over overnight"),
            ("Mon. 12 Oct. 2026", "Stores across the country throw out the spoiled food and close their aisles"),
            ("Tue. 13 Oct. 2026", "The finance minister puts the number of affected stores at about seven hundred"),
            ("Late October 2026", "CERT-FR warns that attack campaigns on this equipment are probably still under way"),
            ("Sun. 1 Nov. 2026", "A second wave hits bakeries and pharmacies on the other side of the border"),
        };
        var cases = new List<(IList<(string, string)> all, int show, string what)>
        {
            (FreezerNodes, 1, "1 node"),
            (FreezerNodes, 3, "3 nodes (Freezer)"),
            (five, 5, "5 nodes"),
            (new[] { ("Sun. 11 Oct.", "Freezer controls in hundreds of French supermarkets taken over overnight"), FreezerNodes[1] }, 2, "a 2-line event"),
            (fiveLong, 5, "5 nodes, 2-line events"),
        };
        foreach (var c in cases)
        {
            var card = BuildTimeline(c.all, c.show, out GameObject root);
            try
            {
                CheckTimeline(log, card, $"Timeline ({c.what})");
                if (c.what == "a 2-line event")
                {
                    card.EventText(0).ForceMeshUpdate(true, true);
                    log.AreEqual(2, card.EventText(0).textInfo.lineCount, "Timeline (a 2-line event): the long event wraps onto two lines");
                    log.IsTrue(card.Layout.Warnings.Count == 0 && card.EventSize >= TimelineTypography.EventMinPx,
                        $"Timeline (a 2-line event): at {card.EventSize:F0} px, no warning");
                }
                if (c.what == "3 nodes (Freezer)" || c.what == "5 nodes")
                    log.IsTrue(card.Layout.Warnings.Count == 0, $"Timeline ({c.what}): no warnings");
                if (c.what == "5 nodes, 2-line events")
                    log.IsTrue(card.Layout.GroupHeight <= card.Layout.InnerHeight + 0.5f && card.DateSize < TimelineTypography.DatePx,
                        $"Timeline ({c.what}): the dates shrink too, so all five still fit inside the card ({card.Layout.GroupHeight:F0}/{card.Layout.InnerHeight:F0} px, dates {card.DateSize:F0})");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Sized once from the read-ahead: adding nodes moves the stack, never resizes it.
        var grow = BuildTimeline(FreezerNodes, 1, out GameObject groot);
        try
        {
            float size1 = grow.EventSize;
            grow.AppendNode(FreezerNodes[1].date, FreezerNodes[1].evt, instant: true);
            grow.AppendNode(FreezerNodes[2].date, FreezerNodes[2].evt, instant: true);
            log.Near(size1, grow.EventSize, 0.01f, "Timeline: sized once from all its nodes — adding them doesn't change the type size");
        }
        finally { UnityEngine.Object.DestroyImmediate(groot); }
    }

    static void Timeline_SixthNodeIsRefused(Log log)
    {
        var five = new[] { ("1", "a"), ("2", "b"), ("3", "c"), ("4", "d"), ("5", "e") };
        var card = BuildTimeline(five, 5, out GameObject root);
        try
        {
            log.IsTrue(!card.AppendNode("6", "f"), "Timeline: a 6th node is refused");
            log.AreEqual(5, card.NodeCount, "…and the timeline keeps its five");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // A second node at 30 fps: the line draws down (0.3 s), then the dot pops
    // and the text slides in, the first node hands over the accent and
    // settles to 70 %, and the stack glides to its new centre.
    static void Timeline_NodeEntranceFrameByFrame(Log log)
    {
        var card = BuildTimeline(FreezerNodes, 1, out GameObject root, instant: false);
        try
        {
            var rt = (RectTransform)card.transform;
            float top0 = RectInCard(card.NodeText(0), rt).yMin;
            card.AppendNode(FreezerNodes[1].date, FreezerNodes[1].evt);
            const float dt = 1f / 30f;
            float t = 0f, lineFullAt = float.NaN, popAt = float.NaN, textInAt = float.NaN, settledAt = float.NaN, glideDoneAt = float.NaN;
            float fullLine = card.DotCentre(1) - card.DotCentre(0);
            float lastLine = -1f, lastTop = top0;
            bool lineMonotonic = true, glideMonotonic = true, accentHeldUntilPop = true;
            for (int f = 0; f < 45; f++)
            {
                card.Advance(dt); t += dt;
                float len = card.Line.enabled ? card.Line.rectTransform.sizeDelta.y : 0f;
                if (len < lastLine - 0.01f) lineMonotonic = false;
                lastLine = len;
                if (float.IsNaN(lineFullAt) && len >= fullLine - 0.5f) lineFullAt = t;
                if (float.IsNaN(popAt) && card.Dot(1).rectTransform.localScale.x > 0f) popAt = t;
                if (float.IsNaN(textInAt) && card.TextAlpha(1) >= 1f - 1e-4f) textInAt = t;
                if (t < TimelineTypography.PopStart - 1e-3f && card.Dot(0).color != ContentCardUIBuilder.AccentColor) accentHeldUntilPop = false;
                if (float.IsNaN(settledAt) && Mathf.Abs(card.NodeAlpha(0) - TimelineTypography.OlderAlpha) < 1e-3f) settledAt = t;
                float top = RectInCard(card.NodeText(0), rt).yMin;
                if (top > lastTop + 0.01f) glideMonotonic = false;
                lastTop = top;
                if (float.IsNaN(glideDoneAt) && Mathf.Abs(top - (card.StackRestTop)) < 0.5f && f > 0) glideDoneAt = t;
            }
            log.IsTrue(lineMonotonic, "the line only ever grows");
            log.IsTrue(lineFullAt >= 0.3f - dt - 1e-3f && lineFullAt <= 0.3f + dt + 1e-3f, $"…reaching the new dot in 0.3 s ({lineFullAt:F3})");
            log.IsTrue(popAt > 0.3f - 1e-3f && popAt <= 0.3f + dt + 1e-3f, $"the dot pops as the line arrives ({popAt:F3} s)");
            log.IsTrue(textInAt <= 0.3f + TimelineTypography.TextSlideSeconds + dt + 1e-3f, $"the text has slid in 0.3 s later ({textInAt:F3})");
            log.IsTrue(accentHeldUntilPop, "the first node keeps the accent until the new dot pops");
            log.IsTrue(settledAt <= 0.3f + TimelineTypography.SettleSeconds + dt + 1e-3f, $"…then settles to 70 % ({settledAt:F3} s)");
            log.IsTrue(card.Dot(1).color == ContentCardUIBuilder.AccentColor && card.Dot(0).color == TimelineCard.Slate,
                "the accent has moved to the newest node");
            log.IsTrue(glideMonotonic, "the stack glides up to re-centre (no jump back)");
            log.IsTrue(glideDoneAt <= TimelineTypography.RecentreSeconds + dt + 1e-3f, $"…and is centred within 0.35 s ({glideDoneAt:F3})");
            log.IsTrue(RectInCard(card.NodeText(0), rt).yMin < top0 - 1f, "the first node moved up to make room");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // The node rules on a real ContentZoneController (edit mode: a card with
    // nothing on screen is built synchronously).
    static void Timeline_ControllerRules(Log log)
    {
        var root = new GameObject("TimelineControllerSelfTest", typeof(RectTransform), typeof(Canvas));
        var warnings = new List<string>();
        var infos = new List<string>();
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(msg);
            else if (type == LogType.Log) infos.Add(msg);
        }
        Application.logMessageReceived += OnLog;
        try
        {
            var zone = new GameObject("Zone", typeof(RectTransform)).GetComponent<RectTransform>();
            zone.SetParent(root.transform, false);
            zone.anchorMin = zone.anchorMax = new Vector2(0f, 0.5f);
            zone.sizeDelta = new Vector2(CardTypography.ReferenceCardWidth, CardTypography.ReferenceCardHeight);
            var czGo = new GameObject("CZ");
            czGo.transform.SetParent(root.transform, false);
            var cz = czGo.AddComponent<ContentZoneController>();
            cz.contentZone = zone;

            var n1 = TL("Sun. 11 Oct.", "Freezer controls taken over overnight", 1f, side: EntryDirection.FromRight);
            var n2 = TL("Mon. 12 Oct.", "Stores throw the food out", 2f, side: EntryDirection.FromLeft);
            var n3 = TL("Oct.", "CERT-FR: attacks probably still under way", 3f);
            var n4 = TL("4", "d", 4f);
            var n5 = TL("5", "e", 5f);
            var n6 = TL("6", "f", 6f);
            var end = TL(null, null, 7f, end: true);
            var after = TL("after", "ignored", 8f);
            var end2 = TL(null, null, 9f, end: true);
            var next = TL("Next", "a new timeline", 10f);
            cz.SetTimeline(new List<ContentCardEvent> { n1, n2, n3, n4, n5, n6, end, after, end2, next }, null);

            cz.ShowCard(n1);
            var tl = cz.ActiveCard as TimelineCard;
            log.IsTrue(tl != null && tl.NodeCount == 1, "controller: the first node opens a Timeline");
            log.IsTrue(tl != null && tl.transform.parent != null && tl.transform.parent.name == "ContentZone_Cards_Right",
                "…on the first node's side (,Right)");
            log.IsTrue(cz.IsCardActive && !cz.ActiveCardIsFeature, "…as an ordinary side card — what {PointCard} and the media gate see");
            log.IsTrue(infos.Exists(m => m.Contains("Timeline shown (side=Right)")), "…logged as \"[Card] t=… Timeline shown (side=Right)\"");

            warnings.Clear();
            cz.ShowCard(n2);
            log.IsTrue(cz.ActiveCard == tl && tl.NodeCount == 2, "controller: the next node is added to the open timeline");
            log.IsTrue(warnings.Exists(w => w.Contains("on a later node is ignored")), "…and its ,Left is ignored with a warning");
            log.IsTrue(tl.transform.parent.name == "ContentZone_Cards_Right", "…the card stays on the first node's side");
            log.IsTrue(infos.Exists(m => m.Contains("Timeline node 2 shown")), "…logged as \"Timeline node 2 shown\"");

            cz.ShowCard(n3); cz.ShowCard(n4); cz.ShowCard(n5);
            warnings.Clear();
            cz.ShowCard(n6);
            log.AreEqual(5, tl.NodeCount, "controller: five nodes at most");
            log.IsTrue(warnings.Exists(w => w.Contains("at most 5 nodes")), "…a 6th logs a warning and is ignored");

            warnings.Clear();
            cz.ClearForTransition();
            log.IsTrue(cz.ActiveCard == null, "controller: a transition wipes the timeline");
            log.IsTrue(warnings.Exists(w => w.Contains("cleared by a transition before {Timeline:End}")), "…before its End: warned");
            cz.ShowCard(after);
            log.IsTrue(cz.ActiveCard == null, "…and a node arriving after that is ignored (no new timeline)");
            warnings.Clear();
            cz.ShowCard(end);
            log.IsTrue(!warnings.Exists(w => w.Contains("Timeline")), "…its End is a quiet no-op");
            cz.ShowCard(next);
            var tl2 = cz.ActiveCard as TimelineCard;
            log.IsTrue(tl2 != null && tl2 != tl && tl2.NodeCount == 1, "after that End, the next node opens a new timeline");

            cz.ShowCard(end2);
            log.IsTrue(tl2 != null && tl2.IsClosing, "{Timeline:End} closes it");
            var opener = TL("New", "during the fade", 11f);
            cz.ShowCard(opener);
            log.AreEqual(1, tl2.NodeCount, "a node arriving while the old timeline fades out is not added to it");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}

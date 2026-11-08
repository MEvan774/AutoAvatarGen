using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Tts;

/// <summary>
/// {Versus:…} — the tag through every registration site (runtime parser, TTS
/// strip + stamp, tag-only unstamp), the panel / glyph geometry and the
/// entrance timing (pure), and the real card laid out with TMP and stepped
/// frame by frame.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Versus Self-Tests")]
    public static void RunVersus()
    {
        var log = new Log();
        RunVersusGroup(log);
        log.Report();
    }

    static void RunVersusGroup(Log log)
    {
        Versus_TagParsing(log);
        Versus_StampUnstampRoundTrip(log);
        Versus_Geometry(log);
        Versus_StampCurve(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Versus_CardLayout(log);
            Versus_EntranceFrameByFrame(log);
            Versus_HeldAndTimedClose(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"Versus layout checks threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // The tag through the registration sites
    // -----------------------------------------------------------------------

    const string VersusTag = "{Versus:\"Binance says\",\"No private key leaked\",\"The on-chain record says\",\"Eleven of its own keys signed\",Start}";

    static void Versus_TagParsing(Log log)
    {
        string script =
            VersusTag + " words\n" +
            "more words {Versus:End}\n" +
            "{Versus:\"They say\",\"Safe\",\"The data says\",\"Not safe\",6} later\n" +
            "{Versus:\"Claim\",\"A\",\"Record\",\"B\",Start,Vs}\n" +
            "{Versus:\"stamped\",\"x\",\"y\",\"z\",T=12.500,D=0,Vs}\n" +
            "{Versus:\"only\",\"three\",\"fields\",Start}\n" +
            "{Versus:\"a\",\"b\",\"c\",\"d\",Start,Left}\n" +
            "{Versus:\"a\",\"\",\"c\",\"d\",Start}\n";
        var (clean, events) = ContentZoneTagParser.ParseContentTags(script, 30f);
        var cards = events.FindAll(e => e.cardType == ContentCardType.Versus && !e.dismissesCard);
        var ends = events.FindAll(e => e.cardType == ContentCardType.Versus && e.dismissesCard);

        log.AreEqual(4, cards.Count, "four valid Versus tags become cards (three malformed ones are skipped)");
        log.AreEqual(1, ends.Count, "{Versus:End} closes a held Versus");
        log.IsTrue(!clean.Contains("{Versus"), "every Versus tag — valid, End, malformed — is stripped from the text");

        ContentCardEvent first = cards.Find(e => e.fields[0] == "Binance says");
        log.IsTrue(first != null && first.duration == 0f && !first.versusVs && first.entryDirectionOverride == null &&
                   string.Join("|", first.fields) == "Binance says|No private key leaked|The on-chain record says|Eleven of its own keys signed",
            "the held tag keeps its four fields in order, no timer, no side, and the ≠ glyph");
        ContentCardEvent timed = cards.Find(e => e.fields[0] == "They say");
        log.IsTrue(timed != null && Mathf.Approximately(timed.duration, 6f) && !timed.versusVs, "a timed tag keeps its duration");
        ContentCardEvent vs = cards.Find(e => e.fields[0] == "Claim");
        log.IsTrue(vs != null && vs.versusVs && vs.duration == 0f, ",Vs picks the VS glyph");
        ContentCardEvent stamped = cards.Find(e => e.fields[0] == "stamped");
        log.IsTrue(stamped != null && Mathf.Approximately(stamped.triggerTime, 12.5f) && stamped.duration == 0f && stamped.versusVs,
            "a stamped tag (T=…, D=0, Vs) fires at its T= and keeps ,Vs");

        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(VersusTag), "the TTS processor recognises the tag");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Versus:\"a\",\"b\",\"c\",\"d\",4.5,Vs}"), "…a timed one with ,Vs");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Versus:End}"), "…and its End");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Versus:\"only\",\"three\",\"fields\",Start}"),
            "…but not a malformed one (the tag-only check warns about it)");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(VersusTag) && !HybridAvatarSystem.EmotionTagRegex.IsMatch("{Versus:End}"),
            "the emotion parser can't mistake it for an emotion");
        log.IsTrue(ContentZoneController.IsFeatureCard(ContentCardType.Versus),
            "Versus is a fullscreen feature card (shows at Center, in front of the presenter)");
    }

    static void Versus_StampUnstampRoundTrip(Log log)
    {
        string script =
            "## VERSUS\n" +
            "Here is the claim. " + VersusTag + " Binance says one thing.\n" +
            "The record says another. {Versus:End} Next.\n" +
            "{Versus:\"They say\",\"Safe\",\"The data says\",\"Not safe\",6,Vs} And timed.\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains("signed\",T=") && section.TimedScript.Contains(",D=0}"),
                "stamping gives the held Versus a T= and D=0");
            log.IsTrue(section.TimedScript.Contains(",D=6,Vs}"), "…and the timed one its D=6 with ,Vs kept");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "Unstamp(Stamp(x)) gives back the Versus tags as written");

            var (_, events) = ContentZoneTagParser.ParseContentTags(section.TimedScript, 30f);
            log.AreEqual(2, events.FindAll(e => e.cardType == ContentCardType.Versus && !e.dismissesCard).Count,
                "the stamped Versus tags parse at playback");
            log.AreEqual(1, events.FindAll(e => e.cardType == ContentCardType.Versus && e.dismissesCard).Count,
                "…and so does the stamped End");
        }
    }

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Versus_Geometry(Log log)
    {
        const float W = 1920f, H = 1080f;
        Rect l = VersusTypography.PanelRect(0, W, H, 500f);
        Rect r = VersusTypography.PanelRect(1, W, H, 500f);
        Rect badge = VersusTypography.BadgeRect(W, H);
        float pad = VersusTypography.PanelPaddingPx;

        log.Near(0.42f * W, l.width, 0.01f, "each panel is 42 % of the width");
        log.Near(l.width, r.width, 0.01f, "…both the same");
        log.Near(W * 0.5f, (l.xMax + r.xMin) * 0.5f, 0.01f, "the panels sit either side of the centre");
        log.IsTrue(l.xMin >= W * 0.05f - 0.5f && r.xMax <= W * 0.95f + 0.5f, "both inside the 5 % title-safe margins");
        log.Near(H * 0.5f, l.center.y, 0.01f, "panels are centred vertically");
        log.Near(W * 0.5f, badge.center.x, 0.01f, "the badge is centred between them");
        log.Near(H * 0.5f, badge.center.y, 0.01f, "…and on the panels' middle");
        log.IsTrue(badge.xMin < l.xMax && badge.xMax > r.xMin, "the badge straddles the gap (overlaps both inner edges)");
        log.IsTrue(badge.xMin > l.xMax - pad && badge.xMax < r.xMin + pad, "…but stops short of either panel's padding, so it never covers text");

        log.Near(VersusTypography.PanelMinHeightFraction * H, VersusTypography.PanelHeight(10f, H), 0.01f,
            "a short group still gets a 40 %-high panel");
        log.Near(VersusTypography.TitleSafeFraction * H, VersusTypography.PanelHeight(5000f, H), 0.01f,
            "…and a huge one stops at title-safe");
        log.IsTrue(VersusTypography.OffscreenOffset(0, l, W, H) + l.xMax < 0f, "the left panel starts wholly off the left edge");
        log.IsTrue(VersusTypography.OffscreenOffset(1, r, W, H) + r.xMin > W, "the right panel starts wholly off the right edge");
        float maxOver = VersusTypography.MaxOvershootPx(W);
        log.Near(-962f, VersusTypography.SlideOffset(0f, -962f, maxOver), 1e-3f, "a slide starts at its off-screen offset");
        log.Near(-481f, VersusTypography.SlideOffset(0.5f, -962f, maxOver), 1e-3f, "…lerps to rest");
        log.Near(0f, VersusTypography.SlideOffset(1f, -962f, maxOver), 1e-4f, "…and is at rest at 1");
        float peak = VersusTypography.SlideOffset(1.115f, -962f, maxOver);
        log.IsTrue(peak > 0f && peak < maxOver, $"an 11 % overshoot over 962 px stays under {maxOver:F0} px ({peak:F1} px, was 111)");
        log.IsTrue(peak * 2f < W * VersusTypography.CenterGapFraction, "…so two overshooting panels can't meet across the gap");
        float before = (VersusTypography.SlideOffset(1f, -962f, maxOver) - VersusTypography.SlideOffset(0.999f, -962f, maxOver)) / 0.001f;
        float after = (VersusTypography.SlideOffset(1.001f, -962f, maxOver) - VersusTypography.SlideOffset(1f, -962f, maxOver)) / 0.001f;
        log.Near(before, after, Mathf.Abs(before) * 0.02f, "…with the same speed through the rest point (no kink)");
        log.IsTrue(VersusTypography.LabelPx * VersusTypography.SmallCapsScale >= 36f,
            $"small-caps label letters stay ≥ 36 px ({VersusTypography.LabelPx * VersusTypography.SmallCapsScale:F1})");
    }

    static void Versus_StampCurve(Log log)
    {
        log.Near(0.15f, VersusTypography.PanelStart(1) - VersusTypography.PanelStart(0), 1e-6f, "the right panel starts 0.15 s after the left");
        log.Near(VersusTypography.PanelStart(1) + VersusTypography.PanelSlideSeconds + 0.15f, VersusTypography.GlyphStart, 1e-6f,
            "the glyph stamps 0.15 s after the right panel lands");
        log.Near(VersusTypography.StampFromScale, VersusTypography.StampScaleAt(0f), 1e-6f, "the stamp starts at 1.6×");
        log.Near(1f, VersusTypography.StampScaleAt(VersusTypography.StampSeconds), 1e-6f, "…and is 1.0 after 0.22 s");
        float lo = 9f, hi = 0f;
        for (float t = 0f; t <= VersusTypography.StampSeconds; t += 0.002f)
        {
            float s = VersusTypography.StampScaleAt(t);
            lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s);
        }
        log.IsTrue(hi <= VersusTypography.StampFromScale + 1e-4f && lo >= 0.95f && lo < 1f,
            $"…a short punch: never over 1.6×, a small dip under 1.0 (lowest {lo:F3})");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    static VersusCard BuildVersus(string[] fields, bool vs, out GameObject root, bool show = true)
    {
        var card = BuildCard<VersusCard>(out root);
        var zone = (RectTransform)card.transform.parent;
        zone.sizeDelta = new Vector2(1920f, 1080f);
        card.Initialize(new ContentCardEvent { cardType = ContentCardType.Versus, fields = fields, versusVs = vs, primaryText = fields[1] }, null);
        if (show)
        {
            card.Show();
            for (float t = 0f; t < VersusTypography.EntranceSeconds + 0.5f; t += 1f / 30f) card.Advance(1f / 30f);
        }
        return card;
    }

    static void CheckVersus(Log log, VersusCard card, string what, bool vs)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        log.IsTrue(report != null, $"{what}: laid out");
        if (report == null) return;
        bool overflowWarned = report.Warnings.Exists(w => w.Contains("don't fit") || w.Contains("run past"));
        float pad = VersusTypography.PanelPaddingPx;
        Rect safe = Rect.MinMaxRect(1920f * 0.05f, 1080f * 0.05f, 1920f * 0.95f, 1080f * 0.95f);

        Transform dim = card.transform.Find("Dim");
        log.IsTrue(dim != null && dim.GetSiblingIndex() == 0, $"{what}: the dim is the bottom layer");
        log.Near(card.Dim.Peak, card.DimAlpha, 1e-4f, $"{what}: the dim is up (the BigText dim, {card.DimAlpha:F3})");

        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name),
                $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        log.Near(card.Text(0).fontSize, card.Text(1).fontSize, 0.01f, $"{what}: one text size for both panels ({card.TextSize:F0} px)");
        log.IsTrue(card.TextSize <= VersusTypography.TextMaxPx + 0.01f, $"{what}: text at most 96 px");

        Rect[] panelNow = new Rect[2];
        for (int i = 0; i < 2; i++)
        {
            panelNow[i] = RectInCard(card.Panel(i), rt);
            Rect rest = card.PanelRestRect(i);
            log.Near(rest.xMin, panelNow[i].xMin, 0.5f, $"{what}: panel {i} at rest after the entrance");
            log.Near(1920f * 0.42f, panelNow[i].width, 0.5f, $"{what}: panel {i} is 42 % of the width");
            log.IsTrue(overflowWarned || Inside(panelNow[i], safe, 1f), $"{what}: panel {i} inside title-safe ({Fmt(panelNow[i])})");
            log.Near(1f, card.PanelAlpha(i), 1e-4f, $"{what}: panel {i} fully visible");

            Rect inner = Rect.MinMaxRect(panelNow[i].xMin + pad, panelNow[i].yMin + pad, panelNow[i].xMax - pad, panelNow[i].yMax - pad);
            foreach (TMP_Text t in new TMP_Text[] { card.Label(i), card.Text(i) })
            {
                log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f, $"{what}: {t.name} {i} at or above 32 px ({t.fontSize:F1})");
                log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} {i} not truncated");
                Rect ink = CardTypography.InkInCard(t, rt);
                log.IsTrue(overflowWarned || Inside(ink, inner, 1f), $"{what}: {t.name} {i} inside the panel padding ({Fmt(ink)} in {Fmt(inner)})");
            }
            card.Text(i).ForceMeshUpdate(true, true);
            int lines = card.Text(i).textInfo.lineCount;
            log.IsTrue(lines <= VersusTypography.TextMaxLines || overflowWarned, $"{what}: text {i} on {lines} line(s), at most 4");
            log.IsTrue((card.Label(i).fontStyle & FontStyles.SmallCaps) != 0, $"{what}: label {i} in small caps");
        }
        log.IsTrue(panelNow[0].xMax < panelNow[1].xMin, $"{what}: the panels don't overlap");
        log.Near(RectInCard(card.Label(0).rectTransform, rt).yMin, RectInCard(card.Label(1).rectTransform, rt).yMin, 0.5f,
            $"{what}: the two labels line up");
        log.Near(RectInCard(card.Text(0).rectTransform, rt).yMin, RectInCard(card.Text(1).rectTransform, rt).yMin, 0.5f,
            $"{what}: …and so do the two texts");
        log.IsTrue(card.Label(0).color == VersusCard.Slate && card.Label(1).color == ContentCardUIBuilder.AccentColor,
            $"{what}: left label slate, right label accent");

        // The glyph: the fixed ≠ or VS, centred on a badge between the panels, covering no text.
        log.AreEqual(vs ? "VS" : "≠", card.Glyph.text, $"{what}: the glyph is {(vs ? "VS" : "≠")}");
        log.Near(1f, card.BadgeAlpha, 1e-4f, $"{what}: the glyph is up");
        log.Near(1f, card.Badge.localScale.x, 1e-4f, $"{what}: the glyph has settled to 1.0×");
        Rect badge = RectInCard(card.Badge, rt);
        Rect glyphInk = CardTypography.InkInCard(card.Glyph, rt);
        log.IsTrue(Inside(glyphInk, badge, 0.5f), $"{what}: the glyph's ink is inside its badge ({Fmt(glyphInk)} in {Fmt(badge)})");
        log.IsTrue(Vector2.Distance(glyphInk.center, badge.center) <= 2f,
            $"{what}: …and centred on it (off by {Vector2.Distance(glyphInk.center, badge.center):F1} px)");
        log.IsTrue(glyphInk.height >= badge.height * 0.3f, $"{what}: …and large ({glyphInk.width:F0}×{glyphInk.height:F0} px)");
        for (int i = 0; i < 2; i++)
            foreach (TMP_Text t in new TMP_Text[] { card.Label(i), card.Text(i) })
                log.IsTrue(!CardTypography.InkInCard(t, rt).Overlaps(badge), $"{what}: the badge covers none of {t.name} {i}");

        Debug.Log($"[Versus] self-test {what}: text {card.TextSize:F0} px, lines {card.Text(0).textInfo.lineCount}+{card.Text(1).textInfo.lineCount}, " +
                  $"panels {Fmt(panelNow[0])} / {Fmt(panelNow[1])}, glyph ink {glyphInk.width:F0}×{glyphInk.height:F0}" +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Versus_CardLayout(Log log)
    {
        var cases = new[]
        {
            (f: new[] { "Binance says", "No private key leaked", "The on-chain record says", "Eleven of its own keys signed" }, vs: false, what: "Binance"),
            (f: new[] { "A", "No", "B", "Yes" }, vs: false, what: "shortest"),
            (f: new[] { "Claim", "Safe", "Record", "Hacked" }, vs: true, what: "short, ,Vs"),
            (f: new[] { "The exchange said in its statement", "No private key or customer account was compromised in the incident",
                        "The blockchain record shows instead", "Eleven of the exchange's own signing keys approved every transfer" },
             vs: false, what: "4-line texts"),
            (f: new[] { "The exchange said in its statement", "No private key or customer account was ever compromised during the incident",
                        "The blockchain record shows instead", "Eleven of the exchange's own signing keys approved every single outgoing transfer" },
             vs: false, what: "4 lines, just too long"),
            (f: new[] { "Binance says", "No private key leaked", "The on-chain record says",
                        "Eleven of the exchange's own signing keys approved every single one of the outgoing transfers, over about twenty minutes, from three separate hot wallets" },
             vs: true, what: "long right, ,Vs"),
        };
        foreach (var c in cases)
        {
            var card = BuildVersus(c.f, c.vs, out GameObject root);
            try
            {
                CheckVersus(log, card, $"Versus ({c.what})", c.vs);
                if (c.what == "4-line texts")
                {
                    card.Text(0).ForceMeshUpdate(true, true);
                    card.Text(1).ForceMeshUpdate(true, true);
                    log.IsTrue(card.Layout.Warnings.Count == 0 && card.TextSize >= VersusTypography.TextMinPx &&
                               Mathf.Max(card.Text(0).textInfo.lineCount, card.Text(1).textInfo.lineCount) == 4,
                        $"Versus (4-line texts): four lines at {card.TextSize:F0} px, no warning");
                }
                if (c.what == "long right, ,Vs")
                    log.IsTrue(card.Layout.Warnings.Exists(w => w.Contains("the right text only fits")),
                        "Versus (long right): the warning names the right text as the one that's too long");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Too long for four lines at any size: degraded, warned, never blank.
        var huge = new[] { "Left", string.Join(" ", new string[60]).Replace(" ", "word "), "Right", "Short" };
        var hcard = BuildVersus(huge, false, out GameObject hroot);
        try
        {
            log.IsTrue(hcard.Layout.Warnings.Count > 0, "Versus (overlong): warned");
            log.IsTrue(hcard.TextSize >= CardTypography.HardFloorPx - 0.01f, $"Versus (overlong): text never under 32 px ({hcard.TextSize:F0})");
            log.IsTrue(hcard.Text(0).text.Length > 0 && !hcard.Text(0).isTextTruncated, "Versus (overlong): the text is still all there");
            Debug.Log($"[Versus] self-test overlong: {string.Join(" | ", hcard.Layout.Warnings)}");
        }
        finally { UnityEngine.Object.DestroyImmediate(hroot); }
    }

    // Steps the entrance at 30 fps: the left panel slides first, the right
    // 0.15 s later, the glyph stamps on its first drawn frame at 1.6× once both
    // have landed, and the dim fades in over 0.2 s.
    static void Versus_EntranceFrameByFrame(Log log)
    {
        CardEntryAnimator anim = CardEntryAnimator.Instance;
        CardEntrySettings.Style saved = anim.entryStyle;
        try
        {
            foreach (CardEntrySettings.Style style in new[] { CardEntrySettings.Style.Overshoot, CardEntrySettings.Style.EaseFade })
            {
                anim.entryStyle = style;
                Versus_EntranceFrameByFrame(log, style.ToString());
            }
        }
        finally { anim.entryStyle = saved; }
    }

    static void Versus_EntranceFrameByFrame(Log log, string style)
    {
        var fields = new[] { "Binance says", "No private key leaked", "The on-chain record says", "Eleven of its own keys signed" };
        var card = BuildVersus(fields, false, out GameObject root, show: false);
        try
        {
            var rt = (RectTransform)card.transform;
            Rect l0 = RectInCard(card.Panel(0), rt), r0 = RectInCard(card.Panel(1), rt);
            log.IsTrue(l0.xMax < 0f && r0.xMin > 1920f && card.BadgeAlpha == 0f, "before Show: both panels off-screen, the glyph hidden");

            card.Show();
            const float dt = 1f / 30f;
            float t = 0f;
            float rightMovedAt = float.NaN, leftMovedAt = float.NaN, glyphAt = float.NaN, settledAt = float.NaN;
            float firstScale = 0f, maxScale = 0f, dimFullAt = float.NaN;
            float worstGap = float.MaxValue, worstOver = 0f;
            for (int f = 0; f < 60; f++)
            {
                card.Advance(dt);
                t += dt;
                Rect l = RectInCard(card.Panel(0), rt), r = RectInCard(card.Panel(1), rt);
                worstGap = Mathf.Min(worstGap, r.xMin - l.xMax);
                worstOver = Mathf.Max(worstOver, Mathf.Max(l.xMin - card.PanelRestRect(0).xMin, card.PanelRestRect(1).xMin - r.xMin));
                if (float.IsNaN(leftMovedAt) && Mathf.Abs(l.xMin - l0.xMin) > 0.5f) leftMovedAt = t;
                if (float.IsNaN(rightMovedAt) && Mathf.Abs(r.xMin - r0.xMin) > 0.5f) rightMovedAt = t;
                if (float.IsNaN(dimFullAt) && card.DimAlpha >= card.Dim.Peak - 1e-4f) dimFullAt = t;
                if (card.BadgeAlpha > 0f)
                {
                    float s = card.Badge.localScale.x;
                    if (float.IsNaN(glyphAt)) { glyphAt = t; firstScale = s; }
                    maxScale = Mathf.Max(maxScale, s);
                    if (float.IsNaN(settledAt) && Mathf.Abs(s - 1f) < 1e-4f) settledAt = t;
                }
            }
            log.IsTrue(worstGap > 0f, $"{style}: the panels never touch mid-slide (closest {worstGap:F0} px apart)");
            log.IsTrue(worstOver <= VersusTypography.MaxOvershootPx(1920f) + 0.5f,
                $"{style}: no panel overshoots its rest by more than {VersusTypography.MaxOvershootPx(1920f):F0} px ({worstOver:F1})");
            log.IsTrue(leftMovedAt <= dt + 1e-4f, $"the left panel moves on the first frame ({leftMovedAt:F3} s)");
            log.IsTrue(rightMovedAt > 0.15f - 1e-3f && rightMovedAt <= 0.15f + dt + 1e-3f,
                $"the right panel starts 0.15 s later ({rightMovedAt:F3} s)");
            log.IsTrue(glyphAt >= VersusTypography.GlyphStart - 1e-3f && glyphAt <= VersusTypography.GlyphStart + dt + 1e-3f,
                $"the glyph appears 0.15 s after the right panel lands ({glyphAt:F3} s)");
            log.Near(VersusTypography.StampFromScale, firstScale, 1e-4f, "…its first drawn frame shows the full 1.6×");
            log.IsTrue(maxScale <= VersusTypography.StampFromScale + 1e-4f, "…never larger");
            log.IsTrue(settledAt - glyphAt <= VersusTypography.StampSeconds + dt + 1e-3f,
                $"…and settles to 1.0 within 0.22 s ({settledAt - glyphAt:F3} s)");
            log.IsTrue(dimFullAt >= BigTextTypography.DimFadeInSeconds - dt && dimFullAt <= BigTextTypography.DimFadeInSeconds + dt + 1e-3f,
                $"the dim reaches its peak after its 0.2 s fade-in ({dimFullAt:F3} s)");
            Rect lEnd = RectInCard(card.Panel(0), rt), rEnd = RectInCard(card.Panel(1), rt);
            log.Near(card.PanelRestRect(0).xMin, lEnd.xMin, 0.5f, "both panels are at rest when the entrance ends");
            log.Near(card.PanelRestRect(1).xMin, rEnd.xMin, 0.5f, "…the right one too");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Held ({Versus:…,Start} … {Versus:End}) and timed cards both leave through
    // Hide — the End, or the controller's duration timer — and the dim fades
    // out with them; a transition's fast hide takes the dim too.
    static void Versus_HeldAndTimedClose(Log log)
    {
        var fields = new[] { "They say", "Safe", "The data says", "Not safe" };
        foreach (bool fast in new[] { false, true })
        {
            var card = BuildVersus(fields, false, out GameObject root);
            try
            {
                card.Hide(fast);
                log.IsTrue(card.Dim.IsClosed, $"Versus {(fast ? "fast " : "")}hide: the dim starts closing");
                for (int f = 0; f < 9; f++) card.Advance(1f / 30f);
                log.Near(0f, card.DimAlpha, 1e-4f, $"Versus {(fast ? "fast " : "")}hide: the dim is gone 0.25 s later");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        var (_, events) = ContentZoneTagParser.ParseContentTags(
            "{Versus:\"a\",\"b\",\"c\",\"d\",Start} x {Versus:End} {Versus:\"a\",\"b\",\"c\",\"d\",4.5} y", 30f);
        var opens = events.FindAll(e => e.cardType == ContentCardType.Versus && !e.dismissesCard);
        log.IsTrue(opens.Count == 2 && opens[0].duration == 0f && Mathf.Approximately(opens[1].duration, 4.5f),
            "a held Versus has no timer (waits for its End); a timed one hides itself after its duration");
    }
}

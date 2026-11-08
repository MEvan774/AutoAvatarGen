using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Tts;
using MugsTech.Style;

/// <summary>
/// {Compare:…} — value parsing and honest scale (pure), the tag through every
/// registration site (runtime parser, TTS strip + stamp, tag-only unstamp),
/// and the real card laid out with TMP.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Compare Self-Tests")]
    public static void RunCompare()
    {
        var log = new Log();
        RunCompareGroup(log);
        log.Report();
    }

    static void RunCompareGroup(Log log)
    {
        Compare_ValueParsing(log);
        Compare_KindsMustMatch(log);
        Compare_HonestRatios(log);
        Compare_ValuePlacement(log);
        Compare_Growth(log);
        Compare_TagParsing(log);
        Compare_StampUnstampRoundTrip(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Compare_CardLayout(log);
            Compare_ValueRidesItsBar(log);
            Compare_FallbackLayout(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"Compare layout checks threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Compare_ValueParsing(Log log)
    {
        void Is(string text, double amount, char cur, CompareMath.Unit unit, bool plus = false)
        {
            CompareMath.Value v = CompareMath.Parse(text);
            log.IsTrue(v.Ok && Math.Abs(v.Amount - amount) < amount * 1e-9 + 1e-9 && v.Currency == cur && v.Unit == unit && v.Plus == plus,
                $"\"{text}\" parses as {amount:G} {(cur == '\0' ? "" : cur.ToString())} {unit}{(plus ? " +" : "")} (got {(v.Ok ? $"{v.Amount:G} {v.Currency} {v.Unit} {v.Plus}" : v.Error)})");
        }
        Is("$6B+", 6e9, '$', CompareMath.Unit.Plain, plus: true);
        Is("$1.5B", 1.5e9, '$', CompareMath.Unit.Plain);
        Is("€360,000", 360000, '€', CompareMath.Unit.Plain);
        Is("€360K", 360000, '€', CompareMath.Unit.Plain);
        Is("95%", 95, '\0', CompareMath.Unit.Percent);
        Is("4x", 4, '\0', CompareMath.Unit.Multiple);
        Is("4×", 4, '\0', CompareMath.Unit.Multiple);
        Is("1.567B", 1.567e9, '\0', CompareMath.Unit.Plain);
        Is("12,345.67", 12345.67, '\0', CompareMath.Unit.Plain);
        Is("6b", 6e9, '\0', CompareMath.Unit.Plain);
        Is("£2.5T", 2.5e12, '£', CompareMath.Unit.Plain);
        Is("¥800M", 8e8, '¥', CompareMath.Unit.Plain);
        foreach (string bad in new[] { "50°C", "1,5", "$", "", "$ 6B", "six", "6B dollars", "-5%", "1,23,456" })
            log.IsTrue(!CompareMath.Parse(bad).Ok, $"\"{bad}\" isn't a value the bars can scale");
    }

    static void Compare_KindsMustMatch(Log log)
    {
        log.IsTrue(CompareMath.Ratios("$6B+", "$1.5B").Ok, "$ vs $: bars");
        log.IsTrue(CompareMath.Ratios("95%", "80%").Ok, "% vs %: bars");
        log.IsTrue(CompareMath.Ratios("4x", "1x").Ok, "multiple vs multiple: bars");
        log.IsTrue(CompareMath.Ratios("1.567B", "12").Ok, "plain vs plain: bars");
        CompareMath.Bars eu = CompareMath.Ratios("€360K", "$1.5B");
        log.IsTrue(!eu.Ok && eu.Problem.Contains("€") && eu.Problem.Contains("$"), $"€ vs $: no bars ({eu.Problem})");
        log.IsTrue(!CompareMath.Ratios("95%", "4x").Ok, "% vs multiple: no bars");
        log.IsTrue(!CompareMath.Ratios("$6B", "6B").Ok, "$ vs plain: no bars");
        log.IsTrue(!CompareMath.Ratios("50°C", "40°C").Ok, "°C isn't a value: no bars");
        log.IsTrue(!CompareMath.Ratios("0", "0").Ok, "two zeros: no bars");
    }

    static void Compare_HonestRatios(Log log)
    {
        CompareMath.Bars a = CompareMath.Ratios("$100B", "$1B");
        log.Near(1f, a.A, 1e-6f, "100:1 — the larger fills the track");
        log.Near(CompareMath.MinBarFraction, a.B, 1e-6f, "100:1 — the smaller (1 %) is drawn as a 3 % sliver");
        log.IsTrue(a.SliverB && !a.SliverA, "…and flagged");
        CompareMath.Bars b = CompareMath.Ratios("$6B+", "$1.5B");
        log.Near(0.25f, b.B, 1e-6f, "$6B vs $1.5B: the smaller bar is exactly a quarter");
        CompareMath.Bars c = CompareMath.Ratios("€360,000", "€360K");
        log.IsTrue(Mathf.Approximately(c.A, 1f) && Mathf.Approximately(c.B, 1f) && !c.SliverA && !c.SliverB,
            "€360,000 vs €360K: 1:1, two full bars");
        CompareMath.Bars d = CompareMath.Ratios("1", "4");
        log.Near(0.25f, d.A, 1e-6f, "A can be the smaller one: 1 vs 4");
    }

    static void Compare_ValuePlacement(Log log)
    {
        CompareMath.Placement p = CompareMath.Place(860f, 1f, 0.25f, 300f, 300f, 21f, 21f);
        log.IsTrue(p.InsideA && !p.InsideB && Mathf.Approximately(p.Track, 860f), "a long bar holds its value; a short one puts it just past its end");
        CompareMath.Placement q = CompareMath.Place(860f, 1f, 0.5f, 300f, 520f, 21f, 21f);
        log.IsTrue(!q.Overflows && q.Track < 860f && 0.5f * q.Track + 21f + 520f <= 860.5f,
            $"a wide value past a mid-length bar shrinks the track so it stays inside ({q.Track:F0} px)");
        CompareMath.Placement r = CompareMath.Place(860f, 1f, 1f, 300f, 300f, 21f, 21f);
        log.IsTrue(r.InsideA && r.InsideB, "1:1 — both values inside their full bars");
        CompareMath.Placement s = CompareMath.Place(860f, 1f, 0.5f, 900f, 300f, 21f, 21f);
        log.IsTrue(s.Overflows, "a value wider than the row is flagged");
    }

    static void Compare_Growth(Log log)
    {
        log.AreEqual(0f, CompareMath.GrowAt(0f), "a bar starts at zero");
        log.Near(1f, CompareMath.GrowAt(CompareMath.GrowSeconds), 1e-6f, "…and is full after 0.6 s");
        log.IsTrue(CompareMath.GrowAt(0.1f) > 0.1f / CompareMath.GrowSeconds, "…easing out (fast first)");
        log.AreEqual(0f, CompareMath.ValueAlphaAt(0.5f), "the value waits for its bar");
        log.Near(1f, CompareMath.ValueAlphaAt(1f), 1e-6f, "…and is there when the bar is full");
    }

    // -----------------------------------------------------------------------
    // The tag through the registration sites
    // -----------------------------------------------------------------------

    const string CompareTag = "{Compare:\"$6B+\",\"Binance, 2026\",\"$1.5B\",\"Bybit, 2025\",\"FBI; TRM Labs\",Start,Left}";

    static void Compare_TagParsing(Log log)
    {
        string script =
            CompareTag + " words\n" +
            "more words {Compare:End}\n" +
            "{Compare:\"95%\",\"Hit\",\"80%\",\"Miss\",\"Survey\",6,Right} later\n" +
            "{Compare:\"$6B\",\"stamped\",\"$1B\",\"x\",\"src\",T=12.500,D=0,Left}\n" +
            "{Compare:\"4x\",\"only\",\"four\",\"fields\",Start}\n";
        var (clean, events) = ContentZoneTagParser.ParseContentTags(script, 30f);
        var compares = events.FindAll(e => e.cardType == ContentCardType.Compare && !e.dismissesCard);
        var ends = events.FindAll(e => e.cardType == ContentCardType.Compare && e.dismissesCard);

        log.AreEqual(3, compares.Count, "three valid Compare tags become cards (the malformed one is skipped)");
        log.AreEqual(1, ends.Count, "{Compare:End} closes a held Compare");
        log.IsTrue(!clean.Contains("{Compare"), "every Compare tag — valid, End, malformed — is stripped from the text");
        ContentCardEvent first = compares.Find(e => e.fields[0] == "$6B+");
        log.IsTrue(first != null && first.duration == 0f && first.entryDirectionOverride == EntryDirection.FromLeft &&
                   string.Join("|", first.fields) == "$6B+|Binance, 2026|$1.5B|Bybit, 2025|FBI; TRM Labs",
            "the held tag keeps its five fields in order, its side, and no timer");
        ContentCardEvent timed = compares.Find(e => e.fields[0] == "95%");
        log.IsTrue(timed != null && Mathf.Approximately(timed.duration, 6f) && timed.entryDirectionOverride == EntryDirection.FromRight,
            "a timed tag keeps its duration and ,Right");
        ContentCardEvent stamped = compares.Find(e => e.fields[0] == "$6B");
        log.IsTrue(stamped != null && Mathf.Approximately(stamped.triggerTime, 12.5f) && stamped.duration == 0f,
            "a stamped tag (T=…, D=0) fires at its T=");

        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(CompareTag), "the TTS processor recognises the tag");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Compare:End}"), "…and its End");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Compare:\"4x\",\"only\",\"four\",\"fields\",Start}"),
            "…but not a malformed one (the tag-only check warns about it)");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(CompareTag) && !HybridAvatarSystem.EmotionTagRegex.IsMatch("{Compare:End}"),
            "the emotion parser can't mistake it for an emotion");
    }

    static void Compare_StampUnstampRoundTrip(Log log)
    {
        string script =
            "## COMPARE\n" +
            "The Binance hack was big. " + CompareTag + " About four times Bybit.\n" +
            "That is the scale. {Compare:End} Next.\n" +
            "{Compare:\"95%\",\"Hit\",\"80%\",\"Miss\",\"Survey\",6,Right} And timed.\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains("T=") && section.TimedScript.Contains(",D=0,Left}"),
                "stamping gives the held Compare a T= and D=0 with its side kept");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "Unstamp(Stamp(x)) gives back the Compare tags as written");

            var (_, events) = ContentZoneTagParser.ParseContentTags(section.TimedScript, 30f);
            log.AreEqual(2, events.FindAll(e => e.cardType == ContentCardType.Compare && !e.dismissesCard).Count,
                "the stamped Compare tags parse at playback");
            log.AreEqual(1, events.FindAll(e => e.cardType == ContentCardType.Compare && e.dismissesCard).Count,
                "…and so does the stamped End");
        }
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    static CompareCard BuildCompare(string[] fields, out GameObject root)
    {
        var card = BuildCard<CompareCard>(out root);
        card.Initialize(new ContentCardEvent { cardType = ContentCardType.Compare, fields = fields, primaryText = fields[0] }, null);
        card.StartGrowthForTest();
        card.Advance(2f); // bars fully grown
        return card;
    }

    static void CheckCompareBars(Log log, CompareCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        Rect inner = InnerRect(card);
        for (int i = 0; i < 2; i++)
        {
            Rect bar = RectInCard(card.Bar(i).rectTransform, rt);
            float want = card.BarFraction(i) * card.TrackWidth;
            log.Near(want, bar.width, 1f, $"{what}: bar {(i == 0 ? "A" : "B")} is {card.BarFraction(i):P0} of the track ({bar.width:F0} px)");
            log.IsTrue(bar.width >= CompareMath.MinBarFraction * card.TrackWidth - 0.5f, $"{what}: bar {(i == 0 ? "A" : "B")} is at least a 3 % sliver");
            log.IsTrue(Inside(bar, inner, 1f), $"{what}: bar {(i == 0 ? "A" : "B")} inside the padding ({Fmt(bar)})");

            Rect ink = CardTypography.InkInCard(card.ValueText(i), rt);
            if (card.ValueInside(i))
                log.IsTrue(Inside(ink, bar, 1f), $"{what}: value {(i == 0 ? "A" : "B")} sits inside its bar");
            else
                log.IsTrue(ink.xMin >= bar.xMax - 1f, $"{what}: value {(i == 0 ? "A" : "B")} sits just past its bar's end");
            log.IsTrue(ink.yMin >= bar.yMin - 2f && ink.yMax <= bar.yMax + 2f, $"{what}: value {(i == 0 ? "A" : "B")} centred on its bar's height");
        }
    }

    static void Compare_CardLayout(Log log)
    {
        var cases = new[]
        {
            (f: new[] { "$6B+", "Binance, 2026", "$1.5B", "Bybit, 2025", "FBI; TRM Labs" }, what: "Binance vs Bybit"),
            (f: new[] { "1", "A", "2", "B", "X" }, what: "shortest"),
            (f: new[] { "€1,234,567,890", "The largest crypto exchange in the world, by its trading volume",
                        "€360K", "A much smaller exchange in Dubai, hacked a year earlier",
                        "Company filings; Chainalysis; TRM Labs; Elliptic research, 2026" }, what: "longest"),
            (f: new[] { "$100B", "Big", "$1B", "Small", "Src" }, what: "100:1"),
            (f: new[] { "95%", "This year", "95%", "Last year", "Survey" }, what: "1:1"),
        };
        foreach (var c in cases)
        {
            var card = BuildCompare(c.f, out GameObject root);
            try
            {
                log.IsTrue(!card.IsFallback, $"Compare ({c.what}): bars");
                CheckCard(log, card, card.Layout, $"Compare ({c.what})");
                CheckCompareBars(log, card, $"Compare ({c.what})");
                log.IsTrue(!card.VsText.gameObject.activeSelf, $"Compare ({c.what}): no \"vs\" with bars");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    // Regression: a value sat at its final spot while its bar grew, so an
    // inside value hung off the bar's growing end ("$6B+" lost its "+").
    static void Compare_ValueRidesItsBar(Log log)
    {
        var fields = new[] { "$6B+", "Binance, 2026", "$1.5B", "Bybit, 2025", "FBI; TRM Labs" };
        var card = BuildCard<CompareCard>(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.Compare, fields = fields, primaryText = fields[0] }, null);
            card.StartGrowthForTest();
            var rt = (RectTransform)card.transform;
            const float dt = 1f / 30f;
            int framesChecked = 0;
            bool insideOk = true, outsideOk = true;
            var firstOff = new float?[2];
            for (int f = 0; f < 30; f++)
            {
                card.Advance(dt);
                for (int i = 0; i < 2; i++)
                {
                    if (!card.Bar(i).enabled) continue;
                    Rect bar = RectInCard(card.Bar(i).rectTransform, rt);
                    Rect box = RectInCard(card.ValueText(i).rectTransform, rt);
                    float off = box.xMin - bar.xMax;
                    if (firstOff[i] == null) firstOff[i] = off;
                    else if (Mathf.Abs(off - firstOff[i].Value) > 0.5f)
                    {
                        if (card.ValueInside(i)) insideOk = false; else outsideOk = false;
                    }
                    framesChecked++;
                }
            }
            log.IsTrue(framesChecked > 20, $"Compare growth: stepped {framesChecked} bar-frames");
            log.IsTrue(insideOk, "Compare growth: an inside value keeps its place at its bar's growing end on every frame");
            log.IsTrue(outsideOk, "Compare growth: an outside value keeps its gap past its bar's growing end on every frame");
            Rect barA = RectInCard(card.Bar(0).rectTransform, rt);
            Rect inkA = CardTypography.InkInCard(card.ValueText(0), rt);
            log.IsTrue(Inside(inkA, barA, 1f), "Compare growth: fully grown, \"$6B+\" is wholly inside its bar");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Compare_FallbackLayout(Log log)
    {
        var cases = new[]
        {
            (f: new[] { "50°C", "Inside the freezer", "40°C", "Outside", "CERT-FR" }, what: "°C"),
            (f: new[] { "€360K", "Fine", "$1.5B", "Stolen", "Src" }, what: "€ vs $"),
        };
        foreach (var c in cases)
        {
            var card = BuildCompare(c.f, out GameObject root);
            try
            {
                log.IsTrue(card.IsFallback, $"Compare fallback ({c.what}): no bars");
                log.IsTrue(!card.Bar(0).gameObject.activeSelf && !card.Bar(1).gameObject.activeSelf, $"Compare fallback ({c.what}): the bars are hidden");
                log.IsTrue(card.VsText.gameObject.activeSelf && card.VsText.text == "vs", $"Compare fallback ({c.what}): a small \"vs\" between them");
                log.IsTrue(card.ValueText(0).text == c.f[0] && card.ValueText(1).text == c.f[2], $"Compare fallback ({c.what}): both values as written");
                CheckCard(log, card, card.Layout, $"Compare fallback ({c.what})");
                Rect a = CardTypography.InkInCard(card.ValueText(0), (RectTransform)card.transform);
                Rect b = CardTypography.InkInCard(card.ValueText(1), (RectTransform)card.transform);
                Rect v = CardTypography.InkInCard(card.VsText, (RectTransform)card.transform);
                log.IsTrue(a.xMax < v.xMin && v.xMax < b.xMin, $"Compare fallback ({c.what}): value A | vs | value B, side by side");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}

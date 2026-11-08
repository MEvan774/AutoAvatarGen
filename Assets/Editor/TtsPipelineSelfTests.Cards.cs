using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Side-card typography (CardTypography + the Stat / Quote / Excerpt /
/// Headline cards). The Cards_Fit* checks are pure maths; the Cards_*Card
/// checks build real cards in edit mode and measure them with TMP, so they
/// need the editor (menu or batchmode) — no play mode, key or credits.
///
/// Each card is laid out with the shortest and the longest text the tag
/// guide's examples imply, plus the Binance take's real cards, and every text
/// on it must (a) render inside the inner padding, (b) be at least 32 px, and
/// (c) be at or above its own floor — or the card must have logged a warning
/// naming that element.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Card Layout Self-Tests")]
    public static void RunCards()
    {
        var log = new Log();
        RunCardGroup(log);
        log.Report();
    }

    static void RunCardGroup(Log log)
    {
        Cards_FitLargestFindsTheEdge(log);
        Cards_FitBodyFallsBackToTheFloors(log);
        Cards_FitStatValueHitsItsTargets(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        Color? savedColor = MugsTech.Style.VisualsRuntimeApplier.CardTextColorOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;      // the default Inter look
        MugsTech.Style.VisualsRuntimeApplier.CardTextColorOverride = null;
        try
        {
            Cards_StatCard(log);
            Cards_QuoteCard(log);
            Cards_ExcerptCard(log);
            Cards_HeadlineCard(log);
            Cards_OtherCardsStayAboveTheFloor(log);
        }
        catch (Exception e)
        {
            // TMP needs the engine — offline harnesses land here.
            log.IsTrue(false, $"card layout checks threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
            MugsTech.Style.VisualsRuntimeApplier.CardTextColorOverride = savedColor;
        }
    }

    // -----------------------------------------------------------------------
    // Pure fitting maths
    // -----------------------------------------------------------------------

    static void Cards_FitLargestFindsTheEdge(Log log)
    {
        log.AreEqual(63f, CardTypography.LargestFitting(s => s <= 63.7f, 44f, 80f), "largest whole size under the edge");
        log.AreEqual(80f, CardTypography.LargestFitting(s => true, 56f, 80f), "everything fits → the maximum");
        log.IsTrue(float.IsNaN(CardTypography.LargestFitting(s => s < 40f, 56f, 80f)), "nothing fits → NaN");
        // A jagged curve (a wrap that only works at some sizes) still lands on a size that fits.
        float jag = CardTypography.LargestFitting(s => s <= 60f || (s >= 70f && s <= 71f), 56f, 80f);
        log.IsTrue(jag <= 60f || (jag >= 70f && jag <= 71f), $"a jagged fit curve still returns a fitting size ({jag})");
    }

    static void Cards_FitBodyFallsBackToTheFloors(Log log)
    {
        var a = CardTypography.FitBody(s => s <= 72f, 56f, 80f, 44f, 32f);
        log.AreEqual(72f, a.Size, "fits in range → largest fitting size");
        log.IsTrue(!a.BelowWarnFloor && !a.Overflows, "in range → no warning");

        var b = CardTypography.FitBody(s => s <= 50f, 56f, 80f, 44f, 32f);
        log.AreEqual(50f, b.Size, "under the range → shrinks below 56");
        log.IsTrue(!b.BelowWarnFloor, "50 px is still above the 44 px warn floor");

        var c = CardTypography.FitBody(s => s <= 40f, 56f, 80f, 44f, 32f);
        log.AreEqual(40f, c.Size, "under the warn floor → keeps shrinking to fit");
        log.IsTrue(c.BelowWarnFloor && !c.Overflows, "under the warn floor → flagged");

        var d = CardTypography.FitBody(s => s <= 20f, 56f, 80f, 44f, 32f);
        log.AreEqual(32f, d.Size, "never below the 32 px hard floor");
        log.IsTrue(d.Overflows, "doesn't fit at the hard floor → flagged as overflowing");
    }

    static void Cards_FitStatValueHitsItsTargets(Log log)
    {
        const float innerW = 860f, cap = 0.727f;

        // 3 characters, 2.0 px of width per unit of size: 70 % of the width gives cap ≈ 219.
        float s3 = CardTypography.StatValueSize(200f, cap, innerW, false, 1f);
        log.Near(0.70f * innerW, s3 * 2.0f, 1f, "a 3-char value fills 70 % of the inner width");
        log.IsTrue(s3 * cap >= 160f && s3 * cap <= 260f, $"…with its cap height in 160–260 ({s3 * cap:F0})");

        // 2 narrow characters would be enormous at 70 % — the 260 px cap height wins.
        float s2 = CardTypography.StatValueSize(130f, cap, innerW, false, 1f);
        log.Near(260f, s2 * cap, 0.5f, "a short value stops at the 260 px cap height");

        // Just too wide for the floor at 70 %: widens toward 85 % to reach cap 160.
        float sw = CardTypography.StatValueSize(290f, cap, innerW, false, 1f);
        log.Near(160f, sw * cap, 0.5f, "a value just short of the floor widens to reach cap 160");
        log.IsTrue(sw * 2.9f <= 0.85f * innerW + 0.5f, "…but never past 85 % of the inner width");

        // 12 characters: width-limited far below the floor — the caller warns.
        float s12 = CardTypography.StatValueSize(700f, cap, innerW, false, 1f);
        log.IsTrue(s12 * 7f <= 0.85f * innerW + 0.5f, "a 12-char value stays inside 85 % of the width");
        log.IsTrue(s12 * cap < 160f, "a 12-char value can't reach cap 160 (it gets a warning)");

        // With an arrow the arrow + gap + value row must fit the inner width.
        float sa = CardTypography.StatValueSize(400f, cap, innerW, true, 1f);
        float row = sa * 4f + sa * cap * (CardTypography.StatArrowToCap + CardTypography.StatArrowGapToCap);
        log.IsTrue(row <= innerW + 0.5f, $"arrow + value fit the inner width ({row:F0} ≤ {innerW})");

        // Everything scales with the card: a 4K card (k = 2) gets twice the cap height.
        float s4k = CardTypography.StatValueSize(130f, cap, innerW * 2f, false, 2f);
        log.Near(520f, s4k * cap, 1f, "a 2× card gets a 2× cap-height ceiling");
    }

    // -----------------------------------------------------------------------
    // Real cards, measured with TMP
    // -----------------------------------------------------------------------

    static void Cards_StatCard(Log log)
    {
        var cases = new[]
        {
            (v: "$6B",          l: "Stolen",                                   c: "FBI",                                              a: StatArrow.None, what: "3-char value, short lines"),
            (v: "$2.3 billion", l: "Users Who Read the Full Terms of Service", c: "vs the same quarter last year, per the company's filing", a: StatArrow.None, what: "12-char value, long lines"),
            (v: "+40%",         l: "Revenue Growth",                           c: "vs the same quarter last year",                    a: StatArrow.Win,  what: "Win arrow"),
            (v: "$2.3 billion", l: "OpenAI 2024 Losses",                       c: "a widening deficit",                               a: StatArrow.Loss, what: "Loss arrow, 12-char value"),
            (v: "$6B+",         l: "Stolen From Binance",                      c: "FBI, Oct. 31",                                     a: StatArrow.None, what: "Binance 0:08"),
            (v: "4x",           l: "The $1.5B Bybit Hack",                     c: "Feb 2025 — TRM Labs",                              a: StatArrow.None, what: "Binance 0:19"),
        };

        foreach (var t in cases)
        {
            var card = BuildCard<StatCard>(out GameObject root);
            try
            {
                card.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Stat, primaryText = t.v, secondaryText = t.l, tertiaryText = t.c, statArrow = t.a,
                }, null);
                CheckCard(log, card, card.Layout, $"Stat ({t.what})");

                if (t.a != StatArrow.None)
                {
                    Transform arrowT = card.transform.Find("DirectionArrow");
                    RectTransform art = arrowT as RectTransform;
                    log.IsTrue(arrowT != null && arrowT.gameObject.activeSelf, $"Stat ({t.what}): arrow shown");
                    if (art != null)
                    {
                        Rect inner = InnerRect(card);
                        Rect ar = RectInCard(art, (RectTransform)card.transform);
                        log.IsTrue(Inside(ar, inner, 1f), $"Stat ({t.what}): arrow inside the padding ({Fmt(ar)})");
                        Rect value = CardTypography.InkInCard(card.transform.Find("NumberText").GetComponent<TMP_Text>(),
                                                             (RectTransform)card.transform);
                        log.IsTrue(ar.xMax <= value.xMin + 1f, $"Stat ({t.what}): arrow left of the value");
                        bool up = art.localScale.y < 0f;
                        log.AreEqual(t.a == StatArrow.Win, up, $"Stat ({t.what}): arrow points the right way");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void Cards_QuoteCard(Log log)
    {
        var cases = new[]
        {
            (q: "Your money is safe with us.", n: "Richard Teng", r: "co-CEO, Binance", what: "6 words"),
            (q: "We did not expect the attack to reach our hot wallets this quickly, and we are still working with law " +
                "enforcement to trace every transfer, freeze what we can, and repay every affected user in full, starting " +
                "with retail customers.",
             n: "Sarah Mitchell", r: "VP of Communications, MegaCorp", what: "40 words"),
            (q: "So far, we have found no evidence of any wallet private key leak.", n: "Richard Teng", r: "co-CEO, Binance", what: "Binance 1:11"),
            (q: "Bottom line - your money is safe, and we're covering every loss.", n: "Richard Teng", r: "co-CEO, Binance, on X", what: "Binance 4:17"),
        };

        foreach (var t in cases)
        {
            var card = BuildCard<QuoteCard>(out GameObject root);
            try
            {
                card.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Quote, primaryText = t.q, secondaryText = t.n, tertiaryText = t.r,
                }, null);
                CheckCard(log, card, card.Layout, $"Quote ({t.what})");

                TMP_Text body = card.transform.Find("QuoteText").GetComponent<TMP_Text>();
                log.IsTrue((body.fontStyle & FontStyles.Italic) == 0, $"Quote ({t.what}): body not italic");
                log.IsTrue((body.alignment & (TextAlignmentOptions)HorizontalAlignmentOptions.Left) != 0,
                    $"Quote ({t.what}): body left-aligned");
                log.IsTrue(card.transform.Find("CloseQuote") == null && card.transform.Find("OpenQuote") == null,
                    $"Quote ({t.what}): the two small corner glyphs are gone");

                TMP_Text mark = card.transform.Find("QuoteMark").GetComponent<TMP_Text>();
                log.IsTrue(mark.fontSize >= 180f && mark.fontSize <= 220f, $"Quote ({t.what}): mark 180–220 px ({mark.fontSize:F0})");
                log.Near(CardTypography.QuoteMarkAlpha, mark.color.a, 0.01f, $"Quote ({t.what}): mark at 25 % opacity");
                log.IsTrue(mark.transform.GetSiblingIndex() < body.transform.GetSiblingIndex(),
                    $"Quote ({t.what}): mark drawn behind the quote");
                Rect mk = CardTypography.InkInCard(mark, (RectTransform)card.transform);
                Rect bd = CardTypography.InkInCard(body, (RectTransform)card.transform);
                log.IsTrue(mk.Overlaps(bd) || mk.yMax >= bd.yMin - 2f,
                    $"Quote ({t.what}): mark sits at the quote's top-left ({Fmt(mk)} vs {Fmt(bd)})");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void Cards_ExcerptCard(Log log)
    {
        var cases = new[]
        {
            (x: "Keys were never exposed, Binance said.", h: "never exposed", s: "Reuters", what: "6 words"),
            (x: "Users hereby grant a perpetual, royalty-free license to use submitted content for any purpose, including " +
                "training AI models, and waive any claim to compensation, attribution or notice arising from such use, now " +
                "or in the future, anywhere in the world.",
             h: "training AI models", s: "Official Terms of Service", what: "40 words"),
            (x: "may involve a compromised DevOps engineer's laptop", h: "a compromised DevOps engineer's laptop", s: "Binance, on X", what: "Binance 0:55"),
            (x: "Eleven Binance hot-wallet keys signed transfers to an address created minutes earlier.",
             h: "Eleven Binance hot-wallet keys signed", s: "Blockaid, on-chain incident report", what: "Binance 1:48"),
            (x: "The attacker still holds about USD 2.4 billion in assets beyond issuer reach.", h: "beyond issuer reach", s: "TRM Labs", what: "Binance 3:54"),
            (x: "approximately 95% of the impacted amount, up from 90%", h: "approximately 95%", s: "Binance, via CoinDesk, Nov 1", what: "Binance 4:25"),
        };

        foreach (var t in cases)
        {
            var card = BuildCard<ExcerptCard>(out GameObject root);
            try
            {
                card.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Excerpt, primaryText = t.x, secondaryText = t.h, tertiaryText = t.s,
                }, null);
                CheckCard(log, card, card.Layout, $"Excerpt ({t.what})");

                TMP_Text body = card.transform.Find("ExcerptText").GetComponent<TMP_Text>();
                log.IsTrue((body.alignment & (TextAlignmentOptions)HorizontalAlignmentOptions.Left) != 0,
                    $"Excerpt ({t.what}): passage left-aligned");

                // The highlight lives on a layer BEHIND the passage (TMP draws a
                // mark over its own glyphs), laid out exactly like it — so the
                // mark sits under the phrase and the phrase keeps its colour.
                TMP_Text layer = card.transform.Find("HighlightLayer").GetComponent<TMP_Text>();
                log.IsTrue(layer.transform.GetSiblingIndex() < body.transform.GetSiblingIndex(),
                    $"Excerpt ({t.what}): highlight layer drawn behind the passage");
                log.Near(body.fontSize, layer.fontSize, 0.01f, $"Excerpt ({t.what}): highlight layer at the passage's size");
                log.IsTrue(layer.rectTransform.offsetMin == body.rectTransform.offsetMin &&
                           layer.rectTransform.offsetMax == body.rectTransform.offsetMax,
                    $"Excerpt ({t.what}): highlight layer on the passage's rect");

                Rect plain = CardTypography.InkInCard(body, (RectTransform)card.transform);
                int at = t.x.IndexOf(t.h, StringComparison.Ordinal);
                layer.text = "<alpha=#00>" + t.x.Substring(0, at) + "<alpha=#FF><mark=#E85D4AAA>" + t.h + "</mark>" +
                             "<alpha=#00>" + t.x.Substring(at + t.h.Length);
                Rect marked = CardTypography.InkInCard(layer, (RectTransform)card.transform);
                log.IsTrue(Mathf.Abs(plain.yMin - marked.yMin) < 0.5f && Mathf.Abs(plain.yMax - marked.yMax) < 0.5f &&
                           Mathf.Abs(plain.xMin - marked.xMin) < 0.5f && Mathf.Abs(plain.xMax - marked.xMax) < 0.5f,
                    $"Excerpt ({t.what}): the highlight layer's lines match the passage's ({Fmt(marked)} vs {Fmt(plain)})");
                log.AreEqual(t.x, body.text, $"Excerpt ({t.what}): the passage itself stays plain text");

                // …and the mark is really drawn, at its own colour and alpha
                // (an <alpha=#00> around it would silently zero it).
                bool markDrawn = false;
                foreach (TMP_MeshInfo mi in layer.textInfo.meshInfo)
                    if (mi.colors32 != null)
                        foreach (Color32 c in mi.colors32)
                            if (c.r == 0xE8 && c.g == 0x5D && c.b == 0x4A && c.a == 0xAA) { markDrawn = true; break; }
                log.IsTrue(markDrawn, $"Excerpt ({t.what}): the highlight is drawn in #E85D4A at alpha AA");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void Cards_HeadlineCard(Log log)
    {
        var cases = new[]
        {
            (h: "Markets React", s: "Bloomberg", what: "2 words"),
            (h: "Tech Giant Quietly Changes Privacy Policy After Regulators Demand Answers Over Data Sharing With Advertisers",
             s: "The Verge, citing two people familiar with the matter", what: "16 words"),
            (h: "Tech Giant Quietly Changes Privacy Policy", s: "The Verge", what: "guide example"),
        };

        foreach (var t in cases)
        {
            var card = BuildCard<HeadlineCard>(out GameObject root);
            try
            {
                card.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Headline, primaryText = t.h, secondaryText = t.s,
                }, null);
                CheckCard(log, card, card.Layout, $"Headline ({t.what})");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    // ,bigCenter headline and the Logo fallback are only CHECKED against the
    // 32 px floor (step 1 doesn't restyle them); their sizes are logged.
    static void Cards_OtherCardsStayAboveTheFloor(Log log)
    {
        var root = new GameObject("CardSelfTest_BigCenter", typeof(RectTransform), typeof(Canvas));
        try
        {
            var zone = new GameObject("Zone", typeof(RectTransform)).GetComponent<RectTransform>();
            zone.SetParent(root.transform, false);
            zone.sizeDelta = new Vector2(1920f, 1080f);

            foreach (string text in new[] { "Binance freezes assets after US$6.3 billion heist",
                                            "40% User Growth After Privacy Backlash" })
            {
                BigCenterCard.CreateHeadlineBlock(zone, Color.white, Color.white,
                    out TextMeshProUGUI headline, out TextMeshProUGUI source);
                headline.text = text;
                source.text = "iTnews (Reuters)";
                headline.ForceMeshUpdate(true, true);
                source.ForceMeshUpdate(true, true);
                float hs = headline.fontSize; // TMP writes the auto-sized result back
                Debug.Log($"[Cards] ,bigCenter \"{text}\": headline {hs:F0} px, source {source.fontSize:F0} px");
                log.IsTrue(hs >= CardTypography.HardFloorPx, $",bigCenter headline at or above 32 px ({hs:F0})");
                log.IsTrue(source.fontSize >= CardTypography.HardFloorPx, $",bigCenter source at or above 32 px ({source.fontSize:F0})");
                UnityEngine.Object.DestroyImmediate(headline.transform.parent.gameObject);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        var logo = BuildCard<LogoDisplay>(out GameObject logoRoot);
        try
        {
            logo.Initialize(new ContentCardEvent { cardType = ContentCardType.Logo, primaryText = "NoSuchLogoForSelfTest" }, null);
            TMP_Text fallback = logo.transform.Find("FallbackText").GetComponent<TMP_Text>();
            Debug.Log($"[Cards] Logo fallback name: {fallback.fontSize:F0} px");
            log.IsTrue(fallback.fontSize >= CardTypography.HardFloorPx, $"Logo fallback name at or above 32 px ({fallback.fontSize:F0})");
        }
        finally { UnityEngine.Object.DestroyImmediate(logoRoot); }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    // A card in a 1000×800 zone (the scene's card size), built the way
    // ContentZoneController builds one. Awake doesn't run for components
    // added in edit mode, so it's invoked here.
    static T BuildCard<T>(out GameObject root) where T : ContentCard
    {
        root = new GameObject("CardSelfTest_" + typeof(T).Name, typeof(RectTransform), typeof(Canvas));
        var zone = new GameObject("Zone", typeof(RectTransform)).GetComponent<RectTransform>();
        zone.SetParent(root.transform, false);
        zone.anchorMin = zone.anchorMax = new Vector2(0.5f, 0.5f);
        zone.sizeDelta = new Vector2(CardTypography.ReferenceCardWidth, CardTypography.ReferenceCardHeight);

        var go = new GameObject(typeof(T).Name, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(zone, false);
        T card = go.AddComponent<T>();
        if (!Application.isPlaying)
            typeof(ContentCard).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                               .Invoke(card, null);
        return card;
    }

    static void CheckCard(Log log, ContentCard card, CardTypography.Report report, string what)
    {
        log.IsTrue(report != null, $"{what}: laid out");
        if (report == null) return;

        var rt = (RectTransform)card.transform;
        Rect inner = InnerRect(card);
        bool overflowWarned = report.Warnings.Exists(w => w.Contains("overflows"));

        foreach (CardTypography.Element e in report.Elements)
        {
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name),
                $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        }

        var summary = new List<string>();
        foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f, $"{what}: {t.name} at or above 32 px ({t.fontSize:F1})");
            log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} not truncated");

            Rect ink = CardTypography.InkInCard(t, rt);
            log.IsTrue(overflowWarned || Inside(ink, inner, 1f),
                $"{what}: {t.name} inside the {CardTypography.PaddingFraction:P0} padding ({Fmt(ink)} in {Fmt(inner)})");
            summary.Add($"{t.name} {t.fontSize:F0}px");
        }

        // How much of the card the text now covers (the brief's 1–2 % baseline).
        float covered = 0f;
        foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text) || t.name == "QuoteMark") continue;
            Rect ink = CardTypography.InkInCard(t, rt);
            covered += ink.width * ink.height;
        }
        Vector2 size = CardTypography.CardSize(rt);
        Debug.Log($"[Cards] self-test {what}: {string.Join(", ", summary)}; text boxes cover " +
                  $"{covered / (size.x * size.y):P0} of the card; group {report.GroupHeight:F0}/{report.InnerHeight:F0} px" +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static Rect InnerRect(ContentCard card)
    {
        Vector2 size = CardTypography.CardSize((RectTransform)card.transform);
        float pad = CardTypography.PaddingFor(size);
        return Rect.MinMaxRect(pad, pad, size.x - pad, size.y - pad);
    }

    // A child RectTransform's rect in card space (x from the left edge, y down from the top).
    static Rect RectInCard(RectTransform child, RectTransform card)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Rect cr = card.rect;
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        foreach (Vector3 w in corners)
        {
            Vector3 p = card.InverseTransformPoint(w);
            float x = p.x - cr.xMin, y = cr.yMax - p.y;
            xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
            yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    static bool Inside(Rect r, Rect outer, float tolerance) =>
        r.width > 0f && r.xMin >= outer.xMin - tolerance && r.xMax <= outer.xMax + tolerance &&
        r.yMin >= outer.yMin - tolerance && r.yMax <= outer.yMax + tolerance;

    static string Fmt(Rect r) => $"x {r.xMin:F0}–{r.xMax:F0}, y {r.yMin:F0}–{r.yMax:F0}";
}

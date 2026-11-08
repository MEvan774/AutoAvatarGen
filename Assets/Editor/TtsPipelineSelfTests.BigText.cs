using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BigText (BigTextTypography + BigTextCard + the stack read-ahead in
/// ContentZoneController). The BigText_Fit / Dim / Anticipate checks are pure;
/// the BigText_*Layout checks build real cards in edit mode and measure them
/// with TMP (menu or batchmode — no play mode, key or credits).
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run BigText Self-Tests")]
    public static void RunBigText()
    {
        var log = new Log();
        RunBigTextGroup(log);
        log.Report();
    }

    static void RunBigTextGroup(Log log)
    {
        BigText_FitWidthCapAndFloor(log);
        BigText_FitKeepsTheStackInsideTitleSafe(log);
        BigText_DimAlphaGivesTheTargetBrightness(log);
        BigText_DimTimelineHeldStack(log);
        BigText_DimTimelineTimedStack(log);
        BigText_AnticipatesTheWholeStack(log);
        BigText_PunchCurve(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            BigText_HeavyFaceStrokes(log);
            BigText_OneWordLayout(log);
            BigText_SafeOptionLayout(log);
            BigText_FourLineStackLayout(log);
            BigText_BinanceStackLayout(log);
            BigText_TimedStackEntranceFrameByFrame(log);
            BigText_HeldAppendFrameByFrame(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"BigText layout checks threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
        }
    }

    // -----------------------------------------------------------------------
    // Pure sizing
    // -----------------------------------------------------------------------

    const float Cap = 0.727f; // Inter's cap height ÷ size

    static void BigText_FitWidthCapAndFloor(Log log)
    {
        // A short word: the width allows far more — the 140 px cap height wins.
        var a = BigTextTypography.FitStack(new[] { 300f }, new[] { 300f }, (i, s) => 1, Cap, 1920f, 1080f);
        log.Near(140f, a.Cap, 0.5f, "a one-word line stops at the 140 px cap height");
        log.IsTrue(!a.Wrapped && !a.BelowFloor, "…unwrapped, no warning");

        // A medium line: width-limited between the floor and the ceiling.
        var b = BigTextTypography.FitStack(new[] { 1200f }, new[] { 400f }, (i, s) => 1, Cap, 1920f, 1080f);
        log.Near(0.85f * 1920f, b.Size * 12f, 1f, "a width-limited line fills 85 % of the frame");
        log.IsTrue(b.Cap >= 80f && b.Cap <= 140f, $"…with its cap height in 80–140 ({b.Cap:F0})");

        // A long line: under 80 px it stops shrinking and wraps instead.
        var c = BigTextTypography.FitStack(new[] { 2000f }, new[] { 400f }, (i, s) => 2, Cap, 1920f, 1080f);
        log.Near(80f, c.Cap, 0.5f, "a long line holds the 80 px cap floor");
        log.IsTrue(c.Wrapped && !c.BelowFloor, "…and wraps rather than shrinking");

        // The LONGEST line sizes the whole stack.
        var d = BigTextTypography.FitStack(new[] { 600f, 1200f }, new[] { 300f, 400f }, (i, s) => 1, Cap, 1920f, 1080f);
        log.Near(b.Size, d.Size, 0.01f, "a stack takes its longest line's size");

        // A single word too long for the line can't wrap: it shrinks, flagged.
        var e = BigTextTypography.FitStack(new[] { 2500f }, new[] { 1800f }, (i, s) => 2, Cap, 1920f, 1080f);
        log.IsTrue(e.BelowFloor && e.Size * 18f <= 0.85f * 1920f + 0.5f, "an over-long word shrinks the line, flagged");

        // Scales with the frame: a 4K frame doubles the ceiling.
        var f = BigTextTypography.FitStack(new[] { 300f }, new[] { 300f }, (i, s) => 1, Cap, 3840f, 2160f);
        log.Near(280f, f.Cap, 1f, "a 4K frame gets a 280 px cap ceiling");
    }

    static void BigText_FitKeepsTheStackInsideTitleSafe(Log log)
    {
        float safe = 1080f * BigTextTypography.TitleSafeFraction;

        // 4 lines × 2 rows at the floor already fit — nothing shrinks.
        var a = BigTextTypography.FitStack(new[] { 2000f, 2000f, 2000f, 2000f }, new[] { 400f, 400f, 400f, 400f },
            (i, s) => 2, Cap, 1920f, 1080f);
        log.Near(80f, a.Cap, 0.5f, "4 lines × 2 rows keep the 80 px floor");
        log.IsTrue(a.StackHeight <= safe, $"…and fit the title-safe height ({a.StackHeight:F0} ≤ {safe:F0})");

        // 4 lines × 3 rows don't: the stack shrinks until it fits, flagged.
        var b = BigTextTypography.FitStack(new[] { 4000f, 4000f, 4000f, 4000f }, new[] { 400f, 400f, 400f, 400f },
            (i, s) => 3, Cap, 1920f, 1080f);
        log.IsTrue(b.StackHeight <= safe + 0.5f, $"a too-tall stack shrinks into title-safe ({b.StackHeight:F0})");
        log.IsTrue(b.BelowFloor && !b.TallerThanSafe, "…flagged as under the floor, but inside title-safe");

        log.Near(80f + BigTextTypography.RowAdvanceToCap * 80f,
            BigTextTypography.StackHeight(new[] { 2 }, 80f), 0.01f, "a wrapped line is cap + one row advance tall");
        log.Near(2f * 80f + BigTextTypography.LineGapToCap * 80f,
            BigTextTypography.StackHeight(new[] { 1, 1 }, 80f), 0.01f, "two lines are separated by the line gap");
    }

    // -----------------------------------------------------------------------
    // Pure dim
    // -----------------------------------------------------------------------

    static void BigText_DimAlphaGivesTheTargetBrightness(Log log)
    {
        float lin = BigTextTypography.DimAlpha(true);
        log.Near(BigTextTypography.DimBrightness, Mathf.Pow(1f - lin, 1f / 2.2f), 0.001f,
            $"linear colour space: alpha {lin:F3} leaves the scene at 45 % in the video");
        log.Near(1f - BigTextTypography.DimBrightness, BigTextTypography.DimAlpha(false), 1e-5f,
            "gamma colour space: alpha 0.55");
    }

    static void BigText_DimTimelineHeldStack(Log log)
    {
        // {BigText:L1} at 10, {BigText:L2} at 12 (joins), {BigText:End} at 15.
        var d = new BigTextTypography.DimEnvelope(0.8f);
        log.AreEqual(0f, d.AlphaAt(9f), "held: no dim before the first line");
        d.Open(10f);
        log.Near(0f, d.AlphaAt(10f), 1e-5f, "held: fades in from 0 on the first line");
        log.Near(0.4f, d.AlphaAt(10.1f), 1e-4f, "held: halfway through the 0.2 s fade-in");
        log.Near(0.8f, d.AlphaAt(10.2f), 1e-5f, "held: full after 0.2 s");
        d.Open(12f); // a joining line never restarts the fade
        log.Near(0.8f, d.AlphaAt(12.05f), 1e-5f, "held: a joining line leaves the dim at full");
        d.Close(15f);
        log.Near(0.8f, d.AlphaAt(15f), 1e-5f, "held: still full on the End's frame");
        log.Near(0.4f, d.AlphaAt(15.125f), 1e-4f, "held: halfway through the 0.25 s fade-out");
        log.Near(0f, d.AlphaAt(15.25f), 1e-5f, "held: gone 0.25 s after the End");
        d.Open(16f);
        log.Near(0f, d.AlphaAt(16.5f), 1e-5f, "held: a closed dim never reopens");
    }

    static void BigText_DimTimelineTimedStack(Log log)
    {
        // {BigText:A+B,3} at 20; a duration-less {BigText:C} joins at 21.5; the timer ends it at 23.
        var d = new BigTextTypography.DimEnvelope(0.8f);
        d.Open(20f);
        d.Open(21.5f);
        log.Near(0.8f, d.AlphaAt(21.6f), 1e-5f, "timed + joined: the joining line changes nothing");
        d.Close(23f);
        log.Near(0f, d.AlphaAt(23.25f), 1e-5f, "timed + joined: gone 0.25 s after the timed card ends");

        // Closed mid-fade-in: fades out from where it was — no jump.
        var e = new BigTextTypography.DimEnvelope(0.8f);
        e.Open(0f);
        e.Close(0.1f);
        log.Near(0.4f, e.AlphaAt(0.1f), 1e-4f, "closed mid-fade-in: continuous at the close");
        log.Near(0.2f, e.AlphaAt(0.225f), 1e-4f, "…and fades out from there");
    }

    // -----------------------------------------------------------------------
    // Pure read-ahead (ContentZoneController.AnticipateBigTextLines)
    // -----------------------------------------------------------------------

    static ContentCardEvent BT(string text, float t, float d = 0f, bool end = false) =>
        new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = text, triggerTime = t, duration = d, dismissesCard = end };

    static void BigText_AnticipatesTheWholeStack(Log log)
    {
        var open = BT("A COMPANY CAN LOSE IT", 28f);
        var timeline = new List<ContentCardEvent>
        {
            BT("THERE IS NO 100% SAFE OPTION", 23.5f), BT("End", 27.3f, end: true),
            open,
            new ContentCardEvent { cardType = ContentCardType.Stat, primaryText = "$1", triggerTime = 30f },
            BT("YOU CAN LOSE IT", 33.4f), BT("End", 36.3f, end: true),
            BT("LATER", 40f),
        };
        log.AreEqual("A COMPANY CAN LOSE IT|YOU CAN LOSE IT",
            string.Join("|", ContentZoneController.AnticipateBigTextLines(timeline, open)),
            "held: the stack's later lines, up to its End");

        var timed = BT("A+B", 0f, 3f);
        var t2 = new List<ContentCardEvent> { timed, BT("C", 1.5f), BT("D", 4f) };
        log.AreEqual("A|B|C", string.Join("|", ContentZoneController.AnticipateBigTextLines(t2, timed)),
            "timed: its own lines plus the duration-less one that joins within its 3 s");

        var t3 = new List<ContentCardEvent> { BT("A", 0f), BT("B+C", 1f), BT("D+E", 2f), BT("End", 3f, end: true) };
        log.AreEqual(4, ContentZoneController.AnticipateBigTextLines(t3, t3[0]).Count, "never more than 4 lines");

        var claimed = BT("UNDER A TRANSITION", 5f);
        log.AreEqual("UNDER A TRANSITION", string.Join("|", ContentZoneController.AnticipateBigTextLines(t3, claimed)),
            "an event not in the timeline (transition-claimed) keeps just its own lines");

        var t4 = new List<ContentCardEvent> { BT("A", 0f), BT("TIMED", 1f, 2f), BT("B", 2f) };
        log.AreEqual("A", string.Join("|", ContentZoneController.AnticipateBigTextLines(t4, t4[0])),
            "a later timed BigText is its own card — the read-ahead stops there");
    }

    // -----------------------------------------------------------------------
    // Real cards, measured with TMP
    // -----------------------------------------------------------------------

    static void BigText_HeavyFaceStrokes(Log log)
    {
        TMP_FontAsset heavy = CardTypography.InterExtraBold;
        log.IsTrue(heavy != null, "Inter ExtraBold loads");
        if (heavy == null) return;
        bool ok = heavy.characterLookupTable.TryGetValue('I', out TMP_Character ch) && ch.glyph != null;
        log.IsTrue(ok, "Inter ExtraBold has an 'I'");
        if (!ok) return;
        float stem = ch.glyph.metrics.width / Mathf.Max(1f, heavy.faceInfo.capLine);
        Debug.Log($"[BigText] self-test: Inter ExtraBold stroke = {stem:P0} of the cap height");
        log.IsTrue(stem >= 0.12f, $"strokes at least 12 % of the cap height ({stem:P0})");
    }

    static BigTextCard BuildBigText(out GameObject root)
    {
        var card = BuildCard<BigTextCard>(out root);
        var zone = (RectTransform)card.transform.parent;
        zone.sizeDelta = new Vector2(1920f, 1080f);
        return card;
    }

    static List<TMP_Text> ActiveLineTexts(BigTextCard card)
    {
        var list = new List<TMP_Text>();
        Transform lines = card.transform.Find("Lines");
        for (int i = 0; i < lines.childCount; i++)
        {
            Transform c = lines.GetChild(i);
            if (c.gameObject.activeSelf) list.Add(c.Find("Text").GetComponent<TMP_Text>());
        }
        return list;
    }

    // Checks every active line: inside title-safe, at/above the floor (unless
    // flagged), rows broken only at spaces, the outline drawn outside the face.
    static void CheckBigText(Log log, BigTextCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        float k = 1f;
        Rect safe = Rect.MinMaxRect(1920f * 0.05f, 1080f * 0.05f, 1920f * 0.95f, 1080f * 0.95f);
        BigTextTypography.StackFit fit = card.LastFit;
        log.IsTrue(fit.Cap / k >= BigTextTypography.CapMinPx - 0.5f || fit.BelowFloor,
            $"{what}: cap {fit.Cap:F0} px at/above 80 or flagged");
        log.IsTrue(fit.Cap / k <= BigTextTypography.CapMaxPx + 0.5f, $"{what}: cap {fit.Cap:F0} px at most 140");

        Transform dim = card.transform.Find("Dim");
        log.IsTrue(dim != null && dim.GetSiblingIndex() == 0, $"{what}: the dim is the bottom layer");
        if (dim != null)
        {
            var drt = (RectTransform)dim;
            log.IsTrue(drt.anchorMin == Vector2.zero && drt.anchorMax == Vector2.one && drt.offsetMin == Vector2.zero
                       && drt.offsetMax == Vector2.zero, $"{what}: the dim covers the whole frame");
        }

        var rows = new List<string>();
        foreach (TMP_Text t in ActiveLineTexts(card))
        {
            Rect ink = CardTypography.InkInCard(t, rt);
            log.IsTrue(Inside(ink, safe, 1f), $"{what}: \"{t.text}\" inside title-safe ({Fmt(ink)})");
            log.IsTrue(ink.width <= 1920f * BigTextTypography.TargetWidthFraction + 2f,
                $"{what}: \"{t.text}\" at most 85 % of the frame wide ({ink.width / 1920f:P0})");

            TMP_TextInfo info = t.textInfo;
            for (int l = 1; l < info.lineCount; l++)
            {
                int first = info.lineInfo[l].firstCharacterIndex;
                bool atSpace = first > 0 && char.IsWhiteSpace(t.text[first - 1]);
                log.IsTrue(atSpace, $"{what}: \"{t.text}\" breaks at a word boundary (row {l + 1})");
            }
            for (int l = 0; l < info.lineCount; l++)
            {
                TMP_LineInfo li = info.lineInfo[l];
                rows.Add(t.text.Substring(li.firstCharacterIndex,
                    Mathf.Max(0, li.lastCharacterIndex - li.firstCharacterIndex + 1)).Trim());
            }

            Material m = t.fontMaterial;
            log.Near(m.GetFloat("_OutlineWidth"), m.GetFloat("_FaceDilate"), 1e-4f,
                $"{what}: the outline sits outside the letters (face dilated by its width)");
        }
        Debug.Log($"[BigText] self-test {what}: cap {fit.Cap:F0} px (font {fit.Size:F0}), " +
                  $"stack {fit.StackHeight:F0}/972 px, rows: {string.Join(" / ", rows)}");
    }

    static void BigText_OneWordLayout(Log log)
    {
        var card = BuildBigText(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = "HACKED" }, null);
            CheckBigText(log, card, "one word");
            log.Near(140f, card.LastFit.Cap, 0.5f, "one word: 140 px cap height");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void BigText_SafeOptionLayout(Log log)
    {
        var card = BuildBigText(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = "THERE IS NO 100% SAFE OPTION" }, null);
            CheckBigText(log, card, "THERE IS NO 100% SAFE OPTION");
            TMP_Text t = ActiveLineTexts(card)[0];
            log.IsTrue(card.LastFit.Wrapped && t.textInfo.lineCount == 2,
                $"28 characters at the 80 px floor wrap into 2 rows ({t.textInfo.lineCount})");
            if (t.textInfo.lineCount == 2)
            {
                float w0 = t.textInfo.lineInfo[0].width, w1 = t.textInfo.lineInfo[1].width;
                log.IsTrue(Mathf.Min(w0, w1) / Mathf.Max(w0, w1) >= 0.55f,
                    $"…balanced rows, not a long row over a stub ({w0:F0} / {w1:F0} px)");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static readonly string[] FourLines =
    {
        "ELEVEN KEYS SIGNED IT ALL", "NOBODY NOTICED FOR HOURS!", "THE MONEY LEFT IN MINUTES", "AND NOW IT IS ALL FROZEN.",
    };

    static void BigText_FourLineStackLayout(Log log)
    {
        foreach (string l in FourLines) log.AreEqual(25, l.Length, $"test line \"{l}\" is 25 characters");

        float timedSize;
        var card = BuildBigText(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = string.Join("+", FourLines) }, null);
            CheckBigText(log, card, "4 × 25 characters");
            log.AreEqual(4, ActiveLineTexts(card).Count, "4 × 25: all four lines shown");
            log.IsTrue(card.LastFit.StackHeight <= 1080f * BigTextTypography.TitleSafeFraction,
                $"4 × 25: the stack fits title-safe ({card.LastFit.StackHeight:F0} px)");
            timedSize = card.LastFit.Size;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        // Held flow: the first line alone, with the other three read ahead,
        // gets the size the full stack will need — no jump when they arrive.
        var held = BuildBigText(out GameObject root2);
        try
        {
            held.AnticipatedLines = new List<string>(FourLines);
            held.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = FourLines[0] }, null);
            log.Near(timedSize, held.LastFit.Size, 0.5f, "held: the first line is already sized for the whole read-ahead stack");
        }
        finally { UnityEngine.Object.DestroyImmediate(root2); }
    }

    static void BigText_BinanceStackLayout(Log log)
    {
        var card = BuildBigText(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent
            {
                cardType = ContentCardType.BigText, primaryText = "A COMPANY CAN LOSE IT+YOU CAN LOSE IT",
            }, null);
            CheckBigText(log, card, "Binance 5:15 stack");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // -----------------------------------------------------------------------
    // Entrance timing (the 2026-11-08 test take: lines hung at 1.12×)
    // -----------------------------------------------------------------------

    static void BigText_PunchCurve(Log log)
    {
        log.Near(BigTextTypography.PunchFromScale, BigTextTypography.PunchScaleAt(0f), 1e-5f, "punch starts at 1.12×");
        log.Near(1f, BigTextTypography.PunchScaleAt(BigTextTypography.PunchDuration), 1e-5f, "punch ends at 1.0 after 0.18 s");
        float min = 2f;
        for (float t = 0f; t <= BigTextTypography.PunchDuration; t += 0.002f) min = Mathf.Min(min, BigTextTypography.PunchScaleAt(t));
        log.IsTrue(min < 1f && min > 0.975f, $"…with a slight overshoot under 1.0 (lowest {min:F3})");
        log.AreEqual(0f, BigTextTypography.LineAlphaAt(-0.01f), "a line is invisible before its beat");
        log.AreEqual(1f, BigTextTypography.LineAlphaAt(0f), "…and fully there on its first frame");
    }

    const float Frame = 1f / 30f;

    static void DestroyAutoAnimator()
    {
        GameObject auto = GameObject.Find("CardEntryAnimator (auto)");
        if (auto != null) UnityEngine.Object.DestroyImmediate(auto);
    }

    // Steps a timed 3-line stack at 30 fps and checks each line appears on its
    // stagger, shows the full 1.12× on its first frame only, and is settled
    // 0.18 s later — the punch never waits after its line has appeared.
    static void BigText_TimedStackEntranceFrameByFrame(Log log)
    {
        var card = BuildBigText(out GameObject root);
        try
        {
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = "THREE+LINES+AT ONCE", duration = 4f }, null);
            card.Show();
            float stagger = CardEntryAnimator.Instance.bigText.staggerDelay;

            const int frames = 45;
            var firstSeen = new int[] { -1, -1, -1 };
            var bigFrames = new int[3];
            var scaleAt = new float[3, frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                if (f > 0) card.Advance(Frame);
                for (int i = 0; i < 3; i++)
                {
                    float a = card.LineAlpha(i), sc = card.LineContainer(i).localScale.x;
                    scaleAt[i, f] = sc;
                    if (a > 0.5f && firstSeen[i] < 0) firstSeen[i] = f;
                    if (a > 0.5f && sc >= 1.10f) bigFrames[i]++;
                }
            }

            for (int i = 0; i < 3; i++)
            {
                log.IsTrue(firstSeen[i] >= 0, $"timed stack: line {i + 1} appears");
                if (firstSeen[i] < 0) continue;
                float shownAt = firstSeen[i] * Frame;
                log.IsTrue(Mathf.Abs(shownAt - stagger * i) <= Frame + 1e-4f,
                    $"timed stack: line {i + 1} appears on its {stagger * i:F2} s beat (at {shownAt:F3} s)");
                log.Near(BigTextTypography.PunchFromScale, scaleAt[i, firstSeen[i]], 1e-3f,
                    $"timed stack: line {i + 1} is at the full 1.12× on its first frame");
                log.AreEqual(1, bigFrames[i], $"timed stack: line {i + 1} is that big for one frame only — it never hangs");
                int settled = firstSeen[i] + Mathf.CeilToInt(BigTextTypography.PunchDuration / Frame) + 1;
                if (settled <= frames)
                    log.Near(1f, scaleAt[i, settled], 1e-3f, $"timed stack: line {i + 1} settled at 1.0 within 0.18 s");
            }
            log.Near(BigTextTypography.DimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear), card.DimAlpha, 1e-3f,
                "timed stack: the dim is full once the entrance is over");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); DestroyAutoAnimator(); }
    }

    // Held flow: a line joins an open stack — it punches in on its beat while
    // the line already up glides to its re-centred slot.
    static void BigText_HeldAppendFrameByFrame(Log log)
    {
        var card = BuildBigText(out GameObject root);
        try
        {
            card.AnticipatedLines = new List<string> { "A COMPANY CAN LOSE IT", "YOU CAN LOSE IT" };
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigText, primaryText = "A COMPANY CAN LOSE IT" }, null);
            card.Show();
            for (int f = 0; f < 15; f++) card.Advance(Frame);
            float firstSlotAlone = card.LineContainer(0).anchoredPosition.y;

            log.IsTrue(card.AppendLines("YOU CAN LOSE IT"), "held: the second line is added");
            log.Near(1f, card.LineAlpha(1), 1e-3f, "held: the new line is visible on the frame of its beat");
            log.Near(BigTextTypography.PunchFromScale, card.LineContainer(1).localScale.x, 1e-3f, "held: …at the full 1.12×");

            int framesToSettle = Mathf.CeilToInt(BigTextTypography.LineShiftSeconds / Frame) + 1;
            for (int f = 0; f < framesToSettle; f++) card.Advance(Frame);
            log.Near(1f, card.LineContainer(1).localScale.x, 1e-3f, "held: the new line has settled");
            log.Near(1f, card.LineContainer(0).localScale.x, 1e-3f, "held: the first line is still at 1.0");
            log.IsTrue(card.LineContainer(0).anchoredPosition.y > firstSlotAlone + 1f,
                "held: the first line glided up to make room (the stack re-centred)");
            float y0 = card.LineContainer(0).anchoredPosition.y, y1 = card.LineContainer(1).anchoredPosition.y;
            log.Near(0f, (y0 + y1) * 0.5f, 1f, "held: the two-line stack is centred");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); DestroyAutoAnimator(); }
    }
}

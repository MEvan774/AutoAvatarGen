using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen chapter titles (ChapterTitles rules + the ChapterTitleController
/// block). Chapters_* rule checks are pure; Chapters_Block* build the real
/// title block in edit mode and measure it with TMP.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Chapter Title Self-Tests")]
    public static void RunChapters()
    {
        var log = new Log();
        RunChapterGroup(log);
        log.Report();
    }

    static void RunChapterGroup(Log log)
    {
        Chapters_PipCounts(log);
        Chapters_FirstTitleIsNeverShown(log);
        Chapters_SideIsOppositeMugs(log);
        Chapters_SpotAvoidsObstacles(log);
        Chapters_PlacementStaysInTheBand(log);
        Chapters_HideOnInterrupt(log);
        Chapters_LabelShrinksThenTruncates(log);
        Chapters_PairsTheOpeningTransition(log);
        Chapters_Timeline(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Chapters_BlockLayout(log);
            Chapters_BlockLongLabel(log);
            Chapters_MugsIsMeasuredByHisOutline(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"chapter title block checks threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
        }
    }

    // -----------------------------------------------------------------------
    // Pure rules
    // -----------------------------------------------------------------------

    static string PipString(bool[] p)
    {
        var c = new char[p.Length];
        for (int i = 0; i < p.Length; i++) c[i] = p[i] ? '●' : '○';
        return new string(c);
    }

    static void Chapters_PipCounts(Log log)
    {
        log.AreEqual(0, ChapterTitles.TitleCount(1), "1 timestamp: no titles at all (it's the cold open)");
        log.AreEqual(4, ChapterTitles.TitleCount(5), "5 timestamps: 4 titles");
        log.AreEqual(8, ChapterTitles.TitleCount(9), "9 timestamps: 8 titles");

        log.AreEqual("●", PipString(ChapterTitles.Pips(1, 0)), "1 timestamp: one pip");
        log.AreEqual("●●○○○", PipString(ChapterTitles.Pips(5, 1)), "5 timestamps, chapter 2: 2 of 5 filled");
        log.AreEqual("●●●○○", PipString(ChapterTitles.Pips(5, 2)), "5 timestamps, chapter 3 (Eleven Keys): 3 of 5 filled");
        log.AreEqual("●●●●●", PipString(ChapterTitles.Pips(5, 4)), "5 timestamps, chapter 5: all filled");
        log.AreEqual("●●●●○○○○○", PipString(ChapterTitles.Pips(9, 3)), "9 timestamps, chapter 4: 4 of 9 filled");
        log.AreEqual(9, ChapterTitles.Pips(9, 8).Length, "9 timestamps: nine pips");
    }

    static void Chapters_FirstTitleIsNeverShown(Log log)
    {
        log.IsTrue(!ChapterTitles.ShowsTitle(0), "the first chapter (cold open) never gets a title");
        for (int i = 1; i < 9; i++) log.IsTrue(ChapterTitles.ShowsTitle(i), $"chapter {i + 1} gets a title");
    }

    static void Chapters_SideIsOppositeMugs(Log log)
    {
        log.AreEqual(ChapterTitles.Side.Right, ChapterTitles.OppositeOf(CharacterPosition.Left), "Mugs Left → title Right");
        log.AreEqual(ChapterTitles.Side.Left, ChapterTitles.OppositeOf(CharacterPosition.Right), "Mugs Right → title Left");
        log.AreEqual(ChapterTitles.Side.Center, ChapterTitles.OppositeOf(CharacterPosition.Center), "Mugs Center → title centred");
    }

    static void Chapters_SpotAvoidsObstacles(Log log)
    {
        const float w = 0.18f, h = 0.05f;
        var none = new List<Rect>();
        log.AreEqual((ChapterTitles.Side?)ChapterTitles.Side.Left,
            ChapterTitles.ChooseSpot(ChapterTitles.Side.Left, w, h, none), "free band → the preferred side");

        // Mugs on the right with his head at 19 %: clear of the band, the left spot stays.
        var mugsRight = new List<Rect> { Rect.MinMaxRect(0.55f, 0.19f, 0.9f, 1f) };
        log.AreEqual((ChapterTitles.Side?)ChapterTitles.Side.Left,
            ChapterTitles.ChooseSpot(ChapterTitles.Side.Left, w, h, mugsRight), "Mugs's head below the band doesn't block it");

        // A zoomed Mugs whose ears reach into the band at the centre: the title moves to a side.
        var headInBand = new List<Rect> { Rect.MinMaxRect(0.35f, 0.07f, 0.65f, 1f) };
        log.AreEqual((ChapterTitles.Side?)ChapterTitles.Side.Left,
            ChapterTitles.ChooseSpot(ChapterTitles.Side.Center, w, h, headInBand), "a head in the centre of the band → next free side");

        // Blocked everywhere: not shown.
        var full = new List<Rect> { Rect.MinMaxRect(0f, 0f, 1f, 0.2f) };
        log.IsTrue(ChapterTitles.ChooseSpot(ChapterTitles.Side.Center, w, h, full) == null, "no free spot → no title");
    }

    static void Chapters_PlacementStaysInTheBand(Log log)
    {
        foreach (ChapterTitles.Side s in new[] { ChapterTitles.Side.Left, ChapterTitles.Side.Center, ChapterTitles.Side.Right })
        {
            Rect r = ChapterTitles.BlockRect(s, 0.25f, 0.054f);
            log.IsTrue(ChapterTitles.InAllowedArea(r), $"{s}: inside title-safe and above the 11 % line ({r.yMax:F3})");
            log.IsTrue(r.yMax < ChapterTitles.BottomKeepOut, $"{s}: nowhere near the bottom 15 %");
        }
        Rect left = ChapterTitles.BlockRect(ChapterTitles.Side.Left, 0.25f, 0.05f);
        Rect right = ChapterTitles.BlockRect(ChapterTitles.Side.Right, 0.25f, 0.05f);
        log.Near(0.05f, left.xMin, 1e-5f, "Left hugs the left title-safe margin");
        log.Near(0.95f, right.xMax, 1e-5f, "Right hugs the right title-safe margin");
        log.IsTrue(!ChapterTitles.InAllowedArea(ChapterTitles.BlockRect(ChapterTitles.Side.Left, 0.25f, 0.08f)),
            "a block too tall for the band is not allowed");
    }

    static void Chapters_HideOnInterrupt(Log log)
    {
        log.AreEqual(null, ChapterTitles.InterruptReason(new ChapterTitles.InterruptState()), "nothing happening → stays up");
        log.AreEqual("{Black} started",
            ChapterTitles.InterruptReason(new ChapterTitles.InterruptState { BlackShowing = true }), "{Black} hides it");
        log.AreEqual("{Zoom:ExtremeIn} close-up",
            ChapterTitles.InterruptReason(new ChapterTitles.InterruptState { CloseUp = true }), "an extreme close-up hides it");
        log.AreEqual("a {Transition} started",
            ChapterTitles.InterruptReason(new ChapterTitles.InterruptState { TransitionStarted = true }), "a new transition hides it");
        log.AreEqual("fullscreen BigText appeared",
            ChapterTitles.InterruptReason(new ChapterTitles.InterruptState { FeatureCardArrived = true, FeatureCardName = "BigText" }),
            "a fullscreen card appearing hides it");

        log.IsTrue(!ChapterTitles.IsArrival(false, 0.1f), "a fullscreen card landing with the title is its backdrop, not an interruption");
        log.IsTrue(ChapterTitles.IsArrival(false, 1.2f), "a fullscreen card appearing 1.2 s later interrupts");
        log.IsTrue(!ChapterTitles.IsArrival(true, 5f), "the card that was already up never counts as arriving");
    }

    static void Chapters_LabelShrinksThenTruncates(Log log)
    {
        Func<string, float, float> width = (t, s) => t.Length * 0.6f * s; // a monospace stand-in
        const float maxW = 768f;

        var a = ChapterTitles.FitLabel("THE LAPTOP", width, maxW, 44f, 32f);
        log.IsTrue(a.Size == 44f && !a.Shrunk && !a.Truncated && a.Text == "THE LAPTOP", "a short label stays at 44 px");

        var b = ChapterTitles.FitLabel("WHAT THE LAWSUITS ACTUALLY SAY NOW", width, maxW, 44f, 32f); // 34 chars
        log.IsTrue(b.Shrunk && !b.Truncated && b.Size >= 32f && b.Size < 44f && width(b.Text, b.Size) <= maxW,
            $"a long label shrinks (to {b.Size:F1} px) before anything is cut");

        string longLabel = "WHY EVERY EXCHANGE SAYS YOUR MONEY IS SAFE UNTIL THE DAY IT SUDDENLY ISN'T";
        var c = ChapterTitles.FitLabel(longLabel, width, maxW, 44f, 32f);
        log.IsTrue(c.Truncated && c.Size == 32f, "too long even at 32 px → truncated at 32 px");
        log.IsTrue(c.Text.EndsWith("…") && width(c.Text, 32f) <= maxW, $"…ending in an ellipsis, inside the width (\"{c.Text}\")");
        log.IsTrue(longLabel.StartsWith(c.Text.TrimEnd('…')) && longLabel[c.Text.Length - 1] == ' ',
            "…cut at a word boundary");
    }

    static void Chapters_PairsTheOpeningTransition(Log log)
    {
        // Binance: Shutter at ~94.19 s, Eleven Keys' Timestamp at 95.06 s.
        log.AreEqual(0, ChapterTitles.OpeningTransition(95.06f, new[] { 94.19f, 130f }), "the transition just before the Timestamp opens its section");
        log.AreEqual(-1, ChapterTitles.OpeningTransition(95.06f, new[] { 80f }), "a transition seconds earlier doesn't");
        log.AreEqual(-1, ChapterTitles.OpeningTransition(95.06f, new float[0]), "no transition → shows on the Timestamp's word");
        log.AreEqual(1, ChapterTitles.OpeningTransition(95.06f, new[] { 93.0f, 94.3f }), "the latest one in the window wins");
    }

    static void Chapters_Timeline(Log log)
    {
        log.Near(0f, ChapterTitles.AlphaAt(0f), 1e-5f, "fades in from 0");
        log.Near(1f, ChapterTitles.AlphaAt(ChapterTitles.FadeInSeconds), 1e-5f, "in after 0.25 s");
        log.Near(1f, ChapterTitles.AlphaAt(ChapterTitles.FadeInSeconds + ChapterTitles.HoldSeconds - 0.01f), 1e-5f, "holds 2.5 s");
        log.Near(0f, ChapterTitles.AlphaAt(ChapterTitles.TotalSeconds), 1e-5f, "gone 0.3 s after the hold");
        log.Near(ChapterTitles.SlidePx, ChapterTitles.SlideAt(0f), 1e-5f, "starts a few px high");
        log.Near(0f, ChapterTitles.SlideAt(ChapterTitles.FadeInSeconds), 1e-5f, "…and lands as the fade completes");
    }

    // -----------------------------------------------------------------------
    // The real block, measured with TMP
    // -----------------------------------------------------------------------

    static ChapterTitleController BuildTitleBlock(out GameObject root, out RectTransform frame)
    {
        root = new GameObject("ChapterSelfTest", typeof(RectTransform), typeof(Canvas));
        frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root.transform, false);
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2(1920f, 1080f);
        var c = new GameObject("Controller").AddComponent<ChapterTitleController>();
        c.transform.SetParent(root.transform, false);
        c.BuildTitle(frame);
        return c;
    }

    static Rect NormInFrame(Rect cardSpace) =>
        Rect.MinMaxRect(cardSpace.xMin / 1920f, cardSpace.yMin / 1080f, cardSpace.xMax / 1920f, cardSpace.yMax / 1080f);

    static Rect PipsInk(ChapterTitleController c, RectTransform frame)
    {
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        var corners = new Vector3[4];
        Rect fr = frame.rect;
        foreach (Image p in c.PipImages)
        {
            if (!p.gameObject.activeSelf) continue;
            p.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 w in corners)
            {
                Vector3 l = frame.InverseTransformPoint(w);
                float x = l.x - fr.xMin, y = fr.yMax - l.y;
                xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
                yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
            }
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    static void Chapters_BlockLayout(Log log)
    {
        var c = BuildTitleBlock(out GameObject root, out RectTransform frame);
        try
        {
            foreach (string name in new[] { "The Laptop", "Eleven Keys", "The Freeze", "The Choice" })
            {
                Vector2 block = c.Compose(2, 5, name, 1f, out bool shrunk, out bool truncated, out string shown);
                log.IsTrue(!shrunk && !truncated && shown == name.ToUpperInvariant(), $"\"{name}\": in caps at full size");
                log.Near(ChapterTitles.LabelPx, c.LabelText.fontSize, 0.01f, $"\"{name}\": 44 px");

                foreach (ChapterTitles.Side side in new[] { ChapterTitles.Side.Left, ChapterTitles.Side.Center, ChapterTitles.Side.Right })
                {
                    c.PlaceForTest(side, block);
                    Rect label = NormInFrame(CardTypography.InkInCard(c.LabelText, frame));
                    Rect pipsR = NormInFrame(PipsInk(c, frame));
                    Rect all = Rect.MinMaxRect(Mathf.Min(label.xMin, pipsR.xMin), Mathf.Min(label.yMin, pipsR.yMin),
                                               Mathf.Max(label.xMax, pipsR.xMax), Mathf.Max(label.yMax, pipsR.yMax));
                    log.IsTrue(all.xMin >= ChapterTitles.SafeMargin - 0.002f && all.xMax <= 1f - ChapterTitles.SafeMargin + 0.002f &&
                               all.yMin >= ChapterTitles.SafeMargin - 0.002f && all.yMax <= ChapterTitles.BandBottom,
                        $"\"{name}\" {side}: label + pips inside title-safe, above 11 % (x {all.xMin:F3}–{all.xMax:F3}, y {all.yMin:F3}–{all.yMax:F3})");
                    if (side == ChapterTitles.Side.Left)
                        log.Near(ChapterTitles.SafeMargin, Mathf.Min(label.xMin, pipsR.xMin), 0.006f, $"\"{name}\" Left: flush with the left margin");
                    if (side == ChapterTitles.Side.Right)
                        log.Near(1f - ChapterTitles.SafeMargin, Mathf.Max(label.xMax, pipsR.xMax), 0.006f, $"\"{name}\" Right: flush with the right margin");
                    if (side == ChapterTitles.Side.Center)
                        log.Near(0.5f, (label.xMin + label.xMax) * 0.5f, 0.006f, $"\"{name}\" Center: centred");
                    log.IsTrue(pipsR.yMin > label.yMax, $"\"{name}\" {side}: the pips sit under the label");
                }
            }

            int filled = 0, empty = 0;
            foreach (Image p in c.PipImages)
            {
                if (!p.gameObject.activeSelf) continue;
                if (p.color.a > 0.99f) filled++; else empty++;
            }
            log.AreEqual(3, filled, "chapter 3 of 5: three filled pips");
            log.AreEqual(2, empty, "…and two empty ones");
            Debug.Log($"[Chapter] self-test block \"THE CHOICE\": label {c.LabelText.fontSize:F0} px, block {c.TitleRect.sizeDelta.x:F0}×{c.TitleRect.sizeDelta.y:F0} px");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Chapters_BlockLongLabel(Log log)
    {
        var c = BuildTitleBlock(out GameObject root, out RectTransform frame);
        try
        {
            Vector2 block = c.Compose(1, 9, "Why Every Exchange Says Your Money Is Safe Until The Day It Suddenly Isn't", 1f,
                out bool shrunk, out bool truncated, out string shown);
            log.IsTrue(shrunk && truncated, "a 75-character label is shrunk and then truncated");
            log.Near(ChapterTitles.LabelMinPx, c.LabelText.fontSize, 0.01f, "…at the 32 px minimum");
            log.IsTrue(shown.EndsWith("…"), $"…ending in an ellipsis (\"{shown}\")");
            c.PlaceForTest(ChapterTitles.Side.Center, block);
            Rect label = NormInFrame(CardTypography.InkInCard(c.LabelText, frame));
            log.IsTrue(label.width <= ChapterTitles.MaxLabelWidth + 0.002f, $"…no wider than 40 % of the frame ({label.width:P0})");

            Vector2 mid = c.Compose(1, 9, "What The Lawsuits Actually Say", 1f, out bool s2, out bool t2, out string shown2);
            Debug.Log($"[Chapter] self-test \"{shown2}\": {c.LabelText.fontSize:F1} px (shrunk {s2}, truncated {t2})");
            log.IsTrue(!t2, "a 30-character label fits without truncating");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Mugs's emotion sprites carry ~8 % transparent canvas above his ears;
    // the title must judge him by what's drawn, not by the texture rectangle.
    static void Chapters_MugsIsMeasuredByHisOutline(Log log)
    {
        var tex = new Texture2D(64, 128, TextureFormat.RGBA32, false);
        var px = new Color[64 * 128];
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 64; x++)
            px[y * 64 + x] = (y < 64 && x >= 8 && x < 56) ? Color.white : Color.clear; // drawn: the lower half only
        tex.SetPixels(px);
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, 64, 128), new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.Tight);
        var go = new GameObject("MugsOutlineTest");
        try
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            var corners = new Vector3[4];
            log.IsTrue(ChapterTitleController.SpriteOutlineWorld(sr, corners), "Mugs outline: measured");
            float top = Mathf.Max(corners[1].y, corners[2].y);
            log.Near(1.28f, sr.bounds.max.y, 0.01f, "Mugs outline: the renderer bounds are the whole 128-px rect");
            log.IsTrue(top <= 0.70f && top >= 0.60f, $"Mugs outline: the outline stops at the drawn top, 0.64 units ({top:F3})");
            sr.flipY = true;
            ChapterTitleController.SpriteOutlineWorld(sr, corners);
            log.IsTrue(Mathf.Min(corners[0].y, corners[3].y) <= -0.60f, "Mugs outline: flips are honoured");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(sp);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}

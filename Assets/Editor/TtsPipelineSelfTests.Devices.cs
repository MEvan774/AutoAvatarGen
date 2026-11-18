using System;
using System.Collections.Generic;
using MugsTech.Stage;
using MugsTech.Style;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The evidence-first stage, steps 3–6, 8 and 10: media runs, the push-in and
/// ,Scroll; the line-by-line logo run and its strike; the {Name:} label; the
/// Quote's portrait and type-on; {Mark:}; {Pop:}.
///
/// Checks without "Engine" in their name are pure (maths and tag parsing) and
/// run offline; the *_Engine ones build cards and overlays in edit mode and
/// need the editor (menu or batchmode) — no play mode, key or credits.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Stage/Run Device Self-Tests")]
    public static void RunDevices()
    {
        var log = new Log();
        RunDeviceGroup(log);
        log.Report();
    }

    static void RunDeviceGroup(Log log)
    {
        Media_CoverAndPushIn(log);
        Media_ScreenshotsAtFullFrame(log);
        Media_Scroll(log);
        Logos_Grid(log);
        Logos_Timing(log);
        Logos_TagParsing(log);
        Name_TagParsing(log);
        Name_LabelFit(log);
        Quote_PhotoTagParsing(log);
        Quote_TypeOnMaths(log);
        Mark_TagParsing(log);
        Mark_Timing(log);
        Pop_TagParsing(log);
        Pop_Maths(log);
        Scroll_TagParsing(log);

        try
        {
            Media_ScrollKeyword_Engine(log);
            Media_ScreenshotsOnTheDisplay_Engine(log);
            Logos_RunOnTheCard_Engine(log);
            Logos_ControllerRules_Engine(log);
            Name_Label_Engine(log);
            Quote_PortraitAndTypeOn_Engine(log);
            Mark_OnDocumentAndExcerpt_Engine(log);
            Pop_Overlay_Engine(log);
            BigImage_ScrollAndFullWidth_Engine(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"device engine checks threw {e.GetType().Name}: {e.Message}");
        }
    }

    // Collects warnings logged while `body` runs. A wrapper round the log
    // handler rather than Application.logMessageReceived: it needs no engine
    // call, so the tag-parsing checks that expect a warning run offline too.
    sealed class WarningTap : ILogHandler
    {
        public readonly List<string> Warnings = new List<string>();
        readonly ILogHandler inner;
        public WarningTap(ILogHandler inner) { this.inner = inner; }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (logType == LogType.Warning)
            {
                try { Warnings.Add(args != null && args.Length > 0 ? string.Format(format, args) : format); }
                catch (FormatException) { Warnings.Add(format); }
            }
            inner?.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context) => inner?.LogException(exception, context);
    }

    static List<string> WarningsFrom(Action body)
    {
        ILogHandler before = Debug.unityLogger.logHandler;
        var tap = new WarningTap(before);
        Debug.unityLogger.logHandler = tap;
        try { body(); }
        finally { Debug.unityLogger.logHandler = before; }
        return tap.Warnings;
    }

    // =======================================================================
    // Step 3/4 — full-frame media: cover, push-in, scroll (pure)
    // =======================================================================

    static void Media_CoverAndPushIn(Log log)
    {
        const float frame = 16f / 9f;
        Rect same = MediaMotionMath.CoverUv(frame, frame);
        log.IsTrue(Mathf.Approximately(same.width, 1f) && Mathf.Approximately(same.height, 1f), "cover: a 16:9 image fills the frame uncropped");

        Rect tall = MediaMotionMath.CoverUv(1080f / 1439f, frame);
        log.IsTrue(Mathf.Approximately(tall.width, 1f) && tall.height < 0.5f && Mathf.Abs(tall.center.y - 0.5f) < 1e-4f,
            "cover: a portrait image keeps its width and is cropped top and bottom, centred");
        Rect wide = MediaMotionMath.CoverUv(3f, frame);
        log.IsTrue(Mathf.Approximately(wide.height, 1f) && wide.width < 0.7f && Mathf.Abs(wide.center.x - 0.5f) < 1e-4f,
            "cover: a panorama keeps its height and is cropped at the sides");
        log.Near(frame, tall.width / tall.height * (1080f / 1439f), 1e-3f, "cover: the window has the frame's shape (never stretched)");

        // The push-in: timed = eased over the image's time on screen; held = a slow fixed rate, capped.
        log.Near(1f, MediaMotionMath.PushScaleAt(0f, 4f, MediaMotionMath.PushInMax), 1e-5f, "push-in: starts at 1.00");
        log.Near(1.08f, MediaMotionMath.PushScaleAt(4f, 4f, MediaMotionMath.PushInMax), 1e-5f, "push-in: a timed image ends at 1.08 exactly as it leaves");
        log.Near(1.04f, MediaMotionMath.PushScaleAt(2f, 4f, MediaMotionMath.PushInMax), 1e-4f, "push-in: eased in and out (halfway at half time)");
        log.Near(1.012f, MediaMotionMath.PushScaleAt(1f, 0f, MediaMotionMath.PushInMax), 1e-5f, "push-in: a held image grows 1.2 % a second");
        log.Near(1.08f, MediaMotionMath.PushScaleAt(60f, 0f, MediaMotionMath.PushInMax), 1e-5f, "push-in: …and stops at its maximum");

        foreach (Rect cover in new[] { same, tall, wide })
            foreach (Vector2 focus in new[] { MediaMotionMath.CentreFocus, MediaMotionMath.ArticleFocus, new Vector2(0f, 0f), new Vector2(1f, 1f) })
            {
                Rect uv = MediaMotionMath.PushUv(cover, 1.08f, focus);
                log.IsTrue(uv.xMin >= cover.xMin - 1e-5f && uv.xMax <= cover.xMax + 1e-5f &&
                           uv.yMin >= cover.yMin - 1e-5f && uv.yMax <= cover.yMax + 1e-5f,
                    $"push-in: the window never leaves the image's cover crop (focus {focus}) — the image can't show its edge");
                log.Near(cover.width / 1.08f, uv.width, 1e-5f, "push-in: the window shrinks by the scale");
            }
        log.IsTrue(MediaMotionMath.FocusFor("article_macrumors_apple") == MediaMotionMath.ArticleFocus &&
                   MediaMotionMath.FocusFor("jensen-huang") == MediaMotionMath.CentreFocus,
            "push-in: an article_ capture is aimed at its headline third, anything else at its centre");
        log.Near(0.6f, MediaMotionMath.MinRunImageSeconds, 1e-5f, "runs: an image replaced within 0.6 s is flagged as too short to read");
    }

    // =======================================================================
    // Screenshots at full frame: an article keeps its top, an X post is shown
    // whole or scrolls (the creator's picks from mock-ups, 2026-11-18)
    // =======================================================================

    // A see-through image of w × h with an opaque card at (x, y from the TOP), cw × ch.
    static Color32[] CutoutPixels(int w, int h, int x, int y, int cw, int ch)
    {
        var px = new Color32[w * h];
        var card = new Color32(255, 255, 255, 255);
        for (int row = y; row < y + ch; row++)
        {
            int r = (h - 1 - row) * w;   // Unity's rows count from the bottom
            for (int col = x; col < x + cw; col++) px[r + col] = card;
        }
        return px;
    }

    static void Media_ScreenshotsAtFullFrame(Log log)
    {
        const float W = 1920f, H = 1080f, frame = W / H;

        // What an image is: its name says it.
        log.AreEqual(MediaMotionMath.ShotKind.Article, MediaMotionMath.KindFor("article_macrumors_crackgate"), "shots: article_… is an article capture");
        log.AreEqual(MediaMotionMath.ShotKind.Article, MediaMotionMath.KindFor("Article_AP_freezers"), "shots: …whatever its case");
        log.AreEqual(MediaMotionMath.ShotKind.Post, MediaMotionMath.KindFor("x_post_markgurman"), "shots: x_post_… is an X post");
        log.AreEqual(MediaMotionMath.ShotKind.Post, MediaMotionMath.KindFor("gurman_reply", "TwitterPosts", "TwitterPosts"), "shots: so is anything found in the posts folder");
        log.AreEqual(MediaMotionMath.ShotKind.Photo, MediaMotionMath.KindFor("AppleBuilding", "Images", "TwitterPosts"), "shots: a photo is a photo");
        log.AreEqual(MediaMotionMath.ShotKind.Photo, MediaMotionMath.KindFor("my_article_notes"), "shots: the prefix, not the word anywhere in the name");
        log.AreEqual(MediaMotionMath.ShotKind.Photo, MediaMotionMath.KindFor(null), "shots: no name is a photo");

        // An article: fills the frame, all of the crop off the bottom.
        foreach (float aspect in new[] { 4f / 3f, 1440f / 1080f, 1f, 0.6f })
        {
            Rect centred = MediaMotionMath.CoverUv(aspect, frame), top = MediaMotionMath.CoverUvKeepTop(aspect, frame);
            log.Near(1f, top.yMax, 1e-6f, $"shots: an article of aspect {aspect:F2} keeps its top edge");
            log.IsTrue(Mathf.Approximately(top.width, centred.width) && Mathf.Approximately(top.height, centred.height) && Mathf.Approximately(top.x, centred.x),
                $"shots: …it fills the frame exactly as before, only the window is moved up (aspect {aspect:F2})");
            log.IsTrue(centred.yMax < 1f - 1e-3f, $"shots: (the centred crop cut {(1f - centred.yMax) * 100f:F1} % off the top — the masthead)");
            // The push-in never lets go of the top edge, at any scale on the way.
            for (float scale = 1f; scale <= MediaMotionMath.ArticlePushInMax + 1e-4f; scale += 0.005f)
            {
                Rect uv = MediaMotionMath.PushUv(top, scale, MediaMotionMath.ArticleFrameFocus);
                if (Mathf.Abs(uv.yMax - 1f) > 1e-5f || Mathf.Abs(uv.center.x - 0.5f) > 1e-5f)
                { log.IsTrue(false, $"shots: pushed in to {scale:F3} the article lost its top or drifted sideways (aspect {aspect:F2})"); break; }
            }
        }
        Rect pushed = MediaMotionMath.PushUv(MediaMotionMath.CoverUvKeepTop(4f / 3f, frame), MediaMotionMath.ArticlePushInMax, MediaMotionMath.ArticleFrameFocus);
        log.Near(1f, pushed.yMax, 1e-6f, "shots: fully pushed in, a 4:3 article still shows its very top");
        log.IsTrue(pushed.xMin < 0.011f && pushed.xMax > 0.989f, $"shots: …and loses about 1 % at each side ({pushed.xMin * 100f:F1} %) — a masthead 1.7 % from the page's edge stays whole");
        log.IsTrue(MediaMotionMath.ArticlePushInMax < MediaMotionMath.PushInMax, "shots: an article pushes in less than a photo");
        // …and then it scrolls down slowly, showing the rest (the creator's ask after the first take).
        float travel = MediaMotionMath.PageTravelFrames(MediaMotionMath.VisibleShare(W, H, 1440f, 1080f));
        log.Near(1f / 3f, travel, 1e-4f, "shots: a 4:3 article has a third of a frame hidden below it (360 px)");
        log.Near(0f, MediaMotionMath.PageTravelFrames(MediaMotionMath.VisibleShare(W, H, 1920f, 1080f)), 1e-6f, "shots: a 16:9 capture has nothing to scroll to");
        log.Near(0f, MediaMotionMath.PageTravelFrames(MediaMotionMath.VisibleShare(W, H, 2400f, 1000f)), 1e-6f, "shots: …nor has a wider one");
        foreach (float d in new[] { travel, 2.5f, 0.02f })
        {
            float total = MediaMotionMath.ArticleScrollSeconds(d);
            log.Near(0f, MediaMotionMath.ArticleScrollProgress(MediaMotionMath.ArticleTopHoldSeconds, d), 1e-6f,
                $"shots: an article rests on its top for {MediaMotionMath.ArticleTopHoldSeconds:F1} s (travel {d * H:F0} px)");
            log.IsTrue(MediaMotionMath.ArticleScrollProgress(MediaMotionMath.ArticleTopHoldSeconds + 0.1f, d) * d * H < 1.5f,
                $"shots: …and starts down gently (under 1.5 px in the first tenth of a second; travel {d * H:F0} px)");
            log.Near(1f, MediaMotionMath.ArticleScrollProgress(total, d), 1e-4f, $"shots: …it is at its bottom after {total:F1} s");
            log.Near(1f, MediaMotionMath.ArticleScrollProgress(total + 30f, d), 1e-6f, "shots: …and rests there");
            bool down = true, calm = true;
            float before = 0f, fastest = 0f;
            for (float t = 0f; t <= total + 0.5f; t += 1f / 120f)
            {
                float now = MediaMotionMath.ArticleScrollProgress(t, d);
                if (now < before - 1e-6f || now > 1f + 1e-6f) down = false;
                float speed = (now - before) * d * 120f;                 // frame heights a second
                fastest = Mathf.Max(fastest, speed);
                if (speed > MediaMotionMath.HeldScrollPerSecond * 1.01f) calm = false;
                before = now;
            }
            log.IsTrue(down, $"shots: it only ever moves down the page (travel {d * H:F0} px)");
            log.IsTrue(calm, $"shots: …never faster than the page's calm rate — {fastest * H:F0} px a second at most (travel {d * H:F0} px)");
        }
        log.Near(MediaMotionMath.ArticleTopHoldSeconds + MediaMotionMath.ArticleScrollRampSeconds + travel / MediaMotionMath.HeldScrollPerSecond,
                 MediaMotionMath.ArticleScrollSeconds(travel), 1e-3f, "shots: a 4:3 article takes about 7.6 s to show all of itself");
        log.Near(0f, MediaMotionMath.ArticleScrollProgress(100f, 0f), 1e-6f, "shots: with nothing to scroll to it stays put");
        log.IsTrue(MediaMotionMath.ArticleScrollSeconds(0.02f) < MediaMotionMath.ArticleTopHoldSeconds + 2f * MediaMotionMath.ArticleScrollRampSeconds,
            "shots: a page with a few pixels to show eases there and back without reaching full speed");

        Rect panorama = MediaMotionMath.CoverUvKeepTop(3f, frame);
        log.IsTrue(Mathf.Approximately(panorama.height, 1f) && Mathf.Abs(panorama.center.x - 0.5f) < 1e-5f, "shots: a capture wider than the frame is cropped at the sides, as any image is");
        log.IsTrue(MediaMotionMath.FocusFor("article_x") == MediaMotionMath.ArticleFocus, "shots: the side slot and {BigImage} still aim an article at its headline third, as before");

        // The post's own bounds inside its see-through image.
        Color32[] px = CutoutPixels(242, 120, 38, 19, 165, 82);
        RectInt bounds = MediaMotionMath.OpaqueBounds(px, 242, 120, 1);
        log.IsTrue(bounds.x == 38 && bounds.y == 19 && bounds.width == 165 && bounds.height == 82, $"shots: the bounds are the opaque card, x and y from the top-left ({bounds})");
        RectInt coarse = MediaMotionMath.OpaqueBounds(px, 242, 120, 2);
        log.IsTrue(coarse.x <= 38 && coarse.y <= 19 && coarse.xMax >= 38 + 165 && coarse.yMax >= 19 + 82 &&
                   coarse.width <= 165 + 2 && coarse.height <= 82 + 2, $"shots: read every 2nd pixel they are the same within a pixel, never smaller ({coarse})");
        px[(120 - 1 - 5) * 242 + 7] = new Color32(0, 0, 0, MediaMotionMath.PostAlphaFloor);
        log.IsTrue(MediaMotionMath.OpaqueBounds(px, 242, 120, 1).x == 38, "shots: a nearly see-through pixel (a shadow's edge) is not part of the post");
        var opaque = new Color32[40 * 30];
        for (int i = 0; i < opaque.Length; i++) opaque[i] = new Color32(9, 9, 9, 255);
        RectInt all = MediaMotionMath.OpaqueBounds(opaque, 40, 30);
        log.IsTrue(all.x == 0 && all.y == 0 && all.width == 40 && all.height == 30, "shots: an image with nothing see-through is its whole rectangle");
        RectInt none = MediaMotionMath.OpaqueBounds(new Color32[40 * 30], 40, 30);
        log.IsTrue(none.width == 40 && none.height == 30, "shots: so is an empty one (nothing to trim to)");
        log.IsTrue(MediaMotionMath.OpaqueBounds(null, 40, 30).width == 40, "shots: …and one that can't be read");

        // The creator's three real posts: 2418 wide, the card 1650 wide, 384 px see-through at the sides, 192 above and below.
        var posts = new (string name, int h, bool scrolls)[] { ("wide", 1203, false), ("medium", 1701, false), ("tall", 3099, true) };
        foreach (var (name, h, scrolls) in posts)
        {
            var card = new RectInt(384, 192, 1650, h - 384);
            MediaMotionMath.PostLayout post = MediaMotionMath.LayoutPost(W, H, 2418, h, card);
            log.IsTrue(post.Scrolls == scrolls, $"shots: the {name} post {(scrolls ? "scrolls" : "is shown whole")}");
            log.Near(card.x / 2418f, post.Uv.xMin, 1e-5f, $"shots: the {name} post's see-through sides are not shown (left)");
            log.Near(card.xMax / 2418f, post.Uv.xMax, 1e-5f, $"shots: …(right)");
            log.IsTrue(post.Uv.yMin >= -1e-5f && post.Uv.yMax <= 1f + 1e-5f, $"shots: the {name} post's window is inside its image");
            if (!scrolls)
            {
                log.IsTrue(post.SlotSize.x <= MediaMotionMath.PostMaxWidth * W + 0.5f && post.SlotSize.y <= MediaMotionMath.PostMaxHeight * H + 0.5f,
                    $"shots: the {name} post fits inside 88 % × 86 % of the frame ({post.SlotSize.x:F0} × {post.SlotSize.y:F0})");
                log.IsTrue(Mathf.Abs(post.SlotSize.x - MediaMotionMath.PostMaxWidth * W) < 0.5f || Mathf.Abs(post.SlotSize.y - MediaMotionMath.PostMaxHeight * H) < 0.5f,
                    $"shots: …as large as that allows");
                log.Near((float)card.width / card.height, post.SlotSize.x / post.SlotSize.y, 1e-3f, $"shots: the {name} post keeps its shape");
                log.Near(1f - card.yMax / (float)h, post.Uv.yMin, 1e-5f, $"shots: the {name} post is shown from its own bottom edge…");
                log.Near(1f - card.y / (float)h, post.Uv.yMax, 1e-5f, "shots: …to its own top edge: all of it, nothing else");
                log.IsTrue(post.SlotSize.x >= MediaMotionMath.PostReadableWidth * W * MediaMotionMath.PostScrollBelow, $"shots: …and readable ({post.SlotSize.x / W:P0} of the frame's width)");
                log.IsTrue(post.UvTravel == 0f && post.TravelFrames == 0f, $"shots: the {name} post doesn't move");
            }
            else
            {
                log.Near(MediaMotionMath.PostReadableWidth * W, post.SlotSize.x, 0.5f, "shots: a tall post is half the frame wide — readable");
                log.Near(H, post.SlotSize.y, 1e-3f, "shots: …in a column the height of the frame (it runs off the top and bottom, it is never cut in mid-air)");
                log.Near(H / post.Scale / h, post.Uv.height, 1e-5f, "shots: …never stretched: the window is one frame of it");
                // Start: the post's top, a margin below the frame's top. End: its bottom, a margin above the frame's bottom.
                float topGap = (card.y - (1f - post.Uv.yMax) * h) * post.Scale;
                Rect end = MediaMotionMath.PostScrollUv(post, 1f);
                float bottomGap = ((1f - end.yMin) * h - card.yMax) * post.Scale;
                log.Near(MediaMotionMath.PostMargin * H, topGap, 1f, $"shots: it starts with its top {topGap:F0} px under the frame's top");
                log.Near(MediaMotionMath.PostMargin * H, bottomGap, 1f, $"shots: …and ends with its bottom {bottomGap:F0} px above the frame's bottom");
                log.Near(post.UvTravel * h * post.Scale / H, post.TravelFrames, 1e-4f, "shots: the travel in frame heights is the travel on screen");
                log.IsTrue(post.TravelFrames > 0.3f && post.TravelFrames < 1.5f, $"shots: it travels {post.TravelFrames * H:F0} px");
                bool down = true, inside = true;
                float before = float.PositiveInfinity;
                for (int i = 0; i <= 20; i++)
                {
                    Rect uv = MediaMotionMath.PostScrollUv(post, i / 20f);
                    if (uv.y > before + 1e-6f) down = false;
                    before = uv.y;
                    if (uv.yMin < -1e-5f || uv.yMax > 1f + 1e-5f) inside = false;
                }
                log.IsTrue(down && inside, "shots: the window only moves down the post, and never leaves the image");

                float needs = MediaMotionMath.PostSecondsNeeded(post.TravelFrames);
                log.IsTrue(needs > 4f && needs < 6f, $"shots: timed, it needs about {needs:F1} s to be readable (shorter is warned about)");
                log.Near(0f, MediaMotionMath.PostScrollProgress(0f, 6f, post.TravelFrames), 1e-6f, "shots: a timed post starts at its top");
                log.Near(1f, MediaMotionMath.PostScrollProgress(6f, 6f, post.TravelFrames), 1e-6f, "shots: …and reaches its bottom exactly as its time is up");
                log.IsTrue(MediaMotionMath.PostScrollProgress(0.6f, 6f, post.TravelFrames) < 0.03f, "shots: …slow off the mark (2.8 % after a tenth of its time)");
                float held = MediaMotionMath.PostScrollProgress(1f, 0f, post.TravelFrames) * post.TravelFrames * H;
                log.Near(MediaMotionMath.HeldScrollPerSecond * H, held, 0.5f, $"shots: held, it moves {held:F0} px a second — the page's calm rate");
                log.Near(1f, MediaMotionMath.PostScrollProgress(600f, 0f, post.TravelFrames), 1e-6f, "shots: …and rests at its bottom");
            }
        }

        // The rule between the two: whole unless that is clearly smaller than readable.
        var justFits = MediaMotionMath.LayoutPost(W, H, 2418, 2000, new RectInt(384, 192, 1650, 1616));
        log.IsTrue(!justFits.Scrolls && justFits.SlotSize.x >= 0.45f * W, $"shots: a post that is whole at {justFits.SlotSize.x / W:P0} of the width stays whole (no scroll for a few pixels)");
        var small = MediaMotionMath.LayoutPost(W, H, 700, 420, new RectInt(50, 40, 600, 340));
        log.Near(MediaMotionMath.PostMaxUpscale, small.Scale, 1e-5f, "shots: a small capture is blown up by a quarter at most");
        log.IsTrue(!small.Scrolls && Mathf.Abs(small.SlotSize.x - 750f) < 0.5f, "shots: …so it sits smaller on the frame rather than blurred");
        var noMargin = MediaMotionMath.LayoutPost(W, H, 1650, 2715, new RectInt(0, 0, 1650, 2715));
        log.IsTrue(noMargin.Scrolls && Mathf.Abs(noMargin.Uv.yMax - 1f) < 1e-5f && MediaMotionMath.PostScrollUv(noMargin, 1f).yMin > -1e-5f,
            "shots: a tall post with nothing see-through around it scrolls from its very top to its very bottom");
        var unknown = MediaMotionMath.LayoutPost(W, H, 2418, 1203, new RectInt(0, 0, 0, 0));
        log.IsTrue(!unknown.Scrolls && unknown.Uv.width == 1f && unknown.Uv.height == 1f, "shots: with no bounds measured the whole image is the post");
        log.Near(1f, MediaMotionMath.PushScaleAt(0f, 5f, MediaMotionMath.PostPushInMax), 1e-6f, "shots: a whole post starts at its own size");
        log.Near(1.03f, MediaMotionMath.PushScaleAt(5f, 5f, MediaMotionMath.PostPushInMax), 1e-5f, "shots: …and has grown 3 % as it leaves (the card, not a crop)");
    }

    static void Media_Scroll(Log log)
    {
        const float w = 1920f, h = 1080f;

        // Exactly as tall as the slot at the slot's width: nothing to scroll.
        float exact = MediaMotionMath.VisibleShare(w, h, 1920f, 1080f);
        log.IsTrue(!MediaMotionMath.CanScroll(exact), "scroll: an image exactly as tall as its slot doesn't scroll");
        log.Near(0f, MediaMotionMath.ScrollProgress(3f, 6f, exact), 1e-6f, "scroll: …and makes no progress");

        foreach (float times in new[] { 1.2f, 5f })
        {
            float share = MediaMotionMath.VisibleShare(w, h, 1920f, 1080f * times);
            log.Near(1f / times, share, 1e-4f, $"scroll: a page {times}× the slot's height shows 1/{times} of itself");
            log.IsTrue(MediaMotionMath.CanScroll(share), $"scroll: …and scrolls ({times}×)");

            // Timed: the whole distance in exactly its duration, eased.
            log.Near(0f, MediaMotionMath.ScrollProgress(0f, 8f, share), 1e-6f, $"scroll {times}×: a timed page starts at its top");
            log.Near(1f, MediaMotionMath.ScrollProgress(8f, 8f, share), 1e-6f, $"scroll {times}×: …and reaches its bottom exactly as it leaves");
            log.Near(0.5f, MediaMotionMath.ScrollProgress(4f, 8f, share), 1e-4f, $"scroll {times}×: …eased in and out");
            log.IsTrue(MediaMotionMath.ScrollProgress(1f, 8f, share) < 0.125f, $"scroll {times}×: …slow off the mark");

            // Held: 6 % of the slot's height a second, then resting at the bottom.
            float travelSlots = times - 1f;                         // slot heights to cover
            float seconds = travelSlots / MediaMotionMath.HeldScrollPerSecond;
            log.Near(0.5f, MediaMotionMath.ScrollProgress(seconds * 0.5f, 0f, share), 1e-3f, $"scroll {times}×: a held page moves at the fixed rate");
            log.Near(1f, MediaMotionMath.ScrollProgress(seconds, 0f, share), 1e-3f, $"scroll {times}×: …reaching the bottom after {seconds:F1} s");
            log.Near(1f, MediaMotionMath.ScrollProgress(seconds + 30f, 0f, share), 1e-6f, $"scroll {times}×: …and rests there until its End");

            Rect top = MediaMotionMath.ScrollUv(share, 0f), bottom = MediaMotionMath.ScrollUv(share, 1f);
            log.Near(1f, top.yMax, 1e-5f, $"scroll {times}×: it opens on the page's top edge");
            log.Near(0f, bottom.yMin, 1e-5f, $"scroll {times}×: …and ends on its bottom edge");
            log.IsTrue(Mathf.Approximately(top.width, 1f) && Mathf.Approximately(top.xMin, 0f), $"scroll {times}×: fitted to the slot's width");
            for (int i = 0; i <= 10; i++)
            {
                Rect uv = MediaMotionMath.ScrollUv(share, i / 10f);
                log.IsTrue(uv.yMin >= -1e-5f && uv.yMax <= 1f + 1e-5f, $"scroll {times}×: never past the page's own edge (at {i * 10} %)");
            }
        }

        // The side slot (1000 × 800) scrolls a page too.
        float side = MediaMotionMath.VisibleShare(1000f, 800f, 1080f, 1439f);
        log.IsTrue(MediaMotionMath.CanScroll(side), "scroll: the library's tallest image scrolls in the side slot too");
    }

    // =======================================================================
    // Step 3 — the logo run (pure)
    // =======================================================================

    static void Logos_Grid(Log log)
    {
        var frame = new Vector2(1920f, 1080f);

        log.AreEqual(8, BigMediaGrid.MaxLogos, "logos: a run holds up to eight");
        log.AreEqual(1, BigMediaGrid.SizedFor(0, 0), "logos: sized for at least one");
        log.AreEqual(5, BigMediaGrid.SizedFor(2, 5), "logos: sized for what is coming");
        log.AreEqual(8, BigMediaGrid.SizedFor(3, 12), "logos: …never for more than eight");

        // One logo: dead centre.
        log.IsTrue(BigMediaGrid.SlotCentre(0, 1, 1, frame).magnitude < 1e-3f, "logos: one logo sits at the centre");

        foreach (int n in new[] { 1, 3, 4, 5, 8 })
        {
            Vector2 size = BigMediaGrid.SlotSize(n, frame);
            float pitch = BigMediaGrid.Pitch(n, frame);
            for (int shown = 1; shown <= n; shown++)
            {
                float sumX0 = 0f, sumX1 = 0f; int c0 = 0, c1 = 0;
                for (int i = 0; i < shown; i++)
                {
                    Vector2 c = BigMediaGrid.SlotCentre(i, shown, n, frame);
                    // Inside the frame.
                    log.IsTrue(Mathf.Abs(c.x) + size.x * 0.5f <= frame.x * 0.5f + 0.5f && Mathf.Abs(c.y) + size.y * 0.5f <= frame.y * 0.5f + 0.5f,
                        $"logos: slot {i + 1} of {shown} (run of {n}) is inside the frame");
                    if (BigMediaGrid.RowOf(i) == 0) { sumX0 += c.x; c0++; } else { sumX1 += c.x; c1++; }
                    if (i > 0 && BigMediaGrid.RowOf(i) == BigMediaGrid.RowOf(i - 1))
                        log.Near(pitch, c.x - BigMediaGrid.SlotCentre(i - 1, shown, n, frame).x, 0.01f,
                            $"logos: neighbours in a row are one slot apart ({shown} of {n})");
                }
                log.Near(0f, c0 > 0 ? sumX0 / c0 : 0f, 0.01f, $"logos: the first row is centred ({shown} of {n})");
                if (c1 > 0) log.Near(0f, sumX1 / c1, 0.01f, $"logos: the second row is centred ({shown} of {n})");
                log.AreEqual(Mathf.Min(shown, 4), c0, $"logos: one row up to four ({shown} of {n})");
            }
        }

        // The fifth logo starts a second row and the first row moves up to make room.
        float y4 = BigMediaGrid.SlotCentre(0, 4, 8, frame).y;
        float y5top = BigMediaGrid.SlotCentre(0, 5, 8, frame).y, y5bottom = BigMediaGrid.SlotCentre(4, 5, 8, frame).y;
        log.Near(0f, y4, 1e-3f, "logos: four logos sit on one row at mid height");
        log.IsTrue(y5top > 0f && y5bottom < 0f && Mathf.Abs(y5top + y5bottom) < 1e-3f,
            "logos: with the fifth the rows sit one above the other, the pair centred");
        Vector2 size8 = BigMediaGrid.SlotSize(8, frame);
        log.IsTrue(y5top - y5bottom >= size8.y, "logos: the two rows don't overlap");
        log.IsTrue(BigMediaGrid.SlotSize(5, frame) == BigMediaGrid.SlotSize(8, frame), "logos: two-row slots are one size, whatever the count");

        log.IsTrue(BigMediaGrid.SameName("brave", " Brave ") && !BigMediaGrid.SameName("Brave", "Brave2"),
            "logos: a strike finds its logo by name, ignoring case and outer spaces");
    }

    static void Logos_Timing(Log log)
    {
        log.Near(0.7f, BigMediaGrid.PopScaleAt(0f), 1e-5f, "logos: a logo pops from 0.7×");
        log.Near(1f, BigMediaGrid.PopScaleAt(0.2f), 1e-5f, "logos: …to full size in 0.2 s");
        float peak = 0f;
        for (int i = 0; i <= 40; i++) peak = Mathf.Max(peak, BigMediaGrid.PopScaleAt(i * 0.005f));
        log.IsTrue(peak > 1.005f && peak < 1.08f, $"logos: …with a small overshoot (peak {peak:F3})");

        log.Near(0f, BigMediaGrid.RecentreAt(0f), 1e-5f, "logos: the grid re-centres from where it was");
        log.Near(1f, BigMediaGrid.RecentreAt(BigMediaGrid.RecentreSeconds), 1e-5f, "logos: …eased, in 0.3 s");

        log.Near(0f, BigMediaGrid.StrikeStrokeAt(0f, 0), 1e-5f, "strike: the first stroke starts at once");
        log.Near(1f, BigMediaGrid.StrikeStrokeAt(0.1f, 0), 1e-5f, "strike: …and is drawn in 0.1 s");
        log.Near(0f, BigMediaGrid.StrikeStrokeAt(0.1f, 1), 1e-5f, "strike: the second stroke starts as the first ends");
        log.Near(1f, BigMediaGrid.StrikeStrokeAt(0.2f, 1), 1e-5f, "strike: the cross is complete in 0.2 s");
        log.Near(1f, BigMediaGrid.StruckAlphaAt(0f), 1e-5f, "strike: the logo is whole as the cross starts");
        log.Near(0.5f, BigMediaGrid.StruckAlphaAt(0.2f), 1e-5f, "strike: …and at half strength once it is drawn");
    }

    static void Logos_TagParsing(Log log)
    {
        string script =
            "{BigMedia:Google,T=1.000,D=0}one {BigMedia:Brave,T=2.000,D=0}two " +
            "{BigMedia:Strike,Brave,T=3.000}three {BigMedia:End,T=4.000,D=0}four " +
            "{BigMedia:Strike,T=5.000,D=4}five {BigMedia:Google+Brave+X,T=6.000,D=4}six " +
            "{BigMedia:Strike,3M,T=7.000}seven";
        List<ContentCardEvent> events = null;
        string clean = null;
        WarningsFrom(() => { var r = ContentZoneTagParser.ParseContentTags(script, 30f); clean = r.Item1; events = r.Item2; });
        log.IsTrue(!clean.Contains("{"), $"logo tags: every form is stripped (\"{clean}\")");

        var media = events.FindAll(e => e.cardType == ContentCardType.BigMedia);
        log.AreEqual(7, media.Count, "logo tags: seven BigMedia events");
        ContentCardEvent At(float t) => media.Find(e => Mathf.Abs(e.triggerTime - t) < 1e-3f);

        log.IsTrue(At(1f) != null && At(1f).duration <= 0f && !At(1f).dismissesCard && !At(1f).strikesLogo && At(1f).primaryText == "Google",
            "logo tags: {BigMedia:Google} with no duration is a logo of a run");
        log.IsTrue(At(3f) != null && At(3f).strikesLogo && At(3f).primaryText == "Brave", "logo tags: {BigMedia:Strike,Brave} is a strike of Brave");
        log.IsTrue(At(4f) != null && At(4f).dismissesCard, "logo tags: {BigMedia:End} closes the run");
        log.IsTrue(At(5f) != null && !At(5f).strikesLogo && Mathf.Approximately(At(5f).duration, 4f) && At(5f).primaryText == "Strike",
            "logo tags: {BigMedia:Strike,4} is still a timed logo called Strike");
        log.IsTrue(At(6f) != null && Mathf.Approximately(At(6f).duration, 4f) && At(6f).primaryText == "Google+Brave+X",
            "logo tags: the timed form is what it was");
        log.IsTrue(At(7f) != null && At(7f).strikesLogo && At(7f).primaryText == "3M", "logo tags: a name that starts with a digit can be struck");

        // Unstamped (legacy) forms parse the same way.
        var legacy = ContentZoneTagParser.ParseContentTags("{BigMedia:Google} a {BigMedia:Strike,Google} b {BigMedia:End} c {BigMedia:X,3} d", 20f).Item2;
        log.AreEqual(4, legacy.Count, "logo tags: the authored forms parse without a T=");
        log.AreEqual(1, legacy.FindAll(e => e.strikesLogo).Count, "logo tags: …one of them a strike");

        // The run read ahead: three logos, the strike and the End not counted.
        var run = ContentZoneController.AnticipateRun(events, At(1f));
        log.AreEqual(2, run.Count, "logo tags: the run is sized from its own logos (two before its End)");
    }

    // =======================================================================
    // Step 4 — the ,Scroll keyword on tags (pure)
    // =======================================================================

    static void Scroll_TagParsing(Log log)
    {
        var events = ContentZoneTagParser.ParseContentTags(
            "{BigImage:page,T=1.000,D=8,Scroll}a {BigImage:page,T=12.000,D=5}b {BigImage:page,8,Scroll}c", 40f).Item2;
        var images = events.FindAll(e => e.cardType == ContentCardType.BigImage);
        log.AreEqual(3, images.Count, "scroll tags: {BigImage:} parses with and without ,Scroll");
        log.IsTrue(images.Exists(e => e.scroll && Mathf.Approximately(e.duration, 8f) && Mathf.Abs(e.triggerTime - 1f) < 1e-3f),
            "scroll tags: {BigImage:name,8,Scroll} carries the keyword");
        log.IsTrue(images.Exists(e => !e.scroll && Mathf.Approximately(e.duration, 5f)), "scroll tags: a plain {BigImage:} doesn't");
    }

    // =======================================================================
    // Step 5 — {Name:…} (pure)
    // =======================================================================

    static void Name_TagParsing(Log log)
    {
        string script =
            "Before {Name:\"Sarah Mitchell\",\"VP of Communications, MegaCorp\",T=1.500,D=0}held " +
            "{Name:End,T=6.000}after {Name:\"Jensen Huang\",\"Nvidia CEO\",T=8.000,D=4}timed " +
            "{Name:\"Authored\",\"as written\",Start}x {Name:End}y {Name:\"only one field\",5}z {Name:\"\",\"no name\",3}w";
        var r = StageTags.ParseNames(script, 20f);

        log.IsTrue(!r.Script.Contains("{Name"), $"name tags: every {{Name:…}} is removed from the script (\"{r.Script}\")");
        log.AreEqual(5, r.Markers.Count, "name tags: three labels and two Ends parse");
        log.AreEqual(2, r.Warnings.Count, "name tags: the two malformed ones are warned about and skipped");
        log.IsTrue(r.Warnings.Count == 2 && r.Warnings[0].Contains("only one field"), "name tags: …the warning quotes the tag");

        StageTags.NameMarker held = r.Markers[0];
        log.IsTrue(held.Name == "Sarah Mitchell" && held.Role == "VP of Communications, MegaCorp" && held.Duration == 0f && !held.Ends,
            "name tags: a held label keeps its name and its role (comma and all)");
        log.Near(1.5f, held.Time, 1e-4f, "name tags: …at its T=");
        log.IsTrue(r.Markers[1].Ends && Mathf.Abs(r.Markers[1].Time - 6f) < 1e-4f, "name tags: {Name:End} closes it at its own T=");
        log.IsTrue(r.Markers[2].Name == "Jensen Huang" && Mathf.Approximately(r.Markers[2].Duration, 4f), "name tags: a timed label carries its seconds");
        log.IsTrue(r.Markers[3].Name == "Authored" && r.Markers[3].Duration == 0f && r.Markers[3].Time > r.Markers[2].Time,
            "name tags: the authored form (Start, no T=) parses, timed from its place in the text");
        log.IsTrue(r.Markers[4].Ends, "name tags: …and so does its End");

        log.AreEqual(0, StageTags.ParseNames("No tags here.", 5f).Markers.Count, "name tags: a script without one is left alone");

        // Away from Mugs: the lower-right corner only while he is on the left.
        log.IsTrue(NameLabelMath.OnRight(CharacterPosition.Left) && NameLabelMath.OnRight(CharacterPosition.PeekLeft),
            "name: with Mugs (or his peek) on the left the label takes the lower right");
        foreach (CharacterPosition p in new[] { CharacterPosition.Right, CharacterPosition.PeekRight, CharacterPosition.Center, CharacterPosition.Off })
            log.IsTrue(!NameLabelMath.OnRight(p), $"name: with Mugs at {p} the label takes the lower left");
        log.IsTrue(NameLabelMath.SortOrder > StageMath.PeekSortingOrder && NameLabelMath.SortOrder < 31000,
            "name: it sits above cards, media and a peeking Mugs, below the full-screen cards");
    }

    static void Name_LabelFit(Log log)
    {
        const float fw = 1920f, fh = 1080f;
        float max = NameLabelMath.MaxWidthOfFrame * fw;

        // Width per unit of font size: about 0.42 a character in Anton, 0.5 in Inter Bold.
        NameLabelMath.Fit two = NameLabelMath.FitLabel(0.42f * 12, 0.5f * 10, fw, fh);          // "Jensen Huang" / "Nvidia CEO"
        log.Near(NameLabelMath.NamePx, two.NameSize, 0.01f, "name: a 2-word name is set at 64 px");
        log.Near(NameLabelMath.RolePx, two.RoleSize, 0.01f, "name: …its role at 38 px");
        log.IsTrue(two.BarWidth < max * 0.6f, "name: the bar is only as wide as its text");

        NameLabelMath.Fit five = NameLabelMath.FitLabel(0.42f * 36, 0.5f * 20, fw, fh);         // a 5-word name
        log.IsTrue(five.BarWidth <= max + 0.5f, "name: a 5-word name still fits 45 % of the frame");
        log.IsTrue(five.NameSize < NameLabelMath.NamePx && five.NameSize >= NameLabelMath.NameMinPx - 0.01f && !five.NameBelowFloor,
            $"name: …shrunk, not under its 44 px floor ({five.NameSize:F0} px)");

        NameLabelMath.Fit longRole = NameLabelMath.FitLabel(0.42f * 12, 0.5f * 48, fw, fh);     // a long role
        log.IsTrue(longRole.RoleSize < NameLabelMath.RolePx && longRole.RoleSize >= NameLabelMath.RoleMinPx - 0.01f,
            $"name: a long role auto-fits ({longRole.RoleSize:F0} px)");
        log.IsTrue(!longRole.RoleWraps, "name: …on one line while it can");
        NameLabelMath.Fit veryLong = NameLabelMath.FitLabel(0.42f * 12, 0.5f * 90, fw, fh);
        log.IsTrue(veryLong.RoleWraps && Mathf.Approximately(veryLong.RoleSize, NameLabelMath.RoleMinPx) && veryLong.BarWidth <= max + 0.5f,
            "name: a role too long for one line at 32 px wraps instead of shrinking further");

        NameLabelMath.Fit huge = NameLabelMath.FitLabel(0.42f * 80, 0f, fw, fh);
        log.IsTrue(huge.NameBelowFloor && huge.NameSize >= 32f - 0.01f, "name: a name too long even at 44 px is flagged, never under 32 px");

        log.Near(0f, NameLabelMath.SlideIn(0f), 1e-5f, "name: slides in from outside the edge");
        log.Near(1f, NameLabelMath.SlideIn(0.3f), 1e-5f, "name: …in 0.3 s");
        log.IsTrue(NameLabelMath.SlideIn(0.15f) > 0.8f, "name: …decelerating");
        log.Near(1f, NameLabelMath.SlideOut(0.25f), 1e-5f, "name: slides back out in 0.25 s");
    }

    // =======================================================================
    // Step 6 — the Quote's photo and type-on (pure)
    // =======================================================================

    static void Quote_PhotoTagParsing(Log log)
    {
        string script =
            "{Quote:\"We believe transparency is at the core.\",\"Sarah Mitchell\",\"VP of Communications, MegaCorp\",T=1.000,D=0,Photo=ceo_mitchell}a " +
            "{Quote:\"q two\",\"n\",\"r\",T=2.000,D=5,Photo=jensen-huang,Right}b " +
            "{Quote:\"q three\",\"n\",\"r\",T=3.000,D=5,Right}c " +
            "{Quote:\"q four\",\"n\",\"r\",Start,Photo=someone}d {Quote:End}e";
        var r = ContentZoneTagParser.ParseContentTags(script, 20f);
        var quotes = r.Item2.FindAll(e => e.cardType == ContentCardType.Quote && !e.dismissesCard);
        log.IsTrue(!r.Item1.Contains("{Quote"), "quote tags: every form is stripped");
        log.AreEqual(4, quotes.Count, "quote tags: four quotes parse");
        ContentCardEvent Q(string text) => quotes.Find(e => e.primaryText.StartsWith(text, StringComparison.Ordinal));

        log.AreEqual("ceo_mitchell", Q("We believe").photoName, "quote tags: ,Photo= names the portrait");
        log.IsTrue(Q("We believe").duration <= 0f && Q("We believe").entryDirectionOverride == null, "quote tags: …on a held quote with no side");
        log.IsTrue(Q("q two").photoName == "jensen-huang" && Q("q two").entryDirectionOverride == EntryDirection.FromRight,
            "quote tags: ,Photo= then ,Right — the side stays last");
        log.IsTrue(Q("q three").photoName == null && Q("q three").entryDirectionOverride == EntryDirection.FromRight,
            "quote tags: a quote without ,Photo= is what it was");
        log.IsTrue(Q("q four").photoName == "someone" && Q("q four").duration <= 0f, "quote tags: the authored form (Start, no T=) parses");
    }

    static void Quote_TypeOnMaths(Log log)
    {
        // An 8-word quote (about 45 characters) and a 40-word one (about 230), over 2 s and over 12 s.
        const int small = 45, large = 230;
        float w2 = TypeOn.Window(10f, 12f), w12 = TypeOn.Window(10f, 22f);
        log.Near(1.7f, w2, 1e-4f, "type-on: a held quote types up to 0.3 s before its End");
        log.Near(11.7f, w12, 1e-4f, "type-on: …whatever its length");

        log.Near(small / 1.7f, TypeOn.Rate(small, w2), 0.01f, "type-on: 8 words over 2 s — spread evenly (26 a second)");
        log.Near(TypeOn.MinCps, TypeOn.Rate(small, w12), 1e-4f, "type-on: 8 words over 12 s — no slower than 12 a second");
        log.Near(TypeOn.MaxCps, TypeOn.Rate(large, w2), 1e-4f, "type-on: 40 words over 2 s — no faster than 30 a second");
        log.Near(large / 11.7f, TypeOn.Rate(large, w12), 0.01f, "type-on: 40 words over 12 s — spread evenly (20 a second)");
        log.Near(TypeOn.TimedCps, TypeOn.Rate(large, float.NaN), 1e-4f, "type-on: a timed quote types at 16 a second");

        foreach (int chars in new[] { small, large })
            foreach (float window in new[] { w2, w12 })
            {
                log.AreEqual(0, TypeOn.VisibleAt(0f, chars, window), $"type-on: nothing of the quote at the first frame ({chars} chars, {window:F1} s)");
                log.AreEqual(chars, TypeOn.VisibleAt(window, chars, window),
                    $"type-on: the last character is on screen 0.3 s before the End ({chars} chars, {window:F1} s)");
                log.IsTrue(TypeOn.DoneAt(chars, window) <= window + 1e-4f, $"type-on: …never later ({chars} chars, {window:F1} s)");
                int before = 0;
                bool monotonic = true;
                for (float t = 0f; t <= window + 0.2f; t += 1f / 30f)
                {
                    int v = TypeOn.VisibleAt(t, chars, window);
                    if (v < before) monotonic = false;
                    before = v;
                }
                log.IsTrue(monotonic && before == chars, $"type-on: characters only ever arrive, in order ({chars} chars, {window:F1} s)");
            }

        // Halfway through an evenly spread quote, about half of it is up.
        int half = TypeOn.VisibleAt(11.7f * 0.5f, large, w12);
        log.IsTrue(Mathf.Abs(half - large / 2) <= 2, $"type-on: evenly spread ({half} of {large} at half time)");
        // The capped case: at 30 a second 230 characters need 7.6 s — the rest lands when the window closes.
        log.IsTrue(TypeOn.VisibleAt(1.69f, large, w2) < large && TypeOn.VisibleAt(1.7f, large, w2) == large,
            "type-on: when the 30-a-second cap can't finish, the rest lands at the window's end");
        log.IsTrue(float.IsNaN(TypeOn.Window(float.NaN, 12f)) && float.IsNaN(TypeOn.Window(10f, float.NaN)),
            "type-on: an unknown End means no window (speaking pace)");
        log.AreEqual(large, TypeOn.VisibleAt(large / TypeOn.TimedCps + 0.1f, large, float.NaN), "type-on: a timed quote finishes at 16 a second");
    }

    // =======================================================================
    // Step 8 — {Mark:"…"} (pure)
    // =======================================================================

    static void Mark_TagParsing(Log log)
    {
        string script =
            "{Document:\"a license is required to export even if located outside\",\"license\",\"Commerce\",T=1.000,D=0,Report}one " +
            "{Mark:\"located outside\",T=3.000}two {Mark:\"export\"}three {Document:End,T=9.000}four {Mark:phrase}five {Mark:\"\"}six";
        List<ContentCardEvent> events = null;
        string clean = null;
        List<string> warnings = WarningsFrom(() => { var r = ContentZoneTagParser.ParseContentTags(script, 20f); clean = r.Item1; events = r.Item2; });

        log.IsTrue(!clean.Contains("{"), $"mark tags: every {{Mark:…}} is stripped, the malformed ones too (\"{clean}\")");
        var marks = events.FindAll(e => e.isMark);
        log.AreEqual(2, marks.Count, "mark tags: two marks parse");
        log.IsTrue(marks.Exists(m => m.primaryText == "located outside" && Mathf.Abs(m.triggerTime - 3f) < 1e-3f), "mark tags: a stamped mark keeps its phrase and its T=");
        log.IsTrue(marks.Exists(m => m.primaryText == "export"), "mark tags: the authored form parses");
        log.AreEqual(2, warnings.FindAll(w => w.Contains("[Card] Mark") && w.Contains("isn't valid")).Count,
            "mark tags: the two malformed ones are warned about and skipped");
        log.AreEqual(1, events.FindAll(e => e.cardType == ContentCardType.Document && !e.isMark && !e.dismissesCard).Count,
            "mark tags: a mark is never mistaken for a Document card");
    }

    static void Mark_Timing(Log log)
    {
        log.AreEqual(3, DocumentTypography.MaxMarks, "marks: up to three per card");
        log.Near(0f, DocumentTypography.MarkSweepAt(0f, 0), 1e-5f, "marks: the marker starts at once");
        log.Near(1f, DocumentTypography.MarkSweepAt(0.3f, 0), 1e-5f, "marks: …and sweeps a line in 0.3 s");
        log.Near(0f, DocumentTypography.MarkSweepAt(0.3f, 1), 1e-5f, "marks: the second line starts as the first ends");
        log.Near(1f, DocumentTypography.MarkSweepAt(0.6f, 1), 1e-5f, "marks: …0.3 s a line");
        log.Near(1f, DocumentTypography.EarlierAlphaAt(0f), 1e-5f, "marks: the earlier highlight is whole as the new mark starts");
        log.Near(DocumentTypography.EarlierMarkAlpha, DocumentTypography.EarlierAlphaAt(0.2f), 1e-5f, "marks: …and a pale tint 0.2 s later");
        log.IsTrue(DocumentTypography.EarlierMarkAlpha > 0.2f && DocumentTypography.EarlierMarkAlpha < 0.6f, "marks: pale, not gone — it stays");
    }

    // =======================================================================
    // Step 10 — {Pop:"…"} (pure)
    // =======================================================================

    static void Pop_TagParsing(Log log)
    {
        string script =
            "[flat] {Pop:\"On their device.\",T=10.000}On their device. {Pop:\"Not yours.\",T=11.200}Not yours. {Pop:End,T=12.500}after " +
            "{Pop:\"one two three four five six seven eight nine\"}long {Pop:End}x {Pop:bare}y {Pop:\"\"}z";
        var r = StageTags.ParsePops(script, 30f);
        log.IsTrue(!r.Script.Contains("{Pop"), $"pop tags: every {{Pop:…}} is removed, in the middle of a line too (\"{r.Script}\")");
        log.IsTrue(r.Script.Contains("[flat] On their device. Not yours. after"), "pop tags: the narration around them is untouched");
        log.AreEqual(5, r.Markers.Count, "pop tags: three phrases and two Ends parse");
        log.IsTrue(r.Markers[0].Phrase == "On their device." && Mathf.Abs(r.Markers[0].Time - 10f) < 1e-4f && r.Markers[0].Words == 3,
            "pop tags: a phrase keeps its words and its T=");
        log.IsTrue(r.Markers[2].Ends && Mathf.Abs(r.Markers[2].Time - 12.5f) < 1e-4f, "pop tags: {Pop:End} closes the run at its own T=");
        log.AreEqual(9, r.Markers[3].Words, "pop tags: a 9-word phrase is still shown");
        log.AreEqual(1, r.Warnings.FindAll(w => w.Contains("9 words")).Count, "pop tags: …with a warning (a Pop is at most six words)");
        log.AreEqual(2, r.Warnings.FindAll(w => w.Contains("isn't valid")).Count, "pop tags: the two malformed ones are warned about and skipped");

        foreach (int n in new[] { 1, 3, 6 })
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append("{Pop:\"Phrase ").Append(i + 1).Append("\",T=").Append(i + 1).Append(".000}word ");
            sb.Append("{Pop:End,T=").Append(n + 1).Append(".000}");
            var run = StageTags.ParsePops(sb.ToString(), 20f);
            log.AreEqual(n + 1, run.Markers.Count, $"pop tags: a run of {n} parses");
            log.AreEqual(0, run.Warnings.Count, $"pop tags: …with nothing to warn about ({n})");
        }
        log.AreEqual(6, StageTags.PopMaxWords, "pop: at most six words");
    }

    static void Pop_Maths(Log log)
    {
        const float fw = 1920f, fh = 1080f, capRatio = 0.859f;

        float shortPhrase = PopMath.FontSize(0.42f * 10, capRatio, fw, fh);          // "Preached."
        log.Near(PopMath.CapPx / capRatio, shortPhrase, 0.5f, "pop: a short phrase is set with 120 px capitals");
        float bar = 0.42f * 10 * shortPhrase + 2f * PopMath.PadXOfCap * capRatio * shortPhrase;
        log.IsTrue(bar < PopMath.MaxWidthOfFrame * fw, "pop: …its bar well inside the frame");

        float longPhrase = PopMath.FontSize(0.42f * 60, capRatio, fw, fh);           // nine long words
        float longBar = 0.42f * 60 * longPhrase + 2f * PopMath.PadXOfCap * capRatio * longPhrase;
        log.IsTrue(longPhrase < shortPhrase, "pop: a long phrase shrinks to stay on one line");
        log.IsTrue(longBar <= PopMath.MaxWidthOfFrame * fw + 0.5f, "pop: …with its bar at most 84 % of the frame");
        log.IsTrue(PopMath.FontSize(0.42f * 400, capRatio, fw, fh) >= PopMath.FloorPx - 0.01f, "pop: never under 32 px");

        log.Near(PopMath.PopFromScale, PopMath.PopScaleAt(0f), 1e-5f, "pop: a phrase pops from 0.9×");
        log.Near(1f, PopMath.PopScaleAt(PopMath.PopInSeconds), 1e-5f, "pop: …to full size in 0.12 s");
        log.Near(0f, PopMath.SoftenAt(0f), 1e-5f, "pop: the softening starts from the sharp picture");
        log.Near(1f, PopMath.SoftenAt(PopMath.SoftenSeconds), 1e-5f, "pop: …and is there in 0.25 s");

        // A 60 % dim: the scene at 40 % of its brightness in the encoded video.
        float linear = PopMath.DimAlpha(true), gamma = PopMath.DimAlpha(false);
        log.Near(0.6f, gamma, 1e-4f, "pop: in gamma space the black layer is 60 % opaque");
        log.Near(0.4f, Mathf.Pow(1f - linear, 1f / 2.2f), 1e-3f, "pop: in linear space (this project) it leaves the scene at 40 % brightness as seen");
        log.IsTrue(PopMath.SortOrder > 31000 && PopMath.SortOrder < 31400, "pop: above every card, under the captions' layer, the Echo and black");
    }

    // =======================================================================
    // Engine checks
    // =======================================================================

    static void Media_ScrollKeyword_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        try
        {
            List<string> warnings = WarningsFrom(() =>
            {
                object parsed = rig.Call("ParseMediaMarkers",
                    "{Image:page,T=1.000,Start,Scroll}a {Image:End,T=5.000}b {Image:page,T=6.000,D=8,Scroll,Right}c " +
                    "{Image:plain,T=15.000,D=4,Right}d {Video:clip,T=20.000,D=0,Scroll}e {Image:page,Start,Scroll}f", 40f);
                var markers = (List<MediaMarkerData>)parsed.GetType().GetField("Item2").GetValue(parsed);
                string clean = (string)parsed.GetType().GetField("Item1").GetValue(parsed);
                log.IsTrue(!clean.Contains("{"), "scroll keyword: every media form is stripped");
                log.AreEqual(6, markers.Count, "scroll keyword: six media markers");
                log.IsTrue(markers[0].scroll && markers[0].holdsOpen && markers[0].side == null, "scroll keyword: {Image:name,Start,Scroll} is a held scrolling image");
                log.IsTrue(markers[1].endsMedia && !markers[1].scroll, "scroll keyword: its End is its End");
                log.IsTrue(markers[2].scroll && !markers[2].holdsOpen && Mathf.Approximately(markers[2].displayDuration, 8f) &&
                           markers[2].side == EntryDirection.FromRight, "scroll keyword: timed, ,Scroll then ,Right — the side stays last");
                log.IsTrue(!markers[3].scroll && markers[3].side == EntryDirection.FromRight, "scroll keyword: an image without it is what it was");
                log.IsTrue(!markers[4].scroll, "scroll keyword: on a {Video:} it is ignored");
                log.IsTrue(markers[5].scroll && markers[5].holdsOpen, "scroll keyword: the authored form (no T=) parses");
            });
            log.AreEqual(1, warnings.FindAll(w => w.Contains(",Scroll only applies to an {Image:}")).Count, "scroll keyword: …with one warning for the clip");
        }
        finally { UnityEngine.Object.DestroyImmediate(rig.Root); }
    }

    static BigMediaCard BuildLogoCard(out GameObject root, string first, int anticipated, float duration = 0f)
    {
        root = new GameObject("LogoSelfTest", typeof(RectTransform), typeof(Canvas));
        var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root.transform, false);
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2(1920f, 1080f);

        var go = new GameObject("BigMediaCard", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(frame, false);
        var card = go.AddComponent<BigMediaCard>();
        card.EnsureBuilt();
        card.AnticipatedCount = anticipated;
        card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigMedia, primaryText = first, duration = duration }, null);
        return card;
    }

    static void Logos_RunOnTheCard_Engine(Log log)
    {
        var frame = new Vector2(1920f, 1080f);
        foreach (int n in new[] { 1, 4, 5, 8 })
        {
            BigMediaCard card = BuildLogoCard(out GameObject root, "Google", n);
            try
            {
                log.IsTrue(card.IsRun, $"logo run of {n}: a duration-less {{BigMedia:}} opens a run");
                card.Show();
                for (int i = 1; i < n; i++)
                    log.IsTrue(card.AppendLogo(i % 2 == 0 ? "Brave" : "X"), $"logo run of {n}: logo {i + 1} lands");
                log.AreEqual(n, card.LogoCount, $"logo run of {n}: {n} logos in the grid");
                card.Advance(1f);   // past every pop and re-centre

                Vector2 size = BigMediaGrid.SlotSize(n, frame);
                for (int i = 0; i < n; i++)
                {
                    RectTransform slot = card.Slot(i);
                    log.IsTrue(slot.gameObject.activeSelf, $"logo run of {n}: slot {i + 1} is shown");
                    Vector2 want = BigMediaGrid.SlotCentre(i, n, n, frame);
                    log.IsTrue((slot.anchoredPosition - want).magnitude < 0.5f, $"logo run of {n}: slot {i + 1} rests where the grid puts it");
                    log.IsTrue((slot.sizeDelta - size).magnitude < 0.5f, $"logo run of {n}: slot {i + 1} has the run's slot size");
                    log.Near(1f, slot.localScale.x, 1e-3f, $"logo run of {n}: slot {i + 1} has settled at full size");
                }

                if (n == 8)
                {
                    log.IsTrue(!card.AppendLogo("Google"), "logo run: a 9th logo is refused");
                    log.AreEqual(8, card.LogoCount, "logo run: …and the grid still holds eight");

                    // Strike the 1st and the 8th.
                    log.AreEqual(MarkResult.Ok, card.Strike(card.LogoName(0)), "strike: the first logo is struck");
                    log.IsTrue(card.IsStruck(0), "strike: …and stays struck");
                    card.Advance(0.05f);
                    log.IsTrue(card.StrokeDrawn(0, 0) > 0.1f && card.StrokeDrawn(0, 1) < 0.05f, "strike: the first stroke draws before the second");
                    card.Advance(0.5f);
                    log.Near(1f, card.StrokeDrawn(0, 0), 1e-2f, "strike: the first stroke is complete");
                    log.Near(1f, card.StrokeDrawn(0, 1), 1e-2f, "strike: …and so is the second");
                    log.Near(BigMediaGrid.StruckAlpha, card.LogoAlpha(0), 1e-2f, "strike: the struck logo dims to half");
                    log.Near(1f, card.LogoAlpha(1), 1e-2f, "strike: its neighbour is untouched");

                    // The same name appears more than once: a strike takes the first one not yet struck? No —
                    // it names a logo; a second strike of that name is a repeat.
                    log.AreEqual(MarkResult.Repeated, card.Strike(card.LogoName(0)), "strike: a second strike on the same logo is refused");
                    log.AreEqual(MarkResult.NotFound, card.Strike("Nokia"), "strike: a name that isn't in the grid is refused");
                    log.IsTrue(card.Strike(card.LogoName(7)) == MarkResult.Ok || card.LogoName(7) == card.LogoName(0),
                        "strike: the eighth logo can be struck");
                }

                if (n == 5)
                {
                    // The re-centre is eased: right after the 5th lands the first row is still on its way up.
                    BigMediaCard growing = BuildLogoCard(out GameObject root2, "Google", 5);
                    try
                    {
                        growing.Show();
                        for (int i = 1; i < 4; i++) growing.AppendLogo("X");
                        growing.Advance(1f);
                        float rowY = growing.Slot(0).anchoredPosition.y;
                        growing.AppendLogo("Brave");
                        growing.Advance(0.05f);
                        float moving = growing.Slot(0).anchoredPosition.y;
                        growing.Advance(1f);
                        float rest = growing.Slot(0).anchoredPosition.y;
                        log.IsTrue(rest > rowY + 10f, "logo run: the first row moves up when the fifth logo lands");
                        log.IsTrue(moving > rowY && moving < rest, "logo run: …eased, not jumped");
                        log.Near(BigMediaGrid.PopFromScale, 0.7f, 1e-5f, "logo run: each logo pops from 0.7×");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root2); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // A name nothing resolves is shown as plain text, as {BigMedia:} always did.
        BigMediaCard text = BuildLogoCard(out GameObject textRoot, "NoSuchLogoAnywhere", 1);
        try
        {
            log.IsTrue(text.IsText(0), "logo run: an unresolved name is shown as text");
            log.AreEqual("NoSuchLogoAnywhere", text.LogoName(0), "logo run: …and can still be struck by that name");
            log.AreEqual(MarkResult.Ok, text.Strike("nosuchlogoanywhere"), "logo run: …in any case");
        }
        finally { UnityEngine.Object.DestroyImmediate(textRoot); }

        // The timed form is untouched: a row laid out by anchors, not a run.
        BigMediaCard timed = BuildLogoCard(out GameObject timedRoot, "Google+Brave+X", 0, duration: 4f);
        try
        {
            log.IsTrue(!timed.IsRun && timed.LogoCount == 3, "timed BigMedia: {BigMedia:a+b+c,4} is the fixed row it always was");
            RectTransform s0 = timed.Slot(0);
            log.IsTrue(s0.anchorMin.x < s0.anchorMax.x && Mathf.Approximately(s0.anchorMin.y, 0.15f) && Mathf.Approximately(s0.anchorMax.y, 0.85f),
                "timed BigMedia: its slots keep their anchored layout");
            // A duration-less logo that lands while it is up joins it (and it becomes a grid).
            log.IsTrue(timed.AppendLogo("Google"), "timed BigMedia: a duration-less logo joins it");
            timed.Advance(1f);
            log.IsTrue(timed.IsRun && timed.LogoCount == 4, "timed BigMedia: …as a grid of four");
        }
        finally { UnityEngine.Object.DestroyImmediate(timedRoot); }
    }

    static void Logos_ControllerRules_Engine(Log log)
    {
        ContentZoneController cz = BuildStageZone(out GameObject root, out RectTransform sideZone);
        try
        {
            ContentCardEvent Logo(string name, float t, float duration = 0f) => new ContentCardEvent
                { cardType = ContentCardType.BigMedia, primaryText = name, triggerTime = t, duration = duration };
            ContentCardEvent Strike(string name, float t) => new ContentCardEvent
                { cardType = ContentCardType.BigMedia, primaryText = name, triggerTime = t, strikesLogo = true };
            var end = new ContentCardEvent { cardType = ContentCardType.BigMedia, primaryText = "End", triggerTime = 9f, dismissesCard = true };

            // A strike with no run open: one warning, nothing happens.
            List<string> w = WarningsFrom(() => cz.ShowCard(Strike("Brave", 0.5f)));
            log.IsTrue(!cz.IsCardActive, "strike rules: a strike with no run open shows nothing");
            log.AreEqual(1, w.FindAll(m => m.Contains("no logo run open")).Count, "strike rules: …and is warned about once");

            var a = Logo("Google", 1f); var b = Logo("Brave", 2f); var c = Logo("X", 3f);
            cz.SetTimeline(new List<ContentCardEvent> { a, b, Strike("Brave", 2.5f), c, end }, null);

            cz.ShowCard(a);
            var card = cz.ActiveCard as BigMediaCard;
            log.IsTrue(card != null && card.IsRun && card.LogoCount == 1, "logo rules: the first duration-less logo opens the grid");
            log.AreEqual(3, card != null ? card.AnticipatedCount : 0, "logo rules: …sized for the whole run (read ahead)");
            cz.ShowCard(b);
            log.IsTrue(cz.ActiveCard == card && card.LogoCount == 2, "logo rules: the next logo lands on the open grid, not as a new card");
            cz.ShowCard(b);
            log.AreEqual(2, card.LogoCount, "logo rules: the same tag delivered twice (catch-up + tracker on one frame) lands once");

            w = WarningsFrom(() => cz.ShowCard(Strike("Brave", 2.5f)));
            log.IsTrue(card.IsStruck(1) && w.Count == 0, "strike rules: {BigMedia:Strike,Brave} strikes the logo, no warning");
            w = WarningsFrom(() => cz.ShowCard(Strike("Brave", 2.6f)));
            log.AreEqual(1, w.FindAll(m => m.Contains("already struck")).Count, "strike rules: a second strike on it is warned about");
            w = WarningsFrom(() => cz.ShowCard(Strike("Nokia", 2.7f)));
            log.AreEqual(1, w.FindAll(m => m.Contains("no logo called \"Nokia\"")).Count, "strike rules: a name not in the grid is warned about");

            cz.ShowCard(c);
            log.AreEqual(3, card.LogoCount, "logo rules: the third logo lands");
            log.IsTrue(card.IsStruck(1), "strike rules: the struck logo stays struck as the run grows");

            for (int i = 4; i <= 8; i++) cz.ShowCard(Logo("X", 3f + i * 0.1f));
            w = WarningsFrom(() => cz.ShowCard(Logo("Google", 4f)));
            log.AreEqual(8, card.LogoCount, "logo rules: the grid stops at eight");
            log.AreEqual(1, w.FindAll(m => m.Contains("at most 8")).Count, "logo rules: …a ninth is ignored with a warning");

            // {BigMedia:Strike,4} is a timed card, not a strike.
            cz.ClearForTransition();
            cz.ShowCard(Logo("Strike", 10f, duration: 4f));
            var timedCard = cz.ActiveCard as BigMediaCard;
            log.IsTrue(timedCard != null && !timedCard.IsRun, "strike rules: {BigMedia:Strike,4} opens the timed card for a logo called Strike");
            w = WarningsFrom(() => cz.ShowCard(Strike("Strike", 10.5f)));
            log.AreEqual(1, w.FindAll(m => m.Contains("no logo run open")).Count, "strike rules: a strike on a TIMED BigMedia is refused (only inside a run)");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static RectTransform BuildOverlayFrame(out GameObject root)
    {
        root = new GameObject("OverlaySelfTest", typeof(RectTransform), typeof(Canvas));
        var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root.transform, false);
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2(1920f, 1080f);
        return frame;
    }

    static void Name_Label_Engine(Log log)
    {
        RectTransform frame = BuildOverlayFrame(out GameObject root);
        NameOverlay label = NameOverlay.CreateOn(frame);
        try
        {
            var cases = new[]
            {
                (name: "Jensen Huang", role: "Nvidia CEO", what: "a 2-word name"),
                (name: "Maria da Conceição Silva Santos", role: "Minister of Science, Technology and Higher Education of the Portuguese Republic", what: "a 5-word name, a long role"),
            };
            foreach (var c in cases)
                foreach (bool right in new[] { false, true })
                {
                    label.SetLabel(c.name, c.role, right);
                    RectTransform bar = label.Bar;
                    float w = bar.sizeDelta.x, h = bar.sizeDelta.y;
                    float left = bar.anchoredPosition.x, top = -bar.anchoredPosition.y;
                    string side = right ? "lower right" : "lower left";
                    log.IsTrue(w <= NameLabelMath.MaxWidthOfFrame * 1920f + 0.5f, $"name ({c.what}, {side}): the bar is at most 45 % of the frame ({w:F0} px)");
                    log.IsTrue(left >= 0.05f * 1920f - 0.5f && left + w <= 0.95f * 1920f + 0.5f, $"name ({c.what}, {side}): inside the title-safe sides");
                    log.Near(NameLabelMath.BottomOfFrame * 1080f, top + h, 0.5f, $"name ({c.what}, {side}): its bottom edge at 87.5 % of the height");
                    log.IsTrue(right ? left + w > 1920f * 0.6f : left < 1920f * 0.4f, $"name ({c.what}): in the {side} corner");
                    log.IsTrue(label.NameText.fontSize >= 32f && label.RoleText.fontSize >= NameLabelMath.RoleMinPx - 0.01f,
                        $"name ({c.what}, {side}): nothing under 32 px (name {label.NameText.fontSize:F0}, role {label.RoleText.fontSize:F0})");
                    log.AreEqual(c.name, label.NameText.text, $"name ({c.what}): the name is shown as written");
                    log.AreEqual(c.role, label.RoleText.text, $"name ({c.what}): the role is shown as written");
                    // The text stays inside the bar.
                    foreach (TMP_Text t in new TMP_Text[] { label.NameText, label.RoleText })
                    {
                        Rect ink = CardTypography.InkInCard(t, bar);
                        log.IsTrue(ink.xMin >= -0.5f && ink.xMax <= w + 0.5f && ink.yMin >= -0.5f && ink.yMax <= h + 0.5f,
                            $"name ({c.what}, {side}): {t.name} is inside the bar ({Fmt(ink)} of {w:F0}×{h:F0})");
                    }
                }

            // A second label replaces the first.
            label.SetLabel("First Person", "role one", false);
            label.SetLabel("Second Person", "role two", false);
            log.AreEqual("Second Person", label.NameText.text, "name: another {Name:} replaces the label");

            // Sliding: outside the frame at 0, at rest at 1.
            label.SetLabel("Jensen Huang", "Nvidia CEO", false);
            label.Place(0f);
            log.IsTrue(label.Bar.anchoredPosition.x + label.Bar.sizeDelta.x <= 0f, "name: it slides in from outside the left edge");
            label.SetLabel("Jensen Huang", "Nvidia CEO", true);
            label.Place(0f);
            log.IsTrue(label.Bar.anchoredPosition.x >= 1920f, "name: …or from outside the right edge when it is on the right");
        }
        finally { UnityEngine.Object.DestroyImmediate(label.gameObject); UnityEngine.Object.DestroyImmediate(root); }
    }

    static QuoteCard BuildQuote(out GameObject root, bool wide, string quote, string photo, float duration = 0f, float endTime = float.NaN)
    {
        QuoteCard card = BuildCard<QuoteCard>(out root);
        if (wide)
        {
            var zone = (RectTransform)card.transform.parent;
            zone.sizeDelta = new Vector2(1440f, 842.4f);
            card.SetStageLayout(true, 1080f);
        }
        card.Initialize(new ContentCardEvent
        {
            cardType = ContentCardType.Quote, primaryText = quote, secondaryText = "Sarah Mitchell",
            tertiaryText = "VP of Communications, MegaCorp", duration = duration, photoName = photo, endTime = endTime, triggerTime = 10f,
        }, null);
        return card;
    }

    const string Quote8 = "We believe transparency is at the core, always.";
    const string Quote40 =
        "We believe transparency is at the core of everything we do, and we will keep saying so in every statement, " +
        "every filing and every interview until the day the regulators finally publish what they found in our own records.";

    static void Quote_PortraitAndTypeOn_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            // No ,Photo=: the card it always was.
            QuoteCard plain = BuildQuote(out GameObject r1, wide: false, Quote8, photo: null);
            try
            {
                log.IsTrue(!plain.HasPortrait && !plain.Portrait.gameObject.activeSelf, "quote: no ,Photo= — no portrait");
                CheckCard(log, plain, plain.Layout, "Quote without a photo");
                Rect q = RectInCard(plain.QuoteText.rectTransform, (RectTransform)plain.transform);
                Rect n = RectInCard(plain.NameText.rectTransform, (RectTransform)plain.transform);
                log.IsTrue(n.yMin >= q.yMax - 1f, "quote: without a photo the name sits BELOW the quote, as before");
                log.IsTrue(!plain.IsTyping, "quote: a side quote shows whole");
            }
            finally { UnityEngine.Object.DestroyImmediate(r1); }

            // A photo that doesn't resolve: no portrait, a warning, the usual layout.
            GameObject r2 = null;
            QuoteCard missing = null;
            List<string> w = WarningsFrom(() => missing = BuildQuote(out r2, wide: false, Quote8, photo: "no_such_portrait_file"));
            try
            {
                log.IsTrue(!missing.HasPortrait, "quote: an unresolved ,Photo= shows the card without a portrait");
                log.AreEqual(1, w.FindAll(m => m.Contains("no_such_portrait_file") && m.Contains("wasn't found")).Count, "quote: …with one warning");
                CheckCard(log, missing, missing.Layout, "Quote with an unresolved photo");
            }
            finally { UnityEngine.Object.DestroyImmediate(r2); }

            // Type-on, wide: both quotes, over 2 s and over 12 s.
            foreach (string text in new[] { Quote8, Quote40 })
                foreach (float seconds in new[] { 2f, 12f })
                {
                    QuoteCard card = BuildQuote(out GameObject root, wide: true, text, photo: null, duration: 0f, endTime: 10f + seconds);
                    try
                    {
                        string what = $"{(text == Quote8 ? "8-word" : "40-word")} quote over {seconds:F0} s";
                        CheckCard(log, card, card.Layout, "wide " + what);
                        float sizeBefore = card.QuoteText.fontSize;
                        int linesBefore = card.QuoteText.textInfo.lineCount;
                        float window = TypeOn.Window(10f, 10f + seconds);
                        card.BeginTypeOn(window);
                        log.IsTrue(card.IsTyping && card.VisibleCharacters == 0, $"type-on ({what}): starts empty");
                        log.IsTrue(card.NameText.text == "Sarah Mitchell" && card.NameText.maxVisibleCharacters > 1000,
                            $"type-on ({what}): the name and role are there from the first frame");

                        int total = card.TotalCharacters, last = 0;
                        bool grew = true, rewrapped = false;
                        for (float t = 0f; t < window - 1e-3f; t += 1f / 30f)
                        {
                            card.Advance(1f / 30f);
                            int v = card.VisibleCharacters;
                            if (v < last) grew = false;
                            last = v;
                            card.QuoteText.ForceMeshUpdate(true, true);
                            if (card.QuoteText.textInfo.lineCount != linesBefore || !Mathf.Approximately(card.QuoteText.fontSize, sizeBefore)) rewrapped = true;
                        }
                        card.Advance(2f / 30f);
                        log.IsTrue(grew, $"type-on ({what}): characters only ever arrive");
                        log.IsTrue(!rewrapped, $"type-on ({what}): the lines never re-wrap while it types (laid out for the full quote)");
                        log.IsTrue(!card.IsTyping && card.VisibleCharacters == total, $"type-on ({what}): the whole quote is up 0.3 s before its End");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }

            // A timed wide quote types at 16 a second.
            QuoteCard timed = BuildQuote(out GameObject r3, wide: true, Quote8, photo: null, duration: 6f);
            try
            {
                timed.BeginTypeOn(float.NaN);
                timed.Advance(1f);
                log.IsTrue(Mathf.Abs(timed.VisibleCharacters - 17) <= 1, $"type-on: a timed quote types 16 characters a second ({timed.VisibleCharacters} after 1 s)");
            }
            finally { UnityEngine.Object.DestroyImmediate(r3); }
        }
        finally { VisualsRuntimeApplier.CardFontOverride = savedFont; }
    }

    static void Mark_OnDocumentAndExcerpt_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        const string passage =
            "A license is required to export advanced computing items to entities headquartered in Country Group D:5, " +
            "even if the entities themselves are located outside of that group and operate under a foreign parent.";
        try
        {
            // ---- Document ----
            DocumentCard doc = BuildCard<DocumentCard>(out GameObject r1);
            try
            {
                doc.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Document, primaryText = passage, secondaryText = "headquartered",
                    tertiaryText = "US Commerce Department guidance", documentStamp = DocumentStamp.Report,
                }, null);
                doc.BeginTimelineForTest();
                doc.Advance(3f);
                int ownRects = doc.MarkerCount;
                log.IsTrue(ownRects >= 1, "mark (Document): the tag's own phrase is highlighted");

                log.AreEqual(MarkResult.NotFound, doc.AddMark("not in the passage"), "mark (Document): a phrase that isn't in the passage is refused");
                log.AreEqual(MarkResult.Repeated, doc.AddMark("headquartered"), "mark (Document): the tag's own phrase can't be marked again");
                log.AreEqual(0, doc.MarkCount, "mark (Document): …and nothing changed");

                log.AreEqual(MarkResult.Ok, doc.AddMark("located outside"), "mark (Document): the first mark is made");
                log.IsTrue(doc.MarkerCount > ownRects, "mark (Document): the marker has a new phrase to sweep");
                int firstNew = ownRects;
                log.AreEqual(1, doc.RectOwner(firstNew), "mark (Document): …owned by mark 1");
                doc.Advance(0.02f);
                log.IsTrue(doc.Marker(firstNew).rectTransform.sizeDelta.x < doc.HighlightRects[firstNew].width, "mark (Document): the sweep starts at once and takes time");
                float strongBefore = doc.Marker(0).color.a;
                doc.Advance(1.5f);
                log.Near(doc.HighlightRects[firstNew].width, doc.Marker(firstNew).rectTransform.sizeDelta.x, 0.5f, "mark (Document): the new phrase is fully marked");
                log.Near(DocumentTypography.Marker.a, doc.Marker(firstNew).color.a, 0.01f, "mark (Document): …in the full marker colour");
                log.Near(DocumentTypography.Marker.a * DocumentTypography.EarlierMarkAlpha, doc.Marker(0).color.a, 0.01f,
                    "mark (Document): the earlier highlight has faded to a pale tint");
                log.IsTrue(doc.Marker(0).enabled && doc.Marker(0).color.a > 0.1f && strongBefore >= doc.Marker(0).color.a, "mark (Document): …and stays");

                // A mark spanning a line break gets one stroke per line.
                log.AreEqual(MarkResult.Ok, doc.AddMark("entities headquartered in Country Group D:5, even if the entities themselves"),
                    "mark (Document): a second mark, long enough to wrap");
                int spanning = 0;
                for (int i = 0; i < doc.MarkerCount; i++) if (doc.RectOwner(i) == 2) spanning++;
                log.IsTrue(spanning >= 2, $"mark (Document): a phrase across a line break is marked on each line ({spanning} strokes)");

                log.AreEqual(MarkResult.Ok, doc.AddMark("foreign parent"), "mark (Document): a third mark");
                log.AreEqual(MarkResult.TooMany, doc.AddMark("advanced computing"), "mark (Document): a fourth is refused");
                log.AreEqual(3, doc.MarkCount, "mark (Document): three marks, no more");

                // The stamp never covers a marked phrase.
                doc.Advance(3f);
                if (doc.StampPlaced && doc.StampPolygon != null)
                    for (int i = 0; i < doc.MarkerCount; i++)
                        log.IsTrue(!DocumentTypography.Overlaps(doc.StampPolygon, doc.HighlightRects[i]),
                            $"mark (Document): the stamp is clear of marked stroke {i + 1}");
            }
            finally { UnityEngine.Object.DestroyImmediate(r1); }

            // A mark that lands under a stamp sitting over the passage moves it.
            DocumentCard crowded = BuildCard<DocumentCard>(out GameObject r2);
            try
            {
                string longPassage = passage + " " + passage + " The order takes effect immediately and applies to every shipment already in transit.";
                crowded.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Document, primaryText = longPassage, secondaryText = "A license is required",
                    tertiaryText = "US Commerce Department guidance", documentStamp = DocumentStamp.Allegation,
                }, null);
                crowded.BeginTimelineForTest();
                crowded.Advance(4f);
                if (crowded.StampPlaced && !crowded.StampInBand)
                {
                    crowded.AddMark("already in transit");
                    crowded.AddMark("takes effect immediately");
                    if (crowded.StampPlaced)
                        for (int i = 0; i < crowded.MarkerCount; i++)
                            log.IsTrue(!DocumentTypography.Overlaps(crowded.StampPolygon, crowded.HighlightRects[i]),
                                $"mark (Document, stamp over the passage): the stamp moved clear of stroke {i + 1}");
                    else
                        log.IsTrue(!crowded.Stamp.gameObject.activeSelf, "mark (Document, stamp over the passage): with no free corner the stamp is taken off");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(r2); }

            // ---- Excerpt ----
            ExcerptCard ex = BuildCard<ExcerptCard>(out GameObject r3);
            try
            {
                const string text = "Apps that attempt to scam users will be removed from the App Store. This includes apps that attempt to " +
                                    "trick users into purchasing a subscription under false pretenses";
                ex.Initialize(new ContentCardEvent
                {
                    cardType = ContentCardType.Excerpt, primaryText = text,
                    secondaryText = "trick users into purchasing a subscription", tertiaryText = "App Store Review Guidelines 3.1.2(a)",
                }, null);
                log.AreEqual(MarkResult.NotFound, ex.AddMark("not there"), "mark (Excerpt): a phrase that isn't in the passage is refused");
                log.AreEqual(MarkResult.Repeated, ex.AddMark("trick users into purchasing a subscription"), "mark (Excerpt): the tag's own phrase can't be marked again");
                log.AreEqual(MarkResult.Ok, ex.AddMark("under false pretenses"), "mark (Excerpt): a mark is made");
                ex.Advance(0.05f);
                log.IsTrue(ex.HighlightMarkup.Contains("<mark=#"), "mark (Excerpt): the highlight starts wiping at once");
                ex.Advance(1f);
                log.IsTrue(ex.HighlightMarkup.Contains("under false pretenses</mark>"), "mark (Excerpt): the whole phrase ends up highlighted");
                log.AreEqual(MarkResult.Ok, ex.AddMark("scam users"), "mark (Excerpt): a second mark");
                ex.Advance(1f);
                log.IsTrue(ex.HighlightMarkup.IndexOf("scam users</mark>", StringComparison.Ordinal) <
                           ex.HighlightMarkup.IndexOf("under false pretenses</mark>", StringComparison.Ordinal),
                    "mark (Excerpt): the spans are written in passage order");
                log.AreEqual(MarkResult.Ok, ex.AddMark("App Store"), "mark (Excerpt): a third mark");
                log.AreEqual(MarkResult.TooMany, ex.AddMark("removed"), "mark (Excerpt): a fourth is refused");
                CheckCard(log, ex, ex.Layout, "Excerpt with marks");
            }
            finally { UnityEngine.Object.DestroyImmediate(r3); }

            // ---- With no text card open ----
            ContentZoneController cz = BuildStageZone(out GameObject r4, out RectTransform zone);
            try
            {
                var mark = new ContentCardEvent { cardType = ContentCardType.Document, isMark = true, primaryText = "anything", triggerTime = 1f };
                List<string> w = WarningsFrom(() => cz.ShowCard(mark));
                log.IsTrue(!cz.IsCardActive, "mark: with no card open nothing is shown");
                log.AreEqual(1, w.FindAll(m => m.Contains("with no Document, Excerpt or Clipping open")).Count, "mark: …and it is warned about once");

                var stat = StatEvent("40%", 2f);
                cz.ShowCard(stat);
                w = WarningsFrom(() => cz.ShowCard(mark));
                log.AreEqual(1, w.FindAll(m => m.Contains("with no Document, Excerpt or Clipping open")).Count, "mark: a Stat is not a text card either");
                log.IsTrue(cz.ActiveCard is StatCard, "mark: …and the card that is up is left alone");
            }
            finally { UnityEngine.Object.DestroyImmediate(r4); }
        }
        finally { VisualsRuntimeApplier.CardFontOverride = savedFont; }
    }

    static void Pop_Overlay_Engine(Log log)
    {
        RectTransform frame = BuildOverlayFrame(out GameObject root);
        PopOverlay pop = PopOverlay.CreateOn(frame);
        try
        {
            log.IsTrue(!pop.IsShowing && !pop.IsRunning, "pop: nothing is up before the first phrase");

            foreach (string phrase in new[] { "Preached.", "On their device.", "One two three four five six", "One two three four five six seven eight nine" })
            {
                float size = pop.SetPhrase(phrase);
                RectTransform bar = pop.Bar;
                log.IsTrue(bar.sizeDelta.x <= PopMath.MaxWidthOfFrame * 1920f + 1f, $"pop \"{phrase}\": the bar fits 84 % of the frame ({bar.sizeDelta.x:F0} px)");
                log.IsTrue(size >= PopMath.FloorPx, $"pop \"{phrase}\": {size:F0} px — never under 32");
                log.AreEqual(phrase, pop.Text.text, $"pop \"{phrase}\": shown exactly as written");
                pop.Text.ForceMeshUpdate(true, true);
                log.AreEqual(1, pop.Text.textInfo.lineCount, $"pop \"{phrase}\": one line");
                Rect ink = CardTypography.InkInCard(pop.Text, bar);
                log.IsTrue(ink.xMin >= -1f && ink.xMax <= bar.sizeDelta.x + 1f, $"pop \"{phrase}\": the words are inside the bar");
                log.IsTrue(Mathf.Abs(bar.anchoredPosition.x) < 0.5f && Mathf.Abs(bar.anchoredPosition.y) < 0.5f, $"pop \"{phrase}\": centred in the frame");
            }

            // A run of three: softening up, each phrase replaces the last, End clears.
            pop.Show("First.");
            log.IsTrue(pop.IsRunning && pop.Bar.gameObject.activeSelf, "pop: the first phrase opens the run");
            log.Near(PopMath.PopFromScale, pop.Bar.localScale.x, 1e-3f, "pop: …popping in from 0.9×");
            pop.Advance(0.3f);
            log.Near(pop.DimPeak, pop.DimAlpha, 1e-3f, "pop: everything behind it is softened within 0.25 s");
            log.Near(1f, pop.Bar.localScale.x, 1e-3f, "pop: the phrase has settled at full size");

            pop.Show("Second.");
            log.AreEqual("Second.", pop.Text.text, "pop: the next phrase REPLACES the one on the frame (they never stack)");
            log.Near(pop.DimPeak, pop.DimAlpha, 1e-3f, "pop: …and the softening doesn't flicker");
            pop.Advance(0.2f);
            pop.Show("Third.");
            pop.Advance(0.2f);

            pop.End();
            log.IsTrue(!pop.IsRunning && pop.IsShowing, "pop: {Pop:End} ends the run; the fade is still showing");
            pop.Advance(0.16f);
            log.IsTrue(!pop.Bar.gameObject.activeSelf, "pop: the phrase is gone after 0.15 s");
            pop.Advance(0.2f);
            log.Near(0f, pop.DimAlpha, 1e-3f, "pop: the softening has cleared after 0.25 s");
            log.IsTrue(!pop.IsShowing, "pop: nothing is left on the frame");

            // Ended early (a transition, a {Black}, a close-up): gone on the frame.
            pop.Show("Cut short.");
            pop.Advance(0.3f);
            pop.EndNow("a transition");
            log.IsTrue(!pop.IsShowing && !pop.IsRunning && pop.DimAlpha < 1e-3f, "pop: a transition ends the run on its frame");
        }
        finally { UnityEngine.Object.DestroyImmediate(pop.gameObject); UnityEngine.Object.DestroyImmediate(root); }
    }

    // On the real display: a RawImage on a 1920 × 1080 canvas, the stage wide.
    static void Media_ScreenshotsOnTheDisplay_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        var canvasGo = new GameObject("MediaFrame", typeof(RectTransform));   // (a plain 1920 × 1080 rect: a root Canvas would size itself to the screen)
        canvasGo.transform.SetParent(rig.Root.transform, false);
        var canvasRt = (RectTransform)canvasGo.transform;
        canvasRt.sizeDelta = new Vector2(1920f, 1080f);
        var displayGo = new GameObject("MediaDisplay", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        displayGo.transform.SetParent(canvasRt, false);
        var display = displayGo.GetComponent<UnityEngine.UI.RawImage>();
        var displayRt = display.rectTransform;
        // The authored side slot.
        displayRt.anchorMin = displayRt.anchorMax = new Vector2(0.25f, 0.5f);
        displayRt.sizeDelta = new Vector2(800f, 640f);
        var made = new List<Texture2D>();
        Texture2D Tex(int w, int h, RectInt? card)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            if (card.HasValue) t.SetPixels32(CutoutPixels(w, h, card.Value.x, card.Value.y, card.Value.width, card.Value.height));
            else { var px = new Color32[w * h]; for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 200, 200, 255); t.SetPixels32(px); }
            t.Apply(false);
            made.Add(t);
            return t;
        }
        bool savedPush = StagePrefs.PushIn;
        try
        {
            StagePrefs.PushIn = true;
            rig.Mps.mediaDisplay = display;
            rig.Set("mediaDisplayBaseSize", displayRt.sizeDelta);
            rig.Set("mediaDisplayBaseAnchorMin", displayRt.anchorMin);
            rig.Set("mediaDisplayBaseAnchorMax", displayRt.anchorMax);
            rig.Set("mediaDisplayBasePivot", displayRt.pivot);
            rig.Set("mediaDisplayBaseAnchoredPos", displayRt.anchoredPosition);
            rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);   // the stage is wide

            void Show(string name, Texture2D tex, float seconds, bool held = false, bool scroll = false)
            {
                display.texture = tex;
                rig.Set("mediaHandoverPending", true);   // (a run's cut: no entry tween to wait for)
                rig.Call("ShowDisplayOnStage", new MediaMarkerData { mediaType = MediaType.IMAGE, mediaName = name, displayDuration = seconds, holdsOpen = held, scroll = scroll }, false);
            }

            // An article: the frame, starting on its top; it rests there, then scrolls down slowly to its bottom.
            Show("article_selftest_capture", Tex(720, 540, null), 0f, held: true);
            log.IsTrue(displayRt.rect.width > 1919f && displayRt.rect.height > 1079f && rig.Get<bool>("mediaIsFullFrame"), "shots rig: an article fills the frame");
            log.Near(1f, display.uvRect.yMax, 1e-5f, "shots rig: …starting on its top edge");
            log.Near(0.75f, display.uvRect.height, 1e-4f, "shots rig: …a 4:3 capture shows three quarters of itself at a time");
            log.IsTrue(Mathf.Approximately(display.uvRect.width, 1f) && Mathf.Approximately(display.uvRect.x, 0f), "shots rig: …its whole width, never cropped at the sides");
            rig.Call("StepMediaMotion", 1f);
            log.Near(1f, display.uvRect.yMax, 1e-5f, "shots rig: after a second it is still on its top (time to read the masthead)");
            rig.Call("StepMediaMotion", 3f);
            float partWay = display.uvRect.yMax;
            log.IsTrue(partWay < 0.99f && partWay > 0.80f, $"shots rig: after 4 s it has scrolled part of the way down (top of the window at {partWay:F3})");
            log.Near((4f - MediaMotionMath.ArticleTopHoldSeconds - MediaMotionMath.ArticleScrollRampSeconds * 0.5f) * MediaMotionMath.HeldScrollPerSecond * 1080f,
                     (1f - partWay) * 540f * (1920f / 720f), 2f, "shots rig: …at 65 px a second on screen");
            rig.Call("StepMediaMotion", 60f);
            log.Near(0f, display.uvRect.yMin, 1e-5f, "shots rig: it ends on its very bottom");
            log.IsTrue(Mathf.Approximately(display.uvRect.width, 1f) && Mathf.Approximately(display.uvRect.height, 0.75f), "shots rig: …still its whole width (it scrolls, it does not push in)");
            log.IsTrue(MediaDisplayShadow.CutoutShownWhole == null, "shots rig: (no cut-out shadow for an article)");
            // Timed, it moves at the same calm rate — it never hurries to reach the bottom.
            Show("article_selftest_capture", made[made.Count - 1], 4f);
            rig.Call("StepMediaMotion", 4f);
            log.Near(partWay, display.uvRect.yMax, 1e-4f, "shots rig: a timed article is as far down after 4 s as a held one");

            // An article that isn't taller than the frame has nothing to scroll to: top kept, a slight push-in.
            Show("article_selftest_wide", Tex(960, 540, null), 4f);
            rig.Call("StepMediaMotion", 4f);
            log.Near(1f, display.uvRect.yMax, 1e-5f, "shots rig: a 16:9 article keeps its top edge to the end");
            log.Near(1f / MediaMotionMath.ArticlePushInMax, display.uvRect.width, 1e-4f, "shots rig: …pushed in 2 %, not a photo's 8 %");

            // A photo is exactly as it was: centred crop, 8 %.
            Show("selftest_photo", Tex(720, 540, null), 4f);
            log.Near(0.5f, display.uvRect.center.y, 1e-5f, "shots rig: a photo is still cropped from its centre");
            rig.Call("StepMediaMotion", 4f);
            log.Near(1f / MediaMotionMath.PushInMax, display.uvRect.width, 1e-4f, "shots rig: …and still pushes in 8 %");

            // A post that fits: whole, centred, its own rectangle, with the cards' shadow.
            Texture2D wide = Tex(1209, 602, new RectInt(192, 96, 825, 410));
            Show("x_post_selftest_wide", wide, 5f);
            MediaMotionMath.PostLayout want = MediaMotionMath.LayoutPost(1920f, 1080f, 1209, 602, new RectInt(192, 96, 825, 410));
            log.IsTrue(!want.Scrolls, "shots rig: (the wide post is shown whole)");
            // (the bounds are read every 2nd pixel: a pixel either way, times the scale)
            log.IsTrue(Mathf.Abs(displayRt.rect.width - want.SlotSize.x) < 4f && Mathf.Abs(displayRt.rect.height - want.SlotSize.y) < 4f,
                $"shots rig: a post's display is the post's own rectangle ({displayRt.rect.width:F0} × {displayRt.rect.height:F0})");
            log.IsTrue(displayRt.anchorMin == new Vector2(0.5f, 0.5f) && displayRt.anchoredPosition == Vector2.zero, "shots rig: …centred on the frame");
            log.IsTrue(Mathf.Abs(display.uvRect.xMin - 192f / 1209f) < 2e-3f && Mathf.Abs(display.uvRect.xMax - 1017f / 1209f) < 2e-3f, "shots rig: …showing the card and none of its see-through margin");
            log.IsTrue(MediaDisplayShadow.CutoutShownWhole == wide, "shots rig: …and it casts the cards' shadow");
            log.IsTrue(rig.Get<bool>("mediaIsFullFrame"), "shots rig: it still counts as having the frame (nothing else opens beside it)");
            rig.Call("StepMediaMotion", 5f);
            log.Near(MediaMotionMath.PostPushInMax, displayRt.localScale.x, 1e-4f, "shots rig: it has grown 3 % as it leaves");
            log.IsTrue(Mathf.Abs(display.uvRect.xMin - 192f / 1209f) < 2e-3f, "shots rig: …the card itself, never a crop of it");

            // A tall post: readable width, a column the height of the frame, the window travelling down.
            Show("x_post_selftest_tall", Tex(1209, 1550, new RectInt(192, 96, 825, 1358)), 0f, held: true);
            log.Near(1f, displayRt.localScale.x, 1e-6f, "shots rig: the next image starts at the display's own scale");
            log.IsTrue(Mathf.Abs(displayRt.rect.width - 960f) < 2f && Mathf.Abs(displayRt.rect.height - 1080f) < 1f, $"shots rig: a tall post is half the frame wide and the frame high ({displayRt.rect.width:F0} × {displayRt.rect.height:F0})");
            log.IsTrue(MediaDisplayShadow.CutoutShownWhole == null, "shots rig: (a column is not the post's rectangle: no shadow box)");
            float top0 = display.uvRect.y;
            rig.Call("StepMediaMotion", 2f);
            float top2 = display.uvRect.y;
            log.IsTrue(top2 < top0 - 1e-4f, "shots rig: held, it scrolls down");
            log.Near(MediaMotionMath.HeldScrollPerSecond * 2f * 1080f, (top0 - top2) * 1550f * (960f / 825f), 2f, "shots rig: …65 px a second on screen");
            rig.Call("StepMediaMotion", 600f);
            float rest = display.uvRect.y;
            rig.Call("StepMediaMotion", 5f);
            log.Near(rest, display.uvRect.y, 1e-6f, "shots rig: …and rests at its bottom");

            // Timed too short to read: warned, with the seconds it needs.
            List<string> warnings = WarningsFrom(() => Show("x_post_selftest_tall", made[made.Count - 1], 2f));
            log.IsTrue(warnings.Exists(w => w.Contains("too fast to read") && w.Contains("at least")), "shots rig: a tall post given 2 s is warned about, with the time it needs");
            warnings = WarningsFrom(() => Show("x_post_selftest_tall", made[made.Count - 1], 8f));
            log.AreEqual(0, warnings.Count, "shots rig: given 8 s it is not");

            // An explicit ,Scroll is the author's own framing: the page's scroll, as before.
            Show("x_post_selftest_tall", made[made.Count - 1], 6f, scroll: true);
            log.IsTrue(displayRt.rect.width > 1919f && Mathf.Approximately(display.uvRect.width, 1f), "shots rig: {Image:x_post_…,6,Scroll} is still the full-width page scroll");

            // {Position:Off,Cut} written with a picture that fills the frame: he stays until it has landed over him.
            {
                rig.Call("ResetMediaSlot");
                rig.Set("isShowingMedia", false);
                var clip = new MediaMarkerData { mediaType = MediaType.VIDEO, mediaName = "selftest_clip", triggerTime = 0.05f };
                var photo = new MediaMarkerData { mediaType = MediaType.IMAGE, mediaName = "selftest_photo", triggerTime = 0.05f, holdsOpen = true };
                var post = new MediaMarkerData { mediaType = MediaType.IMAGE, mediaName = "x_post_selftest_wide", triggerTime = 0.05f, holdsOpen = true };
                var later = new MediaMarkerData { mediaType = MediaType.VIDEO, mediaName = "selftest_clip", triggerTime = 3f };
                var end = new MediaMarkerData { mediaType = MediaType.VIDEO, mediaName = "End", triggerTime = 0.05f, endsMedia = true };
                void AtCenter()
                {
                    rig.Set("currentPosition", CharacterPosition.Off);
                    rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
                }
                bool AtHisMark() => ((Vector2)rig.Presenter.position - (Vector2)rig.Center.position).magnitude < 1e-4f;

                foreach (var (marker, what) in new[] { (clip, "a clip"), (photo, "a photo") })
                {
                    AtCenter();
                    rig.Set("mediaMarkers", new List<MediaMarkerData> { marker });
                    rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                    log.IsTrue(rig.Mps.IsPresenterOff && rig.Mps.IsStageWide, $"cover: {{Position:Off,Cut}} with {what} — the stage is wide at once (the picture takes the frame)");
                    log.IsTrue(rig.Mps.IsCoveredExitPending && AtHisMark(), $"cover: …but he is still standing at his mark ({what} has not landed yet)");
                    rig.Call("StepCoveredExit");
                    log.IsTrue(rig.Mps.IsCoveredExitPending && AtHisMark(), $"cover: …and stays while nothing is on the frame ({what})");
                    // The media appears — lifted over him, because at rest he is drawn in front of the media canvas — and its entry lands.
                    Show("selftest_photo", Tex(96, 54, null), 0f, held: true);
                    Canvas lift = display.GetComponent<Canvas>();
                    log.IsTrue(lift != null && rig.Get<bool>("mediaLiftedOverPresenter") && lift.sortingOrder == StageMath.CoverSortingOrder,
                        $"cover: {what} comes in on a sorting order above his own ({StageMath.CoverSortingOrder}) — it covers him, he is not drawn over it");
                    log.IsTrue(StageMath.CoverSortingOrder > 0 && StageMath.CoverSortingOrder < StageMath.PeekSortingOrder, "cover: …still under the cards, a peek and everything else on the stage");
                    rig.Set("isShowingMedia", true);
                    rig.Call("StepCoveredExit");
                    log.IsTrue(!rig.Mps.IsCoveredExitPending && OutsideFrame(rig.Presenter.position), $"cover: once {what} stands on the frame he is gone, behind it");
                    rig.Call("ResetMediaSlot");
                    rig.Set("isShowingMedia", false);
                    log.IsTrue(!rig.Get<bool>("mediaLiftedOverPresenter"), "cover: when that picture has gone the display is back on the canvas's own order");
                }

                // Not when nothing will cover him: an X post floats on the backdrop, a later clip is another beat, an End is no picture.
                foreach (var (list, what) in new[] { (new List<MediaMarkerData> { post }, "an X post"), (new List<MediaMarkerData> { later }, "a clip three seconds later"),
                                                     (new List<MediaMarkerData> { end }, "a {Video:End}"), (new List<MediaMarkerData>(), "nothing") })
                {
                    AtCenter();
                    rig.Set("mediaMarkers", list);
                    rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                    log.IsTrue(!rig.Mps.IsCoveredExitPending && OutsideFrame(rig.Presenter.position), $"cover: with {what} on the beat the cut is on the tag, as before");
                }
                // Not from a peek (he is drawn in front of the media), and not under a transition's cover.
                rig.Set("mediaMarkers", new List<MediaMarkerData> { clip });
                rig.Set("currentPosition", CharacterPosition.Off);
                rig.Call("MoveToPosition", CharacterPosition.PeekRight, (bool?)true);
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                log.IsTrue(!rig.Mps.IsCoveredExitPending && OutsideFrame(rig.Presenter.position), "cover: from a peek the cut is on the tag (a peek is in front of the picture)");
                AtCenter();
                rig.Set("underTransitionCover", true);
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                rig.Set("underTransitionCover", false);
                log.IsTrue(!rig.Mps.IsCoveredExitPending && OutsideFrame(rig.Presenter.position), "cover: on a {Transition} line the cut is under its cover, as before");
                // A smooth {Position:Off} is not a cut: he slides out as always.
                AtCenter();
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)false);
                log.IsTrue(!rig.Mps.IsCoveredExitPending && rig.Get<bool>("stageMoving"), "cover: a smooth {Position:Off} still slides him out");
                rig.Call("FinishStageTravelNow");

                // Told to come back before the picture landed: he is put off first, then the move is planned from there.
                AtCenter();
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                log.IsTrue(rig.Mps.IsCoveredExitPending, "cover: (waiting again)");
                rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
                log.IsTrue(!rig.Mps.IsCoveredExitPending && ((Vector2)rig.Presenter.position - (Vector2)rig.Left.position).magnitude < 1e-4f,
                    "cover: another {Position:…} before it landed ends the wait — he is where that tag puts him");
                rig.Set("mediaMarkers", new List<MediaMarkerData>());
                rig.Set("currentPosition", CharacterPosition.Left);
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);   // (the stage wide again for what follows)
            }

            // A picture that nothing is waiting for is not lifted.
            Show("selftest_photo", Tex(96, 54, null), 3f);
            log.IsTrue(!rig.Get<bool>("mediaLiftedOverPresenter"), "cover: an image with no cut waiting for it keeps the canvas's own order");

            // When the media has gone: the display's own scale, the authored slot, no cut-out left behind.
            Show("x_post_selftest_wide", wide, 5f);
            rig.Call("StepMediaMotion", 5f);
            log.IsTrue(displayRt.localScale.x > 1.02f && MediaDisplayShadow.CutoutShownWhole == wide, "shots rig: (a whole post is up again, grown)");
            rig.Call("ResetMediaSlot");
            log.IsTrue(displayRt.localScale == Vector3.one && MediaDisplayShadow.CutoutShownWhole == null && displayRt.sizeDelta == new Vector2(800f, 640f) &&
                       displayRt.anchorMin == new Vector2(0.25f, 0.5f) && !rig.Get<bool>("mediaIsFullFrame"),
                "shots rig: after the media has gone the display is the authored side slot again, at its own scale");
        }
        finally
        {
            StagePrefs.PushIn = savedPush;
            MediaDisplayShadow.CutoutShownWhole = null;
            foreach (Texture2D t in made) UnityEngine.Object.DestroyImmediate(t);
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }

    static void BigImage_ScrollAndFullWidth_Engine(Log log)
    {
        // A procedural page five times as tall as the frame is wide-fitted: it must pan, never crop.
        var tall = new Texture2D(64, 320, TextureFormat.RGBA32, false);
        var squat = new Texture2D(320, 64, TextureFormat.RGBA32, false);
        try
        {
            foreach (bool scroll in new[] { true, false })
            {
                var root = new GameObject("BigImageSelfTest", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
                    frame.SetParent(root.transform, false);
                    frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
                    frame.sizeDelta = new Vector2(1920f, 1080f);
                    var go = new GameObject("BigImageCard", typeof(RectTransform), typeof(CanvasGroup));
                    go.transform.SetParent(frame, false);
                    var card = go.AddComponent<BigImageCard>();
                    card.EnsureBuilt();
                    card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigImage, primaryText = "no_such_image", duration = 8f, scroll = scroll }, null);
                    log.IsTrue(!card.IsFullWidth, "BigImage: with Mugs on screen it keeps the left three quarters");

                    // Give it the test page directly (the name above doesn't resolve).
                    var raw = card.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                    raw.enabled = true;
                    raw.texture = tall;
                    card.Advance(0f);
                    if (scroll)
                    {
                        log.IsTrue(card.IsScrolling, "BigImage ,Scroll: a tall page scrolls");
                        Rect top = card.UvRect;
                        log.Near(1f, top.yMax, 1e-3f, "BigImage ,Scroll: it opens on the page's top");
                        log.IsTrue(Mathf.Approximately(top.width, 1f), "BigImage ,Scroll: fitted to the slot's width");
                        card.Advance(4f);
                        Rect mid = card.UvRect;
                        log.IsTrue(mid.yMax < top.yMax && mid.yMin > 0f, "BigImage ,Scroll: halfway through it is halfway down");
                        card.Advance(4.1f);
                        log.Near(0f, card.UvRect.yMin, 1e-3f, "BigImage ,Scroll: it reaches the bottom exactly as its time is up");
                    }
                    else
                    {
                        log.IsTrue(!card.IsScrolling, "BigImage: without ,Scroll it is cover-cropped and still, as before");
                        Rect uv = card.UvRect;
                        card.Advance(4f);
                        log.IsTrue(card.UvRect == uv, "BigImage: …no push-in on the classic left-3/4 card");
                    }

                    if (scroll)
                    {
                        // An image not taller than its slot at that width: shown as before, one warning.
                        List<string> w = WarningsFrom(() =>
                        {
                            raw.texture = squat;
                            card.Advance(0.1f);
                        });
                        log.IsTrue(!card.IsScrolling, "BigImage ,Scroll: an image no taller than its slot doesn't scroll");
                        log.AreEqual(1, w.FindAll(m => m.Contains("not taller than")).Count, "BigImage ,Scroll: …with one warning");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(tall);
            UnityEngine.Object.DestroyImmediate(squat);
        }
    }
}

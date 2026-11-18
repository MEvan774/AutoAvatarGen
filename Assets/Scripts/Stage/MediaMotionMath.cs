using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// How a full-frame image moves while it is up: the slow push-in (an image
    /// filling the frame is never perfectly still) and the ,Scroll pan of a tall
    /// page. Both are drawn by changing the RawImage's uvRect — the slot itself
    /// never moves, so the image can never show its own edge. Pure.
    ///
    /// UV rects are Unity's: x/y from the image's bottom-left, 0–1.
    /// </summary>
    public static class MediaMotionMath
    {
        // ---- Cover --------------------------------------------------------------------

        /// <summary>
        /// The centred UV window whose aspect matches the box, so the image
        /// fills it edge to edge without distortion (the overflow is cropped).
        /// Aspects are width ÷ height.
        /// </summary>
        public static Rect CoverUv(float imageAspect, float boxAspect)
        {
            if (imageAspect <= 0f || boxAspect <= 0f) return new Rect(0f, 0f, 1f, 1f);
            if (imageAspect > boxAspect)
            {
                float w = boxAspect / imageAspect;              // wider than the box: crop the sides
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = imageAspect / boxAspect;                  // taller: crop top and bottom
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        // ---- Push-in ------------------------------------------------------------------

        /// <summary>A side-slot or frame-filling {Image:} pushes in to this scale at most.</summary>
        public const float PushInMax = 1.08f;
        /// <summary>A {BigImage:} to this.</summary>
        public const float BigImagePushInMax = 1.10f;
        /// <summary>A held image doesn't know its length: it pushes in this much per second, up to its maximum.</summary>
        public const float HeldPushPerSecond = 0.012f;
        /// <summary>An article screenshot is framed on masthead + headline: aim there (x, y from the TOP, 0–1).</summary>
        public static readonly Vector2 ArticleFocus = new Vector2(0.5f, 0.33f);
        public static readonly Vector2 CentreFocus = new Vector2(0.5f, 0.5f);

        /// <summary>Where an image is aimed: the headline third of an <c>article_…</c> capture, else its centre.</summary>
        public static Vector2 FocusFor(string mediaName) =>
            !string.IsNullOrEmpty(mediaName) && mediaName.StartsWith("article_", System.StringComparison.OrdinalIgnoreCase)
                ? ArticleFocus : CentreFocus;

        /// <summary>
        /// The push-in scale <paramref name="t"/> s after the image appeared.
        /// <paramref name="duration"/> &gt; 0: a timed image, eased over its whole
        /// time on screen. Otherwise (held): a fixed slow rate, capped.
        /// </summary>
        public static float PushScaleAt(float t, float duration, float max)
        {
            if (max <= 1f || t <= 0f) return 1f;
            if (duration > 0f)
            {
                float x = Mathf.Clamp01(t / duration);
                x = x * x * (3f - 2f * x);
                return 1f + (max - 1f) * x;
            }
            return Mathf.Min(max, 1f + HeldPushPerSecond * t);
        }

        /// <summary>
        /// The UV window of an image shown at <paramref name="scale"/> inside its
        /// cover window, aimed at <paramref name="focusFromTop"/> (0–1 of the
        /// WHOLE image, y down) — clamped so it never leaves the cover window.
        /// </summary>
        public static Rect PushUv(Rect cover, float scale, Vector2 focusFromTop)
        {
            if (scale <= 1f) return cover;
            float w = cover.width / scale, h = cover.height / scale;
            float fx = Mathf.Clamp(focusFromTop.x, cover.xMin, cover.xMax);
            float fy = Mathf.Clamp(1f - focusFromTop.y, cover.yMin, cover.yMax);
            // Zoom about the focus: it keeps its place in the window as the window shrinks.
            float u = cover.width > 0f ? (fx - cover.xMin) / cover.width : 0.5f;
            float v = cover.height > 0f ? (fy - cover.yMin) / cover.height : 0.5f;
            float x = Mathf.Clamp(fx - u * w, cover.xMin, cover.xMax - w);
            float y = Mathf.Clamp(fy - v * h, cover.yMin, cover.yMax - h);
            return new Rect(x, y, w, h);
        }

        // ---- Scroll -------------------------------------------------------------------

        /// <summary>A held scrolling image moves this share of the SLOT's height per second.</summary>
        public const float HeldScrollPerSecond = 0.06f;

        /// <summary>
        /// The share of the image's height that shows when it is fitted to the
        /// slot's width. 1 or more: the image is not taller than its slot at
        /// that width — nothing to scroll.
        /// </summary>
        public static float VisibleShare(float slotWidth, float slotHeight, float imageWidth, float imageHeight)
        {
            if (slotWidth <= 0f || slotHeight <= 0f || imageWidth <= 0f || imageHeight <= 0f) return 1f;
            return (slotHeight / slotWidth) / (imageHeight / imageWidth);
        }

        /// <summary>True when the image is taller than its slot at the slot's width (so it can scroll).</summary>
        public static bool CanScroll(float visibleShare) => visibleShare < 0.999f;

        /// <summary>
        /// How far down the page the window has travelled, 0 (top) → 1 (bottom),
        /// <paramref name="t"/> s after it appeared. Timed: the whole distance in
        /// exactly <paramref name="duration"/> s, eased in and out. Held
        /// (<paramref name="duration"/> ≤ 0): the fixed slow rate, then resting
        /// at the bottom.
        /// </summary>
        public static float ScrollProgress(float t, float duration, float visibleShare)
        {
            if (!CanScroll(visibleShare) || t <= 0f) return 0f;
            if (duration > 0f)
            {
                float x = Mathf.Clamp01(t / duration);
                return x * x * (3f - 2f * x);
            }
            float travel = 1f - visibleShare;                       // in image heights
            float perSecond = HeldScrollPerSecond * visibleShare;   // slot heights → image heights
            return travel > 0f ? Mathf.Clamp01(perSecond * t / travel) : 0f;
        }

        /// <summary>The UV window of a page fitted to the slot's width, <paramref name="progress"/> of the way down.</summary>
        public static Rect ScrollUv(float visibleShare, float progress)
        {
            if (!CanScroll(visibleShare)) return new Rect(0f, 0f, 1f, 1f);
            float travel = 1f - visibleShare;
            float top = Mathf.Clamp01(progress) * travel;            // from the image's top
            return new Rect(0f, 1f - visibleShare - top, 1f, visibleShare);
        }

        /// <summary>A full-frame image replaced sooner than this was too short to read (warned, still shown).</summary>
        public const float MinRunImageSeconds = 0.6f;

        // ---- Screenshots at full frame --------------------------------------------------
        //
        // A photo fills the frame from its centre and nothing is lost. A
        // screenshot is text: cropped from the centre an article capture loses
        // its masthead, and an X post (a see-through cut-out of any shape) loses
        // its top and bottom — a tall one shows only its middle. So, at full
        // frame only (the side slot and {BigImage} are as they were):
        //   an article_… capture still fills the frame, but STARTS ON ITS TOP
        //   (masthead, headline) and then scrolls down slowly, showing the rest;
        //   an x_post_… is shown whole, on the backdrop — or, when it is too
        //   tall to be readable whole, at a readable width, scrolling.
        // The creator chose both from mock-ups of their own screenshots on
        // 2026-11-18 (video_output/FULLFRAME_SCREENSHOT_OPTIONS_2026-11-18.png),
        // and asked for the article's scroll after the first take: held on its
        // top, a quarter of every 4:3 capture never showed.

        public enum ShotKind { Photo, Article, Post }

        public const string ArticlePrefix = "article_";
        public const string PostPrefix = "x_post_";

        /// <summary>
        /// What an image is, by its name: an <c>article_…</c> capture, an
        /// <c>x_post_…</c> — or anything found in the posts folder — else a
        /// photo. No tag keyword: the media library's names already say it.
        /// </summary>
        public static ShotKind KindFor(string mediaName, string foundInFolder = null, string postsFolder = null)
        {
            if (string.IsNullOrEmpty(mediaName)) return ShotKind.Photo;
            if (mediaName.StartsWith(ArticlePrefix, System.StringComparison.OrdinalIgnoreCase)) return ShotKind.Article;
            if (mediaName.StartsWith(PostPrefix, System.StringComparison.OrdinalIgnoreCase)) return ShotKind.Post;
            if (!string.IsNullOrEmpty(foundInFolder) && !string.IsNullOrEmpty(postsFolder) &&
                string.Equals(foundInFolder, postsFolder, System.StringComparison.OrdinalIgnoreCase)) return ShotKind.Post;
            return ShotKind.Photo;
        }

        // -- An article capture --

        /// <summary>
        /// A full-frame article pushes in far less than a photo: its sides are
        /// text too. 2 % takes 1 % off each side — on the creator's captures
        /// the mastheads come within 1.7–2 % of the page's edge (MacRumors,
        /// CoinDesk), and at 4 % the first take clipped the MacRumors logo.
        /// </summary>
        public const float ArticlePushInMax = 1.02f;
        /// <summary>…and about the middle of its TOP edge, so the masthead never leaves the frame.</summary>
        public static readonly Vector2 ArticleFrameFocus = new Vector2(0.5f, 0f);

        /// <summary>
        /// <see cref="CoverUv"/> for a page: when the image is taller than the
        /// box, all of the crop comes off the BOTTOM — the top (masthead,
        /// headline) is always in.
        /// </summary>
        public static Rect CoverUvKeepTop(float imageAspect, float boxAspect)
        {
            Rect cover = CoverUv(imageAspect, boxAspect);
            if (cover.height < 1f) cover.y = 1f - cover.height;
            return cover;
        }

        /// <summary>A full-frame article rests on its top this long before it starts down: time to read the masthead and the headline.</summary>
        public const float ArticleTopHoldSeconds = 1.2f;
        /// <summary>…and takes this long to reach its (slow) speed, and as long to stop at the bottom: no jerk either end.</summary>
        public const float ArticleScrollRampSeconds = 0.8f;

        /// <summary>
        /// How far a page fitted to the frame's width has to travel to show its
        /// bottom, in frame heights (0 when it isn't taller than the frame).
        /// <paramref name="visibleShare"/>: <see cref="VisibleShare"/>.
        /// </summary>
        public static float PageTravelFrames(float visibleShare) =>
            CanScroll(visibleShare) ? (1f - visibleShare) / visibleShare : 0f;

        /// <summary>
        /// How far down a full-frame article is, 0 (its top) → 1 (its bottom),
        /// <paramref name="t"/> s after it appeared: it rests on its top for
        /// <see cref="ArticleTopHoldSeconds"/>, eases up to the page's calm
        /// rate (<see cref="HeldScrollPerSecond"/> frame heights a second —
        /// "slowly": timed or held, it never hurries to reach the bottom),
        /// eases to a stop at the bottom and rests there.
        /// </summary>
        public static float ArticleScrollProgress(float t, float travelFrames)
        {
            if (travelFrames <= 0f) return 0f;
            float tau = t - ArticleTopHoldSeconds;
            if (tau <= 0f) return 0f;

            float v = HeldScrollPerSecond, ramp = ArticleScrollRampSeconds;
            // A trapezoid of speed: up over `ramp`, steady, down over `ramp`. A page with
            // very little to show never reaches the steady speed (a triangle).
            float rampTravel = v * ramp;                       // covered by the two ramps together
            float peak = v, each = ramp;
            if (travelFrames < rampTravel)
            {
                each = Mathf.Sqrt(travelFrames * ramp / v);
                peak = v * each / ramp;
            }
            float steady = Mathf.Max(0f, (travelFrames - peak * each) / peak);
            float total = 2f * each + steady;
            if (tau >= total) return 1f;

            float done;
            if (tau < each) done = 0.5f * peak * tau * tau / each;
            else if (tau < each + steady) done = 0.5f * peak * each + peak * (tau - each);
            else
            {
                float left = total - tau;
                done = travelFrames - 0.5f * peak * left * left / each;
            }
            return Mathf.Clamp01(done / travelFrames);
        }

        /// <summary>How long after it appears a full-frame article has reached its bottom.</summary>
        public static float ArticleScrollSeconds(float travelFrames)
        {
            if (travelFrames <= 0f) return 0f;
            float v = HeldScrollPerSecond, ramp = ArticleScrollRampSeconds;
            float each = travelFrames < v * ramp ? Mathf.Sqrt(travelFrames * ramp / v) : ramp;
            float peak = v * each / ramp;
            return ArticleTopHoldSeconds + 2f * each + Mathf.Max(0f, (travelFrames - peak * each) / peak);
        }

        // -- An X post --

        /// <summary>A post shown whole takes at most this much of the frame's width…</summary>
        public const float PostMaxWidth = 0.88f;
        /// <summary>…and of its height.</summary>
        public const float PostMaxHeight = 0.86f;
        /// <summary>A post is readable when it is this much of the frame's width (or wider).</summary>
        public const float PostReadableWidth = 0.50f;
        /// <summary>It scrolls only when, shown whole, it would be smaller than this share of readable.</summary>
        public const float PostScrollBelow = 0.90f;
        /// <summary>A scrolling post starts with this much of the frame's height above its top, and ends with as much below its bottom.</summary>
        public const float PostMargin = 0.07f;
        /// <summary>A small capture is never blown up by more than this.</summary>
        public const float PostMaxUpscale = 1.25f;
        /// <summary>A post shown whole grows this much while it is up (the whole card: nothing is cropped).</summary>
        public const float PostPushInMax = 1.03f;
        /// <summary>A timed post that scrolls faster than this (frame heights per second, on average) can't be read: warned.</summary>
        public const float PostReadableScrollPerSecond = 0.12f;
        /// <summary>A pixel with more alpha than this is part of the post.</summary>
        public const byte PostAlphaFloor = 16;

        /// <summary>How a post sits on the frame.</summary>
        public struct PostLayout
        {
            /// <summary>The display's size, in the frame's units; it is centred.</summary>
            public Vector2 SlotSize;
            /// <summary>The window of the image it shows (at the start, for a post that scrolls).</summary>
            public Rect Uv;
            /// <summary>True when it is too tall to read whole: readable width, the window travels down.</summary>
            public bool Scrolls;
            /// <summary>How far the window travels down, in UV (the image's heights).</summary>
            public float UvTravel;
            /// <summary>The same in frame heights (what the viewer sees move).</summary>
            public float TravelFrames;
            /// <summary>Frame units per image pixel.</summary>
            public float Scale;
        }

        /// <summary>
        /// Lays an X post out on a frame of <paramref name="frameW"/> ×
        /// <paramref name="frameH"/>. <paramref name="bounds"/> is the post
        /// itself inside its image — the part that isn't see-through — in
        /// pixels, x and y from the image's TOP-left (<see cref="OpaqueBounds"/>).
        ///   Whole: as large as fits <see cref="PostMaxWidth"/> ×
        ///   <see cref="PostMaxHeight"/>; the display is the post's own rectangle.
        ///   Too tall for that to be readable: <see cref="PostReadableWidth"/>
        ///   wide, in a column the height of the frame, and the window travels
        ///   from just above the post's top to just below its bottom.
        /// </summary>
        public static PostLayout LayoutPost(float frameW, float frameH, int imageW, int imageH, RectInt bounds)
        {
            if (imageW <= 0 || imageH <= 0 || frameW <= 0f || frameH <= 0f)
                return new PostLayout { SlotSize = new Vector2(frameW, frameH), Uv = new Rect(0f, 0f, 1f, 1f), Scale = 1f };
            if (bounds.width <= 0 || bounds.height <= 0) bounds = new RectInt(0, 0, imageW, imageH);

            float bw = bounds.width, bh = bounds.height;
            float fit = Mathf.Min(Mathf.Min(PostMaxWidth * frameW / bw, PostMaxHeight * frameH / bh), PostMaxUpscale);
            float readable = Mathf.Min(PostReadableWidth * frameW / bw, PostMaxUpscale);

            if (fit < readable * PostScrollBelow)
            {
                float s = readable;
                float window = frameH / s;                       // the image rows a frame holds
                float margin = PostMargin * frameH / s;
                float last = imageH - window;                    // the lowest the window's top may be
                if (last > 0f)
                {
                    float start = Mathf.Clamp(bounds.y - margin, 0f, last);
                    float end = Mathf.Clamp(bounds.y + bh + margin - window, 0f, last);
                    if (end > start + 1f)
                        return new PostLayout
                        {
                            SlotSize = new Vector2(bw * s, frameH),
                            Uv = new Rect(bounds.x / (float)imageW, 1f - (start + window) / imageH, bw / imageW, window / imageH),
                            Scrolls = true,
                            UvTravel = (end - start) / imageH,
                            TravelFrames = (end - start) * s / frameH,
                            Scale = s,
                        };
                }
            }

            return new PostLayout
            {
                SlotSize = new Vector2(bw * fit, bh * fit),
                Uv = new Rect(bounds.x / (float)imageW, 1f - (bounds.y + bh) / imageH, bw / imageW, bh / imageH),
                Scale = fit,
            };
        }

        /// <summary>
        /// How far down a scrolling post is, 0 → 1, <paramref name="t"/> s
        /// after it appeared. Timed: all the way in exactly its time, eased at
        /// both ends. Held: the page's fixed slow rate, then resting at the end.
        /// </summary>
        public static float PostScrollProgress(float t, float duration, float travelFrames)
        {
            if (travelFrames <= 0f || t <= 0f) return 0f;
            if (duration > 0f)
            {
                float x = Mathf.Clamp01(t / duration);
                return x * x * (3f - 2f * x);
            }
            return Mathf.Clamp01(HeldScrollPerSecond * t / travelFrames);
        }

        /// <summary>The window of a scrolling post <paramref name="progress"/> of the way down.</summary>
        public static Rect PostScrollUv(PostLayout layout, float progress) =>
            new Rect(layout.Uv.x, layout.Uv.y - layout.UvTravel * Mathf.Clamp01(progress), layout.Uv.width, layout.Uv.height);

        /// <summary>The shortest a timed scrolling post can be up and still be read.</summary>
        public static float PostSecondsNeeded(float travelFrames) =>
            travelFrames > 0f ? travelFrames / PostReadableScrollPerSecond : 0f;

        /// <summary>
        /// The rectangle of an image that isn't see-through, in pixels, x and y
        /// from its TOP-left. <paramref name="pixels"/> in Unity's order (rows
        /// from the bottom). Every <paramref name="step"/>th row and column is
        /// read and the result grown by what that could have missed. An image
        /// with nothing see-through around it — or nothing at all in it —
        /// gives its whole rectangle.
        /// </summary>
        public static RectInt OpaqueBounds(Color32[] pixels, int width, int height, int step = 2)
        {
            var whole = new RectInt(0, 0, Mathf.Max(0, width), Mathf.Max(0, height));
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height) return whole;
            step = Mathf.Max(1, step);

            int minX = width, maxX = -1, minY = height, maxY = -1;
            for (int y = 0; y < height; y += step)
            {
                int row = y * width;
                bool any = false;
                for (int x = 0; x < width; x += step)
                {
                    if (pixels[row + x].a <= PostAlphaFloor) continue;
                    any = true;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
                if (!any) continue;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0) return whole;

            minX = Mathf.Max(0, minX - (step - 1));
            maxX = Mathf.Min(width - 1, maxX + (step - 1));
            minY = Mathf.Max(0, minY - (step - 1));
            maxY = Mathf.Min(height - 1, maxY + (step - 1));
            // (rows counted from the bottom → y from the top)
            return new RectInt(minX, height - 1 - maxY, maxX - minX + 1, maxY - minY + 1);
        }
    }

    /// <summary>
    /// A wide Quote's words arrive as they are spoken: its characters appear in
    /// order, spread over the time the card is up. Pure.
    /// </summary>
    public static class TypeOn
    {
        /// <summary>A held quote types between its Start and this long before its End…</summary>
        public const float EndLeadSeconds = 0.3f;
        /// <summary>…at no less and no more than these rates (characters per second).</summary>
        public const float MinCps = 12f, MaxCps = 30f;
        /// <summary>A timed quote (or a held one whose End isn't known) types at speaking pace.</summary>
        public const float TimedCps = 16f;

        /// <summary>
        /// The seconds a held quote has to type in: from when it shows to
        /// <see cref="EndLeadSeconds"/> before its End. NaN when either time is unknown.
        /// </summary>
        public static float Window(float shownAt, float endAt)
        {
            if (float.IsNaN(shownAt) || float.IsNaN(endAt)) return float.NaN;
            return endAt - EndLeadSeconds - shownAt;
        }

        /// <summary>
        /// The typing rate: <paramref name="characters"/> spread evenly over
        /// <paramref name="window"/> seconds, clamped to 12–30 a second;
        /// 16 a second when there is no window (a timed quote).
        /// </summary>
        public static float Rate(int characters, float window)
        {
            if (float.IsNaN(window)) return TimedCps;
            if (window <= 0.01f) return MaxCps;
            return Mathf.Clamp(characters / window, MinCps, MaxCps);
        }

        /// <summary>
        /// How many characters show <paramref name="t"/> s after the card
        /// appeared. With a window, the whole quote is on screen by its end —
        /// if the 30-a-second cap couldn't get there, the rest lands then
        /// (the last character is always up before the End).
        /// </summary>
        public static int VisibleAt(float t, int characters, float window)
        {
            if (characters <= 0) return 0;
            if (t <= 0f) return 0;
            if (!float.IsNaN(window) && t >= Mathf.Max(0f, window)) return characters;
            return Mathf.Clamp(Mathf.FloorToInt(t * Rate(characters, window)) + 1, 0, characters);
        }

        /// <summary>When the last character appears, in seconds after the card showed.</summary>
        public static float DoneAt(int characters, float window)
        {
            if (characters <= 0) return 0f;
            float byRate = (characters - 1) / Rate(characters, window);
            return float.IsNaN(window) ? byRate : Mathf.Min(byRate, Mathf.Max(0f, window));
        }
    }

    /// <summary>The stage's main-menu switches (PlayerPrefs, like the other main-menu fields).</summary>
    public static class StagePrefs
    {
        public const string PushInKey = "AutoAvatarGen.FullFramePushIn";
        public const string TypeQuotesKey = "AutoAvatarGen.TypeQuotesOn";
        public const string SideMoveKey = "AutoAvatarGen.SideMove";

        /// <summary>"Push in on full-frame images" (default on). Off: a full-frame image holds still.</summary>
        public static bool PushIn
        {
            get => PlayerPrefs.GetInt(PushInKey, 1) != 0;
            set { PlayerPrefs.SetInt(PushInKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// "Side move" (default Glide): how Mugs gets between Left, Right and
        /// Center. Glide is the move every existing video was made with — one
        /// eased second and a mirror flip halfway. Dash is the quick one
        /// (<see cref="DashMath"/>). A main-menu choice only: no tag names it.
        /// </summary>
        public static bool SideMoveIsDash
        {
            get => PlayerPrefs.GetInt(SideMoveKey, 0) == 1;
            set { PlayerPrefs.SetInt(SideMoveKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>"Type quotes on" (default on). Off: a wide quote shows whole, like a side quote.</summary>
        public static bool TypeQuotes
        {
            get => PlayerPrefs.GetInt(TypeQuotesKey, 1) != 0;
            set { PlayerPrefs.SetInt(TypeQuotesKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}

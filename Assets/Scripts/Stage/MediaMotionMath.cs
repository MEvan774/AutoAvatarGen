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

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// The evidence-first stage: Mugs can leave the frame ({Position:Off}) or
    /// show only his head over the bottom edge ({Position:PeekLeft/PeekRight}),
    /// and while he is away the side cards are drawn centred and wider and
    /// {Image:}/{Video:} fill the frame. Everything here is pure maths — where
    /// he goes, how he moves, how wide the stage is, how his screen time is
    /// counted — so the self-tests run it with no engine.
    ///
    /// World lengths are in world units of the take camera at rest; frame
    /// fractions are of the frame's width / height.
    /// </summary>
    public static class StageMath
    {
        // ---- Timing (seconds) --------------------------------------------------------
        /// <summary>He slides out of frame (ease-in).</summary>
        public const float SlideOutSeconds = 0.35f;
        /// <summary>He pops up from below the frame at a full position (small overshoot).</summary>
        public const float PopUpSeconds = 0.35f;
        /// <summary>His head rises for a peek (small overshoot).</summary>
        public const float PeekRiseSeconds = 0.25f;
        /// <summary>He drops back down from a peek.</summary>
        public const float PeekDropSeconds = 0.20f;
        /// <summary>An open card glides between its side slot and the wide slot.</summary>
        public const float CardGlideSeconds = 0.35f;

        // ---- The wide slot, as fractions of the frame --------------------------------
        /// <summary>About 75 % of the frame's width, centred…</summary>
        public const float WideXMin = 0.125f, WideXMax = 0.875f;
        /// <summary>…and 78 % of its height: clear of the top strip (chapter titles, the Echo: y &lt; 11 %).</summary>
        public const float WideYMin = 0.11f, WideYMax = 0.89f;

        /// <summary>A Stat alone in the wide slot: its capitals at most this share of the frame's height.</summary>
        public const float WideStatCapOfFrame = 0.27f;

        // ---- The peek ----------------------------------------------------------------
        /// <summary>He takes at most this share of the frame's width…</summary>
        public const float PeekMaxWidthOfFrame = 0.22f;
        /// <summary>…and of its height.</summary>
        public const float PeekMaxHeightOfFrame = 0.35f;
        /// <summary>Head and shoulders: this share of his sprite, from the top (his head is nearly half of him).</summary>
        public const float PeekSpriteShare = 0.58f;
        /// <summary>Gap between the frame's side edge and him.</summary>
        public const float PeekSideInsetOfFrame = 0.012f;
        /// <summary>His sprites' sorting order while peeking: in front of the cards (30000), behind the full-screen cards (31000).</summary>
        public const int PeekSortingOrder = 30500;

        // ---- Off -----------------------------------------------------------------------
        /// <summary>Clear space kept between him and the frame edge once he has slid out (shadow, sway, tail).</summary>
        public const float OffMargin = 1.2f;
        /// <summary>Where he waits while off: this far past the edge, out of reach of any camera move.</summary>
        public const float ParkDistance = 60f;

        /// <summary>He is off and nothing has been on screen for longer than this: a fault.</summary>
        public const float EmptyStageSeconds = 3f;

        public static bool IsFull(CharacterPosition p) =>
            p == CharacterPosition.Left || p == CharacterPosition.Right || p == CharacterPosition.Center;

        public static bool IsPeek(CharacterPosition p) =>
            p == CharacterPosition.PeekLeft || p == CharacterPosition.PeekRight;

        /// <summary>The stage is wide while he is off or only peeking.</summary>
        public static bool IsWide(CharacterPosition p) => p == CharacterPosition.Off || IsPeek(p);

        /// <summary>
        /// A position word from a {Position:…} tag. False for anything else
        /// (the caller keeps its old fallback, Center with a warning).
        /// </summary>
        public static bool TryParsePosition(string word, out CharacterPosition position)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "left": position = CharacterPosition.Left; return true;
                case "right": position = CharacterPosition.Right; return true;
                case "center": position = CharacterPosition.Center; return true;
                case "off": position = CharacterPosition.Off; return true;
                case "peekleft": position = CharacterPosition.PeekLeft; return true;
                case "peekright": position = CharacterPosition.PeekRight; return true;
                default: position = CharacterPosition.Center; return false;
            }
        }

        // ---- The moves ----------------------------------------------------------------

        /// <summary>
        /// What a stage move is made of, in order. Empty when there is nothing
        /// to do (the same position again) or when it isn't a stage move at all
        /// (Left ↔ Right ↔ Center keep their old glide).
        ///   leaving:  a full position slides out, a peek drops;
        ///   arriving: Off parks him out of reach, a peek rises, a full position pops up from below.
        /// </summary>
        public static List<StageLeg> PlanTravel(CharacterPosition from, CharacterPosition target)
        {
            var legs = new List<StageLeg>();
            if (from == target) return legs;
            if (IsFull(from) && IsFull(target)) return legs;

            if (IsPeek(from)) legs.Add(StageLeg.DropFromPeek);
            else if (IsFull(from)) legs.Add(StageLeg.SlideOut);

            if (target == CharacterPosition.Off) legs.Add(StageLeg.Park);
            else if (IsPeek(target)) legs.Add(StageLeg.RiseToPeek);
            else legs.Add(StageLeg.PopUp);
            return legs;
        }

        public static float LegSeconds(StageLeg leg)
        {
            switch (leg)
            {
                case StageLeg.SlideOut: return SlideOutSeconds;
                case StageLeg.DropFromPeek: return PeekDropSeconds;
                case StageLeg.RiseToPeek: return PeekRiseSeconds;
                case StageLeg.PopUp: return PopUpSeconds;
                default: return 0f;
            }
        }

        /// <summary>How long a (non-cut) stage move takes from tag to rest.</summary>
        public static float TravelSeconds(CharacterPosition from, CharacterPosition target)
        {
            float t = 0f;
            foreach (StageLeg leg in PlanTravel(from, target)) t += LegSeconds(leg);
            return t;
        }

        /// <summary>
        /// Cards and media wait for him to ARRIVE at Left / Right / Center (he
        /// would cross them), never for him to leave or to peek — a wide card
        /// may land while he is still sliding out.
        /// </summary>
        public static bool CardsWaitFor(CharacterPosition from, CharacterPosition target) =>
            from != target && IsFull(target);

        /// <summary>
        /// Whether a move ends whatever {Image:}/{Video:} is up: a change of
        /// side always did; so does the layout changing (wide ↔ side). Off ↔
        /// peek keeps the stage wide, and the media with it.
        /// </summary>
        public static bool MoveEndsMedia(CharacterPosition from, CharacterPosition target)
        {
            if (from == target) return false;
            if (IsWide(from) && IsWide(target)) return false;
            return true;
        }

        // ---- Easing --------------------------------------------------------------------

        /// <summary>Sliding out: slow off the mark, fastest as he leaves (ease-in cubic).</summary>
        public static float SlideOutEase(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x;
        }

        /// <summary>Popping up: fast, a small overshoot (about 5 % of the way), then settled.</summary>
        public static float PopEase(float x)
        {
            x = Mathf.Clamp01(x);
            const float s = 1.1f;
            float t = x - 1f;
            return 1f + (s + 1f) * t * t * t + s * t * t;
        }

        /// <summary>Dropping from a peek (ease-in).</summary>
        public static float DropEase(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x;
        }

        /// <summary>An open card gliding to the other slot (ease-in-out cubic).</summary>
        public static float GlideEase(float x)
        {
            x = Mathf.Clamp01(x);
            return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
        }

        // ---- Where he goes -------------------------------------------------------------

        /// <summary>The frame at rest: centre and half-extents in world units.</summary>
        public struct Frame
        {
            public Vector2 Centre;
            public float HalfWidth, HalfHeight;
            public float Left => Centre.x - HalfWidth;
            public float Right => Centre.x + HalfWidth;
            public float Bottom => Centre.y - HalfHeight;
            public float Top => Centre.y + HalfHeight;
            public float Width => 2f * HalfWidth;
            public float Height => 2f * HalfHeight;
        }

        /// <summary>
        /// His sprite relative to the transform that is moved: the sprite's
        /// centre offset from it and the sprite's size, at scale 1.
        /// </summary>
        public struct Body
        {
            public Vector2 CentreOffset;
            public Vector2 Size;
        }

        /// <summary>What he usually measures in the recording scene (used when no sprite can be read).</summary>
        public static Body DefaultBody => new Body { CentreOffset = new Vector2(0f, 2.63f), Size = new Vector2(4.6f, 6.1f) };

        /// <summary>
        /// Where the moved transform ends up once he has slid out from
        /// <paramref name="at"/>: off the left edge from Left, the right from
        /// Right, straight down from anywhere else. Just past the edge —
        /// <see cref="Park"/> then takes him out of reach.
        /// </summary>
        public static Vector2 SlideOutTarget(CharacterPosition from, Vector2 at, Body body, Frame frame, float scale = 1f)
        {
            float halfW = body.Size.x * 0.5f * scale, halfH = body.Size.y * 0.5f * scale;
            Vector2 centre = at + body.CentreOffset * scale;
            switch (from)
            {
                case CharacterPosition.Left:
                case CharacterPosition.PeekLeft:
                    return new Vector2(at.x - ((centre.x + halfW) - frame.Left) - OffMargin, at.y);
                case CharacterPosition.Right:
                case CharacterPosition.PeekRight:
                    return new Vector2(at.x + (frame.Right - (centre.x - halfW)) + OffMargin, at.y);
                default:
                    return new Vector2(at.x, at.y - ((centre.y + halfH) - frame.Bottom) - OffMargin);
            }
        }

        /// <summary>The same point, far out of reach (a pullback or a drift never brings him back into view).</summary>
        public static Vector2 Park(CharacterPosition from, Vector2 slideOutTarget)
        {
            switch (from)
            {
                case CharacterPosition.Left:
                case CharacterPosition.PeekLeft: return slideOutTarget + Vector2.left * ParkDistance;
                case CharacterPosition.Right:
                case CharacterPosition.PeekRight: return slideOutTarget + Vector2.right * ParkDistance;
                default: return slideOutTarget + Vector2.down * ParkDistance;
            }
        }

        /// <summary>Where a pop-up at <paramref name="rest"/> starts: the same x, wholly below the frame.</summary>
        public static Vector2 BelowFrame(Vector2 rest, Body body, Frame frame, float scale = 1f)
        {
            float top = rest.y + (body.CentreOffset.y + body.Size.y * 0.5f) * scale;
            return new Vector2(rest.x, rest.y - (top - frame.Bottom) - OffMargin);
        }

        /// <summary>A peek: where the moved transform rests, and how much he is scaled.</summary>
        public struct PeekPose
        {
            public Vector2 Rest;      // the moved transform, head and shoulders above the bottom edge
            public Vector2 Hidden;    // the same x, wholly below the frame
            public float Scale;       // ≤ 1
            public float VisibleWidth, VisibleHeight;   // what shows, in world units
        }

        /// <summary>
        /// The peek pose at the lower-left or lower-right corner: scaled down if
        /// he is wider than <see cref="PeekMaxWidthOfFrame"/>, the top
        /// <see cref="PeekSpriteShare"/> of him above the edge (never more than
        /// <see cref="PeekMaxHeightOfFrame"/> of the frame).
        /// </summary>
        public static PeekPose Peek(bool right, Body body, Frame frame)
        {
            float maxW = PeekMaxWidthOfFrame * frame.Width;
            float scale = body.Size.x > 0f ? Mathf.Min(1f, maxW / body.Size.x) : 1f;
            float w = body.Size.x * scale, h = body.Size.y * scale;
            float visible = Mathf.Min(PeekSpriteShare * h, PeekMaxHeightOfFrame * frame.Height);
            float inset = PeekSideInsetOfFrame * frame.Width;

            float centreX = right ? frame.Right - inset - w * 0.5f : frame.Left + inset + w * 0.5f;
            float topY = frame.Bottom + visible;
            var rest = new Vector2(centreX - body.CentreOffset.x * scale,
                                   topY - (body.CentreOffset.y + body.Size.y * 0.5f) * scale);
            return new PeekPose
            {
                Rest = rest,
                Hidden = new Vector2(rest.x, rest.y - visible - OffMargin),
                Scale = scale,
                VisibleWidth = w,
                VisibleHeight = visible,
            };
        }

        // ---- A rect moving between two slots ------------------------------------------

        /// <summary>
        /// The offsets (min, max) a child stretched over <paramref name="to"/>
        /// needs so that it covers <paramref name="from"/> instead — both rects
        /// in the same parent space. A glide tweens them to zero.
        /// </summary>
        public static void OffsetsToCover(Rect from, Rect to, out Vector2 offsetMin, out Vector2 offsetMax)
        {
            offsetMin = from.min - to.min;
            offsetMax = from.max - to.max;
        }

        /// <summary>The wide Stat's cap-height ceiling in card px: <see cref="WideStatCapOfFrame"/> of the frame.</summary>
        public static float WideStatCapCeiling(float frameHeightPx) => WideStatCapOfFrame * frameHeightPx;
    }

    /// <summary>One piece of a stage move (see <see cref="StageMath.PlanTravel"/>).</summary>
    public enum StageLeg
    {
        SlideOut,       // from Left / Right / Center: off the nearest edge
        DropFromPeek,   // from a peek: back down
        Park,           // off: out of reach of any camera move
        RiseToPeek,     // head and shoulders over the bottom edge
        PopUp,          // at Left / Right / Center, from below the frame
    }

    /// <summary>What the frame shows of Mugs at one moment.</summary>
    public enum MugsOnScreen
    {
        Full,      // at Left, Right or Center, in view
        Peek,      // only his head over the bottom edge
        Off,       // gone
        Covered,   // at a full position, but behind something opaque edge to edge ({Black}, a big headline, a clipping)
    }

    /// <summary>
    /// Counts Mugs's screen time over a take: seconds at a full position in
    /// view, seconds peeking, seconds off, seconds hidden behind a full-frame
    /// cover — and how many times he left. Pure.
    /// </summary>
    public sealed class StageMeter
    {
        public float Total { get; private set; }
        public float FullSeconds { get; private set; }
        public float PeekSeconds { get; private set; }
        public float OffSeconds { get; private set; }
        public float CoveredSeconds { get; private set; }
        /// <summary>Times he went off (from a full position or a peek; the take opening without him counts once).</summary>
        public int OffCount { get; private set; }
        public int PeekCount { get; private set; }

        MugsOnScreen last = MugsOnScreen.Full;
        bool any;

        public void Add(float dt, MugsOnScreen state)
        {
            if (!(dt > 0f)) dt = 0f;
            if (!any || state != last)
            {
                // Covered is the same place with something in front: not a new exit or peek.
                if (state == MugsOnScreen.Off && (!any || last != MugsOnScreen.Off)) OffCount++;
                if (state == MugsOnScreen.Peek && (!any || last != MugsOnScreen.Peek)) PeekCount++;
            }
            any = true;
            last = state;
            Total += dt;
            switch (state)
            {
                case MugsOnScreen.Full: FullSeconds += dt; break;
                case MugsOnScreen.Peek: PeekSeconds += dt; break;
                case MugsOnScreen.Off: OffSeconds += dt; break;
                default: CoveredSeconds += dt; break;
            }
        }

        /// <summary>His share of the take at a full position, in view (0–1).</summary>
        public float OnScreenShare => Total > 0f ? FullSeconds / Total : 0f;
        /// <summary>His share of the take peeking (0–1).</summary>
        public float PeekShare => Total > 0f ? PeekSeconds / Total : 0f;

        /// <summary>The take-end log line: "[Stage] Mugs on screen 41.2 % of 312.4 s (off 6 times)".</summary>
        public string Line()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string s = "[Stage] Mugs on screen " + (OnScreenShare * 100f).ToString("F1", inv) + " % of " +
                       Total.ToString("F1", inv) + " s (off " + OffCount + (OffCount == 1 ? " time" : " times");
            if (PeekSeconds > 0f || PeekCount > 0)
                s += ", peek " + (PeekShare * 100f).ToString("F1", inv) + " %";
            if (CoveredSeconds > 0f)
                s += ", behind a full-frame cover " + (CoveredSeconds / Mathf.Max(Total, 1e-6f) * 100f).ToString("F1", inv) + " %";
            return s + ")";
        }
    }

    /// <summary>
    /// Watches for the one fault of an empty stage: Mugs off and nothing on
    /// screen for more than <see cref="StageMath.EmptyStageSeconds"/>. One
    /// warning per stretch, given when the stretch ends (so it can say how long
    /// it was). Pure.
    /// </summary>
    public sealed class EmptyStageWatch
    {
        float since = float.NaN, length;

        public int Warnings { get; private set; }
        public float LongestSeconds { get; private set; }

        /// <summary>
        /// One frame at voice time <paramref name="t"/>. <paramref name="empty"/>:
        /// he is off and no card, media, black or full-screen text is up.
        /// Returns the warning to log when an over-long stretch has just ended, else null.
        /// </summary>
        public string Step(float t, float dt, bool empty)
        {
            if (empty)
            {
                if (float.IsNaN(since)) { since = t; length = 0f; }
                length += Mathf.Max(0f, dt);
                return null;
            }
            return Close();
        }

        /// <summary>The take is over: a stretch still running ends here.</summary>
        public string Finish() => Close();

        string Close()
        {
            if (float.IsNaN(since)) return null;
            float at = since, len = length;
            since = float.NaN;
            length = 0f;
            if (len <= StageMath.EmptyStageSeconds) return null;
            Warnings++;
            LongestSeconds = Mathf.Max(LongestSeconds, len);
            CultureInfo inv = CultureInfo.InvariantCulture;
            return "[Stage] t=" + at.ToString("F2", inv) + "s nothing on screen for " + len.ToString("F1", inv) +
                   " s while Mugs was off — put a card, a clip or an image on those lines, or bring him back.";
        }
    }

    /// <summary>This take's stage figures, for the result JSON (stays after the scene).</summary>
    public sealed class StageReport
    {
        public float Seconds;
        public float OnScreenShare, PeekShare, OffShare, CoveredShare;
        public int OffCount, PeekCount, EmptyStageWarnings, IgnoredZooms;
        public float LongestEmptySeconds;
        public bool OpenedWithoutMugs;

        public static StageReport Last { get; set; }
    }
}

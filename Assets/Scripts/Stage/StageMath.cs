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
        // How long each leg takes. What he does in that time is StageMoves' pose
        // for the leg. These are the CALM timings the creator chose from a
        // side-by-side preview on 2026-11-17 (the first Tier 5 values — 0.30 /
        // 0.32 / 0.22 / 0.18 — were "way too hyper" for Mugs; don't shorten them
        // without showing it first).
        /// <summary>He slides out of frame: slow off the mark, then away.</summary>
        public const float SlideOutSeconds = 0.60f;
        /// <summary>He pops up from below the frame at a full position: a little past his mark, a soft settle.</summary>
        public const float PopUpSeconds = 0.65f;
        /// <summary>His head rises for a peek (the pop-up, smaller).</summary>
        public const float PeekRiseSeconds = 0.40f;
        /// <summary>He drops back down from a peek (the slide-out, smaller).</summary>
        public const float PeekDropSeconds = 0.35f;
        /// <summary>He leans in over a side edge (the peek's rise, sideways)…</summary>
        public const float LeanInSeconds = 0.40f;
        /// <summary>…and pulls back out again (the peek's drop, sideways).</summary>
        public const float LeanOutSeconds = 0.35f;
        /// <summary>Only when a tag catches him still on his way out: from where he is, in view, back to a full position.</summary>
        public const float ToFullSeconds = 0.50f;
        /// <summary>The same, back into a peek or a lean.</summary>
        public const float ToPeekSeconds = 0.40f;
        /// <summary>The longest a (non-led) arrival at a full position takes: out of a peek or a lean, then the pop-up.</summary>
        public const float LongestArrivalSeconds = PeekDropSeconds + PopUpSeconds;
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

        // ---- The handover ({Position:…,Over}) -------------------------------------------
        /// <summary>
        /// His sprites' sorting order while he stands in front of what has the
        /// frame: above the full-screen cards (31000) and the {Name:} label
        /// (30600), below the {Pop:} phrase (31300), captions, Echo and black.
        /// </summary>
        public const int OverSortingOrder = 31200;
        /// <summary>The sheet that dims what is under him: above the cards, below him.</summary>
        public const int HandoverDimSortingOrder = 31100;
        /// <summary>What is under him is left at this brightness, so he reads against it.</summary>
        public const float HandoverBrightness = 0.60f;
        /// <summary>The dim goes with what is under him: as long as a card's own fade-out.</summary>
        public const float HandoverDimOutSeconds = 0.30f;

        /// <summary>
        /// Alpha of the black sheet that leaves the scene at <see cref="HandoverBrightness"/>
        /// in the encoded video (UI blends in linear light in linear colour space — as PopMath.DimAlpha).
        /// </summary>
        public static float HandoverDimAlpha(bool linearColorSpace) =>
            1f - (linearColorSpace ? Mathf.Pow(HandoverBrightness, 2.2f) : HandoverBrightness);

        /// <summary>
        /// Whether a ",Over" means anything: something has the frame, and he
        /// was away from it (off or peeking) — or is already standing in front
        /// of it. Otherwise he moves as he would without the word.
        /// </summary>
        public static bool CanHandOver(CharacterPosition from, bool somethingHasTheFrame, bool alreadyOver) =>
            somethingHasTheFrame && (IsWide(from) || alreadyOver);

        /// <summary>
        /// The sorting order his sprites take: in front of everything a card
        /// can be while he is over one, a peek's order while he peeks or rises,
        /// or null for the order the scene gave them.
        /// </summary>
        public static int? PresenterOrder(bool handover, bool peekLayer) =>
            handover ? OverSortingOrder : peekLayer ? PeekSortingOrder : (int?)null;

        /// <summary>
        /// The words after a {Position:Side…}: "Cut" / "Smooth", or "Over"
        /// (he comes up in front of what has the frame). A word after "Over"
        /// is not a form — it is ignored, with <paramref name="warning"/>.
        /// </summary>
        public static void ReadPositionWords(string cutOrSmooth, string overWord, string afterOver,
                                             out bool? cut, out bool over, out string warning)
        {
            cut = null;
            warning = null;
            if (!string.IsNullOrEmpty(cutOrSmooth))
            {
                string w = cutOrSmooth.ToLowerInvariant();
                if (w == "cut") cut = true;
                else if (w == "smooth") cut = false;
            }
            over = !string.IsNullOrEmpty(overWord);
            if (over && !string.IsNullOrEmpty(afterOver))
                warning = "\",Over," + afterOver + "\" is not a form — \"," + afterOver + "\" is ignored (a handover always uses his pop-up).";
        }

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

        /// <summary>A lean: his head and one shoulder in over the left or right edge.</summary>
        public static bool IsLean(CharacterPosition p) =>
            p == CharacterPosition.LeanLeft || p == CharacterPosition.LeanRight;

        /// <summary>One of his two small appearances — a peek or a lean: for one short line, over what has the frame.</summary>
        public static bool IsSmall(CharacterPosition p) => IsPeek(p) || IsLean(p);

        /// <summary>The stage is wide while he is off, only peeking or only leaning in.</summary>
        public static bool IsWide(CharacterPosition p) => p == CharacterPosition.Off || IsSmall(p);

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
                case "leanleft": position = CharacterPosition.LeanLeft; return true;
                case "leanright": position = CharacterPosition.LeanRight; return true;
                default: position = CharacterPosition.Center; return false;
            }
        }

        // ---- The moves ----------------------------------------------------------------

        /// <summary>
        /// What a stage move is made of, in order. Empty when there is nothing
        /// to do (the same position again) or when it isn't a stage move at all
        /// (Left ↔ Right ↔ Center keep their side move).
        /// He always leaves first (a full position slides out, a peek drops, a
        /// lean pulls out) and then arrives (Off parks him out of reach, a peek
        /// rises, a lean comes in, a full position pops up) — also between a
        /// peek or a lean and a full position: the creator watched a peek rise
        /// straight into Center and it "looks wrong"; the drop and pop-up stay.
        /// <paramref name="inView"/> matters only when <paramref name="from"/> is
        /// Off: a move that arrives while he is still on his way out starts from
        /// where he is instead of from below the frame.
        /// </summary>
        public static List<StageLeg> PlanTravel(CharacterPosition from, CharacterPosition target, bool inView = false)
        {
            var legs = new List<StageLeg>();
            if (from == target) return legs;
            if (IsFull(from) && IsFull(target)) return legs;

            if (from == CharacterPosition.Off)
            {
                if (IsPeek(target)) legs.Add(inView ? StageLeg.ToPeek : StageLeg.RiseToPeek);
                else if (IsLean(target)) legs.Add(inView ? StageLeg.ToLean : StageLeg.LeanIn);
                else legs.Add(inView ? StageLeg.ToFull : StageLeg.PopUp);
                return legs;
            }

            // Out, then in: between any two of a full position, a peek and a lean.
            legs.Add(IsPeek(from) ? StageLeg.DropFromPeek : IsLean(from) ? StageLeg.LeanOut : StageLeg.SlideOut);

            if (target == CharacterPosition.Off) legs.Add(StageLeg.Park);
            else if (IsPeek(target)) legs.Add(StageLeg.RiseToPeek);
            else if (IsLean(target)) legs.Add(StageLeg.LeanIn);
            else legs.Add(StageLeg.PopUp);
            return legs;
        }

        /// <summary>A leg that takes him out of sight: the slide-out, the peek's drop, the lean-out.</summary>
        public static bool IsLeaving(StageLeg leg) =>
            leg == StageLeg.SlideOut || leg == StageLeg.DropFromPeek || leg == StageLeg.LeanOut;

        /// <summary>
        /// What a move is made of when its tag arrives while he is still on
        /// his way OUT. <paramref name="leaving"/> is what he is leaving: the
        /// peek or the lean, or — for a slide-out — the full position nearest
        /// to where he is.
        ///   Back to that same place, while he can still be seen: he comes
        ///   back from where he is (one leg, eased at both ends).
        ///   Anywhere else: he FINISHES leaving (<paramref name="finishLeaving"/>;
        ///   the running leg carries on untouched) and then arrives as he
        ///   would from off stage — a peek never grows into an entrance and a
        ///   full position never sinks into a peek, which the creator found
        ///   "looks wrong". Already out of sight, the arrival starts at once.
        /// </summary>
        public static List<StageLeg> PlanWhileLeaving(CharacterPosition leaving, CharacterPosition target,
                                                      bool inView, out bool finishLeaving)
        {
            var legs = new List<StageLeg>();
            finishLeaving = false;
            if (inView && target == leaving && target != CharacterPosition.Off)
            {
                legs.Add(IsPeek(target) ? StageLeg.ToPeek : IsLean(target) ? StageLeg.ToLean : StageLeg.ToFull);
                return legs;
            }

            finishLeaving = inView;
            if (target == CharacterPosition.Off) legs.Add(StageLeg.Park);
            else if (IsPeek(target)) legs.Add(StageLeg.RiseToPeek);
            else if (IsLean(target)) legs.Add(StageLeg.LeanIn);
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
                case StageLeg.ToFull: return ToFullSeconds;
                case StageLeg.ToPeek: return ToPeekSeconds;
                case StageLeg.LeanIn: return LeanInSeconds;
                case StageLeg.LeanOut: return LeanOutSeconds;
                case StageLeg.ToLean: return ToPeekSeconds;
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

        // ---- Easing (his own moves are poses: see StageMoves) ----------------------------

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
                case CharacterPosition.LeanLeft:
                    return new Vector2(at.x - ((centre.x + halfW) - frame.Left) - OffMargin, at.y);
                case CharacterPosition.Right:
                case CharacterPosition.PeekRight:
                case CharacterPosition.LeanRight:
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
                case CharacterPosition.PeekLeft:
                case CharacterPosition.LeanLeft: return slideOutTarget + Vector2.left * ParkDistance;
                case CharacterPosition.Right:
                case CharacterPosition.PeekRight:
                case CharacterPosition.LeanRight: return slideOutTarget + Vector2.right * ParkDistance;
                default: return slideOutTarget + Vector2.down * ParkDistance;
            }
        }

        /// <summary>
        /// True while any part of him is inside the frame, with the moved
        /// transform at <paramref name="at"/> and him at <paramref name="scale"/>.
        /// A move that arrives while he is in view starts from where he is.
        /// </summary>
        public static bool InView(Vector2 at, Body body, Frame frame, float scale = 1f)
        {
            Vector2 centre = at + body.CentreOffset * scale;
            float halfW = body.Size.x * 0.5f * scale, halfH = body.Size.y * 0.5f * scale;
            return centre.x + halfW > frame.Left && centre.x - halfW < frame.Right &&
                   centre.y + halfH > frame.Bottom && centre.y - halfH < frame.Top;
        }

        /// <summary>
        /// The edge he leaves through when a move is cut short and he is
        /// between his marks: the nearest one — left or right from the outer
        /// parts of the frame, down from the middle. (At his marks this is the
        /// same answer as his position gives: Left, Right, Center.)
        /// </summary>
        public static CharacterPosition ExitFrom(float x, Frame frame)
        {
            float reach = frame.HalfWidth * 0.4f;
            if (x < frame.Centre.x - reach) return CharacterPosition.Left;
            if (x > frame.Centre.x + reach) return CharacterPosition.Right;
            return CharacterPosition.Center;
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

        // ---- The lean ----------------------------------------------------------------

        /// <summary>How far he tilts into the frame when he leans in over a side edge, in degrees.</summary>
        public const float LeanTilt = 35f;
        /// <summary>Where his head sits: this share of the frame's height above its middle (0 = mid-height).</summary>
        public const float LeanHeadAboveMiddle = -0.03f;
        /// <summary>
        /// Which part of him sits ON the frame edge at head height, across his
        /// outline from his back (0, the tail) to his front (1): everything in
        /// front of it is in the frame.
        /// </summary>
        public const float LeanCutAcross = 0.34f;

        // His outline as three boxes, in shares of it: across from his back (0)
        // to his front (1), down from the top (0) to his feet (1). Measured on
        // the Mugs sprites (the head is nearly half of him, the tail a third of
        // his width behind him). Only used to say how much of a lean shows.
        static readonly Rect LeanHeadBox = Rect.MinMaxRect(0.30f, 0.00f, 0.94f, 0.47f);
        static readonly Rect LeanTrunkBox = Rect.MinMaxRect(0.41f, 0.47f, 0.85f, 1.00f);
        static readonly Rect LeanTailBox = Rect.MinMaxRect(0.00f, 0.15f, 0.32f, 0.78f);
        /// <summary>The middle of his head, in the same shares.</summary>
        static readonly Vector2 LeanHeadCentre = new Vector2(0.62f, 0.24f);

        /// <summary>A lean: where the moved transform rests, how he is turned and scaled, and how much of him shows.</summary>
        public struct LeanPose
        {
            public Vector2 Rest;      // the moved transform, head and one shoulder inside the side edge
            public Vector2 Hidden;    // the same height, wholly outside that edge
            public float Scale;       // ≤ 1
            public float Tilt;        // degrees, counter-clockwise: + from the right edge, − from the left
            public float VisibleWidth, VisibleHeight;   // what shows, in world units
        }

        /// <summary>
        /// The lean pose at the left or right edge, at about mid-height:
        /// tilted <see cref="LeanTilt"/> into the frame about the moved
        /// transform, his head at mid-height, the frame edge through him at
        /// <see cref="LeanCutAcross"/>. Scaled down until what shows is inside
        /// the peek's limits (<see cref="PeekMaxWidthOfFrame"/>,
        /// <see cref="PeekMaxHeightOfFrame"/>). <paramref name="body"/> is his
        /// outline AS HE FACES INTO THE FRAME (to the right from the left
        /// edge), so his back — the tail — is outside.
        /// </summary>
        public static LeanPose Lean(bool right, Body body, Frame frame) =>
            Lean(right, body, frame, LeanTilt, LeanCutAcross, LeanHeadAboveMiddle);

        /// <summary><see cref="Lean(bool, Body, Frame)"/> with its three dials: the tilt, where the edge cuts him, how high his head is.</summary>
        public static LeanPose Lean(bool right, Body body, Frame frame, float tiltDegrees, float cutAcross, float headAboveMiddle)
        {
            float maxW = PeekMaxWidthOfFrame * frame.Width, maxH = PeekMaxHeightOfFrame * frame.Height;
            float scale = body.Size.x > 0f ? Mathf.Min(1f, maxW / body.Size.x) : 1f;
            float side = right ? -1f : 1f;                  // which way "into the frame" is
            float edge = right ? frame.Right : frame.Left;
            float tilt = (right ? 1f : -1f) * tiltDegrees;  // his head leans into the frame
            float rad = tilt * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

            // A point of his outline (shares across and down) relative to the moved transform, at scale 1, upright.
            Vector2 Local(float across, float down) => new Vector2(
                body.CentreOffset.x + side * (across - 0.5f) * body.Size.x,
                body.CentreOffset.y + (0.5f - down) * body.Size.y);
            Vector2 Turned(Vector2 v, float s) => new Vector2((v.x * cos - v.y * sin) * s, (v.x * sin + v.y * cos) * s);

            var pose = new LeanPose { Scale = scale, Tilt = tilt };
            for (int pass = 0; pass < 4; pass++)
            {
                Vector2 head = Turned(Local(LeanHeadCentre.x, LeanHeadCentre.y), pose.Scale);
                Vector2 cut = Turned(Local(cutAcross, LeanHeadCentre.y), pose.Scale);
                pose.Rest = new Vector2(edge - cut.x, frame.Centre.y + headAboveMiddle * frame.Height - head.y);

                // What shows: the part of the three boxes inside the frame.
                float inMost = 0f, top = float.NegativeInfinity, bottom = float.PositiveInfinity, outMost = 0f;
                foreach (Rect box in new[] { LeanHeadBox, LeanTrunkBox, LeanTailBox })
                    for (int i = 0; i <= 16; i++)
                        for (int j = 0; j <= 16; j++)
                        {
                            Vector2 w = pose.Rest + Turned(Local(Mathf.Lerp(box.xMin, box.xMax, i / 16f), Mathf.Lerp(box.yMin, box.yMax, j / 16f)), pose.Scale);
                            float inside = side * (w.x - edge);   // > 0: in the frame
                            outMost = Mathf.Max(outMost, -inside);
                            if (inside <= 0f || w.y < frame.Bottom || w.y > frame.Top) continue;
                            inMost = Mathf.Max(inMost, inside);
                            top = Mathf.Max(top, w.y);
                            bottom = Mathf.Min(bottom, w.y);
                        }
                pose.VisibleWidth = inMost;
                pose.VisibleHeight = top > bottom ? top - bottom : 0f;
                pose.Hidden = new Vector2(pose.Rest.x - side * (inMost + OffMargin), pose.Rest.y);

                float over = Mathf.Max(maxW > 0f ? pose.VisibleWidth / maxW : 0f, maxH > 0f ? pose.VisibleHeight / maxH : 0f);
                if (over <= 1f) break;
                pose.Scale /= over * 1.01f;
            }
            return pose;
        }

        /// <summary>His outline as it is once the sprite is mirrored (a lean and a peek face into the frame).</summary>
        public static Body Mirrored(Body body) =>
            new Body { CentreOffset = new Vector2(-body.CentreOffset.x, body.CentreOffset.y), Size = body.Size };

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
        ToFull,         // still on his way out, in view: back to Left / Right / Center from where he is
        ToPeek,         // …back into a peek
        LeanIn,         // head and one shoulder in over a side edge
        LeanOut,        // from a lean: back out through that edge
        ToLean,         // …back into a lean
    }

    /// <summary>What the frame shows of Mugs at one moment.</summary>
    public enum MugsOnScreen
    {
        Full,      // at Left, Right or Center, in view
        Peek,      // only his head over the bottom edge
        Lean,      // his head and one shoulder over a side edge (counted with the peek share)
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
        /// <summary>Times he leaned in over a side edge (their seconds are in <see cref="PeekSeconds"/>).</summary>
        public int LeanCount { get; private set; }

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
                if (state == MugsOnScreen.Lean && (!any || last != MugsOnScreen.Lean)) LeanCount++;
            }
            any = true;
            last = state;
            Total += dt;
            switch (state)
            {
                case MugsOnScreen.Full: FullSeconds += dt; break;
                case MugsOnScreen.Peek: PeekSeconds += dt; break;
                case MugsOnScreen.Lean: PeekSeconds += dt; break;   // a lean is a small appearance, like a peek
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
            if (PeekSeconds > 0f || PeekCount > 0 || LeanCount > 0)
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

    /// <summary>
    /// What waited for Mugs to land this take — every "Held … for the
    /// presenter to land" (a side card, an image, a clip). Pure counters;
    /// reset when a take begins.
    /// </summary>
    public static class PresenterHolds
    {
        public static int Count { get; private set; }
        public static float TotalSeconds { get; private set; }
        public static float LongestSeconds { get; private set; }

        public static void Reset()
        {
            Count = 0;
            TotalSeconds = 0f;
            LongestSeconds = 0f;
        }

        public static void Add(float seconds)
        {
            if (!(seconds > 0f)) return;
            Count++;
            TotalSeconds += seconds;
            if (seconds > LongestSeconds) LongestSeconds = seconds;
        }
    }

    /// <summary>This take's stage figures, for the result JSON (stays after the scene).</summary>
    public sealed class StageReport
    {
        public float Seconds;
        public float OnScreenShare, PeekShare, OffShare, CoveredShare;
        public int OffCount, PeekCount, LeanCount, EmptyStageWarnings, IgnoredZooms;
        public float LongestEmptySeconds;
        public bool OpenedWithoutMugs;
        /// <summary>"glide" or "dash": the main menu's Side move for this take.</summary>
        public string SideMove = "glide";
        /// <summary>Cards and media held for him to land: how many, how long in all, the longest.</summary>
        public int HoldCount;
        public float HoldSeconds, LongestHoldSeconds;
        /// <summary>Handovers ({Position:…,Over}): how many times he came up in front of what had the frame, and for how long he stood there.</summary>
        public int HandoverCount;
        public float HandoverSeconds;
        /// <summary>Arrive on the word: arrivals from off stage that started early, and those that could not take their whole lead.</summary>
        public int LedArrivals, ShortenedLeads;

        public static StageReport Last { get; set; }
    }
}

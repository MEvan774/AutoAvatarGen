using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// How Mugs moves on the stage: a pose for a time, for each leg of
    /// <see cref="StageMath.PlanTravel"/> — how far along he is, how much he is
    /// stretched or squashed, and how far he leans. Pure: no Unity state, so
    /// the self-tests run it offline.
    ///
    /// The feel is CALM. Mugs is relaxed, and the 1 s Glide is the move that
    /// fits him: slow off the mark, slow into place. So an arrival eases up, a
    /// little past his mark, and settles softly — no dead stop; a departure has
    /// no wind-up, he simply eases away. (The first version of these moves was
    /// quick: a third of a second, a twitch of anticipation, a hard landing.
    /// The creator found it "way too hyper" for him and chose these timings
    /// from a side-by-side preview on 2026-11-17 — see StageMath's durations.)
    ///
    /// MediaPresentationSystem.Stage applies the pose on the transform the
    /// stage moves (the presenter's parent), so a Hop / Grow / Shrink and the
    /// idle sway on the rig below keep running under it.
    /// </summary>
    public static class StageMoves
    {
        public struct Pose
        {
            /// <summary>0 = where the leg starts, 1 = where it ends. Passes 1 a little as an arrival drifts over its mark.</summary>
            public float Travel;
            /// <summary>Multiply his width and height (1 = as he stands).</summary>
            public float ScaleX, ScaleY;
            /// <summary>Degrees he leans INTO the direction of travel. Sideways moves only.</summary>
            public float Tilt;
        }

        /// <summary>Where every leg ends: there, his own shape, upright.</summary>
        public static readonly Pose Rest = new Pose { Travel = 1f, ScaleX = 1f, ScaleY = 1f, Tilt = 0f };

        // ---- The amounts (the limits the self-tests hold every pose to) ----------------

        /// <summary>The most he stretches along his travel…</summary>
        public const float MaxStretch = 0.06f;
        /// <summary>…and the most he gives as he settles.</summary>
        public const float MaxSquash = 0.06f;
        /// <summary>The other axis changes half as much, the other way (he squashes, he doesn't shrink).</summary>
        public const float CrossShare = 0.5f;
        /// <summary>A peek and a lean are smaller moves: their stretch and give are two thirds of a full one's.</summary>
        public const float PeekAmount = 2f / 3f;

        // ---- An arrival: the pop-up, the peek's rise, the lean-in -----------------------

        /// <summary>
        /// How far an arrival swings past its mark before it settles: the "s"
        /// of a back-ease-out. 0.9 is a drift of about 3 % of the leg.
        /// </summary>
        public const float ArriveBack = 0.9f;

        /// <summary>The share of an arrival at which he is furthest past his mark; the settle is everything after it.</summary>
        public static readonly float ArrivePeakAt = 1f - 2f * ArriveBack / (3f * (ArriveBack + 1f));

        /// <summary>How far past his mark that is, as a share of the leg (about 0.03).</summary>
        public static readonly float ArriveOvershoot = 4f * ArriveBack * ArriveBack * ArriveBack / (27f * (ArriveBack + 1f) * (ArriveBack + 1f));

        /// <summary>
        /// An arrival of <paramref name="seconds"/>, from out of sight: he
        /// comes in already moving and slows all the way — stretched while he
        /// is fast — drifts a little past his mark, and settles back onto it
        /// with a soft give that is over exactly as he is at rest. No dead
        /// stop anywhere. <paramref name="amount"/> scales the stretch and give.
        /// </summary>
        public static Pose Arrive(float t, float seconds, float amount)
        {
            if (seconds <= 0f || t >= seconds) return Rest;
            float x = Mathf.Max(0f, t) / seconds;
            float back = x - 1f;
            var pose = new Pose
            {
                Travel = 1f + (ArriveBack + 1f) * back * back * back + ArriveBack * back * back,
                ScaleX = 1f, ScaleY = 1f,
            };

            if (x < ArrivePeakAt)
            {
                // His speed as a share of the speed he comes in with.
                float speed = (3f * (ArriveBack + 1f) * back * back + 2f * ArriveBack * back) / (ArriveBack + 3f);
                float stretch = amount * MaxStretch * Mathf.Max(0f, speed);
                pose.ScaleY = 1f + stretch;
                pose.ScaleX = 1f - stretch * CrossShare;
            }
            else
            {
                float u = (x - ArrivePeakAt) / (1f - ArrivePeakAt), swell = Mathf.Sin(Mathf.PI * u);
                float give = amount * MaxSquash * swell * swell;
                pose.ScaleY = 1f - give;
                pose.ScaleX = 1f + give * CrossShare;
            }
            return pose;
        }

        // ---- A departure: the slide-out, the peek's drop, the lean-out ------------------

        /// <summary>Sideways: how far he leans into a slide-out by the time he is gone, in degrees.</summary>
        public const float DepartLean = 2f;

        /// <summary>
        /// A departure of <paramref name="seconds"/>: no wind-up. He is slow
        /// off the mark and then away, a little longer along his travel the
        /// faster he goes; through a side edge he leans into it slightly. He
        /// is out of sight before the leg ends, so it ends at speed; the pose
        /// at <paramref name="seconds"/> is <see cref="Rest"/>, which is what
        /// the parked (or re-entering) transform gets.
        /// </summary>
        public static Pose Depart(float t, float seconds, float amount, bool sideways)
        {
            if (seconds <= 0f || t >= seconds) return Rest;
            float x = Mathf.Max(0f, t) / seconds;
            float stretch = amount * MaxStretch * x * x;   // his speed, as a share of the speed he leaves with
            var pose = new Pose { Travel = x * x * x };
            if (sideways)
            {
                pose.ScaleX = 1f + stretch;
                pose.ScaleY = 1f - stretch * CrossShare;
                pose.Tilt = amount * DepartLean * x * x * (3f - 2f * x);
            }
            else
            {
                pose.ScaleY = 1f + stretch;
                pose.ScaleX = 1f - stretch * CrossShare;
            }
            return pose;
        }

        // ---- From where he is, in view, to where he will stand --------------------------

        /// <summary>
        /// A move that starts in view and ends in view — only ever the answer
        /// to a {Position:…} that arrives while he is still on his way out: he
        /// comes back from where he is. Eased at both ends, like the Glide; a
        /// touch longer in the middle of it.
        /// </summary>
        public static Pose Ease(float t, float seconds, float amount)
        {
            if (seconds <= 0f || t >= seconds) return Rest;
            float x = Mathf.Max(0f, t) / seconds;
            float travel = x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
            float swell = Mathf.Sin(Mathf.PI * x);
            float stretch = amount * MaxStretch * 0.5f * swell * swell;
            return new Pose { Travel = travel, ScaleY = 1f + stretch, ScaleX = 1f - stretch * CrossShare, Tilt = 0f };
        }

        // ---- What he carries into a move that starts mid-move ---------------------------

        /// <summary>A leg that starts while he is squashed, stretched or leaning eases that away over this long.</summary>
        public const float CarrySeconds = 0.10f;

        /// <summary>How much of the shape he started a leg with is still on him <paramref name="t"/> seconds in: 1 → 0.</summary>
        public static float CarryLeft(float t)
        {
            float u = Mathf.Clamp01(t / CarrySeconds);
            return 1f - u * u * (3f - 2f * u);
        }

        // ---- The legs ------------------------------------------------------------------

        /// <summary>He pops up from below the frame at a full position.</summary>
        public static Pose PopUp(float t) => Arrive(t, StageMath.PopUpSeconds, 1f);

        /// <summary>His head comes up for a peek.</summary>
        public static Pose PeekRise(float t) => Arrive(t, StageMath.PeekRiseSeconds, PeekAmount);

        /// <summary>He slides out: through the side edge (<paramref name="sideways"/>) or, from Center, down.</summary>
        public static Pose SlideOut(float t, bool sideways) => Depart(t, StageMath.SlideOutSeconds, 1f, sideways);

        /// <summary>He drops back down from a peek.</summary>
        public static Pose PeekDrop(float t) => Depart(t, StageMath.PeekDropSeconds, PeekAmount, sideways: false);

        /// <summary>He leans in over a side edge: the peek's rise, along his own (tilted) length.</summary>
        public static Pose LeanIn(float t) => Arrive(t, StageMath.LeanInSeconds, PeekAmount);

        /// <summary>He pulls back out of a lean.</summary>
        public static Pose LeanOut(float t) => Depart(t, StageMath.LeanOutSeconds, PeekAmount, sideways: false);

        /// <summary>From where he is, in view, back to a full position.</summary>
        public static Pose ToFull(float t) => Ease(t, StageMath.ToFullSeconds, 1f);

        /// <summary>From where he is, in view, back into a peek or a lean.</summary>
        public static Pose ToPeek(float t) => Ease(t, StageMath.ToPeekSeconds, PeekAmount);

        /// <summary>The pose of <paramref name="leg"/> <paramref name="t"/> seconds in (<see cref="Rest"/> for a leg that takes no time).</summary>
        public static Pose For(StageLeg leg, float t, bool sideways)
        {
            switch (leg)
            {
                case StageLeg.PopUp: return PopUp(t);
                case StageLeg.RiseToPeek: return PeekRise(t);
                case StageLeg.SlideOut: return SlideOut(t, sideways);
                case StageLeg.DropFromPeek: return PeekDrop(t);
                case StageLeg.LeanIn: return LeanIn(t);
                case StageLeg.LeanOut: return LeanOut(t);
                case StageLeg.ToFull: return ToFull(t);
                case StageLeg.ToPeek: return ToPeek(t);
                case StageLeg.ToLean: return ToPeek(t);
                default: return Rest;
            }
        }

        // ---- The standing edge ---------------------------------------------------------

        /// <summary>
        /// Where the moved transform must sit so that the line he stands on —
        /// his feet, or the frame edge he peeks over — stays where it is while
        /// he is squashed, stretched or leaning. <paramref name="origin"/> is
        /// where the transform would be with his own shape,
        /// <paramref name="edgeY"/> the line's height, <paramref name="tilt"/>
        /// the lean in degrees (counter-clockwise). The transform is scaled and
        /// turned about its own origin, which is not on that line — this is the
        /// offset that makes up for it (HopMath does the same with its lift).
        /// </summary>
        public static Vector2 HoldEdge(Vector2 origin, float edgeY, float scaleY, float tilt)
        {
            if (scaleY == 1f && tilt == 0f) return origin;   // his own shape: exactly where the travel puts him
            float above = (origin.y - edgeY) * scaleY;   // the origin's height over the line, in his new shape
            float r = tilt * Mathf.Deg2Rad;
            return new Vector2(origin.x - Mathf.Sin(r) * above, edgeY + Mathf.Cos(r) * above);
        }

        /// <summary>
        /// The same for a lean: he is tilted <paramref name="tilt"/> degrees
        /// (counter-clockwise) and stretches along his own length, and the
        /// line that must not move is the SIDE edge of the frame at
        /// <paramref name="edgeX"/> — the point where his length crosses it.
        /// Upright (no crossing to speak of) nothing is moved.
        /// </summary>
        public static Vector2 HoldSideEdge(Vector2 origin, float edgeX, float tilt, float scaleAlong)
        {
            float r = tilt * Mathf.Deg2Rad;
            var up = new Vector2(-Mathf.Sin(r), Mathf.Cos(r));   // his own "up", turned with him
            if (scaleAlong == 1f || Mathf.Abs(up.x) < 0.05f) return origin;
            float along = (edgeX - origin.x) / up.x;             // from the transform to the edge, along his length
            return origin + up * (along * (1f - scaleAlong));
        }
    }
}

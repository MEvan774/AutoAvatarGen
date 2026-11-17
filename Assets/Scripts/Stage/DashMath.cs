using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// The "Dash" side move: Left ↔ Right ↔ Center in under half a second,
    /// in the feel of his pop-up and the Hop — a short lean away, a fast start
    /// that eases out, a turn of his body where the old glide mirrored him on
    /// one frame, a little past his mark, a squash, and exactly rest. Chosen in
    /// the main menu (Side move: Glide / Dash); there is no tag keyword. Pure:
    /// a pose for a time, nothing else.
    /// </summary>
    public static class DashMath
    {
        /// <summary>The whole move, tag to rest (the dashDuration dial).</summary>
        public const float TotalSeconds = 0.45f;
        /// <summary>The lean he reaches into the travel, in degrees (the dashLean dial).</summary>
        public const float Lean = 5f;
        /// <summary>How much longer he is along the travel at his fastest (the dashStretch dial).</summary>
        public const float Stretch = 0.06f;

        // ---- The proportions (they scale with the duration) ----------------------------

        /// <summary>The anticipation: 0.07 s of the 0.45.</summary>
        public const float AnticipationShare = 0.07f / TotalSeconds;
        /// <summary>The settle after he has passed his mark: 0.10 s of the 0.45.</summary>
        public const float SettleShare = 0.10f / TotalSeconds;
        /// <summary>The turn: his width goes to nothing and back in 0.12 s of the 0.45…</summary>
        public const float TurnShare = 0.12f / TotalSeconds;
        /// <summary>…around this point of the move — late, so he runs facing where he goes and turns as he arrives.</summary>
        public const float TurnAt = 0.55f;

        /// <summary>How far the anticipation takes him the wrong way, as a share of the distance.</summary>
        public const float AnticipationTravel = 0.015f;
        /// <summary>Its lean away from where he is going, in degrees…</summary>
        public const float AnticipationTilt = 3f;
        /// <summary>…and its small squash.</summary>
        public const float AnticipationSquash = 0.03f;
        /// <summary>How far he passes his mark, as a share of the distance (3 % at most).</summary>
        public const float Overshoot = 0.02f;
        /// <summary>The squash as he stops.</summary>
        public const float LandSquash = 0.04f;
        /// <summary>He leans back a little against the stop, in degrees.</summary>
        public const float BrakeTilt = 1.5f;

        // The travel: 1 - (1 - v^EaseIn)^EaseOut. A fast start that eases out —
        // three quarters of the way in the first half of the time — with just
        // enough of a run-up that the first frame isn't the largest step.
        const float EaseIn = 1.25f, EaseOut = 2.5f;
        // The share of the going over which the anticipation's lean and squash turn into the travel's.
        const float ReleaseShare = 0.15f;

        public struct Pose
        {
            /// <summary>0 = where he was, 1 = his new mark. Below 0 in the anticipation, a little past 1 before he settles.</summary>
            public float Travel;
            /// <summary>Multiply his width and height (the turn is apart: see <see cref="Width"/>).</summary>
            public float ScaleX, ScaleY;
            /// <summary>Degrees he leans INTO the travel (negative: away from it).</summary>
            public float Tilt;
            /// <summary>The turn: 1 = his full width, 0 = edge-on. Always 1 when his facing doesn't change.</summary>
            public float Width;
            /// <summary>True once he has turned: from the frame his width passes zero he faces the new way.</summary>
            public bool Turned;
        }

        /// <summary>His pose <paramref name="t"/> seconds after the tag, with the default dials.</summary>
        public static Pose At(float t, bool turns) => At(t, TotalSeconds, Lean, Stretch, turns);

        /// <summary>
        /// The dash with its three dials (MediaPresentationSystem's inspector
        /// fields): how long it takes, how far he leans into it and how much he
        /// stretches. <paramref name="turns"/>: his facing changes, so he turns
        /// on the way.
        /// </summary>
        public static Pose At(float t, float totalSeconds, float lean, float stretch, bool turns)
        {
            if (totalSeconds <= 0f || t >= totalSeconds)
                return new Pose { Travel = 1f, ScaleX = 1f, ScaleY = 1f, Tilt = 0f, Width = 1f, Turned = turns };

            float x = Mathf.Max(0f, t) / totalSeconds;
            float goShare = 1f - AnticipationShare - SettleShare;
            var pose = new Pose { Width = 1f };

            if (x < AnticipationShare)
            {
                // Gathering: a lean away from where he is going, a small squash.
                float u = x / AnticipationShare, gathered = u * u * (3f - 2f * u);
                pose.Travel = -AnticipationTravel * gathered;
                pose.Tilt = -AnticipationTilt * gathered;
                pose.ScaleY = 1f - AnticipationSquash * gathered;
                pose.ScaleX = 1f + AnticipationSquash * 0.5f * gathered;
            }
            else if (x < AnticipationShare + goShare)
            {
                // Going: fast off the mark, easing out to a stop a little past the new one.
                float v = (x - AnticipationShare) / goShare;
                float w = Mathf.Pow(v, EaseIn);
                float left = 1f - w;
                pose.Travel = -AnticipationTravel + (1f + Overshoot + AnticipationTravel) * (1f - Mathf.Pow(left, EaseOut));

                // His speed, as a share of his fastest: what the lean and the stretch follow.
                float speed = Speed(v) / FastestSpeed;
                float along = stretch * speed;
                // The anticipation's pose swings over into the travel's as he gets going
                // (he is at his fastest, leaning and stretched in full, about a fifth of the way in).
                float k = Mathf.Clamp01(v / ReleaseShare);
                k = k * k * (3f - 2f * k);
                pose.Tilt = Mathf.Lerp(-AnticipationTilt, lean * speed, k);
                pose.ScaleX = 1f + Mathf.Lerp(AnticipationSquash * 0.5f, along, k);
                pose.ScaleY = 1f - Mathf.Lerp(AnticipationSquash, along * 0.5f, k);
            }
            else
            {
                // Stopping: back onto his mark, a squash, a slight lean against the stop.
                float u = (x - AnticipationShare - goShare) / SettleShare;
                float give = 6.75f * u * (1f - u) * (1f - u);
                pose.Travel = 1f + Overshoot * (1f - u * u * (3f - 2f * u));
                pose.Tilt = -BrakeTilt * give;
                pose.ScaleY = 1f - LandSquash * give;
                pose.ScaleX = 1f + LandSquash * 0.5f * give;
            }

            if (turns)
            {
                // A paper turn: his width is the cosine of the turn, edge-on at its middle.
                float u = (x - (TurnAt - TurnShare * 0.5f)) / TurnShare;
                if (u >= 1f) pose.Turned = true;
                else if (u > 0f)
                {
                    pose.Width = Mathf.Abs(Mathf.Cos(Mathf.PI * u));
                    pose.Turned = u >= 0.5f;
                }
            }
            return pose;
        }

        // The travel's speed at v (0 → 1 over the going), up to a constant factor.
        static float Speed(float v)
        {
            if (v <= 0f) return 0f;
            float w = Mathf.Pow(v, EaseIn);
            return Mathf.Pow(Mathf.Max(0f, 1f - w), EaseOut - 1f) * Mathf.Pow(v, EaseIn - 1f);
        }

        static readonly float FastestSpeed = FindFastest();

        static float FindFastest()
        {
            float best = 0f;
            for (int i = 1; i < 2000; i++) best = Mathf.Max(best, Speed(i / 2000f));
            return best > 0f ? best : 1f;
        }

        /// <summary>The share of the move he spends going (between the anticipation and the settle).</summary>
        public static float GoShare => 1f - AnticipationShare - SettleShare;
    }
}

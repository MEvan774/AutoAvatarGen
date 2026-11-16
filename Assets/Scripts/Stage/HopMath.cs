using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// The "Hop" emotion transition: Mugs ducks, his expression changes at the
    /// bottom of the duck, and he springs back up a little past his rest pose
    /// before settling — the same quick, slightly overshooting motion as his
    /// pop-up onto the stage, at the size of a change of mood. Pure: a pose for
    /// a time, nothing else.
    ///
    /// Lift is a share of his own height (+ up); the scales multiply his rest
    /// scale. He squashes a little as he ducks and stretches a little at the
    /// top, so the hop has weight.
    /// </summary>
    public static class HopMath
    {
        /// <summary>The duck (anticipation), eased in and out.</summary>
        public const float DuckSeconds = 0.10f;
        /// <summary>The spring back up, through rest, a little over, and down onto it.</summary>
        public const float RiseSeconds = 0.26f;
        public const float TotalSeconds = DuckSeconds + RiseSeconds;

        /// <summary>How far he ducks, as a share of his height.</summary>
        public const float DuckOfHeight = 0.05f;
        /// <summary>How much shorter he is at the bottom of the duck (and a third of it taller at the top).</summary>
        public const float Squash = 0.08f;

        public struct Pose
        {
            /// <summary>Vertical offset as a share of his height (negative = down).</summary>
            public float Lift;
            public float ScaleX, ScaleY;
            /// <summary>True once the new expression is on (from the bottom of the duck).</summary>
            public bool Swapped;
        }

        public static readonly Pose Rest = new Pose { Lift = 0f, ScaleX = 1f, ScaleY = 1f, Swapped = false };

        /// <summary>The duck's share of the whole hop (the rest is the spring back up).</summary>
        public const float DuckShare = DuckSeconds / TotalSeconds;

        /// <summary>
        /// How far into the duck he is, 1 at the bottom: 0 → 1 while he ducks,
        /// then 1 → 0 on the way up, dipping below 0 as he passes his rest pose
        /// (the overshoot) and coming back to exactly 0 with no speed left.
        /// </summary>
        public static float Depth(float t) => Depth(t, TotalSeconds);

        /// <summary><see cref="Depth(float)"/> for a hop of <paramref name="totalSeconds"/> (same proportions).</summary>
        public static float Depth(float t, float totalSeconds)
        {
            if (t <= 0f || totalSeconds <= 0f) return 0f;
            float duck = totalSeconds * DuckShare, rise = totalSeconds - duck;
            if (t < duck)
            {
                float x = t / duck;
                return x * x * (3f - 2f * x);
            }
            float u = Mathf.Clamp01((t - duck) / rise);
            return Mathf.Cos(1.5f * Mathf.PI * u) * (1f - u);
        }

        /// <summary>His pose <paramref name="t"/> seconds after the emotion tag fired.</summary>
        public static Pose At(float t) => At(t, TotalSeconds, DuckOfHeight, Squash);

        /// <summary>
        /// The same hop with its three dials (HybridAvatarSystem's inspector
        /// fields): how long it takes, how far he ducks (share of his height)
        /// and how much he squashes at the bottom.
        /// </summary>
        public static Pose At(float t, float totalSeconds, float duckOfHeight, float squash)
        {
            if (totalSeconds <= 0f || t >= totalSeconds) return new Pose { Lift = 0f, ScaleX = 1f, ScaleY = 1f, Swapped = true };
            float d = Depth(t, totalSeconds);
            return new Pose
            {
                Lift = -duckOfHeight * d,
                ScaleY = 1f - squash * d,
                ScaleX = 1f + squash * 0.5f * d,
                Swapped = t >= totalSeconds * DuckShare,
            };
        }
    }
}

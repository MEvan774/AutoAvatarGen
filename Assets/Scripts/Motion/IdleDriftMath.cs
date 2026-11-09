using System;

namespace MugsTech.Motion
{
    /// <summary>
    /// The idle camera drift's rules, with no engine state — the self-tests
    /// drive every one directly.
    ///
    /// THE MOTION — a slow push (zoom 1.000 → 1.025 and back, on a 21 s
    /// cosine) plus a lateral sway (±0.3 % of the frame's width, on a sine).
    /// The sway's period is the push's divided by the golden ratio (12.98 s):
    /// the most irrational ratio there is, so the two never line up and the
    /// motion never visibly repeats.
    ///
    /// THE ENVELOPE — the motion is multiplied by a gain that eases in from
    /// rest over 1.5 s when the frame goes bare, and back to exactly zero over
    /// 0.6 s before anything that lands on the frame (a zoom step, the
    /// close-up, a card, an image, black, a transition) — reaching zero
    /// <see cref="StopLeadSeconds"/> early, so the frame before the event is
    /// already at rest. A {Black} or a camera move nobody announced stops it
    /// dead (unseen behind the black / taken over by the zoom).
    ///
    /// THE BACKGROUND — drifts at 40 % of the camera's amount (parallax): it
    /// follows the camera part of the way and is scaled about the camera's
    /// centre, overscanned only as much as the sway needs so no edge shows.
    /// </summary>
    public static class IdleDriftMath
    {
        /// <summary>The push: the camera's zoom goes from 1 to 1 + this and back.</summary>
        public const float PushAmount = 0.025f;
        public const float PushPeriod = 21f;

        public static readonly float GoldenRatio = (float)((1.0 + Math.Sqrt(5.0)) / 2.0);

        /// <summary>The sway's period: the push's over the golden ratio (12.98 s).</summary>
        public static readonly float SwayPeriod = PushPeriod / GoldenRatio;

        /// <summary>The sway's amplitude as a fraction of the frame's width.</summary>
        public const float SwayFraction = 0.003f;

        public const float EaseInSeconds = 1.5f;
        public const float HandOffSeconds = 0.6f;
        /// <summary>The gain is already zero this long before an event lands.</summary>
        public const float StopLeadSeconds = 0.1f;

        /// <summary>The background drifts at this fraction of the camera's amount.</summary>
        public const float BackgroundParallax = 0.4f;

        /// <summary>The push at <paramref name="t"/> seconds of drift: 0 … <see cref="PushAmount"/>, starting at rest.</summary>
        public static float Push(float t) =>
            PushAmount * 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * t / PushPeriod));

        /// <summary>The sway at <paramref name="t"/> seconds of drift: −1 … 1.</summary>
        public static float Sway(float t) => (float)Math.Sin(2.0 * Math.PI * t / SwayPeriod);

        public static float Smooth(float u)
        {
            u = u < 0f ? 0f : u > 1f ? 1f : u;
            return u * u * (3f - 2f * u);
        }

        /// <summary>
        /// The camera's pose at drift time <paramref name="t"/> with the envelope
        /// at <paramref name="gain"/>: its zoom factor (≥ 1; orthographic size =
        /// rest ÷ zoom) and its sideways offset in world units.
        /// </summary>
        public static void Pose(float t, float gain, float swayAmplitude, out float zoom, out float offsetX)
        {
            zoom = 1f + Push(t) * gain;
            offsetX = Sway(t) * swayAmplitude * gain;
        }

        /// <summary>
        /// Where a world-fixed background goes so it appears to drift at
        /// <paramref name="parallax"/> of the camera: scaled by
        /// <paramref name="scale"/> about the camera's rest centre and moved
        /// <paramref name="shiftX"/> sideways. On screen it then zooms by
        /// overscan × (1 + parallax·(zoom − 1)) and slides by −parallax·offset.
        /// The overscan grows with the gain, so at rest the background is
        /// exactly as authored.
        /// </summary>
        public static void Background(float zoom, float offsetX, float gain, float parallax, float overscan,
                                      out float scale, out float shiftX)
        {
            float o = 1f + (overscan - 1f) * Clamp01(gain);
            float kb = 1f + parallax * (zoom - 1f);
            scale = o * kb / zoom;
            shiftX = offsetX * (1f - parallax / zoom);
        }

        /// <summary>
        /// True when a background whose rectangle (relative to the camera's rest
        /// centre, world units) appears on screen scaled by <paramref name="screenScale"/>
        /// and slid by <paramref name="screenShift"/> still covers the whole frame
        /// (half-sizes in rest world units).
        /// </summary>
        public static bool Covers(float xMin, float xMax, float yMin, float yMax,
                                  float halfW, float halfH, float screenScale, float screenShift)
        {
            const float eps = 1e-5f;
            return screenScale * xMin - screenShift <= -halfW + eps
                && screenScale * xMax - screenShift >= halfW - eps
                && screenScale * yMin <= -halfH + eps
                && screenScale * yMax >= halfH - eps;
        }

        /// <summary>
        /// The overscan (≥ 1) the parallax background needs so the sway never
        /// shows its side edges: its slide is at most parallax × amplitude, and
        /// the push only ever enlarges it on screen. 1 when it already has the
        /// room; a hair over the exact need otherwise.
        /// </summary>
        public static float RequiredOverscan(float xMin, float xMax, float halfW, float swayAmplitude, float parallax)
        {
            float room = Math.Min(xMax, -xMin);
            if (room <= 0f) return 1f;   // not a cover at all — nothing an overscan can fix
            float need = (halfW + parallax * swayAmplitude) / room;
            return need <= 1f ? 1f : need * 1.001f;
        }

        /// <summary>
        /// For a background the drift can't move (it sits in the world, so the
        /// camera's sway slides it fully): the largest sway that keeps its side
        /// edges out of frame, never more than <paramref name="swayAmplitude"/>.
        /// </summary>
        public static float SwayRoom(float xMin, float xMax, float halfW, float swayAmplitude)
        {
            float room = Math.Min(xMax - halfW, -halfW - xMin);
            return Math.Max(0f, Math.Min(swayAmplitude, room));
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// The drift's gain: eases in from rest over 1.5 s while the frame is
    /// bare, eases out over 0.6 s when it isn't, and is capped so it reaches
    /// exactly zero <see cref="IdleDriftMath.StopLeadSeconds"/> before the next
    /// event lands. Gain = smoothstep of a linear state, so every start and
    /// stop has zero velocity.
    /// </summary>
    public sealed class DriftEnvelope
    {
        /// <summary>The linear state, 0 (rest) … 1 (full drift).</summary>
        public float U { get; private set; }

        public float Gain => IdleDriftMath.Smooth(U);

        /// <param name="bare">Nothing is on the frame and the camera is free.</param>
        /// <param name="timeToStop">Seconds until the next event lands (∞ / NaN: none known).</param>
        /// <param name="hardStop">Black, or the zoom system has the camera: zero now.</param>
        public float Step(float dt, bool bare, float timeToStop, bool hardStop)
        {
            if (hardStop) { U = 0f; return 0f; }
            dt = Math.Max(0f, dt);
            U = bare ? Math.Min(1f, U + dt / IdleDriftMath.EaseInSeconds)
                     : Math.Max(0f, U - dt / IdleDriftMath.HandOffSeconds);
            if (!float.IsNaN(timeToStop) && !float.IsInfinity(timeToStop))
                U = Math.Min(U, Math.Max(0f, (timeToStop - IdleDriftMath.StopLeadSeconds) / IdleDriftMath.HandOffSeconds));
            return Gain;
        }

        public void Reset() => U = 0f;
    }
}

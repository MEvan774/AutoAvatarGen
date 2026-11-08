using DG.Tweening;
using UnityEngine;

/// <summary>
/// Puts tweens into a DOTween <see cref="Sequence"/> at ABSOLUTE times while
/// keeping the Join + SetDelay form (Sequence.Insert with an AnimationCurve
/// ease has been seen to drop the curve — see BigMediaCard).
///
/// Why it exists: DOTween's Join places a tween at the PREVIOUS joined tween's
/// start plus the new tween's own delay. So the obvious
/// <c>seq.Join(t.SetDelay(stagger * i))</c> stacks the delays: items land at
/// 0, s, 3s, 6s… and a second tween joined for the same item starts later
/// again. Measured on the 2026-11-08 test take: a 3-line BigText's third line
/// came in at 1.40 s instead of 0.70 s, and every line's punch started one
/// stagger after its line appeared. <see cref="At"/> gives each tween only
/// the delay since the previous join, so it lands exactly where asked.
/// Call it in non-decreasing time order.
/// </summary>
public sealed class StaggeredJoin
{
    readonly Sequence sequence;
    float cursor; // start time of the last joined tween

    public StaggeredJoin(Sequence sequence) { this.sequence = sequence; }

    /// <summary>Joins <paramref name="tween"/> so it starts <paramref name="time"/> s into the sequence.</summary>
    public Tween At(float time, Tween tween)
    {
        float delay = DelayFor(time, ref cursor);
        if (delay > 0f) tween.SetDelay(delay);
        sequence.Join(tween);
        return tween;
    }

    /// <summary>
    /// The delay to give a tween joined after one that started at
    /// <paramref name="cursor"/> so it starts at <paramref name="time"/>;
    /// advances the cursor. Out-of-order times start at the cursor (logged).
    /// </summary>
    public static float DelayFor(float time, ref float cursor)
    {
        float delay = time - cursor;
        if (delay < 0f)
        {
            Debug.LogWarning($"StaggeredJoin: {time:F3} s asked after {cursor:F3} s — joins must go in time order; starting it at {cursor:F3} s.");
            delay = 0f;
        }
        cursor += delay;
        return delay;
    }
}

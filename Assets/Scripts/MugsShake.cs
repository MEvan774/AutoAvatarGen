using System.Collections;
using UnityEngine;

/// <summary>
/// Reproduces the "Shake" emotion-transition from the MugsTech preview.
/// Swap Mugs's expression sprite, then call Play() — same order as the preview:
/// the face changes instantly, then the whole character shudders side to side
/// with a slight tilt and settles back to rest.
///
/// Attach this to a transform that ONLY holds Mugs's visual (see setup notes),
/// so the shake offset never fights your Left/Right/Center positioning.
/// </summary>
[DisallowMultipleComponent]
public class MugsShake : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Total shake length in seconds. Shorter = snappier — the quick multi-shake runs at 0.22.")]
    public float duration = 0.22f;

    [Header("Amplitude")]
    [Tooltip("Peak horizontal offset in local units. Bigger = a stronger shudder — tune by eye.")]
    public float amplitude = 1.8f;

    [Tooltip("Peak tilt in degrees, applied on the Z axis.")]
    public float angle = 4f;

    // Normalised keyframes, evenly spaced across the duration. Each sign flip is
    // half an oscillation; the magnitude decays from full to ~0 so the shake
    // "snaps out hard, then rattles down to rest." This set has ~16 flips;
    // add/remove decaying flip pairs to taste.
    static readonly float[] xKeys =
        { 0f, 1f, -0.94f, 0.87f, -0.81f, 0.75f, -0.69f, 0.62f, -0.56f, 0.5f, -0.44f, 0.37f, -0.31f, 0.25f, -0.19f, 0.12f, -0.06f, 0f };
    static readonly float[] rotKeys =
        { 0f, 1f, -0.92f, 0.85f, -0.78f, 0.71f, -0.64f, 0.57f, -0.5f, 0.44f, -0.38f, 0.32f, -0.26f, 0.2f, -0.15f, 0.1f, -0.05f, 0f };

    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    Coroutine running;

    /// <summary>
    /// Fire the shake. Call this immediately AFTER you swap the expression.
    /// Safe to call again mid-shake — it resets to rest and restarts.
    /// Can also be wired directly to a UnityEvent, UI Button, or Animation Event.
    /// </summary>
    public void Play()
    {
        if (running != null)
        {
            StopCoroutine(running);
            transform.localPosition = baseLocalPos;
            transform.localRotation = baseLocalRot;
        }

        // Capture the rest pose now, so a reposition between shakes is respected.
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        running = StartCoroutine(ShakeRoutine());
    }

    IEnumerator ShakeRoutine()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float te = 1f - Mathf.Pow(1f - p, 2f); // ease-out, matches the preview's timing

            float x = Sample(xKeys, te) * amplitude;
            float r = Sample(rotKeys, te) * angle;

            transform.localPosition = baseLocalPos + new Vector3(x, 0f, 0f);
            transform.localRotation = baseLocalRot * Quaternion.Euler(0f, 0f, r);
            yield return null;
        }

        transform.localPosition = baseLocalPos;
        transform.localRotation = baseLocalRot;
        running = null;
    }

    // Piecewise-linear sample over evenly spaced keys.
    static float Sample(float[] keys, float t)
    {
        int segments = keys.Length - 1;
        float scaled = Mathf.Clamp01(t) * segments;
        int i = Mathf.Min((int)scaled, segments - 1);
        float frac = scaled - i;
        return Mathf.Lerp(keys[i], keys[i + 1], frac);
    }
}

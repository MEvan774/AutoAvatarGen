using UnityEngine;

/// <summary>
/// Slow "floating" drift for a card that is resting on screen: a Perlin-noise
/// wander of the RectTransform's anchoredPosition (plus an optional whisper of
/// Z rotation), the UI twin of the presenter's idle sway in HybridAvatarSystem.
///
/// Only active for the 'Ease in + fade' entry style (see
/// <see cref="CardEntryAnimator.IdleFloatActive"/>) — the overshoot entry is a
/// snappy, graphic motion that reads wrong with a drifting hold.
///
/// Lifecycle: <see cref="Begin"/> is called from the entry tween's OnComplete,
/// so the float never fights the slide for the same anchoredPosition, and the
/// noise is re-based to zero at that instant so the card starts drifting from
/// exactly where it landed (no kick). <see cref="Stop"/> freezes the card where
/// it is — hides start from the floated pose rather than snapping back first.
/// </summary>
[DisallowMultipleComponent]
public class CardIdleFloat : MonoBehaviour
{
    private RectTransform rt;
    private CardEntryAnimator.IdleFloatSettings cfg;

    private Vector2 restPos;
    private Vector3 restEuler;
    private float startTime;
    private float seedX, seedY, seedR;
    private float baseX, baseY, baseR;   // noise values at startTime — subtracted so the drift begins at 0
    private bool running;

    public bool IsRunning => running;

    /// <summary>
    /// Starts (or keeps) a float on <paramref name="target"/>. Idempotent: a
    /// float that is already running is left alone, so re-entering an entry
    /// (BigText appending lines) doesn't re-base the rest pose mid-drift.
    /// </summary>
    public static CardIdleFloat Begin(RectTransform target, CardEntryAnimator.IdleFloatSettings settings)
    {
        if (target == null || settings == null) return null;
        var f = target.GetComponent<CardIdleFloat>();
        if (f == null) f = target.gameObject.AddComponent<CardIdleFloat>();
        f.StartFloat(target, settings);
        return f;
    }

    /// <summary>Freezes the float on <paramref name="target"/> if one is running.</summary>
    public static void StopOn(RectTransform target)
    {
        if (target == null) return;
        var f = target.GetComponent<CardIdleFloat>();
        if (f != null) f.Stop();
    }

    private void StartFloat(RectTransform target, CardEntryAnimator.IdleFloatSettings settings)
    {
        if (running) return;

        rt  = target;
        cfg = settings;
        restPos   = rt.anchoredPosition;
        restEuler = rt.localEulerAngles;
        startTime = Time.time;

        seedX = Random.Range(0f, 100f);
        seedY = Random.Range(0f, 100f);
        seedR = Random.Range(0f, 100f);
        baseX = Noise(seedX, 0f, 0f);
        baseY = Noise(seedY, 1f, 0f);
        baseR = Noise(seedR, 2f, 0f);

        running = true;
    }

    /// <summary>
    /// Stops updating. With <paramref name="restore"/> the card snaps back to
    /// the pose it had when the float began (used for the reusable media slot);
    /// without it the card simply holds its current floated pose.
    /// </summary>
    public void Stop(bool restore = false)
    {
        if (!running) return;
        running = false;
        if (restore && rt != null)
        {
            rt.anchoredPosition = restPos;
            rt.localEulerAngles = restEuler;
        }
    }

    void Update()
    {
        if (!running || rt == null || cfg == null) return;

        float time = (Time.time - startTime) * cfg.speed;

        float nx = Noise(seedX, 0f, time) - baseX;   // each in [-1, 1], 0 at start
        float ny = Noise(seedY, 1f, time) - baseY;
        float nr = Noise(seedR, 2f, time) - baseR;

        rt.anchoredPosition = restPos + new Vector2(nx * cfg.amountX, ny * cfg.amountY);

        if (cfg.rotation > 0f)
            rt.localEulerAngles = restEuler + new Vector3(0f, 0f, nr * cfg.rotation);
    }

    // Perlin sample mapped to [-1, 1]. Separate Y rows keep the three axes
    // uncorrelated even though they share a time axis.
    private static float Noise(float seed, float row, float time)
        => (Mathf.PerlinNoise(seed + time, row * 17.3f) - 0.5f) * 2f;

    void OnDisable()
    {
        running = false;
    }
}

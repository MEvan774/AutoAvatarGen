using UnityEngine;

/// <summary>
/// {Map:…} — sizes, colours and the light-up timeline. Pure maths, so the
/// self-tests run it with no engine. Sizes are px of the 1000-px-wide
/// reference card (× <see cref="CardTypography.ScaleFor"/>).
/// </summary>
public static class MapTypography
{
    // ---- Type -------------------------------------------------------------------------
    public const float CaptionMaxPx = 64f;
    public const float CaptionMinPx = 56f;   // the brief's floor
    public const int CaptionMaxLines = 2;
    public const float SourcePx = 34f;
    public const float SourceFloorPx = 32f;
    public const float MapToCaptionGapPx = 28f;
    public const float CaptionToSourceGapPx = 10f;

    // ---- Drawing ----------------------------------------------------------------------
    /// <summary>Country borders, in the card's own colour so neighbours read apart.</summary>
    public const float BorderPx = 1.4f;
    /// <summary>A listed country whose shape is smaller than this (either side) also gets a lit dot.</summary>
    public const float SmallCountryPx = 14f;
    public const float DotPx = 18f;
    /// <summary>The unlit land: slate at this opacity over the card.</summary>
    public const float LandAlpha = 0.35f;

    // ---- Light-up (seconds after the card has landed) ---------------------------------
    public const float LightDelaySeconds = 0.3f;
    public const float LightStaggerSeconds = 0.1f;
    public const float LightSeconds = 0.3f;
    /// <summary>The soft pulse: a brief brightening as each country lights.</summary>
    public const float PulseSeconds = 0.45f;
    public const float PulseStrength = 0.35f;
    public const float DotPopSeconds = 0.3f;
    public const float DotPopOvershoot = 1.8f;

    /// <summary>When listed country <paramref name="i"/> starts to light, after landing.</summary>
    public static float LightStart(int i) => LightDelaySeconds + i * LightStaggerSeconds;

    /// <summary>Unlit → lit, 0→1 (smoothstep).</summary>
    public static float LitAt(float age)
    {
        float x = Mathf.Clamp01(age / LightSeconds);
        return x * x * (3f - 2f * x);
    }

    /// <summary>The pulse's brightening, 0 → peak → 0 over <see cref="PulseSeconds"/>.</summary>
    public static float PulseAt(float age)
    {
        if (age <= 0f || age >= PulseSeconds) return 0f;
        return Mathf.Sin(Mathf.PI * age / PulseSeconds) * PulseStrength;
    }

    /// <summary>A small country's dot: 0 → 1 with an overshoot (OutBack).</summary>
    public static float DotScaleAt(float age)
    {
        if (age <= 0f) return 0f;
        if (age >= DotPopSeconds) return 1f;
        float t = age / DotPopSeconds - 1f, s = DotPopOvershoot;
        return 1f + (s + 1f) * t * t * t + s * t * t;
    }

    /// <summary>The largest map of this aspect that fits a box, centred in it.</summary>
    public static Rect FitMap(Rect box, float aspect)
    {
        float w = box.width, h = box.width / Mathf.Max(0.01f, aspect);
        if (h > box.height) { h = box.height; w = h * aspect; }
        return new Rect(box.x + (box.width - w) * 0.5f, box.y + (box.height - h) * 0.5f, w, h);
    }
}

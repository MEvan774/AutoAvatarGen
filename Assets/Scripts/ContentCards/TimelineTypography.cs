using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// {Timeline:…} — dated events stacked down a vertical line, each appearing
/// on its own beat. Sizes, the stack geometry and every node's entrance curve.
/// Pure maths only — <see cref="TimelineCard"/> does the TMP measuring and
/// drawing — so the self-tests run it with no engine.
///
/// Vertical, not horizontal: a side card is ~1,000 px wide, and five nodes
/// side by side would leave ~170 px per event.
/// Sizes are px of the 1000-px-wide reference card (× <see cref="CardTypography.ScaleFor"/>).
/// </summary>
public static class TimelineTypography
{
    public const int MaxNodes = 5;

    // ---- Type -------------------------------------------------------------------------
    public const float DatePx = 44f;          // bold, the brief's floor
    public const float DateFloorPx = 44f;
    public const float EventMaxPx = 48f;      // auto-fit down to the floor…
    public const float EventMinPx = 40f;      // …the brief's ≥ 40 px
    public const int EventMaxLines = 2;

    // ---- Geometry ---------------------------------------------------------------------
    public const float DotPx = 30f;
    public const float LineWidthPx = 4f;
    public const float DotToTextGapPx = 32f;
    public const float DateToEventGapPx = 6f;
    public const float NodeGapPx = 30f;
    /// <summary>The gap shrinks toward this only when five long nodes can't otherwise fit.</summary>
    public const float MinNodeGapPx = 12f;

    /// <summary>Older nodes settle to this opacity; the newest is full and in the accent.</summary>
    public const float OlderAlpha = 0.7f;

    // ---- Entrance of an added node (seconds after its tag) ------------------------------
    /// <summary>The line draws down from the previous dot to the new one…</summary>
    public const float LineDrawSeconds = 0.3f;
    /// <summary>…then the dot pops and the text slides in.</summary>
    public const float DotPopSeconds = 0.25f;
    public const float DotPopOvershoot = 1.7f;
    public const float TextSlideSeconds = 0.3f;
    public const float TextSlidePx = 36f;
    /// <summary>The previous newest node hands the accent over and settles to 70 % over this long.</summary>
    public const float SettleSeconds = 0.3f;
    /// <summary>The stack re-centres over this long (as BigText's stack does).</summary>
    public const float RecentreSeconds = 0.35f;

    /// <summary>When the dot pops (and the text starts sliding), after the node's tag.</summary>
    public static float PopStart => LineDrawSeconds;
    /// <summary>A node's entrance is over this long after its tag.</summary>
    public static float EntranceSeconds => PopStart + Mathf.Max(DotPopSeconds, Mathf.Max(TextSlideSeconds, SettleSeconds));

    /// <summary>How far the line has drawn toward the new dot (OutQuad 0→1).</summary>
    public static float LineDrawAt(float age) => OutQuad(age / LineDrawSeconds);

    /// <summary>The dot's scale <paramref name="sincePop"/> s after it pops: 0 → 1 with a small overshoot (OutBack).</summary>
    public static float DotScaleAt(float sincePop)
    {
        if (sincePop <= 0f) return 0f;
        if (sincePop >= DotPopSeconds) return 1f;
        float t = sincePop / DotPopSeconds - 1f, s = DotPopOvershoot;
        return 1f + (s + 1f) * t * t * t + s * t * t;
    }

    /// <summary>The text's slide-in 0→1 (OutQuad) — it moves <see cref="TextSlidePx"/> in from the right and fades in.</summary>
    public static float TextAt(float sincePop) => OutQuad(sincePop / TextSlideSeconds);

    /// <summary>The previous newest node's hand-over 0→1 (smoothstep): accent → neutral, 100 % → 70 %.</summary>
    public static float SettleAt(float sinceHandOver)
    {
        float x = Mathf.Clamp01(sinceHandOver / SettleSeconds);
        return x * x * (3f - 2f * x);
    }

    public static float RecentreAt(float age) => OutQuad(age / RecentreSeconds);

    // ---- Stack ------------------------------------------------------------------------

    /// <summary>The first <paramref name="count"/> nodes' height with the gaps between them.</summary>
    public static float StackHeight(IList<float> nodeHeights, int count, float gap)
    {
        float h = 0f;
        int n = Mathf.Min(count, nodeHeights.Count);
        for (int i = 0; i < n; i++) h += nodeHeights[i];
        return h + Mathf.Max(0, n - 1) * gap;
    }

    /// <summary>Where the stack's top sits so it is centred in the inner area (never above its top).</summary>
    public static float StackTop(float innerTop, float innerHeight, float stackHeight)
        => innerTop + Mathf.Max(0f, (innerHeight - stackHeight) * 0.5f);

    /// <summary>Node <paramref name="i"/>'s top inside the stack.</summary>
    public static float NodeTop(IList<float> nodeHeights, int i, float gap)
    {
        float y = 0f;
        for (int j = 0; j < i && j < nodeHeights.Count; j++) y += nodeHeights[j] + gap;
        return y;
    }

    static float OutQuad(float x)
    {
        x = Mathf.Clamp01(x);
        return 1f - (1f - x) * (1f - x);
    }
}

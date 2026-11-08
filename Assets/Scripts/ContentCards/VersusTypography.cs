using UnityEngine;

/// <summary>
/// {Versus:…} — the claim against the evidence, full screen. Sizes, where the
/// two panels and the glyph sit, and the entrance timing. Pure maths only —
/// <see cref="VersusCard"/> does the TMP measuring and drawing — so the
/// self-tests run it with no engine.
///
/// Sizes are px of a 1080-px-tall frame, scaled by the frame's real height
/// (<see cref="ScaleFor"/>). Rects are in frame space: x from the left edge,
/// y DOWN from the top edge.
/// </summary>
public static class VersusTypography
{
    public const float ReferenceFrameHeight = 1080f;

    // ---- Panels -------------------------------------------------------------------
    /// <summary>Each panel's share of the frame width.</summary>
    public const float PanelWidthFraction = 0.42f;
    /// <summary>The gap between the panels (the glyph sits on it); with two 42 % panels
    /// this leaves 5 % margins — the title-safe edge.</summary>
    public const float CenterGapFraction = 0.06f;
    public const float PanelPaddingPx = 56f;
    /// <summary>A panel is at least this share of the frame height (short texts
    /// still read as a full-screen moment) and at most the title-safe height.</summary>
    public const float PanelMinHeightFraction = 0.40f;
    public const float TitleSafeFraction = 0.90f;

    // ---- Type -----------------------------------------------------------------------
    /// <summary>Label font size. The label is in small caps, which TMP draws at
    /// 0.8× for the lower-case letters: 46 px keeps those at 36.8 px, over the
    /// brief's 36 px floor.</summary>
    public const float LabelPx = 46f;
    public const float LabelFloorPx = 45f;
    public const float SmallCapsScale = 0.8f;
    public const float LabelTrackingEm = 4f;   // TMP characterSpacing (1/100 em)
    public const float LabelToTextGapPx = 18f;

    public const float TextMaxPx = 96f;
    public const float TextMinPx = 60f;
    public const int TextMaxLines = 4;

    // ---- Glyph ----------------------------------------------------------------------
    public const float BadgeDiameterPx = 200f;
    /// <summary>The ≠ glyph's ink fills at most this share of the badge (each axis).</summary>
    public const float NotEqualInkFraction = 0.52f;
    /// <summary>"VS": its capitals at most this share of the badge high, its width at most <see cref="VsInkWidthFraction"/>.</summary>
    public const float VsCapFraction = 0.34f;
    public const float VsInkWidthFraction = 0.62f;

    public const string NotEqualGlyph = "≠";
    public const string VsGlyph = "VS";

    // ---- Entrance ---------------------------------------------------------------------
    public const float PanelSlideSeconds = 0.45f;
    /// <summary>The right panel starts this long after the left.</summary>
    public const float RightDelaySeconds = 0.15f;
    /// <summary>The glyph stamps this long after the right panel has landed —
    /// in place, not over two panels still sliding.</summary>
    public const float GlyphDelaySeconds = 0.15f;

    public const float StampFromScale = 1.6f;
    public const float StampSeconds = 0.22f;
    /// <summary>OutBack overshoot: a slight dip under 1.0 before settling.</summary>
    public const float StampOvershoot = 1.4f;

    public const float ContentFadeOutSeconds = 0.25f;

    /// <summary>A panel's overshoot past its rest is held under this share of the
    /// centre gap, so the two panels never touch mid-bounce: the shared entry
    /// curve overshoots ~11 %, and over a ~960 px slide that was ~110 px — more
    /// than the 115 px gap.</summary>
    public const float MaxOvershootGapFraction = 0.4f;

    /// <summary>When panel <paramref name="i"/> (0 = left, 1 = right) starts sliding, after the card shows.</summary>
    public static float PanelStart(int i) => i == 0 ? 0f : RightDelaySeconds;

    /// <summary>When the glyph stamps in, after the card shows.</summary>
    public static float GlyphStart => RightDelaySeconds + PanelSlideSeconds + GlyphDelaySeconds;

    /// <summary>The whole entrance is over this long after the card shows.</summary>
    public static float EntranceSeconds => GlyphStart + StampSeconds;

    public static float ScaleFor(float frameHeight) => frameHeight / ReferenceFrameHeight;

    // =========================================================================
    // Placement
    // =========================================================================

    /// <summary>Panel <paramref name="i"/>'s rect: 42 % of the width, either side of
    /// the centre gap, centred vertically.</summary>
    public static Rect PanelRect(int i, float frameW, float frameH, float panelH)
    {
        float w = frameW * PanelWidthFraction;
        float gap = frameW * CenterGapFraction;
        float x = i == 0 ? frameW * 0.5f - gap * 0.5f - w : frameW * 0.5f + gap * 0.5f;
        return new Rect(x, (frameH - panelH) * 0.5f, w, panelH);
    }

    /// <summary>The panel height for a content group: the group plus padding,
    /// at least <see cref="PanelMinHeightFraction"/> of the frame, at most title-safe.</summary>
    public static float PanelHeight(float groupH, float frameH)
    {
        float k = ScaleFor(frameH);
        float h = groupH + 2f * PanelPaddingPx * k;
        return Mathf.Clamp(h, frameH * PanelMinHeightFraction, frameH * TitleSafeFraction);
    }

    /// <summary>The glyph's round badge, centred on the frame (it straddles the gap).</summary>
    public static Rect BadgeRect(float frameW, float frameH)
    {
        float d = BadgeDiameterPx * ScaleFor(frameH);
        return new Rect((frameW - d) * 0.5f, (frameH - d) * 0.5f, d, d);
    }

    /// <summary>How far panel <paramref name="i"/> starts from its rest, along x:
    /// wholly off its own side of the frame (shadow included).</summary>
    public static float OffscreenOffset(int i, Rect panel, float frameW, float frameH)
    {
        float margin = 60f * ScaleFor(frameH);
        return i == 0 ? -(panel.xMax + margin) : frameW - panel.xMin + margin;
    }

    /// <summary>
    /// A panel's x offset from its rest when its slide is at <paramref name="eased"/>
    /// (0 = start, 1 = rest; the overshoot curve goes past 1). Before rest it is
    /// the plain lerp from <paramref name="offscreen"/>; past it the overshoot is
    /// softly limited to <paramref name="maxOvershoot"/> px (tanh: the same speed
    /// through the rest point, so there is no kink, and never more than the limit).
    /// </summary>
    public static float SlideOffset(float eased, float offscreen, float maxOvershoot)
    {
        if (eased <= 1f || maxOvershoot <= 0f) return offscreen * (1f - eased);
        float over = Mathf.Abs(offscreen) * (eased - 1f);
        float limited = maxOvershoot * (float)System.Math.Tanh(over / maxOvershoot);
        return -Mathf.Sign(offscreen) * limited;
    }

    /// <summary>The overshoot limit in px for a frame (<see cref="MaxOvershootGapFraction"/> of the centre gap).</summary>
    public static float MaxOvershootPx(float frameW) => frameW * CenterGapFraction * MaxOvershootGapFraction;

    // =========================================================================
    // Stamp
    // =========================================================================

    /// <summary>
    /// The glyph's scale <paramref name="age"/> s after it stamps: from
    /// <see cref="StampFromScale"/> down to 1.0 on an OutBack curve over
    /// <see cref="StampSeconds"/>. The card anchors age 0 to the first frame the
    /// glyph is drawn, so that frame always shows the full 1.6×.
    /// </summary>
    public static float StampScaleAt(float age)
    {
        if (age <= 0f) return StampFromScale;
        if (age >= StampSeconds) return 1f;
        float t = age / StampSeconds - 1f;
        float s = StampOvershoot;
        float f = 1f + (s + 1f) * t * t * t + s * t * t; // OutBack 0→1
        return StampFromScale + (1f - StampFromScale) * f;
    }
}

using UnityEngine;

/// <summary>
/// {Document:…} — a filing, complaint, alert or report on paper. Sizes,
/// colours, the stamp's fixed words, the highlight / stamp timeline and the
/// geometry that keeps the stamp off the highlight. Pure maths only —
/// <see cref="DocumentCard"/> does the TMP measuring and drawing — so the
/// self-tests run it with no engine.
///
/// Sizes are px of the 1000-px-wide reference card (× <see cref="CardTypography.ScaleFor"/>).
/// Rects and polygons are in card space: x from the left edge, y DOWN from the top.
/// </summary>
public static class DocumentTypography
{
    // ---- Type -------------------------------------------------------------------------
    /// <summary>The passage, in Noto Serif: auto-fits from 52 px down to 44 (the
    /// floor; under it the card warns, never under 32).</summary>
    public const float BodyMaxPx = 52f;
    public const float BodyMinPx = 44f;
    /// <summary>Extra row spacing for the serif passage (TMP lineSpacing, 1/100 em).</summary>
    public const float BodyLineSpacing = 8f;

    /// <summary>The source in the header strip, Noto Serif Bold small caps: TMP
    /// draws the lower-case letters at 0.8×, so 40 px keeps them at 32 px.</summary>
    public const float HeaderPx = 40f;
    public const float HeaderFloorPx = 40f;
    public const float HeaderTrackingEm = 6f;
    /// <summary>Space above and below the source inside the strip.</summary>
    public const float HeaderPadYPx = 20f;
    public const float HeaderRulePx = 2f;
    /// <summary>Strip → passage.</summary>
    public const float BodyTopGapPx = 36f;

    // ---- Stamp ------------------------------------------------------------------------
    public const float StampWordPx = 58f;
    /// <summary>The stamp may shrink to fit beside the passage, its word no smaller than this…</summary>
    public const float StampWordMinPx = 44f;
    /// <summary>…and its second line ("NOT PROVEN IN COURT") no smaller than the 32 px floor.</summary>
    public const float StampLinePx = 40f;
    public const float StampLineFloorPx = 32f;
    public const float StampWordTrackingEm = 10f;
    public const float StampLineTrackingEm = 6f;
    public const float StampRotationDegrees = -10f;
    public const float StampPadXPx = 24f;
    public const float StampPadYPx = 16f;
    /// <summary>The double rule: a 6 px outer rule, a 5 px gap, a 2 px inner rule.</summary>
    public const float StampFramePx = 13f;
    public const float StampLineGapPx = 12f;
    /// <summary>Space kept between the stamp band and the passage.</summary>
    public const float StampBandGapPx = 16f;
    public const float StampInkAlpha = 0.9f;
    /// <summary>A stamp that has to overlap passage text is drawn under the words in this lighter ink.</summary>
    public const float StampUnderTextAlpha = 0.55f;

    // ---- Colours ----------------------------------------------------------------------
    public static readonly Color Paper       = Hex(0xF6, 0xF1, 0xE4);
    public static readonly Color HeaderStrip = Hex(0xEC, 0xE4, 0xCF);
    public static readonly Color HeaderRule  = Hex(0xD3, 0xC8, 0xAD);
    public static readonly Color HeaderInk   = Hex(0x6B, 0x60, 0x4E);
    public static readonly Color Ink         = Hex(0x23, 0x20, 0x1A);
    public static readonly Color Marker      = new Color(1f, 0.886f, 0.26f, 0.80f); // highlighter yellow

    public static Color StampColor(DocumentStamp s)
    {
        switch (s)
        {
            case DocumentStamp.Allegation: return Hex(0xC6, 0x28, 0x28); // red
            case DocumentStamp.Filing:     return Hex(0x1F, 0x4E, 0x9E); // blue
            case DocumentStamp.Ruling:     return Hex(0x2E, 0x7D, 0x32); // green
            case DocumentStamp.Report:     return Hex(0x5F, 0x63, 0x68); // grey
            default:                       return Color.clear;
        }
    }

    /// <summary>
    /// The stamp's words — fixed by the keyword, never free text (the brief's
    /// table). <paramref name="sub"/> is the smaller second line, or null.
    /// </summary>
    public static string StampWords(DocumentStamp s, out string sub)
    {
        sub = null;
        switch (s)
        {
            case DocumentStamp.Allegation: sub = "NOT PROVEN IN COURT"; return "ALLEGATION";
            case DocumentStamp.Filing:     return "COURT FILING";
            case DocumentStamp.Ruling:     return "RULING";
            case DocumentStamp.Report:     return "REPORT";
            default:                       return null;
        }
    }

    // ---- Timeline (seconds, from when the card has landed) ----------------------------
    public const float HighlightDelaySeconds = 0.5f;
    public const float HighlightLineSeconds = 0.4f;
    /// <summary>After the highlight has finished (or after landing, with no highlight).</summary>
    public const float StampDelaySeconds = 0.6f;
    public const float StampFromScale = 1.5f;
    public const float StampSeconds = 0.2f;
    public const float StampOvershoot = 1.2f;

    /// <summary>When the marker starts on line <paramref name="line"/> of the phrase.</summary>
    public static float LineSweepStart(int line) => HighlightDelaySeconds + line * HighlightLineSeconds;

    /// <summary>Line <paramref name="line"/>'s sweep 0→1, <paramref name="sinceLanding"/> s after the card landed
    /// (ease in-out: a hand stroke speeds up, then slows into the end of the phrase).</summary>
    public static float LineSweepAt(float sinceLanding, int line)
    {
        float x = Mathf.Clamp01((sinceLanding - LineSweepStart(line)) / HighlightLineSeconds);
        return x * x * (3f - 2f * x);
    }

    /// <summary>When the stamp slams, after landing: 0.6 s after the last line's sweep ends.</summary>
    public static float StampStart(int highlightLines) =>
        (highlightLines > 0 ? LineSweepStart(highlightLines) : 0f) + StampDelaySeconds;

    /// <summary>The stamp's scale <paramref name="age"/> s after it lands: 1.5× → 1.0 on an
    /// OutBack curve. The card anchors age 0 to the first frame it is drawn.</summary>
    public static float StampScaleAt(float age)
    {
        if (age <= 0f) return StampFromScale;
        if (age >= StampSeconds) return 1f;
        float t = age / StampSeconds - 1f;
        float s = StampOvershoot;
        float f = 1f + (s + 1f) * t * t * t + s * t * t;
        return StampFromScale + (1f - StampFromScale) * f;
    }

    // ---- Geometry (card space, y down) ------------------------------------------------

    /// <summary>
    /// The corners of a <paramref name="size"/> box centred on <paramref name="centre"/>,
    /// rotated by <paramref name="uiDegrees"/> as a uGUI z rotation (positive =
    /// counter-clockwise on screen) — returned in y-down card space.
    /// </summary>
    public static Vector2[] RotatedBox(Vector2 centre, Vector2 size, float uiDegrees)
    {
        float a = uiDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        float hx = size.x * 0.5f, hy = size.y * 0.5f;
        var pts = new Vector2[4];
        var offs = new[] { new Vector2(-hx, hy), new Vector2(hx, hy), new Vector2(hx, -hy), new Vector2(-hx, -hy) };
        for (int i = 0; i < 4; i++)
        {
            // Rotate in y-up space, then flip y into card space.
            float x = offs[i].x * c - offs[i].y * s;
            float yUp = offs[i].x * s + offs[i].y * c;
            pts[i] = new Vector2(centre.x + x, centre.y - yUp);
        }
        return pts;
    }

    public static Rect Bounds(Vector2[] poly)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (Vector2 p in poly)
        {
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y);
            x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    /// <summary>Whether a convex polygon and a rect share any area (separating axes).</summary>
    public static bool Overlaps(Vector2[] poly, Rect r)
    {
        if (r.width <= 0f || r.height <= 0f) return false;
        var box = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
        return !Separated(poly, box) && !Separated(box, poly);
    }

    static bool Separated(Vector2[] a, Vector2[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            Vector2 e = a[(i + 1) % a.Length] - a[i];
            var axis = new Vector2(-e.y, e.x);
            float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
            foreach (Vector2 p in a) { float d = Vector2.Dot(p, axis); aMin = Mathf.Min(aMin, d); aMax = Mathf.Max(aMax, d); }
            foreach (Vector2 p in b) { float d = Vector2.Dot(p, axis); bMin = Mathf.Min(bMin, d); bMax = Mathf.Max(bMax, d); }
            if (aMax <= bMin || bMax <= aMin) return true;
        }
        return false;
    }

    static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
}

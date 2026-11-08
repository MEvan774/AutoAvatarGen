using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using TMPro;
using MugsTech.Style;

/// <summary>
/// Readable side-card text (Stat / Quote / Excerpt / Headline).
///
/// Every size below is in px of the reference side card — 1000 px wide at
/// 1080p (the measured 1000×800 card) — and is scaled by the card's real
/// width (<see cref="ScaleFor"/>), so a 4K or 720p canvas keeps the same
/// proportions. A size is a TMP font size, which for a line with ascenders
/// and descenders is about the row height you measure in a frame.
///
/// Layout: the card's content sits as ONE vertically centred group inside an
/// inner padding of <see cref="PaddingFraction"/> of the card's width (body,
/// then attribution beneath it) — instead of the body pinned to the middle and
/// the attribution to the bottom edge. The body auto-fits: the largest size in
/// its range at which the whole group fits; below its warn floor it logs a
/// warning naming the card, and nothing ever goes under
/// <see cref="HardFloorPx"/>. Text is never truncated — every field renders
/// exactly as written.
///
/// Fonts: Inter by role — Regular body, Bold names / labels / headline, the
/// heavy Inter ExtraBold (runtime dynamic asset built from
/// Resources/Fonts/Inter-ExtraBold.ttf) for the Stat value. A font picked in
/// the visuals menu (CardFontOverride) still wins; it gets TMP Bold for the
/// bold roles. The visuals save's card font STYLE (bold/italic) no longer
/// applies to these four cards — the role decides the weight.
///
/// The fitting maths (<see cref="LargestFitting"/>, <see cref="FitBody"/>,
/// <see cref="StatValueSize"/>) is pure so it runs in the self-tests with no
/// engine; the TMP measuring and placing is in the Place*/Measure* helpers.
/// </summary>
public static class CardTypography
{
    // ---- Reference geometry --------------------------------------------------
    public const float ReferenceCardWidth = 1000f;
    public const float ReferenceCardHeight = 800f;
    /// <summary>Inner padding, as a fraction of the card's WIDTH, on every side.</summary>
    public const float PaddingFraction = 0.07f;
    /// <summary>No text on a side card renders below this.</summary>
    public const float HardFloorPx = 32f;
    /// <summary>A size this close under its floor still counts as at the floor.</summary>
    public const float FloorGracePx = 1f;

    // ---- Quote / Excerpt body ----------------------------------------------
    public const float BodyMinPx = 56f;
    public const float BodyMaxPx = 80f;
    public const float BodyWarnFloorPx = 44f;

    // ---- Attribution -------------------------------------------------------
    public const float NamePx = 44f;            // ≥ 40, bold
    public const float NameFloorPx = 40f;
    public const float RolePx = 34f;            // ≥ 32
    public const float RoleFloorPx = 32f;
    public const float ExcerptSourcePx = 34f;   // ≥ 32
    public const float ExcerptSourceFloorPx = 32f;

    // ---- Quote mark ----------------------------------------------------------
    public const float QuoteMarkPx = 200f;      // 180–220
    public const float QuoteMarkAlpha = 0.25f;
    /// <summary>How much of the mark's ink rises above the body's first line.</summary>
    public const float QuoteMarkRise = 0.45f;

    // ---- Stat ----------------------------------------------------------------
    public const float StatCapMinPx = 160f;
    public const float StatCapMaxPx = 260f;
    /// <summary>The value aims at this share of the inner width…</summary>
    public const float StatValueWidthFraction = 0.70f;
    /// <summary>…and may widen up to this share to reach the cap-height floor.</summary>
    public const float StatValueWidthLimit = 0.85f;
    public const float StatLabelPx = 56f;       // ≥ 52, bold
    public const float StatLabelFloorPx = 52f;
    public const float StatContextPx = 40f;     // ≥ 38
    public const float StatContextFloorPx = 38f;
    /// <summary>Win/Loss arrow side, as a share of the value's cap height.</summary>
    public const float StatArrowToCap = 1.0f;
    /// <summary>Gap between arrow and value, as a share of the value's cap height.</summary>
    public const float StatArrowGapToCap = 0.15f;

    // ---- Side Headline -------------------------------------------------------
    public const float HeadlineMinPx = 56f;
    public const float HeadlineMaxPx = 90f;
    public const float HeadlineWarnFloorPx = 56f;
    public const float HeadlineSourcePx = 38f;  // ≥ 36
    public const float HeadlineSourceFloorPx = 36f;
    /// <summary>Source-logo side, as a share of the source font size.</summary>
    public const float HeadlineLogoToSource = 1.15f;

    // ---- Gaps (reference px) -------------------------------------------------
    public const float BodyToAttributionGapPx = 36f;
    public const float NameToRoleGapPx = 4f;
    public const float StatValueToLabelGapPx = 28f;
    public const float StatLabelToContextGapPx = 10f;

    public enum Weight { Regular, Bold, Heavy }

    // =========================================================================
    // Fonts
    // =========================================================================

    const string InterExtraBoldPath = "Fonts/Inter-ExtraBold";
    static TMP_FontAsset s_InterExtraBold;
    static bool s_InterExtraBoldFailed;

    /// <summary>Characters baked into the heavy font's atlas up front, so a Stat
    /// value never rasterises glyphs mid-take.</summary>
    const string HeavyPrewarmCharacters =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
        "abcdefghijklmnopqrstuvwxyz{|}~€£¥¢°±×÷–—‘’“”…‰•";

    /// <summary>
    /// Inter ExtraBold as a runtime dynamic TMP font asset (90 pt sampling,
    /// padding 9, SDFAA — FontRegistry's settings). Null when the TTF is
    /// missing or can't be read, and the callers fall back to Inter Bold.
    /// </summary>
    public static TMP_FontAsset InterExtraBold
    {
        get
        {
            if (s_InterExtraBold != null) return s_InterExtraBold;
            if (s_InterExtraBoldFailed) return null;

            Font source = Resources.Load<Font>(InterExtraBoldPath);
            if (source != null)
            {
                try
                {
                    s_InterExtraBold = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
                        1024, 1024, AtlasPopulationMode.Dynamic, true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Cards] Could not build the Inter ExtraBold font asset: {e.Message}");
                }
            }

            if (s_InterExtraBold == null)
            {
                s_InterExtraBoldFailed = true;
                Debug.LogWarning($"[Cards] Resources/{InterExtraBoldPath}.ttf missing or unreadable — " +
                                 "Stat values fall back to Inter Bold.");
                return null;
            }

            s_InterExtraBold.name = "Inter-ExtraBold SDF (runtime)";
            s_InterExtraBold.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_InterExtraBold.TryAddCharacters(HeavyPrewarmCharacters);
            return s_InterExtraBold;
        }
    }

    /// <summary>
    /// Loads the three card fonts (and bakes the heavy one's common glyphs) so
    /// the first card of a take doesn't stall a frame loading a 33 MB atlas.
    /// Called from ContentZoneController.Awake, before the recorder starts.
    /// </summary>
    public static void Prewarm()
    {
        _ = ContentCardUIBuilder.InterRegular;
        _ = ContentCardUIBuilder.InterBold;
        _ = InterExtraBold;
    }

    /// <summary>
    /// Sets the font and style for a text role. The visuals-menu font wins when
    /// set (Bold style for the bold roles); otherwise the matching Inter face —
    /// a real bold, not the shader's faux bold.
    /// </summary>
    public static void ApplyWeight(TMP_Text t, Weight weight)
    {
        TMP_FontAsset user = VisualsRuntimeApplier.CardFontOverride;
        if (user != null)
        {
            t.font = user;
            t.fontStyle = weight == Weight.Regular ? FontStyles.Normal : FontStyles.Bold;
            return;
        }

        TMP_FontAsset face =
            weight == Weight.Heavy ? (InterExtraBold != null ? InterExtraBold : ContentCardUIBuilder.InterBold) :
            weight == Weight.Bold  ? ContentCardUIBuilder.InterBold :
                                     ContentCardUIBuilder.InterRegular;
        if (face != null)
        {
            t.font = face;
            t.fontStyle = FontStyles.Normal;
        }
        else
        {
            // Project fonts missing — keep whatever font CreateText gave it.
            t.fontStyle = weight == Weight.Regular ? FontStyles.Normal : FontStyles.Bold;
        }
    }

    /// <summary>A card text set up for manual sizing: no auto-size, word wrap,
    /// never truncated.</summary>
    public static void PrepareText(TMP_Text t, Weight weight, TextAlignmentOptions alignment)
    {
        ApplyWeight(t, weight);
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.maxVisibleLines = 99999;
        t.alignment = alignment;
        t.margin = Vector4.zero;
    }

    // =========================================================================
    // Geometry
    // =========================================================================

    /// <summary>The card's size; the reference card when the rect isn't resolved yet.</summary>
    public static Vector2 CardSize(RectTransform card)
    {
        Rect r = card.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(ReferenceCardWidth, ReferenceCardHeight);
    }

    /// <summary>Reference px → this card's px.</summary>
    public static float ScaleFor(Vector2 cardSize) => cardSize.x / ReferenceCardWidth;

    public static float PaddingFor(Vector2 cardSize) => cardSize.x * PaddingFraction;

    /// <summary>
    /// Places a text as a full-inner-width row whose TOP sits <paramref name="top"/>
    /// px below the card's top edge (card space, y down), <paramref name="height"/> tall.
    /// </summary>
    public static void PlaceRow(TMP_Text t, float pad, float top, float height)
    {
        PlaceBox(t, pad, top, -1f, height, pad);
    }

    /// <summary>
    /// Places a text box at <paramref name="left"/> px from the card's left
    /// edge and <paramref name="top"/> px below its top. width &lt; 0 stretches
    /// to <paramref name="rightInset"/> px from the right edge.
    /// </summary>
    public static void PlaceBox(TMP_Text t, float left, float top, float width, float height, float rightInset = 0f)
    {
        RectTransform rt = t.rectTransform;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        if (width < 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-rightInset, -top);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(left, -top);
        }
    }

    /// <summary>Preferred height of the text's current string at a size, wrapped to a width.</summary>
    public static float MeasureHeight(TMP_Text t, float size, float wrapWidth)
    {
        t.fontSize = size;
        return t.GetPreferredValues(wrapWidth, 0f).y;
    }

    /// <summary>Preferred single-line width of the text's current string at a size.</summary>
    public static float MeasureLineWidth(TMP_Text t, float size)
    {
        TextWrappingModes wrap = t.textWrappingMode;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.fontSize = size;
        float w = t.GetPreferredValues(100000f, 100000f).x;
        t.textWrappingMode = wrap;
        return w;
    }

    /// <summary>
    /// Width of the widest word of <paramref name="text"/> at <paramref name="size"/>
    /// from the font's glyph advances (a word wider than the line would be split
    /// mid-word by TMP). 3 % over the bare advances for kerning / bold spacing.
    /// </summary>
    public static float WidestWordWidth(TMP_FontAsset font, string text, float size)
    {
        if (font == null || string.IsNullOrEmpty(text)) return 0f;
        float scale = size / Mathf.Max(1f, font.faceInfo.pointSize) * Mathf.Max(0.0001f, font.faceInfo.scale);
        float widest = 0f, run = 0f;
        foreach (char c in StripRichText(text))
        {
            if (char.IsWhiteSpace(c)) { widest = Mathf.Max(widest, run); run = 0f; continue; }
            if (font.characterLookupTable.TryGetValue((uint)c, out TMP_Character ch) && ch.glyph != null)
                run += ch.glyph.metrics.horizontalAdvance * ch.glyph.scale * scale;
            else
                run += 0.6f * size; // unknown glyph (fallback font) — a generous guess
        }
        return Mathf.Max(widest, run) * 1.03f;
    }

    static string StripRichText(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The rendered glyphs' ink (no SDF padding) of a text, in CARD space with
    /// y measured DOWN from the card's top edge and x from its left edge.
    /// Empty rect when nothing is visible. Forces a mesh update.
    /// </summary>
    public static Rect InkInCard(TMP_Text t, RectTransform card)
    {
        t.ForceMeshUpdate(true, true);
        TMP_TextInfo info = t.textInfo;
        bool any = false;
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;

        Rect cr = card.rect;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || ci.textElement == null || ci.textElement.glyph == null) continue;

            GlyphMetrics m = ci.textElement.glyph.metrics;
            float s = ci.scale;
            float left = ci.origin + m.horizontalBearingX * s;
            float top = ci.baseLine + m.horizontalBearingY * s;
            Vector3 a = card.InverseTransformPoint(t.rectTransform.TransformPoint(new Vector3(left, top, 0f)));
            Vector3 b = card.InverseTransformPoint(t.rectTransform.TransformPoint(
                new Vector3(left + m.width * s, top - m.height * s, 0f)));

            // card local → (x from left edge, y down from top edge)
            float ax = a.x - cr.xMin, bx = b.x - cr.xMin;
            float ay = cr.yMax - a.y, by = cr.yMax - b.y;
            xMin = Mathf.Min(xMin, Mathf.Min(ax, bx)); xMax = Mathf.Max(xMax, Mathf.Max(ax, bx));
            yMin = Mathf.Min(yMin, Mathf.Min(ay, by)); yMax = Mathf.Max(yMax, Mathf.Max(ay, by));
            any = true;
        }
        return any ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : Rect.zero;
    }

    /// <summary>First visible character's baseline, in card space (y down from the top edge).</summary>
    public static float BaselineInCard(TMP_Text t, RectTransform card)
    {
        t.ForceMeshUpdate(true, true);
        TMP_TextInfo info = t.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible) continue;
            Vector3 p = card.InverseTransformPoint(t.rectTransform.TransformPoint(new Vector3(ci.origin, ci.baseLine, 0f)));
            return card.rect.yMax - p.y;
        }
        return 0f;
    }

    /// <summary>Cap height of the font at a size (from its face info).</summary>
    public static float CapHeight(TMP_FontAsset font, float size)
    {
        if (font == null) return size * 0.72f;
        FaceInfo fi = font.faceInfo;
        float ratio = fi.pointSize > 0f ? fi.capLine / fi.pointSize : 0.72f;
        if (ratio <= 0.1f || ratio > 1.2f) ratio = 0.72f;
        return size * ratio;
    }

    // =========================================================================
    // Pure fitting maths
    // =========================================================================

    /// <summary>
    /// Largest whole size in [min, max] for which <paramref name="fits"/> is
    /// true, by binary search (fits is assumed to turn false as size grows), then
    /// stepped down while the found size doesn't actually fit (wrapping can make
    /// the curve jagged). NaN when even <paramref name="min"/> doesn't fit.
    /// </summary>
    public static float LargestFitting(Func<float, bool> fits, float min, float max)
    {
        int lo = Mathf.CeilToInt(min), hi = Mathf.FloorToInt(max);
        if (hi < lo) return float.NaN;
        if (!fits(lo)) return float.NaN;
        if (fits(hi)) return hi;

        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (fits(mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    public struct BodyFit
    {
        public float Size;
        /// <summary>Fits only under the warn floor.</summary>
        public bool BelowWarnFloor;
        /// <summary>Doesn't fit even at the hard floor (rendered there anyway).</summary>
        public bool Overflows;
    }

    /// <summary>
    /// The body's size: the largest in [min, max] that fits; failing that the
    /// largest down to <paramref name="hardFloor"/> (flagged below the warn
    /// floor); failing that the hard floor, flagged as overflowing.
    /// </summary>
    public static BodyFit FitBody(Func<float, bool> fits, float min, float max, float warnFloor, float hardFloor)
    {
        float s = LargestFitting(fits, min, max);
        if (float.IsNaN(s))
            s = LargestFitting(fits, hardFloor, min - 1f);

        var fit = new BodyFit();
        if (float.IsNaN(s))
        {
            fit.Size = hardFloor;
            fit.Overflows = true;
        }
        else
        {
            fit.Size = s;
        }
        fit.BelowWarnFloor = fit.Size < warnFloor - 0.01f; // whole sizes: 43 is below 44
        return fit;
    }

    /// <summary>
    /// The Stat value's font size. <paramref name="widthPer100"/> is the value's
    /// single-line width at size 100, <paramref name="capRatio"/> cap height ÷
    /// size, <paramref name="innerWidth"/> the card's inner width, and
    /// <paramref name="arrow"/> whether a Win/Loss arrow shares the row.
    /// Aims at <see cref="StatValueWidthFraction"/> of the inner width; widens up
    /// to <see cref="StatValueWidthLimit"/> to reach the cap-height floor; caps
    /// the cap height at the ceiling; with an arrow the arrow + gap + value row
    /// must also fit the inner width. All lengths in this card's px (k = card scale).
    /// </summary>
    public static float StatValueSize(float widthPer100, float capRatio, float innerWidth, bool arrow, float k)
    {
        if (widthPer100 <= 0f || capRatio <= 0f) return StatCapMinPx * k / Mathf.Max(0.1f, capRatio);

        float perPx = widthPer100 / 100f;                       // value width per unit of size
        float arrowPerPx = arrow ? capRatio * (StatArrowToCap + StatArrowGapToCap) : 0f;

        float aim   = innerWidth * StatValueWidthFraction / perPx;
        float limit = innerWidth * StatValueWidthLimit / perPx;
        if (arrow) limit = Mathf.Min(limit, innerWidth / (perPx + arrowPerPx));

        float capMin = StatCapMinPx * k / capRatio;
        float capMax = StatCapMaxPx * k / capRatio;

        float s = Mathf.Max(aim, capMin);   // reach the floor if width allows…
        s = Mathf.Min(s, capMax);           // …never past the ceiling…
        s = Mathf.Min(s, limit);            // …and never wider than the limit
        return Mathf.Max(s, HardFloorPx * k);
    }

    // =========================================================================
    // Layout report (read by the self-tests; one log line per card)
    // =========================================================================

    public sealed class Element
    {
        public string Name;
        public TMP_Text Text;
        public float Size;
        public float Floor;
        public bool BelowFloor;
    }

    public sealed class Report
    {
        public string Card;
        public readonly List<Element> Elements = new List<Element>();
        public readonly List<string> Warnings = new List<string>();
        public float GroupHeight;
        public float InnerHeight;

        public Element Add(string name, TMP_Text text, float size, float floor)
        {
            // 1 px of grace: a sub-pixel shortfall isn't worth a warning.
            var e = new Element { Name = name, Text = text, Size = size, Floor = floor, BelowFloor = size < floor - FloorGracePx };
            Elements.Add(e);
            return e;
        }

        public void Warn(string message)
        {
            Warnings.Add(message);
            Debug.LogWarning("[Cards] " + message);
        }

        public bool WarnedAbout(string elementName)
        {
            foreach (string w in Warnings)
                if (w.IndexOf(elementName, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public void LogSummary()
        {
            var sb = new StringBuilder("[Cards] ").Append(Card).Append(':');
            foreach (Element e in Elements) sb.Append(' ').Append(e.Name).Append(' ').Append(e.Size.ToString("F0")).Append("px");
            sb.Append($" (group {GroupHeight:F0} of {InnerHeight:F0}px)");
            Debug.Log(sb.ToString());
        }
    }

    /// <summary>Short card label for logs: type + the first words of its main text.</summary>
    public static string CardLabel(string type, string text)
    {
        string t = (text ?? string.Empty).Replace('\n', ' ').Trim();
        if (t.Length > 40) t = t.Substring(0, 40).TrimEnd() + "…";
        return $"{type} card \"{t}\"";
    }

    // =========================================================================
    // Shared layout: an auto-fit body over fixed attribution rows
    // =========================================================================

    public sealed class Row
    {
        public string Name;
        public TMP_Text Text;
        public float SizePx;     // reference px
        public float FloorPx;    // reference px
        public float GapAbovePx; // reference px
        public float Height;     // row height, card px (≥ MinHeight)
        public float TextHeight; // measured text height, card px
        public float Size;       // card px
        public float Indent;     // card px, left inset beyond the padding (logo)
        public float MinHeight;  // card px (logo row)
        public bool Active => Text != null && Text.gameObject.activeSelf && !string.IsNullOrEmpty(Text.text);
    }

    /// <summary>
    /// Lays out <paramref name="body"/> (auto-fit between min and max, warn
    /// floor, hard floor) above the attribution <paramref name="rows"/> (fixed
    /// sizes, wrapped to the inner width), as one group centred vertically in the
    /// card's inner area. Returns the top of the body in card space (y down).
    /// </summary>
    public static float LayoutBodyGroup(RectTransform card, Report report,
        Row body, float minPx, float maxPx, float warnFloorPx, List<Row> rows)
    {
        Vector2 size = CardSize(card);
        float k = ScaleFor(size);
        float pad = PaddingFor(size);
        float innerW = size.x - 2f * pad;
        float innerH = size.y - 2f * pad;

        // Attribution rows: fixed sizes, measured once.
        float fixedH = 0f;
        foreach (Row r in rows)
        {
            if (!r.Active) continue;
            r.Size = r.SizePx * k;
            r.TextHeight = MeasureHeight(r.Text, r.Size, innerW - r.Indent);
            r.Height = Mathf.Max(r.MinHeight, r.TextHeight);
            fixedH += r.GapAbovePx * k + r.Height;
        }

        string bodyText = body.Text.text;
        TMP_FontAsset bodyFont = body.Text.font;
        BodyFit fit = FitBody(
            s => MeasureHeight(body.Text, s, innerW) + fixedH <= innerH + 0.5f &&
                 WidestWordWidth(bodyFont, bodyText, s) <= innerW,
            minPx * k, maxPx * k, warnFloorPx * k, HardFloorPx * k);

        body.Size = fit.Size;
        body.Height = MeasureHeight(body.Text, body.Size, innerW);
        report.Add(body.Name, body.Text, body.Size / k, warnFloorPx);
        if (fit.Overflows)
            report.Warn($"{report.Card}: {body.Name} doesn't fit even at {HardFloorPx:F0} px — it overflows the card's padding.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: {body.Name} fits only at {body.Size / k:F0} px — below the {warnFloorPx:F0} px floor.");

        float groupH = body.Height + fixedH;
        float top = pad + Mathf.Max(0f, (innerH - groupH) * 0.5f);
        report.GroupHeight = groupH;
        report.InnerHeight = innerH;

        PlaceRow(body.Text, pad, top, body.Height);
        float y = top + body.Height;
        foreach (Row r in rows)
        {
            if (!r.Active) continue;
            y += r.GapAbovePx * k;
            r.Text.fontSize = r.Size;
            PlaceBox(r.Text, pad + r.Indent, y + (r.Height - r.TextHeight) * 0.5f, -1f, r.TextHeight, pad);
            Element e = report.Add(r.Name, r.Text, r.Size / k, r.FloorPx);
            if (e.BelowFloor) report.Warn($"{report.Card}: {r.Name} at {e.Size:F0} px — below its {r.FloorPx:F0} px floor.");
            y += r.Height;
        }
        return top;
    }
}

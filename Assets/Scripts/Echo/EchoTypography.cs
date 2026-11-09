using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MugsTech.Echo
{
    /// <summary>
    /// The Echo's look and motion, in reference pixels of the 1920 × 1080
    /// frame: the thumbnail's own typography (Anton SC, capitals, white, the
    /// accent in #E63946, a hard black outline 5 % of the cap height) in the
    /// strip above the cards.
    ///
    /// THE BAND — from action-safe (3.5 %) down to the cards' line (11 %, the
    /// chapter titles' band bottom): 81 px. Title-safe (5 %) would leave 65 px,
    /// too little for the brief's 72 px minimum cap height, so the top edge
    /// uses action-safe instead (the creator's call). The glyph box — caps
    /// plus the outline above and below — is centred in it.
    ///
    /// THE SIZE — as large as the band allows (73.6 px caps; the brief's
    /// 110 px ceiling can't be reached inside it), smaller only if the line
    /// would pass 70 % of the frame's width; under 72 px that is reported.
    ///
    /// THE MOTION — slides down from above the frame and settles in 0.25 s
    /// with a slight overshoot (≈4 % of the travel), holds, slides back up in
    /// 0.2 s.
    /// </summary>
    public static class EchoTypography
    {
        public const string AccentHex = "#E63946";
        public static readonly Color Accent = new Color(230f / 255f, 57f / 255f, 70f / 255f);

        /// <summary>The band's top: action-safe (3.5 % of the height).</summary>
        public const float BandTop = 0.035f;
        /// <summary>The band's bottom: the cards' line (same as the chapter titles' band).</summary>
        public const float BandBottom = 0.11f;
        public const float MaxWidthFraction = 0.70f;
        public const float MinCapPx = 72f, MaxCapPx = 110f;
        /// <summary>The outline's thickness as a share of the cap height (the thumbnail: 8 px at 200 px type).</summary>
        public const float OutlineOfCap = 0.05f;
        /// <summary>Anton SC's cap height / em (1760 / 2048), used when the font asset can't say.</summary>
        public const float AntonCapRatio = 0.859375f;

        public const float SlideInSeconds = 0.25f;
        public const float SlideOutSeconds = 0.2f;
        /// <summary>The slide-in's back-ease constant: ≈4 % overshoot.</summary>
        public const float OvershootS = 1.1f;

        /// <summary>An Echo whose tag fires later than this (voice seconds) wasn't written at the top.</summary>
        public const float TopOfScriptSeconds = 1.0f;

        public struct Layout
        {
            public float FontSize;      // TMP font size (reference px)
            public float CapPx;
            public float OutlinePx;
            public float BaselineY;     // px from the frame's top
            public float WidthPx;       // the line, outline included
            public float BoxTop, BoxBottom; // the glyph box (caps + outline), px from the top
            public bool WidthBound;     // the 70 % width cap set the size
            public bool UnderMinimum;   // …and pushed it under 72 px caps
        }

        /// <summary>
        /// The fitted layout for a line whose width is
        /// <paramref name="widthPerFontUnit"/> × font size (TMP's preferred width
        /// at size 1; a single line scales linearly) in a frame of the given size.
        /// </summary>
        public static Layout Fit(float widthPerFontUnit, float capRatio, float frameW = 1920f, float frameH = 1080f)
        {
            float bandTop = BandTop * frameH, bandBottom = BandBottom * frameH;
            float capByBand = (bandBottom - bandTop) / (1f + 2f * OutlineOfCap);
            float maxW = MaxWidthFraction * frameW;
            float perFont = Mathf.Max(1e-4f, widthPerFontUnit + 2f * OutlineOfCap * capRatio);
            float capByWidth = maxW / perFont * capRatio;

            var l = new Layout();
            l.CapPx = Mathf.Min(MaxCapPx, capByBand, capByWidth);
            l.WidthBound = capByWidth < Mathf.Min(MaxCapPx, capByBand);
            l.UnderMinimum = l.CapPx < MinCapPx - 1e-3f;
            l.FontSize = l.CapPx / capRatio;
            l.OutlinePx = OutlineOfCap * l.CapPx;
            l.WidthPx = perFont * l.FontSize;
            float box = l.CapPx + 2f * l.OutlinePx;
            l.BoxTop = bandTop + (bandBottom - bandTop - box) * 0.5f;
            l.BoxBottom = l.BoxTop + box;
            l.BaselineY = l.BoxTop + l.OutlinePx + l.CapPx;
            return l;
        }

        /// <summary>The slide-in's progress at <paramref name="t"/> seconds: 0 (above the frame) → 1 (rest), overshooting a little.</summary>
        public static float SlideIn(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= SlideInSeconds) return 1f;
            float x = t / SlideInSeconds - 1f;
            return 1f + (OvershootS + 1f) * x * x * x + OvershootS * x * x;
        }

        /// <summary>The slide-out's progress: 0 (rest) → 1 (above the frame), accelerating away.</summary>
        public static float SlideOut(float t)
        {
            float x = Mathf.Clamp01(t / SlideOutSeconds);
            return x * x * x;
        }

        // ---------------------------------------------------------------------
        // The font, and the outline as a TMP material
        // ---------------------------------------------------------------------

        const string AntonPath = "Fonts/AntonSC-Regular";
        const int SamplingPointSize = 90, AtlasPadding = 9;
        static TMP_FontAsset s_Anton;
        static bool s_AntonFailed;

        /// <summary>Anton SC (SIL OFL 1.1, Resources/Fonts/AntonSC-OFL.txt) as a runtime TMP font. Null if it can't load.</summary>
        public static TMP_FontAsset Anton
        {
            get
            {
                if (s_Anton != null) return s_Anton;
                if (s_AntonFailed) return null;
                Font source = Resources.Load<Font>(AntonPath);
                if (source != null)
                {
                    try
                    {
                        s_Anton = TMP_FontAsset.CreateFontAsset(source, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                                                                1024, 1024, AtlasPopulationMode.Dynamic, true);
                        if (s_Anton != null)
                        {
                            s_Anton.name = "Anton SC (runtime)";
                            s_Anton.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ?!.,:;'’\"$%&-+#@/()", out _);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Echo] Could not build the Anton SC font asset: {e.Message}");
                    }
                }
                if (s_Anton == null)
                {
                    s_AntonFailed = true;
                    Debug.LogWarning("[Echo] Anton SC is missing (Resources/Fonts/AntonSC-Regular.ttf) — the Echo uses the default font.");
                }
                return s_Anton;
            }
        }

        /// <summary>Cap height / font size for <paramref name="font"/> (Anton's 0.859 if the asset can't say).</summary>
        public static float CapRatio(TMP_FontAsset font)
        {
            if (font != null && font.faceInfo.pointSize > 0 && font.faceInfo.capLine > 0)
                return font.faceInfo.capLine / font.faceInfo.pointSize;
            return AntonCapRatio;
        }

        /// <summary>
        /// The TMP outline width for a hard outline of <see cref="OutlineOfCap"/>
        /// × cap height outside the glyph, with the face dilated by the same
        /// amount so the white letters keep their size. The SDF outline sits
        /// half inside, half outside the (dilated) edge and TMP scales both by
        /// (G − 1) / G, G = padding + 1 — so width × that × G sampling pixels
        /// lie outside the original edge.
        /// </summary>
        public static float OutlineWidth(float capRatio, int samplingPointSize = SamplingPointSize, int padding = AtlasPadding)
        {
            float g = padding + 1f;
            float ratioA = (g - 1f) / g;                 // TMP's ScaleRatioA while dilate + outline ≤ 1
            float wantSamplingPx = OutlineOfCap * capRatio * samplingPointSize;
            return Mathf.Clamp(wantSamplingPx / (ratioA * g), 0f, 0.5f);
        }

        /// <summary>A material instance of <paramref name="font"/> with the hard black outline.</summary>
        public static Material OutlineMaterial(TMP_FontAsset font, float capRatio)
        {
            var mat = new Material(font.material) { name = "Echo Outline" };
            float w = OutlineWidth(capRatio);
            mat.SetColor("_OutlineColor", Color.black);
            mat.SetFloat("_OutlineWidth", w);
            mat.SetFloat("_FaceDilate", w);
            mat.SetFloat("_OutlineSoftness", 0f);
            mat.EnableKeyword("OUTLINE_ON");
            return mat;
        }
    }
}

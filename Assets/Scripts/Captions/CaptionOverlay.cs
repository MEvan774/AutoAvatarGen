using System.Collections.Generic;
using System.Text;
using MugsTech.Background;
using TMPro;
using UnityEngine;

namespace MugsTech.Captions
{
    /// <summary>
    /// Captions in the gaps: the script's own words on screen while the frame is
    /// otherwise bare, a whole phrase or nothing: one only starts on its first
    /// word, with nothing due before its last (<see cref="CaptionGate"/>), one phrase at a time
    /// (<see cref="CaptionTrack"/>, built by SegmentSequencer), in a band along
    /// the bottom under Mugs's feet (<see cref="CaptionZones"/>), the word being
    /// spoken in the accent colour when that phrase's timing is believable.
    ///
    /// Main menu "Captions in gaps" (default on) and "Highlight words"
    /// (default on). Off: nothing is made.
    ///
    /// Read each LateUpdate, after the trackers have shown or hidden this
    /// frame's visuals, so a card arriving hides the caption on its own frame.
    /// </summary>
    public class CaptionOverlay : MonoBehaviour
    {
        public const string PrefKey = "AutoAvatarGen.CaptionsInGaps";
        public const string HighlightPrefKey = "AutoAvatarGen.CaptionsHighlightWords";
        /// <summary>Under the Echo and chapter titles (31500) and black (32000); cards never share the frame.</summary>
        public const int SortOrder = 31400;
        public const float MaxFontPx = 60f, MinFontPx = 52f;
        public const string AccentHex = "#E63946";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool HighlightEnabled
        {
            get => PlayerPrefs.GetInt(HighlightPrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(HighlightPrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>This take's captions, for the result JSON (stays after the scene).</summary>
        public sealed class Report
        {
            public bool Enabled, Highlight;
            public int Phrases;              // captionable phrases built (the first line not counted)
            public int Shown;                // phrases that were on screen
            public int HighlightedPhrases;   // …with the spoken word highlighted
            public int FallbackPhrases;      // …timed from the bracketing T= stamps
            public int SkippedLate;          // the frame came free only after the phrase had started
            public int SkippedAhead;         // something was due before its last word
            public int Interrupted;          // hidden before its last word by something unannounced
            public float ShortestShownSeconds = float.NaN;   // shortest time a phrase was up (NaN: none shown)
            public string Note;
        }

        public static Report Last { get; private set; }

        MediaPresentationSystem mps;
        CaptionTrack track;
        readonly CaptionGate gate = new CaptionGate();
        Report report;   // this take's (Last can already belong to the next one when this is destroyed)
        System.Func<float, float> nextStop;
        readonly HashSet<CaptionPhrase> counted = new HashSet<CaptionPhrase>();
        bool highlight;

        RectTransform overlay, box;
        TextMeshProUGUI text;

        CaptionPhrase shown;
        int shownWord = int.MinValue;

        /// <summary>Builds the captions for this take, hidden. Null when off, or the take has no word timings.</summary>
        public static CaptionOverlay Create(MediaPresentationSystem presentation, AudioClip audio)
        {
            Last = new Report { Enabled = Enabled, Highlight = HighlightEnabled };
            CaptionTrack t = audio != null && SegmentSequencer.LastCaptionsClip == audio ? SegmentSequencer.LastCaptions : null;
            if (t == null)
            {
                Last.Note = "no word timings for this take";
                if (Last.Enabled) Debug.Log("[Captions] This take has no word timings (word_timestamps/) — no captions.");
                return null;
            }
            foreach (CaptionPhrase p in t.Phrases) if (!p.Excluded) Last.Phrases++;
            if (!Last.Enabled)
            {
                Last.Note = "off";
                Debug.Log("[Captions] Captions in gaps are off (main menu).");
                return null;
            }
            if (presentation == null || presentation.mediaCanvas == null)
            {
                Last.Note = "no media canvas";
                return null;
            }
            var c = CreateOn(presentation.mediaCanvas.transform, t);
            c.mps = presentation;
            c.report = Last;
            c.nextStop = presentation.NextCaptionStop;
            c.transform.SetParent(presentation.transform, false);
            return c;
        }

        /// <summary>For the self-tests and previews: captions on <paramref name="canvas"/>, driven by hand (<see cref="ShowPhrase"/>).</summary>
        public static CaptionOverlay CreateOn(Transform canvas, CaptionTrack t)
        {
            var c = new GameObject("CaptionOverlay").AddComponent<CaptionOverlay>();
            c.track = t;
            c.highlight = HighlightEnabled;
            c.Build(canvas);
            return c;
        }

        void Build(Transform host)
        {
            var container = new GameObject("Captions_Container", typeof(RectTransform), typeof(Canvas));
            container.transform.SetParent(host, false);
            overlay = container.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Canvas sub = container.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = SortOrder;

            var boxGo = new GameObject("Caption", typeof(RectTransform));
            boxGo.transform.SetParent(overlay, false);
            box = boxGo.GetComponent<RectTransform>();
            text = boxGo.AddComponent<TextMeshProUGUI>();
            CardTypography.ApplyWeight(text, CardTypography.Weight.Heavy);   // the card font, heavy
            text.raycastTarget = false;
            text.richText = true;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Bottom;   // a second line grows upward; the bottom line stays put
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            if (text.font != null)
            {
                // A soft dark shadow so white reads on any backdrop.
                var mat = new Material(text.font.material) { name = "Caption Shadow" };
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.85f));
                mat.SetFloat("_UnderlayOffsetX", 0.35f);
                mat.SetFloat("_UnderlayOffsetY", -0.45f);
                mat.SetFloat("_UnderlayDilate", 0.35f);
                mat.SetFloat("_UnderlaySoftness", 0.55f);
                text.fontSharedMaterial = mat;
            }
            boxGo.SetActive(false);
        }

        void OnDestroy()
        {
            gate.Finish(TakeLoudness.VoiceTime);
            CopyCounts();
            if (text != null && text.fontSharedMaterial != null && text.fontSharedMaterial.name == "Caption Shadow")
                Destroy(text.fontSharedMaterial);
            if (overlay != null) Destroy(overlay.gameObject);
        }

        // ---------------------------------------------------------------------
        // Each frame
        // ---------------------------------------------------------------------

        void LateUpdate()
        {
            if (mps == null || track == null) return;
            float t = TakeLoudness.VoiceTime;
            CaptionPhrase p = gate.Step(Time.deltaTime, FrameBare(), t, track, nextStop);
            if (gate.LastVerdict != null)
                Debug.Log($"[Captions] {gate.LastVerdictPhrase.Start:F2}-{gate.LastVerdictPhrase.End:F2} " +
                          $"\"{gate.LastVerdictPhrase.Text}\": {gate.LastVerdict}");
            ShowPhrase(p, t);
            CopyCounts();
        }

        void CopyCounts()
        {
            if (report == null) return;
            report.SkippedLate = gate.SkippedLate;
            report.SkippedAhead = gate.SkippedAhead;
            report.Interrupted = gate.Interrupted;
            report.ShortestShownSeconds = gate.ShortestShownSeconds;
        }

        // Nothing else on the frame: the voice running, no card (on screen or
        // queued), image, video, full-screen card, Echo, chapter title,
        // black or transition, the presenter standing still — and the camera
        // at rest: no zoom run (any step, or the glide in or out), no held
        // close-up. A push-in is a deliberately clean frame, so it stays
        // text-free (creator, 2026-11-09; IsCameraBusy is the same test the
        // idle drift uses).
        bool FrameBare()
        {
            if (!TakeLoudness.VoiceStarted) return false;
            ContentZoneController cards = mps.contentZoneController;
            if (cards != null && cards.HasActiveOrQueuedCard) return false;
            if (mps.IsShowingMedia || mps.IsBlackShowing || mps.IsCameraBusy) return false;
            if (mps.IsPresenterGliding) return false;
            if (mps.IsEchoShowing || mps.IsChapterTitleShowing) return false;
            if (mps.screenTransitionController != null && mps.screenTransitionController.IsBusy) return false;
            return true;
        }

        /// <summary>Shows <paramref name="p"/> at take time <paramref name="t"/> (null hides).</summary>
        public void ShowPhrase(CaptionPhrase p, float t)
        {
            if (p == null)
            {
                if (shown != null) box.gameObject.SetActive(false);
                shown = null;
                return;
            }
            int word = highlight ? p.WordAt(t) : -1;
            bool newPhrase = p != shown;
            if (!newPhrase && word == shownWord) return;

            text.text = Markup(p, word);
            if (newPhrase)
            {
                Place(CaptionZones.Bottom, p);
                if (counted.Add(p) && Last != null)
                {
                    Last.Shown++;
                    if (p.Timing == PhraseTiming.Words && highlight) Last.HighlightedPhrases++;
                    if (p.Timing == PhraseTiming.Fallback) Last.FallbackPhrases++;
                }
            }
            box.gameObject.SetActive(true);
            shown = p;
            shownWord = word;
        }

        /// <summary>The phrase as TMP rich text: words as written, the spoken one in the accent colour.</summary>
        public static string Markup(CaptionPhrase p, int highlighted)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Words.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                if (i == highlighted) sb.Append("<color=").Append(AccentHex).Append('>');
                sb.Append("<noparse>").Append(p.Display(i)).Append("</noparse>");
                if (i == highlighted) sb.Append("</color>");
            }
            return sb.ToString();
        }

        // The band's box, and the largest size from 60 down to 52 px that keeps
        // the phrase on one line; a phrase too long for one line at 52 wraps
        // (at the largest size that keeps it to two lines, growing upward).
        void Place(CaptionZones.Zone z, CaptionPhrase p)
        {
            box.anchorMin = new Vector2(z.XMin, 1f - z.YMax);
            box.anchorMax = new Vector2(z.XMax, 1f - z.YMin);
            box.offsetMin = box.offsetMax = Vector2.zero;

            Rect frame = overlay.rect;
            float w = z.Width * (frame.width > 1f ? frame.width : 1920f);
            string plain = Markup(p, -1);
            float size = MaxFontPx;
            bool oneLine = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            for (; size >= MinFontPx; size -= 2f)
            {
                text.fontSize = size;
                if (text.GetPreferredValues(plain, 0f, 0f).x <= w) { oneLine = true; break; }
            }
            if (!oneLine)
            {
                // Wrapped in the box itself (its text is this phrase already), counting lines.
                text.textWrappingMode = TextWrappingModes.Normal;
                for (size = MaxFontPx; size > MinFontPx; size -= 2f)
                {
                    text.fontSize = size;
                    text.ForceMeshUpdate(true, true);
                    if (text.textInfo.lineCount <= CaptionZones.MaxLines) break;
                }
            }
            text.fontSize = Mathf.Max(MinFontPx, size);
            FitSize = text.fontSize;
        }

        /// <summary>The size the last phrase was set at (for the self-tests).</summary>
        public float FitSize { get; private set; }
        public TextMeshProUGUI Text => text;
        public RectTransform Box => box;
    }
}

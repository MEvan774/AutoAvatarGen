using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MugsTech.Echo
{
    /// <summary>
    /// The <c>{Echo}</c> on screen: the thumbnail's headline in the band above
    /// the cards from the video's first frame (<see cref="EchoTypography"/>).
    /// Made by MediaPresentationSystem when it parses the script; started by
    /// <see cref="BeginAtFirstFrame"/> on the frame the voice and the recorder
    /// start. An Echo written further down the script (its T= past
    /// <see cref="EchoTypography.TopOfScriptSeconds"/>) waits for its time on
    /// the voice clock instead, with a warning.
    ///
    /// Slides in (0.25 s), holds its seconds, slides out (0.2 s). Anything that
    /// takes over the whole frame — a full-screen card (`,bigCenter` headline,
    /// BigText, BigMedia, BigImage, Versus), {Black}, a transition, the
    /// close-up — sends it out early. Side cards don't (other zone).
    ///
    /// Main menu "Thumbnail echo" (default on). Off: nothing is made.
    /// Time base: Time.deltaTime, like the cards.
    /// </summary>
    public class EchoOverlay : MonoBehaviour
    {
        public const string PrefKey = "AutoAvatarGen.ThumbnailEcho";
        /// <summary>The chapter titles' layer: above the cards, under black and transitions.</summary>
        public const int SortOrder = 31500;

        /// <summary>Main menu "Thumbnail echo" (default on).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>This take's Echo, for the result JSON (stays after the scene).</summary>
        public sealed class Report
        {
            public bool Enabled;
            public string Headline, Accent;
            public bool AccentShown;
            public float Duration;
            public bool Shown;
            public bool AtFirstFrame;
            public float CapPx;
            public bool WidthBound;
            public string EndedBy;
            public readonly List<string> Warnings = new List<string>();
        }

        public static Report Last { get; private set; }

        enum Phase { Waiting, In, Hold, Out, Done }

        MediaPresentationSystem mps;
        AudioSource voice;
        EchoTag.Spec spec;
        RectTransform overlay, line;
        TextMeshProUGUI text;
        EchoTypography.Layout layout;
        Phase phase = Phase.Waiting;
        bool begun, atTop;
        float clock, outFrom;

        /// <summary>
        /// Takes the script's Echo (already parsed) and builds it, hidden. Null
        /// when there is none, the switch is off, or there's no canvas.
        /// </summary>
        public static EchoOverlay Create(MediaPresentationSystem presentation, AudioSource voice, EchoTag.Result parsed)
        {
            Last = new Report { Enabled = Enabled };
            if (parsed != null)
                foreach (string w in parsed.Warnings) { Last.Warnings.Add(w); Debug.LogWarning("[Echo] " + w); }
            if (parsed == null || parsed.Echo == null || parsed.Echo.Words == 0) return null;

            EchoTag.Spec s = parsed.Echo;
            Last.Headline = s.Headline;
            Last.Accent = s.Accent;
            Last.AccentShown = s.AccentShown;
            Last.Duration = s.Duration;
            if (!Last.Enabled)
            {
                Debug.Log("[Echo] Thumbnail echo is off (main menu) — none shown.");
                return null;
            }
            if (presentation == null || presentation.mediaCanvas == null)
            {
                Last.Warnings.Add("no media canvas");
                Debug.LogWarning("[Echo] No media canvas — the Echo can't be drawn.");
                return null;
            }

            var go = new GameObject("EchoOverlay");
            go.transform.SetParent(presentation.transform, false);
            var e = go.AddComponent<EchoOverlay>();
            e.mps = presentation;
            e.voice = voice;
            e.spec = s;
            e.atTop = float.IsNaN(s.Time) || s.Time <= EchoTypography.TopOfScriptSeconds;
            if (!e.atTop)
            {
                string w = $"{{Echo}} fires at {s.Time:F2}s, not at the top of the cold open — it shows then, not from the first frame.";
                Last.Warnings.Add(w);
                Debug.LogWarning("[Echo] " + w);
            }
            e.Build(presentation.mediaCanvas.transform);
            return e;
        }

        /// <summary>The frame the voice and the recorder start: an Echo at the top of the script slides in now.</summary>
        public void BeginAtFirstFrame() => begun = true;

        /// <summary>
        /// For the self-tests and previews: the Echo built on <paramref name="canvas"/>
        /// with no presentation system (it never starts by itself).
        /// </summary>
        public static EchoOverlay CreateOn(Transform canvas, EchoTag.Spec s)
        {
            var e = new GameObject("EchoOverlay").AddComponent<EchoOverlay>();
            e.spec = s;
            e.atTop = true;
            e.Build(canvas);
            return e;
        }

        /// <summary>For previews: the line at its resting place.</summary>
        public void ShowAtRest()
        {
            line.gameObject.SetActive(true);
            Place(1f);
        }

        /// <summary>Builds the line on <paramref name="host"/> (public so the self-tests and previews can lay it out).</summary>
        public void Build(Transform host)
        {
            var container = new GameObject("Echo_Container", typeof(RectTransform), typeof(Canvas));
            container.transform.SetParent(host, false);
            overlay = container.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Canvas sub = container.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = SortOrder;

            var lineGo = new GameObject("Echo_Headline", typeof(RectTransform));
            lineGo.transform.SetParent(overlay, false);
            line = lineGo.GetComponent<RectTransform>();
            text = lineGo.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Baseline;   // the baseline sits on the rect's middle
            text.color = Color.white;
            text.characterSpacing = 0f;

            TMP_FontAsset font = EchoTypography.Anton;
            if (font != null) text.font = font;
            float capRatio = EchoTypography.CapRatio(text.font);
            if (text.font != null) text.fontSharedMaterial = EchoTypography.OutlineMaterial(text.font, capRatio);
            text.text = spec.Markup;

            // Measure at size 100 — one line scales linearly — then fit.
            Vector2 frame = FrameSize();
            text.fontSize = 100f;
            float perUnit = text.GetPreferredValues(spec.Markup, 0f, 0f).x / 100f;
            layout = EchoTypography.Fit(perUnit, capRatio, frame.x, frame.y);
            text.fontSize = layout.FontSize;

            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 0.5f);
            line.sizeDelta = new Vector2(0f, layout.CapPx * 3f);
            Place(0f);

            if (Last != null)
            {
                Last.CapPx = layout.CapPx;
                Last.WidthBound = layout.WidthBound;
            }
            Debug.Log($"[Echo] \"{spec.Plain}\"" + (spec.AccentShown ? $" (accent \"{spec.Accent}\")" : " (no accent)") +
                      $": caps {layout.CapPx:F1} px, line {layout.WidthPx:F0} px of {frame.x:F0}, " +
                      $"y {layout.BoxTop:F0}–{layout.BoxBottom:F0} px, holds {spec.Duration:0.##} s" +
                      (layout.WidthBound ? " (narrowed to 70 % of the width)" : "") + ".");
            if (layout.UnderMinimum)
            {
                string w = $"Echo \"{spec.Plain}\" is too wide for 72 px caps inside 70 % of the frame — shown at {layout.CapPx:F0} px. Shorten it.";
                if (Last != null) Last.Warnings.Add(w);
                Debug.LogWarning("[Echo] " + w);
            }
            line.gameObject.SetActive(false);
        }

        /// <summary>True while the headline is on screen: sliding in, holding or sliding out.</summary>
        public bool IsShowing => phase == Phase.In || phase == Phase.Hold || phase == Phase.Out;

        /// <summary>
        /// Voice time the Echo is due (<paramref name="now"/> when it is about
        /// to slide in, +∞ once it has been, or never will). The captions don't
        /// start a phrase across it.
        /// </summary>
        public float NextDue(float now)
        {
            if (phase != Phase.Waiting) return float.PositiveInfinity;
            return atTop || float.IsNaN(spec.Time) ? now : Mathf.Max(now, spec.Time);
        }

        public EchoTypography.Layout CurrentLayout => layout;
        public TextMeshProUGUI Text => text;

        /// <summary>Puts the line at <paramref name="progress"/> of the way from above the frame (0) to its rest (1).</summary>
        public void Place(float progress)
        {
            float rest = -layout.BaselineY;
            float above = layout.OutlinePx + layout.FontSize * 0.4f;   // the whole glyph box clear of the top edge
            float y = Mathf.LerpUnclamped(above, rest, progress);
            line.anchoredPosition = new Vector2(0f, y);
        }

        Vector2 FrameSize()
        {
            Rect r = overlay != null ? overlay.rect : default;
            return r.width > 1f && r.height > 1f ? r.size : new Vector2(1920f, 1080f);
        }

        void OnDestroy()
        {
            if (text != null && text.fontSharedMaterial != null && text.fontSharedMaterial.name == "Echo Outline")
                Destroy(text.fontSharedMaterial);
            if (overlay != null) Destroy(overlay.gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.Waiting:
                    if (!begun) return;
                    if (!atTop && (voice == null || !voice.isPlaying || voice.time < spec.Time)) return;
                    if (Takeover(out string blocked))
                    {
                        Finish($"never shown — {blocked} was already on screen");
                        return;
                    }
                    phase = Phase.In;
                    clock = 0f;
                    line.gameObject.SetActive(true);
                    if (Last != null) { Last.Shown = true; Last.AtFirstFrame = atTop; }
                    Debug.Log(atTop ? "[Echo] In from the first frame." : $"[Echo] In at {voice.time:F2}s.");
                    Place(0f);
                    break;

                case Phase.In:
                    clock += dt;
                    Place(EchoTypography.SlideIn(clock));
                    if (CheckTakeover()) break;
                    if (clock >= EchoTypography.SlideInSeconds) { phase = Phase.Hold; clock = 0f; Place(1f); }
                    break;

                case Phase.Hold:
                    clock += dt;
                    if (CheckTakeover()) break;
                    if (clock >= spec.Duration) StartOut(null);
                    break;

                case Phase.Out:
                    clock += dt;
                    Place(Mathf.LerpUnclamped(outFrom, 0f, EchoTypography.SlideOut(clock)));
                    if (clock >= EchoTypography.SlideOutSeconds) Finish(null);
                    break;
            }
        }

        bool CheckTakeover()
        {
            if (!Takeover(out string by)) return false;
            StartOut(by);
            return true;
        }

        void StartOut(string by)
        {
            // From wherever it is now (mid-slide included), straight back up.
            outFrom = phase == Phase.In ? EchoTypography.SlideIn(clock) : 1f;
            phase = Phase.Out;
            clock = 0f;
            if (Last != null) Last.EndedBy = by ?? "its duration";
            Debug.Log(by == null ? "[Echo] Out." : $"[Echo] Out early — {by}.");
        }

        void Finish(string why)
        {
            phase = Phase.Done;
            if (line != null) line.gameObject.SetActive(false);
            if (why != null && Last != null) Last.EndedBy = why;
            if (why != null) Debug.Log($"[Echo] {why}.");
            enabled = false;
        }

        // Whatever takes over the whole frame.
        bool Takeover(out string by)
        {
            by = null;
            if (mps == null) return false;
            ContentZoneController cards = mps.contentZoneController;
            if (cards != null && cards.ActiveCardIsFeature) by = "a full-screen card";
            else if (mps.IsBlackShowing) by = "a {Black}";
            else if (mps.IsCloseUpHeld) by = "the close-up";
            else if (mps.screenTransitionController != null && mps.screenTransitionController.IsBusy) by = "a transition";
            return by != null;
        }
    }
}

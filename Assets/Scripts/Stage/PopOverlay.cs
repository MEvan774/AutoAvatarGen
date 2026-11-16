using System.Collections.Generic;
using MugsTech.Echo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Stage
{
    /// <summary>
    /// <c>{Pop:"phrase"}</c> … <c>{Pop:End}</c> — the line that should land,
    /// with the picture stopped behind it: everything on screen is softened
    /// (dimmed to 40 % of its brightness, presenter, cards and media included),
    /// and one short phrase sits in the centre of the frame in the brand font
    /// on a solid bar in the card accent colour. Each next {Pop:…} REPLACES the
    /// phrase on the frame — they never stack (that is BigText's job).
    /// {Pop:End} fades the phrase (0.15 s) and clears the softening (0.25 s).
    ///
    /// A transition, a {Black} or an extreme close-up ends the run early (its
    /// later phrases are then ignored and its End is a quiet no-op). A run that
    /// is never closed is closed when the narration ends, with a warning.
    /// Cards and media that are up stay up underneath and come back sharp; a
    /// card that comes due during a run waits behind it (ContentZoneController),
    /// and the automatic captions never show during one.
    ///
    /// Made by MediaPresentationSystem when it parses the script; driven by the
    /// voice clock like every tracker. Sizes and motion: <see cref="PopMath"/>.
    /// </summary>
    public class PopOverlay : MonoBehaviour
    {
        MediaPresentationSystem mps;
        AudioSource voice;
        List<StageTags.PopMarker> markers;
        int next;

        RectTransform overlay, bar;
        Image dim, barImage;
        TextMeshProUGUI text;
        CanvasGroup barGroup;

        bool running;          // between the run's first phrase and its End
        bool cleared;          // ended early: ignore the rest until the End
        float runMarkerTime = float.NegativeInfinity;   // the T= of the phrase that opened the run on screen
        bool voiceSeen;
        float dimPeak;
        float dimAlpha, dimFrom, dimTo, dimClock = float.PositiveInfinity;
        float phraseClock = float.PositiveInfinity;   // since the phrase on screen landed
        float fadeClock = float.PositiveInfinity;     // since the phrase started fading out
        int phraseInRun, runLength;

        /// <summary>Phrases put on screen this take.</summary>
        public int Shown { get; private set; }

        /// <summary>True from a run's first phrase to its End (cards wait behind it).</summary>
        public bool IsRunning => running;

        /// <summary>True while the softening or a phrase is still visible (the clearing fade included).</summary>
        public bool IsShowing => running || dimAlpha > 0.002f || (bar != null && bar.gameObject.activeSelf);

        // ---- Read by the self-tests ------------------------------------------------------
        public float DimAlpha => dimAlpha;
        public float DimPeak => dimPeak;
        public RectTransform Bar => bar;
        public TextMeshProUGUI Text => text;
        public float PhraseAlpha => barGroup != null ? barGroup.alpha : 0f;

        /// <summary>The script's phrases (already parsed), hidden. Null when there are none or there is no canvas.</summary>
        public static PopOverlay Create(MediaPresentationSystem presentation, AudioSource voice, StageTags.Parsed<StageTags.PopMarker> parsed)
        {
            if (parsed != null)
                foreach (string w in parsed.Warnings) Debug.LogWarning("[Card] " + w);
            if (parsed == null || parsed.Markers.Count == 0) return null;
            if (presentation == null || presentation.mediaCanvas == null)
            {
                Debug.LogWarning("[Card] No media canvas — {Pop:} phrases can't be drawn.");
                return null;
            }

            var go = new GameObject("PopOverlay");
            go.transform.SetParent(presentation.transform, false);
            var p = go.AddComponent<PopOverlay>();
            p.mps = presentation;
            p.voice = voice;
            p.markers = parsed.Markers;
            p.Build(presentation.mediaCanvas.transform);

            // The brand font's atlas is filled on demand: add every phrase's
            // letters now, before the recorder starts.
            TMP_FontAsset anton = EchoTypography.Anton;
            if (anton != null)
                foreach (StageTags.PopMarker m in parsed.Markers)
                    if (!m.Ends && !string.IsNullOrEmpty(m.Phrase)) anton.TryAddCharacters(m.Phrase, out _);
            return p;
        }

        /// <summary>For the self-tests: an overlay built on <paramref name="canvas"/>, driven by hand.</summary>
        public static PopOverlay CreateOn(Transform canvas)
        {
            var p = new GameObject("PopOverlay").AddComponent<PopOverlay>();
            p.markers = new List<StageTags.PopMarker>();
            p.Build(canvas);
            return p;
        }

        void Build(Transform host)
        {
            dimPeak = PopMath.DimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear);

            var container = new GameObject("Pop_Container", typeof(RectTransform), typeof(Canvas));
            container.transform.SetParent(host, false);
            overlay = container.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Canvas sub = container.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = PopMath.SortOrder;

            var dimGo = new GameObject("Pop_Soften", typeof(RectTransform));
            dimGo.transform.SetParent(overlay, false);
            RectTransform drt = dimGo.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;
            dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0f);
            dim.raycastTarget = false;
            dimGo.SetActive(false);

            var barGo = new GameObject("Pop_Bar", typeof(RectTransform), typeof(CanvasGroup));
            barGo.transform.SetParent(overlay, false);
            bar = barGo.GetComponent<RectTransform>();
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            barGroup = barGo.GetComponent<CanvasGroup>();
            barImage = barGo.AddComponent<Image>();
            barImage.color = ContentCardUIBuilder.AccentColor;
            barImage.raycastTarget = false;

            var textGo = new GameObject("Pop_Phrase", typeof(RectTransform));
            textGo.transform.SetParent(bar, false);
            text = textGo.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = false;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Capline;   // the capitals centred in the bar, not the line box
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            TMP_FontAsset anton = EchoTypography.Anton;
            if (anton != null) text.font = anton;
            else CardTypography.ApplyWeight(text, CardTypography.Weight.Heavy);
            RectTransform trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            barGo.SetActive(false);
        }

        void OnDestroy()
        {
            if (overlay != null) Destroy(overlay.gameObject);
        }

        Vector2 FrameSize()
        {
            Rect r = overlay != null ? overlay.rect : default;
            return r.width > 1f && r.height > 1f ? r.size : new Vector2(1920f, 1080f);
        }

        // ---------------------------------------------------------------------
        // A phrase
        // ---------------------------------------------------------------------

        /// <summary>
        /// Puts <paramref name="phrase"/> on the bar, sized to it — one line,
        /// capitals of 120 px, shrunk if the bar would pass 84 % of the frame.
        /// Returns the font size used (frame px). Public for the self-tests.
        /// </summary>
        public float SetPhrase(string phrase)
        {
            Vector2 frame = FrameSize();
            text.text = phrase ?? "";
            text.fontSize = 100f;
            float perUnit = string.IsNullOrEmpty(text.text) ? 0f : text.GetPreferredValues(text.text, 100000f, 0f).x / 100f;
            float capRatio = EchoTypography.CapRatio(text.font);
            float size = PopMath.FontSize(perUnit, capRatio, frame.x, frame.y);
            text.fontSize = size;

            float cap = size * capRatio;
            bar.sizeDelta = new Vector2(perUnit * size + 2f * PopMath.PadXOfCap * cap, cap + 2f * PopMath.PadYOfCap * cap);
            barImage.color = ContentCardUIBuilder.AccentColor;
            // Centre the capitals in the bar: the line box has more room above them than below.
            text.margin = Vector4.zero;
            return size;
        }

        // ---------------------------------------------------------------------
        // The take
        // ---------------------------------------------------------------------

        void Update()
        {
            bool playing = voice != null && voice.isPlaying;
            if (playing) voiceSeen = true;

            if (playing && markers != null)
            {
                float t = voice.time;
                while (next < markers.Count && t >= markers[next].Time)
                {
                    Fire(markers[next], t);
                    next++;
                }
            }
            else if (voiceSeen && !playing && running)
            {
                Debug.LogWarning("[Card] Narration ended with a {Pop:} run still open — missing {Pop:End}. Closing it.");
                End();
            }

            Advance(Time.deltaTime);
        }

        /// <summary>One frame of the overlay's clocks; public so the self-tests can step it in edit mode.</summary>
        public void Advance(float dt)
        {
            // The softening.
            if (!float.IsPositiveInfinity(dimClock))
            {
                dimClock += dt;
                float seconds = dimTo > dimFrom ? PopMath.SoftenSeconds : PopMath.ClearSeconds;
                float x = Mathf.Clamp01(dimClock / seconds);
                x = x * x * (3f - 2f * x);
                dimAlpha = Mathf.Lerp(dimFrom, dimTo, x);
                if (dimClock >= seconds) { dimAlpha = dimTo; dimClock = float.PositiveInfinity; }
                ApplyDim();
            }

            // The phrase: popping in, or fading out.
            if (bar != null && bar.gameObject.activeSelf)
            {
                if (!float.IsPositiveInfinity(fadeClock))
                {
                    fadeClock += dt;
                    barGroup.alpha = 1f - Mathf.Clamp01(fadeClock / PopMath.PhraseFadeSeconds);
                    if (fadeClock >= PopMath.PhraseFadeSeconds)
                    {
                        bar.gameObject.SetActive(false);
                        fadeClock = float.PositiveInfinity;
                    }
                }
                else if (!float.IsPositiveInfinity(phraseClock))
                {
                    phraseClock += dt;
                    float s = PopMath.PopScaleAt(phraseClock);
                    bar.localScale = new Vector3(s, s, 1f);
                }
            }
        }

        void ApplyDim()
        {
            if (dim == null) return;
            dim.color = new Color(0f, 0f, 0f, dimAlpha);
            bool on = dimAlpha > 0.002f;
            if (dim.gameObject.activeSelf != on) dim.gameObject.SetActive(on);
        }

        void FadeDim(float to)
        {
            dimFrom = dimAlpha;
            dimTo = to;
            dimClock = 0f;
        }

        void Fire(StageTags.PopMarker m, float t)
        {
            if (m.Ends)
            {
                if (cleared) { cleared = false; Debug.Log("{Pop:End} — its run was already ended; nothing to close."); }
                else if (running) End();
                else Debug.Log("{Pop:End} — no Pop is up; nothing to close.");
                return;
            }
            if (cleared)
            {
                Debug.Log($"[Card] Pop \"{m.Phrase}\" ignored — its run was ended early.");
                return;
            }
            if (mps != null && (mps.IsCloseUpHeld || mps.IsCloseUpCutImminent))
            {
                Debug.LogWarning($"[Card] t={t:F2}s Pop \"{m.Phrase}\" came due on a {{Zoom:ExtremeIn}} close-up — blocked.");
                return;
            }
            if (!running) runMarkerTime = m.Time;
            Show(m.Phrase, t);
        }

        /// <summary>Shows a phrase now: opens the run (softening) or replaces the phrase on the frame. Public for the self-tests.</summary>
        public void Show(string phrase, float t = float.NaN)
        {
            if (!running)
            {
                running = true;
                phraseInRun = 0;
                runLength = RunLengthFrom(next);
                FadeDim(dimPeak);
                // The camera comes back to rest under the softening, as under any full-screen moment.
                if (mps != null) mps.EndZoomRun("{Pop}", hiddenBehindCover: false);
            }
            phraseInRun++;
            Shown++;

            SetPhrase(phrase);
            bar.gameObject.SetActive(true);
            barGroup.alpha = 1f;
            fadeClock = float.PositiveInfinity;
            phraseClock = 0f;
            bar.localScale = Vector3.one * PopMath.PopFromScale;

            Debug.Log(float.IsNaN(t)
                ? $"[Card] Pop shown ({phraseInRun} of {Mathf.Max(phraseInRun, runLength)}: \"{phrase}\")"
                : $"[Card] t={t:F2}s Pop shown ({phraseInRun} of {Mathf.Max(phraseInRun, runLength)}: \"{phrase}\")");
        }

        // How many phrases the run starting at marker index `from` holds.
        int RunLengthFrom(int from)
        {
            if (markers == null) return 1;
            int n = 0;
            for (int i = from; i < markers.Count; i++)
            {
                if (markers[i].Ends) break;
                n++;
            }
            return Mathf.Max(1, n);
        }

        /// <summary>{Pop:End}: the phrase fades, the softening clears. Public for the self-tests.</summary>
        public void End()
        {
            if (!running) return;
            running = false;
            if (bar.gameObject.activeSelf && float.IsPositiveInfinity(fadeClock)) fadeClock = 0f;
            FadeDim(0f);
        }

        /// <summary>
        /// A transition's cover, a {Black} or an extreme close-up: the run ends
        /// on this frame (a hard cut). Its later phrases are ignored and its
        /// End does nothing.
        /// </summary>
        public void EndNow(string by, float startedBefore = float.PositiveInfinity)
        {
            if (!IsShowing) return;
            // A run opened ON the closing event's own beat (a {Pop:} written on the
            // {Transition:…} line) belongs to what comes after it: leave it.
            if (running && runMarkerTime >= startedBefore - 0.02f) return;
            if (running)
            {
                cleared = true;
                Debug.Log($"[Card] Pop run ended early — {by}.");
            }
            running = false;
            dimAlpha = 0f;
            dimClock = float.PositiveInfinity;
            ApplyDim();
            fadeClock = float.PositiveInfinity;
            if (bar != null) bar.gameObject.SetActive(false);
        }

        /// <summary>The voice time the next phrase is due (+∞: none left).</summary>
        public float NextDue(float now)
        {
            if (markers == null) return float.PositiveInfinity;
            for (int i = next; i < markers.Count; i++)
                if (!markers[i].Ends && markers[i].Time > now) return markers[i].Time;
            return float.PositiveInfinity;
        }
    }
}

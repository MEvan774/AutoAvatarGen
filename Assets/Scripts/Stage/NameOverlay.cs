using System.Collections.Generic;
using MugsTech.Echo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Stage
{
    /// <summary>
    /// <c>{Name:"name","role",duration|Start}</c> … <c>{Name:End}</c> — who is
    /// on screen: a label in the lower corner of the frame, over a full-frame
    /// photo or clip. The name in the brand font, the role smaller beneath it,
    /// on a solid bar in the card accent colour, as wide as its text (at most
    /// 45 % of the frame). It slides in from the frame's edge (0.3 s) and back
    /// out (0.25 s).
    ///
    /// It sits above images, clips and cards and below the full-screen cards.
    /// It closes or replaces nothing, and only another {Name:} replaces it. It
    /// takes the lower-left corner — the lower-right while Mugs stands or peeks
    /// on the left. Closed by its End, its duration, a transition, a {Black}
    /// or an extreme close-up (its later End is then a quiet no-op).
    ///
    /// Made by MediaPresentationSystem when it parses the script; driven by the
    /// voice clock like every tracker. Sizes and motion: <see cref="NameLabelMath"/>.
    /// </summary>
    public class NameOverlay : MonoBehaviour
    {
        enum Phase { Hidden, In, Hold, Out }

        MediaPresentationSystem mps;
        AudioSource voice;
        List<StageTags.NameMarker> markers;
        int next;

        RectTransform overlay, bar;
        Image barImage;
        TextMeshProUGUI nameText, roleText;

        Phase phase = Phase.Hidden;
        float clock;
        float outFrom = 1f;
        StageTags.NameMarker current;
        bool backdropUnchecked;   // the "nothing behind it" warning is still to be judged
        float heldFor;
        bool onRight;
        bool voiceSeen;
        float barHeight;
        NameLabelMath.Fit fit;

        /// <summary>Labels put on screen this take.</summary>
        public int Shown { get; private set; }

        /// <summary>True while a label is on screen (sliding in, holding or sliding out).</summary>
        public bool IsShowing => phase != Phase.Hidden;

        // ---- Read by the self-tests ------------------------------------------------------
        public RectTransform Bar => bar;
        public TextMeshProUGUI NameText => nameText;
        public TextMeshProUGUI RoleText => roleText;
        public NameLabelMath.Fit CurrentFit => fit;
        public bool IsOnRight => onRight;

        /// <summary>The script's labels (already parsed), hidden. Null when there are none or there is no canvas.</summary>
        public static NameOverlay Create(MediaPresentationSystem presentation, AudioSource voice, StageTags.Parsed<StageTags.NameMarker> parsed)
        {
            if (parsed != null)
                foreach (string w in parsed.Warnings) Debug.LogWarning("[Card] " + w);
            if (parsed == null || parsed.Markers.Count == 0) return null;
            if (presentation == null || presentation.mediaCanvas == null)
            {
                Debug.LogWarning("[Card] No media canvas — {Name:} labels can't be drawn.");
                return null;
            }

            var go = new GameObject("NameOverlay");
            go.transform.SetParent(presentation.transform, false);
            var n = go.AddComponent<NameOverlay>();
            n.mps = presentation;
            n.voice = voice;
            n.markers = parsed.Markers;
            n.Build(presentation.mediaCanvas.transform);

            // The brand font's atlas is filled on demand: add every label's
            // letters now, before the recorder starts.
            TMP_FontAsset anton = EchoTypography.Anton;
            if (anton != null)
                foreach (StageTags.NameMarker m in parsed.Markers)
                    if (!m.Ends && !string.IsNullOrEmpty(m.Name)) anton.TryAddCharacters(m.Name, out _);
            return n;
        }

        /// <summary>For the self-tests: a label built on <paramref name="canvas"/>, driven by hand.</summary>
        public static NameOverlay CreateOn(Transform canvas)
        {
            var n = new GameObject("NameOverlay").AddComponent<NameOverlay>();
            n.markers = new List<StageTags.NameMarker>();
            n.Build(canvas);
            return n;
        }

        void Build(Transform host)
        {
            var container = new GameObject("Name_Container", typeof(RectTransform), typeof(Canvas));
            container.transform.SetParent(host, false);
            overlay = container.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Canvas sub = container.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = NameLabelMath.SortOrder;

            var barGo = new GameObject("Name_Bar", typeof(RectTransform));
            barGo.transform.SetParent(overlay, false);
            bar = barGo.GetComponent<RectTransform>();
            bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);   // placed in px from the frame's top-left
            bar.pivot = new Vector2(0f, 1f);
            barImage = barGo.AddComponent<Image>();
            barImage.color = ContentCardUIBuilder.AccentColor;
            barImage.raycastTarget = false;

            nameText = MakeText("Name", Color.white);
            TMP_FontAsset anton = EchoTypography.Anton;
            if (anton != null) { nameText.font = anton; nameText.fontStyle = FontStyles.Normal; }
            else CardTypography.ApplyWeight(nameText, CardTypography.Weight.Heavy);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;

            roleText = MakeText("Role", new Color(1f, 1f, 1f, 0.92f));
            CardTypography.ApplyWeight(roleText, CardTypography.Weight.Bold);

            bar.gameObject.SetActive(false);
        }

        TextMeshProUGUI MakeText(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(bar, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.richText = false;
            t.color = color;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            t.margin = Vector4.zero;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            return t;
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
        // Laying a label out
        // ---------------------------------------------------------------------

        /// <summary>Fills the label and sizes its bar (public so the self-tests can lay one out).</summary>
        public void SetLabel(string name, string role, bool right)
        {
            Vector2 frame = FrameSize();
            float k = frame.y / 1080f;
            onRight = right;

            // TMP re-reads its rectangle only while it is active: laid out on an
            // inactive bar, a wrapping role kept the 200 px width it was created
            // with and ran to eight lines. So the bar is active while it is sized.
            bool wasActive = bar.gameObject.activeSelf;
            if (!wasActive) bar.gameObject.SetActive(true);

            nameText.text = name ?? "";
            roleText.text = role ?? "";
            roleText.gameObject.SetActive(!string.IsNullOrEmpty(role));

            float namePer = WidthPerUnit(nameText), rolePer = roleText.gameObject.activeSelf ? WidthPerUnit(roleText) : 0f;
            fit = NameLabelMath.FitLabel(namePer, rolePer, frame.x, frame.y);

            float padX = NameLabelMath.PadX * k, padY = NameLabelMath.PadY * k, gap = NameLabelMath.LineGap * k;
            float textW = fit.BarWidth - 2f * padX;

            nameText.fontSize = fit.NameSize;
            float nameH = nameText.GetPreferredValues(nameText.text, 100000f, 0f).y;
            nameText.rectTransform.sizeDelta = new Vector2(textW, nameH);
            nameText.rectTransform.anchoredPosition = new Vector2(padX, -padY);

            float roleH = 0f;
            if (roleText.gameObject.activeSelf)
            {
                roleText.fontSize = fit.RoleSize;
                roleText.textWrappingMode = fit.RoleWraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                roleH = roleText.GetPreferredValues(roleText.text, fit.RoleWraps ? textW : 100000f, 0f).y;
                roleText.rectTransform.sizeDelta = new Vector2(textW, roleH);
                roleText.rectTransform.anchoredPosition = new Vector2(padX, -(padY + nameH + gap));
            }

            barHeight = padY + nameH + (roleH > 0f ? gap + roleH : 0f) + padY;
            bar.sizeDelta = new Vector2(fit.BarWidth, barHeight);
            barImage.color = ContentCardUIBuilder.AccentColor;
            Place(1f);

            if (!wasActive) bar.gameObject.SetActive(false);
        }

        static float WidthPerUnit(TMP_Text t)
        {
            if (string.IsNullOrEmpty(t.text)) return 0f;
            TextWrappingModes wrap = t.textWrappingMode;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontSize = 100f;
            float w = t.GetPreferredValues(t.text, 100000f, 0f).x / 100f;
            t.textWrappingMode = wrap;
            return w;
        }

        /// <summary>Puts the bar <paramref name="progress"/> of the way from outside the frame's edge (0) to its rest (1).</summary>
        public void Place(float progress)
        {
            Vector2 frame = FrameSize();
            float inset = NameLabelMath.SideInsetOfFrame * frame.x;
            float top = NameLabelMath.BottomOfFrame * frame.y - barHeight;
            float w = bar.sizeDelta.x;
            float rest = onRight ? frame.x - inset - w : inset;
            float outside = onRight ? frame.x + 4f : -w - 4f;
            bar.anchoredPosition = new Vector2(Mathf.LerpUnclamped(outside, rest, progress), -top);
        }

        // ---------------------------------------------------------------------
        // The take
        // ---------------------------------------------------------------------

        // LateUpdate, not Update: a label written on the same beat as a
        // {Position:Off} and its {Image:} must see where Mugs ends up on that
        // frame (the trackers are coroutines — they have all run by now). On the
        // first real take it picked the corner for where he had just been.
        void LateUpdate()
        {
            float dt = Time.deltaTime;
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
            else if (voiceSeen && !playing && current != null && current.Duration <= 0f && phase != Phase.Out && phase != Phase.Hidden)
            {
                // The narration is over and nothing will close a held label.
                Debug.LogWarning("[Card] Narration ended with a held {Name:} label still up — missing {Name:End}. Closing it.");
                StartOut();
            }

            switch (phase)
            {
                case Phase.In:
                    clock += dt;
                    Place(NameLabelMath.SlideIn(clock));
                    if (clock >= NameLabelMath.SlideInSeconds) { phase = Phase.Hold; clock = 0f; Place(1f); CheckBackdrop(); }
                    FollowMugs();
                    break;

                case Phase.Hold:
                    heldFor += dt;
                    FollowMugs();
                    if (current != null && current.Duration > 0f && heldFor >= current.Duration) StartOut();
                    break;

                case Phase.Out:
                    clock += dt;
                    Place(Mathf.LerpUnclamped(outFrom, 0f, NameLabelMath.SlideOut(clock)));
                    if (clock >= NameLabelMath.SlideOutSeconds) Hide();
                    break;
            }
        }

        void Fire(StageTags.NameMarker m, float t)
        {
            if (m.Ends)
            {
                if (phase == Phase.Hidden || phase == Phase.Out || current == null)
                    Debug.Log("{Name:End} — no label is up; nothing to close.");
                else
                    StartOut();
                return;
            }

            // Nothing is shown over the extreme close-up.
            if (mps != null && (mps.IsCloseUpHeld || mps.IsCloseUpCutImminent))
            {
                Debug.LogWarning($"[Card] t={t:F2}s Name \"{m.Name}\" came due on a {{Zoom:ExtremeIn}} close-up — blocked.");
                return;
            }

            bool right = mps != null && NameLabelMath.OnRight(mps.CurrentPosition);
            current = m;
            heldFor = 0f;
            SetLabel(m.Name, m.Role, right);
            bar.gameObject.SetActive(true);
            phase = Phase.In;
            clock = 0f;
            Place(0f);
            Shown++;

            Debug.Log($"[Card] t={t:F2}s Name shown (\"{m.Name}\", {(right ? "lower right" : "lower left")})");
            if (fit.NameBelowFloor)
                Debug.LogWarning($"[Card] Name \"{m.Name}\" only fits the label at {fit.NameSize / (FrameSize().y / 1080f):F0} px — under the " +
                                 $"{NameLabelMath.NameMinPx:F0} px floor. Shorten it.");

            // It is meant to sit on a photo, a clip or a card — judged once it has
            // slid in (CheckBackdrop): its picture is often written on the same
            // beat and needs a few frames to arrive.
            backdropUnchecked = true;
        }

        void CheckBackdrop()
        {
            if (!backdropUnchecked || mps == null || current == null) return;
            backdropUnchecked = false;
            ContentZoneController cards = mps.contentZoneController;
            bool something = mps.IsShowingMedia || mps.IsMediaFullFrame || (cards != null && cards.HasActiveOrQueuedCard);
            if (!something)
                Debug.LogWarning($"[Card] Name \"{current.Name}\" is up with nothing but the background behind it — " +
                                 "put it on the lines an {Image:}, a {Video:} or a card covers.");
        }

        // Mugs came up (or moved) on the label's side: it changes corner.
        void FollowMugs()
        {
            if (mps == null || current == null) return;
            bool right = NameLabelMath.OnRight(mps.CurrentPosition);
            if (right == onRight) return;
            SetLabel(current.Name, current.Role, right);
            phase = Phase.In;
            clock = 0f;
            Place(0f);
        }

        void StartOut()
        {
            if (phase == Phase.Hidden || phase == Phase.Out) return;
            outFrom = phase == Phase.In ? NameLabelMath.SlideIn(clock) : 1f;
            phase = Phase.Out;
            clock = 0f;
        }

        void Hide()
        {
            phase = Phase.Hidden;
            current = null;
            if (bar != null) bar.gameObject.SetActive(false);
        }

        /// <summary>
        /// A transition's cover, a {Black} or an extreme close-up: the label goes
        /// on this frame (a hard cut, like everything else those take with them).
        /// </summary>
        public void CloseNow(string by, float startedBefore = float.PositiveInfinity)
        {
            if (phase == Phase.Hidden) return;
            // A label written ON the closing event's own beat (on the
            // {Transition:…} line) belongs to what comes after it: leave it.
            if (current != null && current.Time >= startedBefore - 0.02f) return;
            Debug.Log($"[Card] Name \"{(current != null ? current.Name : "")}\" closed early — {by}.");
            Hide();
        }

        /// <summary>The voice time the next label is due (+∞: none left) — the captions don't start a phrase across it.</summary>
        public float NextDue(float now)
        {
            if (markers == null) return float.PositiveInfinity;
            for (int i = next; i < markers.Count; i++)
                if (!markers[i].Ends && markers[i].Time > now) return markers[i].Time;
            return float.PositiveInfinity;
        }
    }
}

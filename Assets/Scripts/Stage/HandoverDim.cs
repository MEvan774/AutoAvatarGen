using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Stage
{
    /// <summary>
    /// The sheet that dims what Mugs stands in front of during a handover
    /// ({Position:…,Over}): a black layer over the whole frame, above the cards
    /// and below him, taking the scene to <see cref="StageMath.HandoverBrightness"/>
    /// so he reads against a full-screen card. It fades in with his pop-up and
    /// out with whatever was under him. Built on the media canvas (the one the
    /// recorder's camera captures), like the {Pop:} overlay.
    /// </summary>
    public class HandoverDim : MonoBehaviour
    {
        RectTransform overlay;
        Image sheet;
        float peak;
        float alpha, from, to, clock, seconds;

        /// <summary>The sheet's alpha now (0 = not there).</summary>
        public float Alpha => alpha;
        /// <summary>The alpha that leaves the scene at 60 % brightness in this colour space.</summary>
        public float Peak => peak;
        /// <summary>True while the sheet is visible at all.</summary>
        public bool IsShowing => alpha > 0.002f;
        /// <summary>Where it is heading: 1 = dimmed, 0 = clear.</summary>
        public float Target { get; private set; }

        public static HandoverDim CreateOn(Transform canvas)
        {
            var d = new GameObject("HandoverDim").AddComponent<HandoverDim>();
            d.Build(canvas);
            return d;
        }

        void Build(Transform host)
        {
            peak = StageMath.HandoverDimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear);

            var container = new GameObject("Handover_Container", typeof(RectTransform), typeof(Canvas));
            container.transform.SetParent(host, false);
            overlay = container.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Canvas sub = container.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = StageMath.HandoverDimSortingOrder;

            var go = new GameObject("Handover_Dim", typeof(RectTransform));
            go.transform.SetParent(overlay, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            sheet = go.AddComponent<Image>();
            sheet.color = new Color(0f, 0f, 0f, 0f);
            sheet.raycastTarget = false;
            overlay.gameObject.SetActive(false);
        }

        /// <summary>
        /// Fades to dimmed (<paramref name="dimmed"/>) or clear over
        /// <paramref name="overSeconds"/> (0 = on this frame). Asking for
        /// where it is already heading changes nothing.
        /// </summary>
        public void FadeTo(bool dimmed, float overSeconds)
        {
            float target = dimmed ? 1f : 0f;
            if (Mathf.Approximately(target, Target) && (clock < seconds || Mathf.Approximately(alpha, target * peak))) return;
            Target = target;
            from = alpha;
            to = target * peak;
            seconds = Mathf.Max(0f, overSeconds);
            clock = 0f;
            if (seconds <= 0f) Apply(to);
            else if (overlay != null) overlay.gameObject.SetActive(true);
        }

        /// <summary>One frame of the fade (Update calls it; the self-tests do too).</summary>
        public void Step(float dt)
        {
            if (clock >= seconds) return;
            clock += Mathf.Max(0f, dt);
            float x = seconds > 0f ? Mathf.Clamp01(clock / seconds) : 1f;
            Apply(Mathf.Lerp(from, to, x * x * (3f - 2f * x)));
        }

        void Update() => Step(Time.deltaTime);

        void Apply(float a)
        {
            alpha = a;
            if (sheet != null) sheet.color = new Color(0f, 0f, 0f, a);
            if (overlay != null) overlay.gameObject.SetActive(a > 0.002f || clock < seconds);
        }

        void OnDestroy()
        {
            if (overlay != null) Destroy(overlay.gameObject);
        }
    }
}

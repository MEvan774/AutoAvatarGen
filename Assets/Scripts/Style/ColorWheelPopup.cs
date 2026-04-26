using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MugsTech.Style
{
    /// <summary>
    /// Self-contained HSV color picker overlay. Generates its own UI on first
    /// use — a hue ring with a saturation/value square inside, an alpha
    /// slider, hex readout, and OK / Cancel buttons. Callers point Show() at
    /// a parent transform and pass an initial color + callback.
    ///
    /// Usage:
    ///   ColorWheelPopup.GetOrCreate(panelRoot.transform)
    ///                  .Show(currentColor, picked => { applyToWhatever(picked); });
    ///
    /// Picks: clicking anywhere on the hue ring snaps the hue to that angle;
    /// clicking the inner square sets saturation (x) and value (y); the alpha
    /// slider sets transparency. The square's texture is regenerated on hue
    /// change so the user sees the gamut for the selected hue.
    /// </summary>
    public class ColorWheelPopup : MonoBehaviour
    {
        public static ColorWheelPopup GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<ColorWheelPopup>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("ColorWheelPopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<ColorWheelPopup>();
        }

        // Built UI references
        Image          hueRingImage;
        Image          svSquareImage;
        RectTransform  hueIndicator;
        RectTransform  svIndicator;
        Image          previewImage;
        Slider         alphaSlider;
        Text           hexLabel;
        Texture2D      svTexture;
        Sprite         svSprite;

        // Current HSVA state
        float hue, sat, val, alpha;
        Action<Color> onApply;

        bool built;

        public void Show(Color initial, Action<Color> apply)
        {
            if (!built) BuildUI();
            this.onApply = apply;

            Color.RGBToHSV(initial, out hue, out sat, out val);
            alpha = initial.a;

            RegenSVTexture();
            UpdateIndicators();
            UpdatePreview();

            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        // -------------------------------------------------------------------
        // Internal state-change entry points (called by handler subcomponents)
        // -------------------------------------------------------------------

        public void SetHue(float h)
        {
            hue = Mathf.Clamp01(h);
            RegenSVTexture();
            UpdateIndicators();
            UpdatePreview();
        }

        public void SetSV(float s, float v)
        {
            sat = Mathf.Clamp01(s);
            val = Mathf.Clamp01(v);
            UpdateIndicators();
            UpdatePreview();
        }

        void OnAlphaChanged(float a)
        {
            alpha = Mathf.Clamp01(a);
            UpdatePreview();
        }

        void Apply()
        {
            Color c = Color.HSVToRGB(hue, sat, val);
            c.a = alpha;
            gameObject.SetActive(false);
            onApply?.Invoke(c);
            onApply = null;
        }

        void Cancel()
        {
            gameObject.SetActive(false);
            onApply = null;
        }

        // -------------------------------------------------------------------
        // UI construction
        // -------------------------------------------------------------------

        void BuildUI()
        {
            built = true;

            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Modal backdrop — full-parent, eats clicks so the editor below
            // doesn't react while the picker is up.
            var backdrop = NewChild("Backdrop", transform, stretch: true);
            var bImg = backdrop.AddComponent<Image>();
            bImg.color = new Color(0, 0, 0, 0.55f);

            // Center panel
            var panel = NewChild("Panel", transform, stretch: false);
            var panelRT = (RectTransform)panel.transform;
            panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(380, 580);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            // Hue ring
            var hr = NewChild("HueRing", panel.transform, stretch: false);
            var hrRT = (RectTransform)hr.transform;
            hrRT.anchorMin = hrRT.anchorMax = hrRT.pivot = new Vector2(0.5f, 1f);
            hrRT.anchoredPosition = new Vector2(0, -30);
            hrRT.sizeDelta = new Vector2(300, 300);
            hueRingImage = hr.AddComponent<Image>();
            hueRingImage.sprite = SpriteFromTexture(GenerateHueRingTexture(256, 0.65f));
            // Skip clicks on the transparent ring corners and inner hole — the
            // SV square below sits in the hole and should receive those.
            hueRingImage.alphaHitTestMinimumThreshold = 0.1f;
            var hueHandler = hr.AddComponent<HueRingHandler>();
            hueHandler.popup = this;

            // Hue indicator
            hueIndicator = NewRect("HueIndicator", hr.transform);
            hueIndicator.anchorMin = hueIndicator.anchorMax = hueIndicator.pivot = new Vector2(0.5f, 0.5f);
            hueIndicator.sizeDelta = new Vector2(20, 20);
            var hueIndImg = hueIndicator.gameObject.AddComponent<Image>();
            hueIndImg.color = Color.white;
            hueIndImg.raycastTarget = false;

            // SV square inside the ring
            var sv = NewChild("SVSquare", hr.transform, stretch: false);
            var svRT = (RectTransform)sv.transform;
            svRT.anchorMin = svRT.anchorMax = svRT.pivot = new Vector2(0.5f, 0.5f);
            svRT.anchoredPosition = Vector2.zero;
            svRT.sizeDelta = new Vector2(170, 170);
            svSquareImage = sv.AddComponent<Image>();
            var svHandler = sv.AddComponent<SVSquareHandler>();
            svHandler.popup = this;

            // SV indicator — anchored to the bottom-left so anchoredPosition
            // can be expressed directly in (sat*w, val*h); pivot stays centered
            // so the dot sits ON the picked point rather than extending past it.
            svIndicator = NewRect("SVIndicator", sv.transform);
            svIndicator.anchorMin = svIndicator.anchorMax = new Vector2(0f, 0f);
            svIndicator.pivot     = new Vector2(0.5f, 0.5f);
            svIndicator.sizeDelta = new Vector2(14, 14);
            var svIndImg = svIndicator.gameObject.AddComponent<Image>();
            svIndImg.color = Color.white;
            svIndImg.raycastTarget = false;

            // Alpha slider
            BuildAlphaSlider(panel.transform);

            // Preview swatch
            var preview = NewChild("Preview", panel.transform, stretch: false);
            var prRT = (RectTransform)preview.transform;
            prRT.anchorMin = prRT.anchorMax = prRT.pivot = new Vector2(0.5f, 1f);
            prRT.anchoredPosition = new Vector2(-90, -420);
            prRT.sizeDelta = new Vector2(80, 50);
            previewImage = preview.AddComponent<Image>();

            // Hex label
            var hex = NewChild("Hex", panel.transform, stretch: false);
            var hexRT = (RectTransform)hex.transform;
            hexRT.anchorMin = hexRT.anchorMax = hexRT.pivot = new Vector2(0.5f, 1f);
            hexRT.anchoredPosition = new Vector2(60, -425);
            hexRT.sizeDelta = new Vector2(180, 40);
            hexLabel = hex.AddComponent<Text>();
            hexLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hexLabel.fontSize = 22;
            hexLabel.alignment = TextAnchor.MiddleLeft;
            hexLabel.color = new Color(0.85f, 0.88f, 0.93f, 1f);

            // Buttons
            BuildButton("OkButton",     panel.transform, "OK",     new Vector2(-80, -510), Apply,  new Color(0.20f, 0.55f, 0.32f, 1f));
            BuildButton("CancelButton", panel.transform, "Cancel", new Vector2( 80, -510), Cancel, new Color(0.45f, 0.20f, 0.20f, 1f));
        }

        void BuildAlphaSlider(Transform parent)
        {
            // Track
            var trackGO = NewChild("AlphaSlider", parent, stretch: false);
            var trackRT = (RectTransform)trackGO.transform;
            trackRT.anchorMin = trackRT.anchorMax = trackRT.pivot = new Vector2(0.5f, 1f);
            trackRT.anchoredPosition = new Vector2(0, -360);
            trackRT.sizeDelta = new Vector2(300, 28);
            var trackImg = trackGO.AddComponent<Image>();
            trackImg.color = new Color(0.18f, 0.20f, 0.24f, 1f);

            alphaSlider = trackGO.AddComponent<Slider>();
            alphaSlider.minValue = 0f;
            alphaSlider.maxValue = 1f;
            alphaSlider.value    = 1f;
            alphaSlider.direction = Slider.Direction.LeftToRight;
            alphaSlider.targetGraphic = trackImg;

            // Fill area (required by Slider for "value" feedback)
            var fillArea = NewChild("Fill Area", trackGO.transform, stretch: false);
            var faRT = (RectTransform)fillArea.transform;
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(8, 0);
            faRT.offsetMax = new Vector2(-8, 0);

            var fill = NewChild("Fill", fillArea.transform, stretch: true);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.55f, 0.78f, 1f, 1f);
            alphaSlider.fillRect = (RectTransform)fill.transform;

            // Handle area
            var handleArea = NewChild("Handle Slide Area", trackGO.transform, stretch: false);
            var haRT = (RectTransform)handleArea.transform;
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(8, 0);
            haRT.offsetMax = new Vector2(-8, 0);

            var handle = NewChild("Handle", handleArea.transform, stretch: false);
            var handleRT = (RectTransform)handle.transform;
            handleRT.sizeDelta = new Vector2(18, 32);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            alphaSlider.handleRect = handleRT;

            alphaSlider.onValueChanged.AddListener(OnAlphaChanged);
        }

        void BuildButton(string name, Transform parent, string label, Vector2 anchoredPos, Action onClick, Color tint)
        {
            var go = NewChild(name, parent, stretch: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(140, 50);

            var img = go.AddComponent<Image>();
            img.color = tint;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            var labelGO = NewChild("Label", go.transform, stretch: true);
            var t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 26;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
        }

        // -------------------------------------------------------------------
        // Visual updates
        // -------------------------------------------------------------------

        void UpdateIndicators()
        {
            // Hue indicator at the midpoint of the ring band, on the hue angle
            var hueRT = (RectTransform)hueRingImage.transform;
            float ringRadius = hueRT.rect.width * 0.5f * (1f + 0.65f) * 0.5f; // halfway through the band
            float angle = hue * Mathf.PI * 2f;
            hueIndicator.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;

            // SV indicator at (sat, val) inside the SV square
            var svRT = (RectTransform)svSquareImage.transform;
            svIndicator.anchoredPosition = new Vector2(sat * svRT.rect.width, val * svRT.rect.height);

            // Sync alpha slider without firing onValueChanged recursion
            if (alphaSlider != null && !Mathf.Approximately(alphaSlider.value, alpha))
                alphaSlider.SetValueWithoutNotify(alpha);
        }

        void UpdatePreview()
        {
            Color c = Color.HSVToRGB(hue, sat, val);
            c.a = alpha;
            if (previewImage != null) previewImage.color = c;
            if (hexLabel != null)
                hexLabel.text = "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        // -------------------------------------------------------------------
        // Texture generation
        // -------------------------------------------------------------------

        static Texture2D s_HueRingTexture;

        static Texture2D GenerateHueRingTexture(int size, float innerRatio)
        {
            if (s_HueRingTexture != null && s_HueRingTexture.width == size) return s_HueRingTexture;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            float r        = size * 0.5f;
            float rOuter   = r - 1f;
            float rInner   = r * innerRatio;
            var pixels     = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rOuter || d < rInner)
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;
                Color c = Color.HSVToRGB(angle / 360f, 1f, 1f);
                pixels[y * size + x] = c;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            s_HueRingTexture = tex;
            return tex;
        }

        void RegenSVTexture()
        {
            const int size = 128;
            if (svTexture == null)
            {
                svTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                svTexture.filterMode = FilterMode.Bilinear;
                svTexture.wrapMode   = TextureWrapMode.Clamp;
            }
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float s = x / (float)(size - 1);
                    pixels[y * size + x] = Color.HSVToRGB(hue, s, v);
                }
            }
            svTexture.SetPixels32(pixels);
            svTexture.Apply();
            if (svSprite == null)
                svSprite = Sprite.Create(svTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            if (svSquareImage != null && svSquareImage.sprite != svSprite)
                svSquareImage.sprite = svSprite;
        }

        static Sprite SpriteFromTexture(Texture2D tex) =>
            Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        // -------------------------------------------------------------------
        // GameObject helpers
        // -------------------------------------------------------------------

        static GameObject NewChild(string name, Transform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (stretch)
            {
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            return go;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // -------------------------------------------------------------------
        // Pointer handlers (separate components so they can be added to the
        // ring/square images and forward their clicks to the popup)
        // -------------------------------------------------------------------

        public class HueRingHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public ColorWheelPopup popup;
            public void OnPointerDown(PointerEventData e) => Pick(e);
            public void OnDrag(PointerEventData e)        => Pick(e);

            void Pick(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rt, e.position, e.pressEventCamera, out Vector2 local))
                    return;
                // Local origin is the rect's pivot (centered) so atan2 gives the
                // angle directly; convert to [0,1) hue.
                float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;
                popup.SetHue(angle / 360f);
            }
        }

        public class SVSquareHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public ColorWheelPopup popup;
            public void OnPointerDown(PointerEventData e) => Pick(e);
            public void OnDrag(PointerEventData e)        => Pick(e);

            void Pick(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rt, e.position, e.pressEventCamera, out Vector2 local))
                    return;
                // Pivot is centered → local in [-w/2,w/2] x [-h/2,h/2]. Normalize.
                float s = Mathf.Clamp01((local.x + rt.rect.width  * 0.5f) / rt.rect.width);
                float v = Mathf.Clamp01((local.y + rt.rect.height * 0.5f) / rt.rect.height);
                popup.SetSV(s, v);
            }
        }
    }
}

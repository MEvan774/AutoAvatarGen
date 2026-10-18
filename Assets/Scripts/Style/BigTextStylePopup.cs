using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Style
{
    /// <summary>
    /// Modal popup for editing BigTextStyleData. Self-builds its UI on first
    /// Show() — clickable color swatches (open ColorWheelPopup), width /
    /// softness / corner-radius sliders, and on/off toggles for shadow and
    /// background. Mutates the passed-in BigTextStyleData directly and fires
    /// onChanged on every edit so the visuals menu can persist live.
    ///
    /// Usage:
    ///   BigTextStylePopup.GetOrCreate(panelRoot.transform)
    ///                    .Show(myStyle, () =&gt; mirrorToPlayerPrefsOrSave());
    /// </summary>
    public class BigTextStylePopup : MonoBehaviour
    {
        public static BigTextStylePopup GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<BigTextStylePopup>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("BigTextStylePopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<BigTextStylePopup>();
        }

        BigTextStyleData data;
        Action           onChanged;
        bool             built;
        TMP_FontAsset    currentFont;

        // Refreshable controls
        Image           textColorSwatch;
        Image           outlineColorSwatch;
        Image           shadowColorSwatch;
        Image           backgroundColorSwatch;
        Slider          outlineWidthSlider;
        Slider          shadowSoftnessSlider;
        Slider          backgroundCornerSlider;
        Toggle          shadowToggle;
        Toggle          backgroundToggle;
        Text            fontStyleLabel;
        Image           previewBackground;
        TextMeshProUGUI previewTmp;

        public void Show(BigTextStyleData target, TMP_FontAsset font, Action onChange)
        {
            this.data        = target;
            this.currentFont = font != null ? font : TMP_Settings.defaultFontAsset;
            this.onChanged   = onChange;
            if (!built) BuildUI();
            RefreshAllControls();
            RefreshPreview();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        void Close()
        {
            gameObject.SetActive(false);
            onChanged = null;
            data      = null;
        }

        void RefreshAllControls()
        {
            if (data == null) return;
            textColorSwatch.color       = ParseHex(data.textColorHex,       Color.white);
            outlineColorSwatch.color    = ParseHex(data.outlineColorHex,    new Color(0, 0, 0, 0.75f));
            shadowColorSwatch.color     = ParseHex(data.shadowColorHex,     new Color(0, 0, 0, 0.75f));
            backgroundColorSwatch.color = ParseHex(data.backgroundColorHex, new Color(0, 0, 0, 0.60f));
            outlineWidthSlider.SetValueWithoutNotify(data.outlineWidth);
            shadowSoftnessSlider.SetValueWithoutNotify(data.shadowSoftness);
            backgroundCornerSlider.SetValueWithoutNotify(data.backgroundCornerRadius);
            shadowToggle.SetIsOnWithoutNotify(data.shadowEnabled);
            backgroundToggle.SetIsOnWithoutNotify(data.backgroundEnabled);
            UpdateFontStyleLabel();
        }

        // Mirrors all current style fields onto the in-popup TMP preview so
        // the user can see what their BigText will look like in the recording
        // without leaving the editor. Uses the same TMP shader keywords +
        // uniforms BigTextCard does so the preview is faithful.
        void RefreshPreview()
        {
            if (previewTmp == null || data == null) return;

            previewTmp.font      = currentFont != null ? currentFont : TMP_Settings.defaultFontAsset;
            previewTmp.color     = ParseHex(data.textColorHex, Color.white);
            previewTmp.fontStyle = ToTmpFontStyles((FontStyle)data.fontStyle);

            Material mat = previewTmp.fontMaterial;

            Color outlineColor = ParseHex(data.outlineColorHex, new Color(0, 0, 0, 0.75f));
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor("_OutlineColor", outlineColor);
            mat.SetFloat("_OutlineWidth", data.outlineWidth);
            previewTmp.outlineColor = outlineColor;
            previewTmp.outlineWidth = data.outlineWidth;

            if (data.shadowEnabled)
            {
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor",    ParseHex(data.shadowColorHex, new Color(0, 0, 0, 0.75f)));
                mat.SetFloat("_UnderlayOffsetX",  1.0f);
                mat.SetFloat("_UnderlayOffsetY", -1.0f);
                mat.SetFloat("_UnderlayDilate",   0.5f);
                mat.SetFloat("_UnderlaySoftness", data.shadowSoftness);
            }
            else
            {
                mat.DisableKeyword("UNDERLAY_ON");
            }
            previewTmp.UpdateMeshPadding();

            if (previewBackground != null)
            {
                if (data.backgroundEnabled)
                {
                    previewBackground.gameObject.SetActive(true);
                    previewBackground.color = ParseHex(data.backgroundColorHex, new Color(0, 0, 0, 0.60f));
                    int radius = Mathf.Max(0, Mathf.RoundToInt(data.backgroundCornerRadius));
                    if (radius > 0)
                    {
                        previewBackground.sprite = StyleSpriteFactory.GetRoundedRect(radius);
                        previewBackground.type   = Image.Type.Sliced;
                    }
                    else
                    {
                        previewBackground.sprite = null;
                        previewBackground.type   = Image.Type.Simple;
                    }
                }
                else
                {
                    previewBackground.gameObject.SetActive(false);
                }
            }
        }

        // Wraps the parent-supplied onChanged so the in-popup preview stays in
        // sync with every edit.
        void NotifyChanged()
        {
            RefreshPreview();
            onChanged?.Invoke();
        }

        static FontStyles ToTmpFontStyles(FontStyle s)
        {
            switch (s)
            {
                case FontStyle.Bold:          return FontStyles.Bold;
                case FontStyle.Italic:        return FontStyles.Italic;
                case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
                default:                      return FontStyles.Normal;
            }
        }

        // -------------------------------------------------------------------
        // UI construction — header (title + Close), live preview, then a form:
        // labels in one column, controls in the next. Rows are stacked from the
        // panel top and the panel is sized to fit them afterwards.
        // -------------------------------------------------------------------

        const float k_PanelWidth = 760f;
        const float k_Pad        = 30f;   // left / right / bottom padding
        const float k_PreviewH   = 150f;
        const float k_RowHeight  = 50f;
        const float k_RowGap     =  6f;
        const float k_GroupGap   = 14f;
        const float k_LabelW     = 200f;
        // Label column starts at the left padding; controls fill the rest.
        const float k_LabelX     = -k_PanelWidth * 0.5f + k_Pad + k_LabelW * 0.5f;
        const float k_ControlL   = -k_PanelWidth * 0.5f + k_Pad + k_LabelW + UITheme.Gap;
        const float k_ControlW   = k_PanelWidth * 0.5f - k_Pad - k_ControlL;
        const float k_ControlX   = k_ControlL + k_ControlW * 0.5f;

        Transform panelTf;

        void BuildUI()
        {
            built = true;

            var selfRT = (RectTransform)transform;
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

            var backdrop = NewChild("Backdrop", transform, stretch: true);
            backdrop.AddComponent<Image>().color = UITheme.Backdrop;

            var panel = NewChild("Panel", transform, stretch: false);
            panelTf = panel.transform;
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            UITheme.Surface(panel.AddComponent<Image>(), UITheme.Window, UITheme.PanelRadius);

            float y = -20f; // from the panel top

            BuildHeader("Big Text Style", ref y);
            BuildPreviewArea(ref y);
            y -= k_GroupGap;

            textColorSwatch = BuildSwatchRow("Text colour", ref y,
                () => ParseHex(data.textColorHex, Color.white),
                c => { data.textColorHex = HexOf(c); textColorSwatch.color = c; NotifyChanged(); });

            BuildFontStyleCycleRow(ref y);
            y -= k_GroupGap;

            BuildSectionHeader("Outline", ref y);
            outlineColorSwatch = BuildSwatchRow("Colour", ref y,
                () => ParseHex(data.outlineColorHex, new Color(0, 0, 0, 0.75f)),
                c => { data.outlineColorHex = HexOf(c); outlineColorSwatch.color = c; NotifyChanged(); });
            outlineWidthSlider = BuildSliderRow("Width", ref y, 0f, 0.3f,
                () => data.outlineWidth,
                v => { data.outlineWidth = v; NotifyChanged(); });
            y -= k_GroupGap;

            shadowToggle = BuildToggleHeader("Shadow", ref y,
                () => data.shadowEnabled,
                v => { data.shadowEnabled = v; NotifyChanged(); });
            shadowColorSwatch = BuildSwatchRow("Colour", ref y,
                () => ParseHex(data.shadowColorHex, new Color(0, 0, 0, 0.75f)),
                c => { data.shadowColorHex = HexOf(c); shadowColorSwatch.color = c; NotifyChanged(); });
            shadowSoftnessSlider = BuildSliderRow("Softness", ref y, 0f, 1f,
                () => data.shadowSoftness,
                v => { data.shadowSoftness = v; NotifyChanged(); });
            y -= k_GroupGap;

            backgroundToggle = BuildToggleHeader("Background", ref y,
                () => data.backgroundEnabled,
                v => { data.backgroundEnabled = v; NotifyChanged(); });
            backgroundColorSwatch = BuildSwatchRow("Colour", ref y,
                () => ParseHex(data.backgroundColorHex, new Color(0, 0, 0, 0.60f)),
                c => { data.backgroundColorHex = HexOf(c); backgroundColorSwatch.color = c; NotifyChanged(); });
            backgroundCornerSlider = BuildSliderRow("Corner radius", ref y, 0f, 60f,
                () => data.backgroundCornerRadius,
                v => { data.backgroundCornerRadius = v; NotifyChanged(); });

            // Fit the panel to the rows (minus the trailing gap) + bottom padding.
            prt.sizeDelta = new Vector2(k_PanelWidth, -y - k_RowGap + k_Pad);

            UITheme.Apply(gameObject);
            UITheme.ApplyControls(gameObject);
        }

        // -------------------------------------------------------------------
        // Preview area + font style cycle
        // -------------------------------------------------------------------

        void BuildPreviewArea(ref float y)
        {
            var rowGO = NewRow("Preview", ref y, k_PreviewH);

            // Faux-recording backdrop so light text remains visible against
            // a dark scene. Sized to fill the row.
            var backdropGO = NewChild("Backdrop", rowGO.transform, stretch: true);
            UITheme.Surface(backdropGO.AddComponent<Image>(), new Color(0.06f, 0.07f, 0.09f, 1f), UITheme.ControlRadius);

            // Per-line-style background plate (mirrors BigTextCard).
            var bgGO = NewChild("LineBg", rowGO.transform, stretch: false);
            var bgRT = (RectTransform)bgGO.transform;
            bgRT.anchorMin = bgRT.anchorMax = bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.anchoredPosition = Vector2.zero;
            bgRT.sizeDelta        = new Vector2(k_PanelWidth - 2f * k_Pad - 40f, k_PreviewH - 30f);
            previewBackground = bgGO.AddComponent<Image>();
            previewBackground.raycastTarget = false;
            previewBackground.gameObject.SetActive(false);

            var tmpGO = NewChild("Text", rowGO.transform, stretch: true);
            previewTmp = tmpGO.AddComponent<TextMeshProUGUI>();
            previewTmp.text                = "Big Text";
            previewTmp.alignment           = TextAlignmentOptions.Center;
            previewTmp.enableAutoSizing    = true;
            previewTmp.fontSizeMin         = 36f;
            previewTmp.fontSizeMax         = 110f;
            previewTmp.textWrappingMode    = TextWrappingModes.NoWrap;
            previewTmp.overflowMode        = TextOverflowModes.Truncate;
            previewTmp.raycastTarget       = false;
            // Padding so the preview rect doesn't crop large outlines / shadows.
            var ttRT = previewTmp.rectTransform;
            ttRT.offsetMin = new Vector2(40f, 18f);
            ttRT.offsetMax = new Vector2(-40f, -18f);
        }

        void BuildFontStyleCycleRow(ref float y)
        {
            var rowGO = NewRow("FontStyle", ref y, k_RowHeight);
            BuildLabel(rowGO.transform, "Style");

            var btnGO = NewChild("Cycle", rowGO.transform, stretch: false);
            PlaceControl(btnGO, UITheme.ControlHeight);
            var img = btnGO.AddComponent<Image>();
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelGO = NewChild("Label", btnGO.transform, stretch: true);
            fontStyleLabel = labelGO.AddComponent<Text>();
            fontStyleLabel.font      = UITheme.LegacyFont;
            UITheme.Style(fontStyleLabel, UITheme.TextRole.Value);
            fontStyleLabel.alignment = TextAnchor.MiddleCenter;

            btn.onClick.AddListener(() =>
            {
                data.fontStyle = (data.fontStyle + 1) & 3; // cycle 0..3
                UpdateFontStyleLabel();
                NotifyChanged();
            });
        }

        void UpdateFontStyleLabel()
        {
            if (fontStyleLabel == null) return;
            switch ((FontStyle)data.fontStyle)
            {
                case FontStyle.Bold:          fontStyleLabel.text = "Bold"; break;
                case FontStyle.Italic:        fontStyleLabel.text = "Italic"; break;
                case FontStyle.BoldAndItalic: fontStyleLabel.text = "Bold + Italic"; break;
                default:                      fontStyleLabel.text = "Normal"; break;
            }
        }

        // Title on the left, Close on the right — like every other window.
        void BuildHeader(string text, ref float y)
        {
            var row = NewRow("Header", ref y, 56f);

            var titleGO = NewChild("Title", row.transform, stretch: true);
            NewText(titleGO, text, UITheme.TextRole.Title);
            ((RectTransform)titleGO.transform).offsetMin = new Vector2(k_Pad - 20f, 0f); // align with the labels

            var btnGO = NewChild("CloseButton", row.transform, stretch: false);
            var rt = (RectTransform)btnGO.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-(k_Pad - 20f), 0f);
            rt.sizeDelta = new Vector2(120f, UITheme.ControlHeight);
            var img = btnGO.AddComponent<Image>();
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Close);
            var label = NewText(NewChild("Label", btnGO.transform, stretch: true), "Close", UITheme.TextRole.Value);
            label.alignment = TextAnchor.MiddleCenter;
        }

        void BuildSectionHeader(string text, ref float y)
        {
            var go = NewRow("Header_" + text, ref y, 36f);
            var labelGO = NewChild("Label", go.transform, stretch: false);
            PlaceLabel(labelGO, 36f);
            NewText(labelGO, text, UITheme.TextRole.Heading);
        }

        // A section heading with its on / off toggle in the controls column.
        Toggle BuildToggleHeader(string text, ref float y, Func<bool> getVal, Action<bool> setVal)
        {
            var rowGO = NewRow("ToggleHeader_" + text, ref y, k_RowHeight);

            var headerLabel = NewChild("Label", rowGO.transform, stretch: false);
            PlaceLabel(headerLabel, k_RowHeight);
            NewText(headerLabel, text, UITheme.TextRole.Heading);

            var toggleGO = NewChild("Toggle", rowGO.transform, stretch: false);
            var tRT = (RectTransform)toggleGO.transform;
            tRT.anchorMin = tRT.anchorMax = tRT.pivot = new Vector2(0.5f, 0.5f);
            tRT.anchoredPosition = new Vector2(k_ControlL + 18f, 0f);
            tRT.sizeDelta = new Vector2(36f, 36f);
            var bgImg = toggleGO.AddComponent<Image>();
            UITheme.Surface(bgImg, UITheme.Field, 6);

            var checkGO = NewChild("Checkmark", toggleGO.transform, stretch: false);
            var cRT = (RectTransform)checkGO.transform;
            cRT.anchorMin = cRT.anchorMax = cRT.pivot = new Vector2(0.5f, 0.5f);
            cRT.anchoredPosition = Vector2.zero;
            cRT.sizeDelta = new Vector2(22f, 22f);
            var checkImg = checkGO.AddComponent<Image>();
            UITheme.Surface(checkImg, UITheme.Heading, 4);

            var toggle = toggleGO.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = checkImg;
            toggle.isOn          = getVal();
            toggle.onValueChanged.AddListener(v => setVal(v));
            return toggle;
        }

        Image BuildSwatchRow(string label, ref float y, Func<Color> getColor, Action<Color> setColor)
        {
            var rowGO = NewRow("Swatch_" + label, ref y, k_RowHeight);

            BuildLabel(rowGO.transform, label);

            var swatchGO = NewChild("Swatch", rowGO.transform, stretch: false);
            PlaceControl(swatchGO, 36f);
            var swatch = swatchGO.AddComponent<Image>();
            UITheme.Round(swatch, UITheme.ControlRadius);
            swatch.color = getColor();

            var btn = swatchGO.AddComponent<Button>();
            btn.targetGraphic = swatch;
            btn.onClick.AddListener(() =>
            {
                ColorWheelPopup.GetOrCreate(transform.parent).Show(getColor(), setColor);
            });
            return swatch;
        }

        Slider BuildSliderRow(string label, ref float y, float min, float max, Func<float> getVal, Action<float> setVal)
        {
            var rowGO = NewRow("Slider_" + label, ref y, k_RowHeight);

            BuildLabel(rowGO.transform, label);

            var trackGO = NewChild("Slider", rowGO.transform, stretch: false);
            PlaceControl(trackGO, 24f);
            var trackImg = trackGO.AddComponent<Image>();

            var slider = trackGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value    = getVal();
            slider.targetGraphic = trackImg;
            slider.direction = Slider.Direction.LeftToRight;

            // Required Slider children
            var fillArea = NewChild("Fill Area", trackGO.transform, stretch: false);
            var faRT = (RectTransform)fillArea.transform;
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(8, 0);
            faRT.offsetMax = new Vector2(-8, 0);

            var fill = NewChild("Fill", fillArea.transform, stretch: true);
            fill.AddComponent<Image>();
            slider.fillRect = (RectTransform)fill.transform;

            var handleArea = NewChild("Handle Slide Area", trackGO.transform, stretch: false);
            var haRT = (RectTransform)handleArea.transform;
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(8, 0);
            haRT.offsetMax = new Vector2(-8, 0);

            var handle = NewChild("Handle", handleArea.transform, stretch: false);
            var handleRT = (RectTransform)handle.transform;
            handleRT.sizeDelta = new Vector2(18f, 32f);
            handle.AddComponent<Image>();
            slider.handleRect = handleRT;

            UITheme.StyleSlider(slider);
            slider.onValueChanged.AddListener(v => setVal(v));
            return slider;
        }

        void BuildLabel(Transform rowParent, string text)
        {
            var labelGO = NewChild("Label", rowParent, stretch: false);
            PlaceLabel(labelGO, k_RowHeight);
            NewText(labelGO, text, UITheme.TextRole.Label);
        }

        static void PlaceLabel(GameObject go, float height)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(k_LabelX, 0f);
            rt.sizeDelta = new Vector2(k_LabelW, height);
        }

        static void PlaceControl(GameObject go, float height)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(k_ControlX, 0f);
            rt.sizeDelta = new Vector2(k_ControlW, height);
        }

        static Text NewText(GameObject go, string text, UITheme.TextRole role)
        {
            var t = go.AddComponent<Text>();
            t.text      = text;
            t.font      = UITheme.LegacyFont;
            t.alignment = TextAnchor.MiddleLeft;
            UITheme.Style(t, role);
            return t;
        }

        // Single 1-row container stacked from the panel top; advances y.
        GameObject NewRow(string name, ref float y, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panelTf, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y - height * 0.5f);
            rt.sizeDelta = new Vector2(k_PanelWidth - 40f, height);
            y -= height + k_RowGap;
            return go;
        }

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

        static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out Color c) ? c : fallback;
        }

        static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}

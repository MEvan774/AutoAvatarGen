using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Tts
{
    /// <summary>
    /// Modal popup for the ElevenLabs API key. Self-builds its UI on first
    /// <see cref="Show"/>, follows the dark-panel-on-dim-backdrop pattern
    /// used by MusicEditPopup. Key persists in PlayerPrefs across sessions.
    /// </summary>
    public class TtsApiKeyPopup : MonoBehaviour
    {
        public const string ApiKeyPrefKey = "AutoAvatarGen.ElevenLabsApiKey";

        public static string LoadKey() => PlayerPrefs.GetString(ApiKeyPrefKey, "");

        public static TtsApiKeyPopup GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<TtsApiKeyPopup>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("TtsApiKeyPopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TtsApiKeyPopup>();
        }

        Action<string> onSaved;
        bool built;

        TMP_InputField keyInput;
        TMP_Text       statusText;
        Toggle         maskToggle;

        public void Show(Action<string> savedCallback = null)
        {
            this.onSaved = savedCallback;
            if (!built) BuildUI();
            keyInput.text = LoadKey();
            UpdateMaskState(maskToggle.isOn);
            UpdateStatus();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            keyInput.Select();
            keyInput.ActivateInputField();
        }

        void Close()
        {
            gameObject.SetActive(false);
            onSaved = null;
        }

        // ---- build -------------------------------------------------------

        const float kPanelWidth  = 720f;
        const float kPanelHeight = 360f;

        void BuildUI()
        {
            built = true;

            var selfRT = (RectTransform)transform;
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

            // Backdrop — dims the panel underneath. Click captured but does
            // nothing; users must use the explicit buttons.
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(transform, false);
            var brt = (RectTransform)backdrop.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            // Panel
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(kPanelWidth, kPanelHeight);
            panel.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            float y = kPanelHeight * 0.5f - 40f;

            BuildTitle(panel.transform, "ElevenLabs API Key", ref y);
            y -= 12f;
            BuildHint(panel.transform,
                "Paste your xi-api-key. Stored locally in PlayerPrefs.",
                ref y);
            y -= 18f;
            BuildKeyRow(panel.transform, ref y);
            y -= 6f;
            BuildMaskToggle(panel.transform, ref y);
            y -= 14f;
            BuildStatusRow(panel.transform, ref y);
            y -= 14f;
            BuildButtonRow(panel.transform, ref y);
        }

        void BuildTitle(Transform parent, string text, ref float y)
        {
            var go = NewRow(parent, "Title", 50f, ref y);
            var tmp = NewTMP(go.transform, text, 26, FontStyles.Bold);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.95f, 0.97f, 1f);
        }

        void BuildHint(Transform parent, string text, ref float y)
        {
            var go = NewRow(parent, "Hint", 30f, ref y);
            var tmp = NewTMP(go.transform, text, 16, FontStyles.Italic);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.65f, 0.70f, 0.78f);
        }

        void BuildKeyRow(Transform parent, ref float y)
        {
            var row = NewRow(parent, "KeyInput", 56f, ref y);

            // Background plate so the input box is visible on the dark panel.
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 1f);

            // Input needs its own raycastable Image so clicks land on it.
            var inputGO = new GameObject("Input",
                typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(row.transform, false);
            var irt = (RectTransform)inputGO.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(12, 6); irt.offsetMax = new Vector2(-12, -6);
            inputGO.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.13f, 1f);

            // TMP_InputField needs a Text child as its textComponent and a
            // separate Text child as its placeholder. Build them inline.
            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(inputGO.transform, false);
            var textRT = (RectTransform)textGO.transform;
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 2); textRT.offsetMax = new Vector2(-10, -2);
            var textTMP = textGO.AddComponent<TextMeshProUGUI>();
            textTMP.font = GetTmpFont();
            textTMP.fontSize = 20;
            textTMP.color = new Color(0.95f, 0.97f, 1f);
            textTMP.alignment = TextAlignmentOptions.MidlineLeft;
            textTMP.enableWordWrapping = false;
            textTMP.overflowMode = TextOverflowModes.Ellipsis;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(inputGO.transform, false);
            var phRT = (RectTransform)placeholderGO.transform;
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(10, 2); phRT.offsetMax = new Vector2(-10, -2);
            var phTMP = placeholderGO.AddComponent<TextMeshProUGUI>();
            phTMP.font = GetTmpFont();
            phTMP.fontSize = 20;
            phTMP.color = new Color(0.45f, 0.50f, 0.58f);
            phTMP.alignment = TextAlignmentOptions.MidlineLeft;
            phTMP.fontStyle = FontStyles.Italic;
            phTMP.text = "sk_...";
            phTMP.enableWordWrapping = false;

            keyInput = inputGO.GetComponent<TMP_InputField>();
            keyInput.textComponent = textTMP;
            keyInput.placeholder   = phTMP;
            keyInput.contentType   = TMP_InputField.ContentType.Password;
            keyInput.lineType      = TMP_InputField.LineType.SingleLine;
        }

        void BuildMaskToggle(Transform parent, ref float y)
        {
            var row = NewRow(parent, "MaskToggle", 36f, ref y);

            var toggleGO = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleGO.transform.SetParent(row.transform, false);
            var trt = (RectTransform)toggleGO.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(20f, 0f);
            trt.sizeDelta = new Vector2(28f, 28f);

            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(toggleGO.transform, false);
            var boxRT = (RectTransform)box.transform;
            boxRT.anchorMin = Vector2.zero; boxRT.anchorMax = Vector2.one;
            boxRT.offsetMin = boxRT.offsetMax = Vector2.zero;
            box.GetComponent<Image>().color = new Color(0.20f, 0.22f, 0.28f);

            var check = new GameObject("Check", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(toggleGO.transform, false);
            var crt = (RectTransform)check.transform;
            crt.anchorMin = new Vector2(0.15f, 0.15f);
            crt.anchorMax = new Vector2(0.85f, 0.85f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            check.GetComponent<Image>().color = new Color(0.40f, 0.85f, 0.55f);

            maskToggle = toggleGO.GetComponent<Toggle>();
            maskToggle.graphic = check.GetComponent<Image>();
            maskToggle.targetGraphic = box.GetComponent<Image>();
            maskToggle.isOn = false; // start masked
            maskToggle.onValueChanged.AddListener(UpdateMaskState);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(row.transform, false);
            var lrt = (RectTransform)labelGO.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(60f, 0f);
            lrt.sizeDelta = new Vector2(300f, 28f);
            var lbl = NewTMP(labelGO.transform, "Show key", 16, FontStyles.Normal);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.color = new Color(0.75f, 0.80f, 0.86f);
        }

        void BuildStatusRow(Transform parent, ref float y)
        {
            var go = NewRow(parent, "Status", 28f, ref y);
            statusText = NewTMP(go.transform, "", 15, FontStyles.Italic);
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.color = new Color(0.65f, 0.70f, 0.78f);
        }

        void BuildButtonRow(Transform parent, ref float y)
        {
            var row = NewRow(parent, "Buttons", 56f, ref y);

            // Layout: [ Clear ]    [ Cancel ] [ Save ]
            BuildButton(row.transform, "Clear",  new Color(0.50f, 0.20f, 0.20f),
                anchorX: 0f, offsetX:  100f, onClick: OnClearClicked);
            BuildButton(row.transform, "Cancel", new Color(0.32f, 0.34f, 0.40f),
                anchorX: 1f, offsetX: -260f, onClick: Close);
            BuildButton(row.transform, "Save",   new Color(0.25f, 0.55f, 0.35f),
                anchorX: 1f, offsetX: -100f, onClick: OnSaveClicked);
        }

        void OnSaveClicked()
        {
            string key = (keyInput.text ?? "").Trim();
            PlayerPrefs.SetString(ApiKeyPrefKey, key);
            PlayerPrefs.Save();
            onSaved?.Invoke(key);
            Close();
        }

        void OnClearClicked()
        {
            keyInput.text = "";
            PlayerPrefs.DeleteKey(ApiKeyPrefKey);
            PlayerPrefs.Save();
            UpdateStatus();
            onSaved?.Invoke("");
        }

        void UpdateMaskState(bool show)
        {
            keyInput.contentType = show
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            keyInput.ForceLabelUpdate();
        }

        void UpdateStatus()
        {
            string key = LoadKey();
            if (string.IsNullOrEmpty(key))
            {
                statusText.text  = "No key saved.";
                statusText.color = new Color(0.95f, 0.55f, 0.40f);
            }
            else
            {
                statusText.text  = $"Saved ({key.Length} chars).";
                statusText.color = new Color(0.55f, 0.85f, 0.60f);
            }
        }

        // ---- tiny build helpers -----------------------------------------

        static GameObject NewRow(Transform parent, string name, float height, ref float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(kPanelWidth - 60f, height);
            rt.anchoredPosition = new Vector2(0f, y - height * 0.5f);
            y -= height;
            return go;
        }

        static TextMeshProUGUI NewTMP(Transform parent, string text, int size, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font     = GetTmpFont();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.text     = text;
            tmp.raycastTarget = false;
            return tmp;
        }

        static TMP_FontAsset _font;
        static TMP_FontAsset GetTmpFont()
        {
            if (_font != null) return _font;
            _font = TMP_Settings.defaultFontAsset;
            return _font;
        }

        void BuildButton(Transform parent, string label, Color tint,
            float anchorX, float offsetX, Action onClick)
        {
            var go = new GameObject(label + "Button",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(140, 44);
            rt.anchoredPosition = new Vector2(offsetX, 0f);
            go.GetComponent<Image>().color = tint;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.font      = GetTmpFont();
            tmp.fontSize  = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text      = label;
            tmp.raycastTarget = false;
        }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Tts
{
    /// <summary>
    /// Modal popup for the ElevenLabs API key — scene-authored variant.
    /// Hierarchy is created once by <c>Tools &gt; AutoAvatarGen &gt; Add TTS
    /// Panel</c> (the popup ships as an initially-disabled child of the
    /// TTS panel) and from then on lives in the .unity file.
    ///
    /// Persistence: the key is stored in PlayerPrefs under
    /// <see cref="ApiKeyPrefKey"/>. <see cref="LoadKey"/> is static so the
    /// TTS job can grab the key without needing a popup instance.
    /// </summary>
    public class TtsApiKeyPopup : MonoBehaviour
    {
        public const string ApiKeyPrefKey = "AutoAvatarGen.ElevenLabsApiKey";

        public static string LoadKey() => PlayerPrefs.GetString(ApiKeyPrefKey, "");

        [Header("Root")]
        [Tooltip("The popup root that gets shown/hidden. Usually this GameObject itself.")]
        [SerializeField] GameObject popupRoot;

        [Header("UI Refs")]
        [SerializeField] TMP_InputField keyInput;
        [SerializeField] TMP_Text       statusText;
        [Tooltip("Toggle. When ON, shows the key in plain text instead of masked.")]
        [SerializeField] Toggle         maskToggle;

        [Header("Buttons")]
        [SerializeField] Button saveButton;
        [SerializeField] Button cancelButton;
        [SerializeField] Button clearButton;

        Action<string> onSaved;

        void Awake()
        {
            if (popupRoot == null) popupRoot = gameObject;

            if (saveButton   != null) saveButton.onClick.AddListener(OnSaveClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (clearButton  != null) clearButton.onClick.AddListener(OnClearClicked);
            if (maskToggle   != null) maskToggle.onValueChanged.AddListener(UpdateMaskState);

            popupRoot.SetActive(false);
        }

        public void Show(Action<string> savedCallback = null)
        {
            this.onSaved = savedCallback;
            if (keyInput != null) keyInput.text = LoadKey();
            UpdateMaskState(maskToggle != null && maskToggle.isOn);
            UpdateStatus();

            popupRoot.SetActive(true);
            popupRoot.transform.SetAsLastSibling();

            if (keyInput != null)
            {
                keyInput.Select();
                keyInput.ActivateInputField();
            }
        }

        void Close()
        {
            popupRoot.SetActive(false);
            onSaved = null;
        }

        // ---- handlers ----------------------------------------------------

        void OnSaveClicked()
        {
            string key = (keyInput != null ? keyInput.text : "").Trim();
            PlayerPrefs.SetString(ApiKeyPrefKey, key);
            PlayerPrefs.Save();
            onSaved?.Invoke(key);
            Close();
        }

        void OnClearClicked()
        {
            if (keyInput != null) keyInput.text = "";
            PlayerPrefs.DeleteKey(ApiKeyPrefKey);
            PlayerPrefs.Save();
            UpdateStatus();
            onSaved?.Invoke("");
        }

        void UpdateMaskState(bool show)
        {
            if (keyInput == null) return;
            keyInput.contentType = show
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            keyInput.ForceLabelUpdate();
        }

        void UpdateStatus()
        {
            if (statusText == null) return;
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
    }
}

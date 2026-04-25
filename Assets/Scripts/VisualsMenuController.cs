using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Direct-edit visuals customization panel.
///
/// Presenter character: one slot per emotion. The user pastes (or browses to,
/// when running in the Editor) an image path; the image is loaded from disk
/// at runtime and shown as a preview. Saved paths persist to PlayerPrefs and
/// are re-loaded the next time the panel opens.
///
/// Card style: live-editable background color, corner radius, text color, and
/// font style. A small card preview at the bottom of the panel reflects the
/// current values in real time. Save writes everything to PlayerPrefs.
///
/// All UI lives as authored GameObjects. To rebuild the layout from scratch:
///   Tools -> AutoAvatarGen -> Build Visuals Menu UI
/// (see Assets/Editor/VisualsMenuUIBuilder.cs).
/// </summary>
public class VisualsMenuController : MonoBehaviour
{
    public const string KeyPrefix         = "AutoAvatarGen.Visuals.";
    public const string CardBgColorKey    = KeyPrefix + "Card.BgColor";
    public const string CardCornerKey     = KeyPrefix + "Card.CornerRadius";
    public const string CardTextColorKey  = KeyPrefix + "Card.TextColor";
    public const string CardFontStyleKey  = KeyPrefix + "Card.FontStyle";

    /// <summary>
    /// Canonical emotion list. Must match HybridAvatarSystem's hardcoded
    /// sprite fields. Adding a sixth emotion here also requires updating
    /// HybridAvatarSystem.cs and the script-marker parser in ScriptFileReader.
    /// </summary>
    public static readonly string[] Emotions = new[]
    {
        "Neutral", "Excited", "Serious", "Sad", "Concerned"
    };

    public static string CharacterPathKey(string emotion) =>
        KeyPrefix + "Character." + emotion;

    [Serializable]
    public class EmotionSlot
    {
        [Tooltip("Display name + the suffix used to build the PlayerPrefs key.")]
        public string emotionName;
        public InputField pathInput;
        public Button loadButton;
        public Image  preview;
    }

    [Header("Panel Root")]
    [Tooltip("The panel GameObject that gets toggled by Open() / Close.")]
    [SerializeField] GameObject panelRoot;

    [Header("Character Slots")]
    [Tooltip("One entry per emotion. The builder populates this with 5 entries " +
             "matching VisualsMenuController.Emotions.")]
    [SerializeField] EmotionSlot[] emotionSlots;

    [Header("Card Style")]
    [SerializeField] InputField bgColorInput;
    [SerializeField] Image      bgColorSwatch;
    [SerializeField] Slider     cornerRadiusSlider;
    [SerializeField] Text       cornerRadiusLabel;
    [SerializeField] InputField textColorInput;
    [SerializeField] Image      textColorSwatch;
    [SerializeField] Button     fontNormalButton;
    [SerializeField] Button     fontBoldButton;
    [SerializeField] Button     fontItalicButton;
    [SerializeField] Button     fontBoldItalicButton;

    [Header("Card Preview")]
    [SerializeField] Image cardPreviewBackground;
    [SerializeField] Text  cardPreviewText;

    [Header("Action UI")]
    [SerializeField] Button saveButton;
    [SerializeField] Button closeButton;
    [SerializeField] Text   toastText;

    Color  bgColor      = new Color(0.98f, 0.95f, 0.88f, 1f);
    float  cornerRadius = 18f;
    Color  textColor    = new Color(0.10f, 0.10f, 0.12f, 1f);
    FontStyle fontStyle = FontStyle.Normal;

    static readonly Color FontButtonInactive = new Color(0.30f, 0.32f, 0.36f, 1f);
    static readonly Color FontButtonActive   = new Color(0.20f, 0.55f, 0.85f, 1f);

    Coroutine toastCoroutine;

    void Awake()
    {
        // Character slot wiring + reload from PlayerPrefs.
        foreach (var slot in emotionSlots)
        {
            EmotionSlot captured = slot;
            string prefKey = CharacterPathKey(captured.emotionName);
            string saved   = PlayerPrefs.GetString(prefKey, "");
            captured.pathInput.text = saved;
            if (!string.IsNullOrEmpty(saved))
                LoadImageInto(saved, captured.preview);
            captured.loadButton.onClick.AddListener(() => OnLoadEmotion(captured));
        }

        // Card style wiring.
        bgColorInput.onEndEdit.AddListener(OnBgColorEdited);
        textColorInput.onEndEdit.AddListener(OnTextColorEdited);
        cornerRadiusSlider.minValue = 0f;
        cornerRadiusSlider.maxValue = 32f;
        cornerRadiusSlider.onValueChanged.AddListener(OnCornerRadiusChanged);

        fontNormalButton.onClick.AddListener(()     => SetFontStyle(FontStyle.Normal));
        fontBoldButton.onClick.AddListener(()       => SetFontStyle(FontStyle.Bold));
        fontItalicButton.onClick.AddListener(()     => SetFontStyle(FontStyle.Italic));
        fontBoldItalicButton.onClick.AddListener(() => SetFontStyle(FontStyle.BoldAndItalic));

        saveButton.onClick.AddListener(OnSave);
        closeButton.onClick.AddListener(OnClose);

        toastText.gameObject.SetActive(false);
        LoadCardStyleFromPrefs();
        UpdatePreview();
    }

    /// <summary>Show the panel. Wire to a "Visuals" button via Button.onClick.</summary>
    public void Open()
    {
        panelRoot.SetActive(true);
    }

    void OnClose()
    {
        panelRoot.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // Character image loading
    // -----------------------------------------------------------------------

    void OnLoadEmotion(EmotionSlot slot)
    {
        string path = slot.pathInput.text;

        string picked = TryPickImagePath(slot.emotionName, path);
        if (!string.IsNullOrEmpty(picked))
        {
            path = picked;
            slot.pathInput.text = picked;
        }

        if (string.IsNullOrEmpty(path))
        {
            ShowToast("Paste a path into the field, or install UnityStandaloneFileBrowser " +
                      "and define STANDALONE_FILE_BROWSER to enable the picker.");
            return;
        }

        LoadImageInto(path, slot.preview);
    }

    /// <summary>
    /// Opens a native file picker if one is available, else returns "".
    /// Resolution order:
    ///   1. UnityStandaloneFileBrowser (cross-platform — Win/Mac/Linux + Editor)
    ///      requires the STANDALONE_FILE_BROWSER scripting define and the plugin
    ///      from https://github.com/gkngkc/UnityStandaloneFileBrowser
    ///   2. UnityEditor.EditorUtility.OpenFilePanel (Editor-only fallback)
    ///   3. None — caller falls back to whatever is in the InputField.
    /// </summary>
    static string TryPickImagePath(string emotionName, string currentPath)
    {
        string startDir = !string.IsNullOrEmpty(currentPath)
            ? Path.GetDirectoryName(currentPath)
            : "";

#if STANDALONE_FILE_BROWSER
        var extensions = new[]
        {
            new SFB.ExtensionFilter("Image Files", "png", "jpg", "jpeg", "bmp", "tga"),
            new SFB.ExtensionFilter("All Files",   "*"),
        };
        string[] picked = SFB.StandaloneFileBrowser.OpenFilePanel(
            "Pick image for " + emotionName, startDir, extensions, false);
        return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel(
            "Pick image for " + emotionName, startDir, "png,jpg,jpeg,bmp,tga");
#else
        return "";
#endif
    }

    void LoadImageInto(string path, Image target)
    {
        if (!File.Exists(path))
        {
            ShowToast("File not found: " + path);
            target.sprite = null;
            target.color  = new Color(1f, 1f, 1f, 0.10f);
            return;
        }

        try
        {
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data))
            {
                ShowToast("Could not decode image: " + Path.GetFileName(path));
                return;
            }
            tex.filterMode = FilterMode.Bilinear;
            target.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));
            target.color = Color.white;
            target.preserveAspect = true;
        }
        catch (Exception e)
        {
            ShowToast("Load failed: " + e.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Card style edits
    // -----------------------------------------------------------------------

    void OnBgColorEdited(string hex)
    {
        if (TryParseHex(hex, out Color c))
        {
            bgColor = c;
            bgColorSwatch.color = c;
            UpdatePreview();
        }
        else
        {
            ShowToast("Invalid color. Use #RRGGBB or #RRGGBBAA.");
            bgColorInput.text = ColorToHex(bgColor);
        }
    }

    void OnTextColorEdited(string hex)
    {
        if (TryParseHex(hex, out Color c))
        {
            textColor = c;
            textColorSwatch.color = c;
            UpdatePreview();
        }
        else
        {
            ShowToast("Invalid color. Use #RRGGBB or #RRGGBBAA.");
            textColorInput.text = ColorToHex(textColor);
        }
    }

    void OnCornerRadiusChanged(float value)
    {
        cornerRadius = value;
        cornerRadiusLabel.text = Mathf.RoundToInt(value) + " px";
        UpdatePreview();
    }

    void SetFontStyle(FontStyle style)
    {
        fontStyle = style;
        UpdatePreview();
    }

    void UpdatePreview()
    {
        if (cardPreviewBackground != null)
        {
            Color bg = bgColor; bg.a = 1f;
            cardPreviewBackground.color = bg;
        }
        if (cardPreviewText != null)
        {
            cardPreviewText.color     = textColor;
            cardPreviewText.fontStyle = fontStyle;
        }

        // Highlight the active font-style button.
        SetButtonTint(fontNormalButton,     fontStyle == FontStyle.Normal);
        SetButtonTint(fontBoldButton,       fontStyle == FontStyle.Bold);
        SetButtonTint(fontItalicButton,     fontStyle == FontStyle.Italic);
        SetButtonTint(fontBoldItalicButton, fontStyle == FontStyle.BoldAndItalic);

        // Note: the corner-radius value is shown as a preview indicator only.
        // Rendering an actual rounded-corner UI Image requires a custom shader
        // or a generated rounded sprite; that wiring belongs in the recording
        // scene's card system, not in this menu.
    }

    static void SetButtonTint(Button btn, bool active)
    {
        if (btn == null) return;
        Color tint = active ? FontButtonActive : FontButtonInactive;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = tint;
        var colors = btn.colors;
        colors.normalColor      = tint;
        colors.highlightedColor = new Color(
            Mathf.Min(1f, tint.r + 0.10f),
            Mathf.Min(1f, tint.g + 0.10f),
            Mathf.Min(1f, tint.b + 0.10f), 1f);
        colors.pressedColor     = new Color(
            Mathf.Max(0f, tint.r - 0.12f),
            Mathf.Max(0f, tint.g - 0.12f),
            Mathf.Max(0f, tint.b - 0.12f), 1f);
        colors.selectedColor    = colors.highlightedColor;
        btn.colors = colors;
    }

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    void OnSave()
    {
        foreach (var slot in emotionSlots)
        {
            PlayerPrefs.SetString(CharacterPathKey(slot.emotionName), slot.pathInput.text ?? "");
        }
        PlayerPrefs.SetString(CardBgColorKey,   ColorToHex(bgColor));
        PlayerPrefs.SetFloat (CardCornerKey,    cornerRadius);
        PlayerPrefs.SetString(CardTextColorKey, ColorToHex(textColor));
        PlayerPrefs.SetInt   (CardFontStyleKey, (int)fontStyle);
        PlayerPrefs.Save();
        ShowToast("Saved.");
    }

    void LoadCardStyleFromPrefs()
    {
        if (TryParseHex(PlayerPrefs.GetString(CardBgColorKey, ColorToHex(bgColor)), out Color bg))
            bgColor = bg;
        if (TryParseHex(PlayerPrefs.GetString(CardTextColorKey, ColorToHex(textColor)), out Color txt))
            textColor = txt;
        cornerRadius = PlayerPrefs.GetFloat(CardCornerKey, cornerRadius);
        fontStyle    = (FontStyle)PlayerPrefs.GetInt(CardFontStyleKey, (int)fontStyle);

        bgColorInput.text          = ColorToHex(bgColor);
        bgColorSwatch.color        = bgColor;
        textColorInput.text        = ColorToHex(textColor);
        textColorSwatch.color      = textColor;
        cornerRadiusSlider.SetValueWithoutNotify(cornerRadius);
        cornerRadiusLabel.text     = Mathf.RoundToInt(cornerRadius) + " px";
    }

    // -----------------------------------------------------------------------
    // Public read API for the recording scene
    // -----------------------------------------------------------------------

    /// <summary>
    /// Path the user picked for the given emotion ("Neutral", "Excited", …),
    /// or empty if none. HybridAvatarSystem (or wherever you wire this) can
    /// read this on Awake to override its serialized sprite.
    /// </summary>
    public static string GetSavedCharacterPath(string emotion) =>
        PlayerPrefs.GetString(CharacterPathKey(emotion), "");

    public static Color  GetSavedCardBackgroundColor() =>
        TryParseHex(PlayerPrefs.GetString(CardBgColorKey, "#FAF3E0FF"), out Color c) ? c : Color.white;

    public static float  GetSavedCardCornerRadius() =>
        PlayerPrefs.GetFloat(CardCornerKey, 18f);

    public static Color  GetSavedCardTextColor() =>
        TryParseHex(PlayerPrefs.GetString(CardTextColorKey, "#1A1A1FFF"), out Color c) ? c : Color.black;

    public static FontStyle GetSavedCardFontStyle() =>
        (FontStyle)PlayerPrefs.GetInt(CardFontStyleKey, (int)FontStyle.Normal);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    static bool TryParseHex(string input, out Color color)
    {
        string s = (input ?? "").Trim();
        if (s.Length > 0 && s[0] != '#') s = "#" + s;
        return ColorUtility.TryParseHtmlString(s, out color);
    }

    static string ColorToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    void ShowToast(string message)
    {
        toastText.text = message;
        toastText.gameObject.SetActive(true);
        if (toastCoroutine != null) StopCoroutine(toastCoroutine);
        toastCoroutine = StartCoroutine(HideToastAfter(1.8f));
    }

    IEnumerator HideToastAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        toastText.gameObject.SetActive(false);
        toastCoroutine = null;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// Direct-edit visuals customization panel.
///
/// Presenter character: one slot per emotion. The user pastes (or browses to,
/// when running in the Editor or with UnityStandaloneFileBrowser installed)
/// an image path; the image is loaded from disk at runtime and shown as a
/// preview. Saved paths persist to PlayerPrefs and are re-loaded on next open.
///
/// Card style: live-editable background color, corner radius, text color, and
/// font style. A small card preview reflects the current values in real time.
///
/// Named saves: "Save As..." writes a JSON file under
/// Application.persistentDataPath/VisualsSaves/ with the image bytes embedded
/// (true backup). "Manage Saves..." opens a sub-panel for load / delete /
/// import / export. Quick "Save" still writes the active state to PlayerPrefs
/// so the editor restores between sessions even without a named save.
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
    public const string CardFontNameKey   = KeyPrefix + "Card.FontName";
    public const string BgVideoPathKey       = KeyPrefix + "BgVideoPath";   // editor working state only
    public const string BigTextStyleKey      = KeyPrefix + "BigText.Style"; // JSON-serialized BigTextStyleData
    // Music: list of paths (newline-separated) and volume — also working state only.
    // VisualsRuntimeApplier reads these as a fallback when no preset is active.
    public const string MusicListWorkingKey   = KeyPrefix + "MusicList";
    public const string MusicVolumeWorkingKey = KeyPrefix + "MusicVolume";
    public const string ActiveSaveNameKey     = KeyPrefix + "ActiveSaveName";

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

    [Header("Active Save Display")]
    [Tooltip("Label at the top of the editor that shows which named save is loaded.")]
    [SerializeField] Text activeSaveLabel;

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

    [Header("Action UI (main panel bottom row)")]
    [SerializeField] Button saveButton;
    [SerializeField] Button saveAsButton;
    [SerializeField] Button manageSavesButton;
    [SerializeField] Button closeButton;
    [SerializeField] Text   toastText;

    [Header("Manage Saves Sub-Panel")]
    [SerializeField] GameObject    savesPanelRoot;
    [SerializeField] RectTransform savesListContent; // ScrollRect.content
    [SerializeField] Text          savesEmptyLabel;
    [SerializeField] Button        importButton;
    [SerializeField] Button        exportSelectedButton;
    [SerializeField] Button        loadSelectedButton;
    [SerializeField] Button        deleteSelectedButton;
    [SerializeField] Button        savesPanelCloseButton;

    [Header("Save As Prompt Overlay")]
    [SerializeField] GameObject saveAsPromptRoot;
    [SerializeField] InputField saveAsNameInput;
    [SerializeField] Button     saveAsConfirmButton;
    [SerializeField] Button     saveAsCancelButton;

    [Header("Overwrite Confirm Overlay")]
    [SerializeField] GameObject overwriteConfirmRoot;
    [SerializeField] Text       overwriteConfirmMessage;
    [SerializeField] Button     overwriteConfirmButton;
    [SerializeField] Button     overwriteCancelButton;

    Color  bgColor      = new Color(0.98f, 0.95f, 0.88f, 1f);
    float  cornerRadius = 18f;
    Color  textColor    = new Color(0.10f, 0.10f, 0.12f, 1f);
    FontStyle fontStyle = FontStyle.Normal;

    // Active font selection. Empty = no override (TMP default). Format matches
    // FontRegistry.FontEntry.Identifier — "project:<name>", "system:<name>",
    // or "user:<absolute path>".
    string fontIdentifier = "";

    // BigText overlay style — edited via BigTextStylePopup, persisted in
    // both the JSON save and a single PlayerPrefs JSON blob.
    BigTextStyleData bigTextStyle = new BigTextStyleData();
    Button           bigTextEditButton;

    // Background mp4 path saved with the preset. The main menu's
    // BackgroundVideoOverridePrefKey still wins at scene load.
    string           backgroundVideoPath = "";
    Text             bgVideoLabel;
    Button           bgVideoLoadButton;
    Button           bgVideoClearButton;

    // Background music — playlist of paths + volume (default 0.25 per README).
    BackgroundMusicData musicData = new BackgroundMusicData();
    Button              musicEditButton;

    // Auto-built font picker controls (runtime, no scene wiring required).
    Text   fontDisplayLabel;
    Button fontPrevButton;
    Button fontNextButton;
    Button fontLoadFileButton;

    // TMP overlay over the legacy cardPreviewText so the preview can render
    // any TMP_FontAsset. The legacy Text is hidden (enabled = false) on first
    // build and the TMP one drives all visible preview state thereafter.
    TextMeshProUGUI cardPreviewTmp;

    string activeSaveName   = "";
    string selectedSaveName = "";
    Action pendingOverwriteAction;

    static readonly Color FontButtonInactive = new Color(0.30f, 0.32f, 0.36f, 1f);
    static readonly Color FontButtonActive   = new Color(0.20f, 0.55f, 0.85f, 1f);
    static readonly Color RowUnselected      = new Color(0.16f, 0.18f, 0.22f, 1f);
    static readonly Color RowSelected        = new Color(0.20f, 0.45f, 0.75f, 1f);

    Coroutine toastCoroutine;

    void Awake()
    {
        // Character slot listener wiring (per-slot values are loaded later by
        // LoadActivePresetOrDefaults so the active named save wins over the
        // potentially-stale PlayerPrefs char paths).
        foreach (var slot in emotionSlots)
        {
            EmotionSlot captured = slot;
            captured.loadButton.onClick.AddListener(() => OnLoadEmotion(captured));
        }

        // Card style wiring.
        bgColorInput.onEndEdit.AddListener(OnBgColorEdited);
        textColorInput.onEndEdit.AddListener(OnTextColorEdited);
        AttachColorPicker(bgColorSwatch,   () => bgColor,   c => ApplyPickedBgColor(c));
        AttachColorPicker(textColorSwatch, () => textColor, c => ApplyPickedTextColor(c));
        cornerRadiusSlider.minValue = 0f;
        cornerRadiusSlider.maxValue = 32f;
        cornerRadiusSlider.onValueChanged.AddListener(OnCornerRadiusChanged);
        fontNormalButton.onClick.AddListener(()     => SetFontStyle(FontStyle.Normal));
        fontBoldButton.onClick.AddListener(()       => SetFontStyle(FontStyle.Bold));
        fontItalicButton.onClick.AddListener(()     => SetFontStyle(FontStyle.Italic));
        fontBoldItalicButton.onClick.AddListener(() => SetFontStyle(FontStyle.BoldAndItalic));

        EnsureTmpCardPreview();
        BuildFontRow();
        BuildBigTextEditButton();
        BuildBgVideoRow();
        BuildMusicEditButton();

        // Bottom row.
        saveButton.onClick.AddListener(OnQuickSave);
        saveAsButton.onClick.AddListener(OnSaveAsClicked);
        manageSavesButton.onClick.AddListener(OnManageSavesClicked);
        closeButton.onClick.AddListener(OnClose);

        // Saves sub-panel.
        importButton.onClick.AddListener(OnImportClicked);
        exportSelectedButton.onClick.AddListener(OnExportSelectedClicked);
        loadSelectedButton.onClick.AddListener(OnLoadSelectedClicked);
        deleteSelectedButton.onClick.AddListener(OnDeleteSelectedClicked);
        savesPanelCloseButton.onClick.AddListener(() => savesPanelRoot.SetActive(false));

        // Save As prompt.
        saveAsConfirmButton.onClick.AddListener(OnSaveAsConfirm);
        saveAsCancelButton.onClick.AddListener(()  => saveAsPromptRoot.SetActive(false));
        saveAsNameInput.onSubmit.AddListener(_     => OnSaveAsConfirm());

        // Overwrite confirm.
        overwriteConfirmButton.onClick.AddListener(OnOverwriteConfirm);
        overwriteCancelButton.onClick.AddListener(()  => overwriteConfirmRoot.SetActive(false));

        // Initial state.
        toastText.gameObject.SetActive(false);
        savesPanelRoot.SetActive(false);
        saveAsPromptRoot.SetActive(false);
        overwriteConfirmRoot.SetActive(false);

        LoadActivePresetOrDefaults();
    }

    /// <summary>Show the panel. Wire to a "Visuals" button via Button.onClick.</summary>
    public void Open()
    {
        gameObject.SetActive(true);
        panelRoot.SetActive(true);
        // Always pull the latest state from the active save (or PlayerPrefs
        // fallback) so the editor reflects the currently-active preset rather
        // than whatever was left in memory from a previous open.
        LoadActivePresetOrDefaults();
    }

    // Loads the editor state from the named JSON save selected as active in
    // the main menu. If no save is active (or the file no longer exists),
    // falls back to the per-emotion paths + card style stored in PlayerPrefs
    // — the implicit "untitled" working state from a Quick Save.
    void LoadActivePresetOrDefaults()
    {
        activeSaveName = PlayerPrefs.GetString(ActiveSaveNameKey, "");

        if (!string.IsNullOrEmpty(activeSaveName))
        {
            var save = VisualsSaveStore.Load(activeSaveName);
            if (save != null)
            {
                ApplySaveToUI(save);
                activeSaveName = save.name;
                UpdateActiveSaveLabel();
                return;
            }
            // The named save was deleted out from under us — clear the stale
            // pref and fall through to the PlayerPrefs defaults.
            activeSaveName = "";
            PlayerPrefs.DeleteKey(ActiveSaveNameKey);
            PlayerPrefs.Save();
        }

        UpdateActiveSaveLabel();

        foreach (var slot in emotionSlots)
        {
            string saved = PlayerPrefs.GetString(CharacterPathKey(slot.emotionName), "");
            slot.pathInput.text = saved;
            if (!string.IsNullOrEmpty(saved))
            {
                LoadImageInto(saved, slot.preview);
            }
            else
            {
                slot.preview.sprite = null;
                slot.preview.color  = new Color(1f, 1f, 1f, 0.10f);
            }
        }
        LoadCardStyleFromPrefs();
        UpdatePreview();
    }

    // The PresetsButton in the main menu opens this panel by SetActive(true) on
    // the controller's GameObject, so closing must deactivate the same object
    // (not just panelRoot). Otherwise re-clicking PresetsButton is a no-op
    // because the parent is still active and the inner panelRoot stays hidden.
    void OnClose()
    {
        savesPanelRoot.SetActive(false);
        saveAsPromptRoot.SetActive(false);
        overwriteConfirmRoot.SetActive(false);
        gameObject.SetActive(false);
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
            ApplyBytesToImage(data, target);
        }
        catch (Exception e)
        {
            ShowToast("Load failed: " + e.Message);
        }
    }

    bool ApplyBytesToImage(byte[] data, Image target)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(data))
        {
            ShowToast("Could not decode embedded image.");
            return false;
        }
        tex.filterMode = FilterMode.Bilinear;
        target.sprite  = Sprite.Create(tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f));
        target.color   = Color.white;
        target.preserveAspect = true;
        return true;
    }

    // -----------------------------------------------------------------------
    // Card style edits
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Live card preview (TMP overlay) — lets the preview render any
    // TMP_FontAsset chosen in the font picker. Built once over the legacy
    // cardPreviewText, which then gets disabled.
    // -----------------------------------------------------------------------

    void EnsureTmpCardPreview()
    {
        if (cardPreviewTmp != null || cardPreviewText == null) return;

        var go = new GameObject("CardPreviewTmp", typeof(RectTransform));
        go.transform.SetParent(cardPreviewText.transform.parent, false);

        // Mirror the legacy Text's RectTransform so the TMP overlay sits
        // exactly on top of where the legacy text used to render.
        var src = cardPreviewText.rectTransform;
        var dst = (RectTransform)go.transform;
        dst.anchorMin        = src.anchorMin;
        dst.anchorMax        = src.anchorMax;
        dst.pivot            = src.pivot;
        dst.anchoredPosition = src.anchoredPosition;
        dst.sizeDelta        = src.sizeDelta;
        dst.offsetMin        = src.offsetMin;
        dst.offsetMax        = src.offsetMax;

        cardPreviewTmp = go.AddComponent<TextMeshProUGUI>();
        cardPreviewTmp.text          = cardPreviewText.text;
        cardPreviewTmp.fontSize      = cardPreviewText.fontSize;
        cardPreviewTmp.alignment     = ToTmpAlignment(cardPreviewText.alignment);
        cardPreviewTmp.color         = textColor;
        cardPreviewTmp.fontStyle     = ToTmpFontStyles(fontStyle);
        cardPreviewTmp.enableWordWrapping = true;
        cardPreviewTmp.overflowMode  = TextOverflowModes.Truncate;
        cardPreviewTmp.raycastTarget = false;

        cardPreviewText.enabled = false;
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

    static TextAlignmentOptions ToTmpAlignment(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
            case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
            default:                      return TextAlignmentOptions.Center;
        }
    }

    // -----------------------------------------------------------------------
    // Font picker (auto-built; not part of VisualsMenuUIBuilder)
    // -----------------------------------------------------------------------

    void BuildFontRow()
    {
        if (panelRoot == null) return;

        // Place in the gap between the live card preview (~y=-340 bottom) and
        // the bottom action button row (top edge at y=-455). Anchor centered.
        var row = new GameObject("FontRow", typeof(RectTransform));
        row.transform.SetParent(panelRoot.transform, false);
        var rt = (RectTransform)row.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -395f);
        rt.sizeDelta        = new Vector2(1700f, 60f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = labelRT.anchorMax = labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.anchoredPosition = new Vector2(-260f, 0f);
        labelRT.sizeDelta        = new Vector2(1000f, 50f);
        fontDisplayLabel = labelGO.AddComponent<Text>();
        fontDisplayLabel.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        fontDisplayLabel.fontSize  = 22;
        fontDisplayLabel.alignment = TextAnchor.MiddleLeft;
        fontDisplayLabel.color     = new Color(0.85f, 0.88f, 0.93f, 1f);
        fontDisplayLabel.text      = "Font:  (default)";

        fontPrevButton     = AddFontRowButton(row.transform, "Prev",  "<",  new Vector2( 350f, 0f), new Vector2(60f, 50f));
        fontNextButton     = AddFontRowButton(row.transform, "Next",  ">",  new Vector2( 420f, 0f), new Vector2(60f, 50f));
        fontLoadFileButton = AddFontRowButton(row.transform, "Load",  "Load Font File…",
                                              new Vector2(640f, 0f), new Vector2(280f, 50f));

        fontPrevButton.onClick.AddListener(() => CycleFont(-1));
        fontNextButton.onClick.AddListener(() => CycleFont(+1));
        fontLoadFileButton.onClick.AddListener(OnLoadFontFileClicked);
    }

    static Button AddFontRowButton(Transform parent, string name, string label,
                                   Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.65f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var t = labelGO.AddComponent<Text>();
        t.text       = label;
        t.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize   = 22;
        t.fontStyle  = FontStyle.Bold;
        t.alignment  = TextAnchor.MiddleCenter;
        t.color      = Color.white;
        return btn;
    }

    void CycleFont(int delta)
    {
        // Slot 0 is "(default)" — no override. Slots 1..N follow FontRegistry.All.
        var all = FontRegistry.All;
        int total = all.Count + 1;
        if (total <= 1) return;

        int current = string.IsNullOrEmpty(fontIdentifier) ? 0 : 1 + IndexOf(all, fontIdentifier);
        if (current < 0) current = 0;

        int next = ((current + delta) % total + total) % total;
        fontIdentifier = (next == 0) ? "" : all[next - 1].Identifier;
        UpdateFontDisplay();
    }

    static int IndexOf(IReadOnlyList<FontEntry> all, string identifier)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i].Identifier == identifier) return i;
        return -1;
    }

    void UpdateFontDisplay()
    {
        FontEntry entry = string.IsNullOrEmpty(fontIdentifier) ? null : FontRegistry.Resolve(fontIdentifier);

        if (fontDisplayLabel != null)
        {
            if (string.IsNullOrEmpty(fontIdentifier))
                fontDisplayLabel.text = "Font:  (default)";
            else
                fontDisplayLabel.text = "Font:  " +
                    (entry != null ? entry.DisplayName : fontIdentifier + "  (unresolved)");
        }

        // Live preview reflects the current pick; null falls back to the TMP
        // default so the preview never goes blank after picking "(default)".
        if (cardPreviewTmp != null)
            cardPreviewTmp.font = entry?.Asset ?? TMP_Settings.defaultFontAsset;
    }

    void OnLoadFontFileClicked()
    {
        string picked = TryPickFontPath();
        if (string.IsNullOrEmpty(picked)) return;

        FontEntry entry = FontRegistry.LoadFromFile(picked);
        if (entry == null)
        {
            ShowToast("Could not load font. Runtime file loading is Windows-only " +
                      "and the file's family name should match its file name.");
            return;
        }
        fontIdentifier = entry.Identifier;
        UpdateFontDisplay();
        ShowToast("Loaded font:  " + entry.DisplayName);
    }

    static string TryPickFontPath()
    {
#if STANDALONE_FILE_BROWSER
        var ext = new[] { new SFB.ExtensionFilter("Font Files", "ttf", "otf") };
        var picked = SFB.StandaloneFileBrowser.OpenFilePanel("Pick font file", "", ext, false);
        return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel("Pick font file", "", "ttf,otf");
#else
        return "";
#endif
    }

    // -----------------------------------------------------------------------
    // Background music editor (preset-saved playlist, with main-menu override)
    // -----------------------------------------------------------------------

    void BuildMusicEditButton()
    {
        if (panelRoot == null) return;
        // Tucked next to the Big Text edit button at y=-395 so it shares the
        // bottom strip with the other compound editors.
        musicEditButton = AddFontRowButton(panelRoot.transform, "MusicEditButton",
            "Edit Music…", new Vector2(-340f, -395f), new Vector2(280f, 50f));
        musicEditButton.onClick.AddListener(OnMusicEditClicked);
    }

    void OnMusicEditClicked()
    {
        if (panelRoot == null) return;
        MusicEditPopup.GetOrCreate(panelRoot.transform)
                      .Show(musicData, OnMusicChanged);
    }

    // Mirror to PlayerPrefs (working state) and to the active named save's
    // JSON so the playlist + volume flow into the next recording without
    // requiring a Quick Save.
    void OnMusicChanged()
    {
        PlayerPrefs.SetString(MusicListWorkingKey,
            MugsTech.Background.BackgroundMusicPlayer.SerializePathList(musicData.filePaths));
        PlayerPrefs.SetFloat (MusicVolumeWorkingKey, musicData.volume);
        PlayerPrefs.Save();

        if (!string.IsNullOrEmpty(activeSaveName) && VisualsSaveStore.Exists(activeSaveName))
        {
            try { VisualsSaveStore.Save(CaptureCurrentState(activeSaveName)); }
            catch (Exception e) { Debug.LogWarning($"[VisualsMenu] Music mirror failed: {e.Message}"); }
        }
    }

    static BackgroundMusicData CloneMusicData(BackgroundMusicData src)
    {
        return new BackgroundMusicData
        {
            filePaths = src.filePaths != null ? new List<string>(src.filePaths) : new List<string>(),
            volume    = src.volume,
        };
    }

    // -----------------------------------------------------------------------
    // Background video (preset-saved, overridden by main menu's field)
    // -----------------------------------------------------------------------

    void BuildBgVideoRow()
    {
        if (panelRoot == null) return;

        var row = new GameObject("BgVideoRow", typeof(RectTransform));
        row.transform.SetParent(panelRoot.transform, false);
        var rt = (RectTransform)row.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        // Sits between the font / Big-Text row (y=-395) and the action button
        // strip (y=-495, top edge at -455). 36 px tall to keep clear of both.
        rt.anchoredPosition = new Vector2(0f, -432f);
        rt.sizeDelta        = new Vector2(1700f, 36f);

        // Label (path display)
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var lRT = (RectTransform)labelGO.transform;
        lRT.anchorMin = lRT.anchorMax = lRT.pivot = new Vector2(0.5f, 0.5f);
        lRT.anchoredPosition = new Vector2(-300f, 0f);
        lRT.sizeDelta        = new Vector2(1000f, 40f);
        bgVideoLabel = labelGO.AddComponent<Text>();
        bgVideoLabel.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bgVideoLabel.fontSize  = 20;
        bgVideoLabel.alignment = TextAnchor.MiddleLeft;
        bgVideoLabel.color     = new Color(0.85f, 0.88f, 0.93f, 1f);
        bgVideoLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

        bgVideoLoadButton  = AddFontRowButton(row.transform, "BgVideoLoad",  "Load…",
            new Vector2(390f, 0f), new Vector2(160f, 40f));
        bgVideoClearButton = AddFontRowButton(row.transform, "BgVideoClear", "Clear",
            new Vector2(580f, 0f), new Vector2(160f, 40f));

        bgVideoLoadButton.onClick.AddListener(OnBgVideoLoadClicked);
        bgVideoClearButton.onClick.AddListener(OnBgVideoClearClicked);
        UpdateBgVideoLabel();
    }

    void UpdateBgVideoLabel()
    {
        if (bgVideoLabel == null) return;
        bgVideoLabel.text = "BG Video:  " + (string.IsNullOrEmpty(backgroundVideoPath)
            ? "(none — uses scene default)"
            : Path.GetFileName(backgroundVideoPath));
    }

    void OnBgVideoLoadClicked()
    {
        string picked = TryPickVideoPath(backgroundVideoPath);
        Debug.Log($"[BgVideoDiag] VisualsMenu OnBgVideoLoadClicked picked='{picked}'");
        if (string.IsNullOrEmpty(picked)) return;
        backgroundVideoPath = picked;
        UpdateBgVideoLabel();
        OnBgVideoChanged();
    }

    void OnBgVideoClearClicked()
    {
        if (string.IsNullOrEmpty(backgroundVideoPath)) return;
        backgroundVideoPath = "";
        UpdateBgVideoLabel();
        OnBgVideoChanged();
    }

    // Mirror to PlayerPrefs (editor working state) and to the active named
    // save's JSON so the change flows into the next recording without an
    // explicit Quick Save.
    void OnBgVideoChanged()
    {
        PlayerPrefs.SetString(BgVideoPathKey, backgroundVideoPath ?? "");
        PlayerPrefs.Save();
        Debug.Log($"[BgVideoDiag] VisualsMenu OnBgVideoChanged wrote BgVideoPathKey='{backgroundVideoPath ?? ""}' " +
                  $"activeSaveName='{activeSaveName}'");

        if (!string.IsNullOrEmpty(activeSaveName) && VisualsSaveStore.Exists(activeSaveName))
        {
            try
            {
                VisualsSaveStore.Save(CaptureCurrentState(activeSaveName));
                Debug.Log($"[BgVideoDiag]   mirrored to JSON save '{activeSaveName}'");
            }
            catch (Exception e) { Debug.LogWarning($"[VisualsMenu] BG video mirror failed: {e.Message}"); }
        }
        else
        {
            Debug.Log("[BgVideoDiag]   no active save; only PlayerPrefs working-state updated.");
        }
    }

    static string TryPickVideoPath(string current)
    {
        string startDir = !string.IsNullOrEmpty(current) ? Path.GetDirectoryName(current) : "";
#if STANDALONE_FILE_BROWSER
        var ext = new[]
        {
            new SFB.ExtensionFilter("Video Files", "mp4", "mov", "webm", "m4v"),
            new SFB.ExtensionFilter("All Files",   "*"),
        };
        var picked = SFB.StandaloneFileBrowser.OpenFilePanel(
            "Pick background video", startDir, ext, false);
        return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel(
            "Pick background video", startDir, "mp4,mov,webm,m4v");
#else
        return "";
#endif
    }

    // -----------------------------------------------------------------------
    // BigText style editor
    // -----------------------------------------------------------------------

    void BuildBigTextEditButton()
    {
        if (panelRoot == null) return;

        // Place under the font row (font row is at y=-395, h=60). Put this
        // one a bit lower-left so it doesn't visually crowd the action row.
        bigTextEditButton = AddFontRowButton(panelRoot.transform, "BigTextEditButton",
            "Edit Big Text…", new Vector2(-650f, -395f), new Vector2(280f, 50f));
        bigTextEditButton.onClick.AddListener(OnBigTextEditClicked);
    }

    void OnBigTextEditClicked()
    {
        if (panelRoot == null) return;
        // Pass the currently-picked font so the popup's preview matches what
        // the recording will render. Falls back to TMP default when "(default)"
        // is selected.
        TMP_FontAsset font = string.IsNullOrEmpty(fontIdentifier)
            ? TMP_Settings.defaultFontAsset
            : (FontRegistry.Resolve(fontIdentifier)?.Asset ?? TMP_Settings.defaultFontAsset);

        BigTextStylePopup.GetOrCreate(panelRoot.transform)
                         .Show(bigTextStyle, font, OnBigTextStyleChanged);
    }

    // Mirror BigText edits into the active named save (if any) and into
    // PlayerPrefs, so changes flow into the next recording without requiring
    // an explicit Quick Save.
    void OnBigTextStyleChanged()
    {
        PlayerPrefs.SetString(BigTextStyleKey, JsonUtility.ToJson(bigTextStyle));
        PlayerPrefs.Save();

        if (!string.IsNullOrEmpty(activeSaveName) && VisualsSaveStore.Exists(activeSaveName))
        {
            try { VisualsSaveStore.Save(CaptureCurrentState(activeSaveName)); }
            catch (Exception e) { Debug.LogWarning($"[VisualsMenu] BigText mirror failed: {e.Message}"); }
        }
    }

    static BigTextStyleData CloneBigTextStyle(BigTextStyleData src)
    {
        return new BigTextStyleData
        {
            textColorHex            = src.textColorHex,
            fontStyle               = src.fontStyle,
            outlineColorHex         = src.outlineColorHex,
            outlineWidth            = src.outlineWidth,
            shadowEnabled           = src.shadowEnabled,
            shadowColorHex          = src.shadowColorHex,
            shadowSoftness          = src.shadowSoftness,
            backgroundEnabled       = src.backgroundEnabled,
            backgroundColorHex      = src.backgroundColorHex,
            backgroundCornerRadius  = src.backgroundCornerRadius,
        };
    }

    // -----------------------------------------------------------------------
    // Color-wheel picker
    // -----------------------------------------------------------------------

    void AttachColorPicker(Image swatch, Func<Color> getCurrent, Action<Color> onPicked)
    {
        if (swatch == null) return;
        var btn = swatch.GetComponent<Button>();
        if (btn == null) btn = swatch.gameObject.AddComponent<Button>();
        btn.targetGraphic = swatch;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
            ColorWheelPopup.GetOrCreate(panelRoot.transform).Show(getCurrent(), onPicked));
    }

    void ApplyPickedBgColor(Color c)
    {
        bgColor             = c;
        bgColorSwatch.color = c;
        bgColorInput.text   = ColorToHex(c);
        UpdatePreview();
    }

    void ApplyPickedTextColor(Color c)
    {
        textColor             = c;
        textColorSwatch.color = c;
        textColorInput.text   = ColorToHex(c);
        UpdatePreview();
    }

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
        if (cardPreviewTmp != null)
        {
            cardPreviewTmp.color     = textColor;
            cardPreviewTmp.fontStyle = ToTmpFontStyles(fontStyle);
        }

        SetButtonTint(fontNormalButton,     fontStyle == FontStyle.Normal);
        SetButtonTint(fontBoldButton,       fontStyle == FontStyle.Bold);
        SetButtonTint(fontItalicButton,     fontStyle == FontStyle.Italic);
        SetButtonTint(fontBoldItalicButton, fontStyle == FontStyle.BoldAndItalic);

        // Corner-radius value is shown numerically only — rendering an actual
        // rounded-corner UI Image needs a custom shader / generated sprite,
        // which lives in the recording scene's card system.
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
    // Quick-save (PlayerPrefs)
    // -----------------------------------------------------------------------

    void OnQuickSave()
    {
        foreach (var slot in emotionSlots)
        {
            PlayerPrefs.SetString(CharacterPathKey(slot.emotionName), slot.pathInput.text ?? "");
        }
        PlayerPrefs.SetString(CardBgColorKey,   ColorToHex(bgColor));
        PlayerPrefs.SetFloat (CardCornerKey,    cornerRadius);
        PlayerPrefs.SetString(CardTextColorKey, ColorToHex(textColor));
        PlayerPrefs.SetInt   (CardFontStyleKey, (int)fontStyle);
        PlayerPrefs.SetString(CardFontNameKey,  fontIdentifier ?? "");
        PlayerPrefs.SetString(BgVideoPathKey,        backgroundVideoPath ?? "");
        PlayerPrefs.SetString(MusicListWorkingKey,   MugsTech.Background.BackgroundMusicPlayer.SerializePathList(musicData.filePaths));
        PlayerPrefs.SetFloat (MusicVolumeWorkingKey, musicData.volume);
        PlayerPrefs.SetString(BigTextStyleKey,       JsonUtility.ToJson(bigTextStyle));
        PlayerPrefs.Save();

        // Mirror into the active named save's JSON so edits flow through to the
        // recording (which loads from the JSON, not PlayerPrefs). Without this,
        // a user editing an already-loaded preset would only see changes after
        // re-running Save As. Skipped silently when nothing is active.
        if (!string.IsNullOrEmpty(activeSaveName) && VisualsSaveStore.Exists(activeSaveName))
        {
            try
            {
                VisualsSaveStore.Save(CaptureCurrentState(activeSaveName));
                ShowToast("Quick-saved (mirrored to \"" + activeSaveName + "\").");
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VisualsMenu] Quick-save mirror failed: {e.Message}");
            }
        }
        ShowToast("Quick-saved (active state).");
    }

    void LoadCardStyleFromPrefs()
    {
        if (TryParseHex(PlayerPrefs.GetString(CardBgColorKey, ColorToHex(bgColor)), out Color bg))
            bgColor = bg;
        if (TryParseHex(PlayerPrefs.GetString(CardTextColorKey, ColorToHex(textColor)), out Color txt))
            textColor = txt;
        cornerRadius   = PlayerPrefs.GetFloat (CardCornerKey,    cornerRadius);
        fontStyle      = (FontStyle)PlayerPrefs.GetInt(CardFontStyleKey, (int)fontStyle);
        fontIdentifier = PlayerPrefs.GetString(CardFontNameKey,  "");
        UpdateFontDisplay();

        backgroundVideoPath = PlayerPrefs.GetString(BgVideoPathKey, "");
        UpdateBgVideoLabel();

        musicData = new BackgroundMusicData
        {
            filePaths = MugsTech.Background.BackgroundMusicPlayer.ParsePathList(
                            PlayerPrefs.GetString(MusicListWorkingKey, "")),
            volume    = PlayerPrefs.GetFloat(MusicVolumeWorkingKey,
                            MugsTech.Background.BackgroundMusicPlayer.DefaultVolume),
        };

        string bigJson = PlayerPrefs.GetString(BigTextStyleKey, "");
        if (!string.IsNullOrEmpty(bigJson))
        {
            try { JsonUtility.FromJsonOverwrite(bigJson, bigTextStyle); }
            catch { /* fall back to defaults already on the field */ }
        }

        bgColorInput.text          = ColorToHex(bgColor);
        bgColorSwatch.color        = bgColor;
        textColorInput.text        = ColorToHex(textColor);
        textColorSwatch.color      = textColor;
        cornerRadiusSlider.SetValueWithoutNotify(cornerRadius);
        cornerRadiusLabel.text     = Mathf.RoundToInt(cornerRadius) + " px";
    }

    // -----------------------------------------------------------------------
    // Save As flow
    // -----------------------------------------------------------------------

    void OnSaveAsClicked()
    {
        saveAsNameInput.text = !string.IsNullOrEmpty(activeSaveName)
            ? activeSaveName : "MyVisuals";
        saveAsPromptRoot.SetActive(true);
        saveAsNameInput.Select();
        saveAsNameInput.ActivateInputField();
    }

    void OnSaveAsConfirm()
    {
        string name = (saveAsNameInput.text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowToast("Name cannot be empty.");
            return;
        }
        if (VisualsSaveStore.Exists(name))
        {
            ShowOverwritePrompt(name, () => DoSaveAs(name));
        }
        else
        {
            DoSaveAs(name);
        }
    }

    void DoSaveAs(string name)
    {
        try
        {
            var data = CaptureCurrentState(name);
            VisualsSaveStore.Save(data);
            activeSaveName = name;
            UpdateActiveSaveLabel();
            PlayerPrefs.SetString(ActiveSaveNameKey, name);
            PlayerPrefs.Save();
            saveAsPromptRoot.SetActive(false);
            ShowToast("Saved: " + name);
        }
        catch (Exception e)
        {
            ShowToast("Save failed: " + e.Message);
        }
    }

    VisualsSaveFile CaptureCurrentState(string saveName)
    {
        var data = new VisualsSaveFile
        {
            schemaVersion       = 1,
            name                = saveName,
            backgroundVideoPath = backgroundVideoPath ?? "",
            card = new CardStyleData
            {
                bgColorHex   = ColorToHex(bgColor),
                cornerRadius = cornerRadius,
                textColorHex = ColorToHex(textColor),
                fontStyle    = (int)fontStyle,
                fontName     = fontIdentifier ?? "",
            },
            bigText = CloneBigTextStyle(bigTextStyle),
            music   = CloneMusicData(musicData),
        };

        foreach (var slot in emotionSlots)
        {
            var entry = new EmotionImageData
            {
                emotion      = slot.emotionName,
                originalPath = slot.pathInput.text ?? "",
            };
            if (!string.IsNullOrEmpty(entry.originalPath) && File.Exists(entry.originalPath))
            {
                try
                {
                    entry.imageBase64 = Convert.ToBase64String(File.ReadAllBytes(entry.originalPath));
                    entry.extension   = Path.GetExtension(entry.originalPath).TrimStart('.').ToLowerInvariant();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsMenu] Could not embed {slot.emotionName}: {e.Message}");
                }
            }
            data.emotions.Add(entry);
        }
        return data;
    }

    // -----------------------------------------------------------------------
    // Overwrite confirm overlay
    // -----------------------------------------------------------------------

    /// <summary>Generic yes/no confirm using the overwrite dialog overlay.</summary>
    void ShowConfirmPrompt(string message, Action onConfirm)
    {
        overwriteConfirmMessage.text = message;
        pendingOverwriteAction = onConfirm;
        overwriteConfirmRoot.SetActive(true);
    }

    void ShowOverwritePrompt(string name, Action onConfirm) =>
        ShowConfirmPrompt("\"" + name + "\" already exists.\nOverwrite it?", onConfirm);

    void OnOverwriteConfirm()
    {
        overwriteConfirmRoot.SetActive(false);
        var pending = pendingOverwriteAction;
        pendingOverwriteAction = null;
        pending?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Manage Saves sub-panel
    // -----------------------------------------------------------------------

    void OnManageSavesClicked()
    {
        selectedSaveName = activeSaveName;
        savesPanelRoot.SetActive(true);
        RefreshSavesList();
    }

    void RefreshSavesList()
    {
        // Tear down old rows.
        for (int i = savesListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(savesListContent.GetChild(i).gameObject);
        }

        var names = VisualsSaveStore.ListSaveNames();
        savesEmptyLabel.gameObject.SetActive(names.Length == 0);

        // Verify the selection still exists; clear if not.
        if (!string.IsNullOrEmpty(selectedSaveName))
        {
            bool stillExists = false;
            foreach (var n in names) { if (n == selectedSaveName) { stillExists = true; break; } }
            if (!stillExists) selectedSaveName = "";
        }

        foreach (var name in names)
        {
            BuildSaveRow(name);
        }
    }

    void BuildSaveRow(string name)
    {
        var row = new GameObject(name + "Row", typeof(RectTransform));
        row.transform.SetParent(savesListContent, false);

        var img = row.AddComponent<Image>();
        img.color = (name == selectedSaveName) ? RowSelected : RowUnselected;

        var btn = row.AddComponent<Button>();
        var btnColors = btn.colors;
        btnColors.normalColor      = img.color;
        btnColors.highlightedColor = new Color(img.color.r + 0.04f, img.color.g + 0.04f, img.color.b + 0.04f, 1f);
        btnColors.pressedColor     = new Color(img.color.r - 0.04f, img.color.g - 0.04f, img.color.b - 0.04f, 1f);
        btn.colors = btnColors;
        btn.targetGraphic = img;

        string captured = name;
        btn.onClick.AddListener(() => SelectSave(captured));

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 50f;
        le.minHeight       = 50f;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var t = labelGO.AddComponent<Text>();
        t.text      = (name == activeSaveName) ? name + "   ★ active" : name;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = 22;
        t.alignment = TextAnchor.MiddleLeft;
        t.color     = Color.white;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16, 0);
        trt.offsetMax = new Vector2(-16, 0);
    }

    void SelectSave(string name)
    {
        selectedSaveName = name;
        RefreshSavesList();
    }

    void OnLoadSelectedClicked()
    {
        if (string.IsNullOrEmpty(selectedSaveName))
        {
            ShowToast("Select a save first.");
            return;
        }
        var data = VisualsSaveStore.Load(selectedSaveName);
        if (data == null)
        {
            ShowToast("Could not load: " + selectedSaveName);
            return;
        }
        ApplySaveToUI(data);
        activeSaveName = data.name;
        UpdateActiveSaveLabel();
        PlayerPrefs.SetString(ActiveSaveNameKey, activeSaveName);
        PlayerPrefs.Save();
        savesPanelRoot.SetActive(false);
        ShowToast("Loaded: " + activeSaveName);
    }

    void OnDeleteSelectedClicked()
    {
        if (string.IsNullOrEmpty(selectedSaveName))
        {
            ShowToast("Select a save first.");
            return;
        }
        string toDelete = selectedSaveName;
        ShowConfirmPrompt(
            "Delete \"" + toDelete + "\"?\nThis cannot be undone.",
            () =>
            {
                VisualsSaveStore.Delete(toDelete);
                if (activeSaveName == toDelete)
                {
                    activeSaveName = "";
                    UpdateActiveSaveLabel();
                    PlayerPrefs.DeleteKey(ActiveSaveNameKey);
                    PlayerPrefs.Save();
                }
                selectedSaveName = "";
                ShowToast("Deleted: " + toDelete);
                RefreshSavesList();
            });
    }

    // -----------------------------------------------------------------------
    // Apply a loaded save to the UI / state
    // -----------------------------------------------------------------------

    void ApplySaveToUI(VisualsSaveFile data)
    {
        if (TryParseHex(data.card.bgColorHex,   out Color bg)) bgColor   = bg;
        if (TryParseHex(data.card.textColorHex, out Color tx)) textColor = tx;
        cornerRadius   = data.card.cornerRadius;
        fontStyle      = (FontStyle)data.card.fontStyle;
        fontIdentifier = data.card.fontName ?? "";
        UpdateFontDisplay();

        // BigText round-trip — fall back to defaults if the save predates this field.
        bigTextStyle = data.bigText != null ? CloneBigTextStyle(data.bigText) : new BigTextStyleData();

        backgroundVideoPath = data.backgroundVideoPath ?? "";
        UpdateBgVideoLabel();

        musicData = data.music != null ? CloneMusicData(data.music) : new BackgroundMusicData();

        bgColorInput.text          = ColorToHex(bgColor);
        bgColorSwatch.color        = bgColor;
        textColorInput.text        = ColorToHex(textColor);
        textColorSwatch.color      = textColor;
        cornerRadiusSlider.SetValueWithoutNotify(cornerRadius);
        cornerRadiusLabel.text     = Mathf.RoundToInt(cornerRadius) + " px";
        UpdatePreview();

        foreach (var slot in emotionSlots)
        {
            var emo = data.emotions.Find(e => e.emotion == slot.emotionName);
            if (emo == null) continue;

            slot.pathInput.text = emo.originalPath ?? "";

            // Prefer the live path so source-file edits propagate; fall back
            // to embedded bytes if the path no longer exists on this machine.
            if (!string.IsNullOrEmpty(emo.originalPath) && File.Exists(emo.originalPath))
            {
                LoadImageInto(emo.originalPath, slot.preview);
            }
            else if (!string.IsNullOrEmpty(emo.imageBase64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(emo.imageBase64);
                    ApplyBytesToImage(bytes, slot.preview);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisualsMenu] Embedded {slot.emotionName} unreadable: {e.Message}");
                    slot.preview.sprite = null;
                    slot.preview.color  = new Color(1f, 1f, 1f, 0.10f);
                }
            }
            else
            {
                slot.preview.sprite = null;
                slot.preview.color  = new Color(1f, 1f, 1f, 0.10f);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Import / Export
    // -----------------------------------------------------------------------

    void OnExportSelectedClicked()
    {
        if (string.IsNullOrEmpty(selectedSaveName))
        {
            ShowToast("Select a save first.");
            return;
        }
        var data = VisualsSaveStore.Load(selectedSaveName);
        if (data == null)
        {
            ShowToast("Could not load: " + selectedSaveName);
            return;
        }
        string path = TryPickSavePath(selectedSaveName + ".json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            VisualsSaveStore.ExportTo(data, path);
            ShowToast("Exported: " + Path.GetFileName(path));
        }
        catch (Exception e)
        {
            ShowToast("Export failed: " + e.Message);
        }
    }

    void OnImportClicked()
    {
        string path = TryPickJsonOpenPath();
        if (string.IsNullOrEmpty(path)) return;
        VisualsSaveFile data;
        try { data = VisualsSaveStore.LoadFromFile(path); }
        catch (Exception e) { ShowToast("Import failed: " + e.Message); return; }
        if (data == null)
        {
            ShowToast("Not a valid visuals save.");
            return;
        }
        if (string.IsNullOrEmpty(data.name))
            data.name = Path.GetFileNameWithoutExtension(path);

        if (VisualsSaveStore.Exists(data.name))
        {
            ShowOverwritePrompt(data.name, () => DoImport(data));
        }
        else
        {
            DoImport(data);
        }
    }

    void DoImport(VisualsSaveFile data)
    {
        try
        {
            VisualsSaveStore.Save(data);
            selectedSaveName = data.name;
            ShowToast("Imported: " + data.name);
            RefreshSavesList();
        }
        catch (Exception e)
        {
            ShowToast("Import failed: " + e.Message);
        }
    }

    static string TryPickSavePath(string defaultFileName)
    {
#if STANDALONE_FILE_BROWSER
        var ext = new[] { new SFB.ExtensionFilter("Visuals Save", "json") };
        return SFB.StandaloneFileBrowser.SaveFilePanel(
            "Export Visuals Save", "", defaultFileName, ext);
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.SaveFilePanel(
            "Export Visuals Save", "", defaultFileName, "json");
#else
        return "";
#endif
    }

    static string TryPickJsonOpenPath()
    {
#if STANDALONE_FILE_BROWSER
        var ext = new[] { new SFB.ExtensionFilter("Visuals Save", "json") };
        var picked = SFB.StandaloneFileBrowser.OpenFilePanel(
            "Import Visuals Save", "", ext, false);
        return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel(
            "Import Visuals Save", "", "json");
#else
        return "";
#endif
    }

    // -----------------------------------------------------------------------
    // Active save label
    // -----------------------------------------------------------------------

    void UpdateActiveSaveLabel()
    {
        if (string.IsNullOrEmpty(activeSaveName))
        {
            activeSaveLabel.text  = "Editing:  Untitled";
            activeSaveLabel.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        }
        else
        {
            activeSaveLabel.text  = "Editing:  " + activeSaveName;
            activeSaveLabel.color = new Color(0.85f, 0.88f, 0.93f, 1f);
        }
    }

    // -----------------------------------------------------------------------
    // Public read API for the recording scene
    // -----------------------------------------------------------------------

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

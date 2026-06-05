using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    // PlayerPrefs key holding the working-state list of emotion names
    // (newline-separated). Lets Quick Save persist user-added / renamed
    // emotions even when no named save is active. The list always starts
    // with "Neutral" — that slot is protected from delete/rename.
    public const string EmotionNamesKey       = KeyPrefix + "EmotionNames";

    /// <summary>
    /// Default emotion set used to seed brand-new editors. The runtime list
    /// is dynamic — users can rename any of these (except Neutral), delete
    /// the four non-Neutral defaults, and add new emotions. HybridAvatarSystem
    /// looks up emotions by string key, so anything in this list (or added by
    /// the user) is callable from the narration script via {EmotionName}.
    /// </summary>
    public static readonly string[] Emotions = new[]
    {
        "Neutral", "Excited", "Serious", "Sad", "Concerned"
    };

    public const string NeutralEmotionName = "Neutral";

    public static string CharacterPathKey(string emotion) =>
        KeyPrefix + "Character." + emotion;

    [Serializable]
    public class EmotionSlot
    {
        [Tooltip("Display name. For Neutral this is fixed; other rows can be renamed.")]
        public string emotionName;
        public InputField pathInput;
        public Button loadButton;
        public Image  preview;

        [Tooltip("Editable name field. Read-only / hidden for the Neutral row.")]
        public InputField nameInput;
        [Tooltip("Delete button. Hidden / disabled for the Neutral row.")]
        public Button     removeButton;

        // Marks rows built at runtime via the + button so we can tear them
        // down when loading a different save without destroying the
        // inspector-wired starter rows (which the UI builder created).
        [NonSerialized] public bool runtimeBuilt;
    }

    [Header("Panel Root")]
    [Tooltip("The panel GameObject that gets toggled by Open() / Close.")]
    [SerializeField] GameObject panelRoot;

    [Header("Active Save Display")]
    [Tooltip("Label at the top of the editor that shows which named save is loaded.")]
    [SerializeField] Text activeSaveLabel;

    [Header("Character Slots")]
    [Tooltip("Initial emotion rows wired up by the UI builder (one per default " +
             "emotion). At runtime these are copied into the working list and " +
             "additional rows can be added via the + button.")]
    [SerializeField] EmotionSlot[] emotionSlots;

    [Tooltip("Parent transform the new emotion rows are attached to. Falls back " +
             "to the first wired slot's parent when null (the typical setup).")]
    [SerializeField] Transform     emotionRowsParent;
    [Tooltip("Optional '+' button below the emotion rows. Created at runtime " +
             "when this slot is empty.")]
    [SerializeField] Button        addEmotionButton;

    // Runtime working list — the actual source of truth. Seeded from
    // emotionSlots in Awake; the + button appends to it, the X button on a
    // row removes from it. Anything in this list is included in Quick Save
    // and Save As, and HybridAvatarSystem will load a sprite for it on the
    // next recording.
    readonly List<EmotionSlot> emotions = new List<EmotionSlot>();

    // Geometry of an emotion row, captured from the first wired slot in
    // Awake. Used both when re-positioning rows after add/remove and when
    // building brand-new rows at runtime. Falls back to baked-in defaults if
    // nothing is wired (defensive — the UI builder always populates the
    // starter set in practice).
    Vector2 emotionRowSize     = new Vector2(900f, 100f);
    float   emotionRowSpacing  = 105f;
    Vector2 emotionRowTopAnchor = new Vector2(-460f, 290f); // (x, y) of the first row's center

    // True when the rows live inside a VerticalLayoutGroup (the modern
    // scrollable layout). In that mode we let the layout group control row
    // positioning instead of writing anchored positions ourselves, and the
    // Add Emotion button is kept as the last sibling. False = legacy scenes
    // where rows are anchored at fixed Y positions on the panel.
    bool emotionsUseLayoutGroup;
    ScrollRect emotionsScrollRect;

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

    // Background mp4 path persisted with the preset. The editing UI was
    // removed, but the field is kept so existing saves keep round-tripping and
    // VisualsRuntimeApplier can still apply a path baked into an older save.
    string           backgroundVideoPath = "";

    // Background music — playlist of paths + volume. The editing UI was removed
    // (no music is authored from the menu anymore); the data is retained so
    // existing save files keep loading without errors.
    BackgroundMusicData musicData = new BackgroundMusicData();

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
        // Seed the runtime emotion list from the inspector-wired starter rows
        // and remember their geometry so newly-built rows match the look.
        SeedEmotionListFromInspector();
        CaptureRowGeometryFromFirstSlot();
        EnsureEmotionRowsParent();
        EnsureAddEmotionButton();

        // Per-slot wiring: load button + the new name field + remove button.
        // Neutral is special-cased — its name is fixed and it can't be deleted
        // (it's the avatar's idle/fallback sprite).
        foreach (var slot in emotions)
        {
            WireEmotionSlot(slot);
        }
        RepositionEmotionRows();

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

        // Skin every button to the cohesive minimalist palette. Done on open so
        // it also catches emotion rows added since the last time it ran.
        UITheme.Apply(gameObject);
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

        // Restore the list of emotion names the user had configured (Quick
        // Save persists this) so dynamic add/rename survive an app restart
        // without requiring a named save.
        List<string> persistedNames = LoadEmotionNamesPref();
        if (persistedNames != null && persistedNames.Count > 0)
            SyncEmotionListToNames(persistedNames);

        foreach (var slot in emotions)
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
            if (slot.nameInput != null) slot.nameInput.text = slot.emotionName;
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

    // -----------------------------------------------------------------------
    // Dynamic emotion list — wiring, add / remove / rename helpers.
    //
    // Emotions are the runtime list of rows shown in the panel. The inspector
    // array (emotionSlots) is just the seed for fresh editors. The "+ Add
    // Emotion" button appends to the list; per-row "X" buttons remove from
    // it (except for Neutral, which is fixed). The name field on each row
    // lets the user pick what {Token} the narration script should fire for
    // that emotion — sanitization keeps it script-safe.
    // -----------------------------------------------------------------------

    static readonly Regex EmotionNameSanitizer =
        new Regex(@"[^A-Za-z0-9_]", RegexOptions.Compiled);

    void SeedEmotionListFromInspector()
    {
        emotions.Clear();
        if (emotionSlots == null) return;
        foreach (var slot in emotionSlots)
        {
            if (slot == null) continue;
            slot.runtimeBuilt = false;
            emotions.Add(slot);
        }
    }

    void CaptureRowGeometryFromFirstSlot()
    {
        if (emotions.Count < 1 || emotions[0].preview == null) return;
        var firstRow = emotions[0].preview.transform.parent as RectTransform;
        if (firstRow == null) return;
        emotionRowTopAnchor = firstRow.anchoredPosition;
        emotionRowSize      = firstRow.sizeDelta;

        if (emotions.Count >= 2 && emotions[1].preview != null)
        {
            var secondRow = emotions[1].preview.transform.parent as RectTransform;
            if (secondRow != null)
            {
                float spacing = emotionRowTopAnchor.y - secondRow.anchoredPosition.y;
                if (spacing > 1f) emotionRowSpacing = spacing;
            }
        }
    }

    void EnsureEmotionRowsParent()
    {
        if (emotionRowsParent == null &&
            emotions.Count > 0 && emotions[0].preview != null)
        {
            emotionRowsParent = emotions[0].preview.transform.parent.parent;
        }

        if (emotionRowsParent != null)
        {
            // Detect whether we're in the modern scroll-view layout (parent
            // has a VerticalLayoutGroup) or the legacy anchored layout.
            emotionsUseLayoutGroup = emotionRowsParent.GetComponent<VerticalLayoutGroup>() != null;

            // Walk up to find the enclosing ScrollRect so we can auto-scroll
            // to a newly-added row. Walks at most a few levels in practice
            // (content → viewport → scroll).
            emotionsScrollRect = emotionRowsParent.GetComponentInParent<ScrollRect>();
        }
    }

    // Builds (or wires) the "+ Add Emotion" button. Inspector slot wins; when
    // empty, a runtime button is created and parented to emotionRowsParent.
    void EnsureAddEmotionButton()
    {
        if (addEmotionButton == null && emotionRowsParent != null)
        {
            addEmotionButton = BuildRuntimeAddButton(emotionRowsParent);
        }
        if (addEmotionButton != null)
        {
            addEmotionButton.onClick.RemoveAllListeners();
            addEmotionButton.onClick.AddListener(OnAddEmotionClicked);
        }
    }

    void WireEmotionSlot(EmotionSlot slot)
    {
        if (slot == null) return;

        bool isNeutral = string.Equals(slot.emotionName, NeutralEmotionName, StringComparison.Ordinal);

        // Load button — original behaviour. Re-wired idempotently in case the
        // builder already attached a no-op listener.
        if (slot.loadButton != null)
        {
            slot.loadButton.onClick.RemoveAllListeners();
            EmotionSlot capturedLoad = slot;
            slot.loadButton.onClick.AddListener(() => OnLoadEmotion(capturedLoad));
        }

        // Name input — lazily created on legacy rows that predate this field.
        // Hidden for Neutral; otherwise wired to OnRenameEmotion on endEdit.
        if (slot.nameInput == null && !isNeutral && slot.preview != null)
        {
            slot.nameInput = BuildRuntimeNameInput(slot.preview.transform.parent);
        }
        if (slot.nameInput != null)
        {
            slot.nameInput.text = slot.emotionName;
            slot.nameInput.onEndEdit.RemoveAllListeners();
            if (isNeutral)
            {
                slot.nameInput.readOnly      = true;
                slot.nameInput.interactable  = false;
            }
            else
            {
                EmotionSlot capturedName = slot;
                slot.nameInput.onEndEdit.AddListener(s => OnRenameEmotion(capturedName, s));
            }
        }

        // Remove button — Neutral keeps no remove button. For other rows,
        // create one if the builder didn't.
        if (slot.removeButton == null && !isNeutral && slot.preview != null)
        {
            slot.removeButton = BuildRuntimeRemoveButton(slot.preview.transform.parent);
        }
        if (slot.removeButton != null)
        {
            slot.removeButton.gameObject.SetActive(!isNeutral);
            slot.removeButton.onClick.RemoveAllListeners();
            if (!isNeutral)
            {
                EmotionSlot capturedRemove = slot;
                slot.removeButton.onClick.AddListener(() => OnRemoveEmotionClicked(capturedRemove));
            }
        }
    }

    void RepositionEmotionRows()
    {
        if (emotionsUseLayoutGroup)
        {
            // Layout group mode: rows are ordered by sibling index, the VLG
            // handles vertical stacking. We just enforce the slot list's
            // order and keep the Add button as the last visible child.
            for (int i = 0; i < emotions.Count; i++)
            {
                var slot = emotions[i];
                if (slot?.preview == null) continue;
                var row = slot.preview.transform.parent;
                if (row != null) row.SetSiblingIndex(i);
            }
            if (addEmotionButton != null)
                addEmotionButton.transform.SetAsLastSibling();
            return;
        }

        // Legacy anchored-position mode (scenes that pre-date the scroll view).
        for (int i = 0; i < emotions.Count; i++)
        {
            var slot = emotions[i];
            if (slot?.preview == null) continue;
            var row = slot.preview.transform.parent as RectTransform;
            if (row == null) continue;
            row.anchoredPosition = new Vector2(
                emotionRowTopAnchor.x,
                emotionRowTopAnchor.y - i * emotionRowSpacing);
            row.sizeDelta = emotionRowSize;
        }

        if (addEmotionButton != null)
        {
            var addRT = addEmotionButton.transform as RectTransform;
            if (addRT != null)
            {
                addRT.anchoredPosition = new Vector2(
                    emotionRowTopAnchor.x,
                    emotionRowTopAnchor.y - emotions.Count * emotionRowSpacing);
            }
        }
    }

    void OnAddEmotionClicked()
    {
        string newName = SanitizeEmotionName("Emotion" + (emotions.Count + 1), null);
        var slot = BuildRuntimeEmotionRow(newName);
        if (slot == null) return;
        emotions.Add(slot);
        WireEmotionSlot(slot);
        RepositionEmotionRows();
        PersistEmotionNamesPref();
        ScrollToBottomNextFrame();
    }

    // Force a layout rebuild on the content (so the new row's height counts)
    // and snap the scroll view to the bottom so the user sees what they just
    // added. Deferred one frame because the LayoutGroup recalculates in its
    // own LateUpdate — scrolling sooner positions against stale bounds.
    void ScrollToBottomNextFrame()
    {
        if (emotionsScrollRect == null) return;
        StartCoroutine(ScrollToBottomCo());
    }

    IEnumerator ScrollToBottomCo()
    {
        yield return null;
        if (emotionRowsParent is RectTransform contentRT)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
        if (emotionsScrollRect != null)
            emotionsScrollRect.verticalNormalizedPosition = 0f;
    }

    void OnRemoveEmotionClicked(EmotionSlot slot)
    {
        if (slot == null) return;
        if (string.Equals(slot.emotionName, NeutralEmotionName, StringComparison.Ordinal))
            return; // Neutral is protected.

        emotions.Remove(slot);

        // Tear down the actual row GameObject. We don't try to distinguish
        // runtime-built rows from inspector-wired ones — the user explicitly
        // asked to remove this slot, so it goes regardless of where it came
        // from. The inspector-wired field on `emotionSlots` may briefly hold
        // a destroyed reference until the next Awake, but nothing else reads
        // it (the runtime list is the source of truth).
        if (slot.preview != null)
        {
            var row = slot.preview.transform.parent;
            if (row != null) Destroy(row.gameObject);
        }

        // Also drop the now-orphan PlayerPref so it doesn't haunt the editor.
        PlayerPrefs.DeleteKey(CharacterPathKey(slot.emotionName));
        RepositionEmotionRows();
        PersistEmotionNamesPref();
    }

    void OnRenameEmotion(EmotionSlot slot, string raw)
    {
        if (slot == null) return;
        if (string.Equals(slot.emotionName, NeutralEmotionName, StringComparison.Ordinal))
        {
            // Shouldn't happen — Neutral's field is read-only — but defend
            // against external scripts forcing a value through onEndEdit.
            if (slot.nameInput != null) slot.nameInput.text = NeutralEmotionName;
            return;
        }

        string clean = SanitizeEmotionName(raw, slot.emotionName);
        if (string.Equals(clean, slot.emotionName, StringComparison.Ordinal))
        {
            // No effective change — just sync the field text to the canonical
            // version (handles the case where the user typed extra spaces
            // around an unchanged name).
            if (slot.nameInput != null) slot.nameInput.text = slot.emotionName;
            return;
        }

        // Move the saved per-emotion path so the user doesn't lose their
        // configured image when renaming.
        string oldKey = CharacterPathKey(slot.emotionName);
        string newKey = CharacterPathKey(clean);
        if (PlayerPrefs.HasKey(oldKey))
        {
            PlayerPrefs.SetString(newKey, PlayerPrefs.GetString(oldKey, ""));
            PlayerPrefs.DeleteKey(oldKey);
        }

        slot.emotionName = clean;
        if (slot.nameInput != null) slot.nameInput.text = clean;

        PersistEmotionNamesPref();
    }

    // Strips characters the script-marker regex can't match (it only accepts
    // \w+ — letters, digits, underscore), trims to a sane length, falls back
    // to a default when the input collapses to empty, and dedupes against
    // every other emotion already in the list. `current` is the name to
    // ignore during dedupe (i.e. the slot's existing name during a rename).
    string SanitizeEmotionName(string raw, string current)
    {
        string s = (raw ?? "").Trim();
        if (s.Length > 0)
        {
            s = EmotionNameSanitizer.Replace(s, "_");
            // Don't allow a leading digit — \w+ in C# regex accepts it, but
            // it reads awkwardly in scripts (`{1Cool}`). Prepend an underscore.
            if (char.IsDigit(s[0])) s = "_" + s;
        }
        if (s.Length == 0) s = "Emotion";
        if (s.Length > 32) s = s.Substring(0, 32);

        // Dedupe — append _2, _3, … until the name is free.
        string candidate = s;
        int suffix = 2;
        while (true)
        {
            bool clash = false;
            foreach (var slot in emotions)
            {
                if (slot == null) continue;
                if (current != null && string.Equals(slot.emotionName, current, StringComparison.Ordinal)) continue;
                if (string.Equals(slot.emotionName, candidate, StringComparison.Ordinal))
                {
                    clash = true;
                    break;
                }
            }
            if (!clash) return candidate;
            candidate = s + "_" + suffix;
            suffix++;
        }
    }

    // Reconciles the live row set with a target name list. Used when loading
    // a named save and when restoring the working-state list from PlayerPrefs.
    // Renames existing rows (preserving their wiring), removes rows that
    // aren't in the target list, and creates dynamic rows for any new names.
    void SyncEmotionListToNames(List<string> targetNames)
    {
        if (targetNames == null) return;

        // Make sure Neutral is always first regardless of save ordering.
        var orderedTarget = new List<string>();
        bool hasNeutral = false;
        foreach (var n in targetNames)
        {
            if (string.IsNullOrEmpty(n)) continue;
            if (string.Equals(n, NeutralEmotionName, StringComparison.Ordinal))
            {
                hasNeutral = true;
                continue;
            }
            orderedTarget.Add(n);
        }
        var finalOrder = new List<string>();
        finalOrder.Add(NeutralEmotionName);
        finalOrder.AddRange(orderedTarget);
        // Caller may or may not have passed Neutral; either way it goes first.
        if (!hasNeutral) { /* no-op — Neutral was synthesized */ }

        // First pass — pair existing slots to target names. We keep the
        // first matching-by-name slot, plus the Neutral slot. Anything left
        // unmatched gets reused (by rename) for new target names; if there
        // still aren't enough we build new runtime rows.
        var unmatched = new List<EmotionSlot>(emotions);
        var result    = new List<EmotionSlot>(finalOrder.Count);

        foreach (string name in finalOrder)
        {
            int idx = unmatched.FindIndex(s =>
                s != null && string.Equals(s.emotionName, name, StringComparison.Ordinal));
            if (idx >= 0)
            {
                result.Add(unmatched[idx]);
                unmatched.RemoveAt(idx);
            }
            else
            {
                result.Add(null); // placeholder, filled below
            }
        }

        // Fill placeholders. Reuse unmatched non-Neutral slots via rename
        // first (so the inspector-wired rows don't get destroyed), then
        // build runtime rows for the remainder.
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i] != null) continue;
            string targetName = finalOrder[i];

            EmotionSlot reuse = null;
            for (int u = 0; u < unmatched.Count; u++)
            {
                if (unmatched[u] != null &&
                    !string.Equals(unmatched[u].emotionName, NeutralEmotionName, StringComparison.Ordinal))
                {
                    reuse = unmatched[u];
                    unmatched.RemoveAt(u);
                    break;
                }
            }

            if (reuse != null)
            {
                // Move the saved path under the new key, then rename.
                string oldKey = CharacterPathKey(reuse.emotionName);
                string newKey = CharacterPathKey(targetName);
                if (PlayerPrefs.HasKey(oldKey))
                {
                    PlayerPrefs.SetString(newKey, PlayerPrefs.GetString(oldKey, ""));
                    PlayerPrefs.DeleteKey(oldKey);
                }
                reuse.emotionName = targetName;
                if (reuse.nameInput != null) reuse.nameInput.text = targetName;
                result[i] = reuse;
            }
            else
            {
                var built = BuildRuntimeEmotionRow(targetName);
                result[i] = built;
            }
        }

        // Anything still unmatched (after reuse) was in the previous list but
        // isn't in the target — tear it down. Never destroys Neutral.
        foreach (var stale in unmatched)
        {
            if (stale == null) continue;
            if (string.Equals(stale.emotionName, NeutralEmotionName, StringComparison.Ordinal)) continue;
            if (stale.preview != null)
            {
                var row = stale.preview.transform.parent;
                if (row != null) Destroy(row.gameObject);
            }
        }

        emotions.Clear();
        emotions.AddRange(result);
        foreach (var slot in emotions) WireEmotionSlot(slot);
        RepositionEmotionRows();
    }

    string SerializeEmotionNames()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < emotions.Count; i++)
        {
            if (emotions[i] == null) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(emotions[i].emotionName);
        }
        return sb.ToString();
    }

    List<string> LoadEmotionNamesPref()
    {
        string raw = PlayerPrefs.GetString(EmotionNamesKey, "");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = new List<string>();
        foreach (string line in raw.Split('\n'))
        {
            string t = line.Trim();
            if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list;
    }

    void PersistEmotionNamesPref()
    {
        PlayerPrefs.SetString(EmotionNamesKey, SerializeEmotionNames());
        PlayerPrefs.Save();
    }

    // -----------------------------------------------------------------------
    // Runtime row construction. Mirrors the layout produced by
    // VisualsMenuUIBuilder.CreateEmotionSlot so dynamic rows look identical.
    // -----------------------------------------------------------------------

    EmotionSlot BuildRuntimeEmotionRow(string emotionName)
    {
        if (emotionRowsParent == null) return null;

        var rowGO = new GameObject(emotionName + "Slot", typeof(RectTransform));
        rowGO.transform.SetParent(emotionRowsParent, false);
        var rowRT = (RectTransform)rowGO.transform;
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 0.5f);
        rowRT.sizeDelta = emotionRowSize;

        // In scroll-view mode the parent's VerticalLayoutGroup needs a
        // preferred height to know how tall to make the row. In legacy mode
        // the LayoutElement is harmless (no VLG reads it).
        if (emotionsUseLayoutGroup)
        {
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = emotionRowSize.y;
            le.minHeight       = emotionRowSize.y;
        }

        // Preview
        var preview = NewRowImage("Preview", rowGO.transform, new Vector2(-410f, 0f), new Vector2(80f, 80f));
        preview.color           = new Color(1f, 1f, 1f, 0.10f);
        preview.preserveAspect  = true;

        // Path input
        var pathInput = NewRowInputField("PathInput", rowGO.transform,
            new Vector2(-15f, -16f), new Vector2(660f, 44f),
            "C:\\path\\to\\" + emotionName.ToLowerInvariant() + ".png");

        // Load button
        var loadButton = NewRowButton("LoadButton", rowGO.transform, "Load",
            new Vector2(380f, 0f), new Vector2(100f, 44f),
            new Color(0.20f, 0.45f, 0.65f), 22, FontStyle.Bold);
        // The original row positions Load at y=-16; mirror it for visual parity.
        var loadRT = (RectTransform)loadButton.transform;
        loadRT.anchoredPosition = new Vector2(380f, -16f);

        var slot = new EmotionSlot
        {
            emotionName  = emotionName,
            preview      = preview,
            pathInput    = pathInput,
            loadButton   = loadButton,
            runtimeBuilt = true,
        };
        // WireEmotionSlot will spawn name + remove buttons via the helpers
        // below — same code path as legacy rows.
        return slot;
    }

    InputField BuildRuntimeNameInput(Transform rowTransform)
    {
        var inputGO = new GameObject("NameInput", typeof(RectTransform));
        inputGO.transform.SetParent(rowTransform, false);
        var rt = (RectTransform)inputGO.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-265f, 28f);
        rt.sizeDelta        = new Vector2(220f, 36f);

        var bg = inputGO.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.17f, 0.21f, 1f);

        var input = inputGO.AddComponent<InputField>();
        input.characterLimit = 32;

        var textGO = NewRowText("Text", inputGO.transform, "", 22, TextAnchor.MiddleLeft, FontStyle.Bold);
        var textRT = textGO.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 2f);
        textRT.offsetMax = new Vector2(-10f, -2f);
        textGO.supportRichText = false;

        var phGO = NewRowText("Placeholder", inputGO.transform, "Name", 22, TextAnchor.MiddleLeft, FontStyle.Italic);
        phGO.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        var phRT = phGO.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(10f, 2f);
        phRT.offsetMax = new Vector2(-10f, -2f);

        input.textComponent = textGO;
        input.placeholder   = phGO;
        input.targetGraphic = bg;
        return input;
    }

    Button BuildRuntimeRemoveButton(Transform rowTransform)
    {
        var btn = NewRowButton("RemoveButton", rowTransform, "X",
            new Vector2(450f, -16f), new Vector2(44f, 44f),
            new Color(0.62f, 0.22f, 0.22f), 22, FontStyle.Bold);
        return btn;
    }

    Button BuildRuntimeAddButton(Transform parent)
    {
        var btn = NewRowButton("AddEmotionButton", parent, "+ Add Emotion",
            new Vector2(emotionRowTopAnchor.x, emotionRowTopAnchor.y - emotions.Count * emotionRowSpacing),
            new Vector2(260f, 44f),
            new Color(0.18f, 0.55f, 0.34f), 22, FontStyle.Bold);
        if (emotionsUseLayoutGroup)
        {
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.minHeight       = 50f;
        }
        return btn;
    }

    static Image NewRowImage(string name, Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        var img = go.AddComponent<Image>();
        return img;
    }

    static Text NewRowText(string name, Transform parent, string value, int size,
                           TextAnchor alignment, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text      = value;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.fontStyle = style;
        t.alignment = alignment;
        t.color     = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        return t;
    }

    static InputField NewRowInputField(string name, Transform parent,
                                       Vector2 anchoredPos, Vector2 size,
                                       string placeholderHint)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.17f, 0.21f, 1f);

        var input = go.AddComponent<InputField>();

        var text = NewRowText("Text", go.transform, "", 22, TextAnchor.MiddleLeft, FontStyle.Normal);
        text.supportRichText = false;
        var textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(14f, 4f);
        textRT.offsetMax = new Vector2(-14f, -4f);

        var ph = NewRowText("Placeholder", go.transform, placeholderHint, 22,
                            TextAnchor.MiddleLeft, FontStyle.Italic);
        ph.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        var phRT = ph.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(14f, 4f);
        phRT.offsetMax = new Vector2(-14f, -4f);

        input.textComponent = text;
        input.placeholder   = ph;
        input.targetGraphic = bg;
        return input;
    }

    static Button NewRowButton(string name, Transform parent, string label,
                               Vector2 anchoredPos, Vector2 size, Color tint,
                               int fontSize, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        var img = go.AddComponent<Image>();
        img.color = tint;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor      = tint;
        colors.highlightedColor = new Color(Mathf.Min(1f, tint.r + 0.10f),
                                            Mathf.Min(1f, tint.g + 0.10f),
                                            Mathf.Min(1f, tint.b + 0.10f), 1f);
        colors.pressedColor     = new Color(Mathf.Max(0f, tint.r - 0.12f),
                                            Mathf.Max(0f, tint.g - 0.12f),
                                            Mathf.Max(0f, tint.b - 0.12f), 1f);
        colors.selectedColor    = colors.highlightedColor;
        btn.colors = colors;

        var labelText = NewRowText("Label", go.transform, label, fontSize,
                                   TextAnchor.MiddleCenter, style);
        var lblRT = labelText.rectTransform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
        return btn;
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

        // Live preview reflects the current pick; null falls back to Inter
        // Regular SDF (from Resources/Fonts/RecordingText/) — the new default
        // for HeadlineCard's source text — so the preview matches what the
        // recording will render. Final TMP default keeps the preview visible
        // if Inter isn't on disk.
        if (cardPreviewTmp != null)
            cardPreviewTmp.font = entry?.Asset
                ?? ContentCardUIBuilder.InterRegular
                ?? TMP_Settings.defaultFontAsset;
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

    // Background music + background video editing UIs were removed. The save
    // data they wrote (musicData / backgroundVideoPath) is still round-tripped
    // by CaptureCurrentState / ApplySaveData below so existing presets load
    // unchanged; CloneMusicData stays because the save/load path uses it.
    static BackgroundMusicData CloneMusicData(BackgroundMusicData src)
    {
        return new BackgroundMusicData
        {
            filePaths = src.filePaths != null ? new List<string>(src.filePaths) : new List<string>(),
            volume    = src.volume,
        };
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
        // the recording will render. Falls back to Inter Bold SDF (from
        // Resources/Fonts/RecordingText/) when "(default)" is selected — big
        // text is Bold 700 per the typographic guideline.
        TMP_FontAsset bigTextFallback = ContentCardUIBuilder.InterBold
                                        ?? TMP_Settings.defaultFontAsset;
        TMP_FontAsset font = string.IsNullOrEmpty(fontIdentifier)
            ? bigTextFallback
            : (FontRegistry.Resolve(fontIdentifier)?.Asset ?? bigTextFallback);

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
        foreach (var slot in emotions)
        {
            PlayerPrefs.SetString(CharacterPathKey(slot.emotionName), slot.pathInput.text ?? "");
        }
        // Mirror the live emotion-name list so user-added / renamed slots
        // survive an app restart even without a named save.
        PlayerPrefs.SetString(EmotionNamesKey, SerializeEmotionNames());
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

        foreach (var slot in emotions)
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

        musicData = data.music != null ? CloneMusicData(data.music) : new BackgroundMusicData();

        bgColorInput.text          = ColorToHex(bgColor);
        bgColorSwatch.color        = bgColor;
        textColorInput.text        = ColorToHex(textColor);
        textColorSwatch.color      = textColor;
        cornerRadiusSlider.SetValueWithoutNotify(cornerRadius);
        cornerRadiusLabel.text     = Mathf.RoundToInt(cornerRadius) + " px";
        UpdatePreview();

        // Reshape the live row set to match the save's emotion list. New
        // dynamic rows are spawned for names beyond the initial set; rows
        // whose name no longer appears in the save are removed (Neutral is
        // always kept regardless). Renames are applied in-place.
        var savedNames = new List<string>();
        if (data.emotions != null)
            foreach (var e in data.emotions)
                if (!string.IsNullOrEmpty(e?.emotion)) savedNames.Add(e.emotion);
        SyncEmotionListToNames(savedNames);

        foreach (var slot in emotions)
        {
            var emo = data.emotions?.Find(e => e.emotion == slot.emotionName);
            if (slot.nameInput != null) slot.nameInput.text = slot.emotionName;

            if (emo == null)
            {
                slot.pathInput.text = "";
                slot.preview.sprite = null;
                slot.preview.color  = new Color(1f, 1f, 1f, 0.10f);
                continue;
            }

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

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using MugsTech.Style;

/// <summary>
/// Drives the main menu scene: Start Recording (launches the recording scene
/// via RecordingSession.Begin) and Quit. On return from a recording, reads
/// RecordingSession.LastResult and shows success / failure plus the saved
/// file path.
///
/// Also exposes:
///   - A text field for the Python pre-processor output folder. ScriptFileReader
///     reads the same PlayerPrefs key and overrides its own pythonOutputFolder.
///   - A text field + Browse button for the external media root folder
///     (containing BRoll / Images / Logos subfolders). MediaPresentationSystem
///     reads the same PlayerPrefs key and overrides its own externalMediaRoot.
///
/// All path fields auto-save to PlayerPrefs on edit (onEndEdit) and on Browse,
/// so once a folder is linked the user doesn't have to relink it next session.
///
/// The UI itself lives as authored GameObjects in MainMenu.unity. Re-build
/// the hierarchy with: Tools -> AutoAvatarGen -> Build Main Menu UI
/// (see Assets/Editor/MainMenuUIBuilder.cs).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // Shared with ScriptFileReader. If you rename this, rename it there too.
    public const string PythonOutputFolderPrefKey = "AutoAvatarGen.PythonOutputFolder";
    public const string DefaultPythonOutputFolder = "Python/output";

    // Shared with ScriptFileReader. When set to 1, the recording scene loads
    // ElevenLabs output from Application.streamingAssetsPath/Python/output
    // (i.e. Assets/StreamingAssets/Python/output in the Editor) so it gets
    // bundled with the build. Lets you test recording from a build without
    // rerunning the TTS pipeline, and the _timed.txt files stay editable in
    // the project. Overrides PythonOutputFolderPrefKey when on.
    public const string UseBundledTtsOutputPrefKey = "AutoAvatarGen.UseBundledTtsOutput";
    public const string BundledTtsOutputSubfolder  = "Python/output";

    // Shared with CrossPlatformRecorder. Empty string = don't override, use
    // the recorder's inspector `saveFolder` (which itself falls back to the
    // Evereal default folder when empty). Absolute paths are used as-is;
    // relative paths are resolved against the project root, matching the
    // recorder's existing convention.
    public const string RecordingOutputFolderPrefKey = "AutoAvatarGen.RecordingOutputFolder";

    // Shared with MediaPresentationSystem. Empty string = use the inspector default
    // (which is empty too, meaning "fall back to Resources/Media").
    public const string MediaRootFolderPrefKey = MediaPresentationSystem.MediaRootFolderPrefKey;

    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text pathText;
    [SerializeField] TMP_InputField pathInput;
    [SerializeField] Button pathBrowseButton;
    [SerializeField] Button startButton;
    [SerializeField] Button quitButton;

    [Header("External Media Root (BRoll / Images / Logos)")]
    [Tooltip("Optional. If left null, the controller spawns its own row at runtime.")]
    [SerializeField] TMP_InputField mediaRootInput;
    [SerializeField] Button         mediaRootBrowseButton;

    [Header("Recording Output Folder")]
    [Tooltip("Optional. Drop a TMP_InputField + Button into the scene and drag " +
             "them here. The path is saved to a PlayerPref that CrossPlatformRecorder " +
             "reads in Awake() — absolute paths are used as-is, relative paths resolve " +
             "against the project root. Leave blank to use the recorder's inspector " +
             "value (or Evereal's default folder if that's empty too).")]
    [SerializeField] TMP_InputField recordingOutputInput;
    [SerializeField] Button         recordingOutputBrowseButton;

    [Header("Bundled TTS Output (build-testable)")]
    [Tooltip("Optional. Drop a Toggle into the scene and drag it here. When " +
             "checked, the recording scene reads ElevenLabs output from " +
             "Assets/StreamingAssets/Python/output (which gets bundled with the " +
             "build) instead of the configured ElevenLabs folder. The _timed.txt " +
             "files stay editable in the project so you can tune timestamps and " +
             "re-test the recording in a build without rerunning the TTS pipeline.")]
    [SerializeField] Toggle useBundledTtsOutputToggle;

    // Output library (generation chooser) — generated at runtime by
    // OutputLibraryController; no scene authoring or Tools menu needed.
    OutputLibraryController outputLibrary;

    [Header("Active Visuals Save")]
    [Tooltip("Optional. If left null, the controller spawns its own row at runtime.")]
    [SerializeField] TMP_Text activeSaveLabel;
    [SerializeField] Button   activeSavePrevButton;
    [SerializeField] Button   activeSaveNextButton;
    [SerializeField] Button   activeSaveExportButton;
    [SerializeField] Button   activeSaveImportButton;

    // Cycle state. availableSaves[0] is always "" (= "(none)"); the rest are the
    // discovered named saves under VisualsSaveStore.SavesDir.
    string[] availableSaves = new[] { "" };
    int      currentSaveIndex;

    void Awake()
    {
        startButton.onClick.AddListener(OnStartClicked);
        quitButton.onClick.AddListener(OnQuitClicked);

        pathInput.onEndEdit.AddListener(OnPathChanged);
        pathInput.text = PlayerPrefs.GetString(PythonOutputFolderPrefKey, DefaultPythonOutputFolder);
        if (pathBrowseButton != null)
            pathBrowseButton.onClick.AddListener(OnPathBrowseClicked);

        EnsureMediaRootControls();
        if (mediaRootInput != null)
        {
            mediaRootInput.onEndEdit.AddListener(OnMediaRootChanged);
            mediaRootInput.text = PlayerPrefs.GetString(MediaRootFolderPrefKey, "");
        }
        if (mediaRootBrowseButton != null)
            mediaRootBrowseButton.onClick.AddListener(OnMediaRootBrowseClicked);

        if (useBundledTtsOutputToggle != null)
        {
            useBundledTtsOutputToggle.SetIsOnWithoutNotify(
                PlayerPrefs.GetInt(UseBundledTtsOutputPrefKey, 0) == 1);
            useBundledTtsOutputToggle.onValueChanged.AddListener(OnUseBundledTtsOutputChanged);
        }

        if (recordingOutputInput != null)
        {
            recordingOutputInput.onEndEdit.AddListener(OnRecordingOutputChanged);
            recordingOutputInput.text = PlayerPrefs.GetString(RecordingOutputFolderPrefKey, "");
        }
        if (recordingOutputBrowseButton != null)
            recordingOutputBrowseButton.onClick.AddListener(OnRecordingOutputBrowseClicked);

        EnsureActiveSaveControls();
        if (activeSavePrevButton != null) activeSavePrevButton.onClick.AddListener(() => CycleActiveSave(-1));
        if (activeSaveNextButton != null) activeSaveNextButton.onClick.AddListener(() => CycleActiveSave(+1));
        if (activeSaveExportButton != null) activeSaveExportButton.onClick.AddListener(OnExportActiveSaveClicked);
        if (activeSaveImportButton != null) activeSaveImportButton.onClick.AddListener(OnImportSaveClicked);
        RefreshActiveSaves();

        WireGenerateAudioButton();
        WireBackgroundModeRow();
        EnsurePresenterTransitionControls();
        WirePresenterTransitionRow();
        EnsureOutputLibrary();

        RefreshResult();

        // Skin every button to the cohesive minimalist palette. Runs last so it
        // catches the rows built above at runtime, and reaches the (inactive)
        // TTS panel that lives under this canvas.
        UITheme.Apply(gameObject);
    }

    // -----------------------------------------------------------------------
    // Background recording mode (Video / Green Screen / Transparent)
    //
    // The cycle row's three GameObjects (< button, value label, > button)
    // are now AUTHORED in the scene (use Tools > AutoAvatarGen > Add
    // Background Mode Row to create them, then style freely). We just wire
    // the listeners + initial label state here in Awake. Mode lives in
    // PlayerPrefs and is applied at scene load by BackgroundModeManager —
    // picking anything other than "Video" disables the BackgroundPanel,
    // ambient shader, scrolling shapes, and mood controller in the recording
    // scene, freeing GPU for the encoder.
    // -----------------------------------------------------------------------

    [Header("Background Mode Cycle Row")]
    [Tooltip("Built by Tools > AutoAvatarGen > Add Background Mode Row. " +
             "Cycle button — previous mode.")]
    [SerializeField] Button   backgroundModePrevButton;
    [Tooltip("Cycle button — next mode.")]
    [SerializeField] Button   backgroundModeNextButton;
    [Tooltip("Text that shows the current mode label.")]
    [SerializeField] TMP_Text backgroundModeLabel;

    [Header("Presenter Transition Cycle Row")]
    [Tooltip("Built by Tools > AutoAvatarGen > Add Presenter Transition Row, or " +
             "auto-created at runtime if left null. Cycle button — previous style.")]
    [SerializeField] Button   presenterTransitionPrevButton;
    [Tooltip("Cycle button — next style.")]
    [SerializeField] Button   presenterTransitionNextButton;
    [Tooltip("Text that shows the current presenter-transition style label.")]
    [SerializeField] TMP_Text presenterTransitionLabel;

    void WireBackgroundModeRow()
    {
        if (backgroundModePrevButton != null)
            backgroundModePrevButton.onClick.AddListener(() => CycleBackgroundMode(-1));
        if (backgroundModeNextButton != null)
            backgroundModeNextButton.onClick.AddListener(() => CycleBackgroundMode(+1));
        UpdateBackgroundModeLabel();
    }

    void CycleBackgroundMode(int direction)
    {
        var current = MugsTech.Background.BackgroundModeManager.LoadMode();
        var next    = MugsTech.Background.BackgroundModeManager.Cycle(current, direction);
        MugsTech.Background.BackgroundModeManager.SaveMode(next);
        UpdateBackgroundModeLabel();
    }

    void UpdateBackgroundModeLabel()
    {
        if (backgroundModeLabel == null) return;
        var mode = MugsTech.Background.BackgroundModeManager.LoadMode();
        backgroundModeLabel.text = MugsTech.Background.BackgroundModeManager.Label(mode);

        // Tint matches the mode for quick visual recognition: green for
        // GreenScreen, faded blue for Transparent (since "alpha" is harder
        // to color-code), the default slate for Video.
        switch (mode)
        {
            case MugsTech.Background.BackgroundModeManager.Mode.GreenScreen:
                backgroundModeLabel.color = new Color(0.40f, 0.85f, 0.45f); break;
            case MugsTech.Background.BackgroundModeManager.Mode.Transparent:
                backgroundModeLabel.color = new Color(0.55f, 0.65f, 0.85f); break;
            default:
                backgroundModeLabel.color = Color.white; break;
        }
    }

    // -----------------------------------------------------------------------
    // Presenter transition style (Squash & Stretch / Crossfade / Shake)
    //
    // Same cycle-row pattern as the background mode row, persisted via
    // PresenterTransitionSettings. HybridAvatarSystem reads the same pref at
    // scene load and runs the matching emotion-transition animation. The row is
    // normally authored via Tools > AutoAvatarGen > Add Presenter Transition Row;
    // if it wasn't, EnsurePresenterTransitionControls builds a fallback at
    // runtime so the option always shows in the menu.
    // -----------------------------------------------------------------------

    // Display default when no choice is saved yet — matches the recording
    // scene's shipped default (HybridAvatarSystem.useCrossfade = crossfade).
    const PresenterTransitionSettings.Style DefaultTransitionStyle =
        PresenterTransitionSettings.Style.Crossfade;

    void WirePresenterTransitionRow()
    {
        if (presenterTransitionPrevButton != null)
            presenterTransitionPrevButton.onClick.AddListener(() => CyclePresenterTransition(-1));
        if (presenterTransitionNextButton != null)
            presenterTransitionNextButton.onClick.AddListener(() => CyclePresenterTransition(+1));
        UpdatePresenterTransitionLabel();
    }

    void CyclePresenterTransition(int direction)
    {
        var current = PresenterTransitionSettings.LoadStyle(DefaultTransitionStyle);
        var next    = PresenterTransitionSettings.Cycle(current, direction);
        PresenterTransitionSettings.SaveStyle(next);
        UpdatePresenterTransitionLabel();
    }

    void UpdatePresenterTransitionLabel()
    {
        if (presenterTransitionLabel == null) return;
        var style = PresenterTransitionSettings.LoadStyle(DefaultTransitionStyle);
        presenterTransitionLabel.text = PresenterTransitionSettings.Label(style);
    }

    // Builds the presenter-transition cycle row at runtime if it wasn't authored
    // in the scene. Mirrors EnsureMediaRootControls / the background mode row:
    // bottom-center, stacked just above where the Background Mode Row sits.
    void EnsurePresenterTransitionControls()
    {
        if (presenterTransitionLabel != null) return; // already wired in the inspector

        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;

        var row = new GameObject("PresenterTransitionRow", typeof(RectTransform));
        row.transform.SetParent(canvas.transform, false);
        var rowRT = (RectTransform)row.transform;
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 0f);
        rowRT.anchoredPosition = new Vector2(0f, 240f);
        rowRT.sizeDelta        = new Vector2(900f, 100f);

        // Header
        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(row.transform, false);
        var headerRT = (RectTransform)headerGO.transform;
        headerRT.anchorMin = new Vector2(0f, 1f);
        headerRT.anchorMax = new Vector2(1f, 1f);
        headerRT.pivot     = new Vector2(0.5f, 1f);
        headerRT.anchoredPosition = new Vector2(0f, -10f);
        headerRT.sizeDelta        = new Vector2(-20f, 28f);
        var header = headerGO.AddComponent<TextMeshProUGUI>();
        header.text      = "Presenter transition";
        header.fontSize  = 22;
        header.fontStyle = FontStyles.Bold;
        header.alignment = TextAlignmentOptions.Center;
        header.color     = new Color(0.82f, 0.85f, 0.9f, 1f);
        header.raycastTarget = false;

        const float controlsY = -56f;

        presenterTransitionPrevButton = BuildTransitionCycleButton(row.transform, "PresenterTransitionPrev", "<", -220f, controlsY);

        // Value display
        var valueGO = new GameObject("PresenterTransitionValue", typeof(RectTransform));
        valueGO.transform.SetParent(row.transform, false);
        valueGO.AddComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 1f);
        var valueRT = (RectTransform)valueGO.transform;
        valueRT.anchorMin = valueRT.anchorMax = valueRT.pivot = new Vector2(0.5f, 1f);
        valueRT.anchoredPosition = new Vector2(0f, controlsY);
        valueRT.sizeDelta        = new Vector2(320f, 44f);

        var labelGO = new GameObject("Text", typeof(RectTransform));
        labelGO.transform.SetParent(valueGO.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.fontSize  = 24;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color     = Color.white;
        label.raycastTarget = false;
        presenterTransitionLabel = label;

        presenterTransitionNextButton = BuildTransitionCycleButton(row.transform, "PresenterTransitionNext", ">", 220f, controlsY);
    }

    static Button BuildTransitionCycleButton(Transform parent, string name, string glyph, float x, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(60f, 44f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.65f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;
        var t = labelGO.AddComponent<TextMeshProUGUI>();
        t.text      = glyph;
        t.fontSize  = 30;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color     = Color.white;
        t.raycastTarget = false;
        return btn;
    }

    // -----------------------------------------------------------------------
    // Generate Audio button — opens the (now scene-baked) TtsPanelController.
    // The button itself is AUTHORED in the scene (use Tools > AutoAvatarGen >
    // Add Generate Audio Button to create it, then style freely). The TTS
    // panel is found at runtime via FindObjectOfType so it works whether the
    // panel is on the same canvas or on a sibling overlay canvas.
    // -----------------------------------------------------------------------

    [Header("Generate Audio")]
    [Tooltip("Built by Tools > AutoAvatarGen > Add Generate Audio Button. " +
             "Click handler opens the TtsPanelController in the scene.")]
    [SerializeField] Button generateAudioButton;
    [Tooltip("Optional explicit reference. If null, found via FindObjectOfType when needed.")]
    [SerializeField] MugsTech.Tts.TtsPanelController ttsPanel;

    void WireGenerateAudioButton()
    {
        if (generateAudioButton != null)
            generateAudioButton.onClick.AddListener(OnGenerateAudioClicked);
    }

    void OnGenerateAudioClicked()
    {
        if (ttsPanel == null)
        {
            // The scene-baked panel starts inactive — FindObjectOfType skips
            // inactive objects, so use the array overload with includeInactive.
            var found = FindObjectsOfType<MugsTech.Tts.TtsPanelController>(includeInactive: true);
            if (found != null && found.Length > 0) ttsPanel = found[0];
        }
        if (ttsPanel == null)
        {
            Debug.LogWarning("[MainMenu] No TtsPanelController found in scene. " +
                             "Run Tools > AutoAvatarGen > Add TTS Panel to create one.");
            return;
        }
        // Refresh the generation dropdown when the panel closes so a freshly
        // generated output appears and is selected.
        ttsPanel.Show(() => { if (outputLibrary != null) outputLibrary.RefreshOutputs(); });
    }

    // -----------------------------------------------------------------------
    // Output library (generation chooser)
    //
    // The dropdown listing every TTS generation — and picking which one Start
    // Recording renders — is normally baked into MainMenu.unity automatically by
    // the editor hook OutputLibraryAutoBaker (permanent, Scene-view-styleable
    // objects + a wired OutputLibraryController). This just grabs that baked
    // component for the post-generation refresh. As a safety net for scenes the
    // baker never touched, we add one at runtime; OutputLibraryController then
    // generates its own fallback row.
    // -----------------------------------------------------------------------
    void EnsureOutputLibrary()
    {
        outputLibrary = FindObjectOfType<OutputLibraryController>();
        if (outputLibrary == null)
            outputLibrary = gameObject.AddComponent<OutputLibraryController>();
    }

    void OnEnable()
    {
        RecordingSession.ResultChanged += RefreshResult;
        // The user may have added/removed a save inside the visuals menu since
        // the menu opened — re-scan so the cycle reflects the current set.
        RefreshActiveSaves();
    }

    void OnDisable()
    {
        RecordingSession.ResultChanged -= RefreshResult;
    }

    void RefreshResult()
    {
        var r = RecordingSession.LastResult;
        if (r == null)
        {
            statusText.text  = "Ready to record.";
            statusText.color = new Color(0.82f, 0.85f, 0.90f, 1f);
            pathText.text    = "No recording has been completed yet in this session.";
            return;
        }

        switch (r.State)
        {
            case RecordingSession.RecordingResult.Status.Generating:
                statusText.text  = "●  Recording complete";
                statusText.color = new Color(0.98f, 0.80f, 0.30f, 1f);
                pathText.text    = "Generating video… Evereal is finalising the file, " +
                                   "this usually takes a few seconds.";
                break;

            case RecordingSession.RecordingResult.Status.Saved:
                statusText.text  = "✓  Video saved";
                statusText.color = new Color(0.35f, 0.85f, 0.45f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.SavePath) ? "(no path returned)" : r.SavePath;
                break;

            case RecordingSession.RecordingResult.Status.Failed:
                statusText.text  = "✗  Recording failed";
                statusText.color = new Color(0.95f, 0.35f, 0.35f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.ErrorMessage) ? "(no error details)" : r.ErrorMessage;
                break;
        }
    }

    void OnStartClicked()
    {
        // Flush field values in case the user typed but didn't click out before hitting Start.
        OnPathChanged(pathInput.text);
        if (mediaRootInput != null) OnMediaRootChanged(mediaRootInput.text);
        if (recordingOutputInput != null) OnRecordingOutputChanged(recordingOutputInput.text);
        RecordingSession.Begin();
    }

    void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnPathChanged(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? DefaultPythonOutputFolder : value.Trim();
        PlayerPrefs.SetString(PythonOutputFolderPrefKey, trimmed);
        PlayerPrefs.Save();
        if (pathInput.text != trimmed) pathInput.text = trimmed;
    }

    void OnPathBrowseClicked()
    {
        // Pre-seed the picker with the current selection so the dialog opens
        // somewhere useful. Resolve relative paths against Assets/ to match
        // ScriptFileReader.ResolveOutputFolder.
        string current = pathInput != null ? pathInput.text : "";
        string startDir;
        if (string.IsNullOrWhiteSpace(current))
            startDir = Application.dataPath;
        else if (Path.IsPathRooted(current))
            startDir = current;
        else
            startDir = Path.Combine(Application.dataPath, current);

        string picked = TryPickFolderPath("Pick ElevenLabs output folder", startDir);
        if (string.IsNullOrEmpty(picked)) return;
        OnPathChanged(picked);
    }

    static string TryPickFolderPath(string title, string startDir)
    {
#if STANDALONE_FILE_BROWSER
        string[] picked = SFB.StandaloneFileBrowser.OpenFolderPanel(title, startDir, false);
        return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFolderPanel(title, startDir, "");
#else
        return "";
#endif
    }

    // -----------------------------------------------------------------------
    // External media root (BRoll / Images / Logos)
    //
    // Mirrors the Python output folder pattern: typing a path or picking one
    // via Browse… auto-saves to PlayerPrefs. MediaPresentationSystem reads the
    // same key in Start() and overrides its inspector value, so the link
    // persists across sessions and scenes.
    // -----------------------------------------------------------------------

    void OnMediaRootChanged(string value)
    {
        // Empty / whitespace = "no override" (system falls back to Resources/Media).
        string trimmed = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        PlayerPrefs.SetString(MediaRootFolderPrefKey, trimmed);
        PlayerPrefs.Save();
        if (mediaRootInput != null && mediaRootInput.text != trimmed)
            mediaRootInput.text = trimmed;
    }

    void OnUseBundledTtsOutputChanged(bool isOn)
    {
        PlayerPrefs.SetInt(UseBundledTtsOutputPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();
    }

    // -----------------------------------------------------------------------
    // Recording output folder
    //
    // Mirrors the Python output / media root pattern: typing a path or picking
    // one via Browse auto-saves to PlayerPrefs. CrossPlatformRecorder reads the
    // same key in Awake() and overrides its inspector `saveFolder`, so the
    // recorded videos land where the user picked across sessions and scenes.
    // -----------------------------------------------------------------------

    void OnRecordingOutputChanged(string value)
    {
        // Empty / whitespace = "no override" (recorder falls back to its
        // inspector saveFolder, which itself falls back to Evereal's default).
        string trimmed = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        PlayerPrefs.SetString(RecordingOutputFolderPrefKey, trimmed);
        PlayerPrefs.Save();
        Debug.Log($"[MainMenu] Recording output folder pref saved: " +
                  $"key='{RecordingOutputFolderPrefKey}' value='{trimmed}'");
        if (recordingOutputInput != null && recordingOutputInput.text != trimmed)
            recordingOutputInput.text = trimmed;
    }

    void OnRecordingOutputBrowseClicked()
    {
        string current = recordingOutputInput != null ? recordingOutputInput.text : "";
        string startDir = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
            ? current
            : "";
        string picked = TryPickFolderPath("Pick recording output folder", startDir);
        if (string.IsNullOrEmpty(picked)) return;
        OnRecordingOutputChanged(picked);
    }

    void OnMediaRootBrowseClicked()
    {
        string current = mediaRootInput != null ? mediaRootInput.text : "";
        string startDir = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
            ? current
            : "";
        string picked = TryPickFolderPath(
            "Pick media folder (must contain BRoll / Images / Logos)", startDir);
        if (string.IsNullOrEmpty(picked)) return;
        OnMediaRootChanged(picked);
    }

    // Builds the media-root row at runtime if it hasn't been hand-wired in the
    // inspector. Same trick as EnsureActiveSaveControls: existing scenes don't
    // need a UI rebuild to get the new controls.
    void EnsureMediaRootControls()
    {
        if (mediaRootInput != null) return; // already wired in the inspector

        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;

        // Row container, anchored top-center.
        var row = new GameObject("MediaRootRow", typeof(RectTransform));
        row.transform.SetParent(canvas.transform, false);
        var rowRT = (RectTransform)row.transform;
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -56f);
        rowRT.sizeDelta        = new Vector2(1500f, 80f);

        // Label
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = labelRT.anchorMax = labelRT.pivot = new Vector2(0.5f, 1f);
        labelRT.anchoredPosition = new Vector2(0f, 0f);
        labelRT.sizeDelta        = new Vector2(1100f, 28f);
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text       = "Media folder (contains BRoll / Images / Logos):";
        label.fontSize   = 22;
        label.alignment  = TextAlignmentOptions.Center;
        label.color      = new Color(0.82f, 0.85f, 0.9f, 1f);

        // Input field
        var inputGO = new GameObject("MediaRootInput", typeof(RectTransform));
        inputGO.transform.SetParent(row.transform, false);
        var inputRT = (RectTransform)inputGO.transform;
        inputRT.anchorMin = inputRT.anchorMax = inputRT.pivot = new Vector2(0.5f, 1f);
        inputRT.anchoredPosition = new Vector2(-90f, -34f);
        inputRT.sizeDelta        = new Vector2(920f, 44f);

        var bg = inputGO.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.17f, 0.21f, 1f);

        var input = inputGO.AddComponent<TMP_InputField>();

        var textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(inputGO.transform, false);
        var taRT = (RectTransform)textArea.transform;
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(14f, 4f);
        taRT.offsetMax = new Vector2(-14f, -4f);
        textArea.AddComponent<RectMask2D>();

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(textArea.transform, false);
        var textRT = (RectTransform)textGO.transform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text      = "";
        text.fontSize  = 22;
        text.alignment = TextAlignmentOptions.Left;
        text.color     = Color.white;
        text.richText  = false;

        var phGO = new GameObject("Placeholder", typeof(RectTransform));
        phGO.transform.SetParent(textArea.transform, false);
        var phRT = (RectTransform)phGO.transform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        ph.text      = "C:\\path\\to\\media-root  (leave blank for Resources/Media)";
        ph.fontSize  = 22;
        ph.fontStyle = FontStyles.Italic;
        ph.alignment = TextAlignmentOptions.Left;
        ph.color     = new Color(0.55f, 0.58f, 0.64f, 1f);

        input.textViewport  = taRT;
        input.textComponent = text;
        input.placeholder   = ph;
        input.targetGraphic = bg;

        mediaRootInput = input;

        // Browse button
        var btnGO = new GameObject("MediaRootBrowseButton", typeof(RectTransform));
        btnGO.transform.SetParent(row.transform, false);
        var btnRT = (RectTransform)btnGO.transform;
        btnRT.anchorMin = btnRT.anchorMax = btnRT.pivot = new Vector2(0.5f, 1f);
        btnRT.anchoredPosition = new Vector2(460f, -34f);
        btnRT.sizeDelta        = new Vector2(160f, 44f);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.20f, 0.45f, 0.65f, 1f);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        var btnLabelGO = new GameObject("Label", typeof(RectTransform));
        btnLabelGO.transform.SetParent(btnGO.transform, false);
        var btnLabelRT = (RectTransform)btnLabelGO.transform;
        btnLabelRT.anchorMin = Vector2.zero;
        btnLabelRT.anchorMax = Vector2.one;
        btnLabelRT.offsetMin = Vector2.zero;
        btnLabelRT.offsetMax = Vector2.zero;
        var btnLabel = btnLabelGO.AddComponent<TextMeshProUGUI>();
        btnLabel.text       = "Browse…";
        btnLabel.fontSize   = 24;
        btnLabel.fontStyle  = FontStyles.Bold;
        btnLabel.alignment  = TextAlignmentOptions.Center;
        btnLabel.color      = Color.white;

        mediaRootBrowseButton = btn;
    }

    // -----------------------------------------------------------------------
    // Active visuals save selector
    // -----------------------------------------------------------------------

    void RefreshActiveSaves()
    {
        if (activeSaveLabel == null) return;

        var list = new List<string> { "" }; // index 0 = "(none)"
        list.AddRange(VisualsSaveStore.ListSaveNames());
        availableSaves = list.ToArray();

        string current = PlayerPrefs.GetString(VisualsMenuController.ActiveSaveNameKey, "");
        currentSaveIndex = Array.IndexOf(availableSaves, current);
        if (currentSaveIndex < 0)
        {
            // The previously-active save was deleted (or its file went missing) —
            // fall back to "(none)" and clear the pref so a stale name doesn't
            // keep getting applied to recordings.
            currentSaveIndex = 0;
            PlayerPrefs.DeleteKey(VisualsMenuController.ActiveSaveNameKey);
            PlayerPrefs.Save();
        }
        UpdateActiveSaveLabel();
    }

    // -----------------------------------------------------------------------
    // Active save export / import — lets users back up the currently selected
    // visuals save to a JSON file before installing a new build, then restore
    // it after the new install. Same JSON format as VisualsMenuController's
    // export, so files are interchangeable between the two screens.
    // -----------------------------------------------------------------------

    void OnExportActiveSaveClicked()
    {
        string current = (availableSaves != null && currentSaveIndex < availableSaves.Length)
            ? availableSaves[currentSaveIndex] : "";
        if (string.IsNullOrEmpty(current))
        {
            FlashMessage("No save selected to export. Pick one with the < > buttons.");
            return;
        }

        var data = VisualsSaveStore.Load(current);
        if (data == null)
        {
            FlashMessage($"Could not load save '{current}'.");
            return;
        }

        string path = TryPickSaveJsonPath(current + ".json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            VisualsSaveStore.ExportTo(data, path);
            FlashMessage("Exported to: " + path);
            Debug.Log($"[MainMenu] Exported visuals save '{current}' to {path}");
        }
        catch (Exception e)
        {
            FlashMessage("Export failed: " + e.Message);
            Debug.LogError($"[MainMenu] Export failed: {e}");
        }
    }

    void OnImportSaveClicked()
    {
        string path = TryPickOpenJsonPath();
        if (string.IsNullOrEmpty(path)) return;

        VisualsSaveFile data;
        try { data = VisualsSaveStore.LoadFromFile(path); }
        catch (Exception e) { FlashMessage("Import failed: " + e.Message); return; }

        if (data == null)
        {
            FlashMessage("Not a valid visuals save file.");
            return;
        }

        if (string.IsNullOrEmpty(data.name))
            data.name = Path.GetFileNameWithoutExtension(path);

        try
        {
            VisualsSaveStore.Save(data); // overwrites if same name already exists
            // Make the imported save the active one so the next recording picks it up.
            PlayerPrefs.SetString(VisualsMenuController.ActiveSaveNameKey, data.name);
            PlayerPrefs.Save();
            RefreshActiveSaves();
            FlashMessage("Imported save: " + data.name);
            Debug.Log($"[MainMenu] Imported visuals save '{data.name}' from {path}");
        }
        catch (Exception e)
        {
            FlashMessage("Import failed: " + e.Message);
            Debug.LogError($"[MainMenu] Import failed: {e}");
        }
    }

    // Pushes a short message into the result panel so users see export / import
    // feedback. Gets overwritten by the next RefreshResult() call (e.g. after a
    // recording completes), which is fine.
    void FlashMessage(string text)
    {
        if (pathText != null) pathText.text = text;
    }

    static string TryPickSaveJsonPath(string defaultFileName)
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

    static string TryPickOpenJsonPath()
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

    void CycleActiveSave(int delta)
    {
        if (availableSaves == null || availableSaves.Length == 0) return;
        int len = availableSaves.Length;
        currentSaveIndex = ((currentSaveIndex + delta) % len + len) % len;

        string chosen = availableSaves[currentSaveIndex];
        if (string.IsNullOrEmpty(chosen))
            PlayerPrefs.DeleteKey(VisualsMenuController.ActiveSaveNameKey);
        else
            PlayerPrefs.SetString(VisualsMenuController.ActiveSaveNameKey, chosen);
        PlayerPrefs.Save();

        UpdateActiveSaveLabel();
    }

    void UpdateActiveSaveLabel()
    {
        if (activeSaveLabel == null) return;
        string current = (availableSaves != null && currentSaveIndex < availableSaves.Length)
            ? availableSaves[currentSaveIndex] : "";
        bool none = string.IsNullOrEmpty(current);
        activeSaveLabel.text = "Active save:  " + (none ? "(none)" : current);
        if (activeSavePrevButton != null) activeSavePrevButton.interactable = availableSaves.Length > 1;
        if (activeSaveNextButton != null) activeSaveNextButton.interactable = availableSaves.Length > 1;
        // Export only makes sense when a real save is selected (not "(none)").
        if (activeSaveExportButton != null) activeSaveExportButton.interactable = !none;
    }

    // The active-save row is created at runtime (rather than in MainMenuUIBuilder)
    // so it appears without forcing a canvas rebuild — which would wipe any
    // hand-added scene objects like the PresetsButton.
    void EnsureActiveSaveControls()
    {
        if (activeSaveLabel != null) return; // already wired in the inspector

        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;

        var row = new GameObject("ActiveSaveRow", typeof(RectTransform));
        row.transform.SetParent(canvas.transform, false);
        var rowRT = (RectTransform)row.transform;
        rowRT.anchorMin = new Vector2(0.5f, 1f);
        rowRT.anchorMax = new Vector2(0.5f, 1f);
        rowRT.pivot     = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -50f);
        rowRT.sizeDelta = new Vector2(1280f, 50f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = labelRT.anchorMax = labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta = new Vector2(440f, 50f);
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text       = "Active save:  (none)";
        label.fontSize   = 26;
        label.alignment  = TextAlignmentOptions.Center;
        label.color      = new Color(0.82f, 0.85f, 0.9f, 1f);
        activeSaveLabel  = label;

        activeSavePrevButton = BuildCycleButton(rowRT, "Prev", "<", -280f);
        activeSaveNextButton = BuildCycleButton(rowRT, "Next", ">",  280f);

        // Export saves the currently-active visuals save to a JSON file the user
        // picks — so they can keep their tweaks across new builds. Import re-loads
        // a previously exported JSON back into the local save store.
        activeSaveExportButton = BuildWideTextButton(rowRT, "ExportSave", "Export…",  470f, new Color(0.20f, 0.45f, 0.65f, 1f));
        activeSaveImportButton = BuildWideTextButton(rowRT, "ImportSave", "Import…", -470f, new Color(0.32f, 0.34f, 0.40f, 1f));
    }

    static Button BuildWideTextButton(RectTransform parent, string name, string label, float xOffset, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0f);
        rt.sizeDelta = new Vector2(160f, 44f);

        var img = go.AddComponent<Image>();
        img.color = tint;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var t = labelGO.AddComponent<TextMeshProUGUI>();
        t.text       = label;
        t.fontSize   = 22;
        t.fontStyle  = FontStyles.Bold;
        t.alignment  = TextAlignmentOptions.Center;
        t.color      = Color.white;
        return btn;
    }

    static Button BuildCycleButton(RectTransform parent, string name, string glyph, float xOffset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0f);
        rt.sizeDelta = new Vector2(56f, 44f);

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
        var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
        labelTmp.text       = glyph;
        labelTmp.fontSize   = 30;
        labelTmp.fontStyle  = FontStyles.Bold;
        labelTmp.alignment  = TextAlignmentOptions.Center;
        labelTmp.color      = Color.white;
        return btn;
    }

}

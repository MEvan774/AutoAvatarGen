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
///   - A text field + Load button for a runtime background-video override.
///     BackgroundVideoLoop reads the same PlayerPrefs key and prefers it over
///     the Inspector default at scene start.
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

    // Shared with BackgroundVideoLoop. Empty string = use the scene's default.
    public const string BackgroundVideoOverridePrefKey = BackgroundVideoLoop.OverridePathPrefKey;

    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text pathText;
    [SerializeField] TMP_InputField pathInput;
    [SerializeField] Button startButton;
    [SerializeField] Button quitButton;

    [Header("Background Video Override")]
    [SerializeField] TMP_InputField videoPathInput;
    [SerializeField] Button         videoLoadButton;
    [SerializeField] Button         videoClearButton;

    [Header("Active Visuals Save")]
    [Tooltip("Optional. If left null, the controller spawns its own row at runtime.")]
    [SerializeField] TMP_Text activeSaveLabel;
    [SerializeField] Button   activeSavePrevButton;
    [SerializeField] Button   activeSaveNextButton;

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

        videoPathInput.onEndEdit.AddListener(OnVideoPathChanged);
        videoPathInput.text = PlayerPrefs.GetString(BackgroundVideoOverridePrefKey, "");
        videoLoadButton.onClick.AddListener(OnVideoLoadClicked);
        videoClearButton.onClick.AddListener(OnVideoClearClicked);

        EnsureActiveSaveControls();
        if (activeSavePrevButton != null) activeSavePrevButton.onClick.AddListener(() => CycleActiveSave(-1));
        if (activeSaveNextButton != null) activeSaveNextButton.onClick.AddListener(() => CycleActiveSave(+1));
        RefreshActiveSaves();

        RefreshResult();
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
        OnVideoPathChanged(videoPathInput.text);
        Debug.Log($"[BgVideoDiag] MainMenu OnStartClicked — videoPathInput.text='{videoPathInput.text}' " +
                  $"OverridePref='{PlayerPrefs.GetString(BackgroundVideoOverridePrefKey, "")}'");
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

    // -----------------------------------------------------------------------
    // Background video override
    // -----------------------------------------------------------------------

    void OnVideoPathChanged(string value)
    {
        // Empty / whitespace = "use the scene's default". Don't normalise to a
        // placeholder string here; an empty PrefValue is the documented signal.
        string trimmed = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        PlayerPrefs.SetString(BackgroundVideoOverridePrefKey, trimmed);
        PlayerPrefs.Save();
        Debug.Log($"[BgVideoDiag] MainMenu OnVideoPathChanged wrote OverridePathPrefKey='{trimmed}'");
        if (videoPathInput.text != trimmed) videoPathInput.text = trimmed;
    }

    void OnVideoLoadClicked()
    {
        string current = videoPathInput.text;
        string startDir = !string.IsNullOrEmpty(current) ? Path.GetDirectoryName(current) : "";

#if STANDALONE_FILE_BROWSER
        var ext = new[]
        {
            new SFB.ExtensionFilter("Video Files", "mp4", "mov", "webm", "m4v"),
            new SFB.ExtensionFilter("All Files",   "*"),
        };
        string[] picked = SFB.StandaloneFileBrowser.OpenFilePanel(
            "Pick background video", startDir, ext, false);
        if (picked != null && picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
            OnVideoPathChanged(picked[0]);
#elif UNITY_EDITOR
        string p = UnityEditor.EditorUtility.OpenFilePanel(
            "Pick background video", startDir, "mp4,mov,webm,m4v");
        if (!string.IsNullOrEmpty(p)) OnVideoPathChanged(p);
#else
        // No picker available — user must paste a path into the field.
#endif
    }

    void OnVideoClearClicked()
    {
        OnVideoPathChanged("");
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
        rowRT.sizeDelta = new Vector2(800f, 50f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelRT = (RectTransform)labelGO.transform;
        labelRT.anchorMin = labelRT.anchorMax = labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta = new Vector2(560f, 50f);
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text       = "Active save:  (none)";
        label.fontSize   = 26;
        label.alignment  = TextAlignmentOptions.Center;
        label.color      = new Color(0.82f, 0.85f, 0.9f, 1f);
        activeSaveLabel  = label;

        activeSavePrevButton = BuildCycleButton(rowRT, "Prev", "<", -340f);
        activeSaveNextButton = BuildCycleButton(rowRT, "Next", ">",  340f);
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

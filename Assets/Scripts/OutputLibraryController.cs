using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Tts;

/// <summary>
/// Lists every TTS generation — each a timestamped subfolder under the output
/// library root — in a dropdown and lets the user pick which one the recording
/// scene will render. The choice is saved to PlayerPrefs; ScriptFileReader
/// reads the same key in the recording scene and loads that exact folder.
///
/// The dropdown UI is baked into MainMenu.unity automatically by the editor
/// hook OutputLibraryAutoBaker (permanent objects you can restyle in the Scene
/// view) and its references wired in the Inspector — no Tools menu, no clicking.
/// As a safety net for scenes the baker never touched, <see cref="EnsureUI"/>
/// generates an equivalent row in code at runtime when the references are empty.
///
/// The library root is the same folder the TTS panel / main menu already point
/// at (PlayerPref <c>AutoAvatarGen.PythonOutputFolder</c>); generations are the
/// subfolders TtsGenerationJob creates inside it.
/// </summary>
public class OutputLibraryController : MonoBehaviour
{
    // Which generation the recording scene should render. Keep in sync with
    // TtsPanelController + ScriptFileReader.
    public const string SelectedGenerationPrefKey = "AutoAvatarGen.SelectedGeneration";

    // Library root — the same key the TTS panel and main menu use for the
    // output folder. Keep in sync with MainMenuController.PythonOutputFolderPrefKey.
    public const string OutputLibraryRootPrefKey  = "AutoAvatarGen.PythonOutputFolder";
    const string DefaultOutputLibraryRoot         = "Python/output";

    [Header("UI References (baked into the scene by OutputLibraryAutoBaker)")]
    [Tooltip("Dropdown listing every generation. Normally wired by the editor " +
             "baker; if left empty at runtime, a fallback row is generated in code.")]
    [SerializeField] TMP_Dropdown generationDropdown;
    [Tooltip("Optional. Rescans the library folder when clicked.")]
    [SerializeField] Button       refreshButton;
    [Tooltip("Optional. Shows the selected generation's full path.")]
    [SerializeField] TMP_Text     selectedPathLabel;

    // Absolute paths to each generation folder, newest first (matches dropdown order).
    readonly List<string> generations = new List<string>();

    /// <summary>Resolved absolute path of the library root that holds the generations.</summary>
    public string LibraryRoot =>
        TtsGenerationJob.ResolveOutputFolder(
            PlayerPrefs.GetString(OutputLibraryRootPrefKey, DefaultOutputLibraryRoot));

    /// <summary>Absolute path of the currently selected generation, or null if none.</summary>
    public string SelectedGeneration { get; private set; }

    void Start()
    {
        EnsureUI();   // generate the dropdown row if it wasn't wired in the Inspector

        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshOutputs);
        if (generationDropdown != null)
            generationDropdown.onValueChanged.AddListener(OnDropdownChanged);

        RefreshOutputs();
    }

    /// <summary>Rescan the library root and repopulate the dropdown (newest first).</summary>
    public void RefreshOutputs()
    {
        // Preserve the persisted choice across the refresh if it still exists.
        string previous = PlayerPrefs.GetString(SelectedGenerationPrefKey, "");

        generations.Clear();
        string root = LibraryRoot;
        if (Directory.Exists(root))
        {
            // Folder names are sortable timestamps, so an ordinal name sort is
            // chronological — descending gives newest first.
            generations.AddRange(
                Directory.GetDirectories(root)
                         .Where(HasOutput)
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal));
        }

        if (generationDropdown != null)
        {
            generationDropdown.ClearOptions();
            generationDropdown.AddOptions(generations.Select(LabelFor).ToList());
        }

        if (generations.Count == 0)
        {
            SelectedGeneration = null;
            if (selectedPathLabel != null) selectedPathLabel.text = "No generations yet.";
            return;
        }

        int index = generations.IndexOf(previous);
        if (index < 0) index = 0;            // prior choice gone (or none) -> newest
        SetSelected(index, persist: true);
    }

    void OnDropdownChanged(int index) => SetSelected(index, persist: true, syncDropdown: false);

    void SetSelected(int index, bool persist, bool syncDropdown = true)
    {
        if (index < 0 || index >= generations.Count) return;

        SelectedGeneration = generations[index];

        if (persist)
        {
            PlayerPrefs.SetString(SelectedGenerationPrefKey, SelectedGeneration);
            PlayerPrefs.Save();
        }
        if (syncDropdown && generationDropdown != null)
            generationDropdown.SetValueWithoutNotify(index);
        if (selectedPathLabel != null)
            selectedPathLabel.text = SelectedGeneration;
    }

    // A folder counts as a generation if it has a manifest or at least one
    // "<SLUG>_timed.txt" — the same things ScriptFileReader knows how to load.
    static bool HasOutput(string dir)
        => File.Exists(Path.Combine(dir, "manifest.json")) ||
           Directory.GetFiles(dir, "*_timed.txt").Length > 0;

    // Dropdown label: folder name, plus the segment count when it's a multi-part
    // generation, so a stitched render is easy to spot.
    static string LabelFor(string dir)
    {
        string name  = Path.GetFileName(dir);
        int    pairs = Directory.GetFiles(dir, "*_timed.txt").Length;
        return pairs > 1 ? $"{name}  ({pairs} segments)" : name;
    }

    // -----------------------------------------------------------------------
    // Runtime UI generation
    //
    // Builds a row (header + dropdown + refresh button + selected-path label)
    // under the first Canvas found, and assigns the references. Only runs when
    // the dropdown wasn't already wired in the Inspector. Mirrors the runtime
    // row builders in MainMenuController so no scene setup is required.
    // -----------------------------------------------------------------------
    void EnsureUI()
    {
        if (generationDropdown != null) return; // already wired — use as-is

        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[OutputLibrary] No Canvas found — cannot generate the " +
                             "generation dropdown. Add this component to a GameObject that " +
                             "has (or is under) a Canvas, or wire the dropdown in the Inspector.");
            return;
        }

        // Row container — anchored top-center, below the existing top rows.
        var row = new GameObject("OutputLibraryRow", typeof(RectTransform));
        row.transform.SetParent(canvas.transform, false);
        var rowRT = (RectTransform)row.transform;
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -104f);
        rowRT.sizeDelta        = new Vector2(1500f, 80f);

        // Header
        BuildLabel(row.transform, "Header",
            "Generation for the video  —  used by Start Recording",
            size: new Vector2(1460f, 28f), anchoredPos: new Vector2(0f, 24f),
            fontSize: 20, italic: false, alignment: TextAlignmentOptions.Center);

        // Dropdown (built via the TMP factory so it gets a working template).
        var ddGO = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        ddGO.name = "GenerationDropdown";
        ddGO.transform.SetParent(row.transform, false);
        var ddRT = (RectTransform)ddGO.transform;
        ddRT.anchorMin = ddRT.anchorMax = ddRT.pivot = new Vector2(0.5f, 0.5f);
        ddRT.anchoredPosition = new Vector2(-430f, -16f);
        ddRT.sizeDelta        = new Vector2(640f, 44f);
        var ddImg = ddGO.GetComponent<Image>();
        if (ddImg != null) ddImg.color = new Color(0.15f, 0.17f, 0.21f, 1f);
        generationDropdown = ddGO.GetComponent<TMP_Dropdown>();
        if (generationDropdown != null && generationDropdown.captionText != null)
            generationDropdown.captionText.color = Color.white;  // readable on the dark box

        // Refresh button
        refreshButton = BuildButton(row.transform, "RefreshButton", "Refresh",
            size: new Vector2(150f, 44f), anchoredPos: new Vector2(20f, -16f));

        // Selected-path label
        selectedPathLabel = BuildLabel(row.transform, "SelectedPath", "",
            size: new Vector2(520f, 44f), anchoredPos: new Vector2(480f, -16f),
            fontSize: 15, italic: true, alignment: TextAlignmentOptions.MidlineLeft);
    }

    static TMP_Text BuildLabel(Transform parent, string name, string text,
        Vector2 size, Vector2 anchoredPos, int fontSize, bool italic,
        TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = TMP_Settings.defaultFontAsset;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
        tmp.text      = text;
        tmp.alignment = alignment;
        tmp.color     = new Color(0.82f, 0.85f, 0.9f, 1f);
        tmp.raycastTarget = false;
        return tmp;
    }

    static Button BuildButton(Transform parent, string name, string label,
        Vector2 size, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.65f, 1f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;

        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.font      = TMP_Settings.defaultFontAsset;
        tmp.fontSize  = 22;
        tmp.fontStyle = FontStyles.Bold;
        tmp.text      = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        tmp.raycastTarget = false;
        return btn;
    }
}

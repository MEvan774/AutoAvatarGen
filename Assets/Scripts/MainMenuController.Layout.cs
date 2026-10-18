using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// Main menu layout. At startup every menu control — scene-authored or built at
/// runtime — is re-parented into one workflow layout:
///
///   AutoAvatarGen                       [YouTube Chapters] [Settings] [Quit]
///   ┌ 1 Voice audio ─────────┐  ┌ 3 Look ─────────────────────────────┐
///   │ [Generate Audio…]      │  │ Background            <  value  >   │
///   └────────────────────────┘  │ Background mode       <  value  >   │
///   ┌ 2 Generation ──────────┐  │ Presenter transition  <  value  >   │
///   │ [dropdown     ][Refresh]│  │ Card animation        <  value  >   │
///   │ selected path          │  │ Visuals preset        <  value  >   │
///   └────────────────────────┘  │                       [Edit visuals…]│
///                               └─────────────────────────────────────┘
///   [ Start Recording ]  status / saved path                [Open video folder]
///
/// The four folder paths and visuals-preset Import / Export live in a Settings
/// window. MainMenu.unity is hand-tweaked and is never re-baked: the controls
/// keep their components, listeners and inspector references and only move
/// into layout groups. The Scene view therefore still shows the old
/// positions; this layout is what Play mode and the build show.
/// </summary>
public partial class MainMenuController
{
    const float LookLabelWidth   = 220f;
    const float FolderLabelWidth = 170f;
    const float WideButtonWidth  = 260f;

    GameObject settingsPanel;
    TMP_Text   settingsStatus;

    Transform canvasRoot;
    // Containers the scene's controls were taken out of; hidden once emptied.
    readonly HashSet<Transform> vacated = new HashSet<Transform>();

    void ArrangeLayout()
    {
        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;
        canvasRoot = canvas.transform;

        // Old free-floating captions — the row labels say this now.
        foreach (string caption in new[] { "Subtitle", "PathLabel", "MediaRootLabel" })
        {
            Transform t = canvasRoot.Find(caption);
            if (t != null) t.gameObject.SetActive(false);
        }

        var root = UITheme.Fill(UITheme.MakeColumn(canvasRoot, "MenuLayout", UITheme.SectionGap));
        root.offsetMin = new Vector2(UITheme.ScreenMarginX, UITheme.ScreenMarginY);
        root.offsetMax = new Vector2(-UITheme.ScreenMarginX, -UITheme.ScreenMarginY);
        // Just above the background, so the overlays already on this canvas
        // (TTS panel, API-key popup, chapters window) keep drawing on top.
        Transform background = canvasRoot.Find("Background");
        root.SetSiblingIndex(background != null ? background.GetSiblingIndex() + 1 : 0);

        BuildTopBar(root);

        var body = UITheme.MakeRow(root, "Body", height: -1f, spacing: UITheme.ColumnGap);
        UITheme.Size(body, flexHeight: 1f);
        var bodyRow = body.GetComponent<HorizontalLayoutGroup>();
        bodyRow.childAlignment         = TextAnchor.UpperLeft;
        bodyRow.childForceExpandHeight = true;

        // Two equal columns: zero preferred width, equal flexible share.
        var left  = UITheme.MakeColumn(body, "LeftColumn",  UITheme.SectionGap);
        var right = UITheme.MakeColumn(body, "RightColumn", UITheme.SectionGap);
        UITheme.Size(left,  width: 0f, flexWidth: 1f);
        UITheme.Size(right, width: 0f, flexWidth: 1f);

        BuildVoiceCard(left);
        BuildGenerationCard(left);
        BuildLookCard(right);
        BuildRecordBar(root);
        BuildSettingsPanel();

        // Hide the old row containers (captions, value boxes, the result
        // panel) — but only once no control is left in them.
        foreach (Transform t in vacated)
            if (t != null && t != canvasRoot && !t.IsChildOf(root) && !t.IsChildOf(settingsPanel.transform)
                && t.GetComponentInChildren<Selectable>(true) == null)
                t.gameObject.SetActive(false);

        UITheme.ApplyControls(root.gameObject);
        UITheme.ApplyControls(settingsPanel);

        // The styling above reset label colours these two set semantically.
        UpdateBackgroundModeLabel();
        UpdateActiveSaveLabel();
    }

    // -----------------------------------------------------------------------
    // Main screen
    // -----------------------------------------------------------------------

    void BuildTopBar(Transform root)
    {
        var bar = UITheme.MakeRow(root, "TopBar", height: 64f);

        TMP_Text title = FindOnCanvas<TMP_Text>("Title")
                         ?? UITheme.MakeText(bar, "Title", "AutoAvatarGen", UITheme.TextRole.Title);
        Take(title, bar);
        UITheme.Style(title, UITheme.TextRole.Title);
        title.fontSize           = UITheme.ScreenTitleSize;
        title.alignment          = TextAlignmentOptions.MidlineLeft;
        title.textWrappingMode   = TextWrappingModes.NoWrap;
        UITheme.Size(title, width: 0f, flexWidth: 1f);

        if (timestampsPanel != null && timestampsPanel.OpenButton != null)
            ToolbarButton(timestampsPanel.OpenButton, bar, 230f, UIRole.Role.Neutral);

        var settingsButton = UITheme.MakeButton(bar, "SettingsButton", "Settings");
        settingsButton.onClick.AddListener(OpenSettings);
        ToolbarButton(settingsButton, bar, 150f, UIRole.Role.Neutral);

        ToolbarButton(quitButton, bar, 120f, UIRole.Role.Destructive);
    }

    void ToolbarButton(Button b, Transform bar, float width, UIRole.Role role)
    {
        if (b == null) return;
        Take(b, bar);
        UITheme.Size(b, width, UITheme.ControlHeight);
        UITheme.StyleButtonLabel(b);
        UITheme.Pin(b, role);
    }

    void BuildVoiceCard(Transform column)
    {
        var card = UITheme.MakeCard(column, "VoiceCard");
        UITheme.MakeText(card, "Heading", "1   Voice audio", UITheme.TextRole.Heading);
        Hint(card, "Paste a script and generate the narration with ElevenLabs (uses credits).");

        if (generateAudioButton == null) return;
        var row = UITheme.MakeRow(card, "Actions");
        Take(generateAudioButton, row);
        UITheme.Size(generateAudioButton, WideButtonWidth, UITheme.ControlHeight);
        SetButtonText(generateAudioButton, "Generate Audio…");
        UITheme.StyleButtonLabel(generateAudioButton);
    }

    void BuildGenerationCard(Transform column)
    {
        var card = UITheme.MakeCard(column, "GenerationCard");
        UITheme.MakeText(card, "Heading", "2   Generation", UITheme.TextRole.Heading);
        Hint(card, "The generated voice track that Start Recording will use.");

        if (outputLibrary == null) return;
        outputLibrary.EnsureUI(); // normally scene-baked; builds a fallback row otherwise

        var row = UITheme.MakeRow(card, "Picker");
        if (outputLibrary.Dropdown != null)
        {
            Take(outputLibrary.Dropdown, row);
            UITheme.Size(outputLibrary.Dropdown, width: 0f, height: UITheme.ControlHeight, flexWidth: 1f);
        }
        if (outputLibrary.RefreshButton != null)
        {
            Take(outputLibrary.RefreshButton, row);
            UITheme.Size(outputLibrary.RefreshButton, 140f, UITheme.ControlHeight);
            UITheme.StyleButtonLabel(outputLibrary.RefreshButton);
        }

        TMP_Text path = outputLibrary.SelectedPathLabel;
        if (path == null) return;
        Take(path, card);
        UITheme.Style(path, UITheme.TextRole.Small);
        path.alignment          = TextAlignmentOptions.MidlineLeft;
        path.textWrappingMode   = TextWrappingModes.NoWrap;
        path.overflowMode       = TextOverflowModes.Ellipsis;
        UITheme.Size(path, width: 0f, height: 22f);
    }

    void BuildLookCard(Transform column)
    {
        var card = UITheme.MakeCard(column, "LookCard");
        UITheme.MakeText(card, "Heading", "3   Look", UITheme.TextRole.Heading);
        Hint(card, "How the video looks. Each choice is saved as soon as you change it.");

        LookRow(card, "Background",           backgroundStylePrevButton,     backgroundStyleLabel,     backgroundStyleNextButton);
        LookRow(card, "Background mode",      backgroundModePrevButton,      backgroundModeLabel,      backgroundModeNextButton);
        LookRow(card, "Presenter transition", presenterTransitionPrevButton, presenterTransitionLabel, presenterTransitionNextButton);
        LookRow(card, "Card animation",       cardEntryPrevButton,           cardEntryLabel,           cardEntryNextButton);
        LookRow(card, "Visuals preset",       activeSavePrevButton,          activeSaveLabel,          activeSaveNextButton);

        // Opens the Visuals editor (the scene button's own onClick does that).
        Button presets = FindOnCanvas<Button>("PresetsButton");
        if (presets == null) return;
        var row = UITheme.MakeRow(card, "EditVisualsRow");
        UITheme.Size(UITheme.NewRect("Indent", row), width: LookLabelWidth); // lines up with the "<" column
        Take(presets, row);
        UITheme.Size(presets, WideButtonWidth, UITheme.ControlHeight);
        SetButtonText(presets, "Edit visuals…");
        UITheme.StyleButtonLabel(presets);
    }

    // "Label   <  [ value ]  >" — the same geometry for every cycle option.
    void LookRow(Transform card, string label, Button prev, TMP_Text value, Button next)
    {
        if (value == null) return;
        var row = UITheme.MakeRow(card, label.Replace(" ", "") + "Row");
        UITheme.Size(UITheme.MakeText(row, "Label", label, UITheme.TextRole.Label), width: LookLabelWidth);

        if (prev != null) Arrow(prev, row);

        var box = UITheme.NewRect("ValueBox", row);
        UITheme.Surface(box.gameObject.AddComponent<Image>(), UITheme.Field);
        UITheme.Size(box, width: 0f, height: UITheme.ControlHeight, flexWidth: 1f);
        Take(value, box);
        UITheme.Fill((RectTransform)value.transform);
        UITheme.Style(value, UITheme.TextRole.Value);
        value.alignment          = TextAlignmentOptions.Center;
        value.textWrappingMode   = TextWrappingModes.NoWrap;
        value.overflowMode       = TextOverflowModes.Ellipsis;

        if (next != null) Arrow(next, row);
    }

    void Arrow(Button b, Transform row)
    {
        Take(b, row);
        UITheme.Size(b, UITheme.ControlHeight, UITheme.ControlHeight);
        UITheme.StyleButtonLabel(b, 24);
    }

    void BuildRecordBar(Transform root)
    {
        var bar = UITheme.MakeRow(root, "RecordBar", height: 112f, spacing: 24f);
        bar.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(20, 24, 20, 20);
        UITheme.Surface(bar.gameObject.AddComponent<Image>(), UITheme.Card, UITheme.PanelRadius);

        Take(startButton, bar);
        UITheme.Size(startButton, 340f, 64f);
        SetButtonText(startButton, "Start Recording");
        UITheme.StyleButtonLabel(startButton, 24);

        var status = UITheme.MakeColumn(bar, "Status", 2f);
        status.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        UITheme.Size(status, width: 0f, flexWidth: 1f);

        Take(statusText, status);
        UITheme.Style(statusText, UITheme.TextRole.Value); // RefreshResult sets the colour
        statusText.fontSize           = 22;
        statusText.alignment          = TextAlignmentOptions.MidlineLeft;
        statusText.textWrappingMode   = TextWrappingModes.NoWrap;
        statusText.overflowMode       = TextOverflowModes.Ellipsis;
        UITheme.Size(statusText, width: 0f, height: 28f);

        // Saved path / error / import-export feedback: two lines, then "…".
        Take(pathText, status);
        UITheme.Style(pathText, UITheme.TextRole.Small);
        pathText.alignment          = TextAlignmentOptions.TopLeft;
        pathText.textWrappingMode   = TextWrappingModes.Normal;
        pathText.overflowMode       = TextOverflowModes.Ellipsis;
        UITheme.Size(pathText, width: 0f, height: 40f);

        if (openFolderButton == null) return;
        Take(openFolderButton, bar);
        UITheme.Size(openFolderButton, 240f, UITheme.ControlHeight);
        UITheme.StyleButtonLabel(openFolderButton);
    }

    // -----------------------------------------------------------------------
    // Settings window — the set-once folders + preset backup.
    // -----------------------------------------------------------------------

    void BuildSettingsPanel()
    {
        var panel = UITheme.Fill(UITheme.NewRect("SettingsPanel", canvasRoot));
        panel.gameObject.AddComponent<Image>().color = UITheme.Backdrop; // also blocks clicks behind
        settingsPanel = panel.gameObject;

        var window = UITheme.MakeColumn(panel, "Window", UITheme.Gap);
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
        window.sizeDelta = new Vector2(1240f, 0f);
        window.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 24, 28);
        window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UITheme.Surface(window.gameObject.AddComponent<Image>(), UITheme.Window, UITheme.PanelRadius);

        var header = UITheme.MakeRow(window, "Header", height: 56f);
        UITheme.Size(UITheme.MakeText(header, "Title", "Settings", UITheme.TextRole.Title), width: 0f, flexWidth: 1f);
        var close = UITheme.MakeButton(header, "SettingsCloseButton", "Close");
        UITheme.Size(close, 120f, UITheme.ControlHeight);
        close.onClick.AddListener(CloseSettings);

        UITheme.MakeText(window, "FoldersHeading", "Folders", UITheme.TextRole.Heading);
        FolderRow(window, "Voice library", pathInput, pathBrowseButton, DefaultPythonOutputFolder,
            "Generated voice folders are saved and listed here. Relative paths start in Assets/.");
        FolderRow(window, "Media", mediaRootInput, mediaRootBrowseButton, "Blank = the built-in media",
            "BRoll, Images, CEO, TwitterPosts, Flags and Logos subfolders are created inside it.");
        FolderRow(window, "Music", musicFolderInput, musicFolderBrowseButton, "Blank = no background music",
            "Tracks from this folder play under the voice. The field turns red if the folder is missing.");
        FolderRow(window, "Video output", recordingOutputInput, recordingOutputBrowseButton,
            "Blank = the recorder's default folder", "Finished recordings are saved here.");

        var divider = UITheme.NewRect("Divider", window).gameObject.AddComponent<Image>();
        divider.color = UITheme.Divider;
        UITheme.Size(divider, height: 1f);

        UITheme.MakeText(window, "PresetsHeading", "Visuals presets", UITheme.TextRole.Heading);
        var presetRow = UITheme.MakeRow(window, "PresetBackupRow");
        var presetHint = UITheme.MakeText(presetRow, "Hint",
            "Back up the active visuals preset to a .json file, or restore one — e.g. after installing a new build.",
            UITheme.TextRole.Muted);
        presetHint.textWrappingMode = TextWrappingModes.Normal;
        UITheme.Size(presetHint, width: 0f, flexWidth: 1f);
        foreach (Button b in new[] { activeSaveImportButton, activeSaveExportButton })
        {
            if (b == null) continue;
            Take(b, presetRow);
            UITheme.Size(b, 140f, UITheme.ControlHeight);
            UITheme.StyleButtonLabel(b);
        }

        settingsStatus = UITheme.MakeText(window, "Status", "", UITheme.TextRole.Small);
        UITheme.Size(settingsStatus, height: 22f);

        settingsPanel.SetActive(false);
    }

    // "Label  [ path ............ ]  [Browse…]" with a hint line under the field.
    void FolderRow(Transform window, string label, TMP_InputField input, Button browse,
                   string placeholder, string hint)
    {
        if (input == null) return;
        var block = UITheme.MakeColumn(window, label.Replace(" ", "") + "Folder", 4f);

        var row = UITheme.MakeRow(block, "Row");
        UITheme.Size(UITheme.MakeText(row, "Label", label, UITheme.TextRole.Label), width: FolderLabelWidth);
        Take(input, row);
        UITheme.Size(input, width: 0f, height: UITheme.ControlHeight, flexWidth: 1f);
        if (input.placeholder is TMP_Text ph) ph.text = placeholder;
        if (browse != null)
        {
            Take(browse, row);
            UITheme.Size(browse, 140f, UITheme.ControlHeight);
            UITheme.StyleButtonLabel(browse);
        }

        var hintRow = UITheme.MakeRow(block, "HintRow", height: 22f);
        UITheme.Size(UITheme.NewRect("Indent", hintRow), width: FolderLabelWidth);
        UITheme.Size(UITheme.MakeText(hintRow, "Hint", hint, UITheme.TextRole.Small), width: 0f, flexWidth: 1f);
    }

    void OpenSettings()
    {
        if (settingsPanel == null) return;
        if (settingsStatus != null) settingsStatus.text = "";
        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();
    }

    void CloseSettings()
    {
        FlushPathFields(); // a path typed without clicking out still counts
        settingsPanel.SetActive(false);
        // The voice-library folder may have changed — rescan the generations.
        if (outputLibrary != null) outputLibrary.RefreshOutputs();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    // Moves a control into a layout group and remembers the container it left.
    void Take(Component c, Transform into)
    {
        if (c == null) return;
        Transform old = c.transform.parent;
        UITheme.Adopt(c, into);
        if (old != null && old != canvasRoot) vacated.Add(old);
    }

    T FindOnCanvas<T>(string name) where T : Component
    {
        Transform t = canvasRoot.Find(name);
        return t != null ? t.GetComponent<T>() : null;
    }

    static void Hint(Transform card, string text)
    {
        var t = UITheme.MakeText(card, "Hint", text, UITheme.TextRole.Small);
        t.textWrappingMode = TextWrappingModes.Normal;
    }

    static void SetButtonText(Button b, string text)
    {
        var tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = text; return; }
        var legacy = b.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = text;
    }
}

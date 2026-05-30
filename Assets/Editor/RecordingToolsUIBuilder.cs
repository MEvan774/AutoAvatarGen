using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Tts;

/// <summary>
/// Editor menu items that materialize the three new recording-tools UIs as
/// real scene GameObjects under MainMenuCanvas, and wire the controller's
/// [SerializeField] references via SerializedObject. After running each
/// menu item once, the UI lives in the .unity file and can be restyled
/// directly in the Scene view — no runtime build code runs at all.
///
///   • Tools > AutoAvatarGen > Add Generate Audio Button
///   • Tools > AutoAvatarGen > Add Background Mode Row
///   • Tools > AutoAvatarGen > Add TTS Panel
///
/// Each command is idempotent: if the target GameObject already exists, the
/// menu item just re-wires the references in case they got disconnected.
/// Runs Undo.SetCurrentGroup so a single Ctrl+Z reverses the whole build.
/// </summary>
public static class RecordingToolsUIBuilder
{
    // ====================================================================
    // 1. Generate Audio Button
    // ====================================================================

    [MenuItem("Tools/AutoAvatarGen/Add Generate Audio Button")]
    static void AddGenerateAudioButton()
    {
        var ctx = OpenScene();
        if (ctx == null) return;

        const string objName = "GenerateAudioButton";
        var existing = ctx.canvas.transform.Find(objName);
        Button btn = existing != null ? existing.GetComponent<Button>() : null;

        if (btn == null)
        {
            Undo.SetCurrentGroupName("Add Generate Audio Button");
            int undoGroup = Undo.GetCurrentGroup();

            btn = CreateTintedButton(ctx.canvas.transform, objName,
                label: "Generate Audio…",
                tint: new Color(0.25f, 0.55f, 0.35f, 1f),
                size: new Vector2(320f, 64f),
                anchor: new Vector2(0f, 0f),
                anchoredPos: new Vector2(40f, 40f));

            Undo.RegisterCreatedObjectUndo(btn.gameObject, "Add Generate Audio Button");
            Undo.CollapseUndoOperations(undoGroup);
        }

        WireField(ctx.controller, "generateAudioButton", btn);
        Done(ctx, objName);
    }

    // ====================================================================
    // 2. Background Mode Row
    // ====================================================================

    [MenuItem("Tools/AutoAvatarGen/Add Background Mode Row")]
    static void AddBackgroundModeRow()
    {
        var ctx = OpenScene();
        if (ctx == null) return;

        const string rowName = "BackgroundModeRow";
        Transform row = ctx.canvas.transform.Find(rowName);

        Button   prevBtn;
        Button   nextBtn;
        TMP_Text label;

        if (row == null)
        {
            Undo.SetCurrentGroupName("Add Background Mode Row");
            int undoGroup = Undo.GetCurrentGroup();

            // Panel
            var panelGO = NewRect(ctx.canvas.transform, rowName);
            var panelRT = (RectTransform)panelGO.transform;
            panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0f);
            panelRT.anchoredPosition = new Vector2(0f, 120f);
            panelRT.sizeDelta        = new Vector2(900f, 100f);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.13f, 0.85f);

            // Header label
            var headerGO = NewText(panelGO.transform, "Header",
                "Background recording mode",
                fontSize: 22, bold: true,
                color: new Color(0.82f, 0.85f, 0.9f, 1f));
            var headerRT = (RectTransform)headerGO.transform;
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot     = new Vector2(0.5f, 1f);
            headerRT.anchoredPosition = new Vector2(0f, -10f);
            headerRT.sizeDelta        = new Vector2(-20f, 28f);

            const float controlsY = -56f;

            // Prev button "<"
            prevBtn = CreateTintedButton(panelGO.transform, "BackgroundModePrev",
                label: "<",
                tint: new Color(0.20f, 0.45f, 0.65f, 1f),
                size: new Vector2(60f, 44f),
                anchor: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(-220f, controlsY));

            // Value display
            var valueGO = NewRect(panelGO.transform, "BackgroundModeValue");
            valueGO.AddComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 1f);
            var valueRT = (RectTransform)valueGO.transform;
            valueRT.anchorMin = valueRT.anchorMax = valueRT.pivot = new Vector2(0.5f, 1f);
            valueRT.anchoredPosition = new Vector2(0f, controlsY);
            valueRT.sizeDelta        = new Vector2(320f, 44f);

            var labelGO = NewText(valueGO.transform, "Text", "Video",
                fontSize: 24, bold: true, color: Color.white);
            var labelRT = (RectTransform)labelGO.transform;
            labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;
            label = labelGO.GetComponent<TextMeshProUGUI>();

            // Next button ">"
            nextBtn = CreateTintedButton(panelGO.transform, "BackgroundModeNext",
                label: ">",
                tint: new Color(0.20f, 0.45f, 0.65f, 1f),
                size: new Vector2(60f, 44f),
                anchor: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(220f, controlsY));

            Undo.RegisterCreatedObjectUndo(panelGO, "Add Background Mode Row");
            Undo.CollapseUndoOperations(undoGroup);
        }
        else
        {
            // Re-wire only — preserves user styling tweaks.
            prevBtn = row.Find("BackgroundModePrev")?.GetComponent<Button>();
            nextBtn = row.Find("BackgroundModeNext")?.GetComponent<Button>();
            label   = row.Find("BackgroundModeValue/Text")?.GetComponent<TMP_Text>();
        }

        WireField(ctx.controller, "backgroundModePrevButton", prevBtn);
        WireField(ctx.controller, "backgroundModeNextButton", nextBtn);
        WireField(ctx.controller, "backgroundModeLabel",      label);
        Done(ctx, rowName);
    }

    // ====================================================================
    // 3. TTS Panel (full hierarchy + API key popup)
    // ====================================================================

    [MenuItem("Tools/AutoAvatarGen/Add TTS Panel")]
    static void AddTtsPanel()
    {
        var ctx = OpenScene();
        if (ctx == null) return;

        const string panelName = "TtsPanel";
        Transform existing = ctx.canvas.transform.Find(panelName);
        GameObject panelGO;

        if (existing == null)
        {
            Undo.SetCurrentGroupName("Add TTS Panel");
            int undoGroup = Undo.GetCurrentGroup();

            panelGO = BuildTtsPanelHierarchy(ctx.canvas.transform);

            Undo.RegisterCreatedObjectUndo(panelGO, "Add TTS Panel");
            Undo.CollapseUndoOperations(undoGroup);
        }
        else
        {
            panelGO = existing.gameObject;
        }

        var ttsCtrl = panelGO.GetComponent<TtsPanelController>();
        if (ttsCtrl == null) ttsCtrl = Undo.AddComponent<TtsPanelController>(panelGO);

        // Re-wire every Inspector reference on TtsPanelController from the
        // baked hierarchy. Names match what BuildTtsPanelHierarchy sets.
        WireField(ttsCtrl, "panelRoot",         panelGO);
        WireField(ttsCtrl, "scriptInput",       FindChild<TMP_InputField>(panelGO, "Panel/ScriptBox/Input"));
        WireField(ttsCtrl, "outputInput",       FindChild<TMP_InputField>(panelGO, "Panel/OutputRow/Input"));
        WireField(ttsCtrl, "outputBrowseButton",FindChild<Button>       (panelGO, "Panel/OutputRow/BrowseButton"));
        WireField(ttsCtrl, "apiKeyDisplay",     FindChild<TMP_Text>     (panelGO, "Panel/ApiKeyRow/KeyDisplay"));
        WireField(ttsCtrl, "apiKeyEditButton",  FindChild<Button>       (panelGO, "Panel/ApiKeyRow/EditButton"));
        WireField(ttsCtrl, "dryTestButton",     FindChild<Button>       (panelGO, "Panel/ActionRow/DryTestButton"));
        WireField(ttsCtrl, "generateButton",    FindChild<Button>       (panelGO, "Panel/ActionRow/GenerateButton"));
        WireField(ttsCtrl, "backButton",        FindChild<Button>       (panelGO, "Panel/HeaderRow/BackButton"));
        WireField(ttsCtrl, "progressTrack",     FindChild<RectTransform>(panelGO, "Panel/ProgressTrack"));
        WireField(ttsCtrl, "progressFill",      FindChild<RectTransform>(panelGO, "Panel/ProgressTrack/Fill"));
        WireField(ttsCtrl, "statusText",        FindChild<TMP_Text>     (panelGO, "Panel/StatusText"));

        // API key popup (baked as a sibling of TtsPanel so it overlays at runtime)
        var popupTf = ctx.canvas.transform.Find("TtsApiKeyPopup");
        TtsApiKeyPopup popup = popupTf != null ? popupTf.GetComponent<TtsApiKeyPopup>() : null;
        if (popup == null)
        {
            Undo.SetCurrentGroupName("Add TTS API Key Popup");
            int undoGroup2 = Undo.GetCurrentGroup();
            var popupGO = BuildApiKeyPopupHierarchy(ctx.canvas.transform);
            popup = popupGO.GetComponent<TtsApiKeyPopup>() ??
                    Undo.AddComponent<TtsApiKeyPopup>(popupGO);
            Undo.RegisterCreatedObjectUndo(popupGO, "Add TTS API Key Popup");
            Undo.CollapseUndoOperations(undoGroup2);
        }

        WireField(popup, "popupRoot",   popup.gameObject);
        WireField(popup, "keyInput",    FindChild<TMP_InputField>(popup.gameObject, "Panel/KeyInputRow/KeyInput"));
        WireField(popup, "statusText",  FindChild<TMP_Text>      (popup.gameObject, "Panel/StatusText"));
        WireField(popup, "maskToggle",  FindChild<Toggle>        (popup.gameObject, "Panel/MaskToggleRow/MaskToggle"));
        WireField(popup, "saveButton",  FindChild<Button>        (popup.gameObject, "Panel/Buttons/SaveButton"));
        WireField(popup, "cancelButton",FindChild<Button>        (popup.gameObject, "Panel/Buttons/CancelButton"));
        WireField(popup, "clearButton", FindChild<Button>        (popup.gameObject, "Panel/Buttons/ClearButton"));

        WireField(ttsCtrl, "apiKeyPopup", popup);
        Done(ctx, panelName);
    }

    // --------------------------------------------------------------------
    // TTS Panel hierarchy builder
    // --------------------------------------------------------------------

    static GameObject BuildTtsPanelHierarchy(Transform canvas)
    {
        // Root — fills the screen so the backdrop can dim the menu underneath.
        var rootGO = NewRect(canvas, "TtsPanel");
        var rootRT = (RectTransform)rootGO.transform;
        rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;

        var backdrop = NewRect(rootGO.transform, "Backdrop");
        var backdropRT = (RectTransform)backdrop.transform;
        backdropRT.anchorMin = Vector2.zero; backdropRT.anchorMax = Vector2.one;
        backdropRT.offsetMin = backdropRT.offsetMax = Vector2.zero;
        backdrop.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        // Centered panel
        var panel = NewRect(rootGO.transform, "Panel");
        var panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(1100f, 760f);
        panel.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

        const float W = 1100f;
        float y = 760f * 0.5f - 30f;

        // Header row
        var headerRow = MakeRow(panel.transform, "HeaderRow", W - 60f, 56f, ref y);
        var title = NewText(headerRow.transform, "Title", "Generate Audio",
            fontSize: 28, bold: true, color: new Color(0.95f, 0.97f, 1f));
        var titleRT = (RectTransform)title.transform;
        titleRT.anchorMin = titleRT.anchorMax = titleRT.pivot = new Vector2(0f, 0.5f);
        titleRT.anchoredPosition = new Vector2(20f, 0f);
        titleRT.sizeDelta        = new Vector2(600f, 48f);
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

        var backBtn = CreateTintedButton(headerRow.transform, "BackButton",
            label: "Back", tint: new Color(0.32f, 0.34f, 0.40f),
            size: new Vector2(140f, 44f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-90f, 0f));

        y -= 12f;

        // Script label + textarea
        var scriptLabel = MakeRow(panel.transform, "ScriptLabel", W - 60f, 24f, ref y);
        var lblTmp = NewText(scriptLabel.transform, "Text", "Script (paste here)",
            fontSize: 16, bold: true, color: new Color(0.75f, 0.80f, 0.86f));
        StretchInside((RectTransform)lblTmp.transform);
        lblTmp.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

        var scriptBox = MakeRow(panel.transform, "ScriptBox", W - 60f, 280f, ref y);
        scriptBox.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
        var scriptInput = NewInputField(scriptBox.transform, "Input",
            placeholder: "## COLD OPEN\n[deadpan] Paste your script here...\n## SETUP\n...",
            multiline: true);
        StretchInside((RectTransform)scriptInput.transform, padX: 12, padY: 10);

        // Long scripts: give the multiline field a clipping viewport + a
        // draggable vertical scrollbar so text scrolls instead of spilling past
        // the box. Same helper the auto-baker uses to fix already-baked panels.
        TtsScriptBoxScrollbarBaker.EnsureScrollable(scriptInput);

        y -= 12f;

        // Output folder row
        var outputRow = MakeRow(panel.transform, "OutputRow", W - 60f, 56f, ref y);
        outputRow.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.10f, 1f);
        AddLabel(outputRow.transform, "Label", "Output folder",
            anchoredPos: new Vector2(20f, 0f), size: new Vector2(180f, 44f));
        var outputInput = NewInputField(outputRow.transform, "Input",
            placeholder: "Python/output", multiline: false);
        var oirt = (RectTransform)outputInput.transform;
        oirt.anchorMin = new Vector2(0f, 0.5f);
        oirt.anchorMax = new Vector2(1f, 0.5f);
        oirt.pivot     = new Vector2(0f, 0.5f);
        oirt.anchoredPosition = new Vector2(210f, 0f);
        oirt.sizeDelta        = new Vector2(W - 60f - 210f - 170f, 44f);
        outputInput.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);

        CreateTintedButton(outputRow.transform, "BrowseButton",
            label: "Browse…", tint: new Color(0.20f, 0.45f, 0.65f),
            size: new Vector2(160f, 44f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-90f, 0f));

        y -= 8f;

        // API key row
        var apiRow = MakeRow(panel.transform, "ApiKeyRow", W - 60f, 56f, ref y);
        apiRow.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.10f, 1f);
        AddLabel(apiRow.transform, "Label", "API key",
            anchoredPos: new Vector2(20f, 0f), size: new Vector2(180f, 44f));
        AddLabel(apiRow.transform, "KeyDisplay", "no key saved",
            anchoredPos: new Vector2(210f, 0f), size: new Vector2(640f, 44f),
            italic: true);
        CreateTintedButton(apiRow.transform, "EditButton",
            label: "Edit Key…", tint: new Color(0.40f, 0.30f, 0.55f),
            size: new Vector2(180f, 44f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-110f, 0f));

        y -= 14f;

        // Action row
        var actionRow = MakeRow(panel.transform, "ActionRow", W - 60f, 60f, ref y);
        CreateTintedButton(actionRow.transform, "DryTestButton",
            label: "Dry Test", tint: new Color(0.30f, 0.55f, 0.65f),
            size: new Vector2(200f, 50f),
            anchor: new Vector2(0f, 0.5f),
            anchoredPos: new Vector2(110f, 0f));
        CreateTintedButton(actionRow.transform, "GenerateButton",
            label: "Generate", tint: new Color(0.25f, 0.65f, 0.40f),
            size: new Vector2(240f, 50f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-130f, 0f));

        y -= 12f;

        // Progress
        var progressRow = MakeRow(panel.transform, "ProgressTrack", W - 60f, 28f, ref y);
        progressRow.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
        var fillGO = NewRect(progressRow.transform, "Fill");
        var frt = (RectTransform)fillGO.transform;
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot     = new Vector2(0f, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta        = new Vector2(0f, 0f);
        fillGO.AddComponent<Image>().color = new Color(0.30f, 0.65f, 0.45f);

        y -= 4f;

        // Status
        var statusRow = MakeRow(panel.transform, "StatusText", W - 60f, 32f, ref y);
        var statusTmp = statusRow.AddComponent<TextMeshProUGUI>();
        statusTmp.font     = TMP_Settings.defaultFontAsset;
        statusTmp.fontSize = 15;
        statusTmp.fontStyle = FontStyles.Italic;
        statusTmp.text     = "Ready.";
        statusTmp.alignment = TextAlignmentOptions.MidlineLeft;
        statusTmp.color     = new Color(0.65f, 0.70f, 0.78f);
        statusTmp.raycastTarget = false;

        rootGO.AddComponent<TtsPanelController>();
        rootGO.SetActive(false); // hidden until Show() is called

        return rootGO;
    }

    // --------------------------------------------------------------------
    // API Key Popup hierarchy builder
    // --------------------------------------------------------------------

    static GameObject BuildApiKeyPopupHierarchy(Transform canvas)
    {
        var rootGO = NewRect(canvas, "TtsApiKeyPopup");
        var rootRT = (RectTransform)rootGO.transform;
        rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;

        var backdrop = NewRect(rootGO.transform, "Backdrop");
        var backdropRT = (RectTransform)backdrop.transform;
        backdropRT.anchorMin = Vector2.zero; backdropRT.anchorMax = Vector2.one;
        backdropRT.offsetMin = backdropRT.offsetMax = Vector2.zero;
        backdrop.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

        var panel = NewRect(rootGO.transform, "Panel");
        var panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(720f, 360f);
        panel.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

        float y = 360f * 0.5f - 40f;

        var title = MakeRow(panel.transform, "Title", 660f, 50f, ref y);
        var titleTmp = title.AddComponent<TextMeshProUGUI>();
        titleTmp.font     = TMP_Settings.defaultFontAsset;
        titleTmp.fontSize = 26;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.text     = "ElevenLabs API Key";
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color     = new Color(0.95f, 0.97f, 1f);
        titleTmp.raycastTarget = false;
        y -= 12f;

        var hint = MakeRow(panel.transform, "Hint", 660f, 30f, ref y);
        var hintTmp = hint.AddComponent<TextMeshProUGUI>();
        hintTmp.font     = TMP_Settings.defaultFontAsset;
        hintTmp.fontSize = 16;
        hintTmp.fontStyle = FontStyles.Italic;
        hintTmp.text     = "Paste your xi-api-key. Stored locally in PlayerPrefs.";
        hintTmp.alignment = TextAlignmentOptions.Center;
        hintTmp.color     = new Color(0.65f, 0.70f, 0.78f);
        hintTmp.raycastTarget = false;
        y -= 18f;

        // Key input
        var keyRow = MakeRow(panel.transform, "KeyInputRow", 660f, 56f, ref y);
        keyRow.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
        var keyInput = NewInputField(keyRow.transform, "KeyInput",
            placeholder: "sk_...", multiline: false);
        StretchInside((RectTransform)keyInput.transform, padX: 12, padY: 6);
        keyInput.contentType = TMP_InputField.ContentType.Password;
        y -= 6f;

        // Mask toggle
        var maskRow = MakeRow(panel.transform, "MaskToggleRow", 660f, 36f, ref y);
        var toggleGO = NewRect(maskRow.transform, "MaskToggle");
        var toggleRT = (RectTransform)toggleGO.transform;
        toggleRT.anchorMin = toggleRT.anchorMax = toggleRT.pivot = new Vector2(0f, 0.5f);
        toggleRT.anchoredPosition = new Vector2(20f, 0f);
        toggleRT.sizeDelta        = new Vector2(28f, 28f);
        var box = NewRect(toggleGO.transform, "Box");
        StretchInside((RectTransform)box.transform);
        box.AddComponent<Image>().color = new Color(0.20f, 0.22f, 0.28f);
        var check = NewRect(toggleGO.transform, "Check");
        var checkRT = (RectTransform)check.transform;
        checkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkRT.offsetMin = checkRT.offsetMax = Vector2.zero;
        var checkImg = check.AddComponent<Image>();
        checkImg.color = new Color(0.40f, 0.85f, 0.55f);
        var toggle = toggleGO.AddComponent<Toggle>();
        toggle.graphic       = checkImg;
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.isOn = false;

        AddLabel(maskRow.transform, "Label", "Show key",
            anchoredPos: new Vector2(60f, 0f), size: new Vector2(300f, 28f));
        y -= 14f;

        // Status
        var status = MakeRow(panel.transform, "StatusText", 660f, 28f, ref y);
        var statusTmp = status.AddComponent<TextMeshProUGUI>();
        statusTmp.font     = TMP_Settings.defaultFontAsset;
        statusTmp.fontSize = 15;
        statusTmp.fontStyle = FontStyles.Italic;
        statusTmp.alignment = TextAlignmentOptions.Center;
        statusTmp.color     = new Color(0.65f, 0.70f, 0.78f);
        statusTmp.raycastTarget = false;
        y -= 14f;

        // Buttons row — Clear (left), Cancel + Save (right)
        var btnRow = MakeRow(panel.transform, "Buttons", 660f, 56f, ref y);
        CreateTintedButton(btnRow.transform, "ClearButton",
            label: "Clear", tint: new Color(0.50f, 0.20f, 0.20f),
            size: new Vector2(140f, 44f),
            anchor: new Vector2(0f, 0.5f),
            anchoredPos: new Vector2(100f, 0f));
        CreateTintedButton(btnRow.transform, "CancelButton",
            label: "Cancel", tint: new Color(0.32f, 0.34f, 0.40f),
            size: new Vector2(140f, 44f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-260f, 0f));
        CreateTintedButton(btnRow.transform, "SaveButton",
            label: "Save", tint: new Color(0.25f, 0.55f, 0.35f),
            size: new Vector2(140f, 44f),
            anchor: new Vector2(1f, 0.5f),
            anchoredPos: new Vector2(-100f, 0f));

        rootGO.AddComponent<TtsApiKeyPopup>();
        rootGO.SetActive(false);
        return rootGO;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    class Context { public MainMenuController controller; public Canvas canvas; }

    static Context OpenScene()
    {
        var controller = Object.FindObjectOfType<MainMenuController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Recording Tools UI Builder",
                "No MainMenuController found in the open scene. Open Assets/Scenes/MainMenu.unity first.",
                "OK");
            return null;
        }
        var canvas = controller.GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Recording Tools UI Builder",
                "No Canvas found under MainMenuController. Run 'Build Main Menu UI' first to create the base canvas.",
                "OK");
            return null;
        }
        return new Context { controller = controller, canvas = canvas };
    }

    static void Done(Context ctx, string objName)
    {
        EditorSceneManager.MarkSceneDirty(ctx.controller.gameObject.scene);
        Selection.activeGameObject = ctx.canvas.transform.Find(objName)?.gameObject;
        Debug.Log($"[RecordingToolsUIBuilder] '{objName}' is in the scene and wired. " +
                  $"Save the scene (Ctrl+S) to persist.");
    }

    // SerializedObject wiring — writes the [SerializeField] reference even
    // for private fields (which assignment via reflection wouldn't pick up
    // for serialization).
    static void WireField(Object target, string fieldName, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[RecordingToolsUIBuilder] {target.GetType().Name} has no " +
                             $"SerializedField '{fieldName}'. Skipping.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    static T FindChild<T>(GameObject root, string path) where T : Component
    {
        var t = root.transform.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    // ----- low-level construction primitives ----------------------------

    static GameObject NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject NewText(Transform parent, string name, string text,
        int fontSize = 18, bool bold = false, bool italic = false, Color? color = null)
    {
        var go = NewRect(parent, name);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font     = TMP_Settings.defaultFontAsset;
        tmp.fontSize = fontSize;
        var style = FontStyles.Normal;
        if (bold)   style |= FontStyles.Bold;
        if (italic) style |= FontStyles.Italic;
        tmp.fontStyle = style;
        tmp.text     = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color ?? Color.white;
        tmp.raycastTarget = false;
        return go;
    }

    static GameObject MakeRow(Transform parent, string name, float width, float height, ref float y)
    {
        var go = NewRect(parent, name);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(0f, y - height * 0.5f);
        y -= height;
        return go;
    }

    static void StretchInside(RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    static void AddLabel(Transform parent, string name, string text,
        Vector2 anchoredPos, Vector2 size, bool italic = false)
    {
        var go = NewText(parent, name, text,
            fontSize: 16, bold: !italic, italic: italic,
            color: new Color(0.75f, 0.80f, 0.86f));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        go.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
    }

    static Button CreateTintedButton(Transform parent, string name,
        string label, Color tint, Vector2 size, Vector2 anchor, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        var img = go.GetComponent<Image>();
        img.color = tint;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        var lblGO = NewRect(go.transform, "Label");
        StretchInside((RectTransform)lblGO.transform);
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.font     = TMP_Settings.defaultFontAsset;
        tmp.fontSize = 22;
        tmp.fontStyle = FontStyles.Bold;
        tmp.text     = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        tmp.raycastTarget = false;
        return btn;
    }

    static TMP_InputField NewInputField(Transform parent, string name,
        string placeholder, bool multiline)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.13f, 1f);

        var input = go.GetComponent<TMP_InputField>();
        // ContentType.Standard resets lineType to SingleLine — set it AFTER
        // contentType for multiline to take effect.
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType    = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;

        var textGO = NewRect(go.transform, "Text");
        var textRT = (RectTransform)textGO.transform;
        StretchInside(textRT, padX: 10, padY: 6);
        var textTMP = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.font      = TMP_Settings.defaultFontAsset;
        textTMP.fontSize  = 16;
        textTMP.color     = new Color(0.95f, 0.97f, 1f);
        textTMP.alignment = TextAlignmentOptions.TopLeft;
        textTMP.enableWordWrapping = true;
        textTMP.raycastTarget = false;

        var phGO = NewRect(go.transform, "Placeholder");
        var phRT = (RectTransform)phGO.transform;
        StretchInside(phRT, padX: 10, padY: 6);
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.font      = TMP_Settings.defaultFontAsset;
        phTMP.fontSize  = 16;
        phTMP.fontStyle = FontStyles.Italic;
        phTMP.color     = new Color(0.45f, 0.50f, 0.58f);
        phTMP.alignment = TextAlignmentOptions.TopLeft;
        phTMP.enableWordWrapping = true;
        phTMP.text      = placeholder;
        phTMP.raycastTarget = false;

        input.textComponent = textTMP;
        input.placeholder   = phTMP;
        return input;
    }
}

using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor utility that materialises the main menu UI as real scene
/// GameObjects under the existing <see cref="MainMenuController"/>. Mirrors
/// the controller's serialized fields and wires every reference.
///
/// Run via: Tools -> AutoAvatarGen -> Build Main Menu UI
///
/// Uses TextMeshPro for all text / inputs / button labels, matching the
/// controller's TMP_Text / TMP_InputField fields.
/// </summary>
public static class MainMenuUIBuilder
{
    // Non-destructive patch — adds just the Browse… button next to the existing
    // PathInput and wires MainMenuController.pathBrowseButton. Use this instead
    // of "Build Main Menu UI" when you've hand-tweaked the canvas and don't
    // want a full rebuild to wipe those edits.
    [MenuItem("Tools/AutoAvatarGen/Add Path Browse Button")]
    static void AddPathBrowseButton()
    {
        var controller = Object.FindFirstObjectByType<MainMenuController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "Add Path Browse Button",
                "No MainMenuController found in the open scene.\n\n" +
                "Open Assets/Scenes/MainMenu.unity first, then re-run this command.",
                "OK");
            return;
        }

        Transform canvas = controller.transform.Find("MainMenuCanvas");
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(
                "Add Path Browse Button",
                "No 'MainMenuCanvas' child found under '" + controller.name +
                "'. Run 'Build Main Menu UI' first to create the base canvas.",
                "OK");
            return;
        }

        Transform pathInputTf = canvas.Find("PathInput");
        if (pathInputTf == null)
        {
            EditorUtility.DisplayDialog(
                "Add Path Browse Button",
                "Could not find 'PathInput' under MainMenuCanvas. Has it been " +
                "renamed? Expected a child named exactly 'PathInput'.",
                "OK");
            return;
        }

        if (canvas.Find("PathBrowseButton") != null)
        {
            // Already there — just rewire the controller field in case it got
            // disconnected, then bail.
            var existingBtn = canvas.Find("PathBrowseButton").GetComponent<Button>();
            WirePathBrowseButton(controller, existingBtn);
            EditorUtility.DisplayDialog(
                "Add Path Browse Button",
                "PathBrowseButton already exists — nothing to add. " +
                "Re-wired the controller reference in case it was missing.",
                "OK");
            return;
        }

        Undo.SetCurrentGroupName("Add Path Browse Button");
        int undoGroup = Undo.GetCurrentGroup();

        // Shrink PathInput and shift it left so the new button has room.
        var inputRT = pathInputTf.GetComponent<RectTransform>();
        Undo.RecordObject(inputRT, "Resize PathInput");
        Vector2 inputAnchorMin = inputRT.anchorMin;
        Vector2 inputAnchorMax = inputRT.anchorMax;
        Vector2 inputPivot     = inputRT.pivot;
        Vector2 inputPos       = inputRT.anchoredPosition;
        inputRT.sizeDelta        = new Vector2(920, inputRT.sizeDelta.y);
        inputRT.anchoredPosition = new Vector2(inputPos.x - 90, inputPos.y);

        // Build the Browse button using the same helpers/styling as the rest.
        var browseBtn = CreateButton("PathBrowseButton", canvas,
            "Browse…", new Color(0.20f, 0.45f, 0.65f), labelSize: 26);
        SetRect(browseBtn.GetComponent<RectTransform>(),
            inputAnchorMin, inputAnchorMax, inputPivot,
            new Vector2(inputPos.x + 460, inputPos.y), new Vector2(160, 56));

        // Place it just after the input in sibling order so it draws together.
        browseBtn.transform.SetSiblingIndex(pathInputTf.GetSiblingIndex() + 1);

        WirePathBrowseButton(controller, browseBtn);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = browseBtn.gameObject;

        Debug.Log("[MainMenuUIBuilder] Added PathBrowseButton next to PathInput. " +
                  "Save the scene (Ctrl+S) to persist.");
    }

    static void WirePathBrowseButton(MainMenuController controller, Button btn)
    {
        var so = new SerializedObject(controller);
        var prop = so.FindProperty("pathBrowseButton");
        if (prop == null)
        {
            Debug.LogError("[MainMenuUIBuilder] MainMenuController has no " +
                           "'pathBrowseButton' field — did the script fail to compile?");
            return;
        }
        prop.objectReferenceValue = btn;
        so.ApplyModifiedProperties();
    }

    [MenuItem("Tools/AutoAvatarGen/Build Main Menu UI")]
    static void Build()
    {
        var controller = Object.FindFirstObjectByType<MainMenuController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "Main Menu UI Builder",
                "No MainMenuController found in the open scene.\n\n" +
                "Open Assets/Scenes/MainMenu.unity first, then re-run this command.",
                "OK");
            return;
        }

        Transform existing = controller.transform.Find("MainMenuCanvas");
        if (existing != null)
        {
            bool ok = EditorUtility.DisplayDialog(
                "Main Menu UI Builder",
                "A MainMenuCanvas already exists under '" + controller.name +
                "'.\n\nDelete it and rebuild from scratch?",
                "Rebuild", "Cancel");
            if (!ok) return;
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        Undo.SetCurrentGroupName("Build Main Menu UI");
        int undoGroup = Undo.GetCurrentGroup();

        // ---------- Canvas ----------
        GameObject canvasObj = NewUIObject("MainMenuCanvas", controller.transform);
        Canvas canvas = Undo.AddComponent<Canvas>(canvasObj);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(canvasObj);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Undo.AddComponent<GraphicRaycaster>(canvasObj);

        // ---------- Background ----------
        Image bg = CreateImage("Background", canvasObj.transform);
        bg.color = new Color(0.07f, 0.08f, 0.11f, 1f);
        StretchToParent(bg.rectTransform);

        // ---------- Title ----------
        var title = CreateText("Title", canvasObj.transform, "AutoAvatarGen",
            90, TextAlignmentOptions.Center, FontStyles.Bold);
        SetRect(title.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -180), new Vector2(1400, 160));

        // ---------- Subtitle ----------
        var subtitle = CreateText("Subtitle", canvasObj.transform,
            "The recorder reads the Python pre-processor output (manifest.json / *_timed.txt " +
            "+ *.mp3). Point the field below at that folder, then press Start.",
            26, TextAlignmentOptions.Center, FontStyles.Normal);
        subtitle.color = new Color(0.72f, 0.76f, 0.82f, 1f);
        subtitle.enableWordWrapping = true;
        SetRect(subtitle.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -300), new Vector2(1500, 80));

        // ---------- Python output folder row ----------
        var pathLabel = CreateText("PathLabel", canvasObj.transform,
            "Python output folder (absolute path or relative to Assets/):",
            24, TextAlignmentOptions.Center, FontStyles.Normal);
        pathLabel.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        SetRect(pathLabel.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 180), new Vector2(1100, 32));

        var pathInput = CreateInputField("PathInput", canvasObj.transform,
            MainMenuController.DefaultPythonOutputFolder,
            "e.g. Python/output  or  D:/MyOutputs/elevenlabs");
        SetRect(pathInput.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-90, 125), new Vector2(920, 56));

        var pathBrowseBtn = CreateButton("PathBrowseButton", canvasObj.transform,
            "Browse…", new Color(0.20f, 0.45f, 0.65f), labelSize: 26);
        SetRect(pathBrowseBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(460, 125), new Vector2(160, 56));

        // ---------- Background video override row (NEW) ----------
        var videoLabel = CreateText("VideoLabel", canvasObj.transform,
            "Background video override (leave blank for default):",
            24, TextAlignmentOptions.Center, FontStyles.Normal);
        videoLabel.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        SetRect(videoLabel.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 55), new Vector2(1100, 32));

        var videoPathInput = CreateInputField("VideoPathInput", canvasObj.transform,
            "", "C:\\path\\to\\background.mp4");
        SetRect(videoPathInput.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-90, 0), new Vector2(920, 56));

        var videoLoadBtn = CreateButton("VideoLoadButton", canvasObj.transform,
            "Load…", new Color(0.20f, 0.45f, 0.65f), labelSize: 26);
        SetRect(videoLoadBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(440, 0), new Vector2(140, 56));

        var videoClearBtn = CreateButton("VideoClearButton", canvasObj.transform,
            "Clear", new Color(0.32f, 0.34f, 0.40f), labelSize: 26);
        SetRect(videoClearBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(580, 0), new Vector2(110, 56));

        // ---------- Start button ----------
        var startBtn = CreateButton("StartButton", canvasObj.transform,
            "Start Recording", new Color(0.18f, 0.62f, 0.34f));
        SetRect(startBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -100), new Vector2(560, 100));

        // ---------- Quit button ----------
        var quitBtn = CreateButton("QuitButton", canvasObj.transform,
            "Quit", new Color(0.58f, 0.17f, 0.17f));
        SetRect(quitBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -220), new Vector2(560, 100));

        // ---------- Result panel ----------
        Image resultPanel = CreateImage("ResultPanel", canvasObj.transform);
        resultPanel.color = new Color(1f, 1f, 1f, 0.06f);
        SetRect(resultPanel.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 80), new Vector2(1500, 170));

        var statusText = CreateText("Status", resultPanel.transform, "Ready to record.",
            42, TextAlignmentOptions.Top, FontStyles.Bold);
        SetRect(statusText.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -25), new Vector2(-40, 60));

        var pathText = CreateText("Path", resultPanel.transform,
            "No recording has been completed yet in this session.",
            24, TextAlignmentOptions.Center, FontStyles.Normal);
        pathText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        pathText.enableWordWrapping = true;
        pathText.overflowMode       = TextOverflowModes.Truncate;
        SetRect(pathText.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -15), new Vector2(-80, -80));

        // ---------- EventSystem (only if missing) ----------
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            Undo.AddComponent<EventSystem>(es);
            Undo.AddComponent<InputSystemUIInputModule>(es);
        }

        // ---------- Wire controller references ----------
        var so = new SerializedObject(controller);
        so.FindProperty("statusText").objectReferenceValue       = statusText;
        so.FindProperty("pathText").objectReferenceValue         = pathText;
        so.FindProperty("pathInput").objectReferenceValue        = pathInput;
        so.FindProperty("pathBrowseButton").objectReferenceValue = pathBrowseBtn;
        so.FindProperty("startButton").objectReferenceValue      = startBtn;
        so.FindProperty("quitButton").objectReferenceValue       = quitBtn;
        so.FindProperty("videoPathInput").objectReferenceValue   = videoPathInput;
        so.FindProperty("videoLoadButton").objectReferenceValue  = videoLoadBtn;
        so.FindProperty("videoClearButton").objectReferenceValue = videoClearBtn;
        so.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = canvasObj;

        Debug.Log("[MainMenuUIBuilder] Built MainMenuCanvas under '" + controller.name +
                  "'. Save the scene (Ctrl+S) to persist.");
    }

    // -----------------------------------------------------------------------
    // Helpers (TextMeshPro-flavoured)
    // -----------------------------------------------------------------------

    static GameObject NewUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        return go;
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta        = sizeDelta;
    }

    static void StretchToParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Sprite GetUISprite()
        => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    static Image CreateImage(string name, Transform parent)
    {
        GameObject go = NewUIObject(name, parent);
        Image img = Undo.AddComponent<Image>(go);
        img.sprite = GetUISprite();
        img.type   = Image.Type.Simple;
        return img;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string value,
        int size, TextAlignmentOptions alignment, FontStyles style)
    {
        GameObject go = NewUIObject(name, parent);
        TextMeshProUGUI t = Undo.AddComponent<TextMeshProUGUI>(go);
        t.text       = value;
        t.fontSize   = size;
        t.fontStyle  = style;
        t.alignment  = alignment;
        t.color      = Color.white;
        t.enableWordWrapping = false;
        return t;
    }

    static Button CreateButton(string name, Transform parent, string label, Color tint,
        int labelSize = 44)
    {
        GameObject go = NewUIObject(name, parent);
        Image img = Undo.AddComponent<Image>(go);
        img.sprite = GetUISprite();
        img.color  = tint;

        Button btn = Undo.AddComponent<Button>(go);
        ColorBlock colors = btn.colors;
        colors.normalColor      = tint;
        colors.highlightedColor = new Color(
            Mathf.Min(1f, tint.r + 0.10f),
            Mathf.Min(1f, tint.g + 0.10f),
            Mathf.Min(1f, tint.b + 0.10f), 1f);
        colors.pressedColor = new Color(
            Mathf.Max(0f, tint.r - 0.12f),
            Mathf.Max(0f, tint.g - 0.12f),
            Mathf.Max(0f, tint.b - 0.12f), 1f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;

        var labelText = CreateText("Label", go.transform, label, labelSize,
            TextAlignmentOptions.Center, FontStyles.Bold);
        StretchToParent(labelText.rectTransform);
        return btn;
    }

    static TMP_InputField CreateInputField(string name, Transform parent,
        string defaultText, string placeholderHint)
    {
        GameObject go = NewUIObject(name, parent);

        Image bg = Undo.AddComponent<Image>(go);
        bg.sprite = GetUISprite();
        bg.type   = Image.Type.Simple;
        bg.color  = new Color(0.15f, 0.17f, 0.21f, 1f);

        TMP_InputField input = Undo.AddComponent<TMP_InputField>(go);

        // The input area's text component (lives inside the field).
        GameObject textArea = NewUIObject("Text Area", go.transform);
        var taRT = textArea.GetComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(16, 6);
        taRT.offsetMax = new Vector2(-16, -6);
        Undo.AddComponent<RectMask2D>(textArea);

        var text = CreateText("Text", textArea.transform, "", 26,
            TextAlignmentOptions.Left, FontStyles.Normal);
        text.richText = false;
        var textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var placeholder = CreateText("Placeholder", textArea.transform, placeholderHint,
            26, TextAlignmentOptions.Left, FontStyles.Italic);
        placeholder.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        var phRT = placeholder.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;

        input.textViewport  = taRT;
        input.textComponent = text;
        input.placeholder   = placeholder;
        input.targetGraphic = bg;
        input.text          = defaultText;

        return input;
    }
}

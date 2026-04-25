using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor utility that materialises the main menu UI as real scene
/// GameObjects under the existing <see cref="MainMenuController"/>. Mirrors the
/// hierarchy that the runtime BuildUI used to construct, then wires the
/// controller's serialized references so the scene is fully editable.
///
/// Run via: Tools -> AutoAvatarGen -> Build Main Menu UI
/// </summary>
public static class MainMenuUIBuilder
{
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
        Text title = CreateText("Title", canvasObj.transform, "AutoAvatarGen",
            90, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -180), new Vector2(1400, 160));

        // ---------- Subtitle ----------
        Text subtitle = CreateText("Subtitle", canvasObj.transform,
            "The recorder reads the Python pre-processor output (manifest.json / *_timed.txt " +
            "+ *.mp3). Point the field below at that folder, then press Start.",
            26, TextAnchor.MiddleCenter, FontStyle.Normal);
        subtitle.color = new Color(0.72f, 0.76f, 0.82f, 1f);
        SetRect(subtitle.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -300), new Vector2(1500, 80));

        // ---------- Path label ----------
        Text pathLabel = CreateText("PathLabel", canvasObj.transform,
            "Python output folder (relative to Assets/):",
            24, TextAnchor.MiddleCenter, FontStyle.Normal);
        pathLabel.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        SetRect(pathLabel.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 155), new Vector2(1100, 32));

        // ---------- Path input ----------
        InputField pathInput = CreateInputField("PathInput", canvasObj.transform,
            MainMenuController.DefaultPythonOutputFolder);
        SetRect(pathInput.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 100), new Vector2(1100, 56));

        // ---------- Start button ----------
        Button startBtn = CreateButton("StartButton", canvasObj.transform,
            "Start Recording", new Color(0.18f, 0.62f, 0.34f));
        SetRect(startBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 10), new Vector2(560, 110));

        // ---------- Quit button ----------
        Button quitBtn = CreateButton("QuitButton", canvasObj.transform,
            "Quit", new Color(0.58f, 0.17f, 0.17f));
        SetRect(quitBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -130), new Vector2(560, 110));

        // ---------- Result panel ----------
        Image resultPanel = CreateImage("ResultPanel", canvasObj.transform);
        resultPanel.color = new Color(1f, 1f, 1f, 0.06f);
        SetRect(resultPanel.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 160), new Vector2(1500, 200));

        Text statusText = CreateText("Status", resultPanel.transform, "Ready to record.",
            42, TextAnchor.UpperCenter, FontStyle.Bold);
        SetRect(statusText.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -25), new Vector2(-40, 60));

        Text pathText = CreateText("Path", resultPanel.transform,
            "No recording has been completed yet in this session.",
            24, TextAnchor.MiddleCenter, FontStyle.Normal);
        pathText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        pathText.horizontalOverflow = HorizontalWrapMode.Wrap;
        pathText.verticalOverflow   = VerticalWrapMode.Truncate;
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
        so.FindProperty("statusText").objectReferenceValue  = statusText;
        so.FindProperty("pathText").objectReferenceValue    = pathText;
        so.FindProperty("pathInput").objectReferenceValue   = pathInput;
        so.FindProperty("startButton").objectReferenceValue = startBtn;
        so.FindProperty("quitButton").objectReferenceValue  = quitBtn;
        so.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = canvasObj;

        Debug.Log("[MainMenuUIBuilder] Built MainMenuCanvas under '" + controller.name +
                  "'. Save the scene (Ctrl+S) to persist.");
    }

    // -----------------------------------------------------------------------
    // Helpers
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

    // Flat white sprite — matches the runtime's Texture2D.whiteTexture look so
    // the editor-built UI is visually identical to what BuildUI produced.
    static Sprite GetUISprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static Image CreateImage(string name, Transform parent)
    {
        GameObject go = NewUIObject(name, parent);
        Image img = Undo.AddComponent<Image>(go);
        img.sprite = GetUISprite();
        img.type   = Image.Type.Simple;
        return img;
    }

    static Text CreateText(string name, Transform parent, string value, int size,
        TextAnchor alignment, FontStyle style)
    {
        GameObject go = NewUIObject(name, parent);
        Text t = Undo.AddComponent<Text>(go);
        t.text      = value;
        t.font      = GetDefaultFont();
        t.fontSize  = size;
        t.fontStyle = style;
        t.alignment = alignment;
        t.color     = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        return t;
    }

    static Button CreateButton(string name, Transform parent, string label, Color tint)
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

        Text labelText = CreateText("Label", go.transform, label, 44,
            TextAnchor.MiddleCenter, FontStyle.Bold);
        StretchToParent(labelText.rectTransform);
        return btn;
    }

    static InputField CreateInputField(string name, Transform parent, string defaultText)
    {
        GameObject go = NewUIObject(name, parent);

        Image bg = Undo.AddComponent<Image>(go);
        bg.sprite = GetUISprite();
        bg.type   = Image.Type.Simple;
        bg.color  = new Color(0.15f, 0.17f, 0.21f, 1f);

        InputField input = Undo.AddComponent<InputField>(go);

        Text text = CreateText("Text", go.transform, "", 26,
            TextAnchor.MiddleLeft, FontStyle.Normal);
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow   = VerticalWrapMode.Truncate;
        RectTransform textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(16, 6);
        textRT.offsetMax = new Vector2(-16, -6);

        Text placeholder = CreateText("Placeholder", go.transform,
            "e.g. Python/output", 26, TextAnchor.MiddleLeft, FontStyle.Italic);
        placeholder.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        RectTransform phRT = placeholder.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(16, 6);
        phRT.offsetMax = new Vector2(-16, -6);

        input.textComponent = text;
        input.placeholder   = placeholder;
        input.targetGraphic = bg;
        input.text          = defaultText;

        return input;
    }
}

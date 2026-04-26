using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Editor utility that materialises the visuals editor panel as scene
/// GameObjects under a <see cref="VisualsMenuController"/>. Wires every
/// serialized reference, including the per-emotion slot array.
///
/// Run via: Tools -> AutoAvatarGen -> Build Visuals Menu UI
///
/// Layout: two columns. Left = one row per emotion (path input + Load + preview).
/// Right = card style controls (background hex, corner-radius slider, text hex,
/// font-style toggles) plus a live card preview.
/// </summary>
public static class VisualsMenuUIBuilder
{
    [MenuItem("Tools/AutoAvatarGen/Build Visuals Menu UI")]
    static void Build()
    {
        VisualsMenuController controller = Object.FindFirstObjectByType<VisualsMenuController>();
        GameObject rootGO;

        if (controller == null)
        {
            rootGO = new GameObject("VisualsMenu");
            Undo.RegisterCreatedObjectUndo(rootGO, "Create VisualsMenu root");
            controller = Undo.AddComponent<VisualsMenuController>(rootGO);
        }
        else
        {
            rootGO = controller.gameObject;
        }

        Transform existing = rootGO.transform.Find("VisualsCanvas");
        if (existing != null)
        {
            bool ok = EditorUtility.DisplayDialog(
                "Visuals Menu UI Builder",
                "A VisualsCanvas already exists under '" + rootGO.name +
                "'.\n\nDelete it and rebuild from scratch?",
                "Rebuild", "Cancel");
            if (!ok) return;
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        Undo.SetCurrentGroupName("Build Visuals Menu UI");
        int undoGroup = Undo.GetCurrentGroup();

        // ---------- Canvas ----------
        GameObject canvasObj = NewUIObject("VisualsCanvas", rootGO.transform);
        Canvas canvas = Undo.AddComponent<Canvas>(canvasObj);
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(canvasObj);
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        Undo.AddComponent<GraphicRaycaster>(canvasObj);

        // ---------- Backdrop ----------
        Image backdrop = CreateImage("Backdrop", canvasObj.transform);
        backdrop.color = new Color(0f, 0f, 0f, 0.65f);
        StretchToParent(backdrop.rectTransform);

        // ---------- Panel ----------
        Image panel = CreateImage("Panel", canvasObj.transform);
        panel.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        SetRect(panel.rectTransform, AC, AC, AC, Vector2.zero, new Vector2(1900, 1170));
        Transform p = panel.transform;

        // ---------- Title ----------
        Text title = CreateText("Title", p, "Customize Visuals",
            56, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform, AC, AC, AC, new Vector2(0, 515), new Vector2(1800, 70));

        // ---------- Active save label (just below title) ----------
        Text activeSaveLabel = CreateText("ActiveSaveLabel", p, "Editing:  Untitled",
            24, TextAnchor.MiddleCenter, FontStyle.Italic);
        activeSaveLabel.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        SetRect(activeSaveLabel.rectTransform, AC, AC, AC,
            new Vector2(0, 458), new Vector2(1800, 30));

        // =======================================================
        // LEFT COLUMN — Presenter character
        // =======================================================
        const float leftCenterX = -460f;

        Text presenterTitle = CreateText("PresenterTitle", p, "Presenter Character",
            34, TextAnchor.MiddleLeft, FontStyle.Bold);
        presenterTitle.color = new Color(0.82f, 0.85f, 0.90f, 1f);
        SetRect(presenterTitle.rectTransform, AC, AC, AC,
            new Vector2(leftCenterX, 400), new Vector2(900, 40));

        Text presenterHint = CreateText("PresenterHint", p,
            "Paste a path or click Load (file picker is editor-only). PNG/JPG/BMP/TGA.",
            20, TextAnchor.MiddleLeft, FontStyle.Italic);
        presenterHint.color = new Color(0.6f, 0.65f, 0.72f, 1f);
        SetRect(presenterHint.rectTransform, AC, AC, AC,
            new Vector2(leftCenterX, 365), new Vector2(900, 26));

        // 5 emotion rows, ~110 tall, stacked.
        var slotRefs = new (string emotion, Image preview, InputField input, Button load)[VisualsMenuController.Emotions.Length];
        float slotHeight  = 105f;
        float topRowY     = 290f; // y of the first slot's center
        for (int i = 0; i < VisualsMenuController.Emotions.Length; i++)
        {
            string emotion = VisualsMenuController.Emotions[i];
            float y = topRowY - i * slotHeight;
            slotRefs[i] = CreateEmotionSlot(emotion, p, new Vector2(leftCenterX, y));
        }

        // =======================================================
        // RIGHT COLUMN — Card style
        // =======================================================
        const float rightCenterX = 460f;

        Text cardTitle = CreateText("CardTitle", p, "Card Style",
            34, TextAnchor.MiddleLeft, FontStyle.Bold);
        cardTitle.color = new Color(0.82f, 0.85f, 0.90f, 1f);
        SetRect(cardTitle.rectTransform, AC, AC, AC,
            new Vector2(rightCenterX, 400), new Vector2(900, 40));

        // -- Background color row --
        var bgRow = CreateColorRow("BgColor", p, "Background Color", "#FAF3E0",
            new Vector2(rightCenterX, 320));

        // -- Corner radius row --
        var cornerRow = CreateSliderRow("CornerRadius", p, "Corner Radius",
            0f, 32f, 18f, new Vector2(rightCenterX, 235));

        // -- Text color row --
        var txtRow = CreateColorRow("TextColor", p, "Text Color", "#1A1A1F",
            new Vector2(rightCenterX, 150));

        // -- Font style row --
        var fontRow = CreateFontStyleRow(p, new Vector2(rightCenterX, 50));

        // -- Card preview --
        Text previewLabel = CreateText("PreviewLabel", p, "Live Preview",
            22, TextAnchor.MiddleLeft, FontStyle.Italic);
        previewLabel.color = new Color(0.6f, 0.65f, 0.72f, 1f);
        SetRect(previewLabel.rectTransform, AC, AC, AC,
            new Vector2(rightCenterX, -40), new Vector2(900, 30));

        Image cardPreviewBg = CreateImage("CardPreviewBackground", p);
        cardPreviewBg.color = new Color(0.98f, 0.95f, 0.88f, 1f);
        SetRect(cardPreviewBg.rectTransform, AC, AC, AC,
            new Vector2(rightCenterX, -200), new Vector2(800, 280));

        Text cardPreviewText = CreateText("CardPreviewText", cardPreviewBg.transform,
            "Sample card body — your saved values flow into the recording scene's card system.",
            36, TextAnchor.MiddleCenter, FontStyle.Normal);
        cardPreviewText.color = new Color(0.10f, 0.10f, 0.12f, 1f);
        cardPreviewText.horizontalOverflow = HorizontalWrapMode.Wrap;
        cardPreviewText.verticalOverflow   = VerticalWrapMode.Truncate;
        var cptRT = cardPreviewText.rectTransform;
        cptRT.anchorMin = Vector2.zero;
        cptRT.anchorMax = Vector2.one;
        cptRT.offsetMin = new Vector2(40, 30);
        cptRT.offsetMax = new Vector2(-40, -30);

        // =======================================================
        // BOTTOM ROW — Save / Save As / Manage Saves / Close + Toast
        // =======================================================
        Button saveBtn = CreateButton("SaveButton", p, "Save",
            new Color(0.18f, 0.62f, 0.34f));
        SetRect(saveBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-395, -495), new Vector2(250, 80));

        Button saveAsBtn = CreateButton("SaveAsButton", p, "Save As…",
            new Color(0.18f, 0.50f, 0.62f));
        SetRect(saveAsBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-135, -495), new Vector2(250, 80));

        Button manageSavesBtn = CreateButton("ManageSavesButton", p, "Manage Saves…",
            new Color(0.30f, 0.40f, 0.55f));
        SetRect(manageSavesBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(135, -495), new Vector2(250, 80));

        Button closeBtn = CreateButton("CloseButton", p, "Close",
            new Color(0.30f, 0.32f, 0.36f));
        SetRect(closeBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(395, -495), new Vector2(250, 80));

        Text toast = CreateText("Toast", p, "Saved.",
            26, TextAnchor.MiddleCenter, FontStyle.Italic);
        toast.color = new Color(0.35f, 0.85f, 0.45f, 1f);
        SetRect(toast.rectTransform, AC, AC, AC,
            new Vector2(0, -555), new Vector2(1800, 30));
        toast.gameObject.SetActive(false);

        // =======================================================
        // MANAGE SAVES SUB-PANEL (overlay, initially hidden)
        // =======================================================
        var savesPanel = BuildSavesPanel(canvasObj.transform);

        // =======================================================
        // SAVE AS PROMPT (overlay, initially hidden)
        // =======================================================
        var saveAsPrompt = BuildSaveAsPrompt(canvasObj.transform);

        // =======================================================
        // OVERWRITE / CONFIRM PROMPT (overlay, initially hidden)
        // =======================================================
        var overwritePrompt = BuildConfirmPrompt(canvasObj.transform);

        // ---------- EventSystem (only if missing) ----------
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            Undo.AddComponent<EventSystem>(es);
            Undo.AddComponent<InputSystemUIInputModule>(es);
        }

        // ---------- Wire serialized references ----------
        var so = new SerializedObject(controller);
        so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;

        // Emotion slots array.
        var slotsProp = so.FindProperty("emotionSlots");
        slotsProp.arraySize = slotRefs.Length;
        for (int i = 0; i < slotRefs.Length; i++)
        {
            var elem = slotsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("emotionName").stringValue       = slotRefs[i].emotion;
            elem.FindPropertyRelative("pathInput").objectReferenceValue = slotRefs[i].input;
            elem.FindPropertyRelative("loadButton").objectReferenceValue = slotRefs[i].load;
            elem.FindPropertyRelative("preview").objectReferenceValue   = slotRefs[i].preview;
        }

        so.FindProperty("bgColorInput").objectReferenceValue          = bgRow.input;
        so.FindProperty("bgColorSwatch").objectReferenceValue         = bgRow.swatch;
        so.FindProperty("cornerRadiusSlider").objectReferenceValue    = cornerRow.slider;
        so.FindProperty("cornerRadiusLabel").objectReferenceValue     = cornerRow.label;
        so.FindProperty("textColorInput").objectReferenceValue        = txtRow.input;
        so.FindProperty("textColorSwatch").objectReferenceValue       = txtRow.swatch;
        so.FindProperty("fontNormalButton").objectReferenceValue      = fontRow.normalBtn;
        so.FindProperty("fontBoldButton").objectReferenceValue        = fontRow.boldBtn;
        so.FindProperty("fontItalicButton").objectReferenceValue      = fontRow.italicBtn;
        so.FindProperty("fontBoldItalicButton").objectReferenceValue  = fontRow.boldItalicBtn;
        so.FindProperty("cardPreviewBackground").objectReferenceValue = cardPreviewBg;
        so.FindProperty("cardPreviewText").objectReferenceValue       = cardPreviewText;

        so.FindProperty("activeSaveLabel").objectReferenceValue       = activeSaveLabel;

        so.FindProperty("saveButton").objectReferenceValue            = saveBtn;
        so.FindProperty("saveAsButton").objectReferenceValue          = saveAsBtn;
        so.FindProperty("manageSavesButton").objectReferenceValue     = manageSavesBtn;
        so.FindProperty("closeButton").objectReferenceValue           = closeBtn;
        so.FindProperty("toastText").objectReferenceValue             = toast;

        so.FindProperty("savesPanelRoot").objectReferenceValue        = savesPanel.root;
        so.FindProperty("savesListContent").objectReferenceValue      = savesPanel.listContent;
        so.FindProperty("savesEmptyLabel").objectReferenceValue       = savesPanel.emptyLabel;
        so.FindProperty("importButton").objectReferenceValue          = savesPanel.importBtn;
        so.FindProperty("exportSelectedButton").objectReferenceValue  = savesPanel.exportBtn;
        so.FindProperty("loadSelectedButton").objectReferenceValue    = savesPanel.loadBtn;
        so.FindProperty("deleteSelectedButton").objectReferenceValue  = savesPanel.deleteBtn;
        so.FindProperty("savesPanelCloseButton").objectReferenceValue = savesPanel.closeBtn;

        so.FindProperty("saveAsPromptRoot").objectReferenceValue      = saveAsPrompt.root;
        so.FindProperty("saveAsNameInput").objectReferenceValue       = saveAsPrompt.input;
        so.FindProperty("saveAsConfirmButton").objectReferenceValue   = saveAsPrompt.confirmBtn;
        so.FindProperty("saveAsCancelButton").objectReferenceValue    = saveAsPrompt.cancelBtn;

        so.FindProperty("overwriteConfirmRoot").objectReferenceValue    = overwritePrompt.root;
        so.FindProperty("overwriteConfirmMessage").objectReferenceValue = overwritePrompt.message;
        so.FindProperty("overwriteConfirmButton").objectReferenceValue  = overwritePrompt.confirmBtn;
        so.FindProperty("overwriteCancelButton").objectReferenceValue   = overwritePrompt.cancelBtn;

        so.ApplyModifiedProperties();

        // Hide the overlays now that they're wired.
        savesPanel.root.SetActive(false);
        saveAsPrompt.root.SetActive(false);
        overwritePrompt.root.SetActive(false);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(rootGO.scene);
        Selection.activeGameObject = canvasObj;

        Debug.Log("[VisualsMenuUIBuilder] Built VisualsCanvas under '" + rootGO.name +
                  "'. Save the scene (Ctrl+S) to persist.");
    }

    // =======================================================================
    // Composite row builders
    // =======================================================================

    /// <summary>Creates a [Preview][Label][PathInput][Load] row for one emotion.</summary>
    static (string emotion, Image preview, InputField input, Button load) CreateEmotionSlot(
        string emotion, Transform parent, Vector2 anchoredPos)
    {
        // Container — does nothing visually but groups the row's children.
        GameObject row = NewUIObject(emotion + "Slot", parent);
        SetRect(row.GetComponent<RectTransform>(), AC, AC, AC, anchoredPos, new Vector2(900, 100));
        Transform r = row.transform;

        Image preview = CreateImage("Preview", r);
        preview.color = new Color(1f, 1f, 1f, 0.10f);
        preview.preserveAspect = true;
        SetRect(preview.rectTransform, AC, AC, AC, new Vector2(-410, 0), new Vector2(80, 80));

        Text label = CreateText("Label", r, emotion, 26, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(label.rectTransform, AC, AC, AC, new Vector2(-265, 28), new Vector2(220, 30));

        InputField input = CreateInputField("PathInput", r,
            "C:\\path\\to\\" + emotion.ToLower() + ".png");
        SetRect(input.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-15, -16), new Vector2(660, 44));

        Button load = CreateButton("LoadButton", r, "Load",
            new Color(0.20f, 0.45f, 0.65f));
        SetRect(load.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(380, -16), new Vector2(100, 44));

        return (emotion, preview, input, load);
    }

    /// <summary>Creates a [Label][HexInput][Swatch] row.</summary>
    static (InputField input, Image swatch) CreateColorRow(
        string name, Transform parent, string labelText, string defaultHex, Vector2 anchoredPos)
    {
        GameObject row = NewUIObject(name + "Row", parent);
        SetRect(row.GetComponent<RectTransform>(), AC, AC, AC, anchoredPos, new Vector2(900, 70));
        Transform r = row.transform;

        Text label = CreateText("Label", r, labelText, 26, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(label.rectTransform, AC, AC, AC, new Vector2(-280, 0), new Vector2(280, 40));

        InputField input = CreateInputField("Hex", r, "#FFFFFF");
        input.text = defaultHex;
        input.characterLimit = 9; // '#' + 8 hex digits
        SetRect(input.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(50, 0), new Vector2(280, 44));

        Image swatch = CreateImage("Swatch", r);
        if (ColorUtility.TryParseHtmlString(defaultHex, out Color c)) swatch.color = c;
        SetRect(swatch.rectTransform, AC, AC, AC,
            new Vector2(310, 0), new Vector2(100, 44));

        return (input, swatch);
    }

    /// <summary>Creates a [Label][Slider][ValueLabel] row.</summary>
    static (Slider slider, Text label) CreateSliderRow(
        string name, Transform parent, string labelText, float min, float max, float value,
        Vector2 anchoredPos)
    {
        GameObject row = NewUIObject(name + "Row", parent);
        SetRect(row.GetComponent<RectTransform>(), AC, AC, AC, anchoredPos, new Vector2(900, 70));
        Transform r = row.transform;

        Text label = CreateText("Label", r, labelText, 26, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(label.rectTransform, AC, AC, AC, new Vector2(-280, 0), new Vector2(280, 40));

        Slider slider = CreateSlider("Slider", r, min, max, value);
        SetRect(slider.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(60, 0), new Vector2(330, 30));

        Text valueLabel = CreateText("Value", r,
            Mathf.RoundToInt(value) + " px", 26, TextAnchor.MiddleLeft, FontStyle.Normal);
        valueLabel.color = new Color(0.82f, 0.85f, 0.90f, 1f);
        SetRect(valueLabel.rectTransform, AC, AC, AC,
            new Vector2(290, 0), new Vector2(120, 40));

        return (slider, valueLabel);
    }

    /// <summary>Creates a [Label][Normal][Bold][Italic][Bold+Italic] toggle row.</summary>
    static (Button normalBtn, Button boldBtn, Button italicBtn, Button boldItalicBtn) CreateFontStyleRow(
        Transform parent, Vector2 anchoredPos)
    {
        GameObject row = NewUIObject("FontStyleRow", parent);
        SetRect(row.GetComponent<RectTransform>(), AC, AC, AC, anchoredPos, new Vector2(900, 80));
        Transform r = row.transform;

        Text label = CreateText("Label", r, "Font Style", 26, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(label.rectTransform, AC, AC, AC, new Vector2(-280, 0), new Vector2(280, 40));

        Color inactive = new Color(0.30f, 0.32f, 0.36f, 1f);

        Button normalBtn = CreateButton("Normal", r, "Normal", inactive);
        SetRect(normalBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-50, 0), new Vector2(140, 50));

        Button boldBtn = CreateButton("Bold", r, "Bold", inactive);
        ((Text)boldBtn.GetComponentInChildren<Text>()).fontStyle = FontStyle.Bold;
        SetRect(boldBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(95, 0), new Vector2(140, 50));

        Button italicBtn = CreateButton("Italic", r, "Italic", inactive);
        ((Text)italicBtn.GetComponentInChildren<Text>()).fontStyle = FontStyle.Italic;
        SetRect(italicBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(240, 0), new Vector2(140, 50));

        Button boldItalicBtn = CreateButton("BoldItalic", r, "Bold+Italic", inactive);
        ((Text)boldItalicBtn.GetComponentInChildren<Text>()).fontStyle = FontStyle.BoldAndItalic;
        SetRect(boldItalicBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(385, 0), new Vector2(160, 50));

        return (normalBtn, boldBtn, italicBtn, boldItalicBtn);
    }

    // =======================================================================
    // Primitive helpers
    // =======================================================================

    static readonly Vector2 AC = new Vector2(0.5f, 0.5f); // anchor center

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

    static Font GetDefaultFont()
        => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

        Text labelText = CreateText("Label", go.transform, label, 28,
            TextAnchor.MiddleCenter, FontStyle.Bold);
        StretchToParent(labelText.rectTransform);
        return btn;
    }

    static InputField CreateInputField(string name, Transform parent, string placeholderHint)
    {
        GameObject go = NewUIObject(name, parent);

        Image bg = Undo.AddComponent<Image>(go);
        bg.sprite = GetUISprite();
        bg.type   = Image.Type.Simple;
        bg.color  = new Color(0.15f, 0.17f, 0.21f, 1f);

        InputField input = Undo.AddComponent<InputField>(go);

        Text text = CreateText("Text", go.transform, "", 22,
            TextAnchor.MiddleLeft, FontStyle.Normal);
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow   = VerticalWrapMode.Truncate;
        var textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(14, 4);
        textRT.offsetMax = new Vector2(-14, -4);

        Text placeholder = CreateText("Placeholder", go.transform, placeholderHint,
            22, TextAnchor.MiddleLeft, FontStyle.Italic);
        placeholder.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        var phRT = placeholder.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(14, 4);
        phRT.offsetMax = new Vector2(-14, -4);

        input.textComponent = text;
        input.placeholder   = placeholder;
        input.targetGraphic = bg;
        input.text          = "";
        input.characterLimit = 0;

        return input;
    }

    /// <summary>Creates a horizontal Slider with the standard Background / Fill / Handle hierarchy.</summary>
    static Slider CreateSlider(string name, Transform parent, float min, float max, float value)
    {
        GameObject sliderObj = NewUIObject(name, parent);

        Image background = Undo.AddComponent<Image>(sliderObj);
        background.sprite = GetUISprite();
        background.color  = new Color(0.18f, 0.20f, 0.25f, 1f);

        Slider slider = Undo.AddComponent<Slider>(sliderObj);
        slider.minValue  = min;
        slider.maxValue  = max;
        slider.direction = Slider.Direction.LeftToRight;

        // Fill area + Fill
        GameObject fillArea = NewUIObject("Fill Area", sliderObj.transform);
        var fillAreaRT = fillArea.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0f);
        fillAreaRT.anchorMax = new Vector2(1f, 1f);
        fillAreaRT.offsetMin = new Vector2(10, 6);
        fillAreaRT.offsetMax = new Vector2(-10, -6);

        GameObject fill = NewUIObject("Fill", fillArea.transform);
        Image fillImg = Undo.AddComponent<Image>(fill);
        fillImg.sprite = GetUISprite();
        fillImg.color  = new Color(0.20f, 0.55f, 0.85f, 1f);
        var fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Handle slide area + Handle
        GameObject handleArea = NewUIObject("Handle Slide Area", sliderObj.transform);
        var handleAreaRT = handleArea.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(10, 0);
        handleAreaRT.offsetMax = new Vector2(-10, 0);

        GameObject handle = NewUIObject("Handle", handleArea.transform);
        Image handleImg = Undo.AddComponent<Image>(handle);
        handleImg.sprite = GetUISprite();
        handleImg.color  = Color.white;
        var handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0f);
        handleRT.anchorMax = new Vector2(0f, 1f);
        handleRT.sizeDelta = new Vector2(22f, 0f);

        slider.fillRect       = fillRT;
        slider.handleRect     = handleRT;
        slider.targetGraphic  = handleImg;
        slider.value          = value;

        return slider;
    }

    // =======================================================================
    // Modal sub-panel builders
    // =======================================================================

    struct SavesPanelRefs
    {
        public GameObject root;
        public RectTransform listContent;
        public Text emptyLabel;
        public Button importBtn, exportBtn, loadBtn, deleteBtn, closeBtn;
    }

    static SavesPanelRefs BuildSavesPanel(Transform canvasParent)
    {
        SavesPanelRefs r = default;

        r.root = NewUIObject("SavesPanel", canvasParent);
        var rootRT = r.root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Backdrop
        Image backdrop = CreateImage("Backdrop", r.root.transform);
        backdrop.color = new Color(0f, 0f, 0f, 0.65f);
        StretchToParent(backdrop.rectTransform);

        // Frame
        Image frame = CreateImage("Frame", r.root.transform);
        frame.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        SetRect(frame.rectTransform, AC, AC, AC, Vector2.zero, new Vector2(820, 700));
        Transform f = frame.transform;

        Text title = CreateText("Title", f, "Manage Saves",
            36, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform, AC, AC, AC, new Vector2(0, 305), new Vector2(800, 50));

        // Top row: Import / Export
        r.importBtn = CreateButton("ImportButton", f, "Import…",
            new Color(0.20f, 0.45f, 0.65f));
        SetRect(r.importBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-180, 240), new Vector2(220, 50));

        r.exportBtn = CreateButton("ExportSelectedButton", f, "Export Selected…",
            new Color(0.18f, 0.50f, 0.62f));
        SetRect(r.exportBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(140, 240), new Vector2(280, 50));

        // Scroll list
        var content = CreateScrollView("SavesScrollView", f,
            new Vector2(0, 5), new Vector2(760, 360));
        r.listContent = content;

        // Empty-state label centered over the list area.
        r.emptyLabel = CreateText("EmptyLabel", f,
            "No saves yet. Click \"Save As…\" on the editor to create one.",
            22, TextAnchor.MiddleCenter, FontStyle.Italic);
        r.emptyLabel.color = new Color(0.55f, 0.58f, 0.64f, 1f);
        SetRect(r.emptyLabel.rectTransform, AC, AC, AC,
            new Vector2(0, 5), new Vector2(720, 60));

        // Bottom row: Load / Delete
        r.loadBtn = CreateButton("LoadSelectedButton", f, "Load",
            new Color(0.18f, 0.62f, 0.34f));
        SetRect(r.loadBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-150, -240), new Vector2(240, 60));

        r.deleteBtn = CreateButton("DeleteSelectedButton", f, "Delete",
            new Color(0.62f, 0.22f, 0.22f));
        SetRect(r.deleteBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(150, -240), new Vector2(240, 60));

        r.closeBtn = CreateButton("CloseButton", f, "Close",
            new Color(0.30f, 0.32f, 0.36f));
        SetRect(r.closeBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(0, -310), new Vector2(220, 50));

        return r;
    }

    struct SaveAsPromptRefs
    {
        public GameObject root;
        public InputField input;
        public Button confirmBtn, cancelBtn;
    }

    static SaveAsPromptRefs BuildSaveAsPrompt(Transform canvasParent)
    {
        SaveAsPromptRefs r = default;

        r.root = NewUIObject("SaveAsPrompt", canvasParent);
        var rootRT = r.root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        Image backdrop = CreateImage("Backdrop", r.root.transform);
        backdrop.color = new Color(0f, 0f, 0f, 0.70f);
        StretchToParent(backdrop.rectTransform);

        Image frame = CreateImage("Frame", r.root.transform);
        frame.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        SetRect(frame.rectTransform, AC, AC, AC, Vector2.zero, new Vector2(620, 360));
        Transform f = frame.transform;

        Text title = CreateText("Title", f, "Save As",
            36, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform, AC, AC, AC, new Vector2(0, 130), new Vector2(580, 50));

        Text label = CreateText("Label", f, "Name:",
            24, TextAnchor.MiddleLeft, FontStyle.Normal);
        label.color = new Color(0.82f, 0.85f, 0.90f, 1f);
        SetRect(label.rectTransform, AC, AC, AC, new Vector2(-220, 50), new Vector2(160, 30));

        r.input = CreateInputField("NameInput", f, "MyVisuals");
        SetRect(r.input.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(40, 50), new Vector2(420, 50));

        r.cancelBtn = CreateButton("CancelButton", f, "Cancel",
            new Color(0.30f, 0.32f, 0.36f));
        SetRect(r.cancelBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-120, -110), new Vector2(220, 60));

        r.confirmBtn = CreateButton("ConfirmButton", f, "Save",
            new Color(0.18f, 0.62f, 0.34f));
        SetRect(r.confirmBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(120, -110), new Vector2(220, 60));

        return r;
    }

    struct ConfirmPromptRefs
    {
        public GameObject root;
        public Text message;
        public Button confirmBtn, cancelBtn;
    }

    static ConfirmPromptRefs BuildConfirmPrompt(Transform canvasParent)
    {
        ConfirmPromptRefs r = default;

        r.root = NewUIObject("OverwritePrompt", canvasParent);
        var rootRT = r.root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        Image backdrop = CreateImage("Backdrop", r.root.transform);
        backdrop.color = new Color(0f, 0f, 0f, 0.75f);
        StretchToParent(backdrop.rectTransform);

        Image frame = CreateImage("Frame", r.root.transform);
        frame.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        SetRect(frame.rectTransform, AC, AC, AC, Vector2.zero, new Vector2(620, 320));
        Transform f = frame.transform;

        Text title = CreateText("Title", f, "Confirm",
            34, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform, AC, AC, AC, new Vector2(0, 110), new Vector2(580, 40));

        r.message = CreateText("Message", f, "(message)",
            22, TextAnchor.MiddleCenter, FontStyle.Normal);
        r.message.color = new Color(0.82f, 0.85f, 0.90f, 1f);
        r.message.horizontalOverflow = HorizontalWrapMode.Wrap;
        r.message.verticalOverflow   = VerticalWrapMode.Truncate;
        SetRect(r.message.rectTransform, AC, AC, AC,
            new Vector2(0, 20), new Vector2(540, 90));

        r.cancelBtn = CreateButton("CancelButton", f, "Cancel",
            new Color(0.30f, 0.32f, 0.36f));
        SetRect(r.cancelBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(-120, -100), new Vector2(220, 60));

        r.confirmBtn = CreateButton("ConfirmButton", f, "Yes",
            new Color(0.62f, 0.22f, 0.22f));
        SetRect(r.confirmBtn.GetComponent<RectTransform>(), AC, AC, AC,
            new Vector2(120, -100), new Vector2(220, 60));

        return r;
    }

    // =======================================================================
    // ScrollView builder — returns the Content RectTransform that the
    // controller appends row GameObjects to at runtime.
    // =======================================================================

    static RectTransform CreateScrollView(string name, Transform parent,
        Vector2 anchoredPos, Vector2 size)
    {
        GameObject scrollObj = NewUIObject(name, parent);
        SetRect(scrollObj.GetComponent<RectTransform>(), AC, AC, AC, anchoredPos, size);

        Image bg = Undo.AddComponent<Image>(scrollObj);
        bg.sprite = GetUISprite();
        bg.color  = new Color(0.06f, 0.07f, 0.10f, 1f);

        ScrollRect sr = Undo.AddComponent<ScrollRect>(scrollObj);

        // Viewport
        GameObject viewport = NewUIObject("Viewport", scrollObj.transform);
        var vpRT = viewport.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(4, 4);
        vpRT.offsetMax = new Vector2(-4, -4);
        Image vpImg = Undo.AddComponent<Image>(viewport);
        vpImg.color = new Color(1f, 1f, 1f, 0.01f); // raycast target, nearly invisible
        var mask = Undo.AddComponent<Mask>(viewport);
        mask.showMaskGraphic = false;

        // Content
        GameObject content = NewUIObject("Content", viewport.transform);
        var cRT = content.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1);
        cRT.anchorMax = new Vector2(1, 1);
        cRT.pivot     = new Vector2(0.5f, 1);
        cRT.sizeDelta = Vector2.zero;
        var vlg = Undo.AddComponent<VerticalLayoutGroup>(content);
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlHeight     = false;
        vlg.childControlWidth      = true;
        vlg.spacing                = 4f;
        vlg.padding                = new RectOffset(8, 8, 8, 8);
        var csf = Undo.AddComponent<ContentSizeFitter>(content);
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport          = vpRT;
        sr.content           = cRT;
        sr.horizontal        = false;
        sr.vertical          = true;
        sr.movementType      = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;

        return cRT;
    }
}

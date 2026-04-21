using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Drives the main menu scene: Start Recording (launches the recording scene
/// via RecordingSession.Begin) and Quit. On return from a recording, reads
/// RecordingSession.LastResult and shows success / failure plus the saved
/// file path.
///
/// Also exposes a text field for the Python pre-processor output folder.
/// ScriptFileReader.Start() reads the same PlayerPrefs key and overrides its
/// own <c>pythonOutputFolder</c> so the user can point the pipeline at a
/// different output directory without editing the SampleScene.
///
/// All UI is built programmatically at Awake so the scene YAML stays minimal.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // Shared with ScriptFileReader. If you rename this, rename it there too.
    public const string PythonOutputFolderPrefKey = "AutoAvatarGen.PythonOutputFolder";
    public const string DefaultPythonOutputFolder = "Python/output";

    Text statusText;
    Text pathText;
    InputField pathInput;

    static Sprite cachedSolidSprite;

    void Awake()
    {
        BuildUI();
        RefreshResult();
    }

    void OnEnable()
    {
        RecordingSession.ResultChanged += RefreshResult;
    }

    void OnDisable()
    {
        RecordingSession.ResultChanged -= RefreshResult;
    }

    // -----------------------------------------------------------------------
    // UI construction
    // -----------------------------------------------------------------------

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("MainMenuCanvas", typeof(RectTransform));
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        Image bg = CreateImage("Background", canvasObj.transform);
        bg.color = new Color(0.07f, 0.08f, 0.11f, 1f);
        StretchToParent(bg.rectTransform);

        Text title = CreateText("Title", canvasObj.transform, "AutoAvatarGen",
            90, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetRect(title.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -180), new Vector2(1400, 160));

        Text subtitle = CreateText("Subtitle", canvasObj.transform,
            "The recorder reads the Python pre-processor output (manifest.json / *_timed.txt " +
            "+ *.mp3). Point the field below at that folder, then press Start.",
            26, TextAnchor.MiddleCenter, FontStyle.Normal);
        subtitle.color = new Color(0.72f, 0.76f, 0.82f, 1f);
        SetRect(subtitle.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -300), new Vector2(1500, 80));

        Text pathLabel = CreateText("PathLabel", canvasObj.transform,
            "Python output folder (relative to Assets/):",
            24, TextAnchor.MiddleCenter, FontStyle.Normal);
        pathLabel.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        SetRect(pathLabel.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 155), new Vector2(1100, 32));

        pathInput = CreateInputField("PathInput", canvasObj.transform, DefaultPythonOutputFolder);
        SetRect(pathInput.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 100), new Vector2(1100, 56));
        pathInput.text = PlayerPrefs.GetString(PythonOutputFolderPrefKey, DefaultPythonOutputFolder);
        pathInput.onEndEdit.AddListener(OnPathChanged);

        Button startBtn = CreateButton("StartButton", canvasObj.transform,
            "Start Recording", new Color(0.18f, 0.62f, 0.34f));
        SetRect(startBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 10), new Vector2(560, 110));
        startBtn.onClick.AddListener(OnStartClicked);

        Button quitBtn = CreateButton("QuitButton", canvasObj.transform,
            "Quit", new Color(0.58f, 0.17f, 0.17f));
        SetRect(quitBtn.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -130), new Vector2(560, 110));
        quitBtn.onClick.AddListener(OnQuitClicked);

        Image resultPanel = CreateImage("ResultPanel", canvasObj.transform);
        resultPanel.color = new Color(1f, 1f, 1f, 0.06f);
        SetRect(resultPanel.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 160), new Vector2(1500, 200));

        statusText = CreateText("Status", resultPanel.transform, "", 42,
            TextAnchor.UpperCenter, FontStyle.Bold);
        SetRect(statusText.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -25), new Vector2(-40, 60));

        pathText = CreateText("Path", resultPanel.transform, "", 24,
            TextAnchor.MiddleCenter, FontStyle.Normal);
        pathText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        pathText.horizontalOverflow = HorizontalWrapMode.Wrap;
        pathText.verticalOverflow   = VerticalWrapMode.Truncate;
        SetRect(pathText.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -15), new Vector2(-80, -80));

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }
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
                statusText.text  = "\u25CF  Recording complete";
                statusText.color = new Color(0.98f, 0.80f, 0.30f, 1f);
                pathText.text    = "Generating video\u2026 Evereal is finalising the file, " +
                                   "this usually takes a few seconds.";
                break;

            case RecordingSession.RecordingResult.Status.Saved:
                statusText.text  = "\u2713  Video saved";
                statusText.color = new Color(0.35f, 0.85f, 0.45f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.SavePath) ? "(no path returned)" : r.SavePath;
                break;

            case RecordingSession.RecordingResult.Status.Failed:
                statusText.text  = "\u2717  Recording failed";
                statusText.color = new Color(0.95f, 0.35f, 0.35f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.ErrorMessage) ? "(no error details)" : r.ErrorMessage;
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Button callbacks
    // -----------------------------------------------------------------------

    void OnStartClicked()
    {
        // Flush the current path value in case the user typed into the input
        // but didn't click out of it before hitting Start.
        if (pathInput != null) OnPathChanged(pathInput.text);
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
        if (pathInput != null && pathInput.text != trimmed) pathInput.text = trimmed;
    }

    // -----------------------------------------------------------------------
    // UI helpers
    // -----------------------------------------------------------------------

    static void StretchToParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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

    static Image CreateImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = GetSolidSprite();
        img.type   = Image.Type.Simple;
        return img;
    }

    static Text CreateText(string name, Transform parent, string value, int size,
        TextAnchor alignment, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
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

    static Button CreateButton(string name, Transform parent, string label, Color tint)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = GetSolidSprite();
        img.color  = tint;
        Button btn = go.AddComponent<Button>();
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
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.sprite = GetSolidSprite();
        bg.color  = new Color(0.15f, 0.17f, 0.21f, 1f);

        InputField input = go.AddComponent<InputField>();

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

    static Sprite GetSolidSprite()
    {
        if (cachedSolidSprite != null) return cachedSolidSprite;
        Texture2D tex = Texture2D.whiteTexture;
        cachedSolidSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return cachedSolidSprite;
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MugsTech;

/// <summary>
/// Runtime (in-build) main-menu panel that shows the {Timestamp:"..."} chapter
/// markers captured during the last recording and copies them as a ready-to-paste
/// YouTube chapter list.
///
/// This is the BUILD-facing counterpart to the Editor-only Timestamps window
/// (Assets/Editor/TimestampsWindow.cs): an EditorWindow doesn't exist in a player
/// build, so when you record from a standalone build (for best performance) this
/// MonoBehaviour gives you the same view + Copy at runtime.
///
/// Data: <see cref="TimestampMarkerLog.GetForDisplay"/> — the in-memory list if the
/// menu is reached right after a recording in the same process, else the persisted
/// JSON under Application.persistentDataPath (which the build itself wrote). Copy
/// uses <see cref="GUIUtility.systemCopyBuffer"/>, the RUNTIME cross-platform
/// clipboard (works in Windows 11 and Linux builds — not the editor-only
/// EditorGUIUtility.systemCopyBuffer).
///
/// Self-building, mirroring OutputLibraryController: MainMenuController adds this
/// component (see EnsureTimestampsPanel) and it constructs its own button + modal
/// under the menu canvas, so the hand-tweaked MainMenu scene needs no rebuild.
/// </summary>
public class TimestampsPanelController : MonoBehaviour
{
    // Palette matched to MainMenuController's runtime-built rows so it blends in.
    static readonly Color Accent   = new Color(0.20f, 0.45f, 0.65f, 1f);
    static readonly Color Slate    = new Color(0.32f, 0.34f, 0.40f, 1f);
    static readonly Color Window   = new Color(0.13f, 0.15f, 0.18f, 1f);
    static readonly Color Field    = new Color(0.10f, 0.11f, 0.14f, 1f);
    static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
    static readonly Color Muted    = new Color(0.82f, 0.85f, 0.90f, 1f);

    Canvas     canvas;
    GameObject panelRoot;       // the modal — hidden until opened
    TMP_Text   bodyText;        // the M:SS — Label block
    TMP_Text   statusText;      // "Copied!" / saved-path feedback
    TMP_Text   snapButtonLabel; // reflects the snap toggle state
    bool       snapFirstToZero = true;
    string     currentBlock = "";
    Coroutine  statusRoutine;

    void Start()
    {
        canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[Timestamps] No Canvas found in the menu scene — panel not built.");
            return;
        }

        BuildOpenButton();
        BuildPanel();
        SetPanelVisible(false);
    }

    // ----- the always-visible "YouTube Chapters" button (top-right) -----------

    void BuildOpenButton()
    {
        var go = new GameObject("TimestampsButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-40f, -40f);
        rt.sizeDelta        = new Vector2(320f, 56f);
        go.GetComponent<Image>().color = Accent;
        go.GetComponent<Button>().onClick.AddListener(Open);
        MakeStretchLabel(go.transform, "YouTube Chapters", 24f);
    }

    // ----- the modal panel ----------------------------------------------------

    void BuildPanel()
    {
        // Backdrop (full-screen, eats clicks behind the modal).
        panelRoot = new GameObject("TimestampsPanel", typeof(RectTransform), typeof(Image));
        panelRoot.transform.SetParent(canvas.transform, false);
        var prt = (RectTransform)panelRoot.transform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        panelRoot.GetComponent<Image>().color = Backdrop;
        // Keep this panel above the rest of the menu.
        panelRoot.transform.SetAsLastSibling();

        // Centered window.
        var win = new GameObject("Window", typeof(RectTransform), typeof(Image));
        win.transform.SetParent(panelRoot.transform, false);
        var wrt = (RectTransform)win.transform;
        wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
        wrt.sizeDelta = new Vector2(1000f, 760f);
        win.GetComponent<Image>().color = Window;

        // Title.
        var title = MakeText(win.transform, "Title", "YouTube Chapters", 34f, FontStyles.Bold, TextAlignmentOptions.Center);
        var trt = (RectTransform)title.transform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -18f);
        trt.sizeDelta        = new Vector2(-40f, 48f);

        // Close (X) top-right of the window.
        var close = MakeButton(win.transform, "Close", "✕", Slate, Close);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-12f, -12f);
        crt.sizeDelta        = new Vector2(48f, 48f);

        // Scrollable body.
        BuildScrollBody(win.transform);

        // Bottom controls.
        var snapBtn = MakeButton(win.transform, "SnapToggle", "", Slate, ToggleSnap);
        var srt = (RectTransform)snapBtn.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0f);
        srt.anchoredPosition = new Vector2(24f, 22f);
        srt.sizeDelta        = new Vector2(360f, 52f);
        snapButtonLabel = snapBtn.GetComponentInChildren<TMP_Text>();
        UpdateSnapLabel();

        var copy = MakeButton(win.transform, "Copy", "Copy", Accent, OnCopy);
        var cprt = (RectTransform)copy.transform;
        cprt.anchorMin = cprt.anchorMax = cprt.pivot = new Vector2(1f, 0f);
        cprt.anchoredPosition = new Vector2(-24f, 22f);
        cprt.sizeDelta        = new Vector2(200f, 52f);

        var save = MakeButton(win.transform, "Save", "Save .txt", Slate, OnSave);
        var savrt = (RectTransform)save.transform;
        savrt.anchorMin = savrt.anchorMax = savrt.pivot = new Vector2(1f, 0f);
        savrt.anchoredPosition = new Vector2(-236f, 22f);
        savrt.sizeDelta        = new Vector2(180f, 52f);

        // Status line (just above the buttons).
        statusText = MakeText(win.transform, "Status", "", 22f, FontStyles.Bold, TextAlignmentOptions.Center);
        var strt = (RectTransform)statusText.transform;
        strt.anchorMin = new Vector2(0f, 0f); strt.anchorMax = new Vector2(1f, 0f); strt.pivot = new Vector2(0.5f, 0f);
        strt.anchoredPosition = new Vector2(0f, 84f);
        strt.sizeDelta        = new Vector2(-48f, 28f);
    }

    void BuildScrollBody(Transform window)
    {
        var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(window, false);
        var srt = (RectTransform)scrollGO.transform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(24f, 92f);    // leave room for the bottom controls
        srt.offsetMax = new Vector2(-24f, -78f);  // leave room for the title
        scrollGO.GetComponent<Image>().color = Field;
        var scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical   = true;
        scroll.scrollSensitivity = 24f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGO.transform, false);
        var vrt = (RectTransform)viewport.transform;
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(12f, 12f); vrt.offsetMax = new Vector2(-12f, -12f);
        scroll.viewport = vrt;

        var content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = (RectTransform)content.transform;
        contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero; contentRT.offsetMax = Vector2.zero;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRT;

        bodyText = content.AddComponent<TextMeshProUGUI>();
        bodyText.fontSize  = 30f;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.color     = Color.white;
        bodyText.richText  = false;
    }

    // ----- open / close / refresh --------------------------------------------

    void Open()
    {
        Refresh();
        SetPanelVisible(true);
        panelRoot.transform.SetAsLastSibling();
    }

    void Close() => SetPanelVisible(false);

    void SetPanelVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    void Refresh()
    {
        var markers = TimestampMarkerLog.GetForDisplay();
        if (markers.Count == 0)
        {
            currentBlock = "";
            bodyText.text =
                "No timestamps captured yet.\n\n" +
                "Record a video whose script has {Timestamp:\"...\"} tags, then reopen this panel.";
            bodyText.color = Muted;
            SetStatus("");
            return;
        }

        bodyText.color = Color.white;
        currentBlock   = TimestampMarkerLog.BuildYouTubeBlock(markers, snapFirstToZero);

        // Note the real first time if snap changed it (never silently forced).
        string note = "";
        if (snapFirstToZero && Mathf.RoundToInt(markers[0].Seconds) != 0)
            note = $"\n\n(first marker's real time is {TimestampMarkerLog.FormatTime(markers[0].Seconds)}, " +
                   "shown as 0:00 — toggle off to see the real value)";

        bodyText.text = currentBlock + note;
        SetStatus($"{markers.Count} chapter(s)");
    }

    void ToggleSnap()
    {
        snapFirstToZero = !snapFirstToZero;
        UpdateSnapLabel();
        Refresh();
    }

    void UpdateSnapLabel()
    {
        if (snapButtonLabel != null)
            snapButtonLabel.text = "First = 0:00:  " + (snapFirstToZero ? "ON" : "OFF");
    }

    // ----- copy / save --------------------------------------------------------

    void OnCopy()
    {
        if (string.IsNullOrEmpty(currentBlock)) { SetStatus("Nothing to copy yet."); return; }
        GUIUtility.systemCopyBuffer = currentBlock;   // runtime cross-platform clipboard
        FlashStatus("Copied!", new Color(0.40f, 0.85f, 0.45f));
    }

    void OnSave()
    {
        if (string.IsNullOrEmpty(currentBlock)) { SetStatus("Nothing to save yet."); return; }
        try
        {
            // persistentDataPath is writable in a build on both Windows and Linux.
            string path = Path.Combine(Application.persistentDataPath, "timestamps.txt");
            File.WriteAllText(path, currentBlock + "\n", new System.Text.UTF8Encoding(false));
            FlashStatus("Saved: " + path, new Color(0.40f, 0.85f, 0.45f));
            Debug.Log($"[Timestamps] Wrote {path}");
        }
        catch (System.Exception e)
        {
            FlashStatus("Save failed: " + e.Message, new Color(0.95f, 0.35f, 0.35f));
        }
    }

    void SetStatus(string text)
    {
        if (statusRoutine != null) { StopCoroutine(statusRoutine); statusRoutine = null; }
        if (statusText != null) { statusText.text = text; statusText.color = Muted; }
    }

    void FlashStatus(string text, Color color)
    {
        if (statusText == null) return;
        if (statusRoutine != null) StopCoroutine(statusRoutine);
        statusRoutine = StartCoroutine(FlashRoutine(text, color));
    }

    IEnumerator FlashRoutine(string text, Color color)
    {
        statusText.text  = text;
        statusText.color = color;
        yield return new WaitForSecondsRealtime(2.5f);
        statusText.text  = "";
        statusText.color = Muted;
        statusRoutine = null;
    }

    // ----- small UI builders --------------------------------------------------

    // A TMP label stretched to fill its parent (used inside buttons).
    static TMP_Text MakeStretchLabel(Transform parent, string text, float size)
    {
        var t = MakeText(parent, "Label", text, size, FontStyles.Bold, TextAlignmentOptions.Center);
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return t;
    }

    static TMP_Text MakeText(Transform parent, string name, string content,
                             float size, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content; t.fontSize = size; t.fontStyle = style; t.alignment = align;
        t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    static Button MakeButton(Transform parent, string name, string label, Color tint,
                             UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = tint;
        var btn = go.GetComponent<Button>();
        if (onClick != null) btn.onClick.AddListener(onClick);
        MakeStretchLabel(go.transform, label, 24f);
        return btn;
    }
}

using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Bakes the "output generation" dropdown into MainMenu.unity automatically —
/// no Tools menu, no clicking. Runs on editor load and whenever a scene opens;
/// when it finds a <see cref="MainMenuController"/> whose canvas doesn't yet
/// contain the row, it creates the row as PERMANENT scene GameObjects (so you
/// can restyle them in the Scene view), adds + wires an
/// <see cref="OutputLibraryController"/>, marks the scene dirty, and saves it
/// (on the scene-open path, where the scene is in a clean state).
///
/// Idempotent: once the row exists in the scene it is never rebuilt — the baker
/// only re-checks that the controller's references are still wired.
/// </summary>
[InitializeOnLoad]
public static class OutputLibraryAutoBaker
{
    const string RowName      = "OutputLibraryRow";
    const string DropdownName = "GenerationDropdown";
    const string RefreshName  = "RefreshButton";
    const string PathLabelName = "SelectedPath";

    static OutputLibraryAutoBaker()
    {
        // Handle the scene that's already open when scripts (re)compile, plus
        // any scene opened afterwards. delayCall defers to a point where the
        // scene graph is safe to touch.
        EditorApplication.delayCall  += TryBakeOpenScenes;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode) => TryBake(scene, allowSave: true);

    static void TryBakeOpenScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            TryBake(SceneManager.GetSceneAt(i), allowSave: false);
    }

    static void TryBake(Scene scene, bool allowSave)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        // Find the menu controller anywhere in the scene (including inactive).
        MainMenuController controller = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            controller = root.GetComponentInChildren<MainMenuController>(true);
            if (controller != null) break;
        }
        if (controller == null) return;

        var canvas = controller.GetComponentInChildren<Canvas>(true);
        if (canvas == null) return; // base menu UI not built yet — nothing to attach to

        // Already baked? Just make sure the controller exists and is wired, then bail.
        Transform existingRow = canvas.transform.Find(RowName);
        if (existingRow != null)
        {
            if (EnsureControllerWired(controller, existingRow))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (allowSave) EditorSceneManager.SaveScene(scene);
            }
            return;
        }

        GameObject row = BuildRow(canvas.transform);
        EnsureControllerWired(controller, row.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        if (allowSave) EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = row; // so you can immediately reposition/style it
        Debug.Log($"[OutputLibraryAutoBaker] Added '{RowName}' (generation dropdown) to " +
                  $"'{controller.name}'. {(allowSave ? "Saved the scene." : "Save the scene (Ctrl+S) to persist.")} " +
                  "Drag/restyle it in the Scene view to taste.");
    }

    // Ensure the OutputLibraryController component exists on the menu controller
    // GameObject and its serialized references point at the baked row's children.
    // Returns true if anything was changed (component added or a ref rewired), so
    // callers know whether the scene needs marking dirty.
    static bool EnsureControllerWired(MainMenuController controller, Transform row)
    {
        bool changed = false;

        var lib = controller.GetComponent<OutputLibraryController>();
        if (lib == null)
        {
            lib = controller.gameObject.AddComponent<OutputLibraryController>();
            changed = true;
        }

        var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
        var refresh  = row.Find(RefreshName)?.GetComponent<Button>();
        var pathLbl  = row.Find(PathLabelName)?.GetComponent<TMP_Text>();

        var so = new SerializedObject(lib);
        changed |= SetRef(so, "generationDropdown", dropdown);
        changed |= SetRef(so, "refreshButton",      refresh);
        changed |= SetRef(so, "selectedPathLabel",  pathLbl);
        if (changed) so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    // Writes the reference only if it differs, so an already-wired scene isn't
    // re-dirtied on every recompile. Returns true if it changed the value.
    static bool SetRef(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[OutputLibraryAutoBaker] OutputLibraryController has no " +
                             $"serialized field '{field}'. Did the script fail to compile?");
            return false;
        }
        if (prop.objectReferenceValue == value) return false;
        prop.objectReferenceValue = value;
        return true;
    }

    // -----------------------------------------------------------------------
    // Hierarchy construction (permanent objects; no Undo needed for a one-time
    // bake, but we keep it tidy so the row is easy to restyle by hand).
    // -----------------------------------------------------------------------

    static GameObject BuildRow(Transform canvas)
    {
        var row = NewRect(canvas, RowName);
        var rowRT = (RectTransform)row.transform;
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -420f); // top-ish; reposition in Scene view
        rowRT.sizeDelta        = new Vector2(1500f, 90f);

        // Header
        BuildLabel(row.transform, "Header",
            "Generation for the video  —  used by Start Recording",
            size: new Vector2(1460f, 28f), pos: new Vector2(0f, 26f),
            fontSize: 20, italic: false, align: TextAlignmentOptions.Center);

        // Dropdown via the TMP factory (gives a working template + arrow + list).
        var ddGO = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        ddGO.name = DropdownName;
        ddGO.transform.SetParent(row.transform, false);
        var ddRT = (RectTransform)ddGO.transform;
        ddRT.anchorMin = ddRT.anchorMax = ddRT.pivot = new Vector2(0.5f, 0.5f);
        ddRT.anchoredPosition = new Vector2(-430f, -16f);
        ddRT.sizeDelta        = new Vector2(640f, 48f);
        var ddImg = ddGO.GetComponent<Image>();
        if (ddImg != null) ddImg.color = new Color(0.15f, 0.17f, 0.21f, 1f);
        var dd = ddGO.GetComponent<TMP_Dropdown>();
        if (dd != null && dd.captionText != null) dd.captionText.color = Color.white;

        // Refresh button
        BuildButton(row.transform, RefreshName, "Refresh",
            size: new Vector2(150f, 48f), pos: new Vector2(20f, -16f));

        // Selected-path label
        BuildLabel(row.transform, PathLabelName, "",
            size: new Vector2(520f, 44f), pos: new Vector2(480f, -16f),
            fontSize: 15, italic: true, align: TextAlignmentOptions.MidlineLeft);

        return row;
    }

    static GameObject NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void BuildLabel(Transform parent, string name, string text,
        Vector2 size, Vector2 pos, int fontSize, bool italic, TextAlignmentOptions align)
    {
        var go = NewRect(parent, name);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = TMP_Settings.defaultFontAsset;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
        tmp.text      = text;
        tmp.alignment = align;
        tmp.color     = new Color(0.82f, 0.85f, 0.9f, 1f);
        tmp.raycastTarget = false;
    }

    static void BuildButton(Transform parent, string name, string label,
        Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.65f, 1f);
        go.GetComponent<Button>().targetGraphic = img;

        var lblGO = NewRect(go.transform, "Label");
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
    }
}

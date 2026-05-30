using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MugsTech.Tts;

/// <summary>
/// Gives the TTS panel's multiline script field a clipping viewport + a
/// draggable vertical scrollbar so long scripts scroll inside the box instead
/// of spilling past it.
///
/// The panel is baked into MainMenu.unity, so the fix has to reach an *existing*
/// scene object. This editor hook runs on load + whenever a scene opens, finds
/// the TtsPanelController's <c>scriptInput</c>, and upgrades it in place
/// (permanent scene objects you can restyle in the Scene view), then saves.
///
/// <see cref="EnsureScrollable"/> is also called by RecordingToolsUIBuilder when
/// it builds a fresh panel, so there's a single source of truth for the
/// structure. Idempotent: once the viewport + scrollbar exist, it's a no-op.
/// </summary>
[InitializeOnLoad]
public static class TtsScriptBoxScrollbarBaker
{
    // Visuals — match the panel's dark fields.
    const float ScrollbarWidth = 18f;
    const float ViewportPad    = 8f;

    static TtsScriptBoxScrollbarBaker()
    {
        EditorApplication.delayCall   += BakeOpenScenes;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode) => TryBake(scene, allowSave: true);

    static void BakeOpenScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            TryBake(SceneManager.GetSceneAt(i), allowSave: false);
    }

    static void TryBake(Scene scene, bool allowSave)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        // Find the TTS panel (it's baked inactive, so include inactive objects).
        TtsPanelController panel = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            panel = root.GetComponentInChildren<TtsPanelController>(true);
            if (panel != null) break;
        }
        if (panel == null) return;

        // Read the private [SerializeField] scriptInput reference.
        var so    = new SerializedObject(panel);
        var prop  = so.FindProperty("scriptInput");
        var input = prop != null ? prop.objectReferenceValue as TMP_InputField : null;
        if (input == null) return;

        if (!EnsureScrollable(input)) return; // already upgraded — nothing to do

        EditorUtility.SetDirty(input);
        EditorSceneManager.MarkSceneDirty(scene);
        if (allowSave) EditorSceneManager.SaveScene(scene);

        Debug.Log($"[TtsScriptBoxScrollbarBaker] Added a clipping viewport + vertical " +
                  $"scrollbar to the TTS script box in '{panel.name}'. " +
                  $"{(allowSave ? "Saved the scene." : "Save the scene (Ctrl+S) to persist.")}");
    }

    /// <summary>
    /// Ensures <paramref name="input"/> has a Text Area viewport (RectMask2D) and
    /// a vertical scrollbar wired up. Reparents the existing text/placeholder
    /// under the viewport. Returns true if it changed anything, false if the
    /// field was already set up.
    /// </summary>
    public static bool EnsureScrollable(TMP_InputField input)
    {
        if (input == null) return false;

        bool hasViewport = input.textViewport != null
                           && input.textViewport.GetComponent<RectMask2D>() != null;
        bool hasScrollbar = input.verticalScrollbar != null;
        if (hasViewport && hasScrollbar) return false; // idempotent

        Transform inputTf = input.transform;

        // 1. Text Area viewport with a RectMask2D, inset on the right so it
        //    doesn't sit under the scrollbar.
        RectTransform viewport = hasViewport ? input.textViewport : null;
        if (viewport == null)
        {
            var vpGO = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            vpGO.transform.SetParent(inputTf, false);
            viewport = (RectTransform)vpGO.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(ViewportPad, ViewportPad);
            viewport.offsetMax = new Vector2(-(ScrollbarWidth + ViewportPad), -ViewportPad);
            viewport.SetAsFirstSibling(); // draw under the scrollbar
        }

        // 2. Move the existing text + placeholder under the viewport and fill it.
        if (input.textComponent != null)
        {
            input.textComponent.rectTransform.SetParent(viewport, false);
            Fill(input.textComponent.rectTransform);
        }
        if (input.placeholder != null)
        {
            input.placeholder.rectTransform.SetParent(viewport, false);
            Fill(input.placeholder.rectTransform);
        }
        input.textViewport = viewport;

        // 3. Vertical scrollbar pinned to the right edge (track + sliding area + handle).
        Scrollbar scrollbar = input.verticalScrollbar;
        if (scrollbar == null)
        {
            var sbGO = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGO.transform.SetParent(inputTf, false);
            var sbRT = (RectTransform)sbGO.transform;
            sbRT.anchorMin        = new Vector2(1f, 0f);
            sbRT.anchorMax        = new Vector2(1f, 1f);
            sbRT.pivot            = new Vector2(1f, 1f);
            sbRT.sizeDelta        = new Vector2(ScrollbarWidth, 0f);
            sbRT.anchoredPosition = Vector2.zero;
            sbGO.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 1f); // track

            var slidingGO = new GameObject("Sliding Area", typeof(RectTransform));
            slidingGO.transform.SetParent(sbGO.transform, false);
            var slidingRT = (RectTransform)slidingGO.transform;
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.offsetMin = new Vector2(2f, 2f);
            slidingRT.offsetMax = new Vector2(-2f, -2f);

            var handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGO.transform.SetParent(slidingRT, false);
            var handleRT = (RectTransform)handleGO.transform;
            handleRT.anchorMin = Vector2.zero;
            handleRT.anchorMax = Vector2.one;
            handleRT.offsetMin = Vector2.zero;
            handleRT.offsetMax = Vector2.zero;
            var handleImg = handleGO.GetComponent<Image>();
            handleImg.color = new Color(0.35f, 0.38f, 0.44f, 1f);

            scrollbar = sbGO.GetComponent<Scrollbar>();
            scrollbar.direction     = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect    = handleRT;
            scrollbar.targetGraphic = handleImg;
        }
        input.verticalScrollbar = scrollbar;

        return true;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

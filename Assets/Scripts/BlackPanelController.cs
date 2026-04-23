using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fullscreen black panel triggered by {Black:duration} script markers.
/// Jump-cut in / jump-cut out — no easing, no fade.
///
/// Recording-aware: the CrossPlatformRecorder's Camera source does NOT capture
/// Screen Space - Overlay canvases. This controller therefore parents its panel
/// under an existing non-overlay canvas (preferring a caller-supplied hostCanvas,
/// falling back to any non-overlay Canvas in the scene, and only finally
/// creating its own Overlay canvas). The panel always stretches to fill the
/// canvas so it reads as a fullscreen cut in the recording.
/// </summary>
public class BlackPanelController : MonoBehaviour
{
    [SerializeField]
    private GameObject panelObj;
    private Coroutine activeCoroutine;
    /*
    void Awake()
    {
        EnsurePanel();
    }

    /// <summary>
    /// Wires the host canvas before Awake runs (or rebuilds the panel under a new host).
    /// Called by MediaPresentationSystem during its own Awake so the panel ends up on the
    /// same canvas the recorder captures.
    /// </summary>
    public void SetHostCanvas(Canvas canvas)
    {
        if (canvas == null || canvas == hostCanvas) return;

        hostCanvas = canvas;

        // If we already built under a different parent, rebuild on the new one.
        if (panelObj != null)
        {
            Destroy(panelObj.transform.parent != null
                ? panelObj.transform.parent.gameObject
                : panelObj.gameObject);
            panelObj = null;
        }
        EnsurePanel();
    }

    void EnsurePanel()
    {
        if (panelObj != null) return;

        if (hostCanvas == null)
            hostCanvas = FindNonOverlayCanvas();

        Transform parent;
        if (hostCanvas != null)
        {
            // Host canvas exists — drop a sub-container under it so our panel
            // doesn't disturb the host's own children.
            GameObject wrapper = new GameObject("BlackPanel_Container", typeof(RectTransform), typeof(Canvas));
            wrapper.transform.SetParent(hostCanvas.transform, false);

            RectTransform wrt = wrapper.GetComponent<RectTransform>();
            wrt.anchorMin = Vector2.zero;
            wrt.anchorMax = Vector2.one;
            wrt.offsetMin = Vector2.zero;
            wrt.offsetMax = Vector2.zero;

            // Nested Canvas lets us override sortingOrder so the panel renders on top
            // of sibling UI without having to fiddle with sibling indices.
            Canvas sub = wrapper.GetComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = sortingOrderOverride;

            parent = wrapper.transform;
        }
        else
        {
            // Absolute fallback — no host found. Create an overlay canvas.
            // WARNING: this will NOT appear in camera-source recordings.
            GameObject canvasGO = new GameObject("BlackPanel_FallbackOverlayCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas overlay = canvasGO.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = sortingOrderOverride;
            parent = canvasGO.transform;
            Debug.LogWarning("BlackPanelController: no host canvas found — falling back to " +
                             "Screen Space - Overlay. This will NOT be captured when the recorder is " +
                             "set to Camera source. Assign 'hostCanvas' to the mediaCanvas to fix.");
        }

        GameObject panelGO = new GameObject("BlackPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGO.transform.SetParent(parent, false);

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        panelObj = panelGO.GetComponent<Image>();
        panelObj.color = Color.black;
        panelObj.raycastTarget = false;
        panelGO.SetActive(false);
    }
    */

    // Prefers a Screen Space - Camera or World Space canvas over any Overlay one,
    // because the camera-source recorder only captures what the camera renders.
    static Canvas FindNonOverlayCanvas()
    {
        Canvas[] all = FindObjectsOfType<Canvas>(includeInactive: false);
        Canvas best = null;
        foreach (Canvas c in all)
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) continue;
            if (best == null || c.sortingOrder > best.sortingOrder) best = c;
        }
        return best;
    }

    /// <summary>Show the black panel for <paramref name="duration"/> seconds, then hide. Jump cuts only.</summary>
    public void Show(float duration)
    {
        Debug.Log($"[BlackPanel] Show({duration:F2}s) called. panelObj={(panelObj != null ? panelObj.name : "NULL")}", this);

        if (panelObj == null)
        {
            Debug.LogError("[BlackPanel] 'panelObj' is not assigned on BlackPanelController. " +
                           "Drag a disabled GameObject (child of the captured canvas, stretched fullscreen, black Image) " +
                           "into the 'Panel Obj' slot in the Inspector.", this);
            return;
        }

        if (!isActiveAndEnabled)
        {
            // Coroutines can't run on inactive components — log so the user knows why nothing moved.
            Debug.LogError("[BlackPanel] BlackPanelController is disabled or on an inactive GameObject. " +
                           "Enable the component and its GameObject.", this);
            return;
        }

        if (activeCoroutine != null)
            StopCoroutine(activeCoroutine);

        activeCoroutine = StartCoroutine(ShowRoutine(duration));
    }

    IEnumerator ShowRoutine(float duration)
    {
        panelObj.SetActive(true);
        Debug.Log($"[BlackPanel] shown for {duration:F2}s", this);
        yield return new WaitForSeconds(duration);
        panelObj.SetActive(false);
        Debug.Log("[BlackPanel] hidden", this);
        activeCoroutine = null;
    }

    /// <summary>Force-hide the panel immediately (jump cut).</summary>
    public void HideImmediate()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
        if (panelObj != null)
            panelObj.SetActive(false);
    }
    
}

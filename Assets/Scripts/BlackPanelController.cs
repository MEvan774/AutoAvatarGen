using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fullscreen black cut triggered by {Black:...} script markers. Jump-cuts a
/// solid black panel IN FRONT of everything — character, content cards, and
/// background — then jump-cuts out. No fade. Two forms: the timed
/// {Black:seconds} (Show) holds for a fixed duration, and the held pair
/// {Black:Start}…{Black:End} (ShowHeld/HideImmediate) stays up until the End
/// tag's exact narration time — the same in-tag/out-tag principle as
/// {Video:name}…{Video:End}, for when a guessed duration would drift off the
/// spoken words.
///
/// Renders as a UI Image on the same canvas the content cards use (the one the
/// recorder actually captures), at a sorting order above every card. The older
/// approach — a black SpriteRenderer parented to a camera — didn't survive this
/// scene's multi-camera compositing, so the panel never appeared in the
/// recording. Hosting it on the captured canvas (like the cards) fixes that.
/// </summary>
public class BlackPanelController : MonoBehaviour
{
    [Tooltip("Legacy scene 'BlackPlane' object. No longer used to render — kept " +
             "only so it can be disabled, since the panel is now a UI Image.")]
    [SerializeField] private GameObject panelObj;

    [Tooltip("Sorting order for the black panel. Above the fullscreen feature " +
             "zone (31000) so it covers cards, character, and background.")]
    [SerializeField] private int sortingOrder = 32000;

    [Tooltip("Optional explicit host canvas. If empty, the captured media canvas " +
             "(from MediaPresentationSystem) is used, then any Canvas in the scene.")]
    [SerializeField] private Canvas hostCanvas;

    private Image panelImage;
    private Coroutine activeCoroutine;

    // True while the panel was opened by {Black:Start} (ShowHeld) and hasn't
    // been closed yet — it has no timer; only HideImmediate ({Black:End} or
    // the end-of-narration safety net) takes it down.
    private bool heldOpen;

    /// <summary>True while the black panel is currently shown (timed hold OR held open).</summary>
    public bool IsShowing => panelImage != null && panelImage.gameObject.activeSelf;

    /// <summary>True while the panel is held open by ShowHeld with no timer running.</summary>
    public bool IsHeldOpen => heldOpen && IsShowing;

    void Awake()
    {
        // The panel is a UI Image now; make sure the legacy scene plane (if any)
        // can't show through or linger.
        if (panelObj != null) panelObj.SetActive(false);
    }

    /// <summary>
    /// Sets the canvas the black panel parents to (the recorder-captured one).
    /// Called by MediaPresentationSystem with its mediaCanvas. Rebuilds the panel
    /// under the new host on the next Show().
    /// </summary>
    public void SetHostCanvas(Canvas canvas)
    {
        if (canvas == null || canvas == hostCanvas) return;
        hostCanvas = canvas;
        if (panelImage != null)
        {
            Destroy(panelImage.gameObject);
            panelImage = null;
        }
    }

    /// <summary>Show the black panel for <paramref name="duration"/> seconds, then hide. Jump cuts only.</summary>
    public void Show(float duration)
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogError("[BlackPanel] Controller is disabled or on an inactive GameObject. " +
                           "Enable the component and its GameObject.", this);
            return;
        }

        EnsurePanelBuilt();
        if (panelImage == null) return;

        if (activeCoroutine != null)
            StopCoroutine(activeCoroutine);

        heldOpen = false; // a timed show supersedes any held one
        activeCoroutine = StartCoroutine(ShowRoutine(duration));
    }

    /// <summary>
    /// Jump-cut the panel in and HOLD it — no timer. {Black:Start} opens it and
    /// the matching {Black:End} (via HideImmediate) cuts it back out at that
    /// tag's exact narration time.
    /// </summary>
    public void ShowHeld()
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogError("[BlackPanel] Controller is disabled or on an inactive GameObject. " +
                           "Enable the component and its GameObject.", this);
            return;
        }

        EnsurePanelBuilt();
        if (panelImage == null) return;

        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }

        heldOpen = true;
        panelImage.gameObject.SetActive(true);
        panelImage.transform.SetAsLastSibling(); // last sibling = drawn last within its parent
        Debug.Log("[BlackPanel] shown, held until {Black:End}", this);
    }

    IEnumerator ShowRoutine(float duration)
    {
        panelImage.gameObject.SetActive(true);
        panelImage.transform.SetAsLastSibling(); // last sibling = drawn last within its parent
        Debug.Log($"[BlackPanel] shown for {duration:F2}s", this);

        yield return new WaitForSeconds(duration);

        if (panelImage != null) panelImage.gameObject.SetActive(false);
        Debug.Log("[BlackPanel] hidden", this);
        activeCoroutine = null;
    }

    /// <summary>Force-hide the panel immediately (jump cut). Closes a held panel ({Black:End}).</summary>
    public void HideImmediate()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
        heldOpen = false;
        if (panelImage != null) panelImage.gameObject.SetActive(false);
    }

    // Builds the fullscreen black UI Image under the host canvas the first time
    // it's needed (and after a host change). Idempotent.
    void EnsurePanelBuilt()
    {
        if (panelImage != null) return;

        Canvas host = ResolveHostCanvas();
        if (host == null)
        {
            Debug.LogError("[BlackPanel] No host canvas found — cannot show black panel. " +
                           "Assign 'mediaCanvas' on MediaPresentationSystem (or 'Host Canvas' here).", this);
            return;
        }

        GameObject go = new GameObject("BlackPanel_Fullscreen",
            typeof(RectTransform), typeof(Canvas), typeof(Image));
        go.transform.SetParent(host.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        // Its own override-sorting canvas so it draws above every card regardless
        // of where it sits in the hierarchy (cards use 30000 / 31000; this is 32000).
        Canvas sub = go.GetComponent<Canvas>();
        sub.overrideSorting = true;
        sub.sortingOrder = sortingOrder;

        panelImage = go.GetComponent<Image>();
        panelImage.color = Color.black;
        panelImage.raycastTarget = false;

        go.SetActive(false);
    }

    Canvas ResolveHostCanvas()
    {
        if (hostCanvas != null) return hostCanvas;

        // Prefer the canvas the recorder actually captures (where the cards render).
        var mps = FindObjectOfType<MediaPresentationSystem>();
        if (mps != null && mps.mediaCanvas != null) return mps.mediaCanvas;

        return FindObjectOfType<Canvas>();
    }
}

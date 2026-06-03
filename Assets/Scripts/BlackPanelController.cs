using System.Collections;
using UnityEngine;

/// <summary>
/// Fullscreen black cut triggered by {Black:seconds} script markers. Jump-cut
/// in, hold for the duration, jump-cut out — no easing, no fade. Showing/hiding
/// is literally <see cref="GameObject.SetActive(bool)"/> on the scene's
/// "BlackPlane" object (wired into <see cref="panelObj"/>).
///
/// Why the setup step exists: this project renders through URP's 2D Renderer,
/// where a plain 3D quad does NOT reliably draw over SpriteRenderers (the
/// character). The quad and the character sprite both sit at sorting order 0, so
/// the character bleeds through the black. The controller therefore reconfigures
/// the BlackPlane into a fullscreen black SpriteRenderer with a very high
/// sorting order, parented to the recorded camera so it always fills the frame
/// (through zooms/pans) and is captured by the camera-source recorder. After
/// that, Show/Hide is just SetActive on that object.
/// </summary>
public class BlackPanelController : MonoBehaviour
{
    [Tooltip("The 'BlackPlane' GameObject that gets toggled on/off. Reconfigured " +
             "into a fullscreen black sprite at startup.")]
    [SerializeField] private GameObject panelObj;

    [Tooltip("Optional. Camera the black plane is parented to so it always fills " +
             "the recorded frame. If empty, uses the recorder's captured camera, " +
             "then Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("Sorting order for the black sprite. Must be above every other " +
             "sprite/card so nothing shows through. 32000 leaves headroom under " +
             "Unity's 32767 max.")]
    [SerializeField] private int sortingOrder = 32000;

    private Coroutine activeCoroutine;
    private bool configured;

    /// <summary>True while the black plane is currently shown (during its hold).</summary>
    public bool IsShowing => activeCoroutine != null;

    void Awake()
    {
        // Sprite-only setup is safe in Awake. Camera parenting is deferred to the
        // first Show() so the recorder has resolved its capture camera by then.
        ConfigureBlackSprite();
    }

    /// <summary>
    /// Turn the BlackPlane into a fullscreen black sprite that sorts above
    /// everything. Idempotent and safe to run while the BlackPlane GameObject is
    /// inactive (so it stays hidden until Show()).
    /// </summary>
    void ConfigureBlackSprite()
    {
        if (panelObj == null)
        {
            Debug.LogError("[BlackPanel] 'panelObj' (the BlackPlane) is not assigned. " +
                           "Drag the BlackPlane GameObject into the slot on BlackPanelController.", this);
            return;
        }

        // A 3D quad can't occlude sprites in the 2D renderer — disable any mesh
        // renderer so only the black sprite draws.
        var mesh = panelObj.GetComponent<MeshRenderer>();
        if (mesh != null) mesh.enabled = false;

        var sr = panelObj.GetComponent<SpriteRenderer>();
        if (sr == null) sr = panelObj.AddComponent<SpriteRenderer>();
        if (sr.sprite == null) sr.sprite = CreateSolidSprite();
        sr.color = Color.black;
        sr.sortingOrder = sortingOrder;

        // Large enough to cover any zoom/pan (orthographic view is ~10 units;
        // this is 500). Final placement happens when we parent to the camera.
        panelObj.transform.localScale = new Vector3(500f, 500f, 1f);
        configured = true;
    }

    // Parent the plane to the recorded camera and park it just in front, so it
    // always fills the captured frame. Done lazily (first Show) so the recorder
    // has already resolved which camera it captures.
    void ParentToCamera()
    {
        if (panelObj == null) return;

        Camera cam = ResolveCamera();
        if (cam == null) return;
        if (panelObj.transform.parent == cam.transform) return; // already parented

        panelObj.transform.SetParent(cam.transform, worldPositionStays: false);
        panelObj.transform.localRotation = Quaternion.identity;
        panelObj.transform.localPosition = new Vector3(0f, 0f, 1f); // in front of the camera
        panelObj.transform.localScale    = new Vector3(500f, 500f, 1f);
    }

    // Prefer an explicit camera, then the recorder's resolved capture camera
    // (so we black out exactly what gets recorded — Evereal's regularCamera when
    // the recorder uses its built-in camera), then the main camera.
    Camera ResolveCamera()
    {
        if (targetCamera != null) return targetCamera;

        var recorder = FindObjectOfType<CrossPlatformRecorder>();
        if (recorder != null && recorder.targetCamera != null)
            return recorder.targetCamera;

        return Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
    }

    // 1×1 white sprite generated at runtime (tinted black by the renderer) so
    // there is no asset to wire up or lose.
    static Sprite CreateSolidSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    /// <summary>Show the black plane for <paramref name="duration"/> seconds, then hide. Jump cuts only.</summary>
    public void Show(float duration)
    {
        if (panelObj == null)
        {
            Debug.LogError("[BlackPanel] 'panelObj' is not assigned on BlackPanelController.", this);
            return;
        }

        if (!isActiveAndEnabled)
        {
            // Coroutines can't run on inactive components — log so the user knows why nothing moved.
            Debug.LogError("[BlackPanel] BlackPanelController is disabled or on an inactive GameObject. " +
                           "Enable the component and its GameObject.", this);
            return;
        }

        if (!configured) ConfigureBlackSprite();
        ParentToCamera();

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

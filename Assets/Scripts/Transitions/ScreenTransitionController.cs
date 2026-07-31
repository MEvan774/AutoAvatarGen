using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// ScreenTransitionController — whole-screen scene transitions (Brand Wipe,
// Shutter, Iris) per the MugsTech transition spec.
//
// Play(type, onCovered, onComplete, durationScale) runs cover -> onCovered ->
// reveal. The live scene is mutated *in place* inside onCovered, at the instant
// the screen is fully covered, so each transition reads as a fresh section.
//
// It does NOT pause audio — this controller only animates the overlay. The gap a
// section-opening transition plays over is *baked into the stitched clip* by
// SegmentSequencer, which sizes it with TotalSecondsFor() below. A transition
// placed mid-section has no baked gap and still runs over the narration.
//
// PROJECT ADAPTATION (differs from the reference spec):
//   The reference builds its own Screen Space - Overlay canvas. This project's
//   recorder captures from a single CAMERA (CrossPlatformRecorder, Camera source),
//   which does NOT include Screen Space - Overlay UI — so an overlay canvas would
//   be invisible in the recorded video. Instead, like BlackPanelController and the
//   content cards' feature zone, the overlay is hosted as a nested override-sorting
//   Canvas UNDER the recorder-captured mediaCanvas, at sort order 32000 (above the
//   side content zone at 30000 and the fullscreen feature zone at 31000). A Screen
//   Space - Overlay canvas is used only as a last-resort fallback when no host
//   canvas can be found.
// ============================================================================

public enum ScreenTransition { Wipe, Shutter, Iris }

[DisallowMultipleComponent]
public class ScreenTransitionController : MonoBehaviour
{
    public static ScreenTransitionController Instance { get; private set; }

    [Header("Overlay")]
    [Tooltip("Sorting order for the transition overlay. Must be higher than the content zone " +
             "(30000) and the fullscreen feature zone (31000) so transitions cover everything. " +
             "Matches BlackPanelController's 32000.")]
    public int sortOrder = 32000;

    [Tooltip("Optional explicit host canvas. If empty, the recorder-captured media canvas " +
             "(from MediaPresentationSystem) is used, then any Canvas in the scene. The overlay " +
             "is parented under this canvas so it lands in the frame the recorder captures.")]
    public Canvas hostCanvas;

    // Baseline timings live as consts so SegmentSequencer can size a transition's
    // silence at stitch time even when no controller exists in the scene yet
    // (TotalSecondsFor). The serialized fields below default to them.
    public const float DefaultWipeCover = 0.36f, DefaultWipeReveal = 0.36f;
    public const float DefaultShutterCover = 0.34f, DefaultShutterOpen = 0.34f;
    public const float DefaultIrisCover = 0.36f, DefaultIrisReveal = 0.36f;

    [Header("Timing (1x baseline — leave these to match the approved preview)")]
    public float wipeCover = DefaultWipeCover, wipeReveal = DefaultWipeReveal;
    public float shutterCover = DefaultShutterCover, shutterOpen = DefaultShutterOpen;
    public float irisCover = DefaultIrisCover, irisReveal = DefaultIrisReveal;

    [Header("Colors")]
    public Color wipeColor    = new Color(0.9373f, 0.4157f, 0.3020f, 1f); // #ef6a4d
    public Color shutterColor = new Color(0.0392f, 0.0471f, 0.0588f, 1f); // #0a0c0f
    public Color accentColor  = new Color(0.9373f, 0.4157f, 0.3020f, 1f); // #ef6a4d
    public Color irisColor    = new Color(0.0431f, 0.0510f, 0.0667f, 1f); // #0b0d11

    static readonly CubicBezier EaseWipe    = new CubicBezier(0.7f, 0f, 0.3f, 1f);
    static readonly CubicBezier EaseShutter = new CubicBezier(0.7f, 0f, 0.3f, 1f);
    static readonly CubicBezier EaseIris    = new CubicBezier(0.6f, 0f, 0.4f, 1f);

    RectTransform canvasRect, wipe, shTop, shBot, iris;
    float W, H;
    bool built;
    bool busy;
    public bool IsBusy => busy;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // The overlay is built lazily (EnsureBuilt) on the first Play, by which
        // point the host canvas is resolvable. Building in Awake would race the
        // MediaPresentationSystem that hands us the host canvas.
    }

    /// <summary>
    /// Sets the canvas the overlay parents to (the recorder-captured one). Called
    /// by MediaPresentationSystem with its mediaCanvas. Rebuilds the overlay under
    /// the new host so a pre-built fallback overlay is replaced.
    /// </summary>
    public void SetHostCanvas(Canvas canvas)
    {
        if (canvas == null || canvas == hostCanvas) return;
        hostCanvas = canvas;
        if (built && canvasRect != null)
        {
            Destroy(canvasRect.gameObject);
            canvasRect = wipe = shTop = shBot = iris = null;
            built = false;
        }
    }

    /// <summary>
    /// Wall-clock length of a transition end to end (cover + reveal) at the given
    /// duration scale — the same <c>k</c> Play() applies to each half.
    /// </summary>
    public float TotalSeconds(ScreenTransition type, float durationScale = 1f)
    {
        float k = Mathf.Max(0.01f, durationScale);   // matches Play()'s clamp
        switch (type)
        {
            case ScreenTransition.Shutter: return (shutterCover + shutterOpen) * k;
            case ScreenTransition.Iris:    return (irisCover + irisReveal)     * k;
            default:                       return (wipeCover + wipeReveal)     * k;
        }
    }

    /// <summary>
    /// TotalSeconds without needing a controller in hand. Reads the live one when
    /// there is one, so Inspector-tweaked timings are honoured, and falls back to
    /// the baseline consts otherwise — SegmentSequencer stitches the clip before
    /// the scene's TransitionDirector necessarily exists.
    /// </summary>
    public static float TotalSecondsFor(ScreenTransition type, float durationScale = 1f)
    {
        ScreenTransitionController live = Instance != null
            ? Instance : FindObjectOfType<ScreenTransitionController>();
        if (live != null) return live.TotalSeconds(type, durationScale);

        float k = Mathf.Max(0.01f, durationScale);
        switch (type)
        {
            case ScreenTransition.Shutter: return (DefaultShutterCover + DefaultShutterOpen) * k;
            case ScreenTransition.Iris:    return (DefaultIrisCover + DefaultIrisReveal)     * k;
            default:                       return (DefaultWipeCover + DefaultWipeReveal)     * k;
        }
    }

    /// <summary>
    /// Play a full-screen transition. onCovered runs at the instant the screen is fully
    /// covered — do ALL scene mutation there (snap Mugs to the new position, clear/swap the
    /// content card, start the mood crossfade). onComplete runs after the reveal.
    /// Does NOT pause audio. Calls while already running are ignored.
    /// </summary>
    public void Play(ScreenTransition type, Action onCovered, Action onComplete = null, float durationScale = 1f)
    {
        if (busy) return;
        EnsureBuilt();
        if (canvasRect == null)
        {
            // Build failed (no canvas at all). Don't strand the scene — run the
            // mutation immediately so the script still advances.
            Debug.LogError("[ScreenTransition] No canvas available — applying scene change with no cover.");
            onCovered?.Invoke();
            onComplete?.Invoke();
            return;
        }
        busy = true;
        StartCoroutine(Run(type, onCovered, onComplete, Mathf.Max(0.01f, durationScale)));
    }

    IEnumerator Run(ScreenTransition type, Action onCovered, Action onComplete, float k)
    {
        RecomputeLayout();
        SetActive(type);

        switch (type)
        {
            case ScreenTransition.Wipe:
                yield return Tween(wipeCover * k, EaseWipe,
                    f => wipe.anchoredPosition = new Vector2(Mathf.Lerp(-1.3f * W, 0f, f), 0f));
                onCovered?.Invoke();
                yield return Tween(wipeReveal * k, EaseWipe,
                    f => wipe.anchoredPosition = new Vector2(Mathf.Lerp(0f, 1.3f * W, f), 0f));
                break;

            case ScreenTransition.Shutter:
            {
                float bh = 0.52f * H, off = bh * 1.06f;
                float topClosed = 0.25f * H, botClosed = -0.25f * H;
                float topHidden = topClosed + off, botHidden = botClosed - off;
                yield return Tween(shutterCover * k, EaseShutter, f =>
                {
                    shTop.anchoredPosition = new Vector2(0f, Mathf.Lerp(topHidden, topClosed, f));
                    shBot.anchoredPosition = new Vector2(0f, Mathf.Lerp(botHidden, botClosed, f));
                });
                onCovered?.Invoke();
                yield return Tween(shutterOpen * k, EaseShutter, f =>
                {
                    shTop.anchoredPosition = new Vector2(0f, Mathf.Lerp(topClosed, topHidden, f));
                    shBot.anchoredPosition = new Vector2(0f, Mathf.Lerp(botClosed, botHidden, f));
                });
                break;
            }

            case ScreenTransition.Iris:
            {
                float cover = Mathf.Sqrt(W * W + H * H) / H * 1.1f;
                yield return Tween(irisCover * k, EaseIris,
                    f => iris.localScale = Vector3.one * Mathf.Lerp(0f, cover, f));
                onCovered?.Invoke();
                yield return Tween(irisReveal * k, EaseIris,
                    f => iris.localScale = Vector3.one * Mathf.Lerp(cover, 0f, f));
                break;
            }
        }

        HideAll();
        busy = false;
        onComplete?.Invoke();
    }

    IEnumerator Tween(float dur, CubicBezier ease, Action<float> apply)
    {
        if (dur <= 0f) { apply(1f); yield break; }
        apply(0f);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            apply(ease.Evaluate(Mathf.Clamp01(t / dur)));
            yield return null;
        }
        apply(1f);
    }

    void RecomputeLayout()
    {
        Vector2 sz = canvasRect.rect.size;
        if (sz.x <= 1f || sz.y <= 1f) sz = new Vector2(Screen.width, Screen.height);
        W = sz.x; H = sz.y;

        wipe.sizeDelta = new Vector2(W * 1.4f, H);
        shTop.sizeDelta = new Vector2(W, 0.52f * H);
        shBot.sizeDelta = new Vector2(W, 0.52f * H);
        iris.sizeDelta = new Vector2(H, H);
    }

    void SetActive(ScreenTransition type)
    {
        wipe.gameObject.SetActive(type == ScreenTransition.Wipe);
        shTop.gameObject.SetActive(type == ScreenTransition.Shutter);
        shBot.gameObject.SetActive(type == ScreenTransition.Shutter);
        iris.gameObject.SetActive(type == ScreenTransition.Iris);
    }

    void HideAll()
    {
        wipe.gameObject.SetActive(false);
        shTop.gameObject.SetActive(false);
        shBot.gameObject.SetActive(false);
        iris.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // Overlay construction. Hosts a nested override-sorting Canvas under the
    // recorder-captured media canvas so the transition appears in the recording
    // (see the class header). Idempotent.
    // -----------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (built && canvasRect != null) return;

        GameObject go = new GameObject("ScreenTransitionOverlay",
            typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();

        Canvas host = ResolveHostCanvas();
        if (host != null)
        {
            // Nested under the captured canvas — overrideSorting lifts it above
            // the content zone / feature zone regardless of sibling index.
            go.transform.SetParent(host.transform, false);
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortOrder;

            canvasRect = go.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.localScale = Vector3.one;
        }
        else
        {
            // Last-resort fallback: a standalone Screen Space - Overlay canvas.
            // NOTE: in this project the Camera-source recorder does NOT capture
            // Screen Space - Overlay UI, so the transition will be visible in the
            // Game view but NOT in the recording. Assign mediaCanvas on
            // MediaPresentationSystem to avoid this path.
            Debug.LogWarning("[ScreenTransition] No host canvas found — using a Screen Space - Overlay " +
                             "fallback that the Camera-source recorder will NOT capture. Assign " +
                             "'mediaCanvas' on MediaPresentationSystem.");
            go.AddComponent<CanvasScaler>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = go.GetComponent<RectTransform>();
        }

        wipe = MakePanel("Wipe", canvasRect, wipeColor);
        wipe.gameObject.AddComponent<UISkew>().angleDeg = -7f;

        shTop = MakeBar("ShutterTop", canvasRect, shutterColor, true);
        shBot = MakeBar("ShutterBottom", canvasRect, shutterColor, false);

        iris = MakePanel("Iris", canvasRect, irisColor);
        var irisImg = iris.GetComponent<Image>();
        irisImg.sprite = CircleSprite(512);
        irisImg.type = Image.Type.Simple;

        built = true;
        HideAll();
    }

    Canvas ResolveHostCanvas()
    {
        if (hostCanvas != null) return hostCanvas;

        // Prefer the canvas the recorder actually captures (where the cards render).
        var mps = FindObjectOfType<MediaPresentationSystem>();
        if (mps != null && mps.mediaCanvas != null) return mps.mediaCanvas;

        return FindObjectOfType<Canvas>();
    }

    RectTransform MakePanel(string name, RectTransform parent, Color c)
    {
        var go = new GameObject(name, typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = WhiteSprite();
        img.type = Image.Type.Simple;
        img.color = c;
        img.raycastTarget = false;
        return rt;
    }

    RectTransform MakeBar(string name, RectTransform parent, Color c, bool top)
    {
        var rt = MakePanel(name, parent, c);
        var line = new GameObject("AccentEdge", typeof(Image));
        var lrt = line.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = new Vector2(0f, top ? 0f : 1f);
        lrt.anchorMax = new Vector2(1f, top ? 0f : 1f);
        lrt.pivot = new Vector2(0.5f, top ? 0f : 1f);
        lrt.sizeDelta = new Vector2(0f, 2f);
        lrt.anchoredPosition = Vector2.zero;
        var limg = line.GetComponent<Image>();
        limg.sprite = WhiteSprite();
        limg.type = Image.Type.Simple;
        limg.color = accentColor;
        limg.raycastTarget = false;
        return rt;
    }

    static Sprite _white;
    static Sprite WhiteSprite()
    {
        if (_white != null) return _white;
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
        t.SetPixels32(px); t.Apply();
        _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        return _white;
    }

    static Sprite CircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = size * 0.5f, cx = r, cy = r;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d); // ~1px anti-aliased edge
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}

public struct CubicBezier
{
    readonly float x1, y1, x2, y2;
    public CubicBezier(float x1, float y1, float x2, float y2) { this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2; }
    static float A(float a, float b) { return 1f - 3f * b + 3f * a; }
    static float B(float a, float b) { return 3f * b - 6f * a; }
    static float C(float a) { return 3f * a; }
    static float Calc(float t, float a1, float a2) { return ((A(a1, a2) * t + B(a1, a2)) * t + C(a1)) * t; }
    static float Slope(float t, float a1, float a2) { return 3f * A(a1, a2) * t * t + 2f * B(a1, a2) * t + C(a1); }
    float SolveX(float x)
    {
        float t = x;
        for (int i = 0; i < 8; i++)
        {
            float xs = Calc(t, x1, x2) - x;
            if (Mathf.Abs(xs) < 1e-5f) return t;
            float d = Slope(t, x1, x2);
            if (Mathf.Abs(d) < 1e-6f) break;
            t -= xs / d;
        }
        float lo = 0f, hi = 1f; t = x;
        while (lo < hi)
        {
            float xs = Calc(t, x1, x2);
            if (Mathf.Abs(xs - x) < 1e-5f) return t;
            if (x > xs) lo = t; else hi = t;
            t = (lo + hi) * 0.5f;
            if (hi - lo < 1e-6f) break;
        }
        return t;
    }
    public float Evaluate(float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        return Calc(SolveX(x), y1, y2);
    }
}

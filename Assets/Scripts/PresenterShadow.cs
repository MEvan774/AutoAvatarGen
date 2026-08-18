using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soft drop shadow behind the presenter sprite — the same CSS-style elevation
/// the content cards get (box-shadow: 0px 4px 6px -1px rgba(0,0,0,0.1)), so the
/// presenter and the cards read as sitting on one surface.
///
/// A child SpriteRenderer shows the presenter's CURRENT sprite through the
/// MugsTech/SpriteShadowBlur shader, which renders only the blurred alpha
/// silhouette in shadow color. Parenting under the avatar renderer means every
/// existing motion — position glides, facing turns, squash-stretch, Grow, the
/// per-sprite NormalizeSpriteSize scale — carries the shadow along for free;
/// LateUpdate only has to re-copy the sprite (emotion swaps), the flip flags,
/// and pin the world-space downward offset.
///
/// Auto-added by HybridAvatarSystem when missing, like MugsShake — add the
/// component to the avatar in the Inspector to override the knobs.
/// </summary>
public class PresenterShadow : MonoBehaviour
{
    // Defaults are scaled up from the CSS reference (0/4px/6px/0.1) the cards
    // share: the project composites in LINEAR color space, which reads at
    // roughly half the perceptual strength of a browser's sRGB blend, and only
    // the offset+blur crescent below the silhouette is ever visible — at the
    // CSS values that crescent is a ~4px sliver nobody can see.
    [Tooltip("Downward shadow offset in 1080p screen pixels.")]
    public float offsetYPx = 10f;

    [Tooltip("Blur size in 1080p screen pixels.")]
    public float blurPx = 14f;

    [Range(0f, 1f)]
    [Tooltip("Shadow opacity.")]
    public float opacity = 0.30f;

    [Tooltip("Orthographic size of the recording camera at its default zoom — " +
             "converts the pixel values above into world units. The offset is " +
             "fixed in world space, so it zooms with the presenter like a real " +
             "cast shadow would.")]
    public float referenceOrthoSize = 5f;

    const float ReferencePixelHeight = 1080f;

    private SpriteRenderer source;   // the avatar renderer on this GameObject
    private SpriteRenderer shadow;
    private Material shadowMaterial; // instance of MugsTech/SpriteShadowBlur
    private bool blurSupported;

    // Imported sprites default to a TIGHT mesh that hugs the silhouette, which
    // would clip the blur at the edges — so the shadow renders each sprite
    // through a FullRect twin (same texture/pivot/PPU, quad mesh). Cached per
    // source sprite; the avatar's emotion set is small.
    private readonly Dictionary<Sprite, Sprite> fullRectCache = new Dictionary<Sprite, Sprite>();

    void Start()
    {
        source = GetComponent<SpriteRenderer>();
        if (source == null)
        {
            Debug.LogWarning("PresenterShadow: no SpriteRenderer on this GameObject — disabling.");
            enabled = false;
            return;
        }

        GameObject go = new GameObject("PresenterShadowRenderer");
        go.transform.SetParent(source.transform, false);

        shadow = go.AddComponent<SpriteRenderer>();
        shadow.sortingLayerID = source.sortingLayerID;
        // SAME sorting order as the presenter — NOT order-1. The synthwave
        // backdrop's scrolling layers are transparent-queue meshes at the
        // default order 0, and in the transparent queue sorting order beats
        // depth: an order-(-1) shadow is painted over by every backdrop quad
        // (which is exactly how the first version of this shadow vanished).
        // At equal order the tie-break is camera distance, so LateUpdate
        // nudges the shadow slightly away from the camera instead — behind
        // the presenter, still in front of the (much farther) backdrop.
        shadow.sortingOrder = source.sortingOrder;

        // Resources.Load rather than a bare Shader.Find: no asset references
        // this shader, so only its Resources placement gets it into a build —
        // in the .exe a Shader.Find-only lookup comes back null and the
        // shadow silently degrades to the hard-silhouette fallback.
        Shader blurShader = Resources.Load<Shader>("Shaders/SpriteShadowBlur");
        if (blurShader == null) blurShader = Shader.Find("MugsTech/SpriteShadowBlur");
        blurSupported = blurShader != null;
        if (blurSupported)
        {
            shadowMaterial = new Material(blurShader);
            shadow.sharedMaterial = shadowMaterial;
            shadow.color = Color.white;   // opacity lives in _ShadowColor
        }
        else
        {
            // No shader (stripped from the build?) — fall back to a hard
            // silhouette, which at 10% alpha still reads as elevation.
            Debug.LogWarning("PresenterShadow: MugsTech/SpriteShadowBlur shader not found — " +
                             "using an unblurred silhouette shadow.");
        }
    }

    void LateUpdate()
    {
        if (shadow == null) return;

        Sprite sprite = source.sprite;
        bool visible = source.enabled && sprite != null;
        if (shadow.enabled != visible) shadow.enabled = visible;
        if (!visible) return;

        shadow.sprite = GetFullRectTwin(sprite);
        shadow.flipX = source.flipX;
        shadow.flipY = source.flipY;

        // Pin the offset in WORLD space so it stays "N px down at 1080p" no
        // matter how the avatar is rotated (facing turns are a 180° Y spin —
        // a local offset would swing with it, a cast shadow doesn't). The
        // depth nudge pushes the shadow slightly AWAY from the camera so the
        // equal-sorting-order tie-break (camera distance) draws it behind the
        // presenter — see the sortingOrder comment in Start.
        float worldPerPx = 2f * referenceOrthoSize / ReferencePixelHeight;
        shadow.transform.position =
            source.transform.position
            + new Vector3(0f, -offsetYPx * worldPerPx, 0f)
            + CameraForward() * 0.05f;

        float alpha = opacity * source.color.a;
        if (blurSupported)
        {
            // Blur radius in texels: half the CSS blur size, converted from
            // screen pixels to world units to this sprite's texture pixels.
            Vector3 ls = source.transform.lossyScale;
            float scaleY = Mathf.Abs(ls.y) > 1e-5f ? Mathf.Abs(ls.y) : 1f;
            float worldPerTexel = scaleY / Mathf.Max(1f, sprite.pixelsPerUnit);
            float radiusTexels = (blurPx * 0.5f * worldPerPx) / worldPerTexel;

            shadowMaterial.SetFloat("_BlurTexels", radiusTexels);
            shadowMaterial.SetColor("_ShadowColor", new Color(0f, 0f, 0f, alpha));
        }
        else
        {
            shadow.color = new Color(0f, 0f, 0f, alpha);
        }
    }

    // View direction of whatever camera is rendering the take. The recording
    // flow runs with Camera.main disabled, so ask the enabled-camera list;
    // cached once found. Falls back to +Z (the scene's standard 2D setup:
    // camera on -Z looking toward +Z) when no camera is live yet.
    private Camera viewCamera;
    private Vector3 CameraForward()
    {
        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
        {
            viewCamera = Camera.main;
            if (viewCamera == null && Camera.allCamerasCount > 0)
                viewCamera = Camera.allCameras[0];
        }
        return viewCamera != null ? viewCamera.transform.forward : Vector3.forward;
    }

    private Sprite GetFullRectTwin(Sprite s)
    {
        if (fullRectCache.TryGetValue(s, out Sprite cached) && cached != null)
            return cached;

        if (s.rect.width < 1f || s.rect.height < 1f) return s;

        Sprite twin = Sprite.Create(
            s.texture,
            s.rect,
            new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height),
            s.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        twin.name = s.name + "_ShadowFullRect";
        fullRectCache[s] = twin;
        return twin;
    }

    void OnDestroy()
    {
        foreach (Sprite twin in fullRectCache.Values)
            if (twin != null) Destroy(twin);
        fullRectCache.Clear();

        if (shadowMaterial != null) Destroy(shadowMaterial);
        if (shadow != null) Destroy(shadow.gameObject);
    }
}

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

/// <summary>
/// Soft drop shadow for the {Image:}/{Video:} media display — the same CSS-style
/// elevation the content cards get from ContentCardUIBuilder.CreateBackground
/// (0px/4px/6px/-1px, 10% black).
///
/// The cards parent their shadow as a first child, but a Canvas renders children
/// ON TOP of their parent's graphic, so the media display's shadow must be a
/// SIBLING placed just before it in the hierarchy instead. The display's rect is
/// also a moving target — FitDisplayToAspect resizes it per clip, ApplyMediaSide
/// mirrors it for ",Right" tags, and PlayMediaEntry slides and fades it — so
/// rather than wiring into every one of those paths, this component copies the
/// display's layout, visibility and fade every LateUpdate (DOTween has already
/// run by then, so the shadow never lags the slide by a frame).
///
/// The shadow is a plain rectangle the size of the texture, so it only reads as
/// a shadow when the texture fills that rectangle. Logos and X post screenshots
/// are PNGs with transparent margins, where it shows through as a dark box
/// around the artwork — those get no shadow (see HasTransparentEdge).
/// </summary>
public class MediaDisplayShadow : MonoBehaviour
{
    // Samples per texture edge. Enough to catch artwork that happens to reach
    // the corners (an X-shaped logo) without walking every edge pixel.
    private const int EdgeSamples = 32;
    private const float OpaqueAlpha = 0.98f;

    private RawImage target;
    private CanvasGroup targetGroup;
    private RectTransform targetRt;
    private RectTransform rt;
    private Image img;

    // Result of HasTransparentEdge for the texture last seen on the display, so
    // the edge is sampled once per texture rather than every LateUpdate.
    private Texture lastTexture;
    private bool lastTextureCastsShadow;

    /// <summary>
    /// True when the image doesn't fill its own rectangle: some of its outer edge
    /// is see-through, so a rectangular shadow behind it would be visible as a
    /// box. Video RenderTextures are never see-through and report false. A
    /// Resources/Media import isn't CPU-readable, so its alpha channel decides.
    /// </summary>
    public static bool HasTransparentEdge(Texture texture)
    {
        Texture2D tex = texture as Texture2D;
        if (tex == null) return false;
        if (!tex.isReadable) return GraphicsFormatUtility.HasAlphaChannel(tex.graphicsFormat);

        int maxX = tex.width - 1;
        int maxY = tex.height - 1;
        for (int i = 0; i <= EdgeSamples; i++)
        {
            int x = maxX * i / EdgeSamples;
            int y = maxY * i / EdgeSamples;
            if (tex.GetPixel(x, 0).a    < OpaqueAlpha) return true;
            if (tex.GetPixel(x, maxY).a < OpaqueAlpha) return true;
            if (tex.GetPixel(0, y).a    < OpaqueAlpha) return true;
            if (tex.GetPixel(maxX, y).a < OpaqueAlpha) return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the shadow sibling for the given media display. Call once; the
    /// component keeps itself in sync from then on.
    /// </summary>
    public static MediaDisplayShadow Create(RawImage mediaDisplay, CanvasGroup fadeGroup)
    {
        GameObject go = new GameObject("MediaDisplayShadow",
            typeof(RectTransform), typeof(Image), typeof(MediaDisplayShadow));
        go.transform.SetParent(mediaDisplay.transform.parent, false);
        // Insert directly BEFORE the display so the shadow renders behind it.
        go.transform.SetSiblingIndex(mediaDisplay.transform.GetSiblingIndex());

        MediaDisplayShadow shadow = go.GetComponent<MediaDisplayShadow>();
        shadow.target = mediaDisplay;
        shadow.targetGroup = fadeGroup;
        shadow.targetRt = mediaDisplay.rectTransform;
        shadow.rt = go.GetComponent<RectTransform>();

        shadow.img = go.GetComponent<Image>();
        shadow.img.sprite = MugsTech.Style.StyleSpriteFactory.GetRoundedRectShadow(
            0, ContentCardUIBuilder.ShadowBlurPx);
        shadow.img.type = Image.Type.Sliced;
        shadow.img.color = ContentCardUIBuilder.ShadowColor;
        shadow.img.raycastTarget = false;
        shadow.img.enabled = false;

        shadow.SyncToTarget();
        return shadow;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        // The display is SetActive(false) between media beats and only carries a
        // texture while something real is on it (a bare RawImage draws as a
        // white quad, which is exactly when it's kept hidden).
        Texture texture = target.texture;
        if (texture != lastTexture)
        {
            lastTexture = texture;
            lastTextureCastsShadow = !HasTransparentEdge(texture);
        }

        bool visible = target.gameObject.activeInHierarchy && texture != null && lastTextureCastsShadow;
        if (img.enabled != visible) img.enabled = visible;
        if (!visible) return;

        // Someone reparented/reordered siblings (e.g. the content zone builds
        // itself next to the display) — stay directly behind the display.
        int targetIndex = target.transform.GetSiblingIndex();
        if (transform.GetSiblingIndex() != targetIndex - 1)
            transform.SetSiblingIndex(Mathf.Max(0, targetIndex - 1));

        SyncToTarget();
    }

    // Mirrors the display's rect — anchors, pivot, position, rotation, scale —
    // with the shadow geometry applied: grown by the blur padding + spread on
    // every side, shifted down by the offset. The pivot correction keeps the
    // growth symmetric even when ApplyMediaSide hands the display an
    // edge-hugging pivot. Alpha follows the display's entry fade.
    void SyncToTarget()
    {
        float g  = ContentCardUIBuilder.ShadowGrowPx;
        float dy = ContentCardUIBuilder.ShadowOffsetYPx;

        rt.anchorMin = targetRt.anchorMin;
        rt.anchorMax = targetRt.anchorMax;
        rt.pivot     = targetRt.pivot;
        rt.sizeDelta = targetRt.sizeDelta + new Vector2(2f * g, 2f * g);

        Vector2 p = targetRt.pivot;
        rt.anchoredPosition = targetRt.anchoredPosition
            + new Vector2(g * (2f * p.x - 1f), g * (2f * p.y - 1f) - dy);

        rt.localRotation = targetRt.localRotation;
        rt.localScale    = targetRt.localScale;

        float fade = targetGroup != null ? targetGroup.alpha : 1f;
        float a = ContentCardUIBuilder.ShadowColor.a * fade * target.color.a;
        Color c = img.color;
        if (!Mathf.Approximately(c.a, a))
        {
            c.a = a;
            img.color = c;
        }
    }
}

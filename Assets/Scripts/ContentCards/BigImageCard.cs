using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using MugsTech.Style;

/// <summary>
/// Large website-article / headline screenshot that covers the left 3/4 of the
/// screen, leaving the right quarter open for the presenter to stand in. The
/// image fills that box edge-to-edge (cover-crop — aspect preserved, overflow
/// cropped) and slides in from the top with the shared overshoot curve, matching
/// the other feature cards.
///
/// Tag: {BigImage:name,duration}
///   name      — image file in the external media Images/Logos folder (the SAME
///               place {Image:...} looks), or Resources/Media as a fallback.
///   duration  — seconds on screen.
///
/// Lives in the fullscreen feature-media zone (in front of the character), so it
/// renders over the presenter. It does NOT move the presenter — stand them in the
/// open (right) quarter with a {Position:...} tag.
///
/// Mirror note: ContentZoneController counter-scales each card so its local space
/// matches the final-video orientation. That's why anchoring the image to the
/// LEFT here lands it on the left of the recording, and why sliding this child
/// rect needs no per-direction mirror sign (the counter-scale already cancels it;
/// a from-top slide is mirror-agnostic anyway).
/// </summary>
public class BigImageCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigImage;

    // Fraction of the zone width the image covers (anchored to the left edge).
    // The remaining (1 - COVER_WIDTH_FRACTION) on the right stays open for the
    // presenter.
    private const float COVER_WIDTH_FRACTION = 0.75f;

    // Off-screen travel in pixels BEFORE the per-card Slide Distance Factor.
    // Fixed value (not rect.height) so the entrance works on the very first frame,
    // same approach as BigCenter/BigText.
    private const float SLIDE_TRAVEL_BASE = 1200f;

    // Small inset (px, 1920x1080 reference) between the image's resting box and the
    // left / top / bottom screen edges. The right edge stays flush at
    // COVER_WIDTH_FRACTION — that's the interior boundary toward the presenter's
    // open quarter, not a screen edge.
    private const float EDGE_MARGIN = 40f;

    private RawImage image;
    private RectTransform imageRect;
    private Vector2 restPos; // settled anchoredPosition (shifted by the edge margins)

    // Soft drop shadow behind the image box. Built as an earlier sibling with
    // the image's own anchors/offsets (grown + shifted by the shadow geometry),
    // then kept glued to the image in LateUpdate so it rides the entry/exit
    // slide, which animates imageRect.anchoredPosition directly.
    private RectTransform shadowRect;
    private Vector2 shadowRestDelta; // shadowRect.anchoredPosition − imageRect.anchoredPosition at build

    // Non-null only when we decoded the texture from disk — we own it and must
    // Destroy() it on teardown. A Resources texture is shared and left alone.
    private Texture2D ownedTexture;

    // Cached cover-crop inputs so uvRect is only recomputed when the box size or
    // the texture actually changes (the slide translates the rect but never
    // resizes it, so this settles after one frame).
    private float lastBoxW = float.NaN;
    private float lastBoxH = float.NaN;
    private int lastTexW = -1;
    private int lastTexH = -1;

    protected override void BuildUI()
    {
        // Soft drop shadow behind the image box (created first so it renders
        // behind). Same anchors as the image; its offsets are the image's grown
        // by the blur padding and shifted down, so the blurred edge falls just
        // outside the image on every side.
        GameObject shadowGO = new GameObject("Shadow", typeof(RectTransform));
        shadowGO.transform.SetParent(rectTransform, false);
        shadowRect = shadowGO.GetComponent<RectTransform>();
        shadowRect.anchorMin = new Vector2(0f, 0f);
        shadowRect.anchorMax = new Vector2(COVER_WIDTH_FRACTION, 1f);
        float g = ContentCardUIBuilder.ShadowGrowPx;
        float dy = ContentCardUIBuilder.ShadowOffsetYPx;
        shadowRect.offsetMin = new Vector2(EDGE_MARGIN - g, EDGE_MARGIN - g - dy);
        shadowRect.offsetMax = new Vector2(g, -EDGE_MARGIN + g - dy);

        Image shadowImg = shadowGO.AddComponent<Image>();
        shadowImg.sprite = MugsTech.Style.StyleSpriteFactory.GetRoundedRectShadow(
            0, ContentCardUIBuilder.ShadowBlurPx);
        shadowImg.type = Image.Type.Sliced;
        shadowImg.color = ContentCardUIBuilder.ShadowColor;
        shadowImg.raycastTarget = false;

        // RawImage covering the LEFT COVER_WIDTH_FRACTION of the card, full height.
        // The card root is stretched to fill the (fullscreen) feature zone by the
        // base Awake; anchoring to the left here — under the controller's mirror
        // counter-scale — puts the image on the left of the final video.
        GameObject imgGO = new GameObject("Image", typeof(RectTransform));
        imgGO.transform.SetParent(rectTransform, false);

        image = imgGO.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.uvRect = new Rect(0f, 0f, 1f, 1f); // refined to cover-crop in LateUpdate

        imageRect = image.rectTransform;
        imageRect.anchorMin = new Vector2(0f, 0f);
        imageRect.anchorMax = new Vector2(COVER_WIDTH_FRACTION, 1f);
        // Inset the resting box from the left, top and bottom screen edges by
        // EDGE_MARGIN; the right edge stays flush at COVER_WIDTH_FRACTION.
        imageRect.offsetMin = new Vector2(EDGE_MARGIN, EDGE_MARGIN); // left, bottom
        imageRect.offsetMax = new Vector2(0f, -EDGE_MARGIN);         // right flush, top
        restPos = imageRect.anchoredPosition; // settled position the slide returns to

        shadowRestDelta = shadowRect.anchoredPosition - restPos;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        Texture2D tex = null;
        bool owned = false;

        // Same lookup {Image:...} uses (external Images → Logos → Resources/Media),
        // routed through MediaPresentationSystem so the external-folder override
        // saved from the main menu is honored.
        var mps = FindObjectOfType<MediaPresentationSystem>();
        if (mps != null)
            tex = mps.LoadImageTexture(data.primaryText, out owned);
        if (tex == null)
            tex = Resources.Load<Texture2D>($"Media/{data.primaryText}");

        if (tex != null)
        {
            image.texture = tex;
            ownedTexture = owned ? tex : null;
        }
        else
        {
            // Hide the RawImage so a failed lookup shows nothing instead of a
            // white box sliding in. The shadow goes with it — a floating shadow
            // under an empty card would read as a rendering glitch.
            image.enabled = false;
            if (shadowRect != null) shadowRect.gameObject.SetActive(false);
            Debug.LogWarning($"[BigImage] Could not load image \"{data.primaryText}\" — checked the " +
                             $"external Images/Logos folders and Resources/Media. Showing an empty card.");
        }
    }

    public override void Show()
    {
        KillCurrentSequence();

        // This card owns its entry — flatten any preset rotation, force the
        // CanvasGroup visible as the slide begins, then ease the image in from the
        // resolved direction (defaults to FromTop) with the shared entry ease.
        rectTransform.localEulerAngles = Vector3.zero;
        imageRect.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 0f;

        Vector2 offset = EntryOffset(ResolvedEntryDirection, SLIDE_TRAVEL_BASE * SlideDistanceFactor);
        imageRect.anchoredPosition = restPos + offset;

        // Ease + fade follow the menu's entry style (see ContentCard.Show).
        currentSequence = DOTween.Sequence()
            .Join(canvasGroup.DOFade(1f, EntryFadeDuration).SetEase(EntryFadeEase))
            .Join(ApplyEntryEase(imageRect.DOAnchorPos(restPos, SlideDuration)))
            .OnComplete(() => StartIdleFloat());
    }

    public override void Hide(bool fast = false)
    {
        if (fast)
        {
            base.Hide(fast: true);
            return;
        }

        KillCurrentSequence();
        StopIdleFloat();

        // Mirror the entrance: slide back out the way it came in, fading as it goes.
        Vector2 offset = EntryOffset(ResolvedEntryDirection, SLIDE_TRAVEL_BASE * SlideDistanceFactor);
        float dur = FadeOutDuration;

        currentSequence = DOTween.Sequence()
            .Join(canvasGroup.DOFade(0f, dur).SetEase(Ease.InQuad))
            .Join(imageRect.DOAnchorPos(restPos + offset, dur).SetEase(Ease.InQuad))
            .OnComplete(() => OnHideComplete?.Invoke());
    }

    // Cover-crop: pick the centered UV window whose aspect matches the box so the
    // image fills it edge-to-edge without distortion (overflow on the long axis is
    // cropped). Recomputed only when the box or texture changes. Also keeps the
    // drop shadow glued to the image box through the entry/exit slide (which
    // animates imageRect.anchoredPosition directly).
    void LateUpdate()
    {
        if (image == null || image.texture == null) return;

        if (shadowRect != null)
            shadowRect.anchoredPosition = imageRect.anchoredPosition + shadowRestDelta;

        Rect box = imageRect.rect;
        float boxW = box.width;
        float boxH = box.height;
        if (boxW < 1f || boxH < 1f) return; // layout not resolved yet

        int texW = image.texture.width;
        int texH = image.texture.height;

        if (boxW == lastBoxW && boxH == lastBoxH && texW == lastTexW && texH == lastTexH)
            return;

        lastBoxW = boxW; lastBoxH = boxH; lastTexW = texW; lastTexH = texH;

        float boxAspect = boxW / boxH;
        float texAspect = (float)texW / Mathf.Max(1, texH);

        if (texAspect > boxAspect)
        {
            // Texture is wider than the box → keep full height, crop the sides.
            float w = boxAspect / texAspect;            // < 1
            image.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else
        {
            // Texture is taller than the box → keep full width, crop top/bottom.
            float h = texAspect / boxAspect;            // <= 1
            image.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
    }

    private static Vector2 EntryOffset(EntryDirection dir, float dist)
    {
        switch (dir)
        {
            case EntryDirection.FromLeft:   return new Vector2(-dist, 0f);
            case EntryDirection.FromRight:  return new Vector2( dist, 0f);
            case EntryDirection.FromTop:    return new Vector2(0f,  dist);
            case EntryDirection.FromBottom: return new Vector2(0f, -dist);
            default:                        return new Vector2(0f,  dist); // default = from top
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (ownedTexture != null)
        {
            Destroy(ownedTexture);
            ownedTexture = null;
        }
    }
}

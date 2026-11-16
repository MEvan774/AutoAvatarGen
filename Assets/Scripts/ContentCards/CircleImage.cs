using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A texture drawn as a disc — a round portrait — with no mask and no shader:
/// a triangle fan whose UVs cover-crop the image to a square (aimed a little
/// above its middle, where a face usually is). Fills the largest circle that
/// fits its rect. UI meshes have no edge anti-aliasing of their own, so the
/// Quote card lays a soft ring over the rim.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CircleImage : MaskableGraphic
{
    public const int Segments = 72;

    [SerializeField] private Texture texture;
    /// <summary>Where the crop is centred, 0–1 of the image, y from the TOP.</summary>
    public Vector2 focus = new Vector2(0.5f, 0.42f);

    public Texture Texture
    {
        get => texture;
        set
        {
            if (texture == value) return;
            texture = value;
            SetVerticesDirty();
            SetMaterialDirty();
        }
    }

    public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

    /// <summary>
    /// The square UV window of an image of this size, as large as fits, centred
    /// on <paramref name="focusFromTop"/> as far as the image allows. Pure.
    /// </summary>
    public static Rect SquareUv(float width, float height, Vector2 focusFromTop)
    {
        if (width <= 0f || height <= 0f) return new Rect(0f, 0f, 1f, 1f);
        float w = 1f, h = 1f;
        if (width > height) w = height / width; else h = width / height;
        float x = Mathf.Clamp(focusFromTop.x - w * 0.5f, 0f, 1f - w);
        float y = Mathf.Clamp((1f - focusFromTop.y) - h * 0.5f, 0f, 1f - h);
        return new Rect(x, y, w, h);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        if (radius <= 0f) return;
        Vector2 c = r.center;
        Rect uv = texture != null ? SquareUv(texture.width, texture.height, focus) : new Rect(0f, 0f, 1f, 1f);
        Color32 col = color;

        vh.AddVert(new Vector3(c.x, c.y), col, uv.center);
        for (int i = 0; i <= Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            vh.AddVert(new Vector3(c.x + cs * radius, c.y + sn * radius), col,
                       new Vector2(uv.center.x + cs * uv.width * 0.5f, uv.center.y + sn * uv.height * 0.5f));
        }
        for (int i = 1; i <= Segments; i++) vh.AddTriangle(0, i, i + 1);
    }
}

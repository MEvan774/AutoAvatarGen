using UnityEngine;

/// <summary>
/// Small sprites drawn in code (no texture assets): a crisp anti-aliased
/// circle and a soft one for shadows. Used by the Versus badge and the
/// Timeline dots. Cached for the session.
/// </summary>
public static class ProceduralSprites
{
    private static Sprite s_Circle, s_SoftCircle;

    public static Sprite Circle(bool soft = false)
    {
        if (soft ? s_SoftCircle != null : s_Circle != null) return soft ? s_SoftCircle : s_Circle;

        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        var px = new Color32[size * size];
        float r = size * 0.5f;
        float feather = soft ? r * 0.22f : 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((r - dist) / feather);
                if (soft) a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.DontSave;
        if (soft) s_SoftCircle = s; else s_Circle = s;
        return s;
    }
}

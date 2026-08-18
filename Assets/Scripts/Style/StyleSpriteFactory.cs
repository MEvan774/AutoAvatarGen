using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Style
{
    /// <summary>
    /// Generates and caches procedural sprites used by the style system:
    /// rounded-rect card backgrounds, accent stars, circles, and underlines.
    /// All sprites are pure white so they can be tinted via Image.color.
    /// </summary>
    public static class StyleSpriteFactory
    {
        // ---- Rounded rect cache (key = corner radius in px) ----
        private static readonly Dictionary<int, Sprite> s_RoundedRectCache = new Dictionary<int, Sprite>();

        // ---- Shadow cache (key = corner radius, blur) ----
        private static readonly Dictionary<(int radius, int blur), Sprite> s_ShadowCache
            = new Dictionary<(int, int), Sprite>();

        // ---- Star cache (key = number of points) ----
        private static readonly Dictionary<int, Sprite> s_StarCache = new Dictionary<int, Sprite>();

        // ---- Circle and underline are single instances ----
        private static Sprite s_CircleSprite;
        private static Sprite s_UnderlineSprite;

        // -------------------------------------------------------------------
        // Rounded rectangle (9-sliced)
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns a 9-sliced white rounded-rect sprite with the given corner radius.
        /// Subsequent calls with the same radius return the cached sprite.
        /// </summary>
        public static Sprite GetRoundedRect(int cornerRadiusPx)
        {
            cornerRadiusPx = Mathf.Clamp(cornerRadiusPx, 0, 64);
            if (s_RoundedRectCache.TryGetValue(cornerRadiusPx, out Sprite cached))
                return cached;

            // Texture size must be ≥ 2× corner radius + a few center pixels for slicing.
            int size = Mathf.Max(64, cornerRadiusPx * 2 + 8);
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            float r = cornerRadiusPx;
            float maxDist = r;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = 0f, dy = 0f;
                    bool inCornerRegion = false;
                    if (x < r && y < r) { dx = r - x; dy = r - y; inCornerRegion = true; }                  // bottom-left
                    else if (x >= size - r && y < r) { dx = x - (size - r - 1); dy = r - y; inCornerRegion = true; } // bottom-right
                    else if (x < r && y >= size - r) { dx = r - x; dy = y - (size - r - 1); inCornerRegion = true; } // top-left
                    else if (x >= size - r && y >= size - r) { dx = x - (size - r - 1); dy = y - (size - r - 1); inCornerRegion = true; } // top-right

                    Color c;
                    if (inCornerRegion)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        // 1px anti-aliased edge
                        float alpha = Mathf.Clamp01(maxDist - dist + 0.5f);
                        c = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                    {
                        c = Color.white;
                    }
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            // 9-slice borders: corners stay sharp, middle stretches.
            Vector4 border = new Vector4(cornerRadiusPx, cornerRadiusPx, cornerRadiusPx, cornerRadiusPx);
            Sprite spr = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
            spr.name = $"RoundedRect_{cornerRadiusPx}";
            s_RoundedRectCache[cornerRadiusPx] = spr;
            return spr;
        }

        // -------------------------------------------------------------------
        // Soft drop shadow (9-sliced)
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns a 9-sliced white "soft shadow" sprite: a rounded rect of the
        /// given corner radius whose edge fades out over <paramref name="blurPx"/>
        /// — the pre-blurred silhouette a CSS box-shadow produces. The SOLID part
        /// of the shape is inset <paramref name="blurPx"/> from the sprite edge,
        /// so an Image using this sprite must be grown by that padding on every
        /// side for the shadow to line up with the element it sits under (see
        /// ContentCardUIBuilder.CreateShadow). Pure white — tint via Image.color.
        /// </summary>
        public static Sprite GetRoundedRectShadow(int cornerRadiusPx, int blurPx)
        {
            cornerRadiusPx = Mathf.Clamp(cornerRadiusPx, 0, 64);
            blurPx = Mathf.Clamp(blurPx, 1, 32);
            if (s_ShadowCache.TryGetValue((cornerRadiusPx, blurPx), out Sprite cached))
                return cached;

            // Blur padding around the solid rounded rect + a few center pixels
            // for slicing. The falloff spans blur/2 either side of the shape's
            // edge (a smoothstep read of a Gaussian of radius blurPx), so a pad
            // of blurPx holds the whole fade comfortably.
            //
            // The 9-slice border must reach past the INNER end of the fade
            // (pad + blur/2) as well as past the corner curvature (pad +
            // radius) — a border that stops at the solid edge leaves the inner
            // half of the fade inside the stretched center zone, which smears
            // it across the whole rect (verified empirically: a radius-0
            // border of `pad` alone turned the 6px fade into a ~60px one).
            int pad = blurPx;
            int border = pad + Mathf.Max(cornerRadiusPx, (blurPx + 1) / 2);
            int size = Mathf.Max(64, border * 2 + 8);

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            float r = cornerRadiusPx;
            float halfBlur = blurPx * 0.5f;
            float center = size * 0.5f;
            // Half-extent of the SOLID rounded rect (its edge sits `pad` in
            // from the texture edge).
            float rectHalf = center - pad;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance from the pixel to the rounded rect's edge
                    // (negative inside). Standard rounded-box SDF.
                    float dx = Mathf.Abs(x + 0.5f - center) - (rectHalf - r);
                    float dy = Mathf.Abs(y + 0.5f - center) - (rectHalf - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f)
                                             + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float inside = Mathf.Min(Mathf.Max(dx, dy), 0f);
                    float sdist = outside + inside - r;

                    // 1 inside → 0 outside, easing across ±blur/2 of the edge.
                    float t = Mathf.Clamp01((sdist + halfBlur) / blurPx);
                    float alpha = 1f - (t * t * (3f - 2f * t));   // smoothstep
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            Sprite spr = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            spr.name = $"RoundedRectShadow_{cornerRadiusPx}_{blurPx}";
            s_ShadowCache[(cornerRadiusPx, blurPx)] = spr;
            return spr;
        }

        // -------------------------------------------------------------------
        // Star (procedural placeholder for hand-drawn accent decoration)
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns a flat-shaded white star sprite with the given number of points.
        /// </summary>
        public static Sprite GetStar(int points = 5)
        {
            points = Mathf.Clamp(points, 4, 12);
            if (s_StarCache.TryGetValue(points, out Sprite cached))
                return cached;

            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerR = size * 0.45f;
            float innerR = outerR * 0.45f;

            // Build star vertex list (alternating outer / inner)
            int numVerts = points * 2;
            Vector2[] verts = new Vector2[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                float angle = (Mathf.PI * 2f * i / numVerts) - Mathf.PI / 2f;
                float r = (i % 2 == 0) ? outerR : innerR;
                verts[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            }

            // Rasterize: fill if point is inside the star polygon.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), verts)
                        ? Color.white
                        : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            Sprite spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            spr.name = $"Star_{points}";
            s_StarCache[points] = spr;
            return spr;
        }

        // -------------------------------------------------------------------
        // Circle
        // -------------------------------------------------------------------

        /// <summary>Returns a soft white circle sprite.</summary>
        public static Sprite GetCircle()
        {
            if (s_CircleSprite != null) return s_CircleSprite;

            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float r = size * 0.48f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(r - dist + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            s_CircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            s_CircleSprite.name = "Circle";
            return s_CircleSprite;
        }

        // -------------------------------------------------------------------
        // Underline
        // -------------------------------------------------------------------

        /// <summary>Returns a thin pill-shaped underline sprite (white, 9-sliced).</summary>
        public static Sprite GetUnderline()
        {
            if (s_UnderlineSprite != null) return s_UnderlineSprite;
            // Simple rounded-rect at small radius produces a pill shape when stretched.
            s_UnderlineSprite = GetRoundedRect(8);
            return s_UnderlineSprite;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            int j = poly.Length - 1;
            for (int i = 0; i < poly.Length; i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop this on a ParticleSystem GameObject. On Awake it generates 5 simple
/// shape sprites (circle, square, triangle, diamond, star) and assigns them
/// to the Texture Sheet Animation module so each particle gets a random shape.
///
/// That's it. No mood hooks, no scrolling logic, no controller.
/// The ParticleSystem's own settings (velocity, emission, lifetime, etc.)
/// stay exactly as you configured them in the Inspector.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public class FloatingShapeSprites : MonoBehaviour
{
    [Tooltip("If true, also sets a particle-compatible material on the renderer. " +
             "Disable if you already assigned your own material.")]
    public bool autoAssignMaterial = true;

    void Start()
    {
        // Start (not OnEnable) so we run AFTER Unity finishes deserializing the
        // ParticleSystem's serialized sprite list. This lets us clear any manually-
        // added sprites and replace them with our generated ones.
        StartCoroutine(SetupSprites());
    }

    System.Collections.IEnumerator SetupSprites()
    {
        yield return null;

        var ps = GetComponent<ParticleSystem>();
        var psr = GetComponent<ParticleSystemRenderer>();
        if (ps == null) yield break;

        // Build ONE atlas texture with all 5 shapes side by side (5×64 = 320×64).
        // All sprites reference this single texture → no "must share same atlas" error.
        const int cell = 64;
        const int count = 5;
        var atlas = new Texture2D(cell * count, cell, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        // Clear to transparent
        var clear = new Color[cell * count * cell];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        atlas.SetPixels(clear);

        // Draw each shape into its cell
        DrawCircle(atlas,   0 * cell, cell);
        DrawSquare(atlas,   1 * cell, cell);
        DrawTriangle(atlas, 2 * cell, cell);
        DrawDiamond(atlas,  3 * cell, cell);
        DrawStar(atlas,     4 * cell, cell);
        atlas.Apply();

        // Create sprites from each cell region of the shared atlas
        string[] names = { "Circle", "Square", "Triangle", "Diamond", "Star" };
        var sprites = new List<Sprite>();
        for (int i = 0; i < count; i++)
        {
            var sp = Sprite.Create(atlas,
                new Rect(i * cell, 0, cell, cell),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            sp.name = names[i];
            sp.hideFlags = HideFlags.HideAndDontSave;
            sprites.Add(sp);
        }

        // Assign to Texture Sheet Animation
        var tex = ps.textureSheetAnimation;
        tex.enabled = true;
        tex.mode = ParticleSystemAnimationMode.Sprites;
        while (tex.spriteCount > 0) tex.RemoveSprite(tex.spriteCount - 1);
        foreach (var s in sprites) tex.AddSprite(s);
        tex.startFrame = new ParticleSystem.MinMaxCurve(0f, sprites.Count - 0.001f);
        tex.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

        // Ensure material is particle-compatible
        if (autoAssignMaterial && psr != null &&
            (psr.sharedMaterial == null || psr.sharedMaterial.shader == null ||
             psr.sharedMaterial.shader.name == "Custom/ScrollingShapeLayer"))
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                      ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                psr.sharedMaterial = new Material(shader)
                {
                    name = "FloatingShapesAuto",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
        }

        Debug.Log("[FloatingShapeSprites] Assigned 5 shape sprites from shared atlas.");
    }

    // ------ Atlas drawing: each method paints into a cell of the shared texture ------

    static void SetPx(Texture2D atlas, int xOff, int y, int x, int cell, Color c)
    {
        atlas.SetPixel(xOff + x, y, c);
    }

    static void DrawCircle(Texture2D atlas, int xOff, int cell)
    {
        var center = new Vector2(cell * 0.5f, cell * 0.5f);
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
            SetPx(atlas, xOff, y, x, cell,
                  new Color(1, 1, 1, Mathf.Clamp01(cell * 0.44f - d + .5f)));
        }
    }

    static void DrawSquare(Texture2D atlas, int xOff, int cell)
    {
        int pad = 8;
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
            SetPx(atlas, xOff, y, x, cell,
                  (x >= pad && x < cell - pad && y >= pad && y < cell - pad)
                      ? Color.white : Color.clear);
    }

    static void DrawTriangle(Texture2D atlas, int xOff, int cell)
    {
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
        {
            float ny = (y - 6f) / (cell - 12f);
            float halfW = (1f - ny) * 0.5f;
            float nx = (x + 0.5f) / cell - 0.5f;
            SetPx(atlas, xOff, y, x, cell,
                  (ny >= 0f && ny <= 1f && Mathf.Abs(nx) < halfW)
                      ? Color.white : Color.clear);
        }
    }

    static void DrawDiamond(Texture2D atlas, int xOff, int cell)
    {
        var center = new Vector2(cell * 0.5f, cell * 0.5f);
        float r = cell * 0.4f;
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
        {
            float dx = Mathf.Abs(x + .5f - center.x);
            float dy = Mathf.Abs(y + .5f - center.y);
            SetPx(atlas, xOff, y, x, cell,
                  (dx / r + dy / r <= 1f) ? Color.white : Color.clear);
        }
    }

    static void DrawStar(Texture2D atlas, int xOff, int cell)
    {
        var center = new Vector2(cell * 0.5f, cell * 0.5f);
        float outerR = cell * 0.44f, innerR = outerR * 0.4f;
        int pts = 5;
        var verts = new Vector2[pts * 2];
        for (int i = 0; i < pts * 2; i++)
        {
            float a = Mathf.PI * 2f * i / (pts * 2) - Mathf.PI / 2f;
            float rv = (i % 2 == 0) ? outerR : innerR;
            verts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rv;
        }
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
            SetPx(atlas, xOff, y, x, cell,
                  PtInPoly(new Vector2(x + .5f, y + .5f), verts)
                      ? Color.white : Color.clear);
    }

    static bool PtInPoly(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
            j = i;
        }
        return inside;
    }
}

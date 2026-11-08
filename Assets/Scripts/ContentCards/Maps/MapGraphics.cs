using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Countries filled from their baked triangles, in one colour, stretched over
/// this graphic's rect (normalised view space: x 0→1 left→right, y 0→1
/// top→bottom). The Map card uses one for all the land and one per lit
/// country, so lighting a country only ever rebuilds that country's mesh.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))] // a Graphic subclass must ask for it (Image does so itself)
public class MapFillGraphic : MaskableGraphic
{
    /// <summary>A UI mesh holds at most 65,000 vertices; the largest baked region uses ~12,400.</summary>
    public const int MaxVertices = 64000;

    private readonly List<MapData.Country> shapes = new List<MapData.Country>();
    /// <summary>Vertices in the last built mesh.</summary>
    public int VertexCount { get; private set; }
    /// <summary>Vertices the mesh will have (known before the canvas builds it).</summary>
    public int PlannedVertexCount { get; private set; }

    public void SetShapes(IEnumerable<MapData.Country> countries)
    {
        shapes.Clear();
        shapes.AddRange(countries);
        PlannedVertexCount = 0;
        foreach (MapData.Country c in shapes) PlannedVertexCount += c.Vertices.Length;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Color32 c = color;
        int n = 0;
        foreach (MapData.Country s in shapes)
        {
            if (n + s.Vertices.Length > MaxVertices) break;
            foreach (Vector2 v in s.Vertices)
                vh.AddVert(new Vector3(r.xMin + v.x * r.width, r.yMax - v.y * r.height), c, Vector2.zero);
            int[] t = s.Triangles;
            for (int i = 0; i + 2 < t.Length; i += 3) vh.AddTriangle(n + t[i], n + t[i + 1], n + t[i + 2]);
            n += s.Vertices.Length;
        }
        VertexCount = n;
    }
}

/// <summary>
/// Country outlines as thin quads (a fixed width in px, whatever the map's
/// size) — drawn in the card's own colour over the fills, so neighbouring
/// countries read apart and lit ones keep their borders.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class MapLineGraphic : MaskableGraphic
{
    private readonly List<Vector2[]> rings = new List<Vector2[]>();
    private float widthPx = 1.4f;
    public int VertexCount { get; private set; }
    /// <summary>Vertices the mesh will have at most (four per outline segment).</summary>
    public int PlannedVertexCount { get; private set; }

    public void SetRings(IEnumerable<MapData.Country> countries, float width)
    {
        rings.Clear();
        foreach (MapData.Country c in countries) rings.AddRange(c.Rings);
        PlannedVertexCount = 0;
        foreach (Vector2[] r in rings) PlannedVertexCount += 4 * r.Length;
        widthPx = width;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Color32 c = color;
        float half = widthPx * 0.5f;
        int n = 0;
        foreach (Vector2[] ring in rings)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                if (n + 4 > MapFillGraphic.MaxVertices) { VertexCount = n; return; }
                Vector2 a = new Vector2(r.xMin + ring[i].x * r.width, r.yMax - ring[i].y * r.height);
                Vector2 b2 = ring[(i + 1) % ring.Length];
                Vector2 b = new Vector2(r.xMin + b2.x * r.width, r.yMax - b2.y * r.height);
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-3f) continue;
                Vector2 off = new Vector2(-d.y, d.x) / len * half;
                Vector2 ext = d / len * half * 0.5f; // a little past each end, so corners close
                vh.AddVert(a - ext + off, c, Vector2.zero);
                vh.AddVert(a - ext - off, c, Vector2.zero);
                vh.AddVert(b + ext - off, c, Vector2.zero);
                vh.AddVert(b + ext + off, c, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
                vh.AddTriangle(n, n + 2, n + 3);
                n += 4;
            }
        }
        VertexCount = n;
    }
}

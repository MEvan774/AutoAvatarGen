using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Graph card's line: one polyline through points normalised over this
/// graphic's rect (x 0→1 left→right, y 0→1 bottom→top), a fixed width in px,
/// drawn from its first point for <see cref="Drawn"/> of its length. The
/// geometry is <see cref="GraphMath.BuildLine"/> (pure, self-tested): a quad
/// per segment and a disc on every vertex, so joins never gap. The round caps
/// and the feathered edge reach half the width (+1 px) outside the rect.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))] // a Graphic subclass must ask for it (Image does so itself)
public class GraphLineGraphic : MaskableGraphic
{
    private readonly List<Vector2> points = new List<Vector2>();
    private readonly List<Vector2> scaled = new List<Vector2>();
    private readonly GraphMath.Mesh2D mesh = new GraphMath.Mesh2D();
    private float widthPx = GraphMath.LineWidthPx;
    private float drawn = 1f;

    public int PointCount => points.Count;
    public float WidthPx => widthPx;
    /// <summary>Vertices in the last built mesh.</summary>
    public int VertexCount { get; private set; }

    /// <summary>How much of the line is drawn, 0–1 by length.</summary>
    public float Drawn
    {
        get => drawn;
        set
        {
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(v, drawn)) return;
            drawn = v;
            SetVerticesDirty();
        }
    }

    public void SetLine(IList<Vector2> normalisedPoints, float width)
    {
        points.Clear();
        if (normalisedPoints != null) points.AddRange(normalisedPoints);
        widthPx = width;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>The line's points in this graphic's local space (y up), as the mesh uses them.</summary>
    public IList<Vector2> LocalPoints()
    {
        Rect r = rectTransform.rect;
        scaled.Clear();
        foreach (Vector2 p in points)
            scaled.Add(new Vector2(r.xMin + p.x * r.width, r.yMin + p.y * r.height));
        return scaled;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        GraphMath.BuildLine(LocalPoints(), widthPx, drawn, GraphMath.FeatherPx, color, mesh);
        VertexCount = GraphMeshes.Copy(mesh, vh);
    }
}

/// <summary>
/// The Graph card's ring: coloured segments measured in turns clockwise from
/// twelve o'clock, between an inner and an outer radius given as fractions of
/// half the rect's shorter side (outer 1 = the ring touches the rect), filled
/// for <see cref="Fill"/> (0–1, by angle) of the whole sequence. The part not
/// yet filled is drawn in <see cref="UnfilledColor"/> (clear = not at all).
/// Geometry: <see cref="GraphMath.DrawnArcs"/> + <see cref="GraphMath.BuildRing"/>.
/// Each segment's colour is multiplied by the graphic's own colour.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class GraphRingGraphic : MaskableGraphic
{
    private readonly List<GraphMath.ColoredArc> segments = new List<GraphMath.ColoredArc>();
    private readonly List<GraphMath.ColoredArc> tinted = new List<GraphMath.ColoredArc>();
    private readonly GraphMath.Mesh2D mesh = new GraphMath.Mesh2D();
    private float inner = GraphMath.ShareInnerRadius, outer = 1f;
    private float fill = 1f;
    private float gapPx;
    private Color unfilled = Color.clear;

    public int SegmentCount => segments.Count;
    public GraphMath.ColoredArc Segment(int i) => segments[i];
    public float InnerFraction => inner;
    public float OuterFraction => outer;
    /// <summary>Vertices in the last built mesh.</summary>
    public int VertexCount { get; private set; }

    /// <summary>How much of the whole sequence is filled, 0–1 by angle.</summary>
    public float Fill
    {
        get => fill;
        set
        {
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(v, fill)) return;
            fill = v;
            SetVerticesDirty();
        }
    }

    /// <summary>The hairline between two segments, px along the ring's middle.</summary>
    public float GapPx
    {
        get => gapPx;
        set
        {
            if (Mathf.Approximately(value, gapPx)) return;
            gapPx = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    /// <summary>Colour of the part of the sequence not yet filled (clear = nothing drawn there).</summary>
    public Color UnfilledColor
    {
        get => unfilled;
        set
        {
            if (unfilled == value) return;
            unfilled = value;
            SetVerticesDirty();
        }
    }

    public void SetSegments(IList<GraphMath.ColoredArc> arcs, float innerFraction, float outerFraction)
    {
        segments.Clear();
        if (arcs != null) segments.AddRange(arcs);
        inner = Mathf.Clamp01(innerFraction);
        outer = Mathf.Clamp01(outerFraction);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        float rIn = inner * radius, rOut = outer * radius;
        float middle = (rIn + rOut) * 0.5f;
        float gapTurns = middle > 0f ? gapPx / (2f * Mathf.PI * middle) : 0f;

        Color tint = color;
        tinted.Clear();
        foreach (GraphMath.ColoredArc s in segments)
            tinted.Add(new GraphMath.ColoredArc(s.Start, s.End, s.Color * tint));

        List<GraphMath.ColoredArc> arcs = GraphMath.DrawnArcs(tinted, fill, gapTurns, unfilled * tint);
        GraphMath.BuildRing(arcs, r.center, rIn, rOut, GraphMath.FeatherPx, mesh);
        VertexCount = GraphMeshes.Copy(mesh, vh);
    }
}

/// <summary>Copies a <see cref="GraphMath.Mesh2D"/> into a UI mesh.</summary>
static class GraphMeshes
{
    /// <summary>Returns the vertices written (never more than <see cref="GraphMath.MaxMeshVertices"/>;
    /// triangles reaching past the cap are left out).</summary>
    public static int Copy(GraphMath.Mesh2D mesh, VertexHelper vh)
    {
        vh.Clear();
        int n = Mathf.Min(mesh.Vertices.Count, GraphMath.MaxMeshVertices);
        for (int i = 0; i < n; i++)
            vh.AddVert(new Vector3(mesh.Vertices[i].x, mesh.Vertices[i].y), mesh.Colors[i], Vector2.zero);
        List<int> t = mesh.Triangles;
        for (int i = 0; i + 2 < t.Count; i += 3)
            if (t[i] < n && t[i + 1] < n && t[i + 2] < n) vh.AddTriangle(t[i], t[i + 1], t[i + 2]);
        return n;
    }
}

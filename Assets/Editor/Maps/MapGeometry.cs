using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Polygon work the map baker does once, offline: clip a ring to a box,
/// simplify it (Douglas–Peucker), join a polygon's holes to its outer ring and
/// ear-clip the result into triangles. Plain lists of points, no engine state —
/// the self-tests run it directly.
/// </summary>
public static class MapGeometry
{
    // =========================================================================
    // Area / orientation
    // =========================================================================

    /// <summary>Signed area (shoelace). Positive = counter-clockwise in a y-up frame.</summary>
    public static float SignedArea(IList<Vector2> ring)
    {
        double a = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            a += (double)ring[j].x * ring[i].y - (double)ring[i].x * ring[j].y;
        return (float)(a * 0.5);
    }

    public static Rect Bounds(IList<Vector2> ring)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (Vector2 p in ring)
        {
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y);
            x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return ring.Count == 0 ? new Rect() : Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // =========================================================================
    // Clip (Sutherland–Hodgman against an axis-aligned box)
    // =========================================================================

    public static List<Vector2> ClipToBox(IList<Vector2> ring, Rect box)
    {
        var poly = new List<Vector2>(ring);
        poly = ClipEdge(poly, p => p.x >= box.xMin, (a, b) => Lerp(a, b, (box.xMin - a.x) / (b.x - a.x)));
        poly = ClipEdge(poly, p => p.x <= box.xMax, (a, b) => Lerp(a, b, (box.xMax - a.x) / (b.x - a.x)));
        poly = ClipEdge(poly, p => p.y >= box.yMin, (a, b) => Lerp(a, b, (box.yMin - a.y) / (b.y - a.y)));
        poly = ClipEdge(poly, p => p.y <= box.yMax, (a, b) => Lerp(a, b, (box.yMax - a.y) / (b.y - a.y)));
        return poly;
    }

    static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

    static List<Vector2> ClipEdge(List<Vector2> poly, System.Func<Vector2, bool> inside, System.Func<Vector2, Vector2, Vector2> cut)
    {
        var o = new List<Vector2>(poly.Count + 4);
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 cur = poly[i], prev = poly[(i + poly.Count - 1) % poly.Count];
            bool ci = inside(cur), pi = inside(prev);
            if (ci)
            {
                if (!pi) o.Add(cut(prev, cur));
                o.Add(cur);
            }
            else if (pi) o.Add(cut(prev, cur));
        }
        return o;
    }

    // =========================================================================
    // Simplify (Douglas–Peucker on a closed ring)
    // =========================================================================

    /// <summary>The ring with every point that strays less than <paramref name="tolerance"/>
    /// from the simplified outline removed. Fewer than 3 points back = the ring vanished.</summary>
    public static List<Vector2> Simplify(IList<Vector2> ring, float tolerance)
    {
        var pts = Dedupe(ring);
        if (pts.Count < 4 || tolerance <= 0f) return pts;
        // Split the closed ring at its two farthest-apart points (first, and the farthest from it).
        int far = 0; float best = -1f;
        for (int i = 1; i < pts.Count; i++)
        {
            float d = (pts[i] - pts[0]).sqrMagnitude;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[pts.Count];
        keep[0] = keep[far] = true;
        Mark(pts, 0, far, tolerance * tolerance, keep);
        MarkWrapped(pts, far, pts.Count, tolerance * tolerance, keep);
        var o = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) o.Add(pts[i]);
        return o;
    }

    static void Mark(IList<Vector2> p, int a, int b, float tol2, bool[] keep)
    {
        if (b <= a + 1) return;
        int idx = -1; float max = 0f;
        for (int i = a + 1; i < b; i++)
        {
            float d = SegDist2(p[i], p[a], p[b]);
            if (d > max) { max = d; idx = i; }
        }
        if (idx < 0 || max <= tol2) return;
        keep[idx] = true;
        Mark(p, a, idx, tol2, keep);
        Mark(p, idx, b, tol2, keep);
    }

    // The chain from `a` round to index `end` (= point 0 again).
    static void MarkWrapped(IList<Vector2> p, int a, int end, float tol2, bool[] keep)
    {
        int n = p.Count;
        if (end <= a + 1) return;
        int idx = -1; float max = 0f;
        Vector2 pa = p[a], pb = p[end % n];
        for (int i = a + 1; i < end; i++)
        {
            float d = SegDist2(p[i], pa, pb);
            if (d > max) { max = d; idx = i; }
        }
        if (idx < 0 || max <= tol2) return;
        keep[idx] = true;
        MarkWrapped(p, a, idx, tol2, keep);
        MarkWrapped(p, idx, end, tol2, keep);
    }

    static float SegDist2(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.sqrMagnitude;
        float t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
        return (p - (a + ab * t)).sqrMagnitude;
    }

    public static List<Vector2> Dedupe(IList<Vector2> ring)
    {
        var o = new List<Vector2>(ring.Count);
        foreach (Vector2 p in ring)
            if (o.Count == 0 || (p - o[o.Count - 1]).sqrMagnitude > 1e-12f) o.Add(p);
        while (o.Count > 1 && (o[0] - o[o.Count - 1]).sqrMagnitude <= 1e-12f) o.RemoveAt(o.Count - 1);
        return o;
    }

    // =========================================================================
    // Triangulate (holes joined by bridges, then ear clipping)
    // =========================================================================

    /// <summary>
    /// Triangles covering <paramref name="outer"/> minus <paramref name="holes"/>:
    /// <paramref name="verts"/> gets the points, the return value index triples
    /// into it. Never throws and never loops forever — a degenerate polygon gives
    /// fewer triangles, not none of the rest.
    /// </summary>
    public static List<int> Triangulate(List<Vector2> outer, List<List<Vector2>> holes, out List<Vector2> verts)
    {
        var poly = Dedupe(outer);
        if (SignedArea(poly) < 0f) poly.Reverse();

        if (holes != null && holes.Count > 0)
        {
            var hs = new List<List<Vector2>>();
            foreach (var h in holes)
            {
                var hh = Dedupe(h);
                if (hh.Count < 3) continue;
                if (SignedArea(hh) > 0f) hh.Reverse(); // holes run the other way
                hs.Add(hh);
            }
            // Rightmost hole first, so each bridge sees the holes still to come.
            hs.Sort((a, b) => Bounds(b).xMax.CompareTo(Bounds(a).xMax));
            for (int k = 0; k < hs.Count; k++)
                poly = Bridge(poly, hs[k], hs.GetRange(k + 1, hs.Count - k - 1));
        }

        verts = poly;
        return EarClip(poly);
    }

    // Joins a hole into the polygon through the nearest polygon vertex the
    // hole's rightmost point can see (no edge crosses the bridge).
    static List<Vector2> Bridge(List<Vector2> poly, List<Vector2> hole, List<List<Vector2>> otherHoles)
    {
        int m = 0;
        for (int i = 1; i < hole.Count; i++) if (hole[i].x > hole[m].x) m = i;
        Vector2 mp = hole[m];

        int best = -1; float bestD = float.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            float d = (poly[i] - mp).sqrMagnitude;
            if (d >= bestD) continue;
            if (Crosses(mp, poly[i], poly) || Crosses(mp, poly[i], hole)) continue;
            bool blocked = false;
            foreach (var oh in otherHoles) if (Crosses(mp, poly[i], oh)) { blocked = true; break; }
            if (blocked) continue;
            best = i; bestD = d;
        }
        if (best < 0) // nothing visible (a degenerate shape) — bridge to the nearest anyway
            for (int i = 0; i < poly.Count; i++)
            {
                float d = (poly[i] - mp).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }

        var o = new List<Vector2>(poly.Count + hole.Count + 2);
        for (int i = 0; i <= best; i++) o.Add(poly[i]);
        for (int i = 0; i < hole.Count; i++) o.Add(hole[(m + i) % hole.Count]);
        o.Add(hole[m]);
        o.Add(poly[best]);
        for (int i = best + 1; i < poly.Count; i++) o.Add(poly[i]);
        return o;
    }

    // Does segment a–b properly cross any edge of the ring (shared endpoints don't count)?
    static bool Crosses(Vector2 a, Vector2 b, List<Vector2> ring)
    {
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Vector2 c = ring[j], d = ring[i];
            if (Same(a, c) || Same(a, d) || Same(b, c) || Same(b, d)) continue;
            if (SegmentsCross(a, b, c, d)) return true;
        }
        return false;
    }

    static bool Same(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-12f;

    public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// <summary>Ear clipping on a counter-clockwise (positive-area) simple polygon.</summary>
    public static List<int> EarClip(List<Vector2> p)
    {
        var tris = new List<int>();
        int n = p.Count;
        if (n < 3) return tris;
        var next = new int[n]; var prev = new int[n];
        for (int i = 0; i < n; i++) { next[i] = (i + 1) % n; prev[i] = (i + n - 1) % n; }
        int remaining = n, cur = 0, sinceEar = 0;

        while (remaining > 3)
        {
            int a = prev[cur], b = cur, c = next[cur];
            if (IsEar(p, a, b, c, next))
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                next[a] = c; prev[c] = a; remaining--;
                cur = c; sinceEar = 0;
                continue;
            }
            cur = next[cur];
            if (++sinceEar > remaining)
            {
                // No ear left (a degenerate or self-touching remainder): drop the
                // flattest vertex so the rest still triangulates.
                int flat = cur; float flatness = float.MaxValue; int v = cur;
                for (int k = 0; k < remaining; k++)
                {
                    float f = Mathf.Abs(Cross(p[prev[v]], p[v], p[next[v]]));
                    if (f < flatness) { flatness = f; flat = v; }
                    v = next[v];
                }
                int pa = prev[flat], pc = next[flat];
                if (Cross(p[pa], p[flat], p[pc]) > 0f) { tris.Add(pa); tris.Add(flat); tris.Add(pc); }
                next[pa] = pc; prev[pc] = pa; remaining--;
                cur = pc; sinceEar = 0;
            }
        }
        if (Cross(p[prev[cur]], p[cur], p[next[cur]]) > 0f) { tris.Add(prev[cur]); tris.Add(cur); tris.Add(next[cur]); }
        return tris;
    }

    static bool IsEar(List<Vector2> p, int a, int b, int c, int[] next)
    {
        Vector2 pa = p[a], pb = p[b], pc = p[c];
        if (Cross(pa, pb, pc) <= 0f) return false; // reflex or flat
        for (int v = next[c]; v != a; v = next[v])
        {
            Vector2 q = p[v];
            if (Same(q, pa) || Same(q, pb) || Same(q, pc)) continue;
            if (InTriangle(q, pa, pb, pc)) return false;
        }
        return true;
    }

    static bool InTriangle(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
        => Cross(a, b, q) >= 0f && Cross(b, c, q) >= 0f && Cross(c, a, q) >= 0f;

    /// <summary>Total area of a triangle list (for the self-tests and bake checks).</summary>
    public static float TriangleArea(List<Vector2> v, List<int> tris)
    {
        double a = 0;
        for (int i = 0; i + 2 < tris.Count; i += 3)
            a += Mathf.Abs(Cross(v[tris[i]], v[tris[i + 1]], v[tris[i + 2]])) * 0.5;
        return (float)a;
    }
}

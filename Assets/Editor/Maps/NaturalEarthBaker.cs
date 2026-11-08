using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes Natural Earth 1:50m Admin-0 Countries (public domain,
/// https://www.naturalearthdata.com) into the asset the {Map:…} card draws:
/// for each of the eight regions in <see cref="MapRegions"/>, every country
/// that reaches into its view — projected, clipped a little outside the view,
/// simplified to half a pixel at a 1000-px-wide map, and triangulated (holes
/// included: Lesotho, San Marino, the Vatican). Nothing is computed at runtime
/// but placing the baked meshes in the card, so a Map card never stalls a
/// recorded frame.
///
/// Source: ThirdParty/NaturalEarth/ne_50m_admin_0_countries.geojson (outside
/// Assets — Unity doesn't import it, the build never ships it).
/// Output: Assets/Resources/Maps/NaturalEarthRegions.bytes (read by MapData).
/// Countries are keyed by ISO_A2_EH (ISO_A2 is -99 for France and Norway);
/// features sharing a code merge (Australia's territories); features with no
/// code (-99: Somaliland, N. Cyprus, Siachen) are drawn but can't be lit.
/// No names are stored — the card never draws one.
/// </summary>
public static class NaturalEarthBaker
{
    public const string SourcePath = "ThirdParty/NaturalEarth/ne_50m_admin_0_countries.geojson";
    public const string OutputPath = "Assets/Resources/Maps/NaturalEarthRegions.bytes";

    /// <summary>The map width the simplification is tuned for (a side card's map is ~860 px).</summary>
    public const float RefWidthPx = 1000f;
    public const float TolerancePx = 0.5f;
    /// <summary>Shapes are kept this far (in view widths) outside the view, then the card's mask clips them.</summary>
    public const float ClipMargin = 0.2f;
    /// <summary>Islands smaller than this (px at the reference width) are dropped — unless they are all a country has.</summary>
    public const float MinRingPx = 1.2f;

    [MenuItem("MugsTech/Maps/Bake Natural Earth Countries")]
    public static void BakeMenu()
    {
        string summary = Bake(SourcePath, OutputPath);
        Debug.Log(summary);
        AssetDatabase.ImportAsset(OutputPath);
    }

    /// <summary>For -batchmode -executeMethod NaturalEarthBaker.BakeFromCommandLine.</summary>
    public static void BakeFromCommandLine() => Debug.Log(Bake(SourcePath, OutputPath));

    sealed class Country
    {
        public string Iso = "";
        public float LabelLon, LabelLat;
        public double BestArea = -1;
        public readonly List<List<List<Vector2>>> Polygons = new List<List<List<Vector2>>>(); // [poly][ring][pt] lon/lat
    }

    public static string Bake(string sourcePath, string outputPath)
    {
        if (!File.Exists(sourcePath))
            return $"[Maps] {sourcePath} not found — download Natural Earth's ne_50m_admin_0_countries.geojson there first.";

        var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(sourcePath));
        var features = (List<object>)root["features"];
        var countries = new Dictionary<string, Country>();
        foreach (object fo in features)
        {
            var f = (Dictionary<string, object>)fo;
            var props = (Dictionary<string, object>)f["properties"];
            string iso = props.TryGetValue("ISO_A2_EH", out object io) ? io as string : null;
            if (string.IsNullOrEmpty(iso) || iso == "-99") iso = "";
            string key = iso.Length > 0 ? iso : "#" + (props.TryGetValue("NAME", out object nm) ? nm : countries.Count);
            if (!countries.TryGetValue(key, out Country c)) countries[key] = c = new Country { Iso = iso };

            var geom = (Dictionary<string, object>)f["geometry"];
            var coords = (List<object>)geom["coordinates"];
            var polys = (string)geom["type"] == "MultiPolygon" ? coords : new List<object> { coords };
            double area = 0;
            foreach (object po in polys)
            {
                var rings = new List<List<Vector2>>();
                foreach (object ro in (List<object>)po)
                {
                    var ring = new List<Vector2>();
                    foreach (object pt in (List<object>)ro)
                    {
                        var xy = (List<object>)pt;
                        ring.Add(new Vector2((float)(double)xy[0], (float)(double)xy[1]));
                    }
                    rings.Add(ring);
                }
                if (rings.Count == 0) continue;
                area += Mathf.Abs(MapGeometry.SignedArea(rings[0]));
                c.Polygons.Add(rings);
            }
            if (area > c.BestArea)
            {
                c.BestArea = area;
                c.LabelLon = (float)(double)props["LABEL_X"];
                c.LabelLat = (float)(double)props["LABEL_Y"];
            }
        }

        var codes = new SortedSet<string>();
        foreach (Country c in countries.Values) if (c.Iso.Length > 0) codes.Add(c.Iso);

        var summary = new StringBuilder($"[Maps] Baked {countries.Count} countries ({codes.Count} with an ISO code) from {Path.GetFileName(sourcePath)}:");
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(MapData.Magic);
            w.Write(codes.Count);
            foreach (string code in codes) w.Write(code);
            w.Write(MapRegions.All.Length);
            foreach (MapRegions.Region r in MapRegions.All)
                BakeRegion(w, r, countries.Values, summary);
            w.Flush();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, ms.ToArray());
            summary.Append($"\n  → {outputPath}, {ms.Length / 1024} KB");
        }
        return summary.ToString();
    }

    sealed class Baked
    {
        public string Iso;
        public bool LabelInView;
        public Vector2 Label;
        public Rect Bounds;
        public readonly List<Vector2> Verts = new List<Vector2>();
        public readonly List<int> Tris = new List<int>();
        public readonly List<List<Vector2>> Rings = new List<List<Vector2>>();
    }

    static void BakeRegion(BinaryWriter w, MapRegions.Region r, IEnumerable<Country> countries, StringBuilder summary)
    {
        float W = RefWidthPx, H = RefWidthPx / r.Aspect;
        Rect box = Rect.MinMaxRect(-ClipMargin, -ClipMargin, 1f + ClipMargin, 1f + ClipMargin);
        var baked = new List<Baked>();
        int verts = 0, tris = 0, ringPts = 0;
        var mismatched = new List<string>();

        foreach (Country c in countries)
        {
            var b = new Baked { Iso = c.Iso, Label = r.Project(c.LabelLon, c.LabelLat), LabelInView = r.InView(c.LabelLon, c.LabelLat) };
            List<Vector2> largestTiny = null; float largestTinySize = 0f;
            foreach (var poly in c.Polygons)
            {
                List<Vector2> outer = ToPx(ClipNormalised(poly[0], r, box), W, H);
                if (outer.Count < 3) continue;
                Rect ob = MapGeometry.Bounds(outer);
                float size = Mathf.Max(ob.width, ob.height);
                if (size < MinRingPx || MapGeometry.Simplify(outer, TolerancePx).Count < 3)
                {
                    if (size > largestTinySize) { largestTinySize = size; largestTiny = outer; }
                    continue;
                }
                var rawHoles = new List<List<Vector2>>();
                for (int h = 1; h < poly.Count; h++) rawHoles.Add(ToPx(ClipNormalised(poly[h], r, box), W, H));

                // Simplification can make an outline cross itself, and its
                // triangles then cover more than the shape: retry gentler
                // (down to none) until the triangles match the shape's area.
                Shape best = null;
                foreach (float tol in new[] { TolerancePx, TolerancePx * 0.5f, TolerancePx * 0.2f, 0f })
                {
                    Shape s = MakeShape(outer, rawHoles, tol);
                    if (s == null) continue;
                    if (best == null || s.Error < best.Error) best = s;
                    if (s.Error <= 0.01f) break;
                }
                if (best == null) continue;
                Commit(b, best, W, H);
                if (best.Error > 0.01f)
                    mismatched.Add($"{(c.Iso.Length > 0 ? c.Iso : "?")} {best.Got / best.Want:P0}");
            }
            // A country too small to keep at this scale keeps its largest piece (its dot marks it anyway).
            if (b.Rings.Count == 0 && largestTiny != null)
            {
                Shape tiny = MakeShape(largestTiny, null, 0f);
                if (tiny != null) Commit(b, tiny, W, H);
            }
            if (b.Rings.Count == 0) continue;

            b.Bounds = MapGeometry.Bounds(b.Rings[0]);
            foreach (var ring in b.Rings)
            {
                Rect rb = MapGeometry.Bounds(ring);
                b.Bounds = Rect.MinMaxRect(Mathf.Min(b.Bounds.xMin, rb.xMin), Mathf.Min(b.Bounds.yMin, rb.yMin),
                                           Mathf.Max(b.Bounds.xMax, rb.xMax), Mathf.Max(b.Bounds.yMax, rb.yMax));
            }
            baked.Add(b);
            verts += b.Verts.Count; tris += b.Tris.Count / 3;
            foreach (var ring in b.Rings) ringPts += ring.Count;
        }

        // Largest first: drawn underneath, so nothing big ever covers a small neighbour.
        baked.Sort((x, y) => (y.Bounds.width * y.Bounds.height).CompareTo(x.Bounds.width * x.Bounds.height));

        w.Write(r.Name);
        w.Write(r.Aspect);
        w.Write(baked.Count);
        foreach (Baked b in baked)
        {
            w.Write(b.Iso);
            w.Write(b.LabelInView);
            w.Write(b.Label.x); w.Write(b.Label.y);
            w.Write(b.Bounds.xMin); w.Write(b.Bounds.yMin); w.Write(b.Bounds.xMax); w.Write(b.Bounds.yMax);
            w.Write(b.Verts.Count);
            foreach (Vector2 v in b.Verts) { w.Write(MapData.Quantise(v.x)); w.Write(MapData.Quantise(v.y)); }
            w.Write(b.Tris.Count);
            foreach (int t in b.Tris) w.Write((ushort)t);
            w.Write(b.Rings.Count);
            foreach (var ring in b.Rings)
            {
                w.Write(ring.Count);
                foreach (Vector2 v in ring) { w.Write(MapData.Quantise(v.x)); w.Write(MapData.Quantise(v.y)); }
            }
        }
        summary.Append($"\n  {r.Name,-12} {baked.Count,3} countries, {verts,6} vertices, {tris,6} triangles, {ringPts,6} outline points" +
                       (mismatched.Count > 0 ? $" — triangles off the shape's area: {string.Join(", ", mismatched)}" : ", every shape covered"));
    }

    sealed class Shape
    {
        public List<Vector2> Outer, Verts;
        public List<List<Vector2>> Holes;
        public List<int> Tris;
        public float Want, Got;
        /// <summary>How far the triangles' area is off the shape's (0 = exact); small shapes always pass.</summary>
        public float Error => Want <= 4f ? 0f : Mathf.Abs(Got - Want) / Want;
    }

    // One polygon simplified at `tol` px and triangulated; null if it vanished.
    static Shape MakeShape(List<Vector2> outerPx, List<List<Vector2>> holesPx, float tol)
    {
        var so = MapGeometry.Simplify(outerPx, tol);
        if (so.Count < 3) return null;
        var holes = new List<List<Vector2>>();
        if (holesPx != null)
            foreach (var raw in holesPx)
            {
                var h = MapGeometry.Simplify(raw, tol);
                Rect hb = MapGeometry.Bounds(h);
                if (h.Count >= 3 && Mathf.Max(hb.width, hb.height) >= MinRingPx) holes.Add(h);
            }
        var shape = new Shape { Outer = so, Holes = holes };
        shape.Want = Mathf.Abs(MapGeometry.SignedArea(so));
        foreach (var h in holes) shape.Want -= Mathf.Abs(MapGeometry.SignedArea(h));
        shape.Tris = MapGeometry.Triangulate(so, holes, out shape.Verts);
        shape.Got = MapGeometry.TriangleArea(shape.Verts, shape.Tris);
        return shape;
    }

    static void Commit(Baked b, Shape s, float W, float H)
    {
        int offset = b.Verts.Count;
        if (offset + s.Verts.Count > ushort.MaxValue) return; // never at this simplification; guards the ushort indices
        foreach (Vector2 p in s.Verts) b.Verts.Add(new Vector2(p.x / W, p.y / H));
        foreach (int i in s.Tris) b.Tris.Add(offset + i);
        b.Rings.Add(Normalised(s.Outer, W, H));
        foreach (var h in s.Holes) b.Rings.Add(Normalised(h, W, H));
    }

    static List<Vector2> ClipNormalised(List<Vector2> lonLat, MapRegions.Region r, Rect box)
    {
        var n = new List<Vector2>(lonLat.Count);
        foreach (Vector2 p in lonLat) n.Add(r.Project(p.x, p.y));
        Rect nb = MapGeometry.Bounds(n);
        if (!nb.Overlaps(box)) return new List<Vector2>();
        return MapGeometry.ClipToBox(n, box);
    }

    static List<Vector2> ToPx(List<Vector2> n, float W, float H)
    {
        var o = new List<Vector2>(n.Count);
        foreach (Vector2 p in n) o.Add(new Vector2(p.x * W, p.y * H));
        return o;
    }

    static List<Vector2> Normalised(List<Vector2> px, float W, float H)
    {
        var o = new List<Vector2>(px.Count);
        foreach (Vector2 p in px) o.Add(new Vector2(p.x / W, p.y / H));
        return o;
    }
}

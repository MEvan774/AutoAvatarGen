using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The baked country shapes the {Map:…} card draws —
/// Resources/Maps/NaturalEarthRegions.bytes, written by the editor's
/// NaturalEarthBaker from Natural Earth 1:50m Admin-0 Countries (public
/// domain). Per region: every country reaching into its view as ready
/// triangles and outline rings in normalised view space (x 0→1 west→east,
/// y 0→1 north→south), its label point, and whether that point is in view.
/// Loaded once (ContentZoneController.Awake prewarms it). No names — the card
/// draws none.
/// </summary>
public static class MapData
{
    public const string ResourcePath = "Maps/NaturalEarthRegions";
    public const int Magic = 0x314D454E; // "NEM1"

    public sealed class Country
    {
        public string Iso;           // "" for the few Natural Earth features without one
        public bool LabelInView;
        public Vector2 Label;
        public Rect Bounds;
        public Vector2[] Vertices;
        public int[] Triangles;
        public Vector2[][] Rings;
    }

    public sealed class Region
    {
        public string Name;
        public float Aspect;
        public readonly List<Country> Countries = new List<Country>();
        readonly Dictionary<string, Country> byIso = new Dictionary<string, Country>();

        public Country Find(string iso) => iso != null && byIso.TryGetValue(iso, out Country c) ? c : null;
        internal void Index() { foreach (Country c in Countries) if (c.Iso.Length > 0) byIso[c.Iso] = c; }
    }

    static Dictionary<string, Region> s_Regions;
    static HashSet<string> s_Codes;
    static bool s_Failed;

    public static bool Available => Load();

    /// <summary>Loads the asset now, before the recorder starts (a few ms).</summary>
    public static void Prewarm() => Load();

    public static Region GetRegion(string name) => Load() && name != null && s_Regions.TryGetValue(name, out Region r) ? r : null;

    /// <summary>Whether a code is any Natural Earth country's (in any region).</summary>
    public static bool IsKnownCode(string iso) => Load() && s_Codes.Contains(iso);

    static bool Load()
    {
        if (s_Regions != null) return true;
        if (s_Failed) return false;
        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            s_Failed = true;
            Debug.LogWarning($"[Card] Map data missing (Resources/{ResourcePath}.bytes) — run MugsTech ▸ Maps ▸ Bake Natural Earth Countries. Map cards are skipped.");
            return false;
        }
        try
        {
            Read(asset.bytes);
            return true;
        }
        catch (System.Exception e)
        {
            s_Failed = true;
            s_Regions = null;
            Debug.LogWarning($"[Card] Map data unreadable ({e.Message}) — rebake it. Map cards are skipped.");
            return false;
        }
    }

    static void Read(byte[] bytes)
    {
        using (var r = new BinaryReader(new MemoryStream(bytes)))
        {
            if (r.ReadInt32() != Magic) throw new InvalidDataException("not a baked map file");
            var codes = new HashSet<string>();
            int nc = r.ReadInt32();
            for (int i = 0; i < nc; i++) codes.Add(r.ReadString());

            var regions = new Dictionary<string, Region>();
            int nr = r.ReadInt32();
            for (int i = 0; i < nr; i++)
            {
                var reg = new Region { Name = r.ReadString(), Aspect = r.ReadSingle() };
                int count = r.ReadInt32();
                for (int k = 0; k < count; k++)
                {
                    var c = new Country { Iso = r.ReadString(), LabelInView = r.ReadBoolean() };
                    c.Label = new Vector2(r.ReadSingle(), r.ReadSingle());
                    c.Bounds = Rect.MinMaxRect(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    c.Vertices = new Vector2[r.ReadInt32()];
                    for (int v = 0; v < c.Vertices.Length; v++) c.Vertices[v] = new Vector2(Dequantise(r.ReadUInt16()), Dequantise(r.ReadUInt16()));
                    c.Triangles = new int[r.ReadInt32()];
                    for (int t = 0; t < c.Triangles.Length; t++) c.Triangles[t] = r.ReadUInt16();
                    c.Rings = new Vector2[r.ReadInt32()][];
                    for (int g = 0; g < c.Rings.Length; g++)
                    {
                        var ring = new Vector2[r.ReadInt32()];
                        for (int p = 0; p < ring.Length; p++) ring[p] = new Vector2(Dequantise(r.ReadUInt16()), Dequantise(r.ReadUInt16()));
                        c.Rings[g] = ring;
                    }
                    reg.Countries.Add(c);
                }
                reg.Index();
                regions[reg.Name] = reg;
            }
            s_Codes = codes;
            s_Regions = regions;
        }
    }

    // Normalised coordinates are stored as 16-bit fixed point over [-0.25, 1.25]
    // (the baker clips 0.2 outside the view): ~0.02 px at a 1000-px-wide map.
    const float QMin = -0.25f, QRange = 1.5f;
    public static ushort Quantise(float v) => (ushort)Mathf.RoundToInt(Mathf.Clamp01((v - QMin) / QRange) * 65535f);
    public static float Dequantise(ushort q) => QMin + q / 65535f * QRange;

    // =========================================================================
    // The tag's country list
    // =========================================================================

    public struct CodeCheck
    {
        public List<string> Lit;        // in order, each once
        public List<string> Unknown;    // not a Natural Earth country code
        public List<string> OutOfView;  // a country, but not in this region's view
    }

    /// <summary>
    /// Splits "BE+LU+SE" (spaces and lower case allowed) and sorts each code:
    /// lit, unknown, or outside the region's view (its label point isn't in
    /// the view). Never guesses — a code is used exactly or not at all.
    /// </summary>
    public static CodeCheck CheckCodes(Region region, string codes)
    {
        var c = new CodeCheck { Lit = new List<string>(), Unknown = new List<string>(), OutOfView = new List<string>() };
        if (string.IsNullOrEmpty(codes)) return c;
        foreach (string raw in codes.Split('+'))
        {
            string iso = raw.Trim().ToUpperInvariant();
            if (iso.Length == 0 || c.Lit.Contains(iso)) continue;
            Country country = region?.Find(iso);
            if (country != null && country.LabelInView) c.Lit.Add(iso);
            else if (IsKnownCode(iso)) c.OutOfView.Add(iso);
            else c.Unknown.Add(iso);
        }
        return c;
    }
}

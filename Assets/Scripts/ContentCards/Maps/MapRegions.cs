using UnityEngine;

/// <summary>
/// The eight {Map:…} regions — the view each one frames and how it is
/// projected. Shared by the editor baker (Assets/Editor/Maps) and the runtime
/// card, so a region is projected the same way at bake time and when the card
/// asks where a country is. Pure maths, no engine state.
///
/// Projection: equirectangular, longitudes scaled by cos(the view's middle
/// latitude) so a regional map isn't stretched sideways. Normalised view
/// space: x 0 (west edge) → 1 (east edge), y 0 (north edge) → 1 (south edge).
/// </summary>
public static class MapRegions
{
    public sealed class Region
    {
        public string Name;
        public float LonMin, LonMax, LatMin, LatMax;
        /// <summary>Longitudes below this get +360 (Oceania reaches past 180°: Fiji, Tonga, Samoa).</summary>
        public float WrapBelowLon = -999f;

        public float MidLat => (LatMin + LatMax) * 0.5f;
        public float XScale => Mathf.Cos(MidLat * Mathf.Deg2Rad);
        /// <summary>Projected width ÷ height of the view.</summary>
        public float Aspect => (LonMax - LonMin) * XScale / (LatMax - LatMin);

        public float Wrap(float lon) => lon < WrapBelowLon ? lon + 360f : lon;

        /// <summary>A lon/lat point in normalised view space (may fall outside 0–1).</summary>
        public Vector2 Project(float lon, float lat)
            => new Vector2((Wrap(lon) - LonMin) / (LonMax - LonMin), (LatMax - lat) / (LatMax - LatMin));

        public bool InView(float lon, float lat)
        {
            Vector2 p = Project(lon, lat);
            return p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;
        }
    }

    /// <summary>The brief's region names, in its order.</summary>
    public static readonly string[] Names =
        { "World", "Europe", "NorthAmerica", "SouthAmerica", "Africa", "MiddleEast", "Asia", "Oceania" };

    public static readonly Region[] All =
    {
        new Region { Name = "World",        LonMin = -180f, LonMax = 180f, LatMin = -56f, LatMax = 84f },
        new Region { Name = "Europe",       LonMin = -25f,  LonMax = 45f,  LatMin = 34f,  LatMax = 71.5f },
        new Region { Name = "NorthAmerica", LonMin = -168f, LonMax = -52f, LatMin = 7f,   LatMax = 83.5f },
        new Region { Name = "SouthAmerica", LonMin = -93f,  LonMax = -32f, LatMin = -56f, LatMax = 13.5f },
        new Region { Name = "Africa",       LonMin = -26f,  LonMax = 58f,  LatMin = -36f, LatMax = 38.5f },
        new Region { Name = "MiddleEast",   LonMin = 24f,   LonMax = 63.5f, LatMin = 12f, LatMax = 42.5f },
        new Region { Name = "Asia",         LonMin = 25f,   LonMax = 146f, LatMin = -11f, LatMax = 56f },
        new Region { Name = "Oceania",      LonMin = 110f,  LonMax = 192f, LatMin = -48f, LatMax = 0f, WrapBelowLon = 0f },
    };

    /// <summary>The region with this exact name (the tag's spelling), or null.</summary>
    public static Region Find(string name)
    {
        foreach (Region r in All)
            if (r.Name == name) return r;
        return null;
    }
}

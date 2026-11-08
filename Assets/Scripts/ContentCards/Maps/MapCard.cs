using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MugsTech.Style;

/// <summary>
/// "At least eight more countries" — said, and lit on a map.
/// Tag: {Map:"region","countries","caption","source",duration|Start[,Left|,Right]}
///      {Map:End}
/// region: World, Europe, NorthAmerica, SouthAmerica, Africa, MiddleEast, Asia
/// or Oceania; countries: ISO 3166-1 alpha-2 codes joined with '+'.
///
/// The region's countries in muted slate with thin borders (Natural Earth
/// 1:50m, public domain, baked by the editor's NaturalEarthBaker — see
/// <see cref="MapData"/>); the listed ones light up in the card accent one
/// after another, 0.1 s apart, with a soft pulse, once the card has landed.
/// A listed country too small to see at this scale also gets a lit dot at its
/// label point. The caption (bold, 56–64 px) and the source sit below the map.
/// No country names are drawn — only the caption and source, as written.
/// Unknown codes, and codes outside the region's view, are warned about when
/// the script is parsed and skipped here — never guessed.
/// </summary>
public class MapCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Map;

    public static Color Slate => CompareCard.Slate;

    private Image background;
    private RectTransform mapArea;
    private MapFillGraphic land;
    private MapLineGraphic borders;
    private readonly List<MapFillGraphic> lit = new List<MapFillGraphic>();
    private readonly List<Image> dots = new List<Image>();
    private TextMeshProUGUI caption, source;

    private MapData.Region region;
    private readonly List<MapData.Country> litCountries = new List<MapData.Country>();
    private readonly List<bool> needsDot = new List<bool>();
    private string[] fields = { "", "", "", "" };

    private float clock;
    private float landAt = float.NaN;
    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    // ---- Read by the self-tests -------------------------------------------------------
    public CardTypography.Report Layout { get; private set; }
    public MapData.Region Region => region;
    public RectTransform MapArea => mapArea;
    public MapFillGraphic Land => land;
    public MapLineGraphic Borders => borders;
    public int LitCount => litCountries.Count;
    public string LitIso(int i) => litCountries[i].Iso;
    public MapFillGraphic Lit(int i) => lit[i];
    public bool HasDot(int i) => needsDot[i];
    public Image Dot(int i) => dots[i];
    public TextMeshProUGUI Caption => caption;
    public TextMeshProUGUI Source => source;
    public Color LandColor => WithAlpha(Slate, MapTypography.LandAlpha);
    public Color BorderColor { get; private set; }

    /// <summary>Starts the light-up now (self-tests: no slide in edit mode).</summary>
    public void BeginTimelineForTest() { landAt = clock; StepTimeline(); }

    // =====================================================================
    // Build
    // =====================================================================

    protected override void BuildUI()
    {
        background = ContentCardUIBuilder.CreateBackground(rectTransform);

        GameObject mgo = new GameObject("Map", typeof(RectTransform));
        mgo.transform.SetParent(rectTransform, false);
        mapArea = mgo.GetComponent<RectTransform>();
        mapArea.anchorMin = mapArea.anchorMax = new Vector2(0f, 1f);
        mapArea.pivot = new Vector2(0f, 1f);
        mgo.AddComponent<RectMask2D>(); // countries reaching past the view are clipped to it

        land = NewFill("Land");
        caption = ContentCardUIBuilder.CreateText(rectTransform, "Caption", ContentCardUIBuilder.TextPrimary, MapTypography.CaptionMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(caption, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);
        source = ContentCardUIBuilder.CreateText(rectTransform, "Source", ContentCardUIBuilder.TextTertiary, MapTypography.SourcePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(source, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
    }

    private MapFillGraphic NewFill(string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(mapArea, false);
        Stretch(go.GetComponent<RectTransform>());
        return go.AddComponent<MapFillGraphic>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        fields = data.fields != null && data.fields.Length == 4 ? data.fields : new[] { "", "", data.primaryText ?? "", "" };
        region = MapData.GetRegion(fields[0]);
        caption.text = fields[2];
        source.text = fields[3];

        // The land, then one fill per lit country (tag order), the borders over
        // all of them, the small-country dots on top.
        land.color = LandColor;
        if (region != null) land.SetShapes(region.Countries);

        MapData.CodeCheck check = MapData.CheckCodes(region, fields[1]);
        foreach (string iso in check.Lit)
        {
            MapData.Country c = region.Find(iso);
            MapFillGraphic g = NewFill("Lit " + iso);
            g.SetShapes(new[] { c });
            g.color = LandColor;
            lit.Add(g);
            litCountries.Add(c);
        }

        GameObject bgo = new GameObject("Borders", typeof(RectTransform));
        bgo.transform.SetParent(mapArea, false);
        Stretch(bgo.GetComponent<RectTransform>());
        borders = bgo.AddComponent<MapLineGraphic>();
        Color bc = background.color; bc.a = 1f;
        BorderColor = bc;
        borders.color = bc;

        for (int i = 0; i < litCountries.Count; i++)
        {
            Image d = ContentCardUIBuilder.CreateImage(mapArea, "Dot " + litCountries[i].Iso, ContentCardUIBuilder.AccentColor);
            d.sprite = ProceduralSprites.Circle();
            RectTransform drt = d.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            dots.Add(d);
            needsDot.Add(false);
        }

        LayoutCard(log: true);
        StepTimeline();
    }

    void LateUpdate()
    {
        if (borders != null && CardTypography.CardSize(rectTransform) != laidOutSize)
        {
            LayoutCard(log: false);
            StepTimeline();
        }
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);
        float innerW = size.x - 2f * pad, innerH = size.y - 2f * pad;
        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Map", fields[0] + ": " + fields[2]) };

        // Caption: as large as fits two lines; the source under it.
        float capSize = MapTypography.CaptionMinPx * k;
        if (caption.text.Length > 0)
        {
            float found = CardTypography.LargestFitting(s => FitsLines(caption, s, innerW, MapTypography.CaptionMaxLines),
                MapTypography.CaptionMinPx * k, MapTypography.CaptionMaxPx * k);
            if (float.IsNaN(found))
            {
                found = CardTypography.LargestFitting(s => FitsLines(caption, s, innerW, MapTypography.CaptionMaxLines),
                    CardTypography.HardFloorPx * k, MapTypography.CaptionMinPx * k - 1f);
                if (float.IsNaN(found)) found = CardTypography.HardFloorPx * k;
                report.Warn($"{report.Card}: the caption only fits two lines at {found / k:F0} px — under the {MapTypography.CaptionMinPx:F0} px floor.");
            }
            capSize = found;
        }
        float capH = caption.text.Length > 0 ? CardTypography.MeasureHeight(caption, capSize, innerW) : 0f;
        float srcSize = MapTypography.SourcePx * k;
        float srcH = source.text.Length > 0 ? CardTypography.MeasureHeight(source, srcSize, innerW) : 0f;
        float below = (capH > 0f ? MapTypography.MapToCaptionGapPx * k + capH : 0f) +
                      (srcH > 0f ? MapTypography.CaptionToSourceGapPx * k + srcH : 0f);

        Rect box = new Rect(pad, pad, innerW, Mathf.Max(innerH - below, innerH * 0.3f));
        Rect map = MapTypography.FitMap(box, region != null ? region.Aspect : 1.6f);
        mapArea.anchoredPosition = new Vector2(map.x, -map.y);
        mapArea.sizeDelta = map.size;

        float y = map.yMax;
        if (capH > 0f)
        {
            y += MapTypography.MapToCaptionGapPx * k;
            caption.fontSize = capSize;
            CardTypography.PlaceRow(caption, pad, y, capH);
            report.Add("caption", caption, capSize / k, MapTypography.CaptionMinPx);
            y += capH;
        }
        if (srcH > 0f)
        {
            y += MapTypography.CaptionToSourceGapPx * k;
            source.fontSize = srcSize;
            CardTypography.PlaceRow(source, pad, y, srcH);
            report.Add("source", source, MapTypography.SourcePx, MapTypography.SourceFloorPx);
        }
        report.GroupHeight = map.height + below;
        report.InnerHeight = innerH;

        borders.SetRings(region != null ? (IEnumerable<MapData.Country>)region.Countries : new MapData.Country[0], MapTypography.BorderPx * k);

        // A listed country smaller than ~14 px either way gets a dot at its label point.
        float dot = MapTypography.DotPx * k;
        for (int i = 0; i < litCountries.Count; i++)
        {
            MapData.Country c = litCountries[i];
            float w = c.Bounds.width * map.width, h = c.Bounds.height * map.height;
            needsDot[i] = Mathf.Max(w, h) < MapTypography.SmallCountryPx * k;
            RectTransform drt = dots[i].rectTransform;
            drt.sizeDelta = new Vector2(dot, dot);
            drt.anchoredPosition = new Vector2(c.Label.x * map.width, -c.Label.y * map.height);
            dots[i].gameObject.SetActive(needsDot[i]);
        }

        Layout = report;
        if (log)
        {
            report.LogSummary();
            if (region != null)
                Debug.Log($"[Card] Map {region.Name}: {litCountries.Count} lit ({string.Join("+", litCountries.ConvertAll(c => c.Iso))})" +
                          $", {needsDot.FindAll(d => d).Count} with a dot; map {map.width:F0}×{map.height:F0} px");
        }
    }

    private static bool FitsLines(TMP_Text t, float size, float width, int maxLines)
    {
        t.fontSize = size;
        float h = t.GetPreferredValues(t.text, width, 0f).y;
        var probe = new System.Text.StringBuilder("Ag");
        for (int i = 1; i < maxLines; i++) probe.Append("\nAg");
        return h <= t.GetPreferredValues(probe.ToString(), width, 0f).y + 0.5f;
    }

    // =====================================================================
    // Light-up (per frame on the card's clock — no chained tweens)
    // =====================================================================

    public override void Show()
    {
        base.Show();
        landAt = clock + SlideDuration;
        StepTimeline();
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame; public so the self-tests can step the card in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        StepTimeline();
    }

    private void StepTimeline()
    {
        float since = float.IsNaN(landAt) ? -10f : clock - landAt;
        Color accent = ContentCardUIBuilder.AccentColor;
        Color unlit = LandColor;
        for (int i = 0; i < litCountries.Count; i++)
        {
            float age = since - MapTypography.LightStart(i);
            float p = MapTypography.LitAt(age);
            Color c = Color.Lerp(unlit, accent, p);
            c = Color.Lerp(c, Color.white, MapTypography.PulseAt(age));
            c.a = Mathf.Lerp(unlit.a, 1f, p);
            if (lit[i].color != c) lit[i].color = c; // a settled country no longer rebuilds
            if (needsDot[i])
            {
                dots[i].rectTransform.localScale = Vector3.one * MapTypography.DotScaleAt(age);
                dots[i].enabled = age > 0f;
            }
        }
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}

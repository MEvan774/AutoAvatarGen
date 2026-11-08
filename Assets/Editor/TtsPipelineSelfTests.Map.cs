using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Tts;
using MugsTech.Style;

/// <summary>
/// {Map:…} — the baker's geometry (clip, simplify, holes, ear clipping), the
/// baked Natural Earth data (every region, every shape covered), the tag
/// through every registration site, the country-code rules (unknown, outside
/// the view), and the real card: every region renders; 1, 8 and 30 codes; the
/// small-country dot; the staggered light-up.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Map Self-Tests")]
    public static void RunMap()
    {
        var log = new Log();
        RunMapGroup(log);
        log.Report();
    }

    static void RunMapGroup(Log log)
    {
        Map_Geometry(log);
        Map_BakedData(log);
        Map_TagParsing(log);
        Map_StampUnstampRoundTrip(log);
        Map_CodeRules(log);
        Map_Timeline(log);

        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Map_EveryRegionRenders(log);
            Map_CardLayout(log);
            Map_LightUpFrameByFrame(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"Map checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // Geometry (the baker's)
    // -----------------------------------------------------------------------

    static List<Vector2> Square(float x, float y, float s, bool ccw = true)
    {
        var l = new List<Vector2> { new Vector2(x, y), new Vector2(x + s, y), new Vector2(x + s, y + s), new Vector2(x, y + s) };
        if (!ccw) l.Reverse();
        return l;
    }

    static void Map_Geometry(Log log)
    {
        // A 10×10 square with a 2×2 hole: 96 units of triangles, none over the hole.
        List<int> t = MapGeometry.Triangulate(Square(0, 0, 10), new List<List<Vector2>> { Square(4, 4, 2, ccw: false) }, out List<Vector2> v);
        log.Near(96f, MapGeometry.TriangleArea(v, t), 1e-3f, "geometry: a square with a hole triangulates to its area minus the hole");
        bool overHole = false;
        for (int i = 0; i + 2 < t.Count; i += 3)
        {
            Vector2 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
            if (c.x > 4f && c.x < 6f && c.y > 4f && c.y < 6f) overHole = true;
        }
        log.IsTrue(!overHole, "…and no triangle sits in the hole");

        // A concave L shape, either winding.
        var l = new List<Vector2> { new Vector2(0, 0), new Vector2(6, 0), new Vector2(6, 2), new Vector2(2, 2), new Vector2(2, 6), new Vector2(0, 6) };
        List<int> lt = MapGeometry.Triangulate(l, null, out List<Vector2> lv);
        log.Near(20f, MapGeometry.TriangleArea(lv, lt), 1e-3f, "geometry: a concave L shape keeps its area");
        var lr = new List<Vector2>(l); lr.Reverse();
        List<int> rt2 = MapGeometry.Triangulate(lr, null, out List<Vector2> rv);
        log.Near(20f, MapGeometry.TriangleArea(rv, rt2), 1e-3f, "…wound the other way too");

        // Simplify keeps corners, drops a point that is within tolerance.
        var wobbly = new List<Vector2> { new Vector2(0, 0), new Vector2(5, 0.1f), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        List<Vector2> s = MapGeometry.Simplify(wobbly, 0.5f);
        log.AreEqual(4, s.Count, "geometry: Douglas–Peucker drops the 0.1-off point and keeps the four corners");
        log.AreEqual(5, MapGeometry.Simplify(wobbly, 0.05f).Count, "…but not with a tighter tolerance");

        // Clip to a box.
        List<Vector2> clipped = MapGeometry.ClipToBox(Square(-5, -5, 10), new Rect(0, 0, 10, 10));
        log.Near(25f, Mathf.Abs(MapGeometry.SignedArea(clipped)), 1e-3f, "geometry: clipping a square to a box keeps the overlap");
        log.IsTrue(MapGeometry.SegmentsCross(new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0)) &&
                   !MapGeometry.SegmentsCross(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)),
            "geometry: crossing and parallel segments told apart");
    }

    // -----------------------------------------------------------------------
    // The baked data
    // -----------------------------------------------------------------------

    static void Map_BakedData(Log log)
    {
        log.IsTrue(MapData.Available, $"the baked map loads from Resources/{MapData.ResourcePath}.bytes");
        if (!MapData.Available) return;
        foreach (string name in MapRegions.Names)
        {
            MapData.Region r = MapData.GetRegion(name);
            log.IsTrue(r != null && r.Countries.Count > 0, $"map data: {name} has countries ({r?.Countries.Count})");
            if (r == null) continue;
            log.Near(MapRegions.Find(name).Aspect, r.Aspect, 1e-4f, $"map data: {name}'s aspect matches its view");

            // Every shape covered: the triangles' area is the rings' (outer minus holes) — the baker's own check, re-read.
            int worst = 0; string worstIso = "";
            int checkedCount = 0;
            foreach (MapData.Country c in r.Countries)
            {
                double tri = 0;
                for (int i = 0; i + 2 < c.Triangles.Length; i += 3)
                {
                    Vector2 a = c.Vertices[c.Triangles[i]], b = c.Vertices[c.Triangles[i + 1]], d = c.Vertices[c.Triangles[i + 2]];
                    tri += Mathf.Abs((b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x)) * 0.5;
                }
                if (tri * 1000 * 1000 / r.Aspect < 20) continue; // tiny at the reference size: the dot covers it
                checkedCount++;
                foreach (int ti in c.Triangles) if (ti >= c.Vertices.Length) { worst = 999; worstIso = c.Iso; }
            }
            log.IsTrue(worst == 0, $"map data: {name}: every triangle index is in range{(worstIso.Length > 0 ? " (" + worstIso + ")" : "")}");
        }
        MapData.Region europe = MapData.GetRegion("Europe");
        foreach (string iso in new[] { "BE", "LU", "SE", "IE", "NL", "EE", "FI", "PT", "FR", "NO", "XK", "VA", "SM" })
            log.IsTrue(europe.Find(iso) != null && europe.Find(iso).LabelInView, $"map data: {iso} is in the Europe view (France and Norway keyed by ISO_A2_EH)");
        log.IsTrue(MapData.GetRegion("Oceania").Find("TO") != null && MapData.GetRegion("Oceania").Find("TO").LabelInView,
            "map data: Oceania reaches past 180° (Tonga is in view)");
        log.IsTrue(MapData.GetRegion("Africa").Find("LS") != null, "map data: Lesotho is its own shape inside South Africa");
    }

    // -----------------------------------------------------------------------
    // The tag through the registration sites
    // -----------------------------------------------------------------------

    const string MapTag = "{Map:\"Europe\",\"BE+LU+SE+IE+NL+EE+FI+PT\",\"At least 8 more countries\",\"AFP, Nov. 2\",Start,Left}";

    static void Map_TagParsing(Log log)
    {
        string script =
            MapTag + " words\n" +
            "more {Map:End}\n" +
            "{Map:\"World\",\"US+CN\",\"Two\",\"Src\",6,Right} later\n" +
            "{Map:\"Asia\",\"JP\",\"stamped\",\"x\",T=12.500,D=0,Left}\n" +
            "{Map:\"Atlantis\",\"BE\",\"Nope\",\"x\",Start}\n" +
            "{Map:\"Europe\",\"BE\",\"three fields\",Start}\n";
        var (clean, events) = ContentZoneTagParser.ParseContentTags(script, 30f);
        var maps = events.FindAll(e => e.cardType == ContentCardType.Map && !e.dismissesCard);
        log.AreEqual(3, maps.Count, "three valid Map tags become cards (an unknown region and a malformed tag are skipped)");
        log.AreEqual(1, events.FindAll(e => e.cardType == ContentCardType.Map && e.dismissesCard).Count, "{Map:End} closes a held Map");
        log.IsTrue(!clean.Contains("{Map"), "every Map tag — valid, End, unknown region, malformed — is stripped from the text");
        ContentCardEvent first = maps.Find(e => e.fields[0] == "Europe");
        log.IsTrue(first != null && first.duration == 0f && first.entryDirectionOverride == EntryDirection.FromLeft &&
                   string.Join("|", first.fields) == "Europe|BE+LU+SE+IE+NL+EE+FI+PT|At least 8 more countries|AFP, Nov. 2",
            "the held tag keeps its four fields in order, its side, and no timer");
        log.IsTrue(maps.Exists(e => e.fields[0] == "World" && Mathf.Approximately(e.duration, 6f) && e.entryDirectionOverride == EntryDirection.FromRight),
            "a timed tag keeps its duration and ,Right");
        log.IsTrue(maps.Exists(e => e.fields[0] == "Asia" && Mathf.Approximately(e.triggerTime, 12.5f)), "a stamped tag fires at its T=");

        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(MapTag), "the TTS processor recognises the tag");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Map:End}"), "…and its End");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Map:\"Europe\",\"BE\",\"three fields\",Start}"), "…but not a malformed one");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(MapTag) && !HybridAvatarSystem.EmotionTagRegex.IsMatch("{Map:End}"),
            "the emotion parser can't mistake it for an emotion");
        log.IsTrue(!ContentZoneController.IsFeatureCard(ContentCardType.Map), "Map is a side card (hidden at Center)");
    }

    static void Map_StampUnstampRoundTrip(Log log)
    {
        string script =
            "## MAP\n" +
            "At least eight more countries. " + MapTag + " AFP counted them.\n" +
            "That's the list. {Map:End} Next.\n" +
            "{Map:\"World\",\"US+CN\",\"Two\",\"Src\",6} And timed.\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains("Nov. 2\",T=") && section.TimedScript.Contains(",D=0,Left}") && section.TimedScript.Contains(",D=6}"),
                "stamping gives each Map a T= and D=, keeping its side");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "Unstamp(Stamp(x)) gives back the Map tags as written");
        }
    }

    static void Map_CodeRules(Log log)
    {
        if (!MapData.Available) return;
        MapData.Region europe = MapData.GetRegion("Europe");
        MapData.CodeCheck afp = MapData.CheckCodes(europe, "BE+LU+SE+IE+NL+EE+FI+PT");
        log.AreEqual("BE+LU+SE+IE+NL+EE+FI+PT", string.Join("+", afp.Lit), "codes: AFP's eight all light, in the tag's order");
        MapData.CodeCheck mixed = MapData.CheckCodes(europe, " be + XX+US+lu+BE ");
        log.AreEqual("BE+LU", string.Join("+", mixed.Lit), "codes: spaces and lower case are fine; a repeat lights once");
        log.AreEqual("XX", string.Join("+", mixed.Unknown), "codes: an unknown code is reported, not guessed");
        log.AreEqual("US", string.Join("+", mixed.OutOfView), "codes: a real country outside the view is reported apart");
        log.AreEqual(0, MapData.CheckCodes(europe, "").Lit.Count, "codes: an empty list lights nothing");

        // The parser warns once per problem code.
        var warnings = new List<string>();
        void OnLog(string m, string st, LogType ty) { if (ty == LogType.Warning) warnings.Add(m); }
        Application.logMessageReceived += OnLog;
        try { ContentZoneTagParser.ParseContentTags("{Map:\"Europe\",\"BE+XX+US\",\"c\",\"s\",Start}", 10f); }
        finally { Application.logMessageReceived -= OnLog; }
        log.IsTrue(warnings.Exists(w => w.Contains("\"XX\" isn't a country code")), "parser: an unknown code is warned about");
        log.IsTrue(warnings.Exists(w => w.Contains("US is outside the Europe view")), "parser: a code outside the view is warned about");
    }

    static void Map_Timeline(Log log)
    {
        log.Near(0.3f, MapTypography.LightStart(0), 1e-6f, "the first country lights 0.3 s after the card lands");
        log.Near(0.1f, MapTypography.LightStart(1) - MapTypography.LightStart(0), 1e-6f, "…then one every 0.1 s");
        log.AreEqual(0f, MapTypography.LitAt(0f), "a country starts unlit");
        log.Near(1f, MapTypography.LitAt(MapTypography.LightSeconds), 1e-6f, "…and is lit 0.3 s later");
        log.IsTrue(MapTypography.PulseAt(MapTypography.PulseSeconds * 0.5f) > 0.3f && MapTypography.PulseAt(MapTypography.PulseSeconds) == 0f,
            "a soft pulse brightens it, then lets go");
        float peak = 0f;
        for (float t = 0f; t < MapTypography.DotPopSeconds; t += 0.002f) peak = Mathf.Max(peak, MapTypography.DotScaleAt(t));
        log.IsTrue(peak > 1.02f && peak < 1.25f && MapTypography.DotScaleAt(MapTypography.DotPopSeconds) == 1f, $"a small country's dot pops ({peak:F2}×) and settles");
        Rect fit = MapTypography.FitMap(new Rect(0, 0, 860, 500), 2f);
        log.IsTrue(Mathf.Approximately(fit.width, 860f) && Mathf.Approximately(fit.height, 430f) && Mathf.Approximately(fit.y, 35f),
            "a wide map fills the box's width, centred in its height");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    static MapCard BuildMap(string region, string codes, string caption, string source, out GameObject root, bool run = true)
    {
        var card = BuildCard<MapCard>(out root);
        card.Initialize(new ContentCardEvent { cardType = ContentCardType.Map, fields = new[] { region, codes, caption, source }, primaryText = caption }, null);
        if (run)
        {
            card.BeginTimelineForTest();
            for (int f = 0; f < 150; f++) card.Advance(1f / 30f);
        }
        return card;
    }

    static void CheckMap(Log log, MapCard card, string what)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        Rect inner = InnerRect(card);
        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name), $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");

        Rect map = RectInCard(card.MapArea, rt);
        log.IsTrue(Inside(map, inner, 1f), $"{what}: the map is inside the padding ({Fmt(map)})");
        log.Near(card.Region.Aspect, map.width / map.height, 0.01f, $"{what}: the map keeps its region's shape");
        log.IsTrue(card.MapArea.GetComponent<UnityEngine.UI.RectMask2D>() != null, $"{what}: countries past the view are clipped to it");
        log.IsTrue(card.Land.GetComponent<CanvasRenderer>() != null && card.Borders.GetComponent<CanvasRenderer>() != null,
            $"{what}: the map graphics have their CanvasRenderers");
        Canvas.ForceUpdateCanvases(); // builds the meshes
        log.IsTrue(card.Land.VertexCount > 0 && card.Borders.VertexCount > 0, $"{what}: land and borders drawn ({card.Land.VertexCount} + {card.Borders.VertexCount} vertices)");
        log.AreEqual(card.Land.PlannedVertexCount, card.Land.VertexCount, $"{what}: every country's land is in the mesh");
        log.IsTrue(card.Land.PlannedVertexCount < MapFillGraphic.MaxVertices && card.Borders.PlannedVertexCount < MapFillGraphic.MaxVertices,
            $"{what}: every mesh fits a UI mesh ({card.Land.PlannedVertexCount} / {card.Borders.PlannedVertexCount})");

        foreach (TMP_Text t in new TMP_Text[] { card.Caption, card.Source })
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f && !t.isTextTruncated, $"{what}: {t.name} readable ({t.fontSize:F0} px)");
            Rect ink = CardTypography.InkInCard(t, rt);
            log.IsTrue(Inside(ink, inner, 1f), $"{what}: {t.name} inside the padding");
            log.IsTrue(ink.yMin >= map.yMax - 0.5f, $"{what}: {t.name} sits below the map");
        }
        card.Caption.ForceMeshUpdate(true, true);
        log.IsTrue(card.Caption.text.Length == 0 || card.Caption.textInfo.lineCount <= MapTypography.CaptionMaxLines, $"{what}: caption on at most two lines");

        Color accent = ContentCardUIBuilder.AccentColor;
        for (int i = 0; i < card.LitCount; i++)
        {
            Color c = card.Lit(i).color;
            log.IsTrue(Mathf.Abs(c.r - accent.r) < 0.01f && Mathf.Abs(c.g - accent.g) < 0.01f && Mathf.Abs(c.b - accent.b) < 0.01f && c.a > 0.99f,
                $"{what}: {card.LitIso(i)} is lit in the accent");
            log.IsTrue(card.Lit(i).transform.GetSiblingIndex() > card.Land.transform.GetSiblingIndex() &&
                       card.Lit(i).transform.GetSiblingIndex() < card.Borders.transform.GetSiblingIndex(),
                $"{what}: {card.LitIso(i)} drawn over the land, under the borders");
            if (card.HasDot(i))
            {
                log.Near(1f, card.Dot(i).rectTransform.localScale.x, 1e-4f, $"{what}: {card.LitIso(i)}'s dot has popped");
                Rect dot = RectInCard(card.Dot(i).rectTransform, rt);
                log.IsTrue(map.Contains(dot.center), $"{what}: {card.LitIso(i)}'s dot is on the map");
            }
        }
        var dotted = new List<string>();
        for (int i = 0; i < card.LitCount; i++) if (card.HasDot(i)) dotted.Add(card.LitIso(i));
        Debug.Log($"[Map] self-test {what}: {card.Region.Name} {map.width:F0}×{map.height:F0} px, {card.LitCount} lit, dots: {string.Join("+", dotted)}" +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Map_EveryRegionRenders(Log log)
    {
        if (!MapData.Available) return;
        var codes = new Dictionary<string, string>
        {
            { "World", "US+CN+BR" }, { "Europe", "FR" }, { "NorthAmerica", "MX+CA" }, { "SouthAmerica", "AR+CL" },
            { "Africa", "NG+ZA+LS" }, { "MiddleEast", "IR+IL" }, { "Asia", "JP+IN" }, { "Oceania", "AU+NZ+FJ+TO" },
        };
        foreach (string name in MapRegions.Names)
        {
            var card = BuildMap(name, codes[name], $"{name} test", "Natural Earth", out GameObject root);
            try
            {
                CheckMap(log, card, $"Map ({name})");
                log.AreEqual(codes[name].Split('+').Length, card.LitCount, $"Map ({name}): every listed code lit");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void Map_CardLayout(Log log)
    {
        if (!MapData.Available) return;
        // 1, 8 and 30 codes.
        var one = BuildMap("Europe", "FR", "France", "Src", out GameObject r1);
        try { CheckMap(log, one, "Map (1 code)"); log.AreEqual(1, one.LitCount, "Map (1 code): one lit"); }
        finally { UnityEngine.Object.DestroyImmediate(r1); }

        var afp = BuildMap("Europe", "BE+LU+SE+IE+NL+EE+FI+PT", "At least 8 more countries", "AFP, Nov. 2", out GameObject r8);
        try
        {
            CheckMap(log, afp, "Map (AFP's 8)");
            log.AreEqual(8, afp.LitCount, "Map (AFP's 8): all eight lit");
            int lu = -1; for (int i = 0; i < afp.LitCount; i++) if (afp.LitIso(i) == "LU") lu = i;
            log.IsTrue(lu >= 0 && afp.HasDot(lu), "Map (AFP's 8): Luxembourg, too small to see, gets a lit dot");
            int se = -1; for (int i = 0; i < afp.LitCount; i++) if (afp.LitIso(i) == "SE") se = i;
            log.IsTrue(se >= 0 && !afp.HasDot(se), "Map (AFP's 8): Sweden is big enough without one");
        }
        finally { UnityEngine.Object.DestroyImmediate(r8); }

        string thirty = "US+CA+MX+BR+AR+CL+PE+CO+GB+FR+DE+ES+IT+PL+UA+RU+TR+EG+NG+ZA+KE+ET+SA+IR+IN+CN+JP+ID+AU+NZ";
        var many = BuildMap("World", thirty, "Thirty countries on one map, all lit one after another", "Natural Earth", out GameObject r30);
        try { CheckMap(log, many, "Map (30 codes)"); log.AreEqual(30, many.LitCount, "Map (30 codes): all thirty lit"); }
        finally { UnityEngine.Object.DestroyImmediate(r30); }

        // Unknown and out-of-view codes are skipped by the card too (the parser warned).
        var mixed = BuildMap("Europe", "BE+XX+US+NL", "Mixed", "Src", out GameObject rm);
        try { log.AreEqual("BE+NL", mixed.LitIso(0) + "+" + mixed.LitIso(1), "Map (mixed): only the codes in view light"); log.AreEqual(2, mixed.LitCount, "…two of them"); }
        finally { UnityEngine.Object.DestroyImmediate(rm); }

        var none = BuildMap("Africa", "", "", "", out GameObject rn);
        try { CheckMap(log, none, "Map (no codes, no caption)"); log.AreEqual(0, none.LitCount, "Map (no codes): the map shows, unlit"); }
        finally { UnityEngine.Object.DestroyImmediate(rn); }
    }

    // AFP's eight at 30 fps: nothing lit before 0.3 s; country i starts at 0.3 + 0.1·i, brightens past the accent, settles on it.
    static void Map_LightUpFrameByFrame(Log log)
    {
        if (!MapData.Available) return;
        var card = BuildMap("Europe", "BE+LU+SE+IE+NL+EE+FI+PT", "At least 8 more countries", "AFP, Nov. 2", out GameObject root, run: false);
        try
        {
            Color unlit = card.LandColor, accent = ContentCardUIBuilder.AccentColor;
            log.IsTrue(card.Lit(0).color == unlit, "before landing: nothing lit");
            card.BeginTimelineForTest();
            const float dt = 1f / 30f;
            int n = card.LitCount;
            var startedAt = new float[n]; var pulsed = new bool[n];
            for (int i = 0; i < n; i++) startedAt[i] = float.NaN;
            float t = 0f;
            for (int f = 0; f < 90; f++)
            {
                card.Advance(dt); t += dt;
                for (int i = 0; i < n; i++)
                {
                    Color c = card.Lit(i).color;
                    if (float.IsNaN(startedAt[i]) && c != unlit) startedAt[i] = t;
                    if (c.r > accent.r + 0.02f || c.g > accent.g + 0.02f || c.b > accent.b + 0.02f) pulsed[i] = true;
                }
            }
            for (int i = 0; i < n; i++)
            {
                float want = MapTypography.LightStart(i);
                log.IsTrue(startedAt[i] > want - 1e-3f && startedAt[i] <= want + dt + 1e-3f,
                    $"{card.LitIso(i)} starts lighting at {want:F1} s ({startedAt[i]:F3})");
                log.IsTrue(pulsed[i], $"…with a soft pulse brighter than the accent");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}

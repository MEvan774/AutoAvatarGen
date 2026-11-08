using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using MugsTech.Style;

/// <summary>
/// Static parser that extracts content card tags from a video script and builds
/// a timeline of ContentCardEvents. Prefers exact T=X.XXX timestamps baked in
/// by the ElevenLabs pre-processor; falls back to character-proportional timing
/// for tags without a T= value.
/// </summary>
public static class ContentZoneTagParser
{
    // Combined pattern that matches ALL content card tags (both legacy and
    // pre-processed forms) — used to compute totalCleanChars and to strip.
    // Pre-processed form has ",T=X.XXX,D=Y"; legacy form has just ",Y".
    // Headline supports an optional trailing ",bigCenter" modifier that promotes
    // the card to the fullscreen BigCenter variant. The side cards
    // (Headline/Excerpt/Quote/Stat/Logo/BRoll) also accept an optional trailing
    // ",Left"/",Right" picking the side they slide in from. The fullscreen
    // feature cards (BigMedia/BigText/BigImage) take no side modifier.
    // The side cards' duration slot also accepts the "Start" keyword — the held
    // pair form ({Quote:...,Start}…{Quote:End}), mirroring
    // {Black:Start}…{Black:End}; the bare {Tag:End} closing edge is its own
    // alternative below. (The TTS processors stamp a held opener as D=0, so the
    // keyword only reaches this parser in legacy/unstamped scripts.) A
    // ",bigCenter" headline holds the same way, closed by the same {Headline:End}.
    // Stat may carry an arrow keyword (",Win"/",Loss"/",NoArrow") as well as a
    // side, in either order — hence up to two trailing modifiers.
    private static readonly Regex StripAllRegex = new Regex(
        @"\{(?:Headline|Excerpt|Quote|Stat):""[^""]*""(?:,""[^""]*"")*(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:bigCenter|Win|Loss|NoArrow|Left|Right)){0,2}\}" +
        @"|\{(?:Logo|BRoll):[^,}]+(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
        @"|\{(?:Headline|Excerpt|Quote|Stat|Logo|BRoll|Compare|Versus|Document|Timeline|Map):End(?:,T=\d+(?:\.\d+)?)?\}" +
        // {Compare:"value A","label A","value B","label B","source",duration|Start[,Left|,Right]}
        // — and, after it, any other {Compare:…} (malformed: stripped, warned, skipped).
        @"|\{Compare:""[^""]*""(?:,""[^""]*""){4}(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
        @"|\{Compare:[^}\n]*\}" +
        // {Versus:"left label","left text","right label","right text",duration|Start[,Vs]}
        // — and, after it, any other {Versus:…} (malformed: stripped, warned, skipped).
        @"|\{Versus:""[^""]*""(?:,""[^""]*""){3}(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*Vs)?\}" +
        @"|\{Versus:[^}\n]*\}" +
        // {Document:"passage","phrase","source",duration|Start[,Allegation|,Filing|,Ruling|,Report][,Left|,Right]}
        // — and, after it, any other {Document:…} (malformed: stripped, warned, skipped).
        @"|\{Document:""[^""]*""(?:,""[^""]*""){2}(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:Allegation|Filing|Ruling|Report))?(?:,\s*(?:Left|Right))?\}" +
        @"|\{Document:[^}\n]*\}" +
        // {Timeline:"date","event"[,Left|,Right]} — one node, no duration (stamped T=…,D=0) —
        // and, after it, any other {Timeline:…} (malformed: stripped, warned, skipped).
        @"|\{Timeline:""[^""]*"",""[^""]*""(?:,T=\d+(?:\.\d+)?)?(?:,D=0)?(?:,\s*(?:Left|Right))?\}" +
        @"|\{Timeline:[^}\n]*\}" +
        // {Map:"region","countries","caption","source",duration|Start[,Left|,Right]}
        // — and, after it, any other {Map:…} (malformed: stripped, warned, skipped).
        @"|\{Map:""[^""]*""(?:,""[^""]*""){3}(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
        @"|\{Map:[^}\n]*\}" +
        @"|\{(?:BigMedia|BigImage):[^,}]+(?:,T=\d+(?:\.\d+)?)?,(?:D=)?\d+(?:\.\d+)?\}" +
        // BigText's duration is OPTIONAL — the duration-less form is the
        // persistent line-by-line flow ({BigText:LINE}…{BigText:End}).
        @"|\{BigText:[^,}]+(?:,T=\d+(?:\.\d+)?)?(?:,(?:D=)?\d+(?:\.\d+)?)?\}");

    // Individual extraction patterns. Each accepts an optional ",T=X.XXX"
    // between the content fields and the duration, and an optional "D=" prefix
    // on the duration itself. Headline also accepts an optional ",bigCenter"
    // modifier after the duration. The side cards each accept an optional
    // trailing ",Left"/",Right" (the last capture group) that picks the side the
    // card slides in from — null/absent keeps the default (Left).
    private static readonly Regex HeadlineRegex = new Regex(
        @"\{Headline:""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(bigCenter))?(?:,\s*(Left|Right))?\}");

    private static readonly Regex ExcerptRegex = new Regex(
        @"\{Excerpt:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    private static readonly Regex QuoteRegex = new Regex(
        @"\{Quote:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    // Stat's last capture group is its whole modifier tail rather than just the
    // side: an optional arrow keyword (",Win"/",Loss"/",NoArrow") and an optional
    // side, accepted in either order. ParseStatModifiers splits it.
    private static readonly Regex StatRegex = new Regex(
        @"\{Stat:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)((?:,\s*(?:Win|Loss|NoArrow|Left|Right)){0,2})\}");

    private static readonly Regex LogoRegex = new Regex(
        @"\{Logo:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    private static readonly Regex BRollRegex = new Regex(
        @"\{BRoll:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    // The held pair's closing edge — {Headline:End}, {Quote:End}, {Logo:End}, …
    // — closes the matching held card ({Tag:...,Start}), the same in-tag/out-tag
    // principle as {Black:Start}…{Black:End} and {BigText:LINE}…{BigText:End}.
    private static readonly Regex CardEndRegex = new Regex(
        @"\{(Headline|Excerpt|Quote|Stat|Logo|BRoll|Compare|Versus|Document|Timeline|Map):End(?:,T=(\d+(?:\.\d+)?))?\}");

    // {Compare:"value A","label A","value B","label B","source",duration|Start[,Left|,Right]}
    private static readonly Regex CompareRegex = new Regex(
        @"\{Compare:""([^""]+)"",""([^""]+)"",""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    // Any {Compare:…} at all — the ones CompareRegex doesn't take are malformed.
    private static readonly Regex CompareAnyRegex = new Regex(@"\{Compare:(?!End\b)[^}\n]*\}");

    // {Versus:"left label","left text","right label","right text",duration|Start[,Vs]}
    // — a fullscreen feature card, so no side.
    private static readonly Regex VersusRegex = new Regex(
        @"\{Versus:""([^""]+)"",""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Vs))?\}");

    // Any {Versus:…} at all — the ones VersusRegex doesn't take are malformed.
    private static readonly Regex VersusAnyRegex = new Regex(@"\{Versus:(?!End\b)[^}\n]*\}");

    // {Document:"passage","phrase to highlight","source",duration|Start[,Allegation|,Filing|,Ruling|,Report][,Left|,Right]}
    // The same first three fields as {Excerpt:}. The phrase and the source may
    // be empty (no highlight / an empty header); the passage may not.
    private static readonly Regex DocumentRegex = new Regex(
        @"\{Document:""([^""]+)"",""([^""]*)"",""([^""]*)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Allegation|Filing|Ruling|Report))?(?:,\s*(Left|Right))?\}");

    // Any {Document:…} at all — the ones DocumentRegex doesn't take are malformed.
    private static readonly Regex DocumentAnyRegex = new Regex(@"\{Document:(?!End\b)[^}\n]*\}");

    // {Timeline:"date","event"[,Left|,Right]} — one node of a line-by-line
    // timeline (like {BigText:LINE}): no duration; the first node opens the
    // card and its side applies, each later one adds a node, {Timeline:End}
    // closes it. Stamped as T=…,D=0 with the side after it.
    private static readonly Regex TimelineRegex = new Regex(
        @"\{Timeline:""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?(?:,D=0)?(?:,\s*(Left|Right))?\}");

    // Any {Timeline:…} at all — the ones TimelineRegex doesn't take are malformed.
    private static readonly Regex TimelineAnyRegex = new Regex(@"\{Timeline:(?!End\b)[^}\n]*\}");

    // {Map:"region","countries","caption","source",duration|Start[,Left|,Right]}
    private static readonly Regex MapRegex = new Regex(
        @"\{Map:""([^""]+)"",""([^""]*)"",""([^""]*)"",""([^""]*)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    // Any {Map:…} at all — the ones MapRegex doesn't take are malformed.
    private static readonly Regex MapAnyRegex = new Regex(@"\{Map:(?!End\b)[^}\n]*\}");

    private static readonly Regex BigMediaRegex = new Regex(
        @"\{BigMedia:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    // Duration optional (unlike every other card): {BigText:LINE} with no
    // duration is a persistent line — it opens/joins an on-screen stack that
    // stays up until {BigText:End}. With a duration it's the classic timed card.
    private static readonly Regex BigTextRegex = new Regex(
        @"\{BigText:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?(?:,(?:D=)?(\d+(?:\.\d+)?))?\}");

    private static readonly Regex BigImageRegex = new Regex(
        @"\{BigImage:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    /// <summary>
    /// Parses all content card tags from the script, builds timed events, and returns
    /// the cleaned script with all card tags stripped.
    /// </summary>
    public static (string, List<ContentCardEvent>) ParseContentTags(string script, float audioDuration)
    {
        List<ContentCardEvent> events = new List<ContentCardEvent>();

        // Compute total clean character count with ALL card tags removed
        string fullyCleanScript = StripAllRegex.Replace(script, "");
        int totalCleanChars = Mathf.Max(1, fullyCleanScript.Length);

        // Extract each tag type
        ExtractHeadlines(script, audioDuration, totalCleanChars, events);
        ExtractExcerpts(script, audioDuration, totalCleanChars, events);
        ExtractQuotes(script, audioDuration, totalCleanChars, events);
        ExtractStats(script, audioDuration, totalCleanChars, events);
        ExtractLogos(script, audioDuration, totalCleanChars, events);
        ExtractBRolls(script, audioDuration, totalCleanChars, events);
        ExtractCardEnds(script, audioDuration, totalCleanChars, events);
        ExtractBigMedias(script, audioDuration, totalCleanChars, events);
        ExtractBigTexts(script, audioDuration, totalCleanChars, events);
        ExtractBigImages(script, audioDuration, totalCleanChars, events);
        ExtractCompares(script, audioDuration, totalCleanChars, events);
        ExtractVersus(script, audioDuration, totalCleanChars, events);
        ExtractDocuments(script, audioDuration, totalCleanChars, events);
        ExtractTimelines(script, audioDuration, totalCleanChars, events);
        ExtractMaps(script, audioDuration, totalCleanChars, events);

        // Sort by trigger time
        events.Sort((a, b) => a.triggerTime.CompareTo(b.triggerTime));

        // Strip all card tags from the script
        string cleanScript = StripAllRegex.Replace(script, "");

        Debug.Log($"ContentZoneTagParser: Found {events.Count} content card events");
        return (cleanScript, events);
    }

    // Returns T=X.XXX from the given group if present/parseable; otherwise
    // falls back to character-proportional timing.
    private static float ResolveTriggerTime(string script, int matchIndex, Group tsGroup,
                                            float audioDuration, int totalCleanChars)
    {
        if (tsGroup != null && tsGroup.Success &&
            float.TryParse(tsGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
            return t;

        string textBefore = script.Substring(0, matchIndex);
        string cleanBefore = StripAllRegex.Replace(textBefore, "");
        return (cleanBefore.Length / (float)totalCleanChars) * audioDuration;
    }

    private static float ParseFloat(string s)
    {
        return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // The side cards' (and the big headline's) duration slot: a number, or the
    // "Start" keyword — the held pair's opening edge, which parses as 0 (the same "no timer" value a
    // stamped D=0 carries; duration <= 0 is what ContentZoneController treats
    // as a held card).
    private static float ParseDuration(string s)
    {
        return s == "Start" ? 0f : ParseFloat(s);
    }

    // Translates an optional ",Left"/",Right" capture group into a forced entry
    // direction. Returns null when the group is absent so the card keeps its
    // default side (the CardEntryAnimator's per-card direction / runtime choice).
    private static EntryDirection? ParseSide(Group sideGroup)
    {
        if (sideGroup == null || !sideGroup.Success) return null;
        return sideGroup.Value == "Right" ? EntryDirection.FromRight : EntryDirection.FromLeft;
    }

    // Splits a Stat tag's modifier tail (",Win,Right" / ",Left,Loss" / ",NoArrow"
    // / "") into its arrow and its side. No arrow keyword = no arrow.
    private static void ParseStatModifiers(string tail, out StatArrow arrow, out EntryDirection? side)
    {
        arrow = StatArrow.None;
        side = null;
        if (string.IsNullOrEmpty(tail)) return;

        foreach (string raw in tail.Split(','))
        {
            switch (raw.Trim())
            {
                case "Win":   arrow = StatArrow.Win; break;
                case "Loss":  arrow = StatArrow.Loss; break;
                case "Left":  side = EntryDirection.FromLeft; break;
                case "Right": side = EntryDirection.FromRight; break;
            }
        }
    }

    private static void ExtractHeadlines(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in HeadlineRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[3], audioDuration, totalCleanChars);
            bool isBigCenter = match.Groups[5].Success;
            ContentCardType type = isBigCenter ? ContentCardType.BigCenter : ContentCardType.Headline;
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = type,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                duration = ParseDuration(match.Groups[4].Value),
                // bigCenter is a centered feature card, so a side has no meaning there.
                entryDirectionOverride = isBigCenter ? null : ParseSide(match.Groups[6])
            });
            Debug.Log($"  {type} at {time:F2}s: \"{match.Groups[1].Value}\"");
        }
    }

    private static void ExtractExcerpts(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in ExcerptRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[4], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Excerpt,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = ParseDuration(match.Groups[5].Value),
                entryDirectionOverride = ParseSide(match.Groups[6])
            });
            Debug.Log($"  Excerpt at {time:F2}s: highlight=\"{match.Groups[2].Value}\"");
        }
    }

    private static void ExtractQuotes(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in QuoteRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[4], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Quote,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = ParseDuration(match.Groups[5].Value),
                entryDirectionOverride = ParseSide(match.Groups[6])
            });
            Debug.Log($"  Quote at {time:F2}s: by {match.Groups[2].Value}");
        }
    }

    // {Compare:…} → a side card with two bars. Warns once — quoting the tag —
    // when the tag is malformed (skipped), when its values can't share a scale
    // (the card shows them side by side, no bars), or when one bar would be
    // thinner than the 3 % sliver it is drawn as.
    private static void ExtractCompares(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        var taken = new HashSet<int>();
        foreach (Match match in CompareRegex.Matches(script))
        {
            taken.Add(match.Index);
            float time = ResolveTriggerTime(script, match.Index, match.Groups[6], audioDuration, totalCleanChars);
            var fields = new[] { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value,
                                 match.Groups[4].Value, match.Groups[5].Value };
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Compare,
                primaryText = fields[0],
                secondaryText = fields[2],
                tertiaryText = fields[4],
                fields = fields,
                duration = ParseDuration(match.Groups[7].Value),
                entryDirectionOverride = ParseSide(match.Groups[8]),
            });
            Debug.Log($"  Compare at {time:F2}s: {fields[0]} vs {fields[2]}");

            CompareMath.Bars bars = CompareMath.Ratios(fields[0], fields[2]);
            if (!bars.Ok)
                Debug.LogWarning($"[Card] Compare {Quote(match.Value)} at {time:F2}s: {bars.Problem} — " +
                                 "showing both values side by side, no bars.");
            else if (bars.SliverA || bars.SliverB)
                Debug.LogWarning($"[Card] Compare {Quote(match.Value)} at {time:F2}s: " +
                                 $"\"{(bars.SliverA ? fields[0] : fields[2])}\" is under 3 % of the other — drawn as a 3 % sliver.");
        }

        foreach (Match match in CompareAnyRegex.Matches(script))
        {
            if (taken.Contains(match.Index)) continue;
            Debug.LogWarning($"[Card] Compare {Quote(match.Value)} isn't valid — it needs five non-empty quoted fields " +
                             "(value A, label A, value B, label B, source), then a duration or Start, then an optional " +
                             ",Left/,Right. Skipped.");
        }
    }

    // {Versus:…} → the fullscreen claim-vs-evidence card. A malformed tag is
    // skipped with one warning that quotes it.
    private static void ExtractVersus(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        var taken = new HashSet<int>();
        foreach (Match match in VersusRegex.Matches(script))
        {
            taken.Add(match.Index);
            float time = ResolveTriggerTime(script, match.Index, match.Groups[5], audioDuration, totalCleanChars);
            var fields = new[] { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value };
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Versus,
                primaryText = fields[1],
                secondaryText = fields[3],
                fields = fields,
                duration = ParseDuration(match.Groups[6].Value),
                versusVs = match.Groups[7].Success,
            });
            Debug.Log($"  Versus at {time:F2}s: \"{fields[1]}\" vs \"{fields[3]}\"" + (match.Groups[7].Success ? " (VS)" : ""));
        }

        foreach (Match match in VersusAnyRegex.Matches(script))
        {
            if (taken.Contains(match.Index)) continue;
            Debug.LogWarning($"[Card] Versus {Quote(match.Value)} isn't valid — it needs four non-empty quoted fields " +
                             "(left label, left text, right label, right text), then a duration or Start, then an " +
                             "optional ,Vs (it is fullscreen, so no ,Left/,Right). Skipped.");
        }
    }

    // {Document:…} → a side card that looks like paper, with a marker sweep
    // over the phrase and an optional stamp. Warns once — quoting the tag —
    // when it is malformed (skipped) or when the phrase isn't in the passage
    // (shown with no highlight; the same exact-match rule as {Excerpt:}).
    private static void ExtractDocuments(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        var taken = new HashSet<int>();
        foreach (Match match in DocumentRegex.Matches(script))
        {
            taken.Add(match.Index);
            float time = ResolveTriggerTime(script, match.Index, match.Groups[4], audioDuration, totalCleanChars);
            DocumentStamp stamp = match.Groups[6].Success
                ? (DocumentStamp)System.Enum.Parse(typeof(DocumentStamp), match.Groups[6].Value)
                : DocumentStamp.None;
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Document,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = ParseDuration(match.Groups[5].Value),
                documentStamp = stamp,
                entryDirectionOverride = ParseSide(match.Groups[7]),
            });
            Debug.Log($"  Document at {time:F2}s: highlight=\"{match.Groups[2].Value}\"" +
                      (stamp != DocumentStamp.None ? $", stamp {stamp}" : ""));

            string phrase = match.Groups[2].Value;
            if (phrase.Length > 0 && match.Groups[1].Value.IndexOf(phrase, System.StringComparison.Ordinal) < 0)
                Debug.LogWarning($"[Card] Document {Quote(match.Value)} at {time:F2}s: the phrase \"{phrase}\" isn't in the " +
                                 "passage (it must match exactly, as with Excerpt) — shown with no highlight.");
        }

        foreach (Match match in DocumentAnyRegex.Matches(script))
        {
            if (taken.Contains(match.Index)) continue;
            Debug.LogWarning($"[Card] Document {Quote(match.Value)} isn't valid — it needs three quoted fields (a non-empty " +
                             "passage, the phrase to highlight, the source), then a duration or Start, then an optional " +
                             "stamp (,Allegation ,Filing ,Ruling ,Report), then an optional ,Left/,Right. Skipped.");
        }
    }

    // {Timeline:…} → one node each. Which node opens a card, and the
    // 5-node / later-side / cleared-before-End rules, are the controller's —
    // they depend on what is on screen. A malformed tag is skipped with one
    // warning that quotes it.
    private static void ExtractTimelines(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        var taken = new HashSet<int>();
        foreach (Match match in TimelineRegex.Matches(script))
        {
            taken.Add(match.Index);
            float time = ResolveTriggerTime(script, match.Index, match.Groups[3], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Timeline,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                duration = 0f, // held: open until {Timeline:End}
                entryDirectionOverride = ParseSide(match.Groups[4]),
            });
            Debug.Log($"  Timeline node at {time:F2}s: \"{match.Groups[1].Value}\" — \"{match.Groups[2].Value}\"");
        }

        foreach (Match match in TimelineAnyRegex.Matches(script))
        {
            if (taken.Contains(match.Index)) continue;
            Debug.LogWarning($"[Card] Timeline {Quote(match.Value)} isn't valid — it needs two non-empty quoted fields " +
                             "(date, event), then an optional ,Left/,Right — no duration. Skipped.");
        }
    }

    // {Map:…} → a side card with a map. An unknown region skips the card;
    // every code is checked against the baked Natural Earth data here, once —
    // an unknown code, or one outside the region's view, is warned about and
    // skipped (never guessed); the card lights only the rest.
    private static void ExtractMaps(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        var taken = new HashSet<int>();
        foreach (Match match in MapRegex.Matches(script))
        {
            taken.Add(match.Index);
            float time = ResolveTriggerTime(script, match.Index, match.Groups[5], audioDuration, totalCleanChars);
            string regionName = match.Groups[1].Value;
            if (MapRegions.Find(regionName) == null)
            {
                Debug.LogWarning($"[Card] Map {Quote(match.Value)}: \"{regionName}\" isn't a region — use one of " +
                                 $"{string.Join(", ", MapRegions.Names)}. Skipped.");
                continue;
            }
            MapData.Region region = MapData.GetRegion(regionName);
            if (region == null)
            {
                Debug.LogWarning($"[Card] Map {Quote(match.Value)}: no baked map data for {regionName} — skipped.");
                continue;
            }
            var fields = new[] { regionName, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value };
            MapData.CodeCheck check = MapData.CheckCodes(region, fields[1]);
            foreach (string code in check.Unknown)
                Debug.LogWarning($"[Card] Map {Quote(match.Value)} at {time:F2}s: \"{code}\" isn't a country code — skipped.");
            foreach (string code in check.OutOfView)
                Debug.LogWarning($"[Card] Map {Quote(match.Value)} at {time:F2}s: {code} is outside the {regionName} view — skipped.");
            if (check.Lit.Count == 0)
                Debug.LogWarning($"[Card] Map {Quote(match.Value)} at {time:F2}s: no country to light — the map is shown unlit.");

            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Map,
                primaryText = fields[2],
                secondaryText = fields[3],
                fields = fields,
                duration = ParseDuration(match.Groups[6].Value),
                entryDirectionOverride = ParseSide(match.Groups[7]),
            });
            Debug.Log($"  Map at {time:F2}s: {regionName}, {check.Lit.Count} lit ({string.Join("+", check.Lit)})");
        }

        foreach (Match match in MapAnyRegex.Matches(script))
        {
            if (taken.Contains(match.Index)) continue;
            Debug.LogWarning($"[Card] Map {Quote(match.Value)} isn't valid — it needs four quoted fields (a region, the " +
                             "country codes joined with +, the caption, the source), then a duration or Start, then an " +
                             "optional ,Left/,Right. Skipped.");
        }
    }

    static string Quote(string tag) => tag.Length <= 90 ? tag : tag.Substring(0, 87) + "…}";

    private static void ExtractStats(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in StatRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[4], audioDuration, totalCleanChars);
            ParseStatModifiers(match.Groups[6].Value, out StatArrow arrow, out EntryDirection? side);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Stat,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = ParseDuration(match.Groups[5].Value),
                entryDirectionOverride = side,
                statArrow = arrow
            });
            Debug.Log($"  Stat at {time:F2}s: {match.Groups[1].Value} (arrow: {arrow})");
        }
    }

    private static void ExtractLogos(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in LogoRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            string name = match.Groups[1].Value.Trim();

            // "End" is the reserved closing keyword (see CardEndRegex), never a
            // company name — a stray duration on it ({Logo:End,4}) still closes.
            if (name.Equals("End", System.StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new ContentCardEvent
                {
                    triggerTime = time,
                    cardType = ContentCardType.Logo,
                    dismissesCard = true
                });
                Debug.Log($"  Logo End at {time:F2}s");
                continue;
            }

            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Logo,
                primaryText = name,
                duration = ParseDuration(match.Groups[3].Value),
                entryDirectionOverride = ParseSide(match.Groups[4])
            });
            Debug.Log($"  Logo at {time:F2}s: {name}");
        }
    }

    private static void ExtractBRolls(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BRollRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            string name = match.Groups[1].Value.Trim();

            // "End" is the reserved closing keyword (see CardEndRegex), never a
            // clip description — a stray duration on it still closes.
            if (name.Equals("End", System.StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new ContentCardEvent
                {
                    triggerTime = time,
                    cardType = ContentCardType.BRoll,
                    dismissesCard = true
                });
                Debug.Log($"  BRoll End at {time:F2}s");
                continue;
            }

            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BRoll,
                primaryText = name,
                duration = ParseDuration(match.Groups[3].Value),
                entryDirectionOverride = ParseSide(match.Groups[4])
            });
            Debug.Log($"  BRoll at {time:F2}s: {name}");
        }
    }

    // {Headline:End} / {Excerpt:End} / {Quote:End} / {Stat:End} / {Logo:End} /
    // {BRoll:End} — the closing edge of a held card opened with ",Start". The
    // event carries only the type and the dismiss flag; ContentZoneController
    // closes the matching active card (or warns when there is none). A
    // {Headline:End} stays a Headline event even when it closes a ",bigCenter"
    // headline — the controller matches it to the BigCenter card.
    private static void ExtractCardEnds(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in CardEndRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            ContentCardType type = (ContentCardType)System.Enum.Parse(
                typeof(ContentCardType), match.Groups[1].Value);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = type,
                dismissesCard = true
            });
            Debug.Log($"  {type} End at {time:F2}s");
        }
    }

    private static void ExtractBigMedias(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BigMediaRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BigMedia,
                primaryText = match.Groups[1].Value.Trim(),
                duration = ParseFloat(match.Groups[3].Value)
            });
            Debug.Log($"  BigMedia at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }

    private static void ExtractBigTexts(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BigTextRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            string text = match.Groups[1].Value.Trim();
            float duration = match.Groups[3].Success ? ParseFloat(match.Groups[3].Value) : 0f;

            // Duration-less "End" (canonical; Stop/Out as typo-tolerance) closes
            // the persistent stack — it is a reserved word, not displayable text.
            string lower = text.ToLowerInvariant();
            bool dismiss = duration <= 0f && (lower == "end" || lower == "stop" || lower == "out");

            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BigText,
                primaryText = text,
                duration = duration,
                dismissesCard = dismiss
            });
            Debug.Log(dismiss
                ? $"  BigText End at {time:F2}s"
                : $"  BigText at {time:F2}s: {text}" + (duration <= 0f ? " (persistent)" : ""));
        }
    }

    private static void ExtractBigImages(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BigImageRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BigImage,
                primaryText = match.Groups[1].Value.Trim(),
                duration = ParseFloat(match.Groups[3].Value)
            });
            Debug.Log($"  BigImage at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }
}

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
    // keyword only reaches this parser in legacy/unstamped scripts.)
    private static readonly Regex StripAllRegex = new Regex(
        @"\{(?:Headline|Excerpt|Quote|Stat):""[^""]*""(?:,""[^""]*"")*(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:bigCenter|Left|Right))?\}" +
        @"|\{(?:Logo|BRoll):[^,}]+(?:,T=\d+(?:\.\d+)?)?,(?:(?:D=)?\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
        @"|\{(?:Headline|Excerpt|Quote|Stat|Logo|BRoll):End(?:,T=\d+(?:\.\d+)?)?\}" +
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

    private static readonly Regex StatRegex = new Regex(
        @"\{Stat:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    private static readonly Regex LogoRegex = new Regex(
        @"\{Logo:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    private static readonly Regex BRollRegex = new Regex(
        @"\{BRoll:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?\}");

    // The held pair's closing edge — {Headline:End}, {Quote:End}, {Logo:End}, …
    // — closes the matching held card ({Tag:...,Start}), the same in-tag/out-tag
    // principle as {Black:Start}…{Black:End} and {BigText:LINE}…{BigText:End}.
    private static readonly Regex CardEndRegex = new Regex(
        @"\{(Headline|Excerpt|Quote|Stat|Logo|BRoll):End(?:,T=(\d+(?:\.\d+)?))?\}");

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

    // The side cards' duration slot: a number, or the "Start" keyword — the
    // held pair's opening edge, which parses as 0 (the same "no timer" value a
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

    private static void ExtractStats(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in StatRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[4], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Stat,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = ParseDuration(match.Groups[5].Value),
                entryDirectionOverride = ParseSide(match.Groups[6])
            });
            Debug.Log($"  Stat at {time:F2}s: {match.Groups[1].Value}");
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
    // closes the matching active card (or warns when there is none).
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

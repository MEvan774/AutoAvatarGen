using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

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
    // the card to the fullscreen BigCenter variant.
    private static readonly Regex StripAllRegex = new Regex(
        @"\{(?:Headline|Excerpt|Quote|Stat):""[^""]*""(?:,""[^""]*"")*(?:,T=\d+(?:\.\d+)?)?,(?:D=)?\d+(?:\.\d+)?(?:,\s*bigCenter)?\}" +
        @"|\{(?:Logo|BRoll|BigMedia|BigText):[^,}]+(?:,T=\d+(?:\.\d+)?)?,(?:D=)?\d+(?:\.\d+)?\}");

    // Individual extraction patterns. Each accepts an optional ",T=X.XXX"
    // between the content fields and the duration, and an optional "D=" prefix
    // on the duration itself. Headline also accepts an optional ",bigCenter"
    // modifier after the duration.
    private static readonly Regex HeadlineRegex = new Regex(
        @"\{Headline:""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)(?:,\s*(bigCenter))?\}");

    private static readonly Regex ExcerptRegex = new Regex(
        @"\{Excerpt:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex QuoteRegex = new Regex(
        @"\{Quote:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex StatRegex = new Regex(
        @"\{Stat:""([^""]+)"",""([^""]+)"",""([^""]+)""(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex LogoRegex = new Regex(
        @"\{Logo:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex BRollRegex = new Regex(
        @"\{BRoll:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex BigMediaRegex = new Regex(
        @"\{BigMedia:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

    private static readonly Regex BigTextRegex = new Regex(
        @"\{BigText:([^,}]+)(?:,T=(\d+(?:\.\d+)?))?,(?:D=)?(\d+(?:\.\d+)?)\}");

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
        ExtractBigMedias(script, audioDuration, totalCleanChars, events);
        ExtractBigTexts(script, audioDuration, totalCleanChars, events);

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
                duration = ParseFloat(match.Groups[4].Value)
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
                duration = ParseFloat(match.Groups[5].Value)
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
                duration = ParseFloat(match.Groups[5].Value)
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
                duration = ParseFloat(match.Groups[5].Value)
            });
            Debug.Log($"  Stat at {time:F2}s: {match.Groups[1].Value}");
        }
    }

    private static void ExtractLogos(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in LogoRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Logo,
                primaryText = match.Groups[1].Value.Trim(),
                duration = ParseFloat(match.Groups[3].Value)
            });
            Debug.Log($"  Logo at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }

    private static void ExtractBRolls(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BRollRegex.Matches(script))
        {
            float time = ResolveTriggerTime(script, match.Index, match.Groups[2], audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BRoll,
                primaryText = match.Groups[1].Value.Trim(),
                duration = ParseFloat(match.Groups[3].Value)
            });
            Debug.Log($"  BRoll at {time:F2}s: {match.Groups[1].Value.Trim()}");
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
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BigText,
                primaryText = match.Groups[1].Value.Trim(),
                duration = ParseFloat(match.Groups[3].Value)
            });
            Debug.Log($"  BigText at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }
}

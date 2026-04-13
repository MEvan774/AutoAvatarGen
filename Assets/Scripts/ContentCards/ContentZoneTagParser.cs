using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Static parser that extracts content card tags from a video script and builds
/// a timeline of ContentCardEvents with character-proportional timing.
/// Follows the same parsing pattern as MediaPresentationSystem's marker parsers.
/// </summary>
public static class ContentZoneTagParser
{
    // Combined pattern that matches ALL content card tags — used to compute totalCleanChars
    private static readonly Regex StripAllRegex = new Regex(
        @"\{(?:Headline|Excerpt|Quote|Stat):""[^""]*""(?:,""[^""]*"")*,\d+(?:\.\d+)?\}" +
        @"|\{(?:Logo|BRoll):[^,}]+,\d+(?:\.\d+)?\}");

    // Individual extraction patterns
    private static readonly Regex HeadlineRegex = new Regex(
        @"\{Headline:""([^""]+)"",""([^""]+)"",(\d+(?:\.\d+)?)\}");

    private static readonly Regex ExcerptRegex = new Regex(
        @"\{Excerpt:""([^""]+)"",""([^""]+)"",""([^""]+)"",(\d+(?:\.\d+)?)\}");

    private static readonly Regex QuoteRegex = new Regex(
        @"\{Quote:""([^""]+)"",""([^""]+)"",""([^""]+)"",(\d+(?:\.\d+)?)\}");

    private static readonly Regex StatRegex = new Regex(
        @"\{Stat:""([^""]+)"",""([^""]+)"",""([^""]+)"",(\d+(?:\.\d+)?)\}");

    private static readonly Regex LogoRegex = new Regex(
        @"\{Logo:([^,}]+),(\d+(?:\.\d+)?)\}");

    private static readonly Regex BRollRegex = new Regex(
        @"\{BRoll:([^,}]+),(\d+(?:\.\d+)?)\}");

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

        // Sort by trigger time
        events.Sort((a, b) => a.triggerTime.CompareTo(b.triggerTime));

        // Strip all card tags from the script
        string cleanScript = StripAllRegex.Replace(script, "");

        Debug.Log($"ContentZoneTagParser: Found {events.Count} content card events");
        return (cleanScript, events);
    }

    private static float ComputeTriggerTime(string script, int matchIndex, float audioDuration, int totalCleanChars)
    {
        string textBefore = script.Substring(0, matchIndex);
        string cleanBefore = StripAllRegex.Replace(textBefore, "");
        int charsBefore = cleanBefore.Length;
        return (charsBefore / (float)totalCleanChars) * audioDuration;
    }

    private static void ExtractHeadlines(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in HeadlineRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Headline,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                duration = float.Parse(match.Groups[3].Value)
            });
            Debug.Log($"  Headline at {time:F2}s: \"{match.Groups[1].Value}\"");
        }
    }

    private static void ExtractExcerpts(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in ExcerptRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Excerpt,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = float.Parse(match.Groups[4].Value)
            });
            Debug.Log($"  Excerpt at {time:F2}s: highlight=\"{match.Groups[2].Value}\"");
        }
    }

    private static void ExtractQuotes(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in QuoteRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Quote,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = float.Parse(match.Groups[4].Value)
            });
            Debug.Log($"  Quote at {time:F2}s: by {match.Groups[2].Value}");
        }
    }

    private static void ExtractStats(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in StatRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Stat,
                primaryText = match.Groups[1].Value,
                secondaryText = match.Groups[2].Value,
                tertiaryText = match.Groups[3].Value,
                duration = float.Parse(match.Groups[4].Value)
            });
            Debug.Log($"  Stat at {time:F2}s: {match.Groups[1].Value}");
        }
    }

    private static void ExtractLogos(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in LogoRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.Logo,
                primaryText = match.Groups[1].Value.Trim(),
                duration = float.Parse(match.Groups[2].Value)
            });
            Debug.Log($"  Logo at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }

    private static void ExtractBRolls(string script, float audioDuration, int totalCleanChars, List<ContentCardEvent> events)
    {
        foreach (Match match in BRollRegex.Matches(script))
        {
            float time = ComputeTriggerTime(script, match.Index, audioDuration, totalCleanChars);
            events.Add(new ContentCardEvent
            {
                triggerTime = time,
                cardType = ContentCardType.BRoll,
                primaryText = match.Groups[1].Value.Trim(),
                duration = float.Parse(match.Groups[2].Value)
            });
            Debug.Log($"  BRoll at {time:F2}s: {match.Groups[1].Value.Trim()}");
        }
    }
}

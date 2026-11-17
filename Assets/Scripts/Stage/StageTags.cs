using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>
    /// The two overlay tags of the evidence-first stage, parsed and stripped
    /// before any other parser sees them (like {Timestamp} and {Echo}):
    ///
    ///   {Name:"name","role",duration|Start} … {Name:End}   a name label over a full-frame photo or clip
    ///   {Pop:"phrase"} … {Pop:End}                          one spoken phrase at a time, centre screen
    ///
    /// Both forms parse — as authored and as the TTS pipeline stamps them
    /// ({Name:"…","…",T=…,D=0}, {Pop:"…",T=…}). A malformed tag is removed and
    /// warned about, never shown. Pure: no engine calls, no logging — the
    /// warnings are returned.
    /// </summary>
    public static class StageTags
    {
        public const int PopMaxWords = 6;

        public sealed class NameMarker
        {
            public float Time;
            public string Name, Role;
            public float Duration;      // 0 = held (Start … End)
            public bool Ends;           // {Name:End}
        }

        public sealed class PopMarker
        {
            public float Time;
            public string Phrase;
            public int Words;
            public bool Ends;           // {Pop:End}
        }

        public sealed class Parsed<T>
        {
            public string Script;                                   // the script with every tag of this kind removed
            public readonly List<T> Markers = new List<T>();        // in script order
            public readonly List<string> Warnings = new List<string>();
        }

        const string Time = @"(?:,T=(\d+(?:\.\d+)?))?";

        static readonly Regex NameValid = new Regex(
            @"\{Name:""([^""]+)"",""([^""]*)""" + Time + @",(?:D=)?(\d+(?:\.\d+)?|Start)\}");
        static readonly Regex NameEnd = new Regex(@"\{Name:End" + Time + @"\}");
        static readonly Regex NameAny = new Regex(@"\{Name:[^}\n]*\}");

        static readonly Regex PopValid = new Regex(@"\{Pop:""([^""]+)""" + Time + @"\}");
        static readonly Regex PopEnd = new Regex(@"\{Pop:End" + Time + @"\}");
        static readonly Regex PopAny = new Regex(@"\{Pop:[^}\n]*\}");

        public static bool HasNames(string script) => script != null && script.IndexOf("{Name:", StringComparison.Ordinal) >= 0;
        public static bool HasPops(string script) => script != null && script.IndexOf("{Pop:", StringComparison.Ordinal) >= 0;

        /// <summary>Every {Name:…} of the script, in order, and the script without them.</summary>
        public static Parsed<NameMarker> ParseNames(string script, float audioDuration)
        {
            var result = new Parsed<NameMarker> { Script = script ?? "" };
            if (!HasNames(script)) return result;

            int total = Math.Max(1, NameAny.Replace(script, "").Length);
            foreach (Match any in NameAny.Matches(script))
            {
                string tag = any.Value;
                float fallback = (float)NameAny.Replace(script.Substring(0, any.Index), "").Length / total * audioDuration;

                Match end = NameEnd.Match(tag);
                if (end.Success && end.Length == tag.Length)
                {
                    result.Markers.Add(new NameMarker { Time = TimeOf(end.Groups[1], fallback), Ends = true });
                    continue;
                }

                Match m = NameValid.Match(tag);
                if (m.Success && m.Length == tag.Length)
                {
                    result.Markers.Add(new NameMarker
                    {
                        Time = TimeOf(m.Groups[3], fallback),
                        Name = m.Groups[1].Value,
                        Role = m.Groups[2].Value,
                        Duration = m.Groups[4].Value == "Start" ? 0f : ParseFloat(m.Groups[4].Value),
                    });
                    continue;
                }

                result.Warnings.Add($"Name {Quote(tag)} isn't valid — it needs two quoted fields (a non-empty name, the role), " +
                                    "then a duration or Start; or {Name:End}. Skipped.");
            }
            result.Script = NameAny.Replace(script, "");
            return result;
        }

        /// <summary>Every {Pop:…} of the script, in order, and the script without them.</summary>
        public static Parsed<PopMarker> ParsePops(string script, float audioDuration)
        {
            var result = new Parsed<PopMarker> { Script = script ?? "" };
            if (!HasPops(script)) return result;

            int total = Math.Max(1, PopAny.Replace(script, "").Length);
            foreach (Match any in PopAny.Matches(script))
            {
                string tag = any.Value;
                float fallback = (float)PopAny.Replace(script.Substring(0, any.Index), "").Length / total * audioDuration;

                Match end = PopEnd.Match(tag);
                if (end.Success && end.Length == tag.Length)
                {
                    result.Markers.Add(new PopMarker { Time = TimeOf(end.Groups[1], fallback), Ends = true });
                    continue;
                }

                Match m = PopValid.Match(tag);
                if (m.Success && m.Length == tag.Length)
                {
                    string phrase = m.Groups[1].Value.Trim();
                    int words = WordCount(phrase);
                    var marker = new PopMarker { Time = TimeOf(m.Groups[2], fallback), Phrase = phrase, Words = words };
                    result.Markers.Add(marker);
                    if (words > PopMaxWords)
                        result.Warnings.Add($"Pop \"{phrase}\" at {marker.Time.ToString("F2", CultureInfo.InvariantCulture)}s is {words} words — " +
                                            $"a Pop is at most {PopMaxWords}. Shown anyway, shrunk to fit.");
                    continue;
                }

                result.Warnings.Add($"Pop {Quote(tag)} isn't valid — it needs one non-empty quoted phrase ({{Pop:\"phrase\"}}), or {{Pop:End}}. Skipped.");
            }
            result.Script = PopAny.Replace(script, "");
            return result;
        }

        public static int WordCount(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return 0;
            return phrase.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        static float TimeOf(Group g, float fallback)
            => g != null && g.Success ? ParseFloat(g.Value) : fallback;

        static float ParseFloat(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        static string Quote(string tag) => tag.Length <= 90 ? tag : tag.Substring(0, 87) + "…}";
    }

    /// <summary>
    /// The {Name:…} label's sizes and motion (px at 1080p unless noted; × frame
    /// height ÷ 1080). Pure.
    /// </summary>
    public static class NameLabelMath
    {
        /// <summary>Above the cards (30000) and a peeking Mugs (30500), below the full-screen cards (31000).</summary>
        public const int SortOrder = 30600;

        public const float NamePx = 64f, NameMinPx = 44f;
        public const float RolePx = 38f, RoleMinPx = 32f;
        public const float PadX = 30f, PadY = 16f, LineGap = 2f;
        /// <summary>The bar is as wide as its text, never more than this share of the frame.</summary>
        public const float MaxWidthOfFrame = 0.45f;
        /// <summary>Title-safe: its outer edge sits this far in from the frame's side…</summary>
        public const float SideInsetOfFrame = 0.05f;
        /// <summary>…and its bottom edge this far down the frame (the role's baseline lands at about 85 %).</summary>
        public const float BottomOfFrame = 0.875f;

        public const float SlideInSeconds = 0.30f, SlideOutSeconds = 0.25f;

        /// <summary>Sliding in 0→1, decelerating.</summary>
        public static float SlideIn(float t)
        {
            float x = Mathf.Clamp01(t / SlideInSeconds);
            return 1f - (1f - x) * (1f - x) * (1f - x);
        }

        /// <summary>Sliding out 0→1, accelerating.</summary>
        public static float SlideOut(float t)
        {
            float x = Mathf.Clamp01(t / SlideOutSeconds);
            return x * x;
        }

        public struct Fit
        {
            public float NameSize, RoleSize;   // TMP font sizes, frame px
            public float BarWidth;             // frame px
            public bool RoleWraps;             // the role needs a second line even at its floor
            public bool NameBelowFloor;        // the name only fits under 44 px (warned)
        }

        /// <summary>
        /// Sizes for a label whose name is <paramref name="nameWidthPerUnit"/> px
        /// wide per unit of font size on one line, and its role
        /// <paramref name="roleWidthPerUnit"/>: each as large as fits the widest
        /// bar allowed, down to its floor; a role still too wide at 32 px wraps;
        /// a name still too wide at 44 px shrinks on (never under 32).
        /// </summary>
        public static Fit FitLabel(float nameWidthPerUnit, float roleWidthPerUnit, float frameWidth, float frameHeight)
        {
            float k = frameHeight / 1080f;
            float maxText = MaxWidthOfFrame * frameWidth - 2f * PadX * k;
            var fit = new Fit();

            fit.NameSize = NamePx * k;
            if (nameWidthPerUnit > 0f && nameWidthPerUnit * fit.NameSize > maxText)
            {
                fit.NameSize = maxText / nameWidthPerUnit;
                if (fit.NameSize < NameMinPx * k - 0.01f)
                {
                    fit.NameBelowFloor = true;
                    fit.NameSize = Mathf.Max(fit.NameSize, 32f * k);
                }
            }

            fit.RoleSize = RolePx * k;
            if (roleWidthPerUnit > 0f && roleWidthPerUnit * fit.RoleSize > maxText)
            {
                fit.RoleSize = Mathf.Max(RoleMinPx * k, maxText / roleWidthPerUnit);
                fit.RoleWraps = roleWidthPerUnit * fit.RoleSize > maxText + 0.5f;
            }

            float textW = Mathf.Max(nameWidthPerUnit * fit.NameSize, fit.RoleWraps ? maxText : roleWidthPerUnit * fit.RoleSize);
            fit.BarWidth = Mathf.Min(maxText, textW) + 2f * PadX * k;
            return fit;
        }

        /// <summary>
        /// Which lower corner the label takes: away from Mugs. He is on the
        /// left (Left, PeekLeft) → the right corner; anywhere else → the left.
        /// </summary>
        public static bool OnRight(CharacterPosition mugs) =>
            mugs == CharacterPosition.Left || mugs == CharacterPosition.PeekLeft || mugs == CharacterPosition.LeanLeft;
    }

    /// <summary>The {Pop:…} phrase's sizes and motion (px at 1080p; × frame height ÷ 1080). Pure.</summary>
    public static class PopMath
    {
        /// <summary>Above every card, full-screen ones included (31000); under the captions' layer, the Echo and black.</summary>
        public const int SortOrder = 31300;

        /// <summary>The phrase's capitals…</summary>
        public const float CapPx = 120f;
        /// <summary>…and the bar never wider than this share of the frame (a longer phrase shrinks).</summary>
        public const float MaxWidthOfFrame = 0.84f;
        public const float FloorPx = 32f;
        /// <summary>The bar's padding, as shares of the cap height.</summary>
        public const float PadXOfCap = 0.42f, PadYOfCap = 0.30f;

        /// <summary>Brightness of the scene behind a Pop, as seen in the video: a 60 % dim.</summary>
        public const float SceneBrightness = 0.40f;
        public const float SoftenSeconds = 0.25f;
        public const float PopInSeconds = 0.12f, PopFromScale = 0.90f;
        public const float PhraseFadeSeconds = 0.15f, ClearSeconds = 0.25f;

        /// <summary>
        /// Alpha of the black layer that leaves the scene at <see cref="SceneBrightness"/>
        /// in the encoded video (in linear colour space UI blends in linear
        /// light — the same reasoning as BigTextTypography.DimAlpha).
        /// </summary>
        public static float DimAlpha(bool linearColorSpace) =>
            1f - (linearColorSpace ? Mathf.Pow(SceneBrightness, 2.2f) : SceneBrightness);

        /// <summary>The phrase's scale <paramref name="age"/> s after it lands: 0.9 → 1.0, decelerating.</summary>
        public static float PopScaleAt(float age)
        {
            float x = Mathf.Clamp01(age / PopInSeconds);
            float e = 1f - (1f - x) * (1f - x);
            return Mathf.Lerp(PopFromScale, 1f, e);
        }

        /// <summary>The softening 0→1 as a run opens.</summary>
        public static float SoftenAt(float age)
        {
            float x = Mathf.Clamp01(age / SoftenSeconds);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// The font size (frame px) for a phrase that is
        /// <paramref name="widthPerUnit"/> px wide per unit of font size:
        /// capitals of <see cref="CapPx"/>, shrunk until the bar fits
        /// <see cref="MaxWidthOfFrame"/>, never under the 32 px floor.
        /// </summary>
        public static float FontSize(float widthPerUnit, float capRatio, float frameWidth, float frameHeight)
        {
            float k = frameHeight / 1080f;
            capRatio = capRatio > 0.1f ? capRatio : 0.86f;
            float size = CapPx * k / capRatio;
            if (widthPerUnit > 0f)
            {
                // bar = text + 2 × padX, padX = PadXOfCap × cap = PadXOfCap × capRatio × size
                float perUnit = widthPerUnit + 2f * PadXOfCap * capRatio;
                size = Mathf.Min(size, MaxWidthOfFrame * frameWidth / perUnit);
            }
            return Mathf.Max(size, FloorPx * k);
        }
    }
}

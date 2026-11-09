using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MugsTech.Echo
{
    /// <summary>
    /// <c>{Echo:"HEADLINE WORDS","accent word",duration}</c> — the thumbnail's
    /// headline on screen for the first seconds, so the viewer sees the words
    /// they clicked. One per script, on its own line after the cold open's
    /// opening tags. Stamped by the TTS pipeline as
    /// <c>{Echo:"…","…",T=0.000,D=3}</c>; both forms parse.
    ///
    /// Pure: the parse, its warnings and the rich-text markup. The headline is
    /// shown in capitals with its <c>?</c> / <c>!</c> kept; the accent is matched
    /// the way the thumbnail maker matches it (whole words, ignoring
    /// <c>?!.,:;</c>) and the whole token, punctuation included, takes the
    /// accent colour. Over five words, or an accent not in the headline: a
    /// warning, and the headline shows without an accent.
    /// </summary>
    public static class EchoTag
    {
        public const int MaxWords = 5;
        public const float MinDuration = 0.5f, MaxDuration = 10f;

        /// <summary>The well-formed tag, authored or stamped.</summary>
        static readonly Regex Valid = new Regex(
            @"\{Echo:""([^""]*)"",""([^""]*)"",(?:T=(\d+(?:\.\d+)?),)?(?:D=)?(\d+(?:\.\d+)?)\}");

        /// <summary>Any {Echo:…} at all — the ones <see cref="Valid"/> doesn't take are malformed.</summary>
        static readonly Regex Any = new Regex(@"\{Echo:[^}\n]*\}");

        public sealed class Spec
        {
            public string Headline;          // as written
            public string Accent;            // as written ("" = none)
            public float Duration;
            public float Time = float.NaN;   // the stamped T= (NaN: unstamped)
            public bool AccentShown;         // false when a rule dropped it
            public string Markup;            // TMP rich text, capitals, accent coloured
            public string Plain;             // the capitals, no markup
            public int Words;
        }

        public sealed class Result
        {
            public string Script;            // the script with every {Echo} removed
            public Spec Echo;                // null: none (or none valid)
            public readonly List<string> Warnings = new List<string>();
        }

        public static bool HasTag(string script) => script != null && script.IndexOf("{Echo:", StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Finds the script's Echo and removes every Echo tag (valid, duplicate
        /// or malformed) — nothing downstream ever sees one.
        /// </summary>
        public static Result Parse(string script, string accentHex = "#E63946")
        {
            var r = new Result { Script = script ?? "" };
            if (!HasTag(r.Script)) return r;

            var valid = new HashSet<int>();
            foreach (Match m in Valid.Matches(r.Script))
            {
                valid.Add(m.Index);
                if (r.Echo != null)
                {
                    r.Warnings.Add($"A second {Quote(m.Value)} is ignored — a script has one Echo.");
                    continue;
                }
                r.Echo = Build(m, accentHex, r.Warnings);
            }
            foreach (Match m in Any.Matches(r.Script))
                if (!valid.Contains(m.Index))
                    r.Warnings.Add($"{Quote(m.Value)} isn't a valid Echo — it needs " +
                                   "{Echo:\"HEADLINE\",\"accent word\",seconds}. Ignored.");

            // Removed like {Timestamp:…} (its line stays, empty), so the text
            // around it is exactly what the other parsers saw before.
            r.Script = Any.Replace(r.Script, "");
            return r;
        }

        static Spec Build(Match m, string accentHex, List<string> warnings)
        {
            var s = new Spec
            {
                Headline = m.Groups[1].Value,
                Accent = m.Groups[2].Value,
                Duration = float.Parse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture),
            };
            if (m.Groups[3].Success)
                s.Time = float.Parse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (s.Duration < MinDuration || s.Duration > MaxDuration)
            {
                float d = Math.Max(MinDuration, Math.Min(MaxDuration, s.Duration));
                warnings.Add($"Echo duration {s.Duration:0.##} s is outside {MinDuration:0.#}–{MaxDuration:0} s — using {d:0.##} s.");
                s.Duration = d;
            }

            string[] tokens = Tokens(s.Headline);
            s.Words = tokens.Length;
            s.Plain = string.Join(" ", tokens);
            if (tokens.Length == 0)
                warnings.Add("The Echo's headline is empty — nothing to show.");

            int from = -1, count = 0;
            if (s.Accent.Trim().Length > 0)
            {
                if (tokens.Length > MaxWords)
                    warnings.Add($"Echo headline \"{s.Headline}\" has {tokens.Length} words (thumbnail headlines are at most " +
                                 $"{MaxWords}) — shown without its accent.");
                else if (!FindAccent(tokens, Tokens(s.Accent), out from, out count))
                    warnings.Add($"Echo accent \"{s.Accent}\" isn't a word of \"{s.Headline}\" — shown without an accent.");
            }
            else if (tokens.Length > MaxWords)
                warnings.Add($"Echo headline \"{s.Headline}\" has {tokens.Length} words (thumbnail headlines are at most {MaxWords}).");

            s.AccentShown = from >= 0 && tokens.Length <= MaxWords;
            s.Markup = Markup(tokens, s.AccentShown ? from : -1, count, accentHex);
            return s;
        }

        /// <summary>The headline's words in capitals (whitespace collapsed, punctuation kept).</summary>
        public static string[] Tokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            return text.Trim().ToUpperInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>A word without the punctuation the thumbnail ignores when matching the accent.</summary>
        public static string WordKey(string token) => token.Trim('?', '!', '.', ',', ':', ';');

        /// <summary>The first run of headline tokens whose keys equal the accent's keys.</summary>
        public static bool FindAccent(string[] tokens, string[] accent, out int from, out int count)
        {
            from = -1;
            count = accent.Length;
            if (accent.Length == 0 || accent.Length > tokens.Length) return false;
            for (int i = 0; i + accent.Length <= tokens.Length; i++)
            {
                bool all = true;
                for (int k = 0; k < accent.Length && all; k++)
                    all = WordKey(tokens[i + k]) == WordKey(accent[k]);
                if (all) { from = i; return true; }
            }
            return false;
        }

        /// <summary>TMP rich text: the tokens, the accent run in its colour. Text is never parsed as tags.</summary>
        public static string Markup(string[] tokens, int accentFrom, int accentCount, string accentHex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                bool open = i == accentFrom, close = accentFrom >= 0 && i == accentFrom + accentCount - 1;
                if (open) sb.Append("<color=").Append(accentHex).Append('>');
                sb.Append("<noparse>").Append(tokens[i]).Append("</noparse>");
                if (close) sb.Append("</color>");
            }
            return sb.ToString();
        }

        static string Quote(string tag) => tag.Length <= 80 ? tag : tag.Substring(0, 77) + "…";
    }
}

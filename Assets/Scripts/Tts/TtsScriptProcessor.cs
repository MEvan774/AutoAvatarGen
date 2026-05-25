using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MugsTech.Tts
{
    /// <summary>
    /// C# port of <c>Assets/Python/elevenlabs_tts_processor.py</c>.
    ///
    /// Pure functions — no Unity dependencies. Splits a raw Script.txt into
    /// segments at <c>## HEADING</c> lines, strips Unity markers / cards /
    /// stage directions to produce the "clean" text that ElevenLabs reads,
    /// then maps each marker's clean-text position back to a precise audio
    /// timestamp using the word-level alignment returned by the TTS API.
    /// Finally re-emits the script with <c>,T=X.XXX,D=Y</c> baked into every
    /// marker so the runtime can trigger cards on the audio timeline.
    ///
    /// Mirror of: extract_markers, map_markers_to_timestamps,
    /// rebuild_timed_script, stamp_marker in the Python source.
    /// </summary>
    public static class TtsScriptProcessor
    {
        // ---- DTOs ----------------------------------------------------------

        public class Segment
        {
            public int    Order;       // 1-based; assigned by the orchestrator
            public string Name;        // raw heading, e.g. "COLD OPEN"
            public string Slug;        // upper-snake, e.g. "COLD_OPEN"
            public string Raw;         // raw segment text (markers intact)
        }

        public class Marker
        {
            public string Text;         // original marker text, e.g. {Position:Left,Cut}
            public int    CharIndex;    // index in the RAW segment
            public int    CleanIndex;   // index in the marker-stripped (clean) text
        }

        public class TimedMarker
        {
            public Marker Marker;
            public float  TriggerTime;  // seconds into the segment audio
        }

        public class WordTimestamp
        {
            public string Word;
            public float  Start;
            public float  End;
        }

        // ---- Regex (1:1 with the Python _ALL_MARKERS) ---------------------

        private static readonly Regex AllMarkers = new Regex(
            @"\{(?:Excited|Serious|Concerned|Neutral|Sad)\}" +
            @"|\{Position:\w+(?:,\w+)?\}" +
            @"|\{Zoom:\w+(?:,(?:Cut|D=\d+(?:\.\d+)?))*\}" +
            @"|\{Black:\d+(?:\.\d+)?\}" +
            @"|\{(?:Image|Video):[^}]+\}" +
            @"|\{Headline:""[^""]*"",""[^""]*"",\d+(?:\.\d+)?(?:,\s*bigCenter)?\}" +
            @"|\{Excerpt:""[^""]*"",""[^""]*"",""[^""]*"",\d+(?:\.\d+)?\}" +
            @"|\{Quote:""[^""]*"",""[^""]*"",""[^""]*"",\d+(?:\.\d+)?\}" +
            @"|\{Stat:""[^""]*"",""[^""]*"",""[^""]*"",\d+(?:\.\d+)?\}" +
            @"|\{Logo:[^,}]+,\d+(?:\.\d+)?\}" +
            @"|\{BRoll:[^,}]+,\d+(?:\.\d+)?\}" +
            @"|\{BigMedia:[^,}]+,\d+(?:\.\d+)?\}" +
            @"|\{BigText:[^,}]+,\d+(?:\.\d+)?\}" +
            @"|\[[\w\s]+\]");

        private static readonly Regex SectionHeader = new Regex(
            @"^##\s+(.+)$", RegexOptions.Multiline);

        private static readonly Regex MultiSpace = new Regex(@"  +");

        // ---- Public API ----------------------------------------------------

        /// <summary>
        /// Split the raw script at every <c>## HEADING</c> line. Text before
        /// the first heading is dropped. If there are no headings, returns a
        /// single segment named "FULL".
        /// </summary>
        public static List<Segment> SplitIntoSegments(string rawScript)
        {
            var segments = new List<Segment>();
            var headers  = SectionHeader.Matches(rawScript);

            if (headers.Count == 0)
            {
                segments.Add(new Segment {
                    Name = "FULL", Slug = "FULL", Raw = (rawScript ?? "").Trim()
                });
                return segments;
            }

            for (int i = 0; i < headers.Count; i++)
            {
                Match header   = headers[i];
                int contentStart = header.Index + header.Length;
                int contentEnd   = (i + 1 < headers.Count)
                                     ? headers[i + 1].Index
                                     : rawScript.Length;

                string name = header.Groups[1].Value.Trim();
                segments.Add(new Segment {
                    Name = name,
                    Slug = MakeSlug(name),
                    Raw  = rawScript.Substring(contentStart, contentEnd - contentStart).Trim(),
                });
            }

            return segments;
        }

        /// <summary>"COLD OPEN" -> "COLD_OPEN", "Story 1 - Lead" -> "STORY_1_LEAD"</summary>
        public static string MakeSlug(string name)
        {
            string s = (name ?? "").ToUpperInvariant();
            s = Regex.Replace(s, @"[^\w\s]", "");
            s = Regex.Replace(s.Trim(), @"\s+", "_");
            return s;
        }

        /// <summary>
        /// Strip every marker from the raw segment and record each marker's
        /// original char_index plus its clean_index (position in the stripped
        /// text where it sat). The clean text is what we send to ElevenLabs.
        /// </summary>
        public static (List<Marker> markers, string cleanText) ExtractMarkers(string rawSegment)
        {
            var markers = new List<Marker>();
            foreach (Match m in AllMarkers.Matches(rawSegment))
            {
                string textBefore  = rawSegment.Substring(0, m.Index);
                string cleanBefore = AllMarkers.Replace(textBefore, "");
                markers.Add(new Marker {
                    Text       = m.Value,
                    CharIndex  = m.Index,
                    CleanIndex = cleanBefore.Length,
                });
            }

            string clean = AllMarkers.Replace(rawSegment, "");
            clean = MultiSpace.Replace(clean, " ").Trim();
            return (markers, clean);
        }

        /// <summary>
        /// For each marker, find the audio timestamp at its clean_index by
        /// scanning forward in a char→time map built from the word alignment.
        /// Markers past the end of speech snap to the total duration.
        /// </summary>
        public static List<TimedMarker> MapMarkersToTimestamps(
            List<Marker> markers, List<WordTimestamp> wordTimestamps, string cleanScript)
        {
            float?[] charTimes      = BuildCharTimeMap(cleanScript, wordTimestamps);
            float    totalDuration  = wordTimestamps.Count > 0
                                        ? wordTimestamps[wordTimestamps.Count - 1].End
                                        : 0f;
            var      timed          = new List<TimedMarker>(markers.Count);

            foreach (var m in markers)
            {
                int   idx = System.Math.Min(m.CleanIndex, charTimes.Length - 1);
                float? t  = null;
                for (int probe = idx; probe < charTimes.Length; probe++)
                {
                    if (charTimes[probe].HasValue) { t = charTimes[probe]; break; }
                }
                timed.Add(new TimedMarker {
                    Marker      = m,
                    TriggerTime = (float)System.Math.Round(
                        (t.HasValue ? t.Value : totalDuration), 3),
                });
            }
            return timed;
        }

        // Walk the word list in order, locate each word in cleanScript via
        // a forward-only search, and stamp char_times[<start_of_word>] with
        // its start time. Holes between marked positions are filled by the
        // mapping pass (it scans forward to the next non-null).
        private static float?[] BuildCharTimeMap(string cleanScript, List<WordTimestamp> words)
        {
            int len = cleanScript == null ? 0 : cleanScript.Length;
            var charTimes = new float?[len];
            int scriptPos = 0;

            foreach (var w in words)
            {
                string wordText = w.Word ?? "";
                int idx = (wordText.Length == 0) ? -1
                        : cleanScript.IndexOf(wordText, scriptPos, System.StringComparison.Ordinal);

                if (idx < 0)
                {
                    // Fuzzy fallback — strip non-word chars and look for the
                    // first window in the script that begins with the same
                    // alpha/digit run. Mirrors the Python fallback.
                    string stripped = Regex.Replace(wordText, @"[^\w]", "");
                    for (int p = scriptPos; p < cleanScript.Length; p++)
                    {
                        int take = System.Math.Min(wordText.Length + 2, cleanScript.Length - p);
                        string window = Regex.Replace(cleanScript.Substring(p, take), @"[^\w]", "");
                        if (window.StartsWith(stripped, System.StringComparison.Ordinal))
                        {
                            idx = p;
                            break;
                        }
                    }
                }

                if (idx >= 0)
                {
                    charTimes[idx] = w.Start;
                    scriptPos = idx + 1;
                }
            }
            return charTimes;
        }

        /// <summary>
        /// Rebuild the segment script with <c>,T=X.XXX[,D=Y]</c> baked into
        /// every marker. Walk markers back-to-front so earlier char_indexes
        /// stay valid after we mutate later ones.
        /// </summary>
        public static string RebuildTimedScript(string rawSegment, List<TimedMarker> timedMarkers)
        {
            var sorted = new List<TimedMarker>(timedMarkers);
            sorted.Sort((a, b) => b.Marker.CharIndex.CompareTo(a.Marker.CharIndex));

            string script = rawSegment;
            foreach (var tm in sorted)
            {
                string original    = tm.Marker.Text;
                string replacement = StampMarker(original, tm.TriggerTime);
                script = script.Substring(0, tm.Marker.CharIndex)
                       + replacement
                       + script.Substring(tm.Marker.CharIndex + original.Length);
            }
            return script;
        }

        // Rewrites one marker into its timestamped form. Each marker kind has
        // its own re-emit rule — kept in lockstep with the Python _stamp_marker
        // function so the timed.txt files are byte-for-byte equivalent.
        private static string StampMarker(string marker, float t)
        {
            string ts = "T=" + t.ToString("F3", CultureInfo.InvariantCulture);

            // [stage direction]
            if (marker.StartsWith("[", System.StringComparison.Ordinal))
            {
                string inner = marker.Substring(1, marker.Length - 2);
                return "[" + inner + "," + ts + "]";
            }

            string innerCurly = marker.Substring(1, marker.Length - 2);

            // {Emotion}
            if (Regex.IsMatch(innerCurly, @"^(Excited|Serious|Concerned|Neutral|Sad)$"))
                return "{" + innerCurly + "," + ts + "}";

            // {Zoom:...}
            if (innerCurly.StartsWith("Zoom:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Position:...}
            if (innerCurly.StartsWith("Position:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Black:3}  →  {Black:D=3,T=X.XXX}
            Match mBlack = Regex.Match(innerCurly, @"^Black:(\d+(?:\.\d+)?)$");
            if (mBlack.Success)
                return "{Black:D=" + mBlack.Groups[1].Value + "," + ts + "}";

            // {Image:file,5}  /  {Video:file,0}
            Match mMedia = Regex.Match(innerCurly,
                @"^(Image|Video):([^,}]+)(?:,(\d+(?:\.\d+)?))?$");
            if (mMedia.Success)
            {
                string dur = mMedia.Groups[3].Success ? mMedia.Groups[3].Value : "0";
                return "{" + mMedia.Groups[1].Value + ":" + mMedia.Groups[2].Value
                     + "," + ts + ",D=" + dur + "}";
            }

            // {Logo:name,5}  /  {BRoll:name,4}  /  {BigMedia:name,5}  /  {BigText:line,5}
            Match mLb = Regex.Match(innerCurly,
                @"^(Logo|BRoll|BigMedia|BigText):([^,}]+),(\d+(?:\.\d+)?)$");
            if (mLb.Success)
                return "{" + mLb.Groups[1].Value + ":" + mLb.Groups[2].Value
                     + "," + ts + ",D=" + mLb.Groups[3].Value + "}";

            // Content cards: Headline / Excerpt / Quote / Stat
            // (DOTALL so embedded newlines in quoted text are tolerated)
            Match mCard = Regex.Match(innerCurly,
                @"^(Headline|Excerpt|Quote|Stat):(.*),(\d+(?:\.\d+)?)(?:,\s*bigCenter)?$",
                RegexOptions.Singleline);
            if (mCard.Success)
            {
                string suffix = innerCurly.TrimEnd().EndsWith("bigCenter") ? ",bigCenter" : "";
                return "{" + mCard.Groups[1].Value + ":" + mCard.Groups[2].Value
                     + "," + ts + ",D=" + mCard.Groups[3].Value + suffix + "}";
            }

            // Fallback — unknown marker shape; append the timestamp anyway.
            return "{" + innerCurly + "," + ts + "}";
        }

        /// <summary>
        /// Group ElevenLabs character-level alignment arrays into word-level
        /// timestamps. Words are split on spaces/newlines/tabs. Mirrors the
        /// Python <c>_group_chars_into_words</c> helper.
        /// </summary>
        public static List<WordTimestamp> GroupCharsIntoWords(
            string[] chars, float[] starts, float[] ends)
        {
            var words = new List<WordTimestamp>();
            if (chars == null || chars.Length == 0) return words;

            var currentChars = new System.Text.StringBuilder();
            float? currentStart = null;

            for (int i = 0; i < chars.Length; i++)
            {
                string ch = chars[i] ?? "";
                bool isSep = ch == " " || ch == "\n" || ch == "\t";
                if (isSep)
                {
                    if (currentChars.Length > 0)
                    {
                        words.Add(new WordTimestamp {
                            Word  = currentChars.ToString(),
                            Start = currentStart ?? 0f,
                            End   = i > 0 ? ends[i - 1] : 0f,
                        });
                        currentChars.Clear();
                        currentStart = null;
                    }
                }
                else
                {
                    if (!currentStart.HasValue) currentStart = starts[i];
                    currentChars.Append(ch);
                }
            }

            if (currentChars.Length > 0)
            {
                words.Add(new WordTimestamp {
                    Word  = currentChars.ToString(),
                    Start = currentStart ?? 0f,
                    End   = ends.Length > 0 ? ends[ends.Length - 1] : 0f,
                });
            }
            return words;
        }
    }
}

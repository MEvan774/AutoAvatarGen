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

        // Transition-style modifier an emotion tag may carry: {Smirk,Blink}.
        // BlinkHeavy before Blink so the longer keyword wins the alternation.
        // Kept in lockstep with HybridAvatarSystem.ParseScriptWithTimeMarkers.
        private const string StyleAlt = "Cut|BlinkHeavy|Blink|SquashStretch|Crossfade|Shake|Grow";

        // An emotion tag is ANY bare one-word curly tag — {Neutral}, {Smirk},
        // {Sip}, {SmugSip}, whatever the avatar's emotion array holds. Matching
        // the SHAPE rather than a fixed list of names is what makes new emotions
        // free: drop a sprite into the array, write {NewName} in the script, and
        // it is stripped here and stamped with a T= with no edit to this file.
        // (No colon, so it can never collide with {Position:…} / {Mood:…} / card
        // tags — those are tried as later alternatives.)
        private const string EmotionTag = @"\{[A-Za-z]\w*(?:,(?:" + StyleAlt + @"))?\}";

        private static readonly Regex AllMarkers = new Regex(
            EmotionTag +                                   // emotion states — ANY bare {Word} / {Word,Style}
            @"|\{Timestamp:""[^""]*""\}" +                  // YouTube chapter markers — never voiced/shown
            @"|\{Position:\w+(?:,\w+)?\}" +
            @"|\{Zoom:\w+(?:,(?:Cut|D=\d+(?:\.\d+)?))*\}" +
            @"|\{Transition:\w+(?:,\d+(?:\.\d+)?)?\}" +     // whole-screen transitions (+optional duration scale)
            @"|\{Mood:\w+\}" +                              // background mood crossfade
            // {Black:3} timed, or the held pair {Black:Start}…{Black:End}
            // (On/In and Stop/Out/Off tolerated as synonyms).
            @"|\{Black:(?:\d+(?:\.\d+)?|Start|On|In|End|Stop|Out|Off)\}" +
            @"|\{(?:Image|Video):[^}]+\}" +
            // Side cards: the duration slot also accepts the "Start" keyword —
            // the held pair form ({Quote:...,Start}…{Quote:End}), mirroring
            // {Black:Start}…{Black:End}. The bare {Tag:End} closing edge is its
            // own alternative below the BRoll line.
            @"|\{Headline:""[^""]*"",""[^""]*"",(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:bigCenter|Left|Right))?\}" +
            @"|\{Excerpt:""[^""]*"",""[^""]*"",""[^""]*"",(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
            @"|\{Quote:""[^""]*"",""[^""]*"",""[^""]*"",(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
            @"|\{Stat:""[^""]*"",""[^""]*"",""[^""]*"",(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
            @"|\{Logo:[^,}]+,(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
            @"|\{BRoll:[^,}]+,(?:\d+(?:\.\d+)?|Start)(?:,\s*(?:Left|Right))?\}" +
            @"|\{(?:Headline|Excerpt|Quote|Stat|Logo|BRoll):End\}" +
            @"|\{BigMedia:[^,}]+,\d+(?:\.\d+)?\}" +
            // BigText duration is OPTIONAL — duration-less is the persistent
            // line-by-line flow ({BigText:LINE}…{BigText:End}); kept in
            // lockstep with elevenlabs_tts_processor.py.
            @"|\{BigText:[^,}]+(?:,\d+(?:\.\d+)?)?\}" +
            @"|\{BigImage:[^,}]+,\d+(?:\.\d+)?\}" +
            // [stage directions] — match anything up to the closing bracket so
            // directions containing commas/punctuation (e.g. "[slowing down,
            // serious]") are stripped too. The OLD pattern \[[\w\s]+\] only
            // allowed word-chars/whitespace, so a comma made the whole marker
            // leak into the TTS text — ElevenLabs then read it aloud, which
            // inflated the audio and pushed every later word (and every T=
            // timestamp derived from it) seconds late. Kept in lockstep with
            // the runtime strip in MediaPresentationSystem (\[[^\]]*\]).
            @"|\[[^\]]*\]" +
            // SAFETY NET — last alternative, so every pattern above still wins
            // first. Anything else wrapped in {...} on a single line is a tag we
            // don't know (new syntax, a typo, a mis-capitalised keyword). Strip
            // it rather than let it reach ElevenLabs, which would read the tag
            // out loud in the finished video. Bounded to one line so an unclosed
            // brace in prose can swallow at most the rest of that line.
            @"|\{[^{}\n]*\}");

        private static readonly Regex SectionHeader = new Regex(
            @"^##\s+(.+)$", RegexOptions.Multiline);

        // Runs of 2+ spaces collapse to one in the clean text. Applied by
        // NormaliseCleanText rather than a Regex.Replace, because the marker
        // indices have to be carried through the same edit.

        // ---- Public API ----------------------------------------------------

        /// <summary>
        /// Split the raw script at every <c>## HEADING</c> line. Text before
        /// the first heading is dropped. If there are no headings, returns a
        /// single segment named "FULL".
        /// </summary>
        public static List<Segment> SplitIntoSegments(string rawScript)
        {
            // Normalise line endings BEFORE anything else. Script.txt is written
            // on Windows, so it is CRLF throughout — and nothing downstream ever
            // removed the \r. It survived the tag strip (only ' ' and the ends
            // were normalised) and went to ElevenLabs inside the clean text as a
            // literal control character. The API echoes every character back in
            // its alignment, so each \r came back as its own timing entry, which
            // GroupCharsIntoWords then turned into a bogus "\r" word — and a
            // line-final word like "YouTube.\r" swallowed whatever duration the
            // API gave that \r, which is how a 19-second stall ended up recorded
            // as part of one word. Doing this first keeps CharIndex, CleanIndex
            // and the rebuilt _timed.txt all in one coordinate space.
            rawScript = NormaliseLineEndings(rawScript);

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

        /// <summary>
        /// CRLF / lone-CR -> LF. The TTS text must never carry a \r (see
        /// SplitIntoSegments), and LF-only keeps every index consistent.
        /// </summary>
        public static string NormaliseLineEndings(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n").Replace("\r", "\n");

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
        ///
        /// CleanIndex indexes the FINAL clean text — the exact string sent to
        /// the API and later fed to BuildCharTimeMap. Collapsing double spaces
        /// and trimming happen after the markers come out, so every index is
        /// carried through that normalisation rather than measured before it
        /// (see NormaliseCleanText). Measuring before it left every marker a
        /// couple of characters high: char_times is only populated at word
        /// STARTS, so a marker that should have landed exactly on a word
        /// instead probed forward and fired a whole word late — most visibly on
        /// a section-opening {Transition:...}, which covered the screen after
        /// the first word had already been spoken.
        /// </summary>
        public static (List<Marker> markers, string cleanText) ExtractMarkers(string rawSegment)
            => ExtractMarkers(rawSegment, keepStageDirections: false);

        /// <summary>
        /// As above, but <paramref name="keepStageDirections"/> leaves
        /// <c>[bracketed]</c> cues IN the text sent to ElevenLabs — eleven_v3
        /// reads them as audio tags that shape the delivery and consumes them
        /// rather than voicing them (SCRIPT_TAG_GUIDE §4). They are still
        /// recorded as markers, so the rebuilt _timed.txt keeps carrying
        /// <c>[deadpan,T=1.234]</c> and the runtime strip in
        /// MediaPresentationSystem still removes them at playback.
        ///
        /// Kept behind a flag because ElevenLabs' own v3 guide warns the model
        /// sometimes speaks delivery guides aloud — if that shows up in a take,
        /// flip the flag off and the text goes back to today's stripped form
        /// with no other change.
        /// </summary>
        public static (List<Marker> markers, string cleanText) ExtractMarkers(
            string rawSegment, bool keepStageDirections)
        {
            rawSegment = rawSegment ?? "";

            var markers  = new List<Marker>();
            var rawClean = new System.Text.StringBuilder(rawSegment.Length);
            int cursor   = 0;

            foreach (Match m in AllMarkers.Matches(rawSegment))
            {
                rawClean.Append(rawSegment, cursor, m.Index - cursor);
                markers.Add(new Marker {
                    Text       = m.Value,
                    CharIndex  = m.Index,
                    CleanIndex = rawClean.Length,   // remapped by NormaliseCleanText
                });

                // A kept stage direction stays in the clean text at exactly the
                // position its marker points at, so the marker's CleanIndex is
                // the '[' itself — the moment the cue applies.
                if (keepStageDirections && IsStageDirection(m.Value))
                    rawClean.Append(m.Value);

                cursor = m.Index + m.Length;
            }
            rawClean.Append(rawSegment, cursor, rawSegment.Length - cursor);

            return (markers, NormaliseCleanText(rawClean.ToString(), markers));
        }

        /// <summary>A <c>[bracketed]</c> cue, as opposed to a <c>{curly}</c> tag.</summary>
        public static bool IsStageDirection(string markerText)
            => !string.IsNullOrEmpty(markerText) && markerText[0] == '[';

        /// <summary>
        /// Collapses runs of 2+ spaces to one and trims the ends — the exact
        /// normalisation the TTS text gets — while rewriting every marker's
        /// CleanIndex onto the result, so marker
        /// positions and the text we send to ElevenLabs stay in one coordinate
        /// space.
        /// </summary>
        private static string NormaliseCleanText(string rawClean, List<Marker> markers)
        {
            var sb  = new System.Text.StringBuilder(rawClean.Length);
            var map = new int[rawClean.Length + 1];   // raw-clean index -> collapsed index

            int i = 0;
            while (i < rawClean.Length)
            {
                if (rawClean[i] == ' ')
                {
                    // A run of spaces collapses to one (a run of 1 is a no-op,
                    // matching the `  +` pattern). Every index inside the run
                    // points at that single surviving space.
                    int run = i;
                    while (run < rawClean.Length && rawClean[run] == ' ') run++;
                    for (int k = i; k < run; k++) map[k] = sb.Length;
                    sb.Append(' ');
                    i = run;
                    continue;
                }

                map[i] = sb.Length;
                sb.Append(rawClean[i]);
                i++;
            }
            map[rawClean.Length] = sb.Length;

            // Trim, and slide the indices along with it.
            string collapsed = sb.ToString();
            int start = 0, end = collapsed.Length;
            while (start < end && char.IsWhiteSpace(collapsed[start])) start++;
            while (end > start && char.IsWhiteSpace(collapsed[end - 1])) end--;

            string clean = collapsed.Substring(start, end - start);
            foreach (var m in markers)
                m.CleanIndex = System.Math.Min(System.Math.Max(0, map[m.CleanIndex] - start),
                                               clean.Length);
            return clean;
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

            // {Timestamp:"Label"} — pure timeline/chapter marker; never voiced or
            // shown. Just append the timestamp so the runtime logs it on the audio
            // clock: {Timestamp:"Label"} -> {Timestamp:"Label",T=X.XXX}
            if (innerCurly.StartsWith("Timestamp:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Emotion} / {Emotion,Style} — any bare one-word tag, matching
            // EmotionTag. Unity reads {Smirk,T=12.480} and {Smirk,Blink,T=12.480}
            // equally well.
            if (Regex.IsMatch(innerCurly, @"^[A-Za-z]\w*(?:,(?:" + StyleAlt + @"))?$"))
                return "{" + innerCurly + "," + ts + "}";

            // {Zoom:...}
            if (innerCurly.StartsWith("Zoom:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Position:...}
            if (innerCurly.StartsWith("Position:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Transition:Wipe}  /  {Transition:Iris,1.2}
            if (innerCurly.StartsWith("Transition:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Mood:Tense}
            if (innerCurly.StartsWith("Mood:", System.StringComparison.Ordinal))
                return "{" + innerCurly + "," + ts + "}";

            // {Black:3}  →  {Black:D=3,T=X.XXX}
            Match mBlack = Regex.Match(innerCurly, @"^Black:(\d+(?:\.\d+)?)$");
            if (mBlack.Success)
                return "{Black:D=" + mBlack.Groups[1].Value + "," + ts + "}";

            // {Black:Start} / {Black:End} — the held pair form; the keyword is
            // preserved verbatim so the runtime parser sees which edge it is.
            Match mBlackKw = Regex.Match(innerCurly,
                @"^Black:(Start|On|In|End|Stop|Out|Off)$", RegexOptions.IgnoreCase);
            if (mBlackKw.Success)
                return "{Black:" + mBlackKw.Groups[1].Value + "," + ts + "}";

            // {Headline:End} / {Quote:End} / {Logo:End} / … — the held pair's
            // closing edge ({Tag:...,Start}…{Tag:End}); keep the keyword
            // verbatim (like {Black:End}) so the runtime parser sees the close.
            if (Regex.IsMatch(innerCurly, @"^(?:Headline|Excerpt|Quote|Stat|Logo|BRoll):End$"))
                return "{" + innerCurly + "," + ts + "}";

            // {Image:file,5}  /  {Video:file,0}  /  {Image:file,Start} held pair
            // Both may carry a trailing ",Left"/",Right" side modifier (the
            // screen side the media rests on); preserve it after D= so the
            // runtime parser still sees the side. "Start" in the duration slot
            // is the held pair form ({Image:name,Start}…{Image:End}) — the
            // keyword is preserved verbatim, like {Black:Start}.
            Match mMedia = Regex.Match(innerCurly,
                @"^(Image|Video):([^,}]+)(?:,(\d+(?:\.\d+)?|Start))?(?:,\s*(Left|Right))?$");
            if (mMedia.Success)
            {
                string side = mMedia.Groups[4].Success ? "," + mMedia.Groups[4].Value : "";
                if (mMedia.Groups[3].Success && mMedia.Groups[3].Value == "Start")
                    return "{" + mMedia.Groups[1].Value + ":" + mMedia.Groups[2].Value
                         + "," + ts + ",Start" + side + "}";
                string dur = mMedia.Groups[3].Success ? mMedia.Groups[3].Value : "0";
                return "{" + mMedia.Groups[1].Value + ":" + mMedia.Groups[2].Value
                     + "," + ts + ",D=" + dur + side + "}";
            }

            // {Logo:name,5}  /  {BRoll:name,4}  — may carry a trailing
            // ",Left"/",Right" side modifier; preserve it after D= so the
            // runtime parser still sees the side. "Start" in the duration slot
            // is the held pair form ({Logo:name,Start}…{Logo:End}) and stamps
            // D=0 — the same persistent marker the duration-less BigText uses.
            Match mLb = Regex.Match(innerCurly,
                @"^(Logo|BRoll):([^,}]+),(\d+(?:\.\d+)?|Start)(?:,\s*(Left|Right))?$");
            if (mLb.Success)
            {
                string side = mLb.Groups[4].Success ? "," + mLb.Groups[4].Value : "";
                string dur = mLb.Groups[3].Value == "Start" ? "0" : mLb.Groups[3].Value;
                return "{" + mLb.Groups[1].Value + ":" + mLb.Groups[2].Value
                     + "," + ts + ",D=" + dur + side + "}";
            }

            // {BigMedia:name,5}  /  {BigText:line,5}  /  {BigImage:name,5}
            // Fullscreen feature cards — timed only, no held pair form.
            Match mBig = Regex.Match(innerCurly,
                @"^(BigMedia|BigText|BigImage):([^,}]+),(\d+(?:\.\d+)?)(?:,\s*(Left|Right))?$");
            if (mBig.Success)
            {
                string side = mBig.Groups[4].Success ? "," + mBig.Groups[4].Value : "";
                return "{" + mBig.Groups[1].Value + ":" + mBig.Groups[2].Value
                     + "," + ts + ",D=" + mBig.Groups[3].Value + side + "}";
            }

            // {BigText:LINE} — duration-less persistent line (or {BigText:End}).
            // D=0 marks the persistent flow for the runtime parser.
            Match mBigText = Regex.Match(innerCurly, @"^BigText:([^,}]+)$");
            if (mBigText.Success)
                return "{BigText:" + mBigText.Groups[1].Value + "," + ts + ",D=0}";

            // Content cards: Headline / Excerpt / Quote / Stat
            // (DOTALL so embedded newlines in quoted text are tolerated). Preserve
            // an optional trailing modifier after D= — ",bigCenter" (Headline →
            // centered feature card) or ",Left"/",Right" (the side a side card
            // flies in from) — so the runtime parser still sees it. "Start" in
            // the duration slot is the held pair form
            // ({Quote:...,Start}…{Quote:End}) and stamps D=0 — the same
            // persistent marker the duration-less BigText flow uses.
            Match mCard = Regex.Match(innerCurly,
                @"^(Headline|Excerpt|Quote|Stat):(.*),(\d+(?:\.\d+)?|Start)(?:,\s*(bigCenter|Left|Right))?$",
                RegexOptions.Singleline);
            if (mCard.Success)
            {
                string suffix = mCard.Groups[4].Success ? "," + mCard.Groups[4].Value : "";
                string dur = mCard.Groups[3].Value == "Start" ? "0" : mCard.Groups[3].Value;
                return "{" + mCard.Groups[1].Value + ":" + mCard.Groups[2].Value
                     + "," + ts + ",D=" + dur + suffix + "}";
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
                // \r is a separator too. We no longer send one (SplitIntoSegments
                // normalises line endings), but treating it as part of a word is
                // what let a stray carriage return attach a multi-second stall to
                // the end of the word in front of it — so keep the guard for any
                // \r the API returns on its own.
                bool isSep = ch == " " || ch == "\n" || ch == "\t" || ch == "\r";
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

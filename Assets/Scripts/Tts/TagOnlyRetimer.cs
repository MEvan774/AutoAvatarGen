using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MugsTech.Tts
{
    /// <summary>
    /// Re-times an edited script's tags against a generation's EXISTING voice,
    /// so moving, changing, adding or removing a tag doesn't cost a new
    /// ElevenLabs render. Pure — no Unity, no file IO; <see cref="TagOnlyUpdate"/>
    /// reads the generation folder and writes the result.
    ///
    /// The contract that makes it safe: the words the voice speaks must not
    /// change. Each section's tag-stripped text is compared with the text the
    /// take was rendered from, ignoring only whitespace (moving a tag that sits
    /// on its own line changes how many newlines surround it). Section headings
    /// and <c>{Transition:…}</c> tags are locked as well — the transition's
    /// silence and gathered line are part of how the section was built — and so
    /// are <c>[stage directions]</c> when they were sent to eleven_v3, because
    /// then they shaped the delivery that's already recorded.
    ///
    /// Timing: a tag that is still where it was (same text, same pause between
    /// the same two words) keeps its exact old <c>T=</c>. Anything moved, new or
    /// changed reads its time off the take's own alignment at its new position —
    /// the same lookup the generation used — clamped so it never fires before an
    /// earlier untouched tag or after a later one.
    /// </summary>
    public static class TagOnlyRetimer
    {
        /// <summary>One section of an existing generation, as it was recorded.</summary>
        public class Section
        {
            public string Slug;                  // "02_SETUP"
            public string Name;                  // "SETUP"
            public string TimedScript;           // the _timed.txt on disk
            public string Spoken;                // the exact text the take was rendered from
            public bool   StageDirectionsVoiced; // [cues] were sent to v3 (and are part of Spoken)
            public Func<int, float> TimeAt;      // section-local seconds of an index into Spoken
            public bool   Approximate;           // TimeAt comes from word timings, not the take's alignment
        }

        public class SectionResult
        {
            public string Slug;
            public string Name;
            public string TimedScript;           // the rebuilt _timed.txt
            public bool   Changed;
            public int    Retimed;               // tags that got a new time (moved, new or edited)
        }

        public class Result
        {
            public bool   Success;
            public string Error;
            public List<SectionResult> Sections = new List<SectionResult>();
            public List<string>        Warnings = new List<string>();
            public List<string>        MissingMedia = new List<string>(); // "SECTION: {Image:name}", once per name

            public int ChangedCount
            {
                get { int n = 0; foreach (var s in Sections) if (s.Changed) n++; return n; }
            }
        }

        // ---- re-timing ----------------------------------------------------

        /// <param name="mediaExists">
        /// Optional (kind, name) → whether that media name resolves, asked once
        /// per distinct <c>{Image:}</c> / <c>{BigImage:}</c> / <c>{Video:}</c> name
        /// in the edited script (see <see cref="TryGetMediaName"/>). Names it
        /// rejects are reported as a warning — a misspelled name parses fine but
        /// shows nothing. Null skips the check.
        /// </param>
        public static Result Retime(string editedScript, IList<Section> generation,
                                    Func<string, string, bool> mediaExists = null)
        {
            var result = new Result();
            if (generation == null || generation.Count == 0)
                return Fail(result, "The generation has no sections to update.");
            if (string.IsNullOrWhiteSpace(editedScript))
                return Fail(result, "Script is empty — load it from the generation first.");

            var segments = TtsScriptProcessor.SplitIntoSegments(editedScript);
            TtsGenerationJob.AssignOrderAndSlugs(segments);

            string headingError = CompareHeadings(segments, generation);
            if (headingError != null) return Fail(result, headingError);

            var unrecognised = new List<string>();
            var mediaChecked = new HashSet<string>();
            bool approximated = false;

            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                var sec = generation[s];
                bool keepCues = sec.StageDirectionsVoiced;
                string spoken = sec.Spoken ?? "";

                // ---- the edit: its tags and where they sit in the voice -------
                var (edited, editedClean) = TtsScriptProcessor.ExtractMarkers(seg.Raw, keepCues);

                foreach (var m in edited)
                    if (BakedTime.IsMatch(m.Text))
                        return Fail(result, $"{sec.Name}: {Short(m.Text)} still has a baked T= time. " +
                                            "Click Load From Generation to get the editable script.");

                if (!TryMapToSpoken(editedClean, spoken, out int[] editedMap,
                                    out int badEdit, out int badSpoken))
                    return Fail(result, DescribeMismatch(sec, editedClean, badEdit, spoken, badSpoken));

                // ---- the original: same thing, read back from _timed.txt ------
                var originalTimes = new List<float>();
                string originalRaw = Unstamp(sec.TimedScript, originalTimes);
                var (original, originalClean) = TtsScriptProcessor.ExtractMarkers(originalRaw, keepCues);

                if (!TryMapToSpoken(originalClean, spoken, out int[] originalMap, out _, out _))
                    return Fail(result, $"{sec.Name}: this generation's {sec.Slug}_timed.txt doesn't match " +
                                        "its own voice take, so it can't be re-timed. Regenerate it.");

                // Untouched tags are recognised by pairing them with the T= they
                // were stamped with. If the counts disagree the pairing is
                // meaningless, so every tag is re-timed from the alignment instead.
                bool anchored = originalTimes.Count == original.Count;

                // ---- transitions are locked ----------------------------------
                string transitionError = CompareTransitions(
                    sec, original, originalMap, edited, editedMap, spoken);
                if (transitionError != null) return Fail(result, transitionError);

                // ---- times ---------------------------------------------------
                int n = edited.Count;
                var times   = new float[n];
                var matched = new bool[n];

                var originalKeys = new string[original.Count];
                for (int j = 0; j < original.Count; j++)
                    originalKeys[j] = Key(original[j], originalMap, spoken);
                var editedKeys = new string[n];
                for (int i = 0; i < n; i++)
                    editedKeys[i] = Key(edited[i], editedMap, spoken);

                // Time is a function of position, so a tag that landed exactly
                // where an old one sat can reuse that tag's time verbatim.
                var timeAtPosition = new Dictionary<int, float>();

                if (anchored)
                {
                    foreach (var (i, j) in LongestCommonSubsequence(editedKeys, originalKeys))
                    {
                        times[i]   = originalTimes[j];
                        matched[i] = true;
                    }
                    for (int j = 0; j < original.Count; j++)
                    {
                        int at = MapIndex(originalMap, original[j].CleanIndex);
                        if (!timeAtPosition.ContainsKey(at)) timeAtPosition[at] = originalTimes[j];
                    }
                }

                int retimed = 0;
                for (int i = 0; i < n; i++)
                {
                    if (matched[i]) continue;
                    int at = MapIndex(editedMap, edited[i].CleanIndex);
                    times[i] = timeAtPosition.TryGetValue(at, out float t) ? t : sec.TimeAt(at);
                    retimed++;
                }

                // A re-timed tag never jumps ahead of an earlier untouched tag or
                // behind a later one: inside one pause the alignment's per-newline
                // times don't know about the lines that were added or removed.
                for (int i = 0; i < n; i++)
                {
                    if (matched[i]) continue;
                    float lo = float.NegativeInfinity, hi = float.PositiveInfinity;
                    for (int k = i - 1; k >= 0; k--) if (matched[k]) { lo = times[k]; break; }
                    for (int k = i + 1; k < n; k++)  if (matched[k]) { hi = times[k]; break; }
                    if (lo <= hi) times[i] = Math.Min(Math.Max(times[i], lo), hi);
                }

                if (retimed > 0 && sec.Approximate) approximated = true;

                foreach (var m in edited)
                    if (!TtsScriptProcessor.IsStageDirection(m.Text) &&
                        !TtsScriptProcessor.IsRecognisedMarker(m.Text))
                        unrecognised.Add($"{sec.Name}: {Short(m.Text)}");

                if (mediaExists != null)
                    foreach (var m in edited)
                    {
                        if (!TryGetMediaName(m.Text, out string kind, out string name)) continue;
                        string tag = "{" + kind + ":" + name + "}";
                        if (mediaChecked.Add(tag) && !mediaExists(kind, name))
                            result.MissingMedia.Add($"{sec.Name}: {Short(tag)}");
                    }

                // ---- rebuild ---------------------------------------------------
                var timed = new List<TtsScriptProcessor.TimedMarker>(n);
                for (int i = 0; i < n; i++)
                    timed.Add(new TtsScriptProcessor.TimedMarker { Marker = edited[i], TriggerTime = times[i] });

                string rebuilt = TtsScriptProcessor.RebuildTimedScript(seg.Raw, timed);
                result.Sections.Add(new SectionResult {
                    Slug        = sec.Slug,
                    Name        = sec.Name,
                    TimedScript = rebuilt,
                    Changed     = rebuilt != TtsScriptProcessor.NormaliseLineEndings(sec.TimedScript),
                    Retimed     = retimed,
                });
            }

            if (unrecognised.Count > 0)
                result.Warnings.Add($"{unrecognised.Count} tag(s) not recognised — they'll do nothing: " +
                                    string.Join("; ", unrecognised.GetRange(0, Math.Min(3, unrecognised.Count))) +
                                    (unrecognised.Count > 3 ? "; …" : ""));
            var missing = result.MissingMedia;
            if (missing.Count > 0)
                result.Warnings.Add($"{missing.Count} media name(s) don't match any media file — they'll show " +
                                    "nothing: " + string.Join("; ", missing.GetRange(0, Math.Min(3, missing.Count))) +
                                    (missing.Count > 3 ? "; …" : ""));
            if (approximated)
                result.Warnings.Add("This generation's chunks/ folder is missing, so moved tags were " +
                                    "timed from word timings (slightly less precise).");

            result.Success = true;
            return result;
        }

        static Result Fail(Result r, string error)
        {
            r.Success = false;
            r.Error   = error;
            r.Sections.Clear();
            return r;
        }

        // A tag's identity for "is this the same tag in the same place": its
        // text plus the pause it sits in, named by the first spoken character
        // after it. Several own-line tags between the same two sentences share
        // a pause, so deleting one doesn't disturb the others.
        static string Key(TtsScriptProcessor.Marker m, int[] map, string spoken)
            => m.Text + "\u0001" + NextSpoken(spoken, MapIndex(map, m.CleanIndex));

        static int NextSpoken(string spoken, int index)
        {
            while (index < spoken.Length && char.IsWhiteSpace(spoken[index])) index++;
            return index;
        }

        static int MapIndex(int[] map, int index)
            => map[Math.Min(Math.Max(0, index), map.Length - 1)];

        // ---- headings & transitions --------------------------------------

        static string CompareHeadings(List<TtsScriptProcessor.Segment> segments, IList<Section> generation)
        {
            for (int i = 0; i < Math.Min(segments.Count, generation.Count); i++)
                if (segments[i].Slug != generation[i].Slug)
                    return $"Section {i + 1} is \"## {generation[i].Name}\" in the voice but " +
                           $"\"## {segments[i].Name}\" in the script. Section headings can't change " +
                           "in tag-only mode.";

            if (segments.Count != generation.Count)
                return $"The voice has {generation.Count} section(s) but the script has {segments.Count}. " +
                       "Section headings can't change in tag-only mode.";
            return null;
        }

        static string CompareTransitions(
            Section sec,
            List<TtsScriptProcessor.Marker> original, int[] originalMap,
            List<TtsScriptProcessor.Marker> edited,   int[] editedMap, string spoken)
        {
            var before = new List<string>();
            foreach (var m in original) if (IsTransition(m.Text)) before.Add(Key(m, originalMap, spoken));
            var after = new List<string>();
            foreach (var m in edited) if (IsTransition(m.Text)) after.Add(Key(m, editedMap, spoken));

            if (before.Count == after.Count)
            {
                bool same = true;
                for (int i = 0; i < before.Count && same; i++) same = before[i] == after[i];
                if (same) return null;
            }
            return $"{sec.Name}: {{Transition:…}} tags can't be added, removed, moved or changed in " +
                   "tag-only mode — put them back as they were, or regenerate.";
        }

        static bool IsTransition(string markerText)
            => markerText.StartsWith("{Transition:", StringComparison.Ordinal);

        // ---- media names --------------------------------------------------

        // Read the way the recording scene reads them — MediaPresentationSystem
        // .ParseMediaMarkers for {Image|Video:name…}, ContentZoneTagParser for
        // {BigImage:name,…}: everything up to the first comma, trimmed.
        static readonly Regex MediaTag = new Regex(@"^\{(Image|Video|BigImage):([^,}]+)");

        /// <summary>
        /// The name an <c>{Image:}</c>, <c>{BigImage:}</c> or <c>{Video:}</c> tag
        /// looks up on disk. False for every other tag, and for <c>{Image:End}</c>
        /// / <c>{Video:End}</c> (and the Stop/Out spellings the player accepts),
        /// which close the running media instead of naming one. Logo, BigMedia
        /// and BRoll are left out on purpose: they resolve through a
        /// ContentCardAssets asset that only the recording scene could hold.
        /// </summary>
        public static bool TryGetMediaName(string markerText, out string kind, out string name)
        {
            kind = name = null;
            Match m = MediaTag.Match(markerText ?? "");
            if (!m.Success) return false;

            kind = m.Groups[1].Value;
            name = m.Groups[2].Value.Trim();
            if (name.Length == 0) return false;
            if (kind != "BigImage")
            {
                string lower = name.ToLowerInvariant();
                if (lower == "end" || lower == "stop" || lower == "out") return false;
            }
            return true;
        }

        // ---- narration comparison ----------------------------------------

        /// <summary>
        /// Maps every index of <paramref name="text"/> (a tag-stripped section)
        /// onto <paramref name="spoken"/>. The two must hold the same characters
        /// apart from whitespace. Inside a run of whitespace the k-th character
        /// maps to the k-th of the spoken run (clamped to its length), so an
        /// unchanged run maps onto itself exactly. Returns false with the first
        /// differing position in each string.
        /// </summary>
        public static bool TryMapToSpoken(string text, string spoken, out int[] map,
                                          out int badText, out int badSpoken)
        {
            text   = text   ?? "";
            spoken = spoken ?? "";
            map = new int[text.Length + 1];
            badText = badSpoken = -1;

            int i = 0, j = 0;
            while (true)
            {
                int runText = i;
                while (runText < text.Length && char.IsWhiteSpace(text[runText])) runText++;
                int runSpoken = j;
                while (runSpoken < spoken.Length && char.IsWhiteSpace(spoken[runSpoken])) runSpoken++;

                int spokenRun = runSpoken - j;
                for (int k = i; k < runText; k++)
                    map[k] = j + (spokenRun == 0 ? 0 : Math.Min(k - i, spokenRun - 1));
                i = runText;
                j = runSpoken;

                if (i >= text.Length || j >= spoken.Length) break;
                if (text[i] != spoken[j]) { badText = i; badSpoken = j; return false; }
                map[i] = j;
                i++; j++;
            }

            if (i < text.Length || j < spoken.Length) { badText = i; badSpoken = j; return false; }
            map[text.Length] = spoken.Length;
            return true;
        }

        static string DescribeMismatch(Section sec, string text, int at, string spoken, int spokenAt)
        {
            string hint = " Only tags can change.";
            if (Window(text, at, 12).IndexOf('{') >= 0)
                hint = " A tag that isn't closed properly is read as narration.";
            else if (sec.StageDirectionsVoiced && (InsideCue(text, at) || InsideCue(spoken, spokenAt)))
                hint = " [Stage directions] were sent to the voice, so they can't change.";

            return $"{sec.Name}: narration differs from the recorded voice — script \"{Context(text, at)}\", " +
                   $"voice \"{Context(spoken, spokenAt)}\".{hint}";
        }

        static string Window(string s, int at, int radius)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int from = Math.Max(0, Math.Min(at, s.Length) - radius);
            int to   = Math.Min(s.Length, Math.Max(0, at) + radius);
            return s.Substring(from, to - from);
        }

        static bool InsideCue(string s, int at)
        {
            if (string.IsNullOrEmpty(s)) return false;
            at = Math.Min(Math.Max(0, at), s.Length - 1);
            int open = s.LastIndexOf('[', at);
            return open >= 0 && s.LastIndexOf(']', at) < open;
        }

        static string Context(string s, int at)
        {
            if (string.IsNullOrEmpty(s)) return "(nothing)";
            at = Math.Min(Math.Max(0, at), s.Length);
            int from = Math.Max(0, at - 18);
            int to   = Math.Min(s.Length, at + 18);
            string piece = Regex.Replace(s.Substring(from, to - from), @"\s+", " ").Trim();
            if (piece.Length == 0) piece = "(end of section)";
            return (from > 0 ? "…" : "") + piece + (to < s.Length ? "…" : "");
        }

        static string Short(string tag)
        {
            string flat = Regex.Replace(tag ?? "", @"\s+", " ");
            return flat.Length <= 48 ? flat : flat.Substring(0, 47) + "…";
        }

        // ---- LCS ----------------------------------------------------------

        static List<(int, int)> LongestCommonSubsequence(string[] a, string[] b)
        {
            var len = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    len[i, j] = a[i] == b[j]
                        ? len[i + 1, j + 1] + 1
                        : Math.Max(len[i + 1, j], len[i, j + 1]);

            var pairs = new List<(int, int)>();
            for (int i = 0, j = 0; i < a.Length && j < b.Length; )
            {
                if (a[i] == b[j])                       { pairs.Add((i, j)); i++; j++; }
                else if (len[i + 1, j] >= len[i, j + 1]) i++;
                else                                     j++;
            }
            return pairs;
        }

        // ---- un-stamping _timed.txt --------------------------------------

        // A stamped tag in a _timed.txt. Quoted fields may hold braces or span
        // lines (card text), so they are skipped as a unit; outside quotes a tag
        // stays on one line, like the catch-all that stripped it.
        static readonly Regex StampedTag = new Regex(@"\{(?:[^{}""\n]|""[^""]*"")*\}|\[[^\]]*\]");

        // The ",T=…" StampMarker appends — followed by ",D=…" for the kinds it
        // also gives a duration slot.
        static readonly Regex StampedTime = new Regex(@",T=(\d+(?:\.\d+)?)(?:,D=(\d+(?:\.\d+)?))?");

        static readonly Regex StampedCue = new Regex(@"^\[(.*),T=(\d+(?:\.\d+)?)\]$", RegexOptions.Singleline);

        static readonly Regex BakedTime = new Regex(@",T=\d");

        /// <summary>
        /// Turns a <c>_timed.txt</c> back into the script it was stamped from —
        /// the inverse of <see cref="TtsScriptProcessor.RebuildTimedScript"/>.
        /// Every removed <c>T=</c> is appended to <paramref name="times"/> in
        /// order (pass null to discard them). Forms StampMarker collapses come
        /// back in their canonical spelling — <c>{Image:x,0}</c> and
        /// <c>{Image:x}</c> stamp identically, so both read back as the latter —
        /// which re-stamps to the same file.
        /// </summary>
        public static string Unstamp(string timedScript, List<float> times)
        {
            timedScript = TtsScriptProcessor.NormaliseLineEndings(timedScript);
            var sb = new StringBuilder(timedScript.Length);
            int cursor = 0;
            foreach (Match m in StampedTag.Matches(timedScript))
            {
                sb.Append(timedScript, cursor, m.Index - cursor);
                sb.Append(UnstampTag(m.Value, out float? t));
                if (t.HasValue) times?.Add(t.Value);
                cursor = m.Index + m.Length;
            }
            sb.Append(timedScript, cursor, timedScript.Length - cursor);
            return sb.ToString();
        }

        static string UnstampTag(string tag, out float? time)
        {
            time = null;

            if (tag[0] == '[')
            {
                Match cue = StampedCue.Match(tag);
                if (!cue.Success) return tag;
                time = ParseFloat(cue.Groups[2].Value);
                return "[" + cue.Groups[1].Value + "]";
            }

            // StampMarker writes T= after every quoted field, so only the tail
            // past the last quote is searched — card text can say anything.
            string inner = tag.Substring(1, tag.Length - 2);
            int tailFrom = inner.LastIndexOf('"') + 1;
            Match stamp = null;
            foreach (Match c in StampedTime.Matches(inner, tailFrom)) stamp = c;
            if (stamp == null) return tag;

            time = ParseFloat(stamp.Groups[1].Value);
            string head = inner.Substring(0, stamp.Index);
            string rest = inner.Substring(stamp.Index + stamp.Length);
            string duration = stamp.Groups[2].Success
                ? UnstampDuration(KindOf(head), stamp.Groups[2].Value)
                : "";

            // {Black:3} is stamped {Black:D=3,T=…} — the one kind whose D= comes first.
            if (head.StartsWith("Black:D=", StringComparison.Ordinal))
                head = "Black:" + head.Substring("Black:D=".Length);

            return "{" + head + duration + rest + "}";
        }

        // D=0 is what StampMarker writes for "no duration" (Image, Video,
        // line-by-line BigText) and for the held "Start" form (side cards).
        static string UnstampDuration(string kind, string d)
        {
            bool zero = d == "0";
            switch (kind)
            {
                case "Image": case "Video": case "BigText":
                    return zero ? "" : "," + d;
                case "Headline": case "Excerpt": case "Quote": case "Stat": case "Logo": case "BRoll":
                    return zero ? ",Start" : "," + d;
                default:
                    return "," + d;
            }
        }

        static string KindOf(string head)
        {
            int colon = head.IndexOf(':');
            return colon < 0 ? head : head.Substring(0, colon);
        }

        static float ParseFloat(string s)
            => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

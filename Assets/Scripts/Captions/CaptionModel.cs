using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MugsTech.Captions
{
    /// <summary>One word of a section's word_timestamps/&lt;slug&gt;_words.json (section seconds, the alignment's timeline).</summary>
    [Serializable]
    public sealed class RawWord
    {
        public string word;
        public float start;
        public float end;
    }

    /// <summary>A spoken word of the script, with its time on the take's clock.</summary>
    public sealed class CaptionWord
    {
        public string Text;            // as written (tags and [cues] removed)
        public string Key;             // for matching: lower case, letters/digits/apostrophes
        public int Section, Line, ScriptIndex;
        public bool FirstLine;         // the cold open's first spoken line — never captioned
        public bool ClauseEnd, SentenceEnd;
        public bool Timed;
        public float Start, End;       // take seconds (start snapped onto a measured word onset, like T=)
        public float RawStart;         // the start without the snap (gaps for splitting and joining)
        public float FallbackStart, FallbackEnd; // from the T= stamps that bracket it
        public int Letters;
    }

    public enum PhraseTiming
    {
        Words,      // every word plausible — the current word can be highlighted
        Span,       // words implausible, the phrase's own start/end fine — shown whole
        Fallback,   // not even that — shown over its share of the bracketing T= stamps
    }

    public sealed class CaptionPhrase
    {
        public readonly List<CaptionWord> Words = new List<CaptionWord>();
        public PhraseTiming Timing;
        public string Why;             // why it isn't word-timed
        public bool Excluded;          // the cold open's first line
        public float Start, End;       // on screen from … to (take seconds)

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Words.Count; i++) { if (i > 0) sb.Append(' '); sb.Append(Display(i)); }
                return sb.ToString();
            }
        }

        /// <summary>The word as shown: as written, a dangling , ; : trimmed off the phrase's last word.</summary>
        public string Display(int i)
        {
            string t = Words[i].Text;
            if (i == Words.Count - 1) t = t.TrimEnd(',', ';', ':');
            return t;
        }

        /// <summary>The word being spoken at <paramref name="t"/> (−1 before the first or after the last word ends).</summary>
        public int WordAt(float t)
        {
            if (Timing != PhraseTiming.Words) return -1;
            for (int i = Words.Count - 1; i >= 0; i--)
                if (t >= Words[i].Start)
                    return i == Words.Count - 1 && t > Words[i].End ? -1 : i;
            return -1;
        }
    }

    public sealed class CaptionTrack
    {
        public readonly List<CaptionPhrase> Phrases = new List<CaptionPhrase>();
        public int Words, Unmatched;

        public int Count(PhraseTiming t)
        {
            int n = 0;
            foreach (var p in Phrases) if (!p.Excluded && p.Timing == t) n++;
            return n;
        }

        /// <summary>The phrase on screen at take time <paramref name="t"/>, or null.</summary>
        public CaptionPhrase At(float t)
        {
            int lo = 0, hi = Phrases.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                CaptionPhrase p = Phrases[mid];
                if (t < p.Start) hi = mid - 1;
                else if (t >= p.End) lo = mid + 1;
                else return p;
            }
            return null;
        }
    }

    /// <summary>
    /// Builds the captions from the script and the generation's word timings.
    /// Pure — SegmentSequencer feeds it each section with the same time mapping
    /// its T= stamps get, and the self-tests feed it real generation data.
    ///
    /// TEXT — the script's spoken words exactly: tags and [stage directions]
    /// out, everything else as written. The word timings only lend times
    /// (matched word by word; the voiced cues in the timings are skipped).
    /// PHRASES — 3–7 words: cut at punctuation first, long clauses at their
    /// largest gap between words, short ones joined to a neighbour (never over
    /// a gap of more than 0.6 s, never across sections).
    /// TIMING — word-level when every word is plausible (in order; 120–900 ms
    /// voiced, measured pauses not counted; words of three letters or fewer may
    /// be shorter: 40 ms, two or fewer 15 ms; none starting inside a measured
    /// pause), else the phrase as a whole over its own span when that is sane,
    /// else over its share of the span between the T= stamps that bracket it
    /// (the section's measured end of speech closes the last span).
    /// Every check reads a word's start AFTER the pause snap its T= would get:
    /// v3 often puts the word after a breath a few hundred ms early, inside the
    /// measured pause, and the snap moves it onto the audible onset — the same
    /// correction the markers get, so those words are timed, not discarded.
    /// ON SCREEN — first word's start to last word's end + 150 ms, bridged to
    /// the next phrase when the gap is 600 ms or less; the cold open's first
    /// spoken line never. A fallback phrase is only a guess, so it is kept
    /// between its neighbours in the script — after the phrase before it ends
    /// and before the next timed one starts — and never cuts a timed phrase short.
    /// </summary>
    public static class CaptionBuilder
    {
        public const int MinWords = 3, MaxWords = 7;
        public const float BridgeGap = 0.6f, Linger = 0.15f;
        public const float MaxVoiced = 0.9f, FloorLong = 0.12f, FloorThree = 0.04f, FloorShort = 0.015f;
        public const float FallbackSecondsPerChar = 0.07f;

        public sealed class Section
        {
            public int Index;
            public string Script;                       // the section's timed script, T= on the take's clock
            public List<RawWord> Words;                 // word_timestamps (section seconds); null = none
            public Func<float, bool, float> Map;        // section seconds → take seconds (bool: snap a start onto a word onset)
            public List<(float From, float To)> Pauses; // measured pauses, take seconds
            public float SpeechEnd = float.NaN;         // the section's measured end of speech, take seconds (NaN = unknown)
        }

        public static CaptionTrack Build(IList<Section> sections)
        {
            var track = new CaptionTrack();
            foreach (Section s in sections)
            {
                List<CaptionWord> words = Narration(s.Script ?? "", s.Index, out var stamps);
                track.Words += words.Count;
                track.Unmatched += Attach(words, s.Words, s.Map);
                ApplyFallback(words, stamps, s.SpeechEnd);
                foreach (List<CaptionWord> group in Split(words))
                {
                    var p = new CaptionPhrase();
                    p.Words.AddRange(group);
                    p.Excluded = group[0].FirstLine;
                    p.Timing = Judge(group, s.Pauses, out p.Why);
                    track.Phrases.Add(p);
                }
            }
            Windows(track.Phrases);
            return track;
        }

        // ---------------------------------------------------------------------
        // The script's spoken words
        // ---------------------------------------------------------------------

        static readonly Regex TagOrCue = new Regex(@"\{(?:[^{}""\n]|""[^""]*"")*\}|\[[^\]]*\]");
        static readonly Regex Stamp = new Regex(@",T=(\d+(?:\.\d+)?)");
        static readonly Regex Space = new Regex(@"\S+");

        /// <summary>
        /// The section's narration words (tags, [cues] and ## headings removed),
        /// and the T= of every stamped tag with its place in the script.
        /// </summary>
        public static List<CaptionWord> Narration(string script, int section, out List<(int Index, float T)> stamps)
        {
            var words = new List<CaptionWord>();
            stamps = new List<(int, float)>();
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < script.Length; i++) if (script[i] == '\n') lineStarts.Add(i + 1);
            int pos = 0;
            foreach (Match m in TagOrCue.Matches(script))
            {
                AddWords(script, pos, m.Index, section, lineStarts, words);
                if (m.Value[0] == '{')
                {
                    Match t = Stamp.Match(m.Value);
                    if (t.Success)
                        stamps.Add((m.Index, float.Parse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture)));
                }
                pos = m.Index + m.Length;
            }
            AddWords(script, pos, script.Length, section, lineStarts, words);

            if (section == 0 && words.Count > 0)
            {
                int first = words[0].Line;
                foreach (CaptionWord w in words) if (w.Line == first) w.FirstLine = true;
            }
            return words;
        }

        static void AddWords(string script, int from, int to, int section, List<int> lineStarts, List<CaptionWord> into)
        {
            if (to <= from) return;
            string chunk = script.Substring(from, to - from);
            foreach (Match m in Space.Matches(chunk))
            {
                int at = from + m.Index;
                int line = lineStarts.BinarySearch(at);
                if (line < 0) line = ~line - 1;
                int k = lineStarts[line];
                while (k < script.Length && (script[k] == ' ' || script[k] == '\t')) k++;
                if (k + 1 < script.Length && script[k] == '#' && script[k + 1] == '#') continue;   // a ## heading
                string text = m.Value;
                string key = Key(text);
                if (key.Length == 0)
                {
                    // A lone dash or ellipsis: part of the previous word, not a word of its own.
                    if (into.Count > 0 && into[into.Count - 1].Section == section)
                    {
                        CaptionWord prev = into[into.Count - 1];
                        prev.Text += " " + text;
                        prev.ClauseEnd = true;
                    }
                    continue;
                }
                into.Add(new CaptionWord
                {
                    Text = text,
                    Key = key,
                    Section = section,
                    Line = line,
                    ScriptIndex = at,
                    Letters = key.Length,
                    SentenceEnd = Regex.IsMatch(text, @"[.!?…]['""’”)\]]*$"),
                    ClauseEnd = Regex.IsMatch(text, @"[.!?…,;:—–]['""’”)\]]*$"),
                });
            }
        }

        /// <summary>A word for matching: lower case, letters, digits and apostrophes only.</summary>
        public static string Key(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '\'' || c == '’') sb.Append('\'');
            return sb.ToString().Trim('\'');
        }

        // ---------------------------------------------------------------------
        // Times from the word timings
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lends each script word the time of the matching timed word (in order,
        /// a short look-ahead; voiced [cues] in the timings are skipped first).
        /// Returns how many script words found no match.
        /// </summary>
        public static int Attach(List<CaptionWord> words, List<RawWord> raw, Func<float, bool, float> map)
        {
            if (raw == null || raw.Count == 0) return words.Count;
            map = map ?? ((t, snap) => t);

            var spoken = new List<RawWord>(raw.Count);
            bool inCue = false;
            foreach (RawWord r in raw)
            {
                string w = r.word ?? "";
                if (!inCue && w.StartsWith("[")) inCue = true;
                if (!inCue) spoken.Add(r);
                if (inCue && w.Contains("]")) inCue = false;
            }

            int j = 0, unmatched = 0;
            foreach (CaptionWord w in words)
            {
                int found = -1;
                for (int k = j; k < spoken.Count && k < j + 12; k++)
                    if (Key(spoken[k].word ?? "") == w.Key) { found = k; break; }
                if (found < 0) { unmatched++; continue; }
                RawWord r = spoken[found];
                w.Timed = true;
                w.Start = map(r.start, true);
                w.RawStart = map(r.start, false);
                w.End = Math.Max(w.RawStart, map(r.end, false));
                j = found + 1;
            }
            return unmatched;
        }

        /// <summary>
        /// Each word's share of the span between the stamped tags around it: the
        /// words between two tags split that span by their length. A tag fires
        /// on the next word, so the span runs from the first word to the word
        /// after the last. The words after the section's last tag run to
        /// <paramref name="sectionEnd"/> (its measured end of speech) when known,
        /// else 70 ms a letter.
        /// </summary>
        public static void ApplyFallback(List<CaptionWord> words, List<(int Index, float T)> stamps,
                                         float sectionEnd = float.NaN)
        {
            if (words.Count == 0) return;
            stamps.Sort((a, b) => a.Index.CompareTo(b.Index));
            int w = 0;
            while (w < words.Count)
            {
                // The group: words after the same last tag.
                int tagBefore = -1;
                for (int s = 0; s < stamps.Count && stamps[s].Index < words[w].ScriptIndex; s++) tagBefore = s;
                int tagAfter = tagBefore + 1;
                int end = w;
                while (end + 1 < words.Count &&
                       (tagAfter >= stamps.Count || words[end + 1].ScriptIndex < stamps[tagAfter].Index))
                    end++;

                int chars = 0;
                for (int i = w; i <= end; i++) chars += words[i].Letters + 1;
                float from = tagBefore >= 0 ? stamps[tagBefore].T : (words[w].Timed ? words[w].Start : 0f);
                float to = tagAfter < stamps.Count ? stamps[tagAfter].T : sectionEnd;
                if (float.IsNaN(to) || to <= from + 0.05f) to = from + chars * FallbackSecondsPerChar;

                float t = from, per = (to - from) / Math.Max(1, chars);
                for (int i = w; i <= end; i++)
                {
                    words[i].FallbackStart = t;
                    t += (words[i].Letters + 1) * per;
                    words[i].FallbackEnd = t;
                }
                w = end + 1;
            }
        }

        // ---------------------------------------------------------------------
        // Phrases
        // ---------------------------------------------------------------------

        /// <summary>Phrases of 3–7 words: punctuation first, then the largest gap, then the short ones joined.</summary>
        public static List<List<CaptionWord>> Split(List<CaptionWord> words)
        {
            var clauses = new List<List<CaptionWord>>();
            var cur = new List<CaptionWord>();
            for (int i = 0; i < words.Count; i++)
            {
                if (cur.Count > 0 && (words[i].Section != cur[0].Section || words[i].FirstLine != cur[0].FirstLine))
                {
                    clauses.Add(cur);
                    cur = new List<CaptionWord>();
                }
                cur.Add(words[i]);
                if (words[i].ClauseEnd) { clauses.Add(cur); cur = new List<CaptionWord>(); }
            }
            if (cur.Count > 0) clauses.Add(cur);

            var parts = new List<List<CaptionWord>>();
            foreach (var c in clauses) SplitLong(c, parts);

            // Join the short ones: a phrase that ends a sentence leans back,
            // one mid-sentence leans forward; never over a long gap.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Count >= MinWords) continue;
                    bool endsSentence = parts[i][parts[i].Count - 1].SentenceEnd;
                    int[] order = endsSentence ? new[] { i - 1, i + 1 } : new[] { i + 1, i - 1 };
                    foreach (int k in order)
                    {
                        if (k < 0 || k >= parts.Count) continue;
                        var a = k < i ? parts[k] : parts[i];
                        var b = k < i ? parts[i] : parts[k];
                        if (!CanJoin(a, b)) continue;
                        a.AddRange(b);
                        parts.RemoveAt(k < i ? i : k);
                        changed = true;
                        break;
                    }
                    if (changed) break;
                }
            }
            return parts;
        }

        static bool CanJoin(List<CaptionWord> a, List<CaptionWord> b)
        {
            if (a.Count + b.Count > MaxWords) return false;
            CaptionWord last = a[a.Count - 1], first = b[0];
            if (last.Section != first.Section || last.FirstLine != first.FirstLine) return false;
            if (last.Timed && first.Timed && first.RawStart - last.End > BridgeGap) return false;
            return true;
        }

        static void SplitLong(List<CaptionWord> c, List<List<CaptionWord>> into)
        {
            if (c.Count <= MaxWords) { into.Add(c); return; }
            // The largest gap between consecutive words, both sides at least 3 words.
            int best = -1;
            float bestGap = float.NegativeInfinity, bestCentre = float.PositiveInfinity;
            for (int i = MinWords; i <= c.Count - MinWords; i++)
            {
                float gap = c[i].Timed && c[i - 1].Timed ? c[i].RawStart - c[i - 1].End : 0f;
                float centre = Math.Abs(i - c.Count / 2f);
                if (gap > bestGap + 1e-4f || (Math.Abs(gap - bestGap) <= 1e-4f && centre < bestCentre))
                {
                    best = i; bestGap = gap; bestCentre = centre;
                }
            }
            SplitLong(c.GetRange(0, best), into);
            SplitLong(c.GetRange(best, c.Count - best), into);
        }

        // ---------------------------------------------------------------------
        // Is the timing believable?
        // ---------------------------------------------------------------------

        public static float Floor(int letters) => letters >= 4 ? FloorLong : letters == 3 ? FloorThree : FloorShort;

        /// <summary>
        /// How a phrase can be timed, and why not word by word. Reads each word's
        /// snapped <see cref="CaptionWord.Start"/> — the onset its T= would get.
        /// </summary>
        public static PhraseTiming Judge(List<CaptionWord> p, List<(float From, float To)> pauses, out string why)
        {
            why = null;
            for (int i = 0; i < p.Count && why == null; i++)
            {
                CaptionWord w = p[i];
                if (!w.Timed) { why = $"\"{w.Text}\" has no timing"; break; }
                if (i > 0 && w.Start < p[i - 1].Start + 0.02f) { why = $"\"{w.Text}\" starts out of order"; break; }
                // Still inside after the snap: more than 0.9 s before the pause ends.
                if (InPause(w.Start, pauses)) { why = $"\"{w.Text}\" starts inside a measured pause"; break; }
                // A word lasts until the next one starts (the timings fold the
                // pause after a word into its end); measured pauses don't count.
                bool hasNext = i + 1 < p.Count && p[i + 1].Timed;
                float own = (hasNext ? Math.Min(w.End, p[i + 1].Start) : w.End) - w.Start;
                own -= PauseOverlap(w.Start, w.Start + Math.Max(0f, own), pauses);
                if (own < Floor(w.Letters)) { why = $"\"{w.Text}\" lasts {own * 1000f:F0} ms"; break; }
                if (hasNext)
                {
                    float toNext = p[i + 1].Start - w.Start - PauseOverlap(w.Start, p[i + 1].Start, pauses);
                    if (toNext > MaxVoiced) why = $"\"{w.Text}\" runs {toNext * 1000f:F0} ms to the next word";
                }
            }
            if (why == null) return PhraseTiming.Words;

            CaptionWord a = p[0], b = p[p.Count - 1];
            if (a.Timed && b.Timed && !InPause(a.Start, pauses))
            {
                float span = b.End - a.Start;
                if (span >= 0.1f * p.Count && span <= 1.0f * p.Count + 1.0f) return PhraseTiming.Span;
            }
            return PhraseTiming.Fallback;
        }

        static bool InPause(float t, List<(float From, float To)> pauses)
        {
            if (pauses == null) return false;
            foreach (var q in pauses)
                if (t > q.From + 0.1f && t < q.To - 0.05f) return true;
            return false;
        }

        static float PauseOverlap(float a, float b, List<(float From, float To)> pauses)
        {
            if (pauses == null || b <= a) return 0f;
            float o = 0f;
            foreach (var q in pauses) o += Math.Max(0f, Math.Min(b, q.To) - Math.Max(a, q.From));
            return o;
        }

        // ---------------------------------------------------------------------
        // On screen
        // ---------------------------------------------------------------------

        /// <summary>
        /// Each phrase's time on screen: its span + 150 ms, bridged over a gap of
        /// 600 ms or less, never overlapping. <paramref name="phrases"/> arrive in
        /// script order; a fallback phrase is kept between its neighbours there.
        /// </summary>
        public static void Windows(List<CaptionPhrase> phrases)
        {
            foreach (CaptionPhrase p in phrases)
            {
                CaptionWord a = p.Words[0], b = p.Words[p.Words.Count - 1];
                if (p.Timing == PhraseTiming.Fallback)
                {
                    p.Start = a.FallbackStart;
                    p.End = b.FallbackEnd;
                }
                else
                {
                    p.Start = a.Start;
                    p.End = Math.Max(b.Start + Floor(b.Letters), Math.Min(b.End, b.Start + MaxVoiced));
                }
            }

            // A fallback phrase is only a guess: it may not start before the
            // phrase ahead of it in the script has been said, nor run into the
            // next one with real times — it moves (keeping its length) or
            // shrinks to fit the room between them.
            for (int i = 0; i < phrases.Count; i++)
            {
                CaptionPhrase p = phrases[i];
                if (p.Timing != PhraseTiming.Fallback) continue;
                float lo = i > 0 ? phrases[i - 1].End : float.NegativeInfinity;
                float hi = float.PositiveInfinity;
                for (int k = i + 1; k < phrases.Count; k++)
                    if (phrases[k].Timing != PhraseTiming.Fallback) { hi = phrases[k].Start; break; }
                float dur = Math.Max(0f, p.End - p.Start);
                float s = Math.Max(p.Start, lo), e = s + dur;
                if (e > hi) { e = hi; s = Math.Max(lo, hi - dur); }
                p.Start = s;
                p.End = Math.Max(s, e);
            }

            // No room at all: it never shows — out of the track, so the windows
            // left stay sorted and apart (CaptionTrack.At searches them).
            phrases.RemoveAll(p => p.End <= p.Start);

            var order = new Dictionary<CaptionPhrase, int>();
            for (int i = 0; i < phrases.Count; i++) order[phrases[i]] = i;
            phrases.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : order[x].CompareTo(order[y]));
            for (int i = 0; i < phrases.Count; i++)
            {
                CaptionPhrase p = phrases[i];
                float next = i + 1 < phrases.Count ? phrases[i + 1].Start : float.PositiveInfinity;
                float end = next - p.End <= BridgeGap ? next : p.End + Linger;
                p.End = Math.Max(p.Start, Math.Min(end, next));
            }
        }
    }
}

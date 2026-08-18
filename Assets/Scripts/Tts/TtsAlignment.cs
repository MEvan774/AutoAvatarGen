using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MugsTech.Tts
{
    /// <summary>
    /// The character-level alignment ElevenLabs returns alongside the audio:
    /// one start/end time per character of the text that was sent.
    ///
    /// This is the map the splitter cuts on. The word-level view the rest of
    /// the pipeline has always used (TtsScriptProcessor.WordTimestamp) is
    /// derived from it and stays available — but a marker's position is a
    /// character index, so reading the time directly off the character is both
    /// simpler and more precise than locating the word that contains it.
    ///
    /// Use the <c>alignment</c> object from the API, never
    /// <c>normalized_alignment</c>: only the former indexes the exact string
    /// that was sent, which is what every offset here assumes.
    /// </summary>
    [Serializable]
    public class TtsAlignment
    {
        public string[] characters                    = Array.Empty<string>();
        public float[]  character_start_times_seconds = Array.Empty<float>();
        public float[]  character_end_times_seconds   = Array.Empty<float>();

        /// <summary>True when the API's alignment was unusable and these times
        /// were spread evenly across the text instead. Cuts still land, but at
        /// estimated positions — the run is worth re-doing.</summary>
        public bool isProportionalFallback;

        public int Length => characters == null ? 0 : characters.Length;

        public bool IsUsable =>
            Length > 0 &&
            character_start_times_seconds != null &&
            character_end_times_seconds   != null &&
            character_start_times_seconds.Length >= Length &&
            character_end_times_seconds.Length   >= Length;

        /// <summary>Total length of the alignment's timeline.</summary>
        public float TotalSeconds =>
            Length == 0 ? 0f : character_end_times_seconds[Length - 1];

        // ---- reads ------------------------------------------------------

        public float StartAt(int index)
        {
            if (Length == 0) return 0f;
            index = Clamp(index, 0, Length - 1);
            return character_start_times_seconds[index];
        }

        public float EndAt(int index)
        {
            if (Length == 0) return 0f;
            index = Clamp(index, 0, Length - 1);
            return character_end_times_seconds[index];
        }

        /// <summary>
        /// End time of the last character in <c>[start, end)</c> that is not
        /// whitespace — the moment the section actually stops being spoken.
        /// Trailing newlines carry times of their own and would otherwise drag
        /// the cut past the end of speech.
        /// </summary>
        public float EndOfLastVisible(int start, int end)
        {
            end   = Clamp(end,   0, Length);
            start = Clamp(start, 0, end);
            for (int i = end - 1; i >= start; i--)
                if (!IsBlank(characters[i])) return character_end_times_seconds[i];
            return start < Length ? character_start_times_seconds[Clamp(start, 0, Length - 1)] : 0f;
        }

        /// <summary>
        /// Start time of the first non-whitespace character in <c>[start, end)</c>
        /// — the moment the next section begins being spoken.
        /// </summary>
        public float StartOfFirstVisible(int start, int end)
        {
            end   = Clamp(end,   0, Length);
            start = Clamp(start, 0, end);
            for (int i = start; i < end; i++)
                if (!IsBlank(characters[i])) return character_start_times_seconds[i];
            return end > 0 ? character_end_times_seconds[Clamp(end - 1, 0, Length - 1)] : 0f;
        }

        static bool IsBlank(string ch)
            => string.IsNullOrEmpty(ch) || char.IsWhiteSpace(ch[0]);

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ---- construction ------------------------------------------------

        /// <summary>
        /// True when the alignment describes exactly the text that was sent.
        /// ElevenLabs echoes one entry per input character; anything else means
        /// every offset the splitter holds is pointing at the wrong character.
        /// </summary>
        public bool MatchesText(string text)
            => text != null && IsUsable && Length == text.Length;

        /// <summary>
        /// Times spread evenly over the text. The last-resort mapping for when
        /// the API's alignment doesn't describe the text that was sent — cuts
        /// land in roughly the right place instead of the run dying.
        /// </summary>
        public static TtsAlignment Proportional(string text, float durationSeconds)
        {
            text = text ?? "";
            int n = text.Length;
            var a = new TtsAlignment {
                characters                    = new string[n],
                character_start_times_seconds = new float[n],
                character_end_times_seconds   = new float[n],
                isProportionalFallback        = true,
            };

            float per = n > 0 ? Math.Max(0f, durationSeconds) / n : 0f;
            for (int i = 0; i < n; i++)
            {
                a.characters[i]                    = text[i].ToString();
                a.character_start_times_seconds[i] = per * i;
                a.character_end_times_seconds[i]   = per * (i + 1);
            }
            return a;
        }

        /// <summary>
        /// A slice of this alignment covering <c>[start, end)</c>, with times
        /// rebased so the slice starts at zero. Used to emit the per-section
        /// word_timestamps debug files from a chunk-wide alignment.
        /// </summary>
        public TtsAlignment Slice(int start, int end, float timeOrigin)
        {
            end   = Clamp(end,   0, Length);
            start = Clamp(start, 0, end);
            int n = end - start;

            var a = new TtsAlignment {
                characters                    = new string[n],
                character_start_times_seconds = new float[n],
                character_end_times_seconds   = new float[n],
                isProportionalFallback        = isProportionalFallback,
            };
            for (int i = 0; i < n; i++)
            {
                a.characters[i]                    = characters[start + i];
                a.character_start_times_seconds[i] = character_start_times_seconds[start + i] - timeOrigin;
                a.character_end_times_seconds[i]   = character_end_times_seconds[start + i]   - timeOrigin;
            }
            return a;
        }

        /// <summary>Word-level view, for the parts of the pipeline that still speak words.</summary>
        public List<TtsScriptProcessor.WordTimestamp> ToWords()
            => TtsScriptProcessor.GroupCharsIntoWords(
                characters, character_start_times_seconds, character_end_times_seconds);

        /// <summary>
        /// Rebuild a character alignment from WORD timings measured on the
        /// rendered audio (<c>/v1/forced-alignment</c>). Each word's characters
        /// are spread evenly across its measured span and the gaps between words
        /// are interpolated, so the result indexes <paramref name="text"/> the
        /// same way the synthesis alignment does — but describes the audio that
        /// actually exists.
        ///
        /// This keeps the forced-alignment defence layer meaningful now that the
        /// alignment decides where the audio is CUT, not just when markers fire.
        /// Returns null when the words can't be located in the text.
        /// </summary>
        public static TtsAlignment FromWords(
            string text, List<TtsScriptProcessor.WordTimestamp> words)
        {
            if (string.IsNullOrEmpty(text) || words == null || words.Count == 0) return null;

            int n = text.Length;
            var starts  = new float[n];
            var ends    = new float[n];
            var known   = new bool[n];
            var chars   = new string[n];
            for (int i = 0; i < n; i++) chars[i] = text[i].ToString();

            int cursor = 0, located = 0;
            foreach (var w in words)
            {
                string word = w.Word ?? "";
                if (word.Length == 0) continue;

                int at = text.IndexOf(word, Math.Min(cursor, n), StringComparison.Ordinal);
                if (at < 0) continue;              // dropped word — interpolation covers it
                located++;

                float span = Math.Max(0f, w.End - w.Start);
                for (int k = 0; k < word.Length && at + k < n; k++)
                {
                    starts[at + k] = w.Start + span * (k / (float)word.Length);
                    ends  [at + k] = w.Start + span * ((k + 1) / (float)word.Length);
                    known [at + k] = true;
                }
                cursor = at + word.Length;
            }

            // Too little of the text placed = these words don't describe this
            // text, and an alignment built from the few that matched would put
            // most characters at time zero — which, now that cut points come
            // from here, would collapse whole sections. Reject and let the
            // caller keep the synthesis alignment instead.
            if (located == 0 || located * 4 < words.Count * 3) return null;

            FillGaps(starts, ends, known);
            return new TtsAlignment {
                characters                    = chars,
                character_start_times_seconds = starts,
                character_end_times_seconds   = ends,
            };
        }

        // Every character between two located words (spaces, punctuation, tags)
        // gets a time interpolated across the hole, so the array is dense and
        // the splitter can read any index without probing.
        static void FillGaps(float[] starts, float[] ends, bool[] known)
        {
            int n = known.Length;

            int firstKnown = -1, lastKnown = -1;
            for (int i = 0; i < n; i++) if (known[i]) { if (firstKnown < 0) firstKnown = i; lastKnown = i; }
            if (firstKnown < 0) return;

            for (int i = 0; i < firstKnown; i++) { starts[i] = 0f; ends[i] = starts[firstKnown]; }
            for (int i = lastKnown + 1; i < n; i++) { starts[i] = ends[lastKnown]; ends[i] = ends[lastKnown]; }

            int gapStart = -1;
            for (int i = firstKnown; i <= lastKnown; i++)
            {
                if (!known[i]) { if (gapStart < 0) gapStart = i; continue; }
                if (gapStart < 0) continue;

                float from = ends[gapStart - 1];
                float to   = starts[i];
                int   len  = i - gapStart;
                for (int k = 0; k < len; k++)
                {
                    starts[gapStart + k] = from + (to - from) * (k       / (float)len);
                    ends  [gapStart + k] = from + (to - from) * ((k + 1) / (float)len);
                }
                gapStart = -1;
            }
        }

        // ---- persistence (dry-run cache + debug trail) --------------------

        public string ToJson() => UnityEngine.JsonUtility.ToJson(this);

        public static TtsAlignment FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var a = UnityEngine.JsonUtility.FromJson<TtsAlignment>(json);
            return a != null && a.IsUsable ? a : null;
        }

        /// <summary>
        /// Compact human-readable dump — one line per character run, for
        /// eyeballing a seam in the render manifest folder.
        /// </summary>
        public string ToDebugText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Length; i++)
            {
                sb.Append(character_start_times_seconds[i].ToString("0.000", CultureInfo.InvariantCulture))
                  .Append('\t')
                  .Append(character_end_times_seconds[i].ToString("0.000", CultureInfo.InvariantCulture))
                  .Append('\t')
                  .Append(characters[i] == "\n" ? "\\n" : characters[i])
                  .Append('\n');
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MugsTech.Tts
{
    /// <summary>
    /// Tags written on the same beat fire on the same frame.
    ///
    /// A RUN is tags with no spoken word between them: tags on adjacent lines
    /// between two spoken lines, or side by side on one line. [Stage
    /// directions] are not spoken words here. The chunked pipeline stamps every
    /// own-line tag with the time of its OWN whitespace character
    /// (ChunkedTtsGenerationJob.WriteSection, <c>alignment.StartAt(CleanIndex)</c>),
    /// and eleven_v3 spreads those characters across the silence. On the
    /// 2026-11-10 Cybertruck take <c>{Shrug,Cut}</c> got T=67.088 and the
    /// <c>{Zoom:ExtremeOut}</c> on the next line T=67.487, while the audio was
    /// silent from 66.44 until "So" at 67.36. SegmentSequencer's pause snap moved
    /// Shrug onto that onset. ExtremeOut was already past it and stayed, so
    /// the expression changed four frames before the camera cut.
    ///
    /// So when a tag in a run snaps forward onto a measured pause's end (the
    /// onset of the word after it), every LATER tag in the same run that the
    /// mapping put after that onset takes the onset too. Tags are only ever
    /// pulled back to it, never pushed later, and only:
    ///   • within <see cref="MaxPullBackSeconds"/> of the onset (the snap's own
    ///     radius, SegmentSequencer.PauseSnapMaxSeconds; past that the alignment
    ///     is too far off to call it the same beat);
    ///   • when no measured pause starts between the onset and the tag (a voiced
    ///     [laughs] between two tags is sound, so a tag after it belongs to the
    ///     next stretch of voice);
    ///   • never in a run that holds a <c>{Pause}</c>. PauseInserter pins those
    ///     to the gap's edges (above fires as it opens, below as it closes).
    /// Tags before the snapped one keep their times. Words are never moved, so
    /// captions are untouched; the word after a run snaps onto the same onset.
    ///
    /// eleven_v4 (VoiceTiming.FirstTagSetsBeat). v4 gives a break's pause to the
    /// sentence before it and starts the newline characters at the silence's
    /// END: on the 2026-11-17 lab takes an own-line tag was stamped a median
    /// 0.01 s before the next word's onset, so it rarely needs the snap — and
    /// with no snap the rule above never starts. Two tags on adjacent lines then
    /// kept the few hundredths between their own characters: different frames
    /// at 30 of 92 breaks, 1–4 frames apart, 0.24 s at worst. So for a
    /// generation voiced on v4, when no tag of a run has snapped the run's
    /// FIRST tag sets the beat and the later ones are pulled back onto it,
    /// inside the same limits. A snap later in the run still takes over — and
    /// then the tags BEFORE it join that onset too (at most 0.9 s, not over
    /// another measured pause): on v4 an own-line tag stamped before its
    /// silence began is the alignment running early (lab take 1, 40.72 s: the
    /// first newline 0.14 s before the silence, the second inside it, 0.46 s
    /// apart after the snap). This is the one place a tag moves LATER, and it
    /// is v4 only: on v3 a tag above a silence fires as the sentence ends.
    /// Pure C#: no Unity types, no IO.
    /// </summary>
    public static class TagBeats
    {
        /// <summary>Furthest a tag is pulled back onto its run's onset (= SegmentSequencer.PauseSnapMaxSeconds).</summary>
        public const float MaxPullBackSeconds = 0.9f;

        // A tag or a stage direction: never spoken. Quoted card fields may hold
        // braces, so they are skipped as a unit (PauseInserter.Unspoken).
        static readonly Regex Unspoken = new Regex(@"\{(?:[^{}""\n]|""[^""]*"")*\}|\[[^\]]*\]");

        /// <summary>
        /// A baked <c>T=</c> stamp. It always follows a <c>,</c> or the tag's
        /// <c>:</c>, and never sits inside a quoted field — so the data of a
        /// <c>{Graph:"…","ChatGPT=400M;AT&amp;T=22M",…}</c> is not mistaken for
        /// one (the look-ahead: only whole quoted fields between it and the
        /// tag's close). SegmentSequencer._TPattern IS this pattern: the match
        /// indices must line up with its Replace.
        /// </summary>
        public const string StampPattern =
            @"(?<=[,:])T=(\d+(?:\.\d+)?)(?=(?:[^""{}\n]*""[^""\n]*"")*[^""{}\n]*[}\]])";
        static readonly Regex TStamp = new Regex(StampPattern);

        static readonly Regex StampSuffix = new Regex(@",T=\d+(?:\.\d+)?");

        /// <summary>One <c>T=</c> in a section's timed script.</summary>
        public sealed class Stamp
        {
            public int    Index;        // where "T=" sits in the script
            public float  T;            // as written (the alignment's timeline)
            public int    Run = -1;     // stamps with the same Run have no spoken word between them; -1 = not in a tag
            public bool   Tag;          // inside a {tag} (false: a [stage direction], or loose text)
            public bool   PauseRun;     // its run holds a {Pause:…}
            public string Name;         // the tag without its T=, for the log: {Zoom:ExtremeOut}
        }

        /// <summary>A tag that was pulled back onto its run's onset.</summary>
        public sealed class Join
        {
            public Stamp Stamp;         // the tag that moved
            public Stamp SnappedBy;     // the tag whose snap set the onset
            public float From, To;      // measured-timeline seconds: where the mapping put it, the onset
            public bool  OnFirstTag;    // joined the run's first tag (v4), not a snapped onset
        }

        /// <summary>What a mapping did with one time: where it landed, and whether the pause snap moved it there.</summary>
        public readonly struct Mapped
        {
            public readonly float At;
            public readonly bool  Snapped;
            public Mapped(float at, bool snapped) { At = at; Snapped = snapped; }
        }

        /// <summary>Every <c>T=</c> in <paramref name="script"/>, in script order, with its run.</summary>
        public static List<Stamp> Find(string script)
        {
            var stamps = new List<Stamp>();
            if (string.IsNullOrEmpty(script)) return stamps;

            var spans = new List<(int Start, int End, int Run, bool Brace, string Text)>();
            var pauseRuns = new HashSet<int>();
            int run = -1, prevEnd = -1;
            foreach (Match m in Unspoken.Matches(script))
            {
                if (prevEnd < 0 || HasSpoken(script, prevEnd, m.Index)) run++;
                bool brace = m.Value[0] == '{';
                spans.Add((m.Index, m.Index + m.Length, run, brace, m.Value));
                if (brace && m.Value.StartsWith("{Pause:", StringComparison.Ordinal)) pauseRuns.Add(run);
                prevEnd = m.Index + m.Length;
            }

            int k = 0;
            foreach (Match m in TStamp.Matches(script))
            {
                var s = new Stamp { Index = m.Index, T = ParseFloat(m.Groups[1].Value) };
                while (k < spans.Count && spans[k].End <= m.Index) k++;
                if (k < spans.Count && spans[k].Start <= m.Index)
                {
                    s.Run      = spans[k].Run;
                    s.Tag      = spans[k].Brace;
                    s.PauseRun = pauseRuns.Contains(spans[k].Run);
                    s.Name     = StampSuffix.Replace(spans[k].Text, "");
                }
                stamps.Add(s);
            }
            return stamps;
        }

        /// <summary>
        /// Each stamp's time on the measured timeline: what <paramref name="map"/>
        /// says, with the run rule applied. <paramref name="pauses"/> are the
        /// section's measured pauses (start, end) on that same timeline.
        /// <paramref name="joins"/>, when given, collects every tag pulled back.
        /// <paramref name="firstTagSetsBeat"/> is the v4 rule (see the class
        /// summary); off, this is exactly what every v3 take was timed with.
        /// </summary>
        public static float[] Resolve(IList<Stamp> stamps, Func<float, Mapped> map,
                                      IList<(float From, float To)> pauses, List<Join> joins = null,
                                      bool firstTagSetsBeat = false)
        {
            var at = new float[stamps.Count];
            int run = int.MinValue;
            float onset = float.NaN;
            Stamp snappedBy = null;
            bool fromSnap = false;
            for (int i = 0; i < stamps.Count; i++)
            {
                Stamp s = stamps[i];
                Mapped m = map(s.T);
                at[i] = m.At;

                if (s.Run != run) { run = s.Run; onset = float.NaN; snappedBy = null; fromSnap = false; }
                if (s.Run < 0 || !s.Tag || s.PauseRun) continue;

                if (m.Snapped)
                {
                    onset = m.At; snappedBy = s; fromSnap = true;
                    if (firstTagSetsBeat) JoinEarlier(stamps, at, i, onset, pauses, joins);
                    continue;
                }
                if (float.IsNaN(onset))
                {
                    // v4: nothing has snapped, so this tag — the run's first — is the beat.
                    if (firstTagSetsBeat) { onset = m.At; snappedBy = s; }
                    continue;
                }
                if (m.At > onset && m.At - onset <= MaxPullBackSeconds && !PauseStartsBetween(pauses, onset, m.At))
                {
                    joins?.Add(new Join { Stamp = s, SnappedBy = snappedBy, From = m.At, To = onset,
                                          OnFirstTag = !fromSnap });
                    at[i] = onset;
                }
            }
            return at;
        }

        // v4: the tags of this run written before the one that just snapped.
        static void JoinEarlier(IList<Stamp> stamps, float[] at, int snapped, float onset,
                                IList<(float From, float To)> pauses, List<Join> joins)
        {
            Stamp by = stamps[snapped];
            for (int j = snapped - 1; j >= 0 && stamps[j].Run == by.Run; j--)
            {
                Stamp e = stamps[j];
                if (!e.Tag || e.PauseRun) continue;
                float was = at[j];
                if (was >= onset || onset - was > MaxPullBackSeconds) continue;
                if (PauseEndsBetween(pauses, was, onset)) continue;   // sound between them: another beat
                joins?.Add(new Join { Stamp = e, SnappedBy = by, From = was, To = onset });
                at[j] = onset;
            }
        }

        static bool PauseEndsBetween(IList<(float From, float To)> pauses, float a, float b)
        {
            if (pauses == null) return false;
            foreach (var p in pauses)
                if (p.To > a && p.To < b - 1e-4f) return true;
            return false;
        }

        static bool PauseStartsBetween(IList<(float From, float To)> pauses, float a, float b)
        {
            if (pauses == null) return false;
            foreach (var p in pauses)
                if (p.From > a && p.From < b) return true;
            return false;
        }

        static bool HasSpoken(string s, int from, int to)
        {
            for (int i = from; i < to; i++)
                if (!char.IsWhiteSpace(s[i])) return true;
            return false;
        }

        static float ParseFloat(string s)
            => float.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }
}

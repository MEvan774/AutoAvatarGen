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
    /// Pure C#: no Unity types, no IO.
    /// </summary>
    public static class TagBeats
    {
        /// <summary>Furthest a tag is pulled back onto its run's onset (= SegmentSequencer.PauseSnapMaxSeconds).</summary>
        public const float MaxPullBackSeconds = 0.9f;

        // A tag or a stage direction: never spoken. Quoted card fields may hold
        // braces, so they are skipped as a unit (PauseInserter.Unspoken).
        static readonly Regex Unspoken = new Regex(@"\{(?:[^{}""\n]|""[^""]*"")*\}|\[[^\]]*\]");

        // SegmentSequencer._TPattern: match indices line up with its Replace.
        static readonly Regex TStamp = new Regex(@"T=(\d+(?:\.\d+)?)");

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
        /// </summary>
        public static float[] Resolve(IList<Stamp> stamps, Func<float, Mapped> map,
                                      IList<(float From, float To)> pauses, List<Join> joins = null)
        {
            var at = new float[stamps.Count];
            int run = int.MinValue;
            float onset = float.NaN;
            Stamp snappedBy = null;
            for (int i = 0; i < stamps.Count; i++)
            {
                Stamp s = stamps[i];
                Mapped m = map(s.T);
                at[i] = m.At;

                if (s.Run != run) { run = s.Run; onset = float.NaN; snappedBy = null; }
                if (s.Run < 0 || !s.Tag || s.PauseRun) continue;

                if (m.Snapped) { onset = m.At; snappedBy = s; continue; }
                if (float.IsNaN(onset)) continue;
                if (m.At > onset && m.At - onset <= MaxPullBackSeconds && !PauseStartsBetween(pauses, onset, m.At))
                {
                    joins?.Add(new Join { Stamp = s, SnappedBy = snappedBy, From = m.At, To = onset });
                    at[i] = onset;
                }
            }
            return at;
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MugsTech.Tts
{
    /// <summary>
    /// <c>{Pause:N}</c> — "the gap in the voice at this point is at least N
    /// seconds". eleven_v3 has no deterministic pause control (SSML
    /// <c>&lt;break&gt;</c> isn't supported on v3; ellipses, dashes and
    /// <c>[pause]</c> are suggestions it honours per take), so the beat is made
    /// AFTER the render, out of the audio the pipeline already has: no credits,
    /// no change to the request, the chunk plan or the words. SegmentSequencer
    /// runs this per section while it stitches the take, which also makes a
    /// <c>{Pause}</c> a tag-only edit.
    ///
    /// For each tag:
    ///   1. FIND THE GAP. The audio decides where it is; the tag's T= only says
    ///      where to look. eleven_v3's synthesis alignment was measured 1–2 s off
    ///      mid-section while its span ends still agreed with the audio (so
    ///      SegmentSequencer's rescale never fires), so the tag's time is a
    ///      starting point and nothing more. A measured pause near it, else the
    ///      nearest short true silence — and NEVER a splice into sound: when
    ///      there is no silence near the tag, nothing is inserted and a severe
    ///      note names the spot.
    ///   2. WIDEN IT TO N, if it is shorter. "At least N", never "plus N" — a
    ///      take that already paused there is left alone.
    ///   3. PIN THE TAGS AROUND IT TO ITS EDGES. Tags written ABOVE the pause
    ///      (between the previous spoken word and the tag) fire as the gap opens,
    ///      right after the setup's last word; tags written BELOW it fire as the
    ///      gap closes, on the punchline's first word. The alignment parks
    ///      own-line tags on the NEXT word's onset and SegmentSequencer's snap
    ///      moves them there anyway, which is why a cut "above" a beat used to
    ///      land on the line after it. Pinned, the beat is on screen even when no
    ///      silence had to be added: whatever is above holds for max(natural, N)
    ///      before the line.
    ///
    /// A pause on a section's edge (nothing spoken before or after it in its
    /// section) isn't spliced here — it widens the gap between sections, which
    /// is SegmentSequencer's to lay down (see <see cref="SizeBoundary"/>).
    ///
    /// Pure C# — no Unity types, no IO — so the self-tests drive it with
    /// synthetic audio, like <see cref="AudioSlicer"/>.
    /// </summary>
    public static class PauseInserter
    {
        public const float MinSeconds = 0.1f;
        public const float MaxSeconds = 3.0f;

        /// <summary>How far from the tag's time a gap may be and still be its
        /// gap. Same radius as SegmentSequencer's snap (PauseSnapMaxSeconds).</summary>
        public const float GapSearchSeconds = 0.9f;

        /// <summary>A gap found further than this from the tag's time is still
        /// used, but reported as worth checking by ear — that's what a drifted
        /// alignment looks like.</summary>
        public const float ReachNoteSeconds = 0.3f;

        /// <summary>Where the "next nearest gap" in a reach note is looked for.</summary>
        public const float AlternativeSearchSeconds = 2.5f;

        /// <summary>Shortest true silence (below the speech threshold) accepted
        /// when no measured pause is near the tag.</summary>
        public const float MinSilenceSeconds = 0.06f;

        /// <summary>Linear fade either side of a splice. The splice sits in
        /// silence, so it's a guard against a click, not an audible fade.</summary>
        public const float FadeSeconds = 0.005f;

        // SegmentSequencer.AnalyzeSegmentAudio's pause rule — used only when that
        // analysis is off (correctTimingFromAudio = false), so the feature still
        // runs. Keep in lockstep with EnvelopeWindowSeconds / PauseThresholdDb /
        // MinRealPauseSeconds there.
        public const float EnvelopeWindowSeconds = 0.02f;
        public const float PauseThresholdDb      = -35f;
        public const float MinPauseSeconds       = 0.25f;

        const float SilenceWindowSeconds      = 0.01f;
        const float SpliceSearchWindowSeconds = 0.005f;   // AudioSlicer.Options.CutSearchWindowSeconds

        // ---- tags -----------------------------------------------------------

        // A tag or a stage direction — never spoken. Quoted card fields may hold
        // braces or span lines, so they're skipped as a unit (TagOnlyRetimer's
        // StampedTag). Whatever this doesn't cover, other than whitespace, is
        // narration.
        static readonly Regex Unspoken = new Regex(@"\{(?:[^{}""\n]|""[^""]*"")*\}|\[[^\]]*\]");

        // {Pause:0.8} / {Pause:0.8,T=12.345}. ".5" is accepted because the MugsTech
        // script checker accepts it. Kept in lockstep with TtsScriptProcessor's
        // KnownMarkers and elevenlabs_tts_processor.py.
        static readonly Regex PauseBody = new Regex(
            @"^\{Pause:(\d+(?:\.\d+)?|\.\d+)(?:,T=(\d+(?:\.\d+)?))?\}$");

        const string PausePrefix = "{Pause:";

        /// <summary>One <c>{Pause:…}</c> in a section's timed script.</summary>
        public class PauseTag
        {
            public int    Index;            // in the section script
            public int    Length;
            public string Text;             // as found, e.g. {Pause:0.8,T=66.802}
            public string Authored;         // {Pause:0.8} — what the author wrote
            public float  Seconds;          // clamped to MinSeconds..MaxSeconds; 0 = unreadable
            public bool   HasTime;
            public float  T;                // section-local, on the alignment's timeline
            public string Problem;          // a clamp or parse note, else null

            // The tags around it: everything between the last spoken character
            // before it and the first spoken character after it. Those are the
            // tags that sit in the same gap.
            public int    GroupStart;
            public int    GroupEnd;
            public bool   AtSectionStart;   // nothing spoken before it in its section
            public bool   AtSectionEnd;     // nothing spoken after it

            public bool   OnEdge => AtSectionStart || AtSectionEnd;
        }

        public static bool HasTags(string script)
            => !string.IsNullOrEmpty(script) && script.IndexOf(PausePrefix, StringComparison.Ordinal) >= 0;

        /// <summary>Every <c>{Pause:…}</c> in <paramref name="script"/>, in script order.</summary>
        public static List<PauseTag> FindTags(string script)
        {
            var tags = new List<PauseTag>();
            if (!HasTags(script)) return tags;

            var unspoken = new bool[script.Length];
            for (int i = 0; i < script.Length; i++) unspoken[i] = char.IsWhiteSpace(script[i]);

            var found = new List<Match>();
            foreach (Match m in Unspoken.Matches(script))
            {
                for (int i = m.Index; i < m.Index + m.Length; i++) unspoken[i] = true;
                if (m.Value.StartsWith(PausePrefix, StringComparison.Ordinal)) found.Add(m);
            }

            foreach (Match m in found)
            {
                var tag = new PauseTag { Index = m.Index, Length = m.Length, Text = m.Value };

                int before = m.Index - 1;
                while (before >= 0 && unspoken[before]) before--;
                int after = m.Index + m.Length;
                while (after < script.Length && unspoken[after]) after++;

                tag.GroupStart     = before + 1;
                tag.GroupEnd       = after;
                tag.AtSectionStart = before < 0;
                tag.AtSectionEnd   = after >= script.Length;

                Match body = PauseBody.Match(m.Value);
                if (!body.Success)
                {
                    tag.Authored = Short(m.Value);
                    tag.Problem  = "unreadable — write the seconds as a number, e.g. {Pause:0.8}";
                    tags.Add(tag);
                    continue;
                }

                tag.Authored = PausePrefix + body.Groups[1].Value + "}";
                float seconds = ParseFloat(body.Groups[1].Value);
                if (seconds < MinSeconds || seconds > MaxSeconds)
                {
                    float clamped = Math.Min(MaxSeconds, Math.Max(MinSeconds, seconds));
                    tag.Problem = $"{seconds.ToString("0.###", CultureInfo.InvariantCulture)}s is outside " +
                                  $"{MinSeconds:0.0}–{MaxSeconds:0.0}s — used {clamped:0.0#}s";
                    seconds = clamped;
                }
                tag.Seconds = seconds;

                if (body.Groups[2].Success)
                {
                    tag.HasTime = true;
                    tag.T       = ParseFloat(body.Groups[2].Value);
                }
                tags.Add(tag);
            }
            return tags;
        }

        /// <summary>
        /// Removes every <c>{Pause:…}</c> — a tag alone on its line takes its line
        /// break with it. Nothing downstream of the audio assembly may see one:
        /// the emotion parser can't mistake it, but a leftover tag is noise in
        /// every parser after it.
        /// </summary>
        public static string StripTags(string script) => StripTags(script, out _);

        public static string StripTags(string script, out int removed)
        {
            removed = 0;
            if (!HasTags(script)) return script;

            var sb = new StringBuilder(script.Length);
            int cursor = 0;
            foreach (Match m in Unspoken.Matches(script))
            {
                if (!m.Value.StartsWith(PausePrefix, StringComparison.Ordinal)) continue;
                removed++;

                int from = m.Index, to = m.Index + m.Length;

                // Alone on its line? Then the line goes, break included.
                int lineStart = from;
                while (lineStart > cursor && (script[lineStart - 1] == ' ' || script[lineStart - 1] == '\t')) lineStart--;
                bool startsLine = lineStart == 0 || script[lineStart - 1] == '\n';
                int lineEnd = to;
                while (lineEnd < script.Length && (script[lineEnd] == ' ' || script[lineEnd] == '\t' || script[lineEnd] == '\r')) lineEnd++;
                bool endsLine = lineEnd >= script.Length || script[lineEnd] == '\n';
                if (startsLine && endsLine && lineStart >= cursor)
                {
                    from = lineStart;
                    to   = lineEnd < script.Length ? lineEnd + 1 : lineEnd;
                }

                sb.Append(script, cursor, from - cursor);
                cursor = to;
            }
            sb.Append(script, cursor, script.Length - cursor);
            return sb.ToString();
        }

        // ---- measurement ----------------------------------------------------

        /// <summary>A quiet run on the section's measured timeline (seconds).</summary>
        public struct Gap
        {
            public float Start, End;
            public Gap(float start, float end) { Start = start; End = end; }
            public float Length => End - Start;
            public float DistanceTo(float t) => t < Start ? Start - t : (t > End ? t - End : 0f);
        }

        /// <summary>
        /// The pauses SegmentSequencer.AnalyzeSegmentAudio would measure —
        /// quiet runs below -35 dBFS lasting ≥ 0.25 s, inside the speech span —
        /// for when that analysis is switched off. <paramref name="startSeconds"/>
        /// is where <paramref name="samples"/> begins in the section file.
        /// </summary>
        public static List<Gap> MeasurePauses(float[] samples, int channels, int sampleRate, float startSeconds)
        {
            var gaps = new List<Gap>();
            if (!TryEnvelope(samples, channels, sampleRate, EnvelopeWindowSeconds,
                             out float[] db, out float perWindow, out int first, out int last))
                return gaps;

            int minWindows = Math.Max(1, (int)Math.Round(MinPauseSeconds / EnvelopeWindowSeconds));
            AddQuietRuns(db, first, last, PauseThresholdDb, minWindows, perWindow, startSeconds, gaps);
            return gaps;
        }

        /// <summary>
        /// Runs below the SPEECH threshold (noise floor + margin, the splitter's
        /// rule) lasting ≥ 60 ms, inside the speech span — the short silences
        /// between words that a -35 dBFS / 0.25 s pause doesn't count.
        /// </summary>
        public static List<Gap> MeasureSilences(float[] samples, int channels, int sampleRate, float startSeconds)
        {
            var gaps = new List<Gap>();
            if (!TryEnvelope(samples, channels, sampleRate, SilenceWindowSeconds,
                             out float[] db, out float perWindow, out int first, out int last))
                return gaps;

            float threshold = AudioSlicer.SpeechThresholdDb(db, new AudioSlicer.Options());
            int minWindows = Math.Max(1, (int)Math.Round(MinSilenceSeconds / SilenceWindowSeconds));
            AddQuietRuns(db, first, last, threshold, minWindows, perWindow, startSeconds, gaps);
            return gaps;
        }

        // Window envelope plus the first/last window above the speech threshold.
        static bool TryEnvelope(float[] samples, int channels, int sampleRate, float windowSeconds,
                                out float[] db, out float perWindow, out int first, out int last)
        {
            db = null; perWindow = 0f; first = last = -1;
            if (samples == null || channels <= 0 || sampleRate <= 0) return false;

            int frames       = samples.Length / channels;
            int windowFrames = Math.Max(1, (int)Math.Round(windowSeconds * sampleRate));
            db = AudioSlicer.WindowDb(samples, channels, frames, windowFrames);
            if (db.Length < 8) return false;

            perWindow = windowFrames / (float)sampleRate;
            float speech = AudioSlicer.SpeechThresholdDb(db, new AudioSlicer.Options());
            for (int w = 0; w < db.Length; w++)
            {
                if (db[w] < speech) continue;
                if (first < 0) first = w;
                last = w;
            }
            return first >= 0;
        }

        // Same loop as AnalyzeSegmentAudio's pause pass.
        static void AddQuietRuns(float[] db, int first, int last, float thresholdDb, int minWindows,
                                 float perWindow, float startSeconds, List<Gap> into)
        {
            int runStart = -1;
            for (int w = first; w <= last + 1; w++)
            {
                bool quiet = w <= last && db[w] < thresholdDb;
                if (quiet)
                {
                    if (runStart < 0) runStart = w;
                }
                else if (runStart >= 0)
                {
                    if (w - runStart >= minWindows)
                        into.Add(new Gap(startSeconds + runStart * perWindow, startSeconds + w * perWindow));
                    runStart = -1;
                }
            }
        }

        // ---- the splice -----------------------------------------------------

        /// <summary>One beat that was found — with or without silence added.</summary>
        public class Splice
        {
            public int   TagIndex, TagEnd;      // the {Pause} itself, in the section script
            public int   GroupStart, GroupEnd;  // the tags that share its gap
            public float GapStart, GapEnd;      // measured, section seconds
            public float At;                    // where the silence went (inside the gap)
            public float Requested;
            public float Inserted;              // seconds actually added (whole frames)
            public int   InsertFrames;
            public Entry Entry;
        }

        /// <summary>
        /// How every <c>T=</c> in the section moves once the silence is in.
        /// SegmentSequencer.ShiftTimestamps asks it for each one, so that is
        /// still the only place a <c>T=</c> is rewritten.
        /// </summary>
        public class Shift
        {
            readonly List<Splice> splices;
            public Shift(List<Splice> splices) { this.splices = splices ?? new List<Splice>(); }

            public IList<Splice> Splices => splices;

            public float Total
            {
                get { float t = 0f; foreach (var s in splices) t += s.Inserted; return t; }
            }

            /// <summary>
            /// Where the <c>T=</c> at <paramref name="index"/> in the section
            /// script lands on the section's measured timeline, silence included
            /// (SegmentSequencer then adds the section's place in the take).
            /// <paramref name="toReal"/> maps an alignment time onto the measured
            /// timeline the way every other tag is mapped today.
            /// </summary>
            public float Resolve(int index, float t, Func<float, float> toReal)
            {
                float before = 0f;
                foreach (var s in splices)          // script order == time order (Apply enforces it)
                {
                    if (index < s.GroupStart) break;
                    if (index < s.TagIndex) return s.GapStart + before;              // above the pause: as the gap opens
                    if (index < s.GroupEnd)
                        return index < s.TagEnd ? s.GapStart + before                // the pause's own T= (stripped later)
                                                : s.GapEnd + before + s.Inserted;   // below it: as the gap closes
                    before += s.Inserted;
                }
                return (toReal != null ? toReal(t) : t) + before;
            }

            /// <summary>Silence inserted by the beats that come before <paramref name="index"/> in the script.</summary>
            public float InsertedBefore(int index)
            {
                float before = 0f;
                foreach (var s in splices)
                {
                    if (index < s.TagEnd) break;
                    before += s.Inserted;
                }
                return before;
            }
        }

        /// <summary>One line of the report: a Player.log line and a result-JSON record.</summary>
        public class Entry
        {
            public string Section;
            public string Tag;              // {Pause:0.8}
            public int    TagIndex;
            public float  Requested;
            public float  At;               // section seconds: the gap's start, or the tag's time when no gap
            public float  Natural;          // the gap v3 left (0 = none found)
            public float  Inserted;
            public string Note;             // worth an ear: a reach, a clamp, a merge, a skip — or null
            public string Info;             // how a section-edge pause was laid out (plain information)
            public bool   Severe;           // nothing could be done safely — check the spot
            public bool   Boundary;         // on a section edge: widens the gap between sections
            public float  VideoAt = -1f;    // on the finished take, filled in by the caller

            public bool NeedsCheck => Severe || Note != null;

            public string Describe()
            {
                var sb = new StringBuilder();
                sb.Append(Tag).Append(' ');
                sb.Append(Boundary ? "at the section edge" : "at " + At.ToString("F2", CultureInfo.InvariantCulture) + "s");
                if (VideoAt >= 0f) sb.Append(" (video ").Append(Clock(VideoAt)).Append(')');
                sb.Append(": ");

                if (Severe)
                    sb.Append(Note ?? "skipped");
                else
                {
                    sb.Append(Boundary ? "gap " : "natural gap ")
                      .Append(Natural.ToString("F2", CultureInfo.InvariantCulture)).Append('s');
                    if (Inserted > 0f)
                        sb.Append(" → inserted ").Append(Inserted.ToString("F2", CultureInfo.InvariantCulture)).Append('s');
                    else
                        sb.Append(" ≥ ").Append(Requested.ToString("F2", CultureInfo.InvariantCulture))
                          .Append("s — nothing inserted");
                    if (Info != null) sb.Append(" — ").Append(Info);
                    if (Note != null) sb.Append(" (").Append(Note).Append(')');
                }
                return sb.ToString();
            }
        }

        public class Result
        {
            public float[]     Samples;                     // the section's audio with the silence in
            public Shift       Shift = new Shift(null);
            public List<Entry> Entries = new List<Entry>(); // mid-section tags, in script order
            public float       TotalInserted => Shift.Total;
        }

        /// <summary>
        /// Finds, widens and pins every mid-section <c>{Pause}</c> in
        /// <paramref name="tags"/> (edge tags are left to the caller).
        /// </summary>
        /// <param name="samples">The section's audio as it goes into the take,
        /// interleaved. It starts <paramref name="startSeconds"/> into the
        /// section file; every time in and out of this method is on the section
        /// file's measured timeline.</param>
        /// <param name="toReal">Alignment <c>T=</c> → measured section time,
        /// WITHOUT SegmentSequencer's snap to a pause's end — the gap is wanted,
        /// not the word after it. Null = identity.</param>
        /// <param name="pauses">The pauses SegmentSequencer measured, or null to
        /// measure them here.</param>
        public static Result Apply(string section, IList<PauseTag> tags,
                                   float[] samples, int channels, int sampleRate, float startSeconds,
                                   Func<float, float> toReal, IList<Gap> pauses)
        {
            var result = new Result { Samples = samples };
            if (tags == null || tags.Count == 0 || samples == null) return result;

            channels   = Math.Max(1, channels);
            sampleRate = Math.Max(1, sampleRate);
            int   frames     = samples.Length / channels;
            float endSeconds = startSeconds + frames / (float)sampleRate;
            toReal = toReal ?? (t => t);

            var measured = new List<Gap>();
            if (pauses != null)
            {
                foreach (var p in pauses)
                    if (p.End > startSeconds && p.Start < endSeconds)
                        measured.Add(new Gap(Math.Max(p.Start, startSeconds), Math.Min(p.End, endSeconds)));
            }
            else measured = MeasurePauses(samples, channels, sampleRate, startSeconds);

            List<Gap> silences = null;      // measured only if some tag needs them
            var accepted = new List<Splice>();
            Splice previous = null;

            foreach (var tag in tags)
            {
                if (tag.OnEdge) continue;   // SegmentSequencer widens the gap between sections

                var entry = new Entry {
                    Section = section, Tag = tag.Authored, TagIndex = tag.Index,
                    Requested = tag.Seconds, Note = tag.Problem,
                };
                result.Entries.Add(entry);

                if (tag.Seconds <= 0f) { entry.Severe = true; continue; }
                if (!tag.HasTime)
                {
                    entry.Severe = true;
                    entry.Note   = "has no T= — re-time the script with Update Tags Only (or regenerate)";
                    continue;
                }

                float u = toReal(tag.T);
                entry.At = u;

                // Two pauses in one gap are one beat; the longer one wins.
                if (previous != null && previous.GroupStart == tag.GroupStart)
                {
                    previous.Requested    = Math.Max(previous.Requested, tag.Seconds);
                    previous.Entry.Requested = previous.Requested;
                    entry.At      = previous.GapStart;
                    entry.Natural = previous.GapEnd - previous.GapStart;
                    entry.Note    = Join(entry.Note, "a second {Pause} in the same gap — merged, the longer one wins");
                    continue;
                }

                if (!TryFindGap(u, measured, ref silences, samples, channels, sampleRate, startSeconds,
                                out Gap gap, out string where))
                {
                    entry.Severe = true;
                    entry.Note   = Join(entry.Note,
                        $"no silence within ±{GapSearchSeconds:0.0}s of {u:F2}s — v3 ran these words " +
                        "together on this take, so nothing was inserted (a splice here would cut " +
                        "speech). Move the tag or regenerate");
                    continue;
                }

                // Each beat owns a later gap than the one before it. Anything else
                // means the alignment has drifted far enough to point two tags at
                // one gap — splicing twice there would guess, so skip and say so.
                if (previous != null && gap.Start < previous.GapEnd + 1e-4f)
                {
                    entry.Severe = true;
                    entry.Note   = Join(entry.Note,
                        $"resolves to the same gap as the {{Pause}} before it " +
                        $"({previous.GapStart:F2}–{previous.GapEnd:F2}s) — skipped; check this spot");
                    continue;
                }

                var splice = new Splice {
                    TagIndex   = tag.Index,      TagEnd   = tag.Index + tag.Length,
                    GroupStart = tag.GroupStart, GroupEnd = tag.GroupEnd,
                    GapStart   = gap.Start,      GapEnd   = gap.End,
                    Requested  = tag.Seconds,    Entry    = entry,
                };
                entry.At      = gap.Start;
                entry.Natural = gap.Length;
                entry.Note    = Join(entry.Note, where);
                accepted.Add(splice);
                previous = splice;
            }

            // Sizes and splice points, now that merges are settled.
            foreach (var s in accepted)
            {
                float want = Math.Max(0f, s.Requested - (s.GapEnd - s.GapStart));
                s.InsertFrames = (int)Math.Round(want * sampleRate);
                s.Inserted     = s.InsertFrames / (float)sampleRate;
                s.At = s.InsertFrames > 0
                    ? startSeconds + AudioSlicer.QuietestPoint(samples, channels, sampleRate, frames,
                                                               s.GapStart - startSeconds, s.GapEnd - startSeconds,
                                                               SpliceSearchWindowSeconds)
                    : s.GapStart;
                s.Entry.Inserted = s.Inserted;
            }

            result.Samples = SpliceIn(samples, channels, sampleRate, startSeconds, accepted);
            result.Shift   = new Shift(accepted);
            return result;
        }

        // The gap for a tag whose time maps to u: a measured pause holding u, else
        // the nearest one within ±GapSearchSeconds, else the nearest true silence.
        static bool TryFindGap(float u, List<Gap> measured, ref List<Gap> silences,
                               float[] samples, int channels, int sampleRate, float startSeconds,
                               out Gap gap, out string note)
        {
            note = null;
            if (Nearest(measured, u, GapSearchSeconds, out gap, out float distance))
            {
                if (distance > ReachNoteSeconds)
                {
                    string side = gap.Start > u ? "after" : "before";
                    note = $"nearest gap is {distance:F2}s {side} the tag's time — the alignment drifts, " +
                           "check this beat by ear";
                    if (NextNearest(measured, u, gap, out Gap other))
                        note += $"; next nearest {other.Start:F2}–{other.End:F2}s";
                }
                return true;
            }

            if (silences == null) silences = MeasureSilences(samples, channels, sampleRate, startSeconds);
            if (Nearest(silences, u, GapSearchSeconds, out gap, out distance))
            {
                note = $"no pause near the tag — used a {gap.Length:F2}s silence " +
                       (distance > 0f ? $"{distance:F2}s {(gap.Start > u ? "after" : "before")} its time" : "at its time") +
                       "; check this beat by ear";
                return true;
            }
            return false;
        }

        static bool Nearest(List<Gap> gaps, float u, float radius, out Gap best, out float bestDistance)
        {
            best = default(Gap);
            bestDistance = float.MaxValue;
            bool found = false;
            foreach (var g in gaps)
            {
                float d = g.DistanceTo(u);
                if (d <= radius && d < bestDistance) { best = g; bestDistance = d; found = true; }
            }
            return found;
        }

        static bool NextNearest(List<Gap> gaps, float u, Gap chosen, out Gap other)
        {
            other = default(Gap);
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var g in gaps)
            {
                if (Math.Abs(g.Start - chosen.Start) < 1e-4f) continue;
                float d = g.DistanceTo(u);
                if (d <= AlternativeSearchSeconds && d < bestDistance) { other = g; bestDistance = d; found = true; }
            }
            return found;
        }

        // Copies the section with each splice's zeros laid in at its point, and a
        // short linear fade out before / in after every splice.
        static float[] SpliceIn(float[] samples, int channels, int sampleRate, float startSeconds, List<Splice> splices)
        {
            long added = 0;
            foreach (var s in splices) added += s.InsertFrames;
            if (added == 0) return samples;

            int frames = samples.Length / channels;
            var outBuf = new float[(frames + added) * channels];
            int fade   = Math.Max(1, (int)Math.Round(FadeSeconds * sampleRate));

            int  src = 0;
            long dst = 0;
            bool fadeInNext = false;

            foreach (var s in splices)
            {
                if (s.InsertFrames <= 0) continue;
                int at = Clamp((int)Math.Round((s.At - startSeconds) * sampleRate), src, frames);
                CopyChunk(samples, outBuf, channels, src, at, dst, fadeInNext ? fade : 0, fade);
                dst += (at - src) + s.InsertFrames;     // the inserted frames are already zero
                src  = at;
                fadeInNext = true;
            }
            CopyChunk(samples, outBuf, channels, src, frames, dst, fadeInNext ? fade : 0, 0);
            return outBuf;
        }

        static void CopyChunk(float[] from, float[] to, int channels, int start, int end, long dst,
                              int fadeIn, int fadeOut)
        {
            int count = end - start;
            if (count <= 0) return;
            Array.Copy(from, (long)start * channels, to, dst * channels, (long)count * channels);

            int fin = Math.Min(fadeIn, count);
            for (int f = 0; f < fin; f++)
            {
                float gain = (f + 1) / (float)(fin + 1);
                for (int c = 0; c < channels; c++) to[(dst + f) * channels + c] *= gain;
            }

            int fout = Math.Min(fadeOut, count);
            for (int f = 0; f < fout; f++)
            {
                float gain = (f + 1) / (float)(fout + 1);
                for (int c = 0; c < channels; c++) to[(dst + count - 1 - f) * channels + c] *= gain;
            }
        }

        // ---- section edges --------------------------------------------------

        /// <summary>How the silence between two sections is laid out.</summary>
        public struct Boundary
        {
            public float PauseBefore;   // pure silence laid down in front of the next section
            public float LeadIn;        // around an opening transition: silence before it starts…
            public float Tail;          // …and after it reveals, before the first word
            public float HeardGap;      // last word to first word, as the listener hears it
        }

        /// <summary>
        /// Sizes the silence in front of a section. With no <c>{Pause}</c> on the
        /// edge (both holds 0) this is exactly what SegmentSequencer did before:
        /// <c>interSegmentPause</c>, widened to lead-in + transition + tail when
        /// the section opens with one.
        /// </summary>
        /// <param name="transitionSeconds">The opening transition's cover+reveal,
        /// or negative when the section doesn't open with one.</param>
        /// <param name="trailPad">Audio kept after the previous section's last word.</param>
        /// <param name="preRoll">Audio kept before this section's first word.</param>
        /// <param name="holdBefore">Longest <c>{Pause}</c> after the previous
        /// section's last word, or above this section's opening transition.</param>
        /// <param name="holdAfter">Longest <c>{Pause}</c> between the opening
        /// transition and the first word.</param>
        public static Boundary SizeBoundary(float interSegmentPause, float transitionSeconds,
                                            float leadIn, float tail, float trailPad, float preRoll,
                                            float holdBefore, float holdAfter)
        {
            var b = new Boundary();
            float basePause = Math.Max(0f, interSegmentPause);
            trailPad = Math.Max(0f, trailPad);
            preRoll  = Math.Max(0f, preRoll);

            if (transitionSeconds < 0f)
            {
                // One gap: "at least N" from the last word to the first.
                float hold = Math.Max(holdBefore, holdAfter);
                b.PauseBefore = Math.Max(basePause, hold - trailPad - preRoll);
            }
            else
            {
                // Above the transition = before it plays; below = after it reveals,
                // so the held beat lands on the new scene. Either way the
                // transition stays inside the silence that's laid down.
                b.LeadIn = Math.Max(Math.Max(0f, leadIn), holdBefore - trailPad);
                b.Tail   = Math.Max(Math.Max(0f, tail),   holdAfter  - preRoll);
                b.PauseBefore = Math.Max(basePause, b.LeadIn + transitionSeconds + b.Tail);
            }
            b.HeardGap = trailPad + b.PauseBefore + preRoll;
            return b;
        }

        // ---- helpers --------------------------------------------------------

        public static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00.0", CultureInfo.InvariantCulture);
        }

        static string Join(string a, string b)
            => string.IsNullOrEmpty(a) ? b : (string.IsNullOrEmpty(b) ? a : a + "; " + b);

        static string Short(string tag)
        {
            string flat = Regex.Replace(tag ?? "", @"\s+", " ");
            return flat.Length <= 32 ? flat : flat.Substring(0, 31) + "…";
        }

        static float ParseFloat(string s)
            => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}

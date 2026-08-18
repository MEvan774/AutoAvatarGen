using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Deterministic checks on the pure parts of the chunked TTS pipeline —
/// chunk assembly and its character offsets, the overlap's sentence
/// extraction, chunk-overflow rebalancing, and the splitter driven by a
/// SYNTHETIC alignment over synthetic audio, where the correct cut sample is
/// known in advance.
///
/// The project has no test-framework package (and no assembly definitions), so
/// these run from the menu rather than through NUnit: <b>MugsTech ▸ TTS ▸ Run
/// Pipeline Self-Tests</b>. Everything here is offline — no API key, no
/// credits, no ffmpeg.
/// </summary>
public static class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/TTS/Run Pipeline Self-Tests")]
    public static void RunAll()
    {
        var log = new Log();

        ChunkAssembly_RecordsExactSpans(log);
        ChunkAssembly_StageDirectionFlag(log);
        ChunkAssembly_OverlapPrefixesLaterChunks(log);
        Overflow_RebalancesAtSectionBoundaries(log);
        Sentences_TakeTheLastOnes(log);
        Splitter_CutsInTheGapAndTrimsToPad(log);
        Splitter_DiscardsTheOverlap(log);
        Alignment_FromWordsInterpolates(log);
        Wav_RoundTrips(log);

        log.Report();
    }

    // -----------------------------------------------------------------------
    // Chunk assembly
    // -----------------------------------------------------------------------

    static void ChunkAssembly_RecordsExactSpans(Log log)
    {
        var segments = Segments(
            ("COLD OPEN", "{Position:Center,Cut} {Neutral}\n[deadpan] One two three."),
            ("SETUP",     "{Excited}\nFour five six."),
            ("BREAKDOWN", "{Serious} Seven eight nine."),
            ("TAKE",      "Ten eleven twelve."),
            ("CLOSING",   "Thirteen fourteen fifteen."));

        var plan = Assemble(segments);

        log.AreEqual(2, plan.Chunks.Count, "two chunks for a five-section script");
        log.AreEqual(3, CountSections(plan.Chunks[0]), "chunk A holds everything up to BREAKDOWN");
        log.AreEqual(2, CountSections(plan.Chunks[1]), "chunk B holds the rest");

        // The offsets are the whole contract with the splitter: every span must
        // address exactly its own section inside the chunk text.
        foreach (var chunk in plan.Chunks)
            foreach (var span in chunk.Spans)
            {
                string slice = chunk.Text.Substring(span.Start, span.Length);
                log.IsTrue(!slice.Contains("{"),
                    $"{chunk.Id}/{Name(span)} span holds no unstripped tags (got \"{Head(slice)}\")");

                if (span.IsOverlap) continue;
                log.IsTrue(slice.EndsWith(".") || slice.EndsWith("!") || slice.EndsWith("?"),
                    $"{chunk.Id}/{Name(span)} span ends where the section ends (got \"{Tail(slice)}\")");
            }

        // A marker's CleanIndex must point INTO its own span, in chunk space.
        foreach (var chunk in plan.Chunks)
            foreach (var span in chunk.Spans)
                foreach (var m in span.Markers)
                    log.IsTrue(m.CleanIndex >= span.Start && m.CleanIndex <= span.End,
                        $"{chunk.Id}/{Name(span)} marker {m.Text} sits inside its span " +
                        $"({m.CleanIndex} in [{span.Start},{span.End}])");

        // The second section of chunk A must NOT start at zero — that would mean
        // offsets were recorded in section space and the splitter would cut the
        // same place twice.
        var second = plan.Chunks[0].Spans[1];
        log.IsTrue(second.Start > 0, "the second section's offset is chunk-relative, not section-relative");
    }

    static void ChunkAssembly_StageDirectionFlag(Log log)
    {
        var segments = Segments(("COLD OPEN", "{Neutral}\n[deadpan] One two three."));

        var kept = TtsChunkAssembler.Assemble(segments,
            new TtsChunkAssembler.Options { KeepStageDirections = true });
        log.IsTrue(kept.Chunks[0].Text.Contains("[deadpan]"),
            "stage directions reach the API when the flag is on");

        var stripped = TtsChunkAssembler.Assemble(Segments(("COLD OPEN", "{Neutral}\n[deadpan] One two three.")),
            new TtsChunkAssembler.Options { KeepStageDirections = false });
        log.IsTrue(!stripped.Chunks[0].Text.Contains("[deadpan]"),
            "and are stripped when it is off");

        // Either way the marker survives, so _timed.txt keeps carrying it.
        log.AreEqual(2, kept.Chunks[0].Spans[0].Markers.Count,
            "both markers are recorded when the direction is kept");
        log.AreEqual(2, stripped.Chunks[0].Spans[0].Markers.Count,
            "and when it is stripped");
    }

    static void ChunkAssembly_OverlapPrefixesLaterChunks(Log log)
    {
        var plan = Assemble(Segments(
            ("COLD OPEN", "One two three. Four five six."),
            ("BREAKDOWN", "Seven eight nine. Ten eleven twelve."),
            ("TAKE",      "Thirteen fourteen. Fifteen sixteen.")));

        var b = plan.Chunks[1];
        log.IsTrue(b.Spans.Count > 0 && b.Spans[0].IsOverlap, "chunk B opens with an overlap span");
        log.AreEqual(0, b.Spans[0].Start, "the overlap sits at the very start of the chunk text");

        string overlap = b.Text.Substring(0, b.Spans[0].Length);
        log.IsTrue(plan.Chunks[0].Text.EndsWith(overlap),
            $"the overlap is the tail of chunk A (got \"{Head(overlap)}\")");
        log.IsTrue(b.Spans[1].Start >= b.Spans[0].End,
            "the first real section starts after the overlap");
    }

    static void Overflow_RebalancesAtSectionBoundaries(Log log)
    {
        // One long section forces an uneven split — the balanced partition has
        // to put it alone rather than insist on equal section counts.
        var sizes = TtsChunkAssembler.BalancedSizes(new[] { 100, 100, 100, 700 }, 2);
        log.AreEqual("3,1", string.Join(",", sizes),
            "the outsized section is isolated");

        var even = TtsChunkAssembler.BalancedSizes(new[] { 100, 100, 100, 100 }, 2);
        log.AreEqual("2,2", string.Join(",", even), "equal sections split down the middle");

        var three = TtsChunkAssembler.BalancedSizes(new[] { 300, 300, 300, 300, 300, 300 }, 3);
        log.AreEqual("2,2,2", string.Join(",", three), "three-way split stays balanced");

        // A script too long for two chunks must produce three, at section edges.
        var segments = Segments(
            ("A", new string('a', 900) + "."), ("B", new string('b', 900) + "."),
            ("C", new string('c', 900) + "."), ("D", new string('d', 900) + "."),
            ("E", new string('e', 900) + "."));
        var plan = TtsChunkAssembler.Assemble(segments,
            new TtsChunkAssembler.Options { MaxChunkChars = 2000, OverlapBudgetChars = 320 });

        log.IsTrue(plan.Chunks.Count >= 3,
            $"a 4,500-character script overflows into 3+ chunks (got {plan.Chunks.Count})");
        foreach (var c in plan.Chunks)
            log.IsTrue(CountSections(c) >= 1, $"{c.Id} holds at least one whole section");
    }

    static void Sentences_TakeTheLastOnes(Log log)
    {
        log.AreEqual("Two. Three.",
            TtsChunkAssembler.LastSentences("One. Two. Three.", 2, 0),
            "the last two sentences");

        log.AreEqual("One. Two. Three.",
            TtsChunkAssembler.LastSentences("One. Two. Three.", 2, 40),
            "extended earlier when the minimum length isn't met");

        log.AreEqual("Only one.",
            TtsChunkAssembler.LastSentences("Only one.", 2, 0),
            "a single sentence is returned whole");

        log.IsTrue(TtsChunkAssembler.LastSentences("No terminator here", 2, 0)
                       .EndsWith("here"),
            "text without a terminator still yields something");
    }

    // -----------------------------------------------------------------------
    // The splitter, against a known-answer alignment
    // -----------------------------------------------------------------------

    static void Splitter_CutsInTheGapAndTrimsToPad(Log log)
    {
        const int   rate    = 44100;
        const float perChar = 0.1f;     // section chars
        const float perNl   = 0.5f;     // the blank line between sections

        string a = "one two three.";     // 14 chars -> 0.0 .. 1.4s
        string b = "four five six.";     // 14 chars -> 2.4 .. 3.8s
        string text = a + "\n\n" + b;

        var alignment = SyntheticAlignment(text, perChar, perNl);
        var audio     = SyntheticAudio(text, alignment, rate);

        var spans = new List<TtsChunkAssembler.Span> {
            new TtsChunkAssembler.Span { Slug = "01_A", Start = 0,             Length = a.Length },
            new TtsChunkAssembler.Span { Slug = "02_B", Start = a.Length + 2,  Length = b.Length },
        };

        var options = new AudioSlicer.Options();
        var split   = AudioSlicer.Split(audio, alignment, spans, options);

        log.AreEqual(2, split.Cuts.Count, "one slice per section");
        log.AreEqual(1, split.Boundaries.Count, "one boundary between two sections");

        // The pause runs 1.4s -> 2.4s; the cut belongs in the middle of it.
        log.Near(1.9f, split.Boundaries[0], 0.06f, "the cut lands mid-pause");

        // Section A: speech 0 -> 1.4, so the file ends one pad later.
        log.Near(0f,     split.Cuts[0].StartSeconds, 0.03f, "section A starts at the take's start");
        log.Near(1.475f, split.Cuts[0].EndSeconds,   0.05f, "section A ends a 75ms pad after its last word");

        // Section B: speech 2.4 -> 3.8, so the file starts one pad earlier.
        log.Near(2.325f, split.Cuts[1].StartSeconds, 0.05f, "section B starts a 75ms pad before its first word");

        // Edge silence must stay inside the pad, or Unity's transition pauses
        // stop being the thing that sets the gap between sections.
        foreach (var cut in split.Cuts)
        {
            log.IsTrue(cut.SpeechStartSeconds <= options.EdgePadSeconds + 0.03f,
                $"{cut.Span.Slug} carries at most a pad of leading silence " +
                $"(got {cut.SpeechStartSeconds:F3}s)");
            log.IsTrue(cut.DurationSeconds - cut.SpeechEndSeconds <= options.EdgePadSeconds + 0.03f,
                $"{cut.Span.Slug} carries at most a pad of trailing silence " +
                $"(got {cut.DurationSeconds - cut.SpeechEndSeconds:F3}s)");
        }

        // Fades: a cut through a live waveform without one is an audible click.
        foreach (var cut in split.Cuts)
        {
            log.IsTrue(Mathf.Abs(cut.Samples[0]) < 0.02f,
                $"{cut.Span.Slug} fades in (first sample {cut.Samples[0]:F4})");
            log.IsTrue(Mathf.Abs(cut.Samples[cut.Samples.Length - 1]) < 0.02f,
                $"{cut.Span.Slug} fades out (last sample {cut.Samples[cut.Samples.Length - 1]:F4})");
        }

        // …and the fade itself, on a signal that would show a click plainly:
        // full-scale DC, so any un-faded edge is unmistakable.
        var flat = new float[4410];
        for (int i = 0; i < flat.Length; i++) flat[i] = 1f;
        float[] faded = AudioSlicer.Extract(flat, 1, 0, flat.Length, 441);

        log.IsTrue(faded[0] < 0.01f, $"the cut edge starts from silence (got {faded[0]:F4})");
        log.IsTrue(faded[faded.Length - 1] < 0.01f,
            $"and returns to it (got {faded[faded.Length - 1]:F4})");
        log.Near(1f, faded[2205], 0.001f, "the middle of the slice is untouched");
        log.Near(1f, faded[441],  0.001f, "the fade is no longer than asked for");

        // Marker mapping: chunk-global alignment time minus the slice's start.
        // 'five' begins at chunk index 16+5, i.e. 2.4 + 0.5 = 2.9s global.
        int markerIndex = a.Length + 2 + 5;
        float local = split.Cuts[1].ToLocal(alignment.StartAt(markerIndex));
        log.Near(2.9f - split.Cuts[1].StartSeconds, local, 0.001f,
            "a marker's T= is its chunk time minus its section's cut point");
        log.Near(0.575f, local, 0.05f, "…which lands 0.575s into section B");
    }

    static void Splitter_DiscardsTheOverlap(Log log)
    {
        const int rate = 44100;
        string overlap = "read into this.";
        string section = "the real section.";
        string text    = overlap + "\n\n" + section;

        var alignment = SyntheticAlignment(text, 0.1f, 0.5f);
        var audio     = SyntheticAudio(text, alignment, rate);

        var spans = new List<TtsChunkAssembler.Span> {
            new TtsChunkAssembler.Span { Name = "(overlap)", Start = 0, Length = overlap.Length, IsOverlap = true },
            new TtsChunkAssembler.Span { Slug = "04_TAKE", Start = overlap.Length + 2, Length = section.Length },
        };

        var split = AudioSlicer.Split(audio, alignment, spans, new AudioSlicer.Options());

        log.AreEqual(1, split.Cuts.Count, "the overlap produces no output file");
        log.AreEqual("04_TAKE", split.Cuts[0].Span.Slug, "only the real section survives");

        // Nothing of the overlap's audio may remain: the section's file must
        // start after the overlap stopped being spoken.
        float overlapEnds = alignment.EndOfLastVisible(0, overlap.Length);
        log.IsTrue(split.Cuts[0].StartSeconds > overlapEnds,
            $"the slice starts after the overlap ends ({split.Cuts[0].StartSeconds:F2}s > {overlapEnds:F2}s)");
    }

    // -----------------------------------------------------------------------
    // Alignment helpers
    // -----------------------------------------------------------------------

    static void Alignment_FromWordsInterpolates(Log log)
    {
        string text = "one two three";
        var words = new List<TtsScriptProcessor.WordTimestamp> {
            new TtsScriptProcessor.WordTimestamp { Word = "one",   Start = 0.0f, End = 1.0f },
            new TtsScriptProcessor.WordTimestamp { Word = "two",   Start = 2.0f, End = 3.0f },
            new TtsScriptProcessor.WordTimestamp { Word = "three", Start = 4.0f, End = 5.0f },
        };

        var a = TtsAlignment.FromWords(text, words);
        log.IsTrue(a != null, "a character alignment is rebuilt from word times");
        if (a == null) return;

        log.AreEqual(text.Length, a.Length, "one entry per character");
        log.Near(0f, a.StartAt(0), 0.001f, "the first character starts when the first word does");
        log.Near(2f, a.StartAt(4), 0.001f, "'two' starts at its measured time");
        log.Near(4f, a.StartAt(8), 0.001f, "'three' starts at its measured time");
        log.IsTrue(a.StartAt(3) >= 1f && a.StartAt(3) <= 2f,
            "the space between words is interpolated across the gap");

        // Words that don't belong to the text must not produce an alignment.
        log.IsTrue(TtsAlignment.FromWords("completely different",
                new List<TtsScriptProcessor.WordTimestamp> {
                    new TtsScriptProcessor.WordTimestamp { Word = "zzz", Start = 0f, End = 1f },
                }) == null,
            "mismatched words are rejected rather than mapped onto the wrong text");
    }

    static void Wav_RoundTrips(Log log)
    {
        var samples = new float[4410];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 220f * i / 44100f);

        string path = Path.Combine(Application.temporaryCachePath, "mugs_selftest.wav");
        WavCodec.Write(path, samples, 44100, 1);
        var read = WavCodec.Read(path);

        log.AreEqual(44100, read.SampleRate, "sample rate survives the round trip");
        log.AreEqual(1,     read.Channels,   "channel count survives the round trip");
        log.AreEqual(samples.Length, read.Samples.Length, "sample count survives the round trip");

        float worst = 0f;
        for (int i = 0; i < samples.Length; i++)
            worst = Mathf.Max(worst, Mathf.Abs(samples[i] - read.Samples[i]));
        log.IsTrue(worst < 0.001f, $"samples survive 16-bit quantisation (worst delta {worst:F5})");

        try { File.Delete(path); } catch { }
    }

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    static List<TtsScriptProcessor.Segment> Segments(params (string name, string body)[] sections)
    {
        var sb = new StringBuilder();
        foreach (var (name, body) in sections)
            sb.Append("## ").Append(name).Append('\n').Append(body).Append("\n\n");

        var segments = TtsScriptProcessor.SplitIntoSegments(sb.ToString());
        TtsGenerationJob.AssignOrderAndSlugs(segments);
        return segments;
    }

    static TtsChunkAssembler.Plan Assemble(List<TtsScriptProcessor.Segment> segments)
        => TtsChunkAssembler.Assemble(segments, new TtsChunkAssembler.Options());

    /// <summary>
    /// Every character gets a fixed duration, except newlines which get a long
    /// one — so the blank line between sections becomes a known pause, exactly
    /// like the pocket v3 renders there.
    /// </summary>
    static TtsAlignment SyntheticAlignment(string text, float perChar, float perNewline)
    {
        var a = new TtsAlignment {
            characters                    = new string[text.Length],
            character_start_times_seconds = new float[text.Length],
            character_end_times_seconds   = new float[text.Length],
        };

        float t = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            float d = text[i] == '\n' ? perNewline : perChar;
            a.characters[i]                    = text[i].ToString();
            a.character_start_times_seconds[i] = t;
            a.character_end_times_seconds[i]   = t + d;
            t += d;
        }
        return a;
    }

    /// <summary>Audio matching that alignment: a tone wherever a character is
    /// spoken, digital silence across the newlines.</summary>
    static WavCodec.AudioBuffer SyntheticAudio(string text, TtsAlignment alignment, int rate)
    {
        int frames = Mathf.CeilToInt(alignment.TotalSeconds * rate) + 1;
        var samples = new float[frames];

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') continue;
            int from = Mathf.Clamp(Mathf.RoundToInt(alignment.StartAt(i) * rate), 0, frames);
            int to   = Mathf.Clamp(Mathf.RoundToInt(alignment.EndAt(i)   * rate), from, frames);
            for (int f = from; f < to; f++)
                samples[f] = 0.5f * Mathf.Sin(2f * Mathf.PI * 200f * f / rate);
        }

        return new WavCodec.AudioBuffer { Samples = samples, Channels = 1, SampleRate = rate };
    }

    static int CountSections(TtsChunkAssembler.Chunk chunk)
    {
        int n = 0;
        foreach (var s in chunk.Spans) if (!s.IsOverlap) n++;
        return n;
    }

    static string Name(TtsChunkAssembler.Span s) => s.IsOverlap ? "(overlap)" : s.Slug;
    static string Head(string s) => s == null ? "" : (s.Length <= 40 ? s : s.Substring(0, 40) + "…");
    static string Tail(string s) => s == null ? "" : (s.Length <= 40 ? s : "…" + s.Substring(s.Length - 40));

    // -----------------------------------------------------------------------

    class Log
    {
        readonly List<string> failures = new List<string>();
        int checks;

        public void IsTrue(bool condition, string what)
        {
            checks++;
            if (!condition) failures.Add(what);
        }

        public void AreEqual(object expected, object actual, string what)
        {
            checks++;
            if (!Equals(expected, actual))
                failures.Add($"{what} — expected {expected}, got {actual}");
        }

        public void Near(float expected, float actual, float tolerance, string what)
        {
            checks++;
            if (Mathf.Abs(expected - actual) > tolerance)
                failures.Add($"{what} — expected {expected:F3} ±{tolerance:F3}, got {actual:F3}");
        }

        public void Report()
        {
            if (failures.Count == 0)
            {
                Debug.Log($"[TtsPipelineSelfTests] {checks} checks passed.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"[TtsPipelineSelfTests] {failures.Count} of {checks} checks FAILED:\n");
            foreach (string f in failures) sb.Append("  • ").Append(f).Append('\n');
            Debug.LogError(sb.ToString());
        }
    }
}

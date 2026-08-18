using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MugsTech.Tts
{
    /// <summary>
    /// Groups the script's <c>## SECTION</c>s into a small number of LONG TTS
    /// requests and records, to the character, where every section sits inside
    /// the text that gets sent.
    ///
    /// WHY: eleven_v3 is non-deterministic between generations, so rendering
    /// each section as its own request gives each one a slightly different
    /// vocal character — an audible "different person" jolt at every seam.
    /// Request stitching (previous_request_ids) isn't available for v3, so the
    /// fix is to generate the voice as a couple of continuous takes and cut the
    /// audio back into per-section files afterwards. One take = one consistent
    /// voice, and everything downstream still receives the per-section files it
    /// has always consumed.
    ///
    /// The offsets recorded here are what the splitter cuts on — nothing
    /// downstream ever re-finds a section by searching the text, because the
    /// section text and the chunk text stop being character-identical the
    /// moment sections are joined.
    ///
    /// Pure functions, no Unity dependencies (see TtsSplitterSelfTests).
    /// </summary>
    public static class TtsChunkAssembler
    {
        // Sections inside a chunk are joined by a blank line. The paragraph
        // break is load-bearing: it makes v3 leave a natural pause pocket at
        // each section boundary, and that silence is where the splitter cuts.
        public const string SectionJoin = "\n\n";

        public class Options
        {
            /// <summary>
            /// Chunk A ends after the section whose name/slug matches this.
            /// Matched case-insensitively against both the heading text
            /// ("BREAKDOWN") and the order-prefixed slug ("03_BREAKDOWN").
            /// When no section matches, the split falls back to the section
            /// boundary closest to the middle by character count.
            /// </summary>
            public string BoundarySection = "BREAKDOWN";

            /// <summary>
            /// Hard ceiling on one request's text. v3's documented limit is
            /// 5,000 characters; staying near 3,000 keeps a take inside the
            /// range where the voice holds together. Overflow re-splits into
            /// more chunks at section boundaries.
            /// </summary>
            public int MaxChunkChars = 3000;

            /// <summary>
            /// How many trailing sentences of the previous chunk to prepend to
            /// this one so the model "reads into" it from the same context.
            /// The overlap audio is cut off and discarded after generation.
            /// </summary>
            public int OverlapSentences = 2;

            /// <summary>Extend the overlap to earlier sentences until it is at least this long.</summary>
            public int MinOverlapChars = 120;

            /// <summary>
            /// Leave <c>[bracketed]</c> cues in the text sent to the API (v3
            /// audio tags). See TtsScriptProcessor.ExtractMarkers.
            /// </summary>
            public bool KeepStageDirections = true;

            /// <summary>Reserved headroom for the overlap prefix when planning chunk sizes.</summary>
            public int OverlapBudgetChars = 320;
        }

        /// <summary>
        /// One section's (or the overlap prefix's) exact character range inside
        /// <see cref="Chunk.Text"/>.
        /// </summary>
        public class Span
        {
            public int    Order;        // 1-based section order; 0 for the overlap
            public string Slug;         // "01_COLD_OPEN"; null for the overlap
            public string Name;         // heading text; null for the overlap
            public string Raw;          // raw section text, markers intact (for RebuildTimedScript)
            public int    Start;        // char index into Chunk.Text
            public int    Length;       // char count in Chunk.Text
            public bool   IsOverlap;    // true = seam conditioning, discarded after slicing

            /// <summary>Markers with CleanIndex rebased into CHUNK coordinates.</summary>
            public List<TtsScriptProcessor.Marker> Markers = new List<TtsScriptProcessor.Marker>();

            public int End => Start + Length;   // exclusive
        }

        public class Chunk
        {
            public int         Index;   // 0-based
            public string      Id;      // "chunkA", "chunkB", …
            public string      Text;    // the exact string POSTed to ElevenLabs
            public List<Span>  Spans = new List<Span>();

            /// <summary>Spans that become output files (everything but the overlap).</summary>
            public IEnumerable<Span> Sections
            {
                get { foreach (var s in Spans) if (!s.IsOverlap) yield return s; }
            }
        }

        public class Plan
        {
            public List<Chunk>  Chunks   = new List<Chunk>();
            public List<string> Warnings = new List<string>();
        }

        // -------------------------------------------------------------------
        // Assembly
        // -------------------------------------------------------------------

        /// <summary>
        /// Build the chunk plan for one script. <paramref name="segments"/> must
        /// already carry their final Order/Slug (the orchestrator assigns the
        /// order prefix before calling), because those slugs become filenames.
        /// </summary>
        public static Plan Assemble(List<TtsScriptProcessor.Segment> segments, Options options)
        {
            options = options ?? new Options();
            var plan = new Plan();

            if (segments == null || segments.Count == 0)
            {
                plan.Warnings.Add("No sections to assemble.");
                return plan;
            }

            // ---- 1. clean text + markers, per section ----------------------
            int n = segments.Count;
            var cleanTexts = new string[n];
            var markerSets = new List<TtsScriptProcessor.Marker>[n];
            var lengths    = new int[n];

            for (int i = 0; i < n; i++)
            {
                var (markers, clean) = TtsScriptProcessor.ExtractMarkers(
                    segments[i].Raw, options.KeepStageDirections);
                cleanTexts[i] = clean;
                markerSets[i] = markers;
                lengths[i]    = clean.Length;
            }

            // ---- 2. decide the group boundaries ----------------------------
            List<int> groupSizes = Partition(segments, lengths, options, plan.Warnings);

            // ---- 3. materialise each chunk ---------------------------------
            int cursor = 0;
            for (int g = 0; g < groupSizes.Count; g++)
            {
                var chunk = new Chunk { Index = g, Id = ChunkId(g) };
                var sb    = new StringBuilder();

                // Seam conditioning: every chunk after the first opens with the
                // tail of the previous chunk, so the model starts this take from
                // the same context it ended the last one in — that is what locks
                // the delivery across the seam. This audio is cut off and thrown
                // away by the splitter; only its influence survives.
                if (g > 0)
                {
                    string overlap = LastSentences(
                        plan.Chunks[g - 1].Text, options.OverlapSentences, options.MinOverlapChars);

                    if (!string.IsNullOrEmpty(overlap))
                    {
                        chunk.Spans.Add(new Span {
                            Order     = 0,
                            Slug      = null,
                            Name      = "(overlap)",
                            Start     = 0,
                            Length    = overlap.Length,
                            IsOverlap = true,
                        });
                        sb.Append(overlap).Append(SectionJoin);
                    }
                    else
                    {
                        plan.Warnings.Add(
                            $"{chunk.Id}: no overlap text available — the seam in front of it " +
                            "is unconditioned.");
                    }
                }

                for (int k = 0; k < groupSizes[g]; k++, cursor++)
                {
                    var seg = segments[cursor];
                    if (sb.Length > 0 && k > 0) sb.Append(SectionJoin);

                    int start = sb.Length;
                    sb.Append(cleanTexts[cursor]);

                    var span = new Span {
                        Order  = seg.Order,
                        Slug   = seg.Slug,
                        Name   = seg.Name,
                        Raw    = seg.Raw,
                        Start  = start,
                        Length = cleanTexts[cursor].Length,
                    };

                    // Rebase every marker from section coordinates into chunk
                    // coordinates. This is the only mapping that ever happens —
                    // the splitter and the T= stamper both read these directly.
                    foreach (var m in markerSets[cursor])
                    {
                        span.Markers.Add(new TtsScriptProcessor.Marker {
                            Text       = m.Text,
                            CharIndex  = m.CharIndex,             // still section-local: rebuild needs it
                            CleanIndex = start + m.CleanIndex,    // chunk-local
                        });
                    }

                    chunk.Spans.Add(span);
                }

                chunk.Text = sb.ToString();
                if (chunk.Text.Length > options.MaxChunkChars)
                    plan.Warnings.Add(
                        $"{chunk.Id} is {chunk.Text.Length} characters, over the " +
                        $"{options.MaxChunkChars} guard — a single section may be too long to " +
                        "split further.");

                plan.Chunks.Add(chunk);
            }

            return plan;
        }

        // -------------------------------------------------------------------
        // Partitioning
        // -------------------------------------------------------------------

        /// <summary>
        /// How many consecutive sections go in each chunk. Prefers the named
        /// boundary; falls back to a balanced split, and adds chunks when the
        /// script is too long to fit two.
        /// </summary>
        static List<int> Partition(
            List<TtsScriptProcessor.Segment> segments, int[] lengths,
            Options options, List<string> warnings)
        {
            int n = segments.Count;
            if (n == 1) return new List<int> { 1 };

            // Budget per chunk, minus the room the overlap prefix will eat.
            int budget = Math.Max(200, options.MaxChunkChars - Math.Max(0, options.OverlapBudgetChars));

            int total = 0;
            foreach (int len in lengths) total += len + SectionJoin.Length;

            // How many chunks the script needs at minimum. Two is the floor —
            // the whole point is a small number of long takes.
            int wanted = Math.Max(2, (int)Math.Ceiling(total / (double)budget));
            wanted = Math.Min(wanted, n);

            if (wanted == 2)
            {
                int boundary = FindBoundaryIndex(segments, options.BoundarySection);
                if (boundary < 0)
                {
                    warnings.Add(
                        $"No section matching '{options.BoundarySection}' — splitting at the " +
                        "section boundary closest to the middle instead.");
                }
                else if (boundary >= n - 1)
                {
                    warnings.Add(
                        $"Section '{options.BoundarySection}' is the last one — splitting at the " +
                        "section boundary closest to the middle instead.");
                    boundary = -1;
                }

                if (boundary >= 0)
                {
                    int a = 0, b = 0;
                    for (int i = 0; i <= boundary; i++) a += lengths[i] + SectionJoin.Length;
                    for (int i = boundary + 1; i < n; i++) b += lengths[i] + SectionJoin.Length;

                    if (a <= budget && b <= budget)
                        return new List<int> { boundary + 1, n - boundary - 1 };

                    warnings.Add(
                        $"The '{options.BoundarySection}' boundary would leave a chunk of " +
                        $"{Math.Max(a, b)} characters, over the {budget} budget — rebalancing.");
                }
            }

            return BalancedSizes(lengths, wanted);
        }

        /// <summary>Index of the last section whose name or slug matches, or -1.</summary>
        static int FindBoundaryIndex(List<TtsScriptProcessor.Segment> segments, string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return -1;
            string needle = TtsScriptProcessor.MakeSlug(wanted);
            if (needle.Length == 0) return -1;

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                string nameSlug = TtsScriptProcessor.MakeSlug(segments[i].Name);
                string slug     = StripOrderPrefix(segments[i].Slug ?? "");
                if (nameSlug == needle || slug == needle) return i;
            }
            return -1;
        }

        static string StripOrderPrefix(string slug)
        {
            Match m = Regex.Match(slug, @"^\d+_(.+)$");
            return m.Success ? m.Groups[1].Value : slug;
        }

        /// <summary>
        /// Split <paramref name="lengths"/> into <paramref name="groups"/>
        /// consecutive runs, minimising the largest run — the "as balanced as
        /// possible at section boundaries" rule. Exact DP; the section count is
        /// always tiny.
        /// </summary>
        public static List<int> BalancedSizes(int[] lengths, int groups)
        {
            int n = lengths.Length;
            groups = Math.Max(1, Math.Min(groups, n));

            // prefix[i] = total of lengths[0..i-1] (+ the join that follows each)
            var prefix = new int[n + 1];
            for (int i = 0; i < n; i++)
                prefix[i + 1] = prefix[i] + lengths[i] + SectionJoin.Length;

            // best[g, i] = minimal possible largest-run when the first i sections
            // are cut into g runs. split[g, i] remembers where the last run began.
            var best  = new int[groups + 1, n + 1];
            var split = new int[groups + 1, n + 1];
            for (int g = 0; g <= groups; g++)
                for (int i = 0; i <= n; i++)
                    best[g, i] = int.MaxValue;
            best[0, 0] = 0;

            for (int g = 1; g <= groups; g++)
                for (int i = g; i <= n; i++)
                    for (int j = g - 1; j < i; j++)
                    {
                        if (best[g - 1, j] == int.MaxValue) continue;
                        int run   = prefix[i] - prefix[j];
                        int worst = Math.Max(best[g - 1, j], run);
                        if (worst >= best[g, i]) continue;
                        best[g, i]  = worst;
                        split[g, i] = j;
                    }

            var sizes = new List<int>(groups);
            int end = n;
            for (int g = groups; g >= 1; g--)
            {
                int begin = split[g, end];
                sizes.Insert(0, end - begin);
                end = begin;
            }
            return sizes;
        }

        // -------------------------------------------------------------------
        // Overlap extraction
        // -------------------------------------------------------------------

        // A sentence ends at . ! ? or … possibly followed by closing quotes or
        // brackets, and is followed by whitespace (or the end of the text).
        static readonly Regex SentenceEnd = new Regex(
            @"[.!?…]+[""'’”\)\]]*(?=\s|$)", RegexOptions.Compiled);

        /// <summary>
        /// The last <paramref name="count"/> sentences of <paramref name="text"/>,
        /// extended to earlier sentences until the result is at least
        /// <paramref name="minChars"/> long. Returns the whole text when it is
        /// shorter than that.
        /// </summary>
        public static string LastSentences(string text, int count, int minChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.TrimEnd();
            count = Math.Max(1, count);

            // Start index of every sentence: 0, then just past each terminator.
            var starts = new List<int> { 0 };
            foreach (Match m in SentenceEnd.Matches(text))
            {
                int after = m.Index + m.Length;
                while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
                if (after < text.Length && after > starts[starts.Count - 1]) starts.Add(after);
            }

            int pick = Math.Max(0, starts.Count - count);
            while (pick > 0 && text.Length - starts[pick] < minChars) pick--;

            return text.Substring(starts[pick]).Trim();
        }

        // chunkA, chunkB … chunkZ, then chunk27, chunk28 … (never realistically reached)
        static string ChunkId(int index)
            => index < 26 ? "chunk" + (char)('A' + index) : "chunk" + (index + 1);
    }
}

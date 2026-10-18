using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MugsTech.Tts
{
    /// <summary>
    /// "Update Tags Only" on a generation folder: load the editable script back
    /// out of it, and write an edited script's tags into it without touching the
    /// voice. The re-timing itself is <see cref="TagOnlyRetimer"/>; this class is
    /// the folder contract.
    ///
    /// What a generation holds that makes this possible: the per-section
    /// <c>_timed.txt</c> files (the script, stamped), and the take's own timing —
    /// <c>chunks/&lt;id&gt;_alignment.json</c> + <c>_text.txt</c> located by
    /// <c>render_manifest.json</c> for the chunked pipeline (the exact lookup the
    /// generation made), or <c>word_timestamps/</c> for the per-section one.
    /// Only <c>_timed.txt</c> and <c>script.txt</c> are ever rewritten, and the
    /// previous ones are copied to <c>tag_edits/&lt;time&gt;/</c> first.
    /// </summary>
    public static class TagOnlyUpdate
    {
        /// <summary>The source script, saved next to every generation (and
        /// rewritten by each tag edit) so Load gets the exact text back.</summary>
        public const string ScriptFileName   = "script.txt";
        public const string BackupFolderName = "tag_edits";

        public class Outcome
        {
            public bool         Success;
            public string       Message;
            public List<string> Warnings = new List<string>();
        }

        // ---- which generation ---------------------------------------------

        /// <summary>
        /// The folder the recording scene will load — mirrors
        /// ScriptFileReader.Start / ResolveActiveGenerationFolder, so the tags
        /// edited are the ones that get recorded. Null when nothing is found.
        /// </summary>
        public static string ResolveActiveGeneration()
        {
            if (PlayerPrefs.GetInt(MainMenuController.UseBundledTtsOutputPrefKey, 0) == 1)
            {
                string bundled = Path.Combine(Application.streamingAssetsPath,
                                              MainMenuController.BundledTtsOutputSubfolder);
                return HasManifest(bundled) ? bundled : null;
            }

            string selected = PlayerPrefs.GetString(TtsPanelController.SelectedGenerationPrefKey, "");
            if (!string.IsNullOrWhiteSpace(selected) && HasOutput(selected)) return selected;

            string root = TtsGenerationJob.ResolveOutputFolder(PlayerPrefs.GetString(
                MainMenuController.PythonOutputFolderPrefKey, MainMenuController.DefaultPythonOutputFolder));
            if (HasOutput(root)) return root;
            if (!Directory.Exists(root)) return null;

            string[] subdirs = Directory.GetDirectories(root);
            Array.Sort(subdirs, (a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
            foreach (string d in subdirs)
                if (HasOutput(d)) return d;
            return null;
        }

        static bool HasOutput(string dir)
            => Directory.Exists(dir) &&
               (File.Exists(Path.Combine(dir, "manifest.json")) ||
                Directory.GetFiles(dir, "*_timed.txt").Length > 0);

        static bool HasManifest(string dir) => File.Exists(Path.Combine(dir, "manifest.json"));

        // ---- load ---------------------------------------------------------

        /// <summary>
        /// The editable script of a generation: its saved <c>script.txt</c> when
        /// that still describes the files on disk, otherwise one rebuilt from the
        /// <c>_timed.txt</c> files (generations made before script.txt existed).
        /// </summary>
        public static bool TryLoadScript(string folder, out string script, out string error)
        {
            script = null;
            if (!TryReadEntries(folder, out var entries, out error)) return false;

            string saved = Path.Combine(folder, ScriptFileName);
            if (File.Exists(saved) && TryLoadSections(folder, entries, out var sections, out _))
            {
                string text = File.ReadAllText(saved);
                var check = TagOnlyRetimer.Retime(text, sections);
                if (check.Success && check.ChangedCount == 0) { script = text; return true; }
            }

            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.Append("## ").Append(e.Manifest.name).Append('\n');
                sb.Append(TagOnlyRetimer.Unstamp(e.TimedScript, null).Trim()).Append("\n\n");
            }
            script = sb.ToString().TrimEnd() + "\n";
            return true;
        }

        // ---- apply --------------------------------------------------------

        /// <summary>
        /// Re-time <paramref name="editedScript"/>'s tags against
        /// <paramref name="folder"/>'s voice and rewrite the sections that
        /// changed. Nothing is written unless every section passes.
        /// </summary>
        public static Outcome Apply(string folder, string editedScript)
        {
            var outcome = new Outcome();
            string name = Path.GetFileName(folder?.TrimEnd('/', '\\') ?? "");

            if (!TryReadEntries(folder, out var entries, out string error) ||
                !TryLoadSections(folder, entries, out var sections, out error))
                return Failed(outcome, error);

            // Media names are checked against the folder the recording will
            // search: the main menu's media root, the same pref
            // MediaPresentationSystem.Start reads.
            string mediaRoot = PlayerPrefs.GetString(MainMenuController.MediaRootFolderPrefKey, "").Trim();
            var result = TagOnlyRetimer.Retime(editedScript, sections, (kind, mediaName) =>
                MediaPresentationSystem.MediaNameResolves(mediaRoot, mediaName, video: kind == "Video"));
            if (!result.Success) return Failed(outcome, result.Error);
            outcome.Warnings.AddRange(result.Warnings);
            if (result.MissingMedia.Count > 0)
            {
                if (mediaRoot.Length == 0)
                    outcome.Warnings.Add("No media folder is set on the main menu, so only Resources/Media names resolve.");
                else if (!Directory.Exists(mediaRoot))
                    outcome.Warnings.Add($"The main menu's media folder doesn't exist: {mediaRoot}");
            }

            if (result.ChangedCount == 0)
            {
                outcome.Success = true;
                outcome.Message = $"No tag changes compared with {name} — nothing to update.";
                return outcome;
            }

            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backup = Path.Combine(folder, BackupFolderName, stamp);
            for (int n = 1; Directory.Exists(backup); n++)
                backup = Path.Combine(folder, BackupFolderName, $"{stamp}_{n}");

            try
            {
                Directory.CreateDirectory(backup);
                foreach (var e in entries)
                    File.Copy(e.TimedPath, Path.Combine(backup, e.Manifest.script_file));
                string saved = Path.Combine(folder, ScriptFileName);
                if (File.Exists(saved)) File.Copy(saved, Path.Combine(backup, ScriptFileName));

                var utf8 = new UTF8Encoding(false);
                for (int i = 0; i < result.Sections.Count; i++)
                    if (result.Sections[i].Changed)
                        File.WriteAllText(entries[i].TimedPath, result.Sections[i].TimedScript, utf8);
                File.WriteAllText(saved, TtsScriptProcessor.NormaliseLineEndings(editedScript), utf8);
            }
            catch (Exception e)
            {
                return Failed(outcome, $"Write failed: {e.Message} — the previous files are in " +
                                       $"{BackupFolderName}/{Path.GetFileName(backup)}.");
            }

            int retimed = 0;
            foreach (var s in result.Sections) retimed += s.Retimed;
            outcome.Success = true;
            outcome.Message = $"Updated {result.ChangedCount} section(s) of {name} ({retimed} tag(s) " +
                              $"re-timed) — voice untouched. Backup: {BackupFolderName}/{Path.GetFileName(backup)}.";
            return outcome;
        }

        static Outcome Failed(Outcome o, string error)
        {
            o.Success = false;
            o.Message = error;
            return o;
        }

        // ---- reading the generation ---------------------------------------

        class SectionFile
        {
            public TtsGenerationJob.ManifestEntry Manifest;
            public string TimedPath;
            public string TimedScript;
        }

        [Serializable] class ManifestFile { public TtsGenerationJob.ManifestEntry[] segments; }

        static bool TryReadEntries(string folder, out List<SectionFile> entries, out string error)
        {
            entries = new List<SectionFile>();
            error   = null;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                error = "No generation selected — pick one in the output library first.";
                return false;
            }

            string manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                error = $"{Path.GetFileName(folder)} has no manifest.json — only generations made by the " +
                        "TTS panel can be tag-edited.";
                return false;
            }

            ManifestFile manifest;
            try { manifest = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(manifestPath)); }
            catch (Exception e) { error = $"manifest.json is unreadable: {e.Message}"; return false; }

            if (manifest?.segments == null || manifest.segments.Length == 0)
            {
                error = "manifest.json lists no sections.";
                return false;
            }

            var ordered = new List<TtsGenerationJob.ManifestEntry>(manifest.segments);
            ordered.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var m in ordered)
            {
                string path = Path.Combine(folder, m.script_file ?? "");
                if (!File.Exists(path))
                {
                    error = $"{m.script_file} is missing from {Path.GetFileName(folder)}.";
                    return false;
                }
                entries.Add(new SectionFile {
                    Manifest    = m,
                    TimedPath   = path,
                    TimedScript = TtsScriptProcessor.NormaliseLineEndings(File.ReadAllText(path)),
                });
            }
            return true;
        }

        // render_manifest.json, as far as re-timing needs it.
        [Serializable] class RenderManifest
        {
            public string        pipeline;
            public RenderChunking chunking;
            public RenderChunk[] chunks;
        }
        [Serializable] class RenderChunking { public bool stage_directions_sent; }
        [Serializable] class RenderChunk
        {
            public string        id;
            public float         audio_seconds;
            public RenderSpan[]  spans;
            public RenderCut[]   sections;
        }
        [Serializable] class RenderSpan { public string slug; public int char_start; public int char_end; public bool discarded; }
        [Serializable] class RenderCut  { public string slug; public float from; public float duration; }

        [Serializable] class WordList { public WordJson[] items; }
        [Serializable] class WordJson { public string word; public float start; public float end; }

        static bool TryLoadSections(string folder, List<SectionFile> entries,
                                    out List<TagOnlyRetimer.Section> sections, out string error)
        {
            sections = new List<TagOnlyRetimer.Section>();
            error    = null;

            RenderManifest render = null;
            string renderPath = Path.Combine(folder, "render_manifest.json");
            if (File.Exists(renderPath))
            {
                try { render = JsonUtility.FromJson<RenderManifest>(File.ReadAllText(renderPath)); }
                catch (Exception e) { Debug.LogWarning($"[TagOnly] render_manifest.json unreadable: {e.Message}"); }
            }

            bool chunked  = render != null && render.pipeline == "chunked";
            bool keepCues = chunked && render.chunking != null && render.chunking.stage_directions_sent;
            var takes = new Dictionary<string, (string text, TtsAlignment alignment)>();

            foreach (var e in entries)
            {
                var section = new TagOnlyRetimer.Section {
                    Slug                  = e.Manifest.slug,
                    Name                  = e.Manifest.name,
                    TimedScript           = e.TimedScript,
                    StageDirectionsVoiced = keepCues,
                };

                if (chunked && TryChunkTiming(folder, render, e.Manifest.slug, takes, section))
                {
                    sections.Add(section);
                    continue;
                }

                // Per-section pipeline (exact: it timed markers off these very
                // words), or a chunked one whose chunks/ folder was deleted.
                var words = ReadWords(Path.Combine(folder, "word_timestamps", e.Manifest.slug + "_words.json"));
                if (words == null)
                {
                    error = $"{e.Manifest.slug} has no timing data (chunks/ and word_timestamps/ are both " +
                            "missing), so its tags can't be re-timed.";
                    return false;
                }

                string raw = TagOnlyRetimer.Unstamp(e.TimedScript, null);
                string spoken = TtsScriptProcessor.ExtractMarkers(raw, keepCues).cleanText;
                section.Spoken      = spoken;
                section.Approximate = chunked;
                section.TimeAt      = at => TtsScriptProcessor.MapMarkersToTimestamps(
                    new List<TtsScriptProcessor.Marker> { new TtsScriptProcessor.Marker { CleanIndex = at } },
                    words, spoken)[0].TriggerTime;
                sections.Add(section);
            }
            return true;
        }

        // Rebuilds the chunked job's own marker lookup (ChunkedTtsGenerationJob
        // .WriteSection): the take's character alignment at the section's
        // offset in the chunk, moved onto the section file's timeline.
        static bool TryChunkTiming(string folder, RenderManifest render, string slug,
                                   Dictionary<string, (string text, TtsAlignment alignment)> takes,
                                   TagOnlyRetimer.Section section)
        {
            if (render.chunks == null) return false;
            foreach (var chunk in render.chunks)
            {
                RenderSpan span = null;
                if (chunk.spans != null)
                    foreach (var s in chunk.spans) if (!s.discarded && s.slug == slug) span = s;
                RenderCut cut = null;
                if (chunk.sections != null)
                    foreach (var c in chunk.sections) if (c.slug == slug) cut = c;
                if (span == null || cut == null) continue;

                if (!takes.TryGetValue(chunk.id, out var take))
                {
                    take = LoadTake(folder, chunk);
                    takes[chunk.id] = take;
                }
                if (take.text == null || take.alignment == null) return false;
                if (span.char_start < 0 || span.char_end > take.text.Length || span.char_end < span.char_start)
                    return false;

                var alignment = take.alignment;
                int   offset   = span.char_start;
                float cutStart = cut.from;
                float duration = cut.duration;

                section.Spoken = take.text.Substring(span.char_start, span.char_end - span.char_start);
                section.TimeAt = at =>
                {
                    float t = alignment.StartAt(offset + at) - cutStart;
                    t = t < 0f ? 0f : (t > duration ? duration : t);
                    return (float)Math.Round(t, 3);
                };
                return true;
            }
            return false;
        }

        static (string text, TtsAlignment alignment) LoadTake(string folder, RenderChunk chunk)
        {
            string dir = Path.Combine(folder, "chunks");
            string textPath = Path.Combine(dir, chunk.id + "_text.txt");
            string alignPath = Path.Combine(dir, chunk.id + "_alignment.json");
            if (!File.Exists(textPath)) return (null, null);

            string text = File.ReadAllText(textPath);
            TtsAlignment alignment = File.Exists(alignPath)
                ? TtsAlignment.FromJson(File.ReadAllText(alignPath))
                : null;

            // Same rescue the job applied before slicing: an alignment that
            // doesn't describe the text was replaced by an even spread.
            if ((alignment == null || !alignment.MatchesText(text)) && chunk.audio_seconds > 0f)
                alignment = TtsAlignment.Proportional(text, chunk.audio_seconds);
            return (text, alignment);
        }

        static List<TtsScriptProcessor.WordTimestamp> ReadWords(string path)
        {
            if (!File.Exists(path)) return null;
            WordList list;
            try { list = JsonUtility.FromJson<WordList>("{\"items\":" + File.ReadAllText(path) + "}"); }
            catch { return null; }
            if (list?.items == null) return null;

            // Generations from before SplitIntoSegments normalised line endings
            // sent CRLF text, so their words carry "\r" ("building.\r", or a bare
            // "\r"). The text they're matched against is LF now; without the \r
            // the words line up with it again.
            var words = new List<TtsScriptProcessor.WordTimestamp>(list.items.Length);
            foreach (var w in list.items)
            {
                string word = (w.word ?? "").Replace("\r", "");
                if (word.Length == 0) continue;
                words.Add(new TtsScriptProcessor.WordTimestamp { Word = word, Start = w.start, End = w.end });
            }
            return words;
        }
    }
}

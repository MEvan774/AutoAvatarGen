using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MugsTech.Tts;
using UnityEngine;
using UnityEngine.Networking;

// ============================================================================
// SegmentSequencer — stitches multiple ElevenLabs output segments into one
// seamless AudioClip + script for the existing reaction pipeline.
//
// HOW IT WORKS
//   1. Reads manifest.json (emitted by elevenlabs_tts_processor.py) which
//      lists segments in playback order plus speech_start / speech_end
//      (first/last word times from the ElevenLabs word-level alignment).
//   2. Loads each segment's .mp3 and _timed.txt.
//   3. Builds ONE combined AudioClip by copying each clip's samples from
//      trimStart → speech_end+trailingPadding, with a configurable
//      interSegmentPause of pure silence between segments. Leading silence
//      is kept on the first segment and trailing silence on the last so
//      openings/endings feel natural; inter-segment silences are replaced
//      by the controlled pause so no double-gap builds up.
//      When a segment OPENS with a {Transition:...} the pause in front of it is
//      widened to fit the whole section break and the transition is re-pointed to
//      fire inside that silence, so the wipe runs over a real gap and the
//      section's first word lands on the revealed scene instead of being spoken
//      behind the cover (see FindOpeningTransition):
//
//        …last word | transitionLeadIn | cover -> reveal | transitionTail | first word…
//   4. Combines all the _timed.txt scripts with every T=X.XXX marker shifted
//      onto the new global timeline. A marker originally at T=X in segment
//      i becomes T = globalOffset[i] + (X - trimStart[i]), clamped to at
//      least globalOffset[i] so markers that sat inside the (now-removed)
//      leading silence fire at the segment's audible start instead of
//      leaking into the previous segment.
//   5. TIMING CORRECTION (correctTimingFromAudio): the manifest's
//      speech_start/speech_end and every baked T= come from the ElevenLabs
//      *synthesis* alignment, and eleven_v3's alignment does not reliably
//      describe its own audio — measured on real generations it drifted up to
//      ±1.3s mid-segment and once claimed speech ended 2.0s before it actually
//      did, which made this stitcher CUT THE LAST WORDS of a section and then
//      play the transition gap over them. So each segment's decoded samples
//      are analyzed directly: the real speech span replaces the manifest span
//      for trimming (a transition's silence can never amputate real speech,
//      and the gap in front of a section-opening transition is exactly the
//      authored leadIn+cover+reveal+tail regardless of how many transitions
//      the script has), markers are linearly rescaled onto the measured span
//      when the two disagree, and any marker that lands inside a measured
//      pause snaps forward to the pause's end — the onset of the word it was
//      authored against. Generations whose alignment is accurate (e.g. from
//      the forced-alignment pass in TtsGenerationJob) pass through unchanged.
//   6. {Pause:N} — "the gap in the voice here is at least N seconds". Inside a
//      section, PauseInserter finds the gap in the decoded samples (the audio
//      decides; T= only says where to look), widens it to N if v3 left less,
//      and pins the tags around it to the gap's edges: tags written above the
//      pause fire as it opens, tags below fire as it closes. Everything later
//      moves by the silence added. On a section's edge a {Pause} widens the
//      gap between sections instead (PauseInserter.SizeBoundary). All of it is
//      silence laid into audio that already exists — the ElevenLabs request
//      never changes — and every {Pause} is stripped before playback.
//
// WHY THIS WORKS WITH THE EXISTING REACTION SYSTEM
//   MediaPresentationSystem / HybridAvatarSystem / ContentZoneController all
//   key their reactions off voiceAudio.time. Because each marker's shifted
//   T= falls inside its own segment's playback window in the combined clip,
//   "reactions from file N fire only while file N's audio is playing" comes
//   out for free — no per-segment mode switching needed.
// ============================================================================

[Serializable]
public class SegmentManifestEntry
{
    public int order;
    public string slug;
    public string name;
    public string audio_file;
    public string script_file;
    public float duration;
    public float speech_start;
    public float speech_end;
}

[Serializable]
public class SegmentManifest
{
    public List<SegmentManifestEntry> segments;
}

public class SegmentSequencer : MonoBehaviour
{
    [Header("Stitching")]
    [Tooltip("Seconds of pure silence inserted between each segment's last word " +
             "and the next segment's first word. Replaces any native leading/" +
             "trailing silence from ElevenLabs so pacing is consistent.")]
    [Range(0f, 1.5f)]
    public float interSegmentPause = 0.35f;

    [Tooltip("Extra silence kept after each segment's last word before the " +
             "inter-segment pause — prevents the final consonant from being " +
             "cut abruptly.")]
    [Range(0f, 0.5f)]
    public float trailingPadding = 0.08f;

    [Tooltip("Hold the narration while a section-opening {Transition:...} plays. The pause in " +
             "front of that segment is widened to fit the transition's own cover+reveal length " +
             "plus the lead-in and tail below, and the transition is re-pointed into that " +
             "silence. Off = the old behaviour, narration playing straight through the " +
             "transition. Transitions placed mid-section are never re-timed.")]
    public bool pauseNarrationForTransitions = true;

    [Tooltip("Silence between the previous section's audio ending (its last word plus " +
             "trailingPadding) and the moment a section-opening transition STARTS. Without it " +
             "the wipe begins the instant the audio cuts, which reads as 'transition, then " +
             "pause' rather than a beat between sections.")]
    [Range(0f, 1.5f)]
    public float transitionLeadIn = 0.4f;

    [Tooltip("Silence between a section-opening transition finishing its reveal and the new " +
             "section's first word — a short settle on the revealed scene before he speaks.")]
    [Range(0f, 1f)]
    public float transitionTail = 0.15f;

    [Header("Timing Correction")]
    [Tooltip("Measure each segment's REAL speech span and pauses from the decoded audio and " +
             "correct the manifest/marker times against them. eleven_v3's synthesis alignment " +
             "can be seconds off (it once ended 2s before the actual last word, so the stitch " +
             "cut real speech ahead of a transition). Costs a few ms per segment. Turn off only " +
             "to A/B against the raw alignment.")]
    public bool correctTimingFromAudio = true;

    // Outputs — populated by LoadAndBuild.
    [HideInInspector] public AudioClip combinedClip;
    [HideInInspector] public string combinedScript;
    [HideInInspector] public string titleSlug;
    [HideInInspector] public List<SegmentManifestEntry> orderedSegments;

    /// <summary>
    /// Every {Pause} the last take was built with — what was measured there and
    /// what was added. Filled by LoadAndBuild (or ApplyPausesToSingleClip on the
    /// single-pair path); AutomationRunner reports it in the result JSON.
    /// </summary>
    public static readonly List<PauseInserter.Entry> LastPauseReport = new List<PauseInserter.Entry>();

    public IEnumerator LoadAndBuild(string folder)
    {
        combinedClip = null;
        combinedScript = null;
        titleSlug = null;
        orderedSegments = null;
        LastPauseReport.Clear();

        string manifestPath = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning($"[SegmentSequencer] manifest.json not found in {folder}");
            yield break;
        }

        SegmentManifest manifest;
        try
        {
            manifest = JsonUtility.FromJson<SegmentManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SegmentSequencer] Failed to parse manifest.json: {ex.Message}");
            yield break;
        }

        if (manifest == null || manifest.segments == null || manifest.segments.Count == 0)
        {
            Debug.LogError("[SegmentSequencer] manifest.json has no segments.");
            yield break;
        }

        manifest.segments.Sort((a, b) => a.order.CompareTo(b.order));

        List<AudioClip> clips = new List<AudioClip>(manifest.segments.Count);
        List<string> scripts = new List<string>(manifest.segments.Count);

        foreach (var seg in manifest.segments)
        {
            string audioPath = Path.Combine(folder, seg.audio_file);
            string scriptPath = Path.Combine(folder, seg.script_file);

            if (!File.Exists(scriptPath))
            {
                Debug.LogError($"[SegmentSequencer] Missing script: {scriptPath}");
                yield break;
            }

            AudioClip clip = null;
            yield return LoadAudioClip(audioPath, loaded => clip = loaded);
            if (clip == null)
            {
                Debug.LogError($"[SegmentSequencer] Failed to load audio: {audioPath}");
                yield break;
            }

            clips.Add(clip);
            scripts.Add(File.ReadAllText(scriptPath));
        }

        BuildCombined(manifest.segments, clips, scripts);

        orderedSegments = manifest.segments;
        titleSlug = manifest.segments.Count == 1
            ? manifest.segments[0].slug
            : StripOrderPrefix(manifest.segments[0].slug) + "_STITCHED";

        Debug.Log($"[SegmentSequencer] Stitched {manifest.segments.Count} segment(s) -> " +
                  $"{combinedClip.length:F2}s combined clip ('{titleSlug}')");
    }

    void BuildCombined(List<SegmentManifestEntry> segs, List<AudioClip> clips, List<string> scripts)
    {
        int sampleRate = clips[0].frequency;
        int channels   = clips[0].channels;

        // Validate the set — AudioClip.Create requires uniform format.
        for (int i = 1; i < clips.Count; i++)
        {
            if (clips[i].frequency != sampleRate || clips[i].channels != channels)
            {
                Debug.LogWarning($"[SegmentSequencer] Segment {segs[i].slug} has mismatched " +
                                 $"format ({clips[i].frequency}Hz/{clips[i].channels}ch vs " +
                                 $"{sampleRate}Hz/{channels}ch). Stitching may sound wrong.");
            }
        }

        List<float> samples = new List<float>();
        StringBuilder scriptSb = new StringBuilder();
        float globalOffset = 0f;
        int last = clips.Count - 1;

        // Pass 1 — measure every segment and fix its trim window before any
        // silence is laid down. A {Pause} on a section's edge sizes the gap
        // BETWEEN two sections, and that needs both sides' trims up front.
        var timings     = new SegmentAudioTiming[clips.Count];
        var speechStart = new float[clips.Count];
        var speechEnd   = new float[clips.Count];
        var trimStart   = new float[clips.Count];
        var trimEnd     = new float[clips.Count];
        var pauseTags   = new List<PauseInserter.PauseTag>[clips.Count];
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            SegmentManifestEntry seg = segs[i];

            // Ground the manifest's alignment-derived speech span (and later every
            // marker) against the audio that was actually rendered.
            SegmentAudioTiming timing = correctTimingFromAudio
                ? AnalyzeSegmentAudio(clip, seg)
                : null;
            timings[i] = timing;
            if (timing != null && timing.rescale)
                Debug.Log($"[SegmentSequencer] {seg.slug}: alignment says speech is " +
                          $"{timing.alignStart:F2}..{timing.alignEnd:F2}s but the audio measures " +
                          $"{timing.realStart:F2}..{timing.realEnd:F2}s — retiming this segment's " +
                          $"markers (end off by {timing.realEnd - timing.alignEnd:+0.00;-0.00}s).");

            // Trim on the MEASURED span when available, so a transition's baked
            // silence starts after the audible last word — never over it.
            speechStart[i] = timing != null ? timing.realStart : Mathf.Max(0f, seg.speech_start);
            speechEnd[i]   = timing != null ? timing.realEnd   : seg.speech_end;

            trimStart[i] = (i == 0) ? 0f : Mathf.Max(0f, speechStart[i] - TrimPreRollSeconds);
            trimEnd[i]   = (i == last)
                ? clip.length
                : Mathf.Min(clip.length, Mathf.Max(speechEnd[i] + trailingPadding, trimStart[i]));

            pauseTags[i] = PauseInserter.FindTags(scripts[i]);
        }

        // Silence to insert BEFORE each segment. Index 0 is always 0 — the first
        // segment opens the video. Every other boundary gets interSegmentPause,
        // widened when that segment opens with a transition to fit the whole
        // beat: leadIn -> cover -> reveal -> tail -> first word — and widened to
        // N by a {Pause:N} on the last line of the section before it or the first
        // line(s) of this one (PauseInserter.SizeBoundary; with no {Pause} it is
        // exactly the sizing above).
        float[] pauseBefore = new float[clips.Count];
        float[] openerTail  = new float[clips.Count];
        OpeningTransition[] openers = new OpeningTransition[clips.Count];
        var boundaryEntries = new PauseInserter.Entry[clips.Count];
        for (int i = 1; i < clips.Count; i++)
        {
            if (pauseNarrationForTransitions)
                openers[i] = FindOpeningTransition(scripts[i]);

            // Edge pauses: after the previous section's last word, and before this
            // one's first. Below an opening transition the beat belongs after the
            // reveal; anywhere else on the boundary it belongs before the cover.
            float holdBefore = 0f, holdAfter = 0f;
            var edge = new List<(PauseInserter.PauseTag tag, string slug)>();
            foreach (var tag in pauseTags[i - 1])
                if (tag.AtSectionEnd && !tag.AtSectionStart && tag.Seconds > 0f)
                {
                    holdBefore = Mathf.Max(holdBefore, tag.Seconds);
                    edge.Add((tag, segs[i - 1].slug));
                }
            foreach (var tag in pauseTags[i])
            {
                if (!tag.AtSectionStart || tag.Seconds <= 0f) continue;
                if (openers[i] != null && tag.Index > openers[i].index)
                    holdAfter = Mathf.Max(holdAfter, tag.Seconds);
                else
                    holdBefore = Mathf.Max(holdBefore, tag.Seconds);
                edge.Add((tag, segs[i].slug));
            }

            float transitionSeconds = openers[i] != null ? openers[i].seconds : -1f;
            float trailPad = trimEnd[i - 1] - speechEnd[i - 1];
            float preRoll  = speechStart[i] - trimStart[i];
            var sized = PauseInserter.SizeBoundary(interSegmentPause, transitionSeconds,
                transitionLeadIn, transitionTail, trailPad, preRoll, holdBefore, holdAfter);
            pauseBefore[i] = sized.PauseBefore;
            openerTail[i]  = sized.Tail;

            if (openers[i] != null)
                Debug.Log($"[SegmentSequencer] {segs[i].slug} opens with a {openers[i].type} " +
                          $"x{openers[i].scale:F2} ({openers[i].seconds:F2}s) — holding the " +
                          $"narration {pauseBefore[i]:F2}s ({sized.LeadIn:F2}s lead-in + " +
                          $"{openers[i].seconds:F2}s transition + {sized.Tail:F2}s tail).");

            if (edge.Count > 0)
            {
                var plain = PauseInserter.SizeBoundary(interSegmentPause, transitionSeconds,
                    transitionLeadIn, transitionTail, trailPad, preRoll, 0f, 0f);
                boundaryEntries[i] = BoundaryEntry(segs[i - 1].slug, segs[i].slug, edge, openers[i],
                                                   sized, plain, holdBefore, holdAfter);
            }
        }

        // A {Pause} before the video's first word or after its last has no gap
        // between sections to widen — say so rather than drop it silently.
        var report = new List<PauseInserter.Entry>();
        foreach (var tag in pauseTags[0])
            if (tag.AtSectionStart)
                report.Add(EdgeIgnored(segs[0].slug, tag, "before the video's first word"));

        // Pass 2 — lay the take down.
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            SegmentManifestEntry seg = segs[i];
            SegmentAudioTiming timing = timings[i];

            if (boundaryEntries[i] != null)
            {
                boundaryEntries[i].VideoAt = globalOffset;   // the gap opens where the last section's audio ends
                report.Add(boundaryEntries[i]);
            }

            if (pauseBefore[i] > 0f)
            {
                int silenceFrames = Mathf.RoundToInt(pauseBefore[i] * sampleRate);
                if (silenceFrames > 0)
                    samples.AddRange(new float[silenceFrames * channels]);
                globalOffset += pauseBefore[i];
            }

            float segDuration = Mathf.Max(0f, trimEnd[i] - trimStart[i]);

            int offsetFrames = Mathf.Clamp(
                Mathf.RoundToInt(trimStart[i] * sampleRate),
                0, clip.samples > 0 ? clip.samples - 1 : 0);
            int frames = Mathf.Clamp(
                Mathf.RoundToInt(segDuration * sampleRate),
                0, Mathf.Max(0, clip.samples - offsetFrames));

            // {Pause:N} inside the section: widen its gap to N in the samples being
            // copied, and pin the tags around it to the gap's edges (step 6).
            PauseInserter.Result paused = null;
            if (frames > 0)
            {
                float[] buffer = new float[frames * channels];
                clip.GetData(buffer, offsetFrames);

                if (HasMidSectionPause(pauseTags[i]))
                {
                    paused = PauseInserter.Apply(seg.slug, pauseTags[i], buffer, channels, sampleRate,
                        trimStart[i],
                        t => timing != null ? timing.MapToReal(t, false) : t,
                        timing != null ? ToGaps(timing.pauses) : null);
                    buffer = paused.Samples;
                }
                samples.AddRange(buffer);
            }

            // delta converts original-clip T values onto the combined timeline.
            // minAllowedT prevents pre-speech markers from bleeding backwards
            // into the previous segment after we trim leading silence.
            float delta        = globalOffset - trimStart[i];
            float minAllowedT  = globalOffset;
            string shifted     = ShiftTimestamps(scripts[i], delta, minAllowedT, timing, paused?.Shift);

            // The opening transition is the one marker that must fire BEFORE its
            // own segment's audio. Position it from the END of the gap — back off
            // the first word by the tail, then by the transition's own length — so
            // the reveal always settles the same beat ahead of the read no matter
            // how wide the gap ended up. Whatever silence is left over lands in
            // front of the transition as the lead-in (never less than
            // transitionLeadIn; more when interSegmentPause or a {Pause} above the
            // transition is the wider). A {Pause} below the transition lengthens
            // the tail instead, so the held beat plays on the revealed scene. The
            // clamp keeps it inside the silence we actually baked.
            if (openers[i] != null && pauseBefore[i] > 0f)
            {
                float triggerAt = Mathf.Max(
                    globalOffset - pauseBefore[i],
                    globalOffset - openerTail[i] - openers[i].seconds);
                shifted = RetimeOpeningTransition(shifted, triggerAt);
            }

            // Every {Pause} is spent by now; none may reach the runtime parsers.
            if (pauseTags[i].Count > 0)
                shifted = PauseInserter.StripTags(shifted);

            scriptSb.AppendLine(shifted);

            if (paused != null)
                foreach (var entry in paused.Entries)
                {
                    entry.VideoAt = globalOffset + (entry.At - trimStart[i])
                                  + paused.Shift.InsertedBefore(entry.TagIndex);
                    report.Add(entry);
                }

            globalOffset += segDuration + (paused != null ? paused.TotalInserted : 0f);
        }

        foreach (var tag in pauseTags[last])
            if (tag.AtSectionEnd && !tag.AtSectionStart)
                report.Add(EdgeIgnored(segs[last].slug, tag, "after the video's last word"));

        int totalFrames = samples.Count / Mathf.Max(1, channels);
        if (totalFrames <= 0)
        {
            Debug.LogError("[SegmentSequencer] Combined clip would have zero samples.");
            combinedClip = null;
            combinedScript = null;
            return;
        }

        combinedClip = AudioClip.Create("StitchedSegments", totalFrames, channels, sampleRate, false);
        combinedClip.SetData(samples.ToArray(), 0);
        combinedScript = scriptSb.ToString();

        LastPauseReport.Clear();
        LastPauseReport.AddRange(report);
        LogPauseReport(report);
    }

    // -----------------------------------------------------------------------
    // {Pause:N} (step 6 of the class header; the work is in PauseInserter)
    // -----------------------------------------------------------------------

    /// <summary>
    /// The single-pair load (one _timed.txt + its audio, no manifest) never runs
    /// BuildCombined, so ScriptFileReader calls this to give its lone clip the
    /// same {Pause} treatment: pauses measured here (with no manifest there is no
    /// alignment span to correct against, so each T= is taken as it is), gaps
    /// widened, tags pinned, the clip rebuilt with the silence in.
    /// </summary>
    public static void ApplyPausesToSingleClip(ref AudioClip clip, ref string script, string slug)
    {
        LastPauseReport.Clear();
        var tags = PauseInserter.FindTags(script);
        if (tags.Count == 0) return;

        var report = new List<PauseInserter.Entry>();
        foreach (var tag in tags)
            if (tag.OnEdge)
                report.Add(EdgeIgnored(slug, tag, tag.AtSectionStart ? "before the first word" : "after the last word"));

        float[] data = null;
        if (clip != null && clip.samples > 0 && clip.channels > 0)
        {
            data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) data = null;
        }
        if (data == null)
        {
            Debug.LogWarning($"[Pause] {slug}: the clip's samples can't be read, so its {tags.Count} " +
                             "{Pause} tag(s) were ignored.");
            script = PauseInserter.StripTags(script);
            return;
        }

        var paused = PauseInserter.Apply(slug, tags, data, clip.channels, clip.frequency, 0f, null, null);
        script = PauseInserter.StripTags(ShiftTimestamps(script, 0f, 0f, null, paused.Shift));

        if (paused.TotalInserted > 0f)
        {
            var longer = AudioClip.Create(clip.name, paused.Samples.Length / clip.channels,
                                          clip.channels, clip.frequency, false);
            longer.SetData(paused.Samples, 0);
            clip = longer;
        }

        foreach (var entry in paused.Entries)
        {
            entry.VideoAt = entry.At + paused.Shift.InsertedBefore(entry.TagIndex);
            report.Add(entry);
        }
        LastPauseReport.AddRange(report);
        LogPauseReport(report);
    }

    static bool HasMidSectionPause(List<PauseInserter.PauseTag> tags)
    {
        foreach (var tag in tags) if (!tag.OnEdge) return true;
        return false;
    }

    static List<PauseInserter.Gap> ToGaps(List<Vector2> pauses)
    {
        var gaps = new List<PauseInserter.Gap>(pauses != null ? pauses.Count : 0);
        if (pauses != null)
            foreach (var p in pauses) gaps.Add(new PauseInserter.Gap(p.x, p.y));
        return gaps;
    }

    static PauseInserter.Entry EdgeIgnored(string slug, PauseInserter.PauseTag tag, string where)
        => new PauseInserter.Entry {
            Section = slug, Tag = tag.Authored, TagIndex = tag.Index, Requested = tag.Seconds,
            Boundary = true, Severe = true,
            Note = $"{where} — there is no gap between sections to widen there, so it was ignored",
        };

    static PauseInserter.Entry BoundaryEntry(string before, string after,
        List<(PauseInserter.PauseTag tag, string slug)> edge, OpeningTransition opener,
        PauseInserter.Boundary sized, PauseInserter.Boundary plain, float holdBefore, float holdAfter)
    {
        var longest = edge[0];
        string problems = null;
        foreach (var e in edge)
        {
            if (e.tag.Seconds > longest.tag.Seconds) longest = e;
            if (e.tag.Problem != null) problems = problems == null ? e.tag.Problem : problems + "; " + e.tag.Problem;
        }

        string info;
        if (opener == null)
            info = $"between {before} and {after}, now {sized.HeardGap:F2}s from word to word";
        else
        {
            info = "";
            if (holdBefore > 0f)
                info += $"{sized.LeadIn:F2}s before the {opener.type} transition (was {plain.LeadIn:F2}s)";
            if (holdAfter > 0f)
                info += (info.Length > 0 ? ", " : "") +
                        $"{sized.Tail:F2}s after its reveal before the first word (was {plain.Tail:F2}s)";
        }

        return new PauseInserter.Entry {
            Section   = longest.slug,
            Tag       = longest.tag.Authored + (edge.Count > 1 ? $" (+{edge.Count - 1} more)" : ""),
            TagIndex  = longest.tag.Index,
            Requested = Mathf.Max(holdBefore, holdAfter),
            Natural   = plain.HeardGap,
            Inserted  = Mathf.Max(0f, sized.PauseBefore - plain.PauseBefore),
            Boundary  = true,
            Info      = info,
            Note      = problems,
        };
    }

    static void LogPauseReport(List<PauseInserter.Entry> report)
    {
        if (report.Count == 0) return;

        int inserted = 0, check = 0;
        float total = 0f;
        foreach (var e in report)
        {
            string line = $"[Pause] {e.Section} {e.Describe()}";
            if (e.NeedsCheck) { Debug.LogWarning(line); check++; }
            else              Debug.Log(line);
            if (e.Inserted > 0f) { inserted++; total += e.Inserted; }
        }
        Debug.Log($"[Pause] summary: {report.Count} tag(s), {inserted} inserted, +{total:F2}s total" +
                  (check > 0 ? $", {check} to check by ear." : "."));
    }

    // -----------------------------------------------------------------------
    // Section-opening transitions
    //
    // A {Transition:...} on the first line of a section (the authored pattern —
    // SCRIPT_TAG_GUIDE §4) used to fire on the section's first spoken word, so
    // cover+reveal ran over the opening of the read. We give it silence to play
    // over instead: widen the pause in front of the segment and re-point the tag
    // into it. Everything else in the segment keeps its normal shifted timeline,
    // so the section's first word is the first thing heard on the new scene.
    // -----------------------------------------------------------------------

    // The whole beat a section-opening transition needs to itself —
    //   last word | leadIn | cover -> reveal | tail | first word
    // — is sized by PauseInserter.SizeBoundary, which also fits a {Pause} on
    // either side of it.
    class OpeningTransition
    {
        public ScreenTransition type;
        public float scale;      // {Transition:Iris,1.2} -> 1.2
        public float seconds;    // cover + reveal, already scaled
        public int   index;      // where the tag sits in the segment script
    }

    // {Transition:Wipe} / {Transition:Iris,1.2} / {Transition:Wipe,T=5.0} /
    // {Transition:Iris,1.2,T=5.0} — in lockstep with MediaPresentationSystem's
    // TransitionRegex, which is what actually parses these at playback.
    static readonly Regex _TransitionTag = new Regex(
        @"\{Transition:(\w+)(?:,(\d+(?:\.\d+)?))?(?:,T=(\d+(?:\.\d+)?))?\}",
        RegexOptions.IgnoreCase);

    // Any tag or stage direction — used to prove nothing is SPOKEN ahead of the
    // transition. Bounded to one line like the TTS processor's own safety net.
    static readonly Regex _AnyTag = new Regex(@"\{[^{}\n]*\}|\[[^\]]*\]");

    // The segment's first {Transition:...}, if nothing but other tags and
    // whitespace precedes it — i.e. it opens the section rather than sitting in
    // the middle of one. Testing the TEXT rather than the tag's T= means a
    // mid-section transition is left alone (it should still play over the read),
    // and it doesn't matter which word the timestamp happened to land on.
    // Returns null when the segment doesn't open with a transition.
    static OpeningTransition FindOpeningTransition(string script)
    {
        if (string.IsNullOrEmpty(script)) return null;

        Match tr = _TransitionTag.Match(script);
        if (!tr.Success) return null;

        string spokenBefore = _AnyTag.Replace(script.Substring(0, tr.Index), "");
        if (spokenBefore.Trim().Length > 0) return null;

        float scale = 1f;
        if (tr.Groups[2].Success)
            float.TryParse(tr.Groups[2].Value,
                           NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
        if (scale <= 0f) scale = 1f;

        ScreenTransition type = ParseTransitionType(tr.Groups[1].Value);
        return new OpeningTransition {
            type    = type,
            scale   = scale,
            seconds = ScreenTransitionController.TotalSecondsFor(type, scale),
            index   = tr.Index,
        };
    }

    // Same fallback as MediaPresentationSystem.ParseTransitionType — an unknown
    // name plays a Wipe there, so size the silence for a Wipe here too.
    static ScreenTransition ParseTransitionType(string s)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "shutter": return ScreenTransition.Shutter;
            case "iris":    return ScreenTransition.Iris;
            default:        return ScreenTransition.Wipe;
        }
    }

    // Rewrite the FIRST {Transition:...} in the segment to fire at `time`. Any
    // later transition in the same segment keeps the timeline ShiftTimestamps
    // gave it. A tag that arrived without a T= gets one, so playback never falls
    // back to MediaPresentationSystem's char-proportional estimate.
    static string RetimeOpeningTransition(string script, float time)
    {
        bool rewritten = false;
        return _TransitionTag.Replace(script, match =>
        {
            if (rewritten) return match.Value;
            rewritten = true;

            string scale = match.Groups[2].Success ? "," + match.Groups[2].Value : "";
            return "{Transition:" + match.Groups[1].Value + scale
                 + ",T=" + time.ToString("F3", CultureInfo.InvariantCulture) + "}";
        });
    }

    static readonly Regex _TPattern = new Regex(@"T=(\d+(?:\.\d+)?)");

    static string ShiftTimestamps(string script, float deltaSeconds, float minAllowedT,
                                  SegmentAudioTiming timing = null, PauseInserter.Shift pauses = null)
    {
        return _TPattern.Replace(script, match =>
        {
            if (!float.TryParse(match.Groups[1].Value,
                                NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                return match.Value;

            // Alignment timeline -> measured-audio timeline first, then onto the
            // combined clip. deltaSeconds is (globalOffset - trimStart), and both
            // sides of that subtraction live on the measured timeline.
            // A {Pause} in the section pins the tags in its gap to the gap's edges
            // and moves everything after it by the silence it added; it resolves
            // by script position, so tags sharing one T= still split either side.
            if (pauses != null)
                t = pauses.Resolve(match.Index, t, x => timing != null ? timing.MapToReal(x) : x);
            else if (timing != null)
                t = timing.MapToReal(t);

            float shifted = Mathf.Max(minAllowedT, t + deltaSeconds);
            return "T=" + shifted.ToString("F3", CultureInfo.InvariantCulture);
        });
    }

    // -----------------------------------------------------------------------
    // Audio-measured segment timing (see step 5 of the class header).
    //
    // The ElevenLabs synthesis alignment is treated as a *claim* about the
    // audio, not the truth: the decoded samples are measured directly, and the
    // claim is corrected where it disagrees. Everything here is intentionally
    // simple — an RMS envelope, a noise-floor-relative speech span, and a list
    // of intra-speech pauses:
    //   * span:   first/last 20ms window above (noise floor + 14dB), clamped
    //             to [-48, -30] dBFS. Replaces manifest speech_start/end for
    //             trimming, so baked transition gaps can't cut real words.
    //   * rescale: when the spans disagree by > 0.25s at either end, marker
    //             times are linearly rescaled from the alignment span onto the
    //             measured span (distributes the observed end-drift).
    //   * snap:  a marker landing inside a measured pause fires before its
    //             word has begun — it snaps forward to the pause end (<= 0.9s),
    //             the measured onset of the word it was authored against.
    //             Pauses are "below -35 dBFS for >= 0.25s", which counts the
    //             breaths eleven_v3 renders between sentences as pause.
    // -----------------------------------------------------------------------

    const float EnvelopeWindowSeconds = 0.02f;
    const float SpanMarginDb          = 14f;
    const float SpanThresholdMinDb    = -48f;
    const float SpanThresholdMaxDb    = -30f;
    const float PauseThresholdDb      = -35f;
    const float MinRealPauseSeconds   = 0.25f;
    const float SpanAgreementSeconds  = 0.25f;
    const float PauseSnapMaxSeconds   = 0.9f;
    const float TrimPreRollSeconds    = 0.05f;
    const float RescaledSnapEndZone   = 8f;

    class SegmentAudioTiming
    {
        public float realStart, realEnd;    // measured speech span in the clip
        public float alignStart, alignEnd;  // manifest (synthesis-alignment) span
        public bool  rescale;               // spans disagree -> warp marker times
        public List<Vector2> pauses;        // measured pauses (x=start, y=end)

        public float MapToReal(float t) => MapToReal(t, true);

        // snap = false is the plain alignment -> audio mapping, for a {Pause}: it
        // wants the gap itself, not the onset of the word after it.
        public float MapToReal(float t, bool snap)
        {
            float u = t;
            if (rescale)
                u = realStart + (t - alignStart)
                    * (realEnd - realStart) / Mathf.Max(0.01f, alignEnd - alignStart);
            if (!snap) return u;

            // Inside a measured pause = the authored word hasn't started yet, so
            // snap forward to the measured word onset. When the alignment is
            // provably wrong (rescale), the rescaled estimate is only trustworthy
            // near the span ends it is anchored to — mid-segment the residual can
            // exceed the snap radius and a snap would lock in an overshoot (v3's
            // error curve swings sign), so snapping is confined to the end zones.
            bool allowSnap = !rescale
                || (t - alignStart) <= RescaledSnapEndZone
                || (alignEnd - t)   <= RescaledSnapEndZone;
            if (allowSnap && pauses != null)
                for (int i = 0; i < pauses.Count; i++)
                {
                    Vector2 p = pauses[i];
                    if (u >= p.x && u < p.y)
                    {
                        if (p.y - u <= PauseSnapMaxSeconds) u = p.y;
                        break;
                    }
                }
            return u;
        }
    }

    static SegmentAudioTiming AnalyzeSegmentAudio(AudioClip clip, SegmentManifestEntry seg)
    {
        var timing = new SegmentAudioTiming {
            alignStart = Mathf.Max(0f, seg.speech_start),
            alignEnd   = Mathf.Max(Mathf.Max(0f, seg.speech_start) + 0.01f, seg.speech_end),
            realStart  = Mathf.Max(0f, seg.speech_start),
            realEnd    = seg.speech_end,
            pauses     = new List<Vector2>(),
        };

        int channels = clip.channels;
        int frames   = clip.samples;
        if (frames <= 0 || channels <= 0) return timing;

        float[] data = new float[frames * channels];
        if (!clip.GetData(data, 0)) return timing;

        int windowFrames = Mathf.Max(1, Mathf.RoundToInt(EnvelopeWindowSeconds * clip.frequency));
        int windowCount  = frames / windowFrames;
        if (windowCount < 8) return timing;

        // Per-window RMS in dBFS across all channels.
        float[] windowDb = new float[windowCount];
        for (int w = 0; w < windowCount; w++)
        {
            int begin = w * windowFrames * channels;
            int end   = begin + windowFrames * channels;
            double acc = 0;
            for (int s = begin; s < end; s++) acc += data[s] * (double)data[s];
            double rms = System.Math.Sqrt(acc / (windowFrames * channels));
            windowDb[w] = rms <= 1e-9 ? -120f : 20f * (float)System.Math.Log10(rms);
        }

        // Noise floor = 5th percentile; span threshold rides above it.
        float[] sorted = (float[])windowDb.Clone();
        System.Array.Sort(sorted);
        float floor = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Length * 0.05f), 0, sorted.Length - 1)];
        float spanThreshold = Mathf.Min(SpanThresholdMaxDb, Mathf.Max(floor + SpanMarginDb, SpanThresholdMinDb));

        int firstLoud = -1, lastLoud = -1;
        for (int w = 0; w < windowCount; w++)
        {
            if (windowDb[w] < spanThreshold) continue;
            if (firstLoud < 0) firstLoud = w;
            lastLoud = w;
        }
        if (firstLoud < 0) return timing; // silent clip — keep manifest values

        float measuredStart = firstLoud * EnvelopeWindowSeconds;
        float measuredEnd   = (lastLoud + 1) * EnvelopeWindowSeconds;
        if (measuredEnd - measuredStart < 1f) return timing; // degenerate — keep manifest

        timing.realStart = measuredStart;
        timing.realEnd   = measuredEnd;
        timing.rescale   = Mathf.Abs(timing.realStart - timing.alignStart) > SpanAgreementSeconds
                        || Mathf.Abs(timing.realEnd   - timing.alignEnd)   > SpanAgreementSeconds;

        // Pauses inside the speech span (quiet run >= MinRealPauseSeconds).
        int minPauseWindows = Mathf.Max(1, Mathf.RoundToInt(MinRealPauseSeconds / EnvelopeWindowSeconds));
        int runStart = -1;
        for (int w = firstLoud; w <= lastLoud + 1; w++)
        {
            bool quiet = w <= lastLoud && windowDb[w] < PauseThresholdDb;
            if (quiet)
            {
                if (runStart < 0) runStart = w;
            }
            else if (runStart >= 0)
            {
                if (w - runStart >= minPauseWindows)
                    timing.pauses.Add(new Vector2(runStart * EnvelopeWindowSeconds,
                                                  w * EnvelopeWindowSeconds));
                runStart = -1;
            }
        }
        return timing;
    }

    // Segment audio is mp3 by default, but the chunked TTS pipeline writes WAV
    // when ffmpeg isn't available to encode. The manifest names the file, so
    // just believe its extension.
    public static AudioType AudioTypeFor(string path)
    {
        switch (Path.GetExtension(path ?? "").ToLowerInvariant())
        {
            case ".wav":  return AudioType.WAV;
            case ".ogg":  return AudioType.OGGVORBIS;
            default:      return AudioType.MPEG;
        }
    }

    IEnumerator LoadAudioClip(string path, Action<AudioClip> onLoaded)
    {
        string uri = new Uri(path).AbsoluteUri;
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioTypeFor(path)))
        {
            // GetData() requires the full clip in memory. Streaming defeats that,
            // and produces a decoded-on-demand clip whose samples aren't readable.
            if (req.downloadHandler is DownloadHandlerAudioClip handler)
                handler.streamAudio = false;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SegmentSequencer] Audio load failed: {req.error} ({uri})");
                onLoaded(null);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = Path.GetFileNameWithoutExtension(path);
            onLoaded(clip);
        }
    }

    // "01_COLD_OPEN" -> "COLD_OPEN";  "COLD_OPEN" -> "COLD_OPEN"
    static string StripOrderPrefix(string slug)
    {
        Match m = Regex.Match(slug, @"^\d+_(.+)$");
        return m.Success ? m.Groups[1].Value : slug;
    }
}

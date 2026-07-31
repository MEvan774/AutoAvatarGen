using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    // Outputs — populated by LoadAndBuild.
    [HideInInspector] public AudioClip combinedClip;
    [HideInInspector] public string combinedScript;
    [HideInInspector] public string titleSlug;
    [HideInInspector] public List<SegmentManifestEntry> orderedSegments;

    public IEnumerator LoadAndBuild(string folder)
    {
        combinedClip = null;
        combinedScript = null;
        titleSlug = null;
        orderedSegments = null;

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

        // Silence to insert BEFORE each segment. Index 0 is always 0 — the first
        // segment opens the video. Every other boundary gets interSegmentPause,
        // widened when that segment opens with a transition to fit the whole
        // beat: leadIn -> cover -> reveal -> tail -> first word.
        float[] pauseBefore = new float[clips.Count];
        OpeningTransition[] openers = new OpeningTransition[clips.Count];
        for (int i = 1; i < clips.Count; i++)
        {
            pauseBefore[i] = Mathf.Max(0f, interSegmentPause);
            if (!pauseNarrationForTransitions) continue;

            openers[i] = FindOpeningTransition(scripts[i]);
            if (openers[i] == null) continue;

            pauseBefore[i] = Mathf.Max(pauseBefore[i], openers[i].TotalGap(this));
            Debug.Log($"[SegmentSequencer] {segs[i].slug} opens with a {openers[i].type} " +
                      $"x{openers[i].scale:F2} ({openers[i].seconds:F2}s) — holding the " +
                      $"narration {pauseBefore[i]:F2}s ({transitionLeadIn:F2}s lead-in + " +
                      $"{openers[i].seconds:F2}s transition + {transitionTail:F2}s tail).");
        }

        int last = clips.Count - 1;
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            SegmentManifestEntry seg = segs[i];

            if (pauseBefore[i] > 0f)
            {
                int silenceFrames = Mathf.RoundToInt(pauseBefore[i] * sampleRate);
                if (silenceFrames > 0)
                    samples.AddRange(new float[silenceFrames * channels]);
                globalOffset += pauseBefore[i];
            }

            float trimStart = (i == 0) ? 0f : Mathf.Max(0f, seg.speech_start);
            float trimEnd   = (i == last)
                ? clip.length
                : Mathf.Min(clip.length, Mathf.Max(seg.speech_end + trailingPadding, trimStart));

            float segDuration = Mathf.Max(0f, trimEnd - trimStart);

            int offsetFrames = Mathf.Clamp(
                Mathf.RoundToInt(trimStart * sampleRate),
                0, clip.samples > 0 ? clip.samples - 1 : 0);
            int frames = Mathf.Clamp(
                Mathf.RoundToInt(segDuration * sampleRate),
                0, Mathf.Max(0, clip.samples - offsetFrames));

            if (frames > 0)
            {
                float[] buffer = new float[frames * channels];
                clip.GetData(buffer, offsetFrames);
                samples.AddRange(buffer);
            }

            // delta converts original-clip T values onto the combined timeline.
            // minAllowedT prevents pre-speech markers from bleeding backwards
            // into the previous segment after we trim leading silence.
            float delta        = globalOffset - trimStart;
            float minAllowedT  = globalOffset;
            string shifted     = ShiftTimestamps(scripts[i], delta, minAllowedT);

            // The opening transition is the one marker that must fire BEFORE its
            // own segment's audio. Position it from the END of the gap — back off
            // the first word by the tail, then by the transition's own length — so
            // the reveal always settles the same beat ahead of the read no matter
            // how wide the gap ended up. Whatever silence is left over lands in
            // front of the transition as the lead-in (never less than
            // transitionLeadIn; more when interSegmentPause is the wider of the
            // two). The clamp keeps it inside the silence we actually baked.
            if (openers[i] != null && pauseBefore[i] > 0f)
            {
                float triggerAt = Mathf.Max(
                    globalOffset - pauseBefore[i],
                    globalOffset - Mathf.Max(0f, transitionTail) - openers[i].seconds);
                shifted = RetimeOpeningTransition(shifted, triggerAt);
            }

            scriptSb.AppendLine(shifted);

            globalOffset += segDuration;
        }

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

    class OpeningTransition
    {
        public ScreenTransition type;
        public float scale;      // {Transition:Iris,1.2} -> 1.2
        public float seconds;    // cover + reveal, already scaled

        // The whole beat this transition needs to itself:
        //   last word | leadIn | cover -> reveal | tail | first word
        public float TotalGap(SegmentSequencer owner)
            => Mathf.Max(0f, owner.transitionLeadIn)
             + seconds
             + Mathf.Max(0f, owner.transitionTail);
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

    static string ShiftTimestamps(string script, float deltaSeconds, float minAllowedT)
    {
        return _TPattern.Replace(script, match =>
        {
            if (!float.TryParse(match.Groups[1].Value,
                                NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                return match.Value;

            float shifted = Mathf.Max(minAllowedT, t + deltaSeconds);
            return "T=" + shifted.ToString("F3", CultureInfo.InvariantCulture);
        });
    }

    IEnumerator LoadAudioClip(string path, Action<AudioClip> onLoaded)
    {
        string uri = new Uri(path).AbsoluteUri;
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
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

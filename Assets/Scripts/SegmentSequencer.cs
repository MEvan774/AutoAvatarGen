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

        int last = clips.Count - 1;
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            SegmentManifestEntry seg = segs[i];

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

            scriptSb.AppendLine(shifted);

            globalOffset += segDuration;

            // Inter-segment silence (not after the last segment).
            if (i < last && interSegmentPause > 0f)
            {
                int silenceFrames = Mathf.RoundToInt(interSegmentPause * sampleRate);
                if (silenceFrames > 0)
                    samples.AddRange(new float[silenceFrames * channels]);
                globalOffset += interSegmentPause;
            }
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

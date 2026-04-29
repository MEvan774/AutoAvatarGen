using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MugsTech.Background
{
    /// <summary>
    /// Runtime background music. Auto-spawns a DontDestroyOnLoad host with an
    /// AudioSource, loads the configured playlist from disk via
    /// UnityWebRequestMultimedia (so .mp3/.wav/.ogg from arbitrary paths work),
    /// applies RMS-based loudness normalization per clip so a quiet master and
    /// a loud master end up at the same perceived level, and plays them back
    /// in order, looping the list when it ends.
    ///
    /// Resolution precedence (matches the bg-video pattern):
    ///   1. <see cref="OverridePathPrefKey"/>  — single track from main menu;
    ///                                            never persisted into a preset.
    ///   2. <see cref="PresetListPrefKey"/>   + <see cref="PresetVolumePrefKey"/>
    ///      — written by VisualsRuntimeApplier from the active VisualsSave.
    ///   3. No music.
    ///
    /// Default volume is 0.15 (15% of voice level) — soft enough to live
    /// under the script narration without competing for attention.
    ///
    /// Playback is gated on the script's voice AudioSource: music starts when
    /// voice starts and stops the instant voice ends. That guarantees the
    /// recording's audible duration is bounded by the script alone — music
    /// can never extend a recording or play after the voice has finished.
    /// </summary>
    public class BackgroundMusicPlayer : MonoBehaviour
    {
        public const string OverridePathPrefKey   = "AutoAvatarGen.MusicOverride";
        public const string PresetListPrefKey     = "AutoAvatarGen.MusicPreset.List";   // newline-separated paths
        public const string PresetVolumePrefKey   = "AutoAvatarGen.MusicPreset.Volume"; // float 0..1

        public const float  DefaultVolume = 0.15f;

        // Target loudness after normalization (in linear amplitude). Roughly
        // -20 dBFS RMS, a typical "comfortable music" baseline.
        const float k_TargetRms = 0.10f;

        // Scene where music should actually play. Other scenes (the main menu)
        // get the configuration applied but no audible playback.
        const string k_PlaybackSceneName = "SampleScene";

        static BackgroundMusicPlayer s_Instance;

        AudioSource     source;
        List<AudioClip> tracks = new List<AudioClip>();
        int             currentIndex;
        Coroutine       loadCoroutine;

        // Voice clip we follow. Music plays only while voiceAudio.isPlaying.
        AudioSource     voiceAudio;
        bool            wasVoicePlaying;

        public static BackgroundMusicPlayer EnsureInstance()
        {
            if (s_Instance != null) return s_Instance;
            var go = new GameObject("BackgroundMusicPlayer");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<BackgroundMusicPlayer>();
            return s_Instance;
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this) { Destroy(gameObject); return; }
            s_Instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.loop        = false; // Update() advances to the next track manually.
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D
        }

        /// <summary>
        /// Reload settings from PlayerPrefs and (re)start playback. Called by
        /// VisualsRuntimeApplier on every scene load after it writes the
        /// preset prefs, so changes flow through automatically.
        /// </summary>
        public static void ApplyToActiveScene()
        {
            EnsureInstance().RefreshFromPlayerPrefs();
        }

        void RefreshFromPlayerPrefs()
        {
            // Stop and clear any in-flight playback before loading fresh.
            if (loadCoroutine != null) StopCoroutine(loadCoroutine);
            loadCoroutine = null;
            if (source != null) source.Stop();
            DisposeTracks();
            currentIndex    = 0;
            voiceAudio      = null;
            wasVoicePlaying = false;

            string overridePath = PlayerPrefs.GetString(OverridePathPrefKey,   "");
            string presetList   = PlayerPrefs.GetString(PresetListPrefKey,     "");
            float  volume       = PlayerPrefs.GetFloat (PresetVolumePrefKey,   DefaultVolume);

            List<string> paths;
            string sourceLabel;
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                paths       = new List<string> { overridePath.Trim() };
                sourceLabel = "main menu override";
            }
            else if (!string.IsNullOrWhiteSpace(presetList))
            {
                paths       = ParsePathList(presetList);
                sourceLabel = "preset playlist";
            }
            else
            {
                Debug.Log("[BgMusic] No override or preset playlist configured; idle.");
                return;
            }
            if (paths.Count == 0) return;

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != k_PlaybackSceneName)
            {
                Debug.Log($"[BgMusic] Scene='{sceneName}' is not the recording scene; deferring playback.");
                return;
            }

            source.volume = Mathf.Clamp01(volume);
            Debug.Log($"[BgMusic] Loading {paths.Count} track(s) from {sourceLabel} at volume {source.volume:F2}.");
            // Load tracks asynchronously. Update() decides when to actually
            // play, gated on the voice clip — that's how we guarantee music
            // never starts before the script does and never plays past it.
            loadCoroutine = StartCoroutine(LoadTracks(paths));
        }

        IEnumerator LoadTracks(List<string> paths)
        {
            foreach (string p in paths)
            {
                yield return LoadAndAdd(p);
            }
            if (tracks.Count == 0)
                Debug.LogWarning("[BgMusic] Loaded zero usable tracks; nothing to play.");
        }

        IEnumerator LoadAndAdd(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[BgMusic] Track path missing on disk: '{path}'");
                yield break;
            }
            string url       = new Uri(path).AbsoluteUri;
            AudioType audioType = GuessAudioType(path);

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[BgMusic] Failed to load '{path}': {www.error}");
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null || clip.samples == 0)
                {
                    Debug.LogWarning($"[BgMusic] Empty/unreadable audio: '{path}'");
                    yield break;
                }
                clip.name = Path.GetFileNameWithoutExtension(path);
                NormalizeClip(clip);
                tracks.Add(clip);
                Debug.Log($"[BgMusic] Loaded '{clip.name}' ({clip.length:F1}s, {clip.channels}ch, {clip.frequency}Hz)");
            }
        }

        void PlayTrack(int idx)
        {
            if (idx < 0 || idx >= tracks.Count) return;
            currentIndex = idx;
            source.clip  = tracks[idx];
            source.time  = 0f;
            source.Play();
            Debug.Log($"[BgMusic] Now playing track {idx + 1}/{tracks.Count}: {tracks[idx].name}");
        }

        void Update()
        {
            if (source == null) return;

            EnsureVoiceAudioReference();
            bool voicePlaying = voiceAudio != null && voiceAudio.isPlaying;

            if (!voicePlaying)
            {
                // Voice not playing — make sure music is silent. Reset clip
                // so the next "voice starts" begins from the top of the
                // playlist rather than mid-track.
                if (source.isPlaying) source.Stop();
                if (source.clip != null) source.clip = null;
                wasVoicePlaying = false;
                return;
            }

            wasVoicePlaying = true;
            if (tracks.Count == 0) return;

            // Voice is playing. If music isn't, either start the playlist
            // (first time) or advance to the next track (current ended).
            if (!source.isPlaying)
            {
                int next;
                if (source.clip == null)
                {
                    next         = 0;
                    currentIndex = 0;
                }
                else
                {
                    next = (currentIndex + 1) % tracks.Count;
                }
                PlayTrack(next);
            }
        }

        // Voice AudioSource lives on a CrossPlatformRecorder somewhere in the
        // recording scene. Search lazily — when this player wakes up the scene
        // may not have spawned yet, and the reference is destroyed on every
        // RefreshFromPlayerPrefs, so we re-find as needed.
        void EnsureVoiceAudioReference()
        {
            if (voiceAudio != null) return;
            var recorder = FindAnyObjectByType<CrossPlatformRecorder>();
            if (recorder != null) voiceAudio = recorder.voiceAudio;
        }

        void DisposeTracks()
        {
            foreach (AudioClip c in tracks)
                if (c != null) Destroy(c);
            tracks.Clear();
        }

        // -------------------------------------------------------------------
        // Loudness normalization (RMS-based, peak-clamped)
        // -------------------------------------------------------------------

        // Compute the clip's RMS amplitude over all samples + channels, then
        // scale samples in place so the new RMS equals k_TargetRms — capped
        // by the peak so we never push past 0 dBFS and clip on output.
        static void NormalizeClip(AudioClip clip)
        {
            int totalSamples = clip.samples * clip.channels;
            if (totalSamples <= 0) return;

            float[] data = new float[totalSamples];
            clip.GetData(data, 0);

            double sumSq = 0;
            float  peak  = 0f;
            for (int i = 0; i < totalSamples; i++)
            {
                float s = data[i];
                sumSq += (double)s * s;
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
            if (peak < 1e-6f) return; // clip is effectively silent
            float rms = Mathf.Sqrt((float)(sumSq / totalSamples));
            if (rms < 1e-6f) return;

            float desiredScale  = k_TargetRms / rms;
            float headroomScale = 0.99f / peak; // leave a sliver below 0 dBFS
            float scale         = Mathf.Min(desiredScale, headroomScale);
            if (Mathf.Approximately(scale, 1f)) return;

            for (int i = 0; i < totalSamples; i++) data[i] *= scale;
            clip.SetData(data, 0);
            Debug.Log($"[BgMusic] Normalized '{clip.name}': rms {rms:F4}->{rms * scale:F4}, " +
                      $"peak {peak:F4}->{peak * scale:F4} (scale={scale:F2})");
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        public static List<string> ParsePathList(string serialized)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(serialized)) return list;
            foreach (string line in serialized.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public static string SerializePathList(List<string> paths)
        {
            return paths == null ? "" : string.Join("\n", paths);
        }

        static AudioType GuessAudioType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".mp3":  return AudioType.MPEG;
                case ".wav":  return AudioType.WAV;
                case ".ogg":  return AudioType.OGGVORBIS;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default:      return AudioType.UNKNOWN;
            }
        }
    }
}

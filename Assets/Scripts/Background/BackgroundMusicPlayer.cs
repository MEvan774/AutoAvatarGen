using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MugsTech.Background
{
    /// <summary>
    /// Runtime background music. Auto-spawns a DontDestroyOnLoad host with an
    /// AudioSource, loads the configured playlist from disk via
    /// UnityWebRequestMultimedia (so .mp3/.wav/.ogg from arbitrary paths work),
    /// applies loudness normalization per clip so a quiet master and a loud
    /// master end up at the same perceived level, and plays them back under
    /// the narration.
    ///
    /// Resolution precedence:
    ///   1. <see cref="FolderPrefKey"/>         — folder mode (main menu "Music
    ///      folder" row): a random playlist drawn from the folder, planned to
    ///      cover the take's voice length, LUFS-equalized (BS.1770) to −16 LUFS
    ///      per track, sequential 2 s fades, constant 0.10 duck. Mirrors the
    ///      Python mixer (music_mixer/add_music.py) so the post-mux step can
    ///      be retired; MusicTakeLog records what played for the credits file.
    ///   2. <see cref="OverridePathPrefKey"/>  — single track from main menu;
    ///                                            never persisted into a preset.
    ///   3. <see cref="PresetListPrefKey"/>   + <see cref="PresetVolumePrefKey"/>
    ///      — written by VisualsRuntimeApplier from the active VisualsSave.
    ///   4. No music.
    ///
    /// Modes 2/3 keep their original RMS normalization and behavior —
    /// bit-identical to before folder mode existed. Folder mode failures never
    /// abort a take: the recording proceeds without music and the problem is
    /// flagged via MusicTakeLog (result panel + automation JSON).
    ///
    /// Playback is gated on the script's voice AudioSource: music starts when
    /// voice starts and stops the instant voice ends. That guarantees the
    /// recording's audible duration is bounded by the script alone — music
    /// can never extend a recording or play after the voice has finished.
    /// </summary>
    public class BackgroundMusicPlayer : MonoBehaviour
    {
        public const string FolderPrefKey         = "AutoAvatarGen.MusicFolder";
        public const string FolderVolumePrefKey   = "AutoAvatarGen.MusicFolderVolume";
        public const string OverridePathPrefKey   = "AutoAvatarGen.MusicOverride";
        public const string PresetListPrefKey     = "AutoAvatarGen.MusicPreset.List";   // newline-separated paths
        public const string PresetVolumePrefKey   = "AutoAvatarGen.MusicPreset.Volume"; // float 0..1

        public const float  DefaultVolume = 0.15f;

        // Folder mode: the channel's mixer settings. Duck 0.10 = the bed sits
        // at a constant 10% of full scale after per-track equalization (no
        // sidechain — matches add_music.py's constant `--volume`).
        public const float  FolderModeDuckVolume = 0.10f;
        const float  TargetLufs  = -16f;   // per-track equalization target
        const float  FadeSeconds = 2f;     // fade-in/out at each track edge + final bed fade

        // Preload must never hang a take: the planner gives up after this and
        // the recording starts with whatever loaded (possibly nothing), with
        // MusicTakeLog.Error set as the loud flag.
        const float  PreloadTimeoutSeconds = 30f;

        // A decoded 3-minute stereo clip is ~64MB of floats; refuse absurd
        // source files outright rather than risk an OOM mid-pipeline.
        const long   MaxTrackFileBytes = 200L * 1024 * 1024;

        // Target loudness after normalization (in linear amplitude). Roughly
        // -20 dBFS RMS, a typical "comfortable music" baseline. (Legacy
        // override/preset modes only — folder mode uses true LUFS above.)
        const float k_TargetRms = 0.10f;

        // Scene where music should actually play. Other scenes (the main menu)
        // get the configuration applied but no audible playback.
        const string k_PlaybackSceneName = "SampleScene";

        static BackgroundMusicPlayer s_Instance;

        AudioSource     source;
        List<AudioClip> tracks = new List<AudioClip>();
        int             currentIndex;
        Coroutine       loadCoroutine;

        // ---- Folder mode state ------------------------------------------------
        class FolderTrack
        {
            public AudioClip Clip;
            public string    FileName;
            public float     Duration;
            public float     GainLinear;   // dB→linear of (−16 − measured LUFS)
        }
        class PlaylistEntry
        {
            public FolderTrack Track;
            public bool        IsRepeat;
        }

        bool   folderMode;
        string folderPath = "";
        float  duckVolume = FolderModeDuckVolume;
        float  plannedVoiceLength;
        bool   folderPreloadStarted;
        bool   folderPreloadComplete;
        int    folderIndex;
        Coroutine preloadCoroutine;
        readonly List<PlaylistEntry> folderPlaylist = new List<PlaylistEntry>();
        // Decoded clips per unique file — a repeated track reuses one decode.
        readonly Dictionary<string, FolderTrack> folderTrackCache =
            new Dictionary<string, FolderTrack>(StringComparer.OrdinalIgnoreCase);

        // Unity's runtime decoder handles these on desktop. add_music.py also
        // accepted .m4a/.aac/.flac/.opus — those are skipped with a log line
        // (UnityWebRequestMultimedia can't decode them on Windows standalone).
        static readonly string[] PlayableExtensions  = { ".mp3", ".wav", ".ogg", ".aif", ".aiff" };
        static readonly string[] UnplayableMixerExts = { ".m4a", ".aac", ".flac", ".opus" };

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

        // -------------------------------------------------------------------
        // Folder mode — public gate
        // -------------------------------------------------------------------

        /// <summary>True while a folder-mode preload is running for this take.
        /// The recording start waits on this (bounded) so the bed is decoded,
        /// measured and planned before capture begins. Always false when the
        /// folder pref is empty — legacy behavior stays untouched.</summary>
        public static bool FolderPreloadPending =>
            s_Instance != null && s_Instance.folderMode &&
            s_Instance.folderPreloadStarted && !s_Instance.folderPreloadComplete;

        /// <summary>
        /// Plans and preloads the folder-mode playlist against the assembled
        /// voice clip's length. Called when the voice length first becomes
        /// known (MediaPresentationSystem.ProcessScriptWithMedia and
        /// HybridAvatarSystem.ProcessWithExistingAudio both call it —
        /// idempotent per scene load, so the second call is a no-op). Also
        /// resets MusicTakeLog for the take, folder mode on or off.
        /// </summary>
        public static void BeginFolderPreload(float voiceLengthSeconds)
        {
            EnsureInstance().BeginFolderPreloadInternal(voiceLengthSeconds);
        }

        void BeginFolderPreloadInternal(float voiceLength)
        {
            if (folderPreloadStarted) return;
            folderPreloadStarted = true;

            string folder = PlayerPrefs.GetString(FolderPrefKey, "").Trim();
            duckVolume = PlayerPrefs.GetFloat(FolderVolumePrefKey, FolderModeDuckVolume);
            folderMode = folder.Length > 0;
            folderPath = folder;
            MusicTakeLog.BeginTake(folderMode ? folder : null, duckVolume);

            if (!folderMode) { folderPreloadComplete = true; return; }

            if (SceneManager.GetActiveScene().name != k_PlaybackSceneName)
            {
                folderPreloadComplete = true;
                return;
            }

            plannedVoiceLength    = voiceLength;
            folderPreloadComplete = false;
            Debug.Log($"[BgMusic] Folder mode: planning a playlist from '{folderPath}' " +
                      $"to cover {voiceLength:F1}s of voice (duck {duckVolume:F2}, " +
                      $"target {TargetLufs:F0} LUFS).");
            preloadCoroutine = StartCoroutine(PreloadFolderPlaylist(voiceLength));
        }

        // -------------------------------------------------------------------
        // Folder mode — planner / preloader
        // -------------------------------------------------------------------

        // Mirrors add_music.py's pick_tracks: shuffle the folder into a pool,
        // draw until the voice length is covered, reshuffle when the pool
        // empties, and never let the same track play back-to-back across the
        // reshuffle boundary. Durations aren't known until decode, so the
        // draw-and-load is sequential — only the picked tracks are ever
        // decoded, never the whole library.
        IEnumerator PreloadFolderPlaylist(float voiceLength)
        {
            float startedAt = Time.realtimeSinceStartup;

            string[] files = null;
            string listError = null;
            try
            {
                if (!Directory.Exists(folderPath))
                    listError = $"Music folder does not exist: '{folderPath}'.";
                else
                    files = ListPlayableTracks(folderPath);
            }
            catch (Exception e)
            {
                listError = $"Music folder unreadable ('{folderPath}'): {e.Message}";
            }
            if (listError == null && (files == null || files.Length == 0))
                listError = $"Music folder '{folderPath}' contains no playable tracks " +
                            $"({string.Join("/", PlayableExtensions)}).";
            if (listError != null)
            {
                FinishFolderPreload(listError);
                yield break;
            }

            var pool   = new List<string>();
            var broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string last = null;
            float covered = 0f;
            int failures = 0;
            // Hard wall-clock deadline: LoadFolderTrack aborts an in-flight
            // request the moment it passes, so the take-start gates (which
            // wait on FolderPreloadPending with a slightly larger cap) can
            // never be outlived by a hung download.
            float deadline = startedAt + PreloadTimeoutSeconds;

            while (covered < voiceLength)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    FinishFolderPreload(
                        $"Music preload timed out after {PreloadTimeoutSeconds:0}s — recording with " +
                        $"{folderPlaylist.Count} track(s) covering {covered:0}s of {voiceLength:0}s.");
                    yield break;
                }

                if (pool.Count == 0)
                {
                    foreach (string f in files)
                        if (!broken.Contains(f)) pool.Add(f);
                    if (pool.Count == 0)
                    {
                        FinishFolderPreload(
                            $"No track in '{folderPath}' could be loaded " +
                            $"({failures} failure(s)) — recording without music.");
                        yield break;
                    }
                    Shuffle(pool);
                    // Avoid the same track back-to-back across reshuffles.
                    if (last != null && pool.Count > 1 &&
                        string.Equals(pool[0], last, StringComparison.OrdinalIgnoreCase))
                    {
                        (pool[0], pool[1]) = (pool[1], pool[0]);
                    }
                }

                string path = pool[0];
                pool.RemoveAt(0);

                FolderTrack track;
                if (!folderTrackCache.TryGetValue(path, out track))
                {
                    yield return LoadFolderTrack(path, deadline, t => track = t);
                    if (track == null)
                    {
                        broken.Add(path);
                        failures++;
                        continue;
                    }
                    folderTrackCache[path] = track;
                }

                bool isRepeat = !seen.Add(track.FileName);
                folderPlaylist.Add(new PlaylistEntry { Track = track, IsRepeat = isRepeat });
                MusicTakeLog.AddTrack(track.FileName, track.Duration, isRepeat);
                covered += track.Duration;
                last = path;
            }

            FinishFolderPreload(failures > 0
                ? $"{failures} music track(s) could not be loaded and were skipped."
                : null);
        }

        void FinishFolderPreload(string error)
        {
            preloadCoroutine = null;
            folderPreloadComplete = true;
            if (error != null)
            {
                Debug.LogWarning("[BgMusic] " + error);
                MusicTakeLog.SetError(error);
            }
            float covered = 0f;
            foreach (PlaylistEntry e in folderPlaylist) covered += e.Track.Duration;
            Debug.Log($"[BgMusic] Folder playlist ready: {folderPlaylist.Count} entry(ies), " +
                      $"{folderTrackCache.Count} unique decode(s), {covered:F0}s of bed " +
                      $"for {plannedVoiceLength:F0}s of voice.");
        }

        IEnumerator LoadFolderTrack(string path, float deadline, Action<FolderTrack> onLoaded)
        {
            string fileName = Path.GetFileName(path);
            try
            {
                if (new FileInfo(path).Length > MaxTrackFileBytes)
                {
                    Debug.LogWarning($"[BgMusic] '{fileName}' exceeds {MaxTrackFileBytes / (1024 * 1024)}MB — skipped.");
                    onLoaded(null);
                    yield break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BgMusic] Cannot stat '{path}': {e.Message}");
                onLoaded(null);
                yield break;
            }

            string url = new Uri(path).AbsoluteUri;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, GuessAudioType(path)))
            {
                UnityWebRequestAsyncOperation op = www.SendWebRequest();
                while (!op.isDone)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        // Preload budget exhausted mid-download — abort so the
                        // planner's own timeout check fires on the next loop.
                        www.Abort();
                    }
                    yield return null;
                }
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[BgMusic] Failed to load '{path}': {www.error}");
                    onLoaded(null);
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null || clip.samples == 0)
                {
                    Debug.LogWarning($"[BgMusic] Empty/unreadable audio: '{path}'");
                    onLoaded(null);
                    yield break;
                }
                clip.name = Path.GetFileNameWithoutExtension(path);

                // True integrated loudness (BS.1770), computed off the main
                // thread — a 3-minute stereo decode is ~16M frames and would
                // hitch the scene for a beat if filtered inline.
                int channels = clip.channels;
                int rate     = clip.frequency;
                float[] data = new float[clip.samples * channels];
                clip.GetData(data, 0);

                Task<double> lufsTask = Task.Run(() =>
                    LoudnessMeter.MeasureIntegratedLufs(data, channels, rate));
                while (!lufsTask.IsCompleted) yield return null;

                float gainDb = 0f;
                if (lufsTask.IsFaulted || double.IsNaN(lufsTask.Result) ||
                    double.IsInfinity(lufsTask.Result))
                {
                    // Same fallback as the Python mixer: unmeasurable (silent /
                    // broken) plays at gain 0 with a logged warning.
                    Debug.LogWarning($"[BgMusic] '{fileName}': loudness unmeasurable " +
                                     "(silent?) — mixing it as-is (0 dB).");
                }
                else
                {
                    gainDb = TargetLufs - (float)lufsTask.Result;
                    Debug.Log($"[BgMusic] '{fileName}': {lufsTask.Result:F1} LUFS -> " +
                              $"{gainDb:+0.0;-0.0} dB gain.");
                }

                // Gain is applied per frame via AudioSource.volume
                // (duck × linear(gain) × fades). AudioSource.volume clamps at
                // 1.0, so a pathological gain (> +20 dB with duck 0.10) is
                // baked into the samples instead — the gain itself is NEVER
                // capped (a clamp silently breaks equalization for outliers).
                float gainLinear = Mathf.Pow(10f, gainDb / 20f);
                if (gainLinear * duckVolume > 0.999f)
                {
                    Debug.Log($"[BgMusic] '{fileName}': gain {gainDb:+0.0} dB exceeds the " +
                              "volume-knob range — baking it into the samples.");
                    for (int i = 0; i < data.Length; i++) data[i] *= gainLinear;
                    clip.SetData(data, 0);
                    gainLinear = 1f;
                }

                onLoaded(new FolderTrack
                {
                    Clip       = clip,
                    FileName   = fileName,
                    Duration   = clip.length,
                    GainLinear = gainLinear,
                });
            }
        }

        // Top-level scan only, sorted — matching add_music.py's iterdir().
        static string[] ListPlayableTracks(string folder)
        {
            var playable = new List<string>();
            var dropped  = new List<string>();
            foreach (string f in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(PlayableExtensions, ext) >= 0) playable.Add(f);
                else if (Array.IndexOf(UnplayableMixerExts, ext) >= 0) dropped.Add(Path.GetFileName(f));
            }
            if (dropped.Count > 0)
                Debug.LogWarning("[BgMusic] Skipping tracks Unity can't decode " +
                                 "(the Python mixer accepted these, the in-app player can't): " +
                                 string.Join(", ", dropped));
            playable.Sort(StringComparer.OrdinalIgnoreCase);
            return playable.ToArray();
        }

        static void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // -------------------------------------------------------------------
        // Configuration refresh (scene load)
        // -------------------------------------------------------------------

        void RefreshFromPlayerPrefs()
        {
            // Stop and clear any in-flight playback before loading fresh.
            if (loadCoroutine != null) StopCoroutine(loadCoroutine);
            loadCoroutine = null;
            if (preloadCoroutine != null) StopCoroutine(preloadCoroutine);
            preloadCoroutine = null;
            if (source != null) source.Stop();
            if (source != null) source.clip = null;
            DisposeTracks();
            currentIndex    = 0;
            voiceAudio      = null;
            wasVoicePlaying = false;
            folderMode            = false;
            folderPreloadStarted  = false;
            folderPreloadComplete = false;
            folderIndex           = 0;

            // Folder mode wins over the legacy modes. Nothing is loaded here —
            // the playlist is planned against the take's voice length, which
            // isn't known until the assembled clip reaches
            // HybridAvatarSystem.ProcessWithExistingAudio (see BeginFolderPreload).
            string folderPref = PlayerPrefs.GetString(FolderPrefKey, "").Trim();
            if (folderPref.Length > 0)
            {
                folderMode = true;
                folderPath = folderPref;
                duckVolume = PlayerPrefs.GetFloat(FolderVolumePrefKey, FolderModeDuckVolume);
                Debug.Log($"[BgMusic] Folder mode configured: '{folderPath}' " +
                          $"(duck {duckVolume:F2}); waiting for the take's voice length.");
                return;
            }

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

            if (folderMode)
            {
                UpdateFolderPlayback(voicePlaying);
                return;
            }

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

        // -------------------------------------------------------------------
        // Folder mode — playback
        // -------------------------------------------------------------------

        // Sequential playlist under the voice, matching the ffmpeg filter the
        // Python mixer built: per track, 2 s linear fade-in and fade-out
        // (their envelopes multiply, exactly like chained afades), the whole
        // bed fades out over the last 2 s of the voice clip, and everything
        // sits at the constant duck volume after each track's equalization
        // gain. The voice gate provides the bed trim: music stops the instant
        // the voice stops, and never plays into trailing visuals.
        void UpdateFolderPlayback(bool voicePlaying)
        {
            if (!voicePlaying)
            {
                // The recorder stops and restarts the voice once the encoder
                // is warm — clearing the clip here is what re-syncs the bed to
                // that restart (it replays from the top of the playlist).
                if (source.isPlaying) source.Stop();
                if (source.clip != null) source.clip = null;
                folderIndex     = 0;
                wasVoicePlaying = false;
                return;
            }

            wasVoicePlaying = true;
            if (folderPlaylist.Count == 0) return;

            if (!source.isPlaying)
            {
                // First start (clip == null) or the current track just ended.
                int next = source.clip == null ? 0 : folderIndex + 1;
                if (next >= folderPlaylist.Count) return; // planned bed exhausted — stay silent
                folderIndex = next;
                PlaylistEntry entry = folderPlaylist[next];
                source.clip   = entry.Track.Clip;
                source.time   = 0f;
                source.volume = 0f; // the fade-in ramps it up from silence
                source.Play();
                Debug.Log($"[BgMusic] Bed {next + 1}/{folderPlaylist.Count}: " +
                          $"{entry.Track.FileName}{(entry.IsRepeat ? " (repeat)" : "")}");
            }

            FolderTrack cur = folderPlaylist[folderIndex].Track;
            float pos     = source.time;
            float fadeIn  = Mathf.Clamp01(pos / FadeSeconds);
            float fadeOut = Mathf.Clamp01((cur.Duration - pos) / FadeSeconds);

            float endFade  = 1f;
            float voiceLen = voiceAudio != null && voiceAudio.clip != null
                ? voiceAudio.clip.length
                : plannedVoiceLength;
            if (voiceLen > 0f && voiceAudio != null)
                endFade = Mathf.Clamp01((voiceLen - voiceAudio.time) / FadeSeconds);

            source.volume = Mathf.Clamp01(duckVolume * cur.GainLinear * fadeIn * fadeOut * endFade);
        }

        // Voice AudioSource discovery. The recorder often lives on the Main
        // Camera, which is INACTIVE during a take (its output is routed to the
        // capture texture), so FindAnyObjectByType would miss it — search the
        // systems that actually own the narration source with inactive objects
        // included, mirroring RecordingSession.TryDiscoverVoiceAudio. Search
        // lazily: when this player wakes up the scene may not have spawned
        // yet, and the reference is cleared on every RefreshFromPlayerPrefs.
        void EnsureVoiceAudioReference()
        {
            if (voiceAudio != null) return;

            var avatar = FindAnyObjectByType<HybridAvatarSystem>(FindObjectsInactive.Include);
            if (avatar != null && avatar.voiceAudio != null)
            {
                voiceAudio = avatar.voiceAudio;
                return;
            }

            var media = FindAnyObjectByType<MediaPresentationSystem>(FindObjectsInactive.Include);
            if (media != null && media.voiceAudio != null)
            {
                voiceAudio = media.voiceAudio;
                return;
            }

            var recorder = FindAnyObjectByType<CrossPlatformRecorder>(FindObjectsInactive.Include);
            if (recorder != null) voiceAudio = recorder.voiceAudio;
        }

        void DisposeTracks()
        {
            foreach (AudioClip c in tracks)
                if (c != null) Destroy(c);
            tracks.Clear();

            // Folder-mode clips are owned by the cache (playlist entries only
            // reference them), so destroying cache entries frees everything
            // exactly once.
            foreach (FolderTrack t in folderTrackCache.Values)
                if (t.Clip != null) Destroy(t.Clip);
            folderTrackCache.Clear();
            folderPlaylist.Clear();
        }

        // -------------------------------------------------------------------
        // Loudness normalization (RMS-based, peak-clamped) — legacy modes only
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

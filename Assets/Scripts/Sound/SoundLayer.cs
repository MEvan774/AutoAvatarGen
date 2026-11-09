using System;
using System.Collections.Generic;
using System.IO;
using MugsTech.Background;
using MugsTech.Tts;
using UnityEngine;

namespace MugsTech.Sound
{
    /// <summary>
    /// The sound-effect layer: a short tick or thud under the picture
    /// changes the script writes (a card arriving, a Stat's count-up, BigText
    /// lines, the big full-screen cards). No words. A card leaving, {Black} and
    /// the close-up stay silent.
    ///
    /// Main menu "Sound effects" (0 = off, default 60 %). Off is exactly
    /// today: nothing is built, no AudioSource exists, nothing is analysed.
    ///
    /// <list type="bullet">
    /// <item>The clips are synthesised (<see cref="SfxSynth"/>) when the take's
    /// voice is known and cached, each in three versions — starting at once, or
    /// after 60 or 120 ms of silence — so holding an effect off a word's attack
    /// is sample-exact instead of rounded to a frame.</item>
    /// <item>One 2D AudioSource with no mixer group, like the music: the listener
    /// hears it, so it's in the take and gets the loudness gain with the rest —
    /// and, while the layer is on, ListenerGain's soft −1 dBFS ceiling, for the
    /// rare effect that lands on a loud syllable.</item>
    /// <item>Requests in one frame are settled in LateUpdate by <see cref="SfxScheduler"/>
    /// (most important wins, one per 150 ms, off word attacks). Count clicks
    /// play the moment the digits change.</item>
    /// <item>Nothing sounds until the voice has really started — the recorder
    /// stops the first Play() and restarts it once the encoder is warm.</item>
    /// <item>Your own sound: <c>&lt;Media folder&gt;/SFX/&lt;name&gt;.wav</c> (names in
    /// <see cref="SfxSynth.FileName"/>) replaces an effect, levelled to the
    /// same peak. The log says where each effect came from.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public class SoundLayer : MonoBehaviour
    {
        public const string VolumePrefKey = "AutoAvatarGen.SfxVolume";
        public const float DefaultVolume = 0.6f;
        public const string OverrideFolderName = "SFX";

        /// <summary>Main menu "Sound effects": 0 (off) … 1; default 0.6.</summary>
        public static float Volume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume));
            set { PlayerPrefs.SetFloat(VolumePrefKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static bool Enabled => Volume > 0.001f;

        /// <summary>This take's effects: asked for (after the gates), and handed to the AudioSource.</summary>
        public static int Requested { get; private set; }
        public static int Played { get; private set; }

        /// <summary>True once a layer exists in this scene (never when the layer is off).</summary>
        public static bool HostExists => s_Instance != null;

        static SoundLayer s_Instance;
        static readonly Dictionary<SfxKind, AudioClip[]> s_Clips = new Dictionary<SfxKind, AudioClip[]>();

        AudioSource source;
        readonly SfxScheduler scheduler = new SfxScheduler();

        // ---------------------------------------------------------------------
        // Entry points
        // ---------------------------------------------------------------------

        /// <summary>A picture change that carries an effect. No-op when off or before the voice starts.</summary>
        public static void Fire(SfxKind k)
        {
            if (!Enabled || !TakeLoudness.VoiceStarted) return;
            Requested++;
            SoundLayer layer = Host();
            if (SfxRules.Exempt(k)) layer.PlayNow(k, 0, logIt: false);
            else layer.scheduler.Request(k);
        }

        /// <summary>
        /// Builds (or loads) every clip for this take and logs each one's source.
        /// Called by TakeLoudness as soon as the voice clip is known.
        /// </summary>
        public static void Prewarm()
        {
            if (!Enabled) return;
            Requested = 0;
            Played = 0;
            Host().scheduler.Clear();
            // Rebuilt per take, so a file dropped into SFX/ since the last one counts.
            foreach (AudioClip[] old in s_Clips.Values)
                foreach (AudioClip c in old)
                    if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            s_Clips.Clear();
            var lines = new List<string>();
            foreach (SfxKind k in (SfxKind[])Enum.GetValues(typeof(SfxKind)))
            {
                ClipsFor(k, out string from);
                lines.Add($"{SfxSynth.FileName(k)} ← {from}");
            }
            Debug.Log($"[Sfx] Sound effects at {Volume:P0}: " + string.Join("; ", lines) + ".");
        }

        static SoundLayer Host()
        {
            if (s_Instance != null) return s_Instance;
            var go = new GameObject("SoundLayer");
            s_Instance = go.AddComponent<SoundLayer>();
            // Made here, not in Awake, so the edit-mode self-tests see the same object.
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;   // 2D, no mixer group — routed like the music
            src.volume = 1f;         // the menu's level rides each PlayOneShot
            s_Instance.source = src;
            return s_Instance;
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        void LateUpdate()
        {
            if (scheduler.PendingCount == 0) return;
            float voiceTime = TakeLoudness.VoiceTime;
            if (scheduler.Resolve(Time.unscaledTime, voiceTime, TakeLoudness.WordOnsets, out SfxKind k, out float delay))
                PlayNow(k, DelayIndex(delay), logIt: true, voiceTime);
            else
                Debug.Log($"[Sfx] {SfxSynth.FileName(k)} skipped — within {SfxRules.MinSpacing * 1000f:F0} ms of the last effect.");
        }

        void PlayNow(SfxKind k, int delayIndex, bool logIt, float voiceTime = float.NaN)
        {
            AudioClip[] clips = ClipsFor(k, out _);
            AudioClip clip = clips[Mathf.Clamp(delayIndex, 0, clips.Length - 1)];
            if (clip == null) return;
            Played++;
            if (Application.isPlaying && source != null) source.PlayOneShot(clip, Volume);
            if (logIt)
            {
                float d = SfxRules.Delays[Mathf.Clamp(delayIndex, 0, SfxRules.Delays.Length - 1)];
                Debug.Log($"[Sfx] {(float.IsNaN(voiceTime) ? "" : $"t={voiceTime:F2}s ")}{SfxSynth.FileName(k)}" +
                          (d > 0f ? $" (+{d * 1000f:F0} ms, off a word's attack)" : ""));
            }
        }

        static int DelayIndex(float delay)
        {
            int best = 0;
            for (int i = 1; i < SfxRules.Delays.Length; i++)
                if (Mathf.Abs(SfxRules.Delays[i] - delay) < Mathf.Abs(SfxRules.Delays[best] - delay)) best = i;
            return best;
        }

        // ---------------------------------------------------------------------
        // Clips
        // ---------------------------------------------------------------------

        /// <summary>The clip an effect plays with <see cref="SfxRules.Delays"/>[<paramref name="delayIndex"/>] of lead-in.</summary>
        public static AudioClip Clip(SfxKind k, int delayIndex = 0)
        {
            AudioClip[] clips = ClipsFor(k, out _);
            return clips[Mathf.Clamp(delayIndex, 0, clips.Length - 1)];
        }

        /// <summary>The effect's three clips (0 / 60 / 120 ms of lead-in), built once per scene.</summary>
        static AudioClip[] ClipsFor(SfxKind k, out string from)
        {
            if (s_Clips.TryGetValue(k, out AudioClip[] cached) && cached != null && Array.TrueForAll(cached, c => c != null))
            {
                from = cached[0].name.EndsWith("(own)") ? "own file" : "synthesised";
                return cached;
            }

            float[] samples = null;  // interleaved
            int channels = 1, rate = OutputRate();
            from = "synthesised";
            string own = OverridePath(k);
            if (own != null)
            {
                try
                {
                    WavCodec.AudioBuffer b = WavCodec.Read(own);
                    if (b != null && b.Frames > 0)
                    {
                        samples = SfxSynth.Normalise(b.Samples, SfxSynth.PeakDbfs(k));
                        channels = b.Channels;
                        rate = b.SampleRate;
                        from = $"{own} ({b.Seconds:F2}s, levelled to {SfxSynth.PeakDbfs(k):F0} dBFS peak)";
                    }
                }
                catch (Exception e)
                {
                    from = $"synthesised (couldn't read {own}: {e.Message})";
                    samples = null;
                }
            }
            bool isOwn = samples != null;
            if (samples == null) { samples = SfxSynth.Make(k, rate); channels = 1; }

            var clips = new AudioClip[SfxRules.Delays.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                int lead = Mathf.RoundToInt(SfxRules.Delays[i] * rate) * channels;
                var data = new float[lead + samples.Length];
                Array.Copy(samples, 0, data, lead, samples.Length);
                AudioClip c = AudioClip.Create($"Sfx {SfxSynth.FileName(k)} +{SfxRules.Delays[i] * 1000f:F0}ms{(isOwn ? " (own)" : "")}",
                                               data.Length / channels, channels, rate, false);
                c.SetData(data, 0);
                clips[i] = c;
            }
            s_Clips[k] = clips;
            return clips;
        }

        static string OverridePath(SfxKind k)
        {
            string root = PlayerPrefs.GetString(MediaPresentationSystem.MediaRootFolderPrefKey, "");
            if (string.IsNullOrWhiteSpace(root)) return null;
            try
            {
                string p = Path.Combine(root, OverrideFolderName, SfxSynth.FileName(k) + ".wav");
                return File.Exists(p) ? p : null;
            }
            catch (ArgumentException) { return null; }
        }

        static int OutputRate()
        {
            int r = AudioSettings.outputSampleRate;
            return r > 0 ? r : 48000;
        }
    }
}

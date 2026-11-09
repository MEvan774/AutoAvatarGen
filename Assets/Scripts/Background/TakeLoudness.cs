using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// The take's level and its ending, settled before the recording starts.
    ///
    /// 1. LAST WORD — where the voice's last word ends on the stitched clip (the
    ///    last section keeps ElevenLabs' trailing silence, so the clip runs on
    ///    past it). The music bed holds through it and fades after it.
    /// 2. LOUDNESS (main menu "Normalise loudness", default on) — the mix the
    ///    AudioListener will hear is rebuilt offline exactly as it will play:
    ///    the voice at −3 dB into each stereo channel (Unity's pan for a mono 2D
    ///    source, measured on a real take), plus the planned folder-mode bed
    ///    through <see cref="TakeLoudnessMath.BedVolume"/> (the function the
    ///    player itself uses), plus the 2 s tail. Its BS.1770 integrated
    ///    loudness and 4× true peak give ONE gain that lands the mix on −14 LUFS
    ///    without passing −1 dBTP. The gain is applied live by a
    ///    <see cref="ListenerGain"/> filter on the AudioListener, ahead of
    ///    Evereal's recorder — no sample is rewritten.
    /// 3. TAIL — once the voice has run to its end the take is held open for
    ///    <c>MediaPresentationSystem.EndTailSeconds</c> (read through
    ///    MediaPresentationSystem.HasActiveTrailingVisual, which the recorder
    ///    already waits on), so the bed's fade is in the file.
    ///
    /// Started by MediaPresentationSystem / HybridAvatarSystem as soon as the
    /// voice clip is known (idempotent per clip); both wait on
    /// <see cref="Pending"/> before playback, like the music preload.
    /// AudioClip data can only be read on the main thread, so the mix is
    /// fetched in 20 s slices there and measured on a worker.
    /// </summary>
    public class TakeLoudness : MonoBehaviour
    {
        public const string NormalisePrefKey = "AutoAvatarGen.NormaliseLoudness";

        /// <summary>Main menu "Normalise loudness" (default on).</summary>
        public static bool NormaliseEnabled
        {
            get => PlayerPrefs.GetInt(NormalisePrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(NormalisePrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public sealed class Result
        {
            public bool Enabled;
            public bool Measured;
            public float InputLufs = float.NaN;          // the mix before the gain
            public float VoiceLufs = float.NaN;          // the voice alone, as it plays
            public float InputTruePeakDbtp = float.NaN;
            public float GainDb;
            public float TargetLufs = TakeLoudnessMath.TargetLufs;
            public float PredictedLufs = float.NaN;
            public float PredictedTruePeakDbtp = float.NaN;
            public bool Capped;
            public bool GainInFile = true;               // false if the gain stage ended up behind Evereal's recorder
            public string Note;
        }

        /// <summary>This take's result (stays after the scene, for the result JSON).</summary>
        public static Result Last { get; private set; }

        /// <summary>Seconds into the voice clip where the last word ends (NaN until measured).</summary>
        public static float LastWordEnd { get; private set; } = float.NaN;

        /// <summary>True while the take's analysis is still running — playback waits for it.</summary>
        public static bool Pending => s_Instance != null && s_Instance.pending;

        /// <summary>True for the tail after the voice has run to its end.</summary>
        public static bool InEndTail => s_Instance != null && s_Instance.InTail;

        /// <summary>
        /// True once the take's voice has really started playing. The recorder
        /// stops the first Play() and restarts it when the encoder is warm, so
        /// anything that fires before then is that pre-restart phantom.
        /// </summary>
        public static bool VoiceStarted => s_Instance != null &&
            (s_Instance.voiceSeen || (s_Instance.voiceSource != null && s_Instance.voiceSource.isPlaying));

        /// <summary>The voice's playback position, or NaN while it isn't playing.</summary>
        public static float VoiceTime => s_Instance != null && s_Instance.voiceSource != null && s_Instance.voiceSource.isPlaying
            ? s_Instance.voiceSource.time : float.NaN;

        /// <summary>Where words (and syllables) start on the voice clip, ascending — null unless the sound-effect layer is on.</summary>
        public static float[] WordOnsets { get; private set; }

        static TakeLoudness s_Instance;

        AudioClip voiceClip;
        AudioSource voiceSource;
        bool pending;

        bool voiceSeen;
        float lastVoiceTime;
        float voiceEndedAt = float.NaN;

        /// <summary>Starts the analysis for this take's voice (a second call for the same clip does nothing).</summary>
        public static void Begin(AudioClip voice, AudioSource source)
        {
            if (voice == null) return;
            if (s_Instance != null && s_Instance.voiceClip == voice) return;
            if (s_Instance == null)
                s_Instance = new GameObject("TakeLoudness").AddComponent<TakeLoudness>();
            s_Instance.StartFor(voice, source);
        }

        void StartFor(AudioClip voice, AudioSource source)
        {
            StopAllCoroutines();
            voiceClip = voice;
            voiceSource = source;
            pending = true;
            voiceSeen = false;
            voiceEndedAt = float.NaN;
            LastWordEnd = float.NaN;
            WordOnsets = null;
            Last = new Result { Enabled = NormaliseEnabled };
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        // ---------------------------------------------------------------------
        // The voice's end (drives the tail)
        // ---------------------------------------------------------------------

        void Update()
        {
            if (voiceSource == null || voiceClip == null) return;
            if (voiceSource.isPlaying)
            {
                voiceSeen = true;
                lastVoiceTime = voiceSource.time;
                voiceEndedAt = float.NaN;
            }
            else if (voiceSeen && float.IsNaN(voiceEndedAt) && lastVoiceTime >= voiceClip.length - 0.5f)
            {
                voiceEndedAt = Time.unscaledTime;
                Debug.Log($"[Loudness] Voice ended — holding the take {MediaPresentationSystem.EndTailSeconds:F1}s for the bed's fade.");
                if (MugsTech.Sound.SoundLayer.Enabled)
                    Debug.Log(ListenerGain.SoftenedSamples > 0
                        ? $"[Sfx] The −1 dBFS ceiling softened {ListenerGain.SoftenedSamples} sample(s) where an effect met a loud syllable."
                        : "[Sfx] No effect pushed the take past −1 dBFS — the ceiling never acted.");
            }
        }

        bool InTail => !float.IsNaN(voiceEndedAt) && Time.unscaledTime - voiceEndedAt < MediaPresentationSystem.EndTailSeconds;

        // ---------------------------------------------------------------------
        // Analysis
        // ---------------------------------------------------------------------

        IEnumerator Run()
        {
            LastWordEnd = MeasureLastWord(voiceClip);
            Debug.Log(float.IsNaN(LastWordEnd)
                ? "[Loudness] Couldn't find the last word in the voice — the bed fades at the clip's end."
                : $"[Loudness] Last word ends at {LastWordEnd:F2}s of {voiceClip.length:F2}s.");

            // The sound-effect layer: its clips, and the word attacks it keeps off.
            if (MugsTech.Sound.SoundLayer.Enabled)
            {
                MugsTech.Sound.SoundLayer.Prewarm();
                yield return MeasureOnsets();
            }

            if (!NormaliseEnabled)
            {
                SetGainStages(1f);
                Last.Note = "off";
                Debug.Log("[Loudness] Normalise loudness is off — the take plays at its own level.");
                pending = false;
                yield break;
            }

            float waited = 0f;
            while (waited < 35f && BackgroundMusicPlayer.FolderPreloadPending)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            List<BackgroundMusicPlayer.BedTrack> bed = BackgroundMusicPlayer.PlannedBed(out float duck);
            if (BackgroundMusicPlayer.LegacyMusicActive)
                Last.Note = "single-track/preset music isn't in the measurement";

            yield return MeasureMix(bed, duck);

            if (Last.Measured && voiceSeen)
            {
                // Playback gave up waiting: a gain switched on mid-take would be a jump.
                Last.Measured = false;
                Last.Note = "the measurement finished after the take had started — no gain applied";
            }
            if (Last.Measured)
            {
                TakeLoudnessMath.Gain g = TakeLoudnessMath.GainFor(Last.InputLufs, Last.InputTruePeakDbtp);
                Last.GainDb = g.Db;
                Last.Capped = g.Capped;
                Last.PredictedLufs = g.PredictedLufs;
                Last.PredictedTruePeakDbtp = g.PredictedTruePeakDbtp;
                SetGainStages(TakeLoudnessMath.DbToLinear(g.Db));
                Debug.Log($"[Loudness] Mix {Last.InputLufs:F1} LUFS (voice {Last.VoiceLufs:F1}, bed {(bed.Count > 0 ? $"{bed.Count} track(s) at {duck:F2}" : "none")}), " +
                          $"true peak {Last.InputTruePeakDbtp:F1} dBTP → gain {g.Db:+0.0;-0.0} dB → " +
                          $"{Last.PredictedLufs:F1} LUFS, {Last.PredictedTruePeakDbtp:F1} dBTP" +
                          (g.Capped ? $" (held back by the {TakeLoudnessMath.TruePeakCeilingDbtp:F0} dBTP ceiling)" : "") +
                          (Last.Note != null ? $" — {Last.Note}" : "") + ".");
            }
            else
            {
                SetGainStages(1f);
                Debug.LogWarning($"[Loudness] Couldn't measure the mix ({Last.Note}) — no gain applied.");
            }
            pending = false;
        }

        static float MeasureLastWord(AudioClip clip)
        {
            int frames = clip.samples, ch = clip.channels, rate = clip.frequency;
            int tail = Mathf.Min(frames, rate * 20);
            int offset = frames - tail;
            var buf = new float[tail * ch];
            if (!clip.GetData(buf, offset)) return float.NaN;
            float t = TakeLoudnessMath.LastSpeechEnd(buf, ch, rate);
            return float.IsNaN(t) ? float.NaN : (float)offset / rate + t;
        }

        IEnumerator MeasureOnsets()
        {
            int frames = voiceClip.samples, ch = voiceClip.channels, rate = voiceClip.frequency;
            var detector = new MugsTech.Sound.OnsetDetector(rate, ch);
            int block = rate * 20;
            for (int b0 = 0; b0 < frames; b0 += block)
            {
                int n = Mathf.Min(block, frames - b0);
                var buf = new float[n * ch];
                if (!voiceClip.GetData(buf, b0))
                {
                    Debug.LogWarning("[Sfx] The voice's samples can't be read — effects won't be held off word attacks.");
                    yield break;
                }
                detector.Add(buf, n);
                yield return null;
            }
            WordOnsets = detector.Finish();
            Debug.Log($"[Sfx] {WordOnsets.Length} word attacks found — effects are held up to " +
                      $"{MugsTech.Sound.SfxRules.Delays[MugsTech.Sound.SfxRules.Delays.Length - 1] * 1000f:F0} ms off them.");
        }

        /// <summary>One bed track's samples for a slice of the mix.</summary>
        public sealed class BedPiece
        {
            public float Start, Duration, GainLinear;  // seconds on the voice clock, track gain
            public int Rate, Channels, FirstFrame;
            public float[] Data;
        }

        IEnumerator MeasureMix(List<BackgroundMusicPlayer.BedTrack> bed, float duck)
        {
            int rate = voiceClip.frequency, vch = voiceClip.channels, voiceFrames = voiceClip.samples;
            float voiceVol = voiceSource != null ? voiceSource.volume : 1f;
            float lastWord = float.IsNaN(LastWordEnd) ? voiceClip.length : LastWordEnd;
            int outFrames = voiceFrames + Mathf.CeilToInt(MediaPresentationSystem.EndTailSeconds * rate);

            var starts = new float[bed.Count];
            for (int k = 1; k < bed.Count; k++) starts[k] = starts[k - 1] + bed[k - 1].Duration;

            var mixAcc = new LoudnessMeter.Accumulator(2, rate);
            var voiceAcc = new LoudnessMeter.Accumulator(2, rate);
            var peak = new TruePeakMeter(2);

            int blockFrames = rate * 20;
            for (int b0 = 0; b0 < outFrames; b0 += blockFrames)
            {
                int n = Mathf.Min(blockFrames, outFrames - b0);

                // Main thread: copy this slice's voice and bed samples out of the clips.
                int vFrames = Mathf.Clamp(voiceFrames - b0, 0, n);
                float[] v = null;
                if (vFrames > 0)
                {
                    v = new float[vFrames * vch];
                    if (!voiceClip.GetData(v, b0)) { Last.Note = "the voice clip's samples can't be read"; yield break; }
                }
                float t0 = (float)b0 / rate, t1 = (float)(b0 + n) / rate;
                var pieces = new List<BedPiece>();
                for (int k = 0; k < bed.Count; k++)
                {
                    BackgroundMusicPlayer.BedTrack tr = bed[k];
                    if (tr.Clip == null) continue;
                    float s = starts[k], e = starts[k] + tr.Duration;
                    float ov0 = Mathf.Max(s, t0), ov1 = Mathf.Min(e, t1);
                    if (ov1 <= ov0) continue;
                    int tRate = tr.Clip.frequency, tCh = tr.Clip.channels;
                    int first = Mathf.Max(0, Mathf.FloorToInt((ov0 - s) * tRate) - 1);
                    int count = Mathf.Min(tr.Clip.samples - first, Mathf.CeilToInt((ov1 - ov0) * tRate) + 4);
                    if (count <= 0) continue;
                    var data = new float[count * tCh];
                    if (!tr.Clip.GetData(data, first)) continue;
                    pieces.Add(new BedPiece { Start = s, Duration = tr.Duration, GainLinear = tr.GainLinear,
                                              Rate = tRate, Channels = tCh, FirstFrame = first, Data = data });
                }

                // Worker: build the slice of the mix and measure it.
                int start = b0;
                Task task = Task.Run(() => MixSlice(start, n, rate, v, vFrames, vch, voiceVol, pieces, duck, lastWord, mixAcc, voiceAcc, peak));
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted)
                {
                    Last.Note = "measuring failed: " + task.Exception?.GetBaseException().Message;
                    yield break;
                }
            }

            double lufs = mixAcc.IntegratedLufs();
            Last.InputLufs = (float)lufs;
            Last.VoiceLufs = (float)voiceAcc.IntegratedLufs();
            Last.InputTruePeakDbtp = (float)peak.PeakDb;
            Last.Measured = !double.IsNaN(lufs);
            if (!Last.Measured) Last.Note = "the mix is silent";
        }

        /// <summary>
        /// One slice of the mix as the listener hears it, fed to the meters
        /// (public so the self-tests can build a mix from known signals).
        /// </summary>
        public static void MixSlice(int b0, int n, int rate, float[] v, int vFrames, int vch, float voiceVol,
                                      List<BedPiece> pieces, float duck, float lastWord,
                                      LoudnessMeter.Accumulator mixAcc, LoudnessMeter.Accumulator voiceAcc, TruePeakMeter peak)
        {
            var mix = new float[n * 2];
            var voice = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float vl = 0f, vr = 0f;
                if (i < vFrames)
                {
                    if (vch == 1) { vl = vr = v[i] * voiceVol * TakeLoudnessMath.MonoToStereoGain; }
                    else { vl = v[i * vch] * voiceVol; vr = v[i * vch + 1] * voiceVol; }
                }
                float t = (float)(b0 + i) / rate;
                float bl = 0f, br = 0f;
                foreach (BedPiece p in pieces)
                {
                    float pos = t - p.Start;
                    if (pos < 0f || pos >= p.Duration) continue;
                    float vol = TakeLoudnessMath.BedVolume(duck, p.GainLinear, pos, p.Duration, t - lastWord);
                    if (vol <= 0f) break;
                    float fpos = pos * p.Rate - p.FirstFrame;
                    int j = (int)fpos;
                    int frames = p.Data.Length / p.Channels;
                    if (j < 0 || j >= frames) break;
                    float f = fpos - j;
                    int j1 = Math.Min(j + 1, frames - 1);
                    if (p.Channels == 1)
                    {
                        float s = (p.Data[j] + (p.Data[j1] - p.Data[j]) * f) * vol * TakeLoudnessMath.MonoToStereoGain;
                        bl = br = s;
                    }
                    else
                    {
                        int c = p.Channels;
                        bl = (p.Data[j * c] + (p.Data[j1 * c] - p.Data[j * c]) * f) * vol;
                        br = (p.Data[j * c + 1] + (p.Data[j1 * c + 1] - p.Data[j * c + 1]) * f) * vol;
                    }
                    break;
                }
                mix[i * 2] = vl + bl; mix[i * 2 + 1] = vr + br;
                voice[i * 2] = vl; voice[i * 2 + 1] = vr;
            }
            mixAcc.Add(mix, n);
            voiceAcc?.Add(voice, n);
            peak.Add(mix, n);
        }

        // A ListenerGain on every AudioListener in the scene: only the one the
        // engine actually listens through runs, and Evereal appends its
        // recorder to that listener's object when capture starts — after ours,
        // so the file gets the gain. ListenerGain checks that order once the
        // recorder exists and says so loudly if it isn't.
        static void SetGainStages(float linear)
        {
            // The sound-effect layer needs the soft ceiling even when there is no gain.
            bool ceiling = MugsTech.Sound.SoundLayer.Enabled;
            ListenerGain.ResetSoftened();
            foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ListenerGain g = l.GetComponent<ListenerGain>();
                if (g == null)
                {
                    if (Mathf.Approximately(linear, 1f) && !ceiling) continue;
                    g = l.gameObject.AddComponent<ListenerGain>();
                }
                g.GainLinear = linear;
                g.Ceiling = ceiling;
            }
        }

        internal static void ReportGainNotInFile(string why)
        {
            if (Last != null) { Last.GainInFile = false; Last.Note = why; }
        }
    }
}

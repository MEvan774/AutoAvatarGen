using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MugsTech.Background;

/// <summary>
/// Tier 3 step 1 — the take's level and its ending: the streaming BS.1770
/// meter against the batch one and the spec's reference sine, the 4× true
/// peak, the gain on synthetic −20 / −10 LUFS clips (re-measured after it),
/// the true-peak ceiling, the mix model (mono voice at −3 dB per channel, the
/// bed under it), the last word and the bed's fade after it, and the music
/// volume's range.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Audio/Run Loudness Self-Tests")]
    public static void RunLoudness()
    {
        var log = new Log();
        RunLoudnessGroup(log);
        log.Report();
    }

    static void RunLoudnessGroup(Log log)
    {
        Loudness_StreamingMatchesBatch(log);
        Loudness_ReferenceSine(log);
        Loudness_TruePeak(log);
        Loudness_GainOnSyntheticClips(log);
        Loudness_TruePeakCeiling(log);
        Loudness_MixModel(log);
        Loudness_LastWordAndEndFade(log);
        Loudness_MusicVolume(log);
    }

    // Deterministic noise, roughly speech-shaped (a little low-passed).
    static float[] Noise(int frames, int channels, float amp, int seed)
    {
        var rng = new System.Random(seed);
        var d = new float[frames * channels];
        float prev = 0f;
        for (int i = 0; i < frames; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1);
            prev = prev * 0.6f + x * 0.4f;
            for (int c = 0; c < channels; c++) d[i * channels + c] = prev * amp;
        }
        return d;
    }

    static double Measure(float[] d, int channels, int rate)
    {
        var acc = new LoudnessMeter.Accumulator(channels, rate);
        acc.Add(d, d.Length / channels);
        return acc.IntegratedLufs();
    }

    static void Loudness_StreamingMatchesBatch(Log log)
    {
        const int rate = 44100, ch = 2;
        float[] d = Noise(rate * 9, ch, 0.3f, 7);
        // Quiet stretches, so the gates have something to do.
        for (int i = rate * 3 * ch; i < rate * 4 * ch; i++) d[i] *= 0.001f;
        double batch = LoudnessMeter.MeasureIntegratedLufs(d, ch, rate);

        var acc = new LoudnessMeter.Accumulator(ch, rate);
        var rng = new System.Random(3);
        int pos = 0, frames = d.Length / ch;
        while (pos < frames)
        {
            int n = Math.Min(frames - pos, rng.Next(1, 30000));
            var piece = new float[n * ch];
            Array.Copy(d, pos * ch, piece, 0, n * ch);
            acc.Add(piece, n);
            pos += n;
        }
        log.Near((float)batch, (float)acc.IntegratedLufs(), 1e-6f,
            $"loudness: the streaming meter, fed in random pieces, matches the batch meter ({batch:F3} LUFS)");
    }

    static void Loudness_ReferenceSine(Log log)
    {
        // BS.1770: a 0 dBFS 997 Hz sine in one front channel reads −3.01 LKFS.
        const int rate = 48000;
        var mono = new float[rate * 5];
        for (int i = 0; i < mono.Length; i++) mono[i] = Mathf.Sin(2f * Mathf.PI * 997f * i / rate);
        log.Near(-3.01f, (float)Measure(mono, 1, rate), 0.05f, "loudness: a full-scale 997 Hz sine in one channel reads −3.01 LUFS");
        var stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) { stereo[2 * i] = mono[i]; stereo[2 * i + 1] = mono[i]; }
        log.Near(0f, (float)Measure(stereo, 2, rate), 0.05f, "…and in both channels 0.0 LUFS");
        float[] at441 = new float[44100 * 5];
        for (int i = 0; i < at441.Length; i++) at441[i] = Mathf.Sin(2f * Mathf.PI * 997f * i / 44100f);
        log.Near(-3.01f, (float)Measure(at441, 1, 44100), 0.05f, "…the same at 44.1 kHz (the filters follow the rate)");
    }

    static void Loudness_TruePeak(Log log)
    {
        // A sine at a quarter of the rate, 45° off: every sample at ±0.707, the wave peaks at 1.0 between them.
        const int rate = 48000;
        var d = new float[rate];
        for (int i = 0; i < d.Length; i++) d[i] = Mathf.Sin(Mathf.PI / 2f * i + Mathf.PI / 4f);
        var tp = new TruePeakMeter(1);
        tp.Add(d, d.Length);
        float sampleDb = 20f * Mathf.Log10(0.70710678f);
        log.Near(0f, (float)tp.PeakDb, 0.3f, $"true peak: the inter-sample peak is found ({tp.PeakDb:F2} dBTP; samples at {sampleDb:F2} dBFS)");

        var slow = new float[rate];
        for (int i = 0; i < slow.Length; i++) slow[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 100f * i / rate);
        var tp2 = new TruePeakMeter(1);
        tp2.Add(slow, slow.Length);
        log.Near(20f * Mathf.Log10(0.5f), (float)tp2.PeakDb, 0.05f, "true peak: a slow sine's true peak is its sample peak");

        // Fed in pieces = fed at once.
        var tp3 = new TruePeakMeter(1);
        for (int p = 0; p < d.Length; p += 777)
        {
            int n = Math.Min(777, d.Length - p);
            var piece = new float[n];
            Array.Copy(d, p, piece, 0, n);
            tp3.Add(piece, n);
        }
        log.Near((float)tp.PeakDb, (float)tp3.PeakDb, 1e-5f, "true peak: fed in pieces, the same result");
    }

    static float[] AtLufs(float target, int rate, int seed, out double measured)
    {
        float[] d = Noise(rate * 8, 2, 0.1f, seed);
        double now = Measure(d, 2, rate);
        float k = TakeLoudnessMath.DbToLinear((float)(target - now));
        for (int i = 0; i < d.Length; i++) d[i] *= k;
        measured = Measure(d, 2, rate);
        return d;
    }

    static void Loudness_GainOnSyntheticClips(Log log)
    {
        const int rate = 44100;
        foreach (float level in new[] { -20f, -10f })
        {
            float[] d = AtLufs(level, rate, level < -15f ? 11 : 12, out double measured);
            var tp = new TruePeakMeter(2);
            tp.Add(d, d.Length / 2);
            TakeLoudnessMath.Gain g = TakeLoudnessMath.GainFor(measured, tp.PeakDb);
            log.Near(level, (float)measured, 0.05f, $"gain: the synthetic clip measures {level:F0} LUFS");
            log.Near(-14f - level, g.Db, 0.05f, $"gain: {level:F0} LUFS needs {-14f - level:+0;-0} dB");
            log.IsTrue(!g.Capped, $"gain: {level:F0} LUFS isn't held back by the ceiling (true peak {tp.PeakDb:F1} dBTP)");

            ListenerGain.Apply(d, TakeLoudnessMath.DbToLinear(g.Db));
            float after = (float)Measure(d, 2, rate);
            log.Near(-14f, after, 0.05f, $"gain: re-measured after the gain, the {level:F0} LUFS clip is at −14 LUFS ({after:F2})");
            log.Near(g.PredictedLufs, after, 0.05f, "…exactly as predicted");
        }
    }

    static void Loudness_TruePeakCeiling(Log log)
    {
        TakeLoudnessMath.Gain g = TakeLoudnessMath.GainFor(-20.0, -2.0);
        log.IsTrue(g.Capped, "ceiling: a quiet mix with a high peak is held back");
        log.Near(1f, g.Db, 1e-4f, "…to the gain that puts its true peak on −1 dBTP (+1 dB, not +6)");
        log.Near(-1f, g.PredictedTruePeakDbtp, 1e-4f, "…true peak −1 dBTP");
        log.Near(-19f, g.PredictedLufs, 1e-4f, "…and it says the mix ends at −19 LUFS");

        // On a real signal: −20 LUFS noise with a few clicks near full scale.
        const int rate = 44100;
        float[] d = AtLufs(-20f, rate, 21, out double measured);
        for (int k = 1; k <= 5; k++) { int i = k * rate; d[2 * i] = 0.79f; d[2 * i + 1] = -0.79f; }
        double lufs = Measure(d, 2, rate);
        var tp = new TruePeakMeter(2);
        tp.Add(d, d.Length / 2);
        TakeLoudnessMath.Gain g2 = TakeLoudnessMath.GainFor(lufs, tp.PeakDb);
        ListenerGain.Apply(d, TakeLoudnessMath.DbToLinear(g2.Db));
        var after = new TruePeakMeter(2);
        after.Add(d, d.Length / 2);
        log.IsTrue(g2.Capped && after.PeakDb <= -1.0 + 0.01, $"ceiling: after the capped gain the true peak is {after.PeakDb:F2} dBTP (≤ −1)");

        TakeLoudnessMath.Gain none = TakeLoudnessMath.GainFor(double.NaN, double.NegativeInfinity);
        log.IsTrue(none.Unmeasured && none.Db == 0f, "gain: a silent mix gets no gain");
        log.Near(TakeLoudnessMath.MaxBoostDb, TakeLoudnessMath.GainFor(-60.0, -50.0).Db, 1e-4f, "gain: never more than +12 dB");
    }

    static void Loudness_MixModel(Log log)
    {
        // A mono voice through a 2D AudioSource lands at −3 dB in each channel —
        // so the stereo mix has the voice's own loudness (measured on a real take).
        const int rate = 44100;
        float[] voice = Noise(rate * 6, 1, 0.2f, 31);
        double monoLufs = Measure(voice, 1, rate);
        var mixAcc = new LoudnessMeter.Accumulator(2, rate);
        var voiceAcc = new LoudnessMeter.Accumulator(2, rate);
        var tp = new TruePeakMeter(2);
        TakeLoudness.MixSlice(0, voice.Length, rate, voice, voice.Length, 1, 1f, new List<TakeLoudness.BedPiece>(),
                              0f, 100f, mixAcc, voiceAcc, tp);
        log.Near((float)monoLufs, (float)mixAcc.IntegratedLufs(), 0.02f,
            "mix: the mono voice at −3 dB per channel has its own loudness in the stereo mix");

        // The bed at the duck, under it: louder than the voice alone, by the energy it adds.
        float[] bedData = Noise(rate * 10, 2, 0.3f, 32);
        var piece = new TakeLoudness.BedPiece
        {
            Start = 0f, Duration = 10f, GainLinear = 1f, Rate = rate, Channels = 2, FirstFrame = 0, Data = bedData,
        };
        var withBed = new LoudnessMeter.Accumulator(2, rate);
        TakeLoudness.MixSlice(0, voice.Length, rate, voice, voice.Length, 1, 1f, new List<TakeLoudness.BedPiece> { piece },
                              0.18f, 100f, withBed, null, new TruePeakMeter(2));
        double mixed = withBed.IntegratedLufs();
        log.IsTrue(mixed > monoLufs + 0.05 && mixed < monoLufs + 3.0,
            $"mix: the bed at 0.18 adds to the voice ({monoLufs:F2} → {mixed:F2} LUFS)");
    }

    static void Loudness_LastWordAndEndFade(Log log)
    {
        // A 60 s "voice" whose speech stops at 58.7 s, then 1.3 s of ElevenLabs-style near-silence.
        const int rate = 44100;
        const float len = 60f, lastWord = 58.7f;
        int tailFrames = rate * 20;
        var tail = new float[tailFrames];
        var rng = new System.Random(5);
        for (int i = 0; i < tailFrames; i++)
        {
            float t = len - 20f + (float)i / rate;
            tail[i] = t < lastWord ? 0.2f * Mathf.Sin(2f * Mathf.PI * 220f * t) : (float)(rng.NextDouble() * 2 - 1) * 0.0002f;
        }
        float found = TakeLoudnessMath.LastSpeechEnd(tail, 1, rate);
        log.Near(lastWord, len - 20f + found, 0.03f, $"ending: the last word is found at {len - 20f + found:F2} s (the clip runs to {len:F0} s)");
        log.IsTrue(float.IsNaN(TakeLoudnessMath.LastSpeechEnd(new float[rate], 1, rate)), "ending: pure silence has no last word");

        // The bed: full duck through the last word, then 1.5 s down to nothing.
        float Bed(float t) => TakeLoudnessMath.BedVolume(0.18f, 1f, 30f + t, 200f, t - lastWord);
        log.Near(0.18f, Bed(lastWord - 1.0f), 1e-5f, "ending: a second before the last word the bed is at the full duck (no early fade)");
        log.Near(0.18f, Bed(lastWord), 1e-5f, "…still full on the last word");
        log.Near(0.09f, Bed(lastWord + 0.75f), 1e-4f, "…half 0.75 s after it");
        log.Near(0f, Bed(lastWord + 1.5f), 1e-5f, "…gone 1.5 s after it");
        log.IsTrue(lastWord + TakeLoudnessMath.EndFadeSeconds <= len + MediaPresentationSystem.EndTailSeconds,
            "ending: the fade is over before the 2 s tail ends, so it is all in the file");
        log.IsTrue(TakeLoudnessMath.EndFadeSeconds <= MediaPresentationSystem.EndTailSeconds,
            "ending: the tail is never shorter than the fade, even when the last word is the clip's last sample");
        log.Near(2.0f, MediaPresentationSystem.EndTailSeconds, 1e-6f, "ending: the take keeps recording 2.0 s after the voice");

        // Track fades still work as before.
        log.Near(0.09f, TakeLoudnessMath.BedVolume(0.18f, 1f, 1f, 200f, -100f), 1e-4f, "bed: halfway through a track's 2 s fade-in");
        log.Near(0.09f, TakeLoudnessMath.BedVolume(0.18f, 1f, 199f, 200f, -100f), 1e-4f, "bed: halfway through its fade-out");
    }

    static void Loudness_MusicVolume(Log log)
    {
        log.Near(0.18f, BackgroundMusicPlayer.FolderModeDuckVolume, 1e-6f, "music: the default duck is 0.18");
        bool had = PlayerPrefs.HasKey(BackgroundMusicPlayer.FolderVolumePrefKey);
        float saved = PlayerPrefs.GetFloat(BackgroundMusicPlayer.FolderVolumePrefKey, 0f);
        try
        {
            PlayerPrefs.DeleteKey(BackgroundMusicPlayer.FolderVolumePrefKey);
            log.Near(0.18f, BackgroundMusicPlayer.MusicVolume, 1e-6f, "music: with nothing saved the volume is 0.18");
            BackgroundMusicPlayer.MusicVolume = 0.9f;
            log.Near(0.40f, BackgroundMusicPlayer.MusicVolume, 1e-6f, "music: the field stops at 0.40");
            BackgroundMusicPlayer.MusicVolume = 0.01f;
            log.Near(0.05f, BackgroundMusicPlayer.MusicVolume, 1e-6f, "…and at 0.05");
        }
        finally
        {
            if (had) PlayerPrefs.SetFloat(BackgroundMusicPlayer.FolderVolumePrefKey, saved);
            else PlayerPrefs.DeleteKey(BackgroundMusicPlayer.FolderVolumePrefKey);
            PlayerPrefs.Save();
        }

        var data = new float[] { 0.5f, -0.25f, 1f };
        ListenerGain.Apply(data, 2f);
        log.IsTrue(data[0] == 1f && data[1] == -0.5f && data[2] == 2f, "listener gain: one multiplier on every sample");
        var same = new float[] { 0.5f };
        ListenerGain.Apply(same, 1f);
        log.IsTrue(same[0] == 0.5f, "listener gain: unity leaves the mix untouched");

        // The soft ceiling (on while the sound-effect layer is).
        var under = new float[] { 0.5f, -0.88f, 0.89f, 0f };
        int bent = ListenerGain.Apply(under, 1f, true);
        log.IsTrue(bent == 0 && under[0] == 0.5f && under[1] == -0.88f && under[2] == 0.89f && under[3] == 0f,
            "ceiling: nothing under −1 dBFS is touched (the voice and bed never reach it after the gain)");
        var over = new float[] { 0.95f, -1.4f, 3f, 1.4f };
        bent = ListenerGain.Apply(over, 1f, true);
        bool inside = true;
        foreach (float v in over) inside &= Mathf.Abs(v) > ListenerGain.CeilingKnee && Mathf.Abs(v) <= 1f;
        log.IsTrue(bent == 4 && inside && over[1] < 0f && over[1] == -over[3], "ceiling: a peak past −1 dBFS is bent toward 0 dBFS, never past it, sign kept");
        log.IsTrue(over[0] < over[3] && over[3] < over[2], "ceiling: louder stays louder (monotone)");
        float k = ListenerGain.CeilingKnee;
        log.Near(1e-3f, ListenerGain.Soft(k + 1e-3f) - k, 2e-5f, "ceiling: slope 1 at the knee — no corner");
        var both = new float[] { 0.8f };
        ListenerGain.Apply(both, 1.2f, true);
        log.IsTrue(both[0] > k && both[0] < 0.96f, $"ceiling: applied after the gain (0.8 × 1.2 → {both[0]:F3})");
    }
}

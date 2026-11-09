using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MugsTech.Sound;

/// <summary>
/// Tier 3 step 2 — the sound-effect layer: every synthesised clip finite,
/// peak-normalised to its table level and no longer than 400 ms; the 150 ms
/// rate limit (count clicks exempt); one effect per frame, the most important;
/// the hold off a word's attack; the onset finder; the count-up's ≤12 clicks;
/// the default sound per tag; and volume 0 → no AudioSource, no activity.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Audio/Run Sound Effect Self-Tests")]
    public static void RunSound()
    {
        var log = new Log();
        RunSoundGroup(log);
        log.Report();
    }

    static void RunSoundGroup(Log log)
    {
        Sfx_ClipsFiniteNormalisedShort(log);
        Sfx_RateLimiter(log);
        Sfx_SchedulerOnePerFrame(log);
        Sfx_WordAttackDelay(log);
        Sfx_OnsetDetector(log);
        Sfx_CountUpClicks(log);
        Sfx_DefaultSounds(log);
        Sfx_VolumeZeroIsSilent(log);
        Sfx_PrewarmedClips(log);
    }

    static float PeakOf(float[] s)
    {
        float p = 0f;
        foreach (float v in s) p = Mathf.Max(p, Mathf.Abs(v));
        return p;
    }

    static int ZeroCrossings(float[] s)
    {
        int n = 0;
        for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0f) != (s[i] < 0f)) n++;
        return n;
    }

    static void Sfx_ClipsFiniteNormalisedShort(Log log)
    {
        foreach (int rate in new[] { 44100, 48000 })
            foreach (SfxKind k in (SfxKind[])Enum.GetValues(typeof(SfxKind)))
            {
                float[] s = SfxSynth.Make(k, rate);
                bool finite = true;
                foreach (float v in s) if (float.IsNaN(v) || float.IsInfinity(v)) { finite = false; break; }
                log.IsTrue(finite, $"sfx: {k} at {rate} Hz is finite");
                log.IsTrue(s.Length > 0 && s.Length <= SfxSynth.MaxSeconds * rate,
                    $"sfx: {k} at {rate} Hz lasts 0–400 ms (got {s.Length * 1000f / rate:F0} ms)");
                log.Near(SfxSynth.PeakDbfs(k), 20f * Mathf.Log10(PeakOf(s)), 0.01f, $"sfx: {k} at {rate} Hz peaks at its table level");
                float[] again = SfxSynth.Make(k, rate);
                bool same = again.Length == s.Length;
                for (int i = 0; same && i < s.Length; i++) same = again[i] == s[i];
                log.IsTrue(same, $"sfx: {k} at {rate} Hz is the same every time (seeded)");
            }

        const int r = 48000;
        log.Near(60f, SfxSynth.Make(SfxKind.CardIn, r).Length * 1000f / r, 1f, "sfx: the card tick is 60 ms");
        log.Near(150f, SfxSynth.Make(SfxKind.BigTextLine, r).Length * 1000f / r, 1f, "sfx: the BigText thud is 150 ms");
        // Only four: a card leaving, {Black} and the close-up were taken out
        // after the 2026-11-09 render (by request) — they stay silent.
        log.AreEqual(4, System.Enum.GetValues(typeof(SfxKind)).Length,
            "sfx: four effects — card in, count click, BigText line, big card in");
        // The first table (−16…−28 dBFS) was inaudible in the 2026-11-09 render.
        // At the default 60 % the ticks and the thud must peak at −14 dBFS or
        // louder (the clicks −22), and nothing at full level louder than −8.
        foreach (SfxKind k in (SfxKind[])System.Enum.GetValues(typeof(SfxKind)))
        {
            float atDefault = SfxSynth.PeakDbfs(k) + 20f * Mathf.Log10(SoundLayer.DefaultVolume);
            float floor = k == SfxKind.CountClick ? -22f : -14f;
            log.IsTrue(atDefault >= floor && SfxSynth.PeakDbfs(k) <= -8f,
                $"sfx: {k} peaks at {atDefault:F1} dBFS at the default 60 % (audible: ≥ {floor:F0}), {SfxSynth.PeakDbfs(k):F0} at full");
        }

        // Pitch: the big card's tick an octave under the card's.
        int zTick = ZeroCrossings(SfxSynth.Make(SfxKind.CardIn, r));
        int zBig = ZeroCrossings(SfxSynth.Make(SfxKind.BigCardIn, r));
        log.IsTrue(zBig < zTick * 0.75f, $"sfx: the big card's tick sits lower than the card's ({zBig} vs {zTick} crossings)");
    }

    static void Sfx_RateLimiter(Log log)
    {
        var l = new SfxRateLimiter();
        log.IsTrue(l.Allow(SfxKind.CardIn, 1.00f), "rate limit: the first effect plays");
        log.IsTrue(!l.Allow(SfxKind.CardIn, 1.10f), "rate limit: one 100 ms later is dropped");
        log.IsTrue(!l.Allow(SfxKind.BigTextLine, 1.149f), "rate limit: even an important one inside 150 ms is dropped");
        log.IsTrue(l.Allow(SfxKind.CardIn, 1.15f), "rate limit: 150 ms after the last played one is fine");
        log.IsTrue(l.Allow(SfxKind.CountClick, 1.16f) && l.Allow(SfxKind.CountClick, 1.17f) && l.Allow(SfxKind.CountClick, 1.171f),
            "rate limit: count clicks are exempt");
        log.IsTrue(!l.Allow(SfxKind.CardIn, 1.25f), "rate limit: count clicks don't restart the window, nor open it");
        log.IsTrue(l.Allow(SfxKind.CardIn, 1.30f), "rate limit: the next effect 150 ms on plays");
    }

    static void Sfx_SchedulerOnePerFrame(Log log)
    {
        var s = new SfxScheduler();
        s.Request(SfxKind.CardIn);
        s.Request(SfxKind.BigCardIn);
        bool played = s.Resolve(5f, float.NaN, null, out SfxKind k, out float d);
        log.IsTrue(played && k == SfxKind.BigCardIn && d == 0f, "sfx: a side card and a big card in one frame → only the big card's tick");
        log.AreEqual(0, s.PendingCount, "sfx: a frame's requests are settled at once");

        s.Request(SfxKind.CardIn);
        played = s.Resolve(5.05f, float.NaN, null, out k, out _);
        log.IsTrue(!played && k == SfxKind.CardIn, "sfx: a request inside 150 ms is dropped (and named)");
        log.IsTrue(!s.Resolve(5.06f, float.NaN, null, out _, out _), "sfx: nothing requested → nothing plays");

        s.Request(SfxKind.CardIn);
        s.Request(SfxKind.BigTextLine);
        s.Request(SfxKind.BigCardIn);
        played = s.Resolve(6f, float.NaN, null, out k, out _);
        log.IsTrue(played && k == SfxKind.BigTextLine, "sfx: a BigText line outranks everything in its frame");

        // The rate limit counts from when the effect will actually start (after its hold).
        var onsets = new[] { 7.00f };
        s.Request(SfxKind.CardIn);
        played = s.Resolve(7.02f, 7.02f, onsets, out _, out d);   // on the attack → held 60 ms → starts 7.08
        log.IsTrue(played && Mathf.Approximately(d, 0.06f), "sfx: an effect on a word's attack is held 60 ms");
        s.Request(SfxKind.CardIn);
        log.IsTrue(!s.Resolve(7.20f, float.NaN, null, out _, out _), "sfx: the limit counts from the held start (7.08 → 7.20 is too soon)");
    }

    static void Sfx_WordAttackDelay(Log log)
    {
        var onsets = new[] { 1.0f, 2.0f, 2.1f };
        log.AreEqual(0f, SfxRules.DelayFor(float.NaN, onsets), "attack: voice not playing → no hold");
        log.AreEqual(0f, SfxRules.DelayFor(1.0f, null), "attack: no onsets → no hold");
        log.AreEqual(0f, SfxRules.DelayFor(0.5f, onsets), "attack: clear of every word → no hold");
        log.AreEqual(0f, SfxRules.DelayFor(1.96f, onsets), "attack: 40 ms before an onset is still clear");
        log.AreEqual(0.06f, SfxRules.DelayFor(1.02f, onsets), "attack: on an attack → held 60 ms");
        log.AreEqual(0.12f, SfxRules.DelayFor(0.98f, onsets), "attack: still on it after 60 ms → held 120 ms");
        log.AreEqual(0.06f, SfxRules.DelayFor(2.00f, onsets), "attack: held into the gap between two close syllables");

        var dense = new List<float>();
        for (float t = 0f; t < 2f; t += 0.06f) dense.Add(t);
        float dd = SfxRules.DelayFor(0.6f, dense.ToArray());
        log.IsTrue(Array.IndexOf(SfxRules.Delays, dd) >= 0, "attack: no clear spot → one of 0/60/120 ms, never longer");

        // Speech-like: an onset every 250 ms. Every moment finds a clear start.
        var steady = new List<float>();
        for (float t = 0.2f; t < 6f; t += 0.25f) steady.Add(t);
        float[] st = steady.ToArray();
        bool allClear = true;
        float worst = 0f;
        for (int i = 0; i < 5000; i++)
        {
            float t = i * 0.001f;
            float start = t + SfxRules.DelayFor(t, st);
            worst = Mathf.Max(worst, start - t);
            foreach (float o in st)
                if (start >= o - SfxRules.AttackLead && start <= o + SfxRules.AttackHold) { allClear = false; break; }
        }
        log.IsTrue(allClear, "attack: with a word every 250 ms, every effect starts clear of every attack");
        log.IsTrue(worst <= 0.1201f, $"attack: and is never held more than 120 ms (worst {worst * 1000f:F0} ms)");
    }

    static void Sfx_OnsetDetector(Log log)
    {
        const int rate = 44100;
        foreach (int ch in new[] { 1, 2 })
        {
            int frames = rate * 3;
            var d = new float[frames * ch];
            var rng = new System.Random(5);
            void Burst(float at, float len, float amp)
            {
                int a = (int)(at * rate), b = Math.Min(frames, (int)((at + len) * rate));
                for (int i = a; i < b; i++)
                {
                    float v = (float)(rng.NextDouble() * 2 - 1) * amp;
                    for (int c = 0; c < ch; c++) d[i * ch + c] = v;
                }
            }
            Burst(0f, 3f, 0.001f);            // room tone
            Burst(0.50f, 0.20f, 0.3f);        // a word
            Burst(0.60f, 0.10f, 0.6f);        // +6 dB inside it — not a new attack
            Burst(1.20f, 0.17f, 0.3f);        // another, then a 20 ms dip…
            Burst(1.39f, 0.15f, 0.3f);        // …and back: 9 dB over the last 40 ms → a new syllable
            Burst(2.00f, 0.02f, 0.3f);        // a short click…
            Burst(2.05f, 0.10f, 0.3f);        // …and a word 50 ms after it, inside the 60 ms refractory

            var det = new OnsetDetector(rate, ch);
            // Fed in uneven slices, like the 20 s blocks off the clip.
            int pos = 0;
            foreach (int n in new[] { 1000, 30000, 7, rate, frames })
            {
                int take = Math.Min(n, frames - pos);
                if (take <= 0) break;
                var piece = new float[take * ch];
                Array.Copy(d, pos * ch, piece, 0, take * ch);
                det.Add(piece, take);
                pos += take;
            }
            float[] on = det.Finish();
            string got = string.Join(", ", Array.ConvertAll(on, o => o.ToString("F3")));
            log.AreEqual(5, on.Length, $"onsets ({ch} ch): two words, a syllable after a dip, a click and the word after it — " +
                                       $"not the +6 dB swell (got {got})");
            if (on.Length == 5)
            {
                log.Near(0.50f, on[0], 0.006f, $"onsets ({ch} ch): the first word's attack");
                log.Near(1.20f, on[1], 0.006f, $"onsets ({ch} ch): the second word's attack");
                log.Near(1.39f, on[2], 0.006f, $"onsets ({ch} ch): the syllable after a 20 ms dip");
                log.Near(2.00f, on[3], 0.006f, $"onsets ({ch} ch): the click");
                log.IsTrue(on[4] - on[3] >= OnsetDetector.RefractorySeconds - 0.006f && on[4] <= 2.065f,
                    $"onsets ({ch} ch): the word 50 ms after the click is reported as the 60 ms refractory ends ({on[4]:F3})");
            }
        }
        log.AreEqual(0, new OnsetDetector(rate, 1).Finish().Length, "onsets: nothing fed → none");
        var silent = new OnsetDetector(rate, 1);
        silent.Add(new float[rate], rate);
        log.AreEqual(0, silent.Finish().Length, "onsets: digital silence → none");
    }

    static void Sfx_CountUpClicks(Log log)
    {
        (int clicks, string lastClicked, float minGap) Run(float target, float duration, int fps, Func<float, string> fmt)
        {
            var c = new CountUpClicker(duration);
            int n = 0;
            string last = null;
            float prev = float.NegativeInfinity, gap = float.PositiveInfinity;
            int frames = Mathf.CeilToInt(duration * fps);
            for (int f = 1; f <= frames; f++)
            {
                float t = Mathf.Min(duration, f / (float)fps);
                float x = t / duration;
                float v = target * (1f - (1f - x) * (1f - x));    // OutQuad, like the Stat's tween
                string s = fmt(v);
                if (c.Step(s, t)) { n++; last = s; gap = Mathf.Min(gap, t - prev); prev = t; }
            }
            if (c.Finish(fmt(target), duration)) { n++; last = fmt(target); }
            log.AreEqual(n, c.Clicks, "count-up: Clicks counts what it said yes to");
            return (n, last, gap);
        }

        var big = Run(1234567f, 0.6f, 30, v => "$" + v.ToString("N0"));
        log.IsTrue(big.clicks <= SfxRules.MaxCountClicks && big.clicks >= 6, $"count-up: a big number at 30 fps clicks 6–12 times (got {big.clicks})");
        log.AreEqual("$1,234,567", big.lastClicked, "count-up: the last click lands on the final value");
        log.IsTrue(big.minGap >= 0.6f / 12f - 1e-4f, $"count-up: clicks are at least duration/12 apart ({big.minGap * 1000f:F0} ms)");

        var fast = Run(9876543f, 2f, 120, v => v.ToString("N0"));   // 7 digits: exact in a float
        log.IsTrue(fast.clicks <= SfxRules.MaxCountClicks, $"count-up: 2 s at 120 fps still clicks at most 12 times (got {fast.clicks})");
        log.AreEqual("9,876,543", fast.lastClicked, "count-up: …and the last is the final value");

        var small = Run(3f, 0.6f, 30, v => Mathf.FloorToInt(v).ToString());
        log.IsTrue(small.clicks >= 3 && small.clicks <= 4, $"count-up: 0→3 clicks once per digit change (got {small.clicks})");
        log.AreEqual("3", small.lastClicked, "count-up: 0→3 ends on a click for 3");

        var c2 = new CountUpClicker(0.6f);
        c2.Step("7", 0.6f);
        log.IsTrue(!c2.Finish("7", 0.6f), "count-up: the final value doesn't click twice");
    }

    static void Sfx_DefaultSounds(Log log)
    {
        log.AreEqual(SfxKind.CardIn, TagSfxPlayer.DefaultSound(TagSfxEvent.Headline), "default sound: a side card → the tick");
        log.AreEqual(SfxKind.CardIn, TagSfxPlayer.DefaultSound(TagSfxEvent.Stat), "default sound: a Stat card → the tick");
        log.AreEqual(SfxKind.CardIn, TagSfxPlayer.DefaultSound(TagSfxEvent.Image), "default sound: a side image → the tick");
        log.AreEqual(SfxKind.BigCardIn, TagSfxPlayer.DefaultSound(TagSfxEvent.BigCenter), "default sound: a big headline → the low tick");
        log.AreEqual(SfxKind.BigCardIn, TagSfxPlayer.DefaultSound(TagSfxEvent.BigMedia), "default sound: BigMedia → the low tick");
        log.IsTrue(TagSfxPlayer.DefaultSound(TagSfxEvent.Black) == null, "default sound: {Black} is silent");
        log.IsTrue(TagSfxPlayer.DefaultSound(TagSfxEvent.ZoomExtreme) == null, "default sound: the close-up is silent");
        log.IsTrue(TagSfxPlayer.DefaultSound(TagSfxEvent.BigText) == null, "default sound: BigText's lines score themselves");
        log.IsTrue(TagSfxPlayer.DefaultSound(TagSfxEvent.ZoomIn) == null && TagSfxPlayer.DefaultSound(TagSfxEvent.ZoomOut) == null &&
                   TagSfxPlayer.DefaultSound(TagSfxEvent.ZoomReset) == null && TagSfxPlayer.DefaultSound(TagSfxEvent.ZoomPullback) == null,
            "default sound: the ordinary zoom steps are silent");
        log.IsTrue(TagSfxPlayer.DefaultSound(TagSfxEvent.PositionLeft) == null && TagSfxPlayer.DefaultSound(TagSfxEvent.PositionCenter) == null,
            "default sound: position moves are silent");
    }

    // Runs body with the Sound effects level (and an empty media folder) set, restoring both after.
    static void WithSfxPrefs(float volume, Action body)
    {
        bool hadVol = PlayerPrefs.HasKey(SoundLayer.VolumePrefKey);
        float savedVol = PlayerPrefs.GetFloat(SoundLayer.VolumePrefKey, 0f);
        bool hadRoot = PlayerPrefs.HasKey(MediaPresentationSystem.MediaRootFolderPrefKey);
        string savedRoot = PlayerPrefs.GetString(MediaPresentationSystem.MediaRootFolderPrefKey, "");
        try
        {
            SoundLayer.Volume = volume;
            PlayerPrefs.SetString(MediaPresentationSystem.MediaRootFolderPrefKey, "");
            body();
        }
        finally
        {
            if (hadVol) PlayerPrefs.SetFloat(SoundLayer.VolumePrefKey, savedVol);
            else PlayerPrefs.DeleteKey(SoundLayer.VolumePrefKey);
            if (hadRoot) PlayerPrefs.SetString(MediaPresentationSystem.MediaRootFolderPrefKey, savedRoot);
            else PlayerPrefs.DeleteKey(MediaPresentationSystem.MediaRootFolderPrefKey);
            PlayerPrefs.Save();
            foreach (string n in new[] { "SoundLayer", "TagSfxPlayer (auto)" })
                for (GameObject go = GameObject.Find(n); go != null; go = GameObject.Find(n))
                    UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void Sfx_VolumeZeroIsSilent(Log log)
    {
        bool had = PlayerPrefs.HasKey(SoundLayer.VolumePrefKey);
        float saved = PlayerPrefs.GetFloat(SoundLayer.VolumePrefKey, 0f);
        PlayerPrefs.DeleteKey(SoundLayer.VolumePrefKey);
        log.Near(0.6f, SoundLayer.Volume, 1e-6f, "sound effects: default 60 %");
        SoundLayer.Volume = 1.7f;
        log.Near(1f, SoundLayer.Volume, 1e-6f, "sound effects: clamps at 100 %");
        SoundLayer.Volume = -0.2f;
        log.IsTrue(SoundLayer.Volume == 0f && !SoundLayer.Enabled, "sound effects: 0 is Off");
        if (had) PlayerPrefs.SetFloat(SoundLayer.VolumePrefKey, saved); else PlayerPrefs.DeleteKey(SoundLayer.VolumePrefKey);
        PlayerPrefs.Save();

        WithSfxPrefs(0f, () =>
        {
            int req = SoundLayer.Requested, played = SoundLayer.Played;
            SoundLayer.Prewarm();
            foreach (SfxKind k in (SfxKind[])Enum.GetValues(typeof(SfxKind))) SoundLayer.Fire(k);
            foreach (TagSfxEvent e in (TagSfxEvent[])Enum.GetValues(typeof(TagSfxEvent))) TagSfxPlayer.Instance.Play(e);
            foreach (ContentCardType c in (ContentCardType[])Enum.GetValues(typeof(ContentCardType))) TagSfxPlayer.Instance.Play(c);
            log.IsTrue(!SoundLayer.HostExists && GameObject.Find("SoundLayer") == null,
                "sound effects Off: no layer, no AudioSource is ever made");
            log.IsTrue(SoundLayer.Requested == req && SoundLayer.Played == played, "sound effects Off: nothing is requested or played");
        });

        // On, but before the voice has started (the pre-restart phantom): still nothing.
        WithSfxPrefs(0.6f, () =>
        {
            int req = SoundLayer.Requested;
            foreach (SfxKind k in (SfxKind[])Enum.GetValues(typeof(SfxKind))) SoundLayer.Fire(k);
            TagSfxPlayer.Instance.Play(TagSfxEvent.Black);
            log.IsTrue(!SoundLayer.HostExists && SoundLayer.Requested == req,
                "sound effects: nothing sounds before the take's voice has really started");
        });
    }

    static void Sfx_PrewarmedClips(Log log)
    {
        WithSfxPrefs(0.6f, () =>
        {
            SoundLayer.Prewarm();
            log.IsTrue(SoundLayer.HostExists, "sound effects on: the take's layer exists after prewarm");
            var host = GameObject.Find("SoundLayer");
            log.IsTrue(host != null && host.GetComponents<AudioSource>().Length == 1, "sound effects on: exactly one AudioSource");
            AudioSource src = host != null ? host.GetComponent<AudioSource>() : null;
            log.IsTrue(src != null && src.spatialBlend == 0f && src.outputAudioMixerGroup == null && !src.playOnAwake,
                "sound effects on: the source is 2D with no mixer group, like the music");
            foreach (SfxKind k in (SfxKind[])Enum.GetValues(typeof(SfxKind)))
            {
                AudioClip c0 = SoundLayer.Clip(k, 0);
                log.IsTrue(c0 != null && c0.channels == 1 && c0.length <= SfxSynth.MaxSeconds + 1e-3f,
                    $"prewarm: {k}'s clip is mono and ≤400 ms");
                if (c0 == null) continue;
                var a = new float[c0.samples];
                c0.GetData(a, 0);
                log.Near(SfxSynth.PeakDbfs(k), 20f * Mathf.Log10(PeakOf(a)), 0.01f, $"prewarm: {k}'s clip peaks at its table level");
                for (int i = 1; i < SfxRules.Delays.Length; i++)
                {
                    AudioClip ci = SoundLayer.Clip(k, i);
                    int lead = Mathf.RoundToInt(SfxRules.Delays[i] * c0.frequency);
                    bool ok = ci != null && ci.samples == c0.samples + lead;
                    if (ok)
                    {
                        var b = new float[ci.samples];
                        ci.GetData(b, 0);
                        for (int j = 0; ok && j < lead; j++) ok = b[j] == 0f;
                        for (int j = 0; ok && j < a.Length; j += 7) ok = b[lead + j] == a[j];
                    }
                    log.IsTrue(ok, $"prewarm: {k}'s +{SfxRules.Delays[i] * 1000f:F0} ms clip is the same sound after that much silence");
                }
            }
        });
        log.IsTrue(!SoundLayer.HostExists, "sound effects: the test's layer is cleaned up");
    }
}

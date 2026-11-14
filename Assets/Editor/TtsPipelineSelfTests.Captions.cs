using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using MugsTech.Captions;

/// <summary>
/// Tier 3 step 5 — captions in the gaps: the script's words exactly; the
/// phrase splitter with and without punctuation; the plausibility verdict on
/// the real Binance THE FREEZE / THE CHOICE timings (which phrases pass, which
/// fall back, and why); the on-screen windows (+150 ms, bridged ≤ 600 ms);
/// the per-phrase gate (TtsPipelineSelfTests.CaptionGate.cs); the bottom band; the T=
/// fallback (closed by the section's end of speech, never over a timed
/// neighbour); starts read after the pause snap; the cold open's first line
/// excluded; the drawn caption; and the 2026-11-09 test render's ENDING,
/// where "Mugs out." came up 1.6 s early and cut the line before it short.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Caption Self-Tests")]
    public static void RunCaptions()
    {
        var log = new Log();
        RunCaptionGroup(log);
        log.Report();
    }

    static void RunCaptionGroup(Log log)
    {
        Caption_Text(log);
        Caption_Splitter(log);
        Caption_Windows(log);
        Caption_Fallback(log);
        Caption_FallbackKeepsOrder(log);
        Caption_SnappedStart(log);
        Caption_BinancePlausibility(log);
        Caption_Tier3Ending(log);
        RunCaptionGateGroup(log);
        Caption_Zones(log);
        Caption_Drawn(log);
        Caption_Switches(log);
    }

    // Words with made-up times: each 0.3 s after the last, plus extra gaps.
    static List<CaptionWord> TimedWords(string script, int section, Dictionary<int, float> extraGapBefore = null)
    {
        var words = CaptionBuilder.Narration(script, section, out _);
        float t = 1f;
        for (int i = 0; i < words.Count; i++)
        {
            if (extraGapBefore != null && extraGapBefore.TryGetValue(i, out float g)) t += g;
            words[i].Timed = true;
            words[i].Start = words[i].RawStart = t;
            words[i].End = t + 0.25f;
            t += 0.3f;
        }
        return words;
    }

    static string Join(List<CaptionWord> p)
    {
        var parts = new List<string>();
        foreach (var w in p) parts.Add(w.Text);
        return string.Join(" ", parts);
    }

    static void Caption_Text(Log log)
    {
        string script =
            "{Timestamp:\"Cold Open\",T=0.000}\n" +
            "{Position:Center,Cut,T=0.000} {Neutral,T=0.000}\n" +
            "[deadpan,T=0.000] More than six billion dollars, according to the FBI.\n" +
            "{Stat:\"$6B+\",\"Stolen\",\"FBI\",T=3.100,D=0,Left}\n" +
            "That's the FBI's number, not mine — honestly.\n" +
            "## NOT A HEADING TO SAY\n";
        var words = CaptionBuilder.Narration(script, 0, out var stamps);
        string all = Join(words);
        log.AreEqual("More than six billion dollars, according to the FBI. That's the FBI's number, not mine — honestly.", all,
            "captions: the spoken words exactly — tags, [cues] and ## lines out, punctuation and dashes as written");
        log.AreEqual(4, stamps.Count, "captions: every stamped tag's T= is kept for the fallback");
        log.IsTrue(words[0].FirstLine && words[8].FirstLine && !words[9].FirstLine,
            "captions: the cold open's first spoken line is marked (and only it)");
        var later = CaptionBuilder.Narration(script, 1, out _);
        log.IsTrue(!later[0].FirstLine, "captions: only the first section has a first line to skip");
        log.IsTrue(words[4].ClauseEnd && !words[4].SentenceEnd && words[8].SentenceEnd, "captions: commas end clauses, full stops end sentences");
        log.AreEqual("usdt", CaptionBuilder.Key("U-S-D-T,"), "captions: matching ignores case, hyphens and punctuation");
        log.AreEqual("that's", CaptionBuilder.Key("That’s"), "captions: …and treats ’ as an apostrophe");
    }

    static void Caption_Splitter(Log log)
    {
        // With punctuation: clauses first, a long one cut at its largest gap, a short one joined forward.
        string a = "{Neutral,T=0.000}\nThen came the freezing. Reuters, on Friday. Binance freezes assets after six point three billion dollar heist.\n";
        var gaps = new Dictionary<int, float> { { 11, 0.5f } };   // a pause before "six"
        var phrases = CaptionBuilder.Split(TimedWords(a, 1, gaps));
        var texts = phrases.ConvertAll(Join);
        log.AreEqual("Then came the freezing.|Reuters, on Friday.|Binance freezes assets after|six point three billion dollar heist.",
            string.Join("|", texts), "splitter: punctuation first, the long clause at its pause, the 1-word clause joined to the next");
        bool sizes = true;
        foreach (var p in phrases) sizes &= p.Count >= CaptionBuilder.MinWords && p.Count <= CaptionBuilder.MaxWords;
        log.IsTrue(sizes, "splitter: every phrase 3–7 words");

        // No punctuation at all: only the timing decides.
        string b = "{Neutral,T=0.000}\nthis is a line of words that never stops for any breath at all\n";
        var p2 = CaptionBuilder.Split(TimedWords(b, 1, new Dictionary<int, float> { { 7, 0.7f } }));
        log.AreEqual("this is a line of words that|never stops for any breath at all",
            string.Join("|", p2.ConvertAll(Join)), "splitter: no punctuation → cut at the largest gap");
        var even = CaptionBuilder.Split(TimedWords("{Neutral,T=0}\none two three four five six seven eight nine ten\n", 1));
        log.IsTrue(even.Count == 2 && even[0].Count == 5 && even[1].Count == 5, "splitter: no gap to go by → cut in the middle");

        // A short sentence alone between long pauses stays short rather than bridge them.
        string c = "{Neutral,T=0}\nThe money is gone forever now. Hi. The same day the market fell again.\n";
        var p3 = CaptionBuilder.Split(TimedWords(c, 1, new Dictionary<int, float> { { 6, 2f }, { 7, 2f } }));
        log.IsTrue(p3.ConvertAll(Join).Contains("Hi."), "splitter: a lone short sentence between long pauses isn't glued across them");

        // The first line never shares a phrase with what follows.
        string d = "{Neutral,T=0}\nShort hook.\nAnd then the rest of it.\n";
        var p4 = CaptionBuilder.Split(TimedWords(d, 0));
        log.IsTrue(p4.Count == 2 && p4[0][0].FirstLine && !p4[1][0].FirstLine, "splitter: the first line stays apart from the next");
    }

    static CaptionPhrase Phrase(float start, float end, PhraseTiming timing = PhraseTiming.Words)
    {
        var p = new CaptionPhrase { Timing = timing };
        p.Words.Add(new CaptionWord { Text = "a", Timed = true, Start = start, RawStart = start, End = start + 0.2f });
        p.Words.Add(new CaptionWord { Text = "b", Timed = true, Start = end - 0.2f, RawStart = end - 0.2f, End = end });
        return p;
    }

    static void Caption_Windows(Log log)
    {
        var list = new List<CaptionPhrase> { Phrase(1f, 2f), Phrase(2.5f, 3f), Phrase(4f, 5f) };
        CaptionBuilder.Windows(list);
        log.Near(2.5f, list[0].End, 1e-4f, "windows: a gap of 0.5 s is bridged — the phrase stays until the next");
        log.Near(3.15f, list[1].End, 1e-4f, "windows: a gap of 1 s shows nothing — the phrase leaves 150 ms after its last word");
        log.Near(5.15f, list[2].End, 1e-4f, "windows: the last phrase lingers 150 ms");
        var overlap = new List<CaptionPhrase> { Phrase(1f, 3f), Phrase(2.5f, 4f) };
        CaptionBuilder.Windows(overlap);
        log.IsTrue(overlap[0].End <= overlap[1].Start, "windows: one phrase at a time — never overlapping");
        var track = new CaptionTrack();
        track.Phrases.AddRange(list);
        log.IsTrue(track.At(1.5f) == list[0] && track.At(3.5f) == null && track.At(4.2f) == list[2], "windows: the phrase at a time");
    }

    static void Caption_Fallback(Log log)
    {
        string s = "{A,T=10.000}\none two three\n{B,T=12.000}\nfour five\n";
        var words = CaptionBuilder.Narration(s, 1, out var stamps);
        CaptionBuilder.ApplyFallback(words, stamps);
        log.Near(10f, words[0].FallbackStart, 1e-4f, "fallback: the words after a tag start at its T=");
        log.Near(12f, words[2].FallbackEnd, 1e-4f, "fallback: …and fill the span to the next tag's T=");
        log.IsTrue(words[1].FallbackStart > words[0].FallbackStart && words[1].FallbackEnd < words[2].FallbackStart + 1e-4f,
            "fallback: in order, split by length");
        log.Near(12f, words[3].FallbackStart, 1e-4f, "fallback: the next group starts at its own tag");
        log.Near(12f + 10 * CaptionBuilder.FallbackSecondsPerChar, words[4].FallbackEnd, 1e-3f,
            "fallback: after the last tag, with no end of speech known — 70 ms a letter");

        var closed = CaptionBuilder.Narration(s, 1, out var stamps2);
        CaptionBuilder.ApplyFallback(closed, stamps2, 14.5f);
        log.Near(14.5f, closed[4].FallbackEnd, 1e-4f, "fallback: after the last tag the words run to the section's measured end of speech");
    }

    static CaptionPhrase Guess(float start, float end)
    {
        CaptionPhrase p = Phrase(start, end, PhraseTiming.Fallback);
        p.Words[0].FallbackStart = start;
        p.Words[p.Words.Count - 1].FallbackEnd = end;
        return p;
    }

    static void Caption_FallbackKeepsOrder(Log log)
    {
        // The 2026-11-09 render: a fallback guess landed inside the timed phrase before it and cut it short.
        CaptionPhrase before = Phrase(10f, 12f), guess = Guess(11f, 11.8f), after = Phrase(13.9f, 15f);
        var list = new List<CaptionPhrase> { before, guess, after };
        CaptionBuilder.Windows(list);
        log.IsTrue(before.End >= 12f - 1e-4f, $"fallback order: the timed phrase keeps the screen until its last word ends (ends {before.End:F2})");
        log.IsTrue(guess.Start >= 12f - 1e-4f && guess.End <= after.Start + 1e-4f,
            $"fallback order: the guess moves after it and stays clear of the next timed phrase ({guess.Start:F2}–{guess.End:F2})");
        log.Near(0.8f + CaptionBuilder.Linger, guess.End - guess.Start, 1e-3f, "fallback order: …keeping its own length (+150 ms linger)");

        CaptionPhrase longGuess = Guess(10.2f, 13.5f);
        var squeezed = new List<CaptionPhrase> { Phrase(9f, 10f), longGuess, Phrase(12f, 13f) };
        CaptionBuilder.Windows(squeezed);
        log.IsTrue(longGuess.Start >= 10f - 1e-4f && longGuess.End <= 12f + 1e-4f,
            $"fallback order: a guess longer than the room shrinks into it ({longGuess.Start:F2}–{longGuess.End:F2})");

        CaptionPhrase stuck = Guess(10.5f, 11f), next = Phrase(11.9f, 13f);
        var none = new List<CaptionPhrase> { Phrase(10f, 12f), stuck, next };
        CaptionBuilder.Windows(none);
        var track = new CaptionTrack();
        track.Phrases.AddRange(none);
        bool everShown = false;
        for (float t = 9f; t < 15f; t += 0.01f) everShown |= track.At(t) == stuck;
        log.IsTrue(!everShown && !none.Contains(stuck) && next.End >= 13f,
            "fallback order: no room at all → that guess never shows (dropped), and its neighbours keep their times");
    }

    // A section's take-clock mapping as SegmentSequencer builds it: the pause
    // snap (onto a pause's end when the time is within 0.9 s of it), then the
    // section's offset, never before the section's start.
    static Func<float, bool, float> SnapMap(List<(float From, float To)> localPauses, float offset, float floorT)
    {
        return (x, snap) =>
        {
            float u = x;
            if (snap)
                foreach (var q in localPauses)
                    if (u >= q.From && u < q.To) { if (q.To - u <= 0.9f) u = q.To; break; }
            return Math.Max(floorT, u + offset);
        };
    }

    static void Caption_SnappedStart(Log log)
    {
        // v3 often starts the word after a breath a few hundred ms early, inside
        // the measured pause; the snap its T= gets moves it onto the onset.
        var pauses = new List<(float From, float To)> { (2.0f, 2.8f) };
        var raw = new List<RawWord>
        {
            new RawWord { word = "Mugs", start = 2.4f, end = 3.1f },
            new RawWord { word = "is", start = 3.15f, end = 3.3f },
            new RawWord { word = "out", start = 3.35f, end = 3.6f },
            new RawWord { word = "now.", start = 3.65f, end = 4.0f },
        };
        var words = CaptionBuilder.Narration("{N,T=0}\nMugs is out now.\n", 1, out _);
        CaptionBuilder.Attach(words, raw, SnapMap(pauses, 0f, 0f));
        log.Near(2.8f, words[0].Start, 1e-4f, "snapped start: the first word's start moves onto the pause's end, like its T=");
        log.AreEqual(PhraseTiming.Words, CaptionBuilder.Judge(words, pauses, out string why),
            $"snapped start: a word the alignment starts inside a breath is still timed word by word ({why ?? "passes"})");

        var farPauses = new List<(float From, float To)> { (1.6f, 2.8f) };
        var far = new List<RawWord>(raw);
        far[0] = new RawWord { word = "Mugs", start = 1.7f, end = 3.1f };
        var farWords = CaptionBuilder.Narration("{N,T=0}\nMugs is out now.\n", 1, out _);
        CaptionBuilder.Attach(farWords, far, SnapMap(farPauses, 0f, 0f));
        log.IsTrue(CaptionBuilder.Judge(farWords, farPauses, out string whyFar) != PhraseTiming.Words,
            $"snapped start: more than 0.9 s before the pause ends there's no snap — not word-timed ({whyFar})");
    }

    static void Caption_Tier3Ending(Log log)
    {
        // The ENDING of the 2026-11-09 tier-3 test render. Pauses measured from
        // its 04_ENDING.mp3 by SegmentSequencer's rule (< −35 dBFS for ≥ 0.25 s);
        // the section opens 81.69 s into the take after a 0.03 s trim.
        string dir = Path.Combine(Application.dataPath, "Editor/TestData/Captions");
        var local = new List<(float From, float To)> { (0.86f, 1.58f), (5.90f, 6.56f), (10.08f, 10.86f) };
        const float offset = 81.66f, floorT = 81.69f;
        Func<float, bool, float> map = SnapMap(local, offset, floorT);
        string script = Regex.Replace(File.ReadAllText(Path.Combine(dir, "TIER3_ENDING_timed.txt")), @",T=(\d+(?:\.\d+)?)",
            m => ",T=" + map(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), true).ToString("F3", CultureInfo.InvariantCulture));
        var take = new List<(float From, float To)>();
        foreach (var q in local) take.Add((q.From + offset, q.To + offset));
        var section = new CaptionBuilder.Section
        {
            Index = 3, Script = script, Words = FixtureWords("TIER3_ENDING"), Map = map, Pauses = take,
            SpeechEnd = 11.62f + offset,
        };
        CaptionTrack t = CaptionBuilder.Build(new[] { section });
        foreach (var p in t.Phrases)
            Debug.Log($"[Captions]   tier-3 ENDING {p.Timing,-8} {p.Start,6:F2}–{p.End,6:F2}  {p.Text}" + (p.Why != null ? $"   ({p.Why})" : ""));

        CaptionPhrase mugs = t.Phrases.Find(p => p.Text == "Mugs out.");
        CaptionPhrase ends = t.Phrases.Find(p => p.Text.EndsWith("before it ends."));
        log.IsTrue(mugs != null && mugs.Timing == PhraseTiming.Words,
            $"tier-3 ENDING: \"Mugs out.\" is word-timed from its snapped onset ({mugs?.Timing}, {mugs?.Why ?? "passes"})");
        log.IsTrue(mugs != null && mugs.Start >= 92.4f,
            $"tier-3 ENDING: \"Mugs out.\" comes up when \"Mugs\" is said (≈92.5 s), not 1.6 s early (at {mugs?.Start:F2})");
        CaptionWord endsWord = ends?.Words[ends.Words.Count - 1];
        log.IsTrue(ends != null && ends.End >= endsWord.End - 1e-3f,
            $"tier-3 ENDING: \"…before it ends.\" stays until \"ends.\" is over ({ends?.End:F2} ≥ {endsWord?.End:F2})");
        log.AreEqual(0, t.Count(PhraseTiming.Fallback), "tier-3 ENDING: no phrase left on the T= guess");
        bool ordered = true;
        for (int i = 1; i < t.Phrases.Count; i++) ordered &= t.Phrases[i].Start >= t.Phrases[i - 1].End - 1e-4f;
        log.IsTrue(ordered, "tier-3 ENDING: one phrase at a time, in script order");
    }

    static List<RawWord> FixtureWords(string slug)
    {
        string path = Path.Combine(Application.dataPath, "Editor/TestData/Captions", slug + "_words.json");
        return JsonUtility.FromJson<FixtureList>("{\"items\":" + File.ReadAllText(path) + "}").items;
    }

    [System.Serializable] class FixtureList { public List<RawWord> items; }

    static void Caption_BinancePlausibility(Log log)
    {
        string dir = Path.Combine(Application.dataPath, "Editor/TestData/Captions");
        foreach (string slug in new[] { "04_THE_FREEZE", "05_THE_CHOICE" })
        {
            string script = File.ReadAllText(Path.Combine(dir, slug + "_timed.txt"));
            var section = new CaptionBuilder.Section { Index = 3, Script = script, Words = FixtureWords(slug) };
            CaptionTrack t = CaptionBuilder.Build(new[] { section });
            int words = t.Count(PhraseTiming.Words), span = t.Count(PhraseTiming.Span), fb = t.Count(PhraseTiming.Fallback);
            log.AreEqual(0, t.Unmatched, $"binance {slug}: every script word finds its timing");
            bool noCue = true, sizes = true;
            foreach (var p in t.Phrases)
            {
                noCue &= !p.Text.Contains("smirk") && !p.Text.Contains("[");
                sizes &= p.Words.Count <= CaptionBuilder.MaxWords;
            }
            log.IsTrue(noCue, $"binance {slug}: voiced [stage directions] never reach a caption");
            log.IsTrue(sizes, $"binance {slug}: no phrase over 7 words");
            Debug.Log($"[Captions] Binance {slug}: {t.Phrases.Count} phrases — {words} word-timed, {span} whole-phrase, {fb} T= fallback.");
            foreach (var p in t.Phrases)
                Debug.Log($"[Captions]   {(p.Timing == PhraseTiming.Words ? "WORDS   " : p.Timing == PhraseTiming.Span ? "PHRASE  " : "FALLBACK")} " +
                          $"{p.Start,7:F2}–{p.End,7:F2}  {p.Text}" + (p.Why != null ? $"   ({p.Why})" : ""));
            log.IsTrue(words > 0 && span + fb > 0, $"binance {slug}: some phrases pass, some don't ({words}/{t.Phrases.Count} word-timed)");

            CaptionPhrase Find(string text) => t.Phrases.Find(p => p.Text.Contains(text));
            if (slug == "05_THE_CHOICE")
            {
                CaptionPhrase mattress = Find("mattress");
                log.IsTrue(mattress != null && mattress.Timing == PhraseTiming.Fallback,
                    $"binance THE CHOICE: \"…money under my mattress.\" (4 words in 25 ms) falls back to the T= span ({mattress?.Why})");
                CaptionPhrase okay = Find("Okay");
                log.IsTrue(okay != null && okay.Timing != PhraseTiming.Words, $"binance THE CHOICE: \"Okay.\" (5 ms) isn't word-timed ({okay?.Why})");
                CaptionPhrase keys = Find("Not your keys");
                log.IsTrue(keys != null && keys.Timing == PhraseTiming.Words, "binance THE CHOICE: \"Not your keys\" passes — highlighted word by word");
                log.IsTrue(words * 2 >= t.Phrases.Count - 4, $"binance THE CHOICE (≈4:40–5:45): about half or more word-timed ({words} of {t.Phrases.Count})");
            }
            else
            {
                CaptionPhrase circle = Find("Circle's CEO");
                log.IsTrue(circle != null && circle.Timing == PhraseTiming.Words, "binance THE FREEZE: \"Circle's CEO, Jeremy Allaire\" passes");
                CaptionPhrase then = Find("Then came");
                log.IsTrue(then != null && then.Timing != PhraseTiming.Words, $"binance THE FREEZE: the line after the voiced cue (\"Then\" 21 ms) isn't word-timed ({then?.Why})");
            }
            bool ordered = true;
            for (int i = 1; i < t.Phrases.Count; i++) ordered &= t.Phrases[i].Start >= t.Phrases[i - 1].End - 1e-4f;
            log.IsTrue(ordered, $"binance {slug}: one phrase at a time, in order");
        }
    }

    static void Caption_Zones(Log log)
    {
        CaptionZones.Zone z = CaptionZones.Bottom;
        log.IsTrue(z.YMin >= 0.78f && z.YMax <= 0.88f + 1e-5f && Mathf.Abs(z.CenterX - 0.5f) < 1e-5f && z.Width <= 0.80f + 1e-5f,
            "zones: one band along the bottom, under Mugs's feet (79–88 %), centred, ≤ 80 % wide");
        log.IsTrue(1f - z.YMax >= 0.12f - 1e-5f, "zones: its lower edge 12 % above the frame's — clear of YouTube's progress bar");
    }

    static void Caption_Drawn(Log log)
    {
        var canvasGo = new GameObject("CaptionTestCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1920f, 1080f);
        try
        {
            var words = TimedWords("{N,T=0}\nCircle's CEO, Jeremy Allaire, posted that\n", 1);
            var p = new CaptionPhrase { Timing = PhraseTiming.Words };
            p.Words.AddRange(words);
            var track = new CaptionTrack();
            track.Phrases.Add(p);
            var c = CaptionOverlay.CreateOn(canvasGo.transform, track);

            c.ShowPhrase(p, words[2].Start + 0.01f);
            c.Text.ForceMeshUpdate(true, true);
            log.IsTrue(c.Box.gameObject.activeSelf, "caption: shown");
            log.IsTrue(c.Text.textInfo.lineCount == 1 && c.FitSize >= CaptionOverlay.MinFontPx && c.FitSize <= CaptionOverlay.MaxFontPx,
                $"caption: one line at {c.FitSize:F0} px (52–60)");
            log.IsTrue(Mathf.Approximately(c.Box.anchorMax.y, 1f - CaptionZones.BandTop) && Mathf.Approximately(c.Box.anchorMin.y, 1f - CaptionZones.BandBottom),
                "caption: in the bottom band (79–88 % down)");
            if (CaptionOverlay.HighlightEnabled)
                log.IsTrue(c.Text.text.Contains("<color=#E63946><noparse>Jeremy</noparse></color>"), "caption: the spoken word in the accent colour");
            log.IsTrue(!c.Text.text.Contains("posted that,") && c.Text.text.EndsWith("<noparse>that</noparse>"), "caption: the words as written");

            var big = TimedWords("{N,T=0}\nTether froze more than one point five six seven billion\n", 1);
            var p2 = new CaptionPhrase { Timing = PhraseTiming.Span };
            p2.Words.AddRange(big.GetRange(0, 7));
            c.ShowPhrase(p2, 2f);
            c.Text.ForceMeshUpdate(true, true);
            log.IsTrue(c.Text.textInfo.lineCount == 1, $"caption: seven words still fit one line ({c.Text.textInfo.lineCount}, {c.FitSize:F0} px)");
            log.IsTrue(!c.Text.text.Contains("<color"), "caption: a phrase without believable word times is shown plainly");

            var huge = TimedWords("{N,T=0}\nInvestigators everywhere simultaneously immobilised stolen cryptocurrency reserves\n", 1);
            var p3 = new CaptionPhrase { Timing = PhraseTiming.Span };
            p3.Words.AddRange(huge);
            c.ShowPhrase(p3, 2f);
            c.Text.ForceMeshUpdate(true, true);
            log.IsTrue(c.Text.textInfo.lineCount == 2 && c.FitSize >= CaptionOverlay.MinFontPx,
                $"caption: a phrase too long for one line wraps onto two ({c.Text.textInfo.lineCount} line(s) at {c.FitSize:F0} px)");
            log.IsTrue(c.Text.alignment == TMPro.TextAlignmentOptions.Bottom, "caption: bottom-aligned — the second line grows upward");

            c.ShowPhrase(null, 3f);
            log.IsTrue(!c.Box.gameObject.activeSelf, "caption: hidden at once when nothing is due");
            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        finally { UnityEngine.Object.DestroyImmediate(canvasGo); }
    }

    static void Caption_Switches(Log log)
    {
        bool had = PlayerPrefs.HasKey(CaptionOverlay.PrefKey), hadH = PlayerPrefs.HasKey(CaptionOverlay.HighlightPrefKey);
        int saved = PlayerPrefs.GetInt(CaptionOverlay.PrefKey, 1), savedH = PlayerPrefs.GetInt(CaptionOverlay.HighlightPrefKey, 1);
        try
        {
            PlayerPrefs.DeleteKey(CaptionOverlay.PrefKey);
            PlayerPrefs.DeleteKey(CaptionOverlay.HighlightPrefKey);
            log.IsTrue(CaptionOverlay.Enabled && CaptionOverlay.HighlightEnabled, "caption switches: both on by default");
            log.IsTrue(CaptionOverlay.Create(null, null) == null && CaptionOverlay.Last.Note != null,
                "captions: a take without word timings makes nothing, and says so");
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(CaptionOverlay.PrefKey, saved); else PlayerPrefs.DeleteKey(CaptionOverlay.PrefKey);
            if (hadH) PlayerPrefs.SetInt(CaptionOverlay.HighlightPrefKey, savedH); else PlayerPrefs.DeleteKey(CaptionOverlay.HighlightPrefKey);
            PlayerPrefs.Save();
        }
    }
}

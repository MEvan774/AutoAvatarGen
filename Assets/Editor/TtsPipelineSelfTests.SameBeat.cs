using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MugsTech.Captions;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tags written on one beat fire on one frame (MugsTech.Tts.TagBeats, through
/// SegmentSequencer's real T= mapping): the 2026-11-10 Cybertruck take, where
/// {Shrug,Cut} and the {Zoom:ExtremeOut} on the next line came out 0.13 s
/// apart; a run inside a {Pause} gap (PauseInserter's pins win); a tag more
/// than 0.9 s from the onset either side; never pushed later; a voiced sound
/// between two tags; the rescale end-zone rule; HybridAvatarSystem moving an
/// emotion onto an extreme-zoom cut; and the captions, whose words don't move.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/TTS/Run Same-Beat Self-Tests")]
    public static void RunSameBeat()
    {
        var log = new Log();
        RunSameBeatGroup(log);
        log.Report();
    }

    static void RunSameBeatGroup(Log log)
    {
        SameBeat_Runs(log);
        SameBeat_CybertruckShrugAndExtremeOut(log);
        SameBeat_PauseGapIsLeftToPauseInserter(log);
        SameBeat_FarFromTheOnsetIsUnchanged(log);
        SameBeat_NeverPushedLater(log);
        SameBeat_VoicedSoundBetweenSplitsTheRun(log);
        SameBeat_RescaledMidSectionDoesNotSnap(log);
        SameBeat_EmotionJoinsTheExtremeZoomCut(log);
        SameBeat_CaptionWordsDontMove(log);
    }

    // Section 03_THE_RECORD of generation 2026-11-10_15-33-18 (the Cybertruck
    // take): its span as the manifest says and as the player measured it, and
    // the pauses the player measured (< −35 dBFS for ≥ 0.25 s), copied from the
    // 2026-11-10 20:31 verification take's Player.log ("measured speech" line).
    // The last one is the silence between "company." and "So": 66.50–67.34.
    // (Unity decodes the mp3 about 0.06 s later than ffmpeg, which puts the same
    // gap at 66.44–67.28; the brief quoted 66.44–67.36.)
    const float Rec03AlignStart = 0.075f, Rec03AlignEnd = 70.615f, Rec03RealStart = 0.12f, Rec03RealEnd = 70.68f;
    static List<Vector2> Rec03Pauses() => new List<Vector2>
    {
        new Vector2(3.68f, 4.12f), new Vector2(5.38f, 6.24f), new Vector2(10.20f, 11.12f), new Vector2(11.68f, 12.04f),
        new Vector2(12.48f, 13.34f), new Vector2(14.02f, 14.64f), new Vector2(18.58f, 19.10f), new Vector2(21.96f, 22.32f),
        new Vector2(25.30f, 25.76f), new Vector2(28.84f, 29.70f), new Vector2(30.88f, 31.20f), new Vector2(32.24f, 33.18f),
        new Vector2(35.96f, 36.42f), new Vector2(40.04f, 40.74f), new Vector2(44.92f, 45.36f), new Vector2(46.96f, 47.86f),
        new Vector2(49.42f, 50.40f), new Vector2(52.12f, 52.62f), new Vector2(55.08f, 55.72f), new Vector2(60.98f, 61.56f),
        new Vector2(62.54f, 63.10f), new Vector2(64.10f, 65.06f), new Vector2(65.44f, 65.68f), new Vector2(66.50f, 67.34f),
    };

    static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(Application.dataPath, "Editor/TestData/Captions", name));

    // The T= of the first tag whose text (without its T=) is exactly `tag`, e.g. "{Shrug,Cut}".
    static float StampOf(string script, string tag)
    {
        foreach (Match m in Regex.Matches(script, @"\{[^{}\n]*\}"))
        {
            string bare = Regex.Replace(m.Value, @",T=\d+(?:\.\d+)?", "");
            if (bare != tag) continue;
            Match t = Regex.Match(m.Value, @"T=(\d+(?:\.\d+)?)");
            return t.Success ? float.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture) : float.NaN;
        }
        return float.NaN;
    }

    // A made-up section: alignment and audio agree on 0..60 s (no rescale), with `pauses`.
    static string Shift(string script, List<Vector2> pauses, PauseInserter.Shift shift = null,
                        float alignEnd = 60f, float realEnd = 60f)
        => SegmentSequencer.ShiftSectionForTest(script, 0f, 0f, 0f, alignEnd, 0f, realEnd, pauses, shift, out _);

    static void SameBeat_Runs(Log log)
    {
        string s = "Yes. His own company.\n{Shrug,Cut,T=67.088}\n{Zoom:ExtremeOut,T=67.487}\n" +
                   "[conversational, loose,T=67.488] So if buyers {Shrug,T=68.194}stopped showing up.\n" +
                   "{Smirk,T=70.1}\n{Pause:0.6,T=70.3}\n[dry,T=70.5] Done.";
        var st = TagBeats.Find(s);
        log.AreEqual(7, st.Count, "same beat: every T= is found");
        log.IsTrue(st[0].Run == st[1].Run && st[1].Run == st[2].Run,
            "same beat: tags on adjacent lines between two spoken lines are one run — the [stage direction] doesn't break it");
        log.IsTrue(!st[2].Tag && st[0].Tag, "same beat: a [stage direction] is in the run but isn't a tag");
        log.IsTrue(st[3].Run != st[2].Run, "same beat: an inline tag after spoken words starts a run of its own");
        log.IsTrue(st[4].PauseRun && st[5].PauseRun && !st[0].PauseRun, "same beat: a run holding a {Pause} is marked");
        log.AreEqual("{Zoom:ExtremeOut}", st[1].Name, "same beat: the log names the tag as written");
    }

    static void SameBeat_CybertruckShrugAndExtremeOut(Log log)
    {
        // The pre-edit 03_THE_RECORD_timed.txt: {Shrug,Cut,T=67.088} on its own
        // line above {Zoom:ExtremeOut,T=67.487}. The audio is silent 66.50–67.34;
        // the snap moves Shrug onto 67.34, and ExtremeOut, which the alignment
        // put 0.15 s into "So", comes back onto the same onset.
        string script = Fixture("03_THE_RECORD_timed.txt");
        string shifted = SegmentSequencer.ShiftSectionForTest(script, 0f, 0f, Rec03AlignStart, Rec03AlignEnd,
            Rec03RealStart, Rec03RealEnd, Rec03Pauses(), null, out _);
        float shrug = StampOf(shifted, "{Shrug,Cut}"), zoom = StampOf(shifted, "{Zoom:ExtremeOut}");
        log.Near(67.34f, shrug, 1e-3f, "cybertruck: {Shrug,Cut} snaps onto the onset of \"So\" (67.34 as measured)");
        log.Near(shrug, zoom, 1e-4f, $"cybertruck: {{Zoom:ExtremeOut}} fires on the same time as the Shrug (was 67.487, now {zoom:F3})");

        // The brief's figures for that gap, 66.44–67.36: the same result on 67.36.
        var brief = Rec03Pauses();
        brief[brief.Count - 1] = new Vector2(66.44f, 67.36f);
        string briefShifted = SegmentSequencer.ShiftSectionForTest(script, 0f, 0f, Rec03AlignStart, Rec03AlignEnd,
            Rec03RealStart, Rec03RealEnd, brief, null, out _);
        log.Near(67.36f, StampOf(briefShifted, "{Shrug,Cut}"), 1e-3f, "cybertruck (pause 66.44–67.36): the Shrug lands on 67.36");
        log.Near(67.36f, StampOf(briefShifted, "{Zoom:ExtremeOut}"), 1e-3f, "cybertruck (pause 66.44–67.36): …and so does the ExtremeOut");

        // On the take's clock (the section sits 75.24 s in) they still share one frame.
        string take = SegmentSequencer.ShiftSectionForTest(script, 75.24f, 75.27f, Rec03AlignStart, Rec03AlignEnd,
            Rec03RealStart, Rec03RealEnd, Rec03Pauses(), null, out _);
        float a = StampOf(take, "{Shrug,Cut}"), b = StampOf(take, "{Zoom:ExtremeOut}");
        log.IsTrue(Mathf.FloorToInt(a * 30f) == Mathf.FloorToInt(b * 30f),
            $"cybertruck: on the take's clock both land on frame {Mathf.FloorToInt(a * 30f)} at 30 fps ({a:F3} / {b:F3})");

        // The same pattern twice more in this section — a card and an emotion
        // written on one beat: {Compare:End} snaps to 29.70 and {Frowned} (0.16 s
        // into "Once") joins it; {Document} snaps to 33.18 and {NarrowedEyes}
        // joins it. Nothing else moves: every other tag keeps the time it
        // mapped to on its own (runs with a {Pause} are PauseInserter's).
        log.Near(29.70f, StampOf(shifted, "{Frowned}"), 1e-3f, "cybertruck: {Frowned} joins {Compare:End} on the onset of \"Once\"");
        log.Near(33.18f, StampOf(shifted, "{NarrowedEyes}"), 1e-3f, "cybertruck: {NarrowedEyes} joins {Document} on the onset of \"It\"");
        string alone = Regex.Replace(script, @"T=(\d+(?:\.\d+)?)", m =>
        {
            float t = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return "T=" + SnapOnly(t, Rec03Pauses()).ToString("F3", CultureInfo.InvariantCulture);
        });
        var before = Regex.Matches(alone, @"T=(\d+(?:\.\d+)?)");
        var after = Regex.Matches(shifted, @"T=(\d+(?:\.\d+)?)");
        int changed = 0;
        for (int i = 0; i < Math.Min(before.Count, after.Count); i++)
            if (before[i].Value != after[i].Value) changed++;
        log.AreEqual(before.Count, after.Count, "cybertruck: every T= is still there");
        log.AreEqual(3, changed, "cybertruck: exactly three T= moved (ExtremeOut, Frowned, NarrowedEyes) — all pulled back, none later");
    }

    // SegmentSequencer's pause snap with no rescale: inside a pause and within 0.9 s of its end → its end.
    static float SnapOnly(float t, List<Vector2> pauses)
    {
        foreach (Vector2 p in pauses)
            if (t >= p.x && t < p.y) return p.y - t <= 0.9f ? p.y : t;
        return t;
    }

    static void SameBeat_PauseGapIsLeftToPauseInserter(Log log)
    {
        string s = "Setup line here.\n{Smirk,T=10.200}\n{Pause:0.6,T=10.500}\n{Zoom:ExtremeOut,T=10.900}\nPunchline words.";
        var pauses = new List<Vector2> { new Vector2(10.0f, 10.8f) };

        // No splice (the gap search came up empty): the run is still PauseInserter's, so nothing joins.
        string plain = Shift(s, pauses);
        log.Near(10.8f, StampOf(plain, "{Smirk}"), 1e-3f, "{Pause} run: the tag above still snaps on its own");
        log.Near(10.9f, StampOf(plain, "{Zoom:ExtremeOut}"), 1e-3f, "{Pause} run: …and the tag below is not pulled onto it");

        // With the beat found, PauseInserter pins them: above as the gap opens, below as it closes.
        PauseInserter.PauseTag tag = PauseInserter.FindTags(s)[0];
        var splice = new PauseInserter.Splice
        {
            TagIndex = tag.Index, TagEnd = tag.Index + tag.Length, GroupStart = tag.GroupStart, GroupEnd = tag.GroupEnd,
            GapStart = 10.0f, GapEnd = 10.8f, At = 10.4f, Requested = 0.6f, Inserted = 0f,
        };
        var shift = new PauseInserter.Shift(new List<PauseInserter.Splice> { splice });
        string pinned = Shift(s, pauses, shift);
        log.Near(10.0f, StampOf(pinned, "{Smirk}"), 1e-3f, "{Pause} run: a tag above the pause fires as the gap opens (PauseInserter)");
        log.Near(10.8f, StampOf(pinned, "{Zoom:ExtremeOut}"), 1e-3f, "{Pause} run: a tag below it fires as the gap closes (PauseInserter)");
    }

    static void SameBeat_FarFromTheOnsetIsUnchanged(Log log)
    {
        var pauses = new List<Vector2> { new Vector2(7.0f, 9.2f) };

        // The first tag sits more than 0.9 s before the pause ends: it doesn't snap, so there's no onset to join.
        string early = Shift("Word one.\n{Shrug,T=8.000}\n{Zoom:ExtremeOut,T=9.400}\nNext words.", pauses);
        log.Near(8.0f, StampOf(early, "{Shrug}"), 1e-3f, "far: a tag 1.2 s before the pause ends doesn't snap");
        log.Near(9.4f, StampOf(early, "{Zoom:ExtremeOut}"), 1e-3f, "far: …and the tag after it is unchanged");

        // The snap happens, but the later tag is more than 0.9 s past the onset: too far to call it the same beat.
        string late = Shift("Word one.\n{Shrug,T=8.600}\n{Zoom:ExtremeOut,T=10.200}\nNext words.", pauses);
        log.Near(9.2f, StampOf(late, "{Shrug}"), 1e-3f, "far: the first tag snaps onto the onset");
        log.Near(10.2f, StampOf(late, "{Zoom:ExtremeOut}"), 1e-3f, "far: a tag 1.0 s past the onset is unchanged");

        string near = Shift("Word one.\n{Shrug,T=8.600}\n{Zoom:ExtremeOut,T=10.050}\nNext words.", pauses);
        log.Near(9.2f, StampOf(near, "{Zoom:ExtremeOut}"), 1e-3f, "far: …one 0.85 s past it joins");
    }

    static void SameBeat_NeverPushedLater(Log log)
    {
        var pauses = new List<Vector2> { new Vector2(7.0f, 9.2f) };
        string s = Shift("Word one.\n{Frowned,T=6.900}\n{Shrug,T=8.600}\n{Zoom:ExtremeOut,T=9.300}\nNext words.", pauses);
        log.Near(6.9f, StampOf(s, "{Frowned}"), 1e-3f, "never later: a tag before the snapped one keeps its time (on the setup's last word)");
        log.Near(9.2f, StampOf(s, "{Zoom:ExtremeOut}"), 1e-3f, "never later: the tag after it comes back onto the onset");
    }

    static void SameBeat_VoicedSoundBetweenSplitsTheRun(Log log)
    {
        // A voiced [laughs] between the two tags: the audio has sound (9.2–9.5) and a
        // pause after it. The tag after that pause belongs to the next stretch.
        var pauses = new List<Vector2> { new Vector2(7.0f, 9.2f), new Vector2(9.5f, 9.7f) };
        string s = Shift("Word one.\n{Shrug,T=8.600}\n[laughs]\n{Zoom:ExtremeOut,T=9.750}\nNext words.", pauses);
        log.Near(9.75f, StampOf(s, "{Zoom:ExtremeOut}"), 1e-3f, "voiced sound: a tag past a later measured pause isn't pulled back");
    }

    static void SameBeat_RescaledMidSectionDoesNotSnap(Log log)
    {
        // Alignment 0–60 s, audio 0–62 s: rescaled. Mid-section (> 8 s from both ends)
        // the rescaled estimate isn't trusted enough to snap, so no run is joined.
        var pauses = new List<Vector2> { new Vector2(30.8f, 31.2f) };
        string s = Shift("Word one.\n{Shrug,T=30.000}\n{Zoom:ExtremeOut,T=30.300}\nNext words.", pauses, null, 60f, 62f);
        log.Near(31.0f, StampOf(s, "{Shrug}"), 2e-3f, "rescale: mid-section the tag is only rescaled (30.0 → 31.0), not snapped");
        log.Near(31.31f, StampOf(s, "{Zoom:ExtremeOut}"), 2e-3f, "rescale: …and the tag after it keeps its own time");
    }

    static void SameBeat_EmotionJoinsTheExtremeZoomCut(Log log)
    {
        var markers = new List<TimeMarkerData>
        {
            new TimeMarkerData { triggerTime = 64.286f, emotion = "SmugLookingAtViewer" },
            new TimeMarkerData { triggerTime = 66.90f,  emotion = "Frowned" },
            new TimeMarkerData { triggerTime = 67.360f, emotion = "Shrug" },
            new TimeMarkerData { triggerTime = 68.194f, emotion = "Neutral" },
        };
        var cuts = new List<float> { 64.287f, 67.487f };
        var moved = HybridAvatarSystem.ShareExtremeZoomCutFrames(markers, cuts, 0.4f);
        TimeMarkerData Find(string e) => markers.Find(m => m.emotion == e);
        log.Near(67.487f, Find("Shrug").triggerTime, 1e-4f, "emotion on a cut: 0.13 s before the ExtremeOut it takes the cut's time");
        log.Near(64.287f, Find("SmugLookingAtViewer").triggerTime, 1e-4f, "emotion on a cut: 1 ms before the ExtremeIn too");
        log.Near(66.90f, Find("Frowned").triggerTime, 1e-4f, "emotion on a cut: 0.59 s away it is left alone");
        log.Near(68.194f, Find("Neutral").triggerTime, 1e-4f, "emotion on a cut: …and so is one 0.71 s after it");
        log.AreEqual(2, moved.Count, "emotion on a cut: two moved, each logged");
        bool sorted = true;
        for (int i = 1; i < markers.Count; i++) sorted &= markers[i].triggerTime >= markers[i - 1].triggerTime;
        log.IsTrue(sorted, "emotion on a cut: the list stays in time order for the tracker");
        log.AreEqual(0, HybridAvatarSystem.ShareExtremeZoomCutFrames(markers, cuts, 0f).Count, "emotion on a cut: window 0 = off");
    }

    // A section's caption track as SegmentSequencer builds it, from the shifted script and its map.
    static CaptionTrack Rec03Track(string shifted, Func<float, bool, float> map, float delta)
    {
        var take = new List<(float From, float To)>();
        foreach (Vector2 p in Rec03Pauses()) take.Add((p.x + delta, p.y + delta));
        var section = new CaptionBuilder.Section
        {
            Index = 2, Script = shifted, Words = FixtureWords("03_THE_RECORD"), Map = map, Pauses = take,
            SpeechEnd = Rec03RealEnd + delta,
        };
        return CaptionBuilder.Build(new[] { section });
    }

    static void SameBeat_CaptionWordsDontMove(Log log)
    {
        const float delta = 75.24f, floorT = 75.27f;
        string script = Fixture("03_THE_RECORD_timed.txt");
        string joined = SegmentSequencer.ShiftSectionForTest(script, delta, floorT, Rec03AlignStart, Rec03AlignEnd,
            Rec03RealStart, Rec03RealEnd, Rec03Pauses(), null, out var map);
        // The pre-fix stamps: every T= mapped on its own, the same way.
        string apart = Regex.Replace(script, @"T=(\d+(?:\.\d+)?)", m =>
            "T=" + map(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), true).ToString("F3", CultureInfo.InvariantCulture));

        CaptionTrack a = Rec03Track(joined, map, delta), b = Rec03Track(apart, map, delta);
        var wa = new List<CaptionWord>(); foreach (var p in a.Phrases) wa.AddRange(p.Words);
        var wb = new List<CaptionWord>(); foreach (var p in b.Phrases) wb.AddRange(p.Words);
        bool same = wa.Count == wb.Count && wa.Count > 0;
        for (int i = 0; same && i < wa.Count; i++)
            same = wa[i].Text == wb[i].Text && wa[i].Start == wb[i].Start && wa[i].End == wb[i].End && wa[i].RawStart == wb[i].RawStart;
        log.IsTrue(same, $"captions: joining the tags moves no caption word ({wa.Count} words, same start and end)");

        // The T= fallback spans: in order, never backwards, and the words after the
        // ExtremeOut now start their guess on the real onset, not 0.13 s into the line.
        var words = CaptionBuilder.Narration(joined, 2, out var stamps);
        CaptionBuilder.ApplyFallback(words, stamps, Rec03RealEnd + delta);
        bool ordered = true;
        for (int i = 0; i < words.Count; i++)
        {
            ordered &= words[i].FallbackEnd >= words[i].FallbackStart - 1e-4f;
            if (i > 0) ordered &= words[i].FallbackStart >= words[i - 1].FallbackStart - 1e-3f;
        }
        log.IsTrue(ordered, "captions: the T= fallback spans stay in order and never run backwards");
        CaptionWord so = words.Find(w => w.Text == "So");
        log.Near(67.34f + delta, so != null ? so.FallbackStart : float.NaN, 2e-3f,
            "captions: the fallback guess for \"So if buyers…\" starts on the onset the run now shares");
    }
}

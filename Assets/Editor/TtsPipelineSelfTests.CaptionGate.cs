using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using MugsTech.Captions;
using UnityEngine;

/// <summary>
/// The caption gate, decided per phrase (MugsTech.Captions.CaptionGate): a
/// phrase starts only on its first word and only with nothing due before its
/// last; it stays up until its End; anything unannounced still hides it at
/// once, for good; the 0.4 s hold-off. Then a replay of the 2026-11-10
/// Cybertruck take's section 03 (75–146 s) against its own markers: no phrase
/// starts more than 0.25 s after its first word, none is up for less than
/// 0.6 s, and the old per-frame gate (1.2 s hold-off, whatever phrase covers
/// the moment) is shown failing both on the same frames.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    static void RunCaptionGateGroup(Log log)
    {
        Gate_StartsOnItsFirstWordAndStaysUntilItsEnd(log);
        Gate_LateIsSkippedWhole(log);
        Gate_LooksAhead(log);
        Gate_StopInTheBridgeIsNotAnInterruption(log);
        Gate_UnannouncedArrivalHidesForGood(log);
        Gate_HoldOff(log);
        Gate_ExcludedNeverShows(log);
        Gate_CybertruckReplay(log);
    }

    const float GateFrame = 1f / 30f;

    sealed class Shown
    {
        public CaptionPhrase P;
        public float From, To;
    }

    // Drives the gate at 30 fps over [from, to). bare(t) = nothing else on the frame; stop(t) = next thing due after t.
    static List<Shown> Drive(CaptionGate g, CaptionTrack track, float from, float to,
                             Func<float, bool> bare, Func<float, float> stop)
    {
        var shown = new List<Shown>();
        Shown cur = null;
        for (int f = 0; from + f * GateFrame < to; f++)
        {
            float t = from + f * GateFrame;
            CaptionPhrase p = g.Step(GateFrame, bare(t), t, track, stop);
            if (cur != null && p != cur.P) { cur.To = t; cur = null; }
            if (p != null && cur == null) { cur = new Shown { P = p, From = t, To = float.NaN }; shown.Add(cur); }
        }
        if (cur != null) cur.To = to;
        g.Finish(to);
        return shown;
    }

    static CaptionTrack Track(params CaptionPhrase[] ps)
    {
        var list = new List<CaptionPhrase>(ps);
        CaptionBuilder.Windows(list);
        var t = new CaptionTrack();
        t.Phrases.AddRange(list);
        return t;
    }

    static readonly Func<float, float> Nothing = _ => float.PositiveInfinity;

    static void Gate_StartsOnItsFirstWordAndStaysUntilItsEnd(Log log)
    {
        CaptionPhrase p = Phrase(2f, 3f);
        var g = new CaptionGate();
        var shown = Drive(g, Track(p), 0f, 5f, _ => true, Nothing);
        log.IsTrue(shown.Count == 1 && shown[0].From - p.Start < GateFrame + 1e-4f,
            $"gate: a phrase on a bare frame appears on its first word ({(shown.Count > 0 ? shown[0].From : float.NaN):F3} for {p.Start:F2})");
        log.IsTrue(shown.Count == 1 && Mathf.Abs(shown[0].To - p.End) < GateFrame + 1e-4f,
            $"gate: …and stays until its End, the linger included ({(shown.Count > 0 ? shown[0].To : float.NaN):F3} for {p.End:F2})");
        log.Near(3f, p.SpokenEnd, 1e-4f, "gate: SpokenEnd is the last word's end, before the 150 ms linger");
        log.IsTrue(g.ShortestShownSeconds >= 1.1f, $"gate: it was up {g.ShortestShownSeconds:F2} s");
    }

    static void Gate_LateIsSkippedWhole(Log log)
    {
        // The frame comes free at 2.1 s; the hold-off opens the gate at 2.5 s, 0.5 s into a phrase that began at 2.0.
        CaptionPhrase a = Phrase(2f, 3.5f), b = Phrase(4.4f, 5.4f);
        var g = new CaptionGate();
        var shown = Drive(g, Track(a, b), 0f, 7f, t => t >= 2.1f, Nothing);
        log.IsTrue(!shown.Exists(s => s.P == a), "gate: a phrase the gate opened on halfway through is not shown at all");
        log.AreEqual(1, g.SkippedLate, "gate: …and counted as skipped late");
        log.IsTrue(shown.Exists(s => s.P == b && s.From - b.Start < GateFrame + 1e-4f), "gate: the next phrase appears on its own first word");

        // Within the tolerance: free at 1.75, open at 2.15 — 0.15 s after the first word.
        CaptionPhrase c = Phrase(2f, 3.5f);
        var g2 = new CaptionGate();
        var shown2 = Drive(g2, Track(c), 0f, 5f, t => t >= 1.75f, Nothing);
        log.IsTrue(shown2.Count == 1 && shown2[0].From - c.Start <= CaptionGate.StartToleranceSeconds + 1e-4f,
            $"gate: up to {CaptionGate.StartToleranceSeconds:F1} s after its first word a phrase may still start ({(shown2.Count > 0 ? shown2[0].From - c.Start : float.NaN):F2} s)");
    }

    static void Gate_LooksAhead(Log log)
    {
        // A card is due at 2.6, before the phrase's last word (3.0): the phrase isn't started.
        CaptionPhrase a = Phrase(2f, 3f), b = Phrase(4.5f, 5.5f);
        var g = new CaptionGate();
        Func<float, float> stop = t => t < 2.6f ? 2.6f : float.PositiveInfinity;
        var shown = Drive(g, Track(a, b), 0f, 7f, t => t < 2.6f || t >= 3.5f, stop);
        log.IsTrue(!shown.Exists(s => s.P == a), "gate: a phrase with a card due before its last word never starts");
        log.AreEqual(1, g.SkippedAhead, "gate: …counted as skipped ahead");
        log.IsTrue(shown.Exists(s => s.P == b), "gate: the next phrase, with nothing due, shows");
        log.AreEqual(0, g.Interrupted, "gate: nothing was cut short");
    }

    static void Gate_StopInTheBridgeIsNotAnInterruption(Log log)
    {
        // Two phrases 0.4 s apart are bridged (the first stays until the second);
        // a card due at 3.2 lands in the bridge, after the first phrase's words.
        CaptionPhrase a = Phrase(2f, 3f), b = Phrase(3.4f, 4.4f);
        var g = new CaptionGate();
        Func<float, float> stop = t => t < 3.2f ? 3.2f : float.PositiveInfinity;
        var shown = Drive(g, Track(a, b), 0f, 6f, t => t < 3.2f, stop);
        log.IsTrue(shown.Count == 1 && shown[0].P == a && Mathf.Abs(shown[0].To - 3.2f) < GateFrame + 1e-4f,
            "gate: a phrase whose words end before the next stop is shown whole, until the card takes the frame");
        log.AreEqual(0, g.Interrupted, "gate: a card in the bridge, after the last word, is not an interruption");
    }

    static void Gate_UnannouncedArrivalHidesForGood(Log log)
    {
        CaptionPhrase a = Phrase(2f, 3f);
        var g = new CaptionGate();
        var shown = Drive(g, Track(a), 0f, 4f, t => t < 2.5f || t >= 2.6f, Nothing);
        log.IsTrue(shown.Count == 1 && Mathf.Abs(shown[0].To - 2.5f) < GateFrame + 1e-4f, "gate: something unannounced hides the phrase on its own frame");
        log.AreEqual(1, g.Interrupted, "gate: …counted as interrupted");
        log.AreEqual(1, shown.Count, "gate: …and the phrase never comes back halfway through");
    }

    static void Gate_HoldOff(Log log)
    {
        log.Near(0.4f, CaptionGate.HoldOffSeconds, 1e-6f, "gate: the hold-off is 0.4 s (a card's 0.3 s fade-out + 3 frames)");
        // Free from 1.0: a phrase at 1.3 waits for the hold-off (1.4) and starts 0.1 s late; one at 1.1 would be 0.3 s late.
        CaptionPhrase a = Phrase(1.3f, 2.5f);
        var g = new CaptionGate();
        var shown = Drive(g, Track(a), 0f, 3f, t => t >= 1.0f, Nothing);
        log.IsTrue(shown.Count == 1 && shown[0].From >= 1.4f - 1e-3f && shown[0].From < 1.4f + GateFrame + 1e-3f,
            $"gate: not before the frame has been bare 0.4 s ({(shown.Count > 0 ? shown[0].From : float.NaN):F3})");
        CaptionPhrase b = Phrase(1.1f, 2.5f);
        var g2 = new CaptionGate();
        var shown2 = Drive(g2, Track(b), 0f, 3f, t => t >= 1.0f, Nothing);
        log.IsTrue(shown2.Count == 0 && g2.SkippedLate == 1, "gate: a phrase starting 0.1 s after the frame frees is skipped (0.3 s late by the time it may show)");
        // Two cards 0.3 s apart never let a caption in between.
        CaptionPhrase c = Phrase(2.0f, 2.25f);
        var g3 = new CaptionGate();
        var shown3 = Drive(g3, Track(c), 0f, 3f, t => !(t >= 1.9f && t < 2.2f) && !(t >= 2.5f), Nothing);
        log.AreEqual(0, shown3.Count, "gate: a short gap between two cards never flickers a caption in");
    }

    static void Gate_ExcludedNeverShows(Log log)
    {
        CaptionPhrase a = Phrase(2f, 3f);
        a.Excluded = true;
        var g = new CaptionGate();
        log.AreEqual(0, Drive(g, Track(a), 0f, 4f, _ => true, Nothing).Count, "gate: the cold open's first line never shows");
    }

    // ---------------------------------------------------------------------
    // The Cybertruck take, section 03 (75.24 s in), replayed
    // ---------------------------------------------------------------------

    // What the frame holds there, from the section's shifted script, the way
    // MediaPresentationSystem plays it: each card from its tag to its End plus
    // the 0.3 s fade-out; the held image; the presenter's 1 s smooth glides; the
    // close-up from ExtremeIn to ExtremeOut; the opening Shutter (0.68 s, laid
    // 0.15 s in front of the first word) and its chapter title 0.2 s after the
    // reveal, up for 3.05 s. Stops are what NextCaptionStop lists: each of those
    // starting, every card event and every zoom.
    sealed class FrameModel
    {
        public readonly List<(float From, float To, string What)> Busy = new List<(float, float, string)>();
        public readonly List<float> Stops = new List<float>();
        public bool Bare(float t)
        {
            foreach (var b in Busy) if (t >= b.From && t < b.To) return false;
            return true;
        }
        public float NextStop(float t)
        {
            float n = float.PositiveInfinity;
            foreach (float s in Stops) if (s > t && s < n) n = s;
            return n;
        }
        public string BusyAt(float t)
        {
            foreach (var b in Busy) if (t >= b.From && t < b.To) return b.What;
            return null;
        }
    }

    static float T(Match m, int group) => float.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    static FrameModel Rec03Frame(string shifted, float firstWord)
    {
        var f = new FrameModel();
        void Add(float from, float to, string what) { f.Busy.Add((from, to, what)); f.Stops.Add(from); }

        // The opening Shutter and its chapter title.
        float cover = firstWord - 0.15f - 0.68f;
        f.Busy.Add((float.NegativeInfinity, cover, "the section before"));
        Add(cover, firstWord - 0.15f, "{Transition:Shutter}");
        Add(firstWord + 0.05f, firstWord + 0.05f + 3.05f, "chapter title");

        // Cards: the real parser, on the script with the non-card tags stripped the way MediaPresentationSystem does first.
        string cardsOnly = Regex.Replace(shifted, @"\{(?:Timestamp|Transition|Mood|Position|Zoom|Black|Image|Video|Pause|Echo):[^{}\n]*\}", "");
        var events = ContentZoneTagParser.ParseContentTags(cardsOnly, 400f).Item2;
        events.Sort((a, b) => a.triggerTime.CompareTo(b.triggerTime));
        foreach (ContentCardEvent e in events)
        {
            f.Stops.Add(e.triggerTime);
            if (e.dismissesCard) continue;
            ContentCardEvent end = events.Find(x => x.dismissesCard && x.cardType == e.cardType && x.triggerTime >= e.triggerTime);
            float until = end != null ? end.triggerTime : e.triggerTime + (e.duration > 0f ? e.duration : 3f);
            Add(e.triggerTime, until + 0.3f, "{" + e.cardType + "}");
        }

        // The held image.
        Match img = Regex.Match(shifted, @"\{Image:(\w+),T=(\d+(?:\.\d+)?),Start[^{}]*\}");
        Match imgEnd = Regex.Match(shifted, @"\{Image:End,T=(\d+(?:\.\d+)?)");
        if (img.Success && imgEnd.Success) Add(T(img, 2), T(imgEnd, 1), "{Image:" + img.Groups[1].Value + "}");

        // Smooth glides (a change of side without ,Cut). The opener's {Position:Center} rides the transition.
        string at = "Center";
        foreach (Match m in Regex.Matches(shifted, @"\{Position:(\w+)((?:,\w+)*),T=(\d+(?:\.\d+)?)\}"))
        {
            if (T(m, 3) < firstWord) continue;
            bool cut = m.Groups[2].Value.Contains("Cut");
            if (m.Groups[1].Value != at && !cut) Add(T(m, 3), T(m, 3) + 1.0f, "{Position:" + m.Groups[1].Value + "} glide");
            at = m.Groups[1].Value;
        }

        // The close-up.
        Match zin = Regex.Match(shifted, @"\{Zoom:ExtremeIn,T=(\d+(?:\.\d+)?)\}");
        Match zout = Regex.Match(shifted, @"\{Zoom:ExtremeOut,T=(\d+(?:\.\d+)?)\}");
        if (zin.Success && zout.Success) { Add(T(zin, 1), T(zout, 1), "close-up"); f.Stops.Add(T(zout, 1)); }
        return f;
    }

    // The gate as it was until 2026-11-10: per frame, open after 1.2 s of bare
    // frame, showing whatever phrase covers the moment.
    static List<Shown> DriveOldGate(CaptionTrack track, float from, float to, Func<float, bool> bare)
    {
        var shown = new List<Shown>();
        Shown cur = null;
        float bareFor = 0f;
        for (int f = 0; from + f * GateFrame < to; f++)
        {
            float t = from + f * GateFrame;
            bool b = bare(t);
            bareFor = b ? bareFor + GateFrame : 0f;
            CaptionPhrase p = b && bareFor >= 1.2f ? track.At(t) : null;
            if (cur != null && p != cur.P) { cur.To = t; cur = null; }
            if (p != null && cur == null) { cur = new Shown { P = p, From = t, To = float.NaN }; shown.Add(cur); }
        }
        if (cur != null) cur.To = to;
        return shown;
    }

    // The section's three {Pause} beats as PauseInserter found them in the take
    // (each tag's measured gap; v3 had left at least N in all three, so nothing
    // was inserted): their tags pinned to the gaps' edges.
    static MugsTech.Tts.PauseInserter.Shift Rec03PauseShift(string script)
    {
        var splices = new List<MugsTech.Tts.PauseInserter.Splice>();
        foreach (var tag in MugsTech.Tts.PauseInserter.FindTags(script))
        {
            if (tag.OnEdge) continue;
            Vector2 gap = default;
            float best = float.PositiveInfinity;
            foreach (Vector2 p in Rec03Pauses())
            {
                float d = tag.T < p.x ? p.x - tag.T : tag.T > p.y ? tag.T - p.y : 0f;
                if (d < best) { best = d; gap = p; }
            }
            splices.Add(new MugsTech.Tts.PauseInserter.Splice
            {
                TagIndex = tag.Index, TagEnd = tag.Index + tag.Length, GroupStart = tag.GroupStart, GroupEnd = tag.GroupEnd,
                GapStart = gap.x, GapEnd = gap.y, At = (gap.x + gap.y) * 0.5f, Requested = tag.Seconds, Inserted = 0f,
            });
        }
        return new MugsTech.Tts.PauseInserter.Shift(splices);
    }

    // The section's T= as the take before this fix had them: every T= mapped on
    // its own (pause snap, {Pause} pins), no run joined.
    static string Rec03BeforeTheFix(string script, float delta, float floorT, MugsTech.Tts.PauseInserter.Shift shift)
        => Regex.Replace(script, @"T=(\d+(?:\.\d+)?)", m =>
        {
            float t = shift.Resolve(m.Index, T(m, 1), x => SnapOnly(x, Rec03Pauses()));
            return "T=" + Mathf.Max(floorT, t + delta).ToString("F3", CultureInfo.InvariantCulture);
        });

    static void Gate_CybertruckReplay(Log log)
    {
        const float delta = 75.24f, floorT = 75.27f;
        string script = Fixture("03_THE_RECORD_timed.txt");
        var pins = Rec03PauseShift(script);
        log.AreEqual(3, pins.Splices.Count, "cybertruck replay: the section's three {Pause} beats are pinned as in the take");
        string shifted = SegmentSequencer.ShiftSectionForTest(script, delta, floorT, Rec03AlignStart, Rec03AlignEnd,
            Rec03RealStart, Rec03RealEnd, Rec03Pauses(), pins, out var map);
        CaptionTrack track = Rec03Track(shifted, map, delta);
        FrameModel frame = Rec03Frame(shifted, floorT);
        float from = floorT - 1.5f, to = Rec03RealEnd + delta + 1f;

        var g = new CaptionGate();
        List<Shown> shown = Drive(g, track, from, to, frame.Bare, frame.NextStop);
        float worstLate = 0f, shortest = float.PositiveInfinity;
        foreach (Shown s in shown)
        {
            worstLate = Mathf.Max(worstLate, s.From - s.P.Start);
            shortest = Mathf.Min(shortest, s.To - s.From);
            Debug.Log($"[Captions]   replay 03 shown {s.From,7:F2}–{s.To,7:F2} (first word {s.P.Start,7:F2})  {s.P.Text}");
        }
        Debug.Log($"[Captions] replay 03: {track.Phrases.Count} phrases, {g.Shown} shown, {g.SkippedLate} skipped late, " +
                  $"{g.SkippedAhead} skipped ahead, {g.Interrupted} interrupted, shortest {g.ShortestShownSeconds:F2} s.");
        log.IsTrue(shown.Count >= 3, $"cybertruck replay: captions still show in the section's bare stretches ({shown.Count} of {track.Phrases.Count})");
        log.IsTrue(worstLate <= 0.25f, $"cybertruck replay: no phrase starts more than 0.25 s after its first word (worst {worstLate:F2} s)");
        log.IsTrue(shortest >= 0.6f, $"cybertruck replay: none is up for less than 0.6 s (shortest {shortest:F2} s)");
        log.AreEqual(0, g.Interrupted, "cybertruck replay: nothing in the marker list cuts a phrase short");
        log.IsTrue(g.ShortestShownSeconds >= 0.6f, $"cybertruck replay: the report's shortest agrees ({g.ShortestShownSeconds:F2} s)");

        foreach (CaptionPhrase p in track.Phrases)
            if (!shown.Exists(s => s.P == p))
                Debug.Log($"[Captions]   replay 03 not shown {p.Start,7:F2}–{p.End,7:F2}  {p.Text}   (frame: {frame.BusyAt(p.Start) ?? "bare"})");

        // The take as it was: the old gate, on the stamps before the fix.
        string before = Rec03BeforeTheFix(script, delta, floorT, pins);
        CaptionTrack oldTrack = Rec03Track(before, map, delta);
        FrameModel oldFrame = Rec03Frame(before, floorT);
        List<Shown> old = DriveOldGate(oldTrack, from, to, oldFrame.Bare);
        foreach (Shown s in old)
            Debug.Log($"[Captions]   replay 03 OLD gate shown {s.From,7:F2}–{s.To,7:F2} (first word {s.P.Start,7:F2})  {s.P.Text}");

        // "So if buyers stopped showing up" (2:22–2:24): the close-up ends on the
        // line's onset. The old gate put it up 1.2 s later, mid-line (~144.0 in
        // the take); now it is either up within 0.25 s of its first word or not at all.
        CaptionPhrase buyers = track.Phrases.Find(p => p.Text.StartsWith("So if buyers"));
        Shown nowBuyers = shown.Find(s => s.P == buyers), oldBuyers = old.Find(s => s.P.Text == buyers?.Text);
        log.IsTrue(buyers != null && (nowBuyers == null || nowBuyers.From - buyers.Start <= 0.25f),
            $"cybertruck replay: \"So if buyers…\" is up on its first word or not at all (at {nowBuyers?.From:F2}, first word {buyers?.Start:F2})");
        log.IsTrue(oldBuyers != null && oldBuyers.From - buyers.Start > 1f,
            $"cybertruck replay: …where the old gate put it up {oldBuyers?.From - buyers?.Start:F2} s late (the take: ~144.0)");
        int oldLate = 0, oldShort = 0;
        foreach (Shown s in old)
        {
            if (s.From - s.P.Start > 0.25f) oldLate++;
            if (s.To - s.From < 0.6f) oldShort++;
        }
        Debug.Log($"[Captions] replay 03, old gate: {old.Count} shown, {oldLate} started mid-phrase, {oldShort} up < 0.6 s.");
        log.IsTrue(oldLate > 0, $"cybertruck replay: the old per-frame gate starts phrases mid-line on these frames ({oldLate})");
    }
}

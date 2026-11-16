using System;
using System.Collections.Generic;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The evidence-first stage's tags, as the TTS side sees them: every new tag
/// and keyword is a KNOWN marker (not just swept up by the catch-all), is
/// stamped the one way both processors agree on, and un-stamps back to exactly
/// what was written — so Update Tags Only can move them freely. Pure; runs
/// offline. (The same table is checked against elevenlabs_tts_processor.py
/// when it is edited: both must give these stamps byte for byte.)
/// </summary>
public static partial class TtsPipelineSelfTests
{
    // (as written, as stamped at T=1.234)
    static readonly (string tag, string stamped)[] Tier4Tags =
    {
        (@"{BigMedia:Google}",
         @"{BigMedia:Google,T=1.234,D=0}"),
        (@"{BigMedia:End}",
         @"{BigMedia:End,T=1.234,D=0}"),
        (@"{BigMedia:Strike,Brave}",
         @"{BigMedia:Strike,Brave,T=1.234}"),
        (@"{BigMedia:Strike,4}",
         @"{BigMedia:Strike,T=1.234,D=4}"),
        (@"{BigMedia:Google+Brave+X,4}",
         @"{BigMedia:Google+Brave+X,T=1.234,D=4}"),
        (@"{Image:page,Start,Scroll}",
         @"{Image:page,T=1.234,Start,Scroll}"),
        (@"{Image:page,8,Scroll,Right}",
         @"{Image:page,T=1.234,D=8,Scroll,Right}"),
        (@"{Image:page,Start,Right}",
         @"{Image:page,T=1.234,Start,Right}"),
        (@"{BigImage:page,8,Scroll}",
         @"{BigImage:page,T=1.234,D=8,Scroll}"),
        (@"{BigImage:page,8}",
         @"{BigImage:page,T=1.234,D=8}"),
        (@"{Name:""Sarah Mitchell"",""VP of Communications, MegaCorp"",Start}",
         @"{Name:""Sarah Mitchell"",""VP of Communications, MegaCorp"",T=1.234,D=0}"),
        (@"{Name:""Jensen Huang"",""Nvidia CEO"",4}",
         @"{Name:""Jensen Huang"",""Nvidia CEO"",T=1.234,D=4}"),
        (@"{Name:End}",
         @"{Name:End,T=1.234}"),
        (@"{Quote:""We believe transparency is at the core."",""Sarah Mitchell"",""VP of Communications, MegaCorp"",Start,Photo=ceo_mitchell}",
         @"{Quote:""We believe transparency is at the core."",""Sarah Mitchell"",""VP of Communications, MegaCorp"",T=1.234,D=0,Photo=ceo_mitchell}"),
        (@"{Quote:""q"",""n"",""r"",5,Photo=jensen-huang,Right}",
         @"{Quote:""q"",""n"",""r"",T=1.234,D=5,Photo=jensen-huang,Right}"),
        (@"{Quote:""q"",""n"",""r"",5,Right}",
         @"{Quote:""q"",""n"",""r"",T=1.234,D=5,Right}"),
        (@"{Clipping:""Apple Store Subscription Sign-Up Controversy Exposed"",""Subscription Sign-Up"",""MacRumors · Nov. 8""}",
         @"{Clipping:""Apple Store Subscription Sign-Up Controversy Exposed"",""Subscription Sign-Up"",""MacRumors · Nov. 8"",T=1.234}"),
        (@"{Clipping:End}",
         @"{Clipping:End,T=1.234}"),
        (@"{Mark:""under false pretenses""}",
         @"{Mark:""under false pretenses"",T=1.234}"),
        (@"{Timeline:""Oct. 25"",""Rossmann's first video"",Wide}",
         @"{Timeline:""Oct. 25"",""Rossmann's first video"",T=1.234,D=0,Wide}"),
        (@"{Timeline:""1"",""Call the trial a gift""}",
         @"{Timeline:""1"",""Call the trial a gift"",T=1.234,D=0}"),
        (@"{Timeline:Span,""Two weeks""}",
         @"{Timeline:Span,""Two weeks"",T=1.234}"),
        (@"{Timeline:End}",
         @"{Timeline:End,T=1.234}"),
        (@"{Pop:""On their device.""}",
         @"{Pop:""On their device."",T=1.234}"),
        (@"{Pop:End}",
         @"{Pop:End,T=1.234}"),
        (@"{Graph:""Apple Services revenue"",""Last quarter=$31.6B;A year earlier=$28.75B"",""Apple quarterly results"",Start,Bars}",
         @"{Graph:""Apple Services revenue"",""Last quarter=$31.6B;A year earlier=$28.75B"",""Apple quarterly results"",T=1.234,D=0,Bars}"),
        (@"{Graph:""Example trend"",""Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M"",""Source"",8,Line,Cropped}",
         @"{Graph:""Example trend"",""Jan=2.1M;Feb=2.4M;Mar=3.9M;Apr=4.0M"",""Source"",T=1.234,D=8,Line,Cropped}"),
        (@"{Graph:""Example share"",""A=69%;B=16%;C=1%"",""Source"",6}",
         @"{Graph:""Example share"",""A=69%;B=16%;C=1%"",""Source"",T=1.234,D=6}"),
        (@"{Graph:End}",
         @"{Graph:End,T=1.234}"),
        (@"{Position:Off,Cut}",
         @"{Position:Off,Cut,T=1.234}"),
        (@"{Position:PeekLeft}",
         @"{Position:PeekLeft,T=1.234}"),
    };

    static void RunTier4TagGroup(Log log)
    {
        Tier4_TagsAreKnownStampedAndUnstamped(log);
        Tier4_TagsAreNeverEmotionsOrSpoken(log);
        Tier4_TagOnlyAcceptsTheNewTags(log);
        Tier4_GraphDataIsNeverAStamp(log);
    }

    static void Tier4_TagsAreKnownStampedAndUnstamped(Log log)
    {
        foreach (var (tag, stamped) in Tier4Tags)
        {
            log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(tag), $"tags: {tag} is a known marker");

            string raw = "Some words " + tag + " and more words.";
            var (markers, clean) = TtsScriptProcessor.ExtractMarkers(raw);
            log.AreEqual(1, markers.Count, $"tags: {tag} is one marker");
            log.AreEqual("Some words and more words.", clean, $"tags: {tag} leaves nothing in the spoken text");
            if (markers.Count != 1) continue;

            var timed = new List<TtsScriptProcessor.TimedMarker>
            {
                new TtsScriptProcessor.TimedMarker { Marker = markers[0], TriggerTime = 1.234f },
            };
            string rebuilt = TtsScriptProcessor.RebuildTimedScript(raw, timed);
            log.AreEqual("Some words " + stamped + " and more words.", rebuilt, $"tags: {tag} is stamped as agreed");

            var times = new List<float>();
            string back = TagOnlyRetimer.Unstamp(rebuilt, times);
            log.AreEqual(raw, back, $"tags: Unstamp(Stamp({tag})) gives it back as written");
            log.IsTrue(times.Count == 1 && Mathf.Abs(times[0] - 1.234f) < 1e-4f, $"tags: {tag} gives its T= back");
        }
    }

    static void Tier4_TagsAreNeverEmotionsOrSpoken(Log log)
    {
        foreach (var (tag, stamped) in Tier4Tags)
        {
            // The avatar's emotion regex scans the script after the colon tags
            // are stripped — but none of these may match it even if one is left in.
            var m = HybridAvatarSystem.EmotionTagRegex.Match(stamped);
            log.IsTrue(!m.Success || m.Length != stamped.Length, $"tags: {stamped} is not an emotion");
            var m2 = HybridAvatarSystem.EmotionTagRegex.Match(tag);
            log.IsTrue(!m2.Success || m2.Length != tag.Length, $"tags: {tag} is not an emotion");
        }

        // Misspelt forms are still stripped (the catch-all) — just not known.
        foreach (string typo in new[] { "{Pop:On their device}", "{Mark:phrase}", "{Clipping:\"only one field\"}",
                                        "{Graph:\"t\",\"a=1\",\"s\"}", "{Name:\"n\",\"r\"}", "{BigMedia:Strike,}" })
        {
            var (markers, clean) = TtsScriptProcessor.ExtractMarkers("Before " + typo + " after.");
            log.IsTrue(!clean.Contains("{") && !clean.Contains("}"), $"tags: a malformed {typo} is never read aloud");
            log.IsTrue(markers.Count == 1 && !TtsScriptProcessor.IsRecognisedMarker(markers[0].Text),
                $"tags: …and is reported as unrecognised ({typo})");
        }
    }

    // An existing voice, re-tagged with every new device: the retimer takes the
    // edit (they are ordinary, unlocked tags) and a second pass changes nothing.
    static void Tier4_TagOnlyAcceptsTheNewTags(Log log)
    {
        const string original =
            "## THE GIFT\n" +
            "{Position:Center}\n" +
            "The button lives on their device. Three outlets reported it. One of them called it a gift.\n" +
            "{Zoom:In}\n" +
            "First. Call the trial a gift. Second. Rush past the bill. On their device.\n";
        var gen = TagGeneration(original, keepCues: false);

        string edited =
            "## THE GIFT\n" +
            "{Position:Off}\n" +
            "{Clipping:\"Apple Store Sign-Up Controversy Exposed\",\"Sign-Up\",\"MacRumors\"}\n" +
            "The button lives on their device. {Mark:\"Controversy\"}Three outlets reported it. {Clipping:End}" +
            "{Name:\"Louis Rossmann\",\"repair shop owner\",Start}One of them {Position:PeekLeft}called it a gift.\n" +
            "{Name:End}\n" +
            "{Timeline:\"1\",\"Call the trial a gift\",Wide}\n" +
            "First. Call the trial a gift. {Timeline:\"2\",\"Rush past the bill\"}Second. Rush past the bill. " +
            "{Timeline:Span,\"Two moves\"}{Timeline:End}{Pop:\"On their device.\"}On their device.\n" +
            "{Pop:End}\n";

        var r = TagOnlyRetimer.Retime(edited, gen);
        log.IsTrue(r.Success, $"tag-only: a script re-tagged with the new devices is accepted ({r.Error})");
        if (!r.Success) return;
        log.AreEqual(0, r.Warnings.FindAll(w => w.Contains("isn't a tag") || w.Contains("unrecognised") || w.Contains("not recognised")).Count,
            "tag-only: none of the new tags is reported as unrecognised");
        string timed = r.Sections[0].TimedScript;
        foreach (string needle in new[] { "{Position:Off,T=", "{Clipping:\"Apple", "\"MacRumors\",T=", "{Mark:\"Controversy\",T=", "{Clipping:End,T=",
                                          "{Name:\"Louis Rossmann\",\"repair shop owner\",T=", ",D=0}", "{Position:PeekLeft,T=", "{Name:End,T=",
                                          ",D=0,Wide}", "{Timeline:Span,\"Two moves\",T=", "{Pop:\"On their device.\",T=", "{Pop:End,T=" })
            log.IsTrue(timed.Contains(needle), $"tag-only: the rebuilt _timed.txt carries {needle}…");

        string roundTrip = TagOnlyRetimer.Unstamp(timed, null);
        log.AreEqual(TtsScriptProcessor.SplitIntoSegments(edited)[0].Raw, roundTrip, "tag-only: and un-stamps back to the edited script");

        // Re-applying the same edit is a no-op.
        var gen2 = new List<TagOnlyRetimer.Section>();
        for (int i = 0; i < gen.Count; i++)
            gen2.Add(new TagOnlyRetimer.Section
            {
                Slug = gen[i].Slug, Name = gen[i].Name, TimedScript = r.Sections[i].TimedScript, Spoken = gen[i].Spoken,
                StageDirectionsVoiced = gen[i].StageDirectionsVoiced, TimeAt = gen[i].TimeAt, Approximate = gen[i].Approximate,
            });
        var again = TagOnlyRetimer.Retime(edited, gen2);
        log.IsTrue(again.Success && again.ChangedCount == 0, "tag-only: applying it twice rewrites nothing the second time");
    }

    // A {Graph:}'s data is "label=value": a label ending in a capital T followed
    // by a number ("ChatGPT=400M") must never be read as a baked T= stamp, or
    // SegmentSequencer would shift the VALUE onto the take's clock.
    static void Tier4_GraphDataIsNeverAStamp(Log log)
    {
        const string graph = "{Graph:\"AI users\",\"ChatGPT=400M;AT&T=22M;MSFT=3.1T\",\"Source\",T=3.000,D=0,Bars}word";
        var stamps = MugsTech.Tts.TagBeats.Find(graph);
        log.AreEqual(1, stamps.Count, "graph data: only the tag's own T= is a stamp (not ChatGPT=400M, AT&T=22M)");
        log.IsTrue(stamps.Count == 1 && Mathf.Abs(stamps[0].T - 3f) < 1e-4f, "graph data: …and it is the 3.000 the processor baked");

        log.AreEqual(1, MugsTech.Tts.TagBeats.Find("{Headline:\"Why GPT=4 matters, T=0 hour\",\"The Verge\",T=4.291,D=0,bigCenter}x").Count,
            "stamps: a T= inside any quoted card field is text, not a stamp");
        log.AreEqual(0, MugsTech.Tts.TagBeats.Find("He said the cost at T=5 was fine.").Count, "stamps: narration is never a stamp");

        // Every stamp form the processors write is still found.
        const string forms =
            "[deadpan,T=0.000] a {BigText:ON THEIR DEVICE,T=58.394,D=0}b {Black:T=4.5,D=3} c {Black:3,T=4.6} d {Neutral,T=0.004} e " +
            "{Smug,Cut,T=1.25} f {Transition:Wipe,T=0.222} g {Zoom:In,Cut,T=46.0,D=4} h {Timestamp:\"Cold Open\",T=0.500} i " +
            "{Clipping:\"A said T=1\",\"said\",\"Src\",T=29.0} j {Mark:\"x\",T=3.4} k {Timeline:Span,\"Two weeks\",T=24.053} l " +
            "{Pop:\"On their device.\",T=58.4} m {Quote:\"q\",\"n\",\"r\",T=2.000,D=5,Photo=jensen-huang,Right} n " +
            "{Name:\"Jensen Huang\",\"Nvidia CEO\",T=27.4,D=0} o {Echo:\"NO KEY LEAKED?\",\"LEAKED\",T=0.100,D=3} p {Pause:0.6,T=6.24}";
        log.AreEqual(17, MugsTech.Tts.TagBeats.Find(forms).Count, "stamps: all 17 baked forms in the sample are found");

        // SegmentSequencer shifts exactly the stamps TagBeats lists (its indices are the join's keys).
        var field = typeof(SegmentSequencer).GetField("_TPattern",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pattern = field != null ? field.GetValue(null) as System.Text.RegularExpressions.Regex : null;
        log.IsTrue(pattern != null && pattern.ToString() == MugsTech.Tts.TagBeats.StampPattern,
            "stamps: SegmentSequencer shifts with TagBeats' own pattern");
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Echo;
using MugsTech.Tts;

/// <summary>
/// Tier 3 step 4 — {Echo:"HEADLINE","accent",seconds}: parse (valid, stamped,
/// six words, accent missing, duplicate, malformed, capitals and punctuation,
/// duration bounds), the TTS stamp round trip, the layout for a 1-word and a
/// 5-word headline at the width cap with the real Anton SC font (inside the
/// band, never wider than 70 %), the outline, the slide curves, and the switch.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Echo Self-Tests")]
    public static void RunEcho()
    {
        var log = new Log();
        RunEchoGroup(log);
        log.Report();
    }

    static void RunEchoGroup(Log log)
    {
        Echo_Parse(log);
        Echo_Rules(log);
        Echo_StampRoundTrip(log);
        Echo_Motion(log);
        Echo_FitMaths(log);
        Echo_LayoutWithAnton(log);
        Echo_SwitchOff(log);
    }

    const string EchoBinance = "{Echo:\"NO KEY LEAKED?\",\"LEAKED\",3}";

    static void Echo_Parse(Log log)
    {
        string script = "## COLD OPEN\n{Timestamp:\"Cold Open\"}\n{Position:Center,Cut} {Neutral}\n" + EchoBinance +
                        "\n[deadpan] More than six billion dollars.\n";
        var r = EchoTag.Parse(script);
        log.IsTrue(r.Echo != null && r.Warnings.Count == 0, "echo: the Binance tag parses with no warning");
        if (r.Echo == null) return;
        log.AreEqual("NO KEY LEAKED?", r.Echo.Plain, "echo: the headline as shown, ? kept");
        log.AreEqual(3f, r.Echo.Duration, "echo: its duration");
        log.IsTrue(float.IsNaN(r.Echo.Time), "echo: unstamped — no time");
        log.IsTrue(r.Echo.AccentShown, "echo: the accent word is found");
        log.IsTrue(r.Echo.Markup.Contains("<color=#E63946><noparse>LEAKED?</noparse></color>"),
            "echo: the whole accent token, punctuation included, takes the thumbnail red (as the thumbnail does)");
        log.IsTrue(r.Echo.Markup.StartsWith("<noparse>NO</noparse> <noparse>KEY</noparse>"), "echo: the other words stay white");
        log.IsTrue(!r.Script.Contains("{Echo") && r.Script.Contains("{Timestamp:\"Cold Open\"}") &&
                   r.Script.Contains("{Position:Center,Cut} {Neutral}\n\n[deadpan]"),
            "echo: the tag is stripped (its line left empty) and nothing else is touched");

        var stamped = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"LEAKED\",T=0.120,D=3}\nWords.");
        log.IsTrue(stamped.Echo != null && Mathf.Approximately(stamped.Echo.Time, 0.12f) && stamped.Echo.Duration == 3f,
            "echo: the stamped form (T=, D=) parses with its time");

        var lower = EchoTag.Parse("{Echo:\"who  leaked it!\",\"leaked\",2.5}");
        log.IsTrue(lower.Echo != null && lower.Echo.Plain == "WHO LEAKED IT!" && lower.Echo.AccentShown &&
                   lower.Echo.Markup.Contains("<color=#E63946><noparse>LEAKED</noparse></color>") && lower.Echo.Duration == 2.5f,
            "echo: written in lower case → capitals, spaces collapsed, ! kept, the accent matched regardless of case");

        var punct = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"LEAKED?\",3}");
        log.IsTrue(punct.Echo != null && punct.Echo.AccentShown && punct.Warnings.Count == 0,
            "echo: an accent written with its punctuation still matches");

        log.IsTrue(EchoTag.Parse("No echo here. {Neutral}").Echo == null, "echo: a script without one has none");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(EchoBinance), "echo: the emotion parser can't mistake it for an emotion");
    }

    static void Echo_Rules(Log log)
    {
        var six = EchoTag.Parse("{Echo:\"THE KEYS NEVER LEFT THE BUILDING\",\"KEYS\",3}");
        log.IsTrue(six.Echo != null && six.Echo.Words == 6, "echo rules: a 6-word headline still parses");
        log.IsTrue(six.Echo != null && !six.Echo.AccentShown && !six.Echo.Markup.Contains("<color"),
            "echo rules: 6 words → shown without the accent");
        log.IsTrue(six.Warnings.Count == 1 && six.Warnings[0].Contains("6 words"), "echo rules: …and a warning says why");

        var missing = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"STOLEN\",3}");
        log.IsTrue(missing.Echo != null && !missing.Echo.AccentShown && !missing.Echo.Markup.Contains("<color"),
            "echo rules: an accent not in the headline → shown without it");
        log.IsTrue(missing.Warnings.Count == 1 && missing.Warnings[0].Contains("STOLEN"), "echo rules: …with a warning naming it");

        var partial = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"LEAK\",3}");
        log.IsTrue(partial.Echo != null && !partial.Echo.AccentShown, "echo rules: part of a word isn't the word (as in the thumbnail)");

        var twice = EchoTag.Parse(EchoBinance + "\nWords.\n{Echo:\"SECOND\",\"SECOND\",2}\nMore.");
        log.IsTrue(twice.Echo != null && twice.Echo.Plain == "NO KEY LEAKED?", "echo rules: the first Echo wins");
        log.IsTrue(twice.Warnings.Count == 1 && twice.Warnings[0].Contains("second"), "echo rules: a second one is ignored with a warning");
        log.IsTrue(!twice.Script.Contains("{Echo"), "echo rules: …and stripped too");

        var bad = EchoTag.Parse("{Echo:\"ONLY A HEADLINE\",3}\nWords.");
        log.IsTrue(bad.Echo == null && bad.Warnings.Count == 1 && !bad.Script.Contains("{Echo"),
            "echo rules: a malformed Echo is stripped, warned about, not shown");

        var shortDur = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"LEAKED\",0.1}");
        log.IsTrue(shortDur.Echo != null && shortDur.Echo.Duration == EchoTag.MinDuration && shortDur.Warnings.Count == 1,
            "echo rules: a duration under 0.5 s is raised to 0.5 s with a warning");

        var noAccent = EchoTag.Parse("{Echo:\"NO KEY LEAKED?\",\"\",3}");
        log.IsTrue(noAccent.Echo != null && !noAccent.Echo.AccentShown && noAccent.Warnings.Count == 0,
            "echo rules: an empty accent is simply no accent — no warning");

        log.IsTrue(EchoTag.Parse("{Echo:\"A<b>B\",\"\",3}").Echo.Markup == "<noparse>A<B>B</noparse>",
            "echo rules: headline text is never read as rich-text tags");
    }

    static void Echo_StampRoundTrip(Log log)
    {
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(EchoBinance), "echo: the TTS processor recognises the tag");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Echo:\"ONLY A HEADLINE\",3}"), "echo: …but not a malformed one");
        var (marks, spoken) = TtsScriptProcessor.ExtractMarkers("{Neutral}\n" + EchoBinance + "\nMore than six billion dollars.");
        log.IsTrue(!spoken.Contains("LEAKED") && marks.Exists(m => m.Text == EchoBinance),
            "echo: stripped before TTS — the headline is never sent as speech");
        string script =
            "## COLD OPEN\n" +
            "{Timestamp:\"Cold Open\"}\n" +
            "{Position:Center,Cut} {Neutral}\n" +
            EchoBinance + "\n" +
            "More than six billion dollars, according to the FBI.\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains("{Echo:\"NO KEY LEAKED?\",\"LEAKED\",T=") && section.TimedScript.Contains(",D=3}"),
                "echo: stamping gives it a T= and D=3, card-style");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "echo: Unstamp(Stamp(x)) gives back the Echo as written");

            var parsed = EchoTag.Parse(section.TimedScript);
            log.IsTrue(parsed.Echo != null && !float.IsNaN(parsed.Echo.Time) && parsed.Echo.Time <= EchoTypography.TopOfScriptSeconds &&
                       parsed.Echo.Duration == 3f,
                "echo: the stamped tag parses at playback, timed at the top of the cold open");
        }
    }

    static void Echo_Motion(Log log)
    {
        log.AreEqual(0f, EchoTypography.SlideIn(0f), "echo motion: starts above the frame");
        log.AreEqual(1f, EchoTypography.SlideIn(EchoTypography.SlideInSeconds), "echo motion: settled at 0.25 s");
        float peak = 0f;
        for (float t = 0f; t <= 0.25f; t += 0.001f) peak = Mathf.Max(peak, EchoTypography.SlideIn(t));
        log.IsTrue(peak > 1.02f && peak < 1.06f, $"echo motion: a slight overshoot ({(peak - 1f) * 100f:F1} % of the travel)");
        log.AreEqual(0f, EchoTypography.SlideOut(0f), "echo motion: the slide out starts at rest");
        log.AreEqual(1f, EchoTypography.SlideOut(EchoTypography.SlideOutSeconds), "echo motion: …and is gone in 0.2 s");
        float w = EchoTypography.OutlineWidth(EchoTypography.AntonCapRatio);
        float g = 10f, outsidePx = w * (g - 1f) / g * g;   // sampling px outside the glyph
        log.Near(0.05f * EchoTypography.AntonCapRatio * 90f, outsidePx, 0.01f, "echo: the outline is 5 % of the cap height");
        log.IsTrue(2f * w <= 1f, "echo: outline + dilate stay within TMP's unscaled range (no ratio shrink)");
    }

    static void Echo_FitMaths(Log log)
    {
        // A short line: the band sets the size.
        var s = EchoTypography.Fit(2f, EchoTypography.AntonCapRatio);
        log.Near(81f / 1.1f, s.CapPx, 0.01f, "echo fit: a short line gets the band's full 73.6 px caps");
        log.IsTrue(!s.WidthBound && !s.UnderMinimum && s.CapPx >= EchoTypography.MinCapPx, "echo fit: …which is over the 72 px minimum");
        log.Near(0.035f * 1080f, s.BoxTop, 0.01f, "echo fit: the outline's top on the action-safe line");
        log.Near(0.11f * 1080f, s.BoxBottom, 0.01f, "echo fit: the outline's bottom on the cards' line");
        // A wide line: the width cap sets it.
        var w = EchoTypography.Fit(20f, EchoTypography.AntonCapRatio);
        log.Near(0.70f * 1920f, w.WidthPx, 0.01f, "echo fit: a wide line spans exactly 70 % of the frame");
        log.IsTrue(w.WidthBound && w.UnderMinimum, "echo fit: …narrowed, and flagged as under 72 px caps");
        log.IsTrue(w.BoxTop >= 0.035f * 1080f - 0.01f && w.BoxBottom <= 0.11f * 1080f + 0.01f, "echo fit: …still inside the band, centred");
    }

    static void Echo_LayoutWithAnton(Log log)
    {
        log.IsTrue(EchoTypography.Anton != null, "echo: Anton SC loads from Resources/Fonts");
        if (EchoTypography.Anton == null) return;
        log.Near(EchoTypography.AntonCapRatio, EchoTypography.CapRatio(EchoTypography.Anton), 0.01f,
            "echo: the font asset's cap height is Anton's 0.859 em");

        var canvasGo = new GameObject("EchoTestCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1920f, 1080f);
        try
        {
            var cases = new[]
            {
                ("1 word",  "{Echo:\"LEAKED?\",\"LEAKED\",3}"),
                ("the Binance headline", EchoBinance),
                ("5 long words", "{Echo:\"BINANCE FROZE EVERY WITHDRAWAL WORLDWIDE\",\"FROZE\",3}"),
            };
            foreach (var (name, tag) in cases)
            {
                var spec = EchoTag.Parse(tag).Echo;
                var e = EchoOverlay.CreateOn(canvasGo.transform, spec);
                var l = e.CurrentLayout;
                TextMeshProUGUI t = e.Text;
                t.ForceMeshUpdate(true, true);   // the line is built hidden
                float measured = t.GetPreferredValues(spec.Markup, 0f, 0f).x + 2f * l.OutlinePx;
                log.IsTrue(t.font == EchoTypography.Anton, $"echo layout ({name}): set in Anton SC");
                log.IsTrue(l.WidthPx <= 0.70f * 1920f + 0.5f && measured <= 0.70f * 1920f + 2f,
                    $"echo layout ({name}): spans at most 70 % of the width ({measured:F0} px)");
                log.IsTrue(l.BoxTop >= 0.035f * 1080f - 0.01f && l.BoxBottom <= 0.11f * 1080f + 0.01f,
                    $"echo layout ({name}): inside the band ({l.BoxTop:F0}–{l.BoxBottom:F0} px)");
                log.Near(l.CapPx * 0.05f, l.OutlinePx, 0.001f, $"echo layout ({name}): outline 5 % of the {l.CapPx:F1} px caps");
                log.Near(0f, t.textInfo.lineInfo[0].baseline, 0.5f, $"echo layout ({name}): the baseline sits on the line's middle (Baseline alignment)");
                if (name == "1 word")
                    log.IsTrue(!l.WidthBound && l.CapPx >= EchoTypography.MinCapPx, $"echo layout ({name}): full size, {l.CapPx:F1} px caps");
                if (name == "the Binance headline")
                    log.IsTrue(!l.WidthBound && l.CapPx >= EchoTypography.MinCapPx,
                        $"echo layout ({name}): fits at full size ({measured:F0} px wide, {l.CapPx:F1} px caps)");
                if (name == "5 long words")
                {
                    log.IsTrue(l.WidthBound, $"echo layout ({name}): the width cap sets the size ({l.CapPx:F1} px caps)");
                    log.Near(0.70f * 1920f, measured, 4f, $"echo layout ({name}): …spanning 70 % of the frame");
                }
                Object.DestroyImmediate(e.gameObject);
            }
        }
        finally { Object.DestroyImmediate(canvasGo); }
    }

    static void Echo_SwitchOff(Log log)
    {
        bool had = PlayerPrefs.HasKey(EchoOverlay.PrefKey);
        int saved = PlayerPrefs.GetInt(EchoOverlay.PrefKey, 1);
        try
        {
            PlayerPrefs.DeleteKey(EchoOverlay.PrefKey);
            log.IsTrue(EchoOverlay.Enabled, "echo switch: on by default");
            EchoOverlay.Enabled = false;
            var e = EchoOverlay.Create(null, null, EchoTag.Parse(EchoBinance));
            log.IsTrue(e == null && GameObject.Find("EchoOverlay") == null, "echo switch: off → nothing is made");
            log.IsTrue(EchoOverlay.Last != null && !EchoOverlay.Last.Enabled && EchoOverlay.Last.Headline == "NO KEY LEAKED?" &&
                       !EchoOverlay.Last.Shown, "echo switch: …and the result says which Echo wasn't shown");
            EchoOverlay.Enabled = true;
            log.IsTrue(EchoOverlay.Create(null, null, EchoTag.Parse(EchoBinance)) == null, "echo switch: on, but no canvas → nothing made");
            log.IsTrue(EchoOverlay.Create(null, null, EchoTag.Parse("Words only.")) == null && EchoOverlay.Last.Headline == null,
                "echo: a script without an Echo makes nothing");
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(EchoOverlay.PrefKey, saved); else PlayerPrefs.DeleteKey(EchoOverlay.PrefKey);
            PlayerPrefs.Save();
        }
    }
}

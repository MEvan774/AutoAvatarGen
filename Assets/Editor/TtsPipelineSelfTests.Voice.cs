using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MugsTech.Captions;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The voice model is a setting, and everything that depends on it
/// (MugsTech.Tts.VoiceModel / VoiceTiming): what a request carries per model,
/// the stability rules, what the render manifest records, the model a
/// generation was voiced with — and, on three real lab takes of 2026-11-17
/// (Editor/TestData/VoiceLab~: two eleven_v4, one eleven_v3, audio + the API's
/// alignment), that the slicer cuts every v4 break in its own silence, that a
/// {Pause} at a v4 line break is widened, that stacked tags share a frame and
/// that a caption word ends where its pause begins.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/TTS/Run Voice Self-Tests")]
    public static void RunVoiceOnly()
    {
        var log = new Log();
        RunVoiceGroup(log);
        log.Report();
    }

    static void RunVoiceGroup(Log log)
    {
        Voice_ModelAndStabilityRules(log);
        Voice_RequestJsonFollowsTheModel(log);
        Voice_ManifestRecordsWhatWasSent(log);
        Voice_SettingRoundTrips(log);
        Voice_V4StackedTagsShareAFrame(log);
        Voice_V4CaptionWordEndsWhereItsPauseBegins(log);
        Voice_LabTakes_EveryBreakIsCutInItsSilence(log);
        Voice_LabTakes_APauseAtAV4LineBreakIsWidened(log);
        Voice_LabTakes_StackedTagsShareAFrame(log);
    }

    // ---------------------------------------------------------------------
    // Rules (pure)
    // ---------------------------------------------------------------------

    static void Voice_ModelAndStabilityRules(Log log)
    {
        log.AreEqual("eleven_v4", VoiceModel.DefaultId, "eleven_v4 is the default model");
        log.AreEqual("eleven_v4", VoiceModel.Normalise(null), "nothing stored = the default");
        log.AreEqual("eleven_v4", VoiceModel.Normalise("  "), "blank = the default");
        log.AreEqual("eleven_v3", VoiceModel.Normalise(" eleven_v3 "), "a stored model is kept (trimmed)");
        log.AreEqual("eleven_v3", VoiceModel.Next("eleven_v4"), "the panel's button switches v4 → v3");
        log.AreEqual("eleven_v4", VoiceModel.Next("eleven_v3"), "…and back");
        log.AreEqual("eleven_v4", VoiceModel.Next("something_else"), "an unknown id goes to the default");

        log.IsTrue(VoiceModel.IsV4("eleven_v4") && VoiceModel.IsV4("eleven_v4_turbo"), "the v4 family is v4");
        log.IsTrue(!VoiceModel.IsV4("eleven_v3") && !VoiceModel.IsV4("eleven_multilingual_v2") && !VoiceModel.IsV4(null),
            "everything else is treated as v3 was");

        // Stability. v4: continuous, as asked. v3: 0.0 unless asked, snapped.
        log.Near(0.5f,  VoiceModel.ResolveStability("eleven_v4", null, 0.5f),  1e-6f, "v4 with nothing asked: the stored setting (default 0.5)");
        log.Near(0.35f, VoiceModel.ResolveStability("eleven_v4", null, 0.35f), 1e-6f, "v4: a stored 0.35 is 0.35 — no snapping");
        log.Near(0.62f, VoiceModel.ResolveStability("eleven_v4", 0.62f, 0.5f), 1e-6f, "v4: an explicit value wins over the setting");
        log.Near(1f,    VoiceModel.ResolveStability("eleven_v4", 1.7f, 0.5f),  1e-6f, "v4: clamped to 1");
        log.Near(0f,    VoiceModel.ResolveStability("eleven_v4", -0.2f, 0.5f), 1e-6f, "v4: clamped to 0");
        log.Near(0f,    VoiceModel.ResolveStability("eleven_v3", null, 0.5f),  1e-6f, "v3 with nothing asked: Creative (0.0), whatever v4's setting is");
        log.Near(0.5f,  VoiceModel.ResolveStability("eleven_v3", 0.35f, 0.9f), 1e-6f, "v3: an asked-for 0.35 snaps to Natural (0.5), as before");
        log.Near(1f,    VoiceModel.ResolveStability("eleven_v3", 0.8f, 0f),    1e-6f, "v3: 0.8 snaps to Robust (1.0), as before");

        log.IsTrue(VoiceModel.TryParseStability("0.35", out float a) && Mathf.Abs(a - 0.35f) < 1e-6f, "the panel reads 0.35");
        log.IsTrue(VoiceModel.TryParseStability("0,4", out float b) && Mathf.Abs(b - 0.4f) < 1e-6f, "…and a decimal comma");
        log.IsTrue(VoiceModel.TryParseStability("3", out float c) && Mathf.Abs(c - 1f) < 1e-6f, "…and clamps to 0–1");
        log.IsTrue(!VoiceModel.TryParseStability("abc", out _) && !VoiceModel.TryParseStability("", out _), "…and refuses what is no number");
        log.AreEqual("0.35", VoiceModel.FormatStability(0.35f), "0.35 is shown as 0.35");

        // The timing rules follow the model.
        VoiceTiming v3 = VoiceTiming.For("eleven_v3"), v4 = VoiceTiming.For("eleven_v4");
        log.IsTrue(Mathf.Abs(v3.MinRealPauseSeconds - 0.25f) < 1e-6f && !v3.FirstTagSetsBeat && !v3.WordEndsAtPause && !v3.CutMidSilence,
            "v3's timing is what every take so far was timed with: 0.25 s floor, none of the v4 rules");
        log.IsTrue(Mathf.Abs(v4.MinRealPauseSeconds - 0.20f) < 1e-6f && v4.FirstTagSetsBeat && v4.WordEndsAtPause && v4.CutMidSilence,
            "v4's: 0.20 s floor, first tag sets the beat, words end at their pause, cuts go mid-silence");
        log.IsTrue(ReferenceEquals(VoiceTiming.For(null), VoiceTiming.V3) && ReferenceEquals(VoiceTiming.For("eleven_multilingual_v2"), VoiceTiming.V3),
            "an unknown or missing model is timed as v3");
        log.IsTrue(Mathf.Abs(SegmentSequencerV3Floor - PauseInserter.MinPauseSeconds) < 1e-6f,
            "PauseInserter's own floor is still v3's 0.25 s");
    }

    const float SegmentSequencerV3Floor = 0.25f;

    // ---------------------------------------------------------------------
    // The request
    // ---------------------------------------------------------------------

    [Serializable] class V4Settings { public float stability = -1f; public float similarity_boost = -1f; }
    [Serializable] class V4Body
    {
        public string text; public string model_id; public V4Settings voice_settings; public int seed = -1;
    }

    // What eleven_v3 has been sent since the chunked pipeline: JsonUtility's
    // rendering of {text, model_id, voice_settings{4 fields}, seed}. Pinned
    // from this build on 2026-11-17 (the v3 code path was not edited).
    const string V3SeededRequest =
        "{\"text\":\"[dry] Hi.\\nTwo.\",\"model_id\":\"eleven_v3\",\"voice_settings\":{\"stability\":0.0," +
        "\"similarity_boost\":0.800000011920929,\"style\":0.3499999940395355,\"use_speaker_boost\":true},\"seed\":282473895}";
    const string V3UnseededRequest =
        "{\"text\":\"[dry] Hi.\\nTwo.\",\"model_id\":\"eleven_v3\",\"voice_settings\":{\"stability\":0.5," +
        "\"similarity_boost\":0.800000011920929,\"style\":0.3499999940395355,\"use_speaker_boost\":true}}";

    static void Voice_RequestJsonFollowsTheModel(Log log)
    {
        var settings = new ElevenLabsClient.VoiceSettings { stability = 0.35f };
        const string text = "[dry, low] He said \"no\".\n\nTwo — \\ done.";

        string v4 = ElevenLabsClient.BuildRequestJson(text, "eleven_v4", settings, 282473895);
        log.IsTrue(!v4.Contains("style") && !v4.Contains("use_speaker_boost"),
            $"a v4 request carries no style and no use_speaker_boost ({v4})");
        log.IsTrue(v4.Contains("\"voice_settings\":{\"stability\":0.35,\"similarity_boost\":0.8}"),
            $"…only stability and similarity_boost, and 0.35 goes out as 0.35 ({v4})");
        log.IsTrue(v4.Contains("\"model_id\":\"eleven_v4\"") && v4.EndsWith(",\"seed\":282473895}"),
            "…with its model and the seed");
        log.IsTrue(!ElevenLabsClient.BuildRequestJson(text, "eleven_v4", settings, null).Contains("seed"),
            "…and no seed field when none is given");

        // It is hand-written JSON, so read it back with a real parser.
        V4Body back = JsonUtility.FromJson<V4Body>(v4);
        log.AreEqual(text, back.text, "the text survives the hand-written JSON (quotes, newlines, a backslash, a dash)");
        log.Near(0.35f, back.voice_settings.stability, 1e-6f, "the parsed stability is 0.35");
        log.Near(0.8f, back.voice_settings.similarity_boost, 1e-6f, "the parsed similarity is 0.8");
        log.AreEqual(282473895, back.seed, "the parsed seed is the seed");

        settings.stability = 1.7f;
        log.IsTrue(ElevenLabsClient.BuildRequestJson("x", "eleven_v4", settings, null).Contains("\"stability\":1,"),
            "a v4 stability above 1 is sent as 1");

        // v3: byte for byte what it has always been sent.
        var old = new ElevenLabsClient.VoiceSettings { stability = 0f };
        log.AreEqual(V3SeededRequest, ElevenLabsClient.BuildRequestJson("[dry] Hi.\nTwo.", "eleven_v3", old, 282473895),
            "the v3 request (seeded, stability 0) is byte for byte what it was");
        log.AreEqual(V3UnseededRequest,
            ElevenLabsClient.BuildRequestJson("[dry] Hi.\nTwo.", "eleven_v3", new ElevenLabsClient.VoiceSettings(), null),
            "the v3 request without a seed too");
    }

    static void Voice_ManifestRecordsWhatWasSent(Log log)
    {
        var settings = new ElevenLabsClient.VoiceSettings { stability = 0.5f };
        string v4 = ElevenLabsClient.SentVoiceSettingsJson("eleven_v4", settings, "    ");
        log.IsTrue(v4.Contains("\"stability\": 0.5") && v4.Contains("\"similarity_boost\": 0.8") &&
                   !v4.Contains("style") && !v4.Contains("use_speaker_boost"),
            $"a v4 render manifest records stability and similarity only — no phantom style ({v4.Trim()})");

        settings.stability = 0f;
        string v3 = ElevenLabsClient.SentVoiceSettingsJson("eleven_v3", settings, "    ");
        log.AreEqual("    \"stability\": 0,\n    \"similarity_boost\": 0.8,\n    \"style\": 0.35,\n    \"use_speaker_boost\": true\n",
            v3, "a v3 render manifest's voice_settings block is unchanged");

        string manifest = "{\n  \"seed\": 7,\n  \"model\": \"eleven_v4\",\n  \"voice_settings\": {\n" + v4 + "  }\n}";
        log.AreEqual("eleven_v4", VoiceModel.ModelInRenderManifest(manifest), "the model is read back from a render manifest");
        log.IsTrue(VoiceModel.StabilityInRenderManifest(manifest) is float s && Mathf.Abs(s - 0.5f) < 1e-6f,
            "…and the stability that was sent");
        log.IsTrue(VoiceModel.ModelInRenderManifest("{}") == null && VoiceModel.StabilityInRenderManifest("") == null,
            "a manifest that holds neither gives neither");

        // The model of a generation FOLDER — what the timing follows.
        string dir = Path.Combine(Path.GetTempPath(), "mugs_voice_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            log.AreEqual("eleven_v3", VoiceModel.OfGeneration(dir), "a generation without a render manifest was voiced on v3");
            log.IsTrue(VoiceModel.StabilityOfGeneration(dir) == null, "…and its stability is not recorded");
            File.WriteAllText(Path.Combine(dir, "render_manifest.json"), manifest);
            log.AreEqual("eleven_v4", VoiceModel.OfGeneration(dir), "a generation's model is its render manifest's");
            File.WriteAllText(Path.Combine(dir, "render_manifest.json"), manifest.Replace("eleven_v4", "eleven_v3"));
            log.AreEqual("eleven_v3", VoiceModel.OfGeneration(dir), "a v3 generation stays v3");
            log.IsTrue(ReferenceEquals(VoiceTiming.For(VoiceModel.OfGeneration(dir)), VoiceTiming.V3),
                "…and is timed with v3's numbers, whatever the setting says");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
        log.AreEqual("eleven_v3", VoiceModel.OfGeneration(null), "no folder = the old voice");
    }

    // The stored setting itself. PlayerPrefs is the app's real store (shared
    // with the built player on this machine), so whatever was there is put
    // back exactly — including "nothing stored".
    static void Voice_SettingRoundTrips(Log log)
    {
        bool hadModel = PlayerPrefs.HasKey(VoiceModel.ModelPrefKey);
        bool hadStab  = PlayerPrefs.HasKey(VoiceModel.StabilityPrefKey);
        string model  = PlayerPrefs.GetString(VoiceModel.ModelPrefKey, "");
        float  stab   = PlayerPrefs.GetFloat(VoiceModel.StabilityPrefKey, 0f);
        try
        {
            PlayerPrefs.DeleteKey(VoiceModel.ModelPrefKey);
            PlayerPrefs.DeleteKey(VoiceModel.StabilityPrefKey);
            log.AreEqual("eleven_v4", VoiceModel.CurrentId, "with nothing stored the model is eleven_v4");
            log.Near(0.5f, VoiceModel.CurrentV4Stability, 1e-6f, "…and v4's stability is 0.5");
            log.AreEqual("eleven_v4", VoiceModel.Resolve(null), "a job that names no model gets the setting");
            log.AreEqual("eleven_v3", VoiceModel.Resolve("eleven_v3"), "a job that names one keeps it");

            VoiceModel.CurrentId = "eleven_v3";
            log.AreEqual("eleven_v3", VoiceModel.CurrentId, "eleven_v3 can be chosen without a rebuild");
            log.AreEqual("eleven_v3", VoiceModel.Resolve(null), "…and a job then gets v3");
            VoiceModel.CurrentV4Stability = 0.35f;
            log.Near(0.35f, VoiceModel.CurrentV4Stability, 1e-6f, "a stability of 0.35 is stored as 0.35");
            log.Near(0f, VoiceModel.ResolveStability(VoiceModel.CurrentId, null, VoiceModel.CurrentV4Stability), 1e-6f,
                "…and v3 still goes out at 0.0");

            VoiceModel.CurrentId = "eleven_v4";
            log.AreEqual("eleven_v4", VoiceModel.CurrentId, "and back to eleven_v4");
            log.Near(0.35f, VoiceModel.ResolveStability(VoiceModel.CurrentId, null, VoiceModel.CurrentV4Stability), 1e-6f,
                "…which now goes out at the stored 0.35");
            VoiceModel.CurrentV4Stability = 4f;
            log.Near(1f, VoiceModel.CurrentV4Stability, 1e-6f, "a stored stability is kept inside 0–1");
        }
        finally
        {
            if (hadModel) PlayerPrefs.SetString(VoiceModel.ModelPrefKey, model); else PlayerPrefs.DeleteKey(VoiceModel.ModelPrefKey);
            if (hadStab)  PlayerPrefs.SetFloat(VoiceModel.StabilityPrefKey, stab); else PlayerPrefs.DeleteKey(VoiceModel.StabilityPrefKey);
            PlayerPrefs.Save();
        }
    }

    // ---------------------------------------------------------------------
    // v4 timing rules on small, exact cases
    // ---------------------------------------------------------------------

    // The pause snap as SegmentSequencer applies it: a time inside a measured
    // pause, at most 0.9 s before its end, moves onto that end.
    static Func<float, TagBeats.Mapped> SnapMap(IList<(float From, float To)> pauses)
        => t =>
        {
            foreach (var p in pauses)
                if (t >= p.From && t < p.To)
                    return p.To - t <= 0.9f ? new TagBeats.Mapped(p.To, true) : new TagBeats.Mapped(t, false);
            return new TagBeats.Mapped(t, false);
        };

    static void Voice_V4StackedTagsShareAFrame(Log log)
    {
        // v4: the silence 9.60–10.00 belongs to "one."; both own-line tags are
        // stamped just AFTER it, a few hundredths apart. Neither snaps.
        const string script = "Line one.\n{Shrug,Cut,T=10.020}\n{Zoom:ExtremeOut,T=10.080}\nSo it goes.";
        var pauses = new List<(float From, float To)> { (9.60f, 10.00f) };
        var stamps = TagBeats.Find(script);

        float[] v3 = TagBeats.Resolve(stamps, SnapMap(pauses), pauses);
        log.IsTrue(Mathf.Abs(v3[0] - 10.02f) < 1e-4f && Mathf.Abs(v3[1] - 10.08f) < 1e-4f,
            $"timed as v3, two unsnapped tags keep their own times — nothing changes for a v3 generation ({v3[0]:F3}, {v3[1]:F3})");

        var joins = new List<TagBeats.Join>();
        float[] v4 = TagBeats.Resolve(stamps, SnapMap(pauses), pauses, joins, firstTagSetsBeat: true);
        log.IsTrue(Mathf.Abs(v4[0] - 10.02f) < 1e-4f && Mathf.Abs(v4[1] - 10.02f) < 1e-4f,
            $"timed as v4, the run's first tag sets the beat and the second joins it ({v4[0]:F3}, {v4[1]:F3})");
        log.IsTrue(joins.Count == 1 && joins[0].OnFirstTag && joins[0].Stamp.Name == "{Zoom:ExtremeOut}",
            "…and the join says it was onto the first tag");

        // A snap in the run still decides, in both modes.
        const string snapped = "Line one.\n{Shrug,Cut,T=9.900}\n{Zoom:ExtremeOut,T=10.080}\nSo it goes.";
        var s2 = TagBeats.Find(snapped);
        joins.Clear();
        float[] both = TagBeats.Resolve(s2, SnapMap(pauses), pauses, joins, firstTagSetsBeat: true);
        log.IsTrue(Mathf.Abs(both[0] - 10f) < 1e-4f && Mathf.Abs(both[1] - 10f) < 1e-4f && joins.Count == 1 && !joins[0].OnFirstTag,
            $"a tag that snaps onto the onset still sets the beat on v4 ({both[0]:F3}, {both[1]:F3})");

        // v4 stamps an own-line tag at the silence's END; one stamped before the
        // silence began is the alignment running early. When a later tag of
        // the run snaps, that earlier one joins the same onset.
        const string early = "Line one.\n{Shrug,Cut,T=9.500}\n{Zoom:ExtremeOut,T=9.800}\nSo it goes.";
        var s3 = TagBeats.Find(early);
        float[] earlyV3 = TagBeats.Resolve(s3, SnapMap(pauses), pauses);
        log.IsTrue(Mathf.Abs(earlyV3[0] - 9.5f) < 1e-4f && Mathf.Abs(earlyV3[1] - 10f) < 1e-4f,
            $"timed as v3, a tag before the snapped one keeps its time, as it always has ({earlyV3[0]:F3}, {earlyV3[1]:F3})");
        joins.Clear();
        float[] earlyV4 = TagBeats.Resolve(s3, SnapMap(pauses), pauses, joins, firstTagSetsBeat: true);
        log.IsTrue(Mathf.Abs(earlyV4[0] - 10f) < 1e-4f && Mathf.Abs(earlyV4[1] - 10f) < 1e-4f && joins.Count == 1,
            $"timed as v4, it joins the onset the later tag snapped to ({earlyV4[0]:F3}, {earlyV4[1]:F3})");
        float[] tooEarly = TagBeats.Resolve(TagBeats.Find("Line one.\n{Shrug,T=9.000}\n{Zoom:In,T=9.800}\nSo."),
            SnapMap(pauses), pauses, null, true);
        log.IsTrue(Mathf.Abs(tooEarly[0] - 9f) < 1e-4f, "…but not from more than 0.9 s before it");
        var twoPauses = new List<(float From, float To)> { (9.20f, 9.45f), (9.60f, 10.00f) };
        float[] otherBeat = TagBeats.Resolve(TagBeats.Find("Line one.\n{Shrug,T=9.150}\n{Zoom:In,T=9.800}\nSo."),
            SnapMap(twoPauses), twoPauses, null, true);
        log.IsTrue(Mathf.Abs(otherBeat[0] - 9.15f) < 1e-4f, "…and not over another measured pause (sound lies between)");

        // The limits are the old ones: not across a measured pause, not past 0.9 s, never in a {Pause} run.
        var withBreath = new List<(float From, float To)> { (9.60f, 10.00f), (10.04f, 10.30f) };
        float[] apart = TagBeats.Resolve(TagBeats.Find("Line one.\n{Shrug,T=10.020}\n{Zoom:In,T=10.350}\nSo."),
            SnapMap(withBreath), withBreath, null, true);
        log.IsTrue(Mathf.Abs(apart[1] - 10.35f) < 1e-4f, "a tag after another measured pause is not pulled over it");
        float[] far = TagBeats.Resolve(TagBeats.Find("Line one.\n{Shrug,T=10.020}\n{Zoom:In,T=11.200}\nSo."),
            SnapMap(pauses), pauses, null, true);
        log.IsTrue(Mathf.Abs(far[1] - 11.2f) < 1e-4f, "a tag more than 0.9 s later is left alone");
        float[] pinned = TagBeats.Resolve(TagBeats.Find("Line one.\n{Black,T=10.020}\n{Pause:0.8,T=10.050}\n{Black:End,T=10.080}\nSo."),
            SnapMap(pauses), pauses, null, true);
        log.IsTrue(Mathf.Abs(pinned[0] - 10.02f) < 1e-4f && Mathf.Abs(pinned[2] - 10.08f) < 1e-4f,
            "a run that holds a {Pause} is never joined — PauseInserter pins it to the gap's edges");
        float[] words = TagBeats.Resolve(TagBeats.Find("{Neutral,T=1.000}One two {Smirk,T=1.400}three."),
            SnapMap(pauses), pauses, null, true);
        log.IsTrue(Mathf.Abs(words[0] - 1f) < 1e-4f && Mathf.Abs(words[1] - 1.4f) < 1e-4f,
            "tags with spoken words between them are different beats");
    }

    static void Voice_V4CaptionWordEndsWhereItsPauseBegins(Log log)
    {
        var pauses = new List<(float From, float To)> { (1.00f, 2.00f) };
        var word   = new CaptionWord { Timed = true, RawStart = 0.70f, Start = 0.70f, End = 2.00f };   // v4: "what." + its pause
        var inside = new CaptionWord { Timed = true, RawStart = 1.20f, Start = 2.00f, End = 2.30f };   // starts in the pause: the snap's case
        var plain  = new CaptionWord { Timed = true, RawStart = 0.20f, Start = 0.20f, End = 0.50f };
        var loose  = new CaptionWord { Timed = false, RawStart = 0.70f, End = 2.00f };
        int n = CaptionBuilder.EndWordsAtPauses(new List<CaptionWord> { plain, word, inside, loose }, pauses);
        log.AreEqual(1, n, "only the word that runs into a pause is shortened");
        log.Near(1.00f, word.End, 1e-5f, "it ends where its pause begins");
        log.IsTrue(Mathf.Abs(inside.End - 2.30f) < 1e-5f && Mathf.Abs(plain.End - 0.50f) < 1e-5f && Mathf.Abs(loose.End - 2f) < 1e-5f,
            "a word that starts inside the pause, a word clear of it and an untimed one are untouched");

        // Through Build: a v4 section whose sentence-final word carries the pause.
        const string script = "{Neutral,T=0.000}\nThe keys did what.\n{Black,T=2.000}\nThey left the door.";
        var raw = new List<RawWord> {
            new RawWord { word = "The", start = 0.00f, end = 0.20f }, new RawWord { word = "keys", start = 0.20f, end = 0.50f },
            new RawWord { word = "did", start = 0.50f, end = 0.70f }, new RawWord { word = "what.", start = 0.70f, end = 2.00f },
            new RawWord { word = "They", start = 2.00f, end = 2.25f }, new RawWord { word = "left", start = 2.25f, end = 2.55f },
            new RawWord { word = "the", start = 2.55f, end = 2.70f }, new RawWord { word = "door.", start = 2.70f, end = 3.10f },
        };
        float SpokenEndOfWhat(bool v4)
        {
            var section = new CaptionBuilder.Section {
                Index = 2, Script = script, Words = raw, Pauses = pauses, SpeechEnd = 3.10f, WordEndsAtPause = v4,
            };
            CaptionPhrase p = CaptionBuilder.Build(new[] { section }).Phrases.Find(x => x.Text.Contains("what"));
            return p != null ? p.SpokenEnd : float.NaN;
        }
        float asV3 = SpokenEndOfWhat(false), asV4 = SpokenEndOfWhat(true);
        log.IsTrue(asV3 > 1.5f, $"timed as v3 the phrase's last word runs on into the pause (said at {asV3:F2}) — unchanged");
        log.IsTrue(asV4 < 1.05f, $"timed as v4 it is said when the pause begins (at {asV4:F2}), so a tag that opens the gap is not \"ahead\" of it");
    }

    // ---------------------------------------------------------------------
    // The lab takes
    // ---------------------------------------------------------------------

    sealed class LabTake
    {
        public string Name, Text;
        public WavCodec.AudioBuffer Audio;
        public TtsAlignment Alignment;
        public List<TtsChunkAssembler.Span> Lines = new List<TtsChunkAssembler.Span>();
        public List<(int From, int To)> Breaks = new List<(int From, int To)>();   // the newline run after line i
    }

    static LabTake LoadLabTake(string name)
    {
        string dir = Path.Combine(Application.dataPath, "Editor/TestData/VoiceLab~");
        var take = new LabTake {
            Name      = name,
            Audio     = WavCodec.Read(Path.Combine(dir, name + ".wavdata")),
            Alignment = TtsAlignment.FromJson(File.ReadAllText(Path.Combine(dir, name + "_alignment.json"))),
        };
        var sb = new StringBuilder();
        foreach (string c in take.Alignment.characters) sb.Append(c);
        take.Text = sb.ToString();

        int i = 0, n = take.Text.Length;
        while (i < n)
        {
            while (i < n && take.Text[i] == '\n') i++;
            if (i >= n) break;
            int start = i;
            while (i < n && take.Text[i] != '\n') i++;
            take.Lines.Add(new TtsChunkAssembler.Span {
                Order = take.Lines.Count + 1, Slug = $"L{take.Lines.Count + 1:00}", Start = start, Length = i - start,
            });
            int nl = i;
            while (i < n && take.Text[i] == '\n') i++;
            if (i < n) take.Breaks.Add((nl, i));
        }
        return take;
    }

    // Quiet runs by the splitter's own rule, measured here on its own.
    static List<(float From, float To)> QuietRuns(WavCodec.AudioBuffer audio, out float[] db, out float threshold, out float perWindow)
    {
        int windowFrames = Math.Max(1, (int)Math.Round(0.02f * audio.SampleRate));
        db        = AudioSlicer.WindowDb(audio.Samples, audio.Channels, audio.Frames, windowFrames);
        threshold = AudioSlicer.SpeechThresholdDb(db, new AudioSlicer.Options());
        perWindow = windowFrames / (float)audio.SampleRate;
        var runs = new List<(float From, float To)>();
        int start = -1;
        for (int w = 0; w <= db.Length; w++)
        {
            bool quiet = w < db.Length && db[w] < threshold;
            if (quiet) { if (start < 0) start = w; continue; }
            if (start >= 0) runs.Add((start * perWindow, w * perWindow));
            start = -1;
        }
        return runs;
    }

    static float MarginToSpeech(List<(float From, float To)> quiet, float t)
    {
        foreach (var q in quiet)
            if (t >= q.From && t <= q.To) return Math.Min(t - q.From, q.To - t);
        return -1f;   // not in silence at all
    }

    static void Voice_LabTakes_EveryBreakIsCutInItsSilence(Log log)
    {
        foreach (string name in new[] { "v4_take1", "v4_clip_C" })
        {
            LabTake take = LoadLabTake(name);
            var quiet = QuietRuns(take.Audio, out _, out _, out _);
            log.IsTrue(take.Lines.Count >= 20 && take.Alignment.MatchesText(take.Text),
                $"{name}: the fixture loads ({take.Lines.Count} lines, {take.Audio.Seconds:F1} s)");

            var options = new AudioSlicer.Options { CutMidSilence = true };
            var split = AudioSlicer.Split(take.Audio, take.Alignment, take.Lines, options);
            log.AreEqual(take.Lines.Count, split.Cuts.Count, $"{name}: one slice per line");
            log.AreEqual(0, split.Severe.Count, $"{name}: no seam is suspect");
            log.AreEqual(0, split.Warnings.Count,
                $"{name}: no break needed the \"alignment has drifted\" rescue ({(split.Warnings.Count > 0 ? split.Warnings[0] : "")})");

            float worst = float.MaxValue;
            for (int i = 0; i < split.Boundaries.Count; i++)
            {
                float margin = MarginToSpeech(quiet, split.Boundaries[i]);
                worst = Math.Min(worst, margin);
                log.IsTrue(margin >= 0.08f,
                    $"{name}: the cut after {take.Lines[i].Slug} ({split.Boundaries[i]:F2} s) sits in silence, " +
                    $"≥ 0.08 s from speech either side (margin {margin:F3} s)");

                // …and it is THIS break's silence: between the line's last word and the next line's first.
                float wordStart = take.Alignment.StartOfLastWord(take.Lines[i].Start, take.Lines[i].End);
                float nextStart = take.Alignment.StartOfFirstVisible(take.Lines[i + 1].Start, take.Lines[i + 1].End);
                log.IsTrue(split.Boundaries[i] > wordStart - 0.4f && split.Boundaries[i] < nextStart + 0.4f,
                    $"{name}: the cut after {take.Lines[i].Slug} is at its own break ({split.Boundaries[i]:F2} s, " +
                    $"last word from {wordStart:F2}, next line from {nextStart:F2})");
            }

            // Nothing is cut inside speech: every slice keeps its whole pad of
            // silence on a cut edge (a cut hard against a word would leave less).
            for (int i = 0; i < split.Cuts.Count; i++)
            {
                var cut = split.Cuts[i];
                if (i > 0)
                    log.IsTrue(cut.SpeechStartSeconds >= options.EdgePadSeconds - 0.021f,
                        $"{name}: {cut.Span.Slug} keeps its full pad before its first word ({cut.SpeechStartSeconds:F3} s)");
                if (i < split.Cuts.Count - 1)
                    log.IsTrue(cut.DurationSeconds - cut.SpeechEndSeconds >= options.EdgePadSeconds - 0.021f,
                        $"{name}: {cut.Span.Slug} keeps its full pad after its last word " +
                        $"({cut.DurationSeconds - cut.SpeechEndSeconds:F3} s)");
            }

            // The rule every v3 take was cut with still finds a silence on v4
            // (its fallback works) — it just gets there by the rescue.
            var old = AudioSlicer.Split(take.Audio, take.Alignment, take.Lines, new AudioSlicer.Options());
            float oldWorst = float.MaxValue;
            foreach (float b in old.Boundaries) oldWorst = Math.Min(oldWorst, MarginToSpeech(quiet, b));
            log.AreEqual(0, old.Severe.Count, $"{name}: the old rule's fallback still finds every break");
            log.IsTrue(old.Warnings.Count > 0 || oldWorst < worst,
                $"{name}: …but needs the rescue or cuts closer to speech than the v4 rule " +
                $"({old.Warnings.Count} rescues, closest {oldWorst:F3} s vs {worst:F3} s)");
            Debug.Log($"[Voice] {name}: {split.Boundaries.Count} breaks cut mid-silence, closest {worst:F3} s from speech; " +
                      $"the v3 rule on the same take: {old.Warnings.Count} rescues, closest {oldWorst:F3} s.");
        }

        // v3, with the options a v3 generation gets: as before.
        LabTake v3 = LoadLabTake("v3_take1");
        var v3Quiet = QuietRuns(v3.Audio, out _, out _, out _);
        var v3Split = AudioSlicer.Split(v3.Audio, v3.Alignment, v3.Lines, new AudioSlicer.Options());
        log.AreEqual(v3.Lines.Count, v3Split.Cuts.Count, "v3_take1: one slice per line");
        log.AreEqual(0, v3Split.Severe.Count, "v3_take1: no seam is suspect");
        int inSilence = 0;
        foreach (float b in v3Split.Boundaries) if (MarginToSpeech(v3Quiet, b) >= 0f) inSilence++;
        log.AreEqual(v3Split.Boundaries.Count, inSilence, "v3_take1: every cut is in silence, by the unchanged v3 rule");
    }

    static void Voice_LabTakes_APauseAtAV4LineBreakIsWidened(Log log)
    {
        foreach (string name in new[] { "v4_take1", "v4_clip_C" })
        {
            LabTake take = LoadLabTake(name);
            var quiet = QuietRuns(take.Audio, out _, out _, out _);
            float floor = VoiceTiming.V4.MinRealPauseSeconds;
            int widened = 0, noted = 0, missedByV3Floor = 0;

            for (int i = 0; i < take.Breaks.Count; i++)
            {
                // The take as one section, with a {Pause:0.8} written on its own
                // line at this break, stamped as the pipeline stamps an own-line
                // tag: the start time of its newline character.
                float stamp = take.Alignment.StartAt(take.Breaks[i].From);
                string tag  = "{Pause:0.8,T=" + stamp.ToString("F3", CultureInfo.InvariantCulture) + "}";
                string script = take.Text.Substring(0, take.Breaks[i].From) + "\n" + tag + "\n" +
                                take.Text.Substring(take.Breaks[i].To);

                var r = PauseInserter.Apply("LAB", PauseInserter.FindTags(script), take.Audio.Samples,
                                            take.Audio.Channels, take.Audio.SampleRate, 0f, null, null, floor);
                if (r.Entries.Count != 1) { log.IsTrue(false, $"{name} break {i + 1}: one beat reported (got {r.Entries.Count})"); continue; }
                var e = r.Entries[0];
                string where = $"{name} break {i + 1} (after {take.Lines[i].Slug}, tag at {stamp:F2} s)";

                log.IsTrue(!e.Severe && e.Inserted > 0f, $"{where}: the pause is widened, not skipped ({e.Describe()})");
                if (e.Severe || e.Inserted <= 0f) continue;
                widened++;
                if (e.Note != null) noted++;

                log.IsTrue(e.Natural >= 0.06f && e.Natural < 0.8f, $"{where}: a real gap shorter than 0.8 s was found ({e.Natural:F2} s)");
                log.Near(0.8f - e.Natural, e.Inserted, 0.021f, $"{where}: exactly the difference is added — at least N, never N on top");
                log.Near(take.Audio.Samples.Length / (float)take.Audio.SampleRate + e.Inserted,
                         r.Samples.Length / (float)take.Audio.SampleRate, 0.001f, $"{where}: the take is longer by what was added");

                var splice = r.Shift.Splices[0];
                // Inside the gap it measured, and inside true silence by the
                // splitter's stricter rule. (On real audio the quietest 5 ms of a
                // gap can be near its edge, so no distance from the edge is asked.)
                log.IsTrue(splice.At >= splice.GapStart && splice.At <= splice.GapEnd && MarginToSpeech(quiet, splice.At) >= 0f,
                    $"{where}: the silence goes into silence, never into sound (at {splice.At:F3} s in the gap " +
                    $"{splice.GapStart:F2}–{splice.GapEnd:F2}, margin {MarginToSpeech(quiet, splice.At):F3} s)");

                float wordStart = take.Alignment.StartOfLastWord(take.Lines[i].Start, take.Lines[i].End);
                float nextStart = take.Alignment.StartOfFirstVisible(take.Lines[i + 1].Start, take.Lines[i + 1].End);
                log.IsTrue(splice.At > wordStart - 0.4f && splice.At < nextStart + 0.4f,
                    $"{where}: and it is this break's gap ({splice.At:F2} s; last word from {wordStart:F2}, next line from {nextStart:F2})");

                // What v3's 0.25 s floor would have made of the same break.
                var asV3 = PauseInserter.Apply("LAB", PauseInserter.FindTags(script), take.Audio.Samples,
                                               take.Audio.Channels, take.Audio.SampleRate, 0f, null, null);
                if (asV3.Entries.Count == 1 && asV3.Entries[0].Note != null && e.Note == null) missedByV3Floor++;
            }
            log.AreEqual(take.Breaks.Count, widened, $"{name}: every line break takes a {{Pause:0.8}}");
            Debug.Log($"[Voice] {name}: {{Pause:0.8}} widened at {widened}/{take.Breaks.Count} line breaks " +
                      $"({noted} with a check-by-ear note; {missedByV3Floor} more would have had one with v3's 0.25 s floor).");
        }
    }

    static void Voice_LabTakes_StackedTagsShareAFrame(Log log)
    {
        foreach (string name in new[] { "v4_take1", "v4_clip_C" })
        {
            LabTake take = LoadLabTake(name);
            var gaps = PauseInserter.MeasurePauses(take.Audio.Samples, take.Audio.Channels, take.Audio.SampleRate, 0f,
                                                   VoiceTiming.V4.MinRealPauseSeconds);
            var pauses = new List<(float From, float To)>();
            foreach (var g in gaps) pauses.Add((g.Start, g.End));

            int pairs = 0, splitAsV3 = 0, shared = 0;
            float worstAsV3 = 0f;
            foreach (var br in take.Breaks)
            {
                if (br.To - br.From < 2) continue;   // one newline = room for one own-line tag only
                // Two tags on adjacent lines between two spoken lines, each
                // stamped at its own newline character.
                string a = take.Alignment.StartAt(br.From).ToString("F3", CultureInfo.InvariantCulture);
                string b = take.Alignment.StartAt(br.From + 1).ToString("F3", CultureInfo.InvariantCulture);
                string script = "Words before.\n{Shrug,Cut,T=" + a + "}\n{Zoom:ExtremeOut,T=" + b + "}\nWords after.";
                var stamps = TagBeats.Find(script);
                pairs++;

                float[] v3 = TagBeats.Resolve(stamps, SnapMap(pauses), pauses);
                float apart = Mathf.Abs(v3[1] - v3[0]);
                if (apart > 1f / 30f) { splitAsV3++; worstAsV3 = Mathf.Max(worstAsV3, apart); }

                float[] v4 = TagBeats.Resolve(stamps, SnapMap(pauses), pauses, null, firstTagSetsBeat: true);
                if (Mathf.Abs(v4[1] - v4[0]) < 1e-4f) shared++;
                else log.IsTrue(false, $"{name}: two stacked tags at {a} / {b} share a time on v4 (got {v4[0]:F3} and {v4[1]:F3})");
            }
            log.IsTrue(pairs >= 20, $"{name}: the take has its line breaks ({pairs})");
            log.AreEqual(pairs, shared, $"{name}: every stacked pair fires on one frame with the v4 rule");
            Debug.Log($"[Voice] {name}: {pairs} stacked pairs — {splitAsV3} would land more than a frame apart " +
                      $"with the v3 rule (worst {worstAsV3:F2} s), {pairs - shared} with the v4 rule.");
        }
    }
}

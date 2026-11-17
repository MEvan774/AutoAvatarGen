using System;
using UnityEngine;

namespace MugsTech.Tts
{
    /// <summary>
    /// Which ElevenLabs model voices a take, and what follows from that choice.
    ///
    /// The model is a SETTING (PlayerPref <see cref="ModelPrefKey"/>, absent =
    /// <see cref="V4"/>), read when a generation starts: the TTS panel,
    /// <c>--auto-script</c> and the editor window all go through
    /// <see cref="Resolve"/>, so none of them needs a flag.
    ///
    /// Two things depend on the model and they are decided in different places:
    ///   • what is SENT (the request's voice settings, how stability is read)
    ///     follows the model being generated — <see cref="IsV4"/>,
    ///     <see cref="ResolveStability"/>, ElevenLabsClient.BuildRequestJson;
    ///   • how a take is TIMED (<see cref="VoiceTiming"/>) follows the model
    ///     the generation was voiced with, as recorded in its
    ///     <c>render_manifest.json</c> (<see cref="OfGeneration"/>) — never the
    ///     current setting. A generation voiced on v3 is re-timed and
    ///     re-recorded with v3's numbers whatever the setting says.
    ///
    /// Everything except the two PlayerPref properties is pure.
    /// </summary>
    public static class VoiceModel
    {
        public const string V3 = "eleven_v3";
        public const string V4 = "eleven_v4";

        /// <summary>What a generation uses when nothing says otherwise.</summary>
        public const string DefaultId = V4;

        public const string ModelPrefKey     = "AutoAvatarGen.TtsModel";
        public const string StabilityPrefKey = "AutoAvatarGen.TtsStability";

        /// <summary>v4's own default. v4 takes any value from 0 to 1.</summary>
        public const float DefaultV4Stability = 0.5f;

        /// <summary>The models the UI offers, default first.</summary>
        public static readonly string[] Choices = { V4, V3 };

        /// <summary>
        /// The v4 family (<c>eleven_v4</c>, <c>eleven_v4_turbo</c>, …). Anything
        /// else is treated as v3 was: every setting sent, stability snapped.
        /// </summary>
        public static bool IsV4(string modelId)
            => !string.IsNullOrWhiteSpace(modelId)
               && modelId.Trim().StartsWith(V4, StringComparison.OrdinalIgnoreCase);

        /// <summary>A stored or configured id, or the default when it is blank.</summary>
        public static string Normalise(string modelId)
            => string.IsNullOrWhiteSpace(modelId) ? DefaultId : modelId.Trim();

        /// <summary>
        /// The stability a request carries. v4: the asked-for value, 0–1, as it
        /// is. v3: one of the three values it accepts (ElevenLabsClient.SnapStability).
        /// </summary>
        public static float ClampStability(string modelId, float requested)
        {
            if (!IsV4(modelId)) return ElevenLabsClient.SnapStability(requested);
            if (float.IsNaN(requested)) return DefaultV4Stability;
            return requested < 0f ? 0f : (requested > 1f ? 1f : requested);
        }

        /// <summary>
        /// The stability for a generation: what the caller asked for
        /// (<paramref name="requested"/>), else the model's own — v4 the stored
        /// setting (<paramref name="storedV4"/>), v3 Creative, the value the
        /// show was tuned on.
        /// </summary>
        public static float ResolveStability(string modelId, float? requested, float storedV4)
        {
            float wanted = requested ?? (IsV4(modelId) ? storedV4 : ElevenLabsClient.StabilityCreative);
            return ClampStability(modelId, wanted);
        }

        /// <summary>Parses the panel's stability field ("0.35", "0,35", ".5"). False = not a number.</summary>
        public static bool TryParseStability(string text, out float value)
        {
            value = DefaultV4Stability;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace(',', '.');
            if (!float.TryParse(t, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float v)
                || float.IsNaN(v) || float.IsInfinity(v))
                return false;
            value = v < 0f ? 0f : (v > 1f ? 1f : v);
            return true;
        }

        public static string FormatStability(float v)
            => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The other choice — what the panel's model button switches to.</summary>
        public static string Next(string modelId)
        {
            string current = Normalise(modelId);
            for (int i = 0; i < Choices.Length; i++)
                if (string.Equals(Choices[i], current, StringComparison.OrdinalIgnoreCase))
                    return Choices[(i + 1) % Choices.Length];
            return Choices[0];
        }

        // ---- the setting (PlayerPrefs) ------------------------------------

        /// <summary>The model a new generation is voiced with.</summary>
        public static string CurrentId
        {
            get => Normalise(PlayerPrefs.GetString(ModelPrefKey, ""));
            set
            {
                PlayerPrefs.SetString(ModelPrefKey, Normalise(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>v4's stability for a new generation (v3 does not read it).</summary>
        public static float CurrentV4Stability
        {
            get => ClampStability(V4, PlayerPrefs.GetFloat(StabilityPrefKey, DefaultV4Stability));
            set
            {
                PlayerPrefs.SetFloat(StabilityPrefKey, ClampStability(V4, value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>A job's configured model, or the setting when it names none.</summary>
        public static string Resolve(string configured)
            => string.IsNullOrWhiteSpace(configured) ? CurrentId : configured.Trim();

        // ---- the model a generation was voiced with -----------------------

        /// <summary>
        /// The <c>"model"</c> of a <c>render_manifest.json</c>. Null when the
        /// text holds none. Read with a pattern, not a JSON parser: the file is
        /// written by this project, and this has to work in the offline tests.
        /// </summary>
        public static string ModelInRenderManifest(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                json, @"""model""\s*:\s*""([^""]*)""");
            return m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>The <c>"stability"</c> a <c>render_manifest.json</c> records
        /// as sent. Null when the text holds none.</summary>
        public static float? StabilityInRenderManifest(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                json, @"""stability""\s*:\s*(-?\d+(?:\.\d+)?)");
            if (!m.Success) return null;
            return float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : (float?)null;
        }

        /// <summary>The stability the generation in <paramref name="folder"/>
        /// was sent with, from its render manifest. Null = not recorded.</summary>
        public static float? StabilityOfGeneration(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    string path = System.IO.Path.Combine(folder, "render_manifest.json");
                    if (System.IO.File.Exists(path))
                        return StabilityInRenderManifest(System.IO.File.ReadAllText(path));
                }
            }
            catch (Exception) { /* unreadable = not recorded */ }
            return null;
        }

        /// <summary>
        /// The model the generation in <paramref name="folder"/> was voiced
        /// with. A generation without a render manifest (the per-section path,
        /// or one older than the chunked pipeline) was voiced on v3.
        /// </summary>
        public static string OfGeneration(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    string path = System.IO.Path.Combine(folder, "render_manifest.json");
                    if (System.IO.File.Exists(path))
                        return ModelInRenderManifest(System.IO.File.ReadAllText(path)) ?? V3;
                }
            }
            catch (Exception) { /* unreadable = unknown = the old voice */ }
            return V3;
        }
    }

    /// <summary>
    /// The timing rules that differ between the models. Measured on the
    /// 2026-11-17 lab takes (four of each, same voice, same 249 words):
    ///
    ///   • v3 leaves 0.6–0.9 s at a line break and spreads the break's
    ///     characters (newline, cue) across that silence.
    ///   • v4 leaves 0.25–0.5 s and gives it to the sentence-final punctuation
    ///     character: the newlines and the cue start at the END of the silence,
    ///     a few hundredths before the next word.
    ///
    /// So on v4 an own-line tag is stamped on the next word's onset already and
    /// the pause snap rarely fires; a sentence's last word "ends" after the
    /// pause; and the gap the alignment proposes for a section cut is a sliver
    /// at the silence's end. Each flag below answers one of those.
    /// </summary>
    public sealed class VoiceTiming
    {
        /// <summary>Shortest quiet run (below −35 dBFS) that counts as a pause —
        /// SegmentSequencer's snap, the caption verdict, PauseInserter's gaps.</summary>
        public float MinRealPauseSeconds = 0.25f;

        /// <summary>TagBeats: in a run where no tag snapped onto a pause's end,
        /// the run's first tag sets the beat and later ones join it.</summary>
        public bool FirstTagSetsBeat;

        /// <summary>Captions: a word's end stops at the start of a measured
        /// pause that begins inside it.</summary>
        public bool WordEndsAtPause;

        /// <summary>AudioSlicer: when the alignment's gap touches a silence, cut
        /// in the middle of that whole silence.</summary>
        public bool CutMidSilence;

        /// <summary>Exactly the numbers every take before v4 was timed with.</summary>
        public static readonly VoiceTiming V3 = new VoiceTiming();

        /// <summary>
        /// 0.20 s: v4's shortest break was 0.26 s under the engine's rule, one
        /// 20 ms window above the old 0.25 s floor; quiet runs inside words
        /// stay under 0.20 s on both models (2 of 127 on v4 reached it).
        /// </summary>
        public static readonly VoiceTiming V4 = new VoiceTiming {
            MinRealPauseSeconds = 0.20f,
            FirstTagSetsBeat    = true,
            WordEndsAtPause     = true,
            CutMidSilence       = true,
        };

        public static VoiceTiming For(string modelId) => VoiceModel.IsV4(modelId) ? V4 : V3;
    }
}

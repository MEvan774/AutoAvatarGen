using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Which animated backdrop the recording scene shows in Normal mode —
    /// "Synthwave" (the original prefab), "Late Night Desk" or "Night City" —
    /// plus the mood router that delivers {Mood:...} crossfades to whichever
    /// one is active.
    ///
    /// Persistence follows the house pattern (PlayerPrefs statics, same shape
    /// as BackgroundModeManager / PresenterTransitionSettings): the main
    /// menu's cycle row writes the pref, BackgroundModeManager reads it on
    /// every scene load and activates exactly one backdrop. The default is
    /// Synthwave, so existing behavior is untouched until the user opts in.
    ///
    /// Mood routing: MediaPresentationSystem calls <see cref="RouteMood"/>
    /// instead of talking to BackgroundMoodController directly. The router
    /// resolves the active <see cref="IAnimatedBackground"/> for the saved
    /// style and forwards the call — the presentation pipeline never knows or
    /// cares which backdrop is running.
    /// </summary>
    public static class BackgroundStyleManager
    {
        public enum Style
        {
            Synthwave      = 0,   // default — the original backdrop
            LateNightDesk  = 1,
            NightCityBokeh = 2,
            VioletDoodles  = 3,   // user-authored prefab rig (purple gradient + desk doodads)
        }

        public const string StylePrefKey = "AutoAvatarGen.BackgroundStyle";

        /// <summary>The saved style; Synthwave when unset or out of range.</summary>
        public static Style LoadStyle()
        {
            int v = PlayerPrefs.GetInt(StylePrefKey, (int)Style.Synthwave);
            int max = System.Enum.GetValues(typeof(Style)).Length - 1;
            if (v < 0 || v > max) return Style.Synthwave;
            return (Style)v;
        }

        public static void SaveStyle(Style style)
        {
            PlayerPrefs.SetInt(StylePrefKey, (int)style);
            PlayerPrefs.Save();
            // Take effect immediately in play mode (the menu scene has no
            // backdrop rigs, so there this is a cheap no-op; in the recording
            // scene it swaps live). Scene loads are covered by
            // BackgroundModeManager's sceneLoaded hook reading the pref.
            ApplyStyleToActiveScene();
        }

        public static string Label(Style s)
        {
            switch (s)
            {
                case Style.Synthwave:      return "Synthwave";
                case Style.LateNightDesk:  return "Late Night Desk";
                case Style.NightCityBokeh: return "Night City";
                case Style.VioletDoodles:  return "Violet Doodles";
                default:                   return s.ToString();
            }
        }

        public static Style Cycle(Style current, int direction = +1)
        {
            int count = System.Enum.GetValues(typeof(Style)).Length;
            int next = (((int)current + direction) % count + count) % count;
            return (Style)next;
        }

        // ----------------------------------------------------------------
        // Mood routing
        // ----------------------------------------------------------------

        // The last mood the pipeline asked for (crossfades count from the
        // moment they start). A style switch parks BOTH rigs here so the
        // incoming backdrop starts at the right mood and the hidden one can
        // never come back half-faded.
        static BackgroundMoodController.MoodType lastTargetMood =
            BackgroundMoodController.MoodType.CalmNeutral;

        /// <summary>One mood change as it happened in the take (seconds = voice clock).</summary>
        public struct MoodEvent
        {
            public float Seconds;
            public BackgroundMoodController.MoodType Mood;
            public bool Instant;
        }

        static readonly List<MoodEvent> moodEvents = new List<MoodEvent>();

        /// <summary>
        /// Mood changes of the current take, in order — the --auto-script
        /// result JSON's "moods" array. Cleared by <see cref="BeginMoodRun"/>.
        /// </summary>
        public static IReadOnlyList<MoodEvent> MoodEvents => moodEvents;

        /// <summary>
        /// A new take is being set up: forget the previous take's moods. The
        /// recording scene's rigs all open on Calm, so that is the "from" mood.
        /// </summary>
        public static void BeginMoodRun()
        {
            moodEvents.Clear();
            lastTargetMood = BackgroundMoodController.MoodType.CalmNeutral;
        }

        static BackgroundMoodController cachedSynthwaveTarget;
        static LateNightDeskBackground  cachedLateNightTarget;
        static NightCityBokehBackground cachedNightCityTarget;

        /// <summary>
        /// Deliver a {Mood:...} crossfade to the active background. No-op
        /// when the scene hosts no matching rig (same as the old
        /// `moodController != null` guards). `synthwaveTarget` lets
        /// MediaPresentationSystem pass its Inspector-assigned / auto-found
        /// BackgroundMoodController so the Synthwave path addresses the exact
        /// instance it always did.
        /// </summary>
        public static void RouteMood(BackgroundMoodController.MoodType mood,
                                     float crossfadeSeconds,
                                     BackgroundMoodController synthwaveTarget = null,
                                     float atSeconds = -1f)
        {
            BackgroundMoodController.MoodType from = lastTargetMood;
            lastTargetMood = mood;
            IAnimatedBackground bg = ResolveActiveBackground(synthwaveTarget);
            Record(from, mood, atSeconds, bg, $"fade={crossfadeSeconds:F1}s", instant: false);
            bg?.SetMood(mood, crossfadeSeconds);
        }

        /// <summary>
        /// Snap the active background to a mood with no crossfade — used for
        /// the video's opening mood (so frame 0 already shows it) and for a
        /// mood on a {Transition:...} line (switched while the screen is fully
        /// covered, so the reveal opens on the new colour like a real cut).
        /// <paramref name="why"/> only feeds the log line.
        /// </summary>
        public static void RouteMoodInstant(BackgroundMoodController.MoodType mood,
                                            BackgroundMoodController synthwaveTarget = null,
                                            float atSeconds = -1f,
                                            string why = "frame 0")
        {
            BackgroundMoodController.MoodType from = lastTargetMood;
            lastTargetMood = mood;
            IAnimatedBackground bg = ResolveActiveBackground(synthwaveTarget);
            Record(from, mood, atSeconds, bg, $"instant, {why}", instant: true);
            bg?.ApplyMoodInstant(mood);
        }

        // One "[Mood] t=… A -> B" line per real change (a repeat of the
        // current mood — e.g. the opening tag firing after it was already
        // snapped in — is silent), and the change goes into MoodEvents.
        static void Record(BackgroundMoodController.MoodType from, BackgroundMoodController.MoodType to,
                           float atSeconds, IAnimatedBackground bg, string how, bool instant)
        {
            bool first = moodEvents.Count == 0;
            if (!first && from == to) return;

            float seconds = Mathf.Max(0f, atSeconds);
            moodEvents.Add(new MoodEvent { Seconds = seconds, Mood = to, Instant = instant });

            string target = bg == null ? "no mood target (style ignores {Mood})"
                          : bg is VioletDoodlesMood violet ? $"bg={violet.DescribeBackground(to)}"
                          : $"style={Label(LoadStyle())}";
            string at = atSeconds >= 0f ? $"t={atSeconds:F2}s" : "t=?";
            Debug.Log($"[Mood] {at} {MoodLook.ScriptName(from)} -> {MoodLook.ScriptName(to)} ({target}, {how})");
        }

        /// <summary>
        /// The IAnimatedBackground the saved style selects, or null when the
        /// scene hosts none (e.g. the main menu).
        /// </summary>
        public static IAnimatedBackground ResolveActiveBackground(
            BackgroundMoodController synthwavePreferred = null)
        {
            if (LoadStyle() == Style.LateNightDesk)
                return FindLateNightTarget();
            if (LoadStyle() == Style.NightCityBokeh)
                return FindNightCityTarget();

            // Violet Doodles: a user-authored prefab rig whose mood driver
            // (VioletDoodlesMood — one picture per mood, crossfaded) is
            // attached at runtime, so it works without touching the prefab.
            if (LoadStyle() == Style.VioletDoodles)
                return VioletDoodlesMood.FindOrAttach();

            // Synthwave: prefer the caller's instance — the exact object the
            // pipeline addressed before the router existed — so this path
            // stays behavior-identical.
            if (synthwavePreferred != null) return synthwavePreferred;
            return FindSynthwaveTarget();
        }

        /// <summary>
        /// Re-applies the saved style to the loaded scene right now: swaps
        /// the rigs via BackgroundModeManager (exactly one active — SetActive
        /// (false) also kills the hidden rig's coroutines) and parks BOTH on
        /// the current target mood. ApplyMoodInstant cancels any in-flight
        /// crossfade, so switching mid-transition leaves no half-faded state
        /// and no orphaned lerp on either rig.
        /// </summary>
        public static void ApplyStyleToActiveScene()
        {
            if (!Application.isPlaying) return;
            BackgroundModeManager.ApplyToActiveScene();

            var synthwave = FindSynthwaveTarget();
            if (synthwave != null) synthwave.ApplyMoodInstant(lastTargetMood);
            var lateNight = FindLateNightTarget();
            if (lateNight != null) lateNight.ApplyMoodInstant(lastTargetMood);
            var nightCity = FindNightCityTarget();
            if (nightCity != null) nightCity.ApplyMoodInstant(lastTargetMood);
            var violet = VioletDoodlesMood.FindOrAttach();
            if (violet != null) violet.ApplyMoodInstant(lastTargetMood);
        }

        // Same lookup MediaPresentationSystem's old auto-find used
        // (FindObjectOfType, active objects only), cached until the instance
        // dies with its scene.
        static BackgroundMoodController FindSynthwaveTarget()
        {
            if (cachedSynthwaveTarget == null)
                cachedSynthwaveTarget = Object.FindObjectOfType<BackgroundMoodController>();
            return cachedSynthwaveTarget;
        }

        // includeInactive: the rig is deactivated in GreenScreen/Transparent
        // modes (and while Synthwave is selected) but must stay reachable so
        // a style switch can park it on the target mood before it shows.
        static LateNightDeskBackground FindLateNightTarget()
        {
            if (cachedLateNightTarget == null)
            {
                var found = Object.FindObjectsOfType<LateNightDeskBackground>(includeInactive: true);
                if (found.Length > 0) cachedLateNightTarget = found[0];
            }
            return cachedLateNightTarget;
        }

        static NightCityBokehBackground FindNightCityTarget()
        {
            if (cachedNightCityTarget == null)
            {
                var found = Object.FindObjectsOfType<NightCityBokehBackground>(includeInactive: true);
                if (found.Length > 0) cachedNightCityTarget = found[0];
            }
            return cachedNightCityTarget;
        }
    }
}

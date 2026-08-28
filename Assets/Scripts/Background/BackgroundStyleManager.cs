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
                                     BackgroundMoodController synthwaveTarget = null)
        {
            lastTargetMood = mood;
            IAnimatedBackground bg = ResolveActiveBackground(synthwaveTarget);
            bg?.SetMood(mood, crossfadeSeconds);
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

            // Violet Doodles is a static, user-authored prefab rig with no
            // mood controller (by choice): {Mood:...} tags are a deliberate
            // no-op while it's selected, same as they visually are on the
            // synthwave backdrop today.
            if (LoadStyle() == Style.VioletDoodles)
                return null;

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

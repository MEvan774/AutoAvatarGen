namespace MugsTech.Background
{
    /// <summary>
    /// Common contract for full-frame animated backgrounds that live behind
    /// the presenter and content zone and respond to the script's
    /// {Mood:Calm/Energetic/Tense/Playful/Minimal} markers.
    ///
    /// Implemented by <see cref="BackgroundMoodController"/> (the original
    /// ambient/synthwave mood driver) and <see cref="LateNightDeskBackground"/>.
    /// The mood vocabulary is shared on purpose: script tags map onto
    /// <see cref="BackgroundMoodController.MoodType"/> once (see
    /// MediaPresentationSystem.TryMapMood) and every background interprets the
    /// same five moods through its own preset table.
    /// </summary>
    public interface IAnimatedBackground
    {
        /// <summary>Mood last applied or currently being transitioned to.</summary>
        BackgroundMoodController.MoodType CurrentMood { get; }

        /// <summary>
        /// Smoothly crossfade every mood-driven parameter to the given mood
        /// over <paramref name="transitionDuration"/> seconds (ease-in-out).
        /// Interrupts any in-flight transition. Never hard-swaps.
        /// </summary>
        void SetMood(BackgroundMoodController.MoodType mood, float transitionDuration = 3f);

        /// <summary>Snap instantly to the given mood with no transition.</summary>
        void ApplyMoodInstant(BackgroundMoodController.MoodType mood);

        /// <summary>
        /// Show or hide the whole background rig (e.g. for the GreenScreen /
        /// Transparent recording modes — see BackgroundModeManager).
        /// </summary>
        void SetActive(bool active);
    }
}

using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// The built-in mood looks for the Violet Doodles backdrop: one background
    /// picture plus a <see cref="MoodProfile"/> per {Mood:X}. Lives at
    /// Assets/Resources/MoodBackgrounds.asset — being under Resources is what
    /// pulls the four mood PNGs (Assets/Art/Backgrounds) and the doodle tint
    /// shader into a player build, since nothing in the scene references them.
    ///
    /// Tune the numbers here in the Inspector (no code change). A mood with no
    /// background picture shows the rig's own artwork (Calm is always that).
    /// Any picture size works: it is cover-fitted to the Calm artwork's
    /// rectangle and centre-cropped, never stretched.
    /// </summary>
    [CreateAssetMenu(fileName = "MoodBackgrounds", menuName = "MugsTech/Mood Backgrounds")]
    public class MoodBackgroundSet : ScriptableObject
    {
        public const string ResourcePath = "MoodBackgrounds";

        [System.Serializable]
        public class Entry
        {
            public BackgroundMoodController.MoodType mood;
            [Tooltip("Background picture for this mood. Empty = the rig's own artwork.")]
            public Texture2D background;
            public MoodProfile look = MoodProfile.Neutral;
        }

        [Tooltip("Shader the floating doodles are drawn with so they can take a mood's tint " +
                 "(Resources/Shaders/MoodTintSprite). Identical to Sprites/Default at Calm.")]
        public Shader iconShader;

        public Entry[] moods = new Entry[0];

        public Entry Find(BackgroundMoodController.MoodType mood)
        {
            if (moods == null) return null;
            foreach (Entry e in moods)
                if (e != null && e.mood == mood) return e;
            return null;
        }

        static MoodBackgroundSet cached;
        static bool loaded;

        /// <summary>Resources/MoodBackgrounds, or null (built-in profiles, no pictures).</summary>
        public static MoodBackgroundSet Load()
        {
            if (!loaded)
            {
                cached = Resources.Load<MoodBackgroundSet>(ResourcePath);
                loaded = true;
            }
            return cached;
        }
    }
}

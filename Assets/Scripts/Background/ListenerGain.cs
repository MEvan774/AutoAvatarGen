using System.Threading;
using UnityEngine;
using Evereal.VideoCapture;

namespace MugsTech.Background
{
    /// <summary>
    /// The take's normalising gain: one fixed multiplier on the finished mix,
    /// as a filter on the AudioListener's object (AudioSource volumes stop at
    /// 1.0 and the project has no AudioMixer; this is the one place a few dB
    /// of boost can go). It must run BEFORE Evereal's AudioRecorder, which
    /// records the listener's mix from the same filter chain — Evereal appends
    /// its recorder to the listener's object when capture starts, so a filter
    /// added before the take comes first. Once the recorder exists this checks
    /// that, and logs an error (and marks the result JSON) if the file would
    /// miss the gain. Added by <see cref="TakeLoudness"/> only when the gain
    /// isn't 1 or the sound-effect layer is on — with "Normalise loudness" and
    /// "Sound effects" both off there is no filter at all.
    ///
    /// CEILING (only while the sound-effect layer is on): a sample past
    /// −1 dBFS is bent softly toward 0 dBFS instead of clipping. The gain
    /// already holds the voice and bed to −1 dBTP, so without an effect nothing
    /// reaches the knee and the take is untouched; it catches an effect that
    /// lands on a loud syllable. How many samples it touched is counted.
    /// </summary>
    public class ListenerGain : MonoBehaviour
    {
        /// <summary>The ceiling's knee: −1 dBFS. Below it every sample passes as it is.</summary>
        public const float CeilingKnee = 0.8912509f;

        volatile float gain = 1f;
        volatile bool ceiling;
        bool orderChecked;
        static int s_Softened;

        public float GainLinear
        {
            get => gain;
            set => gain = value;
        }

        /// <summary>The soft ceiling (the sound-effect layer turns it on).</summary>
        public bool Ceiling
        {
            get => ceiling;
            set => ceiling = value;
        }

        /// <summary>Samples the ceiling has bent this take (any thread may read it).</summary>
        public static int SoftenedSamples => Volatile.Read(ref s_Softened);
        public static void ResetSoftened() => Interlocked.Exchange(ref s_Softened, 0);

        /// <summary>The filter's arithmetic (public for the self-tests).</summary>
        public static void Apply(float[] data, float g) => Apply(data, g, false);

        /// <summary>The gain, then (with <paramref name="ceiling"/>) the soft ceiling. Returns the samples it bent.</summary>
        public static int Apply(float[] data, float g, bool ceiling)
        {
            if (!ceiling)
            {
                if (g == 1f) return 0;
                for (int i = 0; i < data.Length; i++) data[i] *= g;
                return 0;
            }
            int bent = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * g;
                if (v > CeilingKnee) { v = Soft(v); bent++; }
                else if (v < -CeilingKnee) { v = -Soft(-v); bent++; }
                data[i] = v;
            }
            return bent;
        }

        /// <summary>|x| above the knee → knee + (1 − knee)·tanh((|x| − knee)/(1 − knee)): slope 1 at the knee, never past 0 dBFS.</summary>
        public static float Soft(float a)
        {
            const float room = 1f - CeilingKnee;
            return CeilingKnee + room * (float)System.Math.Tanh((a - CeilingKnee) / room);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int bent = Apply(data, gain, ceiling);
            if (bent > 0) Interlocked.Add(ref s_Softened, bent);
        }

        void Update()
        {
            if (orderChecked || (gain == 1f && !ceiling)) return;
            AudioRecorder rec = AudioRecorder.singleton;
            if (rec == null) return;                  // capture hasn't started
            if (rec.gameObject != gameObject) return; // the recorder listens elsewhere — that listener's own ListenerGain checks
            orderChecked = true;

            int mine = -1, theirs = -1;
            Component[] comps = GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == this) mine = i;
                if (comps[i] == rec) theirs = i;
            }
            if (mine >= 0 && mine < theirs)
                Debug.Log($"[Loudness] Gain stage ({20f * Mathf.Log10(gain):+0.0;-0.0} dB{(ceiling ? ", −1 dBFS soft ceiling" : "")}) " +
                          $"runs ahead of Evereal's recorder on '{name}' — the file gets it.");
            else
            {
                Debug.LogError($"[Loudness] The gain stage sits AFTER Evereal's recorder on '{name}' — the recording will " +
                               "NOT get the normalising gain or the ceiling (they are still heard live).");
                TakeLoudness.ReportGainNotInFile("the gain stage ran after Evereal's recorder");
            }
        }
    }
}

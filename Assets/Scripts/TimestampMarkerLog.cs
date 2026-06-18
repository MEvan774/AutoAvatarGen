using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MugsTech
{
    /// <summary>
    /// One captured chapter marker: a free-text label plus the audio playback
    /// position (seconds, global across the stitched video) it was reached at.
    /// </summary>
    public struct TimestampMarker
    {
        public string Label;
        public float  Seconds;

        public TimestampMarker(string label, float seconds)
        {
            Label   = label;
            Seconds = seconds;
        }
    }

    /// <summary>
    /// Buffer of {Timestamp:"..."} markers captured during a recording run. Holds an
    /// in-memory static list for live display AND mirrors every change to a small
    /// JSON file, because the static list alone is NOT durable enough:
    ///
    ///  - The domain reload that fires when you press Play wipes statics, as does
    ///    any script recompile — so a run's markers can vanish between recording and
    ///    opening the window.
    ///  - The recording flow swaps scenes (MainMenu -> SampleScene -> MainMenu);
    ///    statics survive that, but a build recorder runs in a SEPARATE PROCESS, so
    ///    its static list is invisible to the Editor entirely.
    ///
    /// The JSON file fixes all of that. It lives under <see cref="Application.persistentDataPath"/>,
    /// which resolves to the SAME folder for the Editor and a standalone build on the
    /// same machine, so the Timestamps window always finds the most recent run's
    /// markers regardless of domain reloads, scene swaps, or which process recorded.
    /// </summary>
    public static class TimestampMarkerLog
    {
        static readonly List<TimestampMarker> _markers = new List<TimestampMarker>();

        /// <summary>Markers captured in the most recent run, in fire (chronological) order.</summary>
        public static IReadOnlyList<TimestampMarker> Markers => _markers;

        /// <summary>Incremented once per run by <see cref="BeginRun"/>.</summary>
        public static int RunCount { get; private set; }

        /// <summary>
        /// Where captured markers are persisted. The window shows this path so you
        /// always know where to look. Path.Combine + persistentDataPath keep it
        /// cross-platform (Windows 11 + Linux) with no hardcoded separators.
        /// </summary>
        public static string DiskPath =>
            Path.Combine(Application.persistentDataPath, "mugstech_timestamps.json");

        /// <summary>
        /// Call once at the start of each run (from
        /// MediaPresentationSystem.ProcessScriptWithMedia) so the previous run's
        /// markers are dropped from memory AND disk — the window always reflects the
        /// latest run, never stale data.
        /// </summary>
        public static void BeginRun()
        {
            _markers.Clear();
            RunCount++;
            WriteToDisk();
        }

        /// <summary>
        /// Append a captured marker (label + the live voiceAudio.time at the frame
        /// playback reached it) and mirror the buffer to disk.
        /// </summary>
        public static void Capture(string label, float seconds)
        {
            _markers.Add(new TimestampMarker(label ?? string.Empty, seconds));
            WriteToDisk();
        }

        /// <summary>Empty the buffer (memory + disk). Used by the window's Clear button.</summary>
        public static void Clear()
        {
            _markers.Clear();
            WriteToDisk();
        }

        // ---- display helpers (shared by the Editor window AND the runtime menu panel) ----

        /// <summary>
        /// Markers to display: the live in-memory list if a run just populated it,
        /// otherwise the last run persisted to disk. Returns a fresh copy. Works in
        /// a player build (reads the JSON file the build wrote).
        /// </summary>
        public static List<TimestampMarker> GetForDisplay()
        {
            if (_markers.Count > 0) return new List<TimestampMarker>(_markers);
            return TryLoadFromDisk(out var disk, out _) ? disk : new List<TimestampMarker>();
        }

        /// <summary>Whole-second YouTube format: M:SS, or H:MM:SS once past an hour.</summary>
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        /// <summary>
        /// The full copy-paste block: one "M:SS — Label" line per marker joined with
        /// '\n'. snapFirstToZero forces the first line to 0:00 (YouTube needs the
        /// first chapter at 0:00 to enable chapters).
        /// </summary>
        public static string BuildYouTubeBlock(IReadOnlyList<TimestampMarker> markers, bool snapFirstToZero)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < markers.Count; i++)
            {
                float s = (i == 0 && snapFirstToZero) ? 0f : markers[i].Seconds;
                if (i > 0) sb.Append('\n');
                sb.Append(FormatTime(s)).Append(" — ").Append(markers[i].Label);
            }
            return sb.ToString();
        }

        // ---- disk persistence ------------------------------------------------

        [System.Serializable] class DiskEntry   { public string label; public float seconds; }
        [System.Serializable] class DiskPayload { public int runCount; public List<DiskEntry> entries = new List<DiskEntry>(); }

        static void WriteToDisk()
        {
            try
            {
                var payload = new DiskPayload { runCount = RunCount };
                foreach (var m in _markers)
                    payload.entries.Add(new DiskEntry { label = m.Label, seconds = m.Seconds });
                File.WriteAllText(DiskPath, JsonUtility.ToJson(payload, true));
            }
            catch (System.Exception e)
            {
                // Never let a disk hiccup break playback/recording.
                Debug.LogWarning($"[Timestamp] Could not write {DiskPath}: {e.Message}");
            }
        }

        /// <summary>
        /// Load the last persisted run. Used by the Editor window when the in-memory
        /// list is empty (fresh session, post-recompile, or a build recorded it).
        /// </summary>
        public static bool TryLoadFromDisk(out List<TimestampMarker> markers, out int runCount)
        {
            markers  = new List<TimestampMarker>();
            runCount = 0;
            try
            {
                if (!File.Exists(DiskPath)) return false;
                var payload = JsonUtility.FromJson<DiskPayload>(File.ReadAllText(DiskPath));
                if (payload == null) return false;
                runCount = payload.runCount;
                if (payload.entries != null)
                    foreach (var e in payload.entries)
                        markers.Add(new TimestampMarker(e.label, e.seconds));
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Timestamp] Could not read {DiskPath}: {e.Message}");
                return false;
            }
        }
    }
}

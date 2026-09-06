using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Static per-take record of what the folder-based background-music mode
    /// did — which tracks played in what order, at what duck volume, and
    /// whether anything went wrong. Reset by BackgroundMusicPlayer when a
    /// take's music preload begins; read by RecordingSession (credits sidecar),
    /// AutomationRunner (result JSON music* fields) and MainMenuController
    /// (the loud "music failed" warning on the result panel).
    ///
    /// The upload pipeline's attribution step depends on this data, so the
    /// credits sidecar reproduces music_mixer/add_music.py's write_credits
    /// format byte-for-byte — downstream parsing stays one code path.
    /// </summary>
    public static class MusicTakeLog
    {
        public class Entry
        {
            public string FileName;
            public float  DurationSec;
            public bool   IsRepeat;
        }

        /// <summary>Configured music folder, or null when folder mode was off for the take.</summary>
        public static string FolderConfigured { get; private set; }

        /// <summary>Planned playlist in play order (repeats appear as duplicate entries).</summary>
        public static readonly List<Entry> Playlist = new List<Entry>();

        /// <summary>Constant duck volume the bed was played at (0 when folder mode off).</summary>
        public static float DuckVolume { get; private set; }

        /// <summary>Null on success; otherwise the human-readable reason the take has no/partial music.</summary>
        public static string Error { get; private set; }

        /// <summary>Path of the written credits sidecar, or null when none was written.</summary>
        public static string CreditsPath { get; private set; }

        /// <summary>Resets the log for a new take. folderOrNull == null means folder mode is off.</summary>
        public static void BeginTake(string folderOrNull, float duckVolume)
        {
            FolderConfigured = folderOrNull;
            DuckVolume       = folderOrNull != null ? duckVolume : 0f;
            Error            = null;
            CreditsPath      = null;
            Playlist.Clear();
        }

        public static void AddTrack(string fileName, float durationSec, bool isRepeat)
        {
            Playlist.Add(new Entry { FileName = fileName, DurationSec = durationSec, IsRepeat = isRepeat });
        }

        /// <summary>
        /// Records a music problem. The take must still finish — this is the
        /// loud flag, not an abort. Multiple problems are joined.
        /// </summary>
        public static void SetError(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Error = string.IsNullOrEmpty(Error) ? message : Error + " | " + message;
        }

        /// <summary>
        /// Ordered filenames actually planned for the take (repeats duplicated,
        /// not suffixed) — the shape the automation result JSON wants.
        /// </summary>
        public static List<string> PlayedFileNames()
        {
            var names = new List<string>(Playlist.Count);
            foreach (Entry e in Playlist) names.Add(e.FileName);
            return names;
        }

        /// <summary>
        /// Writes the "&lt;video stem&gt;_music_credits.txt" sidecar next to the
        /// saved video, in the exact format of add_music.py's write_credits
        /// (leading blank line, "=== stamp | video: a -> b ===" header, then
        /// "N. name [m:ss]" lines with " (repeat)" markers). For a Unity-baked
        /// take, both sides of the "->" carry the same file name. No-op when
        /// the take had no folder music. Never throws — a credits hiccup must
        /// never fail a finished take.
        /// </summary>
        public static string WriteCreditsSidecar(string videoPath)
        {
            try
            {
                if (string.IsNullOrEmpty(FolderConfigured) || Playlist.Count == 0 ||
                    string.IsNullOrEmpty(videoPath))
                    return null;

                string folder = Path.GetDirectoryName(videoPath);
                if (string.IsNullOrEmpty(folder)) return null;
                string path = Path.Combine(folder,
                    Path.GetFileNameWithoutExtension(videoPath) + "_music_credits.txt");

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                string name  = Path.GetFileName(videoPath);

                var sb = new StringBuilder(256);
                sb.Append('\n').Append("=== ").Append(stamp)
                  .Append(" | video: ").Append(name).Append(" -> ").Append(name).Append(" ===");
                int order = 1;
                foreach (Entry e in Playlist)
                {
                    int total = (int)e.DurationSec;
                    sb.Append('\n').Append(order++).Append(". ").Append(e.FileName)
                      .Append(" [").Append(total / 60).Append(':')
                      .Append((total % 60).ToString("00", CultureInfo.InvariantCulture)).Append(']');
                    if (e.IsRepeat) sb.Append(" (repeat)");
                }
                sb.Append('\n');

                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                CreditsPath = path;
                Debug.Log($"[BgMusic] Credits sidecar written: '{path}' ({Playlist.Count} entries).");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BgMusic] Could not write music credits next to '{videoPath}': {e.Message}");
                SetError("Could not write music credits: " + e.Message);
                return null;
            }
        }
    }
}

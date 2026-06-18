using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using MugsTech;   // TimestampMarker / TimestampMarkerLog (runtime statics + disk store)

namespace MugsTech.EditorTools
{
    /// <summary>
    /// MugsTech &gt; Timestamps — shows the {Timestamp:"..."} chapter markers captured
    /// during the last recording run and produces YouTube-ready "M:SS — Label" text
    /// with a one-click Copy.
    ///
    /// Editor-only: lives under Assets/Editor/ AND uses the UnityEditor API, so it's
    /// stripped from player builds. It reads markers from <see cref="TimestampMarkerLog"/>:
    /// the live in-memory list while you're in play mode, and the persisted JSON file
    /// otherwise — so the list survives domain reloads, scene swaps, recompiles, and
    /// even a recording done by a standalone build (same persistentDataPath folder).
    /// </summary>
    public class TimestampsWindow : EditorWindow
    {
        readonly List<TimestampMarker> _display = new List<TimestampMarker>();
        int     _displayRun = 0;
        Vector2 _scroll;
        bool    _snapFirstToZero = true;
        int     _lastSeenRun     = -1;
        int     _lastSeenCount   = -1;
        long    _lastDiskTicks   = -1;
        string  _exportFolder;

        [MenuItem("MugsTech/Timestamps")]
        public static void ShowWindow()
        {
            var w = GetWindow<TimestampsWindow>("Timestamps");
            w.minSize = new Vector2(380, 300);
            w.Show();
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (string.IsNullOrEmpty(_exportFolder))
                _exportFolder = ResolveDefaultExportFolder();
            _lastSeenRun   = TimestampMarkerLog.RunCount;
            _lastSeenCount = TimestampMarkerLog.Markers.Count;
            _lastDiskTicks = CurrentDiskTicks();
            RefreshDisplay();
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange _) => RefreshDisplay();

        // Pulls markers into _display: the live in-memory list when it has data
        // (during/just-after play in the Editor), otherwise the persisted file
        // (fresh session, after a recompile, or a build recorded it).
        void RefreshDisplay()
        {
            _display.Clear();
            if (TimestampMarkerLog.Markers.Count > 0)
            {
                _display.AddRange(TimestampMarkerLog.Markers);
                _displayRun = TimestampMarkerLog.RunCount;
            }
            else if (TimestampMarkerLog.TryLoadFromDisk(out var disk, out int run))
            {
                _display.AddRange(disk);
                _displayRun = run;
            }
            else
            {
                _displayRun = 0;
            }
            Repaint();
        }

        // Auto-refresh with no manual click, from EITHER source:
        //  - the in-memory list growing while a recording plays in the Editor, OR
        //  - the on-disk JSON file changing (a recording finished while this window
        //    was already open, OR a standalone build wrote it from another process).
        // RefreshDisplay() decides precedence (live memory first, else the file).
        void OnEditorUpdate()
        {
            bool changed = false;

            if (TimestampMarkerLog.RunCount      != _lastSeenRun ||
                TimestampMarkerLog.Markers.Count != _lastSeenCount)
            {
                _lastSeenRun   = TimestampMarkerLog.RunCount;
                _lastSeenCount = TimestampMarkerLog.Markers.Count;
                changed = true;
            }

            long diskTicks = CurrentDiskTicks();
            if (diskTicks != _lastDiskTicks)
            {
                _lastDiskTicks = diskTicks;
                changed = true;
            }

            if (changed) RefreshDisplay();
        }

        // Last-write time of the persisted file (0 if absent). A cheap stat call —
        // used to notice when a recording or build updates the file out from under us.
        static long CurrentDiskTicks()
        {
            try
            {
                return File.Exists(TimestampMarkerLog.DiskPath)
                    ? File.GetLastWriteTimeUtc(TimestampMarkerLog.DiskPath).Ticks
                    : 0L;
            }
            catch { return 0L; }
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("YouTube Chapter Timestamps", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                    RefreshDisplay();
            }

            // ---- empty state -------------------------------------------------
            if (_display.Count == 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "No timestamps captured yet.\n\n" +
                    "1. Make sure your script has {Timestamp:\"...\"} tags and you generated TTS from it.\n" +
                    "2. Run a recording (play the recording scene through to the end).\n" +
                    "3. Click Refresh.\n\n" +
                    "While the recording plays you should see [Timestamp] Captured ... lines in the Console.",
                    MessageType.Info);
                DrawDiskPathFooter();
                return;
            }

            // ---- options -----------------------------------------------------
            _snapFirstToZero = EditorGUILayout.ToggleLeft(
                new GUIContent("Snap first marker to 0:00",
                    "YouTube only enables chapters when the first one is 0:00. If your first captured " +
                    "marker isn't exactly 0, this shows/copies it as 0:00. The real value is reported " +
                    "below when it differs (never silently forced)."),
                _snapFirstToZero);

            EditorGUILayout.LabelField(
                $"{_display.Count} marker(s) — run #{_displayRun}" +
                (EditorApplication.isPlaying ? "  (capturing…)" : ""),
                EditorStyles.miniLabel);

            // ---- the copy-paste-ready block ----------------------------------
            string block = TimestampMarkerLog.BuildYouTubeBlock(_display, _snapFirstToZero);

            EditorGUILayout.Space(2);
            float blockHeight = Mathf.Clamp((_display.Count + 1) * 16f, 60f, 100000f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.SelectableLabel(block, EditorStyles.textArea,
                GUILayout.Height(blockHeight), GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();

            if (_snapFirstToZero && _display.Count > 0 && Mathf.RoundToInt(_display[0].Seconds) != 0)
            {
                EditorGUILayout.HelpBox(
                    $"First marker's real captured time is {TimestampMarkerLog.FormatTime(_display[0].Seconds)} — " +
                    "shown as 0:00 above because \"Snap first marker to 0:00\" is on.",
                    MessageType.None);
            }

            // ---- copy / clear ------------------------------------------------
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy", GUILayout.Height(28)))
                {
                    EditorGUIUtility.systemCopyBuffer = block;   // cross-platform (Win 11 + Linux)
                    ShowNotification(new GUIContent("Copied!"));
                }
                if (GUILayout.Button("Clear", GUILayout.Width(70), GUILayout.Height(28)))
                {
                    TimestampMarkerLog.Clear();
                    RefreshDisplay();
                }
            }

            // ---- optional export to .txt -------------------------------------
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Export (optional)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", GUILayout.Width(46));
                _exportFolder = EditorGUILayout.TextField(_exportFolder);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    string start = Directory.Exists(_exportFolder) ? _exportFolder : Application.dataPath;
                    string picked = EditorUtility.OpenFolderPanel("Export timestamps to…", start, "");
                    if (!string.IsNullOrEmpty(picked)) _exportFolder = picked;
                }
            }
            if (GUILayout.Button("Save timestamps.txt", GUILayout.Height(24)))
                SaveToFile(block);

            DrawDiskPathFooter();
        }

        void DrawDiskPathFooter()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Captured markers are saved to:", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(TimestampMarkerLog.DiskPath, EditorStyles.miniLabel,
                GUILayout.Height(14));
        }

        void SaveToFile(string block)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_exportFolder))
                    _exportFolder = ResolveDefaultExportFolder();
                Directory.CreateDirectory(_exportFolder);
                string path = Path.Combine(_exportFolder, "timestamps.txt");
                File.WriteAllText(path, block + "\n", new UTF8Encoding(false));
                AssetDatabase.Refresh();
                ShowNotification(new GUIContent("Saved timestamps.txt"));
                Debug.Log($"[Timestamps] Wrote {path}");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Save failed", e.Message, "OK");
            }
        }

        // Defaults the export folder to the ElevenLabs output folder the runtime
        // reads (so the .txt lands next to the _timed.txt scripts), using the SAME
        // PlayerPrefs keys. All paths via Path.Combine / Application.dataPath.
        static string ResolveDefaultExportFolder()
        {
            const string selectedKey = "AutoAvatarGen.SelectedGeneration"; // sync: ScriptFileReader
            const string outputKey   = "AutoAvatarGen.PythonOutputFolder"; // sync: ScriptFileReader / MainMenuController

            string selected = PlayerPrefs.GetString(selectedKey, "");
            if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(selected))
                return selected;

            string configured = PlayerPrefs.GetString(outputKey, "Python/output");
            string resolved = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(Application.dataPath, configured);
            if (Directory.Exists(resolved)) return resolved;

            return Path.Combine(Application.dataPath, "Python", "output");
        }
    }
}

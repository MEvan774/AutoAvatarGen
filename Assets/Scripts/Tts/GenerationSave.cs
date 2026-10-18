using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MugsTech.Tts
{
    /// <summary>
    /// Keeps a copy of every finished generation — script, voice and timing —
    /// in a save folder of the user's choosing, named after the video so it's
    /// easy to find again. The copy is a complete generation folder: the main
    /// menu's output library lists the save folder too, so picking a copy there
    /// records it, and Load From Generation fills the script box from it,
    /// exactly like the original in the output library root.
    /// </summary>
    public static class GenerationSave
    {
        /// <summary>Where copies go. Saved as "" when the user clears the field, which turns saving off.</summary>
        public const string FolderPrefKey    = "AutoAvatarGen.GenerationSaveFolder";
        /// <summary>The TTS panel's video name box, kept like the script draft.</summary>
        public const string VideoNamePrefKey = "AutoAvatarGen.TtsVideoName";

        const int MaxNameLength = 60;

        /// <summary>Used until the user picks another folder: Documents/AutoAvatarGen Saves.</summary>
        public static string DefaultFolder
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = Application.persistentDataPath;
                return Path.Combine(docs, "AutoAvatarGen Saves");
            }
        }

        /// <summary>The folder as typed in the TTS panel; empty when saving is off.</summary>
        public static string ConfiguredFolder => PlayerPrefs.GetString(FolderPrefKey, DefaultFolder).Trim();

        /// <summary>
        /// The save folder as an absolute path, or null when saving is off.
        /// Never throws: a path with characters no folder can hold comes back
        /// as typed, and <see cref="Copy"/> reports it.
        /// </summary>
        public static string ResolvedFolder
        {
            get
            {
                string folder = ConfiguredFolder;
                if (folder.Length == 0) return null;
                try { return TtsGenerationJob.ResolveOutputFolder(folder); }
                catch (ArgumentException) { return folder; }
            }
        }

        // ---- naming --------------------------------------------------------

        static readonly Regex FirstChapter = new Regex(@"\{Timestamp:""([^""]*)""");
        static readonly Regex FirstHeading = new Regex(@"^##[ \t]+(.+)$", RegexOptions.Multiline);

        /// <summary>
        /// The copy's folder name: when the generation started, then the video
        /// name — or, when that's blank, <see cref="NameFromScript"/>. The date
        /// leads so the library's newest-first sort keeps working:
        /// "2026-10-18_09-34 Tesla Recall".
        /// </summary>
        public static string FolderName(DateTime started, string videoName, string script)
        {
            string name = SanitiseName(videoName);
            if (name.Length == 0) name = SanitiseName(NameFromScript(script));
            string stamp = started.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
            return name.Length == 0 ? stamp : stamp + " " + name;
        }

        /// <summary>The script's first chapter title (<c>{Timestamp:"…"}</c>), else its first section heading.</summary>
        public static string NameFromScript(string script)
        {
            if (string.IsNullOrEmpty(script)) return "";
            Match chapter = FirstChapter.Match(script);
            if (chapter.Success && chapter.Groups[1].Value.Trim().Length > 0) return chapter.Groups[1].Value;
            Match heading = FirstHeading.Match(script);
            return heading.Success ? heading.Groups[1].Value : "";
        }

        /// <summary>
        /// A name Windows accepts as a folder name: no &lt;&gt;:"/\|?* or control
        /// characters, whitespace collapsed, no trailing dot or space, at most
        /// 60 characters.
        /// </summary>
        public static string SanitiseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0 ? ' ' : c);
            string clean = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength);
            return clean.TrimEnd(' ', '.');
        }

        // ---- copying -------------------------------------------------------

        /// <summary>
        /// Copies <paramref name="generationFolder"/>, subfolders included, to a
        /// new folder named <paramref name="folderName"/> under
        /// <paramref name="saveRoot"/> (created if missing), adding " (2)",
        /// " (3)"… when the name is taken. Returns the copy's path. Throws on
        /// IO failure after removing the partial copy. Plain file IO, so it can
        /// run off the main thread.
        /// </summary>
        public static string Copy(string generationFolder, string saveRoot, string folderName)
        {
            Directory.CreateDirectory(saveRoot);
            string dest = Path.Combine(saveRoot, folderName);
            for (int n = 2; Directory.Exists(dest) || File.Exists(dest); n++)
                dest = Path.Combine(saveRoot, $"{folderName} ({n})");

            try
            {
                CopyTree(generationFolder, dest);
            }
            catch
            {
                try { Directory.Delete(dest, true); } catch { /* best effort */ }
                throw;
            }
            return dest;
        }

        // Subfolders first and manifest.json last: a folder only counts as a
        // generation once it holds manifest.json or a *_timed.txt, so a copy cut
        // short (the app closed mid-way) never shows up in the library.
        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from))
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));

            var files = new List<string>(Directory.GetFiles(from));
            files.Sort((a, b) => CopyOrder(a).CompareTo(CopyOrder(b)));
            foreach (string file in files)
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        static int CopyOrder(string path)
        {
            string name = Path.GetFileName(path);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.EndsWith("_timed.txt", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }
    }
}

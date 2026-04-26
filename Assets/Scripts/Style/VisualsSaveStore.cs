using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MugsTech.Style
{
    /// <summary>
    /// File-system backed store for named visuals saves. Each save is a JSON file
    /// at <see cref="SavesDir"/>/&lt;sanitized-name&gt;.json. Use Save / Load with a
    /// name to manage internal saves, or ExportTo / LoadFromFile with a free-form
    /// path for backup files chosen via a file picker.
    /// </summary>
    public static class VisualsSaveStore
    {
        public static string SavesDir =>
            Path.Combine(Application.persistentDataPath, "VisualsSaves");

        public static string[] ListSaveNames()
        {
            if (!Directory.Exists(SavesDir)) return new string[0];
            return Directory.GetFiles(SavesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool Exists(string name) =>
            File.Exists(GetPath(name));

        public static string GetPath(string name) =>
            Path.Combine(SavesDir, SanitizeName(name) + ".json");

        public static void Save(VisualsSaveFile data)
        {
            Directory.CreateDirectory(SavesDir);
            data.savedAtIso = DateTime.UtcNow.ToString("o");
            File.WriteAllText(GetPath(data.name), JsonUtility.ToJson(data, prettyPrint: true));
        }

        public static VisualsSaveFile Load(string name)
        {
            string path = GetPath(name);
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path));
        }

        public static VisualsSaveFile LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            return Parse(File.ReadAllText(filePath));
        }

        public static void Delete(string name)
        {
            string path = GetPath(name);
            if (File.Exists(path)) File.Delete(path);
        }

        public static void ExportTo(VisualsSaveFile data, string filePath) =>
            File.WriteAllText(filePath, JsonUtility.ToJson(data, prettyPrint: true));

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
            string s = name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static VisualsSaveFile Parse(string json)
        {
            try { return JsonUtility.FromJson<VisualsSaveFile>(json); }
            catch (Exception e)
            {
                Debug.LogError("[VisualsSaveStore] Could not parse save: " + e.Message);
                return null;
            }
        }
    }
}

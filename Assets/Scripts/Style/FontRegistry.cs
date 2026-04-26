using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MugsTech.Style
{
    public enum FontSource { ProjectAsset, SystemFont, UserFile }

    /// <summary>
    /// One pickable font, carrying its origin (project / system / user-loaded
    /// file), a stable identifier that's safe to write into JSON saves, and a
    /// lazily-resolved <see cref="TMP_FontAsset"/>.
    /// </summary>
    public class FontEntry
    {
        public string        DisplayName;
        public string        Identifier;
        public FontSource    Source;
        public string        SourcePath; // user-file only — disk path
        public TMP_FontAsset Asset;      // lazy
    }

    /// <summary>
    /// Central font discovery and resolution for the visuals menu / runtime
    /// applier. The list is the union of:
    ///   - All TMP_FontAssets reachable via Resources.LoadAll (covers the
    ///     project-shipped TMP fonts under "Fonts &amp; Materials").
    ///   - Every OS-installed font reported by Font.GetOSInstalledFontNames.
    ///   - Any .ttf / .otf the user added via LoadFromFile() during this run.
    ///
    /// Identifiers are stable strings (project:&lt;name&gt;, system:&lt;name&gt;,
    /// user:&lt;abs path&gt;) so VisualsSaveFile can round-trip a font choice
    /// across runs.
    ///
    /// System and user-file assets are created via TMP's new path-based /
    /// family-name CreateFontAsset overloads (AtlasPopulationMode.DynamicOS),
    /// which read the font file directly through FreeType — no need for the
    /// asset to have "Include Font Data" enabled, and no need to process-
    /// install user files via Win32. Cross-platform.
    /// </summary>
    public static class FontRegistry
    {
        const int                  k_PointSize    = 90;
        const int                  k_AtlasPadding = 9;
        const int                  k_AtlasWidth   = 1024;
        const int                  k_AtlasHeight  = 1024;
        static readonly GlyphRenderMode k_RenderMode = GlyphRenderMode.SDFAA;

        static List<FontEntry>             _all;
        static Dictionary<string, FontEntry> _byIdentifier;

        public static IReadOnlyList<FontEntry> All
        {
            get { EnsureLoaded(); return _all; }
        }

        /// <summary>
        /// Resolves an identifier (e.g. "system:Arial") to a FontEntry with a
        /// non-null Asset, lazy-creating the TMP_FontAsset on first request.
        /// Returns null when the identifier is empty or can't be resolved
        /// (e.g. a user-file path that no longer exists on disk).
        /// </summary>
        public static FontEntry Resolve(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            EnsureLoaded();

            if (_byIdentifier.TryGetValue(identifier, out FontEntry entry))
            {
                EnsureAsset(entry);
                return entry.Asset != null ? entry : null;
            }

            // User-file identifiers carry the path inline — try to re-load
            // (the registry's in-memory state is lost on app restart).
            if (identifier.StartsWith("user:", StringComparison.Ordinal))
            {
                string path = identifier.Substring("user:".Length);
                return LoadFromFile(path);
            }
            return null;
        }

        /// <summary>
        /// Loads the given .ttf / .otf into a runtime TMP_FontAsset (via
        /// FreeType, no OS install required) and adds it to the registry.
        /// Returns the new entry, or null if the file can't be read.
        /// </summary>
        public static FontEntry LoadFromFile(string ttfPath)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(ttfPath) || !File.Exists(ttfPath)) return null;

            string id = "user:" + ttfPath;
            if (_byIdentifier.TryGetValue(id, out FontEntry existing))
            {
                EnsureAsset(existing);
                return existing.Asset != null ? existing : null;
            }

            TMP_FontAsset asset = CreateFromFilePath(ttfPath);
            if (asset == null)
            {
                Debug.LogWarning($"[FontRegistry] TMP could not load font file '{ttfPath}'.");
                return null;
            }

            var entry = new FontEntry
            {
                DisplayName = "[File] " + Path.GetFileNameWithoutExtension(ttfPath),
                Identifier  = id,
                Source      = FontSource.UserFile,
                SourcePath  = ttfPath,
                Asset       = asset,
            };
            Add(entry);
            return entry;
        }

        // -------------------------------------------------------------------
        // Internal — population & lazy asset creation
        // -------------------------------------------------------------------

        static void EnsureLoaded()
        {
            if (_all != null) return;
            _all          = new List<FontEntry>();
            _byIdentifier = new Dictionary<string, FontEntry>();

            LoadProjectFonts();
            LoadSystemFonts();
        }

        static void LoadProjectFonts()
        {
            TMP_FontAsset[] assets = Resources.LoadAll<TMP_FontAsset>("");
            foreach (TMP_FontAsset fa in assets)
            {
                if (fa == null) continue;
                string id = "project:" + fa.name;
                if (_byIdentifier.ContainsKey(id)) continue;
                Add(new FontEntry
                {
                    DisplayName = "[Project] " + fa.name,
                    Identifier  = id,
                    Source      = FontSource.ProjectAsset,
                    Asset       = fa,
                });
            }

            TMP_FontAsset def = TMP_Settings.defaultFontAsset;
            if (def != null)
            {
                string id = "project:" + def.name;
                if (!_byIdentifier.ContainsKey(id))
                {
                    Add(new FontEntry
                    {
                        DisplayName = "[Default] " + def.name,
                        Identifier  = id,
                        Source      = FontSource.ProjectAsset,
                        Asset       = def,
                    });
                }
            }
        }

        static void LoadSystemFonts()
        {
            string[] names;
            try { names = Font.GetOSInstalledFontNames(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[FontRegistry] System font enumeration failed: {e.Message}");
                return;
            }
            if (names == null) return;

            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                string id = "system:" + n;
                if (_byIdentifier.ContainsKey(id)) continue;
                Add(new FontEntry
                {
                    DisplayName = "[System] " + n,
                    Identifier  = id,
                    Source      = FontSource.SystemFont,
                });
            }
        }

        static void Add(FontEntry entry)
        {
            _all.Add(entry);
            _byIdentifier[entry.Identifier] = entry;
        }

        static void EnsureAsset(FontEntry entry)
        {
            if (entry == null || entry.Asset != null) return;

            switch (entry.Source)
            {
                case FontSource.SystemFont:
                {
                    string name = entry.Identifier.Substring("system:".Length);
                    entry.Asset = CreateFromSystemName(name);
                    if (entry.Asset == null)
                        Debug.LogWarning($"[FontRegistry] Could not resolve system font '{name}'.");
                    break;
                }
                case FontSource.UserFile:
                {
                    if (!string.IsNullOrEmpty(entry.SourcePath) && File.Exists(entry.SourcePath))
                    {
                        entry.Asset = CreateFromFilePath(entry.SourcePath);
                        if (entry.Asset == null)
                            Debug.LogWarning($"[FontRegistry] Could not load font file '{entry.SourcePath}'.");
                    }
                    break;
                }
                // ProjectAsset entries already carry their Asset.
            }
        }

        // -------------------------------------------------------------------
        // TMP CreateFontAsset wrappers
        // -------------------------------------------------------------------

        // System fonts arrive from Font.GetOSInstalledFontNames as full names
        // ("Alef Bold", "Arial Black", etc.). TMP wants family + style split.
        // We try the full name as family with "Regular" style first, then
        // peel a single trailing style word, then two trailing style words.
        // Returns null if nothing resolves.
        static TMP_FontAsset CreateFromSystemName(string fullName)
        {
            TMP_FontAsset a = TryCreate(fullName, "Regular");
            if (a != null) return a;

            string[] parts = fullName.Split(' ');
            if (parts.Length >= 2)
            {
                string family = string.Join(" ", parts, 0, parts.Length - 1);
                string style  = parts[parts.Length - 1];
                a = TryCreate(family, style);
                if (a != null) return a;
            }
            if (parts.Length >= 3)
            {
                string family = string.Join(" ", parts, 0, parts.Length - 2);
                string style  = parts[parts.Length - 2] + " " + parts[parts.Length - 1];
                a = TryCreate(family, style);
                if (a != null) return a;
            }
            return null;
        }

        static TMP_FontAsset TryCreate(string family, string style)
        {
            try { return TMP_FontAsset.CreateFontAsset(family, style, k_PointSize); }
            catch (Exception e)
            {
                Debug.LogWarning($"[FontRegistry] CreateFontAsset('{family}', '{style}') threw: {e.Message}");
                return null;
            }
        }

        // Path-based CreateFontAsset reads the file directly via FreeType —
        // works on all platforms without OS install or "Include Font Data".
        static TMP_FontAsset CreateFromFilePath(string path)
        {
            try
            {
                return TMP_FontAsset.CreateFontAsset(
                    path, 0, k_PointSize, k_AtlasPadding,
                    k_RenderMode, k_AtlasWidth, k_AtlasHeight);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FontRegistry] CreateFontAsset('{path}') threw: {e.Message}");
                return null;
            }
        }
    }
}

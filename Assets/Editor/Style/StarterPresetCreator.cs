using System.IO;
using UnityEditor;
using UnityEngine;
using MugsTech.Style;

namespace MugsTech.Style.Editor
{
    /// <summary>
    /// Creates the three starter ChannelStylePreset assets (Whimsical, Balanced,
    /// Corporate) in Assets/StylePresets/. Run once per project.
    /// Existing presets with the same name are skipped (not overwritten).
    /// </summary>
    public static class StarterPresetCreator
    {
        private const string PresetFolder = "Assets/StylePresets";

        [MenuItem("MugsTech/Style/Create Starter Presets")]
        public static void CreateAll()
        {
            EnsureFolderExists();

            CreatePreset("Mugs_Whimsical", whimsical =>
            {
                whimsical.channelName = "Mugs Whimsical";
                whimsical.identifier = "mugs_whimsical";
                whimsical.cardBackgroundColor = HexColor("FAF3E0");
                whimsical.cornerRadiusPx = 22f;
                whimsical.opacity = 0.88f;
                whimsical.shadowSoftness = 0.7f;
                whimsical.rotationVarianceRange = new Vector2(-3f, 3f);
                whimsical.wobbleIntensity = 0.6f;
                whimsical.headlineSize = 48f;
                whimsical.bodySize = 28f;
                whimsical.accentColor = HexColor("E85D4A");
                whimsical.accentDecorationsEnabled = true;
                whimsical.entryDirection = EntryDirectionMode.CharacterFacing;
                whimsical.entryCurve = EntryAnimationCurve.Elastic;
                whimsical.register = StyleRegister.Whimsical;
            });

            CreatePreset("Balanced_Default", balanced =>
            {
                balanced.channelName = "Balanced";
                balanced.identifier = "balanced";
                balanced.cardBackgroundColor = HexColor("F5E6C8");
                balanced.cornerRadiusPx = 14f;
                balanced.opacity = 0.92f;
                balanced.shadowSoftness = 0.5f;
                balanced.rotationVarianceRange = new Vector2(-1f, 1f);
                balanced.wobbleIntensity = 0.2f;
                balanced.headlineSize = 48f;
                balanced.bodySize = 28f;
                balanced.accentColor = HexColor("E85D4A");
                balanced.accentDecorationsEnabled = true;
                balanced.entryDirection = EntryDirectionMode.CharacterFacing;
                balanced.entryCurve = EntryAnimationCurve.EaseOut;
                balanced.register = StyleRegister.Balanced;
            });

            CreatePreset("Corporate_Clean", corporate =>
            {
                corporate.channelName = "Corporate";
                corporate.identifier = "corporate";
                corporate.cardBackgroundColor = Color.white;
                corporate.cornerRadiusPx = 4f;
                corporate.opacity = 1.0f;
                corporate.shadowSoftness = 0.2f;
                corporate.rotationVarianceRange = new Vector2(0f, 0f);
                corporate.wobbleIntensity = 0f;
                corporate.headlineSize = 48f;
                corporate.bodySize = 28f;
                corporate.accentColor = HexColor("0F3D7F");
                corporate.accentDecorationsEnabled = false;
                corporate.entryDirection = EntryDirectionMode.FromBottom;
                corporate.entryCurve = EntryAnimationCurve.Linear;
                corporate.register = StyleRegister.Serious;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Starter Presets",
                $"Starter presets created (or already exist) in {PresetFolder}.\n\n" +
                "Open MugsTech > Style > Style Presets to activate one.",
                "OK");
        }

        // -------------------------------------------------------------------

        private static void EnsureFolderExists()
        {
            if (!AssetDatabase.IsValidFolder(PresetFolder))
            {
                AssetDatabase.CreateFolder("Assets", "StylePresets");
            }
        }

        private static void CreatePreset(string fileName, System.Action<ChannelStylePreset> configure)
        {
            string path = $"{PresetFolder}/{fileName}.asset";
            if (File.Exists(path))
            {
                Debug.Log($"[StarterPresets] Skipping existing preset: {path}");
                return;
            }

            var preset = ScriptableObject.CreateInstance<ChannelStylePreset>();
            configure(preset);
            AssetDatabase.CreateAsset(preset, path);
            Debug.Log($"[StarterPresets] Created: {path}");
        }

        private static Color HexColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color c))
                return c;
            return Color.white;
        }
    }
}

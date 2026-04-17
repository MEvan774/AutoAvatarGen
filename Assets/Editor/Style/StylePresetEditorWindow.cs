using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MugsTech.Style;

namespace MugsTech.Style.Editor
{
    /// <summary>
    /// Editor window listing all ChannelStylePreset assets in the project.
    /// One-click activation, "Bake for Build" to write the active preset into
    /// every StyleManager in the open scene, and JSON export/import.
    ///
    /// You can ignore this window entirely if you'd rather just drag preset
    /// assets directly into StyleManager.defaultPreset in the Inspector.
    /// </summary>
    public class StylePresetEditorWindow : EditorWindow
    {
        private List<ChannelStylePreset> presets = new List<ChannelStylePreset>();
        private Vector2 scroll;

        [MenuItem("MugsTech/Style/Style Presets")]
        public static void Open()
        {
            var win = GetWindow<StylePresetEditorWindow>("Style Presets");
            win.minSize = new Vector2(420f, 200f);
            win.RefreshPresetList();
        }

        void OnEnable() { RefreshPresetList(); }

        private void RefreshPresetList()
        {
            presets.Clear();
            string[] guids = AssetDatabase.FindAssets("t:ChannelStylePreset");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var p = AssetDatabase.LoadAssetAtPath<ChannelStylePreset>(path);
                if (p != null) presets.Add(p);
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Channel Style Presets", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Refresh List"))
                RefreshPresetList();

            EditorGUILayout.HelpBox(
                "Activate a preset to apply it the next time you press Play. " +
                "The active preset is stored in EditorPrefs and persists between sessions. " +
                "For builds, use 'Bake for Build' to copy the active preset reference into " +
                "every StyleManager in the currently open scene.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            string activeGuid = EditorPrefs.GetString(StyleManager.EditorPrefKey, "");

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var p in presets)
            {
                if (p == null) continue;
                string path = AssetDatabase.GetAssetPath(p);
                string guid = AssetDatabase.AssetPathToGUID(path);
                bool isActive = guid == activeGuid;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                string label = $"{p.channelName}  [{p.register}]";
                if (isActive) label += "   ← ACTIVE";
                EditorGUILayout.LabelField(label, isActive ? EditorStyles.boldLabel : EditorStyles.label);

                if (GUILayout.Button("Edit", GUILayout.Width(48f)))
                {
                    Selection.activeObject = p;
                    EditorGUIUtility.PingObject(p);
                }
                if (GUILayout.Button(isActive ? "Active" : "Activate", GUILayout.Width(70f)))
                {
                    EditorPrefs.SetString(StyleManager.EditorPrefKey, guid);
                    Repaint();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Bake for Build", GUILayout.Width(110f)))
                    BakeForBuild(p);
                if (GUILayout.Button("Export JSON", GUILayout.Width(100f)))
                    ExportJson(p);
                if (GUILayout.Button("Import JSON", GUILayout.Width(100f)))
                    ImportJson(p);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Clear Active Preset (use scene's serialized default)"))
            {
                EditorPrefs.DeleteKey(StyleManager.EditorPrefKey);
                Repaint();
            }
        }

        // -------------------------------------------------------------------
        // Bake / Export / Import
        // -------------------------------------------------------------------

        private void BakeForBuild(ChannelStylePreset preset)
        {
            var managers = Object.FindObjectsOfType<StyleManager>();
            if (managers.Length == 0)
            {
                EditorUtility.DisplayDialog("Bake for Build",
                    "No StyleManager component found in the open scene. " +
                    "Add one to a GameObject first.", "OK");
                return;
            }

            foreach (var mgr in managers)
            {
                var so = new SerializedObject(mgr);
                var prop = so.FindProperty("defaultPreset");
                prop.objectReferenceValue = preset;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(mgr);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            EditorUtility.DisplayDialog("Bake for Build",
                $"Set '{preset.channelName}' as the default preset on " +
                $"{managers.Length} StyleManager(s). Save the scene to persist.", "OK");
        }

        private void ExportJson(ChannelStylePreset preset)
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Preset to JSON",
                "",
                $"{preset.channelName}.json",
                "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, preset.ToJson(prettyPrint: true));
            Debug.Log($"[StylePreset] Exported '{preset.channelName}' to: {path}");
        }

        private void ImportJson(ChannelStylePreset preset)
        {
            string path = EditorUtility.OpenFilePanel("Import Preset from JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            if (!EditorUtility.DisplayDialog("Import JSON",
                $"Overwrite '{preset.channelName}' with values from {Path.GetFileName(path)}?\n\n" +
                "Object references (font, sprites) won't be imported.", "Overwrite", "Cancel"))
                return;

            string json = File.ReadAllText(path);
            Undo.RecordObject(preset, "Import Preset JSON");
            preset.FromJson(json);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);
            Debug.Log($"[StylePreset] Imported into '{preset.channelName}' from: {path}");
        }
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for HybridAvatarSystem. Draws the normal fields, then adds an
/// "Emotion Guide (for Claude)" footer: a preview of the available emotion tags
/// plus a one-click "Copy Emotion Names for Claude" button.
///
/// The copied block lists the emotions you can fire as {Name} tags so Claude
/// writes scripts against your actual line-up. It reflects the "Emotion Images"
/// override array when that array has named entries; otherwise it falls back to
/// the project's default five (Neutral / Excited / Serious / Sad / Concerned).
///
/// Editor-only (lives under Assets/Editor/ and uses the UnityEditor API), so it's
/// stripped from player builds.
/// </summary>
[CustomEditor(typeof(HybridAvatarSystem))]
public class HybridAvatarSystemEditor : Editor
{
    static readonly string[] DefaultEmotions =
        { "Neutral", "Excited", "Serious", "Sad", "Concerned" };

    public override void OnInspectorGUI()
    {
        // All the normal serialized fields, including the override toggle + array.
        DrawDefaultInspector();

        var avatar = (HybridAvatarSystem)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Emotion Guide (for Claude)", EditorStyles.boldLabel);

        List<string> arrayNames = avatar.GetEmotionArrayNames();

        // Surface the two easy-to-miss states.
        if (avatar.useEmotionArrayOverride && arrayNames.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "'Use Emotion Array Override' is ON but the Emotion Images array has no " +
                "named entries. Add Name + Sprite rows above. The list below falls back " +
                "to the default five until you do.",
                MessageType.Warning);
        }
        else if (!avatar.useEmotionArrayOverride && arrayNames.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "The Emotion Images array has entries, but 'Use Emotion Array Override' is " +
                "OFF — these names are copied for Claude, but they won't drive the avatar at " +
                "record time until you check that box.",
                MessageType.Info);
        }

        // Effective set: the override array's names when present, else the defaults.
        List<string> names = arrayNames.Count > 0
            ? arrayNames
            : new List<string>(DefaultEmotions);

        string block = BuildClaudeEmotionBlock(names);

        EditorGUILayout.SelectableLabel(
            block, EditorStyles.textArea,
            GUILayout.Height(Mathf.Clamp((names.Count + 5) * 16f, 80f, 360f)),
            GUILayout.ExpandWidth(true));

        if (GUILayout.Button("Copy Emotion Names for Claude", GUILayout.Height(28)))
        {
            EditorGUIUtility.systemCopyBuffer = block; // cross-platform (Win 11 + Linux)
            Debug.Log($"[HybridAvatarSystem] Copied {names.Count} emotion name(s) to the " +
                      "clipboard for Claude.");
            if (EditorWindow.focusedWindow != null)
                EditorWindow.focusedWindow.ShowNotification(new GUIContent("Copied!"));
        }
    }

    // A paste-ready instruction Claude can read so it only uses the emotions that
    // actually exist for this video. Mirrors the conventions in SCRIPT_TAG_GUIDE.md
    // (own-line {Name} tags, exact spelling/capitalization).
    static string BuildClaudeEmotionBlock(List<string> names)
    {
        string example = names.Count > 0 ? names[0] : "Neutral";

        var sb = new StringBuilder();
        sb.AppendLine("Available emotions for this video — use ONLY these names as {Emotion} tags,");
        sb.AppendLine("spelled exactly as shown (capitalization matters), and use no emotion name");
        sb.AppendLine("outside this list. Place a tag on its own line just before the narration line");
        sb.AppendLine($"it should color, e.g. {{{example}}}.");
        sb.AppendLine();

        var tags = new StringBuilder();
        foreach (string n in names)
        {
            if (tags.Length > 0) tags.Append(' ');
            tags.Append('{').Append(n).Append('}');
        }
        sb.Append(tags);
        return sb.ToString();
    }
}

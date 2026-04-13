using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// ScriptableObject that maps company names to logo sprites and
/// b-roll descriptions to video clips for the content card system.
/// Create via Assets > Create > MugsTech > Content Card Assets.
/// </summary>
[CreateAssetMenu(fileName = "ContentCardAssets", menuName = "MugsTech/Content Card Assets")]
public class ContentCardAssets : ScriptableObject
{
    [System.Serializable]
    public class LogoEntry
    {
        public string companyName;
        public Sprite sprite;
    }

    [System.Serializable]
    public class BRollEntry
    {
        public string description;
        public VideoClip clip;
    }

    [Header("Logo Sprites")]
    [Tooltip("Map company names (lowercase) to logo sprites for LogoDisplay cards.")]
    public List<LogoEntry> logos = new List<LogoEntry>();

    [Header("B-Roll Clips")]
    [Tooltip("Map description strings to video clips for BRoll cards.")]
    public List<BRollEntry> bRollClips = new List<BRollEntry>();

    // Lazy-built runtime dictionaries for O(1) lookup
    private Dictionary<string, Sprite> logoDict;
    private Dictionary<string, VideoClip> bRollDict;

    public Sprite GetLogo(string name)
    {
        if (logoDict == null)
        {
            logoDict = new Dictionary<string, Sprite>();
            foreach (var entry in logos)
            {
                if (!string.IsNullOrEmpty(entry.companyName))
                    logoDict[entry.companyName.ToLower()] = entry.sprite;
            }
        }

        if (logoDict.TryGetValue(name.ToLower(), out Sprite sprite))
            return sprite;

        Debug.LogWarning($"ContentCardAssets: No logo found for \"{name}\"");
        return null;
    }

    public VideoClip GetBRoll(string description)
    {
        if (bRollDict == null)
        {
            bRollDict = new Dictionary<string, VideoClip>();
            foreach (var entry in bRollClips)
            {
                if (!string.IsNullOrEmpty(entry.description))
                    bRollDict[entry.description.ToLower()] = entry.clip;
            }
        }

        if (bRollDict.TryGetValue(description.ToLower(), out VideoClip clip))
            return clip;

        Debug.LogWarning($"ContentCardAssets: No b-roll clip found for \"{description}\"");
        return null;
    }
}

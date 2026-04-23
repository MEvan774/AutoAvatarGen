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

    [Header("Big Media Fallback")]
    [Tooltip("Resources folder path to search for BigMedia images when the name doesn't match a logo. Default: \"Media\".")]
    public string bigMediaResourcesFolder = "Media";

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

    /// <summary>
    /// Resolves a name for a BigMedia card. Tries the logo dictionary first,
    /// then falls back to a Sprite in Resources/{bigMediaResourcesFolder}/{name},
    /// and finally to a Texture2D at the same path (wrapped as a Sprite).
    /// Returns null if nothing matches.
    /// </summary>
    public Sprite GetBigMedia(string name)
    {
        Sprite logo = GetLogoSilent(name);
        if (logo != null) return logo;

        string folder = string.IsNullOrEmpty(bigMediaResourcesFolder) ? "Media" : bigMediaResourcesFolder;

        Sprite sprite = Resources.Load<Sprite>($"{folder}/{name}");
        if (sprite != null) return sprite;

        Texture2D tex = Resources.Load<Texture2D>($"{folder}/{name}");
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        Debug.LogWarning($"ContentCardAssets: No BigMedia asset found for \"{name}\"");
        return null;
    }

    private Sprite GetLogoSilent(string name)
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
        return logoDict.TryGetValue(name.ToLower(), out Sprite sprite) ? sprite : null;
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

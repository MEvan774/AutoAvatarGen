using System;
using System.Collections.Generic;

namespace MugsTech.Style
{
    /// <summary>
    /// JSON-serializable snapshot of the visuals editor state. Image bytes are
    /// embedded base64 so a save file is portable across machines (true backup).
    /// The original path is also kept — at load time we prefer the path so live
    /// edits to the source file propagate, falling back to the embedded copy if
    /// the path no longer resolves.
    /// </summary>
    [Serializable]
    public class VisualsSaveFile
    {
        public int    schemaVersion = 1;
        public string name          = "";
        public string savedAtIso    = "";
        // Absolute file path to a background mp4. Empty = use the scene
        // default. Note: the main menu's "Background Video Override" still
        // wins over this — it's an ad-hoc top-level override that is never
        // persisted into the preset.
        public string backgroundVideoPath = "";
        public CardStyleData       card     = new CardStyleData();
        public BigTextStyleData    bigText  = new BigTextStyleData();
        public BackgroundMusicData music    = new BackgroundMusicData();
        public List<EmotionImageData> emotions = new List<EmotionImageData>();
    }

    /// <summary>
    /// Background music playlist that travels with the preset. The runtime
    /// player loops through <see cref="filePaths"/> in order, applies
    /// loudness normalization per clip so different masters sound balanced,
    /// then scales by <see cref="volume"/>. Default volume is 15% — soft
    /// enough to live underneath the script's voice without competing.
    /// </summary>
    [Serializable]
    public class BackgroundMusicData
    {
        public List<string> filePaths = new List<string>();
        public float        volume    = 0.15f;
    }

    [Serializable]
    public class CardStyleData
    {
        public string bgColorHex   = "#FAF3E0FF";
        public float  cornerRadius = 18f;
        public string textColorHex = "#1A1A1FFF";
        public int    fontStyle    = 0; // UnityEngine.FontStyle as int
        // FontRegistry identifier — empty = no override (use TMP default).
        // Format: "project:<name>" / "system:<name>" / "user:<absolute-path>"
        public string fontName     = "";
    }

    /// <summary>
    /// Style for the big-text overlay (BigTextCard). Outline is always on
    /// (just edit color/width); shadow and background each have their own
    /// enabled flag.
    /// </summary>
    [Serializable]
    public class BigTextStyleData
    {
        public string textColorHex            = "#FFFFFFFF";
        public int    fontStyle               = 1; // UnityEngine.FontStyle as int (1 = Bold, the default look)
        public string outlineColorHex         = "#000000BF"; // black @ ~0.75 alpha
        public float  outlineWidth            = 0.10f;       // TMP _OutlineWidth (0..1)
        public bool   shadowEnabled           = false;
        public string shadowColorHex          = "#000000BF";
        public float  shadowSoftness          = 0.5f;        // TMP _UnderlaySoftness
        public bool   backgroundEnabled       = false;
        public string backgroundColorHex      = "#000000A0";
        public float  backgroundCornerRadius  = 18f;
    }

    [Serializable]
    public class EmotionImageData
    {
        public string emotion;       // e.g. "Neutral"
        public string originalPath;  // last-known disk location (may not still exist)
        public string imageBase64;   // raw bytes of the PNG/JPG/etc, base64-encoded
        public string extension;     // file extension without dot, lowercase
    }
}

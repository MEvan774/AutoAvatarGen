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
        public CardStyleData       card     = new CardStyleData();
        public List<EmotionImageData> emotions = new List<EmotionImageData>();
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

    [Serializable]
    public class EmotionImageData
    {
        public string emotion;       // e.g. "Neutral"
        public string originalPath;  // last-known disk location (may not still exist)
        public string imageBase64;   // raw bytes of the PNG/JPG/etc, base64-encoded
        public string extension;     // file extension without dot, lowercase
    }
}

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
        // Saves written before this block existed load it with these defaults,
        // which reproduce the original look — no schema bump needed.
        public BigHeadlineStyleData bigHeadline = new BigHeadlineStyleData();
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

    /// <summary>
    /// Style of the fullscreen headline (BigCenterCard, the
    /// <c>{Headline:"…","…",N,bigCenter}</c> tag): a solid panel, an optional
    /// scrolling tiled pattern drawn by Custom/UIScrollingPattern, and the
    /// headline/source text. Every default reproduces the card's original look.
    /// </summary>
    [Serializable]
    public class BigHeadlineStyleData
    {
        // Empty = follow the side cards' background (the original look). Once
        // set, the headline keeps its own color. Always drawn fully opaque.
        public string backgroundColorHex   = "";
        public bool   patternEnabled       = true;
        // Pattern image embedded like EmotionImageData so the save stays
        // portable. Empty base64 = built-in Resources/Patterns/Default.
        public string patternImageBase64   = "";
        public string patternExtension     = "";
        public string patternOriginalPath  = "";
        // Tint multiplies the image (so it recolors light pixels; a black
        // image stays black). Its alpha lives in patternOpacity instead of the
        // hex so the default stays exactly 0.35 — 8-bit hex can't hold that.
        public string patternTintHex       = "#FFFFFFFF";
        public float  patternOpacity       = 0.35f;
        public float  patternTileScale     = 12f;    // tiles across the panel width
        public float  patternTilePadding   = 0.05f;  // empty ring per tile (0..0.49)
        public float  patternScrollSpeed   = 0.36f;  // tiles per second
        // Direction the pattern visibly travels, in degrees (0 = right,
        // 90 = up). 135 = up-left, which is what the original negative speed
        // against the shader's default (1,-1) direction produced.
        public float  patternScrollDirection = 135f;

        // ---- Headline + source text. Empty / -1 = follow the side cards'
        // text style (the original look). Outline and shadow apply to both.
        public string headlineColorHex = "";
        public string sourceColorHex   = "";
        public string fontName         = "";    // FontRegistry identifier
        public int    fontStyle        = -1;    // UnityEngine.FontStyle as int
        public bool   outlineEnabled   = false;
        public string outlineColorHex  = "#000000FF";
        public float  outlineWidth     = 0.15f; // TMP _OutlineWidth (0..1)
        public bool   shadowEnabled    = false;
        public string shadowColorHex   = "#000000BF";
        public float  shadowSoftness   = 0.5f;  // TMP _UnderlaySoftness

        // ---- Vignette: edge darkening over the panel and pattern, under the
        // text (Custom/UIVignette). Off by default = the original look.
        public bool   vignetteEnabled  = false;
        public string vignetteColorHex = "#000000FF"; // RGB; alpha lives in vignetteStrength
        public float  vignetteStrength = 0.6f;  // opacity at full darkness
        public float  vignetteSize     = 0.35f; // how far it reaches toward the center
        public float  vignetteSoftness = 0.6f;  // width of the fade band
        public float  vignetteShape    = 0.5f;  // 0 = round, 1 = follows the frame
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

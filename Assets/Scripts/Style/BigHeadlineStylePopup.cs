using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Style
{
    /// <summary>
    /// Modal popup for editing BigHeadlineStyleData — the fullscreen headline
    /// (BigCenterCard). A Background tab (panel color + scrolling tiled
    /// pattern) and a Text tab (colors, font, style, outline, shadow).
    /// Self-builds its UI on first Show(), mutates the passed-in data directly
    /// and fires onChanged on every edit so the visuals menu can persist live
    /// (same contract as BigTextStylePopup).
    ///
    /// The live preview is built with BigCenterCard's own drawing calls on a
    /// 1920×1080 stage scaled into the popup, so it shows what the recording
    /// renders — pattern scrolling included.
    ///
    /// Usage:
    ///   BigHeadlineStylePopup.GetOrCreate(panelRoot.transform)
    ///                        .Show(myStyle, previewContext, pickImage, pickFont, () =&gt; persist());
    /// </summary>
    public class BigHeadlineStylePopup : MonoBehaviour
    {
        /// <summary>Visuals-menu state the preview needs to match the recording.</summary>
        public struct PreviewContext
        {
            public Color         CardBackground; // side cards' color — the "match cards" default
            public float         CornerRadius;   // side cards' corner radius (the panel reuses it)
            public Color         TextColor;
            public TMP_FontAsset Font;           // null = TMP default, as on the card
            public FontStyle     FontStyle;
            public bool          HasActiveSave;  // recordings only apply a named save
        }

        public static BigHeadlineStylePopup GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<BigHeadlineStylePopup>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("BigHeadlineStylePopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<BigHeadlineStylePopup>();
        }

        BigHeadlineStyleData data;
        PreviewContext       ctx;
        Action               onChanged;
        Func<string, string> pickImagePath;
        Func<string>         pickFontPath;
        bool                 built;

        // The side cards' text style as the preview's ctx describes it — what
        // unset text options fall back to (mirrors CardTextStyle.Capture on
        // the real card).
        BigCenterCard.CardTextStyle cardText;

        // Preview — the same pieces BigCenterCard builds.
        Image           previewPanel;
        RawImage        previewPattern;
        Material        previewMaterial;
        TextMeshProUGUI previewHeadline;
        TextMeshProUGUI previewSource;
        Texture2D       previewTexture;     // decoded custom pattern, owned by the popup
        string          previewTextureKey;

        // Refreshable controls
        Image   backgroundSwatch;
        Button  matchCardsButton;
        Toggle  patternToggle;
        Text    patternImageLabel;
        Button  useDefaultButton;
        Image   tintSwatch;
        Slider  opacitySlider;
        Slider  tileSizeSlider;
        Slider  spacingSlider;
        Slider  speedSlider;
        Text    opacityValue;
        Text    tileSizeValue;
        Text    spacingValue;
        Text    speedValue;
        Image[] directionButtons;
        Text    statusLabel;
        Text    saveHintLabel;

        // Text tab
        Image   headlineColorSwatch;
        Image   sourceColorSwatch;
        Button  matchHeadlineButton;
        Button  matchSourceButton;
        Text    fontNameLabel;
        Text    fontStyleLabel;
        Toggle  outlineToggle;
        Image   outlineColorSwatch;
        Slider  outlineWidthSlider;
        Text    outlineWidthValue;
        Toggle  shadowToggle;
        Image   shadowColorSwatch;
        Slider  shadowSoftnessSlider;
        Text    shadowSoftnessValue;

        // Tabs: 0 = Background, 1 = Text. Kept across opens.
        GameObject[] tabGroups;
        Image[]      tabButtons;
        Text         resetButtonLabel;
        int          activeTab;
        Transform    rowParent;  // group the row builders add to

        // Visible travel directions, clockwise from up (degrees, 0 = right).
        static readonly float[] k_Directions = { 90f, 45f, 0f, 315f, 270f, 225f, 180f, 135f };

        // The tile-size slider works in canvas pixels per tile; the data stores
        // tiles across the (1920-wide) panel.
        const float k_StageWidth  = 1920f;
        const float k_StageHeight = 1080f;

        public void Show(BigHeadlineStyleData target, PreviewContext context,
                         Func<string, string> pickImage, Func<string> pickFont, Action onChange)
        {
            this.data          = target;
            this.ctx           = context;
            this.pickImagePath = pickImage;
            this.pickFontPath  = pickFont;
            this.onChanged     = onChange;
            if (!built) BuildUI();
            statusLabel.text   = "";
            saveHintLabel.text = ctx.HasActiveSave
                ? ""
                : "Recordings use named saves — use Save As to apply this look.";
            ApplyPreviewContext();
            RefreshAllControls();
            RefreshPreview();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        void Close()
        {
            gameObject.SetActive(false);
            onChanged     = null;
            pickImagePath = null;
            pickFontPath  = null;
            data          = null;
        }

        void OnDestroy()
        {
            if (previewTexture  != null) Destroy(previewTexture);
            if (previewMaterial != null) Destroy(previewMaterial);
        }

        // Wraps the parent-supplied onChanged so the preview stays in sync.
        void NotifyChanged()
        {
            RefreshPreview();
            onChanged?.Invoke();
        }

        // -------------------------------------------------------------------
        // Refresh
        // -------------------------------------------------------------------

        void RefreshAllControls()
        {
            if (data == null) return;
            bool matchesCards = string.IsNullOrEmpty(data.backgroundColorHex);
            backgroundSwatch.color        = EffectivePanelColor();
            matchCardsButton.interactable = !matchesCards;

            patternToggle.SetIsOnWithoutNotify(data.patternEnabled);
            UpdatePatternImageLabel();
            useDefaultButton.interactable = HasCustomPattern();
            tintSwatch.color = TintColor();

            opacitySlider.SetValueWithoutNotify(data.patternOpacity);
            tileSizeSlider.SetValueWithoutNotify(k_StageWidth / Mathf.Max(0.5f, data.patternTileScale));
            spacingSlider.SetValueWithoutNotify(data.patternTilePadding);
            speedSlider.SetValueWithoutNotify(data.patternScrollSpeed);
            UpdateDirectionButtons();

            headlineColorSwatch.color        = ParseHex(data.headlineColorHex, cardText.HeadlineColor);
            sourceColorSwatch.color          = ParseHex(data.sourceColorHex,   cardText.SourceColor);
            matchHeadlineButton.interactable = !string.IsNullOrEmpty(data.headlineColorHex);
            matchSourceButton.interactable   = !string.IsNullOrEmpty(data.sourceColorHex);
            UpdateFontLabels();
            outlineToggle.SetIsOnWithoutNotify(data.outlineEnabled);
            outlineColorSwatch.color = ParseHex(data.outlineColorHex, Color.black);
            outlineWidthSlider.SetValueWithoutNotify(data.outlineWidth);
            shadowToggle.SetIsOnWithoutNotify(data.shadowEnabled);
            shadowColorSwatch.color = ParseHex(data.shadowColorHex, new Color(0f, 0f, 0f, 0.75f));
            shadowSoftnessSlider.SetValueWithoutNotify(data.shadowSoftness);

            UpdateSliderReadouts();
            PaintTabs();
        }

        void UpdateSliderReadouts()
        {
            opacityValue.text  = Mathf.RoundToInt(data.patternOpacity * 100f) + "%";
            tileSizeValue.text = Mathf.RoundToInt(k_StageWidth / Mathf.Max(0.5f, data.patternTileScale)) + " px";
            spacingValue.text  = Mathf.RoundToInt(data.patternTilePadding * 100f) + "%";
            speedValue.text    = data.patternScrollSpeed.ToString("0.00");
            outlineWidthValue.text   = data.outlineWidth.ToString("0.00");
            shadowSoftnessValue.text = data.shadowSoftness.ToString("0.00");
        }

        void UpdateFontLabels()
        {
            if (string.IsNullOrEmpty(data.fontName))
                fontNameLabel.text = "Match Cards";
            else
            {
                FontEntry entry = FontRegistry.Resolve(data.fontName);
                fontNameLabel.text = entry != null ? entry.DisplayName : data.fontName + "  (missing)";
            }

            switch (data.fontStyle)
            {
                case (int)FontStyle.Normal:        fontStyleLabel.text = "Normal"; break;
                case (int)FontStyle.Bold:          fontStyleLabel.text = "Bold"; break;
                case (int)FontStyle.Italic:        fontStyleLabel.text = "Italic"; break;
                case (int)FontStyle.BoldAndItalic: fontStyleLabel.text = "Bold + Italic"; break;
                default:                           fontStyleLabel.text = "Match Cards"; break;
            }
        }

        void UpdatePatternImageLabel()
        {
            if (!HasCustomPattern())
            {
                patternImageLabel.text = "Built-in pattern";
                return;
            }
            string file = Path.GetFileName(data.patternOriginalPath ?? "");
            if (string.IsNullOrEmpty(file)) file = "Embedded image";
            patternImageLabel.text = file + (string.IsNullOrEmpty(data.patternImageBase64) ? "  (missing)" : "");
        }

        void UpdateDirectionButtons()
        {
            for (int i = 0; i < directionButtons.Length; i++)
            {
                bool active = Mathf.Abs(Mathf.DeltaAngle(k_Directions[i], data.patternScrollDirection)) < 1f;
                directionButtons[i].color = active ? k_ActiveColor : k_TrackColor;
            }
        }

        // The preview's panel color, sprite and text style mirror what the
        // recording derives from the same save (ContentCardUIBuilder's
        // CreateBackground / TextPrimary / TextTertiary / font overrides).
        void ApplyPreviewContext()
        {
            int radius = Mathf.Max(0, Mathf.RoundToInt(ctx.CornerRadius));
            if (radius > 0)
            {
                previewPanel.sprite = StyleSpriteFactory.GetRoundedRect(radius);
                previewPanel.type   = Image.Type.Sliced;
            }
            else
            {
                previewPanel.sprite = null;
                previewPanel.type   = Image.Type.Simple;
            }

            // What ContentCardUIBuilder.CreateText gives the card when a save
            // with this card style is active: its font (TMP default when
            // unset), the requested styles ORed with the card style, the card
            // text color and the 70% tertiary for the source line.
            FontStyles userMask = ToTmpFontStyles(ctx.FontStyle);
            Color tertiary = ctx.TextColor;
            tertiary.a *= 0.70f;
            cardText = new BigCenterCard.CardTextStyle
            {
                Font          = ctx.Font != null ? ctx.Font : TMP_Settings.defaultFontAsset,
                HeadlineStyle = BigCenterCard.HeadlineFontStyles | userMask,
                SourceStyle   = BigCenterCard.SourceFontStyles | userMask,
                HeadlineColor = ctx.TextColor,
                SourceColor   = tertiary,
            };
        }

        void RefreshPreview()
        {
            if (data == null) return;
            BigHeadlineLook look = BigHeadlineLook.FromStyle(data, ResolvePreviewTexture());
            previewPanel.color = BigCenterCard.ResolvePanelColor(look, CardPaper());
            if (previewPattern != null) BigCenterCard.ApplyPatternLook(previewPattern, look);
            BigCenterCard.ApplyTextLook(previewHeadline, previewSource, look, cardText);
        }

        // Decodes the custom pattern once per distinct image. The key compare
        // is a reference hit while the same base64 string is in place.
        Texture2D ResolvePreviewTexture()
        {
            string key = !string.IsNullOrEmpty(data.patternImageBase64)  ? data.patternImageBase64
                       : !string.IsNullOrEmpty(data.patternOriginalPath) ? "path:" + data.patternOriginalPath
                       : "";
            if (key == previewTextureKey) return previewTexture;

            if (previewTexture != null) Destroy(previewTexture);
            previewTexture    = null;
            previewTextureKey = key;
            if (key.Length == 0) return null;

            byte[] bytes = VisualsRuntimeApplier.ReadPatternBytes(data);
            if (bytes != null) previewTexture = VisualsRuntimeApplier.DecodePatternTexture(bytes);
            return previewTexture;
        }

        bool HasCustomPattern() =>
            !string.IsNullOrEmpty(data.patternImageBase64) || !string.IsNullOrEmpty(data.patternOriginalPath);

        // Same palette hook ContentCardUIBuilder.CreateBackground applies to
        // the card color, so "match cards" previews the recorded color.
        Color CardPaper() => BackdropPalette.CardPaper(ctx.CardBackground);

        Color EffectivePanelColor() =>
            BigCenterCard.ResolvePanelColor(BigHeadlineLook.FromStyle(data, null), CardPaper());

        Color TintColor()
        {
            Color c = ParseHex(data.patternTintHex, Color.white);
            c.a = Mathf.Clamp01(data.patternOpacity);
            return c;
        }

        // -------------------------------------------------------------------
        // Edits
        // -------------------------------------------------------------------

        void OnChoosePatternImage()
        {
            if (pickImagePath == null) return;
            string picked = pickImagePath(data.patternOriginalPath ?? "");
            if (string.IsNullOrEmpty(picked)) return; // cancelled, or no picker in this build

            byte[] bytes;
            try { bytes = File.ReadAllBytes(picked); }
            catch (Exception e)
            {
                statusLabel.text = "Could not read the file: " + e.Message;
                return;
            }

            Texture2D tex = VisualsRuntimeApplier.DecodePatternTexture(bytes);
            if (tex == null)
            {
                statusLabel.text = "Not a PNG/JPG image Unity can read.";
                return;
            }

            data.patternImageBase64  = Convert.ToBase64String(bytes);
            data.patternExtension    = Path.GetExtension(picked).TrimStart('.').ToLowerInvariant();
            data.patternOriginalPath = picked;

            // Adopt the validation decode as the preview texture.
            if (previewTexture != null) Destroy(previewTexture);
            previewTexture    = tex;
            previewTextureKey = data.patternImageBase64;

            statusLabel.text = $"Using {Path.GetFileName(picked)} ({tex.width}×{tex.height}) — a copy is stored in the save.";
            RefreshAllControls();
            NotifyChanged();
        }

        void OnUseDefaultPattern()
        {
            data.patternImageBase64  = "";
            data.patternExtension    = "";
            data.patternOriginalPath = "";
            statusLabel.text = "";
            RefreshAllControls();
            NotifyChanged();
        }

        // Resets only the visible tab's settings to the original look.
        void OnResetTab()
        {
            var d = new BigHeadlineStyleData();
            if (activeTab == 0)
            {
                data.backgroundColorHex     = d.backgroundColorHex;
                data.patternEnabled         = d.patternEnabled;
                data.patternImageBase64     = d.patternImageBase64;
                data.patternExtension       = d.patternExtension;
                data.patternOriginalPath    = d.patternOriginalPath;
                data.patternTintHex         = d.patternTintHex;
                data.patternOpacity         = d.patternOpacity;
                data.patternTileScale       = d.patternTileScale;
                data.patternTilePadding     = d.patternTilePadding;
                data.patternScrollSpeed     = d.patternScrollSpeed;
                data.patternScrollDirection = d.patternScrollDirection;
            }
            else
            {
                data.headlineColorHex = d.headlineColorHex;
                data.sourceColorHex   = d.sourceColorHex;
                data.fontName         = d.fontName;
                data.fontStyle        = d.fontStyle;
                data.outlineEnabled   = d.outlineEnabled;
                data.outlineColorHex  = d.outlineColorHex;
                data.outlineWidth     = d.outlineWidth;
                data.shadowEnabled    = d.shadowEnabled;
                data.shadowColorHex   = d.shadowColorHex;
                data.shadowSoftness   = d.shadowSoftness;
            }
            statusLabel.text = "";
            RefreshAllControls();
            NotifyChanged();
        }

        // Steps through "Match Cards" + every FontRegistry font (project,
        // system and loaded files), like the visuals menu's own font picker.
        void CycleFont(int delta)
        {
            var all = FontRegistry.All;
            int total = all.Count + 1;
            int current = 0;
            if (!string.IsNullOrEmpty(data.fontName))
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Identifier == data.fontName) { current = i + 1; break; }

            int next = ((current + delta) % total + total) % total;
            data.fontName = next == 0 ? "" : all[next - 1].Identifier;
            statusLabel.text = "";
            UpdateFontLabels();
            NotifyChanged();
        }

        void OnLoadFontFile()
        {
            string picked = pickFontPath?.Invoke();
            if (string.IsNullOrEmpty(picked)) return; // cancelled, or no picker in this build

            FontEntry entry = FontRegistry.LoadFromFile(picked);
            if (entry == null)
            {
                statusLabel.text = "Could not load that font file.";
                return;
            }
            data.fontName = entry.Identifier;
            statusLabel.text = "Using font " + entry.DisplayName + ".";
            UpdateFontLabels();
            NotifyChanged();
        }

        // Match Cards (-1) → Normal → Bold → Italic → Bold + Italic → Match Cards.
        void CycleFontStyle()
        {
            data.fontStyle = data.fontStyle >= (int)FontStyle.BoldAndItalic ? -1 : data.fontStyle + 1;
            UpdateFontLabels();
            NotifyChanged();
        }

        void SelectTab(int tab)
        {
            activeTab = tab;
            for (int i = 0; i < tabGroups.Length; i++) tabGroups[i].SetActive(i == tab);
            resetButtonLabel.text = tab == 0 ? "Reset Background" : "Reset Text";
            PaintTabs();
        }

        void PaintTabs()
        {
            for (int i = 0; i < tabButtons.Length; i++)
                tabButtons[i].color = i == activeTab ? k_ActiveColor : k_TrackColor;
        }

        static FontStyles ToTmpFontStyles(FontStyle s)
        {
            switch (s)
            {
                case FontStyle.Bold:          return FontStyles.Bold;
                case FontStyle.Italic:        return FontStyles.Italic;
                case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
                default:                      return FontStyles.Normal;
            }
        }

        // -------------------------------------------------------------------
        // UI construction
        // -------------------------------------------------------------------

        const float k_PanelWidth   = 1560f;
        const float k_PanelHeight  = 940f;
        const float k_PreviewW     = 768f;                 // 0.4 × 1920
        const float k_PreviewH     = k_PreviewW * 9f / 16f;
        const float k_ColumnTop    = k_PanelHeight * 0.5f - 100f;
        const float k_ControlsTop  = k_ColumnTop - 30f;    // first row, under the tabs
        const float k_PreviewX     = -k_PanelWidth * 0.5f + 40f + k_PreviewW * 0.5f;
        const float k_ControlsX    = 410f;                 // controls column center
        const float k_ControlsW    = 660f;
        const float k_RowHeight    = 50f;
        const float k_RowGap       = 12f;
        const float k_LabelW       = 190f;
        const float k_ControlLeft  = -k_ControlsW * 0.5f + k_LabelW; // control's left edge within a row

        static readonly Color k_PanelColor  = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        static readonly Color k_LabelColor  = new Color(0.85f, 0.88f, 0.93f, 1f);
        static readonly Color k_HeaderColor = new Color(0.55f, 0.78f, 1f, 1f);
        static readonly Color k_MutedColor  = new Color(0.55f, 0.58f, 0.64f, 1f);
        static readonly Color k_TrackColor  = new Color(0.18f, 0.20f, 0.24f, 1f);
        static readonly Color k_ActiveColor = new Color(0.20f, 0.45f, 0.65f, 1f);

        Transform panelTf;

        void BuildUI()
        {
            built = true;

            var selfRT = (RectTransform)transform;
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

            var backdrop = NewChild("Backdrop", transform, stretch: true);
            backdrop.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var panel = NewChild("Panel", transform, stretch: false);
            panelTf = panel.transform;
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(k_PanelWidth, k_PanelHeight);
            panel.AddComponent<Image>().color = k_PanelColor;

            var title = NewText("Title", panelTf, "Big Headline Style", 32, FontStyle.Bold,
                                TextAnchor.MiddleCenter, new Color(0.95f, 0.97f, 1f, 1f));
            Place(title.rectTransform, new Vector2(0f, k_PanelHeight * 0.5f - 50f), new Vector2(k_PanelWidth - 60f, 56f));

            BuildPreviewArea();
            BuildTabs();
            BuildBackgroundControls();
            BuildTextControls();
            BuildBottomButtons();
            SelectTab(activeTab);

            // Skin the plain buttons like the rest of the menu now (the menu
            // re-skins on every open anyway). Swatches, tabs and direction
            // buttons use Transition.None, and RefreshAllControls repaints them.
            UITheme.Apply(gameObject);
        }

        void BuildPreviewArea()
        {
            var header = NewText("PreviewHeader", panelTf, "Live preview", 22, FontStyle.Bold,
                                 TextAnchor.MiddleLeft, k_HeaderColor);
            Place(header.rectTransform, new Vector2(k_PreviewX, k_ColumnTop + 4f), new Vector2(k_PreviewW, 36f));

            // Frame at the preview's on-screen size; a dark "scene" shows
            // through the panel's rounded corners like the backdrop would.
            var frame = NewChild("PreviewFrame", panelTf, stretch: false);
            var frt = (RectTransform)frame.transform;
            Place(frt, new Vector2(k_PreviewX, k_ColumnTop - 24f - k_PreviewH * 0.5f), new Vector2(k_PreviewW, k_PreviewH));
            var scene = frame.AddComponent<Image>();
            scene.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            scene.raycastTarget = false;

            // 1920×1080 stage scaled into the frame, so BigCenterCard's
            // layout, auto-sized text and tile counts come out identical.
            var stage = NewChild("Stage", frame.transform, stretch: false);
            var srt = (RectTransform)stage.transform;
            Place(srt, Vector2.zero, new Vector2(k_StageWidth, k_StageHeight));
            srt.localScale = Vector3.one * (k_PreviewW / k_StageWidth);

            var panelGO = NewChild("OverlayPanel", stage.transform, stretch: true);
            previewPanel = panelGO.AddComponent<Image>();
            previewPanel.raycastTarget = false;

            previewPattern = BigCenterCard.CreatePatternLayer((RectTransform)panelGO.transform);
            if (previewPattern != null) previewMaterial = previewPattern.material;

            BigCenterCard.CreateHeadlineBlock(srt, Color.white, Color.white,
                out previewHeadline, out previewSource);
            previewHeadline.text = "Test Headline Text";
            previewSource.text   = "Test Outlet";

            var caption = NewText("PreviewCaption", panelTf,
                "Panel, pattern and text exactly as the recording draws them.",
                18, FontStyle.Normal, TextAnchor.UpperLeft, k_MutedColor);
            Place(caption.rectTransform,
                  new Vector2(k_PreviewX, k_ColumnTop - 24f - k_PreviewH - 26f),
                  new Vector2(k_PreviewW, 30f));

            saveHintLabel = NewText("SaveHint", panelTf, "", 18, FontStyle.Italic,
                                    TextAnchor.UpperLeft, new Color(1f, 0.80f, 0.45f, 1f));
            Place(saveHintLabel.rectTransform,
                  new Vector2(k_PreviewX, k_ColumnTop - 24f - k_PreviewH - 58f),
                  new Vector2(k_PreviewW, 30f));

            statusLabel = NewText("Status", panelTf, "", 18, FontStyle.Normal,
                                  TextAnchor.UpperLeft, k_LabelColor);
            Place(statusLabel.rectTransform,
                  new Vector2(k_PreviewX, k_ColumnTop - 24f - k_PreviewH - 90f),
                  new Vector2(k_PreviewW, 30f));
        }

        // Tab buttons across the top of the controls column; each tab's rows
        // live in their own group that SelectTab shows/hides.
        void BuildTabs()
        {
            string[] names = { "Background", "Text" };
            tabGroups  = new GameObject[names.Length];
            tabButtons = new Image[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int tab = i;
                var btn = BuildButton(panelTf, "Tab" + names[i], names[i],
                    new Vector2(k_ControlsX - k_ControlsW * 0.5f + 100f + i * 212f, k_ColumnTop + 4f),
                    new Vector2(200f, 40f));
                btn.transition = Selectable.Transition.None; // PaintTabs shows the active one
                btn.onClick.AddListener(() => SelectTab(tab));
                tabButtons[i] = (Image)btn.targetGraphic;

                tabGroups[i] = NewChild(names[i] + "Tab", panelTf, stretch: true);
            }
        }

        void BuildBackgroundControls()
        {
            rowParent = tabGroups[0].transform;
            float y = k_ControlsTop;

            BuildSectionHeader("Panel", ref y);

            // Background color + "match cards" reset (empty hex = follow the
            // side cards' color, the original look).
            var bgRow = NewRow("Swatch_Background", ref y);
            BuildLabel(bgRow.transform, "Color");
            backgroundSwatch = BuildSwatch(bgRow.transform, 250f,
                () => EffectivePanelColor(),
                c =>
                {
                    c.a = 1f; // the panel is always opaque
                    data.backgroundColorHex = HexOf(c);
                    RefreshAllControls();
                    NotifyChanged();
                });
            matchCardsButton = BuildButton(bgRow.transform, "MatchCards", "Match Cards",
                new Vector2(k_ControlLeft + 250f + 12f + 100f, 0f), new Vector2(200f, 40f));
            matchCardsButton.onClick.AddListener(() =>
            {
                data.backgroundColorHex = "";
                RefreshAllControls();
                NotifyChanged();
            });
            y -= 10f;

            patternToggle = BuildToggleHeader("Pattern", "scrolling tiles over the panel", ref y, v =>
            {
                data.patternEnabled = v;
                NotifyChanged();
            });

            var imgRow = NewRow("PatternImage", ref y);
            BuildLabel(imgRow.transform, "Image");
            var chooseBtn = BuildButton(imgRow.transform, "ChoosePatternImage", "Choose Image…",
                new Vector2(k_ControlLeft + 85f, 0f), new Vector2(170f, 40f));
            chooseBtn.onClick.AddListener(OnChoosePatternImage);
            useDefaultButton = BuildButton(imgRow.transform, "UseDefaultPattern", "Use Default",
                new Vector2(k_ControlLeft + 170f + 10f + 70f, 0f), new Vector2(140f, 40f));
            useDefaultButton.onClick.AddListener(OnUseDefaultPattern);
            patternImageLabel = NewText("FileName", imgRow.transform, "", 18, FontStyle.Italic,
                                        TextAnchor.MiddleLeft, k_MutedColor);
            Place(patternImageLabel.rectTransform,
                  new Vector2(k_ControlLeft + 330f + 70f, 0f), new Vector2(140f, k_RowHeight));
            patternImageLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            patternImageLabel.resizeTextForBestFit = true;
            patternImageLabel.resizeTextMinSize = 12;
            patternImageLabel.resizeTextMaxSize = 18;

            var tintRow = NewRow("Swatch_Tint", ref y);
            BuildLabel(tintRow.transform, "Tint");
            tintSwatch = BuildSwatch(tintRow.transform, 250f,
                () => TintColor(),
                c =>
                {
                    data.patternOpacity = c.a;
                    c.a = 1f;
                    data.patternTintHex = HexOf(c);
                    RefreshAllControls();
                    NotifyChanged();
                });
            var tintHint = NewText("TintHint", tintRow.transform, "multiplies the image", 16,
                                   FontStyle.Italic, TextAnchor.MiddleLeft, k_MutedColor);
            Place(tintHint.rectTransform, new Vector2(k_ControlLeft + 250f + 12f + 100f, 0f),
                  new Vector2(200f, k_RowHeight));

            opacitySlider = BuildSliderRow("Opacity", ref y, 0f, 1f, out opacityValue,
                v => data.patternOpacity = v);
            tileSizeSlider = BuildSliderRow("Tile Size", ref y, 40f, 480f, out tileSizeValue,
                v => data.patternTileScale = k_StageWidth / Mathf.Max(1f, v));
            spacingSlider = BuildSliderRow("Spacing", ref y, 0f, 0.45f, out spacingValue,
                v => data.patternTilePadding = v);
            speedSlider = BuildSliderRow("Speed", ref y, 0f, 1.5f, out speedValue,
                v => data.patternScrollSpeed = v);

            BuildDirectionRow(ref y);
        }

        void BuildTextControls()
        {
            rowParent = tabGroups[1].transform;
            float y = k_ControlsTop;

            headlineColorSwatch = BuildColorRowWithMatch("Headline Color", ref y,
                () => ParseHex(data.headlineColorHex, cardText.HeadlineColor),
                hex => data.headlineColorHex = hex, out matchHeadlineButton);
            sourceColorSwatch = BuildColorRowWithMatch("Source Color", ref y,
                () => ParseHex(data.sourceColorHex, cardText.SourceColor),
                hex => data.sourceColorHex = hex, out matchSourceButton);

            // Font: ‹ name › plus loading a .ttf/.otf from disk.
            var fontRow = NewRow("Font", ref y);
            BuildLabel(fontRow.transform, "Font");
            var prev = BuildButton(fontRow.transform, "FontPrev", "<",
                new Vector2(k_ControlLeft + 22f, 0f), new Vector2(44f, 40f));
            prev.onClick.AddListener(() => CycleFont(-1));
            fontNameLabel = NewText("FontName", fontRow.transform, "", 18, FontStyle.Normal,
                                    TextAnchor.MiddleCenter, k_LabelColor);
            Place(fontNameLabel.rectTransform, new Vector2(k_ControlLeft + 54f + 100f, 0f), new Vector2(200f, k_RowHeight));
            fontNameLabel.resizeTextForBestFit = true;
            fontNameLabel.resizeTextMinSize = 12;
            fontNameLabel.resizeTextMaxSize = 18;
            var next = BuildButton(fontRow.transform, "FontNext", ">",
                new Vector2(k_ControlLeft + 264f + 22f, 0f), new Vector2(44f, 40f));
            next.onClick.AddListener(() => CycleFont(+1));
            var load = BuildButton(fontRow.transform, "LoadFontFile", "Load File…",
                new Vector2(k_ControlLeft + 318f + 76f, 0f), new Vector2(152f, 40f));
            load.onClick.AddListener(OnLoadFontFile);

            var styleRow = NewRow("Style", ref y);
            BuildLabel(styleRow.transform, "Style");
            var styleBtn = BuildButton(styleRow.transform, "FontStyleCycle", "",
                new Vector2(k_ControlLeft + 125f, 0f), new Vector2(250f, 40f));
            fontStyleLabel = styleBtn.GetComponentInChildren<Text>();
            styleBtn.onClick.AddListener(CycleFontStyle);
            var styleHint = NewText("StyleHint", styleRow.transform, "source stays italic", 16,
                                    FontStyle.Italic, TextAnchor.MiddleLeft, k_MutedColor);
            Place(styleHint.rectTransform, new Vector2(k_ControlLeft + 250f + 12f + 100f, 0f),
                  new Vector2(200f, k_RowHeight));
            y -= 10f;

            outlineToggle = BuildToggleHeader("Outline", "around every letter", ref y, v =>
            {
                data.outlineEnabled = v;
                NotifyChanged();
            });
            outlineColorSwatch = BuildColorRow("Color", ref y,
                () => ParseHex(data.outlineColorHex, Color.black),
                hex => data.outlineColorHex = hex);
            outlineWidthSlider = BuildSliderRow("Thickness", ref y, 0f, 0.4f, out outlineWidthValue,
                v => data.outlineWidth = v);
            y -= 10f;

            shadowToggle = BuildToggleHeader("Shadow", "soft drop shadow", ref y, v =>
            {
                data.shadowEnabled = v;
                NotifyChanged();
            });
            shadowColorSwatch = BuildColorRow("Color", ref y,
                () => ParseHex(data.shadowColorHex, new Color(0f, 0f, 0f, 0.75f)),
                hex => data.shadowColorHex = hex);
            shadowSoftnessSlider = BuildSliderRow("Softness", ref y, 0f, 1f, out shadowSoftnessValue,
                v => data.shadowSoftness = v);
        }

        // A color swatch row (alpha kept) writing its pick as hex.
        Image BuildColorRow(string label, ref float y, Func<Color> getColor, Action<string> setHex)
        {
            var row = NewRow("Swatch_" + label, ref y);
            BuildLabel(row.transform, label);
            return BuildSwatch(row.transform, 250f, getColor, c =>
            {
                setHex(HexOf(c));
                RefreshAllControls();
                NotifyChanged();
            });
        }

        // Same, plus a "Match Cards" button that clears the override (empty
        // hex = follow the side cards' text color, the original look).
        Image BuildColorRowWithMatch(string label, ref float y, Func<Color> getColor,
                                     Action<string> setHex, out Button matchButton)
        {
            Image swatch = BuildColorRow(label, ref y, getColor, setHex);
            matchButton = BuildButton(swatch.transform.parent, "Match" + label.Replace(" ", ""), "Match Cards",
                new Vector2(k_ControlLeft + 250f + 12f + 100f, 0f), new Vector2(200f, 40f));
            matchButton.onClick.AddListener(() =>
            {
                setHex("");
                RefreshAllControls();
                NotifyChanged();
            });
            return swatch;
        }

        void BuildBottomButtons()
        {
            float y = -k_PanelHeight * 0.5f + 50f;
            var reset = BuildButton(panelTf, "ResetTabButton", "Reset Background",
                new Vector2(-150f, y), new Vector2(300f, 60f));
            reset.onClick.AddListener(OnResetTab);
            resetButtonLabel = reset.GetComponentInChildren<Text>();
            var close = BuildButton(panelTf, "CloseButton", "Close",
                new Vector2(140f, y), new Vector2(220f, 60f));
            close.onClick.AddListener(Close);
            foreach (var t in new[] { reset, close })
            {
                var label = t.GetComponentInChildren<Text>();
                label.fontSize = 24;
            }
        }

        // -------------------------------------------------------------------
        // Row builders
        // -------------------------------------------------------------------

        void BuildSectionHeader(string text, ref float y)
        {
            var row = NewRow("Header_" + text, ref y, 36f);
            var t = NewText("Label", row.transform, text, 22, FontStyle.Bold, TextAnchor.MiddleLeft, k_HeaderColor);
            Stretch(t.rectTransform);
        }

        Toggle BuildToggleHeader(string text, string hintText, ref float y, Action<bool> setVal)
        {
            var row = NewRow("ToggleHeader_" + text, ref y);
            var label = NewText("Label", row.transform, text, 22, FontStyle.Bold, TextAnchor.MiddleLeft, k_HeaderColor);
            Place(label.rectTransform, new Vector2(-k_ControlsW * 0.5f + k_LabelW * 0.5f, 0f),
                  new Vector2(k_LabelW, k_RowHeight));

            var toggleGO = NewChild("Toggle", row.transform, stretch: false);
            Place((RectTransform)toggleGO.transform, new Vector2(k_ControlLeft + 18f, 0f), new Vector2(36f, 36f));
            var bgImg = toggleGO.AddComponent<Image>();
            bgImg.color = k_TrackColor;

            var checkGO = NewChild("Checkmark", toggleGO.transform, stretch: false);
            Place((RectTransform)checkGO.transform, Vector2.zero, new Vector2(24f, 24f));
            var checkImg = checkGO.AddComponent<Image>();
            checkImg.color = k_HeaderColor;

            var toggle = toggleGO.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = checkImg;
            toggle.onValueChanged.AddListener(v => setVal(v));

            var hint = NewText("Hint", row.transform, hintText, 16,
                               FontStyle.Italic, TextAnchor.MiddleLeft, k_MutedColor);
            Place(hint.rectTransform, new Vector2(k_ControlLeft + 60f + 150f, 0f), new Vector2(300f, k_RowHeight));
            return toggle;
        }

        Image BuildSwatch(Transform row, float width, Func<Color> getColor, Action<Color> setColor)
        {
            var swatchGO = NewChild("Swatch", row, stretch: false);
            Place((RectTransform)swatchGO.transform, new Vector2(k_ControlLeft + width * 0.5f, 0f), new Vector2(width, 36f));
            var swatch = swatchGO.AddComponent<Image>();

            var btn = swatchGO.AddComponent<Button>();
            btn.targetGraphic = swatch;
            btn.transition    = Selectable.Transition.None; // keep the swatch its true color
            btn.onClick.AddListener(() =>
            {
                ColorWheelPopup.GetOrCreate(transform.parent).Show(getColor(), setColor);
            });
            return swatch;
        }

        Slider BuildSliderRow(string label, ref float y, float min, float max,
                              out Text valueText, Action<float> setVal)
        {
            var row = NewRow("Slider_" + label, ref y);
            BuildLabel(row.transform, label);

            const float trackW = 360f;
            var trackGO = NewChild("Slider", row.transform, stretch: false);
            Place((RectTransform)trackGO.transform, new Vector2(k_ControlLeft + trackW * 0.5f, 0f), new Vector2(trackW, 28f));
            var trackImg = trackGO.AddComponent<Image>();
            trackImg.color = k_TrackColor;

            var slider = trackGO.AddComponent<Slider>();
            slider.minValue      = min;
            slider.maxValue      = max;
            slider.targetGraphic = trackImg;
            slider.direction     = Slider.Direction.LeftToRight;

            var fillArea = NewChild("Fill Area", trackGO.transform, stretch: false);
            var faRT = (RectTransform)fillArea.transform;
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(8, 0);
            faRT.offsetMax = new Vector2(-8, 0);

            var fill = NewChild("Fill", fillArea.transform, stretch: true);
            fill.AddComponent<Image>().color = k_HeaderColor;
            slider.fillRect = (RectTransform)fill.transform;

            var handleArea = NewChild("Handle Slide Area", trackGO.transform, stretch: false);
            var haRT = (RectTransform)handleArea.transform;
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(8, 0);
            haRT.offsetMax = new Vector2(-8, 0);

            var handle = NewChild("Handle", handleArea.transform, stretch: false);
            var handleRT = (RectTransform)handle.transform;
            handleRT.sizeDelta = new Vector2(18f, 32f);
            handle.AddComponent<Image>().color = Color.white;
            slider.handleRect = handleRT;

            valueText = NewText("Value", row.transform, "", 20, FontStyle.Normal, TextAnchor.MiddleLeft, k_LabelColor);
            Place(valueText.rectTransform, new Vector2(k_ControlLeft + trackW + 16f + 45f, 0f), new Vector2(90f, k_RowHeight));

            slider.onValueChanged.AddListener(v =>
            {
                setVal(v);
                UpdateSliderReadouts();
                NotifyChanged();
            });
            return slider;
        }

        // Eight arrow buttons, one per compass direction the pattern can
        // travel toward. Arrows are drawn from Images (shaft + two head bars)
        // so they don't depend on glyph coverage in the legacy font.
        void BuildDirectionRow(ref float y)
        {
            var row = NewRow("Direction", ref y);
            BuildLabel(row.transform, "Direction");

            const float size = 44f, gap = 8f;
            directionButtons = new Image[k_Directions.Length];
            for (int i = 0; i < k_Directions.Length; i++)
            {
                float angle = k_Directions[i];
                var btnGO = NewChild("Dir_" + Mathf.RoundToInt(angle), row.transform, stretch: false);
                Place((RectTransform)btnGO.transform,
                      new Vector2(k_ControlLeft + size * 0.5f + i * (size + gap), 0f), new Vector2(size, size));
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition    = Selectable.Transition.None; // RefreshAllControls paints active/idle
                btn.onClick.AddListener(() =>
                {
                    data.patternScrollDirection = angle;
                    UpdateDirectionButtons();
                    NotifyChanged();
                });
                directionButtons[i] = img;

                var arrow = NewChild("Arrow", btnGO.transform, stretch: false);
                var art = (RectTransform)arrow.transform;
                Place(art, Vector2.zero, new Vector2(30f, 30f));
                art.localEulerAngles = new Vector3(0f, 0f, angle);
                AddBar(art, new Vector2(-2f, 0f), new Vector2(22f, 3f), 0f);
                AddBar(art, new Vector2(5f,  3.5f), new Vector2(11f, 3f), -45f);
                AddBar(art, new Vector2(5f, -3.5f), new Vector2(11f, 3f),  45f);
            }
        }

        static void AddBar(RectTransform parent, Vector2 pos, Vector2 size, float rotation)
        {
            var bar = NewChild("Bar", parent, stretch: false);
            var rt = (RectTransform)bar.transform;
            Place(rt, pos, size);
            rt.localEulerAngles = new Vector3(0f, 0f, rotation);
            var img = bar.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
        }

        void BuildLabel(Transform row, string text)
        {
            var t = NewText("Label", row, text, 20, FontStyle.Normal, TextAnchor.MiddleLeft, k_LabelColor);
            Place(t.rectTransform, new Vector2(-k_ControlsW * 0.5f + k_LabelW * 0.5f, 0f), new Vector2(k_LabelW, k_RowHeight));
        }

        static Button BuildButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            var go = NewChild(name, parent, stretch: false);
            Place((RectTransform)go.transform, pos, size);
            var img = go.AddComponent<Image>();
            img.color = k_ActiveColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var t = NewText("Label", go.transform, label, 20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(t.rectTransform);
            return btn;
        }

        // A row in the controls column of the tab being built; advances y.
        GameObject NewRow(string name, ref float y, float height = k_RowHeight)
        {
            var go = NewChild(name, rowParent, stretch: false);
            Place((RectTransform)go.transform, new Vector2(k_ControlsX, y - height * 0.5f), new Vector2(k_ControlsW, height));
            y -= height + k_RowGap;
            return go;
        }

        static Text NewText(string name, Transform parent, string text, int size, FontStyle style,
                            TextAnchor anchor, Color color)
        {
            var go = NewChild(name, parent, stretch: false);
            var t = go.AddComponent<Text>();
            t.text          = text;
            t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize      = size;
            t.fontStyle     = style;
            t.alignment     = anchor;
            t.color         = color;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static GameObject NewChild(string name, Transform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (stretch) Stretch((RectTransform)go.transform);
            return go;
        }

        static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out Color c) ? c : fallback;
        }

        static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}

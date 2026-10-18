using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Style
{
    /// <summary>
    /// The one shared look for the menus: palette, type scale, metrics, and the
    /// small helpers every runtime-built menu row uses.
    ///
    /// The scene's controls were authored three different ways over time — some
    /// baked by the UI builders (Image tint AND ColorBlock tint, which renders
    /// tint²), some hand-tweaked, some built at runtime with their own hard-coded
    /// colours and sizes. Everything now reads its colours / sizes from here.
    ///
    /// <see cref="Apply"/> normalises every Button under a root: it forces the
    /// target graphic white (on a rounded sprite) and drives the colour entirely
    /// from the ColorBlock, so every button renders its exact role colour with
    /// consistent hover / press states. Roles are inferred from the GameObject
    /// name so it works without per-button wiring.
    ///
    /// <see cref="ApplyControls"/> does the same for inputs, dropdowns, sliders
    /// and scrollbars. Text colours are deliberately NOT swept (semantic colours
    /// like the green / blue background-mode label must survive); text is styled
    /// explicitly with <see cref="Style(TMP_Text, TextRole)"/>.
    /// </summary>
    public static class UITheme
    {
        // ---- Button roles ---------------------------------------------------
        // Primary / utility actions (Browse, Load, cycle ‹ ›, Manage, Export…).
        public static readonly Color Accent      = new Color(0.30f, 0.46f, 0.78f, 1f);
        // Affirmative actions (Start, Save, Add, Confirm).
        public static readonly Color Positive    = new Color(0.22f, 0.60f, 0.42f, 1f);
        // Destructive actions (Quit, Delete, Remove).
        public static readonly Color Destructive = new Color(0.72f, 0.30f, 0.32f, 1f);
        // Dismissive / toggle actions (Close, Cancel, Clear, font-style toggles).
        public static readonly Color Neutral     = new Color(0.22f, 0.24f, 0.29f, 1f);
        // The chosen option of a toggle group (font style, selected list row).
        public static readonly Color Selected    = new Color(0.24f, 0.50f, 0.86f, 1f);

        // ---- Surfaces (darkest → lightest) -----------------------------------
        // The menu screen's own background (baked into MainMenu's Background).
        public static readonly Color Screen     = new Color(0.055f, 0.059f, 0.078f, 1f);
        public static readonly Color Backdrop   = new Color(0f, 0f, 0f, 0.65f);
        public static readonly Color Window     = new Color(0.10f, 0.11f, 0.135f, 0.98f);
        public static readonly Color Card       = new Color(0.125f, 0.135f, 0.16f, 1f);
        public static readonly Color Field      = new Color(0.165f, 0.18f, 0.215f, 1f);
        public static readonly Color FieldError = new Color(0.38f, 0.13f, 0.15f, 1f);
        public static readonly Color Track      = new Color(0.20f, 0.22f, 0.26f, 1f);
        public static readonly Color Divider    = new Color(1f, 1f, 1f, 0.08f);

        // ---- Text ----------------------------------------------------------------
        public static readonly Color TextStrong = new Color(0.95f, 0.97f, 1f, 1f);
        public static readonly Color Text       = new Color(0.85f, 0.88f, 0.93f, 1f);
        public static readonly Color TextMuted  = new Color(0.55f, 0.58f, 0.64f, 1f);
        public static readonly Color Heading    = new Color(0.55f, 0.78f, 1f, 1f);

        // ---- Status --------------------------------------------------------------
        public static readonly Color Success = new Color(0.35f, 0.85f, 0.45f, 1f);
        public static readonly Color Warning = new Color(0.98f, 0.80f, 0.30f, 1f);
        public static readonly Color Error   = new Color(0.95f, 0.35f, 0.35f, 1f);

        // ---- Type scale (reference px on the 1920×1080 canvas) -------------------
        public const int ScreenTitleSize = 44;  // the main menu's app title
        public const int TitleSize       = 32;  // window / popup titles
        public const int HeadingSize     = 20;  // section headings
        public const int BodySize        = 20;  // labels, inputs, values, buttons
        public const int SmallSize       = 16;  // hints, paths, secondary info

        // ---- Metrics ---------------------------------------------------------------
        public const float ControlHeight = 44f;
        public const float Gap           = 12f;  // between controls in a row / rows in a card
        public const float SectionGap    = 20f;  // between cards / sections
        public const float CardPadding   = 24f;
        public const float ColumnGap     = 24f;  // between side-by-side cards
        // Page margins of the full-screen menus (main menu, Visuals editor).
        public const float ScreenMarginX = 56f;
        public const float ScreenMarginY = 40f;
        public const int   ControlRadius = 8;
        public const int   PanelRadius   = 14;

        public enum TextRole
        {
            Title,    // window title — big, bold, bright
            Heading,  // section heading — bold, accent blue
            Label,    // row label — regular body text
            Value,    // the current value of a picker — bold, bright
            Muted,    // body-size secondary text
            Small,    // hints, paths
        }

        // =====================================================================
        // Sweeps
        // =====================================================================

        public static void Apply(GameObject root)
        {
            if (root == null) return;
            var buttons = root.GetComponentsInChildren<Button>(includeInactive: true);
            for (int i = 0; i < buttons.Length; i++) Skin(buttons[i]);
        }

        static void Skin(Button btn)
        {
            if (btn == null) return;
            string n = btn.gameObject.name.ToLowerInvariant();
            // Colour swatches are buttons whose graphic IS the value — never tint them.
            if (n.Contains("swatch")) return;

            if (btn.targetGraphic is Image img) Round(img, ControlRadius);
            SetRole(btn, btn.TryGetComponent(out UIRole pinned) ? ColorOf(pinned.role) : RoleFor(n));
        }

        /// <summary>Pins (and applies) a role that the name-based sweep won't override.</summary>
        public static void Pin(Button btn, UIRole.Role role)
        {
            if (btn == null) return;
            if (!btn.TryGetComponent(out UIRole pinned)) pinned = btn.gameObject.AddComponent<UIRole>();
            pinned.role = role;
            SetRole(btn, ColorOf(role));
        }

        static Color ColorOf(UIRole.Role role)
        {
            switch (role)
            {
                case UIRole.Role.Positive:    return Positive;
                case UIRole.Role.Destructive: return Destructive;
                case UIRole.Role.Neutral:     return Neutral;
                default:                      return Accent;
            }
        }

        /// <summary>Paints a button in a role colour (white graphic, ColorBlock tint).</summary>
        public static void SetRole(Button btn, Color role)
        {
            if (btn == null) return;
            // Render exactly the role colour: ColorBlock tints a white graphic.
            if (btn.targetGraphic != null) btn.targetGraphic.color = Color.white;

            ColorBlock cb = btn.colors;
            cb.normalColor      = role;
            cb.highlightedColor = Lighten(role, 0.07f);
            cb.pressedColor     = Darken(role, 0.12f);
            cb.selectedColor    = Lighten(role, 0.07f);
            cb.disabledColor    = new Color(role.r * 0.5f, role.g * 0.5f, role.b * 0.5f, 0.5f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.1f;
            btn.colors = cb;
        }

        /// <summary>
        /// Toggle-group state: the chosen option shows <see cref="Selected"/>,
        /// the rest <paramref name="off"/> (default <see cref="Neutral"/>).
        /// </summary>
        public static void SetSelected(Button btn, bool selected, Color? off = null)
            => SetRole(btn, selected ? Selected : (off ?? Neutral));

        static Color RoleFor(string n)
        {
            if (n.Contains("quit") || n.Contains("delete") || n.Contains("remove"))
                return Destructive;

            if (n.Contains("close") || n.Contains("cancel") || n.Contains("clear") || n.Contains("back")
                || n == "normal" || n == "bold" || n == "italic" || n == "bolditalic")
                return Neutral;

            // StartsWith (not Contains) so "ManageSaves" doesn't read as a Save.
            if (n.StartsWith("start") || n.StartsWith("save") || n.StartsWith("add")
                || n.StartsWith("confirm") || n.StartsWith("apply"))
                return Positive;

            return Accent;
        }

        /// <summary>
        /// Skins inputs (TMP + legacy), TMP dropdowns, sliders and scrollbars
        /// under <paramref name="root"/>. Text inside them is set to the body size.
        /// </summary>
        public static void ApplyControls(GameObject root)
        {
            if (root == null) return;
            foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true)) StyleInput(f);
            foreach (var f in root.GetComponentsInChildren<InputField>(true))     StyleInput(f);
            foreach (var d in root.GetComponentsInChildren<TMP_Dropdown>(true))   StyleDropdown(d);
            foreach (var s in root.GetComponentsInChildren<Slider>(true))         StyleSlider(s);
            foreach (var s in root.GetComponentsInChildren<Scrollbar>(true))      StyleScrollbar(s);
        }

        static readonly ColorBlock FieldColors = new ColorBlock
        {
            normalColor      = Color.white,
            highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f),
            pressedColor     = new Color(0.92f, 0.92f, 0.92f, 1f),
            selectedColor    = new Color(1.08f, 1.08f, 1.08f, 1f),
            disabledColor    = new Color(1f, 1f, 1f, 0.5f),
            colorMultiplier  = 1f,
            fadeDuration     = 0.1f,
        };

        public static void StyleInput(TMP_InputField f)
        {
            if (f == null) return;
            if (f.targetGraphic is Image img) Surface(img, Field, ControlRadius);
            f.colors = FieldColors;
            f.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
            f.caretColor = TextStrong;
            if (f.lineType == TMP_InputField.LineType.SingleLine) f.pointSize = BodySize;
            if (f.textComponent != null) f.textComponent.color = TextStrong;
            if (f.placeholder is TMP_Text ph)
            {
                ph.color = TextMuted;
                ph.fontStyle = FontStyles.Italic;
            }
        }

        public static void StyleInput(InputField f)
        {
            if (f == null) return;
            if (f.targetGraphic is Image img) Surface(img, Field, ControlRadius);
            f.colors = FieldColors;
            f.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
            f.caretColor = TextStrong;
            if (f.textComponent != null)
            {
                f.textComponent.font     = LegacyFont;
                f.textComponent.fontSize = BodySize;
                f.textComponent.color    = TextStrong;
            }
            if (f.placeholder is Text ph)
            {
                ph.font      = LegacyFont;
                ph.fontSize  = BodySize;
                ph.color     = TextMuted;
                ph.fontStyle = FontStyle.Italic;
            }
        }

        public static void StyleDropdown(TMP_Dropdown d)
        {
            if (d == null) return;
            if (d.targetGraphic is Image img) Surface(img, Field, ControlRadius);
            d.colors = FieldColors;
            if (d.captionText != null)
            {
                d.captionText.fontSize = BodySize;
                d.captionText.color    = TextStrong;
            }
            var arrow = d.transform.Find("Arrow");
            if (arrow != null && arrow.TryGetComponent(out Image arrowImg)) arrowImg.color = Text;

            if (d.template != null)
            {
                if (d.template.TryGetComponent(out Image listBg)) Surface(listBg, Card, ControlRadius);
                if (d.itemText != null)
                {
                    d.itemText.fontSize = BodySize;
                    d.itemText.color    = TextStrong;
                }
                var item = d.template.GetComponentInChildren<Toggle>(true);
                if (item != null)
                {
                    if (item.targetGraphic != null) item.targetGraphic.color = Color.white;
                    ColorBlock cb = item.colors;
                    cb.normalColor      = Card;
                    cb.highlightedColor = Field;
                    cb.selectedColor    = Field;
                    cb.pressedColor     = Track;
                    cb.colorMultiplier  = 1f;
                    item.colors = cb;
                    // The default item is 20 px — too cramped for body text.
                    var itemRT = (RectTransform)item.transform;
                    itemRT.sizeDelta = new Vector2(itemRT.sizeDelta.x, 36f);
                    if (item.graphic != null) item.graphic.color = Heading;
                }
                // Match the content height to the taller item.
                if (d.template.TryGetComponent(out ScrollRect sr) && sr.content != null)
                    sr.content.sizeDelta = new Vector2(sr.content.sizeDelta.x, 36f);
            }
        }

        public static void StyleSlider(Slider s)
        {
            if (s == null) return;
            // Track: the slider's own Image, or a "Background" child (default control).
            Image track = s.GetComponent<Image>();
            if (track == null)
            {
                var bg = s.transform.Find("Background");
                if (bg != null) track = bg.GetComponent<Image>();
            }
            if (track != null) Surface(track, Track, 6);
            if (s.fillRect != null && s.fillRect.TryGetComponent(out Image fill)) Surface(fill, Heading, 6);
            if (s.handleRect != null && s.handleRect.TryGetComponent(out Image handle))
            {
                Surface(handle, TextStrong, 6);
                s.colors = FieldColors;
            }
        }

        public static void StyleScrollbar(Scrollbar s)
        {
            if (s == null) return;
            if (s.TryGetComponent(out Image track)) Surface(track, new Color(1f, 1f, 1f, 0.04f), 6);
            if (s.handleRect != null && s.handleRect.TryGetComponent(out Image handle)) Surface(handle, Track, 6);
            s.colors = FieldColors;
        }

        // =====================================================================
        // Surfaces & text
        // =====================================================================

        /// <summary>Paints an Image as a rounded surface of the given colour.</summary>
        public static void Surface(Image img, Color color, int radius = ControlRadius)
        {
            if (img == null) return;
            Round(img, radius);
            img.color = color;
        }

        /// <summary>
        /// Swaps in the shared 9-sliced rounded sprite, keeping the colour.
        /// Leaves real artwork (a simple sprite, i.e. an icon or a picture) alone.
        /// </summary>
        public static void Round(Image img, int radius)
        {
            if (img.sprite != null && img.type != Image.Type.Sliced && !IsStockSprite(img.sprite)) return;
            // The Nobi rounded-corners shader would double-round over our sprite.
            var shaderCorners = img.GetComponent<Nobi.UiRoundedCorners.ImageWithRoundedCorners>();
            if (shaderCorners != null) Object.Destroy(shaderCorners);
            img.sprite = StyleSpriteFactory.GetRoundedRect(radius);
            img.type   = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>
        /// A flat, edge-to-edge fill (full-screen backdrops): no sprite, so no
        /// soft stretched corners for the screen behind to show through.
        /// </summary>
        public static void Flat(Image img, Color color)
        {
            if (img == null) return;
            img.sprite = null;
            img.type   = Image.Type.Simple;
            img.color  = color;
        }

        // Unity's stock UI sprites. The UI builders left them on Simple, which
        // stretches their tiny anti-aliased corners into blurry pills — they are
        // placeholders, never artwork, so they always get the rounded sprite.
        static bool IsStockSprite(Sprite s)
        {
            switch (s.name)
            {
                case "UISprite":
                case "Background":
                case "InputFieldBackground":
                case "Knob":
                case "UIMask":
                    return true;
                default:
                    return s.name.StartsWith("RoundedRect_"); // ours, if left on Simple
            }
        }

        public static void Style(TMP_Text t, TextRole role)
        {
            if (t == null) return;
            t.enableAutoSizing = false;
            switch (role)
            {
                case TextRole.Title:   Set(t, TitleSize,   FontStyles.Bold,   TextStrong); break;
                case TextRole.Heading: Set(t, HeadingSize, FontStyles.Bold,   Heading);    break;
                case TextRole.Value:   Set(t, BodySize,    FontStyles.Bold,   TextStrong); break;
                case TextRole.Muted:   Set(t, BodySize,    FontStyles.Normal, TextMuted);  break;
                case TextRole.Small:   Set(t, SmallSize,   FontStyles.Normal, TextMuted);  break;
                default:               Set(t, BodySize,    FontStyles.Normal, Text);       break;
            }
        }

        static void Set(TMP_Text t, int size, FontStyles style, Color color)
        {
            t.fontSize = size; t.fontStyle = style; t.color = color;
        }

        static Font s_LegacyFont;

        /// <summary>
        /// The font for legacy uGUI Text: the same Liberation Sans file TMP's
        /// default font asset is generated from (via its dynamic fallback), so
        /// Text and TMP labels match. Unity's LegacyRuntime.ttf maps to a
        /// different system face on Linux; it is only the last resort.
        /// </summary>
        public static Font LegacyFont
        {
            get
            {
                if (s_LegacyFont != null) return s_LegacyFont;
                TMP_FontAsset tmp = TMP_Settings.defaultFontAsset;
                if (tmp != null)
                {
                    s_LegacyFont = tmp.sourceFontFile;
                    if (s_LegacyFont == null && tmp.fallbackFontAssetTable != null)
                        foreach (TMP_FontAsset fallback in tmp.fallbackFontAssetTable)
                            if (fallback != null && fallback.sourceFontFile != null) { s_LegacyFont = fallback.sourceFontFile; break; }
                }
                if (s_LegacyFont == null) s_LegacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_LegacyFont;
            }
        }

        public static void Style(Text t, TextRole role)
        {
            if (t == null) return;
            t.font = LegacyFont;
            t.resizeTextForBestFit = false;
            switch (role)
            {
                case TextRole.Title:   Set(t, TitleSize,   FontStyle.Bold,   TextStrong); break;
                case TextRole.Heading: Set(t, HeadingSize, FontStyle.Bold,   Heading);    break;
                case TextRole.Value:   Set(t, BodySize,    FontStyle.Bold,   TextStrong); break;
                case TextRole.Muted:   Set(t, BodySize,    FontStyle.Normal, TextMuted);  break;
                case TextRole.Small:   Set(t, SmallSize,   FontStyle.Normal, TextMuted);  break;
                default:               Set(t, BodySize,    FontStyle.Normal, Text);       break;
            }
        }

        static void Set(Text t, int size, FontStyle style, Color color)
        {
            t.fontSize = size; t.fontStyle = style; t.color = color;
        }

        /// <summary>Sets a button's label (TMP or legacy) to the body size, bold, white.</summary>
        public static void StyleButtonLabel(Button btn, int size = BodySize)
        {
            if (btn == null) return;
            var tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.enableAutoSizing = false; Set(tmp, size, FontStyles.Bold, TextStrong); return; }
            var legacy = btn.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.font = LegacyFont;
                legacy.resizeTextForBestFit = false;
                Set(legacy, size, FontStyle.Bold, TextStrong);
            }
        }

        // =====================================================================
        // Runtime layout helpers (TMP). Rows are HorizontalLayoutGroups, cards
        // and columns are VerticalLayoutGroups; children size themselves through
        // LayoutElement (see Size).
        // =====================================================================

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Stretches <paramref name="rt"/> over its parent with the given inset.</summary>
        public static RectTransform Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static RectTransform MakeColumn(Transform parent, string name, float spacing = Gap)
        {
            var rt = NewRect(name, parent);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>A rounded panel section: Card background + padded column.</summary>
        public static RectTransform MakeCard(Transform parent, string name, float spacing = Gap)
        {
            var rt = MakeColumn(parent, name, spacing);
            int p = Mathf.RoundToInt(CardPadding);
            rt.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(p, p, p - 4, p);
            Surface(rt.gameObject.AddComponent<Image>(), Card, PanelRadius);
            return rt;
        }

        public static RectTransform MakeRow(Transform parent, string name,
                                            float height = ControlHeight, float spacing = Gap)
        {
            var rt = NewRect(name, parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Size(rt, height: height);
            return rt;
        }

        /// <summary>Flexible empty space inside a row or column.</summary>
        public static RectTransform MakeSpacer(Transform parent)
        {
            var rt = NewRect("Spacer", parent);
            Size(rt, flexWidth: 1f, flexHeight: 1f);
            return rt;
        }

        public static TMP_Text MakeText(Transform parent, string name, string text, TextRole role)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            Style(t, role);
            return t;
        }

        public static Button MakeButton(Transform parent, string name, string label, int fontSize = BodySize)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            Round(img, ControlRadius);

            var t = MakeText(rt, "Label", label, TextRole.Value);
            t.fontSize  = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            Fill((RectTransform)t.transform);

            SetRole(btn, RoleFor(name.ToLowerInvariant()));
            return btn;
        }

        public static TMP_InputField MakeInput(Transform parent, string name, string placeholder)
        {
            var rt = NewRect(name, parent);
            var bg = rt.gameObject.AddComponent<Image>();
            var input = rt.gameObject.AddComponent<TMP_InputField>();

            var area = NewRect("Text Area", rt);
            Fill(area);
            area.offsetMin = new Vector2(14f, 4f);
            area.offsetMax = new Vector2(-14f, -4f);
            area.gameObject.AddComponent<RectMask2D>();

            var text = MakeText(area, "Text", "", TextRole.Label);
            Fill((RectTransform)text.transform);
            text.richText = false;
            text.raycastTarget = true;

            var ph = MakeText(area, "Placeholder", placeholder, TextRole.Muted);
            Fill((RectTransform)ph.transform);

            input.textViewport  = area;
            input.textComponent = text;
            input.placeholder   = ph;
            input.targetGraphic = bg;
            StyleInput(input);
            return input;
        }

        /// <summary>
        /// Adds / updates the LayoutElement on <paramref name="c"/>. Negative
        /// values leave that dimension to the layout group.
        /// </summary>
        public static LayoutElement Size(Component c, float width = -1f, float height = -1f,
                                         float flexWidth = -1f, float flexHeight = -1f)
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            if (width  >= 0f) { le.preferredWidth  = width;  le.minWidth  = width; }
            if (height >= 0f) { le.preferredHeight = height; le.minHeight = height; }
            if (flexWidth  >= 0f) le.flexibleWidth  = flexWidth;
            if (flexHeight >= 0f) le.flexibleHeight = flexHeight;
            return le;
        }

        /// <summary>
        /// Moves an existing (scene-authored) control into a layout group,
        /// clearing the hand-placed anchors / offsets so the group owns it.
        /// </summary>
        public static void Adopt(Component c, Transform parent)
        {
            if (c == null || parent == null) return;
            var rt = (RectTransform)c.transform;
            rt.SetParent(parent, false);
            rt.SetAsLastSibling(); // layout order = adoption order, even within the same parent
            rt.localScale    = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        static Color Lighten(Color c, float d)
            => new Color(Mathf.Min(1f, c.r + d), Mathf.Min(1f, c.g + d), Mathf.Min(1f, c.b + d), c.a);

        static Color Darken(Color c, float d)
            => new Color(Mathf.Max(0f, c.r - d), Mathf.Max(0f, c.g - d), Mathf.Max(0f, c.b - d), c.a);
    }
}

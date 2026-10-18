using UnityEngine;
using UnityEngine.UI;
using MugsTech.Style;

/// <summary>
/// Visuals editor layout. Run once from Awake: the authored controls (built by
/// VisualsMenuUIBuilder, then hand-tweaked) and the runtime-built rows are
/// re-parented into the same page structure as the main menu — opaque screen
/// background, same margins, title where the app title sits, cards side by
/// side, and an action bar card at the bottom:
///
///   Customize Visuals  Editing: &lt;save&gt;                             [Close]
///   ┌ Presenter character ──────────┐  ┌ Cards ──────────────────────────┐
///   │ emotion rows (scroll)         │  │ Background / Text colour        │
///   │                               │  │ Corner radius, Font, Font style │
///   │ [+ Add Emotion]               │  │ live preview                    │
///   └───────────────────────────────┘  │ Full-screen  [Big Headline][Big Text]
///                                      └─────────────────────────────────┘
///   ┌ [Manage Saves…]            toast             [Save As…]  [Save] ┐
///
/// The Manage Saves / Save As / Overwrite dialogs get the same header / body /
/// footer treatment. Nothing is re-baked into the scene.
/// </summary>
public partial class VisualsMenuController
{
    const float FormLabelWidth = 190f;

    void ArrangeLayout()
    {
        if (panelRoot == null) return;
        var panel = (RectTransform)panelRoot.transform;

        // An opaque page of its own: the main menu must not show through
        // (the authored backdrop was a stretched stock sprite with soft,
        // see-through corners).
        if (panel.parent.Find("Backdrop") is Transform backdrop && backdrop.TryGetComponent(out Image screen))
            UITheme.Flat(screen, UITheme.Screen);

        // Same page margins as the main menu; the cards are the surfaces, so
        // the panel itself draws nothing.
        UITheme.Fill(panel);
        panel.offsetMin = new Vector2(UITheme.ScreenMarginX, UITheme.ScreenMarginY);
        panel.offsetMax = new Vector2(-UITheme.ScreenMarginX, -UITheme.ScreenMarginY);
        if (panel.TryGetComponent(out Image panelBg)) panelBg.enabled = false;

        var root = UITheme.Fill(UITheme.MakeColumn(panel, "Layout", UITheme.SectionGap));
        root.SetAsFirstSibling(); // the style popups open later, on top of it

        // ---- Header (mirrors the main menu's top bar) -------------------------
        var header = UITheme.MakeRow(root, "Header", height: 64f, spacing: 20f);
        Text title = TakeText(PanelChild<Text>("Title"), header, UITheme.TextRole.Title);
        if (title != null) title.fontSize = UITheme.ScreenTitleSize;
        // Its colour is set by UpdateActiveSaveLabel (muted when Untitled).
        Text editing = TakeText(activeSaveLabel, header, UITheme.TextRole.Muted);
        if (editing != null) UITheme.Size(editing, width: 0f, flexWidth: 1f);
        HeaderButton(closeButton, header, "Close");

        // ---- Two equal cards ---------------------------------------------------
        var body = UITheme.MakeRow(root, "Body", height: -1f, spacing: UITheme.ColumnGap);
        UITheme.Size(body, flexHeight: 1f);
        var bodyRow = body.GetComponent<HorizontalLayoutGroup>();
        bodyRow.childAlignment         = TextAnchor.UpperLeft;
        bodyRow.childForceExpandHeight = true;

        var presenter = UITheme.MakeCard(body, "PresenterCard");
        var cards     = UITheme.MakeCard(body, "CardStyleCard");
        UITheme.Size(presenter, width: 0f, flexWidth: 1f);
        UITheme.Size(cards,     width: 0f, flexWidth: 1f);

        ArrangePresenterCard(presenter);
        ArrangeCardStyleCard(cards);

        // ---- Action bar (a card, like the main menu's record bar) ---------------
        var footer = UITheme.MakeRow(root, "Footer", height: 84f);
        footer.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(20, 20, 20, 20);
        UITheme.Surface(footer.gameObject.AddComponent<Image>(), UITheme.Card, UITheme.PanelRadius);
        FooterButton(manageSavesButton, footer, 220f);
        // Toast lives in a flexible slot so the buttons don't jump when it hides.
        var toastSlot = UITheme.NewRect("ToastSlot", footer);
        UITheme.Size(toastSlot, width: 0f, height: UITheme.ControlHeight, flexWidth: 1f);
        if (toastText != null)
        {
            UITheme.Adopt(toastText, toastSlot);
            UITheme.Fill(toastText.rectTransform);
            UITheme.Style(toastText, UITheme.TextRole.Label);
            toastText.alignment = TextAnchor.MiddleCenter;
        }
        FooterButton(saveAsButton, footer, 180f);
        FooterButton(saveButton,   footer, 160f);

        ArrangeDialogs();

        UITheme.Apply(gameObject);
        UITheme.ApplyControls(gameObject);
        // Any authored label not restyled above still gets the shared font.
        foreach (Text t in GetComponentsInChildren<Text>(true)) t.font = UITheme.LegacyFont;
    }

    void ArrangePresenterCard(Transform card)
    {
        TakeText(PanelChild<Text>("PresenterTitle"), card, UITheme.TextRole.Heading, "Presenter character");
        Text hint = TakeText(PanelChild<Text>("PresenterHint"), card, UITheme.TextRole.Small,
            "One image per emotion — use its name in the script as {Name}. PNG, JPG, BMP or TGA.");
        if (hint != null) hint.horizontalOverflow = HorizontalWrapMode.Wrap;

        var scroll = PanelChild<ScrollRect>("EmotionsScrollView");
        if (scroll == null) return;
        UITheme.Adopt(scroll, card);
        UITheme.Size(scroll, flexHeight: 1f);
        // A slightly darker well, so the rows read as one list.
        if (scroll.TryGetComponent(out Image well)) UITheme.Surface(well, UITheme.Window, UITheme.ControlRadius);
        if (addEmotionButton != null) UITheme.StyleButtonLabel(addEmotionButton);
    }

    void ArrangeCardStyleCard(Transform card)
    {
        TakeText(PanelChild<Text>("CardTitle"), card, UITheme.TextRole.Heading, "Cards");

        var bg = FormRow("BgColorRow", card, "Background colour");
        FixWidth(bg, "Hex", 200f);
        FixWidth(bg, "Swatch", 64f);

        var text = FormRow("TextColorRow", card, "Text colour");
        FixWidth(text, "Hex", 200f);
        FixWidth(text, "Swatch", 64f);

        var corner = FormRow("CornerRadiusRow", card, "Corner radius");
        if (cornerRadiusSlider != null) UITheme.Size(cornerRadiusSlider, width: 0f, height: 24f, flexWidth: 1f);
        if (cornerRadiusLabel != null)
        {
            UITheme.Style(cornerRadiusLabel, UITheme.TextRole.Value);
            cornerRadiusLabel.alignment = TextAnchor.MiddleRight;
            UITheme.Size(cornerRadiusLabel, width: 80f);
        }

        if (fontRow != null) UITheme.Adopt(fontRow, card);

        var style = FormRow("FontStyleRow", card, "Font style");
        FixWidth(style, "Normal", 110f);
        FixWidth(style, "Bold", 110f);
        FixWidth(style, "Italic", 110f);
        FixWidth(style, "BoldItalic", 150f);

        TakeText(PanelChild<Text>("PreviewLabel"), card, UITheme.TextRole.Small, "Preview");
        if (cardPreviewBackground != null)
        {
            UITheme.Adopt(cardPreviewBackground, card);
            UITheme.Size(cardPreviewBackground, height: 160f, flexHeight: 1f);
        }

        // The full-screen card styles have their own popups.
        var fullScreen = UITheme.MakeRow(card, "FullScreenRow");
        UITheme.Size(LegacyText(fullScreen, "Label", "Full-screen", UITheme.TextRole.Label), width: FormLabelWidth);
        foreach (Button b in new[] { bigHeadlineEditButton, bigTextEditButton })
        {
            if (b == null) continue;
            UITheme.Adopt(b, fullScreen);
            UITheme.Size(b, 230f, UITheme.ControlHeight);
            UITheme.StyleButtonLabel(b);
        }
    }

    // Manage Saves / Save As / Overwrite: same window look as everything else.
    void ArrangeDialogs()
    {
        RectTransform frame = DialogFrame(savesPanelRoot, hugHeight: false);
        if (frame != null)
        {
            var header = UITheme.MakeRow(frame, "Header", height: 48f);
            Text title = TakeText(Child<Text>(frame, "Title"), header, UITheme.TextRole.Title);
            if (title != null) UITheme.Size(title, width: 0f, flexWidth: 1f);
            HeaderButton(savesPanelCloseButton, header, "Close");

            var tools = UITheme.MakeRow(frame, "Tools");
            FooterButton(importButton, tools, 160f);
            FooterButton(exportSelectedButton, tools, 220f);

            var list = Child<ScrollRect>(frame, "SavesScrollView");
            if (list != null)
            {
                UITheme.Adopt(list, frame);
                UITheme.Size(list, flexHeight: 1f);
                if (list.TryGetComponent(out Image well)) UITheme.Surface(well, UITheme.Card, UITheme.ControlRadius);
            }
            if (savesEmptyLabel != null)
            {
                // Floats over the empty list instead of taking a row.
                UITheme.Size(savesEmptyLabel).ignoreLayout = true;
                UITheme.Style(savesEmptyLabel, UITheme.TextRole.Muted);
            }

            var footer = UITheme.MakeRow(frame, "Footer");
            FooterButton(deleteSelectedButton, footer, 200f);
            UITheme.MakeSpacer(footer);
            FooterButton(loadSelectedButton, footer, 200f);

            // Drawn after the list's background, or the well would hide it.
            if (savesEmptyLabel != null) savesEmptyLabel.transform.SetAsLastSibling();
        }

        frame = DialogFrame(saveAsPromptRoot, hugHeight: true);
        if (frame != null)
        {
            TakeText(Child<Text>(frame, "Title"), frame, UITheme.TextRole.Title);
            var row = UITheme.MakeRow(frame, "NameRow");
            Text label = TakeText(Child<Text>(frame, "Label"), row, UITheme.TextRole.Label);
            if (label != null) UITheme.Size(label, width: 90f);
            if (saveAsNameInput != null)
            {
                UITheme.Adopt(saveAsNameInput, row);
                UITheme.Size(saveAsNameInput, width: 0f, height: UITheme.ControlHeight, flexWidth: 1f);
            }
            DialogFooter(frame, saveAsCancelButton, saveAsConfirmButton);
        }

        frame = DialogFrame(overwriteConfirmRoot, hugHeight: true);
        if (frame != null)
        {
            TakeText(Child<Text>(frame, "Title"), frame, UITheme.TextRole.Title);
            Text message = TakeText(overwriteConfirmMessage, frame, UITheme.TextRole.Label);
            if (message != null) UITheme.Size(message, height: 60f);
            DialogFooter(frame, overwriteCancelButton, overwriteConfirmButton);
        }
    }

    // -----------------------------------------------------------------------
    // Emotion rows — a thumbnail with two lines beside it: the name on top,
    // the path field + Load + X underneath. Anchored to the row's edges so it
    // follows the list width. Runs for authored and runtime-built rows alike.
    // -----------------------------------------------------------------------

    void LayoutEmotionRow(EmotionSlot slot)
    {
        if (slot?.preview == null) return;
        var row = (RectTransform)slot.preview.transform.parent;

        Pin(slot.preview.rectTransform, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(80f, 80f));

        // Neutral has a fixed caption instead of an editable name.
        Text caption = slot.nameInput == null ? Child<Text>(row, "Label") : null;
        RectTransform name = slot.nameInput != null ? (RectTransform)slot.nameInput.transform : caption?.rectTransform;
        if (name != null) Pin(name, new Vector2(0f, 1f), new Vector2(104f, -8f), new Vector2(260f, 36f));
        if (caption != null)
        {
            UITheme.Style(caption, UITheme.TextRole.Value);
            caption.alignment = TextAnchor.MiddleLeft;
        }

        if (slot.removeButton != null)
            Pin((RectTransform)slot.removeButton.transform, new Vector2(1f, 0f), new Vector2(-8f, 8f), new Vector2(44f, 44f));
        if (slot.loadButton != null)
            Pin((RectTransform)slot.loadButton.transform, new Vector2(1f, 0f), new Vector2(-60f, 8f), new Vector2(100f, 44f));

        if (slot.pathInput != null)
        {
            var path = (RectTransform)slot.pathInput.transform;
            path.anchorMin = new Vector2(0f, 0f);
            path.anchorMax = new Vector2(1f, 0f);
            path.pivot     = new Vector2(0f, 0f);
            path.offsetMin = new Vector2(104f, 8f);
            path.offsetMax = new Vector2(-168f, 52f); // clear of Load (100) + X (44) + gaps
        }

        UITheme.Apply(row.gameObject);
        UITheme.ApplyControls(row.gameObject);
        UITheme.StyleButtonLabel(slot.loadButton);
        UITheme.StyleButtonLabel(slot.removeButton);
    }

    // Anchors + pivots a rect at one point of its parent (corner or edge middle).
    static void Pin(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta        = size;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    T PanelChild<T>(string name) where T : Component => Child<T>(panelRoot.transform, name);

    static T Child<T>(Transform parent, string name) where T : Component
    {
        if (parent == null) return null;
        Transform t = parent.Find(name);
        return t != null ? t.GetComponent<T>() : null;
    }

    // Moves an authored label into a layout column, restyles it and
    // (optionally) rewrites its text.
    static Text TakeText(Text t, Transform into, UITheme.TextRole role, string text = null)
    {
        if (t == null) return null;
        UITheme.Adopt(t, into);
        UITheme.Style(t, role);
        t.alignment = TextAnchor.MiddleLeft;
        if (text != null) t.text = text;
        return t;
    }

    // Turns an authored "Label + controls" row into a layout row: label in a
    // fixed column, controls left to right after it.
    RectTransform FormRow(string rowName, Transform card, string label)
    {
        var row = PanelChild<RectTransform>(rowName);
        if (row == null) return null;
        UITheme.Adopt(row, card);
        UITheme.Size(row, height: UITheme.ControlHeight);

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UITheme.Gap;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        Text l = Child<Text>(row, "Label");
        if (l != null)
        {
            UITheme.Style(l, UITheme.TextRole.Label);
            l.text = label;
            l.alignment = TextAnchor.MiddleLeft;
            UITheme.Size(l, width: FormLabelWidth);
        }
        return row;
    }

    static void FixWidth(Transform row, string child, float width)
    {
        if (row == null) return;
        Transform t = row.Find(child);
        if (t == null) return;
        UITheme.Size(t, width, UITheme.ControlHeight);
        if (t.TryGetComponent(out Button b)) UITheme.StyleButtonLabel(b);
        // Colour swatches show a value: round them but keep their colour.
        if (child == "Swatch" && t.TryGetComponent(out Image swatch)) UITheme.Round(swatch, UITheme.ControlRadius);
    }

    static void HeaderButton(Button b, Transform header, string label)
    {
        if (b == null) return;
        UITheme.Adopt(b, header);
        UITheme.Size(b, 120f, UITheme.ControlHeight);
        Text t = b.GetComponentInChildren<Text>(true);
        if (t != null) t.text = label;
        UITheme.StyleButtonLabel(b);
    }

    static void FooterButton(Button b, Transform row, float width)
    {
        if (b == null) return;
        UITheme.Adopt(b, row);
        UITheme.Size(b, width, UITheme.ControlHeight);
        UITheme.StyleButtonLabel(b);
    }

    static void DialogFooter(Transform frame, Button cancel, Button confirm)
    {
        var footer = UITheme.MakeRow(frame, "Footer");
        UITheme.MakeSpacer(footer);
        FooterButton(cancel,  footer, 140f);
        FooterButton(confirm, footer, 140f);
    }

    // A dialog's "Frame" as a padded, rounded column. hugHeight shrinks the
    // frame to its content (short prompts); otherwise it keeps its height.
    static RectTransform DialogFrame(GameObject dialogRoot, bool hugHeight)
    {
        if (dialogRoot == null) return null;
        if (dialogRoot.transform.Find("Backdrop") is Transform backdrop && backdrop.TryGetComponent(out Image dim))
            UITheme.Flat(dim, UITheme.Backdrop);

        var frame = dialogRoot.transform.Find("Frame") as RectTransform;
        if (frame == null) return null;
        if (frame.TryGetComponent(out Image img)) UITheme.Surface(img, UITheme.Window, UITheme.PanelRadius);

        var v = frame.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(28, 28, 20, 24);
        v.spacing = UITheme.Gap + 4f;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        if (hugHeight)
            frame.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return frame;
    }

    static Text LegacyText(Transform parent, string name, string value, UITheme.TextRole role)
    {
        var rt = UITheme.NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UITheme.LegacyFont;
        t.text = value;
        t.alignment = TextAnchor.MiddleLeft;
        t.raycastTarget = false;
        UITheme.Style(t, role);
        return t;
    }
}

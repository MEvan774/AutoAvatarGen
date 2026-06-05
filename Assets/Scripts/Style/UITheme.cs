using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Style
{
    /// <summary>
    /// Runtime button skinning for the menus.
    ///
    /// The scene's buttons were authored three different ways over time — some
    /// baked by the UI builders (Image tint AND ColorBlock tint, which renders
    /// tint²), some hand-tweaked (white Image + ColorBlock tint), some built at
    /// runtime (Image tint + default white ColorBlock). The result is an
    /// inconsistent, slightly muddy button look.
    ///
    /// <see cref="Apply"/> normalises every Button under a root to a single,
    /// restrained palette: it forces the target graphic white and drives the
    /// colour entirely from the ColorBlock, so every button renders its exact
    /// role colour with consistent hover / press states. Roles are inferred from
    /// the GameObject name so it works without per-button wiring.
    ///
    /// Surface and text colours are baked directly into the scene (input / panel
    /// backgrounds use a white ColorBlock, so their Image colour renders as-is);
    /// this pass deliberately only touches Buttons to avoid clobbering
    /// semantic text colours (e.g. the green / blue background-mode label).
    /// </summary>
    public static class UITheme
    {
        // Primary / utility actions (Browse, Load, cycle ‹ ›, Manage, Export…).
        public static readonly Color Accent      = new Color(0.30f, 0.46f, 0.78f, 1f);
        // Affirmative actions (Start, Save, Add, Confirm).
        public static readonly Color Positive    = new Color(0.22f, 0.60f, 0.42f, 1f);
        // Destructive actions (Quit, Delete, Remove).
        public static readonly Color Destructive = new Color(0.72f, 0.30f, 0.32f, 1f);
        // Dismissive / toggle actions (Close, Cancel, Clear, font-style toggles).
        public static readonly Color Neutral     = new Color(0.22f, 0.24f, 0.29f, 1f);

        public static void Apply(GameObject root)
        {
            if (root == null) return;
            var buttons = root.GetComponentsInChildren<Button>(includeInactive: true);
            for (int i = 0; i < buttons.Length; i++) Skin(buttons[i]);
        }

        static void Skin(Button btn)
        {
            if (btn == null) return;
            Color role = RoleFor(btn.gameObject.name);

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

        static Color RoleFor(string rawName)
        {
            string n = rawName.ToLowerInvariant();

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

        static Color Lighten(Color c, float d)
            => new Color(Mathf.Min(1f, c.r + d), Mathf.Min(1f, c.g + d), Mathf.Min(1f, c.b + d), c.a);

        static Color Darken(Color c, float d)
            => new Color(Mathf.Max(0f, c.r - d), Mathf.Max(0f, c.g - d), Mathf.Max(0f, c.b - d), c.a);
    }
}

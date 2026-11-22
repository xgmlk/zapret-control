using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZapretControl
{
    // Central colour palette. Every custom control reads its colours here, so
    // the light theme reproduces the previous values exactly and the dark
    // theme is one palette swap. The mode is persisted in settings and applied
    // at startup; switching asks for a restart, which avoids half-themed
    // windows and owner-drawn leftovers.
    public sealed class ThemePalette
    {
        public Color WindowBg;
        public Color WindowTop;
        public Color WindowBottom;
        public Color CardBg;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color Caption;
        public Color Border;
        public Color Divider;
        public Color ButtonBg;
        public Color ButtonBorder;
        public Color ButtonHover;
        public Color ButtonDown;
        public Color InputBg;
        public Color InputReadOnlyBg;
        public Color Success;
        public Color Warning;
        public Color Danger;
        public Color TabActiveBg;
        public Color TabInactiveText;
        public Color TitleText;
        public Color SelectionBg;
        public Color ToggleTrackTop;
        public Color ToggleTrackBottom;
        public Color ToggleTrackBorder;
        public Color ToggleText;
        public Color ToggleKnobTop;
        public Color ToggleKnobBottom;
        public Color ToggleKnobBorder;
        public Color ToggleShadow;
        public Color ToggleOverlayBg;
        public int ToggleOverlayAlpha;
        public Color TrafficInactiveFill;
        public Color TrafficInactiveBorder;
    }

    public static class Theme
    {
        private static ThemePalette _current;
        private static bool _dark;

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static ThemePalette Current
        {
            get
            {
                if (_current == null)
                {
                    _current = CreateLight();
                }
                return _current;
            }
        }

        // Must be called before the first form is constructed.
        public static void Initialize()
        {
            bool dark = false;
            try
            {
                dark = Settings.Load().DarkTheme;
            }
            catch (Exception)
            {
            }
            SetMode(dark);
        }

        public static void SetMode(bool dark)
        {
            _dark = dark;
            _current = dark ? CreateDark() : CreateLight();
        }

        // Re-applies the palette to a control tree. Used after constructing a
        // form so controls created directly (not through TabView helpers) get
        // the same colours. Labels marked "state" keep their semantic colour.
        public static void Apply(Control root)
        {
            if (root == null)
            {
                return;
            }
            StyleControl(root);
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Apply(root.Controls[i]);
            }
        }

        private static void StyleControl(Control control)
        {
            if (control == null)
            {
                return;
            }
            // GUI-only custom controls are matched by name so the installer
            // binary (which does not include their sources) can share Theme.
            string typeName = control.GetType().Name;
            if (typeName == "ToggleSwitch" || typeName == "TrafficLightButton" ||
                control.Name == "tabButton")
            {
                return;
            }
            ThemePalette palette = Current;
            if (typeName == "CardPanel")
            {
                control.BackColor = palette.CardBg;
                control.ForeColor = palette.TextPrimary;
                return;
            }
            if (typeName == "TabView")
            {
                control.BackColor = palette.WindowBg;
                control.ForeColor = palette.TextPrimary;
                return;
            }
            if (control is Form)
            {
                control.BackColor = palette.WindowBg;
                control.ForeColor = palette.TextPrimary;
                return;
            }
            if (control is Panel)
            {
                control.BackColor = palette.WindowBg;
                return;
            }
            if (control is Label)
            {
                control.BackColor = Color.Transparent;
                string tag = control.Tag as string;
                if (tag == "state")
                {
                    return;
                }
                if (tag == "muted")
                {
                    control.ForeColor = palette.TextSecondary;
                }
                else if (tag == "caption")
                {
                    control.ForeColor = palette.Caption;
                }
                else
                {
                    control.ForeColor = palette.TextPrimary;
                }
                return;
            }
            if (control is Button)
            {
                StyleButton((Button)control);
                return;
            }
            if (control is TextBox)
            {
                TextBox box = (TextBox)control;
                box.BackColor = box.ReadOnly
                    ? palette.InputReadOnlyBg
                    : palette.InputBg;
                box.ForeColor = palette.TextPrimary;
                return;
            }
            if (control is ComboBox)
            {
                StyleCombo((ComboBox)control);
                return;
            }
            if (control is CheckBox)
            {
                StyleCheck((CheckBox)control);
                return;
            }
            if (control is ListView)
            {
                ListView list = (ListView)control;
                list.BackColor = palette.InputBg;
                list.ForeColor = palette.TextPrimary;
                return;
            }
        }

        public static void StyleButton(Button button)
        {
            if (button == null)
            {
                return;
            }
            ThemePalette palette = Current;
            button.ForeColor = palette.TextPrimary;
            button.BackColor = palette.ButtonBg;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = palette.ButtonBorder;
            button.FlatAppearance.MouseOverBackColor = palette.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = palette.ButtonDown;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleCombo(ComboBox combo)
        {
            if (combo == null)
            {
                return;
            }
            ThemePalette palette = Current;
            // FlatStyle resets the background to a system colour, so it is set
            // first; owner-draw then paints both the closed box and the list.
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = palette.InputBg;
            combo.ForeColor = palette.TextPrimary;
            if (Theme.IsDark)
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem -= ComboDrawItem;
                combo.DrawItem += ComboDrawItem;
            }
        }

        private static void ComboDrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null)
            {
                return;
            }
            ThemePalette palette = Current;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected
                ? palette.SelectionBg
                : palette.InputBg;
            using (SolidBrush fill = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(fill, e.Bounds);
            }
            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                string text = combo.GetItemText(combo.Items[e.Index]);
                using (SolidBrush brush = new SolidBrush(palette.TextPrimary))
                {
                    e.Graphics.DrawString(
                        text, combo.Font, brush,
                        e.Bounds.X + 2, e.Bounds.Y + 2);
                }
            }
            if ((e.State & DrawItemState.Focus) != 0)
            {
                e.DrawFocusRectangle();
            }
        }

        public static void StyleCheck(CheckBox check)
        {
            if (check == null)
            {
                return;
            }
            check.BackColor = Color.Transparent;
            check.ForeColor = Current.TextPrimary;
            check.FlatStyle = FlatStyle.Flat;
        }

        public static void StyleList(ListView list)
        {
            if (list == null)
            {
                return;
            }
            list.BackColor = Current.InputBg;
            list.ForeColor = Current.TextPrimary;
            if (!Theme.IsDark)
            {
                list.OwnerDraw = false;
                return;
            }
            list.OwnerDraw = true;
            list.DrawColumnHeader -= ListDrawColumnHeader;
            list.DrawColumnHeader += ListDrawColumnHeader;
            list.DrawItem -= ListDrawItem;
            list.DrawItem += ListDrawItem;
            list.DrawSubItem -= ListDrawSubItem;
            list.DrawSubItem += ListDrawSubItem;
        }

        private static void ListDrawColumnHeader(
            object sender, DrawListViewColumnHeaderEventArgs e)
        {
            ThemePalette palette = Current;
            using (SolidBrush fill = new SolidBrush(palette.ButtonBg))
            {
                e.Graphics.FillRectangle(fill, e.Bounds);
            }
            using (Pen border = new Pen(palette.Border))
            {
                e.Graphics.DrawLine(
                    border, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            Font font = e.Font ?? SystemFonts.DefaultFont;
            TextRenderer.DrawText(
                e.Graphics,
                e.Header == null ? string.Empty : e.Header.Text,
                font,
                e.Bounds,
                palette.TextSecondary,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private static void ListDrawItem(
            object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
        }

        private static void ListDrawSubItem(
            object sender, DrawListViewSubItemEventArgs e)
        {
            ThemePalette palette = Current;
            bool selected = (e.ItemState & ListViewItemStates.Selected) != 0;
            Color background;
            if (selected)
            {
                background = palette.SelectionBg;
            }
            else if (!e.Item.BackColor.IsEmpty)
            {
                background = e.Item.BackColor;
            }
            else
            {
                background = palette.InputBg;
            }
            using (SolidBrush fill = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(fill, e.Bounds);
            }
            Color text = e.Item.ForeColor.IsEmpty
                ? palette.TextPrimary
                : e.Item.ForeColor;
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem == null ? string.Empty : e.SubItem.Text,
                e.SubItem == null ? e.Item.Font : e.SubItem.Font,
                e.Bounds,
                text,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private static ThemePalette CreateLight()
        {
            ThemePalette p = new ThemePalette();
            p.WindowBg = Color.FromArgb(247, 246, 243);
            p.WindowTop = Color.FromArgb(250, 249, 246);
            p.WindowBottom = Color.FromArgb(242, 240, 236);
            p.CardBg = Color.FromArgb(252, 251, 248);
            p.TextPrimary = Color.FromArgb(44, 44, 46);
            p.TextSecondary = Color.FromArgb(110, 110, 115);
            p.Caption = Color.FromArgb(111, 108, 102);
            p.Border = Color.FromArgb(232, 229, 223);
            p.Divider = Color.FromArgb(236, 233, 227);
            p.ButtonBg = Color.FromArgb(253, 252, 249);
            p.ButtonBorder = Color.FromArgb(214, 210, 202);
            p.ButtonHover = Color.FromArgb(247, 243, 234);
            p.ButtonDown = Color.FromArgb(238, 233, 222);
            p.InputBg = Color.White;
            p.InputReadOnlyBg = Color.FromArgb(248, 247, 244);
            p.Success = Color.FromArgb(31, 122, 51);
            p.Warning = Color.FromArgb(150, 112, 28);
            p.Danger = Color.FromArgb(168, 58, 51);
            p.TabActiveBg = Color.FromArgb(252, 251, 248);
            p.TabInactiveText = Color.FromArgb(90, 88, 84);
            p.TitleText = Color.FromArgb(110, 110, 115);
            p.SelectionBg = Color.FromArgb(232, 245, 233);
            p.ToggleTrackTop = Color.FromArgb(243, 241, 237);
            p.ToggleTrackBottom = Color.FromArgb(228, 226, 221);
            p.ToggleTrackBorder = Color.FromArgb(213, 210, 204);
            p.ToggleText = Color.FromArgb(178, 174, 167);
            p.ToggleKnobTop = Color.White;
            p.ToggleKnobBottom = Color.FromArgb(243, 241, 237);
            p.ToggleKnobBorder = Color.FromArgb(226, 223, 217);
            p.ToggleShadow = Color.FromArgb(90, 85, 75);
            p.ToggleOverlayBg = Color.FromArgb(247, 246, 243);
            p.ToggleOverlayAlpha = 120;
            p.TrafficInactiveFill = Color.FromArgb(222, 220, 215);
            p.TrafficInactiveBorder = Color.FromArgb(208, 206, 201);
            return p;
        }

        private static ThemePalette CreateDark()
        {
            ThemePalette p = new ThemePalette();
            p.WindowBg = Color.FromArgb(27, 26, 24);
            p.WindowTop = Color.FromArgb(34, 32, 29);
            p.WindowBottom = Color.FromArgb(23, 22, 20);
            p.CardBg = Color.FromArgb(37, 36, 33);
            p.TextPrimary = Color.FromArgb(236, 232, 224);
            p.TextSecondary = Color.FromArgb(162, 157, 148);
            p.Caption = Color.FromArgb(156, 150, 139);
            p.Border = Color.FromArgb(58, 55, 49);
            p.Divider = Color.FromArgb(51, 48, 42);
            p.ButtonBg = Color.FromArgb(43, 41, 37);
            p.ButtonBorder = Color.FromArgb(70, 66, 58);
            p.ButtonHover = Color.FromArgb(52, 49, 43);
            p.ButtonDown = Color.FromArgb(61, 57, 50);
            p.InputBg = Color.FromArgb(31, 30, 27);
            p.InputReadOnlyBg = Color.FromArgb(35, 34, 32);
            p.Success = Color.FromArgb(88, 200, 120);
            p.Warning = Color.FromArgb(232, 183, 90);
            p.Danger = Color.FromArgb(255, 122, 112);
            p.TabActiveBg = Color.FromArgb(37, 36, 33);
            p.TabInactiveText = Color.FromArgb(162, 157, 148);
            p.TitleText = Color.FromArgb(162, 157, 148);
            p.SelectionBg = Color.FromArgb(44, 58, 45);
            p.ToggleTrackTop = Color.FromArgb(48, 46, 41);
            p.ToggleTrackBottom = Color.FromArgb(39, 37, 33);
            p.ToggleTrackBorder = Color.FromArgb(78, 73, 64);
            p.ToggleText = Color.FromArgb(140, 133, 122);
            p.ToggleKnobTop = Color.FromArgb(78, 73, 64);
            p.ToggleKnobBottom = Color.FromArgb(54, 51, 45);
            p.ToggleKnobBorder = Color.FromArgb(100, 93, 82);
            p.ToggleShadow = Color.FromArgb(5, 5, 4);
            p.ToggleOverlayBg = Color.FromArgb(27, 26, 24);
            p.ToggleOverlayAlpha = 150;
            p.TrafficInactiveFill = Color.FromArgb(58, 55, 49);
            p.TrafficInactiveBorder = Color.FromArgb(70, 66, 58);
            return p;
        }
    }
}

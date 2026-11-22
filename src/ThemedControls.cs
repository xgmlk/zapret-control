using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZapretControl
{
    // Flat button with theme-aware colours. The stock Button draws disabled
    // text in the system gray, which is nearly invisible on a dark surface;
    // this control blends the text towards the background instead and keeps
    // the light theme visually identical to the previous flat buttons.
    public sealed class ThemedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public ThemedButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            Invalidate();
            base.OnBackColorChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = Theme.Current;
            Color background = BackColor;
            if (background == Color.Transparent)
            {
                background = Parent != null ? Parent.BackColor : palette.WindowBg;
            }
            Color mouseOver = FlatAppearance.MouseOverBackColor;
            Color mouseDown = FlatAppearance.MouseDownBackColor;
            if (Enabled && _pressed && mouseDown != Color.Empty)
            {
                background = mouseDown;
            }
            else if (Enabled && _hover && mouseOver != Color.Empty)
            {
                background = mouseOver;
            }

            using (SolidBrush fill = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }
            if (FlatAppearance.BorderSize > 0)
            {
                using (Pen border = new Pen(
                    FlatAppearance.BorderColor, FlatAppearance.BorderSize))
                {
                    int inset = FlatAppearance.BorderSize - 1;
                    e.Graphics.DrawRectangle(
                        border, inset, inset,
                        ClientSize.Width - 1 - inset * 2,
                        ClientSize.Height - 1 - inset * 2);
                }
            }

            Color text = Enabled
                ? ForeColor
                : Blend(ForeColor, background, 0.35);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                text,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                Rectangle focus = new Rectangle(3, 3,
                    Math.Max(1, ClientSize.Width - 7),
                    Math.Max(1, ClientSize.Height - 7));
                ControlPaint.DrawFocusRectangle(
                    e.Graphics, focus, text, background);
            }
        }

        private static Color Blend(Color foreground, Color background, double amount)
        {
            int r = (int)(foreground.R * (1.0 - amount) + background.R * amount);
            int g = (int)(foreground.G * (1.0 - amount) + background.G * amount);
            int b = (int)(foreground.B * (1.0 - amount) + background.B * amount);
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, r)),
                Math.Max(0, Math.Min(255, g)),
                Math.Max(0, Math.Min(255, b)));
        }
    }

    // Check box that keeps the native look in the light theme and draws a
    // high-contrast box in the dark theme (the native glyph is a white square).
    public sealed class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Theme.IsDark)
            {
                base.OnPaint(e);
                return;
            }
            ThemePalette palette = Theme.Current;
            Color background = Parent != null
                ? Parent.BackColor
                : palette.WindowBg;
            using (SolidBrush fill = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }

            const int boxSize = 13;
            int top = (Height - boxSize) / 2;
            Rectangle box = new Rectangle(0, top, boxSize, boxSize);
            using (SolidBrush fill = new SolidBrush(
                Checked ? palette.Success : palette.InputBg))
            {
                e.Graphics.FillRectangle(fill, box);
            }
            using (Pen border = new Pen(
                Checked ? palette.Success : palette.ButtonBorder))
            {
                e.Graphics.DrawRectangle(border, box);
            }
            if (Checked)
            {
                using (Pen check = new Pen(palette.WindowBg, 2f))
                {
                    e.Graphics.DrawLine(check, 3, top + 7, 6, top + 10);
                    e.Graphics.DrawLine(check, 6, top + 10, 11, top + 3);
                }
            }

            Color text = Enabled
                ? palette.TextPrimary
                : palette.TextSecondary;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(boxSize + 6, 0,
                    Math.Max(1, Width - boxSize - 6), Height),
                text,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    new Rectangle(boxSize + 4, 1,
                        Math.Max(1, Width - boxSize - 5), Height - 3),
                    text, background);
            }
        }
    }

    // Radio button: native in the light theme, a high-contrast dot in the dark.
    public sealed class ThemedRadioButton : RadioButton
    {
        public ThemedRadioButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Theme.IsDark)
            {
                base.OnPaint(e);
                return;
            }
            ThemePalette palette = Theme.Current;
            Color background = Parent != null
                ? Parent.BackColor
                : palette.WindowBg;
            using (SolidBrush fill = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }

            const int size = 13;
            int top = (Height - size) / 2;
            Rectangle circle = new Rectangle(0, top, size, size);
            using (SolidBrush fill = new SolidBrush(palette.InputBg))
            {
                e.Graphics.FillEllipse(fill, circle);
            }
            using (Pen border = new Pen(
                Checked ? palette.Success : palette.ButtonBorder))
            {
                e.Graphics.DrawEllipse(border, circle);
            }
            if (Checked)
            {
                using (SolidBrush dot = new SolidBrush(palette.Success))
                {
                    e.Graphics.FillEllipse(dot, 3, top + 3, 7, 7);
                }
            }

            Color text = Enabled
                ? palette.TextPrimary
                : palette.TextSecondary;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(size + 6, 0,
                    Math.Max(1, Width - size - 6), Height),
                text,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    new Rectangle(size + 4, 1,
                        Math.Max(1, Width - size - 5), Height - 3),
                    text, background);
            }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretControl
{
    public enum TrafficLightKind
    {
        Close,
        Minimize,
        Zoom
    }

    // macOS-style round window button: close / minimize / zoom.
    public sealed class TrafficLightButton : Control
    {
        private readonly TrafficLightKind _kind;
        private bool _hover;
        private bool _inactive;

        public TrafficLightButton(TrafficLightKind kind)
        {
            _kind = kind;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Size = new Size(14, 14);
            TabStop = false;
            if (kind == TrafficLightKind.Zoom)
            {
                Enabled = false;
            }
            else
            {
                Cursor = Cursors.Hand;
            }
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = kind == TrafficLightKind.Close ? "Закрыть" :
                             kind == TrafficLightKind.Minimize ? "Свернуть" : "Развернуть";
        }

        public bool Inactive
        {
            get { return _inactive; }
            set
            {
                if (_inactive != value)
                {
                    _inactive = value;
                    Invalidate();
                }
            }
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill;
            Color border;
            if (!Enabled || _inactive)
            {
                fill = Theme.Current.TrafficInactiveFill;
                border = Theme.Current.TrafficInactiveBorder;
            }
            else if (_kind == TrafficLightKind.Close)
            {
                fill = Color.FromArgb(255, 95, 87);
                border = Color.FromArgb(224, 68, 62);
            }
            else if (_kind == TrafficLightKind.Minimize)
            {
                fill = Color.FromArgb(254, 188, 46);
                border = Color.FromArgb(222, 161, 35);
            }
            else
            {
                fill = Color.FromArgb(40, 200, 64);
                border = Color.FromArgb(26, 171, 41);
            }

            RectangleF circle = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillEllipse(brush, circle);
            }
            using (Pen pen = new Pen(border, 1f))
            {
                e.Graphics.DrawEllipse(pen, circle);
            }

            if (!_hover || !Enabled || _inactive)
            {
                return;
            }

            float cx = Width / 2f;
            float cy = Height / 2f;
            using (Pen glyph = new Pen(Color.FromArgb(120, 20, 20, 20), 1.4f))
            {
                if (_kind == TrafficLightKind.Close)
                {
                    e.Graphics.DrawLine(glyph, cx - 2.6f, cy - 2.6f, cx + 2.6f, cy + 2.6f);
                    e.Graphics.DrawLine(glyph, cx + 2.6f, cy - 2.6f, cx - 2.6f, cy + 2.6f);
                }
                else if (_kind == TrafficLightKind.Minimize)
                {
                    e.Graphics.DrawLine(glyph, cx - 3f, cy, cx + 3f, cy);
                }
            }
        }
    }
}

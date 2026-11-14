using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretControl
{
    // Large soft "ON/OFF" pill with an animated knob (iOS/macOS-like).
    // The control does not change its own state on click: it raises ToggleRequested
    // and the form applies the real service state, so the UI never lies.
    public sealed class ToggleSwitch : Control
    {
        private readonly Timer _animation;
        private readonly Font _labelFont;
        private bool _checked;
        private bool _busy;
        private float _position;
        private float _from;
        private float _to;
        private DateTime _started;

        public event EventHandler ToggleRequested;

        public ToggleSwitch()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.Selectable,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = "Zapret";
            AccessibleDescription = "Включить или выключить службу zapret";

            _labelFont = new Font("Segoe UI", 20f, FontStyle.Regular);

            _animation = new Timer();
            _animation.Interval = 15;
            _animation.Tick += OnAnimationTick;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    return;
                }
                _checked = value;
                _from = _position;
                _to = value ? 1f : 0f;
                _started = DateTime.Now;
                _animation.Start();
                Invalidate();
            }
        }

        public bool Busy
        {
            get { return _busy; }
            set
            {
                if (_busy != value)
                {
                    _busy = value;
                    Cursor = value ? Cursors.WaitCursor : Cursors.Hand;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && !_busy)
            {
                RaiseToggle();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                if (!_busy)
                {
                    RaiseToggle();
                }
            }
        }

        private void RaiseToggle()
        {
            if (ToggleRequested != null)
            {
                ToggleRequested(this, EventArgs.Empty);
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            double progress = (DateTime.Now - _started).TotalMilliseconds / 190.0;
            if (progress >= 1.0)
            {
                _position = _to;
                _animation.Stop();
            }
            else
            {
                double eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
                _position = (float)(_from + (_to - _from) * eased);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = g.DpiX / 96f;
            float margin = 7f * scale;
            RectangleF track = new RectangleF(
                1.5f * scale, 1.5f * scale,
                Width - 3f * scale, Height - 3f * scale);
            float radius = track.Height / 2f;

            using (GraphicsPath trackPath = UiDrawing.RoundedRect(track, radius))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    track,
                    Color.FromArgb(243, 241, 237),
                    Color.FromArgb(228, 226, 221),
                    90f))
                {
                    g.FillPath(fill, trackPath);
                }

                // soft inner shadow at the top of the track
                g.SetClip(trackPath);
                RectangleF innerRect = new RectangleF(track.X, track.Y, track.Width, track.Height * 0.42f);
                using (LinearGradientBrush inner = new LinearGradientBrush(
                    innerRect,
                    Color.FromArgb(30, 120, 115, 105),
                    Color.FromArgb(0, 120, 115, 105),
                    90f))
                {
                    g.FillRectangle(inner, innerRect);
                }
                g.ResetClip();

                using (Pen border = new Pen(Color.FromArgb(213, 210, 204), 1.2f * scale))
                {
                    g.DrawPath(border, trackPath);
                }
            }

            float knobSize = Height - margin * 2f;
            float travel = Width - knobSize - margin * 2f;
            float knobX = margin + _position * travel;
            float targetKnobX = margin + (_checked ? travel : 0f);
            RectangleF knob = new RectangleF(knobX, margin, knobSize, knobSize);

            // Label lives in the free area of the target state and is drawn under
            // the knob, so it stays inside the pill while the knob is animating.
            string text = _checked ? "ON" : "OFF";
            RectangleF labelRect;
            if (_checked)
            {
                labelRect = new RectangleF(0f, 0f, targetKnobX - margin * 0.5f, Height);
            }
            else
            {
                float labelStart = targetKnobX + knobSize + margin * 0.5f;
                labelRect = new RectangleF(labelStart, 0f, Width - labelStart - margin * 0.5f, Height);
            }

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(178, 174, 167)))
                {
                    g.DrawString(text, _labelFont, textBrush, labelRect, format);
                }
            }

            // layered soft shadow under the knob
            for (int layer = 3; layer >= 1; layer--)
            {
                float grow = layer * 2.4f * scale;
                RectangleF shadow = new RectangleF(
                    knob.X - grow / 2f,
                    knob.Y + 2.2f * scale,
                    knob.Width + grow,
                    knob.Height + grow);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(shadow);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(26 / layer, 90, 85, 75);
                        brush.SurroundColors = new Color[] { Color.FromArgb(0, 90, 85, 75) };
                        g.FillPath(brush, path);
                    }
                }
            }

            using (LinearGradientBrush knobFill = new LinearGradientBrush(
                knob,
                Color.White,
                Color.FromArgb(243, 241, 237),
                90f))
            {
                g.FillEllipse(knobFill, knob);
            }
            using (Pen knobBorder = new Pen(Color.FromArgb(226, 223, 217), 1f * scale))
            {
                g.DrawEllipse(knobBorder, knob);
            }

            if (_busy)
            {
                using (GraphicsPath overlayPath = UiDrawing.RoundedRect(track, radius))
                using (SolidBrush overlay = new SolidBrush(Color.FromArgb(120, 247, 246, 243)))
                {
                    g.FillPath(overlay, overlayPath);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _labelFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

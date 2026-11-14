using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretControl
{
    public abstract class TabView : Panel
    {
        protected static readonly Color TextColor = Color.FromArgb(44, 44, 46);
        protected static readonly Color MutedColor = Color.FromArgb(110, 110, 115);
        protected static readonly Color GreenColor = Color.FromArgb(31, 122, 51);
        protected static readonly Color RedColor = Color.FromArgb(168, 58, 51);
        protected static readonly Color AmberColor = Color.FromArgb(150, 112, 28);
        protected static readonly Color CardColor = Color.FromArgb(252, 251, 248);
        protected static readonly Color BorderColor = Color.FromArgb(232, 229, 223);
        protected static readonly Color ButtonColor = Color.FromArgb(253, 252, 249);

        protected TabView()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(247, 246, 243);
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9F);
        }

        public abstract string TabTitle { get; }

        public virtual void ActivateTab()
        {
        }

        public virtual void RefreshData()
        {
        }

        protected CardPanel AddCard(string title, int x, int y, int width, int height)
        {
            CardPanel card = new CardPanel();
            card.SetBounds(x, y, width, height);
            Controls.Add(card);
            if (!string.IsNullOrEmpty(title))
            {
                Label caption = new Label();
                caption.Text = title;
                caption.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
                caption.ForeColor = Color.FromArgb(111, 108, 102);
                caption.BackColor = Color.Transparent;
                caption.SetBounds(18, 12, width - 36, 16);
                card.Controls.Add(caption);
            }
            return card;
        }

        protected static Label AddLabel(
            Control parent, string text, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = TextColor;
            label.BackColor = Color.Transparent;
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }

        protected static Label AddMutedLabel(
            Control parent, string text, int x, int y, int width, int height)
        {
            Label label = AddLabel(parent, text, x, y, width, height);
            label.ForeColor = MutedColor;
            label.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            return label;
        }

        protected static Button AddButton(
            Control parent, string text, int x, int y, int width, int height,
            EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            button.ForeColor = TextColor;
            button.BackColor = ButtonColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(214, 210, 202);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(247, 243, 234);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(238, 233, 222);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.SetBounds(x, y, width, height);
            button.Click += handler;
            parent.Controls.Add(button);
            return button;
        }

        protected static TextBox AddTextBox(
            Control parent, int x, int y, int width, int height, bool readOnly)
        {
            TextBox box = new TextBox();
            box.SetBounds(x, y, width, height);
            box.ReadOnly = readOnly;
            box.BackColor = readOnly ? Color.FromArgb(248, 247, 244) : Color.White;
            box.ForeColor = TextColor;
            box.BorderStyle = BorderStyle.FixedSingle;
            if (height > 40)
            {
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Vertical;
            }
            parent.Controls.Add(box);
            return box;
        }

        protected static void SetState(Label label, string text, Color color)
        {
            label.Text = text;
            label.ForeColor = color;
        }

        protected static string ShortError(Exception ex)
        {
            if (ex == null)
            {
                return "Неизвестная ошибка.";
            }
            string message = ex.Message;
            if (string.IsNullOrEmpty(message))
            {
                message = ex.GetType().Name;
            }
            return message.Length > 500 ? message.Substring(0, 500) : message;
        }

        protected void ShowError(string title, Exception ex)
        {
            MessageBox.Show(
                this,
                SafeMessage(ex),
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static string SafeMessage(Exception ex)
        {
            if (ex == null)
            {
                return "Неизвестная ошибка.";
            }
            LogException(ex);
            string message = ex.Message ?? string.Empty;
            if (message.IndexOf(":\\", StringComparison.Ordinal) >= 0 ||
                message.IndexOf("\\\\", StringComparison.Ordinal) >= 0)
            {
                return "Операция не удалась (" + ex.GetType().Name + ").\n" +
                       "Подробности записаны в logs\\ui-error.log.";
            }
            if (message.Length > 500)
            {
                message = message.Substring(0, 500);
            }
            return message.Length == 0 ? ex.GetType().Name : message;
        }

        private static void LogException(Exception ex)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                string line = "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" +
                              Environment.NewLine + ex + Environment.NewLine;
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppPaths.LogDirectory, "ui-error.log"),
                    line, new System.Text.UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }

        protected void ShowInfo(string text)
        {
            MessageBox.Show(
                this,
                text,
                "Zapret Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        protected static bool Confirm(string text)
        {
            return MessageBox.Show(
                text,
                "Zapret Control",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }

    public sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(252, 251, 248);
            ForeColor = Color.FromArgb(44, 44, 46);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiDrawing.RoundedRect(
                new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10f))
            using (Pen pen = new Pen(Color.FromArgb(232, 229, 223), 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}

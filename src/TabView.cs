using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretControl
{
    public abstract class TabView : Panel
    {
        protected static Color TextColor
        {
            get { return Theme.Current.TextPrimary; }
        }

        protected static Color MutedColor
        {
            get { return Theme.Current.TextSecondary; }
        }

        protected static Color GreenColor
        {
            get { return Theme.Current.Success; }
        }

        protected static Color RedColor
        {
            get { return Theme.Current.Danger; }
        }

        protected static Color AmberColor
        {
            get { return Theme.Current.Warning; }
        }

        protected static Color CardColor
        {
            get { return Theme.Current.CardBg; }
        }

        protected static Color BorderColor
        {
            get { return Theme.Current.Border; }
        }

        protected static Color ButtonColor
        {
            get { return Theme.Current.ButtonBg; }
        }

        protected TabView()
        {
            DoubleBuffered = true;
            BackColor = Theme.Current.WindowBg;
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
                caption.ForeColor = Theme.Current.Caption;
                caption.BackColor = Color.Transparent;
                caption.Tag = "caption";
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
            label.Tag = "text";
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
            label.Tag = "muted";
            return label;
        }

        protected static Button AddButton(
            Control parent, string text, int x, int y, int width, int height,
            EventHandler handler)
        {
            Button button = new ThemedButton();
            button.Text = text;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            Theme.StyleButton(button);
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
            box.BackColor = readOnly
                ? Theme.Current.InputReadOnlyBg
                : Theme.Current.InputBg;
            box.ForeColor = TextColor;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Tag = "input";
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
            label.Tag = "state";
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

        protected void ShowHelp(string title, string text)
        {
            using (HelpForm form = new HelpForm(title, text))
            {
                form.ShowDialog(this);
            }
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
            BackColor = Theme.Current.CardBg;
            ForeColor = Theme.Current.TextPrimary;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiDrawing.RoundedRect(
                new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10f))
            using (Pen pen = new Pen(Theme.Current.Border, 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}

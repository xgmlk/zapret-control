using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZapretControl
{
    public enum FirstRunChoice
    {
        None,
        Advise,
        Manual,
        Later
    }

    // One-time invitation after installation. Closing the window leaves
    // FirstRunChoice.None so the caller does not persist the first-run flag.
    public sealed class FirstRunForm : Form
    {
        public FirstRunChoice Choice { get; private set; }

        public FirstRunForm()
        {
            Choice = FirstRunChoice.None;
            Text = "Zapret Control — первый запуск";
            ClientSize = new Size(520, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(247, 246, 243);
            ForeColor = Color.FromArgb(44, 44, 46);
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            using (System.IO.Stream iconStream = typeof(FirstRunForm).Assembly
                .GetManifestResourceStream("ZapretControl.app.ico"))
            {
                if (iconStream != null)
                {
                    Icon = new Icon(iconStream, new Size(64, 64));
                }
            }

            Label title = new Label();
            title.Text = "Похоже, вы запускаете программу впервые.";
            title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            title.SetBounds(24, 20, 472, 28);
            Controls.Add(title);

            Label text = new Label();
            text.Text =
                "Подобрать рабочую стратегию автоматически?\n\n" +
                "Проверка займёт 1–4 минуты и временно прервёт защиту. " +
                "IPSet переключается в «любой» и восстанавливается после неё. " +
                "Программа проверит Discord, YouTube (страница, плеер, видео), " +
                "Google и DPI-заморозку, затем покажет список вариантов.";
            text.SetBounds(24, 58, 472, 104);
            Controls.Add(text);

            Button advise = new Button();
            advise.Text = "Подобрать";
            advise.SetBounds(156, 190, 108, 32);
            advise.Click += delegate
            {
                Choice = FirstRunChoice.Advise;
                DialogResult = DialogResult.Yes;
                Close();
            };
            Controls.Add(advise);

            Button manual = new Button();
            manual.Text = "Выбрать вручную";
            manual.SetBounds(272, 190, 132, 32);
            manual.Click += delegate
            {
                Choice = FirstRunChoice.Manual;
                DialogResult = DialogResult.No;
                Close();
            };
            Controls.Add(manual);

            Button later = new Button();
            later.Text = "Позже";
            later.SetBounds(412, 190, 84, 32);
            later.Click += delegate
            {
                Choice = FirstRunChoice.Later;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(later);

            AcceptButton = advise;
            CancelButton = later;
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class MainForm : Form
    {
        private const int TitleBarHeight = 44;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private readonly Font _titleFont;
        private readonly Font _tabFont;
        private readonly Font _tabActiveFont;
        private readonly Font _footerFont;
        private readonly TrafficLightButton[] _lights;
        private readonly Button[] _tabButtons;
        private readonly TabView[] _tabs;
        private readonly Timer _timer;
        private Label _footer;
        private int _activeIndex = -1;
        private int _firstRunRetries;

        public MainForm()
        {
            Text = "Zapret Control";
            ClientSize = new Size(900, 680);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 246, 243);
            ForeColor = Color.FromArgb(44, 44, 46);
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            _titleFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            _tabFont = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            _tabActiveFont = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            _footerFont = new Font("Segoe UI", 8F, FontStyle.Regular);

            _lights = new TrafficLightButton[3];
            _lights[0] = CreateLight(TrafficLightKind.Minimize, ClientSize.Width - 70);
            _lights[1] = CreateLight(TrafficLightKind.Zoom, ClientSize.Width - 50);
            _lights[2] = CreateLight(TrafficLightKind.Close, ClientSize.Width - 30);

            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("ZapretControl.app.ico"))
            {
                if (iconStream != null)
                {
                    Icon = new Icon(iconStream, new Size(256, 256));
                }
            }

            _tabs = new TabView[]
            {
                new HomeTab(),
                new ServiceTab(),
                new GameTab(),
                new IpsetTab(),
                new FakesTab(),
                new UpdatesTab(),
                new DiagnosticsTab(),
                new TestsTab(),
                new TelegramTab()
            };

            HomeTab homeTab = (HomeTab)_tabs[0];
            TestsTab testsTab = (TestsTab)_tabs[7];
            UpdatesTab updatesTab = (UpdatesTab)_tabs[5];
            updatesTab.AppUpdateAvailable += OnAppUpdateAvailable;
            homeTab.CheckCompleted += delegate(HomeCheckReport report)
            {
                testsTab.ShowReport(report);
            };
            homeTab.DetailsRequested += delegate
            {
                ActivateTab(7);
            };
            testsTab.AdviseRequested += delegate
            {
                ActivateTab(0);
                homeTab.StartAdvice(true);
            };

            _tabButtons = new Button[_tabs.Length];
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].SetBounds(24, 98, 852, 520);
                _tabs[i].Visible = false;
                Controls.Add(_tabs[i]);

                Button button = new Button();
                button.Text = _tabs[i].TabTitle;
                button.Font = _tabFont;
                button.ForeColor = Color.FromArgb(90, 88, 84);
                button.BackColor = Color.Transparent;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(241, 238, 232);
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
                button.SetBounds(24 + i * 92, 50, 88, 36);
                button.Tag = i;
                button.Click += OnTabClick;
                button.AccessibleName = _tabs[i].TabTitle;
                Controls.Add(button);
                _tabButtons[i] = button;
            }

            _footer = new Label();
            _footer.Font = _footerFont;
            _footer.ForeColor = Color.FromArgb(110, 110, 115);
            _footer.BackColor = Color.Transparent;
            _footer.TextAlign = ContentAlignment.MiddleLeft;
            _footer.SetBounds(24, 640, 480, 18);
            Controls.Add(_footer);

            Label version = new Label();
            version.Text = "Zapret Control " + AppInfo.Version + " · права администратора";
            version.Font = _footerFont;
            version.ForeColor = Color.FromArgb(110, 110, 115);
            version.BackColor = Color.Transparent;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.SetBounds(420, 640, 456, 18);
            Controls.Add(version);

            Activated += delegate { SetLightsInactive(false); };
            Deactivate += delegate { SetLightsInactive(true); };
            Shown += delegate
            {
                ActivateTab(0);
                RefreshActiveTab();
                ((TelegramTab)_tabs[8]).StartIfConfigured();
                BeginInvoke(new Action(CheckFirstRunAndUpdates));
            };
            FormClosing += OnFormClosing;

            _timer = new Timer();
            _timer.Interval = 3000;
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds,
                Color.FromArgb(250, 249, 246),
                Color.FromArgb(242, 240, 236),
                90f))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = g.DpiX / 96f;

            using (GraphicsPath border = UiDrawing.RoundedRect(
                new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f),
                10f * scale))
            using (Pen pen = new Pen(Color.FromArgb(229, 226, 220), 1f))
            {
                g.DrawPath(pen, border);
            }

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(110, 110, 115)))
                {
                    g.DrawString(
                        "Zapret Control",
                        _titleFont,
                        brush,
                        new RectangleF(0f, 0f, ClientSize.Width, TitleBarHeight * scale),
                        format);
                }
            }

            using (Pen divider = new Pen(Color.FromArgb(236, 233, 227), 1f))
            {
                g.DrawLine(divider, 24f * scale, 92f * scale,
                    ClientSize.Width - 24f * scale, 92f * scale);
                g.DrawLine(divider, 24f * scale, 628f * scale,
                    ClientSize.Width - 24f * scale, 628f * scale);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            float scale;
            using (Graphics g = CreateGraphics())
            {
                scale = g.DpiX / 96f;
            }
            if (e.Y >= TitleBarHeight * scale)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private TrafficLightButton CreateLight(TrafficLightKind kind, int x)
        {
            TrafficLightButton light = new TrafficLightButton(kind);
            light.SetBounds(x, 15, 14, 14);
            if (kind == TrafficLightKind.Close)
            {
                light.Click += delegate { Close(); };
            }
            else if (kind == TrafficLightKind.Minimize)
            {
                light.Click += delegate { WindowState = FormWindowState.Minimized; };
            }
            Controls.Add(light);
            return light;
        }

        private void SetLightsInactive(bool inactive)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                _lights[i].Inactive = inactive;
            }
        }

        private void OnTabClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is int))
            {
                return;
            }
            ActivateTab((int)button.Tag);
        }

        private void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Length || index == _activeIndex)
            {
                if (index == _activeIndex && _tabs[index].Visible)
                {
                    _tabs[index].ActivateTab();
                }
                return;
            }
            if (_activeIndex >= 0)
            {
                _tabs[_activeIndex].Visible = false;
                _tabButtons[_activeIndex].Font = _tabFont;
                _tabButtons[_activeIndex].ForeColor = Color.FromArgb(90, 88, 84);
                _tabButtons[_activeIndex].BackColor = Color.Transparent;
            }
            _activeIndex = index;
            _tabs[index].Visible = true;
            _tabs[index].BringToFront();
            _tabButtons[index].Font = _tabActiveFont;
            _tabButtons[index].ForeColor = Color.FromArgb(44, 44, 46);
            _tabButtons[index].BackColor = Color.FromArgb(252, 251, 248);
            _tabs[index].ActivateTab();
            _tabs[index].RefreshData();
            _footer.Text = "Обновлено " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            RefreshActiveTab();
        }

        private void CheckFirstRunAndUpdates()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            CheckFirstRun();
            // Start the update check only after the first-run dialog closes so
            // a payload update cannot change the service under the prompt.
            if (!OperationGuard.IsBusy)
            {
                ((UpdatesTab)_tabs[5]).CheckIfDue();
            }
        }

        // Shown after the main window is painted. The flag is persisted only
        // after an explicit choice, so closing this dialog asks again next time.
        private void CheckFirstRun()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            SettingsData settings = Settings.Load();
            if (settings.FirstRunCompleted)
            {
                return;
            }
            if (OperationGuard.IsBusy)
            {
                if (_firstRunRetries < 3)
                {
                    _firstRunRetries++;
                    Timer retry = new Timer();
                    retry.Interval = 3000;
                    retry.Tick += delegate
                    {
                        retry.Stop();
                        retry.Dispose();
                        CheckFirstRunAndUpdates();
                    };
                    retry.Start();
                }
                return;
            }
            _firstRunRetries = 0;
            string root = AppPaths.ResolvePayloadRoot();
            bool payload = AppPaths.IsPayloadRoot(root);
            int strategies = payload ? StrategyParser.ListStrategies(root).Count : 0;
            if (!payload || strategies == 0)
            {
                settings.FirstRunCompleted = true;
                Settings.Save();
                MessageBox.Show(
                    this,
                    payload
                        ? "В payload нет стратегий .bat. Переустановите Zapret Control " +
                          "или проверьте папку zapret."
                        : "Payload zapret не найден. Переустановите Zapret Control " +
                          "или проверьте папку zapret.",
                    "Первый запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            FirstRunChoice choice;
            using (FirstRunForm form = new FirstRunForm())
            {
                form.ShowDialog(this);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                choice = form.Choice;
            }
            if (choice == FirstRunChoice.None)
            {
                return;
            }
            settings.FirstRunCompleted = true;
            Settings.Save();
            HomeTab home = (HomeTab)_tabs[0];
            ActivateTab(0);
            if (choice == FirstRunChoice.Advise)
            {
                home.StartAdvice(false);
            }
            else if (choice == FirstRunChoice.Manual)
            {
                home.StartManualSelection();
            }
        }

        private void OnAppUpdateAvailable(UpdateCheckResult result)
        {
            if (IsDisposed || Disposing || result == null)
            {
                return;
            }
            string text = "Доступна новая версия Zapret Control " +
                result.LatestAppVersion + " (установлена " + AppInfo.Version +
                ").\nОткрыть вкладку «Обновления»?";
            if (MessageBox.Show(
                    this,
                    text,
                    "Обновление Zapret Control",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
            {
                ActivateTab(5);
            }
        }

        private void RefreshActiveTab()
        {
            if (_activeIndex < 0 || IsDisposed || Disposing)
            {
                return;
            }
            _footer.Text = "Обновлено " + DateTime.Now.ToString("HH:mm:ss");
            _tabs[_activeIndex].RefreshData();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (OperationGuard.IsBusy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Дождитесь завершения текущей операции.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            _timer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _titleFont.Dispose();
                _tabFont.Dispose();
                _tabActiveFont.Dispose();
                _footerFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

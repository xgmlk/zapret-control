using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZapretControl
{
    public sealed class MainForm : Form
    {
        private const int TitleBarHeight = 44;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_RESTORE = 0xF120;
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
        private readonly System.Windows.Forms.Timer _timer;
        private readonly bool _startHidden;
        private readonly EventWaitHandle _showEvent;
        private readonly EventWaitHandle _exitEvent;
        private readonly Icon _windowIcon;
        private Label _footer;
        private Button _themeButton;
        private Icon _trayOnIcon;
        private Icon _trayOffIcon;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayProtectItem;
        private ToolStripMenuItem _trayTelegramItem;
        private Thread _trayListener;
        private volatile bool _trayListenerStop;
        private volatile bool _exitRequested;
        private bool _pendingExit;
        private bool _trayHintShown;
        private bool _trayPromoted;
        private int _trayPromoteTries;
        private Action _balloonClick;
        private DateTime _lastDataCheck = DateTime.MinValue;
        private DateTime _lastReleaseCheck = DateTime.MinValue;
        private volatile bool _dataCheckRunning;
        private int _trayTicks;
        private int _activeIndex = -1;
        private int _firstRunRetries;

        public MainForm(
            bool startHidden, EventWaitHandle showEvent, EventWaitHandle exitEvent)
        {
            _startHidden = startHidden;
            _showEvent = showEvent;
            _exitEvent = exitEvent;
            Text = "Zapret Control";
            ClientSize = new Size(900, 680);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Current.WindowBg;
            ForeColor = Theme.Current.TextPrimary;
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

            _themeButton = new ThemedButton();
            _themeButton.Text = Theme.IsDark ? "Светлая тема" : "Тёмная тема";
            _themeButton.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            _themeButton.SetBounds(ClientSize.Width - 190, 9, 110, 26);
            _themeButton.AccessibleName = "Переключить тему";
            _themeButton.Click += OnThemeClick;
            Theme.StyleButton(_themeButton);
            Controls.Add(_themeButton);

            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("ZapretControl.app.ico"))
            {
                if (iconStream != null)
                {
                    // The .ico has no 256 px frame; requesting one produces a
                    // broken Icon that throws in DrawIcon/ToBitmap. 32 px is
                    // enough for both the window and the tray.
                    _windowIcon = new Icon(iconStream, new Size(32, 32));
                    Icon = _windowIcon;
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
            TelegramTab telegramTab = (TelegramTab)_tabs[8];
            updatesTab.AppUpdateAvailable += OnAppUpdateAvailable;
            homeTab.CheckCompleted += delegate(HomeCheckReport report)
            {
                testsTab.ShowReport(report);
            };
            homeTab.DetailsRequested += delegate
            {
                ActivateTab(7);
            };
            homeTab.TelegramToggleRequested += delegate
            {
                telegramTab.ToggleFromExternal();
            };
            homeTab.TelegramSettingsRequested += delegate
            {
                ActivateTab(8);
            };
            telegramTab.ProxyStateChanged += delegate
            {
                UpdateHomeTelegram();
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

                Button button = new ThemedButton();
                button.Text = _tabs[i].TabTitle;
                button.Font = _tabFont;
                button.ForeColor = Theme.Current.TabInactiveText;
                button.BackColor = Color.Transparent;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Theme.Current.ButtonHover;
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
                button.Name = "tabButton";
                button.SetBounds(24 + i * 92, 50, 88, 36);
                button.Tag = i;
                button.Click += OnTabClick;
                button.AccessibleName = _tabs[i].TabTitle;
                Controls.Add(button);
                _tabButtons[i] = button;
            }

            _footer = new Label();
            _footer.Font = _footerFont;
            _footer.ForeColor = Theme.Current.TextSecondary;
            _footer.BackColor = Color.Transparent;
            _footer.Tag = "muted";
            _footer.TextAlign = ContentAlignment.MiddleLeft;
            _footer.SetBounds(24, 640, 480, 18);
            Controls.Add(_footer);

            Label version = new Label();
            version.Text = "Zapret Control " + AppInfo.Version + " · права администратора";
            version.Font = _footerFont;
            version.ForeColor = Theme.Current.TextSecondary;
            version.BackColor = Color.Transparent;
            version.Tag = "muted";
            version.TextAlign = ContentAlignment.MiddleRight;
            version.SetBounds(420, 640, 456, 18);
            Controls.Add(version);

            Activated += delegate { SetLightsInactive(false); };
            Deactivate += delegate { SetLightsInactive(true); };
            Shown += delegate
            {
                ActivateTab(0);
                RefreshActiveTab();
                UpdateHomeTelegram();
                telegramTab.StartIfConfigured();
                if (_startHidden)
                {
                    BeginInvoke(new Action(HideToTray));
                }
                BeginInvoke(new Action(CheckFirstRunAndUpdates));
                BeginInvoke(new Action(StartDataCheck));
            };
            FormClosing += OnFormClosing;
            SystemEvents.SessionEnding += OnSessionEnding;
            DataUpdater.Completed += OnDataUpdateCompleted;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 3000;
            _timer.Tick += OnTimerTick;
            _timer.Start();
            Theme.Apply(this);

            CreateTray();
            StartTrayListener();
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
                Theme.Current.WindowTop,
                Theme.Current.WindowBottom,
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
            using (Pen pen = new Pen(Theme.Current.Border, 1f))
            {
                g.DrawPath(pen, border);
            }

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                using (SolidBrush brush = new SolidBrush(Theme.Current.TitleText))
                {
                    g.DrawString(
                        "Zapret Control",
                        _titleFont,
                        brush,
                        new RectangleF(0f, 0f, ClientSize.Width, TitleBarHeight * scale),
                        format);
                }
            }

            using (Pen divider = new Pen(Theme.Current.Divider, 1f))
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

        // Clicking the taskbar button sends SC_MINIMIZE / SC_RESTORE through
        // the system menu. A borderless window ignores both unless it is
        // minimise-capable and the commands are handled here, which is what
        // makes the panel icon collapse and restore the window.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND)
            {
                int command = m.WParam.ToInt32() & 0xFFF0;
                if (command == SC_MINIMIZE)
                {
                    WindowState = FormWindowState.Minimized;
                    return;
                }
                if (command == SC_RESTORE)
                {
                    WindowState = FormWindowState.Normal;
                    Activate();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void OnThemeClick(object sender, EventArgs e)
        {
            SettingsData settings = Settings.Load();
            settings.DarkTheme = !settings.DarkTheme;
            Settings.Save();
            if (MessageBox.Show(
                    this,
                    "Тема изменится после перезапуска. Перезапустить сейчас?",
                    "Тема",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                RestartElevated();
            }
        }

        private void RestartElevated()
        {
            if (OperationGuard.IsBusy)
            {
                MessageBox.Show(
                    this,
                    "Дождитесь завершения текущей операции.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.WorkingDirectory = AppPaths.InstallDirectory;
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Не удалось перезапустить приложение: " + ex.Message,
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _exitRequested = true;
            Close();
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
                light.Click += delegate { HideToTray(); };
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
                _tabButtons[_activeIndex].ForeColor = Theme.Current.TabInactiveText;
                _tabButtons[_activeIndex].BackColor = Color.Transparent;
            }
            _activeIndex = index;
            _tabs[index].Visible = true;
            _tabs[index].BringToFront();
            _tabButtons[index].Font = _tabActiveFont;
            _tabButtons[index].ForeColor = Theme.Current.TextPrimary;
            _tabButtons[index].BackColor = Theme.Current.TabActiveBg;
            _tabs[index].ActivateTab();
            if (index == 0)
            {
                UpdateHomeTelegram();
            }
            _tabs[index].RefreshData();
            _footer.Text = "Обновлено " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_pendingExit && !OperationGuard.IsBusy)
            {
                _pendingExit = false;
                ExitFromTray();
                return;
            }
            // Hidden tabs must not repaint or hit the service every 3 seconds;
            // only the visible tab refreshes. The tray polls the service every
            // 12 s while hidden and every tick while the window is open.
            if (Visible)
            {
                RefreshActiveTab();
            }
            _trayTicks++;
            if (Visible || _trayTicks % 4 == 0)
            {
                UpdateTrayState();
            }
            if (DateTime.UtcNow - _lastDataCheck >= TimeSpan.FromHours(6))
            {
                StartDataCheck();
            }
            if (DateTime.UtcNow - _lastReleaseCheck >= TimeSpan.FromHours(1))
            {
                _lastReleaseCheck = DateTime.UtcNow;
                // CheckIfDue keeps its own six-hour gate and does nothing while
                // an operation is running.
                UpdatesTab updates = _tabs[5] as UpdatesTab;
                if (updates != null)
                {
                    updates.CheckIfDue();
                }
            }
        }

        private void StartDataCheck()
        {
            if (_dataCheckRunning || IsDisposed || Disposing)
            {
                return;
            }
            _dataCheckRunning = true;
            _lastDataCheck = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    DataUpdater.Check(true);
                }
                catch (Exception ex)
                {
                    DataUpdater.Log("unhandled: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    _dataCheckRunning = false;
                }
            });
        }

        private void OnDataUpdateCompleted(DataUpdateResult result)
        {
            if (IsDisposed || Disposing || result == null)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<DataUpdateResult>(OnDataUpdateCompleted), result);
                }
                catch (Exception)
                {
                }
                return;
            }
            if (result.Ok && result.Changed)
            {
                Notify("Данные Telegram обновлены",
                    "Фронтов: " + result.Fronts + ", данные seq " + result.Seq + ".");
                if (Visible)
                {
                    RefreshActiveTab();
                }
            }
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
                    System.Windows.Forms.Timer retry = new System.Windows.Forms.Timer();
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

        private void UpdateHomeTelegram()
        {
            HomeTab home = _tabs[0] as HomeTab;
            TelegramTab telegram = _tabs[8] as TelegramTab;
            if (home == null || telegram == null)
            {
                return;
            }
            home.SetTelegramProxyState(
                telegram.ProxyIsRunning,
                telegram.ProxyStatusShort,
                false,
                telegram.ProxyStartError);
        }

        private void OnAppUpdateAvailable(UpdateCheckResult result)
        {
            if (IsDisposed || Disposing || result == null)
            {
                return;
            }
            string text = "Доступна новая версия Zapret Control " +
                result.LatestAppVersion + " (установлена " + AppInfo.Version + ").";
            if (_tray != null)
            {
                _balloonClick = delegate
                {
                    ShowFromTray();
                    ActivateTab(5);
                };
                Notify("Обновление Zapret Control", text);
                return;
            }
            if (MessageBox.Show(
                    this,
                    text + "\nОткрыть вкладку «Обновления»?",
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
            bool forced = e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing ||
                e.CloseReason == CloseReason.ApplicationExitCall;
            if (OperationGuard.IsBusy && !forced)
            {
                e.Cancel = true;
                _exitRequested = false;
                Notify("Дождитесь завершения операции",
                    "Программа закроется после её окончания.");
                if (Visible)
                {
                    MessageBox.Show(
                        this,
                        "Дождитесь завершения текущей операции.",
                        "Zapret Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            bool userClosing = e.CloseReason == CloseReason.UserClosing;
            SettingsData settings = Settings.Load();
            if (!_exitRequested && userClosing && settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (!_exitRequested && userClosing && !settings.CloseToTray)
            {
                TelegramTab confirmation = _tabs[8] as TelegramTab;
                if (confirmation != null && confirmation.ProxyIsRunning &&
                    MessageBox.Show(
                        this,
                        "Прокси Telegram будет остановлен. Закрыть программу?",
                        "Завершение работы",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _exitRequested = true;
            _timer.Stop();
            StopTrayListener();
            TelegramTab telegram = _tabs[8] as TelegramTab;
            if (telegram != null)
            {
                telegram.StopProxy();
            }
            DisposeTray();
        }

        private void CreateTray()
        {
            try
            {
                _trayOnIcon = TrayIcons.Create(
                    _windowIcon, Color.FromArgb(53, 199, 89));
                _trayOffIcon = TrayIcons.Create(
                    _windowIcon, Color.FromArgb(142, 142, 147));

                _trayMenu = new ContextMenuStrip();
                _trayMenu.ShowImageMargin = false;
                _trayMenu.ShowCheckMargin = false;
                _trayMenu.BackColor = Theme.Current.CardBg;
                _trayMenu.ForeColor = Theme.Current.TextPrimary;
                _trayMenu.Opening += OnTrayMenuOpening;

                ToolStripMenuItem open = new ToolStripMenuItem("Открыть окно");
                open.Click += delegate { ShowFromTray(); };
                StyleTrayItem(open);

                _trayProtectItem = new ToolStripMenuItem("Выключить защиту");
                _trayProtectItem.Click += OnTrayProtect;
                StyleTrayItem(_trayProtectItem);

                _trayTelegramItem = new ToolStripMenuItem("Включить прокси Telegram");
                _trayTelegramItem.Click += OnTrayTelegram;
                StyleTrayItem(_trayTelegramItem);

                ToolStripMenuItem check = new ToolStripMenuItem(
                    "Проверить обновления");
                check.Click += OnTrayCheck;
                StyleTrayItem(check);

                ToolStripMenuItem where = new ToolStripMenuItem(
                    "Где значок в трее…");
                where.Click += delegate { OpenTaskbarSettings(); };
                StyleTrayItem(where);

                ToolStripMenuItem exit = new ToolStripMenuItem("Выйти полностью");
                exit.Click += delegate { ExitFromTray(); };
                StyleTrayItem(exit);

                _trayMenu.Items.Add(open);
                _trayMenu.Items.Add(new ToolStripSeparator());
                _trayMenu.Items.Add(_trayProtectItem);
                _trayMenu.Items.Add(_trayTelegramItem);
                _trayMenu.Items.Add(check);
                _trayMenu.Items.Add(where);
                _trayMenu.Items.Add(new ToolStripSeparator());
                _trayMenu.Items.Add(exit);

                _tray = new NotifyIcon();
                _tray.Icon = _trayOffIcon;
                _tray.Text = "Zapret Control";
                LogTray("create: on=" + (_trayOnIcon != null) +
                    " off=" + (_trayOffIcon != null));
                _tray.ContextMenuStrip = _trayMenu;
                _tray.Visible = true;
                _tray.DoubleClick += delegate { ShowFromTray(); };
                _tray.MouseClick += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        ShowFromTray();
                    }
                };
                _tray.BalloonTipClicked += delegate
                {
                    Action action = _balloonClick;
                    _balloonClick = null;
                    if (action != null)
                    {
                        action();
                    }
                };
                UpdateTrayState();
                PromoteTrayIcon();
                LogTray("created visible=" + _tray.Visible +
                    " text=" + _tray.Text);
            }
            catch (Exception ex)
            {
                // No notification area (server core, locked shell) must not
                // prevent the main window from working.
                LogTray("create failed: " + ex);
                DisposeTray();
            }
        }

        private static void LogTray(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "tray.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message +
                    Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }

        // Windows 11 defaults every new tray icon to the overflow area. The
        // shell stores the user's choice as IsPromoted in NotifyIconSettings;
        // set it only when the value is absent, so an explicit "hide" wins.
        private void PromoteTrayIcon()
        {
            if (_trayPromoted || _trayPromoteTries >= 10)
            {
                return;
            }
            _trayPromoteTries++;
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\NotifyIconSettings", true))
                {
                    if (root == null)
                    {
                        return;
                    }
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        using (RegistryKey key = root.OpenSubKey(names[i], true))
                        {
                            if (key == null)
                            {
                                continue;
                            }
                            string path = key.GetValue("ExecutablePath") as string;
                            if (string.IsNullOrEmpty(path) ||
                                path.IndexOf("ZapretControl",
                                    StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            _trayPromoted = true;
                            if (key.GetValue("IsPromoted") == null)
                            {
                                key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                                LogTray("promoted " + names[i]);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTray("promote failed: " + ex.Message);
            }
        }

        private void OpenTaskbarSettings()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "ms-settings:taskbar";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception)
            {
                Notify("Настройки панели задач",
                    "Параметры → Персонализация → Панель задач → Другие значки.");
            }
        }

        private static void StyleTrayItem(ToolStripMenuItem item)
        {
            item.BackColor = Theme.Current.CardBg;
            item.ForeColor = Theme.Current.TextPrimary;
        }

        private void StartTrayListener()
        {
            if (_showEvent == null || _exitEvent == null)
            {
                return;
            }
            _trayListener = new Thread(TrayListenerLoop);
            _trayListener.IsBackground = true;
            _trayListener.Name = "ZapretControl.TraySignals";
            _trayListener.Start();
        }

        private void TrayListenerLoop()
        {
            WaitHandle[] handles = new WaitHandle[] { _showEvent, _exitEvent };
            while (!_trayListenerStop)
            {
                int index;
                try
                {
                    index = WaitHandle.WaitAny(handles, 1000);
                }
                catch (Exception)
                {
                    return;
                }
                if (index == WaitHandle.WaitTimeout || _trayListenerStop)
                {
                    continue;
                }
                try
                {
                    if (index == 0)
                    {
                        BeginInvoke(new Action(ShowFromTray));
                    }
                    else
                    {
                        BeginInvoke(new Action(ExitFromTray));
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        private void StopTrayListener()
        {
            _trayListenerStop = true;
            Thread thread = _trayListener;
            _trayListener = null;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(1500);
                }
                catch (Exception)
                {
                }
            }
        }

        private void ShowFromTray()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            ShowInTaskbar = true;
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }

        private void HideToTray()
        {
            if (IsDisposed || Disposing || !Visible)
            {
                return;
            }
            if (_tray == null || !_tray.Visible)
            {
                // Without a working notification icon hiding the window would
                // leave no way back. Fall back to a normal minimised window.
                WindowState = FormWindowState.Minimized;
                LogTray("hide fallback: tray unavailable");
                return;
            }
            Hide();
            ShowInTaskbar = false;
            LogTray("hidden, proxy=" + ProxyRunning());
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                // One-time onboarding hint even when ordinary balloons are
                // off: Windows 11 hides new icons in the overflow area.
                try
                {
                    _tray.ShowBalloonTip(6000, "Zapret Control свёрнут в трей",
                        "Прокси продолжает работать. Если значка не видно, " +
                        "включите его в «Другие значки» панели задач.",
                        ToolTipIcon.Info);
                }
                catch (Exception)
                {
                }
            }
        }

        private bool ProxyRunning()
        {
            TelegramTab telegram = _tabs[8] as TelegramTab;
            return telegram != null && telegram.ProxyIsRunning;
        }

        private void ExitFromTray()
        {
            if (OperationGuard.IsBusy)
            {
                // Setup waits only 15 s; remember the request and finish as
                // soon as the current operation releases the guard.
                _pendingExit = true;
                Notify("Дождитесь завершения операции",
                    "Программа закроется после её окончания.");
                return;
            }
            _pendingExit = false;
            _exitRequested = true;
            Close();
        }

        private void OnTrayProtect(object sender, EventArgs e)
        {
            if (OperationGuard.IsBusy)
            {
                Notify("Дождитесь завершения операции", "Защита не изменена.");
                return;
            }
            HomeTab home = _tabs[0] as HomeTab;
            if (home != null)
            {
                home.ToggleProtectionFromTray();
            }
        }

        private void OnTrayTelegram(object sender, EventArgs e)
        {
            TelegramTab telegram = _tabs[8] as TelegramTab;
            if (telegram == null)
            {
                return;
            }
            telegram.ToggleFromTray();
            Notify("Прокси Telegram",
                telegram.ProxyIsRunning ? "Включён." : "Выключен.");
        }

        private void OnTrayCheck(object sender, EventArgs e)
        {
            UpdatesTab updates = _tabs[5] as UpdatesTab;
            if (updates == null)
            {
                return;
            }
            Notify("Zapret Control", "Проверяю обновления…");
            updates.CheckFromTray();
        }

        private void OnTrayMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdateTrayState();
            bool busy = OperationGuard.IsBusy;
            bool running = false;
            bool canToggle = false;
            try
            {
                ZapretStatus status = ZapretService.GetStatus();
                running = status.State == ZapretState.Running;
                canToggle = running || status.State == ZapretState.Stopped;
            }
            catch (Exception)
            {
            }
            _trayProtectItem.Text = running ? "Выключить защиту" : "Включить защиту";
            _trayProtectItem.Enabled = !busy && canToggle;
            TelegramTab telegram = _tabs[8] as TelegramTab;
            bool proxy = telegram != null && telegram.ProxyIsRunning;
            _trayTelegramItem.Text = proxy
                ? "Выключить прокси Telegram"
                : "Включить прокси Telegram";
            _trayTelegramItem.Enabled = !busy;
        }

        private void UpdateTrayState()
        {
            if (_tray == null || IsDisposed || Disposing)
            {
                return;
            }
            bool running = false;
            try
            {
                running = ZapretService.GetStatus().State == ZapretState.Running;
            }
            catch (Exception)
            {
            }
            TelegramTab telegram = _tabs[8] as TelegramTab;
            bool proxy = telegram != null && telegram.ProxyIsRunning;
            Icon icon = running ? _trayOnIcon : _trayOffIcon;
            if (icon != null)
            {
                _tray.Icon = icon;
            }
            string text = "Zapret Control: защита " +
                (running ? "включена" : "выключена") +
                ", Telegram " + (proxy ? "включён" : "выключен");
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }
            _tray.Text = text;
            PromoteTrayIcon();
        }

        private void Notify(string title, string text)
        {
            if (_tray == null || !_tray.Visible || IsDisposed || Disposing)
            {
                return;
            }
            if (!Settings.Load().TrayBalloons)
            {
                return;
            }
            try
            {
                _tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
            }
            catch (Exception)
            {
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            _exitRequested = true;
            _timer.Stop();
            TelegramTab telegram = _tabs[8] as TelegramTab;
            if (telegram != null)
            {
                telegram.StopProxy();
            }
        }

        private void DisposeTray()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            if (_trayOnIcon != null)
            {
                _trayOnIcon.Dispose();
                _trayOnIcon = null;
            }
            if (_trayOffIcon != null)
            {
                _trayOffIcon.Dispose();
                _trayOffIcon = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.SessionEnding -= OnSessionEnding;
                DataUpdater.Completed -= OnDataUpdateCompleted;
                StopTrayListener();
                DisposeTray();
                _timer.Dispose();
                _titleFont.Dispose();
                _tabFont.Dispose();
                _tabActiveFont.Dispose();
                _footerFont.Dispose();
                if (_windowIcon != null)
                {
                    _windowIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}

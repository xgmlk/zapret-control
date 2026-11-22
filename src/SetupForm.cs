using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    internal sealed class SetupForm : Form
    {
        private readonly SetupOptions _options;
        private readonly string _mode;
        private ComboBox _strategy;
        private CheckBox _autoPick;
        private CheckBox _service;
        private CheckBox _task;
        private CheckBox _shortcuts;
        private CheckBox _launch;
        private CheckBox _keepData;
        private TextBox _log;
        private Button _start;
        private Button _cancel;
        private ProgressBar _progress;
        private Label _status;
        private bool _running;
        private bool _finished;

        public SetupForm(SetupOptions options, string mode)
        {
            _options = options ?? new SetupOptions();
            _mode = mode ?? "install";
            Text = "Zapret Control — " + ModeTitle(_mode);
            ClientSize = new Size(600, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Current.WindowBg;
            ForeColor = Theme.Current.TextPrimary;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            using (System.IO.Stream iconStream = typeof(SetupForm).Assembly
                .GetManifestResourceStream(SetupEngine.IconResource))
            {
                if (iconStream != null)
                {
                    Icon = new Icon(iconStream, new Size(64, 64));
                }
            }

            Label title = new Label();
            title.Text = "Zapret Control";
            title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.SetBounds(24, 18, 540, 32);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = ModeTitle(_mode) + " · версия " + AppInfo.Version +
                            " · " + AppPaths.TargetInstallDirectory;
            subtitle.ForeColor = Theme.Current.TextSecondary;
            subtitle.BackColor = Color.Transparent;
            subtitle.Tag = "muted";
            subtitle.SetBounds(26, 52, 550, 20);
            Controls.Add(subtitle);

            int top = 86;
            if (_mode != "uninstall" && _mode != "rollback")
            {
                Label strategyLabel = new Label();
                strategyLabel.Text = "Стратегия zapret:";
                strategyLabel.SetBounds(26, top + 3, 150, 20);
                Controls.Add(strategyLabel);

                _strategy = new ComboBox();
                _strategy.DropDownStyle = ComboBoxStyle.DropDownList;
                _strategy.SetBounds(180, top, 390, 24);
                Theme.StyleCombo(_strategy);
                Controls.Add(_strategy);
                PopulateStrategies();
                top += 36;

                _autoPick = CreateCheck(
                    "Подобрать рабочую стратегию после установки (1–3 минуты)", top);
                _strategy.SelectedIndexChanged += OnStrategySelectionChanged;
                top += 26;
            }

            _service = CreateCheck("Установить и запустить службу zapret", top);
            top += 26;
            _task = CreateCheck("Задача Zapret-IfIndex-Sync (синхронизация интерфейса)", top);
            top += 26;
            _shortcuts = CreateCheck("Ярлыки в меню «Пуск» и на рабочем столе", top);
            top += 26;
            _launch = CreateCheck("Запустить Zapret Control после установки", top);
            top += 26;
            _keepData = CreateCheck("Сохранить пользовательские списки и настройки", top);
            top += 32;

            _service.Checked = _options.InstallService;
            _task.Checked = _options.RegisterTask;
            _shortcuts.Checked = _options.CreateShortcuts;
            _launch.Checked = !_options.NoLaunch && !_options.Silent;
            _keepData.Checked = _options.KeepData;
            if (_autoPick != null)
            {
                // A fresh interactive install offers the deep check by default;
                // updates keep the user's existing choice, and silent installs
                // ask at the first manual GUI start instead.
                _autoPick.Checked = _mode == "install" &&
                                    string.IsNullOrEmpty(_options.Strategy);
                OnStrategySelectionChanged(null, EventArgs.Empty);
            }
            bool uninstall = _mode == "uninstall";
            bool serviceOptions = !uninstall && _mode != "rollback";
            _service.Visible = serviceOptions;
            _task.Visible = serviceOptions;
            _shortcuts.Visible = serviceOptions;
            _launch.Visible = !uninstall && _mode != "rollback";
            _keepData.Visible = uninstall;
            if (_autoPick != null)
            {
                _autoPick.Visible = serviceOptions;
            }

            Label logLabel = new Label();
            logLabel.Text = "Журнал:";
            logLabel.SetBounds(26, top, 100, 20);
            Controls.Add(logLabel);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Theme.Current.InputReadOnlyBg;
            _log.ForeColor = Theme.Current.TextPrimary;
            _log.SetBounds(26, top + 22, 548, 128);
            Controls.Add(_log);

            _progress = new ProgressBar();
            _progress.SetBounds(26, 438, 548, 6);
            _progress.Style = ProgressBarStyle.Continuous;
            Controls.Add(_progress);

            _status = new Label();
            _status.Text = "Готово к установке.";
            _status.ForeColor = Theme.Current.TextSecondary;
            _status.BackColor = Color.Transparent;
            _status.Tag = "muted";
            _status.SetBounds(26, 450, 360, 20);
            Controls.Add(_status);

            _start = new ThemedButton();
            _start.Text = ModeAction(_mode);
            _start.SetBounds(376, 474, 96, 30);
            _start.Click += OnStart;
            Theme.StyleButton(_start);
            Controls.Add(_start);

            _cancel = new ThemedButton();
            _cancel.Text = "Отмена";
            _cancel.SetBounds(478, 474, 96, 30);
            _cancel.Click += delegate
            {
                if (!_running)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Theme.StyleButton(_cancel);
            Controls.Add(_cancel);

            FormClosing += OnFormClosing;
            AcceptButton = _start;
            CancelButton = _cancel;
            Theme.Apply(this);
        }

        private CheckBox CreateCheck(string text, int top)
        {
            CheckBox check = new ThemedCheckBox();
            check.Text = text;
            check.SetBounds(26, top, 548, 22);
            Theme.StyleCheck(check);
            Controls.Add(check);
            return check;
        }

        private void OnStrategySelectionChanged(object sender, EventArgs e)
        {
            if (_autoPick == null || _running)
            {
                return;
            }
            bool automatic = !(_strategy.SelectedItem is StrategyInfo);
            _autoPick.Enabled = automatic;
            if (!automatic)
            {
                _autoPick.Checked = false;
            }
        }

        private void PopulateStrategies()
        {
            _strategy.Items.Add("Автоматически (general.bat или подбор)");
            string root = AppPaths.TargetPayloadDirectory;
            if (!AppPaths.IsPayloadRoot(root))
            {
                root = AppPaths.GetServicePayloadRoot();
            }
            if (!AppPaths.IsPayloadRoot(root))
            {
                root = AppPaths.ManagedPayloadDirectory;
            }
            string selected = _options.Strategy;
            if (string.IsNullOrEmpty(selected))
            {
                selected = ServiceManager.GetCurrentStrategyName();
            }
            int selectedIndex = 0;
            foreach (StrategyInfo info in StrategyParser.ListStrategies(root))
            {
                int index = _strategy.Items.Add(info);
                if (!string.IsNullOrEmpty(selected) &&
                    info.Name.Equals(selected, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = index;
                }
            }
            _strategy.SelectedIndex = selectedIndex;
        }

        private void OnStart(object sender, EventArgs e)
        {
            if (_running)
            {
                return;
            }
            if (_mode == "uninstall")
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "Удалить Zapret Control, службу zapret и задачу синхронизации?",
                    "Zapret Control",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            _running = true;
            _start.Enabled = false;
            _cancel.Enabled = false;
            if (_strategy != null)
            {
                _strategy.Enabled = false;
            }
            _service.Enabled = false;
            _task.Enabled = false;
            _shortcuts.Enabled = false;
            _launch.Enabled = false;
            _keepData.Enabled = false;
            if (_autoPick != null)
            {
                _autoPick.Enabled = false;
            }
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _status.Text = "Выполняется...";
            _log.Clear();

            if (_strategy != null && _strategy.SelectedItem is StrategyInfo)
            {
                _options.Strategy = ((StrategyInfo)_strategy.SelectedItem).Name;
            }
            else
            {
                _options.Strategy = string.Empty;
            }
            _options.InstallService = _service.Checked;
            _options.RegisterTask = _task.Checked;
            _options.CreateShortcuts = _shortcuts.Checked;
            _options.NoLaunch = !_launch.Checked;
            _options.KeepData = _keepData.Checked;
            _options.AutoPickStrategy = _autoPick != null && _autoPick.Checked &&
                !(_strategy != null && _strategy.SelectedItem is StrategyInfo);
            _options.Silent = false;

            Task.Run(delegate
            {
                try
                {
                    Action<string> log = delegate(string message)
                    {
                        Report(message);
                    };
                    if (_mode == "uninstall")
                    {
                        SetupEngine.Uninstall(_options, log);
                    }
                    else if (_mode == "rollback")
                    {
                        SetupEngine.Rollback(_options, log);
                    }
                    else if (_mode == "update")
                    {
                        SetupEngine.Update(_options, log);
                    }
                    else if (_mode == "repair")
                    {
                        SetupEngine.Repair(_options, log);
                    }
                    else
                    {
                        SetupEngine.Install(_options, log);
                    }
                    Report("Готово.");
                    Finish(true, null);
                }
                catch (Exception ex)
                {
                    Finish(false, ex.Message);
                }
            });
        }

        private void Report(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(Report), message);
                }
                catch (Exception)
                {
                }
                return;
            }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message +
                            Environment.NewLine);
            _status.Text = message.Length > 120 ? message.Substring(0, 120) : message;
        }

        private void Finish(bool success, string error)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<bool, string>(Finish), success, error);
                }
                catch (Exception)
                {
                }
                return;
            }

            _running = false;
            _finished = success;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Style = ProgressBarStyle.Continuous;
            if (success)
            {
                _status.Text = "Готово.";
                _start.Enabled = true;
                _cancel.Enabled = true;
                _cancel.Text = "Закрыть";
                MessageBox.Show(
                    this,
                    ModeAction(_mode) + " завершено.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _status.Text = "Ошибка.";
                _start.Enabled = true;
                _cancel.Enabled = true;
                _service.Enabled = true;
                _task.Enabled = true;
                _shortcuts.Enabled = true;
                _launch.Enabled = true;
                _keepData.Enabled = true;
                if (_autoPick != null)
                {
                    _autoPick.Enabled = true;
                }
                if (_strategy != null)
                {
                    _strategy.Enabled = true;
                }
                MessageBox.Show(
                    this,
                    error,
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Дождитесь завершения операции.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (_finished)
            {
                DialogResult = DialogResult.OK;
            }
        }

        private static string ModeTitle(string mode)
        {
            if (mode == "update")
            {
                return "обновление";
            }
            if (mode == "uninstall")
            {
                return "удаление";
            }
            if (mode == "repair")
            {
                return "восстановление";
            }
            if (mode == "rollback")
            {
                return "откат";
            }
            return "установка";
        }

        private static string ModeAction(string mode)
        {
            if (mode == "update")
            {
                return "Обновить";
            }
            if (mode == "uninstall")
            {
                return "Удалить";
            }
            if (mode == "repair")
            {
                return "Восстановить";
            }
            if (mode == "rollback")
            {
                return "Откатить";
            }
            return "Установить";
        }
    }
}

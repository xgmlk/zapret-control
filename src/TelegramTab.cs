using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZapretControl
{
    public sealed class TelegramTab : TabView
    {
        private readonly TgProxyService _proxy = new TgProxyService();
        private TextBox _port;
        private TextBox _secret;
        private TextBox _link;
        private TextBox _journal;
        private Label _status;
        private Label _coverageHint;
        private Button _toggle;
        private Button _open;
        private Button _copy;
        private Button _check;
        private Button _bridge;
        private CheckBox _autostart;
        private CheckBox _coverage;
        private bool _busy;
        private bool _ready;

        public override string TabTitle
        {
            get { return "Telegram"; }
        }

        public TelegramTab()
        {
            SettingsData settings = Settings.Load();

            CardPanel proxyCard = AddCard("ПРОКСИ TELEGRAM", 0, 0, 852, 240);
            AddMutedLabel(proxyCard,
                "Локальный MTProto-прокси: Telegram подключается к 127.0.0.1, " +
                "а трафик идёт к дата-центрам через WebSocket-TLS и " +
                "Cloudflare. Работает без прав администратора и обходит " +
                "блокировку по IP, которую не исправляет zapret.",
                20, 34, 812, 42);

            _status = AddMutedLabel(proxyCard, "Выключен.", 20, 80, 812, 30);
            _status.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            _toggle = AddButton(proxyCard, "Включить прокси", 20, 116, 150, 32,
                OnToggle);
            AddLabel(proxyCard, "Порт", 184, 123, 40, 20);
            _port = AddTextBox(proxyCard, 226, 118, 58, 24, false);
            _port.Text = settings.TgProxyPort.ToString();
            _port.Leave += OnPortLeave;

            AddLabel(proxyCard, "Секрет", 296, 123, 50, 20);
            _secret = AddTextBox(proxyCard, 350, 118, 184, 24, true);

            AddLabel(proxyCard, "Ссылка", 546, 123, 48, 20);
            _link = AddTextBox(proxyCard, 598, 118, 234, 24, true);

            _open = AddButton(proxyCard, "Открыть в Telegram", 20, 158, 150, 32,
                OnOpen);
            _copy = AddButton(proxyCard, "Скопировать ссылку", 182, 158, 150, 32,
                OnCopy);
            _check = AddButton(proxyCard, "Проверить соединение", 344, 158,
                170, 32, OnCheck);
            _bridge = AddButton(proxyCard, "Тест моста", 526, 158, 120, 32,
                OnBridgeTest);

            _autostart = new ThemedCheckBox();
            _autostart.Text = "Автозапуск";
            _autostart.SetBounds(20, 202, 110, 22);
            _autostart.ForeColor = TextColor;
            _autostart.BackColor = Color.Transparent;
            _autostart.Checked = settings.TgProxyAutostart;
            _autostart.CheckedChanged += OnAutostartChanged;
            Theme.StyleCheck(_autostart);
            proxyCard.Controls.Add(_autostart);

            _coverage = new ThemedCheckBox();
            _coverage.Text = "Покрыть zapret";
            _coverage.SetBounds(138, 202, 130, 22);
            _coverage.ForeColor = TextColor;
            _coverage.BackColor = Color.Transparent;
            _coverage.Checked = settings.TgProxyZapret;
            _coverage.CheckedChanged += OnCoverageChanged;
            Theme.StyleCheck(_coverage);
            proxyCard.Controls.Add(_coverage);

            _coverageHint = AddMutedLabel(proxyCard,
                "домены прокси добавляются в пользовательский список; " +
                "служба zapret перезапускается", 276, 204, 552, 20);

            CardPanel journalCard = AddCard("ЖУРНАЛ", 0, 250, 852, 270);
            _journal = AddTextBox(journalCard, 20, 34, 812, 222, true);
            _journal.Font = new Font("Consolas", 8.5F);

            _proxy.LogLine += OnProxyLog;
            _proxy.StateChanged += OnProxyStateChanged;
            _proxy.Configure(settings.TgProxyPort, settings.TgProxySecret);

            _secret.Text = "dd" + _proxy.SecretHex;
            _link.Text = _proxy.ProxyLink;
            _ready = true;
            UpdateState();
        }

        // Called from MainForm.Shown: starts the proxy when the user opted in.
        public void StartIfConfigured()
        {
            SettingsData settings = Settings.Load();
            if (!settings.TgProxyAutostart || _proxy.IsRunning)
            {
                return;
            }
            StartProxy(false);
        }

        public void StopProxy()
        {
            if (_proxy.IsRunning)
            {
                _proxy.Stop();
            }
        }

        public bool ProxyIsRunning
        {
            get { return _proxy.IsRunning; }
        }

        public string ProxyStartError
        {
            get { return _proxy.StartError; }
        }

        public string ProxyStatusShort
        {
            get
            {
                return _proxy.IsRunning
                    ? "Включён · " + _proxy.Host + ":" + _proxy.Port
                    : "Выключен";
            }
        }

        public event Action ProxyStateChanged;

        // Home tab toggle: same action as the local button, no settings.
        public void ToggleFromExternal()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (_proxy.IsRunning)
            {
                _proxy.Stop();
            }
            else
            {
                StartProxy(true);
            }
        }

        public override void ActivateTab()
        {
            UpdateState();
        }

        public override void RefreshData()
        {
            UpdateState();
        }

        private void OnToggle(object sender, EventArgs e)
        {
            if (_proxy.IsRunning)
            {
                _proxy.Stop();
            }
            else
            {
                StartProxy(true);
            }
        }

        // Starts the listener. openTelegram is true only for a user-initiated
        // toggle (home or this tab), not for autostart or a port restart: the
        // request is that enabling the proxy immediately shows Telegram's
        // proxy-connection dialog.
        private void StartProxy(bool openTelegram)
        {
            SettingsData settings = Settings.Load();
            ApplyCoverage(settings.TgProxyZapret);
            if (!_proxy.Start())
            {
                UpdateState();
                return;
            }
            if (openTelegram)
            {
                OpenInTelegram(true);
            }
        }

        private void OnPortLeave(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) ||
                port < 1 || port > 65535)
            {
                _port.Text = _proxy.Port.ToString();
                return;
            }
            if (port == _proxy.Port)
            {
                return;
            }
            SettingsData settings = Settings.Load();
            settings.TgProxyPort = port;
            Settings.Save();
            bool wasRunning = _proxy.IsRunning;
            if (wasRunning)
            {
                _proxy.Stop();
            }
            _proxy.Configure(port, settings.TgProxySecret);
            if (wasRunning)
            {
                StartProxy(false);
            }
            UpdateState();
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            if (!_ready)
            {
                return;
            }
            SettingsData settings = Settings.Load();
            settings.TgProxyAutostart = _autostart.Checked;
            Settings.Save();
        }

        private void OnCoverageChanged(object sender, EventArgs e)
        {
            if (!_ready)
            {
                return;
            }
            SettingsData settings = Settings.Load();
            settings.TgProxyZapret = _coverage.Checked;
            Settings.Save();
            ApplyCoverage(_coverage.Checked);
        }

        private void ApplyCoverage(bool enabled)
        {
            string root = AppPaths.ResolvePayloadRoot();
            TelegramCoverageResult result = TelegramCoverage.Apply(root, enabled);
            if (!result.Ok)
            {
                AppendLog("покрытие zapret: " + result.Error);
                return;
            }
            if (!result.Changed)
            {
                return;
            }
            AppendLog("покрытие zapret: " +
                (enabled ? "добавлено " : "удалено ") +
                result.Lines + " домен(ов) в " +
                Path.GetFileName(result.Path));
            if (ZapretService.GetStatus().State == ZapretState.Running)
            {
                bool restarted = ZapretService.RestartIfRunning();
                AppendLog(restarted
                    ? "служба zapret перезапущена"
                    : "службу zapret перезапустите вручную");
            }
        }

        private void OnOpen(object sender, EventArgs e)
        {
            OpenInTelegram(false);
        }

        private void OpenInTelegram(bool automatic)
        {
            if (automatic && !IsTelegramLinkRegistered())
            {
                AppendLog("Telegram не найден — включите прокси вручную по " +
                    "ссылке на вкладке «Telegram»");
                return;
            }
            if (_proxy.IsRunning)
            {
                AppendLog("открываю Telegram со ссылкой на прокси");
            }
            try
            {
                Process.Start(_proxy.ProxyLink);
            }
            catch (Exception ex)
            {
                AppendLog("не удалось открыть Telegram: " + ex.Message);
                if (!automatic)
                {
                    ShowInfo("Ссылка скопирована. Откройте её в Telegram вручную.");
                    CopyLink();
                }
            }
        }

        private static bool IsTelegramLinkRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey("tg", false))
                {
                    return key != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnCopy(object sender, EventArgs e)
        {
            CopyLink();
        }

        private void CopyLink()
        {
            try
            {
                Clipboard.SetText(_proxy.ProxyLink);
                AppendLog("ссылка скопирована");
            }
            catch (Exception)
            {
            }
        }

        private async void OnCheck(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            _check.Enabled = false;
            SetState(_status, "Проверяю соединение с Telegram…", AmberColor);
            try
            {
                string verdict = await _proxy.SelfTestAsync(CancellationToken.None);
                SetState(_status, verdict,
                    verdict.StartsWith("РАБОТАЕТ", StringComparison.Ordinal)
                        ? GreenColor
                        : RedColor);
            }
            catch (Exception ex)
            {
                SetState(_status, "Проверка не удалась: " + ShortError(ex),
                    RedColor);
            }
            finally
            {
                _busy = false;
                _check.Enabled = true;
            }
        }

        private async void OnBridgeTest(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!_proxy.IsRunning)
            {
                ShowInfo("Сначала включите прокси.");
                return;
            }
            _busy = true;
            _bridge.Enabled = false;
            SetState(_status, "Проверяю мост через Telegram…", AmberColor);
            try
            {
                string verdict = await _proxy.BridgeSelfTestAsync(
                    2, CancellationToken.None);
                SetState(_status, verdict,
                    verdict.IndexOf("МОСТ OK", StringComparison.Ordinal) >= 0
                        ? GreenColor
                        : RedColor);
            }
            catch (Exception ex)
            {
                SetState(_status, "Тест моста не удался: " + ShortError(ex),
                    RedColor);
            }
            finally
            {
                _busy = false;
                _bridge.Enabled = true;
            }
        }

        private void OnProxyLog(string message)
        {
            AppendLog(message);
        }

        private void OnProxyStateChanged()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(UpdateState));
                    return;
                }
                UpdateState();
            }
            catch (Exception)
            {
            }
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || Disposing || _journal == null)
            {
                return;
            }
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(AppendLog), message);
                    return;
                }
                string line = DateTime.Now.ToString("HH:mm:ss") + "  " +
                    message + Environment.NewLine;
                if (_journal.TextLength > 60000)
                {
                    _journal.Clear();
                }
                _journal.AppendText(line);
            }
            catch (Exception)
            {
            }
        }

        private void UpdateState()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            bool running = _proxy.IsRunning;
            _toggle.Text = running ? "Выключить прокси" : "Включить прокси";
            _open.Enabled = true;
            _copy.Enabled = true;
            _bridge.Enabled = running && !_busy;
            _port.Enabled = !running;
            _coverageHint.Text = _coverage.Checked
                ? "домены прокси в списке; служба zapret перезапускается"
                : "домены прокси не добавляются в список zapret";
            _secret.Text = "dd" + _proxy.SecretHex;
            _link.Text = _proxy.ProxyLink;
            SettingsData settings = Settings.Load();
            if (string.IsNullOrEmpty(settings.TgProxySecret) && running)
            {
                settings.TgProxySecret = _proxy.SecretHex;
                Settings.Save();
            }
            if (running)
            {
                SetState(_status,
                    "Включён · " + _proxy.Host + ":" + _proxy.Port +
                    " · секрет dd" + _proxy.SecretHex,
                    GreenColor);
            }
            else
            {
                SetState(_status,
                    _proxy.StartError ?? "Выключен. Включите прокси и " +
                    "нажмите «Открыть в Telegram».",
                    _proxy.StartError == null ? MutedColor : RedColor);
            }
            Action handler = ProxyStateChanged;
            if (handler != null)
            {
                handler();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _proxy.LogLine -= OnProxyLog;
                _proxy.StateChanged -= OnProxyStateChanged;
                _proxy.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

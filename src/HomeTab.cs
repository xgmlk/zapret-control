using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class HomeTab : TabView
    {
        private ToggleSwitch _toggle;
        private Label _status;
        private Label _detail;
        private Label _iface;
        private Label _ifaceState;
        private Button _ifaceButton;
        private Label _strategy;
        private Label _payload;
        private Label _task;
        private IfaceStatus _ifaceStatus;
        private bool _busy;
        private bool _refreshing;
        private bool _checkTask;
        private int _generation;

        public override string TabTitle
        {
            get { return "Главная"; }
        }

        public HomeTab()
        {
            Label caption = new Label();
            caption.Text = "СЛУЖБА ZAPRET";
            caption.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            caption.ForeColor = MutedColor;
            caption.BackColor = System.Drawing.Color.Transparent;
            caption.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            caption.SetBounds(0, 8, 852, 16);
            Controls.Add(caption);

            _toggle = new ToggleSwitch();
            _toggle.SetBounds(276, 28, 300, 130);
            _toggle.ToggleRequested += OnToggleRequested;
            Controls.Add(_toggle);

            _status = new Label();
            _status.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold);
            _status.ForeColor = TextColor;
            _status.BackColor = System.Drawing.Color.Transparent;
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _status.SetBounds(0, 162, 852, 30);
            Controls.Add(_status);

            _detail = new Label();
            _detail.Font = new System.Drawing.Font("Segoe UI", 9F);
            _detail.ForeColor = MutedColor;
            _detail.BackColor = System.Drawing.Color.Transparent;
            _detail.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _detail.SetBounds(0, 196, 852, 20);
            Controls.Add(_detail);

            CardPanel ifaceCard = AddCard("ИНТЕРФЕЙС", 0, 232, 852, 120);
            _iface = AddLabel(ifaceCard, "Сейчас: —", 20, 38, 600, 22);
            _iface.Font = new System.Drawing.Font("Segoe UI", 9.25F);
            _ifaceState = AddMutedLabel(ifaceCard, string.Empty, 20, 66, 620, 20);
            _ifaceButton = AddButton(
                ifaceCard, "Обновить", 716, 46, 112, 32, OnSyncIface);
            _ifaceButton.Enabled = false;
            _ifaceButton.AccessibleName = "Обновить интерфейс в конфиге";

            CardPanel infoCard = AddCard("СОСТОЯНИЕ", 0, 364, 852, 140);
            AddMutedLabel(infoCard, "СТРАТЕГИЯ", 20, 36, 120, 16);
            _strategy = AddLabel(infoCard, "—", 20, 54, 390, 22);
            AddMutedLabel(infoCard, "PAYLOAD FLOWSEAL", 430, 36, 200, 16);
            _payload = AddLabel(infoCard, "—", 430, 54, 390, 22);
            AddMutedLabel(infoCard, "ЗАДАЧА СИНХРОНИЗАЦИИ", 20, 88, 240, 16);
            _task = AddLabel(infoCard, "—", 20, 106, 390, 20);
            AddMutedLabel(infoCard, "ВЕРСИЯ ПРИЛОЖЕНИЯ", 430, 88, 200, 16);
            AddLabel(infoCard, AppInfo.Version, 430, 106, 390, 20);
        }

        public override void ActivateTab()
        {
            _checkTask = true;
        }

        public override async void RefreshData()
        {
            if (_busy || _refreshing || IsDisposed || Disposing)
            {
                return;
            }
            _refreshing = true;
            int generation = ++_generation;
            bool checkTask = _checkTask;
            _checkTask = false;
            try
            {
                ZapretStatus status = null;
                IfaceStatus iface = null;
                string strategy = string.Empty;
                string payloadVersion = string.Empty;
                bool? taskRegistered = null;
                try
                {
                    await Task.Run(delegate
                    {
                        status = ZapretService.GetStatus();
                        iface = IfaceStatusProvider.GetStatus();
                        strategy = ServiceManager.GetCurrentStrategyName();
                        payloadVersion = PayloadManager.GetVersion(AppPaths.ResolvePayloadRoot());
                        if (checkTask)
                        {
                            taskRegistered = SyncTask.IsRegistered();
                        }
                    });
                }
                catch (Exception)
                {
                    return;
                }
                if (IsDisposed || Disposing || generation != _generation || _busy)
                {
                    return;
                }
                ApplyStatus(status, iface, strategy, payloadVersion);
                if (taskRegistered.HasValue)
                {
                    SetState(_task,
                        taskRegistered.Value ? "зарегистрирована" : "не зарегистрирована",
                        taskRegistered.Value ? GreenColor : AmberColor);
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplyStatus(
            ZapretStatus status, IfaceStatus iface, string strategy, string payloadVersion)
        {
            System.Drawing.Color color;
            string title;
            string detail;
            switch (status.State)
            {
                case ZapretState.Running:
                    color = GreenColor;
                    title = "Включён";
                    detail = status.WinwsPid > 0
                        ? "Служба работает · winws.exe PID " + status.WinwsPid
                        : "Служба работает";
                    break;
                case ZapretState.Stopped:
                    color = RedColor;
                    title = "Выключен";
                    detail = "Служба остановлена";
                    break;
                case ZapretState.Starting:
                    color = AmberColor;
                    title = "Запускается…";
                    detail = "Подождите…";
                    break;
                case ZapretState.Stopping:
                    color = AmberColor;
                    title = "Останавливается…";
                    detail = "Подождите…";
                    break;
                case ZapretState.Paused:
                    color = AmberColor;
                    title = "Пауза";
                    detail = status.ServiceStatus;
                    break;
                case ZapretState.NotFound:
                    color = MutedColor;
                    title = "Не найдена";
                    detail = "Служба zapret не установлена — вкладка «Служба»";
                    break;
                case ZapretState.AccessDenied:
                    color = MutedColor;
                    title = "Нет прав";
                    detail = "Запустите приложение от администратора";
                    break;
                default:
                    color = MutedColor;
                    title = "Неизвестно";
                    detail = status.ServiceStatus;
                    break;
            }
            SetState(_status, title, color);
            _detail.Text = detail;

            bool canToggle = status.State == ZapretState.Running || status.State == ZapretState.Stopped;
            _toggle.Busy = !canToggle || _busy;
            if (canToggle)
            {
                _toggle.Checked = status.State == ZapretState.Running;
            }
            _strategy.Text = string.IsNullOrEmpty(strategy)
                ? (status.State == ZapretState.NotFound ? "служба не установлена" : "не выбрана")
                : strategy;
            _payload.Text = string.IsNullOrEmpty(payloadVersion)
                ? "неизвестна"
                : payloadVersion;
            ApplyIfaceStatus(iface);
        }

        private void ApplyIfaceStatus(IfaceStatus iface)
        {
            _ifaceStatus = iface;
            if (iface == null)
            {
                _iface.Text = "Сейчас: нет данных";
                _ifaceState.Text = string.Empty;
                _ifaceButton.Enabled = false;
                return;
            }
            _iface.Text = "Сейчас: " +
                (iface.ActiveFound ? iface.ActiveDisplay : "не определён");
            if (!iface.ServiceFound)
            {
                SetState(_ifaceState, "Служба zapret не установлена", MutedColor);
                _ifaceButton.Enabled = false;
                return;
            }
            if (!iface.ActiveFound)
            {
                SetState(_ifaceState, "Не удалось определить активный интерфейс", RedColor);
                _ifaceButton.Enabled = false;
                return;
            }
            string config = iface.HasConfig ? iface.ConfigDisplay : "не задан";
            if (iface.NeedsChange)
            {
                SetState(_ifaceState, "В конфиге: " + config + " · нужно обновить", AmberColor);
                _ifaceButton.Enabled = !_busy;
            }
            else
            {
                SetState(_ifaceState, "В конфиге: " + config + " · совпадает", GreenColor);
                _ifaceButton.Enabled = false;
            }
        }

        private async void OnSyncIface(object sender, EventArgs e)
        {
            if (_busy || _ifaceStatus == null)
            {
                return;
            }
            _generation++;
            _busy = true;
            _toggle.Busy = true;
            _ifaceButton.Enabled = false;
            SetState(_ifaceState, "Обновляю интерфейс…", AmberColor);

            IfaceSyncResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    result = IfaceStatusProvider.SyncInterface();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            _busy = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось обновить интерфейс", error);
                return;
            }
            if (result != null && result.StrategyError != null)
            {
                MessageBox.Show(
                    this,
                    "Интерфейс службы обновлён, но стратегию .bat обновить не удалось.\n" +
                    "Подробности — в logs\\sync-iface.log.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (result != null && result.NeedsChange)
            {
                MessageBox.Show(
                    this,
                    "Конфиг обновлён, но активный интерфейс всё ещё не совпадает.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void OnToggleRequested(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            ZapretStatus current = ZapretService.GetStatus();
            if (current.State != ZapretState.Running && current.State != ZapretState.Stopped)
            {
                return;
            }
            bool stopping = current.State == ZapretState.Running;
            _generation++;
            _busy = true;
            _toggle.Busy = true;
            _ifaceButton.Enabled = false;
            SetState(_status, stopping ? "Выключаю…" : "Включаю…", AmberColor);
            _detail.Text = "Подождите…";

            Exception error = null;
            bool timedOut = false;
            try
            {
                await Task.Run(delegate
                {
                    if (stopping)
                    {
                        ZapretService.Stop();
                    }
                    else
                    {
                        ZapretService.Start();
                    }
                });
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                timedOut = true;
            }
            catch (Exception ex)
            {
                error = ex;
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            _busy = false;
            RefreshData();
            if (timedOut)
            {
                ShowInfo("Служба всё ещё меняет состояние. Статус обновится автоматически.");
            }
            else if (error != null)
            {
                ShowError("Не удалось изменить состояние службы", error);
            }
        }
    }
}

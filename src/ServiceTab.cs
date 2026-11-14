using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class ServiceTab : TabView
    {
        private Label _serviceState;
        private Label _serviceDetail;
        private Label _imagePath;
        private ComboBox _strategy;
        private Button _apply;
        private Button _restart;
        private Button _remove;
        private Label _taskState;
        private Label _payloadRoot;
        private Label _payloadVersion;
        private Label _payloadState;
        private bool _busy;
        private bool _refreshing;
        private bool _checkTask;
        private string _payload;
        private string _currentStrategy;

        public override string TabTitle
        {
            get { return "Служба"; }
        }

        public ServiceTab()
        {
            CardPanel service = AddCard("СЛУЖБА ZAPRET", 0, 0, 852, 236);
            _serviceState = AddLabel(service, "Проверка…", 20, 38, 260, 24);
            _serviceState.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            _serviceDetail = AddMutedLabel(service, string.Empty, 280, 42, 552, 20);

            AddMutedLabel(service, "СТРАТЕГИЯ", 20, 74, 160, 16);
            _strategy = new ComboBox();
            _strategy.DropDownStyle = ComboBoxStyle.DropDownList;
            _strategy.SetBounds(20, 92, 500, 24);
            service.Controls.Add(_strategy);
            _apply = AddButton(service, "Применить", 532, 90, 120, 28, OnApplyStrategy);
            _restart = AddButton(service, "Перезапустить", 660, 90, 150, 28, OnRestart);

            AddButton(service, "Переустановить службу", 20, 132, 190, 30, OnReinstall);
            _remove = AddButton(service, "Удалить службу", 220, 132, 150, 30, OnRemoveService);
            AddMutedLabel(service,
                "Пересоздаёт ImagePath и сохраняет --wf-iface.",
                390, 138, 442, 20);
            _imagePath = AddMutedLabel(service, string.Empty, 20, 172, 812, 18);
            _imagePath.AutoEllipsis = true;

            CardPanel task = AddCard("АВТОСИНХРОНИЗАЦИЯ ИНТЕРФЕЙСА", 0, 248, 852, 116);
            _taskState = AddLabel(task, "Проверка…", 20, 42, 420, 22);
            AddButton(task, "Зарегистрировать задачу", 20, 70, 200, 30, OnRegisterTask);
            AddButton(task, "Удалить задачу", 230, 70, 150, 30, OnUnregisterTask);
            AddMutedLabel(task,
                "Задача Zapret-IfIndex-Sync запускает установленный CLI от SYSTEM при входе.",
                400, 74, 432, 20);

            CardPanel payload = AddCard("PAYLOAD FLOWSEAL", 0, 376, 852, 144);
            _payloadRoot = AddLabel(payload, "—", 20, 42, 812, 20);
            _payloadRoot.AutoEllipsis = true;
            _payloadVersion = AddMutedLabel(payload, string.Empty, 20, 68, 400, 18);
            _payloadState = AddMutedLabel(payload, string.Empty, 20, 92, 500, 18);
            AddButton(payload, "Открыть папку", 520, 86, 140, 30, OnOpenFolder);
            AddButton(payload, "Проверить", 670, 86, 140, 30, OnValidatePayload);
        }

        public override void ActivateTab()
        {
            _checkTask = true;
            ReloadStrategies();
        }

        public override async void RefreshData()
        {
            if (_busy || _refreshing || IsDisposed || Disposing)
            {
                return;
            }
            _refreshing = true;
            bool checkTask = _checkTask;
            _checkTask = false;
            try
            {
                ZapretStatus status = null;
                string strategy = string.Empty;
                string imagePath = string.Empty;
                string root = string.Empty;
                string version = string.Empty;
                bool? task = null;
                try
                {
                    await Task.Run(delegate
                    {
                        status = ZapretService.GetStatus();
                        strategy = ServiceManager.GetCurrentStrategyName();
                        imagePath = AppPaths.GetServiceImagePath() ?? string.Empty;
                        root = AppPaths.ResolvePayloadRoot();
                        version = PayloadManager.GetVersion(root);
                        if (checkTask)
                        {
                            task = SyncTask.IsRegistered();
                        }
                    });
                }
                catch (Exception)
                {
                    return;
                }
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _currentStrategy = strategy;
                _payload = root;
                _serviceState.Text = StateText(status.State);
                _serviceState.ForeColor = status.State == ZapretState.Running
                    ? GreenColor
                    : (status.State == ZapretState.Stopped ? RedColor : AmberColor);
                _serviceDetail.Text = status.WinwsPid > 0
                    ? "PID winws.exe " + status.WinwsPid
                    : status.ServiceStatus;
                _imagePath.Text = "ImagePath: " + (imagePath.Length == 0 ? "не задан" : imagePath);
                _payloadRoot.Text = root;
                _payloadVersion.Text = "Версия: " + (version.Length == 0 ? "неизвестна" : version);
                bool installed = NativeService.Exists(AppInfo.ServiceName);
                _remove.Enabled = installed;
                _restart.Enabled = status.State == ZapretState.Running ||
                                   status.State == ZapretState.Stopped;
                if (task.HasValue)
                {
                    SetState(_taskState,
                        task.Value ? "Задача зарегистрирована" : "Задача не зарегистрирована",
                        task.Value ? GreenColor : AmberColor);
                }
                SelectStrategy(strategy);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static string StateText(ZapretState state)
        {
            switch (state)
            {
                case ZapretState.Running:
                    return "Работает";
                case ZapretState.Stopped:
                    return "Остановлена";
                case ZapretState.Starting:
                    return "Запускается…";
                case ZapretState.Stopping:
                    return "Останавливается…";
                case ZapretState.Paused:
                    return "Пауза";
                case ZapretState.NotFound:
                    return "Не установлена";
                case ZapretState.AccessDenied:
                    return "Нет прав";
                default:
                    return "Неизвестно";
            }
        }

        private void ReloadStrategies()
        {
            string root = AppPaths.ResolvePayloadRoot();
            _payload = root;
            _strategy.Items.Clear();
            foreach (StrategyInfo info in StrategyParser.ListStrategies(root))
            {
                _strategy.Items.Add(info);
            }
            string current = ServiceManager.GetCurrentStrategyName();
            SelectStrategy(current);
        }

        private void SelectStrategy(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            for (int i = 0; i < _strategy.Items.Count; i++)
            {
                StrategyInfo info = _strategy.Items[i] as StrategyInfo;
                if (info != null && info.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    _strategy.SelectedIndex = i;
                    return;
                }
            }
        }

        private string SelectedStrategy()
        {
            StrategyInfo info = _strategy.SelectedItem as StrategyInfo;
            if (info != null)
            {
                return info.Name;
            }
            return _currentStrategy;
        }

        private async void OnApplyStrategy(object sender, EventArgs e)
        {
            await ApplyStrategy(true);
        }

        private async void OnReinstall(object sender, EventArgs e)
        {
            await ApplyStrategy(true);
        }

        private async Task ApplyStrategy(bool restart)
        {
            if (_busy)
            {
                return;
            }
            string strategy = SelectedStrategy();
            if (string.IsNullOrEmpty(strategy))
            {
                ShowInfo("Выберите стратегию.");
                return;
            }
            if (!Confirm("Пересоздать службу zapret со стратегией \"" + strategy + "\"?"))
            {
                return;
            }
            _busy = true;
            _serviceDetail.Text = "Пересоздаю службу: " + strategy + "…";
            _serviceDetail.ForeColor = AmberColor;
            Exception error = null;
            try
            {
                string root = AppPaths.ResolvePayloadRoot();
                int iface = AppPaths.GetServiceIfaceIndex();
                IfaceStatus status = IfaceStatusProvider.GetStatus();
                if (status.ActiveFound)
                {
                    iface = status.ActiveIndex;
                }
                string path = ServiceManager.FindStrategyPath(root, strategy);
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException("Стратегия не найдена: " + strategy);
                }
                bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
                await Task.Run(delegate
                {
                    ServiceManager.Install(root, path, restart || wasRunning, iface);
                });
                ShowInfo("Служба пересоздана со стратегией \"" + strategy + "\".");
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось изменить службу", error);
            }
        }

        private async void OnRestart(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    ZapretStatus status = ZapretService.GetStatus();
                    if (status.State == ZapretState.Running)
                    {
                        ZapretService.Stop();
                        ZapretService.Start();
                    }
                    else if (status.State == ZapretState.Stopped)
                    {
                        ZapretService.Start();
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Служба в переходном состоянии: " + status.State);
                    }
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось перезапустить службу", error);
            }
        }

        private async void OnRemoveService(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm(
                "Удалить службу zapret и связанные WinDivert/GoodbyeDPI?\n" +
                "Приложение и payload останутся на месте."))
            {
                return;
            }
            _busy = true;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    ServiceManager.Remove();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось удалить службу", error);
            }
        }

        private async void OnRegisterTask(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    SyncTask.Register();
                });
                ShowInfo("Задача " + SyncTask.TaskName + " зарегистрирована.");
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            _checkTask = true;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось зарегистрировать задачу", error);
            }
        }

        private async void OnUnregisterTask(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm("Удалить задачу " + SyncTask.TaskName + "?"))
            {
                return;
            }
            _busy = true;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    SyncTask.Unregister();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            _checkTask = true;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось удалить задачу", error);
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                string root = string.IsNullOrEmpty(_payload) ? AppPaths.ResolvePayloadRoot() : _payload;
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "explorer.exe";
                info.Arguments = "\"" + root + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть папку", ex);
            }
        }

        private void OnValidatePayload(object sender, EventArgs e)
        {
            string root = string.IsNullOrEmpty(_payload) ? AppPaths.ResolvePayloadRoot() : _payload;
            string error;
            if (PayloadManager.ValidatePayloadRoot(root, out error))
            {
                _payloadState.Text = "Проверка: OK, стратегий " +
                    StrategyParser.ListStrategies(root).Count + ".";
                _payloadState.ForeColor = GreenColor;
                ShowInfo("Payload корректен.\n" + root);
            }
            else
            {
                _payloadState.Text = "Проверка: " + error;
                _payloadState.ForeColor = RedColor;
                ShowError("Payload некорректен", new InvalidOperationException(error));
            }
        }
    }
}

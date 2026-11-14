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
        private Button _toggleProtection;
        private CheckBox _autoStart;
        private Button _advanced;
        private ContextMenuStrip _advancedMenu;
        private ToolStripMenuItem _recreateItem;
        private ToolStripMenuItem _removeItem;
        private Label _taskState;
        private Label _payloadRoot;
        private Label _payloadVersion;
        private Label _payloadState;
        private bool _busy;
        private bool _refreshing;
        private bool _checkTask;
        private bool _updatingStrategy;
        private bool _selectionDirty;
        private int _generation;
        private string _payload;
        private string _currentStrategy;

        public override string TabTitle
        {
            get { return "Служба"; }
        }

        public ServiceTab()
        {
            CardPanel service = AddCard("ЗАЩИТА ZAPRET", 0, 0, 852, 252);
            _serviceState = AddLabel(service, "Проверка…", 20, 34, 260, 24);
            _serviceState.Font = new System.Drawing.Font(
                "Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            _serviceDetail = AddMutedLabel(service, string.Empty, 280, 38, 552, 20);

            AddMutedLabel(service, "СТРАТЕГИЯ", 20, 70, 160, 16);
            _strategy = new ComboBox();
            _strategy.DropDownStyle = ComboBoxStyle.DropDownList;
            _strategy.SetBounds(20, 88, 500, 24);
            _strategy.SelectedIndexChanged += OnStrategyChanged;
            service.Controls.Add(_strategy);
            _apply = AddButton(
                service, "Применить стратегию", 532, 86, 150, 28, OnApplyStrategy);
            _restart = AddButton(
                service, "Перезапустить", 690, 86, 142, 28, OnRestart);

            _autoStart = new CheckBox();
            _autoStart.Text = "Запускать защиту при включении компьютера";
            _autoStart.SetBounds(20, 124, 370, 22);
            _autoStart.CheckedChanged += OnAutoStartChanged;
            service.Controls.Add(_autoStart);
            _toggleProtection = AddButton(
                service, "Остановить защиту", 400, 120, 180, 30, OnToggleProtection);
            _advanced = AddButton(
                service, "Дополнительно…", 590, 120, 242, 30, OnAdvanced);
            _imagePath = AddMutedLabel(service, string.Empty, 20, 164, 812, 18);
            _imagePath.AutoEllipsis = true;
            AddMutedLabel(service,
                "Пересоздание и удаление службы — в «Дополнительно».",
                20, 190, 812, 18);

            _advancedMenu = new ContextMenuStrip();
            _advancedMenu.ShowImageMargin = false;
            _recreateItem = new ToolStripMenuItem(
                "Пересоздать службу с текущей стратегией…");
            _recreateItem.Click += OnRecreateService;
            _advancedMenu.Items.Add(_recreateItem);
            _advancedMenu.Items.Add(new ToolStripSeparator());
            _removeItem = new ToolStripMenuItem("Удалить службу…");
            _removeItem.Click += OnRemoveService;
            _advancedMenu.Items.Add(_removeItem);

            CardPanel task = AddCard("АВТОСИНХРОНИЗАЦИЯ ИНТЕРФЕЙСА", 0, 264, 852, 116);
            _taskState = AddLabel(task, "Проверка…", 20, 38, 500, 22);
            AddButton(task, "Включить", 20, 66, 120, 30, OnRegisterTask);
            AddButton(task, "Отключить", 150, 66, 130, 30, OnUnregisterTask);
            AddMutedLabel(task,
                "Обновляет --wf-iface при входе в систему.",
                300, 70, 532, 20);

            CardPanel payload = AddCard("PAYLOAD FLOWSEAL", 0, 392, 852, 112);
            _payloadRoot = AddLabel(payload, "—", 20, 32, 812, 20);
            _payloadRoot.AutoEllipsis = true;
            _payloadVersion = AddMutedLabel(payload, string.Empty, 20, 56, 400, 18);
            _payloadState = AddMutedLabel(payload, string.Empty, 20, 78, 500, 18);
            AddButton(payload, "Открыть папку", 520, 72, 140, 28, OnOpenFolder);
            AddButton(payload, "Проверить", 670, 72, 140, 28, OnValidatePayload);
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
            int generation = ++_generation;
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
                bool installed = false;
                bool autoStart = false;
                try
                {
                    await Task.Run(delegate
                    {
                        status = ZapretService.GetStatus();
                        strategy = ServiceManager.GetCurrentStrategyName();
                        imagePath = AppPaths.GetServiceImagePath() ?? string.Empty;
                        root = AppPaths.ResolvePayloadRoot();
                        version = PayloadManager.GetVersion(root);
                        installed = NativeService.Exists(AppInfo.ServiceName);
                        autoStart = installed && NativeService.IsAutoStart(AppInfo.ServiceName);
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
                if (IsDisposed || Disposing || generation != _generation)
                {
                    return;
                }
                _currentStrategy = strategy;
                _payload = root;
                _serviceState.Text = StateText(status.State);
                _serviceState.ForeColor = status.State == ZapretState.Running
                    ? GreenColor
                    : (status.State == ZapretState.Stopped
                        ? RedColor
                        : (status.State == ZapretState.NotFound ? MutedColor : AmberColor));
                _serviceDetail.Text = status.WinwsPid > 0
                    ? "PID winws.exe " + status.WinwsPid
                    : (status.State == ZapretState.NotFound
                        ? "Служба zapret не установлена"
                        : status.ServiceStatus);
                _imagePath.Text = "Команда запуска: " +
                    (imagePath.Length == 0 ? "не задана" : imagePath);
                _payloadRoot.Text = root;
                _payloadVersion.Text = "Версия: " +
                    (version.Length == 0 ? "неизвестна" : version);
                _removeItem.Enabled = installed;
                _recreateItem.Enabled = _strategy.Items.Count > 0;
                _autoStart.Enabled = installed;
                _autoStart.Checked = autoStart;
                _strategy.Enabled = !_busy && _strategy.Items.Count > 0;
                _apply.Enabled = !_busy && _strategy.Items.Count > 0;
                _advanced.Enabled = installed || _strategy.Items.Count > 0;
                _restart.Enabled = status.State == ZapretState.Running ||
                                   status.State == ZapretState.Stopped;
                _toggleProtection.Text = status.State == ZapretState.Running
                    ? "Остановить защиту"
                    : "Запустить защиту";
                _toggleProtection.Enabled = status.State == ZapretState.Running ||
                                            status.State == ZapretState.Stopped;
                if (task.HasValue)
                {
                    SetState(_taskState,
                        task.Value
                            ? "Автосинхронизация включена"
                            : "Автосинхронизация выключена",
                        task.Value ? GreenColor : AmberColor);
                }
                if (!_selectionDirty)
                {
                    SelectStrategy(strategy);
                }
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
                    return "Защита включена";
                case ZapretState.Stopped:
                    return "Защита выключена";
                case ZapretState.Starting:
                    return "Запускается…";
                case ZapretState.Stopping:
                    return "Останавливается…";
                case ZapretState.Paused:
                    return "Пауза";
                case ZapretState.NotFound:
                    return "Служба не установлена";
                case ZapretState.AccessDenied:
                    return "Нет прав";
                default:
                    return "Неизвестно";
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _strategy.Enabled = !busy && _strategy.Items.Count > 0;
            if (!busy)
            {
                return;
            }
            _apply.Enabled = false;
            _restart.Enabled = false;
            _toggleProtection.Enabled = false;
            _autoStart.Enabled = false;
            _advanced.Enabled = false;
        }

        private void ReloadStrategies()
        {
            string selected = SelectedStrategy();
            if (string.IsNullOrEmpty(selected))
            {
                selected = _currentStrategy;
            }
            if (string.IsNullOrEmpty(selected))
            {
                selected = ServiceManager.GetCurrentStrategyName();
            }
            _updatingStrategy = true;
            try
            {
                _strategy.BeginUpdate();
                _strategy.Items.Clear();
                string root = AppPaths.ResolvePayloadRoot();
                foreach (StrategyInfo info in StrategyParser.ListStrategies(root))
                {
                    _strategy.Items.Add(info);
                }
            }
            finally
            {
                _strategy.EndUpdate();
                _updatingStrategy = false;
            }
            if (!SelectStrategy(selected) && _strategy.Items.Count > 0)
            {
                SetSelectedIndex(0);
            }
            _selectionDirty = false;
            _apply.Enabled = _strategy.Items.Count > 0;
        }

        private void OnStrategyChanged(object sender, EventArgs e)
        {
            if (!_updatingStrategy)
            {
                _selectionDirty = true;
            }
        }

        private bool SelectStrategy(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            for (int i = 0; i < _strategy.Items.Count; i++)
            {
                StrategyInfo info = _strategy.Items[i] as StrategyInfo;
                if (info != null && info.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedIndex(i);
                    return true;
                }
            }
            return false;
        }

        private void SetSelectedIndex(int index)
        {
            _updatingStrategy = true;
            try
            {
                _strategy.SelectedIndex = index;
            }
            finally
            {
                _updatingStrategy = false;
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
            if (NativeService.Exists(AppInfo.ServiceName) &&
                strategy.Equals(_currentStrategy, StringComparison.OrdinalIgnoreCase))
            {
                ShowInfo(ZapretService.GetStatus().State == ZapretState.Running
                    ? "Эта стратегия уже применена."
                    : "Стратегия уже выбрана. Нажмите «Запустить защиту».");
                return;
            }
            await ApplyStrategy(strategy, true, true);
        }

        private async void OnRecreateService(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm(
                "Пересоздать службу zapret с текущей стратегией?\n" +
                "Защита прервётся на несколько секунд."))
            {
                return;
            }
            string strategy = string.IsNullOrEmpty(_currentStrategy)
                ? SelectedStrategy()
                : _currentStrategy;
            if (string.IsNullOrEmpty(strategy))
            {
                ShowInfo("Сначала выберите стратегию.");
                return;
            }
            await ApplyStrategy(strategy, false, false);
        }

        private async Task ApplyStrategy(string strategy, bool forceStart, bool confirm)
        {
            if (_busy)
            {
                return;
            }
            string root = AppPaths.ResolvePayloadRoot();
            string path = ServiceManager.FindStrategyPath(root, strategy);
            if (string.IsNullOrEmpty(path))
            {
                ShowInfo("Стратегия не найдена: " + strategy);
                return;
            }
            bool installed = NativeService.Exists(AppInfo.ServiceName);
            ZapretStatus current = ZapretService.GetStatus();
            bool wasRunning = current.State == ZapretState.Running;
            bool start = forceStart || wasRunning || !installed;
            if (confirm)
            {
                string question = "Применить стратегию \"" + strategy + "\"?";
                if (!installed)
                {
                    question += "\nСлужба будет установлена и запущена.";
                }
                else if (start)
                {
                    question += wasRunning
                        ? "\nЗащита будет перезапущена."
                        : "\nЗащита будет запущена.";
                }
                if (!Confirm(question))
                {
                    return;
                }
            }

            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
            _serviceDetail.Text = "Применяю стратегию: " + strategy + "…";
            _serviceDetail.ForeColor = AmberColor;
            ServiceInstallResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    int iface = AppPaths.GetServiceIfaceIndex();
                    IfaceStatus status = IfaceStatusProvider.GetStatus();
                    if (status.ActiveFound)
                    {
                        iface = status.ActiveIndex;
                    }
                    result = ServiceManager.Install(root, path, start, iface);
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            _selectionDirty = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось изменить службу", error);
                return;
            }
            if (result != null)
            {
                _currentStrategy = result.StrategyName;
                SelectStrategy(result.StrategyName);
                string message = "Стратегия применена: " + result.StrategyName + ".";
                if (result.Started)
                {
                    message += " Защита запущена.";
                }
                else if (start)
                {
                    message += " Защита не запустилась — проверьте журнал службы Windows.";
                }
                ShowInfo(message);
            }
        }

        private async void OnRestart(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
            _serviceDetail.Text = "Перезапускаю защиту…";
            _serviceDetail.ForeColor = AmberColor;
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
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось перезапустить защиту", error);
            }
        }

        private async void OnToggleProtection(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            ZapretStatus status = ZapretService.GetStatus();
            if (status.State != ZapretState.Running && status.State != ZapretState.Stopped)
            {
                return;
            }
            bool stopping = status.State == ZapretState.Running;
            if (stopping && !Confirm(
                "Остановить защиту?\nОбход блокировок перестанет работать."))
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
            _serviceDetail.Text = stopping ? "Останавливаю защиту…" : "Запускаю защиту…";
            _serviceDetail.ForeColor = AmberColor;
            Exception error = null;
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
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            RefreshData();
            if (error != null)
            {
                ShowError(stopping
                    ? "Не удалось остановить защиту"
                    : "Не удалось запустить защиту", error);
            }
        }

        private async void OnAutoStartChanged(object sender, EventArgs e)
        {
            if (_busy || _refreshing)
            {
                return;
            }
            bool autoStart = _autoStart.Checked;
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                RefreshData();
                return;
            }
            _generation++;
            SetBusy(true);
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    NativeService.SetAutoStart(AppInfo.ServiceName, autoStart);
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось изменить автозагрузку", error);
            }
        }

        private void OnAdvanced(object sender, EventArgs e)
        {
            if (_advancedMenu == null)
            {
                return;
            }
            _advancedMenu.Show(_advanced, new System.Drawing.Point(0, _advanced.Height));
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
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
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
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            _selectionDirty = false;
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
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    SyncTask.Register();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            _checkTask = true;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось включить автосинхронизацию", error);
            }
            else
            {
                ShowInfo("Автосинхронизация интерфейса включена.");
            }
        }

        private async void OnUnregisterTask(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm("Отключить автосинхронизацию интерфейса?"))
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
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
            finally
            {
                OperationGuard.Exit();
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetBusy(false);
            _checkTask = true;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось отключить автосинхронизацию", error);
            }
            else
            {
                ShowInfo("Автосинхронизация интерфейса отключена.");
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                string root = string.IsNullOrEmpty(_payload)
                    ? AppPaths.ResolvePayloadRoot()
                    : _payload;
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
            string root = string.IsNullOrEmpty(_payload)
                ? AppPaths.ResolvePayloadRoot()
                : _payload;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && _advancedMenu != null)
            {
                _advancedMenu.Dispose();
                _advancedMenu = null;
            }
            base.Dispose(disposing);
        }
    }
}

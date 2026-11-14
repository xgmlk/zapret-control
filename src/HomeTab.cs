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
        private Label _strategyCurrent;
        private ComboBox _strategy;
        private Button _applyStrategy;
        private Button _advise;
        private Label _advice;
        private Label _checkSummary;
        private Button _checkButton;
        private Button _detailsButton;
        private IfaceStatus _ifaceStatus;
        private HomeCheckReport _lastCheck;
        private bool _busy;
        private bool _refreshing;
        private bool _updatingStrategy;
        private bool _selectionDirty;
        private string _currentStrategy;
        private int _generation;

        public event Action<HomeCheckReport> CheckCompleted;
        public event Action DetailsRequested;

        public override string TabTitle
        {
            get { return "Главная"; }
        }

        public HomeTab()
        {
            Label caption = new Label();
            caption.Text = "ЗАЩИТА ZAPRET";
            caption.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            caption.ForeColor = MutedColor;
            caption.BackColor = System.Drawing.Color.Transparent;
            caption.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            caption.SetBounds(0, 4, 852, 16);
            Controls.Add(caption);

            _toggle = new ToggleSwitch();
            _toggle.SetBounds(276, 20, 300, 130);
            _toggle.ToggleRequested += OnToggleRequested;
            Controls.Add(_toggle);

            _status = new Label();
            _status.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold);
            _status.ForeColor = TextColor;
            _status.BackColor = System.Drawing.Color.Transparent;
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _status.SetBounds(0, 154, 852, 30);
            Controls.Add(_status);

            _detail = new Label();
            _detail.Font = new System.Drawing.Font("Segoe UI", 9F);
            _detail.ForeColor = MutedColor;
            _detail.BackColor = System.Drawing.Color.Transparent;
            _detail.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _detail.SetBounds(0, 186, 852, 18);
            Controls.Add(_detail);

            CardPanel ifaceCard = AddCard("ИНТЕРФЕЙС", 0, 210, 852, 96);
            _iface = AddLabel(ifaceCard, "Сейчас: —", 20, 32, 600, 22);
            _iface.Font = new System.Drawing.Font("Segoe UI", 9.25F);
            _ifaceState = AddMutedLabel(ifaceCard, string.Empty, 20, 58, 660, 20);
            _ifaceButton = AddButton(
                ifaceCard, "Обновить", 716, 30, 112, 32, OnSyncIface);
            _ifaceButton.Enabled = false;
            _ifaceButton.AccessibleName = "Обновить интерфейс в конфиге";

            CardPanel strategyCard = AddCard("СТРАТЕГИЯ", 0, 314, 852, 112);
            _strategyCurrent = AddLabel(strategyCard, "Сейчас: —", 20, 32, 500, 18);
            _strategyCurrent.Font = new System.Drawing.Font("Segoe UI", 9.25F);
            _strategy = new ComboBox();
            _strategy.DropDownStyle = ComboBoxStyle.DropDownList;
            _strategy.SetBounds(20, 54, 500, 24);
            _strategy.SelectedIndexChanged += OnStrategyChanged;
            strategyCard.Controls.Add(_strategy);
            _applyStrategy = AddButton(
                strategyCard, "Применить", 532, 52, 140, 28, OnApplyStrategy);
            _applyStrategy.AccessibleName = "Применить стратегию";
            _advise = AddButton(
                strategyCard, "Подобрать", 682, 52, 150, 28, OnAdvise);
            _advise.AccessibleName = "Подобрать рабочую стратегию";
            _advice = AddMutedLabel(strategyCard,
                "Выберите стратегию или нажмите «Подобрать».",
                20, 86, 812, 18);

            CardPanel checkCard = AddCard("ПРОВЕРКА", 0, 430, 852, 84);
            _checkSummary = AddLabel(
                checkCard, "Проверка не выполнялась.", 20, 32, 450, 24);
            _checkSummary.Font = new System.Drawing.Font(
                "Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            _checkButton = AddButton(
                checkCard, "Проверить всё", 492, 28, 150, 30, OnCheck);
            _checkButton.AccessibleName = "Проверить соединения и hosts";
            _detailsButton = AddButton(
                checkCard, "Подробнее", 652, 28, 180, 30, OnDetails);
            _detailsButton.AccessibleName = "Подробный отчёт на вкладке Тесты";
            _detailsButton.Enabled = false;
        }

        public override void ActivateTab()
        {
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
            try
            {
                ZapretStatus status = null;
                IfaceStatus iface = null;
                string strategy = string.Empty;
                try
                {
                    await Task.Run(delegate
                    {
                        status = ZapretService.GetStatus();
                        iface = IfaceStatusProvider.GetStatus();
                        strategy = ServiceManager.GetCurrentStrategyName();
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
                ApplyStatus(status, iface, strategy);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplyStatus(ZapretStatus status, IfaceStatus iface, string strategy)
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
                    detail = "Служба zapret не установлена — примените стратегию ниже";
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

            bool canToggle = status.State == ZapretState.Running ||
                             status.State == ZapretState.Stopped;
            _toggle.Busy = !canToggle || _busy;
            if (canToggle)
            {
                _toggle.Checked = status.State == ZapretState.Running;
            }
            _currentStrategy = strategy;
            _strategyCurrent.Text = "Сейчас: " + (string.IsNullOrEmpty(strategy)
                ? (status.State == ZapretState.NotFound
                    ? "служба не установлена"
                    : "не выбрана")
                : strategy);
            if (!_selectionDirty)
            {
                SelectStrategy(strategy);
            }
            UpdateActionState();
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

        private void UpdateActionState()
        {
            bool hasStrategies = _strategy.Items.Count > 0;
            _strategy.Enabled = !_busy && hasStrategies;
            _applyStrategy.Enabled = !_busy && hasStrategies;
            _advise.Enabled = !_busy && hasStrategies;
            _checkButton.Enabled = !_busy;
            _detailsButton.Enabled = !_busy && _lastCheck != null;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _toggle.Busy = busy;
            if (busy)
            {
                _ifaceButton.Enabled = false;
            }
            UpdateActionState();
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
            if (_strategy.Items.Count == 0)
            {
                SetState(_advice, "Стратегии не найдены: проверьте payload.", RedColor);
            }
            else
            {
                if (_advice.Text.StartsWith("Стратегии не найдены",
                    StringComparison.Ordinal))
                {
                    SetState(_advice,
                        "Выберите стратегию или нажмите «Подобрать».", MutedColor);
                }
                if (!SelectStrategy(selected))
                {
                    SetSelectedIndex(0);
                }
            }
            _selectionDirty = false;
            UpdateActionState();
        }

        private void OnStrategyChanged(object sender, EventArgs e)
        {
            if (_updatingStrategy)
            {
                return;
            }
            _selectionDirty = true;
            StrategyInfo info = _strategy.SelectedItem as StrategyInfo;
            if (info != null)
            {
                SetState(_advice,
                    "Выбрана: " + info.Name + ". Нажмите «Применить».", MutedColor);
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
            return info == null ? string.Empty : info.Name;
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
                strategy.Equals(ServiceManager.GetCurrentStrategyName(),
                    StringComparison.OrdinalIgnoreCase) &&
                ZapretService.GetStatus().State == ZapretState.Running)
            {
                ShowInfo("Эта стратегия уже применена.");
                return;
            }
            await ApplyStrategy(strategy, true);
        }

        private async Task ApplyStrategy(string strategy, bool confirm)
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
            if (confirm && !Confirm(
                "Применить стратегию \"" + strategy + "\" и запустить защиту?"))
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
            SetState(_status, "Применяю стратегию…", AmberColor);
            _detail.Text = strategy;
            SetState(_advice, "Применяю стратегию " + strategy + "…", AmberColor);

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
                    ServiceManager.Install(root, path, true, iface);
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
            ReloadStrategies();
            RefreshData();
            if (error != null)
            {
                SetState(_advice, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось применить стратегию", error);
            }
            else
            {
                SetState(_advice, "Стратегия применена: " + strategy + ".", GreenColor);
            }
        }

        private async void OnAdvise(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm(
                "Подобрать рабочую стратегию?\n" +
                "Проверка займёт 1–2 минуты и временно прервёт защиту."))
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
            SetState(_status, "Подбираю стратегию…", AmberColor);
            _detail.Text = "Проверяю доступ к Discord, YouTube и Google.";
            SetState(_advice, "Подбираю стратегию…", AmberColor);

            StrategyAdviceReport report = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    report = StrategyAdvisor.Recommend(
                        AppPaths.ResolvePayloadRoot(),
                        new StrategyAdviceOptions(),
                        ReportProgress);
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
            ReloadStrategies();
            RefreshData();
            if (error != null)
            {
                SetState(_advice, "Ошибка подбора: " + ShortError(error), RedColor);
                ShowError("Не удалось подобрать стратегию", error);
                return;
            }
            if (report == null)
            {
                SetState(_advice, "Подбор не вернул результат.", RedColor);
                return;
            }
            string restoreWarning = report.ServiceWasRunning && !report.ServiceRestored
                ? " Служба не вернулась после проверки."
                : string.Empty;
            if (!string.IsNullOrEmpty(report.RecommendedName))
            {
                SelectStrategy(report.RecommendedName);
                _selectionDirty = true;
                string text = "Рекомендация: " + report.RecommendedName +
                    " (" + report.RecommendedPassed + " из " +
                    report.RecommendedTotal + ")." + restoreWarning;
                SetState(_advice, text, GreenColor);
                if (Confirm(text + "\nПрименить и запустить защиту?"))
                {
                    await ApplyStrategy(report.RecommendedName, false);
                }
                else
                {
                    SetState(_advice,
                        text + " Нажмите «Применить», чтобы использовать.",
                        AmberColor);
                }
            }
            else
            {
                string text = string.IsNullOrEmpty(report.Error)
                    ? "Рабочая стратегия не найдена."
                    : report.Error;
                if (!string.IsNullOrEmpty(report.BestName))
                {
                    text += " Лучший результат: " + report.BestName +
                        " (" + report.BestPassed + " из " + report.BestTotal + ").";
                }
                text += restoreWarning;
                SetState(_advice, text, RedColor);
                ShowInfo(text);
            }
        }

        private void ReportProgress(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(ReportProgress), message);
                }
                catch (Exception)
                {
                }
                return;
            }
            SetState(_advice, message, AmberColor);
        }

        private async void OnCheck(object sender, EventArgs e)
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
            SetState(_checkSummary, "Проверяю соединения и hosts…", AmberColor);

            HomeCheckReport report = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    report = HomeCheck.Run();
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
            if (error != null)
            {
                SetState(_checkSummary, "Ошибка: " + ShortError(error), RedColor);
                RefreshData();
                ShowError("Не удалось выполнить проверку", error);
                return;
            }
            if (report == null)
            {
                SetState(_checkSummary, "Проверка не вернула результат.", RedColor);
                RefreshData();
                return;
            }
            _lastCheck = report;
            ApplyCheckReport(report);
            UpdateActionState();
            if (CheckCompleted != null)
            {
                CheckCompleted(report);
            }
            RefreshData();
        }

        private void ApplyCheckReport(HomeCheckReport report)
        {
            System.Drawing.Color color = report.Passed == 0
                ? RedColor
                : (report.AllOk ? GreenColor : AmberColor);
            SetState(_checkSummary, report.Summary, color);
        }

        private void OnDetails(object sender, EventArgs e)
        {
            if (DetailsRequested != null)
            {
                DetailsRequested();
            }
        }

        private async void OnSyncIface(object sender, EventArgs e)
        {
            if (_busy || _ifaceStatus == null)
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
            if (!OperationGuard.TryEnter())
            {
                ShowInfo("Другая операция уже выполняется. Подождите.");
                return;
            }
            _generation++;
            SetBusy(true);
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

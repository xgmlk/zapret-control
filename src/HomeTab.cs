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
        private Button _lastReport;
        private ToolTip _toolTip;
        private Label _advice;
        private Label _checkSummary;
        private Button _checkButton;
        private Button _detailsButton;
        private Label _telegramStatus;
        private Button _telegramButton;
        private Button _telegramSettings;
        private IfaceStatus _ifaceStatus;
        private HomeCheckReport _lastCheck;
        private bool _hasLastReport;
        private bool _busy;
        private bool _refreshing;
        private bool _updatingStrategy;
        private bool _selectionDirty;
        private string _currentStrategy;
        private int _generation;

        public event Action<HomeCheckReport> CheckCompleted;
        public event Action DetailsRequested;
        public event Action TelegramToggleRequested;
        public event Action TelegramSettingsRequested;

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
            caption.Tag = "caption";
            caption.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            caption.SetBounds(0, 4, 520, 16);
            Controls.Add(caption);

            _toggle = new ToggleSwitch();
            _toggle.SetBounds(110, 20, 300, 130);
            _toggle.ToggleRequested += OnToggleRequested;
            Controls.Add(_toggle);

            _status = new Label();
            _status.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold);
            _status.ForeColor = TextColor;
            _status.BackColor = System.Drawing.Color.Transparent;
            _status.Tag = "state";
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _status.SetBounds(0, 154, 520, 30);
            Controls.Add(_status);

            _detail = new Label();
            _detail.Font = new System.Drawing.Font("Segoe UI", 9F);
            _detail.ForeColor = MutedColor;
            _detail.BackColor = System.Drawing.Color.Transparent;
            _detail.Tag = "muted";
            _detail.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _detail.SetBounds(0, 186, 520, 18);
            Controls.Add(_detail);

            CardPanel telegramCard = AddCard("TELEGRAM", 540, 0, 312, 204);
            _telegramStatus = AddMutedLabel(
                telegramCard, "Выключен.", 16, 30, 280, 40);
            _telegramStatus.Font = new System.Drawing.Font("Segoe UI", 9F);
            _telegramButton = AddButton(
                telegramCard, "Включить прокси", 16, 76, 130, 30,
                OnTelegramToggle);
            _telegramButton.AccessibleName = "Включить или выключить прокси Telegram";
            _telegramSettings = AddButton(
                telegramCard, "Настройки", 154, 76, 142, 30,
                OnTelegramSettings);
            _telegramSettings.AccessibleName = "Открыть настройки прокси Telegram";
            AddMutedLabel(telegramCard,
                "Прокси на 127.0.0.1. Настройки, журнал и проверки — на " +
                "вкладке «Telegram».",
                16, 114, 280, 42);

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
            Theme.StyleCombo(_strategy);
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
                20, 80, 640, 30);
            _lastReport = AddButton(
                strategyCard, "Последняя проверка", 682, 84, 150, 24, OnLastReport);
            _lastReport.AccessibleName =
                "Открыть результат последней проверки стратегий";
            _lastReport.Enabled = false;
            _toolTip = new ToolTip();
            _toolTip.ShowAlways = true;
            _toolTip.SetToolTip(_lastReport, "Проверок пока не было.");

            CardPanel checkCard = AddCard("ПРОВЕРКА", 0, 430, 852, 84);
            _checkSummary = AddLabel(
                checkCard, "Проверка не выполнялась.", 20, 32, 450, 24);
            _checkSummary.Font = new System.Drawing.Font(
                "Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            _checkButton = AddButton(
                checkCard, "Проверить всё", 492, 28, 150, 30, OnCheck);
            _checkButton.AccessibleName =
                "Проверить соединения, видео YouTube, DPI-заморозку и hosts";
            _detailsButton = AddButton(
                checkCard, "Подробнее", 652, 28, 180, 30, OnDetails);
            _detailsButton.AccessibleName = "Подробный отчёт на вкладке Тесты";
            _detailsButton.Enabled = false;
        }

        // State pushed by MainForm from the Telegram tab: the home card only
        // mirrors the proxy, all settings stay on the Telegram tab.
        public void SetTelegramProxyState(
            bool running, string status, bool busy, string error)
        {
            if (IsDisposed || Disposing || _telegramButton == null)
            {
                return;
            }
            _telegramButton.Text = running
                ? "Выключить прокси"
                : "Включить прокси";
            _telegramButton.Enabled = !busy;
            if (!running && !string.IsNullOrEmpty(error))
            {
                SetState(_telegramStatus, error, RedColor);
            }
            else if (running)
            {
                SetState(_telegramStatus,
                    string.IsNullOrEmpty(status) ? "Включён" : status,
                    GreenColor);
            }
            else
            {
                SetState(_telegramStatus,
                    string.IsNullOrEmpty(status) ? "Выключен" : status,
                    MutedColor);
            }
        }

        private void OnTelegramToggle(object sender, EventArgs e)
        {
            if (TelegramToggleRequested != null)
            {
                TelegramToggleRequested();
            }
        }

        private void OnTelegramSettings(object sender, EventArgs e)
        {
            if (TelegramSettingsRequested != null)
            {
                TelegramSettingsRequested();
            }
        }

        public override void ActivateTab()
        {
            ReloadStrategies();
            RefreshLastReportState();
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
            _lastReport.Enabled = !_busy;
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
            ServiceInstallResult result = null;
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
                    result = ServiceManager.Install(root, path, true, iface);
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
            else if (result == null)
            {
                SetState(_advice, "Применение не вернуло результат.", RedColor);
            }
            else if (result.Started)
            {
                string pid = result.WinwsPid > 0
                    ? " · winws.exe PID " + result.WinwsPid
                    : string.Empty;
                SetState(
                    _advice,
                    "Стратегия применена: " + result.StrategyName +
                    ". Служба работает" + pid + ".",
                    GreenColor);
            }
            else if (!string.IsNullOrEmpty(result.Error))
            {
                if (result.RolledBack)
                {
                    string previous = string.IsNullOrEmpty(result.PreviousStrategyName)
                        ? "предыдущая стратегия"
                        : result.PreviousStrategyName;
                    string text = "Восстановлена: " + previous + ".";
                    text += result.PreviousRunning && result.ServiceRunning
                        ? " Защита работает."
                        : " Защита остановлена.";
                    SetState(_advice, text, result.ServiceRunning ? AmberColor : RedColor);
                    MessageBox.Show(
                        this,
                        "Стратегия \"" + strategy + "\" не запустилась.\n\n" +
                        text + "\n\n" + result.Error,
                        "Служба не запустилась",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    string text = "Стратегия \"" + strategy + "\" не запустилась.";
                    SetState(_advice, text, RedColor);
                    MessageBox.Show(
                        this,
                        text + "\n\n" + result.Error,
                        "Служба не запустилась",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else if (!result.StartRequested)
            {
                SetState(
                    _advice,
                    "Стратегия выбрана: " + result.StrategyName + ".",
                    GreenColor);
            }
            else
            {
                string text = "Стратегия \"" + strategy + "\" не запустилась.";
                SetState(_advice, text, RedColor);
                MessageBox.Show(
                    this,
                    text + "\n\nСостояние службы: " + result.StatusText,
                    "Служба не запустилась",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OnAdvise(object sender, EventArgs e)
        {
            StartAdvice(true);
        }

        private void RefreshLastReportState()
        {
            SetLastReportState(StrategyReportStore.Load());
        }

        private void SetLastReportState(StrategyAdviceReport report)
        {
            _hasLastReport = report != null;
            if (_lastReport != null && !_lastReport.IsDisposed)
            {
                _lastReport.Enabled = !_busy;
            }
            if (_toolTip != null && _lastReport != null)
            {
                string text;
                if (!_hasLastReport)
                {
                    text = "Проверок пока не было.";
                }
                else if (string.IsNullOrEmpty(report.CheckedAtDisplay))
                {
                    text = "Последняя проверка сохранена.";
                }
                else
                {
                    text = "Последняя проверка: " + report.CheckedAtDisplay + ".";
                }
                _toolTip.SetToolTip(_lastReport, text);
            }
        }

        private void OnLastReport(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            StrategyAdviceReport report = StrategyReportStore.Load();
            if (report == null)
            {
                RefreshLastReportState();
                ShowInfo("Проверок пока не было. Нажмите «Подобрать».");
                return;
            }
            using (StrategyResultsForm form =
                new StrategyResultsForm(report, true))
            {
                form.ShowDialog(this);
            }
        }

        // Reusable entry point for the home button and the first-run prompt.
        // The first-run prompt already asks for consent, so it calls this with
        // confirm=false.
        public async void StartAdvice(bool confirm)
        {
            if (_busy)
            {
                return;
            }
            string ipset = IpsetManager.StateAny;
            try
            {
                ipset = IpsetManager.GetState(AppPaths.ResolvePayloadRoot());
            }
            catch (Exception)
            {
            }
            string ipsetNote = ipset == IpsetManager.StateAny
                ? string.Empty
                : "\nIPSet будет переключён в «любой» на время проверки и " +
                  "восстановлен после неё.";
            string curlNote = FreezeProbe.FindCurl() == null
                ? "\ncurl.exe не найден: DPI-заморозка не будет проверена, " +
                  "полная рекомендация невозможна."
                : string.Empty;
            if (confirm && !Confirm(
                "Подобрать рабочую стратегию?\n" +
                "Проверка займёт 1–4 минуты и временно прервёт защиту." +
                ipsetNote + curlNote))
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
            _detail.Text = "Проверяю Discord, YouTube (страница, плеер, 2 видео), " +
                           "Google и DPI-заморозку.";
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
            if (report.Results.Count == 0)
            {
                string text = string.IsNullOrEmpty(report.Error)
                    ? "Не удалось проверить стратегии."
                    : report.Error;
                SetState(_advice, text, RedColor);
                ShowInfo(text);
                return;
            }

            bool reportSaved = StrategyReportStore.Save(report);
            if (reportSaved)
            {
                SetLastReportState(report);
            }

            string restoreWarning = report.ServiceWasRunning && !report.ServiceRestored
                ? "Служба не вернулась после проверки — включите защиту вручную."
                : string.Empty;
            if (!report.IpsetRestored)
            {
                restoreWarning += " IPSet не восстановлен — проверьте вкладку IPSet " +
                                  "или перезапустите программу.";
            }
            if (!report.CurlAvailable)
            {
                restoreWarning += " curl.exe не найден: DPI-заморозка не проверена, " +
                                  "полная рекомендация невозможна.";
            }
            StrategyProbeResult selected;
            using (StrategyResultsForm form = new StrategyResultsForm(report))
            {
                form.ShowDialog(this);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                selected = form.SelectedResult;
            }
            if (!reportSaved)
            {
                ShowInfo(
                    "Отчёт проверки не сохранён — кнопка «Последняя проверка» " +
                    "останется с прежним результатом.");
            }
            if (selected == null)
            {
                string summary = report.HasRecommendation
                    ? "Лучший результат: " + report.RecommendedName + " (" +
                      report.RecommendedPassed + " из " + report.RecommendedTotal + ")."
                    : (report.HasPartial
                        ? "Полностью работающих нет, лучший частичный: " +
                          report.BestName + " (" + report.BestPassed + " из " +
                          report.BestTotal + ")."
                        : "Рабочих стратегий не найдено.");
                if (!string.IsNullOrEmpty(restoreWarning))
                {
                    summary += " " + restoreWarning;
                }
                SetState(
                    _advice, summary, report.HasRecommendation ? GreenColor : AmberColor);
                return;
            }

            SelectStrategy(selected.Name);
            _selectionDirty = true;
            string question = selected.Ok
                ? "Применить стратегию \"" + selected.Name + "\" и запустить защиту?"
                : "Стратегия \"" + selected.Name + "\" " +
                  (selected.PartiallyOk ? "работает частично" : "не прошла проверку") +
                  " (" + selected.Passed + " из " + selected.Total + ").\n" +
                  selected.Detail + "\n\nВсё равно применить её?";
            if (!string.IsNullOrEmpty(restoreWarning))
            {
                question += "\n\n" + restoreWarning;
            }
            if (!Confirm(question))
            {
                SetState(
                    _advice,
                    "Выбрана: " + selected.Name + ". Нажмите «Применить».",
                    selected.Ok ? GreenColor : AmberColor);
                return;
            }
            await ApplyStrategy(selected.Name, false);
        }

        public void StartManualSelection()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            _strategy.Focus();
            if (_strategy.Items.Count > 0)
            {
                _strategy.DroppedDown = true;
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
            SetState(_checkSummary,
                "Проверяю соединения, видео YouTube, DPI и hosts…", AmberColor);

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

        // Tray menu entry: the same guarded operation as the main toggle.
        // It must not duplicate the service logic, otherwise tray and window
        // can disagree about OperationGuard and the busy state.
        public void ToggleProtectionFromTray()
        {
            if (IsDisposed || Disposing || _busy)
            {
                return;
            }
            OnToggleRequested(this, EventArgs.Empty);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && _toolTip != null)
            {
                _toolTip.Dispose();
                _toolTip = null;
            }
            base.Dispose(disposing);
        }
    }
}

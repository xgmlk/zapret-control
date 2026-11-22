using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class UpdatesTab : TabView
    {
        private Label _appCurrent;
        private Label _appLatest;
        private Label _payloadCurrent;
        private Label _payloadLatest;
        private Label _checkStatus;
        private Button _check;
        private Button _updatePayload;
        private Button _updateApp;
        private Button _rollback;
        private TextBox _manifest;
        private CheckBox _autoCheck;
        private CheckBox _autoPayload;
        private CheckBox _closeToTray;
        private CheckBox _trayBalloons;
        private CheckBox _startInTray;
        private TextBox _log;
        private UpdateCheckResult _result;
        private DateTime _lastAutoCheck;
        private bool _busy;

        // Raised only by the silent startup check, not by manual checks.
        public event Action<UpdateCheckResult> AppUpdateAvailable;

        public override string TabTitle
        {
            get { return "Обновления"; }
        }

        public UpdatesTab()
        {
            CardPanel status = AddCard("КАНАЛЫ ОБНОВЛЕНИЙ", 0, 0, 852, 194);
            AddMutedLabel(status, "ПРИЛОЖЕНИЕ", 20, 36, 180, 16);
            _appCurrent = AddLabel(status, "—", 20, 56, 180, 20);
            AddMutedLabel(status, "ДОСТУПНО", 210, 36, 180, 16);
            _appLatest = AddLabel(status, "—", 210, 56, 180, 20);
            AddMutedLabel(status, "PAYLOAD FLOWSEAL", 430, 36, 200, 16);
            _payloadCurrent = AddLabel(status, "—", 430, 56, 180, 20);
            AddMutedLabel(status, "ДОСТУПНО", 620, 36, 180, 16);
            _payloadLatest = AddLabel(status, "—", 620, 56, 212, 20);
            _checkStatus = AddMutedLabel(status, "Проверка не выполнялась.", 20, 86, 812, 20);
            _check = AddButton(status, "Проверить", 20, 116, 130, 32, OnCheck);
            _updatePayload = AddButton(
                status, "Обновить zapret", 160, 116, 160, 32, OnUpdatePayload);
            _updateApp = AddButton(
                status, "Обновить приложение", 330, 116, 180, 32, OnUpdateApp);
            _rollback = AddButton(
                status, "Откатить payload", 520, 116, 160, 32, OnRollback);
            _updatePayload.Enabled = false;
            _updateApp.Enabled = false;
            _rollback.Enabled = false;
            AddMutedLabel(status,
                "Обновление приложения: SHA256 проверяется всегда, подпись — если Setup подписан.",
                20, 158, 812, 20);

            CardPanel settings = AddCard("ИСТОЧНИК", 0, 206, 852, 166);
            AddMutedLabel(settings, "RELEASE.JSON", 20, 36, 160, 16);
            _manifest = AddTextBox(settings, 20, 56, 600, 24, false);
            AddButton(settings, "Сохранить", 630, 54, 120, 26, OnSaveSettings);
            _autoCheck = new ThemedCheckBox();
            _autoCheck.Text = "Проверять обновления при открытии вкладки";
            _autoCheck.SetBounds(20, 84, 360, 22);
            Theme.StyleCheck(_autoCheck);
            settings.Controls.Add(_autoCheck);
            _autoPayload = new ThemedCheckBox();
            _autoPayload.Text = "Устанавливать payload автоматически";
            _autoPayload.SetBounds(390, 84, 360, 22);
            Theme.StyleCheck(_autoPayload);
            settings.Controls.Add(_autoPayload);
            _closeToTray = new ThemedCheckBox();
            _closeToTray.Text = "Закрывать окно в трей";
            _closeToTray.SetBounds(20, 112, 280, 22);
            _closeToTray.CheckedChanged += OnTraySettingsChanged;
            Theme.StyleCheck(_closeToTray);
            settings.Controls.Add(_closeToTray);
            _trayBalloons = new ThemedCheckBox();
            _trayBalloons.Text = "Показывать уведомления в трее";
            _trayBalloons.SetBounds(320, 112, 320, 22);
            _trayBalloons.CheckedChanged += OnTraySettingsChanged;
            Theme.StyleCheck(_trayBalloons);
            settings.Controls.Add(_trayBalloons);
            _startInTray = new ThemedCheckBox();
            _startInTray.Text = "Стартовать в трее";
            _startInTray.SetBounds(650, 112, 182, 22);
            _startInTray.CheckedChanged += OnTraySettingsChanged;
            Theme.StyleCheck(_startInTray);
            settings.Controls.Add(_startInTray);
            AddMutedLabel(settings,
                "Полный выход — в меню значка в области уведомлений.",
                20, 138, 500, 18);

            CardPanel logCard = AddCard("ЖУРНАЛ ОБНОВЛЕНИЙ", 0, 384, 852, 136);
            _log = AddTextBox(logCard, 20, 36, 812, 84, true);
            _log.Font = new System.Drawing.Font("Consolas", 8.5F);
        }

        public override async void ActivateTab()
        {
            LoadSettingsIntoControls();
            _appCurrent.Text = AppInfo.Version;
            _payloadCurrent.Text = PayloadManager.GetVersion(AppPaths.ResolvePayloadRoot());
            if (_payloadCurrent.Text.Length == 0)
            {
                _payloadCurrent.Text = "неизвестна";
            }
            if (_result == null)
            {
                _appLatest.Text = "—";
                _payloadLatest.Text = "—";
            }
            bool auto = _autoCheck.Checked &&
                (DateTime.UtcNow - _lastAutoCheck).TotalHours >= 6;
            if (auto && !_busy)
            {
                _lastAutoCheck = DateTime.UtcNow;
                await CheckNow(false);
            }
        }

        // Called once when the main window opens: a new installation should
        // update payload automatically even if the user never visits this tab.
        public async void CheckIfDue()
        {
            if (IsDisposed || Disposing || _busy)
            {
                return;
            }
            LoadSettingsIntoControls();
            if (!_autoCheck.Checked)
            {
                return;
            }
            if ((DateTime.UtcNow - _lastAutoCheck).TotalHours < 6)
            {
                return;
            }
            _lastAutoCheck = DateTime.UtcNow;
            await CheckNow(true);
        }

        // Tray menu entry: an explicit check ignores the six-hour interval but
        // still notifies through the normal AppUpdateAvailable path.
        public async void CheckFromTray()
        {
            if (IsDisposed || Disposing || _busy)
            {
                return;
            }
            LoadSettingsIntoControls();
            await CheckNow(true);
        }

        private void LoadSettingsIntoControls()
        {
            SettingsData settings = Settings.Load();
            _manifest.Text = settings.ManifestUrl;
            _autoCheck.Checked = settings.AutoCheckUpdates;
            _autoPayload.Checked = settings.AutoUpdatePayload;
            _closeToTray.Checked = settings.CloseToTray;
            _trayBalloons.Checked = settings.TrayBalloons;
            _startInTray.Checked = settings.StartInTray;
            if (_lastAutoCheck == DateTime.MinValue &&
                !string.IsNullOrEmpty(settings.LastCheckUtc))
            {
                DateTime parsed;
                if (DateTime.TryParse(settings.LastCheckUtc, out parsed))
                {
                    _lastAutoCheck = parsed.ToUniversalTime();
                }
            }
        }

        private async void OnCheck(object sender, EventArgs e)
        {
            await CheckNow(false);
        }

        private async Task CheckNow(bool notify)
        {
            if (_busy)
            {
                return;
            }
            string url = _manifest.Text.Trim();
            if (url.Length == 0)
            {
                url = AppInfo.DefaultManifestUrl;
            }
            _busy = true;
            _check.Enabled = false;
            SetState(_checkStatus, "Проверяю release.json…", AmberColor);
            UpdateCheckResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    result = UpdateService.Check(url, AppPaths.ResolvePayloadRoot());
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
            _check.Enabled = true;
            if (error != null)
            {
                SetState(_checkStatus, "Ошибка: " + ShortError(error), RedColor);
                AppendLog("Ошибка проверки: " + ShortError(error));
                return;
            }
            _result = result;
            _appLatest.Text = string.IsNullOrEmpty(result.LatestAppVersion)
                ? "—"
                : result.LatestAppVersion;
            _payloadLatest.Text = string.IsNullOrEmpty(result.LatestPayloadVersion)
                ? "—"
                : result.LatestPayloadVersion;
            _updateApp.Enabled = result.AppUpdateAvailable && AppPaths.IsInstalledLayout();
            _updatePayload.Enabled = result.PayloadUpdateAvailable;
            _rollback.Enabled = PayloadManager.ListBackups(
                AppPaths.ResolvePayloadRoot()).Count > 0;

            if (!string.IsNullOrEmpty(result.ManifestError))
            {
                SetState(_checkStatus, result.ManifestError, AmberColor);
                AppendLog(result.ManifestError);
            }
            else if (result.AppUpdateAvailable || result.PayloadUpdateAvailable)
            {
                string text = "Доступно:";
                if (result.AppUpdateAvailable)
                {
                    text += " приложение " + result.LatestAppVersion;
                }
                if (result.PayloadUpdateAvailable)
                {
                    text += (text == "Доступно:" ? string.Empty : ",") +
                            " payload " + result.LatestPayloadVersion;
                }
                SetState(_checkStatus, text, AmberColor);
                AppendLog(text);
            }
            else
            {
                SetState(_checkStatus, "Обновлений нет.", GreenColor);
                AppendLog("Обновлений нет.");
            }

            SettingsData settings = Settings.Load();
            settings.LastCheckUtc = DateTime.UtcNow.ToString("o");
            settings.AutoCheckUpdates = _autoCheck.Checked;
            settings.AutoUpdatePayload = _autoPayload.Checked;
            settings.CloseToTray = _closeToTray.Checked;
            settings.TrayBalloons = _trayBalloons.Checked;
            settings.StartInTray = _startInTray.Checked;
            Settings.Save();

            if (_autoPayload.Checked && result.PayloadUpdateAvailable)
            {
                await ApplyPayload(true);
            }

            // Only the silent startup check notifies; the tab itself already
            // shows the same result when the user opens it.
            if (notify &&
                result.AppUpdateAvailable &&
                AppPaths.IsInstalledLayout() &&
                AppUpdateAvailable != null)
            {
                AppUpdateAvailable(result);
            }
        }

        private async void OnUpdatePayload(object sender, EventArgs e)
        {
            await ApplyPayload(false);
        }

        private async Task ApplyPayload(bool automatic)
        {
            if (_busy || _result == null)
            {
                return;
            }
            if (!_result.PayloadUpdateAvailable)
            {
                ShowInfo("Обновление payload не требуется.");
                return;
            }
            if (!automatic &&
                !Confirm("Обновить payload Flowseal до " + _result.LatestPayloadVersion +
                         "?\nСлужба будет остановлена, создана резервная копия и выполнен " +
                         "перезапуск при успехе."))
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                string blocked = automatic
                    ? "Автообновление payload отложено: выполняется другая операция."
                    : "Другая операция уже выполняется. Подождите.";
                SetState(_checkStatus, blocked, AmberColor);
                AppendLog(blocked);
                return;
            }
            _busy = true;
            _updatePayload.Enabled = false;
            _updateApp.Enabled = false;
            SetState(_checkStatus,
                automatic ? "Автоматическое обновление payload…" : "Обновляю payload…",
                AmberColor);
            ConnectionTestReport baseline = null;
            if (automatic)
            {
                // D36: remember which Discord/YouTube stages worked before
                // the payload swap, so a regression can be rolled back.
                try
                {
                    baseline = await Task.Run(delegate
                    {
                        return ConnectionTests.RunSingle();
                    });
                    AppendLog("Проверка до payload: " + baseline.Passed + " из " +
                        baseline.Results.Count + ".");
                }
                catch (Exception ex)
                {
                    AppendLog("Проверка до payload не выполнена: " + ShortError(ex));
                }
            }
            Exception error = null;
            PayloadUpdateResult result = null;
            try
            {
                await Task.Run(delegate
                {
                    result = UpdateService.ApplyPayload(
                        _result,
                        AppPaths.ResolvePayloadRoot(),
                        delegate(long received, long total)
                        {
                            if (total > 0)
                            {
                                AppendLogAsync("Загрузка payload: " +
                                    (received * 100 / total) + "%");
                            }
                        });
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
            _busy = false;
            _updatePayload.Enabled = true;
            _updateApp.Enabled = _result.AppUpdateAvailable && AppPaths.IsInstalledLayout();
            _payloadCurrent.Text = PayloadManager.GetVersion(AppPaths.ResolvePayloadRoot());
            if (error != null)
            {
                SetState(_checkStatus, "Ошибка обновления: " + ShortError(error), RedColor);
                AppendLog("Ошибка payload: " + ShortError(error));
                if (!automatic)
                {
                    ShowError("Не удалось обновить payload", error);
                }
                return;
            }
            if (result != null)
            {
                if (automatic && baseline != null)
                {
                    ConnectionTestReport after = null;
                    try
                    {
                        after = await Task.Run(delegate
                        {
                            return ConnectionTests.RunSingle();
                        });
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Проверка после payload не выполнена: " +
                            ShortError(ex));
                    }
                    if (after != null && HasRegression(baseline, after))
                    {
                        string rollbackText =
                            "Payload " + result.NewVersion +
                            " ухудшил проверки Discord/YouTube; откат.";
                        AppendLog(rollbackText);
                        try
                        {
                            PayloadUpdateResult rolledBack = await Task.Run(delegate
                            {
                                return PayloadManager.RestoreLatestBackup(true);
                            });
                            if (rolledBack != null)
                            {
                                rollbackText += " Восстановлен " +
                                    rolledBack.NewVersion + ".";
                            }
                        }
                        catch (Exception ex)
                        {
                            rollbackText += " Ошибка отката: " + ShortError(ex);
                        }
                        SetState(_checkStatus, rollbackText, RedColor);
                        return;
                    }
                }
                _result.PayloadUpdateAvailable = false;
                _updatePayload.Enabled = false;
                string text = "Payload обновлён: " + result.OldVersion + " -> " +
                    result.NewVersion + ", стратегия " + result.StrategyName + ".";
                if (result.Warning != null)
                {
                    text += " " + result.Warning;
                }
                SetState(_checkStatus, text, GreenColor);
                AppendLog(text);
                if (!automatic)
                {
                    ShowInfo(text);
                }
            }
        }

        private async void OnUpdateApp(object sender, EventArgs e)
        {
            if (_busy || _result == null || !_result.AppUpdateAvailable)
            {
                return;
            }
            string question = "Скачать и установить Zapret Control " +
                _result.LatestAppVersion +
                "?\nПриложение закроется, установщик обновит файлы и запустит " +
                "новую версию. Настройки и payload сохранятся.";
            question += "\nНеподписанный установщик принимается без проверки издателя " +
                        "(SHA256 проверяется).";
            if (!Confirm(question))
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                SetState(_checkStatus,
                    "Другая операция уже выполняется. Подождите.", AmberColor);
                return;
            }
            _busy = true;
            _updateApp.Enabled = false;
            SetState(_checkStatus, "Скачиваю установщик…", AmberColor);
            Exception error = null;
            AppUpdateLaunch launch = null;
            try
            {
                await Task.Run(delegate
                {
                    launch = UpdateService.ApplyAppUpdate(
                        _result,
                        delegate(long received, long total)
                        {
                            if (total > 0)
                            {
                                AppendLogAsync("Загрузка установщика: " +
                                    (received * 100 / total) + "%");
                            }
                        });
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
            _busy = false;
            if (error != null)
            {
                _updateApp.Enabled = true;
                SetState(_checkStatus, "Ошибка: " + ShortError(error), RedColor);
                AppendLog("Ошибка приложения: " + ShortError(error));
                ShowError("Не удалось обновить приложение", error);
                return;
            }
            if (launch != null)
            {
                AppendLog("Установщик запущен: " + launch.SetupPath);
                MessageBox.Show(
                    this,
                    "Установщик запущен. Приложение закроется, обновление продолжится " +
                    "в отдельном окне.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Application.Exit();
            }
        }

        private async void OnRollback(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm("Откатить payload к последней резервной копии?\n" +
                         "Служба будет пересоздана."))
            {
                return;
            }
            if (!OperationGuard.TryEnter())
            {
                SetState(_checkStatus,
                    "Другая операция уже выполняется. Подождите.", AmberColor);
                return;
            }
            _busy = true;
            SetState(_checkStatus, "Откатываю payload…", AmberColor);
            Exception error = null;
            PayloadUpdateResult result = null;
            try
            {
                await Task.Run(delegate
                {
                    result = PayloadManager.RestoreLatestBackup(true);
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
            _busy = false;
            if (error != null)
            {
                SetState(_checkStatus, "Ошибка отката: " + ShortError(error), RedColor);
                ShowError("Не удалось откатить payload", error);
                return;
            }
            if (result != null)
            {
                string text = "Payload откатан: " + result.NewVersion +
                    ", стратегия " + result.StrategyName + ".";
                SetState(_checkStatus, text, GreenColor);
                AppendLog(text);
                _payloadCurrent.Text = PayloadManager.GetVersion(AppPaths.ResolvePayloadRoot());
                ShowInfo(text);
            }
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            SettingsData settings = Settings.Load();
            settings.ManifestUrl = _manifest.Text.Trim();
            if (settings.ManifestUrl.Length == 0)
            {
                settings.ManifestUrl = AppInfo.DefaultManifestUrl;
                _manifest.Text = settings.ManifestUrl;
            }
            settings.AutoCheckUpdates = _autoCheck.Checked;
            settings.AutoUpdatePayload = _autoPayload.Checked;
            settings.CloseToTray = _closeToTray.Checked;
            settings.TrayBalloons = _trayBalloons.Checked;
            settings.StartInTray = _startInTray.Checked;
            if (Settings.Save())
            {
                SetState(_checkStatus, "Настройки обновлений сохранены.", GreenColor);
            }
            else
            {
                SetState(_checkStatus,
                    "Не удалось сохранить настройки (нет доступа к ProgramData).", RedColor);
            }
        }

        // Only stages that worked before may trigger a rollback. A host that
        // was already failing is not evidence against the new payload.
        private static bool HasRegression(
            ConnectionTestReport before, ConnectionTestReport after)
        {
            if (before == null || after == null)
            {
                return false;
            }
            Dictionary<string, bool> afterResults =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < after.Results.Count; i++)
            {
                afterResults[after.Results[i].Name] = after.Results[i].Success;
            }
            for (int i = 0; i < before.Results.Count; i++)
            {
                ConnectionTestResult stage = before.Results[i];
                if (!stage.Success)
                {
                    continue;
                }
                if (stage.Name != "Discord" &&
                    stage.Name != "YouTube хосты и страница")
                {
                    continue;
                }
                bool now;
                if (afterResults.TryGetValue(stage.Name, out now) && !now)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnTraySettingsChanged(object sender, EventArgs e)
        {
            if (_closeToTray == null || _trayBalloons == null ||
                _startInTray == null)
            {
                return;
            }
            SettingsData settings = Settings.Load();
            settings.CloseToTray = _closeToTray.Checked;
            settings.TrayBalloons = _trayBalloons.Checked;
            settings.StartInTray = _startInTray.Checked;
            if (!AutostartManager.SetEnabled(
                    _startInTray.Checked, Application.ExecutablePath))
            {
                ShowInfo("Не удалось изменить автозапуск в реестре пользователя.");
            }
            Settings.Save();
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message +
                            Environment.NewLine);
        }

        private void AppendLogAsync(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(AppendLogAsync), message);
                }
                catch (Exception)
                {
                }
                return;
            }
            AppendLog(message);
        }
    }
}

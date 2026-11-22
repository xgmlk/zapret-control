using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class DiagnosticsTab : TabView
    {
        private ListView _list;
        private Label _status;
        private Button _run;
        private DiagnosticReport _report;
        private bool _busy;
        private bool _hasRun;

        public override string TabTitle
        {
            get { return "Диагностика"; }
        }

        public DiagnosticsTab()
        {
            CardPanel card = AddCard("ДИАГНОСТИКА", 0, 0, 852, 470);
            AddMutedLabel(card,
                "Нативный аналог пункта Run Diagnostics из service.bat: служба, BFE, прокси, " +
                "TCP timestamps, конфликтующие программы, hosts, интерфейс и задача.",
                20, 34, 812, 36);
            _run = AddButton(card, "Запустить диагностику", 20, 76, 180, 32, OnRun);
            AddButton(card, "Копировать отчёт", 210, 76, 150, 32, OnCopy);
            AddButton(card, "Кэш Discord", 370, 76, 180, 32, OnClearCache);
            AddButton(card, "Проверить hosts", 560, 76, 120, 32, OnHostsCheck);
            AddButton(card, "Обновить hosts", 690, 76, 122, 32, OnHostsApply);
            _status = AddMutedLabel(card, "Проверка ещё не выполнялась.", 20, 108, 812, 18);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.SetBounds(20, 134, 812, 316);
            _list.Columns.Add("", 36);
            _list.Columns.Add("Проверка", 200);
            _list.Columns.Add("Результат", 570);
            Theme.StyleList(_list);
            card.Controls.Add(_list);
        }

        public override void ActivateTab()
        {
            if (!_hasRun && !_busy)
            {
                _hasRun = true;
                RunDiagnostics();
            }
        }

        private void OnRun(object sender, EventArgs e)
        {
            RunDiagnostics();
        }

        private async void RunDiagnostics()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            _run.Enabled = false;
            SetState(_status, "Выполняю проверки…", AmberColor);
            DiagnosticReport report = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    report = Diagnostics.Run(AppPaths.ResolvePayloadRoot());
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            _run.Enabled = true;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось выполнить диагностику", error);
                return;
            }
            _report = report;
            _list.BeginUpdate();
            _list.Items.Clear();
            try
            {
                foreach (DiagnosticItem item in report.Items)
                {
                    ListViewItem row = new ListViewItem(item.Symbol);
                    row.SubItems.Add(item.Name);
                    row.SubItems.Add(item.Detail);
                    row.ForeColor = StateColor(item.State);
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            int warnings = report.Count(DiagnosticState.Warning);
            int fails = report.Count(DiagnosticState.Fail);
            SetState(_status,
                "Готово: предупреждений " + warnings + ", ошибок " + fails + ".",
                fails > 0 ? RedColor : (warnings > 0 ? AmberColor : GreenColor));
        }

        private void OnCopy(object sender, EventArgs e)
        {
            if (_report == null)
            {
                ShowInfo("Сначала запустите диагностику.");
                return;
            }
            try
            {
                Clipboard.SetText(_report.ToText());
                _status.Text = "Отчёт скопирован в буфер обмена.";
                _status.ForeColor = GreenColor;
            }
            catch (Exception ex)
            {
                ShowError("Не удалось скопировать отчёт", ex);
            }
        }

        private async void OnClearCache(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm("Закрыть Discord (Stable/PTB/Canary/Development) и удалить кэш?"))
            {
                return;
            }
            _busy = true;
            SetState(_status, "Очищаю кэш Discord…", AmberColor);
            DiscordCacheResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    result = DiscordCache.Clear();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось очистить кэш Discord", error);
                return;
            }
            string text = string.Join(Environment.NewLine, result.Messages.ToArray());
            SetState(_status, "Кэш Discord: удалено каталогов " +
                result.DeletedDirectories + ".", GreenColor);
            ShowInfo(text);
        }

        private async void OnHostsCheck(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetState(_status, "Проверяю hosts…", AmberColor);
            HostsCheckResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    result = HostsManager.Check();
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось проверить hosts", error);
                return;
            }
            if (result.Error != null)
            {
                SetState(_status, "Ошибка: " + result.Error, RedColor);
                return;
            }
            if (result.NeedsUpdate)
            {
                SetState(_status, "hosts требует обновления.", AmberColor);
                ShowInfo("hosts требует обновления. Первые строки:\n\n" +
                    (result.Preview ?? string.Empty));
            }
            else
            {
                SetState(_status, "hosts актуален.", GreenColor);
            }
        }

        private async void OnHostsApply(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm(
                "Обновить hosts из Flowseal?\nБудет заменён только управляемый блок " +
                "Zapret Control, остальные записи сохранятся. Перед записью создаётся бэкап."))
            {
                return;
            }
            _busy = true;
            SetState(_status, "Обновляю hosts…", AmberColor);
            HostsApplyResult result = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    HostsCheckResult check = HostsManager.Check();
                    if (check.Error != null)
                    {
                        throw new InvalidOperationException(check.Error);
                    }
                    if (string.IsNullOrEmpty(check.UpstreamText))
                    {
                        throw new InvalidOperationException("Не удалось скачать hosts.");
                    }
                    result = HostsManager.Apply(check.UpstreamText);
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось обновить hosts", error);
                return;
            }
            SetState(_status, "hosts обновлён: " + result.Lines + " записей.", GreenColor);
            ShowInfo("hosts обновлён, DNS-кэш сброшен." +
                (string.IsNullOrEmpty(result.BackupPath)
                    ? string.Empty
                    : "\nБэкап: " + result.BackupPath));
        }

        private static Color StateColor(DiagnosticState state)
        {
            switch (state)
            {
                case DiagnosticState.Pass:
                    return GreenColor;
                case DiagnosticState.Warning:
                    return AmberColor;
                case DiagnosticState.Fail:
                    return RedColor;
                default:
                    return MutedColor;
            }
        }
    }
}

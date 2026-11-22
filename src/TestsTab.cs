using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class TestsTab : TabView
    {
        private TextBox _output;
        private Label _status;
        private Button _check;
        private Button _advise;
        private bool _busy;
        private HomeCheckReport _pendingReport;

        public event Action AdviseRequested;

        public override string TabTitle
        {
            get { return "Тесты"; }
        }

        public TestsTab()
        {
            CardPanel card = AddCard("ПРОВЕРКА СОЕДИНЕНИЙ", 0, 0, 852, 470);
            AddMutedLabel(card,
                "Единая проверка: Discord, YouTube, Google, Cloudflare и " +
                "Telegram по HTTP/1.1, TLS 1.2 и TLS 1.3, страница YouTube, " +
                "плеер, два видео по три фрагмента и DPI-заморозка 16–20 КБ " +
                "через curl.exe.",
                20, 34, 812, 52);
            _check = AddButton(card, "Проверить", 20, 94, 130, 32, OnCheck);
            _advise = AddButton(card, "Подобрать", 160, 94, 140, 32, OnAdvise);
            _status = AddMutedLabel(card, "Проверка не запускалась.",
                314, 102, 518, 20);
            _output = AddTextBox(card, 20, 138, 812, 312, true);
            _output.Font = new System.Drawing.Font("Consolas", 8.5F);
        }

        public void ShowReport(HomeCheckReport report)
        {
            if (report == null || IsDisposed || Disposing)
            {
                return;
            }
            if (_busy)
            {
                _pendingReport = report;
                return;
            }
            ApplyReport(report);
        }

        private void ApplyReport(HomeCheckReport report)
        {
            _output.Text = report.ToText();
            SetState(_status, report.Summary, report.AllOk
                ? GreenColor
                : (report.Passed > 0 ? AmberColor : RedColor));
        }

        private void OnCheck(object sender, EventArgs e)
        {
            RunCheck();
        }

        private void OnAdvise(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (AdviseRequested != null)
            {
                AdviseRequested();
            }
        }

        private async void RunCheck()
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
            _busy = true;
            _check.Enabled = false;
            _advise.Enabled = false;
            SetState(_status, "Выполняю единую проверку…", AmberColor);
            _output.Clear();
            ConnectionTestReport report = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    report = ConnectionTests.RunSingle();
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
            _check.Enabled = true;
            _advise.Enabled = true;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось выполнить проверку", error);
                return;
            }
            if (report == null)
            {
                SetState(_status, "Проверка не вернула результат.", RedColor);
                return;
            }
            _output.Text = report.ToText();
            bool all = report.Passed == report.Results.Count;
            SetState(_status,
                "Готово: " + report.Passed + " из " + report.Results.Count + " успешно.",
                all ? GreenColor : (report.Passed > 0 ? AmberColor : RedColor));
            if (_pendingReport != null)
            {
                HomeCheckReport pending = _pendingReport;
                _pendingReport = null;
                _output.AppendText(Environment.NewLine + pending.ToText());
            }
        }
    }
}
